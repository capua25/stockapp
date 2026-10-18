using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Pill de estado: palabra + color (spec §3.3).
/// </summary>
public class Insignia : TemplatedControl
{
    public static readonly StyledProperty<string?> TextoProperty =
        AvaloniaProperty.Register<Insignia, string?>(nameof(Texto));

    public static readonly StyledProperty<VarianteInsignia> VarianteProperty =
        AvaloniaProperty.Register<Insignia, VarianteInsignia>(nameof(Variante), VarianteInsignia.Neutro);

    public string? Texto
    {
        get => GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    public VarianteInsignia Variante
    {
        get => GetValue(VarianteProperty);
        set => SetValue(VarianteProperty, value);
    }
}
