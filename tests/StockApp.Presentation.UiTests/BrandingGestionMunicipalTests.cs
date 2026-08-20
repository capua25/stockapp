using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián de branding (rename "StockApp" → "Gestión Municipal", 2026-08-20): ninguna vista
/// debe volver a mostrar el nombre viejo del producto como texto visible en pantalla.
///
/// Analiza el <c>.axaml</c> crudo como texto (mismo enfoque que
/// <see cref="EspaciadoBotonesEnFilaDeGridTests"/> y <c>ReflexionVistaViewModelTests</c>), no
/// monta las vistas: <see cref="GuardianDePatronTests"/> monta sin datos, así que un
/// <c>ItemsControl</c>/<c>ItemTemplate</c>/<c>CellTemplate</c> nunca realiza sus filas y un
/// literal ahí adentro pasaría inadvertido (Ruling B-16). Este test sí ve ese contenido porque
/// nunca necesita montar nada.
///
/// El invariante NO es "el archivo no contiene la palabra StockApp": los namespaces C# como
/// <c>StockApp.Presentation</c>/<c>StockApp.Application</c> siguen existiendo a propósito (son
/// plomería invisible, ver decisión del rename) y aparecen en <c>xmlns</c>, <c>x:Class</c>,
/// <c>clr-namespace</c> y comentarios internos ("StockApp UI Kit"). Todos esos usos van seguidos
/// de un punto (<c>StockApp.Algo</c>) o viven dentro de un comentario XML. El invariante real es:
/// "StockApp" NO debe aparecer, fuera de un comentario, sin ir seguido de un punto — esa es
/// exactamente la forma que tendría un <c>Text="StockApp"</c>/<c>Title="StockApp"</c> reintroducido
/// por error.
/// </summary>
public class BrandingGestionMunicipalTests
{
    private static string DirectorioDeVistas([CallerFilePath] string archivoDeEsteTest = "")
    {
        var dirDeTests = Path.GetDirectoryName(archivoDeEsteTest)!;
        return Path.GetFullPath(Path.Combine(dirDeTests, "..", "..", "src",
            "StockApp.Presentation", "Views"));
    }

    private static IReadOnlyList<string> ArchivosAxamlDeVistas()
    {
        var dir = DirectorioDeVistas();
        Assert.True(Directory.Exists(dir), $"No se encontró el directorio de vistas esperado: {dir}");
        return Directory.GetFiles(dir, "*.axaml", SearchOption.AllDirectories);
    }

    private static readonly Regex ComentarioXml = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    // "StockApp" que NO sea el arranque de un namespace calificado (StockApp.Presentation, etc).
    private static readonly Regex StockAppComoTextoVisible = new(@"StockApp(?!\.\w)", RegexOptions.Compiled);

    [Fact]
    public void Vistas_NoMuestranElNombreViejoStockAppComoTextoVisible()
    {
        var fallos = new List<string>();

        foreach (var archivo in ArchivosAxamlDeVistas())
        {
            var axaml = File.ReadAllText(archivo);
            var sinComentarios = ComentarioXml.Replace(axaml, string.Empty);

            foreach (Match m in StockAppComoTextoVisible.Matches(sinComentarios))
            {
                var inicio = System.Math.Max(0, m.Index - 30);
                var largo = System.Math.Min(60, sinComentarios.Length - inicio);
                var contexto = sinComentarios.Substring(inicio, largo).Replace('\n', ' ').Replace('\r', ' ');
                fallos.Add($"{Path.GetFileName(archivo)}: \"...{contexto}...\"");
            }
        }

        Assert.True(fallos.Count == 0,
            "Volvió a aparecer \"StockApp\" como texto visible (el producto se llama "
            + "\"Gestión Municipal\" en pantalla):\n" + string.Join("\n", fallos));
    }
}
