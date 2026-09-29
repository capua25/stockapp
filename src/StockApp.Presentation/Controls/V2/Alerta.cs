using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls.V2;

/// <summary>Mensaje de info, advertencia o error (spec §3.3).</summary>
public class Alerta : TemplatedControl
{
    public static readonly StyledProperty<string?> TextoProperty =
        AvaloniaProperty.Register<Alerta, string?>(nameof(Texto));

    public static readonly StyledProperty<VarianteAlerta> VarianteProperty =
        AvaloniaProperty.Register<Alerta, VarianteAlerta>(nameof(Variante), VarianteAlerta.Info);

    public string? Texto
    {
        get => GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    public VarianteAlerta Variante
    {
        get => GetValue(VarianteProperty);
        set => SetValue(VarianteProperty, value);
    }
}
