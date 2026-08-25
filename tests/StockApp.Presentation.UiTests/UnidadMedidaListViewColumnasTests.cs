using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Domain.Entities;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Migración mecánica de UnidadMedidaListView.axaml de ListBox a DataGrid (2026-08-24), mismo
/// molde que ProveedorListView.axaml pero sin columnas nuevas -- solo Nombre/Abreviatura, que ya
/// entraban en el ListBox. Guardián de no-regresión: columnas visibles, badge "Inactiva" y
/// SelectedItem siguen funcionando igual que con el ListBox.
/// </summary>
public class UnidadMedidaListViewColumnasTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:cat="clr-namespace:StockApp.Presentation.Views.Catalogo;assembly=GestionMunicipal"
                Width="800" Height="500">
            <cat:UnidadMedidaListView />
        </Window>
        """;

    private static (Window Window, UnidadMedidaListViewModel Vm) Montar(IReadOnlyList<UnidadMedida> unidades)
    {
        var vm = new UnidadMedidaListViewModel(
            new UnidadMedidaServiceFake(unidades),
            new NavigationServiceFake(),
            new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_UnidadActiva_MuestraNombreYAbreviatura()
    {
        var unidad = new UnidadMedida { Id = 1, Nombre = "Kilogramo", Abreviatura = "kg", Activo = true };
        var (window, _) = Montar(new[] { unidad });

        var textos = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(tb => ReferenceEquals(tb.DataContext, unidad))
            .Select(tb => tb.Text)
            .ToList();

        Assert.Contains("Kilogramo", textos);
        Assert.Contains("kg", textos);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Montar_UnidadInactiva_MuestraBadgeInactivaYAtenuaLasCeldas()
    {
        var unidad = new UnidadMedida { Id = 2, Nombre = "Litro", Abreviatura = "l", Activo = false };
        var (window, _) = Montar(new[] { unidad });

        var textoNombre = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(tb => ReferenceEquals(tb.DataContext, unidad) && tb.Text == "Litro");
        Assert.True(textoNombre.Opacity < 1.0);

        var badge = window.GetVisualDescendants()
            .OfType<StockApp.Presentation.Controls.BadgeEstado>()
            .Single(b => ReferenceEquals(b.DataContext, unidad));
        Assert.True(badge.IsVisible);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Montar_SeleccionarFilaEnGrilla_ActualizaItemSeleccionadoEnElViewModel()
    {
        var unidad = new UnidadMedida { Id = 3, Nombre = "Metro", Abreviatura = "m", Activo = true };
        var (window, vm) = Montar(new[] { unidad });

        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.SelectedItem = unidad;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(unidad, vm.ItemSeleccionado);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
