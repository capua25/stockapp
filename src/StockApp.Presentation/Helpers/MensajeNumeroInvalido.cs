using StockApp.Domain.Formato;

namespace StockApp.Presentation.Helpers;

/// <summary>
/// Arma el mensaje de un monto/cantidad mal tipeado en un formulario: el mensaje propio del
/// campo ("El monto total no es un número válido.") más, cuando lo hay, el detalle de
/// <see cref="FormatoEsUy.TryParseDecimal"/> que dice CÓMO escribirlo ("Usá coma para los
/// decimales: 5,4"). Si el detalle es el genérico, no se repite.
/// </summary>
public static class MensajeNumeroInvalido
{
    public static string Armar(string mensajeDelCampo, string? detalle)
        => string.IsNullOrEmpty(detalle) || detalle == FormatoEsUy.MensajeNoEsNumero
            ? mensajeDelCampo
            : $"{mensajeDelCampo} {detalle}";
}
