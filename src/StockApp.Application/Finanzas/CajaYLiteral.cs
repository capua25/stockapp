using StockApp.Domain.Enums;

namespace StockApp.Application.Finanzas;

/// <summary>
/// Reglas compartidas entre caja y literal (fuente de financiamiento) de los servicios de Finanzas.
/// </summary>
public static class CajaYLiteral
{
    public const string MensajeCajaInexistente = "La caja indicada no existe.";

    /// <summary>
    /// La API no valida el rango del enum (acepta cualquier entero): sin este chequeo se guardaría un
    /// movimiento en una caja que ninguna pantalla muestra.
    /// </summary>
    public static void ValidarCaja(Caja caja)
    {
        if (!Enum.IsDefined(caja))
            throw new ArgumentException(MensajeCajaInexistente);
    }
}
