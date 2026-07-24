using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
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

    private static async Task<NuevaImportacionViewModel> CrearEnPasoRevisarAsync(
        Mock<IImportacionService> svc, Mock<IServicioSeleccionArchivo> seleccion, Mock<IConfirmacionService> confirm,
        ResultadoAnalisisDto analisis)
    {
        seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
            .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
            .ReturnsAsync(("poa.ods", new byte[] { 2 }));
        svc.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(analisis);

        var vm = new NuevaImportacionViewModel(svc.Object, seleccion.Object, confirm.Object);
        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);
        await vm.AnalizarCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Analizar_PopulaLasGrillasDelPaso2()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Gastos = new List<GastoAnalizadoDto>
            {
                new("ENERO", 3, EstadoFila.Ok, new List<MotivoEstado>(),
                    new DateOnly(2026, 1, 15), 500m, "ACME SA", false, "F-1", "O-1",
                    "Compra de insumos", null, "Literal A", false, 1, "Materiales", false, null),
            },
            Resumen = new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
        };

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);

        Assert.Single(vm.GastosAnalizados);
        Assert.Equal("ACME SA", vm.GastosAnalizados[0].Proveedor);
    }

    [Fact]
    public async Task Resumen_ConErrores_ConfirmarQuedaDeshabilitado()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(1, 0, 0, 1, 0, 0, 0),
        };

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);

        Assert.False(vm.PuedeConfirmar);
        Assert.False(vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Resumen_SoloAdvertencias_ConfirmarQuedaHabilitado()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
        };

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);

        Assert.True(vm.PuedeConfirmar);
        Assert.True(vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmarAsync_AnalisisLimpio_MapeaGastoContadoYAvanzaAResultado()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Gastos = new List<GastoAnalizadoDto>
            {
                new("ENERO", 3, EstadoFila.Ok, new List<MotivoEstado>(),
                    new DateOnly(2026, 1, 15), 500m, "ACME SA", false, "F-1", "O-1",
                    "Compra de insumos", null, "Literal A", false, 1, "Materiales", false, null),
            },
            Resumen = new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
        };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);
        var resultadoConfirmacion = new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        ConfirmarImportacionDto? dtoCapturado = null;
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .Callback<ConfirmarImportacionDto>(dto => dtoCapturado = dto)
            .ReturnsAsync(resultadoConfirmacion);

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Resultado, vm.PasoActual);
        Assert.NotNull(dtoCapturado);
        var gasto = Assert.Single(dtoCapturado!.Gastos);
        Assert.Equal(CondicionPago.Contado, gasto.Condicion);
        Assert.Null(gasto.FechaVencimiento);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConLineaPoaAsignada_MapeaCredito()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Gastos = new List<GastoAnalizadoDto>
            {
                new("ENERO", 3, EstadoFila.Ok, new List<MotivoEstado>(),
                    new DateOnly(2026, 1, 15), 500m, "ACME SA", false, "F-1", "O-1",
                    "Compromiso POA", null, "Literal A", false, 1, "Materiales", false, "COMPOSTERAS"),
            },
            Resumen = new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
        };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);
        ConfirmarImportacionDto? dtoCapturado = null;
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .Callback<ConfirmarImportacionDto>(dto => dtoCapturado = dto)
            .ReturnsAsync(new ResultadoConfirmacionDto(
                Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));

        await vm.ConfirmarCommand.ExecuteAsync(null);

        var gasto = Assert.Single(dtoCapturado!.Gastos);
        Assert.Equal(CondicionPago.Credito, gasto.Condicion);
        Assert.Equal(new DateOnly(2026, 1, 15), gasto.FechaVencimiento);
        Assert.Empty(dtoCapturado.LineasPoa); // gap documentado: Entrega 1 nunca declara LineaPoa nueva
    }

    [Fact]
    public async Task ConfirmarAsync_ElServidorRechaza400_InformaYNoAvanzaDePaso()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
        };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ThrowsAsync(new ArgumentException("MaestrosNuevos.Rubros[0].Nombre: Requerido"));

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Revisar, vm.PasoActual);
        confirm.Verify(c => c.InformarAsync("MaestrosNuevos.Rubros[0].Nombre: Requerido"), Times.Once);
    }

    [Fact]
    public async Task ConfirmarAsync_ElServidorRechazaValidacionEstructurada_InformaElDetallePorCampo()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
        };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);
        var errores = new Dictionary<string, string[]>
        {
            ["Gastos[3].Fuente"] = new[] { "La fuente 'X' no existe ni fue declarada nueva" },
            ["Gastos[3].FechaVencimiento"] = new[] { "Requerido" },
        };
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ThrowsAsync(new ValidacionImportacionException(errores));
        string? mensajeInformado = null;
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>()))
            .Callback<string>(m => mensajeInformado = m)
            .Returns(Task.CompletedTask);

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Revisar, vm.PasoActual);
        Assert.NotNull(mensajeInformado);
        Assert.Contains("Gastos[3].Fuente", mensajeInformado);
        Assert.Contains("La fuente 'X' no existe ni fue declarada nueva", mensajeInformado);
        Assert.Contains("Gastos[3].FechaVencimiento", mensajeInformado);
        Assert.Contains("Requerido", mensajeInformado);
    }
}
