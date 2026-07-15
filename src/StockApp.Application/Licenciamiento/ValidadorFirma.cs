using System.Security.Cryptography;
using System.Text;

namespace StockApp.Application.Licenciamiento;

/// <summary>Resultado de bajo nivel de verificar la firma de un string firmado.</summary>
public enum ResultadoVerificacion { Ok, FormatoInvalido, FirmaInvalida }

/// <summary>
/// Verifica la firma ECDSA P-256 de un string `base64url(payload).base64url(firma)` contra
/// una clave pública. NUNCA lanza: cualquier problema de formato o de firma se devuelve como
/// enum (fail-closed). La clave pública viene de configuración (o de la constante embebida).
/// </summary>
public sealed class ValidadorFirma
{
    private readonly string _clavePublicaBase64;

    public ValidadorFirma(string clavePublicaBase64) => _clavePublicaBase64 = clavePublicaBase64;

    public ResultadoVerificacion Verificar(string tokenFirmado, out byte[] payloadJson)
    {
        payloadJson = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(tokenFirmado))
            return ResultadoVerificacion.FormatoInvalido;

        var partes = tokenFirmado.Split('.');
        if (partes.Length != 2 || partes[0].Length == 0 || partes[1].Length == 0)
            return ResultadoVerificacion.FormatoInvalido;

        byte[] payload;
        byte[] firma;
        try
        {
            payload = CodificadorBase64Url.Decodificar(partes[0]);
            firma   = CodificadorBase64Url.Decodificar(partes[1]);
        }
        catch (FormatException)
        {
            return ResultadoVerificacion.FormatoInvalido;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(
                Convert.FromBase64String(_clavePublicaBase64), out _);

            var valida = ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(partes[0]), firma, HashAlgorithmName.SHA256);

            if (!valida)
                return ResultadoVerificacion.FirmaInvalida;

            payloadJson = payload;
            return ResultadoVerificacion.Ok;
        }
        catch (Exception)
        {
            // Clave pública basura / firma con longitud inválida: fail-closed.
            return ResultadoVerificacion.FirmaInvalida;
        }
    }
}
