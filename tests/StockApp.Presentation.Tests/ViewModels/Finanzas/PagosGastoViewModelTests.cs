using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class PagosGastoViewModelTests
{
    private static readonly DateTime Hoy = DateTime.UtcNow;

    private static Gasto GastoConPago() => new()
    {
        Id = 5, ProveedorId = 1, Detalle = "Materiales", Fecha = Hoy, MontoTotal = 1000m,
        FuenteFinanciamientoId = 2, RubroGastoId = 3,
        CondicionPago = CondicionPago.Credito, FechaVencimiento = Hoy.AddDays(30),
        Pagos = { new PagoGasto { Id = 21, GastoId = 5, Fecha = Hoy, Monto = 400m } },
    };

    private static (PagosGastoViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear()
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ObtenerPorIdAsync(5)).ReturnsAsync(GastoConPago());
        svc.Setup(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>())).ReturnsAsync(22);

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new PagosGastoViewModel(svc.Object, nav.Object, confirm.Object);
        return (vm, svc, nav, confirm);
    }

    [Fact]
    public async Task Inicializar_MuestraPagosYSaldo()
    {
        var (vm, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());

        await vm.InicializarAsync();

        Assert.Single(vm.Pagos);
        Assert.Equal(600m, vm.SaldoPendiente);
        Assert.Contains("Materiales", vm.TituloGasto);
    }

    [Fact]
    public async Task RegistrarPago_ParseaEsUY_RegistraYRefresca()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "600,00";
        vm.Nota = "saldo final";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        svc.Verify(s => s.RegistrarPagoAsync(It.Is<PagoGasto>(p =>
            p.GastoId == 5 && p.Monto == 600m && p.Nota == "saldo final")), Times.Once);
        svc.Verify(s => s.ObtenerPorIdAsync(5), Times.AtLeastOnce);  // refresco post-pago
    }

    [Fact]
    public async Task RegistrarPago_MontoIlegible_MuestraError()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "no-es-numero";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        svc.Verify(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarPago_ReglaDeNegocio_MuestraMensaje()
    {
        var (vm, svc, _, _) = Crear();
        svc.Setup(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>()))
            .ThrowsAsync(new ReglaDeNegocioException("El pago supera el saldo pendiente."));
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "9999";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.Equal("El pago supera el saldo pendiente.", vm.MensajeError);
    }

    [Fact]
    public async Task AnularPago_ConConfirmacion_AnulaYRefresca()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        await vm.AnularPagoCommand.ExecuteAsync(vm.Pagos[0]);

        svc.Verify(s => s.AnularPagoAsync(5, 21), Times.Once);
        svc.Verify(s => s.ObtenerPorIdAsync(5), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Volver_NavegaALaGrilla()
    {
        var (vm, _, nav, _) = Crear();
        vm.CargarParaGasto(GastoConPago());

        vm.VolverCommand.Execute(null);

        nav.Verify(n => n.Navegar<GastosViewModel>(), Times.Once);
        await Task.CompletedTask;
    }
}
