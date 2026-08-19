using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// El header de vista existe porque hoy cada vista improvisa el suyo y 15 no tienen ni titulo.
/// Estos tests fijan el contrato que las 58 vistas van a consumir en las tandas 6 a 13.
/// </summary>
public class HeaderVistaTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:c="using:StockApp.Presentation.Controls"
                Width="800" Height="200">
            <c:HeaderVista x:Name="Header"
                           Eyebrow="INVENTARIO"
                           Titulo="Productos"
                           Resumen="128 productos activos">
                <c:HeaderVista.Acciones>
                    <Button x:Name="BotonAccion" Classes="primary" Content="Nuevo" />
                </c:HeaderVista.Acciones>
            </c:HeaderVista>
        </Window>
        """;

    private static Window Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Montar_ConLosTresTextos_LosTresSeRenderizan()
    {
        var textos = Montar().GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();

        Assert.Contains("INVENTARIO", textos);
        Assert.Contains("Productos", textos);
        Assert.Contains("128 productos activos", textos);
    }

    [AvaloniaFact]
    public void Montar_ConAcciones_ElBotonDelSlotLlegaAlArbolVisual()
    {
        var boton = Montar().GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Name == "BotonAccion");

        Assert.NotNull(boton);
        Assert.True(ArbolVisual.EsVisibleEnArbol(boton!));
    }

    [AvaloniaFact]
    public void Montar_SinEyebrow_ElEyebrowNoOcupaLugar()
    {
        // Muchas vistas no tienen seccion padre. Un eyebrow vacio no debe dejar un hueco.
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:c="using:StockApp.Presentation.Controls"
                    Width="800" Height="200">
                <c:HeaderVista Titulo="Productos" />
            </Window>
            """, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = window.GetVisualDescendants().OfType<HeaderVista>().First();
        var eyebrow = header.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Classes.Contains("micro"));

        Assert.True(eyebrow is null || !eyebrow.IsVisible,
            "Con Eyebrow en null el TextBlock del eyebrow debe estar oculto, no vacio ocupando alto.");
    }

    [AvaloniaFact]
    public void Montar_SinResumen_ElResumenNoOcupaLugar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:c="using:StockApp.Presentation.Controls"
                    Width="800" Height="200">
                <c:HeaderVista Titulo="Productos" />
            </Window>
            """, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = window.GetVisualDescendants().OfType<HeaderVista>().First();
        var visiblesConTexto = header.GetVisualDescendants().OfType<TextBlock>()
            .Count(t => t.IsVisible && !string.IsNullOrEmpty(t.Text));

        Assert.Equal(1, visiblesConTexto);
    }
}
