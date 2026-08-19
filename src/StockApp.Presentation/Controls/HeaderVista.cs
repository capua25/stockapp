using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace StockApp.Presentation.Controls;

/// <summary>
/// Encabezado estandar de vista: eyebrow de seccion, titulo, linea de resumen y slot de acciones
/// a la derecha. Existe porque cada una de las 58 vistas improvisaba el suyo y 15 no tenian ni
/// titulo. TemplatedControl y no UserControl: un UserControl trae su propio NameScope y
/// Window.FindControl no lo atraviesa (ver el comentario de InicioViewTests.cs).
/// </summary>
public class HeaderVista : TemplatedControl
{
    public static readonly StyledProperty<string?> EyebrowProperty =
        AvaloniaProperty.Register<HeaderVista, string?>(nameof(Eyebrow));

    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<HeaderVista, string?>(nameof(Titulo));

    public static readonly StyledProperty<string?> ResumenProperty =
        AvaloniaProperty.Register<HeaderVista, string?>(nameof(Resumen));

    public static readonly StyledProperty<object?> AccionesProperty =
        AvaloniaProperty.Register<HeaderVista, object?>(nameof(Acciones));

    /// <summary>Etiqueta de seccion sobre el titulo, en escala .micro. Si es null, no ocupa alto.</summary>
    public string? Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public string? Titulo
    {
        get => GetValue(TituloProperty);
        set => SetValue(TituloProperty, value);
    }

    /// <summary>Linea de contexto bajo el titulo. Si es null, no ocupa alto.</summary>
    public string? Resumen
    {
        get => GetValue(ResumenProperty);
        set => SetValue(ResumenProperty, value);
    }

    /// <summary>
    /// Slot de acciones, alineado a la derecha. Regla de jerarquia: UNA sola accion primaria
    /// (Classes="primary") por vista. Si hay dos acciones principales, no hay ninguna.
    /// </summary>
    [Content]
    public object? Acciones
    {
        get => GetValue(AccionesProperty);
        set => SetValue(AccionesProperty, value);
    }
}
