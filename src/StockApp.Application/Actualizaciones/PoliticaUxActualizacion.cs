namespace StockApp.Application.Actualizaciones;

/// <summary>Dada una severity + si la descarga fue posible, decide la acción de UI.
/// Sin dependencias de UI ni Velopack → 100% unit-testeable.</summary>
public class PoliticaUxActualizacion
{
    public AccionUx Decidir(UpdateCheckResult resultado, bool descargaPosible)
    {
        if (!resultado.HayUpdate)
            return new AccionUx(ModoUx.Ninguno, null, false, false, resultado.Version);

        return resultado.Severity switch
        {
            UpdateSeverity.Normal => new AccionUx(
                ModoUx.BannerDiscreto, resultado.NotasMarkdown,
                Posponible: true, ReintentaEnArranque: true, Version: resultado.Version),

            UpdateSeverity.Important => new AccionUx(
                ModoUx.ModalPosponible, resultado.NotasMarkdown,
                Posponible: true, ReintentaEnArranque: true, Version: resultado.Version),

            UpdateSeverity.Critical when descargaPosible => new AccionUx(
                ModoUx.BloqueoCritico, resultado.NotasMarkdown,
                Posponible: false, ReintentaEnArranque: true, Version: resultado.Version),

            UpdateSeverity.Critical => new AccionUx(   // critical pero no se pudo bajar
                ModoUx.ModoDegradado, resultado.NotasMarkdown,
                Posponible: false, ReintentaEnArranque: true, Version: resultado.Version),

            _ => new AccionUx(ModoUx.Ninguno, null, false, false, resultado.Version),
        };
    }
}
