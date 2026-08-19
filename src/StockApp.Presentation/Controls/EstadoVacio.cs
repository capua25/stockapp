using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls;

/// <summary>
/// Estado vacio de una grilla o listado. EsError distingue "todavia no hay datos" de "fallo la
/// carga": hoy los dos casos se ven identicos y el usuario no sabe si cargar datos o reintentar.
/// </summary>
public class EstadoVacio : TemplatedControl
{
    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<EstadoVacio, string?>(nameof(Titulo));

    public static readonly StyledProperty<string?> MensajeProperty =
        AvaloniaProperty.Register<EstadoVacio, string?>(nameof(Mensaje));

    public static readonly StyledProperty<bool> EsErrorProperty =
        AvaloniaProperty.Register<EstadoVacio, bool>(nameof(EsError));

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

    /// <summary>True = la carga fallo. False = no hay datos todavia.</summary>
    public bool EsError
    {
        get => GetValue(EsErrorProperty);
        set => SetValue(EsErrorProperty, value);
    }
}
