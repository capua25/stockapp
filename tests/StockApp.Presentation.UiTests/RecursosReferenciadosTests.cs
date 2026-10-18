using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Un <c>{DynamicResource X}</c> o <c>{StaticResource X}</c> sin definición no explota: el control cae a su valor por
/// defecto (un botón sin fondo, un margen en cero). Borrar un token del tema viejo que parecía muerto, pero que consume
/// una vista viva (la importación, que sigue en v1), se vería como un defecto visual sin ningún rojo. Este test junta toda
/// clave referenciada en los .axaml y en el código de StockApp.Presentation y exige que la app la resuelva.
/// </summary>
public class RecursosReferenciadosTests
{
    private static readonly Regex ReferenciaEnXaml =
        new(@"\{\s*(?:DynamicResource|StaticResource)\s+(?:ResourceKey\s*=\s*)?([A-Za-z_][\w.]*)\s*\}", RegexOptions.Compiled);

    private static readonly Regex ReferenciaEnCodigo =
        new(@"(?:FindResource|TryGetResource)\(\s*""([A-Za-z_][\w.]*)""", RegexOptions.Compiled);

    private static readonly Regex ClaveDeclarada = new(@"x:Key\s*=\s*""([^""]+)""", RegexOptions.Compiled);

    /// <summary>Claves referenciadas en el texto, de las tres formas: <c>DynamicResource</c>, <c>StaticResource</c> y el código.</summary>
    internal static List<string> ReferenciasEn(string texto, bool esCodigo) =>
        (esCodigo ? ReferenciaEnCodigo : ReferenciaEnXaml).Matches(texto).Select(m => m.Groups[1].Value).ToList();

    private static string CarpetaPresentation([CallerFilePath] string archivoDeEsteTest = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivoDeEsteTest)!, "..", "..", "src", "StockApp.Presentation"));

    private static bool SeResuelveAlCargarse(string rutaRelativa)
    {
        var ruta = rutaRelativa.Replace('\\', '/');
        var archivo = Path.GetFileName(ruta);
        return ruta.StartsWith("Themes/", StringComparison.Ordinal)
               || (ruta.StartsWith("Controls/", StringComparison.Ordinal) && archivo.EndsWith(".axaml", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void RecursosReferenciados_SeResuelvenEnLaApp()
    {
        var raiz = CarpetaPresentation();
        var archivos = Directory.EnumerateFiles(raiz, "*.*", SearchOption.AllDirectories)
            .Where(a => a.EndsWith(".axaml", StringComparison.Ordinal) || a.EndsWith(".cs", StringComparison.Ordinal))
            .Where(a => !a.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                        && !a.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Select(a => (Ruta: a, Relativa: Path.GetRelativePath(raiz, a).Replace('\\', '/')))
            .Where(a => !SeResuelveAlCargarse(a.Relativa))
            .ToList();

        Assert.True(archivos.Count > 100, "El barrido no encontró los archivos: el test no custodiaría nada.");

        var faltan = new SortedSet<string>(StringComparer.Ordinal);
        var revisadas = 0;
        foreach (var (ruta, relativa) in archivos)
        {
            var texto = File.ReadAllText(ruta);
            var esCodigo = relativa.EndsWith(".cs", StringComparison.Ordinal);
            // Una clave declarada en el propio archivo (Resources locales) se resuelve por su cuenta.
            var locales = esCodigo
                ? new HashSet<string>()
                : ClaveDeclarada.Matches(texto).Select(m => m.Groups[1].Value).ToHashSet();

            foreach (var clave in ReferenciasEn(texto, esCodigo).Distinct())
            {
                revisadas++;
                if (locales.Contains(clave)) continue;
                if (!Avalonia.Application.Current!.TryGetResource(clave, ThemeVariant.Light, out _))
                    faltan.Add($"{clave} ({relativa})");
            }
        }

        Assert.True(revisadas > 50, $"Solo se revisaron {revisadas} referencias: el barrido no encontró las vistas.");
        Assert.True(faltan.Count == 0, "Recursos referenciados que la app no resuelve: " + string.Join(", ", faltan));
    }

    [Fact]
    public void RecursosReferenciados_DetectorVeDynamicStaticYFindResource()
    {
        var xaml = """<Border Padding="{StaticResource PaddingCard}" Background="{DynamicResource FondoBrush}" Margin="{ DynamicResource  Espacio3 }" Tag="{Binding Fondo}" />""";
        Assert.Equal(new[] { "PaddingCard", "FondoBrush", "Espacio3" }, ReferenciasEn(xaml, esCodigo: false));

        var codigo = """var a = FindResource("WarningBrush"); Application.Current.TryGetResource("Espacio2", ThemeVariant.Light, out _); var b = Algo("NoEsUnRecurso");""";
        Assert.Equal(new[] { "WarningBrush", "Espacio2" }, ReferenciasEn(codigo, esCodigo: true));
    }
}
