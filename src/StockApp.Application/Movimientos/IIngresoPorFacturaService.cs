namespace StockApp.Application.Movimientos;

/// <summary>Ingreso de stock por factura en un solo lote atómico (Gasto + N MovimientoStock).</summary>
public interface IIngresoPorFacturaService
{
    Task<IngresoPorFacturaResultadoDto> RegistrarAsync(IngresoPorFacturaDto dto);

    /// <summary>Anula el lote completo por asiento inverso. Ver Task 5.</summary>
    Task AnularLoteAsync(int gastoId, bool confirmarAnulacionDePagoAutomatico = false);
}
