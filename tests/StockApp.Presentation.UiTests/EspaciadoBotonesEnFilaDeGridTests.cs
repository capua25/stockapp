using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián nuevo para el bug real encontrado por el usuario en la app corriendo (2026-08-19):
/// los botones de acción de las filas de <c>DocumentoListView</c>/<c>TareaListView</c> estaban
/// PEGADOS, sin ningún espacio entre ellos. Causa raíz confirmada contra el commit fundacional
/// 88accb3: el contenedor de esos botones es un <c>Grid</c> con columnas <c>Auto</c>, no un
/// <c>StackPanel</c> — <c>Spacing</c> es propiedad de <c>StackPanel</c>, un <c>Grid</c> no la
/// tiene, y ningún <c>Button</c> llevaba <c>Margin</c>. 3358 tests en verde no lo detectaron.
///
/// <see cref="GuardianDePatronTests"/> no cubre esto: por diseño (Ruling B-16) no mira dentro de
/// <c>ItemTemplate</c>/<c>CellTemplate</c> — monta las vistas sin datos, así que un
/// <c>ItemsControl</c> nunca realiza sus filas. Este test, en cambio, analiza el <c>.axaml</c>
/// crudo como texto (mismo enfoque que <see cref="ReflexionVistaViewModelTests"/>), así que SÍ ve
/// el contenido de los templates sin necesitar montar datos.
///
/// El invariante: dentro de cualquier <c>Grid</c> "hoja" (sin otro <c>Grid</c> anidado adentro,
/// para no confundir una fila de botones con un layout más grande que la contiene), si dos
/// <c>Button</c> ocupan columnas <c>Grid.Column</c> CONSECUTIVAS (ej. 1 y 2), el segundo debe
/// tener <c>Margin</c> propio. Se exige "consecutivas" y no "cualquier par de botones en el mismo
/// Grid" a propósito: hay Grids legítimos con 2 botones separados por una columna <c>*</c> o de
/// texto (ej. LoginView, PagosGastoView, ControlPoaView) donde no hay nada pegado — un chequeo
/// más laxo daría falsos positivos ahí. Verificado con un script de calibración sobre las 25
/// vistas del ensamblado que hoy tienen 2+ botones: con la regla de columnas consecutivas, el
/// único hallazgo es exactamente los 4 sitios de este bug (2 filas en DocumentoListView, 2 filas
/// en TareaListView) — cero falsos positivos en el resto del árbol.
/// </summary>
public class EspaciadoBotonesEnFilaDeGridTests
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

    private static readonly Regex TagGrid = new(@"<Grid\b[^>]*?(?<selfclosed>/)?>|</Grid>", RegexOptions.Compiled);

    /// <summary>
    /// Extrae los bloques de texto de cada <c>&lt;Grid ...&gt;...&lt;/Grid&gt;</c> "hoja" (que no
    /// contiene ningún otro <c>&lt;Grid</c> en su interior), manejando anidamiento con una pila de
    /// posiciones de apertura. Un <c>&lt;Grid .../&gt;</c> autocerrado no aporta bloque (no tiene
    /// contenido).
    /// </summary>
    private static IReadOnlyList<string> GridsHoja(string axaml)
    {
        var pilaDeAperturas = new Stack<int>();
        var bloques = new List<(int inicio, int fin)>();

        foreach (Match m in TagGrid.Matches(axaml))
        {
            if (m.Value == "</Grid>")
            {
                if (pilaDeAperturas.Count == 0)
                    continue;
                var inicio = pilaDeAperturas.Pop();
                bloques.Add((inicio, m.Index + m.Length));
            }
            else if (m.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                // Grid autocerrado: sin contenido, no aporta bloque ni afecta el anidamiento.
            }
            else
            {
                pilaDeAperturas.Push(m.Index);
            }
        }

        var hoja = new List<string>();
        foreach (var (inicio, fin) in bloques)
        {
            var texto = axaml.Substring(inicio, fin - inicio);
            var finDeApertura = texto.IndexOf('>') + 1;
            var interior = texto[finDeApertura..];
            if (!interior.Contains("<Grid", StringComparison.Ordinal))
                hoja.Add(texto);
        }
        return hoja;
    }

    private static readonly Regex TagButton = new(@"<Button\b[^>]*?/?>", RegexOptions.Compiled);
    private static readonly Regex AtributoMargin = new(@"\bMargin\s*=", RegexOptions.Compiled);
    private static readonly Regex AtributoGridColumn = new(@"Grid\.Column\s*=\s*""(\d+)""", RegexOptions.Compiled);

    [Fact]
    public void Vistas_BotonesEnColumnasConsecutivasDeGridTienenMargin()
    {
        var fallos = new List<string>();

        foreach (var archivo in ArchivosAxamlDeVistas())
        {
            var axaml = File.ReadAllText(archivo);

            foreach (var grid in GridsHoja(axaml))
            {
                var botones = TagButton.Matches(grid).Cast<Match>().ToList();
                if (botones.Count < 2)
                    continue;

                var columnas = botones
                    .Select(b => AtributoGridColumn.Match(b.Value) is { Success: true } m
                        ? (int?)int.Parse(m.Groups[1].Value)
                        : null)
                    .ToList();

                for (var i = 1; i < botones.Count; i++)
                {
                    if (columnas[i] is null || columnas[i - 1] is null)
                        continue;
                    if (columnas[i] != columnas[i - 1] + 1)
                        continue;

                    if (!AtributoMargin.IsMatch(botones[i].Value))
                    {
                        fallos.Add($"{Path.GetFileName(archivo)}: Button en Grid.Column=\"{columnas[i]}\" "
                            + $"pegado al de Grid.Column=\"{columnas[i - 1]}\" sin Margin propio.");
                    }
                }
            }
        }

        Assert.True(fallos.Count == 0,
            "Boton(es) pegados en una fila de Grid (sin Margin ni gutter que los separe):\n"
            + string.Join("\n", fallos));
    }
}
