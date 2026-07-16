using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

/// <summary>
/// Vínculo stock ↔ finanzas (spec §5.1): al confirmar una ENTRADA por COMPRA se ofrece
/// el paso OPCIONAL "Asociar factura". Ajustes no lo ofrecen y rechazar no bloquea nada.
/// </summary>
public class EntradaRegistroFacturaTests
{
    private static MovimientoRegistradoDto Registrado(int id = 40) => new(
        MovimientoId: id, ProductoId: 1, Tipo: TipoMovimiento.Entrada,
        Motivo: MotivoMovimiento.Compra, Cantidad: 5m, PrecioUnitario: 500m,
        StockAnterior: 0m, StockNuevo: 5m, Fecha: DateTime.UtcNow);

    private static (EntradaRegistroViewModel vm,
                    Mock<IMovimientoStockService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(bool aceptaAsociar)
    {
        var svc = new Mock<IMovimientoStockService>();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), It.IsAny<bool>()))
            .ReturnsAsync(Registrado());

        var productos = new Mock<IProductoService>();
        productos.Setup(p => p.BuscarAsync(null, null, null))
            .ReturnsAsync(new List<ProductoDto>
            {
                // Firma real de ProductoDto (src/StockApp.Application/Catalogo/Dtos.cs):
                // (Id, Codigo, CodigoBarras, Nombre, Descripcion, CategoriaId, CategoriaNombre,
                //  ProveedorId, UnidadMedidaId, UnidadMedidaNombre, PrecioCosto, PrecioVenta,
                //  StockActual, StockMinimo, Activo, FechaAlta)
                new(1, "COD1", null, "Prod test", null, null, null, null,
                    1, "Unidad", 100m, 200m, 10m, 0m, true, DateTime.UtcNow),
            });

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(aceptaAsociar);

        var vm = new EntradaRegistroViewModel(svc.Object, productos.Object, nav.Object, confirm.Object);
        return (vm, svc, nav, confirm);
    }

    private static async Task RegistrarCompraAsync(EntradaRegistroViewModel vm)
    {
        await vm.InicializarAsync();
        vm.ProductoSeleccionado = vm.Productos[0];
        vm.Cantidad = 5m;
        vm.Motivo = MotivoMovimiento.Compra;
        await vm.RegistrarCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Compra_AceptaAsociar_NavegaAlFormDeGastoConElMovimiento()
    {
        var (vm, _, nav, confirm) = Crear(aceptaAsociar: true);

        await RegistrarCompraAsync(vm);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(s => s.Contains("factura"))), Times.Once);
        nav.Verify(n => n.Navegar<GastoFormViewModel>(
            It.IsAny<Action<GastoFormViewModel>>()), Times.Once);
        nav.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Never);
    }

    [Fact]
    public async Task Compra_RechazaAsociar_SigueAlHistorialComoSiempre()
    {
        var (vm, _, nav, _) = Crear(aceptaAsociar: false);

        await RegistrarCompraAsync(vm);

        nav.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
        nav.Verify(n => n.Navegar<GastoFormViewModel>(
            It.IsAny<Action<GastoFormViewModel>>()), Times.Never);
    }

    [Fact]
    public async Task Ajuste_NoOfreceFactura()
    {
        var (vm, svc, nav, confirm) = Crear(aceptaAsociar: true);
        svc.Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), It.IsAny<bool>()))
            .ReturnsAsync(Registrado() with { Motivo = MotivoMovimiento.Ajuste });
        await vm.InicializarAsync();
        vm.ProductoSeleccionado = vm.Productos[0];
        vm.Cantidad = 5m;
        vm.Motivo = MotivoMovimiento.Ajuste;

        await vm.RegistrarCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        nav.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
    }
}
