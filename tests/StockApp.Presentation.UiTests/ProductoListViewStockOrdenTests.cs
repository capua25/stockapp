using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Presentation.Views.Catalogo;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Regresión (bug en producción): al ordenar la grilla de Productos por click en el encabezado
/// ("Costo" desc, "Stock") la columna Stock se dibujaba mal: el número quedaba corrido a la
/// izquierda o recortado (22 aparecía como "2").
///
/// Causa raíz: la celda de Stock llevaba un StackPanel con HorizontalAlignment="Right" (ajustado
/// al contenido) con el badge "Stock negativo" + el número. La DataGrid recicla las celdas al
/// reordenar; cuando una celda que mostraba un stock negativo (badge visible) pasa a mostrar uno
/// positivo (badge oculto), el contenedor conservaba el arreglo calculado para el valor anterior.
/// El árbol visual queda con tamaños inconsistentes, así que el guardián no mira el valor de
/// una celda aislada sino el invariante: TODOS los números de la columna terminan en el mismo X
/// y ninguno queda recortado, después de cada ordenamiento.
///
/// Se monta la View real y se ordena con click real de puntero (mismo mecanismo que
/// <see cref="DataGridSortClickTests"/>).
/// </summary>
public class ProductoListViewStockOrdenTests
{
    private static ProductoDto ProductoDe(int id, decimal costo, decimal stock) => new(
        Id: id, Codigo: $"C{id}", CodigoBarras: null, Nombre: $"Producto {id}", Descripcion: null,
        CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: 1,
        UnidadMedidaNombre: "Unidad", PrecioCosto: costo, StockActual: stock,
        StockMinimo: 0m, Activo: true, FechaAlta: DateTime.UtcNow);

    private static void ClickearHeader(Window window, DataGrid grid, string headerTexto)
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

    /// <summary>Un TextBlock por celda de Stock (el del número, que es el último del template).</summary>
    private static List<TextBlock> NumerosDeStock(DataGrid grid)
        => grid.GetVisualDescendants().OfType<DataGridCell>()
            .Where(c => c.Content is Grid or StackPanel)
            .Select(c => c.GetVisualDescendants().OfType<TextBlock>().Last())
            .ToList();

    private static void AssertNumerosAlineadosYSinRecorte(DataGrid grid, Window window, string etapa)
    {
        var numeros = NumerosDeStock(grid);
        Assert.NotEmpty(numeros);

        var recortados = numeros
            .Where(tb => tb.Bounds.Width < tb.DesiredSize.Width - 0.5)
            .Select(tb => $"'{tb.Text}' mide {tb.Bounds.Width:F1}px y necesita {tb.DesiredSize.Width:F1}px")
            .ToList();
        Assert.True(recortados.Count == 0, $"[{etapa}] números recortados: {string.Join("; ", recortados)}");

        var bordesDerechos = numeros
            .Select(tb => (Texto: tb.Text, X: tb.TranslatePoint(new Point(tb.Bounds.Width, 0), window)!.Value.X))
            .ToList();
        var esperado = bordesDerechos.Max(b => b.X);
        var corridos = bordesDerechos
            .Where(b => Math.Abs(b.X - esperado) > 0.5)
            .Select(b => $"'{b.Texto}' termina en x={b.X:F0} (el resto en x={esperado:F0})")
            .ToList();
        Assert.True(corridos.Count == 0, $"[{etapa}] números mal alineados: {string.Join("; ", corridos)}");
    }

    [AvaloniaFact]
    public void Ordenar_PorCostoYPorStock_MantieneAlineadosYSinRecorteLosNumerosDeStock()
    {
        var vista = new ProductoListView();
        var window = new Window { Width = 1700, Height = 900, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = vista.GetVisualDescendants().OfType<DataGrid>().Single();

        // Un producto con stock NEGATIVO arriba (badge visible) y el resto positivo, con stocks de
        // 1 y 2 dígitos y costos en distinto orden que el stock: al reordenar, las celdas recicladas
        // pasan de badge visible a oculto y de un ancho de número a otro.
        var stocks = new decimal[] { -5, 22, 8, 60, 45, 30, 9, 45, 18, 6, 5, 35, 28, 3, 40, 50, 0, 25, 4, 14, 20, 12 };
        var costos = new decimal[] { 1, 1200, 350, 900, 500, 500, 250, 800, 600, 1500, 400, 550, 1800, 2500, 300, 450, 123, 700, 1600, 3800, 900, 200 };
        grid.ItemsSource = new DataGridCollectionView(
            stocks.Select((s, i) => ProductoDe(i + 1, costos[i], s)).ToList());
        Dispatcher.UIThread.RunJobs();
        AssertNumerosAlineadosYSinRecorte(grid, window, "inicial");

        // Pasos del reporte: dos clicks en "Costo" (descendente), y también por "Stock".
        ClickearHeader(window, grid, "Costo");
        ClickearHeader(window, grid, "Costo");
        AssertNumerosAlineadosYSinRecorte(grid, window, "Costo descendente");

        ClickearHeader(window, grid, "Stock");
        AssertNumerosAlineadosYSinRecorte(grid, window, "Stock ascendente");
    }
}
