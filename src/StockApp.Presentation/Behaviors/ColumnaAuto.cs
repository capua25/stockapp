using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.VisualTree;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Columna Auto de contenido libre que mide su ancho sobre TODAS las filas de la fuente, no solo las que la
/// virtualización realizó. Sin esto una Auto crece cuando aparece al scrollear una fila más ancha y mueve a las
/// demás (en Productos: CÓDIGO 65 a 140, COSTO 100 a 115, UNIDAD 63 a 97).
///
/// <para>Ancho = el mayor entre el texto más ancho de la fuente más el padding de la celda, y el título más el
/// padding del header más <see cref="ReservaOrden"/> (así ordenar no corta el título); redondeado hacia arriba,
/// nunca menor que el MinWidth de la columna ni mayor que <see cref="TopeProperty"/>. Lo que pasa el tope se
/// corta con elipsis (TextTrimming en la celda) y se lee en el tooltip (<see cref="TooltipSiSeCorta"/>).</para>
///
/// <para>El texto de cada fila es el mismo que pinta la celda: una DataGridTextColumn usa su Binding; una de
/// plantilla, <see cref="RutaProperty"/> (por defecto su SortMemberPath) con <see cref="ConversorProperty"/>,
/// que tiene que ser el mismo conversor de la plantilla. La tipografía se copia del TextBlock de una celda
/// realizada de esa columna (incluye los FontFeatures: -calt, y +tnum en las numéricas). Lo dispara
/// <see cref="DataGridColumnasAutoBehavior"/> al cambiar los datos, que además cede ante un arrastre del usuario.</para>
/// </summary>
public static class ColumnaAuto
{
    /// <summary>Tope por defecto de una columna medida: más que esto se corta con elipsis.</summary>
    public const double TopePorDefecto = 240;

    /// <summary>Lugar para la flecha de orden del header v2: margen 8 + glifo 12 (Themes/V2/DataGridV2.axaml).</summary>
    public const double ReservaOrden = 20;

    public static readonly AttachedProperty<bool> MedirProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, DataGridColumn, bool>("Medir");

    public static readonly AttachedProperty<string?> RutaProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, DataGridColumn, string?>("Ruta");

    public static readonly AttachedProperty<IValueConverter?> ConversorProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, DataGridColumn, IValueConverter?>("Conversor");

    public static readonly AttachedProperty<double> TopeProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, DataGridColumn, double>("Tope", TopePorDefecto);

    public static bool GetMedir(DataGridColumn c) => c.GetValue(MedirProperty);
    public static void SetMedir(DataGridColumn c, bool v) => c.SetValue(MedirProperty, v);
    public static string? GetRuta(DataGridColumn c) => c.GetValue(RutaProperty);
    public static void SetRuta(DataGridColumn c, string? v) => c.SetValue(RutaProperty, v);
    public static IValueConverter? GetConversor(DataGridColumn c) => c.GetValue(ConversorProperty);
    public static void SetConversor(DataGridColumn c, IValueConverter? v) => c.SetValue(ConversorProperty, v);
    public static double GetTope(DataGridColumn c) => c.GetValue(TopeProperty);
    public static void SetTope(DataGridColumn c, double v) => c.SetValue(TopeProperty, v);

    /// <summary>La colección que se mide: la SourceCollection de un DataGridCollectionView (sin su filtro ni su
    /// orden), o la fuente tal cual.</summary>
    internal static IEnumerable? Fuente(IEnumerable? itemsSource) =>
        itemsSource is DataGridCollectionView vista ? vista.SourceCollection : itemsSource;

    /// <summary>Si la fuente tiene alguna fila, sin recorrerla entera.</summary>
    internal static bool HayFilas(IEnumerable? itemsSource) =>
        Fuente(itemsSource)?.Cast<object?>().Any(o => o is not null) == true;

    /// <summary>Todas las filas de la fuente, sin el filtro de un DataGridCollectionView.</summary>
    internal static IReadOnlyList<object> Items(IEnumerable? itemsSource)
    {
        var todas = Fuente(itemsSource);
        return todas is null ? [] : todas.Cast<object?>().Where(o => o is not null).Select(o => o!).ToList();
    }

    /// <summary>El texto que pinta la celda de <paramref name="item"/> en esa columna.</summary>
    internal static string Texto(DataGridColumn columna, object item) => Evaluador(columna)(item);

    private static Func<object, string> Evaluador(DataGridColumn columna)
    {
        if (GetRuta(columna) is null && columna is DataGridBoundColumn { Binding: { } binding })
        {
            var evaluador = new TextBlock();
            evaluador.Bind(TextBlock.TextProperty, binding);
            return item =>
            {
                evaluador.DataContext = item;
                return evaluador.Text ?? string.Empty;
            };
        }

        var ruta = GetRuta(columna) ?? columna.SortMemberPath;
        if (string.IsNullOrEmpty(ruta))
            throw new InvalidOperationException(
                $"La columna '{columna.Header}' mide su contenido pero no tiene Ruta, SortMemberPath ni Binding.");
        var partes = ruta.Split('.');
        var conversor = GetConversor(columna);
        var cache = new Dictionary<(Type, string), PropertyInfo?>();
        return item =>
        {
            object? valor = item;
            foreach (var parte in partes)
            {
                if (valor is null) break;
                var clave = (valor.GetType(), parte);
                if (!cache.TryGetValue(clave, out var propiedad))
                    cache[clave] = propiedad = valor.GetType().GetProperty(parte);
                valor = propiedad?.GetValue(valor);
            }
            var texto = conversor is null ? valor : conversor.Convert(valor, typeof(string), null, CultureInfo.CurrentCulture);
            return texto?.ToString() ?? string.Empty;
        };
    }

    /// <summary>
    /// Ancho de la columna para las filas que devuelve <paramref name="items"/>, o null si todavía no se puede medir.
    /// <paramref name="nuncaMedible"/> distingue el porqué: true si con esta configuración no se va a poder medir
    /// nunca (Header vacío o no textual, Header repetido entre las columnas visibles, plantilla sin TextBlock
    /// visible); false si falta que la grilla realice el header o una celda de esa columna (primera pasada, grilla de
    /// alto 0). Las filas se piden recién cuando hay con qué medirlas.
    /// </summary>
    internal static double? Medir(DataGrid grid, DataGridColumn columna, Func<IReadOnlyList<object>> items, out bool nuncaMedible)
    {
        // Con el Header repetido no hay cómo saber cuál de los headers (y la celda debajo) es el de esta columna:
        // se mediría con la tipografía y el padding de la otra.
        nuncaMedible = grid.Columns.Count(c => c.IsVisible && Equals(c.Header, columna.Header)) > 1;
        if (nuncaMedible)
            return null;

        var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => h.IsEffectivelyVisible && h.Bounds.Width > 0 && Equals(h.Content, columna.Header));
        var celda = header is null ? null : CeldaBajo(grid, header);
        if (header is null || celda is null)
            return null;

        var textoCelda = celda.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.IsEffectivelyVisible);
        var textoHeader = header.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text));
        if (textoCelda is null || textoHeader is null)
        {
            nuncaMedible = true;
            return null;
        }

        var sonda = Sonda(textoCelda);
        var texto = Evaluador(columna);
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var maximo = 0.0;
        foreach (var item in items())
        {
            var t = texto(item);
            if (!vistos.Add(t))
                continue;
            sonda.Text = t;
            sonda.Measure(Size.Infinity);
            maximo = Math.Max(maximo, sonda.DesiredSize.Width);
        }
        var contenido = maximo + celda.Padding.Left + celda.Padding.Right + textoCelda.Margin.Left + textoCelda.Margin.Right;

        var sondaHeader = Sonda(textoHeader);
        sondaHeader.Text = textoHeader.Text;
        sondaHeader.Measure(Size.Infinity);
        var titulo = sondaHeader.DesiredSize.Width + header.Padding.Left + header.Padding.Right + ReservaOrden;

        var minimo = columna.MinWidth > 0 ? columna.MinWidth : grid.MinColumnWidth;
        var ancho = Math.Min(GetTope(columna), Math.Ceiling(Math.Max(contenido, titulo)));
        return Math.Max(minimo, ancho);
    }

    /// <summary>La primera celda realizada que está debajo del centro del header (DataGridCell.OwningColumn es interno).</summary>
    private static DataGridCell? CeldaBajo(DataGrid grid, DataGridColumnHeader header)
    {
        var centro = header.TranslatePoint(new Point(header.Bounds.Width / 2, 0), grid)?.X;
        if (centro is null)
            return null;
        return grid.GetVisualDescendants().OfType<DataGridCell>().FirstOrDefault(c =>
        {
            if (!c.IsEffectivelyVisible || c.Bounds.Width <= 0)
                return false;
            var x0 = c.TranslatePoint(default, grid)?.X;
            return x0 is not null && x0 <= centro && centro <= x0 + c.Bounds.Width;
        });
    }

    /// <summary>TextBlock suelto con la tipografía del modelo: un control oculto no mide, y los estilos de celda
    /// necesitan una DataGridCell real como ancestro, así que se copia lo que ya resolvió el tema.</summary>
    private static TextBlock Sonda(TextBlock modelo) => new()
    {
        FontFamily = modelo.FontFamily,
        FontSize = modelo.FontSize,
        FontWeight = modelo.FontWeight,
        FontStyle = modelo.FontStyle,
        FontStretch = modelo.FontStretch,
        LetterSpacing = modelo.LetterSpacing,
        FontFeatures = modelo.FontFeatures,
    };
}
