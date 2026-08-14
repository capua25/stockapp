using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Cubre el bug de contraste de <c>Button.ghost</c> (Controls.axaml): el estilo fue diseñado
/// EXCLUSIVAMENTE para el sidebar (fondo verde bosque oscuro, texto blanco, ver comentario
/// original en Controls.axaml líneas 84-90) pero se reusó sobre fondo CLARO (Border.card,
/// SuperficieBrush #FFFFFF) en DocumentoListView/TareaListView/InicioView/NuevaImportacionView
/// -- texto blanco sobre fondo blanco, contraste ~1:1, botones invisibles aunque clickeables.
///
/// El fix esperado: Button.ghost pasa a ser la variante NEUTRA (texto oscuro) para fondo claro,
/// y la variante de sidebar (texto blanco) se aplica solo dentro del contenedor del sidebar via
/// un selector de descendencia (Border.sidebar Button.ghost), sin tocar los ~26 usos de
/// ShellMainView.axaml ni los 11 usos rotos uno por uno.
/// </summary>
public class ButtonGhostContrasteTests
{
    /// <summary>
    /// Réplica del patrón real: Border.card (fondo blanco, como en DocumentoListView/TareaListView/
    /// InicioView) con un Button.ghost adentro -- el caso roto que reportó el usuario.
    /// </summary>
    private const string XamlCardConGhost = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
            <Border Name="Card" Classes="card">
                <Button Name="Boton" Classes="ghost" Content="Ver" />
            </Border>
        </Window>
        """;

    /// <summary>
    /// Réplica del contenedor del sidebar real (ShellMainView.axaml línea 14-16: Border con
    /// Background="{DynamicResource SidebarBrush}"), agregándole la clase "sidebar" que el fix
    /// necesita declarar ahí para que el selector de descendencia matchee.
    /// </summary>
    private const string XamlSidebarConGhost = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
            <Border Name="Sidebar" Classes="sidebar" Background="{DynamicResource SidebarBrush}">
                <Button Name="Boton" Classes="ghost" Content="Inicio" />
            </Border>
        </Window>
        """;

    private static (Window Window, Border Contenedor, Button Boton) Montar(string xaml, string nombreContenedor)
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var contenedor = window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == nombreContenedor)
            ?? throw new InvalidOperationException($"No se encontró el Border '{nombreContenedor}'.");
        var boton = window.FindDescendantOfType<Button>() ?? throw new InvalidOperationException("No se encontró el Button.");
        return (window, contenedor, boton);
    }

    /// <summary>
    /// Calcula el ratio de contraste WCAG 2.x entre dos colores a partir de su luminancia
    /// relativa. Fórmula estándar: L = 0.2126R + 0.7152G + 0.0722B (canales linealizados),
    /// contraste = (L_claro + 0.05) / (L_oscuro + 0.05).
    /// </summary>
    private static double RatioDeContraste(Color a, Color b)
    {
        var luminanciaA = LuminanciaRelativa(a);
        var luminanciaB = LuminanciaRelativa(b);
        var (claro, oscuro) = luminanciaA >= luminanciaB ? (luminanciaA, luminanciaB) : (luminanciaB, luminanciaA);
        return (claro + 0.05) / (oscuro + 0.05);
    }

    private static double LuminanciaRelativa(Color color)
    {
        double Canal(byte c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Canal(color.R) + 0.7152 * Canal(color.G) + 0.0722 * Canal(color.B);
    }

    [AvaloniaFact]
    public void BotonGhost_SobreCardClara_TieneContrasteSuficienteConElFondo()
    {
        var (_, card, boton) = Montar(XamlCardConGhost, "Card");

        var fondoCard = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
        var textoBoton = Assert.IsAssignableFrom<ISolidColorBrush>(boton.Foreground).Color;

        // El bug: el texto NO debe ser indistinguible del fondo blanco de la card.
        Assert.NotEqual(fondoCard, textoBoton);

        var ratio = RatioDeContraste(fondoCard, textoBoton);
        Assert.True(ratio >= 4.5, $"Contraste insuficiente sobre card clara: {ratio:F2}:1 (fondo {fondoCard}, texto {textoBoton}). Se requiere >= 4.5:1 (WCAG AA).");
    }

    [AvaloniaFact]
    public void BotonGhost_SobreSidebarOscuro_MantieneTextoClaroConContraste()
    {
        var (_, sidebar, boton) = Montar(XamlSidebarConGhost, "Sidebar");

        var fondoSidebar = Assert.IsAssignableFrom<ISolidColorBrush>(sidebar.Background).Color;
        var textoBoton = Assert.IsAssignableFrom<ISolidColorBrush>(boton.Foreground).Color;

        var ratio = RatioDeContraste(fondoSidebar, textoBoton);
        Assert.True(ratio >= 4.5, $"Contraste insuficiente sobre sidebar oscuro: {ratio:F2}:1 (fondo {fondoSidebar}, texto {textoBoton}). Se requiere >= 4.5:1 (WCAG AA).");
    }
}
