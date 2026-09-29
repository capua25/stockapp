using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Encabezado de página (spec §3.3 y §4.4): breadcrumb opcional, título, subtítulo y slot de
/// acciones a la derecha. El primer tramo del breadcrumb ejecuta BreadcrumbComando, que las
/// vistas bindean al comando Volver que su ViewModel ya expone: no hay back-stack.
/// </summary>
public class EncabezadoPagina : TemplatedControl
{
    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<EncabezadoPagina, string?>(nameof(Titulo));

    public static readonly StyledProperty<string?> SubtituloProperty =
        AvaloniaProperty.Register<EncabezadoPagina, string?>(nameof(Subtitulo));

    public static readonly StyledProperty<string?> BreadcrumbOrigenProperty =
        AvaloniaProperty.Register<EncabezadoPagina, string?>(nameof(BreadcrumbOrigen));

    public static readonly StyledProperty<ICommand?> BreadcrumbComandoProperty =
        AvaloniaProperty.Register<EncabezadoPagina, ICommand?>(nameof(BreadcrumbComando));

    public static readonly StyledProperty<string?> BreadcrumbActualProperty =
        AvaloniaProperty.Register<EncabezadoPagina, string?>(nameof(BreadcrumbActual));

    public static readonly StyledProperty<object?> AccionesProperty =
        AvaloniaProperty.Register<EncabezadoPagina, object?>(nameof(Acciones));

    public string? Titulo
    {
        get => GetValue(TituloProperty);
        set => SetValue(TituloProperty, value);
    }

    /// <summary>Si es null o vacío, no ocupa alto.</summary>
    public string? Subtitulo
    {
        get => GetValue(SubtituloProperty);
        set => SetValue(SubtituloProperty, value);
    }

    /// <summary>Primer tramo del breadcrumb (link). Si es null o vacío, no hay breadcrumb.</summary>
    public string? BreadcrumbOrigen
    {
        get => GetValue(BreadcrumbOrigenProperty);
        set => SetValue(BreadcrumbOrigenProperty, value);
    }

    public ICommand? BreadcrumbComando
    {
        get => GetValue(BreadcrumbComandoProperty);
        set => SetValue(BreadcrumbComandoProperty, value);
    }

    public string? BreadcrumbActual
    {
        get => GetValue(BreadcrumbActualProperty);
        set => SetValue(BreadcrumbActualProperty, value);
    }

    /// <summary>Slot de acciones a la derecha. Un solo botón primario por vista (spec §3.3).</summary>
    [Content]
    public object? Acciones
    {
        get => GetValue(AccionesProperty);
        set => SetValue(AccionesProperty, value);
    }
}
