using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Logs;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class LogsEndpointTests : ApiTestBase
{
    public LogsEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteCon(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Con la unificacion del hallazgo "dos fuentes de verdad", LogsEndpoints ahora resuelve
    // el directorio via DirectorioLogsResolver (config primero, IUserDataPathProvider como
    // fallback). ApiFactory setea Logs:Directorio, asi que los tests tienen que sembrar ahi,
    // no en el directorio del fake de IUserDataPathProvider (que ya no gana).
    private string DirectorioLogs() =>
        Factory.Services.GetRequiredService<IConfiguration>()["Logs:Directorio"]!;

    private void SembrarLog(string nombre, string contenido)
    {
        var dir = DirectorioLogs();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, nombre), contenido);
    }

    private void LimpiarLogs()
    {
        var dir = DirectorioLogs();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task GetLogs_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLogs_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador()).GetAsync("/logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetLogs_ConTokenAdminYSinArchivos_Devuelve200ConResumenVacio()
    {
        LimpiarLogs();

        var response = await ClienteCon(TokenAdmin()).GetAsync("/logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resumen = await response.Content.ReadFromJsonAsync<ResumenLogsDto>();
        Assert.NotNull(resumen);
        Assert.Equal(0, resumen!.CantidadArchivos);
    }

    [Fact]
    public async Task GetLogs_ConArchivos_DevuelveCantidadYTamanio()
    {
        LimpiarLogs();
        SembrarLog("stockapp-20260728.log", "warn uno");
        SembrarLog("stockapp-20260729.log", "warn dos");

        var response = await ClienteCon(TokenAdmin()).GetAsync("/logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resumen = await response.Content.ReadFromJsonAsync<ResumenLogsDto>();
        Assert.Equal(2, resumen!.CantidadArchivos);
        Assert.Equal(16, resumen.TamanioTotalBytes);

        LimpiarLogs();
    }

    [Fact]
    public async Task GetContenido_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/logs/contenido");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador()).GetAsync("/logs/contenido");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_SinArchivos_Devuelve404()
    {
        LimpiarLogs();

        var response = await ClienteCon(TokenAdmin()).GetAsync("/logs/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_ConArchivos_DevuelveZipConTodosLosArchivos()
    {
        LimpiarLogs();
        SembrarLog("stockapp-20260728.log", "contenido uno");
        SembrarLog("stockapp-20260729.log", "contenido dos");

        var response = await ClienteCon(TokenAdmin()).GetAsync("/logs/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(2, zip.Entries.Count);
        Assert.Contains(zip.Entries, e => e.Name == "stockapp-20260728.log");
        Assert.Contains(zip.Entries, e => e.Name == "stockapp-20260729.log");

        LimpiarLogs();
    }

    [Fact]
    public async Task GetLogs_ConLicenciaVencida_Devuelve200()
    {
        LimpiarLogs();
        var estado = Factory.Services.GetRequiredService<EstadoLicencia>();
        estado.Activada = false;
        try
        {
            var response = await ClienteCon(TokenAdmin()).GetAsync("/logs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            estado.Activada = true;
        }
    }
}
