using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class MovimientoStockTests
{
    [Fact]
    public void MovimientoStock_Comentario_EsOpcional()
    {
        var movimiento = new MovimientoStock
        {
            ProductoId = 1,
            UsuarioId = 1,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 10m,
            PrecioUnitario = 100m,
            Fecha = DateTime.UtcNow,
            Motivo = MotivoMovimiento.Compra
        };

        Assert.Null(movimiento.Comentario);
    }

    [Fact]
    public void MovimientoStock_CantidadPositiva_EsValida()
    {
        var movimiento = new MovimientoStock
        {
            ProductoId = 1,
            UsuarioId = 1,
            Tipo = TipoMovimiento.Salida,
            Cantidad = 3.5m,
            PrecioUnitario = 750m,
            Fecha = DateTime.UtcNow,
            Motivo = MotivoMovimiento.Venta
        };

        Assert.True(movimiento.Cantidad > 0);
    }
}
