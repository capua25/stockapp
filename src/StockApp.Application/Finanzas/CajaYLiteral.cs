using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// Reglas compartidas entre caja y literal (fuente de financiamiento) de los servicios de Finanzas.
/// </summary>
public static class CajaYLiteral
{
    public const string MensajeCajaInexistente = "La caja indicada no existe.";

    public const string MensajePagoManualEnGastoPoa =
        "Un gasto de la caja POA no admite pagos manuales: anulalo y cargalo de nuevo.";

    /// <summary>
    /// La caja se elige explícitamente, nunca se deduce: un body sin caja (null) se rechaza con
    /// <paramref name="mensaje"/> en vez de caer en el valor por defecto del enum (Local).
    /// </summary>
    public static Caja Obligatoria(Caja? caja, string mensaje)
        => caja ?? throw new ArgumentException(mensaje);

    /// <summary>
    /// La API no valida el rango del enum (acepta cualquier entero): sin este chequeo se guardaría un
    /// movimiento en una caja que ninguna pantalla muestra.
    /// </summary>
    public static void ValidarCaja(Caja caja)
    {
        if (!Enum.IsDefined(caja))
            throw new ArgumentException(MensajeCajaInexistente);
    }

    public static string MensajeFuenteDeOtraCaja(string fuente, Caja caja)
        => $"La fuente de financiamiento '{fuente}' no corresponde a la caja {caja.Nombre()}.";

    /// <summary>
    /// Valida la fuente de un movimiento contra su caja. La caja y la existencia se chequean siempre; la baja y la
    /// pertenencia a la caja solo cuando <paramref name="cambio"/> (alta, o se cambió la caja o la fuente): editar
    /// otro campo de un movimiento histórico no puede quedar bloqueado por una fuente que hoy es de otra caja.
    /// </summary>
    public static async Task<FuenteFinanciamiento> ValidarFuenteAsync(
        IFuenteFinanciamientoRepository fuentes, Caja caja, int fuenteFinanciamientoId, bool cambio)
    {
        ValidarCaja(caja);
        var fuente = await fuentes.ObtenerPorIdAsync(fuenteFinanciamientoId)
            ?? throw new EntidadNoEncontradaException(
                $"Fuente de financiamiento {fuenteFinanciamientoId} no encontrada.");
        if (cambio)
        {
            if (!fuente.Activo)
                throw new ReglaDeNegocioException(
                    $"La fuente de financiamiento '{fuente.Nombre}' está dada de baja.");
            if (fuente.Caja != caja)
                throw new ReglaDeNegocioException(MensajeFuenteDeOtraCaja(fuente.Nombre, caja));
        }
        return fuente;
    }
}
