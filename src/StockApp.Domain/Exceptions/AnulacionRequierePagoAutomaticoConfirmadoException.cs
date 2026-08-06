namespace StockApp.Domain.Exceptions;

/// <summary>
/// Se lanza al intentar anular (por asiento inverso o baja lógica) un gasto que tiene un
/// pago automático de contado activo, sin haber confirmado explícitamente que también se va
/// a anular ese pago (parámetro confirmarAnulacionDePagoAutomatico). Mismo patrón que
/// StockInsuficienteException: hereda de ReglaDeNegocioException (cae a 409 por default en
/// DomainExceptionHandler) pero expone datos estructurados (GastoId, MontoPagoAutomatico) para
/// que la capa HTTP los agregue como extensión del problem+json y el cliente reconstruya esta
/// excepción específica en vez de una ReglaDeNegocioException genérica — así puede distinguir
/// "che, esto va a borrar un pago, ¿confirmás?" de cualquier otro 409 de negocio sin parsear
/// el texto del mensaje.
/// </summary>
public class AnulacionRequierePagoAutomaticoConfirmadoException : ReglaDeNegocioException
{
    public int GastoId { get; }
    public decimal MontoPagoAutomatico { get; }

    public AnulacionRequierePagoAutomaticoConfirmadoException(int gastoId, decimal montoPagoAutomatico)
        : base($"El gasto {gastoId} tiene un pago automático de contado activo por {montoPagoAutomatico}: " +
               "anularlo también va a eliminar ese pago. Confirmá la anulación para continuar.")
    {
        GastoId             = gastoId;
        MontoPagoAutomatico = montoPagoAutomatico;
    }
}
