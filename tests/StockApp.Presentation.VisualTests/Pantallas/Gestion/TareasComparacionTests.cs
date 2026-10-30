using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Tareas;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Gestion;

/// <summary>Listado de Tareas dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos
/// renders. La selección de las filas la pone la comparación (la carga que dispara la vista reemplaza las filas).</summary>
[Trait("Categoria", "ComparacionVisual")]
public class TareasComparacionTests
{
    // Títulos y proyectos que no entran en su columna: el mockup también los corta (v2-celda-cortada) y el píxel coincide.
    private static readonly string[] CortesDelMockup =
    [
        "Reparar el bache de la calle Zorrilla", "Poda de los plátanos de la avenida Artigas", "Limpieza de las cunetas del Barrio Norte",
        "Plan de mantenimiento vial del centro 2026",
    ];

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null,
        string[]? recortesPermitidos = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar,
            recortesPermitidos: recortesPermitidos);

    private static Action<Control> SeleccionandoLaTarea(int id) => vista =>
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var vm = (TareaListViewModel)((ShellMainViewModel)vista.DataContext!).CurrentContent!;
        vm.FilaSeleccionada = vm.Tareas.Single(f => f.Id == id);
        Dispatcher.UIThread.RunJobs();
    };

    [AvaloniaFact] public void Tareas_IgualAlMockup() =>
        Comparar(DatosTareas.Listado(), "gestion/tareas.html", preparar: SeleccionandoLaTarea(4), recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void Tareas_IgualAlMockup_1366x768() =>
        Comparar(DatosTareas.Listado(), "gestion/tareas.html", TamanoLienzo.Municipio, SeleccionandoLaTarea(4), CortesDelMockup);
    [AvaloniaFact] public void TareasOperador_IgualAlMockup() =>
        Comparar(DatosTareas.ListadoOperador(), "gestion/tareas-operador.html", preparar: SeleccionandoLaTarea(6), recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void TareasOperador_IgualAlMockup_1366x768() =>
        Comparar(DatosTareas.ListadoOperador(), "gestion/tareas-operador.html", TamanoLienzo.Municipio, SeleccionandoLaTarea(6), CortesDelMockup);
    [AvaloniaFact] public void TareasCargando_IgualAlMockup() => Comparar(DatosTareas.Cargando(), "gestion/tareas-cargando.html");
    [AvaloniaFact] public void TareasCargando_IgualAlMockup_1366x768() => Comparar(DatosTareas.Cargando(), "gestion/tareas-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void TareasSinConexion_IgualAlMockup() => Comparar(DatosTareas.SinConexion(), "gestion/tareas-sin-conexion.html");
    [AvaloniaFact] public void TareasSinConexion_IgualAlMockup_1366x768() => Comparar(DatosTareas.SinConexion(), "gestion/tareas-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void TareasSinPermiso_IgualAlMockup() => Comparar(DatosTareas.SinPermiso(), "gestion/tareas-sin-permiso.html");
    [AvaloniaFact] public void TareasSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosTareas.SinPermiso(), "gestion/tareas-sin-permiso.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void TareasVacio_IgualAlMockup() => Comparar(DatosTareas.SinTareas(), "gestion/tareas-vacio.html");
    [AvaloniaFact] public void TareasVacio_IgualAlMockup_1366x768() => Comparar(DatosTareas.SinTareas(), "gestion/tareas-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void TareasSinResultados_IgualAlMockup() => Comparar(DatosTareas.SinResultados(), "gestion/tareas-sin-resultados.html");
    [AvaloniaFact] public void TareasSinResultados_IgualAlMockup_1366x768() => Comparar(DatosTareas.SinResultados(), "gestion/tareas-sin-resultados.html", TamanoLienzo.Municipio);
}
