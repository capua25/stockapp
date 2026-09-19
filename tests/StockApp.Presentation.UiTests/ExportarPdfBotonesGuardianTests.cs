using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views.Finanzas;
using StockApp.Presentation.Views.Reportes;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Tarea 21 (agregada fuera del plan original): las Tareas 12-20 sumaron 9 botones "Exportar
/// PDF" (más "Exportar CSV" nuevo en ReporteTareas) a 9 vistas .axaml, y NINGUNO tenía un solo
/// test que lo custodiara. Antecedente ya documentado en el proyecto (ver
/// <c>ReporteTareasCriterioCierreAvisoTests</c> y el hallazgo de "un test de VM no custodia un
/// gate de UI"): un test de ViewModel prueba la propiedad/comando, no lo que el XAML hace con
/// ella. Si alguien borra el <c>&lt;Button&gt;</c> o le rompe el <c>Command="{Binding ...}"</c>,
/// los tests de ViewModel siguen 100% verdes.
///
/// Cada test monta la View REAL (mismo patrón que GastosViewTests/LibroCajaViewTests/
/// ReporteTareasCriterioCierreAvisoTests: VM real + fakes escritos a mano, sin Moq porque este
/// proyecto no lo referencia) y afirma DOS cosas, no una:
///   1. El botón existe y está visible en el árbol visual montado.
///   2. Su <c>Command</c> es, por REFERENCIA, el comando correcto del ViewModel
///      (<c>vm.ExportarPdfCommand</c> / <c>vm.ExportarCsvCommand</c>) -- no alcanza con "hay un
///      botón con ese texto": un botón sin comando bindeado (o bindeado al comando equivocado)
///      pasaría igual un test que solo mirara el texto.
///
/// Decisión de diseño: 10 <c>[AvaloniaFact]</c> separados (uno por botón) en vez de un
/// <c>[Theory]</c> parametrizado. Cada vista arma su ViewModel con una constelación distinta de
/// fakes (7, 8 o 12 parámetros según la pantalla) -- forzar eso a una fábrica genérica
/// (<c>Func&lt;(Window, ICommand)&gt;</c> por caso) hubiese escondido la construcción real detrás
/// de indirección sin ahorrar una sola línea de fake. Con métodos separados, el nombre del test
/// que se pone rojo ES el reporte de qué vista se rompió; agrupar todo en un solo archivo (como
/// ya hace <c>SignoNegativoBadgeTests.cs</c> con 8 vistas) evita la alternativa real -- 9 archivos
/// nuevos con el mismo boilerplate de usings y comentario de cabecera.
///
/// Verificado por mutación (ver task-21-report.md para las salidas completas): por cada vista,
/// se borró el &lt;Button&gt; del .axaml (o se le rompió el Command) y el test correspondiente
/// se puso rojo; se restauró y se confirmó el verde.
/// </summary>
public class ExportarPdfBotonesGuardianTests
{
    private static Button BuscarBotonExportarPorTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b =>
            (b.Content as string) == texto ||
            b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == texto));

    // ==================================================================================
    // Reportes: ValorizacionView, StockCategoriaView, MasMovidosView comparten exactamente
    // la misma forma de constructor (IReporteStockService + 6 dependencias de exportación) --
    // un solo fake de IReporteStockService les alcanza a los tres.
    // ==================================================================================

    private sealed class ReporteStockServiceFake : IReporteStockService
    {
        public Task<ValorizacionReporteDto> ObtenerValorizacionAsync()
            => Task.FromResult(new ValorizacionReporteDto(Array.Empty<ValorizacionItemDto>(), new ValorizacionTotalesDto(0m)));

        public Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
            => Task.FromResult<IReadOnlyList<StockCategoriaDto>>(Array.Empty<StockCategoriaDto>());

        public Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20)
            => Task.FromResult<IReadOnlyList<MasMovidoDto>>(Array.Empty<MasMovidoDto>());

        public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(
            int productoId, DateTime? fechaDesde, DateTime? fechaHasta)
            => Task.FromResult<IReadOnlyList<MovimientoHistorialDto>>(Array.Empty<MovimientoHistorialDto>());
    }

    private sealed class CsvExporterFake : ICsvExporter
    {
        public string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder) => "csv";
    }

    private sealed class PdfExporterNoOpFake : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos)
            => Array.Empty<byte>();
    }

    private sealed class GuardadoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(false);
        public Task<bool> GuardarBytesAsync(
            Stream contenido, string nombreSugerido, CancellationToken ct = default,
            string? extension = null, string? tipoMime = null) => Task.FromResult(false);
    }

    // ---- ValorizacionView ----

    private static (Window Window, ValorizacionViewModel Vm) MontarValorizacion()
    {
        var vm = new ValorizacionViewModel(
            new ReporteStockServiceFake(), new CsvExporterFake(), new GuardadoFake(),
            new ConfirmacionServiceFake(), new PdfExporterNoOpFake(), new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Admin));

        var vista = new ValorizacionView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_ValorizacionView_BotonExportarPdf_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarValorizacion();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar PDF");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarPdfCommand, boton.Command);
    }

    // ---- StockCategoriaView ----

    private static (Window Window, StockCategoriaViewModel Vm) MontarStockCategoria()
    {
        var vm = new StockCategoriaViewModel(
            new ReporteStockServiceFake(), new CsvExporterFake(), new GuardadoFake(),
            new ConfirmacionServiceFake(), new PdfExporterNoOpFake(), new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Admin));

        var vista = new StockCategoriaView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_StockCategoriaView_BotonExportarPdf_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarStockCategoria();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar PDF");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarPdfCommand, boton.Command);
    }

    // ---- MasMovidosView ----

    private static (Window Window, MasMovidosViewModel Vm) MontarMasMovidos()
    {
        var vm = new MasMovidosViewModel(
            new ReporteStockServiceFake(), new CsvExporterFake(), new GuardadoFake(),
            new ConfirmacionServiceFake(), new PdfExporterNoOpFake(), new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Admin));

        var vista = new MasMovidosView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_MasMovidosView_BotonExportarPdf_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarMasMovidos();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar PDF");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarPdfCommand, boton.Command);
    }

    // ---- HistorialPorProductoView (mismo IReporteStockService + IProductoService extra) ----

    private sealed class ProductoServiceFake : IProductoService
    {
        public Task<int> AltaAsync(Producto producto) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task ModificarAsync(Producto producto) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
            => Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());
        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
            => Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());
    }

    private static (Window Window, HistorialPorProductoViewModel Vm) MontarHistorialPorProducto()
    {
        var vm = new HistorialPorProductoViewModel(
            new ReporteStockServiceFake(), new CsvExporterFake(), new GuardadoFake(),
            new ConfirmacionServiceFake(), new ProductoServiceFake(), new PdfExporterNoOpFake(),
            new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin));

        var vista = new HistorialPorProductoView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_HistorialPorProductoView_BotonExportarPdf_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarHistorialPorProducto();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar PDF");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarPdfCommand, boton.Command);
    }

    // ---- AuditoriaLogView (IAuditoriaQueryService + IUsuarioService) ----

    private sealed class AuditoriaQueryServiceFake : IAuditoriaQueryService
    {
        public Task<IReadOnlyList<AuditoriaItemDto>> ObtenerLogAsync(int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta)
            => Task.FromResult<IReadOnlyList<AuditoriaItemDto>>(Array.Empty<AuditoriaItemDto>());
    }

    private sealed class UsuarioServiceFake : IUsuarioService
    {
        public Task<int> AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task BajaLogicaAsync(int usuarioId) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<IReadOnlyList<UsuarioDto>> ListarAsync()
            => Task.FromResult<IReadOnlyList<UsuarioDto>>(Array.Empty<UsuarioDto>());
        public Task<IReadOnlyList<string>> ObtenerPermisosAsync(int usuarioId) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task GuardarPermisosAsync(int usuarioId, IReadOnlyList<string> permisos) => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private static (Window Window, AuditoriaLogViewModel Vm) MontarAuditoriaLog()
    {
        var vm = new AuditoriaLogViewModel(
            new AuditoriaQueryServiceFake(), new CsvExporterFake(), new GuardadoFake(),
            new ConfirmacionServiceFake(), new UsuarioServiceFake(), new PdfExporterNoOpFake(),
            new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin));

        var vista = new AuditoriaLogView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_AuditoriaLogView_BotonExportarPdf_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarAuditoriaLog();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar PDF");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarPdfCommand, boton.Command);
    }

    // ---- ReporteTareasView (única pantalla con DOS botones nuevos: CSV y PDF) ----

    private sealed class ReporteTareasServiceFake : IReporteTareasService
    {
        public Task<ReporteTareasDto> ObtenerAsync(FiltroReporteTareas filtro)
            => Task.FromResult(new ReporteTareasDto(new List<FilaReporteTareas>(), 0));
    }

    private static (Window Window, ReporteTareasViewModel Vm) MontarReporteTareas()
    {
        var vm = new ReporteTareasViewModel(
            new ReporteTareasServiceFake(), new CsvExporterFake(), new PdfExporterNoOpFake(),
            new GuardadoFake(), new ServicioAperturaArchivoFake(), new ConfirmacionServiceFake(),
            new SesionFake(RolUsuario.Admin));

        var vista = new ReporteTareasView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_ReporteTareasView_BotonExportarCsv_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarReporteTareas();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar CSV");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarCsvCommand, boton.Command);
    }

    [AvaloniaFact]
    public void Montar_ReporteTareasView_BotonExportarPdf_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarReporteTareas();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar PDF");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarPdfCommand, boton.Command);
    }

    // ==================================================================================
    // Finanzas: GastosView, LibroCajaView, ControlPoaView -- cada una con su propia forma
    // de constructor (12, 7 y 8 parámetros respectivamente). Fakes locales, sin compartir con
    // GastosViewTests.cs/LibroCajaViewTests.cs (mismo criterio ya usado en el proyecto: cada
    // archivo trae su propia copia chica de ICsvExporter/IPdfExporter/servicio de dominio, ver
    // CsvExporterFake repetido en 5 archivos distintos de esta carpeta).
    // ==================================================================================

    // ---- GastosView ----

    private sealed class GastoServiceFake : IGastoService
    {
        public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<Gasto> ObtenerPorIdAsync(int id)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
            => Task.FromResult<IReadOnlyList<Gasto>>(Array.Empty<Gasto>());
        public Task<int> RegistrarPagoAsync(PagoGasto pago)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AnularPagoAsync(int gastoId, int pagoId)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private static (Window Window, GastosViewModel Vm) MontarGastos()
    {
        var vm = new GastosViewModel(
            new GastoServiceFake(),
            new SesionFake(RolUsuario.Admin),
            new ProveedorServiceFake(Array.Empty<Proveedor>()),
            new FuenteFinanciamientoServiceFake(Array.Empty<FuenteFinanciamiento>()),
            new RubroGastoServiceFake(Array.Empty<RubroGasto>()),
            new LineaPoaServiceFake(Array.Empty<LineaPoa>()),
            new NavigationServiceFake(),
            new ConfirmacionServiceFake(),
            new CsvExporterFake(),
            new GuardadoFake(),
            new PdfExporterNoOpFake(),
            new ServicioAperturaArchivoFake());

        var vista = new GastosView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_GastosView_BotonExportarPdf_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarGastos();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar PDF");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarPdfCommand, boton.Command);
    }

    // ---- LibroCajaView ----

    private sealed class FinanzasVistasServiceFake : IFinanzasVistasService
    {
        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)
            => Task.FromResult(new LibroCajaMesDto(
                anio, mes, 0m, 0m,
                Array.Empty<MovimientoCajaDto>(), Array.Empty<TotalPorClaveDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio)
            => Task.FromResult(new LibroCajaAnualDto(anio, Array.Empty<TotalMensualDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
            => Task.FromResult<IReadOnlyList<ControlPoaLineaDto>>(Array.Empty<ControlPoaLineaDto>());

        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private static (Window Window, LibroCajaViewModel Vm) MontarLibroCaja()
    {
        var vm = new LibroCajaViewModel(
            new FinanzasVistasServiceFake(),
            new CsvExporterFake(),
            new GuardadoFake(),
            new ConfirmacionServiceFake(),
            new PdfExporterNoOpFake(),
            new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Admin));

        var vista = new LibroCajaView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_LibroCajaView_BotonExportarPdf_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarLibroCaja();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar PDF");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarPdfCommand, boton.Command);
    }

    // ---- ControlPoaView ----

    private static (Window Window, ControlPoaViewModel Vm) MontarControlPoa()
    {
        var vm = new ControlPoaViewModel(
            new FinanzasVistasServiceFake(),
            new NavigationServiceFake(),
            new CsvExporterFake(),
            new GuardadoFake(),
            new ConfirmacionServiceFake(),
            new PdfExporterNoOpFake(),
            new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Admin));

        var vista = new ControlPoaView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_ControlPoaView_BotonExportarPdf_ExisteYEstaBindeadoAlComando()
    {
        var (window, vm) = MontarControlPoa();

        var boton = BuscarBotonExportarPorTexto(window, "Exportar PDF");

        Assert.True(boton.IsVisible);
        Assert.Same(vm.ExportarPdfCommand, boton.Command);
    }
}
