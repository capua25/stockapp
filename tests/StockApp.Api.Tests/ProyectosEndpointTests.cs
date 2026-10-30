using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ProyectosEndpointTests : ApiTestBase
{
    public ProyectosEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedAdminAsync()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
    }

    /// <summary>Operador con exactamente los permisos pedidos (estado real tras recortarle permisos).</summary>
    private async Task<HttpClient> ClienteOperadorAsync(string nombre, params string[] permisos)
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(ctx, nombre, "Secreta123!", permisos);
        var token = Factory.Services.GetRequiredService<IJwtTokenService>()
            .GenerarToken(operador.Id, RolUsuario.Operador);
        return ClienteAutenticado(token);
    }

    private static CrearProyectoRequest Alta(string titulo = "Plaza nueva", decimal? presupuesto = null) =>
        new(titulo, null, PrioridadTarea.Media, null, presupuesto, null, null, null, null);

    private static async Task<int> CrearAsync(HttpClient client, string titulo = "Plaza nueva", decimal? presupuesto = null)
    {
        var response = await client.PostAsJsonAsync("/proyectos", Alta(titulo, presupuesto));
        var creado = await response.Content.ReadFromJsonAsync<ProyectoCreadoResponse>();
        return creado!.Id;
    }

    [Fact]
    public async Task PostProyectos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().PostAsJsonAsync("/proyectos", Alta());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProyectos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/proyectos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostProyectos_ConAdmin_Devuelve201YPersiste()
    {
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync("/proyectos", Alta("Plaza nueva", 5000m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = await response.Content.ReadFromJsonAsync<ProyectoCreadoResponse>();
        Assert.True(creado!.Id > 0);
        await using var ctx = Factory.CrearContexto();
        var proyecto = await ctx.Proyectos.SingleAsync();
        Assert.Equal("Plaza nueva", proyecto.Titulo);
        Assert.Equal(5000m, proyecto.Presupuesto);
        Assert.Equal(EstadoProyecto.Pendiente, proyecto.Estado);
    }

    [Fact]
    public async Task PostProyectos_TituloVacio_Devuelve400()
    {
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync("/proyectos", Alta(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostProyectos_OperadorSoloVerProyectos_Devuelve403()
    {
        var client = await ClienteOperadorAsync("op.ver", Permisos.VerProyectos);

        var response = await client.PostAsJsonAsync("/proyectos", Alta());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetProyectos_OperadorSinVerProyectos_Devuelve403()
    {
        var client = await ClienteOperadorAsync("op.nada", Permisos.VerFinanzas);

        var response = await client.GetAsync("/proyectos");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetProyectos_OperadorVerProyectos_Devuelve200ConLaLista()
    {
        await SeedAdminAsync();
        await CrearAsync(ClienteAutenticado(TokenAdmin()));
        var client = await ClienteOperadorAsync("op.ver", Permisos.VerProyectos);

        var response = await client.GetAsync("/proyectos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<ProyectoDto>>();
        var item = Assert.Single(lista!);
        Assert.Equal("Plaza nueva", item.Titulo);
        Assert.Equal(EstadoProyecto.Pendiente, item.Estado);
    }

    [Fact]
    public async Task GetFicha_Inexistente_Devuelve404()
    {
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/proyectos/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFicha_SinVerProyectos_Devuelve403()
    {
        await SeedAdminAsync();
        var id = await CrearAsync(ClienteAutenticado(TokenAdmin()));
        var client = await ClienteOperadorAsync("op.nada", Permisos.VerFinanzas);

        var response = await client.GetAsync($"/proyectos/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetFicha_Admin_IncluyeGastosYTotales()
    {
        await SeedAdminAsync();
        var id = await CrearAsync(ClienteAutenticado(TokenAdmin()), presupuesto: 1000m);
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync($"/proyectos/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ficha = await response.Content.ReadFromJsonAsync<FichaProyectoResponse>();
        Assert.Equal(id, ficha!.Proyecto.Id);
        Assert.Equal(0m, ficha.TotalGastado);
        Assert.Equal(1000m, ficha.Disponible);
        Assert.NotNull(ficha.Gastos);
        Assert.Empty(ficha.Gastos!);
    }

    [Fact]
    public async Task GetFicha_OperadorSinVerFinanzas_GastosNullEnJson()
    {
        await SeedAdminAsync();
        var id = await CrearAsync(ClienteAutenticado(TokenAdmin()));
        var client = await ClienteOperadorAsync("op.ver", Permisos.VerProyectos);

        var response = await client.GetAsync($"/proyectos/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tieneGastos = doc.RootElement.TryGetProperty("gastos", out var gastos);
        Assert.True(!tieneGastos || gastos.ValueKind == JsonValueKind.Null,
            "Sin finanzas.ver la ficha no debe revelar gastos (ni siquiera una lista vacía).");
    }

    [Fact]
    public async Task GetOpciones_OperadorSinPermisosDeProyectos_200()
    {
        await SeedAdminAsync();
        await CrearAsync(ClienteAutenticado(TokenAdmin()), "Plaza nueva");
        var client = await ClienteOperadorAsync("op.nada", Permisos.VerFinanzas);

        var response = await client.GetAsync("/proyectos/opciones");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var opciones = await response.Content.ReadFromJsonAsync<List<OpcionProyectoResponse>>();
        var opcion = Assert.Single(opciones!);
        Assert.Equal("Plaza nueva", opcion.Titulo);
        Assert.Equal(EstadoProyecto.Pendiente, opcion.Estado);
    }

    [Fact]
    public async Task GetOpciones_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/proyectos/opciones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutProyecto_ModificaYDevuelve200()
    {
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearAsync(client);

        var response = await client.PutAsJsonAsync($"/proyectos/{id}",
            new ModificarProyectoRequest("Plaza renovada", "con juegos", PrioridadTarea.Alta, null, 800m, null, null, null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var ctx = Factory.CrearContexto();
        var proyecto = await ctx.Proyectos.SingleAsync();
        Assert.Equal("Plaza renovada", proyecto.Titulo);
        Assert.Equal(PrioridadTarea.Alta, proyecto.Prioridad);
        Assert.Equal(800m, proyecto.Presupuesto);
    }

    [Fact]
    public async Task PutProyecto_Inexistente_Devuelve404()
    {
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PutAsJsonAsync("/proyectos/9999",
            new ModificarProyectoRequest("x", null, PrioridadTarea.Media, null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutProyecto_FechaLimiteSinCambios_NoAuditaCambioDeFecha()
    {
        // FechaLimite viaja por el converter global (Kind=Utc): reenviar la misma fecha no es un cambio.
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var json = """{"titulo":"Plaza","prioridad":1,"fechaLimite":"2026-12-10T15:00:00Z"}""";
        var creada = await client.PostAsync("/proyectos", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        var id = (await creada.Content.ReadFromJsonAsync<ProyectoCreadoResponse>())!.Id;

        var put = await client.PutAsync($"/proyectos/{id}", new StringContent(
            """{"titulo":"Plaza","prioridad":1,"fechaLimite":"2026-12-10T15:00:00Z"}""",
            System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        await using var ctx = Factory.CrearContexto();
        var proyecto = await ctx.Proyectos.Include(p => p.Notas).SingleAsync();
        Assert.DoesNotContain(proyecto.Notas, n => n.Texto.Contains("Fecha límite"));
    }

    [Fact]
    public async Task PostEstado_ConAdmin_CambiaElEstado()
    {
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearAsync(client);

        var response = await client.PostAsJsonAsync($"/proyectos/{id}/estado", new CambiarEstadoProyectoRequest(EstadoProyecto.EnCurso));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var ctx = Factory.CrearContexto();
        Assert.Equal(EstadoProyecto.EnCurso, (await ctx.Proyectos.SingleAsync()).Estado);
    }

    [Fact]
    public async Task PostEstado_TransicionInvalida_409()
    {
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearAsync(client);

        var response = await client.PostAsJsonAsync($"/proyectos/{id}/estado", new CambiarEstadoProyectoRequest(EstadoProyecto.Terminado));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostEstado_Inexistente_Devuelve404()
    {
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync("/proyectos/9999/estado", new CambiarEstadoProyectoRequest(EstadoProyecto.EnCurso));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostEstado_OperadorSoloVerProyectos_Devuelve403()
    {
        await SeedAdminAsync();
        var id = await CrearAsync(ClienteAutenticado(TokenAdmin()));
        var client = await ClienteOperadorAsync("op.ver", Permisos.VerProyectos);

        var response = await client.PostAsJsonAsync($"/proyectos/{id}/estado", new CambiarEstadoProyectoRequest(EstadoProyecto.EnCurso));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostNotas_AgregaLaNotaYAparecenEnLaFicha()
    {
        await SeedAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearAsync(client);

        var response = await client.PostAsJsonAsync($"/proyectos/{id}/notas", new AgregarNotaProyectoRequest("Se aprobó el presupuesto"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ficha = await (await client.GetAsync($"/proyectos/{id}")).Content.ReadFromJsonAsync<FichaProyectoResponse>();
        Assert.Contains(ficha!.Proyecto.Notas, n => n.Texto == "Se aprobó el presupuesto" && !n.EsAutomatica);
    }

    [Fact]
    public async Task PostNotas_OperadorSoloVerProyectos_Devuelve403()
    {
        await SeedAdminAsync();
        var id = await CrearAsync(ClienteAutenticado(TokenAdmin()));
        var client = await ClienteOperadorAsync("op.ver", Permisos.VerProyectos);

        var response = await client.PostAsJsonAsync($"/proyectos/{id}/notas", new AgregarNotaProyectoRequest("x"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
