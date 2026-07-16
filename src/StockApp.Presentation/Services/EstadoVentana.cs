namespace StockApp.Presentation.Services;

/// <summary>
/// Snapshot del tamaño, posición y estado (maximizada o no) de la ventana principal,
/// persistido localmente para restaurarlo en el próximo arranque de la app.
/// </summary>
public sealed record EstadoVentana
{
    /// <summary>Ancho de la ventana en su estado "normal" (no maximizada), en píxeles independientes de dispositivo.</summary>
    public double Ancho { get; init; }

    /// <summary>Alto de la ventana en su estado "normal" (no maximizada), en píxeles independientes de dispositivo.</summary>
    public double Alto { get; init; }

    /// <summary>Posición X de la ventana en coordenadas de pantalla (píxeles físicos).</summary>
    public int X { get; init; }

    /// <summary>Posición Y de la ventana en coordenadas de pantalla (píxeles físicos).</summary>
    public int Y { get; init; }

    /// <summary>Indica si la ventana estaba maximizada al momento de guardar el estado.</summary>
    public bool Maximizada { get; init; }
}
