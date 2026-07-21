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
/// transacción real. Ingresos/Gastos (Task 5) y guard/auditoría (Task 6) se agregan en tasks
/// posteriores sobre el MISMO ConfirmarAsync — acá los contadores respectivos quedan en 0.
/// </summary>
public class ImportacionRepositoryTests : PostgresRepositoryTestBase
{
    private const int Ejercicio = 2026;
    private readonly ImportacionRepository _repo;

    public ImportacionRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ImportacionRepository(Context);
    }

    private static ConfirmarImportacionDto PayloadSoloMaestrosYPoa(
        IReadOnlyList<string>? proveedoresNuevos = null,
        IReadOnlyList<string>? fuentesNuevas = null,
        IReadOnlyList<RubroNuevoConfirmarDto>? rubrosNuevos = null,
        IReadOnlyList<LineaPoaConfirmarDto>? lineasPoa = null) => new(
        Ejercicio: Ejercicio,
        Forzar: false,
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
        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        Assert.Equal(1, resultado.FuentesCreadas); // solo "Literal C" es nueva
        await using var verificacion = Fixture.CrearContexto();
        var cantidad = verificacion.FuentesFinanciamiento.Count(f => f.Nombre == "Literal B");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaConFinanciamientoMixto_CreaLaLineaConDosAsignaciones()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

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
        await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        var segundo = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

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
        await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

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
            () => _repo.ConfirmarAsync(payload, usuarioId: 1));

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

        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        Assert.Equal(1, resultado.FuentesCreadas); // "Literal B" ya existe (2 filas); solo "Literal C" es nueva
    }

    [Fact]
    public async Task ConfirmarAsync_ProveedoresNuevos_LosCreaYDevuelveElContador()
    {
        var payload = PayloadSoloMaestrosYPoa(
            proveedoresNuevos: new List<string> { "Ferretería Lopez", "Corralón Pérez" });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: 1);

        Assert.Equal(0, resultado.ProveedoresCreados);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Proveedores.CountAsync(p => p.Nombre == "Ferretería Lopez"));
    }

    [Fact]
    public async Task ConfirmarAsync_RubrosNuevos_LosCreaYDevuelveElContador()
    {
        var payload = PayloadSoloMaestrosYPoa(
            rubrosNuevos: new List<RubroNuevoConfirmarDto> { new(5, "Combustibles"), new(9, "Viáticos") });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: 1);

        Assert.Equal(0, resultado.RubrosCreados);
        Assert.Equal(1, resultado.RubrosReactivados);
        await using var verificacion = Fixture.CrearContexto();
        var rubro = await verificacion.RubrosGasto.SingleAsync(r => r.Codigo == 5);
        Assert.True(rubro.Activo);
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaInactivaPorReversionAnterior_LaReactivaYReestampaIdImportacion()
    {
        // Important #3 (review): ciclo confirmar → revertir → confirmar dado como escenario de
        // aceptación explícito en el spec. RevertirAsync todavía no existe (Task 8), así que la
        // reversa se simula a mano: baja lógica directa de la línea, tal como haría /revertir.
        var primero = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        await using (var ctxRevertir = Fixture.CrearContexto())
        {
            var linea = await ctxRevertir.LineasPoa.SingleAsync(l => l.Nombre == "COMPOSTERAS");
            linea.Activo = false;
            await ctxRevertir.SaveChangesAsync();
        }

        // El Context del repo todavía tiene la línea trackeada (Activo = true, desactualizado
        // porque la baja de arriba pasó por OTRO DbContext) — sin este Clear, el identity map de
        // EF Core devolvería la instancia vieja en la próxima query y el bug reaparecería en
        // silencio.
        Context.ChangeTracker.Clear();

        var segundo = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

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
        var primero = await _repo.ConfirmarAsync(payloadInicial, usuarioId: 1);

        var payloadSegundo = PayloadSoloMaestrosYPoa(
            fuentesNuevas: new List<string> { "Literal C" },
            lineasPoa: new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto>
                {
                    new("Literal B", 92748m),
                    new("Literal C", 1407252m),
                }),
            });
        var segundo = await _repo.ConfirmarAsync(payloadSegundo, usuarioId: 1);

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
        var resultado = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 1);

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
        await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 1);

        var segunda = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: true), usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: 1);

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
        await _repo.ConfirmarAsync(PayloadUnGastoConFactura(monto: 500m), usuarioId: 1);

        var resultado = await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(monto: 550m, forzar: true, proveedoresNuevos: new List<string>()),
            usuarioId: 1);

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
        await _repo.ConfirmarAsync(PayloadUnGastoConFactura(numeroFactura: "F-1"), usuarioId: 1);

        var resultado = await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(numeroFactura: " f-1 ", forzar: true, proveedoresNuevos: new List<string>()),
            usuarioId: 1);

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
            PayloadUnGastoConFactura(numeroFactura: null, numeroOrden: "O-1"), usuarioId: 1);

        var resultado = await _repo.ConfirmarAsync(
            PayloadUnGastoConFactura(numeroFactura: "", numeroOrden: "O-1", forzar: true, proveedoresNuevos: new List<string>()),
            usuarioId: 1);

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
        await _repo.ConfirmarAsync(payload, usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(payloadRepetido, usuarioId: 1);

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
        await _repo.ConfirmarAsync(payload, usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(payloadRepetido, usuarioId: 1);

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
        await _repo.ConfirmarAsync(payload, usuarioId: 1);

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

        var resultado = await _repo.ConfirmarAsync(payloadDistinto, usuarioId: 1);

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

        await _repo.ConfirmarAsync(payload, usuarioId: 1);

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
        await _repo.ConfirmarAsync(PayloadUnGastoConFactura(numeroFactura: "F-1"), usuarioId: 1);

        var proveedor = await Context.Proveedores.SingleAsync(p => p.Nombre == "ACME SA");
        var fuente = await Context.FuentesFinanciamiento.SingleAsync(f => f.Nombre == "Literal A");
        var rubro = await Context.RubrosGasto.SingleAsync(r => r.Codigo == 1);
        Context.ChangeTracker.Clear();

        var repoRoto = new ImportacionRepositoryConFacturaDuplicadaInyectada(Context, proveedor.Id, fuente.Id, rubro.Id);
        var payloadSinNadaNuevo = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
            Ingresos: new List<IngresoConfirmarDto>(),
            Gastos: new List<GastoConfirmarDto>(),
            LineasPoa: new List<LineaPoaConfirmarDto>());

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => repoRoto.ConfirmarAsync(payloadSinNadaNuevo, usuarioId: 1));

        Assert.Contains("IX_Gastos_ProveedorId_NumeroFactura", ex.Message);

        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync()); // el inyectado NO quedó persistido (rollback)
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
/// directo al ChangeTracker un segundo Gasto con la MISMA (ProveedorId, NumeroFactura) que uno ya
/// activo en la base — vía el mismo seam AntesDeGuardarAsync que ImportacionRepositoryConFalloInyectado.
/// Fuerza que sea Postgres (índice único parcial IX_Gastos_ProveedorId_NumeroFactura), no la
/// lógica de la aplicación, quien rechace la fila — así se prueba de forma REAL (no simulada) que
/// la red de seguridad de A.5 traduce el 23505 a ReglaDeNegocioException. Usada solo en
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
