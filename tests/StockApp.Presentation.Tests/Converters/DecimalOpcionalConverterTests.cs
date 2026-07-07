using System;
using System.Globalization;
using Avalonia.Data;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Reproduce y verifica el fix del bug de <see cref="InvalidCastException"/> al vaciar el
/// campo opcional "Precio unitario" (decimal? bindeado TwoWay a TextBox en
/// MovimientoFormControl.axaml, compartido por Registrar Entrada y Registrar Salida): el
/// conversor default de Avalonia no mapea "" a null e intenta castear "" a decimal.
/// </summary>
public class DecimalOpcionalConverterTests
{
    private static readonly DecimalOpcionalConverter Sut = DecimalOpcionalConverter.Instance;

    [Fact]
    public void ConvertBack_CadenaVacia_DevuelveNull()
    {
        var resultado = Sut.ConvertBack(string.Empty, typeof(decimal?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }

    [Fact]
    public void ConvertBack_Whitespace_DevuelveNull()
    {
        var resultado = Sut.ConvertBack("   ", typeof(decimal?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }

    [Fact]
    public void ConvertBack_ValorNulo_DevuelveNull()
    {
        var resultado = Sut.ConvertBack(null, typeof(decimal?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }

    [Fact]
    public void ConvertBack_NumeroValido_DevuelveDecimal()
    {
        var resultado = Sut.ConvertBack("10", typeof(decimal?), null, CultureInfo.InvariantCulture);

        Assert.Equal(10m, resultado);
    }

    [Fact]
    public void ConvertBack_TextoInvalido_NoLanza_DevuelveBindingNotificationDeError()
    {
        var resultado = Sut.ConvertBack("abc", typeof(decimal?), null, CultureInfo.InvariantCulture);

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        Assert.Equal(BindingErrorType.Error, notificacion.ErrorType);
        Assert.IsType<FormatException>(notificacion.Error);
    }

    [Fact]
    public void Convert_Null_DevuelveCadenaVacia()
    {
        var resultado = Sut.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, resultado);
    }

    [Fact]
    public void Convert_Decimal_DevuelveSuRepresentacionDeTexto()
    {
        var resultado = Sut.Convert(10m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("10", resultado);
    }
}
