using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Estado de un documento administrativo (el enum o su nombre) → texto legible, tono de su Insignia o "va en texto
/// terciario": Pendiente y Anulado neutros, En proceso advertencia ("En proceso"), Finalizado éxito; solo el Anulado
/// es inactivo.
/// </summary>
public sealed class EstadoDocumentoConverter : IValueConverter
{
    private enum Modo { Texto, Variante, Inactivo }

    public static readonly EstadoDocumentoConverter Texto = new(Modo.Texto);
    public static readonly EstadoDocumentoConverter Variante = new(Modo.Variante);
    public static readonly EstadoDocumentoConverter Inactivo = new(Modo.Inactivo);

    private readonly Modo _modo;

    private EstadoDocumentoConverter(Modo modo) => _modo = modo;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var estado = value switch
        {
            EstadoDocumento e => e,
            string s when Enum.TryParse<EstadoDocumento>(s, out var parseado) && Enum.IsDefined(parseado) => parseado,
            _ => (EstadoDocumento?)null,
        };

        return _modo switch
        {
            Modo.Texto => estado switch
            {
                EstadoDocumento.Pendiente => "Pendiente",
                EstadoDocumento.EnProceso => "En proceso",
                EstadoDocumento.Finalizado => "Finalizado",
                EstadoDocumento.Anulado => "Anulado",
                _ => string.Empty,
            },
            Modo.Variante => estado switch
            {
                EstadoDocumento.EnProceso => VarianteInsignia.Advertencia,
                EstadoDocumento.Finalizado => VarianteInsignia.Exito,
                _ => VarianteInsignia.Neutro,
            },
            _ => estado == EstadoDocumento.Anulado,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
