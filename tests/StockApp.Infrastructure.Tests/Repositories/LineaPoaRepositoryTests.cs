using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class LineaPoaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly LineaPoaRepository _repo;
    private readonly FuenteFinanciamientoRepository _fuentes;

    public LineaPoaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new LineaPoaRepository(Context);
        _fuentes = new FuenteFinanciamientoRepository(Context);
    }

    private async Task<int> SeedFuenteAsync(string nombre)
        => await _fuentes.AgregarAsync(new FuenteFinanciamiento { Nombre = nombre });

    [Fact]
    public async Task AgregarAsync_ConAsignaciones_Y_ObtenerPorId_IncluyeAsignaciones()
    {
        var fuenteB = await SeedFuenteAsync("Literal B");
        var fuenteC = await SeedFuenteAsync("Literal C");

        var id = await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "COMPOSTERAS",
            Programa = "Ambiente",
            Ejercicio = 2026,
            Asignaciones =
            {
                new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteB, Monto = 100000m },
                new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteC, Monto = 50000.5000m },
            },
        });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("COMPOSTERAS", found!.Nombre);
        Assert.Equal(2, found.Asignaciones.Count);
        // El Include trae la nav de la fuente para poder mostrar su nombre en la grilla
        Assert.All(found.Asignaciones, a => Assert.NotNull(a.FuenteFinanciamiento));
        Assert.Contains(found.Asignaciones, a => a.Monto == 50000.5000m);
    }

    [Fact]
    public async Task ActualizarAsync_ReemplazaLasAsignaciones()
    {
        var fuenteB = await SeedFuenteAsync("Literal B");
        var fuenteC = await SeedFuenteAsync("Literal C");
        var id = await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "PRENSA",
            Programa = "Comunicación",
            Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteB, Monto = 80000m } },
        });
        Context.ChangeTracker.Clear();

        var original = await _repo.ObtenerPorIdAsync(id);
        original!.Programa = "Prensa y Comunicación";
        await _repo.ActualizarAsync(original, new List<AsignacionPresupuestal>
        {
            new() { FuenteFinanciamientoId = fuenteC, Monto = 120000m },
        });
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Prensa y Comunicación", updated!.Programa);
        var asignacion = Assert.Single(updated.Asignaciones);
        Assert.Equal(fuenteC, asignacion.FuenteFinanciamientoId);
        Assert.Equal(120000m, asignacion.Monto);
    }

    [Fact]
    public async Task ListarTodasAsync_OrdenaPorEjercicioDescYNombre_ConAsignaciones()
    {
        var fuente = await SeedFuenteAsync("Literal B");
        await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "Rambla", Programa = "Obras", Ejercicio = 2025,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuente, Monto = 1m } },
        });
        await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "Eventos", Programa = "Cultura", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuente, Monto = 2m } },
        });
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Eventos", result[0].Nombre);   // 2026 antes que 2025
        Assert.Equal("Rambla", result[1].Nombre);
        Assert.NotEmpty(result[0].Asignaciones);
    }

    [Fact]
    public async Task ActualizarSinAsignacionesAsync_NoCambiaLosIdsDeLasAsignacionesExistentes()
    {
        var fuente = await SeedFuenteAsync("Literal B");
        var id = await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "PRENSA", Programa = "Comunicación", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuente, Monto = 80000m } },
        });
        Context.ChangeTracker.Clear();

        var original = await _repo.ObtenerPorIdAsync(id);
        var idAsignacionOriginal = original!.Asignaciones.Single().Id;
        original.Activo = false;
        await _repo.ActualizarSinAsignacionesAsync(original);
        Context.ChangeTracker.Clear();

        var actualizada = await _repo.ObtenerPorIdAsync(id);
        Assert.False(actualizada!.Activo);
        var asignacion = Assert.Single(actualizada.Asignaciones);
        // Si se hubiera hecho delete+insert (como ActualizarAsync), el Id cambiaría: la
        // baja lógica NO debe reinsertar físicamente las asignaciones.
        Assert.Equal(idAsignacionOriginal, asignacion.Id);
    }

    [Fact]
    public async Task ExisteNombreEjercicioAsync_DistingueEjercicios()
    {
        var fuente = await SeedFuenteAsync("Literal B");
        var id = await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuente, Monto = 1m } },
        });

        Assert.True(await _repo.ExisteNombreEjercicioAsync("Rambla", 2026));
        Assert.False(await _repo.ExisteNombreEjercicioAsync("Rambla", 2027));
        Assert.False(await _repo.ExisteNombreEjercicioAsync("Rambla", 2026, excluyendoId: id));
    }
}
