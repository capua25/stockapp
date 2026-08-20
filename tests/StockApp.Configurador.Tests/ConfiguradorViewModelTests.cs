using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Configuracion;
using StockApp.Configurador.Servicios;
using StockApp.Configurador.ViewModels;
using Xunit;

namespace StockApp.Configurador.Tests;

public class ConfiguradorViewModelTests : IDisposable
{
    private readonly string _rutaArchivo =
        Path.Combine(Path.GetTempPath(), "configurador-vm-test-" + Guid.NewGuid() + ".json");

    public void Dispose()
    {
        if (File.Exists(_rutaArchivo))
        {
            File.Delete(_rutaArchivo);
        }
    }

    private ConfiguradorViewModel Crear(IProbadorConexion? probador = null) =>
        new(probador ?? Mock.Of<IProbadorConexion>(), _rutaArchivo);

    // ── Ruta mostrada en pantalla (requisito explícito del usuario) ───────────

    [Fact]
    public void RutaArchivo_ExponeLaRutaQueVaAEscribir()
    {
        var vm = Crear();

        Assert.Equal(_rutaArchivo, vm.RutaArchivo);
    }

    // ── Precarga de valores ────────────────────────────────────────────────────

    [Fact]
    public void AlConstruir_SinArchivoPrevio_PrecargaElDefaultUnico()
    {
        var vm = Crear();

        var uriDefault = new Uri(ConexionDefaults.UrlPorDefecto);
        Assert.Equal(uriDefault.Host, vm.Ip);
        Assert.Equal(uriDefault.Port.ToString(), vm.Puerto);
    }

    [Fact]
    public void AlConstruir_ConArchivoPrevio_PrecargaIpYPuertoGuardados()
    {
        ConexionConfigStore.Guardar("http://192.168.1.50:5080", _rutaArchivo);

        var vm = Crear();

        Assert.Equal("192.168.1.50", vm.Ip);
        Assert.Equal("5080", vm.Puerto);
    }

    // ── Guardar: escribe con el MISMO store que usa el desktop para leer ──────

    [Fact]
    public void GuardarCommand_EscribeLaUrlArmadaConIpYPuerto()
    {
        var vm = Crear();
        vm.Ip = "10.0.0.9";
        vm.Puerto = "6060";

        vm.GuardarCommand.Execute(null);

        Assert.Equal("http://10.0.0.9:6060", ConexionConfigStore.Leer(_rutaArchivo));
    }

    // ── Probar conexión: mapea los 3 resultados a mensaje + tono ───────────────

    [Fact]
    public async Task ProbarConexion_Ok_MuestraMensajeDeExitoYTonoExito()
    {
        var probadorMock = new Mock<IProbadorConexion>();
        probadorMock.Setup(p => p.ProbarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoPruebaConexion.Ok);
        var vm = Crear(probadorMock.Object);

        await vm.ProbarConexionCommand.ExecuteAsync(null);

        Assert.Equal("exito", vm.ClaseEstado);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeEstado));
    }

    [Fact]
    public async Task ProbarConexion_RespondeOtraCosa_MuestraTonoAdvertencia()
    {
        var probadorMock = new Mock<IProbadorConexion>();
        probadorMock.Setup(p => p.ProbarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoPruebaConexion.RespondeOtraCosa);
        var vm = Crear(probadorMock.Object);

        await vm.ProbarConexionCommand.ExecuteAsync(null);

        Assert.Equal("advertencia", vm.ClaseEstado);
    }

    [Fact]
    public async Task ProbarConexion_NoResponde_MuestraTonoPeligro()
    {
        var probadorMock = new Mock<IProbadorConexion>();
        probadorMock.Setup(p => p.ProbarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoPruebaConexion.NoResponde);
        var vm = Crear(probadorMock.Object);

        await vm.ProbarConexionCommand.ExecuteAsync(null);

        Assert.Equal("peligro", vm.ClaseEstado);
    }

    [Fact]
    public async Task ProbarConexion_ArmaLaUrlConIpYPuertoActuales()
    {
        var probadorMock = new Mock<IProbadorConexion>();
        probadorMock.Setup(p => p.ProbarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoPruebaConexion.Ok);
        var vm = Crear(probadorMock.Object);
        vm.Ip = "172.16.0.2";
        vm.Puerto = "9090";

        await vm.ProbarConexionCommand.ExecuteAsync(null);

        probadorMock.Verify(p => p.ProbarAsync("http://172.16.0.2:9090", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Cancelar: cierra sin escribir nada ──────────────────────────────────────

    [Fact]
    public void CancelarCommand_NoEscribeElArchivo()
    {
        var vm = Crear();
        vm.Ip = "1.2.3.4";
        vm.Puerto = "9999";

        vm.CancelarCommand.Execute(null);

        Assert.Null(ConexionConfigStore.Leer(_rutaArchivo));
    }

    [Fact]
    public void CancelarCommand_DisparaSolicitarCierre()
    {
        var vm = Crear();
        var disparado = false;
        vm.SolicitarCierre += (_, _) => disparado = true;

        vm.CancelarCommand.Execute(null);

        Assert.True(disparado);
    }
}
