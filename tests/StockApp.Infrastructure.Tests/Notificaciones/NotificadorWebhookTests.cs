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
}
