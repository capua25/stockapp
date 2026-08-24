using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte entre <c>decimal</c> (NO nullable) y el <c>string</c> que muestra/edita una celda de
/// <c>DataGridTextColumn</c> o un <c>TextBox</c> — cultura fija con PUNTO decimal en vez de coma,
/// para la grilla de renglones y la zona de carga de "Ingreso de
/// stock por factura" (Cantidad/Precio unitario/Subtotal).
///
/// Bug real (verificación visual, 2026-08-24): en la MISMA fila de la grilla de renglones,
/// "Precio unitario" mostraba "5,4" (coma, vía el extinto <c>DecimalConverter</c>, que ahí fijaba
/// es-UY) mientras "Subtotal" mostraba "27.0" (punto: esa columna NO tenía converter y caía en el
/// binding default de Avalonia bajo la cultura AMBIENTE del hilo, distinta de es-UY en esta
/// máquina). Decisión de producto: unificar TODA la grilla con PUNTO — no coma.
///
/// Por qué un converter NUEVO en vez de cambiarle la cultura al <c>DecimalConverter</c> de
/// entonces: ese converter también estaba cableado a <c>NuevoProductoPrecioVenta</c> ("Precio de
/// venta" del alta rápida de producto, dentro de esta misma vista) — un campo fuera de alcance a
/// propósito en ese momento (había otro frente de trabajo en paralelo sobre precio de venta).
/// Cambiarle la cultura a <c>DecimalConverter</c> hubiera cambiado también el formato de ESE
/// campo, sin que nadie lo pidiera. Este converter separado dejaba "Precio de venta" intacto
/// (seguía con coma/es-UY) y solo tocaba Cantidad/Precio unitario/Subtotal de la grilla y
/// Cantidad/Precio unitario de la zona de carga (los campos que alimentan esas mismas columnas).
/// Ese frente en paralelo resultó ser la eliminación completa de <c>Producto.PrecioVenta</c>
/// (el cliente no vende); al desaparecer el campo, <c>DecimalConverter</c> quedó sin consumidor
/// y se borró. Este converter no se vio afectado: nunca tuvo relación con PrecioVenta.
///
/// Cultura FIJA: <see cref="CultureInfo.InvariantCulture"/> (punto decimal, sin separador de
/// miles en el formato "G" que usa <c>ToString(IFormatProvider)</c> sin especificador).
/// <c>NumberStyles</c>: <c>AllowDecimalPoint | AllowLeadingSign</c> (SIN <c>AllowThousands</c>) —
/// un valor tipeado nunca se cuela como miles, sin importar la cultura ambiente de la máquina (ver
/// <c>ClickReal_ZonaDeCarga_PuntoDecimal_CulturaAmbienteHostilEsUy_...</c> en
/// IngresoPorFacturaViewTests.cs, que verifica esto bajo cultura ambiente es-UY explícita, donde
/// el punto sería tradicionalmente separador de miles).
/// </summary>
public sealed class DecimalPuntoConverter : IValueConverter
{
    public static readonly DecimalPuntoConverter Instance = new();

    private static readonly IFormatProvider CulturaFija = CultureInfo.InvariantCulture;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? d.ToString(CulturaFija) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texto = value as string;
        if (string.IsNullOrWhiteSpace(texto))
            return new BindingNotification(
                new FormatException("El valor ingresado no puede estar vacío."),
                BindingErrorType.Error);

        if (decimal.TryParse(
                texto,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CulturaFija,
                out var resultado))
            return resultado;

        return new BindingNotification(
            new FormatException("El valor ingresado no es un número válido."),
            BindingErrorType.Error);
    }
}
