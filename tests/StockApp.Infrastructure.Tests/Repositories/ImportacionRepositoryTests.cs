using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
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
}
