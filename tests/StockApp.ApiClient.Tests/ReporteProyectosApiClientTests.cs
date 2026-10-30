using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Reportes;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class ReporteProyectosApiClientTests
{
    [Fact]
    public async Task ObtenerAsync_GETReportesProyectos_ConQueryDeFiltro()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            filas = new[]
            {
                new { clasificador = "Centro", pendientes = 1, enCurso = 0, terminados = 2, cancelados = 0, total = 3 },
            },
            totalGeneral = new { clasificador = "Total general", pendientes = 1, enCurso = 0, terminados = 2, cancelados = 0, total = 3 },
        }));
        var client = new ReporteProyectosApiClient(TestHttp.CrearCliente(fake));

        var filtro = new FiltroReporteProyectos(
            AgrupadorProyectos.Zona, CriterioFechaProyectos.Creacion,
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

        var reporte = await client.ObtenerAsync(filtro);

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/reportes/proyectos?", pathAndQuery);
        Assert.Contains("agrupador=Zona", pathAndQuery);
        Assert.Contains("criterio=Creacion", pathAndQuery);
        Assert.Contains("desde=2026-01-01T00%3A00%3A00.0000000", pathAndQuery);
        Assert.Contains("hasta=2026-12-31T00%3A00%3A00.0000000", pathAndQuery);

        var fila = Assert.Single(reporte.Filas);
        Assert.Equal("Centro", fila.Clasificador);
        Assert.Equal(3, fila.Total);
        Assert.Equal(3, reporte.TotalGeneral.Total);
        Assert.Equal(2, reporte.TotalGeneral.Terminados);
    }

    [Fact]
    public async Task ObtenerAsync_ConAgrupadorOrganismoYCriterioCierre_ArmaLaQueryCorrecta()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { filas = Array.Empty<object>(), totalGeneral = new { clasificador = "Total general", pendientes = 0, enCurso = 0, terminados = 0, cancelados = 0, total = 0 } }));
        var client = new ReporteProyectosApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerAsync(new FiltroReporteProyectos(
            AgrupadorProyectos.OrganismoResponsable, CriterioFechaProyectos.Cierre,
            new DateTime(2026, 6, 1), new DateTime(2026, 6, 30)));

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.Contains("agrupador=OrganismoResponsable", pathAndQuery);
        Assert.Contains("criterio=Cierre", pathAndQuery);
    }

    [Fact]
    public async Task ObtenerAsync_403Operador_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new ReporteProyectosApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ObtenerAsync(new FiltroReporteProyectos(
                AgrupadorProyectos.Zona, CriterioFechaProyectos.Creacion,
                new DateTime(2026, 1, 1), new DateTime(2026, 12, 31))));
    }
}
