namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Base64url (RFC 4648 §5): base64 sin padding, con '+'→'-' y '/'→'_'. Es el alfabeto
/// seguro para un string pegable a mano que no se rompe al copiar por chat/mail.
/// </summary>
public static class CodificadorBase64Url
{
    public static string Codificar(byte[] datos)
        => Convert.ToBase64String(datos)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static byte[] Decodificar(string texto)
    {
        var normalizado = texto.Replace('-', '+').Replace('_', '/');
        var relleno = normalizado.Length % 4;
        if (relleno == 2) normalizado += "==";
        else if (relleno == 3) normalizado += "=";
        else if (relleno == 1) throw new FormatException("Longitud base64url inválida.");
        return Convert.FromBase64String(normalizado);
    }
}
