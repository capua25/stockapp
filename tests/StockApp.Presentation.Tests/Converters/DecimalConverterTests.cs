using System;
using System.Globalization;
using Avalonia.Data;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Verifica <see cref="DecimalConverter"/> — converter de un <c>decimal</c> OBLIGATORIO (no
/// nullable) editable en un <c>TextBox</c> o una celda de grilla, con el formato único es-UY de
/// <see cref="StockApp.Domain.Formato.FormatoEsUy"/> (decisión 2026-10-01). Reemplaza al extinto
/// <c>DecimalPuntoConverter</c> (punto decimal invariante) de Ingreso por factura.
///
/// La cultura del binding se pasa adrede hostil (en-US e Invariant): el converter tiene que ser
/// determinista por sí mismo, sin depender de <paramref name="culture"/>.
/// </summary>
public class DecimalConverterTests
{
    private static readonly DecimalConverter Sut = DecimalConverter.Instance;

    public static TheoryData<string> CulturasHostiles => new() { "en-US", "" };

    private static CultureInfo Cultura(string nombre)
        => nombre.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(nombre);

    [Theory]
    [MemberData(nameof(CulturasHostiles))]
    public void Convert_Decimal_SeMuestraConComaYMiles(string cultura)
    {
        Assert.Equal("5,4", Sut.Convert(5.4m, typeof(string), null, Cultura(cultura)));
        Assert.Equal("27", Sut.Convert(27.0m, typeof(string), null, Cultura(cultura)));
        Assert.Equal("1.500,5", Sut.Convert(1500.5m, typeof(string), null, Cultura(cultura)));
    }

    [Fact]
    public void Convert_NoDecimal_DevuelveCadenaVacia()
    {
        Assert.Equal(string.Empty, Sut.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("12,35", "12.35")]
    [InlineData("1.500,50", "1500.50")]
    [InlineData("-3", "-3")]
    [InlineData("0,5", "0.5")]
    public void ConvertBack_FormatoEsUy_DevuelveElDecimal(string texto, string esperado)
    {
        var resultado = Sut.ConvertBack(texto, typeof(decimal), null, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(decimal.Parse(esperado, CultureInfo.InvariantCulture), resultado);
    }

    [Theory]
    [InlineData("5.4", "Usá coma para los decimales: 5,4")]
    [InlineData("12.35", "Usá coma para los decimales: 12,35")]
    [InlineData("1,500.50", "Usá coma para los decimales y punto para los miles: 1.500,50")]
    public void ConvertBack_PuntoDecimal_SeRechazaConMensajeClaro(string texto, string mensaje)
    {
        var resultado = Sut.ConvertBack(texto, typeof(decimal), null, CultureInfo.GetCultureInfo("en-US"));

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        Assert.Equal(BindingErrorType.Error, notificacion.ErrorType);
        var error = Assert.IsType<FormatException>(notificacion.Error);
        Assert.Equal(mensaje, error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ConvertBack_Vacio_EsErrorPorqueElCampoEsObligatorio(string? texto)
    {
        var resultado = Sut.ConvertBack(texto, typeof(decimal), null, CultureInfo.InvariantCulture);

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        var error = Assert.IsType<FormatException>(notificacion.Error);
        Assert.Equal("El valor ingresado no puede estar vacío.", error.Message);
    }

    [Fact]
    public void ConvertBack_Texto_NoLanza_DevuelveError()
    {
        var resultado = Sut.ConvertBack("abc", typeof(decimal), null, CultureInfo.InvariantCulture);

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        Assert.IsType<FormatException>(notificacion.Error);
    }

    [Fact]
    public void RoundTrip_CierraAlConvertirYVolverAParsear()
    {
        var texto = Sut.Convert(1500.25m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(1500.25m, Sut.ConvertBack(texto, typeof(decimal), null, CultureInfo.InvariantCulture));
    }
}
