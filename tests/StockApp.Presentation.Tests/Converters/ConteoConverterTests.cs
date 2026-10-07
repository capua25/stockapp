using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class ConteoConverterTests
{
    private static object? Convertir(object? valor, object? parametro = null) =>
        ConteoConverter.Instance.Convert(valor, typeof(string), parametro ?? "producto|productos", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0, "0 productos")]
    [InlineData(1, "1 producto")]
    [InlineData(12, "12 productos")]
    [InlineData(1250, "1.250 productos")]
    public void Convert_SingularPluralYMilesEsUy(int n, string esperado) => Assert.Equal(esperado, Convertir(n));

    [Fact]
    public void Convert_ParametroSinFormaSingularPlural_DevuelveVacio()
    {
        Assert.Equal(string.Empty, Convertir(3, "productos"));
    }

    [Fact]
    public void Convert_ValorNoEntero_DevuelveVacio()
    {
        Assert.Equal(string.Empty, Convertir(null));
        Assert.Equal(string.Empty, Convertir("12"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3, false)]
    public void EsCeroYHayElementos_SonOpuestos(int n, bool esCero)
    {
        Assert.Equal(esCero, ConteoConverter.EsCero.Convert(n, typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(!esCero, ConteoConverter.HayElementos.Convert(n, typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
