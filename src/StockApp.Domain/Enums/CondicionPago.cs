namespace StockApp.Domain.Enums;

/// <summary>Condición de pago de un gasto. Contado crea un pago automático por el total.</summary>
public enum CondicionPago
{
    Contado = 0,
    Credito = 1,
}
