using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Documentos;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Documentos;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Gestion;

/// <summary>Listado de Documentos dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos
/// renders. La selección de las filas la pone la comparación (la carga que dispara la vista reemplaza las filas).</summary>
[Trait("Categoria", "ComparacionVisual")]
public class DocumentosComparacionTests
{
    // Descripciones que no entran en la columna *: el mockup también las corta (v2-celda-cortada) y el píxel coincide.
    private static readonly string[] CortesDelMockup =
    [
        "Respuesta a la Junta Departamental por el alumbrado de la costanera",
        "Convenio de uso del predio del club, reemplazado por el de marzo",
    ];

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null,
        string[]? recortesPermitidos = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar,
            recortesPermitidos: recortesPermitidos);

    // Aprobación puntual de Martin (2026-10-17), no sube el umbral global: antialias de Chromium contra Skia en texto de cuerpo
    // y en negrita. Verificado en cada pantalla: ninguna tesela fuera del texto, ninguna línea con desvío mayor a 1 px, línea base
    // igual y sin deriva entre líneas, sin teselas desplazadas salvo el texto de "Volver" (1 px: el texto de Skia es más angosto).
    // Tope = medido.
    private static UmbralAprobado AprobacionAntialias(int teselas, string? mutacionPalida = null) => new(
        teselas, "Martin", "2026-10-17",
        "Antialias Chromium vs Skia en títulos en negrita; layout alineado (cajas de texto a 1 px, misma línea base), teselas solo sobre el texto. Tope = medido.",
        mutacionPalida);



    private static Action<Control> SeleccionandoActivo(int id) => vista =>
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var vm = (DocumentoListViewModel)((ShellMainViewModel)vista.DataContext!).CurrentContent!;
        vm.FilaSeleccionadaActivos = vm.Activos.Single(f => f.Id == id);
        Dispatcher.UIThread.RunJobs();
    };

    private static Action<Control> SeleccionandoHistorial(int id) => vista =>
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var vm = (DocumentoListViewModel)((ShellMainViewModel)vista.DataContext!).CurrentContent!;
        vm.FilaSeleccionadaHistorial = vm.Historial.Single(f => f.Id == id);
        Dispatcher.UIThread.RunJobs();
    };

    [AvaloniaFact] public void Documentos_IgualAlMockup() =>
        Comparar(DatosDocumentos.Listado(), "gestion/documentos.html", preparar: SeleccionandoActivo(9), recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void Documentos_IgualAlMockup_1366x768() =>
        Comparar(DatosDocumentos.Listado(), "gestion/documentos.html", TamanoLienzo.Municipio, SeleccionandoActivo(9), CortesDelMockup);
    [AvaloniaFact] public void DocumentosHistorial_IgualAlMockup() =>
        Comparar(DatosDocumentos.ListadoHistorial(), "gestion/documentos-historial.html", preparar: SeleccionandoHistorial(6), recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void DocumentosHistorial_IgualAlMockup_1366x768() =>
        Comparar(DatosDocumentos.ListadoHistorial(), "gestion/documentos-historial.html", TamanoLienzo.Municipio, SeleccionandoHistorial(6), CortesDelMockup);
    [AvaloniaFact] public void DocumentosOperador_IgualAlMockup() =>
        Comparar(DatosDocumentos.ListadoOperador(), "gestion/documentos-operador.html", preparar: SeleccionandoActivo(9), recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void DocumentosOperador_IgualAlMockup_1366x768() =>
        Comparar(DatosDocumentos.ListadoOperador(), "gestion/documentos-operador.html", TamanoLienzo.Municipio, SeleccionandoActivo(9), CortesDelMockup);
    [AvaloniaFact] public void DocumentosCargando_IgualAlMockup() => Comparar(DatosDocumentos.Cargando(), "gestion/documentos-cargando.html");
    [AvaloniaFact] public void DocumentosCargando_IgualAlMockup_1366x768() => Comparar(DatosDocumentos.Cargando(), "gestion/documentos-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void DocumentosSinConexion_IgualAlMockup() => Comparar(DatosDocumentos.SinConexion(), "gestion/documentos-sin-conexion.html");
    [AvaloniaFact] public void DocumentosSinConexion_IgualAlMockup_1366x768() => Comparar(DatosDocumentos.SinConexion(), "gestion/documentos-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void DocumentosSinPermiso_IgualAlMockup() => Comparar(DatosDocumentos.SinPermiso(), "gestion/documentos-sin-permiso.html");
    [AvaloniaFact] public void DocumentosSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosDocumentos.SinPermiso(), "gestion/documentos-sin-permiso.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void DocumentosVacio_IgualAlMockup() => Comparar(DatosDocumentos.SinDocumentos(), "gestion/documentos-vacio.html");
    [AvaloniaFact] public void DocumentosVacio_IgualAlMockup_1366x768() => Comparar(DatosDocumentos.SinDocumentos(), "gestion/documentos-vacio.html", TamanoLienzo.Municipio, aprobado: AprobacionAntialias(12));
    [AvaloniaFact] public void DocumentosSinResultados_IgualAlMockup() => Comparar(DatosDocumentos.SinResultados(), "gestion/documentos-sin-resultados.html", aprobado: AprobacionAntialias(13));
    [AvaloniaFact] public void DocumentosSinResultados_IgualAlMockup_1366x768() => Comparar(DatosDocumentos.SinResultados(), "gestion/documentos-sin-resultados.html", TamanoLienzo.Municipio);
}
