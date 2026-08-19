using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte <c>GrupoNavegacion.EstaExpandido</c> (Tanda 5, sidebar colapsable) en el ícono
/// mdi del chevron del header de grupo: apuntando abajo cuando está expandido, a la derecha
/// cuando está colapsado. Expuesto como instancia estática, igual que
/// <see cref="ActivoOpacidadConverter"/> y el resto de los converters de esta carpeta —
/// se referencia con <c>{x:Static conv:IconoChevronConverter.Instance}</c>, no como recurso
/// de <c>StaticResource</c>.
/// </summary>
public sealed class IconoChevronConverter : IValueConverter
{
    public static readonly IconoChevronConverter Instance = new();

    private const string IconoExpandido = "mdi-chevron-down";
    private const string IconoColapsado = "mdi-chevron-right";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? IconoExpandido : IconoColapsado;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
