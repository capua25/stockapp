using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SkiaSharp;
using StockApp.Presentation.VisualTests.Catalogo;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Fix round 1 de la Task 17, medido con Skia real sobre el render del catálogo (la grilla ocupa
/// x 41..742, y 179..336): encabezados sin recorte y esquinas inferiores recortadas al radio 8.
/// </summary>
public class DataGridV2CatalogoTests
{
    private static (CatalogoComponentesView Vista, string Png) Renderizar()
    {
        var vista = new CatalogoComponentesView();
        var png = Path.Combine(Directory.CreateTempSubdirectory("grilla-catalogo-").FullName, "catalogo.png");
        RenderizadorVista.RenderizarAPng(vista, new CatalogoComponentesViewModel(), png);
        return (vista, png);
    }

    [AvaloniaFact]
    public void Headers_TextoCompletoSinRecorte()
    {
        // F1: Fluent reservaba 32 px para la flecha de orden y "CÓDIGO" (columna de 100) y "STOCK"
        // (columna de 90) salían "CÓDIG" y "STOC". El TextBlock recorta si su ancho asignado es
        // menor que el que pide sin restricción.
        var (vista, _) = Renderizar();
        var textos = vista.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .Where(h => h.Content is string)
            .Select(h => h.GetVisualDescendants().OfType<TextBlock>().Single())
            .ToList();

        Assert.Equal(5, textos.Count);
        Assert.All(textos, t =>
        {
            t.Measure(Avalonia.Size.Infinity);
            Assert.True(t.DesiredSize.Width <= t.Bounds.Width,
                $"\"{t.Text}\": pide {t.DesiredSize.Width} y tiene {t.Bounds.Width}");
        });
    }

    [AvaloniaTheory]
    [InlineData(42, 335)]
    [InlineData(741, 335)]
    public void EsquinaInferior_FueraDelRadio_EsFondoNoSeparador(int x, int y)
    {
        // F2: el separador de la última fila (#F0EFED) llegaba recto hasta el borde en vez de
        // seguir la curva del radio 8 como en el mockup (overflow: hidden de .v2-grilla).
        var (_, png) = Renderizar();
        using var imagen = SKBitmap.Decode(png);

        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF), imagen.GetPixel(x, y));
    }
}
