using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Notificaciones;
using StockApp.Infrastructure.Tests.TestInfra;
using Xunit;

namespace StockApp.Infrastructure.Tests.Notificaciones;

public class NotificadorWebhookTests
{
    private sealed class ConfiguracionAlertasRepositoryFake : IConfiguracionAlertasRepository
    {
        public ConfiguracionAlertas Configuracion { get; set; } = new();

        public Task<ConfiguracionAlertas> ObtenerAsync() => Task.FromResult(Configuracion);

        public Task GuardarAsync(ConfiguracionAlertas configuracion)
        {
            Configuracion = configuracion;
            return Task.CompletedTask;
        }
    }

    private static CorridaBackup Exitosa() => new()
    {
        IniciadaEn = DateTime.UtcNow.AddMinutes(-1),
        FinalizadaEn = DateTime.UtcNow,
        Resultado = ResultadoBackup.Exitosa,
        NombreArchivo = "backup_20260809_030000000.dump",
        TamanioBytes = 2048,
    };

    private static CorridaBackup Fallida(string motivo = "pg_dump: fallo simulado") => new()
    {
        IniciadaEn = DateTime.UtcNow.AddMinutes(-1),
        FinalizadaEn = DateTime.UtcNow,
        Resultado = ResultadoBackup.Fallida,
        MotivoFallo = motivo,
    };

    private static (NotificadorWebhook Sut, FakeHttpHandler Handler, ConfiguracionAlertasRepositoryFake Repo) Crear(
        string? url = "https://hc-ping.com/abc",
        bool habilitado = true,
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var handler = new FakeHttpHandler(responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var repo = new ConfiguracionAlertasRepositoryFake
        {
            Configuracion = new ConfiguracionAlertas { UrlWebhook = url, Habilitado = habilitado },
        };
        var sut = new NotificadorWebhook(
            new HttpClient(handler), repo, NullLogger<NotificadorWebhook>.Instance);
        return (sut, handler, repo);
    }

    [Fact]
    public async Task CorridaExitosa_PosteaALaUrlSinSufijo()
    {
        var (sut, handler, _) = Crear();

        await sut.NotificarCorridaBackupAsync(Exitosa());

        Assert.Equal(1, handler.Llamadas);
        Assert.Equal(HttpMethod.Post, handler.UltimaRequest!.Method);
        Assert.Equal("https://hc-ping.com/abc", handler.UltimaRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task CorridaFallida_PosteaAlSufijoFailConElMotivoEnElBody()
    {
        var (sut, handler, _) = Crear();

        await sut.NotificarCorridaBackupAsync(Fallida("pg_dump: server closed the connection"));

        Assert.Equal("https://hc-ping.com/abc/fail", handler.UltimaRequest!.RequestUri!.ToString());
        Assert.Contains("pg_dump: server closed the connection", handler.UltimoBody);
    }

    [Fact]
    public async Task UrlConBarraFinal_NoDuplicaLaBarraEnElSufijoFail()
    {
        var (sut, handler, _) = Crear(url: "https://hc-ping.com/abc/");

        await sut.NotificarCorridaBackupAsync(Fallida());

        Assert.Equal("https://hc-ping.com/abc/fail", handler.UltimaRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Deshabilitado_NoHaceNingunaLlamada()
    {
        var (sut, handler, _) = Crear(habilitado: false);

        await sut.NotificarCorridaBackupAsync(Fallida());

        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SinUrlConfigurada_NoHaceNingunaLlamada(string? url)
    {
        var (sut, handler, _) = Crear(url: url);

        await sut.NotificarCorridaBackupAsync(Fallida());

        Assert.Equal(0, handler.Llamadas);
    }

    [Fact]
    public async Task ElWebhookDevuelveError_NoPropagaExcepcion()
    {
        var (sut, _, _) = Crear(responder: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Record.ExceptionAsync(() => sut.NotificarCorridaBackupAsync(Fallida()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ElWebhookEstaCaido_NoPropagaExcepcion()
    {
        var (sut, _, _) = Crear(responder: _ => throw new HttpRequestException("sin red"));

        var ex = await Record.ExceptionAsync(() => sut.NotificarCorridaBackupAsync(Fallida()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task MotivoFalloMuyLargo_SeTruncaA2000Caracteres()
    {
        var (sut, handler, _) = Crear();

        await sut.NotificarCorridaBackupAsync(Fallida(new string('x', 5000)));

        Assert.Equal(2000, handler.UltimoBody!.Length);
    }

    [Fact]
    public async Task UrlConQueryString_InsertaFailEnElPathYPreservaElQuery()
    {
        var (sut, handler, _) = Crear(url: "https://hc-ping.com/abc?x=1");

        await sut.NotificarCorridaBackupAsync(Fallida());

        Assert.Equal("https://hc-ping.com/abc/fail?x=1", handler.UltimaRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task UrlConQueryString_EnExito_PosteaLaUrlTalCual()
    {
        var (sut, handler, _) = Crear(url: "https://hc-ping.com/abc?x=1");

        await sut.NotificarCorridaBackupAsync(Exitosa());

        Assert.Equal("https://hc-ping.com/abc?x=1", handler.UltimaRequest!.RequestUri!.ToString());
    }

    // ── ProbarPingAsync (fix CRÍTICO del review final: el verificador era un placebo) ──────
    //
    // Antes, "Probar" se construía sobre NotificarCorridaBackupAsync, que devuelve Task a secas y
    // se traga todo por contrato: la pantalla decía "se envió un ping de prueba" ante una URL con
    // typo (404), un check borrado, un DNS caído o el egress bloqueado por el firewall. Estos
    // tests son los que hacen que eso no pueda volver: cada uno afirma que el resultado REFLEJA lo
    // que pasó del otro lado.

    [Fact]
    public async Task ProbarPingAsync_WebhookResponde200_DevuelveExitosoConElStatusCodeReal()
    {
        var (sut, handler, _) = Crear();

        var resultado = await sut.ProbarPingAsync("https://hc-ping.com/abc");

        Assert.True(resultado.Exitoso);
        Assert.Equal(200, resultado.StatusCode);
        Assert.Equal(1, handler.Llamadas);
        Assert.Equal(HttpMethod.Post, handler.UltimaRequest!.Method);
    }

    [Fact]
    public async Task ProbarPingAsync_PosteaALaUrlIndicadaSinSufijoFail()
    {
        // Probar el canal no puede dejar el check en rojo: siempre es un ping de éxito.
        var (sut, handler, _) = Crear();

        await sut.ProbarPingAsync("https://hc-ping.com/abc");

        Assert.Equal("https://hc-ping.com/abc", handler.UltimaRequest!.RequestUri!.ToString());
        Assert.DoesNotContain("/fail", handler.UltimaRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task ProbarPingAsync_WebhookResponde404_DevuelveNoExitosoConElStatusCode()
    {
        // EL caso que motivó el fix: una URL con typo (o un check borrado en healthchecks)
        // respondía 404 y la pantalla igual decía "se envió un ping de prueba".
        var (sut, _, _) = Crear(responder: _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var resultado = await sut.ProbarPingAsync("https://hc-ping.com/typo");

        Assert.False(resultado.Exitoso);
        Assert.Equal(404, resultado.StatusCode);
    }

    [Fact]
    public async Task ProbarPingAsync_WebhookCaido_DevuelveNoExitosoSinStatusYNoPropaga()
    {
        var (sut, _, _) = Crear(responder: _ => throw new HttpRequestException("sin red"));

        var resultado = await sut.ProbarPingAsync("https://hc-ping.com/abc");

        Assert.False(resultado.Exitoso);
        Assert.Null(resultado.StatusCode);
        Assert.Contains("No se pudo contactar el webhook", resultado.Mensaje!);
    }

    [Fact]
    public async Task ProbarPingAsync_WebhookCaido_NoFiltraElMensajeDeLaExcepcionNiElBodyRemoto()
    {
        // Nota SSRF del spec: viaja el status code y un mensaje PROPIO, nunca nada que venga del
        // otro lado. Se responde con un body que sería jugoso si se filtrara.
        var (sut, _, _) = Crear(responder: _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("secreto-de-la-red-interna"),
        });

        var resultado = await sut.ProbarPingAsync("https://hc-ping.com/abc");

        Assert.False(resultado.Exitoso);
        Assert.DoesNotContain("secreto-de-la-red-interna", resultado.Mensaje!);
    }

    [Fact]
    public async Task ProbarPingAsync_UsaLaUrlIndicadaAunqueLaGuardadaSeaOtra()
    {
        // Fix IMPORTANTE del review final (I2): "Probar" tiene que probar lo que está EN PANTALLA,
        // no lo que hay en la base. Este notificador no lee la configuración en absoluto.
        var (sut, handler, _) = Crear(url: "https://hc-ping.com/vieja");

        await sut.ProbarPingAsync("https://hc-ping.com/nueva");

        Assert.Equal("https://hc-ping.com/nueva", handler.UltimaRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ProbarPingAsync_CanalDeshabilitado_IgualPingueaLaUrlIndicada()
    {
        // Probar ANTES de habilitar es el orden en que un humano configura esto por primera vez.
        var (sut, handler, _) = Crear(habilitado: false);

        var resultado = await sut.ProbarPingAsync("https://hc-ping.com/abc");

        Assert.Equal(1, handler.Llamadas);
        Assert.True(resultado.Exitoso);
    }
}
