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
    /// El precio unitario es OBLIGATORIO (&gt; 0) solo en una ENTRADA por Compra: es el único
    /// caso con un valor de adquisición. En Entrada por Ajuste es opcional; en Salida no aplica.
    /// </summary>
    public static bool PrecioEsObligatorio(this MotivoMovimiento motivo, TipoMovimiento tipo)
        => motivo.PrecioAplica(tipo) && motivo == MotivoMovimiento.Compra;

    /// <summary>
    /// El precio unitario aplica solo a las ENTRADAS. En una SALIDA no aplica nunca, sea cual
    /// sea el motivo (Uso o consumo, Merma, Ajuste): ni se pide ni se guarda (el municipio no
    /// vende ni valoriza lo que sale; la valorización usa Producto.PrecioCosto). Ajuste es válido
    /// en ambos sentidos, por eso la regla depende de tipo + motivo y no solo del motivo.
    /// </summary>
    public static bool PrecioAplica(this MotivoMovimiento motivo, TipoMovimiento tipo)
        => tipo == TipoMovimiento.Entrada;
}
