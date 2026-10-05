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

    /// <summary>
    /// El precio unitario es OBLIGATORIO (&gt; 0) solo para Compra: es el único motivo con un
    /// valor de adquisición. Para Ajuste y Merma es opcional.
    /// </summary>
    public static bool PrecioEsObligatorio(this MotivoMovimiento motivo)
        => motivo == MotivoMovimiento.Compra;

    /// <summary>
    /// El precio aplica a todos los motivos salvo Uso o consumo, donde ni se pide ni se guarda
    /// (el municipio no vende ni valoriza el consumo interno).
    /// </summary>
    public static bool PrecioAplica(this MotivoMovimiento motivo)
        => motivo != MotivoMovimiento.UsoOConsumo;
}
