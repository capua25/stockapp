using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

/// <summary>
/// Task 7 F5b — test de aceptación (spec §11, criterio duro): analizando las DOS planillas
/// REALES del municipio (mismos fixtures gitignored que usa el test de aceptación de F5a en
/// StockApp.Infrastructure.Tests), el <see cref="ResultadoAnalisisDto"/> que devuelve
/// POST /finanzas/importar/analizar debe reproducir EXACTO los 3 saldos verificados manualmente
/// contra las planillas: caja a junio 2026 = 43.705; POA Literal B = 6.643.349; POA Literal C =
/// 4.654.206. F5b es READ-ONLY: se asevera sobre la respuesta del endpoint, nada se escribe en
/// la base.
/// </summary>
public class ImportacionAceptacionTests : ApiTestBase
{
    private static readonly string[] MesesEneroAJunio =
        { "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO" };

    public ImportacionAceptacionTests(ApiFactory factory) : base(factory) { }

    private static string RutaFixture(string archivo) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Finanzas", archivo);

    [Fact]
    public async Task PostAnalizar_PlanillasReales_ReproduceLosSaldosDeLaSpec()
    {
        var token = Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var gastosBytes = await File.ReadAllBytesAsync(RutaFixture("PlanillaGastos2026.ods"));
        var poaBytes = await File.ReadAllBytesAsync(RutaFixture("PlanillaPoa2026.ods"));

        var contenido = new MultipartFormDataContent();

        var archivoGastos = new ByteArrayContent(gastosBytes);
        archivoGastos.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        contenido.Add(archivoGastos, "gastos", "PlanillaGastos2026.ods");

        var archivoPoa = new ByteArrayContent(poaBytes);
        archivoPoa.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        contenido.Add(archivoPoa, "poa", "PlanillaPoa2026.ods");

        contenido.Add(new StringContent("2026"), "ejercicio");

        var response = await client.PostAsync("/finanzas/importar/analizar", contenido);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<ResultadoAnalisisDto>();
        Assert.NotNull(resultado);

        // Caja junio 2026 = saldo inicial (NumeroFila=0, ver AgregarSaldoInicialEnero) +
        // Σ(Ingresos ENERO..JUNIO) − Σ(Gastos ENERO..JUNIO).
        var saldoInicial = resultado!.Ingresos.Single(i => i.NumeroFila == 0).Monto!.Value;
        var ingresosEneroAJunio = resultado.Ingresos
            .Where(i => i.NumeroFila != 0 && MesesEneroAJunio.Contains(i.HojaOrigen))
            .Sum(i => i.Monto ?? 0m);
        var gastosEneroAJunio = resultado.Gastos
            .Where(g => MesesEneroAJunio.Contains(g.HojaOrigen))
            .Sum(g => g.Monto ?? 0m);

        var cajaJunio = saldoInicial + ingresosEneroAJunio - gastosEneroAJunio;
        Assert.Equal(43705m, cajaJunio);

        // POA Literal B/C = SaldosPoa (F5b, decisión del usuario): la fuente de verdad es la
        // hoja "SALDO TOTALES" de la planilla, NO la suma de SaldoPlanilla por Literal a través
        // de LineasPoa — esa suma puede no cuadrar contra la planilla real (ver
        // docs/finanzas-discrepancias-planilla-poa-2026.md).
        Assert.Equal(6643349m, resultado.SaldosPoa.SaldoLiteralB);
        Assert.Equal(4654206m, resultado.SaldosPoa.SaldoLiteralC);
    }
}
