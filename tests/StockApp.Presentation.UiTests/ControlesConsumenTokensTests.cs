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
                <NumericUpDown x:Name="Numero" />
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

    // ── Bug visual reportado 2026-08-19: flechitas del spinner en TODOS los campos
    //    numéricos (ej. "Producto ID"). Sin un Style global para NumericUpDown, el control
    //    queda con el default crudo de FluentTheme (ShowButtonSpinner=True, Background
    //    semitransparente, Padding asimétrico) que además NO combina con el TextBox themeado
    //    de este archivo. El fix es un Style global en Controls.axaml, mismo patrón que
    //    TextBox/ComboBox de más arriba.

    [AvaloniaFact]
    public void NumericUpDown_OcultaElBotonSpinner()
    {
        Assert.False(Buscar<NumericUpDown>(Montar(), "Numero").ShowButtonSpinner);
    }

    [AvaloniaFact]
    public void NumericUpDown_UsaElRadioBaseDelSistema()
    {
        Assert.Equal(Token("RadioBase"), Buscar<NumericUpDown>(Montar(), "Numero").CornerRadius);
    }

    [AvaloniaFact]
    public void NumericUpDown_UsaElMismoPaddingQueUnTextBoxComun()
    {
        var window = Montar();
        var numero = Buscar<NumericUpDown>(window, "Numero");
        var caja = Buscar<TextBox>(window, "Caja");

        // Sin flechitas, un NumericUpDown tiene que quedar indistinguible de un TextBox: mismo
        // padding (que además se propaga por TemplateBinding al TextBox interno PART_TextBox,
        // así que alcanza con igualarlo acá para que el texto se vea alineado igual) y mismo
        // fondo/borde/radio -- si alguno diverge, el campo "sin spinner" igual se ve como un
        // control distinto en vez de un campo de texto común.
        Assert.Equal(caja.Padding, numero.Padding);
        Assert.Equal(caja.CornerRadius, numero.CornerRadius);
    }
}
