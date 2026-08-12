using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Documentos;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class AdjuntoDocumentoApiClientTests
{
    [Fact]
    public async Task AgregarAsync_EnviaMultipartYParseaRespuesta()
    {
        var dto = new AdjuntoDocumentoDto(1, 5, "expediente.pdf", "application/pdf", 100, DateTime.UtcNow);
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("documentos/5/adjuntos", request.RequestUri!.PathAndQuery.TrimStart('/'));
            Assert.IsType<MultipartFormDataContent>(request.Content);
            return TestHttp.Json(dto, HttpStatusCode.Created);
        });
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.AgregarAsync(5, "expediente.pdf", new byte[] { 1, 2, 3 });

        Assert.Equal(1, resultado.Id);
        Assert.Equal("expediente.pdf", resultado.NombreArchivo);
    }

    [Fact]
    public async Task AgregarAsync_ErrorDelServidor_LanzaExcepcionDeDominio()
    {
        var fake = new FakeHttpHandler(_ =>
            TestHttp.Problema(HttpStatusCode.NotFound, "El documento no existe."));
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => client.AgregarAsync(999, "expediente.pdf", new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_GETParseaListaJson()
    {
        var dtos = new[]
        {
            new AdjuntoDocumentoDto(1, 5, "a.pdf", "application/pdf", 10, DateTime.UtcNow),
            new AdjuntoDocumentoDto(2, 5, "b.pdf", "application/pdf", 20, DateTime.UtcNow),
        };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("documentos/5/adjuntos", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dtos);
        });
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ListarPorDocumentoAsync(5);

        Assert.Equal(2, resultado.Count);
        Assert.Equal("a.pdf", resultado[0].NombreArchivo);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_DevuelveBytesYNombreDesdeHeaders()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("documentos/adjuntos/1/contenido", request.RequestUri!.PathAndQuery.TrimStart('/'));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition =
                new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = "expediente.pdf" };
            return response;
        });
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ObtenerContenidoAsync(1);

        Assert.Equal(bytes, resultado.Contenido);
        Assert.Equal("expediente.pdf", resultado.NombreArchivo);
        Assert.Equal("application/pdf", resultado.ContentType);
    }

    [Fact]
    public async Task QuitarAsync_EnviaDelete()
    {
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("documentos/adjuntos/7", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        await client.QuitarAsync(7);
    }

    [Fact]
    public async Task QuitarAsync_403_LanzaUnauthorizedAccessException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol Operador no tiene permiso para ejecutar la acción 'documentos.administrar'."));
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.QuitarAsync(7));
    }
}
