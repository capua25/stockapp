using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Authorization;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Api.Tests;

public class ReportesEndpointTests : ApiTestBase
{
    public ReportesEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    // ── GET /reportes/valorizacion ───────────────────────────────────────────

    [Fact]
    public async Task GetValorizacion_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetValorizacion_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetValorizacion_ConTokenAdmin_Devuelve200ConValorizacion()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-R1", "Producto Reporte 1");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reporte = await response.Content.ReadFromJsonAsync<ValorizacionReporteDto>();
        Assert.Contains(reporte!.Items, i => i.Codigo == "SKU-R1");
    }

    [Fact]
    public async Task GetProductosReporteValorizacion_RutaVieja_Devuelve405()
    {
        // Desde la Task 10, /productos/{id:int} tiene PUT y DELETE mapeados: ASP.NET Core
        // considera esa forma de ruta como existente (aunque "reporte-valorizacion" no matchee
        // la restricción :int) y responde 405 Method Not Allowed en vez de 404 para un GET.
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/productos/reporte-valorizacion");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // ── GET /reportes/stock-por-categoria ────────────────────────────────────

    [Fact]
    public async Task GetStockPorCategoria_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-R2", "Producto Reporte 2");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/stock-por-categoria");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<StockCategoriaDto>>());
    }

    [Fact]
    public async Task GetStockPorCategoria_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/reportes/stock-por-categoria");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /reportes/mas-movidos ────────────────────────────────────────────

    [Fact]
    public async Task GetMasMovidos_ConTokenAdmin_Devuelve200()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/mas-movidos?topN=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<MasMovidoDto>>());
    }

    [Fact]
    public async Task GetMasMovidos_SinQueryParamTopN_Devuelve200ConDefaultTopN()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/mas-movidos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<List<MasMovidoDto>>();
        Assert.NotNull(resultado);
    }

    // ── GET /reportes/historial-producto/{productoId} ────────────────────────

    [Fact]
    public async Task GetHistorialProducto_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-R3", "Producto Reporte 3");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync($"/reportes/historial-producto/{producto.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>());
    }

    [Fact]
    public async Task GetHistorialProducto_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/reportes/historial-producto/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /reportes/tareas ──────────────────────────────────────────────

    [Fact]
    public async Task GetReporteTareas_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Zona&criterio=Creacion&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteTareas_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // Operador REAL (SeedOperadorConTokenAsync, los 10 PermisosInicialesOperador
        // completos) -- ninguno de esos es VerReportes (queda afuera adrede, ver
        // PermisoUsuarioBackfillSql). A diferencia de TokenOperador() (un usuarioId=2 sin
        // sembrar, que da 403 por el fail-closed de PoblarPermisosMiddleware sin llegar a
        // mirar el permiso puntual), este test prueba lo que dice probar: un Operador con
        // permisos reales, pero sin VerReportes.
        await using var ctx = Factory.CrearContexto();
        var (_, token) = await DatosDePrueba.SeedOperadorConTokenAsync(
            ctx, Factory.Services.GetRequiredService<IJwtTokenService>(), "operador.sinreportes");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Zona&criterio=Creacion&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteTareas_ConTokenAdmin_Devuelve200()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Zona&criterio=Creacion&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reporte = await response.Content.ReadFromJsonAsync<ReporteTareasDto>();
        Assert.NotNull(reporte);
    }

    [Fact]
    public async Task GetReporteTareas_ConTokenOperadorConVerReportes_Devuelve200()
    {
        // GetReporteTareas_ConTokenAdmin_Devuelve200 usa Admin, que en este repo cortocircuita
        // la autorización (PermisosEstructuralesAdmin/Admin-siempre-Succeed) ANTES de mirar
        // PermisosActuales -- acredita que el pipeline no rompe, no que la policy VerReportes
        // filtre nada. Este test sí ejercita esa policy: un Operador real sembrado con
        // VerReportes como único permiso.
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.reportes", "Secreta123!", new[] { Permisos.VerReportes });
        var token = Factory.Services.GetRequiredService<IJwtTokenService>()
            .GenerarToken(operador.Id, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Zona&criterio=Creacion&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reporte = await response.Content.ReadFromJsonAsync<ReporteTareasDto>();
        Assert.NotNull(reporte);
    }

    [Fact]
    public async Task GetReporteTareas_SinRangoDeFechas_Devuelve400()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/tareas?agrupador=Zona&criterio=Creacion");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteTareas_RangoDeFechasInvertido_Devuelve400()
    {
        // A diferencia de SinRangoDeFechas_400 (que ni siquiera llega al servicio -- lo
        // rechaza el model-binder de ASP.NET porque faltan los query params), este caso manda
        // las dos fechas presentes pero invertidas: ejercita el segundo `if` de
        // ReporteTareasService.ObtenerAsync (Desde > Hasta), no el primero.
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Zona&criterio=Creacion&desde=2026-12-31&hasta=2026-01-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteTareas_AgrupadoPorExpediente_Devuelve200()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Expediente&criterio=Cierre&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteTareas_AgrupadorInvalidoPorNumero_Devuelve400()
    {
        // AgrupadorTareas 99 no existe: el model-binder de Minimal API lo bindea igual (un
        // enum sin [Flags] acepta cualquier valor numérico vía Enum.TryParse), y el switch de
        // ReporteTareasRepository.ObtenerAsync lo rechaza con ArgumentOutOfRangeException --
        // que hereda de ArgumentException y por eso el 400 llega hoy por herencia accidental
        // del DomainExceptionHandler, no por una validación explícita del valor del enum.
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=99&criterio=Creacion&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── GET /reportes/tareas — normalización de fechas de query string ──────

    private static async Task SembrarTareaAsync(AppDbContext ctx, DateTime fechaCreacionUtc)
    {
        var creador = new Usuario
        {
            NombreUsuario = $"creador-{Guid.NewGuid():N}",
            HashContrasena = "x",
            Rol = RolUsuario.Operador,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };
        ctx.Usuarios.Add(creador);
        await ctx.SaveChangesAsync();

        ctx.Tareas.Add(new Tarea
        {
            Titulo = "Tarea límite de rango",
            CreadaPorUsuarioId = creador.Id,
            FechaCreacion = fechaCreacionUtc,
            Estado = EstadoTarea.Pendiente,
        });
        await ctx.SaveChangesAsync();
    }

    // Los tres casos de entrada que puede mandar un cliente HTTP en el query string, apuntando
    // los tres al MISMO instante UTC (2026-01-01T00:00:00Z) con formatos distintos: sin offset
    // (Kind=Unspecified al bindear), con "Z" y con un offset explícito no-UTC (Kind=Local en
    // ambos casos, ya convertido a la zona horaria del servidor -- acá, America/Montevideo,
    // -03:00). La tarea sembrada tiene FechaCreacion exactamente en ese instante: si la
    // normalización corrompe el instante (bug original: SpecifyKind ciego sin
    // ToUniversalTime), el filtro "hasta fin del día" calcula el límite sobre el día
    // equivocado y la excluye -- Total pasa de 1 a 0.
    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("2026-01-01T00:00:00Z")]
    [InlineData("2025-12-31T21:00:00-03:00")]
    public async Task GetReporteTareas_NormalizaElInstanteUtcDeLaFechaDeQueryString_IncluyeLaTareaDelLimite(
        string fecha)
    {
        await using var ctx = Factory.CrearContexto();
        await SembrarTareaAsync(ctx, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync(
            $"/reportes/tareas?agrupador=Zona&criterio=Creacion&desde={Uri.EscapeDataString(fecha)}&hasta={Uri.EscapeDataString(fecha)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reporte = await response.Content.ReadFromJsonAsync<ReporteTareasDto>();
        Assert.Equal(1, reporte!.TotalGeneral);
    }
}
