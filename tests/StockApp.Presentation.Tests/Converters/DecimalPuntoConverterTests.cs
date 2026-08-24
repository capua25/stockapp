using System;
using System.Globalization;
using Avalonia.Data;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Verifica <see cref="DecimalPuntoConverter"/> — converter con PUNTO decimal, creado para la
/// grilla de renglones y la zona de carga de "Ingreso de stock por factura"
/// (Cantidad/Precio unitario/Subtotal).
///
/// Hallazgo de verificación visual (2026-08-24): en la MISMA fila de la grilla, "Precio unitario"
/// mostraba "5,4" (coma, vía el entonces existente <c>DecimalConverter</c>, que ahí fijaba es-UY)
/// y "Subtotal" mostraba "27.0" (punto, sin converter, cultura ambiente del hilo). Decisión de
/// producto: unificar TODA la grilla con PUNTO. Ver el comentario de
/// <see cref="DecimalPuntoConverter"/> para el detalle de por qué se creó un converter nuevo en
/// vez de reutilizar el de coma (que en su momento también alimentaba
/// <c>NuevoProductoPrecioVenta</c>, hoy eliminado junto con <c>Producto.PrecioVenta</c>).
///
/// La cultura del binding acá simula el peor caso (es-UY, donde el punto sería tradicionalmente
/// separador de miles), igual que DecimalOpcionalConverterTests: el
/// converter debe ser determinista por sí mismo, sin depender de <paramref name="culture"/>.
/// </summary>
public class DecimalPuntoConverterTests
{
    private static readonly DecimalPuntoConverter Sut = DecimalPuntoConverter.Instance;

    private static CultureInfo CulturaHostil()
    {
        try { return CultureInfo.GetCultureInfo("es-UY"); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("es-AR"); }
    }

    [Fact]
    public void ConvertBack_CadenaVacia_DevuelveBindingNotificationDeError()
    {
        var resultado = Sut.ConvertBack(string.Empty, typeof(decimal), null, CulturaHostil());

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        Assert.Equal(BindingErrorType.Error, notificacion.ErrorType);
        Assert.IsType<FormatException>(notificacion.Error);
    }

    [Fact]
    public void ConvertBack_Whitespace_DevuelveBindingNotificationDeError()
    {
        var resultado = Sut.ConvertBack("   ", typeof(decimal), null, CulturaHostil());

        Assert.IsType<BindingNotification>(resultado);
    }

    [Fact]
    public void ConvertBack_NumeroValido_DevuelveDecimal()
    {
        var resultado = Sut.ConvertBack("10", typeof(decimal), null, CulturaHostil());

        Assert.Equal(10m, resultado);
    }

    [Fact]
    public void ConvertBack_TextoInvalido_NoLanza_DevuelveBindingNotificationDeError()
    {
        var resultado = Sut.ConvertBack("abc", typeof(decimal), null, CulturaHostil());

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        Assert.Equal(BindingErrorType.Error, notificacion.ErrorType);
        Assert.IsType<FormatException>(notificacion.Error);
    }

    [Fact]
    public void Convert_Decimal_DevuelveSuRepresentacionDeTextoConPunto()
    {
        var resultado = Sut.Convert(12.35m, typeof(string), null, CulturaHostil());

        Assert.Equal("12.35", resultado);
    }

    /// <summary>
    /// La coma decimal (formato viejo de <see cref="DecimalConverter"/>) YA NO es aceptada por
    /// este converter: con <see cref="CultureInfo.InvariantCulture"/> + <c>AllowDecimalPoint</c>
    /// (SIN <c>AllowThousands</c>), "12,35" no es un número válido.
    /// </summary>
    [Fact]
    public void ConvertBack_ComaDecimal_SeRechaza_DevuelveBindingNotificationDeError()
    {
        var resultado = Sut.ConvertBack("12,35", typeof(decimal), null, CulturaHostil());

        var notificacion = Assert.IsType<BindingNotification>(resultado);
        Assert.Equal(BindingErrorType.Error, notificacion.ErrorType);
    }

    /// <summary>
    /// El caso central: "12.35" (punto decimal) SIEMPRE debe parsear a 12.35m, sin importar qué
    /// cultura pase el binding -- acá se simula el peor caso (es-UY, donde el punto sería
    /// tradicionalmente separador de miles: "12.35" podría malinterpretarse como 1235 si el
    /// converter no fijara su propia cultura).
    /// </summary>
    [Fact]
    public void ConvertBack_PuntoDecimal_CulturaAmbienteHostilEsUy_DevuelveElDecimalCorrecto()
    {
        var resultado = Sut.ConvertBack("12.35", typeof(decimal), null, CulturaHostil());

        Assert.Equal(12.35m, resultado);
        Assert.NotEqual(1235m, resultado);
    }

    [Fact]
    public void RoundTrip_PuntoDecimal_CierraAlConvertirYVolverAParsear()
    {
        var parseado = Sut.ConvertBack("12.35", typeof(decimal), null, CulturaHostil());
        var texto = Sut.Convert(parseado, typeof(string), null, CulturaHostil());
        var reparseado = Sut.ConvertBack(texto, typeof(decimal), null, CulturaHostil());

        Assert.Equal(12.35m, reparseado);
    }
}
