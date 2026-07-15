using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Emite strings firmados `base64url(payload).base64url(firma)` con ECDSA P-256.
/// Lo reutiliza la CLI del desarrollador (tools/StockApp.Licencias.Cli) — un solo formato
/// que no puede divergir del que valida <see cref="ValidadorFirma"/>.
/// </summary>
public static class FirmadorLicencias
{
    public static string EmitirLicencia(LicenciaPayload payload, string clavePrivadaPem)
        => Firmar(payload, clavePrivadaPem);

    public static string EmitirTokenReset(TokenResetPayload payload, string clavePrivadaPem)
        => Firmar(payload, clavePrivadaPem);

    private static string Firmar<T>(T payload, string clavePrivadaPem)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        var segmentoPayload = CodificadorBase64Url.Codificar(json);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(clavePrivadaPem);

        // La firma se calcula sobre los bytes UTF-8 del segmento base64url del payload,
        // no sobre el JSON crudo: el validador re-verifica sobre ese mismo segmento.
        var firma = ecdsa.SignData(
            Encoding.UTF8.GetBytes(segmentoPayload), HashAlgorithmName.SHA256);

        return segmentoPayload + "." + CodificadorBase64Url.Codificar(firma);
    }
}
