using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ProductosEndpointTests : ApiTestBase
{
    public ProductosEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetProductos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProductos_ConTokenAdmin_Devuelve200ConProductosSeedeados()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-A1", "Producto Admin Test");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var productos = await response.Content.ReadFromJsonAsync<List<ProductoDto>>();
        Assert.Contains(productos!, p => p.Codigo == "SKU-A1");
    }

    [Fact]
    public async Task GetProductos_ConTokenOperador_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-O1", "Producto Operador Test");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── GET /productos?texto= ────────────────────────────────────────────────

    [Fact]
    public async Task GetProductos_ConTexto_FiltraPorTexto()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-T1", "Coca Cola 1.5L");
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-T2", "Sprite 1.5L");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos?texto=Coca");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var productos = await response.Content.ReadFromJsonAsync<List<ProductoDto>>();
        Assert.Single(productos!);
        Assert.Equal("SKU-T1", productos![0].Codigo);
    }

    // ── POST /productos ───────────────────────────────────────────────────────

    [Fact]
    public async Task PostProductos_ConTokenOperador_CreaProductoYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        // El alta registra auditoría con FK real a Usuarios; TokenOperador reclama UsuarioId=2,
        // así que sembramos Admin (Id=1) + Operador (Id=2) en ese orden.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        var unidad = new UnidadMedida { Nombre = "Kilo", Abreviatura = "kg", Activo = true };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/productos", new CrearProductoRequest(
            "SKU-P1", null, "Producto Nuevo", null, null, null, unidad.Id, 5m, 10m, 0m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Productos.AnyAsync(p => p.Codigo == "SKU-P1"));
    }

    [Fact]
    public async Task PostProductos_CodigoDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-P2", "Producto Existente");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/productos", new CrearProductoRequest(
            "SKU-P2", null, "Otro Nombre", null, null, null, producto.UnidadMedidaId, 5m, 10m, 0m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── PUT /productos/{id} ───────────────────────────────────────────────────

    [Fact]
    public async Task PutProductos_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        // La modificación registra auditoría con FK real a Usuarios; TokenAdmin reclama UsuarioId=1.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-P3", "Nombre Original");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync($"/productos/{producto.Id}", new ModificarProductoRequest(
            producto.Codigo, null, "Nombre Modificado", null, null, null, producto.UnidadMedidaId, 10m, 20m, 0m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.Equal("Nombre Modificado", actualizado.Nombre);
    }

    // ── DELETE /productos/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteProductos_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        // La baja lógica registra auditoría con FK real a Usuarios; TokenAdmin reclama UsuarioId=1.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-P4", "Producto Baja");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.DeleteAsync($"/productos/{producto.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.False(actualizado.Activo);
    }

    // ── PUT /productos/{id}/precio ───────────────────────────────────────────

    [Fact]
    public async Task PutPrecio_ConTokenOperador_CambiaPrecioYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        // El cambio de precio registra auditoría con FK real a Usuarios; TokenOperador reclama
        // UsuarioId=2, así que sembramos Admin (Id=1) + Operador (Id=2) en ese orden.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-P5", "Producto Precio");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync($"/productos/{producto.Id}/precio", new CambiarPrecioRequest(15m, 30m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(15m, actualizado.PrecioCosto);
        Assert.Equal(30m, actualizado.PrecioVenta);
    }
}
