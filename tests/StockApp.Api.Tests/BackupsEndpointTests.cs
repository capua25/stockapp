using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Backups;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Platform;
using Xunit;

namespace StockApp.Api.Tests;

public class BackupsEndpointTests : ApiTestBase
{
    public BackupsEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private async Task<CorridaBackup> SembrarCorridaExitosaConArchivoAsync(byte[]? contenido = null)
    {
        var paths = Factory.Services.GetRequiredService<IUserDataPathProvider>();
        var directorio = paths.GetBackupsDirectory();
        Directory.CreateDirectory(directorio);
        var nombreArchivo = $"backup_test_{Guid.NewGuid():N}.dump";
        File.WriteAllBytes(Path.Combine(directorio, nombreArchivo), contenido ?? new byte[] { 1, 2, 3 });

        await using var ctx = Factory.CrearContexto();
        var corrida = new CorridaBackup
        {
            IniciadaEn = DateTime.UtcNow.AddMinutes(-1), FinalizadaEn = DateTime.UtcNow,
            Resultado = ResultadoBackup.Exitosa, NombreArchivo = nombreArchivo, TamanioBytes = 3,
        };
        ctx.CorridasBackup.Add(corrida);
        await ctx.SaveChangesAsync();
        return corrida;
    }

    // ── GET /backups ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBackups_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/backups");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetBackups_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/backups");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetBackups_ConTokenAdmin_Devuelve200ConLaLista()
    {
        await SembrarCorridaExitosaConArchivoAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/backups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<CorridaBackupDto>>();
        Assert.Single(lista!);
        Assert.Equal("Exitosa", lista![0].Resultado);
    }

    // ── GET /backups/{id}/contenido ─────────────────────────────────────────

    [Fact]
    public async Task GetContenido_ConTokenAdmin_DevuelveLosBytesRealesYElNombreDeArchivo()
    {
        var corrida = await SembrarCorridaExitosaConArchivoAsync(new byte[] { 9, 8, 7, 6 });
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync($"/backups/{corrida.Id}/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, bytes);
        Assert.Equal(corrida.NombreArchivo, response.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task GetContenido_IdInexistente_Devuelve404()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/backups/999999/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_CorridaFallidaSinArchivo_Devuelve404()
    {
        await using var ctx = Factory.CrearContexto();
        var corrida = new CorridaBackup
        {
            IniciadaEn = DateTime.UtcNow.AddMinutes(-1), FinalizadaEn = DateTime.UtcNow,
            Resultado = ResultadoBackup.Fallida, MotivoFallo = "simulado",
        };
        ctx.CorridasBackup.Add(corrida);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync($"/backups/{corrida.Id}/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_ArchivoRegistradoPeroBorradoDelDisco_Devuelve404()
    {
        var corrida = await SembrarCorridaExitosaConArchivoAsync();
        var paths = Factory.Services.GetRequiredService<IUserDataPathProvider>();
        File.Delete(Path.Combine(paths.GetBackupsDirectory(), corrida.NombreArchivo!));

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync($"/backups/{corrida.Id}/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/backups/1/contenido");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_ConTokenOperador_Devuelve403()
    {
        var corrida = await SembrarCorridaExitosaConArchivoAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync($"/backups/{corrida.Id}/contenido");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /backups/salud ──────────────────────────────────────────────────

    [Fact]
    public async Task GetSalud_SinCorridas_DevuelveVencidoTrueYUltimoExitoNull()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/backups/salud");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var salud = await response.Content.ReadFromJsonAsync<SaludBackupDto>();
        Assert.True(salud!.Vencido);
        Assert.Null(salud.UltimoExitoEn);
        Assert.Equal(26, salud.UmbralHoras);
    }

    [Fact]
    public async Task GetSalud_ConCorridaRecienteExitosa_DevuelveVencidoFalse()
    {
        await SembrarCorridaExitosaConArchivoAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/backups/salud");

        var salud = await response.Content.ReadFromJsonAsync<SaludBackupDto>();
        Assert.False(salud!.Vencido);
        Assert.NotNull(salud.UltimoExitoEn);
    }

    [Fact]
    public async Task GetSalud_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/backups/salud");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
