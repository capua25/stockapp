namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Compuertas de aceptación de una pantalla contra su mockup (spec §6.3), calibradas en la
/// Fase 0 con la pantalla catálogo-de-componentes (spec §6.5, mediciones y regla). No se suben
/// por pantalla sin aprobación del usuario.
/// </summary>
public static class UmbralComparacion
{
    /// <summary>Máximo % de píxeles con diferencia fuerte (delta de canal &gt; 32).</summary>
    public const double PorcentajePorDefecto = 1.68;

    /// <summary>Máximo de teselas 16x16 densas (≥ 25% fuertes o ≥ 90% leves).</summary>
    public const int TeselasDensasPorDefecto = 11;
}
