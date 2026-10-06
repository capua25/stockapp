using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inicio;

[Trait("Categoria", "ComparacionVisual")]
public class InicioComparacionTests
{
    // Pantalla de contenido (convención 6): shell real, Inicio resuelto por ViewLocator. El sidebar ya tiene su
    // comparación (ShellComparacionTests) y la marca de agua no se compara (sin test propio por decisión del
    // usuario, U9): se ocultan en AMBOS renders sin mover el layout. Valores calibrados (11 teselas, 1.68%), sin UmbralAprobado.
    private static readonly string[] OcultarEnMockup = [".v2-sidebar", ".marca-agua"];

    public static void OcultarSidebarYMarcaDeAgua(Control vista)
    {
        var shell = (ShellMainView)vista;
        shell.FindControl<Border>("Sidebar")!.IsVisible = false;
        var inicio = shell.GetVisualDescendants().OfType<InicioView>().Single();
        inicio.FindControl<Image>("MarcaAgua")!.IsVisible = false;
    }

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebarYMarcaDeAgua);

    [AvaloniaFact]
    public void Admin_IgualAlMockup() => Comparar(DatosInicio.Admin(), "inicio/inicio-admin.html");

    [AvaloniaFact]
    public void Admin_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.Admin(), "inicio/inicio-admin.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void OperadorMixto_IgualAlMockup() => Comparar(DatosInicio.OperadorMixto(), "inicio/inicio-operador.html");

    [AvaloniaFact]
    public void OperadorMixto_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.OperadorMixto(), "inicio/inicio-operador.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void OperadorSinFinanzasNiTareas_IgualAlMockup() =>
        Comparar(DatosInicio.OperadorSinFinanzasNiTareas(), "inicio/inicio-operador-sin-finanzas.html");

    [AvaloniaFact]
    public void OperadorSinFinanzasNiTareas_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.OperadorSinFinanzasNiTareas(), "inicio/inicio-operador-sin-finanzas.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void AdminSinDatos_IgualAlMockup() => Comparar(DatosInicio.AdminSinDatos(), "inicio/inicio-admin-sin-datos.html");

    [AvaloniaFact]
    public void AdminSinDatos_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.AdminSinDatos(), "inicio/inicio-admin-sin-datos.html", TamanoLienzo.Municipio);
}
