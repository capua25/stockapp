using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Licenciamiento;

public class LicenciaEndpointsTests : ApiTestBase
{
    public LicenciaEndpointsTests(ApiFactory factory) : base(factory) { }

    private void Bloquear()
        => Factory.Services.GetRequiredService<EstadoLicencia>().Activada = false;

    [Fact]
    public async Task Estado_DevuelveCodigoDeMaquina()
    {
        var client = Factory.CreateClient();

        var estado = await client.GetFromJsonAsync<LicenciaEstadoResponse>("/licencia/estado");

        Assert.Equal(ClavesDePrueba.CodigoMaquina, estado!.CodigoMaquina);
    }

    [Fact]
    public async Task Activar_LicenciaValida_ActivaYDevuelveEstado()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/licencia/activar",
            new ActivarLicenciaRequest(ClavesDePrueba.EmitirLicencia()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var estado = await response.Content.ReadFromJsonAsync<LicenciaEstadoResponse>();
        Assert.True(estado!.Activada);

        // Tras activar, un endpoint normal ya no da 423.
        Assert.NotEqual((HttpStatusCode)423,
            (await client.GetAsync("/productos")).StatusCode);
    }

    [Fact]
    public async Task Activar_LicenciaDeOtraMaquina_Devuelve400YSigueBloqueada()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/licencia/activar",
            new ActivarLicenciaRequest(ClavesDePrueba.EmitirLicencia(maquina: "OTRA-MAQUINA")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((HttpStatusCode)423, (await client.GetAsync("/productos")).StatusCode);
    }

    [Fact]
    public async Task Activar_Exitosa_QuedaAuditada()
    {
        // Sembrar un admin para que el evento de licencia se pueda atribuir.
        await SembrarAdminAsync();
        Bloquear();
        var client = Factory.CreateClient();

        await client.PostAsJsonAsync("/licencia/activar",
            new ActivarLicenciaRequest(ClavesDePrueba.EmitirLicencia()));

        using var ctx = Factory.CrearContexto();
        var hayEvento = await ctx.LogsAuditoria
            .AnyAsync(l => l.Accion == AccionAuditada.ActivacionLicencia);
        Assert.True(hayEvento);
    }

    [Fact]
    public async Task Activar_LicenciaInvalida_QuedaAuditadaComoIntentoFallido()
    {
        // Con admin sembrado: el intento fallido se puede atribuir y queda en LogsAuditoria.
        await SembrarAdminAsync();
        var client = Factory.CreateClient();

        await client.PostAsJsonAsync("/licencia/activar",
            new ActivarLicenciaRequest(ClavesDePrueba.EmitirLicencia(maquina: "OTRA-MAQUINA")));

        using var ctx = Factory.CrearContexto();
        var hayEvento = await ctx.LogsAuditoria
            .AnyAsync(l => l.Accion == AccionAuditada.IntentoActivacionLicenciaFallido);
        Assert.True(hayEvento);
    }

    [Fact]
    public async Task Activar_LicenciaInvalida_SinNingunAdmin_NoCrasheaNiAudita()
    {
        // Sin admin sembrado: AuditarAsync no encuentra a quién atribuir el evento y no
        // escribe fila (UsuarioId es FK requerida) — el endpoint debe responder igual, sin 500.
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/licencia/activar",
            new ActivarLicenciaRequest(ClavesDePrueba.EmitirLicencia(maquina: "OTRA-MAQUINA")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var ctx = Factory.CrearContexto();
        var hayEvento = await ctx.LogsAuditoria
            .AnyAsync(l => l.Accion == AccionAuditada.IntentoActivacionLicenciaFallido);
        Assert.False(hayEvento);
    }

    private async Task SembrarAdminAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var primerArranque = scope.ServiceProvider
            .GetRequiredService<StockApp.Application.Auth.IPrimerArranqueService>();
        await primerArranque.CrearAdminInicialAsync("admin-lic", "clave-lic-123");
    }
}
