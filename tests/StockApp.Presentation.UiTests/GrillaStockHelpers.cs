using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Piezas compartidas por los guardianes de "la columna de Stock se dibuja corrida o recortada
/// al ordenar" (Productos, Historial de movimientos, Stock por categoría, Valorización e
/// Historial por producto). Ver <see cref="ProductoListViewStockOrdenTests"/> para la causa raíz.
/// El invariante no mira el valor de una celda aislada: TODOS los números de la columna terminan
/// en el mismo X y ninguno queda recortado, después de cada ordenamiento.
/// </summary>
internal static class GrillaStockHelpers
{
    /// <summary>Click real de puntero sobre el encabezado de la columna (mismo mecanismo que DataGridSortClickTests).</summary>
    public static void ClickearHeader(Window window, DataGrid grid, string headerTexto)
    {
        Dispatcher.UIThread.RunJobs();
        var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .First(h => Equals(h.Content, headerTexto));
        var centro = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
        window.MouseMove(centro);
        window.MouseDown(centro, MouseButton.Left);
        window.MouseUp(centro, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Un TextBlock por celda de la columna (el del número, que es el último del template). La
    /// columna se identifica por el encabezado: la celda cuyo rango horizontal contiene el centro
    /// del header (DataGridCell.OwningColumn es interno).
    /// </summary>
    public static List<TextBlock> NumerosDeColumna(DataGrid grid, Window window, string headerTexto)
    {
        var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .First(h => Equals(h.Content, headerTexto));
        var centroX = header.TranslatePoint(new Point(header.Bounds.Width / 2, 0), window)!.Value.X;

        return grid.GetVisualDescendants().OfType<DataGridCell>()
            .Where(c =>
            {
                var x0 = c.TranslatePoint(new Point(0, 0), window)!.Value.X;
                return centroX >= x0 && centroX <= x0 + c.Bounds.Width;
            })
            .Select(c => c.GetVisualDescendants().OfType<TextBlock>().LastOrDefault())
            .Where(tb => tb is not null)
            .Select(tb => tb!)
            .ToList();
    }

    /// <summary>DesiredSize incluye el Margin del TextBlock; Bounds no, así que se descuenta.</summary>
    private static double AnchoNecesario(TextBlock tb) => tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right;

    public static void AssertNumerosAlineadosYSinRecorte(
        DataGrid grid, Window window, string headerTexto, string etapa)
    {
        var numeros = NumerosDeColumna(grid, window, headerTexto);
        Assert.NotEmpty(numeros);

        var recortados = numeros
            .Where(tb => tb.Bounds.Width < AnchoNecesario(tb) - 0.5)
            .Select(tb => $"'{tb.Text}' mide {tb.Bounds.Width:F1}px y necesita {AnchoNecesario(tb):F1}px")
            .ToList();
        Assert.True(recortados.Count == 0,
            $"[{headerTexto} / {etapa}] números recortados: {string.Join("; ", recortados)}");

        var bordesDerechos = numeros
            .Select(tb => (Texto: tb.Text, X: tb.TranslatePoint(new Point(tb.Bounds.Width, 0), window)!.Value.X))
            .ToList();
        var esperado = bordesDerechos.Max(b => b.X);
        var corridos = bordesDerechos
            .Where(b => Math.Abs(b.X - esperado) > 0.5)
            .Select(b => $"'{b.Texto}' termina en x={b.X:F0} (el resto en x={esperado:F0})")
            .ToList();
        Assert.True(corridos.Count == 0,
            $"[{headerTexto} / {etapa}] números mal alineados: {string.Join("; ", corridos)}");
    }

    // Stocks de 1 y 2 dígitos (y uno negativo arriba, con badge visible) y otro orden que los costos:
    // al reordenar, las celdas recicladas pasan de badge visible a oculto y de un ancho de número a otro.
    // El último (-99999,5) es el PEOR CASO que el ancho fijo de la columna tiene que absorber: badge
    // "Stock negativo" (102px) + número de 5 dígitos con decimal (62px) + margen y padding de celda.
    public static readonly decimal[] Stocks =
        { -5, 22, 8, 60, 45, 30, 9, 45, 18, 6, 5, 35, 28, 3, 40, 50, 0, 25, 4, 14, 20, 12, -99999.5m };

    /// <summary>
    /// El badge "Stock negativo" no pisa al número: en una celda de Grid compartida (badge a la
    /// izquierda, número a la derecha) un ancho insuficiente los superpone en vez de recortar.
    /// </summary>
    public static void AssertBadgeNoPisaElNumero(DataGrid grid, Window window, string headerTexto, string etapa)
    {
        var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .First(h => Equals(h.Content, headerTexto));
        var centroX = header.TranslatePoint(new Point(header.Bounds.Width / 2, 0), window)!.Value.X;

        var pisados = new List<string>();
        var celdasConBadge = 0;
        foreach (var celda in grid.GetVisualDescendants().OfType<DataGridCell>())
        {
            var x0 = celda.TranslatePoint(new Point(0, 0), window)!.Value.X;
            if (centroX < x0 || centroX > x0 + celda.Bounds.Width) continue;

            var badge = celda.GetVisualDescendants().OfType<BadgeEstado>().FirstOrDefault(b => b.IsVisible);
            var numero = celda.GetVisualDescendants().OfType<TextBlock>().LastOrDefault();
            if (badge is null || numero is null) continue;
            celdasConBadge++;

            var derechaBadge = badge.TranslatePoint(new Point(badge.Bounds.Width, 0), window)!.Value.X;
            var izquierdaNumero = numero.TranslatePoint(new Point(0, 0), window)!.Value.X;
            if (derechaBadge > izquierdaNumero + 0.5)
                pisados.Add($"badge termina en x={derechaBadge:F0} y '{numero.Text}' empieza en x={izquierdaNumero:F0}");
        }
        Assert.True(celdasConBadge > 0, $"[{headerTexto} / {etapa}] no hay ninguna celda con badge visible: el test no custodia nada.");
        Assert.True(pisados.Count == 0, $"[{headerTexto} / {etapa}] el badge pisa al número: {string.Join("; ", pisados)}");
    }
}
