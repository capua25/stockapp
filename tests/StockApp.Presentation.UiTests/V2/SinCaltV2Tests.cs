using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.Views.Catalogo;
using Avalonia.Headless.XUnit;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Toda la UI v2 se dibuja sin la alternativa contextual calt de Inter (que cambia la "x" entre dígitos por
/// "×"): la comparación por teselas no la ve (son pocos píxeles), así que se fija el rasgo EFECTIVO de los
/// TextBlock reales de una vista real y de los popups, que no cuelgan del árbol con .ui2.
/// </summary>
public class SinCaltV2Tests
{
    private sealed class ProductosFake : IProductoService
    {
        public List<ProductoDto> Lista { get; set; } = new();

        public Task<int> AltaAsync(Producto producto) => throw new NotSupportedException();
        public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => Task.CompletedTask;

        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) =>
            Task.FromResult<IReadOnlyList<ProductoDto>>(Lista.ToList());

        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) =>
            Task.FromResult<IReadOnlyList<ProductoDto>>(Lista.ToList());
    }

    private static ProductoDto P(int id, string nombre, decimal stock) => new(
        id, $"C-{id:000}", null, nombre, null, 1, "Construcción", null, 1, "Unidad", 100m + id, stock, 0m, true,
        new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private static bool CaltApagado(AvaloniaObjectLike c) => c.Rasgos is { } r && r.Any(f => f.Tag == "calt" && f.Value == 0);

    private readonly record struct AvaloniaObjectLike(FontFeatureCollection? Rasgos);

    private static AvaloniaObjectLike Rasgos(Avalonia.AvaloniaObject o) => new(o.GetValue(TextElement.FontFeaturesProperty));

    [AvaloniaFact]
    public void TextoDeCeldasDeUnaVistaV2_TieneCaltApagado_YLasCifrasTabularesSiguen()
    {
        var servicio = new ProductosFake { Lista = [P(1, "Cable 2x1,5 mm", 12m)] };
        var vista = new ProductoListView { DataContext = new ProductoListViewModel(servicio, new NavigationRecorderFake(), new ConfirmacionServiceFake()) };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var nombre = vista.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Cable 2x1,5 mm");
        Assert.True(CaltApagado(Rasgos(nombre)), "El TextBlock de NOMBRE (\"2x1,5\") se dibuja con calt: saldría \"2×1,5\".");

        var stock = vista.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "12");
        Assert.True(CaltApagado(Rasgos(stock)), "La celda numérica reemplazó la colección y perdió -calt.");
        Assert.Contains(Rasgos(stock).Rasgos!, f => f.Tag == "tnum" && f.Value == 1);

        var caja = vista.GetVisualDescendants().OfType<TextBox>().First();
        Assert.True(CaltApagado(Rasgos(caja)), "El buscador (TextBox) se dibuja con calt.");
    }

    [AvaloniaFact]
    public void ToolTipYFlyout_FueraDeUi2_TambienSinCalt()
    {
        var tip = new ToolTip { Content = "Cable 2x1,5 mm" };
        var flyout = new FlyoutPresenter { Content = "Cable 2x1,5 mm" };
        var ventana = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { tip, flyout } } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(CaltApagado(Rasgos(tip)), "El ToolTip (popup fuera de .ui2) se dibuja con calt.");
        Assert.True(CaltApagado(Rasgos(flyout)), "El FlyoutPresenter (popup fuera de .ui2) se dibuja con calt.");
    }
}
