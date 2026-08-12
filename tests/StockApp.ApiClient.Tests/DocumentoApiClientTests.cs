using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class DocumentoApiClientTests
{
    [Fact]
    public async Task RegistrarAsync_POSTDocumentos_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 9 }, HttpStatusCode.Created));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.RegistrarAsync(new DocumentoAdministrativo
        {
            Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
            FechaEmision = new DateTime(2026, 1, 15), Descripcion = "Expediente de prueba",
        });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/documentos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"numero\":\"0087\"", fake.UltimoBody);
        Assert.Equal(9, id);
    }

    [Fact]
    public async Task RegistrarAsync_409_LanzaReglaDeNegocioException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe un Expediente 0087/2026."));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.RegistrarAsync(new DocumentoAdministrativo
        {
            Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
            FechaEmision = new DateTime(2026, 1, 15), Descripcion = "x",
        }));
        Assert.Equal("Ya existe un Expediente 0087/2026.", ex.Message);
    }

    [Fact]
    public async Task EditarAsync_PUTDocumentosId_ConLaRutaCorrecta()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        await client.EditarAsync(5, new DatosEdicionDocumento(
            "0088", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "corregido"));

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/documentos/5", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"numero\":\"0088\"", fake.UltimoBody);
    }

    [Fact]
    public async Task ListarActivosAsync_GETDocumentosActivos_ConQueryYDeserializa()
    {
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal("documentos/activos", request.RequestUri!.AbsolutePath.TrimStart('/'));
            Assert.Contains("tipo=0", request.RequestUri.Query);
            return TestHttp.Json(new[]
            {
                new
                {
                    id = 1, numero = "0087", anio = 2026, tipo = TipoDocumento.Expediente,
                    fechaEmision = new DateTime(2026, 1, 15), descripcion = "x", estado = EstadoDocumento.Pendiente,
                    registradoPorUsuarioId = 1, registradoPorNombre = "admin",
                    fechaRegistro = new DateTime(2026, 1, 15), fechaCierre = (DateTime?)null,
                    esActivo = true, esCerrado = false,
                    eventos = Array.Empty<object>(),
                },
            });
        });
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var documentos = await client.ListarActivosAsync(new FiltroDocumentos(TipoDocumento.Expediente, null, null, null));

        var documento = Assert.Single(documentos);
        Assert.Equal("0087", documento.Numero);
        Assert.Equal("admin", documento.RegistradoPor!.NombreUsuario);
    }

    [Fact]
    public async Task ListarHistorialAsync_GETDocumentosHistorial_ConAnioEnQuery()
    {
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal("documentos/historial", request.RequestUri!.AbsolutePath.TrimStart('/'));
            Assert.Contains("anio=2026", request.RequestUri.Query);
            return TestHttp.Json(Array.Empty<object>());
        });
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var documentos = await client.ListarHistorialAsync(new FiltroDocumentos(null, 2026, null, null));

        Assert.Empty(documentos);
    }

    [Fact]
    public async Task ListarHistorialAsync_400_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "El filtro 'anio' es obligatorio para consultar el historial."));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => client.ListarHistorialAsync(new FiltroDocumentos(null, null, null, null)));
        Assert.Equal("El filtro 'anio' es obligatorio para consultar el historial.", ex.Message);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_GETDocumentosId_Deserializa()
    {
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal("documentos/5", request.RequestUri!.AbsolutePath.TrimStart('/'));
            return TestHttp.Json(new
            {
                id = 5, numero = "0087", anio = 2026, tipo = TipoDocumento.Expediente,
                fechaEmision = new DateTime(2026, 1, 15), descripcion = "x", estado = EstadoDocumento.Pendiente,
                registradoPorUsuarioId = 1, registradoPorNombre = (string?)null,
                fechaRegistro = new DateTime(2026, 1, 15), fechaCierre = (DateTime?)null,
                esActivo = true, esCerrado = false,
                eventos = Array.Empty<object>(),
            });
        });
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var documento = await client.ObtenerPorIdAsync(5);

        Assert.Equal("0087", documento!.Numero);
        Assert.Null(documento.RegistradoPor);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_404_DevuelveNull()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.NotFound, "Documento 999 no encontrado."));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var documento = await client.ObtenerPorIdAsync(999);

        Assert.Null(documento);
    }

    [Fact]
    public async Task AnularAsync_POSTAnular_ConLaRutaYElMotivo()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        await client.AnularAsync(5, "el interesado desistió");

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/documentos/5/anular", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"motivo\":\"el interesado desistió\"", fake.UltimoBody);
    }

    [Fact]
    public async Task ReabrirAsync_403_LanzaUnauthorizedAccessException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol Operador no tiene permiso para ejecutar la acción 'documentos.administrar'."));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.ReabrirAsync(5, "motivo"));
    }
}
