using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Converter a <c>bool</c> del signo de un <c>decimal</c>, usado para gatear la visibilidad del
/// <c>BadgeEstado</c> "Stock negativo"/"Sobreejecutado"/"Saldo negativo"/"Déficit"/"Vencida"
/// (Task B2-T, Ruling B-6, 2026-08-19). <see cref="SignoNegativoBrushConverter"/> devuelve un
/// <c>IBrush</c>, no sirve para <c>IsVisible</c> -- de ahí este converter hermano.
/// </summary>
public class EsNegativoConverterTests
{
    private static readonly EsNegativoConverter Sut = EsNegativoConverter.Instance;

    [Fact]
    public void Convert_Negativo_DevuelveTrue()
    {
        var resultado = Sut.Convert(-3.5m, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(true, resultado);
    }

    [Fact]
    public void Convert_Cero_DevuelveFalse()
    {
        var resultado = Sut.Convert(0m, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, resultado);
    }

    [Fact]
    public void Convert_Positivo_DevuelveFalse()
    {
        var resultado = Sut.Convert(12m, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, resultado);
    }

    [Fact]
    public void Convert_Null_DevuelveFalse()
    {
        var resultado = Sut.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, resultado);
    }
}
