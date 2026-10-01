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
        Assert.IsType<EntradaNumericaInvalidaException>(notificacion.Error);
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

    /// <summary>
    /// Bug real reproducido vía verificación orgánica: en Registrar Entrada, precio unitario
    /// "850,50" se guardó como 85050 en la base. Causa: el converter parseaba con la cultura
    /// del binding (Invariant en máquinas no es-*) y con <see cref="NumberStyles.Number"/>
    /// (incluye AllowThousands), así que la coma se interpretaba como separador de miles.
    /// La cultura del binding acá simula justamente ese escenario (Invariant).
    /// </summary>
    [Fact]
    public void ConvertBack_ComaDecimal_DevuelveElDecimalCorrecto()
    {
        var resultado = Sut.ConvertBack("850,50", typeof(decimal?), null, CultureInfo.InvariantCulture);

        Assert.Equal(850.50m, resultado);
    }

    [Fact]
    public void ConvertBack_PuntoDecimal_NoLoInterpretaComoSeparadorDeMiles()
    {
        var resultado = Sut.ConvertBack("850.50", typeof(decimal?), null, CultureInfo.InvariantCulture);

        Assert.NotEqual(85050m, resultado);
    }

    [Fact]
    public void RoundTrip_ComaDecimal_CierraAlConvertirYVolverAParsear()
    {
        var parseado = Sut.ConvertBack("850,50", typeof(decimal?), null, CultureInfo.InvariantCulture);
        var texto = Sut.Convert(parseado, typeof(string), null, CultureInfo.InvariantCulture);
        var reparsead = Sut.ConvertBack(texto, typeof(decimal?), null, CultureInfo.InvariantCulture);

        Assert.Equal(850.50m, reparsead);
    }

    /// <summary>Decisión 2026-10-01: es-UY en toda la app. "850.50" ya no es un decimal válido:
    /// se rechaza con un mensaje que sugiere la forma correcta (antes solo se garantizaba que no
    /// se leyera como 85050).</summary>
    [Fact]
    public void ConvertBack_PuntoDecimal_SeRechazaConMensajeClaro()
    {
        var resultado = Sut.ConvertBack("850.50", typeof(decimal?), null, CultureInfo.InvariantCulture);

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        var error = Assert.IsType<EntradaNumericaInvalidaException>(notificacion.Error);
        Assert.Equal("Usá coma para los decimales: 850,50", error.Message);
    }

    [Fact]
    public void ConvertBack_MilesConPunto_DevuelveElDecimalCorrecto()
    {
        var resultado = Sut.ConvertBack("1.500,50", typeof(decimal?), null, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(1500.50m, resultado);
    }

    [Fact]
    public void Convert_DecimalConEscala_SeMuestraEsUySinCerosDeRelleno()
    {
        var resultado = Sut.Convert(1500.5000m, typeof(string), null, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("1.500,5", resultado);
    }
}
