using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Application.Actualizaciones;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Actualizaciones.Views;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Review Focus F1: App.axaml registra <c>ViewLocator</c> en Application.DataTemplates (única
/// fuente de verdad). IncludesAppAxamlTests solo compara ResourceInclude/StyleInclude, así que
/// nadie más detecta si VisualTestApp se queda sin ese registro: cualquier ContentControl cuyo
/// Content sea un ViewModel real (MainWindow, ShellMainView, formularios con paneles anidados)
/// renderiza el placeholder "Not Found: ..." de ViewLocator en vez de la vista real, y una
/// comparación visual contra el mockup daría verde contra un render que no es el de producción.
/// </summary>
public class ViewLocatorTests
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("render-").FullName;

    [AvaloniaFact]
    public void ResuelveLaVistaRealParaUnViewModelDeProduccion_NoElPlaceholder()
    {
        var accion = new AccionUx(ModoUx.BannerDiscreto, "Nueva versión disponible", true, false, "1.2.3");
        var contenedor = new ContentControl { Content = new ActualizacionBannerViewModel(accion) };

        RenderizadorVista.RenderizarAPng(contenedor, null, Path.Combine(_carpeta, "view-locator.png"));

        var descendientes = contenedor.GetVisualDescendants().ToList();
        Assert.Contains(descendientes, c => c is ActualizacionBannerView);
        Assert.DoesNotContain(descendientes, c =>
            c is TextBlock textBlock && textBlock.Text is not null && textBlock.Text.StartsWith("Not Found", StringComparison.Ordinal));
    }
}
