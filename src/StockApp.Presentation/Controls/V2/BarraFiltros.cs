using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Metadata;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Buscador (280 de ancho) más filtros de un listado (spec §3.3 y §5.1). TextoBusqueda es
/// TwoWay por defecto: {Binding Busqueda} en la vista alcanza, como en un TextBox.Text.
/// </summary>
public class BarraFiltros : TemplatedControl
{
    public static readonly StyledProperty<string?> TextoBusquedaProperty =
        AvaloniaProperty.Register<BarraFiltros, string?>(nameof(TextoBusqueda), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> MarcaAguaBusquedaProperty =
        AvaloniaProperty.Register<BarraFiltros, string?>(nameof(MarcaAguaBusqueda));

    public static readonly StyledProperty<object?> FiltrosProperty =
        AvaloniaProperty.Register<BarraFiltros, object?>(nameof(Filtros));

    public string? TextoBusqueda
    {
        get => GetValue(TextoBusquedaProperty);
        set => SetValue(TextoBusquedaProperty, value);
    }

    public string? MarcaAguaBusqueda
    {
        get => GetValue(MarcaAguaBusquedaProperty);
        set => SetValue(MarcaAguaBusquedaProperty, value);
    }

    /// <summary>Filtros a la derecha del buscador (ComboBox, chips), separados 8.</summary>
    [Content]
    public object? Filtros
    {
        get => GetValue(FiltrosProperty);
        set => SetValue(FiltrosProperty, value);
    }
}
