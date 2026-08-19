using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls;

/// <summary>
/// KPI de la fila de metricas que va sobre las grillas. Etiqueta en .micro, valor grande,
/// detalle opcional debajo.
/// </summary>
public class TarjetaMetrica : TemplatedControl
{
    public static readonly StyledProperty<string?> EtiquetaProperty =
        AvaloniaProperty.Register<TarjetaMetrica, string?>(nameof(Etiqueta));

    public static readonly StyledProperty<string?> ValorProperty =
        AvaloniaProperty.Register<TarjetaMetrica, string?>(nameof(Valor));

    public static readonly StyledProperty<string?> DetalleProperty =
        AvaloniaProperty.Register<TarjetaMetrica, string?>(nameof(Detalle));

    public string? Etiqueta
    {
        get => GetValue(EtiquetaProperty);
        set => SetValue(EtiquetaProperty, value);
    }

    public string? Valor
    {
        get => GetValue(ValorProperty);
        set => SetValue(ValorProperty, value);
    }

    /// <summary>Linea de contexto bajo el valor. Si es null, no ocupa alto.</summary>
    public string? Detalle
    {
        get => GetValue(DetalleProperty);
        set => SetValue(DetalleProperty, value);
    }
}
