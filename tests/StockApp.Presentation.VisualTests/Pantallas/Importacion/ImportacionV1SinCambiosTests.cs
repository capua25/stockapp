using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views.Finanzas;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;
using StockApp.UiV2.Herramientas.Imagenes;

namespace StockApp.Presentation.VisualTests.Pantallas.Importacion;

/// <summary>
/// La importación sigue en v1 (Fase 8 fuera del rediseño): conserva el tema viejo (HeaderVista, Border.card, los botones
/// primary/secondary/ghost, las grillas del asistente). Esta es su custodia: cada pantalla se renderiza con la vista real y
/// se compara PÍXEL A PÍXEL (tolerancia 0) con la captura de referencia versionada en <c>Referencias/</c>. Si borrar una pieza
/// del tema viejo cambia un píxel de la importación, esto se pone en rojo.
///
/// Para regenerar las referencias: <c>STOCKAPP_GENERAR_REFERENCIAS_V1=1</c> las copia y falla pidiendo revisarlas.
/// </summary>
[Trait("Categoria", "ImportacionV1")]
public class ImportacionV1SinCambiosTests
{
    private static string CarpetaReferencias =>
        Path.Combine(RutasRepo.Raiz, "tests", "StockApp.Presentation.VisualTests", "Pantallas", "Importacion", "Referencias");

    private static string CarpetaSalida => Path.Combine(RutasRepo.Raiz, "artifacts", "ui-v2", "importacion-v1");

    private static Control Vista(string pantalla, out object viewModel)
    {
        switch (pantalla)
        {
            case "importacion-inicio":
                viewModel = DatosImportacion.Importacion();
                return new ImportacionView();
            case "importacion-nueva-revision":
                viewModel = DatosImportacion.NuevaEnRevision();
                return new NuevaImportacionView();
            case "importacion-historial":
                viewModel = DatosImportacion.HistorialConTresFilas();
                return new HistorialImportacionesView();
            default:
                throw new ArgumentOutOfRangeException(nameof(pantalla), pantalla, null);
        }
    }

    [AvaloniaTheory]
    [InlineData("importacion-inicio", false)]
    [InlineData("importacion-inicio", true)]
    [InlineData("importacion-nueva-revision", false)]
    [InlineData("importacion-nueva-revision", true)]
    [InlineData("importacion-historial", false)]
    [InlineData("importacion-historial", true)]
    public void ImportacionV1_NoCambioDeAspecto(string pantalla, bool municipio)
    {
        var tamano = municipio ? TamanoLienzo.Municipio : TamanoLienzo.Estandar;
        var nombre = pantalla + tamano.SufijoPantalla.Replace('@', '_') + ".png";
        var candidata = Path.Combine(CarpetaSalida, nombre);
        var referencia = Path.Combine(CarpetaReferencias, nombre);
        var diff = Path.Combine(CarpetaSalida, Path.ChangeExtension(nombre, null) + ".diff.png");

        var vista = Vista(pantalla, out var viewModel);
        RenderizadorVista.RenderizarAPng(vista, viewModel, candidata, tamano: tamano);

        if (!File.Exists(referencia))
        {
            if (Environment.GetEnvironmentVariable("STOCKAPP_GENERAR_REFERENCIAS_V1") == "1")
            {
                Directory.CreateDirectory(CarpetaReferencias);
                File.Copy(candidata, referencia);
                Assert.Fail($"referencia generada: revisarla y commitearla ({referencia})");
            }

            Assert.Fail($"falta la referencia {referencia}; se genera con STOCKAPP_GENERAR_REFERENCIAS_V1=1");
        }

        var resultado = ComparadorImagenes.Comparar(referencia, candidata, diff, toleranciaCanal: 0);
        Assert.True(resultado.PixelesDistintos == 0,
            $"la importación v1 cambió de aspecto: {resultado.PixelesDistintos} píxeles distintos, ver {diff}");
    }
}
