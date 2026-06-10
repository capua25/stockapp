namespace StockApp.Domain.Exceptions;

/// <summary>
/// Se lanza cuando se intenta registrar una salida de stock y el stock disponible
/// es menor a la cantidad solicitada (y no se forzó la operación).
/// </summary>
public class StockInsuficienteException : Exception
{
    public int ProductoId           { get; }
    public decimal StockActual      { get; }
    public decimal CantidadSolicitada { get; }
    public decimal StockResultante  { get; }

    public StockInsuficienteException(int productoId, decimal stockActual, decimal cantidadSolicitada)
        : base($"Stock insuficiente para el producto {productoId}: "
               + $"tenés {stockActual} unidades pero solicitaste {cantidadSolicitada}. "
               + $"El stock resultante sería {stockActual - cantidadSolicitada}.")
    {
        ProductoId         = productoId;
        StockActual        = stockActual;
        CantidadSolicitada = cantidadSolicitada;
        StockResultante    = stockActual - cantidadSolicitada;
    }
}
