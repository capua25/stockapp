using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.Views.Movimientos;
using StockApp.Presentation.Views.Reportes;
using Xunit;
using static StockApp.Presentation.UiTests.GrillaStockHelpers;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Regresión: al ordenar por encabezado, los números de las columnas <c>CellStyleClasses="num"</c>
/// perdían el último carácter ("75" se veía "7", "$ 450,00" se veía "$ 450,0(") aunque la
/// columna tenía ancho de sobra.
///
/// Causa raíz: el estilo global <c>DataGridCell.num</c> ponía <c>HorizontalContentAlignment=Right</c>,
/// que el template de la celda pasa como <c>HorizontalAlignment</c> del ContentPresenter: el
/// presenter quedaba AJUSTADO al contenido. Al reciclar la celda, el texto nuevo cambia el
/// DesiredSize del presenter durante el measure de la grilla (la grilla re-mide las celdas Auto con
/// otro constraint en cada pasada, así que el cambio sube "por adentro" del measure del padre y
/// nadie invalida el arrange del presenter). Después, <c>Layoutable.Arrange</c> recibe el MISMO
/// rectángulo de siempre y se saltea: el presenter conserva el ancho del valor anterior y recorta
/// el número nuevo. Es el mismo mecanismo que el StackPanel Right de la columna Stock
/// (<see cref="ProductoListViewStockOrdenTests"/>).
///
/// Invariante: ningún TextBlock de ninguna celda queda arreglado más angosto que su propio
/// DesiredSize (que ya viene acotado por el ancho de la columna, así que una columna angosta no
/// lo dispara: eso es otro problema).
/// </summary>
public class DataGridCeldaNumericaOrdenTests
{
    private static void AssertSinTextosConArregloViejo(DataGrid grid, string etapa)
    {
        var recortados = grid.GetVisualDescendants().OfType<DataGridCell>()
            .SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>())
            .Where(tb => tb.IsEffectivelyVisible && !string.IsNullOrEmpty(tb.Text))
            .Where(tb => tb.Bounds.Width < tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right - 0.5)
            .Select(tb => $"'{tb.Text}' mide {tb.Bounds.Width:F1}px y necesita {tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right:F1}px")
            .ToList();
        Assert.True(recortados.Count == 0, $"[{etapa}] textos recortados: {string.Join("; ", recortados)}");
    }

    private static Window Montar(Control vista)
    {
        var window = new Window { Width = 1700, Height = 1600, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void OrdenarYVerificar(Window window, DataGrid grid, params string[] headers)
    {
        AssertSinTextosConArregloViejo(grid, "inicial");
        foreach (var header in headers)
        {
            ClickearHeader(window, grid, header);
            AssertSinTextosConArregloViejo(grid, $"click en '{header}'");
        }
    }

    // Cantidades y precios de 1 a 4 dígitos en distinto orden: al reordenar, una celda reciclada
    // pasa de un número angosto ("8") a uno más ancho ("75"), que es lo que se veía recortado.
    private static readonly decimal[] Cantidades = { 75, 8, 6, 55, 50, 45, 45, 45, 40, 40, 40, 38, 35, 32, 30, 30, 30, 30, 11, 9, 18, 12, 1 };
    private static readonly decimal[] Precios = { 100, 100, 100, 100, 100, 100, 100, 480, 250, 600, 300, 2800, 500, 450, 900, 550, 1800, 800, 700, 300, 900, 250, 3800 };

    private static MovimientoHistorialDto MovimientoDe(int i) => new(
        MovimientoId: i, ProductoId: i, ProductoNombre: $"Producto {i}",
        Tipo: i % 3 == 0 ? TipoMovimiento.Salida : TipoMovimiento.Entrada, Motivo: MotivoMovimiento.Compra,
        Cantidad: Cantidades[i], PrecioUnitario: Precios[i], StockAnterior: 0m, StockNuevo: Cantidades[i],
        Comentario: null, Fecha: DateTime.UtcNow, UsuarioId: 1, UsuarioNombre: "admin");

    [AvaloniaFact]
    public void MovimientoHistorialView_Ordenar_NoRecortaLosNumerosDeCantidadNiPrecio()
    {
        var vista = new MovimientoHistorialView();
        var window = Montar(vista);
        var grid = vista.GetVisualDescendants().OfType<DataGrid>().Single();

        grid.ItemsSource = new DataGridCollectionView(Enumerable.Range(0, Cantidades.Length).Select(MovimientoDe).ToList());
        Dispatcher.UIThread.RunJobs();

        OrdenarYVerificar(window, grid, "Cantidad", "Cantidad", "Precio", "Precio", "Tipo", "Tipo");
    }

    [AvaloniaFact]
    public void HistorialPorProductoView_Ordenar_NoRecortaLosNumerosDeCantidadNiPrecio()
    {
        var vista = new HistorialPorProductoView();
        var window = Montar(vista);
        var grid = vista.GetVisualDescendants().OfType<DataGrid>().Single();

        grid.ItemsSource = (IReadOnlyList<MovimientoHistorialDto>)Enumerable.Range(0, Cantidades.Length).Select(MovimientoDe).ToList();
        Dispatcher.UIThread.RunJobs();

        OrdenarYVerificar(window, grid, "Cantidad", "Cantidad", "P. Unitario", "P. Unitario", "Stock Nuevo");
    }

    [AvaloniaFact]
    public void ValorizacionView_Ordenar_NoRecortaLosImportes()
    {
        var vista = new ValorizacionView();
        var window = Montar(vista);
        var grid = vista.GetVisualDescendants().OfType<DataGrid>().Single();

        grid.ItemsSource = (IReadOnlyList<ValorizacionItemDto>)Enumerable.Range(0, Cantidades.Length)
            .Select(i => new ValorizacionItemDto(i, $"C{i}", $"Producto {i}", "Categoría", Cantidades[i], Precios[i], Cantidades[i] * Precios[i]))
            .ToList();
        Dispatcher.UIThread.RunJobs();

        OrdenarYVerificar(window, grid, "P. Costo", "P. Costo", "Valor Costo", "Valor Costo", "Stock");
    }
}
