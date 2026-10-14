using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class NombreMesConverterTests
{
    [Theory]
    [InlineData(1, "Enero")]
    [InlineData(3, "Marzo")]
    [InlineData(9, "Setiembre")]
    [InlineData(12, "Diciembre")]
    [InlineData(13, "13")]
    [InlineData(0, "0")]
    public void Nombre_DelMes(int mes, string esperado) => Assert.Equal(esperado, NombreMesConverter.Nombre(mes));

    [Fact]
    public void Convert_DeUnInt_DaElNombre_YDeOtraCosa_Vacio()
    {
        Assert.Equal("Junio", NombreMesConverter.Instance.Convert(6, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, NombreMesConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
