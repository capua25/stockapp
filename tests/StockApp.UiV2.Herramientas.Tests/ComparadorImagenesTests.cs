using SkiaSharp;
using StockApp.UiV2.Herramientas.Imagenes;

namespace StockApp.UiV2.Herramientas.Tests;

public class ComparadorImagenesTests
{
    private static readonly SKColor Fondo = SKColor.Parse("#F7F7F5");
    private static readonly SKColor Acento = SKColor.Parse("#1D4ED8");
    private readonly string _carpeta = Directory.CreateTempSubdirectory("comparador-").FullName;

    private string Diff => Path.Combine(_carpeta, "diff.png");

    [Fact]
    public void ImagenesIdenticas_CeroDistintosYCeroTeselas()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 100, 100, Fondo, (10, 10, 20, 20, Acento));
        var b = PngDePrueba.Crear(_carpeta, "b.png", 100, 100, Fondo, (10, 10, 20, 20, Acento));

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.Equal(new ResultadoComparacion(100, 100, 0, 0), r);
        Assert.Equal(0, r.PorcentajeDistinto);
    }

    [Fact]
    public void UnPixelDistinto_CuentaUnoYNoFormaTeselaDensa()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 100, 100, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 100, 100, Fondo, (50, 50, 1, 1, Acento));

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.Equal(1, r.PixelesDistintos);
        Assert.Equal(0.01, r.PorcentajeDistinto, 6);
        Assert.Equal(0, r.TeselasDensas);
    }

    [Theory]
    [InlineData(32, 0)]
    [InlineData(33, 1)]
    public void DeltaDeCanal_SoloCuentaSiSuperaLaTolerancia(byte delta, int esperados)
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 20, 20, new SKColor(100, 100, 100));
        var b = PngDePrueba.Crear(_carpeta, "b.png", 20, 20, new SKColor(100, 100, 100),
            (5, 5, 1, 1, new SKColor((byte)(100 + delta), 100, 100)));

        Assert.Equal(esperados, ComparadorImagenes.Comparar(a, b, Diff).PixelesDistintos);
    }

    [Fact]
    public void BloqueDesplazado8px_MarcaTeselasDensas()
    {
        // Review Focus 3: 200x100 corrido 8px en 1280x760 son 1600 píxeles, 0.16% de la pantalla.
        // El porcentaje global solo no lo ve; las teselas densas sí.
        var a = PngDePrueba.Crear(_carpeta, "a.png", 1280, 760, Fondo, (100, 100, 200, 100, Acento));
        var b = PngDePrueba.Crear(_carpeta, "b.png", 1280, 760, Fondo, (108, 100, 200, 100, Acento));

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.True(r.PorcentajeDistinto < 0.2, $"porcentaje {r.PorcentajeDistinto}");
        Assert.True(r.TeselasDensas >= 12, $"teselas densas {r.TeselasDensas}");
    }

    [Fact]
    public void FondoPalidoCambiado_MarcaTeselasPorColorAunqueNingunPixelDifieraFuerte()
    {
        // Insignia Peligro (#FEE2E2) pintada como Neutro (#F5F5F4): delta máximo 19, invisible
        // para la tolerancia de 32. Un bloque de 64x64 alineado a la grilla cubre 16 teselas.
        var a = PngDePrueba.Crear(_carpeta, "a.png", 256, 256, Fondo, (32, 32, 64, 64, SKColor.Parse("#FEE2E2")));
        var b = PngDePrueba.Crear(_carpeta, "b.png", 256, 256, Fondo, (32, 32, 64, 64, SKColor.Parse("#F5F5F4")));

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.Equal(0, r.PixelesDistintos);
        Assert.Equal(16, r.TeselasDensas);
    }

    [Fact]
    public void DiferenciasLevesDispersas_NoMarcanTeselas()
    {
        var leves = Enumerable.Range(0, 30)
            .Select(i => (X: 10 + i * 8, Y: 10 + (i % 5) * 40, Ancho: 1, Alto: 1, Color: new SKColor(0xF7, 0xF7, 0xE9)))
            .ToArray();
        var a = PngDePrueba.Crear(_carpeta, "a.png", 256, 256, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 256, 256, Fondo, leves);

        Assert.Equal(0, ComparadorImagenes.Comparar(a, b, Diff).TeselasDensas);
    }

    [Fact]
    public void PixelesSueltosDispersos_NoFormanTeselasDensas()
    {
        var sueltos = Enumerable.Range(0, 40)
            .Select(i => (X: 20 + i * 30, Y: 50 + (i % 7) * 90, Ancho: 1, Alto: 1, Color: Acento))
            .ToArray();
        var a = PngDePrueba.Crear(_carpeta, "a.png", 1280, 760, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 1280, 760, Fondo, sueltos);

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.Equal(40, r.PixelesDistintos);
        Assert.Equal(0, r.TeselasDensas);
    }

    [Fact]
    public void TamanosDistintos_LanzaNombrandoAmbos()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 1280, 760, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 1600, 950, Fondo);

        var ex = Assert.Throws<ArgumentException>(() => ComparadorImagenes.Comparar(a, b, Diff));

        Assert.Contains("1280x760", ex.Message);
        Assert.Contains("1600x950", ex.Message);
    }

    [Fact]
    public void ImagenUniforme_LanzaPorCapturaVacia()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 50, 50, Fondo);
        var b = PngDePrueba.CrearUniforme(_carpeta, "b.png", 50, 50, Fondo);

        var ex = Assert.Throws<InvalidOperationException>(() => ComparadorImagenes.Comparar(a, b, Diff));

        Assert.Contains("uniforme", ex.Message);
    }

    [Fact]
    public void EscribeDiffConLosDistintosEnRojo()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 40, 40, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 40, 40, Fondo, (20, 20, 1, 1, Acento));

        ComparadorImagenes.Comparar(a, b, Diff);

        using var diff = SKBitmap.Decode(Diff);
        Assert.Equal(new SKColor(255, 0, 0), diff.GetPixel(20, 20));
        Assert.NotEqual(new SKColor(255, 0, 0), diff.GetPixel(30, 30));
    }
}
