using Avalonia.Headless.XUnit;
using StockApp.Presentation.Actualizaciones.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Actualizaciones;

[Trait("Categoria", "ComparacionVisual")]
public class ActualizacionesComparacionTests
{
    [AvaloniaFact]
    public void Banner_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ActualizacionBannerView(), DatosActualizaciones.Banner(), "actualizaciones/banner.html");

    [AvaloniaFact]
    public void Banner_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ActualizacionBannerView(), DatosActualizaciones.Banner(), "actualizaciones/banner.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void Modal_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ActualizacionModalView(), DatosActualizaciones.Modal(), "actualizaciones/modal.html");

    [AvaloniaFact]
    public void Modal_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ActualizacionModalView(), DatosActualizaciones.Modal(), "actualizaciones/modal.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void Bloqueo_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ActualizacionBloqueoView(), DatosActualizaciones.Bloqueo(), "actualizaciones/bloqueo.html");

    [AvaloniaFact]
    public void Bloqueo_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ActualizacionBloqueoView(), DatosActualizaciones.Bloqueo(), "actualizaciones/bloqueo.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ModoDegradado_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ActualizacionBloqueoView(), DatosActualizaciones.ModoDegradado(), "actualizaciones/modo-degradado.html");

    [AvaloniaFact]
    public void ModoDegradado_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ActualizacionBloqueoView(), DatosActualizaciones.ModoDegradado(), "actualizaciones/modo-degradado.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void AvisoCritico_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ActualizacionAvisoCriticoView(), DatosActualizaciones.AvisoCritico(), "actualizaciones/aviso-critico.html");

    [AvaloniaFact]
    public void AvisoCritico_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ActualizacionAvisoCriticoView(), DatosActualizaciones.AvisoCritico(), "actualizaciones/aviso-critico.html", tamano: TamanoLienzo.Municipio);
}
