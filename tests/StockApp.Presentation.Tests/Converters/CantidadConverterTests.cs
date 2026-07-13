using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Verifica que <see cref="CantidadConverter"/> formatee cantidades de stock sin ceros de
/// relleno (formato "0.####"), con coma decimal fija (es-UY), determinista sin importar la
/// cultura del entorno donde corran los tests.
/// </summary>
public class CantidadConverterTests
{
    private static readonly CantidadConverter Sut = CantidadConverter.Instance;

    [Fact]
    public void Convert_Entero_SinDecimales()
    {
        var resultado = Sut.Convert(22m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("22", resultado);
    }

    [Fact]
    public void Convert_Negativo_AntepnoneSigno()
    {
        var resultado = Sut.Convert(-6m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("-6", resultado);
    }

    [Fact]
    public void Convert_ConDecimal_UsaComaDecimal()
    {
        var resultado = Sut.Convert(22.5m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("22,5", resultado);
    }

    [Fact]
    public void Convert_ConCerosDeRelleno_LosOculta()
    {
        var resultado = Sut.Convert(22.0000m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("22", resultado);
    }

    [Fact]
    public void Convert_ConInt_FormateaIgualQueDecimal()
    {
        var resultado = Sut.Convert(22, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("22", resultado);
    }

    [Fact]
    public void Convert_ConIntNegativo_AntepnoneSigno()
    {
        var resultado = Sut.Convert(-6, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("-6", resultado);
    }

    [Fact]
    public void Convert_DecimalNulo_DevuelveCadenaVacia()
    {
        var resultado = Sut.Convert((decimal?)null, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, resultado);
    }

    [Fact]
    public void ConvertBack_NoSoportado_Lanza()
    {
        Assert.Throws<NotSupportedException>(
            () => Sut.ConvertBack("22", typeof(decimal), null, CultureInfo.InvariantCulture));
    }
}
