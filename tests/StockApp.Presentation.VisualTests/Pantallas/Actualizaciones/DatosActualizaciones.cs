using StockApp.Application.Actualizaciones;
using StockApp.Presentation.Actualizaciones;

namespace StockApp.Presentation.VisualTests.Pantallas.Actualizaciones;

/// <summary>Datos fijos de docs/ui-v2/mockups/actualizaciones/*.html.</summary>
public static class DatosActualizaciones
{
    private const string TextoCritico = "Esta versión corrige un problema de seguridad. La app se va a reiniciar para aplicarla.";

    public static ActualizacionBannerViewModel Banner() =>
        new(new AccionUx(ModoUx.BannerDiscreto, "Mejoras en reportes.", true, false, "0.5.0"));

    public static ActualizacionModalViewModel Modal() =>
        new(new AccionUx(ModoUx.ModalPosponible,
            "Esta versión corrige el cálculo del Libro caja y mejora la exportación a PDF.", true, false, "0.5.0"));

    public static ActualizacionBloqueoViewModel Bloqueo() =>
        new(new AccionUx(ModoUx.BloqueoCritico, TextoCritico, false, true, "0.5.0"));

    public static ActualizacionBloqueoViewModel ModoDegradado() =>
        new(new AccionUx(ModoUx.ModoDegradado, TextoCritico, false, true, "0.5.0"));
}
