using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte un monto (<c>decimal</c>/<c>decimal?</c>) a su representación en pesos: símbolo
/// "$", separador de miles "." y coma decimal, con 2 decimales fijos (ej. 26400m →
/// "$ 26.400,00"; negativos anteponen el signo antes del símbolo, ej. -600m → "-$ 600,00").
///
/// Formato único es-UY de <see cref="FormatoEsUy"/> (decisión 2026-10-01): no depende de la
/// cultura del hilo/entorno ni de que el runtime tenga ICU.
///
/// Expuesto como instancia estática, igual que <see cref="DecimalOpcionalConverter"/> y
/// <see cref="FechaUtcALocalConverter"/>. Solo de LECTURA (grilla de solo lectura de
/// Valorización): <see cref="ConvertBack"/> no está soportado.
/// </summary>
public sealed class MonedaConverter : IValueConverter
{
    public static readonly MonedaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            decimal d => Formatear(d),
            null => string.Empty,
            _ => string.Empty,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Formatea un monto con el mismo criterio que las grillas (ej. 850.5000m → "$ 850,50"),
    /// para reutilizar en mensajes de confirmación/error de los ViewModels sin duplicar el
    /// formato es-UY (bug real: esos mensajes mostraban el decimal crudo, ej. "(799.5000)").
    /// </summary>
    public static string Formatear(decimal monto) => FormatoEsUy.Moneda(monto);
}
