namespace StockApp.Domain.Enums;

/// <summary>
/// Texto visible del motivo (único lugar: combo, grillas, PDF, CSV) y regla de precio por
/// motivo (única fuente para el servicio y para el ViewModel del formulario).
/// </summary>
public static class MotivoMovimientoExtensions
{
    /// <summary>Texto para mostrar al usuario ("Uso o consumo", no "UsoOConsumo").</summary>
    public static string Nombre(this MotivoMovimiento motivo) => motivo switch
    {
        MotivoMovimiento.UsoOConsumo => "Uso o consumo",
        _ => motivo.ToString(),
    };
}
