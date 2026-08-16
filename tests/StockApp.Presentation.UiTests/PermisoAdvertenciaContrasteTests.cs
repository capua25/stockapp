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
/// Cubre el color elegido para el aviso NO bloqueante de dependencias BLANDAS del panel de
/// permisos (paso 5 del refactor, PermisoDependencias.Recomendados): un TextBlock
/// Classes="caption" debajo del CheckBox, dentro del Border.card blanco de UsuariosAdminView.
/// Mismo criterio y misma fórmula WCAG que ButtonGhostContrasteTests -- se agrega acá,
/// separado, porque cubre una elección de color puntual (por qué caption/TextoSecundarioBrush
/// y no WarningBrush) y no el bug de Button.ghost.
///
/// WarningBrush (Tokens.axaml, #D97706) se descartó a mano: sobre SuperficieBrush (#FFFFFF) da
/// 3.19:1, por debajo del piso WCAG AA de 4.5:1 -- se habría reproducido el mismo bug de
/// contraste que ButtonGhostContrasteTests ya documentó para Button.ghost, con otro color.
/// </summary>
public class PermisoAdvertenciaContrasteTests
{
    private const string XamlCardConAdvertencia = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
            <Border Name="Card" Classes="card">
                <TextBlock Name="Advertencia" Classes="caption"
                           Text="Sin permiso de productos solo va a poder consultar el historial." />
            </Border>
        </Window>
        """;

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
    public void AdvertenciaCaption_SobreCardClara_TieneContrasteSuficienteConElFondo()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(XamlCardConAdvertencia, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "Card")
            ?? throw new InvalidOperationException("No se encontró el Border 'Card'.");
        var advertencia = window.FindDescendantOfType<TextBlock>()
            ?? throw new InvalidOperationException("No se encontró el TextBlock.");

        var fondoCard = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
        var textoAdvertencia = Assert.IsAssignableFrom<ISolidColorBrush>(advertencia.Foreground).Color;

        var ratio = RatioDeContraste(fondoCard, textoAdvertencia);
        Assert.True(ratio >= 4.5,
            $"Contraste insuficiente para la advertencia sobre card clara: {ratio:F2}:1 (fondo {fondoCard}, texto {textoAdvertencia}). Se requiere >= 4.5:1 (WCAG AA).");
    }
}
