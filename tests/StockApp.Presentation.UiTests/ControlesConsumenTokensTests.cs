using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Verifica que los controles base resuelvan su geometria DESDE los tokens, no desde literales.
/// El valor del test no es el numero en si: es que si manana la escala de radios cambia, estos
/// controles la siguen; si alguien vuelve a hardcodear un 8, el test se pone rojo.
/// </summary>
public class ControlesConsumenTokensTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="500" Height="400">
            <StackPanel>
                <TextBox x:Name="Caja" />
                <ComboBox x:Name="Combo" />
                <Button x:Name="BotonPrimario" Classes="primary" Content="Guardar" />
                <Border x:Name="Tarjeta" Classes="card" />
            </StackPanel>
        </Window>
        """;

    private static Window Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Buscar<T>(Window w, string nombre) where T : Control
        => w.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static CornerRadius Token(string clave)
    {
        Avalonia.Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor);
        return (CornerRadius)valor!;
    }

    [AvaloniaFact]
    public void TextBox_UsaElRadioBaseDelSistema()
    {
        Assert.Equal(Token("RadioBase"), Buscar<TextBox>(Montar(), "Caja").CornerRadius);
    }

    [AvaloniaFact]
    public void ComboBox_UsaElRadioBaseDelSistema()
    {
        Assert.Equal(Token("RadioBase"), Buscar<ComboBox>(Montar(), "Combo").CornerRadius);
    }

    [AvaloniaFact]
    public void BotonPrimario_UsaElRadioBaseDelSistema()
    {
        Assert.Equal(Token("RadioBase"), Buscar<Button>(Montar(), "BotonPrimario").CornerRadius);
    }

    [AvaloniaFact]
    public void Card_UsaElRadioDeCardYSuPaddingYSombra()
    {
        var tarjeta = Buscar<Border>(Montar(), "Tarjeta");

        Assert.Equal(Token("RadioCard"), tarjeta.CornerRadius);
        Assert.Equal(new Thickness(16), tarjeta.Padding);
        Assert.True(tarjeta.BoxShadow.Count > 0, "La card perdio su sombra: sin ella no se despega del fondo.");
    }
}
