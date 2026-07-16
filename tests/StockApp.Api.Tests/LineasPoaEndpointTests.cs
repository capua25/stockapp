using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class LineasPoaEndpointTests : ApiTestBase
{
    public LineasPoaEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private async Task<int> SeedFuenteAsync(string nombre)
    {
        await using var ctx = Factory.CrearContexto();
        var fuente = new FuenteFinanciamiento { Nombre = nombre, Activo = true };
        ctx.FuentesFinanciamiento.Add(fuente);
        await ctx.SaveChangesAsync();
        return fuente.Id;
    }

    [Fact]
    public async Task GetLineasPoa_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/finanzas/lineas-poa");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLineasPoa_ConAsignaciones_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuenteB = await SeedFuenteAsync("Literal B");
        var fuenteC = await SeedFuenteAsync("Literal C");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("COMPOSTERAS", "Ambiente", 2026, new List<AsignacionPresupuestalRequest>
            {
                new(fuenteB, 100000m),
                new(fuenteC, 50000m),
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var linea = await verificacion.LineasPoa
            .Include(l => l.Asignaciones)
            .SingleAsync(l => l.Nombre == "COMPOSTERAS");
        Assert.Equal(2, linea.Asignaciones.Count);
    }

    [Fact]
    public async Task PostLineasPoa_SinAsignaciones_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("PRENSA", "Comunicación", 2026, new List<AsignacionPresupuestalRequest>()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostLineasPoa_SinCampoAsignaciones_Devuelve409YNo500()
    {
        // El body JSON omite "asignaciones" por completo (no es solo una lista vacía):
        // el binder deserializa null, y el mapeo AEntidad debe tolerarlo con ?? [] en vez
        // de tirar NRE en el .Select — el servicio ya devuelve 409 por lista vacía.
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var content = new StringContent(
            "{\"nombre\":\"PRENSA SIN ASIGNACIONES\",\"programa\":\"Comunicación\",\"ejercicio\":2026}",
            System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/finanzas/lineas-poa", content);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostLineasPoa_NombreYEjercicioDuplicados_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = await SeedFuenteAsync("Literal B");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());
        var request = new CrearLineaPoaRequest("Rambla", "Obras", 2026,
            new List<AsignacionPresupuestalRequest> { new(fuente, 1000m) });

        var primera = await client.PostAsJsonAsync("/finanzas/lineas-poa", request);
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        var segunda = await client.PostAsJsonAsync("/finanzas/lineas-poa", request);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }

    [Fact]
    public async Task PutLineasPoa_ReemplazaAsignaciones_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuenteB = await SeedFuenteAsync("Literal B");
        var fuenteC = await SeedFuenteAsync("Literal C");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var alta = await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("PRENSA", "Comunicación", 2026,
                new List<AsignacionPresupuestalRequest> { new(fuenteB, 80000m) }));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var creado = await alta.Content.ReadFromJsonAsync<IdCreadoResponse>();

        var response = await client.PutAsJsonAsync($"/finanzas/lineas-poa/{creado!.Id}",
            new ModificarLineaPoaRequest("PRENSA", "Prensa y Comunicación", 2026,
                new List<AsignacionPresupuestalRequest> { new(fuenteC, 120000m) }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var linea = await verificacion.LineasPoa
            .Include(l => l.Asignaciones)
            .SingleAsync(l => l.Id == creado.Id);
        Assert.Equal("Prensa y Comunicación", linea.Programa);
        var asignacion = Assert.Single(linea.Asignaciones);
        Assert.Equal(fuenteC, asignacion.FuenteFinanciamientoId);
        Assert.Equal(120000m, asignacion.Monto);
    }

    [Fact]
    public async Task DeleteLineasPoa_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = await SeedFuenteAsync("Literal B");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var alta = await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("Eventos", "Cultura", 2026,
                new List<AsignacionPresupuestalRequest> { new(fuente, 5000m) }));
        var creado = await alta.Content.ReadFromJsonAsync<IdCreadoResponse>();

        var response = await client.DeleteAsync($"/finanzas/lineas-poa/{creado!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var linea = await verificacion.LineasPoa.SingleAsync(l => l.Id == creado.Id);
        Assert.False(linea.Activo);
    }

    [Fact]
    public async Task GetLineasPoa_DevuelveAsignacionesConNombreDeFuente()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = await SeedFuenteAsync("Literal B");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("Rambla", "Obras", 2026,
                new List<AsignacionPresupuestalRequest> { new(fuente, 300000m) }));

        var response = await client.GetAsync("/finanzas/lineas-poa");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lineas = await response.Content.ReadFromJsonAsync<List<LineaPoaDto>>();
        var linea = Assert.Single(lineas!, l => l.Nombre == "Rambla");
        var asignacion = Assert.Single(linea.Asignaciones);
        Assert.Equal("Literal B", asignacion.FuenteFinanciamientoNombre);
        Assert.Equal(300000m, asignacion.Monto);
    }
}

/// <summary>Shape del body de los 201 ({ "id": n }) para deserializar en los tests.</summary>
public record IdCreadoResponse(int Id);
