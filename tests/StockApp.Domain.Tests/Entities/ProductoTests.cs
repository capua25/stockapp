using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class ProductoTests
{
    [Fact]
    public void Producto_Nuevo_TieneStockMinimoEnCero()
    {
        var producto = new Producto
        {
            Codigo = "SKU001",
            Nombre = "Tornillo 6x1",
            UnidadMedidaId = 1,
            PrecioCosto = 10.50m,
            PrecioVenta = 15.00m,
            FechaAlta = DateTime.UtcNow
        };

        Assert.Equal(0m, producto.StockMinimo);
        Assert.Equal(0m, producto.StockActual);
        Assert.True(producto.Activo);
    }

    [Fact]
    public void Producto_CodigoBarras_EsOpcional()
    {
        var producto = new Producto
        {
            Codigo = "SKU002",
            Nombre = "Pintura blanca 1L",
            UnidadMedidaId = 2,
            PrecioCosto = 500m,
            PrecioVenta = 750m,
            FechaAlta = DateTime.UtcNow
        };

        Assert.Null(producto.CodigoBarras);
    }
}
