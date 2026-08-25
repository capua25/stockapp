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
/// Cubre la migración de ProveedorListView.axaml de ListBox a DataGrid (2026-08-24): el ListBox
/// simulaba columnas con un StackPanel de ancho fijo (Nombre 250 + Teléfono 120 + Email 200 =
/// 570 de 620 disponibles) y no dejaba lugar para Dirección/Notas -- pedido original del
/// usuario ("no muestra ni las notas ni la dirección"). Ambas propiedades ya existían en
/// <see cref="Proveedor"/> y llegaban pobladas al ViewModel; solo faltaba mostrarlas. El
/// DataGrid con columnas redimensionables (mismo patrón que ProductoListView.axaml) resuelve el
/// problema de raíz en vez de acomodar más texto en el mismo ancho fijo.
/// </summary>
public class ProveedorListViewColumnasTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:cat="clr-namespace:StockApp.Presentation.Views.Catalogo;assembly=GestionMunicipal"
                Width="1000" Height="600">
            <cat:ProveedorListView />
        </Window>
        """;

    private static (Window Window, ProveedorListViewModel Vm) Montar(IReadOnlyList<Proveedor> proveedores)
    {
        var vm = new ProveedorListViewModel(
            new ProveedorServiceFake(proveedores),
            new NavigationServiceFake(),
            new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_ProveedorConDireccionYNotas_LasMuestraEnLaGrilla()
    {
        var proveedor = new Proveedor
        {
            Id = 1,
            Nombre = "Ferretería Central",
            Telefono = "099123456",
            Email = "contacto@ferreteria.com",
            Direccion = "Av. 18 de Julio 1234",
            Notas = "Entrega los martes",
            Activo = true,
        };

        var (window, _) = Montar(new[] { proveedor });

        var textos = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(tb => ReferenceEquals(tb.DataContext, proveedor))
            .Select(tb => tb.Text)
            .ToList();

        Assert.Contains("Av. 18 de Julio 1234", textos);
        Assert.Contains("Entrega los martes", textos);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Montar_ProveedorSinDireccionNiNotas_NoRompeYMuestraElRestoDeColumnas()
    {
        var proveedor = new Proveedor
        {
            Id = 2,
            Nombre = "Distribuidora del Este",
            Telefono = "099999999",
            Email = "ventas@distrieste.com",
            Direccion = null,
            Notas = null,
            Activo = true,
        };

        var (window, _) = Montar(new[] { proveedor });

        var textos = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(tb => ReferenceEquals(tb.DataContext, proveedor))
            .Select(tb => tb.Text)
            .ToList();

        Assert.Contains("Distribuidora del Este", textos);
        Assert.Contains("ventas@distrieste.com", textos);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Montar_SeleccionarFilaEnGrilla_ActualizaItemSeleccionadoEnElViewModel()
    {
        var proveedor = new Proveedor { Id = 3, Nombre = "Proveedor X", Activo = true };
        var (window, vm) = Montar(new[] { proveedor });

        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.SelectedItem = proveedor;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(proveedor, vm.ItemSeleccionado);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Montar_ProveedorInactivo_MuestraBadgeInactivaYAtenuaLasCeldasDeDatos()
    {
        var proveedor = new Proveedor
        {
            Id = 4,
            Nombre = "Proveedor Inactivo",
            Direccion = "Calle Falsa 123",
            Activo = false,
        };

        var (window, _) = Montar(new[] { proveedor });

        var textoNombre = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(tb => ReferenceEquals(tb.DataContext, proveedor) && tb.Text == "Proveedor Inactivo");
        Assert.True(textoNombre.Opacity < 1.0);

        var badge = window.GetVisualDescendants()
            .OfType<StockApp.Presentation.Controls.BadgeEstado>()
            .Single(b => ReferenceEquals(b.DataContext, proveedor));
        Assert.True(badge.IsVisible);
        Assert.Equal(1.0, badge.Opacity);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
