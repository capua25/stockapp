using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
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
        var fuentesEnBase = await Fixture.CrearContexto().FuentesFinanciamiento.ToListAsync();
        Assert.Contains(fuentesEnBase, f => f.Nombre == "Literal B");
        Assert.Contains(fuentesEnBase, f => f.Nombre == "Literal C");
    }

    [Fact]
    public async Task ConfirmarAsync_FuenteYaExistente_NoLaDuplicaYNoLaCuentaComoCreada()
    {
        Context.FuentesFinanciamiento.Add(new StockApp.Domain.Entities.FuenteFinanciamiento { Nombre = "Literal B" });
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
        var primero = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

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
}
