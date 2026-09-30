using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Guardián semántico de la comparación visual (spec §6.5): un TextBlock visible cuyo texto no se
/// ve entero ("CÓDIG" por "CÓDIGO", Task 17). Un glifo que falta son ~40-60 píxeles de tinta: pesa
/// lo mismo que el ruido de antialiasing y ninguna métrica de píxeles lo detecta sin rechazar
/// también el ruido. Dos formas de recorte:
/// <list type="number">
/// <item>Propio: el tamaño que el texto pide (sin restricción de ancho con NoWrap; con el ancho
/// asignado si hace wrap) supera el que le asignó el layout.</item>
/// <item>Por un ancestro: el texto tiene todo su lugar (en un StackPanel horizontal el Bounds
/// siempre coincide con lo que pide) pero cae fuera de un ancestro con ClipToBounds o de la
/// ventana. El viewport de un ScrollViewer no cuenta en el eje en que se puede scrollear: el texto
/// sigue alcanzable.</item>
/// </list>
/// TextTrimming NO exime: un texto con "..." que efectivamente recorta se reporta, salvo que el
/// test lo permita por su texto (alineado con un mockup que use text-overflow: ellipsis).
/// Se llama con la ventana todavía abierta y DESPUÉS de capturar el PNG: vuelve a medir cada
/// TextBlock y deja su layout inválido.
/// </summary>
public static class GuardianTextoRecortado
{
    // Redondeo de layout: un recorte real es al menos medio glifo (varios px).
    private const double Tolerancia = 0.5;

    public static IReadOnlyList<string> Buscar(Visual raiz, IReadOnlyCollection<string>? recortesPermitidos = null)
    {
        var recortes = new List<string>();
        foreach (var texto in raiz.GetVisualDescendants().OfType<TextBlock>().ToList())
        {
            if (!texto.IsEffectivelyVisible || string.IsNullOrEmpty(texto.Text)
                || recortesPermitidos?.Contains(texto.Text) == true)
            {
                continue;
            }

            var asignado = texto.Bounds.Size;
            var margen = texto.Margin;
            var anchoDisponible = texto.TextWrapping == TextWrapping.NoWrap
                ? double.PositiveInfinity
                : asignado.Width + margen.Left + margen.Right;
            var pedido = MedirSinTamanoFijo(texto, new Size(anchoDisponible, double.PositiveInfinity)).Deflate(margen);

            if (pedido.Width > asignado.Width + Tolerancia || pedido.Height > asignado.Height + Tolerancia)
            {
                recortes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"\"{texto.Text}\" pide {pedido.Width:0.#}x{pedido.Height:0.#} y tiene {asignado.Width:0.#}x{asignado.Height:0.#}"));
                continue;
            }

            var recortador = BuscarRecortador(texto, RectDelTexto(texto, pedido));
            if (recortador is not null)
            {
                recortes.Add($"\"{texto.Text}\" queda fuera de {recortador}");
            }
        }

        return recortes;
    }

    // Un Width o MaxWidth fijo en el TextBlock también limita su Measure: el texto pediría lo que
    // tiene y un recorte quedaría invisible. Se mide sin ellos (prioridad Animation, por encima de
    // valores locales y estilos) y se restauran al terminar.
    private static Size MedirSinTamanoFijo(TextBlock texto, Size disponible)
    {
        using var ancho = texto.SetValue(Layoutable.WidthProperty, double.NaN, BindingPriority.Animation);
        using var anchoMax = texto.SetValue(Layoutable.MaxWidthProperty, double.PositiveInfinity, BindingPriority.Animation);
        using var alto = texto.SetValue(Layoutable.HeightProperty, double.NaN, BindingPriority.Animation);
        using var altoMax = texto.SetValue(Layoutable.MaxHeightProperty, double.PositiveInfinity, BindingPriority.Animation);
        texto.Measure(disponible);
        return texto.DesiredSize;
    }

    // Donde se dibuja el texto dentro del TextBlock: puede ser más ancho que el texto (Stretch).
    private static Rect RectDelTexto(TextBlock texto, Size pedido)
    {
        var asignado = texto.Bounds.Size;
        var ancho = Math.Min(pedido.Width, asignado.Width);
        var libre = asignado.Width - ancho;
        var x = texto.TextAlignment switch
        {
            TextAlignment.Right or TextAlignment.End => libre,
            TextAlignment.Center => libre / 2,
            _ => 0,
        };
        return new Rect(x, 0, ancho, Math.Min(pedido.Height, asignado.Height));
    }

    private static string? BuscarRecortador(TextBlock texto, Rect rectLocal)
    {
        var raizVisual = TopLevel.GetTopLevel(texto);
        if (raizVisual is null || texto.TransformToVisual(raizVisual) is not { } aRaiz)
        {
            return null;
        }

        var rect = rectLocal.TransformToAABB(aRaiz);
        var horizontalFijo = true;
        var verticalFijo = true;
        foreach (var ancestro in texto.GetVisualAncestors())
        {
            if (ancestro == raizVisual)
            {
                break;
            }

            if (ancestro is ScrollContentPresenter { TemplatedParent: ScrollViewer scroll })
            {
                // Lo que el viewport recorta en un eje desplazable se alcanza scrolleando; lo de
                // afuera (la ventana incluida) tampoco decide en ese eje.
                horizontalFijo &= scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled;
                verticalFijo &= scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled;
            }

            if (ancestro.ClipToBounds && ancestro.TransformToVisual(raizVisual) is { } ancestroARaiz
                && !Contiene(new Rect(ancestro.Bounds.Size).TransformToAABB(ancestroARaiz), rect, horizontalFijo, verticalFijo))
            {
                return ancestro.GetType().Name + (string.IsNullOrEmpty((ancestro as Control)?.Name) ? "" : "#" + ((Control)ancestro).Name);
            }
        }

        return Contiene(new Rect(raizVisual.ClientSize), rect, horizontalFijo, verticalFijo) ? null : "la ventana";
    }

    private static bool Contiene(Rect caja, Rect rect, bool horizontal, bool vertical) =>
        (!horizontal || (rect.Left >= caja.Left - Tolerancia && rect.Right <= caja.Right + Tolerancia))
        && (!vertical || (rect.Top >= caja.Top - Tolerancia && rect.Bottom <= caja.Bottom + Tolerancia));
}
