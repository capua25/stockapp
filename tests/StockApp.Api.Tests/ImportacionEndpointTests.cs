using System.IO.Compression;
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
/// F5b Task 6: matriz de autorización + smoke test del contrato del endpoint
/// POST /finanzas/importar/analizar. Los .ods de estos tests son SINTÉTICOS (zip + content.xml
/// armado a mano, mismo mecanismo que OdsTestHelper de StockApp.Infrastructure.Tests, pero sin
/// compartir código entre proyectos): apenas lo mínimo que PlanillaOdsParser exige para no
/// lanzar (columna FECHA/LITERAL en Gastos; PRESUPUESTO/SALDO/LITERAL/FACTURA + SALDO TOTALES en
/// POA). El test de aceptación contra las planillas REALES es Task 7.
/// </summary>
public class ImportacionEndpointTests : ApiTestBase
{
    public ImportacionEndpointTests(ApiFactory factory) : base(factory) { }

    private static readonly string[] MesesGastos =
    {
        "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO",
        "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE",
    };

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static MultipartFormDataContent ArmarMultipart(byte[] gastosBytes, byte[] poaBytes, int ejercicio)
    {
        var contenido = new MultipartFormDataContent();

        var archivoGastos = new ByteArrayContent(gastosBytes);
        archivoGastos.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        contenido.Add(archivoGastos, "gastos", "gastos.ods");

        var archivoPoa = new ByteArrayContent(poaBytes);
        archivoPoa.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        contenido.Add(archivoPoa, "poa", "poa.ods");

        contenido.Add(new StringContent(ejercicio.ToString()), "ejercicio");

        return contenido;
    }

    /// <summary>
    /// .ods mínimo de Gastos: cada una de las 12 hojas mensuales solo tiene la fila de
    /// encabezado con "FECHA" (sin filas de movimiento: el loop de ParsearHojaMesGastos no
    /// itera porque CantidadFilas == 1). "Variables" solo con el encabezado "LITERAL".
    /// </summary>
    private static byte[] CrearOdsGastosMinimo()
    {
        const string filaFecha = """
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>FECHA</text:p></table:table-cell>
            </table:table-row>
            """;
        const string filaLiteral = """
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>LITERAL</text:p></table:table-cell>
            </table:table-row>
            """;

        var tablas = string.Join("\n", MesesGastos.Select(mes => $"""
            <table:table table:name="{mes}">{filaFecha}</table:table>
            """));
        tablas += $"""
            <table:table table:name="Variables">{filaLiteral}</table:table>
            """;

        return EmpaquetarOds(tablas);
    }

    /// <summary>
    /// .ods mínimo de POA: una sola hoja de línea ("Linea1") con PRESUPUESTO/SALDO + LITERAL B
    /// y encabezado FACTURA sin movimientos, más la hoja "SALDO TOTALES" con los 2 saldos.
    /// </summary>
    private static byte[] CrearOdsPoaMinimo()
    {
        const string filaEncabezadoLinea = """
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>PRESUPUESTO</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>SALDO</text:p></table:table-cell>
            </table:table-row>
            """;
        const string filaValoresLinea = """
            <table:table-row>
              <table:table-cell office:value-type="float" office:value="1000"><text:p>1.000,00</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="500"><text:p>500,00</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>LITERAL B</text:p></table:table-cell>
            </table:table-row>
            """;
        const string filaEncabezadoDatos = """
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>FACTURA</text:p></table:table-cell>
            </table:table-row>
            """;

        var tablaLinea = $"""
            <table:table table:name="Linea1">{filaEncabezadoLinea}{filaValoresLinea}{filaEncabezadoDatos}</table:table>
            """;

        const string filaEtiquetasSaldos = """
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>SALDO LITERAL B</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>SALDO LITERAL C</text:p></table:table-cell>
            </table:table-row>
            """;
        const string filaVacia = """<table:table-row><table:table-cell/></table:table-row>""";
        const string filaValoresSaldos = """
            <table:table-row>
              <table:table-cell office:value-type="float" office:value="6643349"><text:p>6.643.349,00</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="4654206"><text:p>4.654.206,00</text:p></table:table-cell>
            </table:table-row>
            """;

        var tablaSaldos = $"""
            <table:table table:name="SALDO TOTALES">{filaEtiquetasSaldos}{filaVacia}{filaVacia}{filaValoresSaldos}</table:table>
            """;

        return EmpaquetarOds(tablaLinea + tablaSaldos);
    }

    private static byte[] EmpaquetarOds(string tablas)
    {
        var contentXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content
                xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
                xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
              <office:body><office:spreadsheet>{tablas}</office:spreadsheet></office:body>
            </office:document-content>
            """;

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entrada = zip.CreateEntry("content.xml");
            using var writer = new StreamWriter(entrada.Open());
            writer.Write(contentXml);
        }
        return stream.ToArray();
    }

    /// <summary>Zip válido pero SIN content.xml: fuerza el InvalidOperationException("...falta
    /// content.xml") de PlanillaOdsParser, envuelto por el servicio como ArgumentException -> 400.
    /// (Bytes puramente al azar, sin estructura zip, no pasan por esta rama: ZipArchive los
    /// rechaza en el constructor con InvalidDataException, no capturada por el servicio — por
    /// eso el "archivo inválido" de este test es un zip real sin la entrada esperada.)</summary>
    private static byte[] CrearZipSinContentXml()
    {
        using var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Intencionalmente vacío.
        }
        return stream.ToArray();
    }

    [Fact]
    public async Task PostAnalizar_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsync(
            "/finanzas/importar/analizar",
            ArmarMultipart(CrearOdsGastosMinimo(), CrearOdsPoaMinimo(), 2026));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalizar_ComoOperador_Devuelve403()
    {
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync(
            "/finanzas/importar/analizar",
            ArmarMultipart(CrearOdsGastosMinimo(), CrearOdsPoaMinimo(), 2026));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalizar_ComoAdmin_ConDosOds_Devuelve200YResultado()
    {
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsync(
            "/finanzas/importar/analizar",
            ArmarMultipart(CrearOdsGastosMinimo(), CrearOdsPoaMinimo(), 2026));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<ResultadoAnalisisDto>();
        Assert.NotNull(resultado);
        Assert.Single(resultado!.LineasPoa);
        Assert.Equal(0, resultado.Resumen.TotalFilas);
    }

    [Fact]
    public async Task PostAnalizar_ArchivoNoOds_Devuelve400()
    {
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsync(
            "/finanzas/importar/analizar",
            ArmarMultipart(CrearZipSinContentXml(), CrearOdsPoaMinimo(), 2026));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Hardening (gap detectado en Task 6): bytes crudos que NO son un zip hacen que
    /// <see cref="ZipArchive"/> lance <see cref="System.IO.InvalidDataException"/> ("End of
    /// Central Directory record could not be found") en vez de <see cref="InvalidOperationException"/>
    /// — antes del fix, ese caso no estaba capturado por ParsearGastosSeguro/ParsearPoaSeguro y el
    /// endpoint devolvía 500 en vez de 400 (resolución pre-flight #10: TODO .ods inválido → 400).
    /// El payload tiene que tener longitud suficiente: con muy pocos bytes (ej. 5) el stream
    /// envuelto por IFormFile.OpenReadStream() dispara un ArgumentOutOfRangeException distinto en
    /// vez de InvalidDataException, que por herencia de ArgumentException ya mapeaba a 400 sin
    /// necesitar este fix — no cubre el gap real.
    /// </summary>
    [Fact]
    public async Task PostAnalizar_ArchivoBasuraCruda_Devuelve400()
    {
        var client = ClienteAutenticado(TokenAdmin());

        var basuraCruda = System.Text.Encoding.UTF8.GetBytes(
            "esto no es un archivo .ods para nada, es texto plano de prueba con longitud suficiente");

        var response = await client.PostAsync(
            "/finanzas/importar/analizar",
            ArmarMultipart(basuraCruda, CrearOdsPoaMinimo(), 2026));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
