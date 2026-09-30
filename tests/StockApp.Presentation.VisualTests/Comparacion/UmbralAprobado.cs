namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Registro de un umbral de teselas densas aprobado para UNA pantalla (spec §6.5, "Umbral por
/// pantalla"). Solo se usa cuando la pantalla supera lo calibrado sin defecto: el usuario revisó
/// cada tesela densa (contorno magenta) en la página de comparación y confirmó que es solo tono.
/// </summary>
/// <param name="TeselasDeTono">Teselas densas de la pantalla, todas revisadas y confirmadas como tono.</param>
/// <param name="AprobadoPor">Quién las revisó y aprobó.</param>
/// <param name="Fecha">Fecha de la aprobación, yyyy-MM-dd.</param>
/// <param name="Motivo">Qué son esas teselas (p. ej. "grilla de 12 filas: tono del texto").</param>
/// <param name="MutacionPalidaEnRojo">Obligatoria desde <see cref="UmbralComparacion.TeselasMutacionPalidaCatalogo"/>:
/// la mutación equivalente a M2 corrida en ESTA pantalla y cuántas teselas dio en rojo.</param>
/// <param name="PorcentajeMaximo">% de píxeles fuertes aprobado, si hace falta superar
/// <see cref="UmbralComparacion.PorcentajePorDefecto"/>; null deja el calibrado.</param>
public sealed record UmbralAprobado(
    int TeselasDeTono,
    string AprobadoPor,
    string Fecha,
    string Motivo,
    string? MutacionPalidaEnRojo = null,
    double? PorcentajeMaximo = null);
