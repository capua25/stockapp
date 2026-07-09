using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auditoria;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class AuditoriaEndpointTests : ApiTestBase
{
    public AuditoriaEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetAuditoria_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/auditoria");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditoria_ConTokenOperador_Devuelve403()
    {
        // Seed: crear usuarios (Admin=1, Operador=2)
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/auditoria");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditoria_ConTokenAdmin_Devuelve200ConLogSembrado()
    {
        await using var ctx = Factory.CrearContexto();
        // Seed: crear usuarios (Admin=1, Operador=2)
        var admin = await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = admin.Id,
            Fecha = DateTime.UtcNow,
            Accion = AccionAuditada.AltaUsuario,
            Entidad = "Usuario",
            EntidadId = admin.Id,
            Detalle = "Alta de usuario de prueba (seed directo por DB)",
        });
        await ctx.SaveChangesAsync();

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var tokenAdmin = jwt.GenerarToken(admin.Id, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);

        var response = await client.GetAsync("/auditoria");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<AuditoriaItemDto>>();
        Assert.Contains(items!, i => i.Accion == AccionAuditada.AltaUsuario);
    }
}
