namespace StockApp.UiV2.Herramientas.Imagenes;

/// <summary>
/// PixelesDistintos: delta de canal mayor a la tolerancia. TeselasDensas: bloques de 16x16 con
/// al menos 25% de píxeles distintos (defectos de layout o color; el antialiasing no los forma).
/// </summary>
public sealed record ResultadoComparacion(int Ancho, int Alto, int PixelesDistintos, int TeselasDensas)
{
    public double PorcentajeDistinto => 100.0 * PixelesDistintos / ((double)Ancho * Alto);
}
