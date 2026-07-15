using System.Security.Cryptography;
using System.Text.Json;
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Application.Tests.Licenciamiento;

public class ValidadorFirmaTests
{
    // Par de claves EFÍMERO por corrida: determinístico dentro del test, sin claves
    // hardcodeadas. La privada firma, la pública valida — exactamente el flujo real.
    private static (string publicaBase64, string privadaPem) CrearPar()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publica = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        var privada = ecdsa.ExportPkcs8PrivateKeyPem();
        return (publica, privada);
    }

    [Fact]
    public void Verificar_LicenciaBienFirmada_DevuelveOkYPayload()
    {
        var (publica, privada) = CrearPar();
        var payload = new LicenciaPayload(1, "Ferretería X", "A3F2-9B41", "2026-07-15");
        var licencia = FirmadorLicencias.EmitirLicencia(payload, privada);

        var validador = new ValidadorFirma(publica);
        var resultado = validador.Verificar(licencia, out var payloadJson);

        Assert.Equal(ResultadoVerificacion.Ok, resultado);
        var decodificado = JsonSerializer.Deserialize<LicenciaPayload>(payloadJson);
        Assert.Equal("Ferretería X", decodificado!.Cliente);
        Assert.Equal("A3F2-9B41", decodificado.Maquina);
    }

    [Fact]
    public void Verificar_TokenResetBienFirmado_DevuelveOk()
    {
        var (publica, privada) = CrearPar();
        var payload = new TokenResetPayload(1, "reset-admin", "A3F2-9B41", "nonce-123");
        var token = FirmadorLicencias.EmitirTokenReset(payload, privada);

        var resultado = new ValidadorFirma(publica).Verificar(token, out var payloadJson);

        Assert.Equal(ResultadoVerificacion.Ok, resultado);
        var decodificado = JsonSerializer.Deserialize<TokenResetPayload>(payloadJson);
        Assert.Equal("reset-admin", decodificado!.Accion);
        Assert.Equal("nonce-123", decodificado.Desafio);
    }

    [Fact]
    public void Verificar_FirmadaConOtraClave_DevuelveFirmaInvalida()
    {
        var (_, privada) = CrearPar();
        var (otraPublica, _) = CrearPar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "X", "MAQ", "2026-07-15"), privada);

        var resultado = new ValidadorFirma(otraPublica).Verificar(licencia, out _);

        Assert.Equal(ResultadoVerificacion.FirmaInvalida, resultado);
    }

    [Fact]
    public void Verificar_PayloadAdulterado_DevuelveFirmaInvalida()
    {
        var (publica, privada) = CrearPar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "X", "MAQ", "2026-07-15"), privada);

        // Adulterar el payload conservando la firma original.
        var partes = licencia.Split('.');
        var payloadFalso = CodificadorBase64Url.Codificar(
            System.Text.Encoding.UTF8.GetBytes("{\"ver\":1,\"cliente\":\"HACK\",\"maquina\":\"MAQ\",\"emitida\":\"2026-07-15\"}"));
        var adulterada = payloadFalso + "." + partes[1];

        var resultado = new ValidadorFirma(publica).Verificar(adulterada, out _);

        Assert.Equal(ResultadoVerificacion.FirmaInvalida, resultado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-punto")]
    [InlineData("demasiados.puntos.aca")]
    [InlineData(".")]
    [InlineData("no-base64-url-válido!.firma")]
    public void Verificar_FormatoRoto_DevuelveFormatoInvalido(string entrada)
    {
        var (publica, _) = CrearPar();

        var resultado = new ValidadorFirma(publica).Verificar(entrada, out _);

        Assert.Equal(ResultadoVerificacion.FormatoInvalido, resultado);
    }

    [Fact]
    public void Verificar_ClavePublicaBasura_DevuelveFirmaInvalidaSinLanzar()
    {
        var (_, privada) = CrearPar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "X", "MAQ", "2026-07-15"), privada);

        // Clave pública inválida (no es SubjectPublicKeyInfo): fail-closed, sin excepción.
        var resultado = new ValidadorFirma("no-es-una-clave").Verificar(licencia, out _);

        Assert.Equal(ResultadoVerificacion.FirmaInvalida, resultado);
    }
}
