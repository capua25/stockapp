using Avalonia.Collections;
using Moq;
using System.IO;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class ControlPoaViewModelTests
{
    private static (
        ControlPoaViewModel vm,
        Mock<IFinanzasVistasService> svcMock,
        Mock<INavigationService> navMock,
        Mock<ICsvExporter> csvMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<ControlPoaLineaDto>? lineas = null)
    {
        var svc = new Mock<IFinanzasVistasService>();
        svc.Setup(s => s.ObtenerControlPoaAsync(It.IsAny<int>())).ReturnsAsync(lineas ?? new List<ControlPoaLineaDto>());
        var nav = new Mock<INavigationService>();
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new ControlPoaViewModel(svc.Object, nav.Object, csv.Object, guardado.Object, confirm.Object);
        return (vm, svc, nav, csv, guardado, confirm);
    }

    [Fact]
    public async Task CargarAsync_PopulaLasFilas()
    {
        var (vm, _, _, _, _, _) = Crear(new List<ControlPoaLineaDto>
        {
            new(1, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false),
            new(2, "Prensa", "Comunicación", 2026, 1000m, 8915m, -7915m, 891.5m, true),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Filas.Count);
        Assert.True(vm.Filas[1].Sobregirada);
    }

    [Fact]
    public async Task FilasView_EsOrdenable()
    {
        var (vm, _, _, _, _, _) = Crear();

        await vm.CargarAsync();

        Assert.IsType<DataGridCollectionView>(vm.FilasView);
        Assert.True(vm.FilasView.CanSort);
    }

    [Fact]
    public async Task AbrirGastosDeLaLinea_NavegaAGastosViewModelFiltrado()
    {
        var (vm, _, nav, _, _, _) = Crear(new List<ControlPoaLineaDto>
        {
            new(1, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false),
        });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        vm.AbrirGastosDeLaLineaCommand.Execute(null);

        nav.Verify(n => n.Navegar(It.IsAny<Action<GastosViewModel>>()), Times.Once);
    }

    // ── bugfix 2026-08-14: falla silenciosa al guardar el CSV ──────────────────

    [Fact]
    public async Task ExportarCsvCommand_SiFallaGuardarTextoAsync_InformaYNoPropagaLaExcepcion()
    {
        var (vm, _, _, _, guardado, confirm) = Crear(new List<ControlPoaLineaDto>
        {
            new(1, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false),
        });
        guardado
            .Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));
        await vm.CargarAsync();

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }
}
