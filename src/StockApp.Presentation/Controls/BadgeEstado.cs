using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls;

/// <summary>Tono semantico del badge. Mapea a los brushes de Tokens.axaml.</summary>
public enum TonoBadge
{
    Neutro,
    Exito,
    Advertencia,
    Peligro,
    Info,
}

/// <summary>
/// Estado comunicado con PALABRA mas color, no solo color. Hoy el stock negativo se pinta de
/// rojo y nada mas: un usuario daltonico no lo distingue de un stock normal.
/// </summary>
public class BadgeEstado : TemplatedControl
{
    public static readonly StyledProperty<string?> TextoProperty =
        AvaloniaProperty.Register<BadgeEstado, string?>(nameof(Texto));

    public static readonly StyledProperty<TonoBadge> TonoProperty =
        AvaloniaProperty.Register<BadgeEstado, TonoBadge>(nameof(Tono), TonoBadge.Neutro);

    public string? Texto
    {
        get => GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    public TonoBadge Tono
    {
        get => GetValue(TonoProperty);
        set => SetValue(TonoProperty, value);
    }
}
