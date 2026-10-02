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
///
/// [AvaloniaFact]
/// public void Productos_IgualAlMockup_1366x768() =>
///     ComparacionVisual.CompararConMockup(new ProductoListView(), vmConDatosFijos, "inventario/productos.html",
///         tamano: TamanoLienzo.Municipio);
/// </code>
/// Toda pantalla se compara a los dos tamaños con el MISMO umbral (spec §6.1/§6.2). La corrida a
/// 1366x768 deja su página en "&lt;pantalla&gt;@1366x768" y no pisa la del tamaño estándar.
/// </summary>
public static class ComparacionVisual
{
    public static string CarpetaSalida => Path.Combine(RutasRepo.Raiz, "artifacts", "ui-v2", "comparacion");

    public static ResultadoComparacion CompararConMockup(
        Control vista,
        object? viewModel,
        string rutaMockup,
        double umbral = UmbralComparacion.PorcentajePorDefecto,
        int teselasDensasMax = UmbralComparacion.TeselasDensasPorDefecto,
        UmbralAprobado? aprobado = null,
        IReadOnlyCollection<string>? recortesPermitidos = null,
        TamanoLienzo? tamano = null)
    {
        // Antes de renderizar: un umbral sin aprobación registrada no llega a correr (spec §6.5).
        teselasDensasMax = UmbralComparacion.TeselasMaximas(teselasDensasMax, aprobado);
        umbral = UmbralComparacion.PorcentajeMaximo(umbral, aprobado);
        var rutaHtml = Path.GetFullPath(Path.Combine(RutasRepo.CarpetaMockups, rutaMockup));
        var pantalla = NombrePantalla(rutaMockup, tamano);
        var carpeta = Directory.CreateDirectory(Path.Combine(CarpetaSalida, pantalla)).FullName;
        // I2b (revisión final): si la captura de abajo lanza, esta llamada evita que quede vigente
        // el resultado.json (y la fila del índice) de una corrida anterior aprobada.
        PaginaComparacion.BorrarResultadoAnterior(carpeta);
        var pngMockup = Path.Combine(carpeta, "mockup.png");
        var pngApp = Path.Combine(carpeta, "app.png");
        var pngDiff = Path.Combine(carpeta, "diff.png");

        // Playwright es async y este código corre en el dispatcher de Avalonia: se captura en el
        // thread pool (sin contexto de sincronización) y se espera. Verificado sin deadlock.
        Task.Run(() => CapturadorMockup.CapturarAsync(rutaHtml, pngMockup, tamano)).GetAwaiter().GetResult();
        IReadOnlyList<string> recortes = [];
        RenderizadorVista.RenderizarAPng(vista, viewModel, pngApp, v => recortes = GuardianTextoRecortado.Buscar(v, recortesPermitidos),
            tamano);

        var resultado = ComparadorImagenes.Comparar(pngMockup, pngApp, pngDiff);
        // I2a (revisión final): Escribir ahora recibe los recortes, así que la página/resultado.json
        // ya nacen RECHAZADA cuando hay texto recortado, en vez de un veredicto que solo mira píxeles.
        var pagina = PaginaComparacion.Escribir(carpeta, pantalla, resultado, umbral, teselasDensasMax, recortes);

        // Los píxeles no ven un glifo que falta (spec §6.5): el recorte de texto se verifica aparte.
        Assert.True(recortes.Count == 0,
            $"{pantalla}: texto recortado por el layout (la comparación de píxeles no lo detecta):"
            + Environment.NewLine + string.Join(Environment.NewLine, recortes) + Environment.NewLine + $"Comparación: {pagina}");
        Assert.True(
            PaginaComparacion.Aprobada(resultado, umbral, teselasDensasMax, recortes),
            string.Create(CultureInfo.InvariantCulture,
                $"{pantalla}: {resultado.PorcentajeDistinto:0.000}% de píxeles distintos (umbral {umbral:0.000}%) y "
                + $"{resultado.TeselasDensas} teselas densas (máximo {teselasDensasMax}). Comparación: {pagina}"));
        return resultado;
    }

    public static string NombrePantalla(string rutaMockup, TamanoLienzo? tamano = null) =>
        Path.ChangeExtension(rutaMockup.Replace('\\', '/'), null)!.Replace('/', '-')
        + (tamano ?? TamanoLienzo.Estandar).SufijoPantalla;
}
