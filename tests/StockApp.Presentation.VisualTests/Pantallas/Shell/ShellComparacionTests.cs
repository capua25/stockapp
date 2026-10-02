using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Shell;

[Trait("Categoria", "ComparacionVisual")]
public class ShellComparacionTests
{
    [AvaloniaFact]
    public void SidebarAdmin_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosShell.Admin(), "shell/sidebar-admin.html");

    [AvaloniaFact]
    public void SidebarAdmin_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosShell.Admin(), "shell/sidebar-admin.html",
            tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void SidebarOperadorMixto_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosShell.OperadorMixto(), "shell/sidebar-operador.html");

    [AvaloniaFact]
    public void SidebarOperadorMixto_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosShell.OperadorMixto(), "shell/sidebar-operador.html",
            tamano: TamanoLienzo.Municipio);
}
