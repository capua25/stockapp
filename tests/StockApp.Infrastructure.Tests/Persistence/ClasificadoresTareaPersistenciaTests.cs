using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

/// <summary>
/// Ejercita la configuración de AppDbContext (índice único + default Activo) para los cuatro
/// catálogos nuevos directamente contra los DbSet, ANTES de que exista ningún repositorio
/// (Task 3). Roundtrip básico + violación de unicidad, mismo criterio que
/// DocumentoAdministrativoIndiceUnicoTests del módulo de Documentos.
/// </summary>
public class ClasificadoresTareaPersistenciaTests : PostgresRepositoryTestBase
{
    public ClasificadoresTareaPersistenciaTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Zona_Roundtrip_ActivoPorDefectoTrue()
    {
        Context.Zonas.Add(new Zona { Nombre = "Centro" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var zona = await Context.Zonas.SingleAsync(z => z.Nombre == "Centro");
        Assert.True(zona.Activo);
    }

    [Fact]
    public async Task Zona_NombreDuplicado_ViolaIndiceUnico()
    {
        Context.Zonas.Add(new Zona { Nombre = "Centro" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.Zonas.Add(new Zona { Nombre = "Centro" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task DimensionTematica_Roundtrip_ActivoPorDefectoTrue()
    {
        Context.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Infraestructura" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var dimension = await Context.DimensionesTematicas.SingleAsync(d => d.Nombre == "Infraestructura");
        Assert.True(dimension.Activo);
    }

    [Fact]
    public async Task DimensionTematica_NombreDuplicado_ViolaIndiceUnico()
    {
        Context.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Infraestructura" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Infraestructura" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task OrganismoResponsable_Roundtrip_ActivoPorDefectoTrue()
    {
        Context.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var organismo = await Context.OrganismosResponsables.SingleAsync(o => o.Nombre == "Intendencia de Colonia");
        Assert.True(organismo.Activo);
    }

    [Fact]
    public async Task OrganismoResponsable_NombreDuplicado_ViolaIndiceUnico()
    {
        Context.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task OrigenFinanciamiento_Roundtrip_ActivoPorDefectoTrue()
    {
        Context.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var origen = await Context.OrigenesFinanciamiento.SingleAsync(o => o.Nombre == "Presupuesto propio");
        Assert.True(origen.Activo);
    }

    [Fact]
    public async Task OrigenFinanciamiento_NombreDuplicado_ViolaIndiceUnico()
    {
        Context.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }
}
