using System;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Views.Movimientos;
using Xunit;
using static StockApp.Presentation.UiTests.GrillaStockHelpers;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Misma regresión que <see cref="ProductoListViewStockOrdenTests"/> (causa raíz ahí) sobre la
/// grilla de Historial de movimientos: "STOCK ANT." y "STOCK NUEVO". "STOCK NUEVO" lleva el badge
/// "Stock negativo" de visibilidad condicional; "STOCK ANT." es un número suelto (se custodia
/// igual porque comparte el ordenamiento y el reciclaje de celdas).
/// </summary>
public class MovimientoHistorialViewStockOrdenTests
{
    private static MovimientoHistorialDto MovimientoDe(int id, decimal cantidad, decimal stockAnterior, decimal stockNuevo) => new(
        MovimientoId: id, ProductoId: id, ProductoNombre: $"Producto {id}",
        Tipo: TipoMovimiento.Entrada, Motivo: MotivoMovimiento.Compra,
        Cantidad: cantidad, PrecioUnitario: 10m, StockAnterior: stockAnterior, StockNuevo: stockNuevo,
        Comentario: null, Fecha: DateTime.UtcNow, UsuarioId: 1, UsuarioNombre: "admin");

    [AvaloniaFact]
    public void Ordenar_PorCantidadYPorColumnasDeStock_MantieneAlineadosYSinRecorteLosNumerosDeStock()
    {
        var vista = new MovimientoHistorialView();
        var window = new Window { Width = 1700, Height = 1600, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = vista.GetVisualDescendants().OfType<DataGrid>().Single();

        var cantidades = new decimal[] { 1, 1200, 350, 900, 500, 500, 250, 800, 600, 1500, 400, 550, 1800, 2500, 300, 450, 123, 700, 1600, 3800, 900, 12, 77 };
        grid.ItemsSource = new DataGridCollectionView(
            Stocks.Select((s, i) => MovimientoDe(i + 1, cantidades[i], Stocks[(i + 7) % Stocks.Length], s)).ToList());
        Dispatcher.UIThread.RunJobs();

        foreach (var columna in new[] { "STOCK ANT.", "STOCK NUEVO" })
            AssertNumerosAlineadosYSinRecorte(grid, window, columna, "inicial");
        AssertBadgeNoPisaElNumero(grid, window, "STOCK NUEVO", "inicial");

        ClickearHeader(window, grid, "CANTIDAD");
        ClickearHeader(window, grid, "CANTIDAD");
        foreach (var columna in new[] { "STOCK ANT.", "STOCK NUEVO" })
            AssertNumerosAlineadosYSinRecorte(grid, window, columna, "CANTIDAD descendente");

        foreach (var ordenar in new[] { "STOCK NUEVO", "STOCK ANT." })
        {
            ClickearHeader(window, grid, ordenar);
            foreach (var columna in new[] { "STOCK ANT.", "STOCK NUEVO" })
                AssertNumerosAlineadosYSinRecorte(grid, window, columna, $"{ordenar} ascendente");
            ClickearHeader(window, grid, ordenar);
            foreach (var columna in new[] { "STOCK ANT.", "STOCK NUEVO" })
                AssertNumerosAlineadosYSinRecorte(grid, window, columna, $"{ordenar} descendente");
            AssertBadgeNoPisaElNumero(grid, window, "STOCK NUEVO", $"{ordenar} descendente");
        }
    }
}
