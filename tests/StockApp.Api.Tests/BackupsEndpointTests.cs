using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Backups;
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

    // ── POST /backups ───────────────────────────────────────────────────────

    [Fact]
    public async Task PostBackups_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().PostAsync("/backups", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostBackups_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsync("/backups", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostBackups_ConTokenAdmin_Devuelve202YLaCorridaQuedaConElUsuarioIdDelToken()
    {
        // Admin ocupa Id=1 (coincide con TokenAdmin()): sin este seed, la corrida disparada
        // violaría la FK CorridasBackup.UsuarioId -> Usuarios.Id agregada en esta rama.
        await using var ctx = Factory.CrearContexto();
        var admin = await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsync("/backups", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // La corrida corre en background (pg_dump real contra el Postgres de Testcontainers) --
        // se espera a que aparezca con un timeout acotado en vez de asumir que ya terminó.
        var corrida = await EsperarCorridaAsync();
        Assert.Equal(admin.Id, corrida.UsuarioId);
    }

    [Fact]
    public async Task PostBackups_DosDisparosSimultaneos_UnoEsAcceptedYOtroConflict_YSoloUnaCorridaSePersiste()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var clienteA = Factory.CreateClient();
        clienteA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());
        var clienteB = Factory.CreateClient();
        clienteB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var tareaA = clienteA.PostAsync("/backups", null);
        var tareaB = clienteB.PostAsync("/backups", null);
        var respuestas = await Task.WhenAll(tareaA, tareaB);
        var codigos = respuestas.Select(r => r.StatusCode).OrderBy(c => c).ToList();

        Assert.Equal(
            new[] { HttpStatusCode.Accepted, HttpStatusCode.Conflict }.OrderBy(c => c),
            codigos);

        // Prueba directa de "no corrieron dos pg_dump": esperando lo mismo que el test anterior
        // (una corrida aparece), después de un margen extra confirmamos que sigue siendo una sola.
        await EsperarCorridaAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await using var verificacion = Factory.CrearContexto();
        Assert.Single(await verificacion.CorridasBackup.ToListAsync());
    }

    /// <summary>Espera a que termine la corrida de backup manual disparada en background,
    /// sincronizándose con la Task real (<see cref="DisparadorBackupManual.UltimaCorridaEnBackgroundParaTests"/>)
    /// en vez de sondear <see cref="DateTime.UtcNow"/> contra un timeout fijo.
    ///
    /// Antes este método calculaba un <c>limite = DateTime.UtcNow.AddSeconds(10)</c> una sola vez y
    /// lo comparaba en cada vuelta de un sondeo cada 200ms. <see cref="DateTime.UtcNow"/> es reloj
    /// de PARED (CLOCK_REALTIME), no monotónico: bajo contención de host (WSL2, CI) puede saltar
    /// hacia adelante al resincronizarse, y el sondeo interpreta ese salto como "pasaron 10
    /// segundos" aunque el trabajo real haya tardado bien menos de un segundo -- de ahí el
    /// TimeoutException espurio (~4% de las corridas en loop, ver bugfix/backups-endpoint-tests-flaky).
    /// Agrandar el timeout no resuelve la causa: sigue midiendo con el reloj equivocado. Este método
    /// no mide duración en absoluto -- awaitea la Task real del trabajo en background, así que no
    /// hay reloj de por medio.</summary>
    private async Task<CorridaBackup> EsperarCorridaAsync()
    {
        var disparador = Factory.Services.GetRequiredService<DisparadorBackupManual>();

        // Disparar() asigna esta Task de forma SÍNCRONA, antes de que el endpoint devuelva
        // 202/Accepted -- y este método siempre se llama después de haber esperado esa respuesta
        // HTTP. Si acá es null no es una carrera para resolver con un sondeo: es un bug real (un
        // 202 sin que la corrida haya llegado a arrancar), y hay que fallar rápido, no encubrirlo
        // reintroduciendo un sondeo (aunque sea con Stopwatch).
        var tarea = disparador.UltimaCorridaEnBackgroundParaTests
            ?? throw new InvalidOperationException(
                "UltimaCorridaEnBackgroundParaTests es null pese al 202/Accepted recibido: " +
                "Disparar() nunca llegó a arrancar la corrida en background.");

        await tarea;

        await using var ctx = Factory.CrearContexto();
        return await ctx.CorridasBackup.SingleOrDefaultAsync()
            ?? throw new InvalidOperationException(
                "La corrida de backup manual terminó (Task completada) pero no aparece en CorridasBackup.");
    }
}
