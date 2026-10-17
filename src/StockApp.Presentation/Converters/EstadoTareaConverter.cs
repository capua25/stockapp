using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Estado de una tarea (el enum o su nombre) → texto legible, tono de su Insignia o "va en texto terciario": Pendiente y
/// Cancelada neutras, En curso advertencia ("En curso"), Terminada éxito; solo la Cancelada es inactiva.
/// </summary>
public sealed class EstadoTareaConverter : IValueConverter
{
    private enum Modo { Texto, Variante, Inactivo }

    public static readonly EstadoTareaConverter Texto = new(Modo.Texto);
    public static readonly EstadoTareaConverter Variante = new(Modo.Variante);
    public static readonly EstadoTareaConverter Inactivo = new(Modo.Inactivo);

    private readonly Modo _modo;

    private EstadoTareaConverter(Modo modo) => _modo = modo;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var estado = value switch
        {
            EstadoTarea e => e,
            string s when Enum.TryParse<EstadoTarea>(s, out var parseado) && Enum.IsDefined(parseado) => parseado,
            _ => (EstadoTarea?)null,
        };

        return _modo switch
        {
            Modo.Texto => estado switch
            {
                EstadoTarea.Pendiente => "Pendiente",
                EstadoTarea.EnCurso => "En curso",
                EstadoTarea.Terminada => "Terminada",
                EstadoTarea.Cancelada => "Cancelada",
                _ => string.Empty,
            },
            Modo.Variante => estado switch
            {
                EstadoTarea.EnCurso => VarianteInsignia.Advertencia,
                EstadoTarea.Terminada => VarianteInsignia.Exito,
                _ => VarianteInsignia.Neutro,
            },
            _ => estado == EstadoTarea.Cancelada,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
