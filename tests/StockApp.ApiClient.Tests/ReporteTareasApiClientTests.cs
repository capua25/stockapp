using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Reportes;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class ReporteTareasApiClientTests
{
    [Fact]
    public async Task ObtenerAsync_GETReportesTareas_ConQueryDeFiltro()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            filas = new[]
            {
                new { clasificador = "Centro", pendientes = 1, enCurso = 0, terminadas = 2, canceladas = 0, total = 3 },
            },
            totalGeneral = 3,
        }));
        var client = new ReporteTareasApiClient(TestHttp.CrearCliente(fake));

        var filtro = new FiltroReporteTareas(
            AgrupadorTareas.Zona, CriterioFechaTareas.Creacion,
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

        var reporte = await client.ObtenerAsync(filtro);

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/reportes/tareas?", pathAndQuery);
        Assert.Contains("agrupador=Zona", pathAndQuery);
        Assert.Contains("criterio=Creacion", pathAndQuery);
        Assert.Contains("desde=2026-01-01T00%3A00%3A00.0000000", pathAndQuery);
        Assert.Contains("hasta=2026-12-31T00%3A00%3A00.0000000", pathAndQuery);

        var fila = Assert.Single(reporte.Filas);
        Assert.Equal("Centro", fila.Clasificador);
        Assert.Equal(3, fila.Total);
        Assert.Equal(3, reporte.TotalGeneral);
    }

    [Fact]
    public async Task ObtenerAsync_ConAgrupadorExpedienteYCriterioCierre_ArmaLaQueryCorrecta()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { filas = Array.Empty<object>(), totalGeneral = 0 }));
        var client = new ReporteTareasApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerAsync(new FiltroReporteTareas(
            AgrupadorTareas.Expediente, CriterioFechaTareas.Cierre,
            new DateTime(2026, 6, 1), new DateTime(2026, 6, 30)));

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.Contains("agrupador=Expediente", pathAndQuery);
        Assert.Contains("criterio=Cierre", pathAndQuery);
    }

    [Fact]
    public async Task ObtenerAsync_403Operador_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new ReporteTareasApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ObtenerAsync(new FiltroReporteTareas(
                AgrupadorTareas.Zona, CriterioFechaTareas.Creacion,
                new DateTime(2026, 1, 1), new DateTime(2026, 12, 31))));
    }
}
