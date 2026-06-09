using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

/// <summary>
/// Verifica que Categoria, Proveedor y UnidadMedida tienen la propiedad Activo
/// con valor por defecto true (baja lógica, Incremento 4).
/// </summary>
public class CatalogoBajaLogicaTests
{
    // ── Categoria ────────────────────────────────────────────────────────────

    [Fact]
    public void Categoria_Nueva_TieneActivoEnTrue()
    {
        var categoria = new Categoria { Nombre = "Lácteos" };

        Assert.True(categoria.Activo);
    }

    [Fact]
    public void Categoria_Activo_SePuedePonersEnFalse()
    {
        var categoria = new Categoria { Nombre = "Bebidas", Activo = false };

        Assert.False(categoria.Activo);
    }

    // ── Proveedor ─────────────────────────────────────────────────────────────

    [Fact]
    public void Proveedor_Nuevo_TieneActivoEnTrue()
    {
        var proveedor = new Proveedor { Nombre = "DistribuidoraX" };

        Assert.True(proveedor.Activo);
    }

    [Fact]
    public void Proveedor_Activo_SePuedePonersEnFalse()
    {
        var proveedor = new Proveedor { Nombre = "DistribuidoraY", Activo = false };

        Assert.False(proveedor.Activo);
    }

    // ── UnidadMedida ──────────────────────────────────────────────────────────

    [Fact]
    public void UnidadMedida_Nueva_TieneActivoEnTrue()
    {
        var unidad = new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg" };

        Assert.True(unidad.Activo);
    }

    [Fact]
    public void UnidadMedida_Activo_SePuedePonersEnFalse()
    {
        var unidad = new UnidadMedida { Nombre = "Litro", Abreviatura = "l", Activo = false };

        Assert.False(unidad.Activo);
    }
}
