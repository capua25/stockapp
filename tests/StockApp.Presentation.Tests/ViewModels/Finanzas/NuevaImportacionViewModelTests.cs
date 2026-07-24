using Moq;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class NuevaImportacionViewModelTests
{
    private static ResultadoAnalisisDto ResultadoAnalisisVacio() => new(
        Ingresos: new List<IngresoAnalizadoDto>(),
        Gastos: new List<GastoAnalizadoDto>(),
        LineasPoa: new List<LineaPoaAnalizadaDto>(),
        MaestrosNuevos: new MaestrosNuevosDto(
            new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
        Resumen: new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
        SaldosPoa: new SaldosTotalesPoaOds(0m, 0m));

    private static (NuevaImportacionViewModel vm, Mock<IImportacionService> svc,
                    Mock<IServicioSeleccionArchivo> seleccion, Mock<IConfirmacionService> confirm)
        Crear()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new NuevaImportacionViewModel(svc.Object, seleccion.Object, confirm.Object);
        return (vm, svc, seleccion, confirm);
    }

    [Fact]
    public void EstadoInicial_PasoActualEsCargar()
    {
        var (vm, _, _, _) = Crear();

        Assert.Equal(PasoWizardImportacion.Cargar, vm.PasoActual);
    }

    [Fact]
    public void AnalizarCommand_SinArchivosSeleccionados_NoPuedeEjecutar()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.AnalizarCommand.CanExecute(null));
    }

    [Fact]
    public async Task SeleccionarGastosYPoa_HabilitaAnalizar()
    {
        var (vm, _, seleccion, _) = Crear();
        seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
            .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
            .ReturnsAsync(("poa.ods", new byte[] { 2 }));

        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);

        Assert.True(vm.AnalizarCommand.CanExecute(null));
        Assert.Equal("gastos.ods", vm.GastosNombreArchivo);
        Assert.Equal("poa.ods", vm.PoaNombreArchivo);
    }

    [Fact]
    public async Task AnalizarAsync_ConExito_AvanzaAPasoRevisar()
    {
        var (vm, svc, seleccion, _) = Crear();
        seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
            .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
            .ReturnsAsync(("poa.ods", new byte[] { 2 }));
        svc.Setup(s => s.AnalizarAsync(
                "gastos.ods", It.IsAny<byte[]>(), "poa.ods", It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(ResultadoAnalisisVacio());
        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);

        await vm.AnalizarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Revisar, vm.PasoActual);
    }

    [Fact]
    public async Task AnalizarAsync_ElServidorFalla_InformaYNoAvanzaDePaso()
    {
        var (vm, svc, seleccion, confirm) = Crear();
        seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
            .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
            .ReturnsAsync(("poa.ods", new byte[] { 2 }));
        svc.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ThrowsAsync(new ArgumentException("El archivo no es un .ods válido."));
        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);

        await vm.AnalizarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Cargar, vm.PasoActual);
        confirm.Verify(c => c.InformarAsync("El archivo no es un .ods válido."), Times.Once);
    }
}
