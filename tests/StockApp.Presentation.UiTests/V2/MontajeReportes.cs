using Avalonia.Controls;
using Avalonia.Threading;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views.Reportes;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Monta cada reporte v2 con su View REAL, su ViewModel real y los fakes de servicio (nada de Moq en este banco).</summary>
internal static class MontajeReportes
{
    internal sealed record Montaje<TVista, TVm>(Window Ventana, TVista Vista, TVm Vm, NavigationServiceFake Navegacion, ConfirmacionServiceFake Confirmacion)
        where TVista : Control;

    private static Montaje<TVista, TVm> Mostrar<TVista, TVm>(TVista vista, TVm vm, NavigationServiceFake nav, ConfirmacionServiceFake conf, double ancho)
        where TVista : Control
    {
        vista.DataContext = vm;
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje<TVista, TVm>(ventana, vista, vm, nav, conf);
    }

    public static Montaje<ValorizacionView, ValorizacionViewModel> Valorizacion(ReporteStockFake servicio, SesionFake? sesion = null, double ancho = 1040)
    {
        var nav = new NavigationServiceFake();
        var conf = new ConfirmacionServiceFake();
        var vm = new ValorizacionViewModel(servicio, new GuardadoSinUsoFake(), conf, new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(),
            sesion ?? new SesionFake(RolUsuario.Admin), nav);
        return Mostrar(new ValorizacionView(), vm, nav, conf, ancho);
    }

    public static Montaje<StockCategoriaView, StockCategoriaViewModel> StockCategoria(ReporteStockFake servicio, SesionFake? sesion = null, double ancho = 1040)
    {
        var nav = new NavigationServiceFake();
        var conf = new ConfirmacionServiceFake();
        var vm = new StockCategoriaViewModel(servicio, new GuardadoSinUsoFake(), conf, new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(),
            sesion ?? new SesionFake(RolUsuario.Admin), nav);
        return Mostrar(new StockCategoriaView(), vm, nav, conf, ancho);
    }

    public static Montaje<MasMovidosView, MasMovidosViewModel> MasMovidos(ReporteStockFake servicio, double ancho = 1040)
    {
        var nav = new NavigationServiceFake();
        var conf = new ConfirmacionServiceFake();
        var vm = new MasMovidosViewModel(servicio, new GuardadoSinUsoFake(), conf, new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Admin), nav);
        return Mostrar(new MasMovidosView(), vm, nav, conf, ancho);
    }

    public static Montaje<HistorialPorProductoView, HistorialPorProductoViewModel> Historial(ReporteStockFake servicio, ProductosBusquedaFake? productos = null, double ancho = 1040)
    {
        var nav = new NavigationServiceFake();
        var conf = new ConfirmacionServiceFake();
        var vm = new HistorialPorProductoViewModel(servicio, new GuardadoSinUsoFake(), conf, productos ?? new ProductosBusquedaFake(), new PdfExporterSinUsoFake(),
            new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin), nav);
        return Mostrar(new HistorialPorProductoView(), vm, nav, conf, ancho);
    }

    public static Montaje<AuditoriaLogView, AuditoriaLogViewModel> Auditoria(AuditoriaQueryFake servicio, UsuariosListaFake usuarios, double ancho = 1040, SesionFake? sesion = null)
    {
        var nav = new NavigationServiceFake();
        var conf = new ConfirmacionServiceFake();
        sesion ??= new SesionFake(RolUsuario.Admin);
        usuarios.Sesion = sesion;
        var vm = new AuditoriaLogViewModel(servicio, new GuardadoSinUsoFake(), conf, usuarios, new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(),
            sesion, nav);
        return Mostrar(new AuditoriaLogView(), vm, nav, conf, ancho);
    }

    public static Montaje<ReporteProyectosView, ReporteProyectosViewModel> Proyectos(ReporteProyectosFake servicio, double ancho = 1040)
    {
        var nav = new NavigationServiceFake();
        var conf = new ConfirmacionServiceFake();
        var vm = new ReporteProyectosViewModel(servicio, new PdfExporterSinUsoFake(), new GuardadoSinUsoFake(), new ServicioAperturaArchivoFake(), conf,
            new SesionFake(RolUsuario.Admin), nav);
        return Mostrar(new ReporteProyectosView(), vm, nav, conf, ancho);
    }
}
