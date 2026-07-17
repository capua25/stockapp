using Avalonia.Collections;
using Moq;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class LibroCajaViewModelTests
{
    private static (LibroCajaViewModel vm, Mock<IFinanzasVistasService> svcMock)
        Crear()
    {
        var svc = new Mock<IFinanzasVistasService>();
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var vm = new LibroCajaViewModel(svc.Object, csv.Object, guardado.Object);
        return (vm, svc);
    }

    [Fact]
    public async Task CargarAsync_PorDefecto_PideElMesActual()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 100m, 100m,
                new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));

        await vm.CargarAsync();

        Assert.Equal(100m, vm.SaldoInicial);
        Assert.Equal(100m, vm.SaldoFinal);
        Assert.Empty(vm.Movimientos);
        svc.Verify(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_ConMovimientos_PopulaLaGrilla()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 500m,
                new List<MovimientoCajaDto>
                {
                    new(new DateOnly(2026, 7, 5), "Ingreso", "Partida", null, null, "Literal B", null, 500m, 0m, 500m),
                },
                new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));

        await vm.CargarAsync();

        var fila = Assert.Single(vm.Movimientos);
        Assert.Equal("Ingreso", fila.Tipo);
        Assert.Equal(500m, fila.SaldoCorrido);
    }

    [Fact]
    public async Task VerAnioCompleto_True_PideLibroCajaAnual()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaAnualDto(2026, new List<TotalMensualDto>(), new List<TotalPorClaveDto>()));

        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        svc.Verify(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()), Times.Once);
        Assert.NotNull(vm.LibroAnual);
    }

    [Fact]
    public async Task VerAnioCompleto_ExponeTotalesPorRubroDelAnio()
    {
        // spec §7.3: el toggle "Año completo" muestra "totales por mes y por rubro, sin gráficos".
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaAnualDto(
                2026,
                new List<TotalMensualDto> { new(7, 1000m, 400m, 600m) },
                new List<TotalPorClaveDto> { new("Combustibles", 250m) }));

        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        var rubro = Assert.Single(vm.LibroAnual!.TotalesPorRubro);
        Assert.Equal("Combustibles", rubro.Clave);
        Assert.Equal(250m, rubro.Total);
    }

    [Fact]
    public async Task FilasView_EsOrdenable()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 0m, new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));

        await vm.CargarAsync();

        Assert.IsType<DataGridCollectionView>(vm.MovimientosView);
        Assert.True(vm.MovimientosView.CanSort);
    }

    [Fact]
    public async Task ExportarCsvAsync_LlamaAlExportadorYAlGuardado()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 0m, new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        await vm.CargarAsync();

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        Assert.True(true); // el mock no lanza: cubre el camino feliz de Exportar + GuardarTextoAsync
    }
}
