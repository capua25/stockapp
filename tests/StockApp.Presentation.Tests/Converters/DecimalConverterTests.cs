using System;
using System.Globalization;
using Avalonia.Data;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Verifica el fix del bug real de locale en "Ingreso de stock por factura": las columnas
/// "Cantidad" y "Precio unitario" de la grilla de renglones (y "Precio de venta" del alta en
/// línea de producto nuevo) bindeaban DIRECTO a propiedades <c>decimal</c>, SIN converter — a
/// diferencia de <see cref="DecimalOpcionalConverter"/> (usado en Registrar Entrada/Salida para
/// el mismo tipo de campo, pero <c>decimal?</c>). Un binding sin converter usa la cultura
/// AMBIENTE del hilo (la app no fija ninguna) y <see cref="NumberStyles.Number"/> (incluye
/// AllowThousands): reproducido con un test headless que edita la celda real bajo cultura es-UY
/// explícita (ver IngresoPorFacturaLocaleDecimalTests.cs en StockApp.Presentation.UiTests) — con
/// esa cultura, escribir "12.35" hace que el punto se interprete como separador de miles y el
/// valor se pierde/rechaza en vez de guardarse como 12,35.
///
/// La cultura del binding acá simula ese escenario (Invariant), igual que
/// DecimalOpcionalConverterTests: el converter debe ser determinista por sí mismo, sin depender
/// de <paramref name="culture"/>.
/// </summary>
public class DecimalConverterTests
{
    private static readonly DecimalConverter Sut = DecimalConverter.Instance;

    [Fact]
    public void ConvertBack_CadenaVacia_DevuelveBindingNotificationDeError()
    {
        var resultado = Sut.ConvertBack(string.Empty, typeof(decimal), null, CultureInfo.InvariantCulture);

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        Assert.Equal(BindingErrorType.Error, notificacion.ErrorType);
        Assert.IsType<FormatException>(notificacion.Error);
    }

    [Fact]
    public void ConvertBack_Whitespace_DevuelveBindingNotificationDeError()
    {
        var resultado = Sut.ConvertBack("   ", typeof(decimal), null, CultureInfo.InvariantCulture);

        Assert.IsType<BindingNotification>(resultado);
    }

    [Fact]
    public void ConvertBack_NumeroValido_DevuelveDecimal()
    {
        var resultado = Sut.ConvertBack("10", typeof(decimal), null, CultureInfo.InvariantCulture);

        Assert.Equal(10m, resultado);
    }

    [Fact]
    public void ConvertBack_TextoInvalido_NoLanza_DevuelveBindingNotificationDeError()
    {
        var resultado = Sut.ConvertBack("abc", typeof(decimal), null, CultureInfo.InvariantCulture);

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        Assert.Equal(BindingErrorType.Error, notificacion.ErrorType);
        Assert.IsType<FormatException>(notificacion.Error);
    }

    [Fact]
    public void Convert_Decimal_DevuelveSuRepresentacionDeTexto()
    {
        var resultado = Sut.Convert(10m, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("10", resultado);
    }

    /// <summary>
    /// El caso central del bug: "12,35" (coma decimal, lo que el usuario ve/espera en toda la
    /// app — es-UY) SIEMPRE debe parsear a 12.35m, sin importar qué cultura pase el binding
    /// (acá se simula el peor caso: Invariant, que interpretaría la coma como separador de
    /// miles si el converter no fijara su propia cultura).
    /// </summary>
    [Fact]
    public void ConvertBack_ComaDecimal_DevuelveElDecimalCorrecto()
    {
        var resultado = Sut.ConvertBack("12,35", typeof(decimal), null, CultureInfo.InvariantCulture);

        Assert.Equal(12.35m, resultado);
    }

    [Fact]
    public void ConvertBack_PuntoDecimal_NoLoInterpretaComoSeparadorDeMiles()
    {
        var resultado = Sut.ConvertBack("12.35", typeof(decimal), null, CultureInfo.InvariantCulture);

        Assert.NotEqual(1235m, resultado);
    }

    [Fact]
    public void RoundTrip_ComaDecimal_CierraAlConvertirYVolverAParsear()
    {
        var parseado = Sut.ConvertBack("12,35", typeof(decimal), null, CultureInfo.InvariantCulture);
        var texto = Sut.Convert(parseado, typeof(string), null, CultureInfo.InvariantCulture);
        var reparseado = Sut.ConvertBack(texto, typeof(decimal), null, CultureInfo.InvariantCulture);

        Assert.Equal(12.35m, reparseado);
    }
}
