using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class ProveedorApiClientTests
{
    [Fact]
    public async Task ListarTodos_GETProveedores_MapeaTodosLosCampos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, nombre = "Distribuidora Sur", telefono = "099123456",
                email = "ventas@sur.com.uy", direccion = "Ruta 21 km 2", notas = (string?)null,
                activo = true,
            },
        }));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        var proveedores = await client.ListarTodosAsync();

        Assert.Equal("/proveedores", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var p = Assert.Single(proveedores);
        Assert.Equal(1, p.Id);
        Assert.Equal("Distribuidora Sur", p.Nombre);
        Assert.Equal("099123456", p.Telefono);
        Assert.Equal("ventas@sur.com.uy", p.Email);
        Assert.Equal("Ruta 21 km 2", p.Direccion);
        Assert.Null(p.Notas);
        Assert.True(p.Activo);
    }

    [Fact]
    public async Task Alta_POSTProveedores_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 5 }, HttpStatusCode.Created));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new Proveedor { Nombre = "Distribuidora Sur", Telefono = "099123456" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/proveedores", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Distribuidora Sur\"", fake.UltimoBody);
        Assert.Contains("\"telefono\":\"099123456\"", fake.UltimoBody);
        Assert.Equal(5, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new Proveedor { Id = 2, Nombre = "Sur SRL", Email = "sur@srl.uy" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/proveedores/2", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"email\":\"sur@srl.uy\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEProveedoresId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(2);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/proveedores/2", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Baja_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "El proveedor ya está inactivo."));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.BajaLogicaAsync(2));

        Assert.Equal("El proveedor ya está inactivo.", ex.Message);
    }
}
