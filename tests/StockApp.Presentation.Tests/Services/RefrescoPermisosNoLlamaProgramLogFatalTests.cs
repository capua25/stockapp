using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Guardián de texto crudo (mismo enfoque que <c>BrandingGestionMunicipalTests</c> /
/// <c>EtiquetadoDeCamposTests</c> / <c>ReflexionVistaViewModelTests</c> en
/// StockApp.Presentation.UiTests): antes del fix del 2026-08-20, <c>RefrescoPermisos</c> llamaba
/// directo a <c>Program.LogFatal</c>, así que cada corrida de <c>dotnet test</c> escribía en el
/// crash.log REAL del usuario (llegó a 5175 entradas / 4MB). El fix reemplazó esa llamada por
/// <see cref="StockApp.Presentation.Services.IRegistroFallos"/>, inyectable y reemplazable desde
/// el bootstrap de tests. Este test evita que alguien reintroduzca el llamado directo.
/// </summary>
public class RefrescoPermisosNoLlamaProgramLogFatalTests
{
    private static string LeerFuente([CallerFilePath] string archivoDeEsteTest = "")
    {
        var dirDeTests = Path.GetDirectoryName(archivoDeEsteTest)!;
        var ruta = Path.GetFullPath(Path.Combine(dirDeTests, "..", "..", "..", "src",
            "StockApp.Presentation", "Services", "RefrescoPermisos.cs"));
        Assert.True(File.Exists(ruta), $"No se encontró el archivo fuente esperado en: {ruta}");
        return File.ReadAllText(ruta);
    }

    // OJO: "//.*$" con Multiline (SIN Singleline) alcanza tanto "//" como "///" porque ambos
    // arrancan igual, y el "." no cruza saltos de línea — si se le agrega Singleline acá, la
    // alternativa de línea se vuelve voraz y se come el resto del archivo completo (bug real
    // detectado en la mutación de este mismo guardián: con Singleline, el comentario ///
    // <summary> del encabezado tapaba código real más abajo y el test daba falso verde).
    private static readonly Regex ComentarioDeBloque = new(@"/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ComentarioDeLinea = new(@"//.*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void RefrescoPermisos_NoLlamaDirectoAProgramLogFatal()
    {
        var fuente = LeerFuente();
        var sinComentarios = ComentarioDeBloque.Replace(fuente, string.Empty);
        sinComentarios = ComentarioDeLinea.Replace(sinComentarios, string.Empty);

        Assert.False(sinComentarios.Contains("Program.LogFatal("),
            "RefrescoPermisos volvió a llamar directo a Program.LogFatal: eso hace que cada "
            + "corrida de `dotnet test` escriba en el crash.log REAL del usuario (llegó a 5175 "
            + "entradas / 4MB antes del fix del 2026-08-20). Usá IRegistroFallos (inyectable vía "
            + "ConfigurarRegistroFallos) en su lugar, igual que el resto de DispararBestEffortAsync.");
    }
}
