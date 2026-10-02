namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// Task 5b: la herramienta compara a 1280x760 y a 1366x768 (varias PCs del municipio son de
/// 1366x768). El estándar sigue siendo el de la Fase 0 y Lienzo.Ancho/Alto son su alias.
/// </summary>
public class TamanoLienzoTests
{
    [Fact]
    public void Estandar_Es1280x760YCoincideConLienzo()
    {
        Assert.Equal(new TamanoLienzo(1280, 760), TamanoLienzo.Estandar);
        Assert.Equal(Lienzo.Ancho, TamanoLienzo.Estandar.Ancho);
        Assert.Equal(Lienzo.Alto, TamanoLienzo.Estandar.Alto);
    }

    [Fact]
    public void Municipio_Es1366x768() =>
        Assert.Equal(new TamanoLienzo(1366, 768), TamanoLienzo.Municipio);

    [Fact]
    public void ToString_EsAnchoXAlto() =>
        Assert.Equal("1366x768", TamanoLienzo.Municipio.ToString());

    [Fact]
    public void SufijoPantalla_VacioEnElEstandarYConArrobaEnElResto()
    {
        Assert.Equal("", TamanoLienzo.Estandar.SufijoPantalla);
        Assert.Equal("@1366x768", TamanoLienzo.Municipio.SufijoPantalla);
    }

    [Theory]
    [InlineData("1280x760", 1280, 760)]
    [InlineData("1366x768", 1366, 768)]
    public void TryParsear_AceptaLosTamanosConocidos(string texto, int ancho, int alto)
    {
        Assert.True(TamanoLienzo.TryParsear(texto, out var tamano));
        Assert.Equal(new TamanoLienzo(ancho, alto), tamano);
    }

    [Theory]
    [InlineData("1920x1080")]
    [InlineData("1366")]
    [InlineData("")]
    [InlineData("1366X768x")]
    public void TryParsear_RechazaLoQueNoEsUnTamanoConocido(string texto) =>
        Assert.False(TamanoLienzo.TryParsear(texto, out _));
}
