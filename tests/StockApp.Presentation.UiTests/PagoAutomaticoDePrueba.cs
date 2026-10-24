using System;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// El gasto ya no tiene fuente: la del contado vive en su pago automático. EsAutomatico solo se pone
/// con PagoGasto.Automatico, así que los datos de prueba lo arman por acá.
/// </summary>
internal static class PagoAutomaticoDePrueba
{
    public static PagoGasto Con(
        FuenteFinanciamiento fuente, Caja caja = Caja.Local, DateTime? fecha = null, decimal monto = 0m)
    {
        var pago = PagoGasto.Automatico(
            fecha ?? DateTime.UtcNow, monto, "Pago contado (automático)", caja, fuente.Id);
        pago.FuenteFinanciamiento = fuente;
        return pago;
    }
}
