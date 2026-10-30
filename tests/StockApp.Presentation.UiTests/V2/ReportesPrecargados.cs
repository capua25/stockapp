using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using StockApp.Application.Auditoria;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views.Reportes;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Reportes v2 con su ViewModel real y los datos ya cargados por el servicio (fakes), SIN ventana: la View queda lista para que el
/// guardián la monte en la suya. Los reportes muestran la grilla solo si el ViewModel tiene filas (<c>MarcoReporte.HayDatos</c> sale
/// de <c>Items.Count</c>): así entra el dato como en la app, en vez de forzar el marco. La carga corre al asignar el DataContext
/// (<c>DataContextChanged</c> de cada View) y los fakes responden al instante.
/// </summary>
internal static class ReportesPrecargados
{
    private static TView Cargada<TView>(TView vista, object vm) where TView : Control
    {
        vista.DataContext = vm;
        Dispatcher.UIThread.RunJobs();
        return vista;
    }

    public static ValorizacionView Valorizacion(IEnumerable<ValorizacionItemDto> filas)
    {
        var servicio = new ReporteStockFake { Valorizacion = filas.ToList() };
        var vm = new ValorizacionViewModel(servicio, new GuardadoSinUsoFake(), new ConfirmacionServiceFake(), new PdfExporterSinUsoFake(),
            new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin), new NavigationServiceFake());
        return Cargada(new ValorizacionView(), vm);
    }

    public static StockCategoriaView StockCategoria(IEnumerable<StockCategoriaDto> filas)
    {
        var servicio = new ReporteStockFake { Categorias = filas.ToList() };
        var vm = new StockCategoriaViewModel(servicio, new GuardadoSinUsoFake(), new ConfirmacionServiceFake(), new PdfExporterSinUsoFake(),
            new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin), new NavigationServiceFake());
        return Cargada(new StockCategoriaView(), vm);
    }

    public static MasMovidosView MasMovidos(IEnumerable<MasMovidoDto> filas)
    {
        var servicio = new ReporteStockFake { MasMovidos = filas.ToList() };
        var vm = new MasMovidosViewModel(servicio, new GuardadoSinUsoFake(), new ConfirmacionServiceFake(), new PdfExporterSinUsoFake(),
            new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin), new NavigationServiceFake());
        return Cargada(new MasMovidosView(), vm);
    }

    /// <summary>El historial es de UN producto: se deja elegido el 1 para que el VM consulte al asignarse el DataContext.</summary>
    public static HistorialPorProductoView HistorialPorProducto(IEnumerable<MovimientoHistorialDto> filas)
    {
        var servicio = new ReporteStockFake { Historial = filas.ToList() };
        var vm = new HistorialPorProductoViewModel(servicio, new GuardadoSinUsoFake(), new ConfirmacionServiceFake(), new ProductosBusquedaFake(),
            new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin), new NavigationServiceFake())
        {
            ProductoId = 1,
            ProductoSeleccionado = DatosDeReportes.Producto(1, "Producto de prueba"),
        };
        return Cargada(new HistorialPorProductoView(), vm);
    }

    public static AuditoriaLogView Auditoria(IEnumerable<AuditoriaItemDto> filas)
    {
        var sesion = new SesionFake(RolUsuario.Admin);
        var usuarios = new UsuariosListaFake { Sesion = sesion };
        var vm = new AuditoriaLogViewModel(new AuditoriaQueryFake { Registros = filas.ToList() }, new GuardadoSinUsoFake(), new ConfirmacionServiceFake(),
            usuarios, new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(), sesion, new NavigationServiceFake());
        return Cargada(new AuditoriaLogView(), vm);
    }

    public static ReporteProyectosView Proyectos(IEnumerable<FilaReporteProyectos> filas)
    {
        var vm = new ReporteProyectosViewModel(new ReporteProyectosFake { Filas = filas.ToList() }, new PdfExporterSinUsoFake(), new GuardadoSinUsoFake(),
            new ServicioAperturaArchivoFake(), new ConfirmacionServiceFake(), new SesionFake(RolUsuario.Admin), new NavigationServiceFake());
        return Cargada(new ReporteProyectosView(), vm);
    }
}
