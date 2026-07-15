using System.Security.Cryptography;

namespace StockApp.Licencias.Cli;

/// <summary>Genera un par ECDSA P-256: privada en PEM (PKCS#8), pública en base64 (SubjectPublicKeyInfo).</summary>
public static class GeneradorClaves
{
    public static (string privadaPem, string publicaBase64) Generar()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privadaPem = ecdsa.ExportPkcs8PrivateKeyPem();
        var publicaBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        return (privadaPem, publicaBase64);
    }
}
