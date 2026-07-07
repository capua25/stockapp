using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte el flag <c>Activo</c> de una entidad de catálogo (Categoría/Proveedor/
/// UnidadMedida) en la opacidad de su fila en el listado: entidades dadas de baja se
/// muestran atenuadas (decisión de UX del fix del crash al dar de baja — ver
/// UnidadMedidaListView/CategoriaListView/ProveedorListView). Expuesto como instancia
/// estática, igual que <see cref="DecimalOpcionalConverter"/> y <see cref="ColeccionVaciaConverter"/>.
/// </summary>
public sealed class ActivoOpacidadConverter : IValueConverter
{
    public static readonly ActivoOpacidadConverter Instance = new();

    private const double OpacidadCompleta = 1.0;
    private const double OpacidadAtenuada = 0.55;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool activo && !activo ? OpacidadAtenuada : OpacidadCompleta;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
