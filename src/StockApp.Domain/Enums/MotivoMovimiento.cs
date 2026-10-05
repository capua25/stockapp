namespace StockApp.Domain.Enums;

/// <summary>
/// Motivo de un movimiento de stock. Se persiste como int y viaja como número en la API:
/// los valores son EXPLÍCITOS y no deben reordenarse ni reutilizarse
/// (ver MotivoMovimientoTests). UsoOConsumo conserva el 1 que antes era "Venta": el municipio
/// no vende, así que los datos existentes pasan a significar "Uso o consumo" sin migración.
/// </summary>
public enum MotivoMovimiento
{
    Compra = 0,
    UsoOConsumo = 1,
    Ajuste = 2,
    Merma = 3
}
