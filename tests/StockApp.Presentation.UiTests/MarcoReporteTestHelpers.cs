using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
        // Sin filas el marco no arma la grilla (ZonaContenido oculta): se la muestra desde la vista (o desde un control de adentro).
        var marco = desde as MarcoReporte ?? desde.FindAncestorOfType<MarcoReporte>() ?? desde.GetLogicalAncestors().OfType<MarcoReporte>().FirstOrDefault() ?? desde.GetVisualDescendants().OfType<MarcoReporte>().FirstOrDefault()
            ?? throw new System.InvalidOperationException("La vista no tiene un MarcoReporte.");
        marco.HayDatos = true;
        Dispatcher.UIThread.RunJobs();
    }
}
