using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// F5c Task 4: get-or-create de maestros + LineaPoa/AsignacionPresupuestal dentro de la
/// transacción real. Ingresos/Gastos (Task 5) y guard/auditoría (Task 6) se agregaron sobre el
/// MISMO ConfirmarAsync en tasks posteriores (ver región "Task 6" al final de la clase).
/// </summary>
public class ImportacionRepositoryTests : PostgresRepositoryTestBase
{
    private const int Ejercicio = 2026;
    private readonly ImportacionRepository _repo;
    private readonly int _usuarioId;

    public ImportacionRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        // Task 6: ConfirmarAsync ahora escribe SIEMPRE un LogAuditoria, y LogAuditoria.UsuarioId
        // tiene FK real hacia Usuarios (AppDbContext.cs, FK_LogsAuditoria_Usuarios_UsuarioId,
        // DeleteBehavior.Restrict). Antes de esta task ningún test necesitaba un Usuario real
        // porque ConfirmarAsync nunca tocaba LogsAuditoria. Sembramos UNO acá y lo exponemos por
        // Id real (no asumimos que RESTART IDENTITY lo deja en 1 — esa suposición es un
        // acoplamiento oculto que rompe en silencio el día que alguien agregue otro seed antes).
        var usuarioSemilla = new Usuario
        {
            NombreUsuario = "importador-tests",
            HashContrasena = "hash",
            Rol = RolUsuario.Admin,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuarioSemilla);
        Context.SaveChanges();
        _usuarioId = usuarioSemilla.Id;
        Context.ChangeTracker.Clear();

        _repo = new ImportacionRepository(Context);
    }

    private static ConfirmarImportacionDto PayloadSoloMaestrosYPoa(
        IReadOnlyList<string>? proveedoresNuevos = null,
        IReadOnlyList<string>? fuentesNuevas = null,
        IReadOnlyList<RubroNuevoConfirmarDto>? rubrosNuevos = null,
        IReadOnlyList<LineaPoaConfirmarDto>? lineasPoa = null,
        bool forzar = false) => new(
        Ejercicio: Ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            proveedoresNuevos ?? new List<string>(),
            fuentesNuevas ?? new List<string> { "Literal B", "Literal C" },
            rubrosNuevos ?? new List<RubroNuevoConfirmarDto>()),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>(),
        LineasPoa: lineasPoa ?? new List<LineaPoaConfirmarDto>
        {
            new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto>
            {
                new("Literal B", 92748m),
                new("Literal C", 1407252m),
            }),
        });

    [Fact]
    public async Task ConfirmarAsync_FuentesNuevas_LasCreaYDevuelveElContador()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        Assert.Equal(2, resultado.FuentesCreadas);
        await using var verificacion = Fixture.CrearContexto();
        var fuentesEnBase = await verificacion.FuentesFinanciamiento.ToListAsync();
        Assert.Contains(fuentesEnBase, f => f.Nombre == "Literal B");
        Assert.Contains(fuentesEnBase, f => f.Nombre == "Literal C");
    }

    [Fact]
    public async Task ConfirmarAsync_FuenteYaExistente_NoLaDuplicaYNoLaCuentaComoCreada()
    {
        Context.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Literal B" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        Assert.Equal(1, resultado.FuentesCreadas); // solo "Literal C" es nueva
        await using var verificacion = Fixture.CrearContexto();
        var cantidad = verificacion.FuentesFinanciamiento.Count(f => f.Nombre == "Literal B");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaConFinanciamientoMixto_CreaLaLineaConDosAsignaciones()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        Assert.Equal(1, resultado.LineasPoaCreadas);
        Assert.Equal(2, resultado.AsignacionesCreadas);

        await using var verificacion = Fixture.CrearContexto();
        var linea = verificacion.LineasPoa
            .Include(l => l.Asignaciones).ThenInclude(a => a.FuenteFinanciamiento)
            .Single(l => l.Nombre == "COMPOSTERAS");
        Assert.Equal("Ambiente", linea.Programa);
        Assert.Equal(Ejercicio, linea.Ejercicio);
        Assert.NotNull(linea.IdImportacion);
        Assert.Equal(resultado.IdImportacion, linea.IdImportacion);
        Assert.Equal(2, linea.Asignaciones.Count);
        Assert.Contains(linea.Asignaciones, a => a.FuenteFinanciamiento!.Nombre == "Literal B" && a.Monto == 92748m);
        Assert.Contains(linea.Asignaciones, a => a.FuenteFinanciamiento!.Nombre == "Literal C" && a.Monto == 1407252m);
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaYaExistenteConLaMismaAsignacion_NoLaDuplicaNiCuentaComoNueva()
    {
        await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        // Task 6: sin el guard de re-importación, este test predata el guard y corría dos veces
        // con Forzar=false por defecto — ahora eso es exactamente lo que el guard bloquea.
        // Forzar=true reproduce el escenario real ("reimportar a propósito") que el test
        // original quería ejercitar: dedupe/idempotencia de LineaPoa+Asignaciones, no el guard.
        var segundo = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(forzar: true), usuarioId: _usuarioId);

        Assert.Equal(0, segundo.LineasPoaCreadas);
        Assert.Equal(0, segundo.AsignacionesCreadas);
        Assert.Equal(0, segundo.FuentesCreadas);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, verificacion.LineasPoa.Count(l => l.Nombre == "COMPOSTERAS"));
        Assert.Equal(2, verificacion.AsignacionesPresupuestales.Count());
    }

    [Fact]
    public async Task ConfirmarAsync_TodoElGrafoSeCommiteaJuntoEnUnaSolaTransaccion()
    {
        // Atomicidad: si la corrida completa, TODO lo que creó (fuentes + línea + 2
        // asignaciones) tiene que estar visible desde un contexto NUEVO (no el mismo Context,
        // que podría mostrar el change tracker en vez del estado real de la BD).
        await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(2, await verificacion.FuentesFinanciamiento.CountAsync());
        Assert.Equal(1, await verificacion.LineasPoa.CountAsync());
        Assert.Equal(2, await verificacion.AsignacionesPresupuestales.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_AsignacionConFuenteQueNoResuelve_FallaAntesDeCualquierSaveChanges()
    {
        // Renombrado (review Important B): este test NO prueba atomicidad/rollback — la excepción
        // se lanza DENTRO de GetOrCrearLineasPoaAsync, que corre completo en memoria ANTES del
        // único SaveChangesAsync del final. En este punto no hubo todavía ni un solo round-trip de
        // escritura a Postgres, así que "no queda nada persistido" es trivialmente cierto (pasaría
        // igual sin transacción, sin tx y sin advisory lock). Es un test de "falla temprana /
        // validación defensiva": prueba que una asignación cuya fuente no resuelve ni contra la
        // base ni contra MaestrosNuevos.Fuentes corta el procesamiento antes de tocar la BD, y que
        // lo hace con el tipo de excepción correcto (ValidacionImportacionException → 400
        // estructurado, mismo contrato que ConfirmacionImportacionService.ValidarAsync — review
        // Important A). El test de rollback real con DbUpdateException de Postgres está abajo:
        // ConfirmarAsync_SaveChangesFallaPorConstraintReal_RevierteTodoElGrafoDeLaCorrida.
        var payload = PayloadSoloMaestrosYPoa(
            fuentesNuevas: new List<string> { "Literal B" }, // "Literal Fantasma" NO se declara
            lineasPoa: new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto>
                {
                    new("Literal B", 92748m),
                    new("Literal Fantasma", 1407252m),
                }),
            });

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(
            () => _repo.ConfirmarAsync(payload, usuarioId: _usuarioId));

        Assert.True(ex.Errores.ContainsKey("LineasPoa[0].Asignaciones[1].Fuente"));

        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(0, await verificacion.FuentesFinanciamiento.CountAsync());
        Assert.Equal(0, await verificacion.LineasPoa.CountAsync());
        Assert.Equal(0, await verificacion.AsignacionesPresupuestales.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_SaveChangesFallaPorConstraintReal_RevierteTodoElGrafoDeLaCorrida()
    {
        // C4-style (review Important B): mismo patrón que
        // MovimientoStockRepositoryTests."C4: Rollback atómico" — una subclase sobrescribe un seam
        // (ImportacionRepository.AntesDeGuardarAsync) para agregar al ChangeTracker una fila
        // inválida (LogAuditoria.Detalle = null, viola NOT NULL) justo antes del único
        // SaveChangesAsync. En ese punto el grafo completo de la corrida (fuente nueva + línea POA
        // + asignación) YA está en el ChangeTracker, así que el DbUpdateException que dispara
        // Postgres fuerza un rollback real: si la transacción es atómica de verdad, nada de eso
        // queda persistido, y un maestro que ya existía ANTES de la corrida no se ve afectado.
        var usuario = new Usuario
        {
            NombreUsuario  = "admin",
            HashContrasena = "hash",
            Rol            = RolUsuario.Admin,
            Activo         = true,
            FechaAlta      = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuario);
        Context.Proveedores.Add(new Proveedor { Nombre = "Ferretería Lopez" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var repoRoto = new ImportacionRepositoryConFalloInyectado(Context, usuario.Id);

        var payload = PayloadSoloMaestrosYPoa(
            fuentesNuevas: new List<string> { "Literal B" },
            lineasPoa: new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto>
                {
                    new("Literal B", 92748m),
                }),
            });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => repoRoto.ConfirmarAsync(payload, usuarioId: usuario.Id));

        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(0, await verificacion.FuentesFinanciamiento.CountAsync());
        Assert.Equal(0, await verificacion.LineasPoa.CountAsync());
        Assert.Equal(0, await verificacion.AsignacionesPresupuestales.CountAsync());
        Assert.Equal(0, await verificacion.LogsAuditoria.CountAsync());
        var proveedor = await verificacion.Proveedores.SingleAsync(p => p.Nombre == "Ferretería Lopez");
        Assert.True(proveedor.Activo); // maestro preexistente intacto, no tocado por el rollback
    }

    [Fact]
    public async Task ConfirmarAsync_MaestrosConNombresQueDifierenSoloEnMayusculas_NoRevientaElToDictionary()
    {
        // Important #1 (review): los índices únicos son sobre el nombre crudo (Postgres
        // case-sensitive) y los ABM (FuenteFinanciamientoRepository/ProveedorRepository/
        // LineaPoaRepository) comparan con "==", no normalizado — dos filas que difieren solo en
        // mayúsculas pueden coexistir en la base. Antes del fix, el ToDictionary de
        // proveedorPorNombre/fuentePorNombre/lineaPorNombre lanzaba ArgumentException por clave
        // duplicada ANTES de tocar el payload.
        Context.Proveedores.AddRange(
            new Proveedor { Nombre = "Ferretería Lopez" },
            new Proveedor { Nombre = "FERRETERÍA LOPEZ" });
        Context.FuentesFinanciamiento.AddRange(
            new FuenteFinanciamiento { Nombre = "Literal B" },
            new FuenteFinanciamiento { Nombre = "literal b" });
        Context.LineasPoa.AddRange(
            new LineaPoa { Nombre = "Duplicado X", Programa = "Ambiente", Ejercicio = Ejercicio },
            new LineaPoa { Nombre = "DUPLICADO X", Programa = "Ambiente", Ejercicio = Ejercicio });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        Assert.Equal(1, resultado.FuentesCreadas); // "Literal B" ya existe (2 filas); solo "Literal C" es nueva
    }

    [Fact]
    public async Task ConfirmarAsync_ProveedoresNuevos_LosCreaYDevuelveElContador()
    {
        var payload = PayloadSoloMaestrosYPoa(
            proveedoresNuevos: new List<string> { "Ferretería Lopez", "Corralón Pérez" });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        Assert.Equal(2, resultado.ProveedoresCreados);
        await using var verificacion = Fixture.CrearContexto();
        var proveedores = await verificacion.Proveedores.ToListAsync();
        Assert.Contains(proveedores, p => p.Nombre == "Ferretería Lopez");
        Assert.Contains(proveedores, p => p.Nombre == "Corralón Pérez");
    }

    [Fact]
    public async Task ConfirmarAsync_ProveedorYaExistente_NoLoDuplicaYNoLoCuentaComoCreado()
    {
        Context.Proveedores.Add(new Proveedor { Nombre = "Ferretería Lopez" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var payload = PayloadSoloMaestrosYPoa(
            proveedoresNuevos: new List<string> { "Ferretería Lopez" });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        Assert.Equal(0, resultado.ProveedoresCreados);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Proveedores.CountAsync(p => p.Nombre == "Ferretería Lopez"));
    }

    [Fact]
    public async Task ConfirmarAsync_RubrosNuevos_LosCreaYDevuelveElContador()
    {
        var payload = PayloadSoloMaestrosYPoa(
            rubrosNuevos: new List<RubroNuevoConfirmarDto> { new(5, "Combustibles"), new(9, "Viáticos") });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        Assert.Equal(2, resultado.RubrosCreados);
        await using var verificacion = Fixture.CrearContexto();
        var rubros = await verificacion.RubrosGasto.ToListAsync();
        Assert.Contains(rubros, r => r.Codigo == 5 && r.Nombre == "Combustibles");
        Assert.Contains(rubros, r => r.Codigo == 9 && r.Nombre == "Viáticos");
    }

    [Fact]
    public async Task ConfirmarAsync_RubroYaExistente_NoLoDuplicaYNoLoCuentaComoCreado()
    {
        Context.RubrosGasto.Add(new RubroGasto { Codigo = 5, Nombre = "Combustibles" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var payload = PayloadSoloMaestrosYPoa(
            rubrosNuevos: new List<RubroNuevoConfirmarDto> { new(5, "Combustibles") });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        Assert.Equal(0, resultado.RubrosCreados);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.RubrosGasto.CountAsync(r => r.Codigo == 5));
    }

    [Fact]
    public async Task ConfirmarAsync_ProveedorInactivo_LoReactivaYNoLoCuentaComoCreado()
    {
        Context.Proveedores.Add(new Proveedor { Nombre = "Ferretería Lopez", Activo = false });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var payload = PayloadSoloMaestrosYPoa(
            proveedoresNuevos: new List<string> { "Ferretería Lopez" });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        Assert.Equal(0, resultado.ProveedoresCreados);
        Assert.Equal(1, resultado.ProveedoresReactivados);
        await using var verificacion = Fixture.CrearContexto();
        var proveedor = await verificacion.Proveedores.SingleAsync(p => p.Nombre == "Ferretería Lopez");
        Assert.True(proveedor.Activo);
    }

    [Fact]
    public async Task ConfirmarAsync_FuenteInactiva_LaReactivaYNoLaCuentaComoCreada()
    {
        Context.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Literal B", Activo = false });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        Assert.Equal(1, resultado.FuentesCreadas); // solo "Literal C" es genuinamente nueva
        Assert.Equal(1, resultado.FuentesReactivadas); // "Literal B" estaba inactiva
        await using var verificacion = Fixture.CrearContexto();
        var fuente = await verificacion.FuentesFinanciamiento.SingleAsync(f => f.Nombre == "Literal B");
        Assert.True(fuente.Activo);
    }

    [Fact]
    public async Task ConfirmarAsync_RubroInactivo_LoReactivaYNoLoCuentaComoCreado()
    {
        Context.RubrosGasto.Add(new RubroGasto { Codigo = 5, Nombre = "Combustibles", Activo = false });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var payload = PayloadSoloMaestrosYPoa(
            rubrosNuevos: new List<RubroNuevoConfirmarDto> { new(5, "Combustibles") });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        Assert.Equal(0, resultado.RubrosCreados);
        Assert.Equal(1, resultado.RubrosReactivados);
        await using var verificacion = Fixture.CrearContexto();
        var rubro = await verificacion.RubrosGasto.SingleAsync(r => r.Codigo == 5);
        Assert.True(rubro.Activo);
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaInactivaPorReversionAnterior_LaReactivaYReestampaIdImportacion()
    {
        // Important #3 (review, cerrado en el re-review de Task 8): ciclo confirmar → revertir →
        // confirmar dado como escenario de aceptación explícito en el spec. RevertirAsync ya
        // existe (Task 8) — la reversa se ejercita REAL de punta a punta, sin Forzar en la
        // segunda corrida (antes se simulaba a mano con Forzar=true porque RevertirAsync no
        // existía todavía).
        var primero = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        await _repo.RevertirAsync(primero.IdImportacion, usuarioId: _usuarioId);

        var segundo = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        Assert.Equal(0, segundo.LineasPoaCreadas);
        Assert.Equal(1, segundo.LineasPoaReactivadas);
        Assert.NotEqual(primero.IdImportacion, segundo.IdImportacion);

        await using var verificacion = Fixture.CrearContexto();
        var lineaFinal = await verificacion.LineasPoa.SingleAsync(l => l.Nombre == "COMPOSTERAS");
        Assert.True(lineaFinal.Activo);
        Assert.Equal(segundo.IdImportacion, lineaFinal.IdImportacion);
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaActivaExistente_AsignacionNueva_NoLaCreaPeroSumaLaAsignacion()
    {
        // Minor #5 (review): el XML doc declara como intencional que IdImportacion NO se
        // re-estampa sobre una línea vieja ACTIVA (a diferencia de una inactiva que se reactiva,
        // cubierto arriba), pero no había ningún test que lo probara.
        var payloadInicial = PayloadSoloMaestrosYPoa(
            fuentesNuevas: new List<string> { "Literal B" },
            lineasPoa: new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto>
                {
                    new("Literal B", 92748m),
                }),
            });
        var primero = await _repo.ConfirmarAsync(payloadInicial, usuarioId: _usuarioId);

        // forzar: true (Task 6): sin el guard esta segunda corrida sobre el mismo ejercicio
        // corría sin más — ahora es exactamente lo que el guard bloquea por defecto. El test
        // quiere ejercitar la lógica de "asignación mixta agregada en una corrida separada", no
        // el guard, así que Forzar=true reproduce el reimport intencional.
        var payloadSegundo = PayloadSoloMaestrosYPoa(
            fuentesNuevas: new List<string> { "Literal C" },
            lineasPoa: new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto>
                {
                    new("Literal B", 92748m),
                    new("Literal C", 1407252m),
                }),
            },
            forzar: true);
        var segundo = await _repo.ConfirmarAsync(payloadSegundo, usuarioId: _usuarioId);

        Assert.Equal(0, segundo.LineasPoaCreadas);
        Assert.Equal(0, segundo.LineasPoaReactivadas);
        Assert.Equal(1, segundo.AsignacionesCreadas);

        await using var verificacion = Fixture.CrearContexto();
        var linea = await verificacion.LineasPoa.SingleAsync(l => l.Nombre == "COMPOSTERAS");
        Assert.Equal(primero.IdImportacion, linea.IdImportacion); // NO re-estampado: la línea seguía activa
    }

    private static ConfirmarImportacionDto PayloadConIngresoYGasto(bool forzar = false) => new(
        Ejercicio: Ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string> { "ACME SA" },
            new List<string> { "Literal A" },
            new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        Ingresos: new List<IngresoConfirmarDto>
        {
            new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
        },
        Gastos: new List<GastoConfirmarDto>
        {
            new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null, CondicionPago.Contado, null),
        },
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task ConfirmarAsync_IngresoYGastoNuevos_LosCreaYElGastoContadoTraePagoAutomatico()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);

        Assert.Equal(1, resultado.IngresosCreados);
        Assert.Equal(0, resultado.IngresosOmitidos);
        Assert.Equal(1, resultado.GastosCreados);
        Assert.Equal(0, resultado.GastosOmitidos);
        Assert.Equal(1, resultado.PagosCreados);

        await using var verificacion = Fixture.CrearContexto();
        var gasto = verificacion.Gastos.Include(g => g.Pagos).Single(g => g.Detalle == "Compra de insumos");
        Assert.Equal(resultado.IdImportacion, gasto.IdImportacion);
        Assert.Single(gasto.Pagos);
        Assert.Equal(500m, gasto.Pagos[0].Monto);
        var ingreso = verificacion.IngresosCaja.Single(i => i.Concepto == "Saldo inicial");
        Assert.Equal(resultado.IdImportacion, ingreso.IdImportacion);
    }

    [Fact]
    public async Task ConfirmarAsync_CorridaRepetidaConForzar_NoDuplicaIngresosNiGastos()
    {
        await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);

        var segunda = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: true), usuarioId: _usuarioId);

        Assert.Equal(0, segunda.IngresosCreados);
        Assert.Equal(1, segunda.IngresosOmitidos);
        Assert.Equal(0, segunda.GastosCreados);
        Assert.Equal(1, segunda.GastosOmitidos);
        Assert.Equal(0, segunda.PagosCreados);

        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync());
        Assert.Equal(1, await verificacion.IngresosCaja.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_CompromisoPoaCredito_NoGeneraPago()
    {
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string> { "Contratista SRL" },
                new List<string> { "Literal C" },
                new List<RubroNuevoConfirmarDto> { new(2, "Obras") }),
            Ingresos: new List<IngresoConfirmarDto>(),
            Gastos: new List<GastoConfirmarDto>
            {
                new("Contratista SRL", "F-9", null, "Compromiso POA sin pago", null,
                    new DateOnly(Ejercicio, 12, 31), 300000m, "Literal C", 2, "COMPOSTERAS",
                    CondicionPago.Credito, new DateOnly(Ejercicio, 12, 31)),
            },
            LineasPoa: new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente",
                    new List<AsignacionConfirmarDto> { new("Literal C", 1407252m) }),
            });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        Assert.Equal(1, resultado.GastosCreados);
        Assert.Equal(0, resultado.PagosCreados);
        await using var verificacion = Fixture.CrearContexto();
        var gasto = verificacion.Gastos.Include(g => g.Pagos).Single();
        Assert.Empty(gasto.Pagos);
        Assert.Equal(gasto.MontoTotal, gasto.SaldoPendiente); // SaldoPendiente == MontoTotal: sin pagos
        Assert.Equal(new DateTime(Ejercicio, 12, 31, 0, 0, 0, DateTimeKind.Utc), gasto.FechaVencimiento);
    }

    // ── Important A.1/A.2: clave natural del gasto partida por presencia de NumeroFactura ──────

    private static ConfirmarImportacionDto PayloadUnGastoConFactura(
        string proveedor = "ACME SA", string? numeroFactura = "F-1", string? numeroOrden = "O-1",
        DateOnly? fecha = null, decimal monto = 500m, bool forzar = false,
        IReadOnlyList<string>? proveedoresNuevos = null) => new(
        Ejercicio: Ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            proveedoresNuevos ?? new List<string> { proveedor },
            new List<string> { "Literal A" },
            new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>
        {
            new(proveedor, numeroFactura, numeroOrden, "Compra de insumos", null,
                fecha ?? new DateOnly(Ejercicio, 1, 15), monto, "Literal A", 1, null, CondicionPago.Contado, null),
        },
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task ConfirmarAsync_GastoConMismaFacturaYMontoDistinto_NoLoEscribeYLoReportaComoConflicto()
    {
        // Escenario del bug real (review Important A): corrida 1 importa F-1 en 500; el usuario
        // corrige el .ods a 550 y reimporta con Forzar. Antes del fix, la clave natural incluía
        // MontoTotal, así que la fila "nueva" no matcheaba contra la existente y el segundo
        // SaveChangesAsync explotaba con un 23505 (mismo ProveedorId+NumeroFactura, índice único
        // parcial de la base) que tiraba abajo TODA la importación. Con la clave partida
        // (ProveedorId, NumeroFactura), la segunda corrida SÍ matchea, detecta que MontoTotal
        // difiere, y lo reporta como conflicto sin escribir nada ni romper la transacción.
        await _repo.ConfirmarAsync(PayloadUnGastoConFactura(monto: 500m), usuarioId: _usuarioId);

        var resultado = await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(monto: 550m, forzar: true, proveedoresNuevos: new List<string>()),
            usuarioId: _usuarioId);

        Assert.Equal(0, resultado.GastosCreados);
        Assert.Equal(0, resultado.GastosOmitidos);
        Assert.Single(resultado.Conflictos);
        var conflicto = resultado.Conflictos[0];
        Assert.Equal("ACME SA", conflicto.Proveedor);
        Assert.Equal("F-1", conflicto.NumeroFactura);
        Assert.Contains(conflicto.CamposDivergentes, c => c.Campo == "MontoTotal" && c.ValorAnterior == "500" && c.ValorNuevo == "550");

        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync());
        var gasto = await verificacion.Gastos.SingleAsync();
        Assert.Equal(500m, gasto.MontoTotal); // el original NO se pisó
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConNumeroFacturaEnDistintoCasingYConEspacios_SeOmiteComoDuplicadoIdentico()
    {
        // Minor del review: ProyectarClaveGastoConFactura existe para que "F-1" y " f-1 " sean
        // la MISMA clave — sin Trim/ToUpperInvariant este test fallaría (crearía una fila nueva
        // en vez de omitir).
        await _repo.ConfirmarAsync(PayloadUnGastoConFactura(numeroFactura: "F-1"), usuarioId: _usuarioId);

        var resultado = await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(numeroFactura: " f-1 ", forzar: true, proveedoresNuevos: new List<string>()),
            usuarioId: _usuarioId);

        Assert.Equal(0, resultado.GastosCreados);
        Assert.Equal(1, resultado.GastosOmitidos);
        Assert.Empty(resultado.Conflictos);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_GastoSinFacturaNullYLuegoFacturaVacia_SeConsideranLaMismaClave_SeOmite()
    {
        // Minor del review (null vs ""): ambos casos van por la clave SIN factura — si no fueran
        // la misma clave, la segunda corrida crearía una fila nueva en vez de omitir.
        await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(numeroFactura: null, numeroOrden: "O-1"), usuarioId: _usuarioId);

        var resultado = await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(numeroFactura: "", numeroOrden: "O-1", forzar: true, proveedoresNuevos: new List<string>()),
            usuarioId: _usuarioId);

        Assert.Equal(0, resultado.GastosCreados);
        Assert.Equal(1, resultado.GastosOmitidos);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_IngresoConConceptoEnDistintoCasingYEspacios_SeOmiteComoDuplicado()
    {
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string> { "Literal A" }, new List<RubroNuevoConfirmarDto>()),
            Ingresos: new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
            },
            Gastos: new List<GastoConfirmarDto>(),
            LineasPoa: new List<LineaPoaConfirmarDto>());
        await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        var payloadRepetido = payload with
        {
            Forzar = true,
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
            Ingresos = new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio, 1, 1), "  SALDO INICIAL  ", 1000m, "Literal A"),
            },
        };

        var resultado = await _repo.ConfirmarAsync(payloadRepetido, usuarioId: _usuarioId);

        Assert.Equal(0, resultado.IngresosCreados);
        Assert.Equal(1, resultado.IngresosOmitidos);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.IngresosCaja.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_IngresoConMontoDeDistintaEscalaDecimal_SeConsideraElMismoIngreso_SeOmite()
    {
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string> { "Literal A" }, new List<RubroNuevoConfirmarDto>()),
            Ingresos: new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
            },
            Gastos: new List<GastoConfirmarDto>(),
            LineasPoa: new List<LineaPoaConfirmarDto>());
        await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        var payloadRepetido = payload with
        {
            Forzar = true,
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
            Ingresos = new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000.0000m, "Literal A"),
            },
        };

        var resultado = await _repo.ConfirmarAsync(payloadRepetido, usuarioId: _usuarioId);

        Assert.Equal(0, resultado.IngresosCreados);
        Assert.Equal(1, resultado.IngresosOmitidos);
    }

    [Fact]
    public async Task ConfirmarAsync_IngresoConMontoDistinto_NoEsDuplicado_CreaFilaNueva()
    {
        // Negativo (Minor del review): cambiar un campo de la CLAVE (Monto es parte de
        // ClaveIngreso) tiene que crear una fila nueva, no omitir.
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string> { "Literal A" }, new List<RubroNuevoConfirmarDto>()),
            Ingresos: new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
            },
            Gastos: new List<GastoConfirmarDto>(),
            LineasPoa: new List<LineaPoaConfirmarDto>());
        await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        var payloadDistinto = payload with
        {
            Forzar = true,
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
            Ingresos = new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 2000m, "Literal A"),
            },
        };

        var resultado = await _repo.ConfirmarAsync(payloadDistinto, usuarioId: _usuarioId);

        Assert.Equal(1, resultado.IngresosCreados);
        Assert.Equal(0, resultado.IngresosOmitidos);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(2, await verificacion.IngresosCaja.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConNumeroFacturaYOrdenYDestinoConEspacios_LosGuardaTrimeados()
    {
        // Minor del review: Detalle/Concepto ya se trimeaban; NumeroFactura/NumeroOrden/Destino
        // no. El índice único SÍ distingue " F-1" de "F-1" aunque la clave natural los vea
        // iguales (Trim+ToUpperInvariant) — sin este fix quedaría data sucia en la base.
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string> { "ACME SA" }, new List<string> { "Literal A" },
                new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
            Ingresos: new List<IngresoConfirmarDto>(),
            Gastos: new List<GastoConfirmarDto>
            {
                new("ACME SA", " F-1 ", " O-1 ", "Compra de insumos", " Playa ",
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null, CondicionPago.Contado, null),
            },
            LineasPoa: new List<LineaPoaConfirmarDto>());

        await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        await using var verificacion = Fixture.CrearContexto();
        var gasto = await verificacion.Gastos.SingleAsync();
        Assert.Equal("F-1", gasto.NumeroFactura);
        Assert.Equal("O-1", gasto.NumeroOrden);
        Assert.Equal("Playa", gasto.Destino);
    }

    [Fact]
    public async Task ConfirmarAsync_ViolacionDeIndiceUnicoRealForzada_LaMapeaAReglaDeNegocioException()
    {
        // A.5 (review Important A): red de seguridad. A.1 (clave = índice) y A.4 (validación
        // intra-payload en el Service) deberían evitar esto SIEMPRE, pero si algo bypassea esas
        // dos defensas y dos gastos activos terminan con la misma (ProveedorId, NumeroFactura)
        // en el mismo SaveChangesAsync, Postgres tira 23505 y el repo tiene que traducirlo a una
        // excepción de dominio con el nombre de la restricción — nunca dejarlo pasar crudo (eso
        // sería un 500). Se fuerza el bypass con el mismo seam que el test C4
        // (AntesDeGuardarAsync), agregando el segundo gasto directo al ChangeTracker, evitando
        // por completo el dedupe en memoria de ProcesarGastosAsync.
        await _repo.ConfirmarAsync(PayloadUnGastoConFactura(numeroFactura: "F-1"), usuarioId: _usuarioId);

        var proveedor = await Context.Proveedores.SingleAsync(p => p.Nombre == "ACME SA");
        var fuente = await Context.FuentesFinanciamiento.SingleAsync(f => f.Nombre == "Literal A");
        var rubro = await Context.RubrosGasto.SingleAsync(r => r.Codigo == 1);
        Context.ChangeTracker.Clear();

        var repoRoto = new ImportacionRepositoryConFacturaDuplicadaInyectada(Context, proveedor.Id, fuente.Id, rubro.Id);
        // Forzar: true (Task 6): esta corrida ya tiene una importación previa sin revertir para
        // el mismo ejercicio (la de la línea 785) — sin esto, el guard la bloquearía ANTES de
        // llegar al seam que inyecta el gasto duplicado, y el test dejaría de probar lo que
        // quiere probar (la red de seguridad A.5 contra un 23505 real).
        var payloadSinNadaNuevo = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: true,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
            Ingresos: new List<IngresoConfirmarDto>(),
            Gastos: new List<GastoConfirmarDto>(),
            LineasPoa: new List<LineaPoaConfirmarDto>());

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => repoRoto.ConfirmarAsync(payloadSinNadaNuevo, usuarioId: _usuarioId));

        Assert.Contains("IX_Gastos_ProveedorId_NumeroFactura", ex.Message);

        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync()); // el inyectado NO quedó persistido (rollback)
    }

    // ── Regresiones del re-review (CRITICAL 1 / IMPORTANT 2 / IMPORTANT 3) ──────────────────────

    [Fact]
    public async Task ConfirmarAsync_IngresoFechadoFueraDelEjercicio_SegundaCorridaLoOmiteNoLoDuplica()
    {
        // CRITICAL 1 (re-review): el acotado por ejercicio que filtraba el set de dedupe
        // (Where(i => i.Activo && i.Fecha >= inicioEjercicio && i.Fecha < finEjercicio)) asumía
        // que TODA Fecha del payload cae dentro del Ejercicio declarado — supuesto que nadie
        // valida (ValidarAsync no compara Fecha contra Ejercicio). Escenario real: planilla del
        // ejercicio 2026 con una fila de diciembre arrastrada (Fecha = 2025-12-30). Con el filtro
        // puesto, la segunda corrida (Forzar) no encuentra esa fila en el set acotado a 2026 →
        // Add → duplicado SILENCIOSO (IngresoCaja no tiene índice único que lo frene, no hay
        // 23505, no hay error).
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string> { "Literal A" }, new List<RubroNuevoConfirmarDto>()),
            Ingresos: new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio - 1, 12, 30), "Transferencia MTOP", 40000m, "Literal A"),
            },
            Gastos: new List<GastoConfirmarDto>(),
            LineasPoa: new List<LineaPoaConfirmarDto>());
        await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        var payloadRepetido = payload with
        {
            Forzar = true,
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
        };

        var resultado = await _repo.ConfirmarAsync(payloadRepetido, usuarioId: _usuarioId);

        Assert.Equal(0, resultado.IngresosCreados);
        Assert.Equal(1, resultado.IngresosOmitidos);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.IngresosCaja.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_MismaFacturaEnDistintasFechasDeEjercicio_SeDetectaNoRevientaCon23505()
    {
        // IMPORTANT 2 (re-review): IX_Gastos_ProveedorId_NumeroFactura no tiene NINGUNA
        // restricción de fecha ni de ejercicio — la numeración de facturas de un proveedor puede
        // reiniciarse cada año. datosPorClaveConFactura tiene que cubrir el universo COMPLETO de
        // gastos activos con factura, sin acotar por fecha, o esta segunda corrida no ve el gasto
        // de diciembre del ejercicio anterior, intenta un Add con la misma (ProveedorId,
        // NumeroFactura), y Postgres tira 23505 (que la red de seguridad A.5 traduce a
        // ReglaDeNegocioException, tumbando igual la fila en vez de detectar la colisión). La
        // Fecha difiere entre las dos corridas a propósito (Dic del ejercicio anterior vs. Ene
        // del actual) — eso hace que se detecte como CONFLICTO (A.2), no como omitido; lo que
        // importa acá es que se DETECTE sin romper la transacción, no cuál de los dos resultados
        // sea.
        await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(fecha: new DateOnly(Ejercicio - 1, 12, 20), numeroFactura: "F-1", monto: 500m),
            usuarioId: _usuarioId);

        var resultado = await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(
                fecha: new DateOnly(Ejercicio, 1, 15), numeroFactura: "F-1", monto: 500m,
                forzar: true, proveedoresNuevos: new List<string>()),
            usuarioId: _usuarioId);

        Assert.Equal(0, resultado.GastosCreados);
        Assert.Equal(0, resultado.GastosOmitidos);
        Assert.Single(resultado.Conflictos); // Fecha divergente ⇒ conflicto, no duplicado idéntico
        Assert.Equal("F-1", resultado.Conflictos[0].NumeroFactura);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_MismaFacturaDistintoNumeroOrden_CreaAmbosGastosSinConflicto()
    {
        // Caso real (docs/finanzas-facturas-duplicadas-planilla-2026.md): GARAY POZO HERNÁN,
        // factura 82446, dos renglones de la MISMA hoja/corrida con distinto número de orden.
        // F5c amplió la clave natural del importador (ProveedorId, NumeroFactura, NumeroOrden)
        // para que matchee el índice único ampliado — antes de este fix, la segunda fila NO
        // colisionaba en memoria (misma clave "angosta" que la primera) pero SÍ colisionaba en
        // Postgres, tirando 23505 y abortando TODA la importación.
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string> { "GARAY POZO HERNAN" },
                new List<string> { "Literal A" },
                new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
            Ingresos: new List<IngresoConfirmarDto>(),
            Gastos: new List<GastoConfirmarDto>
            {
                new("GARAY POZO HERNAN", "82446", "865813", "PAPEL HIGIENICO", "MANT OPERATIVO",
                    new DateOnly(Ejercicio, 1, 23), 263m, "Literal A", 1, null, CondicionPago.Contado, null),
                new("GARAY POZO HERNAN", "82446", "865901", "BOLSAS PARA RESIDUOS", "MANT OPERATIVO",
                    new DateOnly(Ejercicio, 1, 23), 6407m, "Literal A", 1, null, CondicionPago.Contado, null),
            },
            LineasPoa: new List<LineaPoaConfirmarDto>());

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        Assert.Equal(2, resultado.GastosCreados);
        Assert.Equal(0, resultado.GastosOmitidos);
        Assert.Empty(resultado.Conflictos);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(2, await verificacion.Gastos.CountAsync(g => g.NumeroFactura == "82446"));
    }

    [Fact]
    public async Task ConfirmarAsync_ReimportacionConNuevoNumeroOrdenParaFacturaExistente_LoCreaNoLoConfunde()
    {
        // La clave natural del dedupe en memoria (ClaveGastoConFactura) tiene que incluir
        // NumeroOrden, igual que el índice único de la base (invariante del archivo: la clave
        // nunca puede ser más fina que la restricción que respalda). Antes de este fix, la clave
        // era solo (ProveedorId, NumeroFactura): en una REIMPORTACIÓN (Forzar) que agrega un
        // renglón con un NumeroOrden nuevo para una factura ya existente, ese renglón matcheaba
        // por error contra el gasto viejo de OTRO orden, y como el monto difiere se reportaba
        // como CONFLICTO en vez de crearse como el gasto nuevo y distinto que realmente es.
        await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(numeroFactura: "82446", numeroOrden: "865813", monto: 263m),
            usuarioId: _usuarioId);

        var payloadReimportacion = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: true,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
            Ingresos: new List<IngresoConfirmarDto>(),
            Gastos: new List<GastoConfirmarDto>
            {
                // Idéntico al ya importado (mismo orden, misma fecha, mismo monto) ⇒ omitido.
                new("ACME SA", "82446", "865813", "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 263m, "Literal A", 1, null, CondicionPago.Contado, null),
                // Misma factura, orden NUEVO ⇒ tiene que crearse, no confundirse con el de arriba.
                new("ACME SA", "82446", "865901", "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 6407m, "Literal A", 1, null, CondicionPago.Contado, null),
            },
            LineasPoa: new List<LineaPoaConfirmarDto>());

        var resultado = await _repo.ConfirmarAsync(payloadReimportacion, usuarioId: _usuarioId);

        Assert.Equal(1, resultado.GastosCreados);
        Assert.Equal(1, resultado.GastosOmitidos);
        Assert.Empty(resultado.Conflictos);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(2, await verificacion.Gastos.CountAsync(g => g.NumeroFactura == "82446"));
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConFacturaEnBlanco_SegundaCorridaLoOmiteNoRevienta()
    {
        // IMPORTANT 3 (re-review): con NumeroFactura = " " (un espacio, alcanzable vía
        // OdsContentXmlReader.NuloSiVacio que usa IsNullOrEmpty, no IsNullOrWhiteSpace, o vía un
        // input vacío de F5d por JSON), el lado del payload (tieneFactura =
        // !IsNullOrWhiteSpace(...)) lo trata SIN factura, pero si se persiste tal cual
        // (?.Trim() da "", no null), la columna queda "" — que NO es null, así que el índice
        // único parcial (WHERE NumeroFactura IS NOT NULL) SÍ le aplica, y en la relectura
        // (Where(g => g.NumeroFactura is null)) esa fila queda AFUERA del set SIN factura. La
        // segunda corrida no la encuentra, hace Add, y Postgres tira 23505. El test existente de
        // null-vs-"" no cubre este caso porque arranca con null en la primera corrida, no con
        // blanco.
        await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(numeroFactura: " ", numeroOrden: "O-1"), usuarioId: _usuarioId);

        var resultado = await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(
                numeroFactura: " ", numeroOrden: "O-1", forzar: true, proveedoresNuevos: new List<string>()),
            usuarioId: _usuarioId);

        Assert.Equal(0, resultado.GastosCreados);
        Assert.Equal(1, resultado.GastosOmitidos);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync());
        var gasto = await verificacion.Gastos.SingleAsync();
        Assert.Null(gasto.NumeroFactura);
    }

    // ── Task 6: guard de re-importación + auditoría de la corrida ──────────────────────────────

    [Fact]
    public async Task ConfirmarAsync_SegundaCorridaSinForzar_LanzaReglaDeNegocio()
    {
        await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);

        await Assert.ThrowsAsync<StockApp.Domain.Exceptions.ReglaDeNegocioException>(
            () => _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: false), usuarioId: _usuarioId));

        // El rollback del guard no debe dejar un segundo LogAuditoria ni datos a medio escribir.
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync());
        Assert.Equal(1, verificacion.LogsAuditoria.Count(
            l => l.Accion == StockApp.Domain.Enums.AccionAuditada.ImportacionPlanillas));
    }

    [Fact]
    public async Task ConfirmarAsync_SegundaCorridaConForzar_NoLanzaYAuditaLasDosCorridasConSuPropioIdLote()
    {
        var primera = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);

        var segunda = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: true), usuarioId: _usuarioId);

        Assert.NotEqual(Guid.Empty, segunda.IdImportacion);
        await using var verificacion = Fixture.CrearContexto();
        var logs = verificacion.LogsAuditoria
            .Where(l => l.Accion == StockApp.Domain.Enums.AccionAuditada.ImportacionPlanillas)
            .ToList();
        Assert.Equal(2, logs.Count);
        // Post-review: cada corrida estampa su propio IdLote tipado — ya no hace falta parsear
        // Detalle para saber a qué corrida pertenece cada LogAuditoria.
        Assert.Contains(logs, l => l.IdLote == primera.IdImportacion);
        Assert.Contains(logs, l => l.IdLote == segunda.IdImportacion);
    }

    [Fact]
    public async Task ConfirmarAsync_PrimeraCorrida_AuditaConElResumenDeContadores()
    {
        // El "7" del brief original era solo un vehículo para decir "un usuario distinto del
        // usado en el resto de la clase" — no tiene significado propio, y con la FK real de
        // LogAuditoria.UsuarioId (ver constructor) un literal inventado rompería con 23503.
        // Sembramos un segundo Usuario real y verificamos contra SU Id, preservando la intención
        // original del test: que la auditoría registra el usuario que efectivamente confirmó,
        // no un valor fijo.
        var otroUsuario = new Usuario
        {
            NombreUsuario = "importador-tests-otro",
            HashContrasena = "hash",
            Rol = RolUsuario.Admin,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(otroUsuario);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: otroUsuario.Id);

        await using var verificacion = Fixture.CrearContexto();
        var log = verificacion.LogsAuditoria
            .Single(l => l.Accion == StockApp.Domain.Enums.AccionAuditada.ImportacionPlanillas);
        Assert.Equal(otroUsuario.Id, log.UsuarioId);
        Assert.Equal(Ejercicio, log.EntidadId);
        // Post-review: el vínculo con el lote ya no viaja como texto embebido en Detalle — es la
        // columna tipada LogAuditoria.IdLote (Guid?, con índice no único). Detalle sigue existiendo
        // solo como resumen legible para un humano, sin el token "IdImportacion=" al principio.
        Assert.Equal(resultado.IdImportacion, log.IdLote);
        Assert.DoesNotContain("IdImportacion=", log.Detalle);
        Assert.Contains("Gastos creados: 1", log.Detalle);
    }

    // ── Re-review de Task 6: guard con IdLote nulo + orden determinístico ──────────────────────

    [Fact]
    public async Task ConfirmarAsync_ConfirmacionLegacySinIdLote_NoBypaseaElGuardParaUnaImportacionRealSinRevertir()
    {
        // Important (re-review): una fila de confirmación con IdLote == null (dato legacy, o una
        // escritura futura fuera de este flujo) hacía que "l.IdLote == idLote" con idLote = null
        // se tradujera a SQL "IS NULL". Si esa fila se procesaba antes que una corrida real sin
        // revertir (sin ORDER BY, el orden no estaba garantizado), BuscarImportacionNoRevertidaAsync
        // devolvía null — el propio IdLote de la fila legacy —, y el caller lee null como "nada
        // bloqueado" en vez de "encontré un bloqueo sin Guid identificable". Resultado: el guard
        // bypaseaba una re-importación real que sí debía bloquearse.
        Context.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = _usuarioId,
            Fecha = DateTime.UtcNow,
            Accion = AccionAuditada.ImportacionPlanillas,
            Entidad = "Importacion",
            EntidadId = Ejercicio,
            IdLote = null, // dato legacy: confirmación sin lote tipado
            Detalle = "Confirmación legacy sin IdLote",
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        // Corrida real, con IdLote real, genuinamente sin revertir.
        await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);

        await Assert.ThrowsAsync<StockApp.Domain.Exceptions.ReglaDeNegocioException>(
            () => _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: false), usuarioId: _usuarioId));
    }

    [Fact]
    public async Task ConfirmarAsync_DosConfirmacionesSinRevertir_ElGuardReportaLaMasReciente()
    {
        // Minor 3 (re-review): con más de una confirmación sin revertir para el mismo ejercicio,
        // cuál se reportaba en el 409 no estaba garantizado (sin ORDER BY). Ahora se ordena por Id
        // descendente y se reporta la MÁS RECIENTE — la que le sirve al humano que lee el error
        // para saber qué lote revertir.
        var primera = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);
        var segunda = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: true), usuarioId: _usuarioId);

        var ex = await Assert.ThrowsAsync<StockApp.Domain.Exceptions.ReglaDeNegocioException>(
            () => _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: false), usuarioId: _usuarioId));

        Assert.Contains(segunda.IdImportacion.ToString(), ex.Message);
        Assert.DoesNotContain(primera.IdImportacion.ToString(), ex.Message);
    }

    // ── Task 8: reversa por lote ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevertirAsync_LoteExistente_DaDeBajaGastosIngresosYLineasPoaPeroNoLosMaestros()
    {
        // Minor (re-review): cantidades DISTINTAS a propósito — 2 gastos (uno Contado con pago,
        // otro Crédito sin pago), 1 pago, 3 ingresos. Con los tres contadores en 1 (como antes),
        // un swap posicional en el constructor de ResultadoReversionDto pasaba este test en
        // verde igual.
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string> { "ACME SA" }, new List<string> { "Literal A" },
                new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
            Ingresos: new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
                new(new DateOnly(Ejercicio, 2, 1), "Transferencia MTOP", 2000m, "Literal A"),
                new(new DateOnly(Ejercicio, 3, 1), "Otra transferencia", 3000m, "Literal A"),
            },
            Gastos: new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", null, "Compra contado", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null, CondicionPago.Contado, null),
                new("ACME SA", "F-2", null, "Compra crédito", null,
                    new DateOnly(Ejercicio, 1, 20), 700m, "Literal A", 1, null,
                    CondicionPago.Credito, new DateOnly(Ejercicio, 6, 30)),
            },
            LineasPoa: new List<LineaPoaConfirmarDto>());

        var confirmacion = await _repo.ConfirmarAsync(payload, usuarioId: _usuarioId);

        var reversion = await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId);

        Assert.Equal(confirmacion.IdImportacion, reversion.IdImportacion);
        Assert.Equal(2, reversion.GastosRevertidos);
        Assert.Equal(1, reversion.PagosRevertidos);
        Assert.Equal(3, reversion.IngresosRevertidos);

        await using var verificacion = Fixture.CrearContexto();
        Assert.All(await verificacion.Gastos.ToListAsync(), g => Assert.False(g.Activo));
        Assert.All(await verificacion.IngresosCaja.ToListAsync(), i => Assert.False(i.Activo));
        var gastoContado = await verificacion.Gastos.Include(g => g.Pagos).SingleAsync(g => g.NumeroFactura == "F-1");
        Assert.All(gastoContado.Pagos, p => Assert.False(p.Activo));
        // Los maestros (Proveedor/FuenteFinanciamiento/RubroGasto) SIGUEN activos.
        Assert.True(verificacion.Proveedores.Single().Activo);
        Assert.True(verificacion.FuentesFinanciamiento.Single().Activo);
        Assert.True(verificacion.RubrosGasto.Single().Activo);
    }

    [Fact]
    public async Task RevertirAsync_EscribeElLogDeAuditoriaConLosCamposCorrectos()
    {
        // Minor (re-review): sin aserciones directas sobre el LogAuditoria de reversión — solo
        // se verificaba el Detalle (texto libre) indirectamente en otros tests.
        var confirmacion = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);

        await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId);

        await using var verificacion = Fixture.CrearContexto();
        var log = await verificacion.LogsAuditoria
            .SingleAsync(l => l.Accion == AccionAuditada.ReversionImportacion);
        Assert.Equal(AccionAuditada.ReversionImportacion, log.Accion);
        Assert.Equal(confirmacion.IdImportacion, log.IdLote);
        Assert.Equal(Ejercicio, log.EntidadId);
        Assert.Equal(_usuarioId, log.UsuarioId);
    }

    [Fact]
    public async Task RevertirAsync_LineaPoaDelLote_QuedaInactivaConSusAsignacionesColgando()
    {
        var confirmacion = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: _usuarioId);

        var reversion = await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId);

        Assert.Equal(1, reversion.LineasPoaRevertidas);
        Assert.Equal(2, reversion.AsignacionesRevertidas);
        await using var verificacion = Fixture.CrearContexto();
        var linea = verificacion.LineasPoa.Include(l => l.Asignaciones).Single();
        Assert.False(linea.Activo);
        Assert.Equal(2, linea.Asignaciones.Count); // siguen ahí, colgando de la línea inactiva
    }

    [Fact]
    public async Task RevertirAsync_IdInexistente_LanzaEntidadNoEncontrada()
    {
        await Assert.ThrowsAsync<StockApp.Domain.Exceptions.EntidadNoEncontradaException>(
            () => _repo.RevertirAsync(Guid.NewGuid(), usuarioId: _usuarioId));
    }

    [Fact]
    public async Task RevertirAsync_LoteYaRevertido_LanzaReglaDeNegocio()
    {
        var confirmacion = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);
        await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId);

        await Assert.ThrowsAsync<StockApp.Domain.Exceptions.ReglaDeNegocioException>(
            () => _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId));
    }

    [Fact]
    public async Task CicloCompleto_ConfirmarRevertirConfirmarSinForzar_SegundaConfirmacionEs200()
    {
        var primera = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);
        await _repo.RevertirAsync(primera.IdImportacion, usuarioId: _usuarioId);

        // El guard de Task 6 no cuenta las corridas YA revertidas — sin Forzar tiene que andar.
        var segunda = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: false), usuarioId: _usuarioId);

        Assert.Equal(1, segunda.GastosCreados); // la clave natural del gasto revertido está Activo=false, no bloquea
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(2, await verificacion.Gastos.CountAsync()); // el revertido + el nuevo
        Assert.Equal(1, await verificacion.Gastos.CountAsync(g => g.Activo));
    }

    [Fact]
    public async Task CicloCompleto_ConLineaPoa_ConfirmarRevertirConfirmarRevertir_LaLineaSigueElEstadoDelUltimoLote()
    {
        // IMPORTANT 3 (re-review): los dos tests de ciclo de arriba usan PayloadConIngresoYGasto,
        // cuyo LineasPoa es SIEMPRE una lista vacía — el ciclo completo con LineaPoa (gasto
        // vinculado a una línea POA que se reactiva/desactiva en cada vuelta) no tenía NINGÚN
        // test de punta a punta. Ejercita: confirmar A (crea la línea) → revertir A (la
        // desactiva) → confirmar B SIN Forzar (la reactiva y re-estampa IdImportacion = B) →
        // revertir B (la vuelve a desactivar).
        var payloadA = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string> { "ACME SA" }, new List<string> { "Literal A" },
                new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
            Ingresos: new List<IngresoConfirmarDto>(),
            Gastos: new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", null, "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, "COMPOSTERAS",
                    CondicionPago.Contado, null),
            },
            LineasPoa: new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto> { new("Literal A", 1000m) }),
            });

        var confirmacionA = await _repo.ConfirmarAsync(payloadA, usuarioId: _usuarioId);
        Assert.Equal(1, confirmacionA.LineasPoaCreadas);

        await _repo.RevertirAsync(confirmacionA.IdImportacion, usuarioId: _usuarioId);

        await using (var ctxTrasRevertirA = Fixture.CrearContexto())
        {
            var lineaTrasA = await ctxTrasRevertirA.LineasPoa.SingleAsync(l => l.Nombre == "COMPOSTERAS");
            Assert.False(lineaTrasA.Activo);
        }

        // B: mismos maestros (ya existen, no hace falta declararlos de nuevo), gasto distinto
        // (otra factura) vinculado a la MISMA línea POA. Sin Forzar: el guard no bloquea porque
        // A ya está revertida.
        var payloadB = payloadA with
        {
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-2", null, "Compra de insumos 2", null,
                    new DateOnly(Ejercicio, 2, 15), 300m, "Literal A", 1, "COMPOSTERAS",
                    CondicionPago.Contado, null),
            },
        };

        var confirmacionB = await _repo.ConfirmarAsync(payloadB, usuarioId: _usuarioId);

        Assert.Equal(0, confirmacionB.LineasPoaCreadas);
        Assert.Equal(1, confirmacionB.LineasPoaReactivadas);

        await using (var ctxTrasConfirmarB = Fixture.CrearContexto())
        {
            var lineaTrasB = await ctxTrasConfirmarB.LineasPoa.SingleAsync(l => l.Nombre == "COMPOSTERAS");
            Assert.True(lineaTrasB.Activo);
            Assert.Equal(confirmacionB.IdImportacion, lineaTrasB.IdImportacion);
        }

        await _repo.RevertirAsync(confirmacionB.IdImportacion, usuarioId: _usuarioId);

        await using var verificacionFinal = Fixture.CrearContexto();
        var lineaFinal = await verificacionFinal.LineasPoa.SingleAsync(l => l.Nombre == "COMPOSTERAS");
        Assert.False(lineaFinal.Activo);
    }

    // ── Re-review de Task 8: CRITICAL 1 (movimientos atrapados) ─────────────────────────────────

    [Fact]
    public async Task RevertirAsync_MovimientoAsociadoAlGastoRevertido_QuedaLibreYSePuedeReasociar()
    {
        // CRITICAL 1 (re-review): GastoService.AsociarMovimientosAsync acepta cualquier gasto
        // ACTIVO, incluidos los importados — no distingue el origen. Antes del fix, RevertirAsync
        // desactivaba el gasto pero dejaba MovimientoStock.GastoId apuntando al gasto ahora
        // inactivo: ni se podía refacturar (ValidarMovimientosAsync rechaza un movimiento con
        // GastoId no nulo) ni liberar (el gasto ya está anulado, GastoService.AnularAsync
        // rechaza anular dos veces). Sin acceso al servidor, ese estado era permanente — la clase
        // de bug que esta reversa existe para eliminar.
        var confirmacion = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);
        var gasto = await Context.Gastos.SingleAsync(g => g.Detalle == "Compra de insumos");

        var unidad = new UnidadMedida { Nombre = "Unidad", Abreviatura = "u" };
        var usuarioStock = new Usuario
        {
            NombreUsuario = "operador-stock", HashContrasena = "hash", Rol = RolUsuario.Operador,
            Activo = true, FechaAlta = DateTime.UtcNow,
        };
        Context.AddRange(unidad, usuarioStock);
        await Context.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = "PROD-1", Nombre = "Prod test", UnidadMedidaId = unidad.Id,
        };
        Context.Productos.Add(producto);
        await Context.SaveChangesAsync();
        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuarioStock.Id,
            Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra,
            Cantidad = 5m, PrecioUnitario = 100m, Fecha = DateTime.UtcNow,
            // Simula GastoService.AsociarMovimientosAsync sobre el gasto importado — el service
            // no distingue el origen del gasto, cualquier gasto Activo es un destino válido.
            GastoId = gasto.Id,
        };
        Context.MovimientosStock.Add(movimiento);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId);

        await using var verificacion = Fixture.CrearContexto();
        var movimientoRevertido = await verificacion.MovimientosStock.SingleAsync(m => m.Id == movimiento.Id);
        Assert.Null(movimientoRevertido.GastoId);

        // No alcanza con ver el campo en null en esta lectura: hay que probar que realmente
        // quedó libre, re-asociándolo a un gasto NUEVO sin ningún conflicto.
        var otroProveedor = new Proveedor { Nombre = "Otro Proveedor" };
        var otraFuente = new FuenteFinanciamiento { Nombre = "Otra Fuente" };
        var otroRubro = new RubroGasto { Codigo = 99, Nombre = "Otro Rubro" };
        verificacion.AddRange(otroProveedor, otraFuente, otroRubro);
        await verificacion.SaveChangesAsync();
        var gastoNuevo = new Gasto
        {
            Proveedor = otroProveedor, FuenteFinanciamiento = otraFuente, RubroGasto = otroRubro,
            Detalle = "Refacturación", Fecha = DateTime.UtcNow, MontoTotal = 500m,
            CondicionPago = CondicionPago.Contado,
        };
        verificacion.Gastos.Add(gastoNuevo);
        await verificacion.SaveChangesAsync();

        movimientoRevertido.GastoId = gastoNuevo.Id;
        await verificacion.SaveChangesAsync();

        await using var verificacionFinal = Fixture.CrearContexto();
        var movimientoFinal = await verificacionFinal.MovimientosStock.SingleAsync(m => m.Id == movimiento.Id);
        Assert.Equal(gastoNuevo.Id, movimientoFinal.GastoId);
    }

    // ── Re-review de Task 8: IMPORTANT 2 (pagos manuales bloquean la reversa) ───────────────────

    private static ConfirmarImportacionDto PayloadUnGastoCredito(
        string proveedor = "ACME SA", string numeroFactura = "F-1", decimal monto = 500m) => new(
        Ejercicio: Ejercicio,
        Forzar: false,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string> { proveedor }, new List<string> { "Literal A" },
            new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>
        {
            new(proveedor, numeroFactura, null, "Compra a crédito", null,
                new DateOnly(Ejercicio, 1, 15), monto, "Literal A", 1, null,
                CondicionPago.Credito, new DateOnly(Ejercicio, 6, 30)),
        },
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task RevertirAsync_GastoConPagoManualNoCreadoPorElLote_LanzaReglaDeNegocioYNombraElGasto()
    {
        // IMPORTANT 2 (re-review, decisión del usuario): un gasto a crédito sin pago se importa;
        // semanas después un operador registra un pago PARCIAL real (plata que efectivamente
        // salió) — ese PagoGasto NO tiene IdImportacion (no lo creó el importador). La reversa
        // tiene que BLOQUEARSE (409) en vez de dar de baja en silencio un pago real, mismo
        // criterio que GastoService.AnularAsync aplica a la anulación individual.
        var confirmacion = await _repo.ConfirmarAsync(PayloadUnGastoCredito(), usuarioId: _usuarioId);
        var gasto = await Context.Gastos.SingleAsync(g => g.Detalle == "Compra a crédito");
        Context.PagosGasto.Add(new PagoGasto
        {
            GastoId = gasto.Id, Fecha = DateTime.UtcNow, Monto = 200m,
            Nota = "Pago parcial real", IdImportacion = null,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId));

        Assert.Contains("ACME SA", ex.Message);
        Assert.Contains("F-1", ex.Message);

        // Nada se escribió: el gasto sigue activo y el pago manual intacto.
        await using var verificacion = Fixture.CrearContexto();
        Assert.True(verificacion.Gastos.Single().Activo);
        Assert.True(verificacion.PagosGasto.Single(p => p.Nota == "Pago parcial real").Activo);
    }

    [Fact]
    public async Task RevertirAsync_TodosLosPagosDelPropioLote_RevierteSinBloquear()
    {
        // El pago automático de contado que ProcesarGastosAsync crea ya viene estampado con
        // IdImportacion = el lote (fix de este mismo re-review) — este test confirma que ESO no
        // bloquea la reversa: solo un pago con IdImportacion DISTINTO la bloquea.
        var confirmacion = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: _usuarioId);

        var reversion = await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId);

        Assert.Equal(1, reversion.PagosRevertidos);
        await using var verificacion = Fixture.CrearContexto();
        Assert.False(verificacion.PagosGasto.Single().Activo);
    }

    [Fact]
    public async Task RevertirAsync_DespuesDeAnularElPagoManual_LaReversaProcede()
    {
        var confirmacion = await _repo.ConfirmarAsync(PayloadUnGastoCredito(), usuarioId: _usuarioId);
        var gasto = await Context.Gastos.SingleAsync(g => g.Detalle == "Compra a crédito");
        Context.PagosGasto.Add(new PagoGasto
        {
            GastoId = gasto.Id, Fecha = DateTime.UtcNow, Monto = 200m,
            Nota = "Pago parcial real", IdImportacion = null,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId));

        // El humano anula el pago manual desde el desktop (GastoService.AnularPagoAsync) — acá
        // se simula directamente la baja lógica, que es lo único que ese flujo hace.
        await using (var ctxAnular = Fixture.CrearContexto())
        {
            var pago = await ctxAnular.PagosGasto.SingleAsync(p => p.Nota == "Pago parcial real");
            pago.Activo = false;
            await ctxAnular.SaveChangesAsync();
        }
        Context.ChangeTracker.Clear();

        var reversion = await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: _usuarioId);

        Assert.Equal(confirmacion.IdImportacion, reversion.IdImportacion);
        await using var verificacion = Fixture.CrearContexto();
        Assert.False(verificacion.Gastos.Single().Activo);
    }
}

/// <summary>
/// Variante que inyecta una fila inválida (LogAuditoria.Detalle = null, viola NOT NULL) mediante
/// el seam ImportacionRepository.AntesDeGuardarAsync, para forzar un DbUpdateException REAL de
/// Postgres DENTRO de la transacción explícita de ConfirmarAsync. Mismo patrón que
/// MovimientoStockRepositoryConDetalleNulo en MovimientoStockRepositoryTests.cs (test "C4"). Usada
/// solo en ConfirmarAsync_SaveChangesFallaPorConstraintReal_RevierteTodoElGrafoDeLaCorrida.
/// </summary>
internal sealed class ImportacionRepositoryConFalloInyectado : ImportacionRepository
{
    private readonly AppDbContext _ctx;
    private readonly int _usuarioId;

    public ImportacionRepositoryConFalloInyectado(AppDbContext ctx, int usuarioId) : base(ctx)
    {
        _ctx = ctx;
        _usuarioId = usuarioId;
    }

    protected override Task AntesDeGuardarAsync()
    {
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = _usuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = AccionAuditada.AltaProveedor,
            Entidad   = "Importacion",
            EntidadId = 0,
            Detalle   = null!   // viola NOT NULL → SaveChangesAsync lanza DbUpdateException real
        });
        return Task.CompletedTask;
    }
}

/// <summary>
/// Variante que bypassea POR COMPLETO el dedupe en memoria de ProcesarGastosAsync, agregando
/// directo al ChangeTracker un segundo Gasto con la MISMA (ProveedorId, NumeroFactura,
/// NumeroOrden) que uno ya activo en la base — vía el mismo seam AntesDeGuardarAsync que
/// ImportacionRepositoryConFalloInyectado. Fuerza que sea Postgres (índice único parcial
/// IX_Gastos_ProveedorId_NumeroFactura_NumeroOrden, F5c), no la lógica de la aplicación, quien
/// rechace la fila — así se prueba de forma REAL (no simulada) que la red de seguridad de A.5
/// traduce el 23505 a ReglaDeNegocioException. Usada solo en
/// ConfirmarAsync_ViolacionDeIndiceUnicoRealForzada_LaMapeaAReglaDeNegocioException.
/// </summary>
internal sealed class ImportacionRepositoryConFacturaDuplicadaInyectada : ImportacionRepository
{
    private readonly AppDbContext _ctx;
    private readonly int _proveedorId;
    private readonly int _fuenteId;
    private readonly int _rubroId;

    public ImportacionRepositoryConFacturaDuplicadaInyectada(
        AppDbContext ctx, int proveedorId, int fuenteId, int rubroId) : base(ctx)
    {
        _ctx = ctx;
        _proveedorId = proveedorId;
        _fuenteId = fuenteId;
        _rubroId = rubroId;
    }

    protected override Task AntesDeGuardarAsync()
    {
        _ctx.Gastos.Add(new Gasto
        {
            ProveedorId = _proveedorId,
            NumeroFactura = "F-1",
            // F5c: el índice ahora es (ProveedorId, NumeroFactura, NumeroOrden). El gasto
            // original de PayloadUnGastoConFactura("F-1") usa el NumeroOrden por defecto
            // "O-1" — sin repetirlo acá, este inyectado ya NO colisionaría con el índice
            // ampliado y el 23505 real que este test necesita forzar no ocurriría.
            NumeroOrden = "O-1",
            Detalle = "Inyectado para forzar 23505 (test A.5)",
            Fecha = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            MontoTotal = 999m,
            FuenteFinanciamientoId = _fuenteId,
            RubroGastoId = _rubroId,
            CondicionPago = CondicionPago.Contado,
        });
        return Task.CompletedTask;
    }
}
