using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Verifica que <see cref="MonedaConverter"/> formatee montos en pesos con cultura FIJA
/// es-UY (miles con punto, decimales con coma, símbolo "$"), sin depender de la cultura
/// del hilo/entorno donde corran los tests (por eso los asserts no cambian el
/// CultureInfo.CurrentCulture del test — el converter debe ser determinista por sí mismo).
/// </summary>
public class MonedaConverterTests
{
    private static readonly MonedaConverter Sut = MonedaConverter.Instance;

    [Fact]
    public void Convert_ValorConMiles_FormateaPuntoParaMilesYComaParaDecimales()
    {
        var resultado = Sut.Convert(26400m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("$ 26.400,00", resultado);
    }

    [Fact]
    public void Convert_Cero_FormateaConDosDecimales()
    {
        var resultado = Sut.Convert(0m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("$ 0,00", resultado);
    }

    [Fact]
    public void Convert_Negativo_AntepnoneSigno()
    {
        // Patrón CurrencyNegativePattern de es-UY: "-$ n" (verificado con
        // CultureInfo.GetCultureInfo("es-UY").NumberFormat.CurrencyNegativePattern == 9).
        var resultado = Sut.Convert(-600m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("-$ 600,00", resultado);
    }

    [Fact]
    public void Convert_ValorConDecimales_RedondeaADosDecimales()
    {
        var resultado = Sut.Convert(1234.5m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("$ 1.234,50", resultado);
    }

    [Fact]
    public void Convert_DecimalNulo_DevuelveCadenaVacia()
    {
        var resultado = Sut.Convert((decimal?)null, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, resultado);
    }

    [Fact]
    public void Convert_ValorNulo_DevuelveCadenaVacia()
    {
        var resultado = Sut.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, resultado);
    }

    [Fact]
    public void ConvertBack_NoSoportado_Lanza()
    {
        Assert.Throws<NotSupportedException>(
            () => Sut.ConvertBack("$ 26.400,00", typeof(decimal), null, CultureInfo.InvariantCulture));
    }
}
