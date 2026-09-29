using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls.V2;

/// <summary>KPI de Inicio (spec §3.3 y §5.3): valor, etiqueta y estado sin dato.</summary>
public class TarjetaMetrica : TemplatedControl
{
    public static readonly StyledProperty<string?> ValorProperty =
        AvaloniaProperty.Register<TarjetaMetrica, string?>(nameof(Valor));

    public static readonly StyledProperty<string?> EtiquetaProperty =
        AvaloniaProperty.Register<TarjetaMetrica, string?>(nameof(Etiqueta));

    public static readonly StyledProperty<bool> SinDatoProperty =
        AvaloniaProperty.Register<TarjetaMetrica, bool>(nameof(SinDato));

    public string? Valor
    {
        get => GetValue(ValorProperty);
        set => SetValue(ValorProperty, value);
    }

    public string? Etiqueta
    {
        get => GetValue(EtiquetaProperty);
        set => SetValue(EtiquetaProperty, value);
    }

    /// <summary>True: muestra "—" en color texto grupo en lugar del valor.</summary>
    public bool SinDato
    {
        get => GetValue(SinDatoProperty);
        set => SetValue(SinDatoProperty, value);
    }
}
