using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Indicador de carga v2: un texto ("Cargando productos…") en el lugar del estado vacío, mientras la carga corre
/// y todavía no hay nada que mostrar. Texto y no spinner: una animación no se puede fijar contra un mockup.
/// </summary>
public class EstadoCargando : TemplatedControl
{
    public static readonly StyledProperty<string?> TextoProperty =
        AvaloniaProperty.Register<EstadoCargando, string?>(nameof(Texto));

    public string? Texto
    {
        get => GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }
}
