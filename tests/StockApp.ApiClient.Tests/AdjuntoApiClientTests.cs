using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Finanzas;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class AdjuntoApiClientTests
{
    [Fact]
    public async Task AgregarAGastoAsync_EnviaMultipartYParseaRespuesta()
    {
        var dto = new AdjuntoDto(1, "factura.pdf", "application/pdf", 100, 5, null, DateTime.UtcNow);
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("finanzas/gastos/5/adjuntos", request.RequestUri!.PathAndQuery.TrimStart('/'));
            Assert.IsType<MultipartFormDataContent>(request.Content);
            return TestHttp.Json(dto, HttpStatusCode.Created);
        });
        var client = new AdjuntoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.AgregarAGastoAsync(5, "factura.pdf", new byte[] { 1, 2, 3 });

        Assert.Equal(1, resultado.Id);
        Assert.Equal("factura.pdf", resultado.NombreArchivo);
    }

    [Fact]
    public async Task AgregarAPagoAsync_EnviaMultipartYParseaRespuesta()
    {
        var dto = new AdjuntoDto(2, "recibo.jpg", "image/jpeg", 200, null, 8, DateTime.UtcNow);
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("finanzas/pagos/8/adjuntos", request.RequestUri!.PathAndQuery.TrimStart('/'));
            Assert.IsType<MultipartFormDataContent>(request.Content);
            return TestHttp.Json(dto, HttpStatusCode.Created);
        });
        var client = new AdjuntoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.AgregarAPagoAsync(8, "recibo.jpg", new byte[] { 4, 5, 6 });

        Assert.Equal(2, resultado.Id);
        Assert.Equal("recibo.jpg", resultado.NombreArchivo);
    }

    [Fact]
    public async Task ListarPorGastoAsync_GETParseaListaJson()
    {
        var dtos = new[]
        {
            new AdjuntoDto(1, "a.pdf", "application/pdf", 10, 5, null, DateTime.UtcNow),
            new AdjuntoDto(2, "b.pdf", "application/pdf", 20, 5, null, DateTime.UtcNow),
        };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("finanzas/gastos/5/adjuntos", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dtos);
        });
        var client = new AdjuntoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ListarPorGastoAsync(5);

        Assert.Equal(2, resultado.Count);
        Assert.Equal("a.pdf", resultado[0].NombreArchivo);
    }

    [Fact]
    public async Task ListarPorPagoAsync_GETParseaListaJson()
    {
        var dtos = new[]
        {
            new AdjuntoDto(3, "c.pdf", "application/pdf", 30, null, 8, DateTime.UtcNow),
        };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("finanzas/pagos/8/adjuntos", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dtos);
        });
        var client = new AdjuntoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ListarPorPagoAsync(8);

        Assert.Single(resultado);
        Assert.Equal("c.pdf", resultado[0].NombreArchivo);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_DevuelveBytesYNombreDesdeHeaders()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("finanzas/adjuntos/1/contenido", request.RequestUri!.PathAndQuery.TrimStart('/'));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition =
                new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = "factura.pdf" };
            return response;
        });
        var client = new AdjuntoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ObtenerContenidoAsync(1);

        Assert.Equal(bytes, resultado.Contenido);
        Assert.Equal("factura.pdf", resultado.NombreArchivo);
        Assert.Equal("application/pdf", resultado.ContentType);
    }

    [Fact]
    public async Task QuitarAsync_EnviaDelete()
    {
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("finanzas/adjuntos/7", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new AdjuntoApiClient(TestHttp.CrearCliente(fake));

        await client.QuitarAsync(7);
    }

    [Fact]
    public async Task AgregarAGastoAsync_ErrorDelServidor_LanzaExcepcionDeDominio()
    {
        var fake = new FakeHttpHandler(_ =>
            TestHttp.Problema(HttpStatusCode.NotFound, "El gasto no existe."));
        var client = new AdjuntoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<StockApp.Domain.Exceptions.EntidadNoEncontradaException>(
            () => client.AgregarAGastoAsync(999, "factura.pdf", new byte[] { 1, 2, 3 }));
    }
}
