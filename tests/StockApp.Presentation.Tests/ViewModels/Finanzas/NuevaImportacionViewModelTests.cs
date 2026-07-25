using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
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
                    Mock<IServicioSeleccionArchivo> seleccion, Mock<IConfirmacionService> confirm,
                    Mock<IFuenteFinanciamientoService> fuentes, Mock<IRubroGastoService> rubros,
                    Mock<IProveedorService> proveedores)
        Crear()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        var lineasPoa = new Mock<ILineaPoaService>();
        lineasPoa.Setup(l => l.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa>());

        var vm = new NuevaImportacionViewModel(
            svc.Object, seleccion.Object, confirm.Object, fuentes.Object, rubros.Object, proveedores.Object, lineasPoa.Object);
        return (vm, svc, seleccion, confirm, fuentes, rubros, proveedores);
    }

    [Fact]
    public void EstadoInicial_PasoActualEsCargar()
    {
        var (vm, _, _, _, _, _, _) = Crear();

        Assert.Equal(PasoWizardImportacion.Cargar, vm.PasoActual);
    }

    [Fact]
    public void AnalizarCommand_SinArchivosSeleccionados_NoPuedeEjecutar()
    {
        var (vm, _, _, _, _, _, _) = Crear();

        Assert.False(vm.AnalizarCommand.CanExecute(null));
    }

    [Fact]
    public async Task SeleccionarGastosYPoa_HabilitaAnalizar()
    {
        var (vm, _, seleccion, _, _, _, _) = Crear();
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
        var (vm, svc, seleccion, _, _, _, _) = Crear();
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
        var (vm, svc, seleccion, confirm, _, _, _) = Crear();
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
        Mock<IFuenteFinanciamientoService> fuentes, Mock<IRubroGastoService> rubros, Mock<IProveedorService> proveedores,
        ResultadoAnalisisDto analisis)
    {
        seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
            .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
            .ReturnsAsync(("poa.ods", new byte[] { 2 }));
        svc.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(analisis);
        var lineasPoa = new Mock<ILineaPoaService>();
        lineasPoa.Setup(l => l.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa>());

        var vm = new NuevaImportacionViewModel(
            svc.Object, seleccion.Object, confirm.Object, fuentes.Object, rubros.Object, proveedores.Object, lineasPoa.Object);
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
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
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

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);

        var fila = Assert.Single(vm.FilasGasto);
        Assert.Equal("ACME SA", fila.Proveedor);
    }

    [Fact]
    public async Task Resumen_ConErrores_ConfirmarQuedaDeshabilitado()
    {
        // Ripple F5d Entrega 2 (Task 6): el gate ya no lee Resumen.Errores directamente — un
        // Resumen con Errores>0 sin ninguna fila real ya no bloquea (PuedeConfirmar depende de
        // HasErrors de las filas VM, ver docstring). Se reproduce acá la causa REAL de
        // EstadoFila.Error (FechaIlegible deja Fecha en null en el DTO), que el [Required] de
        // FilaGastoEditableVm.Fecha captura vía HasErrors.
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        var analisis = ResultadoAnalisisVacio() with
        {
            Gastos = new List<GastoAnalizadoDto>
            {
                new("ENERO", 3, EstadoFila.Error,
                    new List<MotivoEstado> { new(TipoMotivo.FechaIlegible, "Fecha ilegible") },
                    null, 500m, "ACME SA", false, "F-1", "O-1",
                    "Compra de insumos", null, "Literal A", false, 1, "Materiales", false, null),
            },
            Resumen = new ResumenAnalisisDto(1, 0, 0, 1, 0, 0, 0),
        };

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);

        Assert.False(vm.PuedeConfirmar);
        Assert.False(vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Resumen_SoloAdvertencias_ConfirmarQuedaHabilitado()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
        };

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);

        Assert.True(vm.PuedeConfirmar);
        Assert.True(vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Resumen_SinErroresPeroGastoSinFuente_ConfirmarQuedaDeshabilitadoYExplicaPorQue()
    {
        // Caso real (backend): una fuente vacía (LiteralVacio) es Advertencia, no Error — el gasto
        // puede quedar con Errores == 0 pero Fuente == null. Bajo el gating de Entrega 2, la fila VM
        // con Fuente == null cae en HasErrors == true por [Required] — el bloqueo ahora es directo,
        // no por un gate aparte para "errores" vs "incompletas".
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        var analisis = ResultadoAnalisisVacio() with
        {
            Gastos = new List<GastoAnalizadoDto>
            {
                new("ENERO", 3, EstadoFila.Advertencia,
                    new List<MotivoEstado> { new(TipoMotivo.LiteralVacio, "Fuente sin identificar") },
                    new DateOnly(2026, 1, 15), 500m, "ACME SA", false, "F-1", "O-1",
                    "Compra de insumos", null, null, true, 1, "Materiales", false, null),
            },
            Resumen = new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
        };

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);

        Assert.False(vm.PuedeConfirmar);
        Assert.False(vm.ConfirmarCommand.CanExecute(null));
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeConfirmarBloqueado));
    }

    [Fact]
    public async Task Resumen_SinErroresYSinCamposFaltantes_ConfirmarQuedaHabilitadoYSinMensaje()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
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

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);

        Assert.True(vm.PuedeConfirmar);
        Assert.True(vm.ConfirmarCommand.CanExecute(null));
        Assert.True(string.IsNullOrEmpty(vm.MensajeConfirmarBloqueado));
    }

    [Fact]
    public async Task ConfirmarAsync_AnalisisLimpio_MapeaGastoContadoYAvanzaAResultado()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
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
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);
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
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
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
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);
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
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
        };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ThrowsAsync(new ArgumentException("MaestrosNuevos.Rubros[0].Nombre: Requerido"));

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Revisar, vm.PasoActual);
        confirm.Verify(c => c.InformarAsync("MaestrosNuevos.Rubros[0].Nombre: Requerido"), Times.Once);
    }

    [Fact]
    public async Task ConfirmarAsync_ElServidorRechazaValidacionEstructurada_InformaMensajeGenericoYNoAvanzaDePaso()
    {
        // F5d Entrega 2 Task 11: el detalle por campo ya NO se informa como texto plano en el
        // diálogo — se descompone visualmente por fila (TieneErrorServidor/MensajeErrorServidor,
        // ver ConfirmarAsync_Error400EnGastos_MarcaLaFilaYSaltaALaPestanaDeGastos). El diálogo ahora
        // muestra un mensaje genérico fijo.
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
        };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);
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
        Assert.Equal("El servidor encontró errores de validación — revisá las celdas resaltadas.", mensajeInformado);
    }

    private static async Task<(NuevaImportacionViewModel vm, ResultadoConfirmacionDto resultado)>
        CrearEnPasoResultadoAsync(
            Mock<IImportacionService> svc, Mock<IServicioSeleccionArchivo> seleccion, Mock<IConfirmacionService> confirm,
            Mock<IFuenteFinanciamientoService> fuentes, Mock<IRubroGastoService> rubros, Mock<IProveedorService> proveedores,
            ResultadoConfirmacionDto resultado)
    {
        var analisis = ResultadoAnalisisVacio() with { Resumen = new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0) };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, analisis);
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>())).ReturnsAsync(resultado);

        await vm.ConfirmarCommand.ExecuteAsync(null);
        return (vm, resultado);
    }

    [Fact]
    public async Task Confirmar_ConConflictos_PopulaLaGrillaDeConflictos()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        var resultado = new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            new List<ConflictoGastoDto>
            {
                new("ACME SA", "F-1",
                    new List<CampoDivergenteDto> { new("MontoTotal", "500", "550") }, 0),
            });

        var (vm, _) = await CrearEnPasoResultadoAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, resultado);

        var conflicto = Assert.Single(vm.Conflictos);
        Assert.Equal("ACME SA", conflicto.Proveedor);
        Assert.Equal("MontoTotal: 500 → 550", conflicto.CamposTexto);
    }

    [Fact]
    public async Task RevertirAsync_ConfirmaYLlamaAlServicio_ReiniciaElWizard()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        var idImportacion = Guid.NewGuid();
        var resultado = new ResultadoConfirmacionDto(
            idImportacion, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        var (vm, _) = await CrearEnPasoResultadoAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, resultado);
        svc.Setup(s => s.RevertirAsync(idImportacion))
            .ReturnsAsync(new ResultadoReversionDto(idImportacion, 0, 0, 0, 0, 0));

        await vm.RevertirCommand.ExecuteAsync(null);

        svc.Verify(s => s.RevertirAsync(idImportacion), Times.Once);
        Assert.Equal(PasoWizardImportacion.Cargar, vm.PasoActual);
        Assert.Null(vm.ResultadoConfirmacion);
    }

    [Fact]
    public async Task RevertirAsync_UsuarioCancelaConfirmacion_NoLlamaAlServicio()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        var resultado = new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        var (vm, _) = await CrearEnPasoResultadoAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, resultado);

        await vm.RevertirCommand.ExecuteAsync(null);

        svc.Verify(s => s.RevertirAsync(It.IsAny<Guid>()), Times.Never);
        Assert.Equal(PasoWizardImportacion.Resultado, vm.PasoActual);
    }

    [Fact]
    public async Task NuevaImportacionCommand_DesdePasoResultado_ReiniciaElWizardSinRevertir()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        var resultado = new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            new List<ConflictoGastoDto>
            {
                new("ACME SA", "F-1",
                    new List<CampoDivergenteDto> { new("MontoTotal", "500", "550") }, 0),
            });
        var (vm, _) = await CrearEnPasoResultadoAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, resultado);
        Assert.Equal(PasoWizardImportacion.Resultado, vm.PasoActual); // precondición del test

        vm.NuevaImportacionCommand.Execute(null);

        Assert.Equal(PasoWizardImportacion.Cargar, vm.PasoActual);
        Assert.Null(vm.ResultadoConfirmacion);
        Assert.Empty(vm.Conflictos);
        Assert.Empty(vm.FilasGasto);
        svc.Verify(s => s.RevertirAsync(It.IsAny<Guid>()), Times.Never);
        confirm.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnalizarAsync_PopulaFilasGastoComoVmEditables()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var gastoDto = new GastoAnalizadoDto(
            HojaOrigen: "MARZO", NumeroFila: 1,
            Estado: EstadoFila.Advertencia, Motivos: new List<MotivoEstado>(),
            Fecha: null, Monto: 1000m,
            Proveedor: null, ProveedorNuevo: false,
            NumeroFactura: "F-1", NumeroOrden: null,
            Detalle: "Compra", Destino: null,
            Fuente: "Rentas Generales", FuenteDesconocida: false,
            CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
            LineaPoaAsignada: null);
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoDto },
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";

        await vm.AnalizarCommand.ExecuteAsync(null);

        var fila = Assert.Single(vm.FilasGasto);
        Assert.IsType<FilaGastoEditableVm>(fila);
        Assert.Equal("MARZO", fila.HojaOrigen);
        Assert.True(fila.HasErrors); // Proveedor null => [Required] falla
    }

    [Fact]
    public async Task AnalizarAsync_PopulaFilasLineaPoaAgrupadasPorHoja()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var lineaC = new LineaPoaAnalizadaDto(
            Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: true,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Literal: "C", FuenteDesconocida: false, Presupuesto: 100m, SaldoPlanilla: 100m,
            Movimientos: new List<MovimientoPoaAnalizadoDto>());
        var lineaB = lineaC with { Literal = "B", Presupuesto = 50m, SaldoPlanilla = 50m };
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
                new List<LineaPoaAnalizadaDto> { lineaC, lineaB },
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";

        await vm.AnalizarCommand.ExecuteAsync(null);

        var fila = Assert.Single(vm.FilasLineaPoa);
        Assert.Equal("COMPOSTERAS", fila.Hoja);
        Assert.Equal(2, fila.Asignaciones.Count);
    }

    [Fact]
    public async Task PuedeConfirmar_FilaConErrorDeValidacion_EsFalse()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var gastoIncompleto = new GastoAnalizadoDto(
            HojaOrigen: "MARZO", NumeroFila: 1,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
            Proveedor: null, ProveedorNuevo: false,
            NumeroFactura: null, NumeroOrden: null,
            Detalle: "Compra", Destino: null,
            Fuente: "Rentas Generales", FuenteDesconocida: false,
            CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
            LineaPoaAsignada: null);
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoIncompleto },
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";

        await vm.AnalizarCommand.ExecuteAsync(null);

        Assert.False(vm.PuedeConfirmar);
        Assert.NotNull(vm.MensajeConfirmarBloqueado);
        Assert.Contains("1", vm.MensajeConfirmarBloqueado);
    }

    [Fact]
    public async Task PuedeConfirmar_TodasLasFilasCompletas_EsTrue()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var gastoCompleto = new GastoAnalizadoDto(
            HojaOrigen: "MARZO", NumeroFila: 1,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
            Proveedor: "ACME SA", ProveedorNuevo: false,
            NumeroFactura: "F-1", NumeroOrden: null,
            Detalle: "Compra", Destino: null,
            Fuente: "Rentas Generales", FuenteDesconocida: false,
            CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
            LineaPoaAsignada: null);
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoCompleto },
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";

        await vm.AnalizarCommand.ExecuteAsync(null);

        Assert.True(vm.PuedeConfirmar);
        Assert.Null(vm.MensajeConfirmarBloqueado);
    }

    [Fact]
    public async Task EditarProveedorDeUnaFilaGasto_ConTextoQueNoMatcheaNingunProveedorExistente_LoAgregaAProveedoresNuevos()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var gastoDto = new GastoAnalizadoDto(
            HojaOrigen: "MARZO", NumeroFila: 1,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
            Proveedor: null, ProveedorNuevo: false,
            NumeroFactura: null, NumeroOrden: null,
            Detalle: "Compra", Destino: null,
            Fuente: "Rentas Generales", FuenteDesconocida: false,
            CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
            LineaPoaAsignada: null);
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoDto },
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);

        vm.FilasGasto[0].Proveedor = "Nuevo Proveedor SRL";

        Assert.Contains("Nuevo Proveedor SRL", vm.ProveedoresNuevos);
    }

    [Fact]
    public async Task EditarProveedorDeUnaFilaGasto_ConTextoQueYaExisteEnProveedoresDisponibles_NoLoAgrega()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var gastoDto = new GastoAnalizadoDto(
            HojaOrigen: "MARZO", NumeroFila: 1,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
            Proveedor: null, ProveedorNuevo: false,
            NumeroFactura: null, NumeroOrden: null,
            Detalle: "Compra", Destino: null,
            Fuente: "Rentas Generales", FuenteDesconocida: false,
            CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
            LineaPoaAsignada: null);
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoDto },
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);
        vm.ProveedoresDisponibles.Add(new Proveedor { Id = 1, Nombre = "ACME SA", Activo = true });

        vm.FilasGasto[0].Proveedor = "ACME SA";

        Assert.DoesNotContain("ACME SA", vm.ProveedoresNuevos);
    }

    [Fact]
    public async Task AnalizarAsync_PopulaRubrosNuevosComoFilasEditables()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto> { new(42, null) }),
                new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";

        await vm.AnalizarCommand.ExecuteAsync(null);

        var rubro = Assert.Single(vm.RubrosNuevos);
        Assert.Equal(42, rubro.Codigo);
        Assert.True(rubro.HasErrors); // NombreSugerido null => [Required] falla
        Assert.False(vm.PuedeConfirmar); // el gating agregado incluye RubrosNuevos
    }

    [Fact]
    public async Task ConfirmarAsync_UsuarioCorrigioCondicionYVencimiento_MapeaLosValoresDeLaFilaNoLaHeuristica()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var gastoConPoa = new GastoAnalizadoDto(
            HojaOrigen: "MARZO", NumeroFila: 1,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
            Proveedor: "ACME SA", ProveedorNuevo: false,
            NumeroFactura: "F-1", NumeroOrden: null,
            Detalle: "Compra", Destino: null,
            Fuente: "Rentas Generales", FuenteDesconocida: false,
            CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
            LineaPoaAsignada: "RAMBLA"); // heurística de Entrega 1 lo sugeriría Credito, vto=Fecha
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoConPoa },
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ReturnsAsync(new ResultadoConfirmacionDto(
                Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);

        // El usuario corrige: es Contado, no Crédito (el reconciliador se equivocó de heurística).
        vm.FilasGasto[0].Condicion = CondicionPago.Contado;
        vm.FilasGasto[0].FechaVencimiento = null;

        await vm.ConfirmarCommand.ExecuteAsync(null);

        service.Verify(s => s.ConfirmarAsync(It.Is<ConfirmarImportacionDto>(dto =>
            dto.Gastos[0].Condicion == CondicionPago.Contado && dto.Gastos[0].FechaVencimiento == null)));
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaNueva_MandaNombreProgramaYAsignacionesAgrupadas()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var lineaC = new LineaPoaAnalizadaDto(
            Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: true,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Literal: "C", FuenteDesconocida: false, Presupuesto: 1000m, SaldoPlanilla: 1000m,
            Movimientos: new List<MovimientoPoaAnalizadoDto>());
        var lineaB = lineaC with { Literal = "B", Presupuesto = 500m, SaldoPlanilla = 500m };
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
                new List<LineaPoaAnalizadaDto> { lineaC, lineaB },
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ReturnsAsync(new ResultadoConfirmacionDto(
                Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);
        vm.FilasLineaPoa[0].Programa = "Obras públicas";

        await vm.ConfirmarCommand.ExecuteAsync(null);

        service.Verify(s => s.ConfirmarAsync(It.Is<ConfirmarImportacionDto>(dto =>
            dto.LineasPoa.Count == 1
            && dto.LineasPoa[0].Nombre == "COMPOSTERAS"
            && dto.LineasPoa[0].Programa == "Obras públicas"
            && dto.LineasPoa[0].Asignaciones.Count == 2)));
    }

    [Fact]
    public async Task ConfirmarAsync_RubroNuevoConNombreCompletado_LoMandaEnMaestrosNuevos()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto> { new(42, null) }),
                new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ReturnsAsync(new ResultadoConfirmacionDto(
                Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);
        vm.RubrosNuevos[0].Nombre = "Materiales de obra";

        await vm.ConfirmarCommand.ExecuteAsync(null);

        service.Verify(s => s.ConfirmarAsync(It.Is<ConfirmarImportacionDto>(dto =>
            dto.MaestrosNuevos.Rubros.Count == 1
            && dto.MaestrosNuevos.Rubros[0].Codigo == 42
            && dto.MaestrosNuevos.Rubros[0].Nombre == "Materiales de obra")));
    }

    [Fact]
    public async Task ConfirmarAsync_Error400EnGastos_MarcaLaFilaYSaltaALaPestanaDeGastos()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var gasto = new GastoAnalizadoDto(
            HojaOrigen: "MARZO", NumeroFila: 1,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
            Proveedor: "ACME SA", ProveedorNuevo: false,
            NumeroFactura: "F-1", NumeroOrden: null,
            Detalle: "Compra", Destino: null,
            Fuente: "Rentas Generales", FuenteDesconocida: false,
            CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
            LineaPoaAsignada: null);
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gasto },
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ThrowsAsync(new ValidacionImportacionException(new Dictionary<string, string[]>
            {
                ["Gastos[0].Fuente"] = new[] { "La fuente no existe en el catálogo." },
            }));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.True(vm.FilasGasto[0].TieneErrorServidor);
        Assert.Contains("Fuente", vm.FilasGasto[0].MensajeErrorServidor);
        Assert.Equal(0, vm.PestanaSeleccionada);
    }

    [Fact]
    public async Task ConfirmarAsync_Error400EnLineasPoa_SaltaALaPestanaDeLineasPoa()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var linea = new LineaPoaAnalizadaDto(
            Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: true,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Literal: "C", FuenteDesconocida: false, Presupuesto: 1000m, SaldoPlanilla: 1000m,
            Movimientos: new List<MovimientoPoaAnalizadoDto>());
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
                new List<LineaPoaAnalizadaDto> { linea },
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ThrowsAsync(new ValidacionImportacionException(new Dictionary<string, string[]>
            {
                ["LineasPoa[0].Programa"] = new[] { "El programa es obligatorio." },
            }));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);
        vm.FilasLineaPoa[0].Programa = "Obras"; // pasa la validación CLIENTE, el 400 simula un error de SERVIDOR (p.ej. carrera con otro import)

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.True(vm.FilasLineaPoa[0].TieneErrorServidor);
        Assert.Equal(2, vm.PestanaSeleccionada);
    }

    [Fact]
    public async Task ConfirmarAsync_ReintentoDespuesDeCorregir_LimpiaElErrorDeServidorAnterior()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var gasto = new GastoAnalizadoDto(
            HojaOrigen: "MARZO", NumeroFila: 1,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
            Proveedor: "ACME SA", ProveedorNuevo: false,
            NumeroFactura: "F-1", NumeroOrden: null,
            Detalle: "Compra", Destino: null,
            Fuente: "Rentas Generales", FuenteDesconocida: false,
            CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
            LineaPoaAsignada: null);
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gasto },
                new List<LineaPoaAnalizadaDto>(),
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        service.SetupSequence(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ThrowsAsync(new ValidacionImportacionException(new Dictionary<string, string[]>
            {
                ["Gastos[0].Fuente"] = new[] { "La fuente no existe en el catálogo." },
            }))
            .ReturnsAsync(new ResultadoConfirmacionDto(
                Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);
        await vm.ConfirmarCommand.ExecuteAsync(null);
        Assert.True(vm.FilasGasto[0].TieneErrorServidor);

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.False(vm.FilasGasto[0].TieneErrorServidor);
    }

    /// <summary>
    /// El orden de enumeración de IReadOnlyDictionary (Dictionary por debajo) NO está garantizado por
    /// .NET — no se puede usar "la primera clave del diccionario" para decidir la pestaña. Este test
    /// inserta la clave de LineasPoa ANTES que la de Gastos en el diccionario literal (a propósito,
    /// para que una implementación ingenua basada en orden de enumeración falle) y verifica que la
    /// pestaña resultante es igual siempre: Gastos (orden fijo Gastos→Ingresos→LineasPoa, índice menor
    /// dentro del mismo tipo), sin importar el orden de inserción.
    /// </summary>
    [Fact]
    public async Task ConfirmarAsync_Error400ConClavesDeVariasPestanas_SaltaALaPestanaDeMenorOrden()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var gasto = new GastoAnalizadoDto(
            HojaOrigen: "MARZO", NumeroFila: 1,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
            Proveedor: "ACME SA", ProveedorNuevo: false,
            NumeroFactura: "F-1", NumeroOrden: null,
            Detalle: "Compra", Destino: null,
            Fuente: "Rentas Generales", FuenteDesconocida: false,
            CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
            LineaPoaAsignada: null);
        var linea = new LineaPoaAnalizadaDto(
            Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: true,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Literal: "C", FuenteDesconocida: false, Presupuesto: 1000m, SaldoPlanilla: 1000m,
            Movimientos: new List<MovimientoPoaAnalizadoDto>());
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gasto },
                new List<LineaPoaAnalizadaDto> { linea },
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ThrowsAsync(new ValidacionImportacionException(new Dictionary<string, string[]>
            {
                // A propósito en este orden: LineasPoa insertada ANTES que Gastos.
                ["LineasPoa[0].Programa"] = new[] { "El programa es obligatorio." },
                ["Gastos[0].Fuente"] = new[] { "La fuente no existe en el catálogo." },
            }));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);
        vm.FilasLineaPoa[0].Programa = "Obras"; // pasa la validación cliente

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.True(vm.FilasGasto[0].TieneErrorServidor);
        Assert.True(vm.FilasLineaPoa[0].TieneErrorServidor);
        Assert.Equal(0, vm.PestanaSeleccionada); // Gastos (orden fijo), NO LineasPoa (que apareció primero en el diccionario)
    }

    /// <summary>
    /// Review Task 11 (Minor): el cálculo de PestanaSeleccionada debe aplicar el MISMO bounds-check
    /// (indice &lt; FilasX.Count) que ya aplica el marcado de filas — si no, un desync cliente/servidor
    /// (clave con índice fuera de rango) puede saltar a una pestaña donde NINGUNA fila quedó marcada,
    /// aunque otra pestaña sí tenga una fila real marcada.
    /// </summary>
    [Fact]
    public async Task ConfirmarAsync_Error400ConIndiceFueraDeRango_IgnoraEsaClaveYSaltaALaPestanaConFilaRealmenteMarcada()
    {
        var (vm, service, _, _, _, _, _) = Crear();
        var linea = new LineaPoaAnalizadaDto(
            Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: true,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Literal: "C", FuenteDesconocida: false, Presupuesto: 1000m, SaldoPlanilla: 1000m,
            Movimientos: new List<MovimientoPoaAnalizadoDto>());
        service.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new ResultadoAnalisisDto(
                new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
                new List<LineaPoaAnalizadaDto> { linea },
                new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
                new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
                new SaldosTotalesPoaOds(0m, 0m)));
        service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ThrowsAsync(new ValidacionImportacionException(new Dictionary<string, string[]>
            {
                // FilasGasto está vacía: índice 99 queda fuera de rango, no marca ninguna fila.
                ["Gastos[99].Fuente"] = new[] { "La fuente no existe en el catálogo." },
                ["LineasPoa[0].Programa"] = new[] { "El programa es obligatorio." },
            }));
        vm.GastosNombreArchivo = "gastos.ods";
        vm.PoaNombreArchivo = "poa.ods";
        await vm.AnalizarCommand.ExecuteAsync(null);
        vm.FilasLineaPoa[0].Programa = "Obras"; // pasa la validación cliente

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.True(vm.FilasLineaPoa[0].TieneErrorServidor);
        Assert.Equal(2, vm.PestanaSeleccionada); // LineasPoa, la única pestaña con una fila realmente marcada
    }
}
