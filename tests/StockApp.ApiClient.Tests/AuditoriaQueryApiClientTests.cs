using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class AuditoriaQueryApiClientTests
{
    [Fact]
    public async Task ObtenerLog_SinFiltros_GETAuditoria_MapeaLosDtos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                fecha = "2026-07-10T12:00:00Z", nombreUsuario = "admin", accion = 1,
                entidad = "Producto", entidadId = 7, detalle = "Alta de producto 'Agua 2L'",
            },
        }));
        var client = new AuditoriaQueryApiClient(TestHttp.CrearCliente(fake));

        var log = await client.ObtenerLogAsync(null, null, null);

        Assert.Equal("/auditoria", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var item = Assert.Single(log);
        Assert.Equal("admin", item.NombreUsuario);
        Assert.Equal(AccionAuditada.AltaProducto, item.Accion);
        Assert.Equal(7, item.EntidadId);
    }

    [Fact]
    public async Task ObtenerLog_ConFiltros_ArmaLaQuery()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new AuditoriaQueryApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerLogAsync(3, new DateTime(2026, 7, 1), new DateTime(2026, 7, 10));

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/auditoria?", pathAndQuery);
        Assert.Contains("usuarioId=3", pathAndQuery);
        Assert.Contains("fechaDesde=2026-07-01T00%3A00%3A00.0000000", pathAndQuery);
        Assert.Contains("fechaHasta=2026-07-10T00%3A00%3A00.0000000", pathAndQuery);
    }

    [Fact]
    public async Task ObtenerLog_403_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new AuditoriaQueryApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ObtenerLogAsync(null, null, null));
    }
}
