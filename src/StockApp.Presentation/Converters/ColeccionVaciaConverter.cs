using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Devuelve <c>true</c> cuando la colección bindeada es nula o no tiene elementos.
/// Se usa para mostrar un empty state ("Sin resultados") en las grillas de
/// reportes. Expuesto como instancia estática para referenciarlo vía
/// <c>{x:Static conv:ColeccionVaciaConverter.Instance}</c>, igual que los
/// converters de Avalonia (<c>ObjectConverters</c>, <c>StringConverters</c>)
/// ya usados en el proyecto — no requiere registrarlo como recurso.
/// </summary>
public sealed class ColeccionVaciaConverter : IValueConverter
{
    public static readonly ColeccionVaciaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null => true,
            ICollection coleccion => coleccion.Count == 0,
            IEnumerable enumerable => !enumerable.GetEnumerator().MoveNext(),
            _ => false,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
