using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// <see cref="MonedaOGuionConverter"/>: un precio 0 — salida o ajuste sin precio — se muestra "—"; cualquier otro
/// monto, con el formato es-UY de <see cref="MonedaConverter"/>.
/// </summary>
public class MonedaOGuionConverterTests
{
    [Theory]
    [InlineData(0, "—")]
    [InlineData(480, "$ 480,00")]
    [InlineData(12345.6, "$ 12.345,60")]
    public void Convierte(decimal valor, string esperado) =>
        Assert.Equal(esperado, MonedaOGuionConverter.Instance.Convert(valor, typeof(string), null, CultureInfo.InvariantCulture));

    [Fact]
    public void Null_DaVacio() =>
        Assert.Equal(string.Empty, MonedaOGuionConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
}
