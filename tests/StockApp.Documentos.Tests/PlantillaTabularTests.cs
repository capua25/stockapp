using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StockApp.Application.Exportacion;
using StockApp.Documentos;
using UglyToad.PdfPig;
using Xunit;

namespace StockApp.Documentos.Tests;

public class PlantillaTabularTests
{
    private sealed record FilaSimple(string Codigo, string Nombre);

    private static MetadatosDocumento Metadatos(string titulo = "Reporte de prueba") =>
        new(titulo, "Sin filtros aplicados.", "admin");

    [Fact]
    public void Generar_ConDatos_ElHeaderYLosValoresSonLegibles()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar"), new FilaSimple("P002", "Harina") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("Codigo", texto);
        Assert.Contains("Nombre", texto);
        Assert.Contains("P001", texto);
        Assert.Contains("Azúcar", texto);
        Assert.Contains("P002", texto);
        Assert.Contains("Harina", texto);
    }

    [Fact]
    public void Generar_ColeccionVacia_SoloTieneElHeader()
    {
        var items = System.Array.Empty<FilaSimple>();
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("Codigo", texto);
        Assert.Contains("Nombre", texto);
    }

    [Fact]
    public void Generar_ColumnaInexistente_LanzaArgumentException()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var ex = Assert.Throws<ArgumentException>(
            () => plantilla.Generar(items, new[] { "Codigo", "NoExiste" }, Metadatos()));

        Assert.Contains("NoExiste", ex.Message);
    }

    /// <summary>
    /// Guardián del encabezado repetido (MigraDoc HeadingFormat, nativo): con suficientes filas
    /// para forzar 2+ páginas, "Codigo" tiene que aparecer en AMBAS páginas, no solo en la primera.
    /// </summary>
    [Fact]
    public void Generar_ConSuficientesFilasParaDosPaginas_ElHeaderApareceEnAmbasPaginas()
    {
        var items = Enumerable.Range(1, 80)
            .Select(i => new FilaSimple($"P{i:0000}", $"Producto número {i}"))
            .ToList();
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        Assert.True(documento.NumberOfPages >= 2, $"Se esperaban 2+ páginas, hubo {documento.NumberOfPages}.");

        foreach (var pagina in documento.GetPages())
            Assert.Contains("Codigo", pagina.Text);
    }

    /// <summary>Contrato IPdfExporter cumplido por PdfExporterMigraDoc, delegando a PlantillaTabular.</summary>
    [Fact]
    public void PdfExporterMigraDoc_ImplementaIPdfExporter_YGeneraContenidoLegible()
    {
        IPdfExporter exportador = new PdfExporterMigraDoc();
        var items = new[] { new FilaSimple("P001", "Azúcar") };

        var pdf = exportador.Exportar(items, new[] { "Codigo", "Nombre" }, Metadatos("Título del exportador"));

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("P001", texto);
    }

    [Fact]
    public void Generar_ConSeisColumnasOMenos_UsaOrientacionVertical()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        Assert.True(pagina.Height > pagina.Width, "6 columnas o menos debe ser A4 vertical.");
    }

    [Fact]
    public void Generar_ConMasDeSeisColumnas_UsaOrientacionApaisada()
    {
        var items = new[] { new FilaOnceColumnas() };
        var columnas = new[]
        {
            "C1", "C2", "C3", "C4", "C5", "C6", "C7",
        };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, columnas, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        Assert.True(pagina.Width > pagina.Height, "Más de 6 columnas debe ser A4 apaisado.");
    }

    private sealed record FilaOnceColumnas(
        string C1 = "a", string C2 = "b", string C3 = "c", string C4 = "d",
        string C5 = "e", string C6 = "f", string C7 = "g");

    private sealed record FilaNumerica(string Codigo, decimal Precio, double Cantidad, long Total, float Peso);

    /// <summary>
    /// Guardián de formato invariante: fuerza la cultura del hilo a "es-AR" (separador decimal
    /// coma) para demostrar que el PDF NO hereda la cultura del sistema operativo. Sin formato
    /// explícito, <c>decimal</c>/<c>double</c> caerían a <c>valor.ToString()</c> con coma; el
    /// proyecto exige punto (decisión Uruguay, no Argentina, agosto 2026) porque es un documento
    /// oficial que se archiva.
    /// </summary>
    [Fact]
    public void Generar_ConCulturaDeHiloDeComaDecimal_FormateaNumerosConPunto()
    {
        var culturaOriginal = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
            var items = new[] { new FilaNumerica("P001", 45.5m, 10.75, 1000L, 3.25f) };
            var plantilla = new PlantillaTabular();

            var pdf = plantilla.Generar(
                items, new[] { "Codigo", "Precio", "Cantidad", "Total", "Peso" }, Metadatos());

            using var documento = PdfDocument.Open(pdf);
            var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
            Assert.Contains("45.50", texto);
            Assert.Contains("10.75", texto);
            Assert.Contains("1000", texto);
            Assert.Contains("3.25", texto);
            Assert.DoesNotContain("45,50", texto);
            Assert.DoesNotContain("10,75", texto);
            Assert.DoesNotContain("3,25", texto);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
        }
    }
}
