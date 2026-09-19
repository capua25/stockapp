using System.Linq;
using StockApp.Documentos;
using UglyToad.PdfPig;
using Xunit;

namespace StockApp.Documentos.Tests;

/// <summary>
/// Guardián del bloqueante hallado en la Tarea 0 (spike, no contemplado por el plan original):
/// <c>PDFsharp-MigraDoc</c> 6.2.4 no resuelve NINGUNA fuente en NINGUNA plataforma sin un
/// <see cref="ResolvedorFuentes"/> registrado en
/// <see cref="PdfSharp.Fonts.GlobalFontSettings.FontResolver"/>. Este test tiene que ponerse en
/// rojo con "No appropriate font found" si se saca la línea de registro de
/// <see cref="PdfExporterMigraDoc"/> — verificado por mutación (ver reporte de la Tarea 1).
/// Cubre además específicamente la cara "Bold" del resolver, que el test de humo
/// (<see cref="HumoPdfTests"/>) no ejercita, y que necesita el encabezado de tabla en negrita
/// de las tareas siguientes.
/// </summary>
public class ResolvedorFuentesTests
{
    [Fact]
    public void GenerarPdfConTextoRegularYNegrita_ElResolverEstaRegistradoYAmbosSonLegibles()
    {
        var bytes = PdfExporterMigraDoc.GenerarPdfDeHumoConNegrita(
            "GUARDIAN-RESOLVER-REGULAR",
            "GUARDIAN-RESOLVER-NEGRITA");

        using var documento = PdfDocument.Open(bytes);

        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("GUARDIAN-RESOLVER-REGULAR", texto);
        Assert.Contains("GUARDIAN-RESOLVER-NEGRITA", texto);
    }
}
