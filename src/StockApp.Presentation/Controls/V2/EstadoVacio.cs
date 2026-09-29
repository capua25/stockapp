using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Estado vacío rediseñado (spec §3.3). Conserva la distinción del viejo: EsError = "falló la
/// carga" (título en peligro), false = "no hay datos todavía". Acciones opcionales debajo.
/// </summary>
public class EstadoVacio : TemplatedControl
{
    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<EstadoVacio, string?>(nameof(Titulo));

    public static readonly StyledProperty<string?> MensajeProperty =
        AvaloniaProperty.Register<EstadoVacio, string?>(nameof(Mensaje));

    public static readonly StyledProperty<bool> EsErrorProperty =
        AvaloniaProperty.Register<EstadoVacio, bool>(nameof(EsError));

    public static readonly StyledProperty<object?> AccionesProperty =
        AvaloniaProperty.Register<EstadoVacio, object?>(nameof(Acciones));

    public string? Titulo
    {
        get => GetValue(TituloProperty);
        set => SetValue(TituloProperty, value);
    }

    public string? Mensaje
    {
        get => GetValue(MensajeProperty);
        set => SetValue(MensajeProperty, value);
    }

    public bool EsError
    {
        get => GetValue(EsErrorProperty);
        set => SetValue(EsErrorProperty, value);
    }

    [Content]
    public object? Acciones
    {
        get => GetValue(AccionesProperty);
        set => SetValue(AccionesProperty, value);
    }
}
