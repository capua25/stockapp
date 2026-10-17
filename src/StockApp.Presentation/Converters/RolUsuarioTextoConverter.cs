using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.Converters;

/// <summary>El rol como lo lee el usuario: "Administrador" (el enum se llama Admin) y "Operador".</summary>
public sealed class RolUsuarioTextoConverter : IValueConverter
{
    public static readonly RolUsuarioTextoConverter Instance = new();

    public static string Formatear(RolUsuario rol) => rol == RolUsuario.Admin ? "Administrador" : "Operador";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RolUsuario rol ? Formatear(rol) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
