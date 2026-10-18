using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.UiTests.V2;
using StockApp.Presentation.Views.Reportes;
using Xunit;
using static StockApp.Presentation.UiTests.GrillaStockHelpers;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Misma regresión que <see cref="ProductoListViewStockOrdenTests"/> (causa raíz ahí) sobre las
/// tres grillas de Reportes con columna de Stock con badge "Stock negativo": Stock por categoría
/// ("Stock Total"), Valorización ("Stock") e Historial por producto ("Stock Nuevo", más "Stock
/// Ant." que es un número suelto).
///
/// Estos VMs exponen <c>Items</c> como <c>IReadOnlyList</c> y NO como <c>DataGridCollectionView</c>,
/// así que se asigna la lista cruda igual que hace el binding real (ver
/// <see cref="ReportesAlineacionNumericaTests"/>). Los tests se montan con la View real, miden el
/// estado inicial (el badge de la fila negativa ya descuadra el ancho Auto de la columna) y
/// después hacen click real en los encabezados.
/// </summary>
public class ReportesStockOrdenTests
{
    private static Window Montar(Control vista)
    {
        var window = new Window { Width = 1700, Height = 1600, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void OrdenarYVerificar(Window window, DataGrid grid, string columnaStock, string otraColumna, params string[] columnasAVerificar)
    {
        foreach (var c in columnasAVerificar)
            AssertNumerosAlineadosYSinRecorte(grid, window, c, "inicial");
        AssertBadgeNoPisaElNumero(grid, window, columnaStock, "inicial");

        var pasos = new[] { otraColumna, otraColumna, columnaStock, columnaStock };
        for (var i = 0; i < pasos.Length; i++)
        {
            ClickearHeader(window, grid, pasos[i]);
            foreach (var c in columnasAVerificar)
                AssertNumerosAlineadosYSinRecorte(grid, window, c, $"click {i + 1} en '{pasos[i]}'");
            AssertBadgeNoPisaElNumero(grid, window, columnaStock, $"click {i + 1} en '{pasos[i]}'");
        }
    }

    [AvaloniaFact]
    public void StockCategoriaView_Ordenar_MantieneAlineadosYSinRecorteLosNumerosDeStockTotal()
    {
        var valores = new decimal[] { 1, 1200, 350, 900, 500, 500, 250, 800, 600, 1500, 400, 550, 1800, 2500, 300, 450, 123, 700, 1600, 3800, 900, 12, 77 };
        var vista = ReportesPrecargados.StockCategoria(Stocks
            .Select((s, i) => new StockCategoriaDto($"Categoría {i + 1}", i + 1, s, valores[i])).ToList());
        var window = Montar(vista);
        var grid = vista.GetVisualDescendants().OfType<DataGrid>().Single();

        OrdenarYVerificar(window, grid, "STOCK TOTAL", "VALOR COSTO", "STOCK TOTAL");
    }

    [AvaloniaFact]
    public void ValorizacionView_Ordenar_MantieneAlineadosYSinRecorteLosNumerosDeStock()
    {
        var costos = new decimal[] { 1, 1200, 350, 900, 500, 500, 250, 800, 600, 1500, 400, 550, 1800, 2500, 300, 450, 123, 700, 1600, 3800, 900, 12, 77 };
        var vista = ReportesPrecargados.Valorizacion(Stocks
            .Select((s, i) => new ValorizacionItemDto(i + 1, $"C{i + 1}", $"Producto {i + 1}", "Categoría", s, costos[i], s * costos[i])).ToList());
        var window = Montar(vista);
        var grid = vista.GetVisualDescendants().OfType<DataGrid>().Single();

        OrdenarYVerificar(window, grid, "STOCK", "P. COSTO", "STOCK");
    }

    [AvaloniaFact]
    public void HistorialPorProductoView_Ordenar_MantieneAlineadosYSinRecorteLosNumerosDeStock()
    {
        var cantidades = new decimal[] { 1, 1200, 350, 900, 500, 500, 250, 800, 600, 1500, 400, 550, 1800, 2500, 300, 450, 123, 700, 1600, 3800, 900, 12, 77 };
        var vista = ReportesPrecargados.HistorialPorProducto(Stocks
            .Select((s, i) => new MovimientoHistorialDto(
                MovimientoId: i + 1, ProductoId: 1, ProductoNombre: "Producto",
                Tipo: TipoMovimiento.Entrada, Motivo: MotivoMovimiento.Compra,
                Cantidad: cantidades[i], PrecioUnitario: 10m,
                StockAnterior: Stocks[(i + 7) % Stocks.Length], StockNuevo: s,
                Comentario: null, Fecha: DateTime.UtcNow, UsuarioId: 1, UsuarioNombre: "admin")).ToList());
        var window = Montar(vista);
        var grid = vista.GetVisualDescendants().OfType<DataGrid>().Single();

        OrdenarYVerificar(window, grid, "STOCK NUEVO", "CANTIDAD", "STOCK ANT.", "STOCK NUEVO");
    }
}
