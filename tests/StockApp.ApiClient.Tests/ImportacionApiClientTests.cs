using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class ImportacionApiClientTests
{
    [Fact]
    public async Task ListarHistorialAsync_GETParseaListaJson()
    {
        var dtos = new[]
        {
            new ImportacionHistorialDto(Guid.NewGuid(), DateTime.UtcNow, 2026, "admin", false),
        };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("finanzas/importar/historial", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dtos);
        });
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ListarHistorialAsync();

        Assert.Single(resultado);
        Assert.Equal("admin", resultado[0].Usuario);
    }

    [Fact]
    public async Task RevertirAsync_POSTConIdEnLaRuta_ParseaResultado()
    {
        var id = Guid.NewGuid();
        var dto = new ResultadoReversionDto(id, 2, 1, 1, 0, 0);
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"finanzas/importar/revertir/{id}", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dto);
        });
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.RevertirAsync(id);

        Assert.Equal(2, resultado.GastosRevertidos);
    }

    private static ConfirmarImportacionDto PayloadMinimo() => new(
        Ejercicio: 2026,
        Forzar: false,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>(),
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task ConfirmarAsync_POSTConJson_ParseaResultado()
    {
        var idImportacion = Guid.NewGuid();
        var dto = new ResultadoConfirmacionDto(
            idImportacion, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("finanzas/importar/confirmar", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dto);
        });
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ConfirmarAsync(PayloadMinimo());

        Assert.Equal(idImportacion, resultado.IdImportacion);
        Assert.Equal(1, resultado.ProveedoresCreados);
    }

    [Fact]
    public async Task ConfirmarAsync_400ConErroresEstructurados_LanzaValidacionImportacionException()
    {
        var errores = new Dictionary<string, string[]>
        {
            ["Gastos[3].Fuente"] = new[] { "La fuente 'X' no existe ni fue declarada nueva" },
            ["Gastos[3].FechaVencimiento"] = new[] { "Requerido" },
        };
        var fake = new FakeHttpHandler(request => TestHttp.Json(
            new { title = "Error.", detail = "El payload tiene errores de validación.", status = 400, errors = errores },
            HttpStatusCode.BadRequest));
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(
            () => client.ConfirmarAsync(PayloadMinimo()));

        Assert.Equal(
            "La fuente 'X' no existe ni fue declarada nueva",
            ex.Errores["Gastos[3].Fuente"][0]);
        Assert.Equal("Requerido", ex.Errores["Gastos[3].FechaVencimiento"][0]);
    }

    [Fact]
    public async Task AnalizarAsync_EnviaMultipartConDosArchivosYEjercicio_ParseaResultado()
    {
        var dto = new ResultadoAnalisisDto(
            Ingresos: new List<IngresoAnalizadoDto>(),
            Gastos: new List<GastoAnalizadoDto>(),
            LineasPoa: new List<LineaPoaAnalizadaDto>(),
            MaestrosNuevos: new MaestrosNuevosDto(
                new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            Resumen: new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
            SaldosPoa: new SaldosTotalesPoaOds(0m, 0m));
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("finanzas/importar/analizar", request.RequestUri!.PathAndQuery.TrimStart('/'));
            Assert.IsType<MultipartFormDataContent>(request.Content);
            return TestHttp.Json(dto);
        });
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.AnalizarAsync(
            "gastos.ods", new byte[] { 1, 2, 3 }, "poa.ods", new byte[] { 4, 5, 6 }, 2026);

        Assert.Equal(0, resultado.Resumen.TotalFilas);
    }
}
