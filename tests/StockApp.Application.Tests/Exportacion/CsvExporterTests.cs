using StockApp.Application.Exportacion;
using Xunit;

namespace StockApp.Application.Tests.Exportacion;

/// <summary>
/// Verifica que CsvExporter cumple RFC 4180 con las correcciones obligatorias del
/// Incremento 6: BOM UTF-8 al inicio y fin de línea CRLF explícito.
/// El escaping envuelve en comillas solo los campos con caracteres especiales
/// (coma, comilla doble, salto de línea) y duplica las comillas internas.
/// </summary>
public class CsvExporterTests
{
    private readonly ICsvExporter _exporter = new CsvExporter();

    private sealed record Fila(string Nombre, string Detalle);

    [Fact]
    public void Exportar_CampoSimple_SinComillas()
    {
        var items = new[] { new Fila("Harina", "ok") };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Detalle" });

        Assert.Contains("Harina,ok\r\n", resultado);
        Assert.DoesNotContain("\"Harina\"", resultado);
    }

    [Fact]
    public void Exportar_CampoConComa_EntreComillas()
    {
        var items = new[] { new Fila("Harina, 000", "ok") };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Detalle" });

        Assert.Contains("\"Harina, 000\",ok\r\n", resultado);
    }

    [Fact]
    public void Exportar_CampoConComillaDoble_Duplicada()
    {
        var items = new[] { new Fila("Caja \"grande\"", "ok") };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Detalle" });

        Assert.Contains("\"Caja \"\"grande\"\"\",ok\r\n", resultado);
    }

    [Fact]
    public void Exportar_CampoConSaltoDeLinea_EntreComillas()
    {
        var items = new[] { new Fila("Linea1\nLinea2", "ok") };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Detalle" });

        Assert.Contains("\"Linea1\nLinea2\",ok\r\n", resultado);
    }

    [Fact]
    public void Exportar_CampoVacio_SinComillas()
    {
        var items = new[] { new Fila("Harina", "") };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Detalle" });

        Assert.Contains("Harina,\r\n", resultado);
        Assert.DoesNotContain("Harina,\"\"", resultado);
    }

    [Fact]
    public void Exportar_BomUtf8_AlInicio()
    {
        var items = new[] { new Fila("Harina", "ok") };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Detalle" });

        Assert.Equal('﻿', resultado[0]);
    }

    [Fact]
    public void Exportar_Acentos_NoCorrompen()
    {
        var items = new[] { new Fila("Maní salado", "café") };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Detalle" });

        Assert.Contains("Maní salado,café\r\n", resultado);
    }

    [Fact]
    public void Exportar_FinDeLinea_EsCRLF()
    {
        var items = new[] { new Fila("Harina", "ok") };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Detalle" });

        // El header termina en CRLF
        Assert.Contains("Nombre,Detalle\r\n", resultado);
        // No debe existir ningún \n que no esté precedido por \r
        var sinCrlf = resultado.Replace("\r\n", "");
        Assert.DoesNotContain("\n", sinCrlf);
    }

    [Fact]
    public void Exportar_OrdenColumnas_Deterministico()
    {
        var items = new[] { new Fila("Harina", "ok") };

        // columnOrder invertido respecto a la declaración del record
        var resultado = _exporter.Exportar(items, new[] { "Detalle", "Nombre" });

        Assert.StartsWith("﻿Detalle,Nombre\r\nok,Harina\r\n", resultado);
    }
}
