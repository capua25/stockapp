using Moq;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Movimientos;

/// <summary>
/// Verifica que IMovimientoStockRepository e IMovimientoStockService
/// existen y son moqueables con Moq (compilación + mockeo = test de contrato).
/// </summary>
public class InterfacesTest
{
    [Fact]
    public void IMovimientoStockRepository_EsMockeable()
    {
        var mock = new Mock<IMovimientoStockRepository>();

        // Verificar que los métodos de la interfaz existen y son configurables
        mock.Setup(r => r.ObtenerProductoAsync(It.IsAny<int>()))
            .ReturnsAsync((Producto?)null);

        mock.Setup(r => r.SumarMovimientosAsync(It.IsAny<int>()))
            .ReturnsAsync((0m, 0));

        mock.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(1);

        mock.Setup(r => r.RecalcularAtomicoAsync(It.IsAny<RecalculoAtomicoArgs>()))
            .Returns(Task.CompletedTask);

        mock.Setup(r => r.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(new List<MovimientoHistorialDto>());

        Assert.NotNull(mock.Object);
    }

    [Fact]
    public void IMovimientoStockService_EsMockeable()
    {
        var mock = new Mock<IMovimientoStockService>();

        mock.Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), It.IsAny<bool>()))
            .ReturnsAsync(new MovimientoRegistradoDto(
                MovimientoId: 1,
                ProductoId: 1,
                Tipo: TipoMovimiento.Entrada,
                Motivo: MotivoMovimiento.Compra,
                Cantidad: 1m,
                PrecioUnitario: 10m,
                StockAnterior: 0m,
                StockNuevo: 1m,
                Fecha: DateTime.UtcNow));

        mock.Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(new List<MovimientoHistorialDto>());

        mock.Setup(s => s.RecalcularStockAsync(It.IsAny<int>()))
            .ReturnsAsync(new RecalculoResultadoDto(1, 0m, 0m, 0));

        Assert.NotNull(mock.Object);
    }
}
