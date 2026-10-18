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
/// Cubre el bug de contraste de <c>Button.ghost</c> (Themes/Controls.axaml): el estilo nació para un sidebar
/// oscuro con texto blanco y se reusó sobre fondo CLARO (Border.card, SuperficieBrush #FFFFFF) --
/// texto blanco sobre fondo blanco, contraste ~1:1, botones invisibles aunque clickeables. Button.ghost es la
/// variante NEUTRA (texto oscuro) para fondo claro. El sidebar de la app es v2 y no usa esta variante.
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

    [AvaloniaFact]
    public void BotonGhost_SobreCardClara_TieneContrasteSuficienteConElFondo()
    {
        var (_, card, boton) = Montar(XamlCardConGhost, "Card");

        var fondoCard = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
        var textoBoton = Assert.IsAssignableFrom<ISolidColorBrush>(boton.Foreground).Color;

        // El bug: el texto NO debe ser indistinguible del fondo blanco de la card.
        Assert.NotEqual(fondoCard, textoBoton);

        var ratio = Contraste.Ratio(fondoCard, textoBoton);
        Assert.True(ratio >= 4.5, $"Contraste insuficiente sobre card clara: {ratio:F2}:1 (fondo {fondoCard}, texto {textoBoton}). Se requiere >= 4.5:1 (WCAG AA).");
    }
}
