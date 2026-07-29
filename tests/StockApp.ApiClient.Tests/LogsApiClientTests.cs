using System.Net;
using System.Net.Http.Headers;
using System.Text;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Logs;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class LogsApiClientTests
{
    [Fact]
    public async Task ObtenerResumenAsync_DevuelveElResumenDeserializado()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(
            new ResumenLogsDto(3, new DateTime(2026, 7, 1), new DateTime(2026, 7, 29), 4096)));
        var client = new LogsApiClient(TestHttp.CrearCliente(fake));

        var resumen = await client.ObtenerResumenAsync();

        Assert.Equal(3, resumen.CantidadArchivos);
        Assert.Equal(4096, resumen.TamanioTotalBytes);
        Assert.Equal("/logs", fake.UltimaRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DescargarZipAsync_DevuelveElNombreDelContentDisposition()
    {
        var fake = new FakeHttpHandler(_ =>
        {
            var contenido = new ByteArrayContent(Encoding.UTF8.GetBytes("zip falso"));
            contenido.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "\"logs_20260729_101500.zip\"",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = contenido };
        });
        var client = new LogsApiClient(TestHttp.CrearCliente(fake));

        await using var descarga = await client.DescargarZipAsync();

        Assert.Equal("logs_20260729_101500.zip", descarga.NombreArchivo);
        Assert.Equal("/logs/contenido", fake.UltimaRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DescargarZipAsync_SinContentDisposition_UsaUnNombrePorDefecto()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("zip falso")),
        });
        var client = new LogsApiClient(TestHttp.CrearCliente(fake));

        await using var descarga = await client.DescargarZipAsync();

        Assert.Equal("logs.zip", descarga.NombreArchivo);
    }

    [Fact]
    public async Task DescargarZipAsync_ConRespuesta404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "No hay archivos de log para descargar todavía."));
        var client = new LogsApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => client.DescargarZipAsync());
    }
}
