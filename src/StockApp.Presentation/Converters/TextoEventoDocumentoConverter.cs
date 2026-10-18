using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Evento del historial de un documento → su texto para mostrar. El servicio guarda el cambio de estado con el enum crudo
/// ("Pendiente → EnProceso"): acá se reemplaza ese encabezado por el texto legible que la app ya usa para el estado
/// (<see cref="EstadoDocumentoConverter.Texto"/>, "En proceso"), conservando lo que siga (el motivo). Los eventos que no
/// arrancan con ese encabezado (notas, "Anulado: motivo", adjuntos) se muestran tal cual. Solo de LECTURA.
/// </summary>
public sealed class TextoEventoDocumentoConverter : IValueConverter
{
    public static readonly TextoEventoDocumentoConverter Instance = new();

    private const string Flecha = " → ";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EventoDocumento evento)
            return string.Empty;

        if (evento.EstadoAnterior is { } anterior && evento.EstadoNuevo is { } nuevo)
        {
            var crudo = $"{anterior}{Flecha}{nuevo}";
            if (evento.Texto.StartsWith(crudo, StringComparison.Ordinal))
                return $"{Legible(anterior)}{Flecha}{Legible(nuevo)}{evento.Texto[crudo.Length..]}";
        }

        return evento.Texto;
    }

    private static string Legible(object estado)
        => EstadoDocumentoConverter.Texto.Convert(estado, typeof(string), null, CultureInfo.InvariantCulture) as string ?? estado.ToString()!;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
