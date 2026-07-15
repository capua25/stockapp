using System.Security.Cryptography;
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Application.Tests.Licenciamiento;

public class ServicioLicenciaTests
{
    private const string Maquina = "AAAA-BBBB-CCCC";

    private sealed class FingerprintFake : IFingerprintMaquina
    {
        private readonly Func<string> _codigo;
        public FingerprintFake(string codigo) => _codigo = () => codigo;
        public FingerprintFake(Func<string> codigo) => _codigo = codigo;
        public string CodigoAgrupado => _codigo();
    }

    private sealed class AlmacenFake : IAlmacenLicencia
    {
        public string? Guardado { get; private set; }
        public AlmacenFake(string? inicial = null) => Guardado = inicial;
        public Task<string?> LeerAsync() => Task.FromResult(Guardado);
        public Task GuardarAsync(string licencia) { Guardado = licencia; return Task.CompletedTask; }
    }

    private static (ValidadorFirma validador, string privadaPem) CrearCripto()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var validador = new ValidadorFirma(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return (validador, ecdsa.ExportPkcs8PrivateKeyPem());
    }

    private static string EmitirLicencia(string privadaPem, string maquina)
        => FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "Ferretería X", maquina, "2026-07-15"), privadaPem);

    [Fact]
    public void Validar_LicenciaValidaParaEstaMaquina_DevuelveValida()
    {
        var (validador, privada) = CrearCripto();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), new EstadoLicencia());

        Assert.Equal(ResultadoValidacionLicencia.Valida,
            servicio.Validar(EmitirLicencia(privada, Maquina)));
    }

    [Fact]
    public void Validar_LicenciaDeOtraMaquina_DevuelveMaquinaDistinta()
    {
        var (validador, privada) = CrearCripto();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), new EstadoLicencia());

        Assert.Equal(ResultadoValidacionLicencia.MaquinaDistinta,
            servicio.Validar(EmitirLicencia(privada, "OTRA-MAQUINA")));
    }

    [Fact]
    public void Validar_FirmadaConOtraClave_DevuelveFirmaInvalida()
    {
        var (validador, _) = CrearCripto();
        var (_, otraPrivada) = CrearCripto();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), new EstadoLicencia());

        Assert.Equal(ResultadoValidacionLicencia.FirmaInvalida,
            servicio.Validar(EmitirLicencia(otraPrivada, Maquina)));
    }

    [Fact]
    public void Validar_FormatoRoto_DevuelveFormatoInvalido()
    {
        var (validador, _) = CrearCripto();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), new EstadoLicencia());

        Assert.Equal(ResultadoValidacionLicencia.FormatoInvalido, servicio.Validar("basura"));
    }

    [Fact]
    public async Task ActivarAsync_LicenciaValida_PersisteYActivaEstado()
    {
        var (validador, privada) = CrearCripto();
        var almacen = new AlmacenFake();
        var estado = new EstadoLicencia();
        var servicio = new ServicioLicencia(validador, new FingerprintFake(Maquina), almacen, estado);

        var licencia = EmitirLicencia(privada, Maquina);
        var resultado = await servicio.ActivarAsync(licencia);

        Assert.Equal(ResultadoValidacionLicencia.Valida, resultado);
        Assert.True(estado.Activada);
        Assert.Equal(Maquina, estado.CodigoMaquina);
        Assert.Equal(licencia, almacen.Guardado);
    }

    [Fact]
    public async Task ActivarAsync_LicenciaInvalida_NoPersisteNiActiva()
    {
        var (validador, privada) = CrearCripto();
        var almacen = new AlmacenFake();
        var estado = new EstadoLicencia();
        var servicio = new ServicioLicencia(validador, new FingerprintFake(Maquina), almacen, estado);

        var resultado = await servicio.ActivarAsync(EmitirLicencia(privada, "OTRA"));

        Assert.Equal(ResultadoValidacionLicencia.MaquinaDistinta, resultado);
        Assert.False(estado.Activada);
        Assert.Null(almacen.Guardado);
    }

    [Fact]
    public async Task CargarAlArranqueAsync_ConLicenciaValidaGuardada_DejaEstadoActivado()
    {
        var (validador, privada) = CrearCripto();
        var estado = new EstadoLicencia();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(EmitirLicencia(privada, Maquina)), estado);

        await servicio.CargarAlArranqueAsync();

        Assert.True(estado.Activada);
        Assert.Equal(Maquina, estado.CodigoMaquina);
    }

    [Fact]
    public async Task CargarAlArranqueAsync_SinLicencia_DejaEstadoBloqueadoPeroConCodigo()
    {
        var (validador, _) = CrearCripto();
        var estado = new EstadoLicencia();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), estado);

        await servicio.CargarAlArranqueAsync();

        Assert.False(estado.Activada);
        Assert.Equal(Maquina, estado.CodigoMaquina);
    }

    [Fact]
    public async Task CargarAlArranqueAsync_FingerprintIlegible_NoCrasheaYQuedaBloqueado()
    {
        var (validador, _) = CrearCripto();
        var estado = new EstadoLicencia();
        var fingerprintRoto = new FingerprintFake(() => throw new InvalidOperationException("registro ilegible"));
        var servicio = new ServicioLicencia(validador, fingerprintRoto, new AlmacenFake(), estado);

        await servicio.CargarAlArranqueAsync(); // no debe lanzar

        Assert.False(estado.Activada);
        Assert.Equal("", estado.CodigoMaquina);
    }
}
