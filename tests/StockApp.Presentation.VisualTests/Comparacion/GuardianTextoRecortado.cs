using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Guardián semántico de la comparación visual (spec §6.5): un TextBlock visible cuyo texto no
/// entra en el lugar que le asignó el layout sale recortado ("CÓDIG" por "CÓDIGO", Task 17). Un
/// glifo que falta son ~40-60 píxeles de tinta: pesa lo mismo que el ruido de antialiasing y
/// ninguna métrica de píxeles lo detecta sin rechazar también el ruido.
/// Recorta si el tamaño que el texto pide (sin restricción de ancho con NoWrap; con el ancho
/// asignado si hace wrap) supera el asignado. El recorte declarado (TextTrimming distinto de
/// None) es intencional y no se reporta.
/// Se llama DESPUÉS de capturar el PNG: vuelve a medir cada TextBlock y deja su layout inválido.
/// </summary>
public static class GuardianTextoRecortado
{
    // Redondeo de layout: un recorte real es al menos medio glifo (varios px).
    private const double Tolerancia = 0.5;

    public static IReadOnlyList<string> Buscar(Visual raiz)
    {
        var recortes = new List<string>();
        foreach (var texto in raiz.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!texto.IsEffectivelyVisible || string.IsNullOrEmpty(texto.Text) || texto.TextTrimming != TextTrimming.None)
            {
                continue;
            }

            var asignado = texto.Bounds.Size;
            var margen = texto.Margin;
            var anchoDisponible = texto.TextWrapping == TextWrapping.NoWrap
                ? double.PositiveInfinity
                : asignado.Width + margen.Left + margen.Right;
            texto.Measure(new Size(anchoDisponible, double.PositiveInfinity));
            var pedido = texto.DesiredSize.Deflate(margen);

            if (pedido.Width > asignado.Width + Tolerancia || pedido.Height > asignado.Height + Tolerancia)
            {
                recortes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"\"{texto.Text}\" pide {pedido.Width:0.#}x{pedido.Height:0.#} y tiene {asignado.Width:0.#}x{asignado.Height:0.#}"));
            }
        }

        return recortes;
    }
}
