using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.Views.Reportes;

/// <summary>Texto legible de la acción auditada para la columna ACCIÓN del Log de auditoría (el mismo que usa su PDF).</summary>
public sealed class AccionAuditadaConverter : IValueConverter
{
    public static readonly AccionAuditadaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AccionAuditada accion ? TextoAccionAuditada.De(accion) : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
