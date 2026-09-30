using System.Globalization;
using Avalonia.Controls;
using StockApp.UiV2.Herramientas;
using StockApp.UiV2.Herramientas.Imagenes;
using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Helper que cada fase usa para su test de regresión visual por pantalla:
/// <code>
/// [AvaloniaFact]
/// public void Productos_IgualAlMockup() =>
///     ComparacionVisual.CompararConMockup(new ProductoListView(), vmConDatosFijos, "inventario/productos.html");
/// </code>
/// </summary>
public static class ComparacionVisual
{
    public static string CarpetaSalida => Path.Combine(RutasRepo.Raiz, "artifacts", "ui-v2", "comparacion");

    public static ResultadoComparacion CompararConMockup(
        Control vista,
        object? viewModel,
        string rutaMockup,
        double umbral = UmbralComparacion.PorcentajePorDefecto,
        int teselasDensasMax = UmbralComparacion.TeselasDensasPorDefecto)
    {
        var rutaHtml = Path.GetFullPath(Path.Combine(RutasRepo.CarpetaMockups, rutaMockup));
        var pantalla = NombrePantalla(rutaMockup);
        var carpeta = Directory.CreateDirectory(Path.Combine(CarpetaSalida, pantalla)).FullName;
        var pngMockup = Path.Combine(carpeta, "mockup.png");
        var pngApp = Path.Combine(carpeta, "app.png");
        var pngDiff = Path.Combine(carpeta, "diff.png");

        // Playwright es async y este código corre en el dispatcher de Avalonia: se captura en el
        // thread pool (sin contexto de sincronización) y se espera. Verificado sin deadlock.
        Task.Run(() => CapturadorMockup.CapturarAsync(rutaHtml, pngMockup)).GetAwaiter().GetResult();
        IReadOnlyList<string> recortes = [];
        RenderizadorVista.RenderizarAPng(vista, viewModel, pngApp, v => recortes = GuardianTextoRecortado.Buscar(v));

        var resultado = ComparadorImagenes.Comparar(pngMockup, pngApp, pngDiff);
        var pagina = PaginaComparacion.Escribir(carpeta, pantalla, resultado, umbral, teselasDensasMax);

        // Los píxeles no ven un glifo que falta (spec §6.5): el recorte de texto se verifica aparte.
        Assert.True(recortes.Count == 0,
            $"{pantalla}: texto recortado por el layout (la comparación de píxeles no lo detecta):"
            + Environment.NewLine + string.Join(Environment.NewLine, recortes) + Environment.NewLine + $"Comparación: {pagina}");
        Assert.True(
            PaginaComparacion.Aprobada(resultado, umbral, teselasDensasMax),
            string.Create(CultureInfo.InvariantCulture,
                $"{pantalla}: {resultado.PorcentajeDistinto:0.000}% de píxeles distintos (umbral {umbral:0.000}%) y "
                + $"{resultado.TeselasDensas} teselas densas (máximo {teselasDensasMax}). Comparación: {pagina}"));
        return resultado;
    }

    public static string NombrePantalla(string rutaMockup) =>
        Path.ChangeExtension(rutaMockup.Replace('\\', '/'), null)!.Replace('/', '-');
}
