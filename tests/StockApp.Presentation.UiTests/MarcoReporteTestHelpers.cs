using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Views.Reportes;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Los reportes v2 muestran la grilla solo con filas (<c>MarcoReporte.HayDatos</c>, que sale de <c>Items.Count</c> del ViewModel).
/// Los guardianes heredados asignan <c>ItemsSource</c> directo al <c>DataGrid</c> de una vista SIN ViewModel: esto fuerza
/// <c>HayDatos</c> para que la grilla se mida y se realice, sin cambiar lo que cada test custodia.
/// </summary>
internal static class MarcoReporteTest
{
    public static void MostrarContenido(Control desde)
    {
        var marco = desde.FindAncestorOfType<MarcoReporte>() ?? throw new System.InvalidOperationException("La vista no está dentro de un MarcoReporte.");
        marco.HayDatos = true;
        Dispatcher.UIThread.RunJobs();
    }
}
