using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class ClasificadoresTareaTests
{
    [Fact]
    public void Zona_Defaults_NombreVacioYActivoTrue()
    {
        var zona = new Zona();

        Assert.Equal(0, zona.Id);
        Assert.Equal(string.Empty, zona.Nombre);
        Assert.True(zona.Activo);
    }

    [Fact]
    public void DimensionTematica_Defaults_NombreVacioYActivoTrue()
    {
        var dimension = new DimensionTematica();

        Assert.Equal(0, dimension.Id);
        Assert.Equal(string.Empty, dimension.Nombre);
        Assert.True(dimension.Activo);
    }

    [Fact]
    public void OrganismoResponsable_Defaults_NombreVacioYActivoTrue()
    {
        var organismo = new OrganismoResponsable();

        Assert.Equal(0, organismo.Id);
        Assert.Equal(string.Empty, organismo.Nombre);
        Assert.True(organismo.Activo);
    }

    [Fact]
    public void OrigenFinanciamiento_Defaults_NombreVacioYActivoTrue()
    {
        var origen = new OrigenFinanciamiento();

        Assert.Equal(0, origen.Id);
        Assert.Equal(string.Empty, origen.Nombre);
        Assert.True(origen.Activo);
    }

    [Fact]
    public void CuatroEntidades_PuedenAsignarNombreYDarDeBaja()
    {
        var zona = new Zona { Id = 1, Nombre = "Centro", Activo = false };
        var dimension = new DimensionTematica { Id = 2, Nombre = "Infraestructura", Activo = false };
        var organismo = new OrganismoResponsable { Id = 3, Nombre = "Intendencia de Colonia", Activo = false };
        var origen = new OrigenFinanciamiento { Id = 4, Nombre = "Presupuesto propio", Activo = false };

        Assert.Equal("Centro", zona.Nombre);
        Assert.Equal("Infraestructura", dimension.Nombre);
        Assert.Equal("Intendencia de Colonia", organismo.Nombre);
        Assert.Equal("Presupuesto propio", origen.Nombre);
        Assert.False(zona.Activo);
        Assert.False(dimension.Activo);
        Assert.False(organismo.Activo);
        Assert.False(origen.Activo);
    }
}
