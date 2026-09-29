using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Autotest de la herramienta contra docs/ui-v2/mockups/_fixtures/bloque.html: la misma
/// escena aprueba, y la escena con el bloque corrido 8px falla (Review Focus 3).
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class ComparacionVisualTests
{
    private static Control VistaBloque(double desplazamiento)
    {
        var bloque = new Border { Width = 200, Height = 100, Background = new SolidColorBrush(Color.Parse("#1D4ED8")) };
        Canvas.SetLeft(bloque, 100 + desplazamiento);
        Canvas.SetTop(bloque, 100);
        var texto = new TextBlock { Text = "Bloque de prueba", FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#1C1917")) };
        Canvas.SetLeft(texto, 100);
        Canvas.SetTop(texto, 240);
        return new Canvas { Children = { bloque, texto } };
    }

    [AvaloniaFact]
    public void VistaIgualAlMockup_ApruebaYDejaLaPaginaDeComparacion()
    {
        var r = ComparacionVisual.CompararConMockup(VistaBloque(0), null, "_fixtures/bloque.html", umbral: 0.5, teselasDensasMax: 2);

        Assert.True(r.PorcentajeDistinto <= 0.5);
        var carpeta = Path.Combine(ComparacionVisual.CarpetaSalida, "_fixtures-bloque");
        Assert.True(File.Exists(Path.Combine(carpeta, "index.html")));
        Assert.True(File.Exists(Path.Combine(carpeta, "mockup.png")));
        Assert.True(File.Exists(Path.Combine(carpeta, "app.png")));
        Assert.True(File.Exists(Path.Combine(carpeta, "diff.png")));
    }

    [AvaloniaFact]
    public void VistaDesplazada8px_FallaYDejaLaPaginaDeComparacion()
    {
        var ex = Assert.ThrowsAny<Exception>(
            () => ComparacionVisual.CompararConMockup(VistaBloque(8), null, "_fixtures/bloque.html", umbral: 0.5, teselasDensasMax: 2));

        Assert.Contains("teselas densas", ex.Message);
        Assert.Contains("index.html", ex.Message);
    }

    [Theory]
    [InlineData("componentes/catalogo.html", "componentes-catalogo")]
    [InlineData("inventario\\productos.html", "inventario-productos")]
    public void NombrePantalla_UneModuloYPantalla(string ruta, string esperado) =>
        Assert.Equal(esperado, ComparacionVisual.NombrePantalla(ruta));
}
