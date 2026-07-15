using System.Security.Cryptography;
using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Par de claves ECDSA P-256 fijo por proceso de test: la MISMA clave configura la API
/// (pública) y firma las licencias/tokens que los tests emiten (privada). El código de
/// máquina es fijo y coincide con el que devuelve FingerprintMaquinaFake.
/// </summary>
public static class ClavesDePrueba
{
    public const string CodigoMaquina = "TEST-MAQUINA-0001";

    private static readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static string ClavePublicaBase64 { get; } =
        Convert.ToBase64String(_ecdsa.ExportSubjectPublicKeyInfo());

    public static string ClavePrivadaPem { get; } = _ecdsa.ExportPkcs8PrivateKeyPem();

    public static string EmitirLicencia(string maquina = CodigoMaquina, string cliente = "Ferretería Test")
        => FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, cliente, maquina, "2026-07-15"), ClavePrivadaPem);

    public static string EmitirTokenReset(string desafio, string maquina = CodigoMaquina)
        => FirmadorLicencias.EmitirTokenReset(
            new TokenResetPayload(1, "reset-admin", maquina, desafio), ClavePrivadaPem);
}
