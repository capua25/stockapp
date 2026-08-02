using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class TareasEndpointTests : ApiTestBase
{
    public TareasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedUsuariosAsync()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
    }

    private async Task<int> CrearTareaAsync(HttpClient client, string titulo = "Reparar bache")
    {
        var response = await client.PostAsJsonAsync("/tareas", new CrearTareaRequest(titulo, null, null));
        var creada = await response.Content.ReadFromJsonAsync<TareaCreadaResponse>();
        return creada!.Id;
    }

    [Fact]
    public async Task PostTareas_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient()
            .PostAsJsonAsync("/tareas", new CrearTareaRequest("Reparar bache", null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostTareas_TituloVacio_Devuelve400()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/tareas", new CrearTareaRequest("", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTareas_ConTokenOperador_Crea201()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync(
            "/tareas", new CrearTareaRequest("Reparar bache", "en calle Rivera", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creada = await response.Content.ReadFromJsonAsync<TareaCreadaResponse>();
        Assert.True(creada!.Id > 0);
    }

    [Fact]
    public async Task GetTareas_ConTokenOperador_Devuelve200ConLaLista()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await CrearTareaAsync(client);

        var response = await client.GetAsync("/tareas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tareas = await response.Content.ReadFromJsonAsync<List<TareaDto>>();
        Assert.Single(tareas!);
        Assert.Equal(PrioridadTarea.Media, tareas![0].Prioridad);
    }

    [Fact]
    public async Task PostTomar_TareaInexistente_Devuelve404()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync("/tareas/9999/tomar", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostTerminar_DesdePendiente_Devuelve409()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var id = await CrearTareaAsync(client);

        var response = await client.PostAsync($"/tareas/{id}/terminar", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostCancelar_ConTokenOperador_Devuelve403()
    {
        await SeedUsuariosAsync();
        var clienteAdmin = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(clienteAdmin);
        var clienteOperador = ClienteAutenticado(TokenOperador());

        var response = await clienteOperador.PostAsync($"/tareas/{id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostCancelar_ConTokenAdmin_Devuelve200()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(client);

        var response = await client.PostAsync($"/tareas/{id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostPrioridad_ConTokenOperador_Devuelve403()
    {
        await SeedUsuariosAsync();
        var clienteAdmin = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(clienteAdmin);
        var clienteOperador = ClienteAutenticado(TokenOperador());

        var response = await clienteOperador.PostAsJsonAsync(
            $"/tareas/{id}/prioridad", new CambiarPrioridadRequest(PrioridadTarea.Alta));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostPrioridad_ConTokenAdmin_Devuelve200()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/tareas/{id}/prioridad", new CambiarPrioridadRequest(PrioridadTarea.Alta));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostPrioridad_TareaTerminada_Devuelve409()
    {
        // Decisión 14 del spec: la prioridad no se puede tocar una vez que la tarea está
        // Terminada. DomainExceptionHandler ya mapea ReglaDeNegocioException a 409; esto
        // verifica el circuito completo (dominio → servicio → endpoint) contra la API real.
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(client);

        var tomar = await client.PostAsync($"/tareas/{id}/tomar", content: null);
        Assert.Equal(HttpStatusCode.OK, tomar.StatusCode);
        var terminar = await client.PostAsync($"/tareas/{id}/terminar", content: null);
        Assert.Equal(HttpStatusCode.OK, terminar.StatusCode);

        var response = await client.PostAsJsonAsync(
            $"/tareas/{id}/prioridad", new CambiarPrioridadRequest(PrioridadTarea.Alta));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostNotas_ConTokenOperador_Crea200()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var id = await CrearTareaAsync(client);

        var response = await client.PostAsJsonAsync($"/tareas/{id}/notas", new AgregarNotaRequest("avance del día"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostTerminar_TareaAjena_LaNotaAutomaticaIncluyeElNombreDelActor()
    {
        // Fix (review final, Critical): circuito real de punta a punta contra
        // HttpCurrentSession (el ICurrentSession real de la API, NO el mock de
        // TareaServiceTests). Un usuario ("juan") toma la tarea y OTRO ("garcia") la
        // termina; se lee la nota directo de la base para verificar el texto exacto de
        // la decisión 11 del spec: "García terminó una tarea tomada por Juan".
        await using var seedCtx = Factory.CrearContexto();
        var juan = await DatosDePrueba.SeedUsuarioAsync(seedCtx, "juan", "Secreta123!", RolUsuario.Operador);
        var garcia = await DatosDePrueba.SeedUsuarioAsync(seedCtx, "garcia", "Secreta123!", RolUsuario.Operador);

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var clienteJuan = ClienteAutenticado(jwt.GenerarToken(juan.Id, RolUsuario.Operador));
        var clienteGarcia = ClienteAutenticado(jwt.GenerarToken(garcia.Id, RolUsuario.Operador));

        var id = await CrearTareaAsync(clienteJuan);
        var tomar = await clienteJuan.PostAsync($"/tareas/{id}/tomar", content: null);
        Assert.Equal(HttpStatusCode.OK, tomar.StatusCode);

        var terminar = await clienteGarcia.PostAsync($"/tareas/{id}/terminar", content: null);
        Assert.Equal(HttpStatusCode.OK, terminar.StatusCode);

        await using var verificarCtx = Factory.CrearContexto();
        var tarea = await verificarCtx.Tareas.Include(t => t.Notas).SingleAsync(t => t.Id == id);
        var nota = Assert.Single(tarea.Notas);
        Assert.True(nota.EsAutomatica);
        Assert.Equal("garcia terminó una tarea tomada por juan.", nota.Texto);
    }
}
