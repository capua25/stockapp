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
        Assert.False(vm.UsarHttps);
    }

    // ── Round-trip esquema/puerto: cargar y volver a guardar da el mismo string ──

    [Fact]
    public void AlConstruir_ConHttpsSinPuerto_PrecargaLosTresCamposYGuardaIdentico()
    {
        ConexionConfigStore.Guardar("https://stockapp.capuanomartin.dev", _rutaArchivo);

        var vm = Crear();

        Assert.Equal("stockapp.capuanomartin.dev", vm.Ip);
        Assert.Equal(string.Empty, vm.Puerto);
        Assert.True(vm.UsarHttps);

        vm.GuardarCommand.Execute(null);

        Assert.Equal("https://stockapp.capuanomartin.dev", ConexionConfigStore.Leer(_rutaArchivo));
    }

    [Fact]
    public void AlConstruir_ConHttpsYPuerto_PrecargaLosTresCamposYGuardaIdentico()
    {
        ConexionConfigStore.Guardar("https://stockapp.capuanomartin.dev:8080", _rutaArchivo);

        var vm = Crear();

        Assert.Equal("stockapp.capuanomartin.dev", vm.Ip);
        Assert.Equal("8080", vm.Puerto);
        Assert.True(vm.UsarHttps);

        vm.GuardarCommand.Execute(null);

        Assert.Equal("https://stockapp.capuanomartin.dev:8080", ConexionConfigStore.Leer(_rutaArchivo));
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

    [Fact]
    public void GuardarCommand_ConHttpsYPuerto_EscribeConEsquemaHttps()
    {
        var vm = Crear();
        vm.Ip = "10.0.0.9";
        vm.Puerto = "8080";
        vm.UsarHttps = true;

        vm.GuardarCommand.Execute(null);

        Assert.Equal("https://10.0.0.9:8080", ConexionConfigStore.Leer(_rutaArchivo));
    }

    [Fact]
    public void GuardarCommand_ConHttpsYPuertoVacio_NoDejaDosPuntosColgando()
    {
        var vm = Crear();
        vm.Ip = "stockapp.capuanomartin.dev";
        vm.Puerto = "";
        vm.UsarHttps = true;

        vm.GuardarCommand.Execute(null);

        var guardado = ConexionConfigStore.Leer(_rutaArchivo);
        Assert.Equal("https://stockapp.capuanomartin.dev", guardado);
        Assert.False(guardado!.EndsWith(':'));
    }

    [Fact]
    public void GuardarCommand_ConHttpYPuertoVacio_NoDejaDosPuntosColgando()
    {
        // Este es el bug encontrado: con Puerto vacío, la versión vieja de ConstruirUrl()
        // producía "http://host:" (dos puntos colgando) y Guardar lo persistía tal cual, sin
        // validar. ConexionConfigStore.Guardar no valida nada — la garantía tiene que venir
        // del VM antes de llamarlo.
        var vm = Crear();
        vm.Ip = "stockapp.capuanomartin.dev";
        vm.Puerto = "";

        vm.GuardarCommand.Execute(null);

        var guardado = ConexionConfigStore.Leer(_rutaArchivo);
        Assert.Equal("http://stockapp.capuanomartin.dev", guardado);
        Assert.False(guardado!.EndsWith(':'));
        Assert.True(Uri.TryCreate(guardado, UriKind.Absolute, out _));
    }

    [Fact]
    public void GuardarCommand_ConHostname_ArmaLaUrlCorrectamente()
    {
        var vm = Crear();
        vm.Ip = "stockapp.capuanomartin.dev";
        vm.Puerto = "8080";
        vm.UsarHttps = true;

        vm.GuardarCommand.Execute(null);

        Assert.Equal("https://stockapp.capuanomartin.dev:8080", ConexionConfigStore.Leer(_rutaArchivo));
    }

    [Fact]
    public void GuardarCommand_ConHostVacio_NoGuardaYMuestraError()
    {
        var vm = Crear();
        vm.Ip = "   ";
        vm.Puerto = "8080";

        vm.GuardarCommand.Execute(null);

        Assert.Null(ConexionConfigStore.Leer(_rutaArchivo));
        Assert.Equal("peligro", vm.ClaseEstado);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeEstado));
    }

    [Fact]
    public void GuardarCommand_ConHostConEspacios_LoTrimea()
    {
        var vm = Crear();
        vm.Ip = "  10.0.0.9  ";
        vm.Puerto = "6060";

        vm.GuardarCommand.Execute(null);

        Assert.Equal("http://10.0.0.9:6060", ConexionConfigStore.Leer(_rutaArchivo));
    }

    [Fact]
    public void GuardarCommand_ConHostConEspacioInterno_NoGuardaYMuestraError()
    {
        // Trim() solo saca espacios en los extremos; un espacio en el MEDIO del host sigue
        // ahí y Uri.TryCreate lo rechaza (verificado: "http://my host" => TryCreate false).
        // Esta es la razón real de validar con Uri.TryCreate en vez de conformarse con los
        // checks de host-vacío y puerto-numérico.
        var vm = Crear();
        vm.Ip = "my host";
        vm.Puerto = "8080";

        vm.GuardarCommand.Execute(null);

        Assert.Null(ConexionConfigStore.Leer(_rutaArchivo));
        Assert.Equal("peligro", vm.ClaseEstado);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeEstado));
    }

    [Fact]
    public void GuardarCommand_ConPuertoNoNumerico_NoGuardaYMuestraError()
    {
        var vm = Crear();
        vm.Ip = "10.0.0.9";
        vm.Puerto = "abc";

        vm.GuardarCommand.Execute(null);

        Assert.Null(ConexionConfigStore.Leer(_rutaArchivo));
        Assert.Equal("peligro", vm.ClaseEstado);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeEstado));
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
