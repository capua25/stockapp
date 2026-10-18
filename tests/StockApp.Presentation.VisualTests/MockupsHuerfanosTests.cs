using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Cada mockup aprobado de docs/ui-v2/mockups es el contrato de una pantalla: tiene que haber un test de este proyecto
/// que lo compare con la app. Un mockup que ningún test cita es un contrato que nadie custodia (o un archivo muerto).
/// Se excluyen las carpetas que no son pantallas: referencia, _base y _fixtures.
/// </summary>
public class MockupsHuerfanosTests
{
    private static readonly string[] CarpetasExcluidas = ["referencia", "_base", "_fixtures"];

    private static IReadOnlyList<string> Mockups()
    {
        var raiz = RutasRepo.CarpetaMockups;
        return Directory.EnumerateFiles(raiz, "*.html", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(raiz, f).Replace('\\', '/'))
            .Where(r => !CarpetasExcluidas.Any(c => r.StartsWith(c + "/", StringComparison.Ordinal)))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
    }

    private static string TextoDeLosTests()
    {
        var carpeta = Path.Combine(RutasRepo.Raiz, "tests", "StockApp.Presentation.VisualTests");
        var sep = Path.DirectorySeparatorChar;
        var archivos = Directory.EnumerateFiles(carpeta, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}"));
        // Las líneas de comentario no cuentan: citar un mockup en un ejemplo de documentación no lo custodia.
        return string.Join("\n", archivos.SelectMany(File.ReadLines).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    [Fact]
    public void TodoMockupLoUsaUnTest()
    {
        var mockups = Mockups();
        Assert.True(mockups.Count > 150, $"El barrido solo encontró {mockups.Count} mockups: el guardián no custodiaría nada.");

        var texto = TextoDeLosTests();
        var huerfanos = mockups.Where(m => !texto.Contains(m, StringComparison.Ordinal)).ToList();

        Assert.True(huerfanos.Count == 0, "Mockups que ningún test de VisualTests cita: " + string.Join(", ", huerfanos));
    }
}
