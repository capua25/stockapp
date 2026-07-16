using System.Globalization;
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

        Assert.Equal('\uFEFF', resultado[0]);
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

    [Fact]
    public void Exportar_ColumnaInexistente_LanzaArgumentException()
    {
        var items = new[] { new Fila("Harina", "ok") };

        var ex = Assert.Throws<ArgumentException>(
            () => _exporter.Exportar(items, new[] { "Nombre", "NoExiste" }));

        Assert.Contains("NoExiste", ex.Message);
    }

    [Fact]
    public void Exportar_ColeccionVacia_SoloBomYHeader()
    {
        var items = Array.Empty<Fila>();

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Detalle" });

        Assert.Equal("\uFEFFNombre,Detalle\r\n", resultado);
    }

    // \u2500\u2500 Fix bug de huso horario: columnas DateTime se exportan en hora LOCAL \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    private sealed record FilaConFecha(string Nombre, DateTime Fecha);

    /// <summary>
    /// Bug real: CsvExporter serializa por reflexi\u00F3n con <c>valor.ToString()</c>, lo que
    /// emit\u00EDa la fecha UTC cruda (misma causa ra\u00EDz que la grilla, ver
    /// FechaUtcALocalConverter). Debe convertir a hora local con formato expl\u00EDcito, igual
    /// que la UI, para que el CSV no contradiga lo que el usuario ve en pantalla.
    /// </summary>
    [Fact]
    public void Exportar_ColumnaDateTime_ConvierteAHoraLocalConFormatoExplicito()
    {
        var fechaUtc = new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Utc);
        var items = new[] { new FilaConFecha("Harina", fechaUtc) };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Fecha" });

        var esperado = fechaUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        Assert.Contains($"Harina,{esperado}\r\n", resultado);
    }

    [Fact]
    public void Exportar_ColumnaDateTimeConKindUnspecified_SeInterpretaComoUtc()
    {
        // Reproduce el gotcha de EF Core + SQLite: al releer, el DateTime vuelve Unspecified
        // aunque el valor almacenado sea un instante UTC.
        var comoVuelveDeSqlite = new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Unspecified);
        var items = new[] { new FilaConFecha("Harina", comoVuelveDeSqlite) };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Fecha" });

        var esperado = DateTime.SpecifyKind(comoVuelveDeSqlite, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        Assert.Contains($"Harina,{esperado}\r\n", resultado);
    }

    // ── Bug real (verificación orgánica Fase 2 Finanzas): export CSV de Gastos corría la
    // fecha un día para atrás (grilla 16/07/2026 → CSV "15/07/2026 21:00:00"). Causa: la
    // columna Fecha es un valor date-only (medianoche UTC) pero pasaba por el mismo camino
    // que un DateTime real, que SIEMPRE convierte a hora local. DateOnly es un tipo distinto
    // a propósito: no representa un instante, así que no debe convertirse.

    private sealed record FilaConFechaSola(string Nombre, DateOnly Fecha);

    [Fact]
    public void Exportar_ColumnaDateOnly_SinConversionDeHusoNiHora()
    {
        var fecha = new DateOnly(2026, 7, 16);
        var items = new[] { new FilaConFechaSola("Gasto", fecha) };

        var resultado = _exporter.Exportar(items, new[] { "Nombre", "Fecha" });

        Assert.Contains("Gasto,16/07/2026\r\n", resultado);
    }
}
