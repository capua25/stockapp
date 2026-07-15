using StockApp.Application.Licenciamiento;
using StockApp.Licencias.Cli;
using Xunit;

namespace StockApp.Licencias.Cli.Tests;

public class RoundTripTests
{
    [Fact]
    public void GenerarClaves_ProduceUnParUsable()
    {
        var (privadaPem, publicaBase64) = GeneradorClaves.Generar();

        Assert.Contains("PRIVATE KEY", privadaPem);
        Assert.False(string.IsNullOrWhiteSpace(publicaBase64));
    }

    [Fact]
    public void LicenciaEmitidaPorLaCli_ValidaConLaClavePublica()
    {
        var (privada, publica) = GeneradorClaves.Generar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "Ferretería X", "A3F2-9B41", "2026-07-15"), privada);

        var resultado = new ValidadorFirma(publica).Verificar(licencia, out _);

        Assert.Equal(ResultadoVerificacion.Ok, resultado);
    }

    [Fact]
    public void TokenDeResetEmitidoPorLaCli_ValidaConLaClavePublica()
    {
        var (privada, publica) = GeneradorClaves.Generar();
        var token = FirmadorLicencias.EmitirTokenReset(
            new TokenResetPayload(1, "reset-admin", "A3F2-9B41", "nonce-1"), privada);

        var resultado = new ValidadorFirma(publica).Verificar(token, out _);

        Assert.Equal(ResultadoVerificacion.Ok, resultado);
    }

    [Fact]
    public void LicenciaDeUnPar_NoValidaConLaPublicaDeOtroPar()
    {
        var (privadaA, _) = GeneradorClaves.Generar();
        var (_, publicaB) = GeneradorClaves.Generar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "X", "MAQ", "2026-07-15"), privadaA);

        Assert.Equal(ResultadoVerificacion.FirmaInvalida,
            new ValidadorFirma(publicaB).Verificar(licencia, out _));
    }
}
