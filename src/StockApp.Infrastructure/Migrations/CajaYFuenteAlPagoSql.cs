namespace StockApp.Infrastructure.Migrations;

/// <summary>
/// SQL crudo de la migración AgregaCajaYFuenteAlPago, en una constante compartida entre la
/// migración y su test (mismo criterio que NormalizacionCatalogosSql): el texto probado es el
/// que realmente corre.
/// </summary>
public static class CajaYFuenteAlPagoSql
{
    /// <summary>
    /// Los literales B y C del FIGM pertenecen a la caja POA; todo lo demás queda en Local
    /// (default de la columna). Se compara el nombre sin mayúsculas ni espacios de más
    /// ("Literal B", " b ", "C"). No falla si no existe ninguno.
    /// </summary>
    public const string ClasificarFuentesPoa =
        "UPDATE \"FuentesFinanciamiento\" SET \"Caja\" = 1 " +
        "WHERE regexp_replace(lower(btrim(\"Nombre\")), '\\s+', ' ', 'g') ~ '^(literal )?[bc]$';";

    /// <summary>
    /// Cada pago hereda la fuente de su gasto. Tiene que correr ANTES de que el gasto deje de
    /// tener la fuente (migración posterior): después no hay de dónde copiarla.
    /// </summary>
    public const string CopiarFuenteDelGastoALosPagos =
        "UPDATE \"PagosGasto\" p SET \"FuenteFinanciamientoId\" = g.\"FuenteFinanciamientoId\" " +
        "FROM \"Gastos\" g WHERE g.\"Id\" = p.\"GastoId\";";
}
