using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Estado de un proyecto → texto legible, tono de su Insignia o "va en texto terciario". Mismo criterio que
/// <see cref="EstadoTareaConverter"/>: Pendiente y Cancelado neutros, En curso advertencia, Terminado éxito; solo el
/// Cancelado es inactivo.
/// </summary>
public sealed class EstadoProyectoConverter : IValueConverter
{
    private enum Modo { Texto, Variante, Inactivo }

    public static readonly EstadoProyectoConverter Texto = new(Modo.Texto);
    public static readonly EstadoProyectoConverter Variante = new(Modo.Variante);
    public static readonly EstadoProyectoConverter Inactivo = new(Modo.Inactivo);

    private readonly Modo _modo;

    private EstadoProyectoConverter(Modo modo) => _modo = modo;

    /// <summary>Texto del estado, para usar fuera de un binding (filas y textos armados en el ViewModel).</summary>
    public static string TextoDe(EstadoProyecto? estado) => estado switch
    {
        EstadoProyecto.Pendiente => "Pendiente",
        EstadoProyecto.EnCurso => "En curso",
        EstadoProyecto.Terminado => "Terminado",
        EstadoProyecto.Cancelado => "Cancelado",
        _ => string.Empty,
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var estado = value as EstadoProyecto?;

        return _modo switch
        {
            Modo.Texto => TextoDe(estado),
            Modo.Variante => estado switch
            {
                EstadoProyecto.EnCurso => VarianteInsignia.Advertencia,
                EstadoProyecto.Terminado => VarianteInsignia.Exito,
                _ => VarianteInsignia.Neutro,
            },
            _ => estado == EstadoProyecto.Cancelado,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
