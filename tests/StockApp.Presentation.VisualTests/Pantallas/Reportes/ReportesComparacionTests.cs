using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Reportes;

/// <summary>El hub y los seis reportes dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos
/// renders (el sidebar lo compara ShellComparacionTests). 15 pantallas a 1280x760 y 1366x768.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class ReportesComparacionTests
{
    // Textos que el mockup también corta (elipsis y tooltip): los comentarios del historial, que cede la columna *, y el detalle largo
    // de la auditoría. Aprobado en el GATE 1.
    private static readonly string[] ComentariosCortados =
    [
        "Remito 0042-00001234", "Diferencia de conteo del inventario de setiembre.", "Reposición de emergencia de la escuela 12",
        "Cordón cuneta del barrio sur", "Bolsas rotas por humedad", "Remito 0042-00001190", "Bacheo de la calle 18 de Julio",
        "Obra de la plazoleta", "Remito 0042-00001101",
    ];

    private static readonly string[] DetalleCortado = [DatosReportes.DetalleLargo];

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, string[]? recortesPermitidos = null,
        int teselasDensasMax = UmbralComparacion.TeselasDensasPorDefecto) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, teselasDensasMax: teselasDensasMax,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar, recortesPermitidos: recortesPermitidos);

    [AvaloniaFact] public void Hub_IgualAlMockup() => Comparar(DatosReportes.HubAdmin(), "reportes/hub.html");
    [AvaloniaFact] public void Hub_IgualAlMockup_1366x768() => Comparar(DatosReportes.HubAdmin(), "reportes/hub.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void HubOperador_IgualAlMockup() => Comparar(DatosReportes.HubOperador(), "reportes/hub-operador.html");
    [AvaloniaFact] public void HubOperador_IgualAlMockup_1366x768() => Comparar(DatosReportes.HubOperador(), "reportes/hub-operador.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void Valorizacion_IgualAlMockup() => Comparar(DatosReportes.Valorizacion(), "reportes/valorizacion.html");
    [AvaloniaFact] public void Valorizacion_IgualAlMockup_1366x768() => Comparar(DatosReportes.Valorizacion(), "reportes/valorizacion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ValorizacionCargando_IgualAlMockup() => Comparar(DatosReportes.ValorizacionCargando(), "reportes/valorizacion-cargando.html");
    [AvaloniaFact] public void ValorizacionCargando_IgualAlMockup_1366x768() => Comparar(DatosReportes.ValorizacionCargando(), "reportes/valorizacion-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ValorizacionSinConexion_IgualAlMockup() => Comparar(DatosReportes.ValorizacionSinConexion(), "reportes/valorizacion-sin-conexion.html");
    [AvaloniaFact] public void ValorizacionSinConexion_IgualAlMockup_1366x768() => Comparar(DatosReportes.ValorizacionSinConexion(), "reportes/valorizacion-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ValorizacionSinPermiso_IgualAlMockup() => Comparar(DatosReportes.ValorizacionSinPermiso(), "reportes/valorizacion-sin-permiso.html");
    [AvaloniaFact] public void ValorizacionSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosReportes.ValorizacionSinPermiso(), "reportes/valorizacion-sin-permiso.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ValorizacionVacio_IgualAlMockup() => Comparar(DatosReportes.ValorizacionVacia(), "reportes/valorizacion-vacio.html");
    [AvaloniaFact] public void ValorizacionVacio_IgualAlMockup_1366x768() => Comparar(DatosReportes.ValorizacionVacia(), "reportes/valorizacion-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void StockCategoria_IgualAlMockup() => Comparar(DatosReportes.StockCategoria(), "reportes/stock-categoria.html");
    [AvaloniaFact] public void StockCategoria_IgualAlMockup_1366x768() => Comparar(DatosReportes.StockCategoria(), "reportes/stock-categoria.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void HistorialProducto_IgualAlMockup() => Comparar(DatosReportes.HistorialProducto(), "reportes/historial-producto.html", recortesPermitidos: ComentariosCortados);
    [AvaloniaFact] public void HistorialProducto_IgualAlMockup_1366x768() => Comparar(DatosReportes.HistorialProducto(), "reportes/historial-producto.html", TamanoLienzo.Municipio, ComentariosCortados);
    [AvaloniaFact] public void HistorialSinProducto_IgualAlMockup() => Comparar(DatosReportes.HistorialSinProducto(), "reportes/historial-producto-sin-producto.html");
    [AvaloniaFact] public void HistorialSinProducto_IgualAlMockup_1366x768() => Comparar(DatosReportes.HistorialSinProducto(), "reportes/historial-producto-sin-producto.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void MasMovidos_IgualAlMockup() => Comparar(DatosReportes.MasMovidos(), "reportes/mas-movidos.html");
    [AvaloniaFact] public void MasMovidos_IgualAlMockup_1366x768() => Comparar(DatosReportes.MasMovidos(), "reportes/mas-movidos.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void MasMovidosErrorFechas_IgualAlMockup() => Comparar(DatosReportes.MasMovidosErrorFechas(), "reportes/mas-movidos-error-fechas.html");
    [AvaloniaFact] public void MasMovidosErrorFechas_IgualAlMockup_1366x768() => Comparar(DatosReportes.MasMovidosErrorFechas(), "reportes/mas-movidos-error-fechas.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void Auditoria_IgualAlMockup() => Comparar(DatosReportes.Auditoria(), "reportes/auditoria.html", recortesPermitidos: DetalleCortado);
    [AvaloniaFact] public void Auditoria_IgualAlMockup_1366x768() => Comparar(DatosReportes.Auditoria(), "reportes/auditoria.html", TamanoLienzo.Municipio, DetalleCortado);
    [AvaloniaFact] public void Proyectos_IgualAlMockup() => Comparar(DatosReportes.Proyectos(), "reportes/proyectos.html");
    [AvaloniaFact] public void Proyectos_IgualAlMockup_1366x768() => Comparar(DatosReportes.Proyectos(), "reportes/proyectos.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ProyectosCierre_IgualAlMockup() => Comparar(DatosReportes.ProyectosCierre(), "reportes/proyectos-cierre.html");
    [AvaloniaFact] public void ProyectosCierre_IgualAlMockup_1366x768() => Comparar(DatosReportes.ProyectosCierre(), "reportes/proyectos-cierre.html", TamanoLienzo.Municipio);
}
