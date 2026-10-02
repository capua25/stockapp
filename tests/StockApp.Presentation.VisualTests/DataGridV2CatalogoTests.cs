using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using StockApp.Presentation.VisualTests.Catalogo;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Fix round 1 de la Task 17, medido con Skia real sobre el render del catálogo (la grilla ocupa
/// x 41..742, y 179..336): esquinas inferiores recortadas al radio 8 (el recorte de encabezados lo custodia el guardián
/// de texto, spec §6.5).
/// </summary>
public class DataGridV2CatalogoTests
{
    private static string Renderizar()
    {
        var vista = new CatalogoComponentesView();
        var png = Path.Combine(Directory.CreateTempSubdirectory("grilla-catalogo-").FullName, "catalogo.png");
        RenderizadorVista.RenderizarAPng(vista, new CatalogoComponentesViewModel(), png);
        return png;
    }

    [AvaloniaTheory]
    [InlineData(42, 335)]
    [InlineData(741, 335)]
    public void EsquinaInferior_FueraDelRadio_EsFondoNoSeparador(int x, int y)
    {
        // F2: el separador de la última fila (#F0EFED) llegaba recto hasta el borde en vez de
        // seguir la curva del radio 8 como en el mockup (overflow: hidden de .v2-grilla).
        var png = Renderizar();
        using var imagen = SKBitmap.Decode(png);

        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF), imagen.GetPixel(x, y));
    }
}
