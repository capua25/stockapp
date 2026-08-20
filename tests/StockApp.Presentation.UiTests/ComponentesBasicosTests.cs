using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls;
using Xunit;

namespace StockApp.Presentation.UiTests;

public class ComponentesBasicosTests
{
    private static Window Montar(string contenido)
    {
        var xaml = $$"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:c="using:StockApp.Presentation.Controls"
                    Width="600" Height="300">
                {{contenido}}
            </Window>
            """;
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void TarjetaMetrica_RenderizaEtiquetaValorYDetalle()
    {
        var textos = Montar("""<c:TarjetaMetrica Etiqueta="STOCK TOTAL" Valor="1.284" Detalle="+12 esta semana" />""")
            .GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("STOCK TOTAL", textos);
        Assert.Contains("1.284", textos);
        Assert.Contains("+12 esta semana", textos);
    }

    [AvaloniaFact]
    public void TarjetaMetrica_SinDetalle_NoDejaHueco()
    {
        var tarjeta = Montar("""<c:TarjetaMetrica Etiqueta="STOCK" Valor="1.284" />""")
            .GetVisualDescendants().OfType<TarjetaMetrica>().First();

        var conTexto = tarjeta.GetVisualDescendants().OfType<TextBlock>()
            .Count(t => t.IsVisible && !string.IsNullOrEmpty(t.Text));

        Assert.Equal(2, conTexto);
    }

    [AvaloniaFact]
    public void BadgeEstado_DiceLaPalabraNoSoloElColor()
    {
        // El punto del componente: hoy el stock negativo se comunica SOLO pintando el numero de
        // rojo, y un usuario daltonico no lo distingue. El badge dice la palabra Y la pinta.
        var textos = Montar("""<c:BadgeEstado Texto="Bajo minimo" Tono="Advertencia" />""")
            .GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("Bajo minimo", textos);
    }

    [AvaloniaTheory]
    [InlineData("Exito", "#16A34A")]
    [InlineData("Advertencia", "#D97706")]
    [InlineData("Peligro", "#DC2626")]
    [InlineData("Info", "#0EA5E9")]
    public void BadgeEstado_CadaTonoUsaSuColorSemantico(string tono, string colorEsperado)
    {
        var badge = Montar($"""<c:BadgeEstado Texto="Estado" Tono="{tono}" />""")
            .GetVisualDescendants().OfType<BadgeEstado>().First();

        var texto = badge.GetVisualDescendants().OfType<TextBlock>().First();

        Assert.Equal(
            Color.Parse(colorEsperado),
            Assert.IsType<SolidColorBrush>(texto.Foreground).Color);
    }

    [AvaloniaTheory]
    [InlineData("Advertencia", "#FEF3C7")]
    [InlineData("Peligro", "#FEE2E2")]
    [InlineData("Info", "#E0F2FE")]
    public void BadgeEstado_TonoNoExito_TieneFondoSuave(string tono, string hexEsperado)
    {
        // Ruling B-26 (Fase B, tanda 12): antes de esta task, Advertencia/Peligro/Info se caian
        // al gris DeshabilitadoFondoBrush del selector default -- solo Exito tenia fondo suave.
        var badge = Montar($"""<c:BadgeEstado Texto="Estado" Tono="{tono}" />""")
            .GetVisualDescendants().OfType<BadgeEstado>().First();

        var fondo = badge.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Fondo");

        Assert.Equal(Color.Parse(hexEsperado), Assert.IsType<SolidColorBrush>(fondo.Background).Color);
    }

    [AvaloniaFact]
    public void EstadoVacio_SinDatos_YFalloDeCarga_SonDistinguibles()
    {
        // Hoy los dos casos se ven identicos: una grilla vacia. El usuario no sabe si tiene que
        // cargar datos o reintentar.
        var vacio = Montar("""<c:EstadoVacio Titulo="Sin movimientos" Mensaje="Todavia no registraste ninguno." />""")
            .GetVisualDescendants().OfType<EstadoVacio>().First();

        var error = Montar("""<c:EstadoVacio Titulo="No se pudo cargar" Mensaje="Revisa la conexion." EsError="True" />""")
            .GetVisualDescendants().OfType<EstadoVacio>().First();

        Assert.False(vacio.EsError);
        Assert.True(error.EsError);

        var colorVacio = Assert.IsType<SolidColorBrush>(
            vacio.GetVisualDescendants().OfType<TextBlock>().First().Foreground).Color;
        var colorError = Assert.IsType<SolidColorBrush>(
            error.GetVisualDescendants().OfType<TextBlock>().First().Foreground).Color;

        Assert.NotEqual(colorVacio, colorError);
    }
}
