using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte una fecha persistida en UTC (<c>DateTime.UtcNow</c> al escribir, ver
/// <see cref="StockApp.Application.Movimientos.MovimientoStockService"/> y
/// <see cref="StockApp.Infrastructure.Repositories.MovimientoStockRepository"/>) a la hora
/// LOCAL de la máquina para mostrarla en la UI.
///
/// Necesario por un gotcha de EF Core + SQLite: al releer la columna, el <c>DateTime</c>
/// vuelve con <c>Kind=Unspecified</c> (se pierde la marca UTC), aunque el valor almacenado
/// sea un instante UTC real. Por eso se fuerza
/// <c>DateTime.SpecifyKind(valor, DateTimeKind.Utc)</c> ANTES de <c>.ToLocalTime()</c>: sin
/// este paso el offset esperado no se aplica de forma confiable. Sin este converter, un
/// usuario en UTC-3 (Argentina) veía todas las fechas +3 horas adelantadas respecto de la
/// hora real en que ocurrió el movimiento.
///
/// Expuesto como instancia estática, igual que <see cref="ColeccionVaciaConverter"/> y
/// <see cref="DecimalOpcionalConverter"/> — no requiere registrarlo como recurso. Solo de
/// LECTURA (se usa en bindings OneWay de grillas de solo lectura): <see cref="ConvertBack"/>
/// lanza.
/// </summary>
public sealed class FechaUtcALocalConverter : IValueConverter
{
    public static readonly FechaUtcALocalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime fecha
            ? DateTime.SpecifyKind(fecha, DateTimeKind.Utc).ToLocalTime()
            : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
