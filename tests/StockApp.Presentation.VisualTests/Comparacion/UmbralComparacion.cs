using System.Globalization;

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

    /// <summary>
    /// Teselas de M2 en el catálogo (relleno pálido equivocado, el defecto más difícil). Un umbral
    /// aprobado que llega acá dejaría pasar ese defecto: exige ver la mutación equivalente en rojo.
    /// </summary>
    public const int TeselasMutacionPalidaCatalogo = 16;

    /// <summary>
    /// El máximo de teselas densas que aplica: sin aprobación, a lo sumo el calibrado; con
    /// aprobación, el registrado, validado. Falla (InvalidOperationException) si no se cumple.
    /// </summary>
    public static int TeselasMaximas(int teselasDensasMax, UmbralAprobado? aprobado)
    {
        if (aprobado is null)
        {
            if (teselasDensasMax > TeselasDensasPorDefecto)
            {
                throw new InvalidOperationException(
                    $"{teselasDensasMax} teselas densas supera las {TeselasDensasPorDefecto} calibradas: un umbral mayor "
                    + "se pasa como UmbralAprobado, con la revisión del usuario registrada (spec §6.5).");
            }

            return teselasDensasMax;
        }

        if (teselasDensasMax != TeselasDensasPorDefecto)
        {
            throw new InvalidOperationException("Umbral de teselas doble: con UmbralAprobado no se pasa teselasDensasMax.");
        }

        if (string.IsNullOrWhiteSpace(aprobado.AprobadoPor) || string.IsNullOrWhiteSpace(aprobado.Motivo)
            || !DateOnly.TryParseExact(aprobado.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new InvalidOperationException(
                "UmbralAprobado incompleto: AprobadoPor, Motivo y Fecha (yyyy-MM-dd) son obligatorios (spec §6.5).");
        }

        if (aprobado.TeselasDeTono >= TeselasMutacionPalidaCatalogo && string.IsNullOrWhiteSpace(aprobado.MutacionPalidaEnRojo))
        {
            throw new InvalidOperationException(
                $"{aprobado.TeselasDeTono} teselas aprobadas alcanza las {TeselasMutacionPalidaCatalogo} de M2 del catálogo "
                + "(relleno pálido equivocado): hay que correr en esta pantalla la mutación equivalente a M2, verla en "
                + "rojo y registrarla en MutacionPalidaEnRojo (spec §6.5).");
        }

        return aprobado.TeselasDeTono;
    }
}
