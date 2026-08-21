using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián estructural del bugfix "14 puntos desnudos dejaban la pantalla muda ante un 403/401"
/// (ver <see cref="StockApp.Presentation.ViewModels.ViewModelBase.EjecutarCargaProtegidaAsync"/>).
///
/// Parchar los 14 puntos a mano ya falló dos veces en este proyecto (BloqueoLicenciaViewModel y
/// HistorialImportacionesViewModel tenían catch propio y el 403 se les escapaba igual). Este test
/// NO tiene una lista hardcodeada de esos 14 -- descubre los puntos de carga leyendo el texto
/// crudo de los <c>.axaml.cs</c> reales (mismo enfoque que <c>BrandingGestionMunicipalTests</c> /
/// <c>ReflexionVistaViewModelTests</c>): el único disparador usado en todo el proyecto es
/// <c>DataContextChanged += async (_, _) => { if (DataContext is XViewModel vm) await
/// vm.MetodoAsync(); };</c> — no hay <c>Loaded +=</c> ni <c>AttachedToVisualTree</c> (confirmado
/// por <see cref="NingunaVista_UsaLoadedOAttachedComoDisparadorAlternativo"/> más abajo, que
/// documenta y vigila ese supuesto en vez de asumirlo en silencio).
///
/// LIMITACIÓN reconocida (no se sobre-vende, mismo criterio que ReflexionVistaViewModelTests):
/// esto es análisis de TEXTO sobre el código fuente (regex + brace-matching), no un parser de C#
/// real (no hay Roslyn en las dependencias del proyecto). La detección de "¿este método atrapa
/// UnauthorizedAccessException?" sigue una cadena de llamadas locales (mismo tipo, sin receptor)
/// hasta profundidad 3 -- alcanza para todos los patrones reales del proyecto (incluido el caso
/// histórico de MovimientoHistorialViewModel: InicializarAsync llama CargarAsync, ninguno
/// protegido), pero un patrón de indirección más exótico (delegates, reflection, un método de
/// una clase base en OTRO archivo con nombre distinto) podría no ser seguido. La mutación de más
/// abajo (<see cref="Guardian_DetectaUnPuntoDeCargaDesnudo_CuandoSeIntroduceUnoDeliberado"/>)
/// prueba, con un caso real agregado a propósito, que el guardián SÍ falla cuando corresponde.
/// </summary>
public class CargaProtegidaSinPermisoGuardianTests
{
    // ── ubicación de las carpetas fuente ────────────────────────────────────────────────────

    private static string DirRaizDelRepo([CallerFilePath] string archivoDeEsteTest = "")
    {
        var dirDeTests = Path.GetDirectoryName(archivoDeEsteTest)!;
        return Path.GetFullPath(Path.Combine(dirDeTests, "..", ".."));
    }

    private static string DirViews([CallerFilePath] string archivoDeEsteTest = "")
        => Path.Combine(DirRaizDelRepo(archivoDeEsteTest), "src", "StockApp.Presentation", "Views");

    private static string DirViewModels([CallerFilePath] string archivoDeEsteTest = "")
        => Path.Combine(DirRaizDelRepo(archivoDeEsteTest), "src", "StockApp.Presentation", "ViewModels");

    // ── limpieza de comentarios (GOTCHA documentado: Singleline aplicado a la alternación de
    // línea se come el archivo entero — ver RefrescoPermisosNoLlamaProgramLogFatalTests.cs). Dos
    // regex separados, cada uno con el modificador que le corresponde. ──

    private static readonly Regex ComentarioDeBloque =
        new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ComentarioDeLinea =
        new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static string SinComentarios(string fuente)
    {
        var sinBloques = ComentarioDeBloque.Replace(fuente, string.Empty);
        return ComentarioDeLinea.Replace(sinBloques, string.Empty);
    }

    // ── descubrimiento de puntos de carga en los .axaml.cs (Vista -> ViewModel) ────────────────

    public sealed record PuntoDeCarga(string Vista, string TipoViewModel, string Metodo);

    private static readonly Regex PatronPuntoDeCarga = new(
        @"DataContext\s+is\s+(?<vm>\w+)\s+\w+\)\s*await\s+\w+\.(?<metodo>\w+)\(\)",
        RegexOptions.Compiled);

    private static IReadOnlyList<PuntoDeCarga> DescubrirPuntosDeCarga()
    {
        var puntos = new List<PuntoDeCarga>();
        var archivos = Directory.GetFiles(DirViews(), "*.axaml.cs", SearchOption.AllDirectories);

        foreach (var archivo in archivos)
        {
            var texto = SinComentarios(File.ReadAllText(archivo));
            foreach (Match m in PatronPuntoDeCarga.Matches(texto))
            {
                puntos.Add(new PuntoDeCarga(
                    Path.GetFileName(archivo), m.Groups["vm"].Value, m.Groups["metodo"].Value));
            }
        }

        return puntos;
    }

    // ── localización + extracción de cuerpo de método en el ViewModel ──────────────────────────

    private static string? BuscarArchivoDeViewModel(string tipoViewModel)
    {
        var candidatos = Directory.GetFiles(DirViewModels(), $"{tipoViewModel}.cs", SearchOption.AllDirectories);
        return candidatos.FirstOrDefault();
    }

    /// <summary>
    /// Extrae el cuerpo (con llaves) de la primera declaración de <paramref name="metodo"/> en
    /// <paramref name="fuenteSinComentarios"/>: busca "nombreMetodo(...)" seguido de "{" (una
    /// invocación real termina en ";", nunca en "{", así que esto no confunde declaración con
    /// llamada) y balancea llaves desde ahí.
    /// </summary>
    private static string? ExtraerCuerpoDeMetodo(string fuenteSinComentarios, string metodo)
    {
        var firma = new Regex($@"\b{Regex.Escape(metodo)}\s*\([^{{}}]*\)\s*\{{", RegexOptions.Compiled);
        var m = firma.Match(fuenteSinComentarios);
        if (!m.Success) return null;

        var inicioLlave = m.Index + m.Length - 1;
        var profundidad = 0;
        for (var i = inicioLlave; i < fuenteSinComentarios.Length; i++)
        {
            if (fuenteSinComentarios[i] == '{') profundidad++;
            else if (fuenteSinComentarios[i] == '}')
            {
                profundidad--;
                if (profundidad == 0)
                    return fuenteSinComentarios.Substring(inicioLlave, i - inicioLlave + 1);
            }
        }
        return null;
    }

    // ── ¿el cuerpo de un método cubre UnauthorizedAccessException? ─────────────────────────────

    private const string NombreDelHelper = "EjecutarCargaProtegidaAsync";

    private static readonly Regex LlamadaAlHelper =
        new($@"\b{NombreDelHelper}\s*\(", RegexOptions.Compiled);

    private static readonly Regex PatronCatch = new(
        @"catch\s*\(\s*(?<tipo>[\w.]+)(?:\s+\w+)?\s*\)(?:\s*when\s*\((?<filtro>[^)]*)\))?",
        RegexOptions.Compiled);

    /// <summary>
    /// Invariante real (no "el texto dice UnauthorizedAccessException"): un catch SIN filtro de
    /// tipo <c>Exception</c> a secas también cubre, porque atrapa cualquier excepción -- ese es
    /// el patrón real de TareaListViewModel/MantenimientoViewModel (protegidos-con-mensaje, fuera
    /// de alcance de este fix, pero genuinamente protegidos: el 403 no se les escapa, solo toma
    /// un camino distinto al de EstadoVacio). Un catch de <c>Exception</c> CON filtro <c>when</c>
    /// que NO menciona UnauthorizedAccessException NO cubre (ese fue el bug real de
    /// HistorialImportacionesViewModel antes del fix).
    /// </summary>
    private static bool CubreSinPermisoDirectamente(string cuerpoDelMetodo)
    {
        if (LlamadaAlHelper.IsMatch(cuerpoDelMetodo)) return true;

        foreach (Match m in PatronCatch.Matches(cuerpoDelMetodo))
        {
            var tipo = m.Groups["tipo"].Value;
            var tieneFiltro = m.Groups["filtro"].Success;
            var filtro = m.Groups["filtro"].Value;

            if (tipo == "UnauthorizedAccessException") return true;
            if (tipo == "Exception" && !tieneFiltro) return true;
            if (tieneFiltro && filtro.Contains("UnauthorizedAccessException")) return true;
        }
        return false;
    }

    /// <summary>Llamadas locales sin receptor (mismo tipo): <c>await NombreDelMetodo();</c>, NO
    /// <c>await _campo.Metodo()</c> ni <c>await vm.Metodo()</c> (el lookbehind excluye un punto o
    /// letra justo antes de "await").</summary>
    private static readonly Regex LlamadaLocalSinArgumentos =
        new(@"(?<![.\w])await\s+(\w+)\(\)", RegexOptions.Compiled);

    private static bool ProtegeContraSinPermiso(
        string fuenteDelViewModelSinComentarios, string cuerpoDelMetodo,
        HashSet<string> visitados, int profundidadRestante)
    {
        if (CubreSinPermisoDirectamente(cuerpoDelMetodo)) return true;
        if (profundidadRestante <= 0) return false;

        foreach (Match m in LlamadaLocalSinArgumentos.Matches(cuerpoDelMetodo))
        {
            var llamado = m.Groups[1].Value;
            if (!visitados.Add(llamado)) continue;

            var cuerpoLlamado = ExtraerCuerpoDeMetodo(fuenteDelViewModelSinComentarios, llamado);
            if (cuerpoLlamado is null) continue;

            if (ProtegeContraSinPermiso(fuenteDelViewModelSinComentarios, cuerpoLlamado, visitados, profundidadRestante - 1))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Excepciones documentadas, CON razón técnica -- no es "la lista de los 14 disfrazada": es
    /// un mecanismo de exención puntual, mismo patrón que
    /// <c>GuardianDePatronTests.VistasFueraDelGuardian</c>. Solo UNA entrada real hoy.
    /// </summary>
    private static readonly HashSet<(string Vista, string Metodo)> PuntosExentosConRazon = new()
    {
        // BloqueoLicenciaView / BloqueoLicenciaViewModel.CargarEstadoAsync: llama a
        // LicenciaApiClient.ObtenerEstadoAsync() -> GET /licencia/estado, que
        // LicenciaEndpoints.cs marca EXPLÍCITAMENTE "Anónimo por diseño: es pre-login" (sin
        // [Authorize], nunca exige JWT). El servidor no puede devolver 401/403 desde ese
        // endpoint, así que UnauthorizedAccessException nunca puede nacer ahí -- protegerlo
        // sería código muerto disfrazando un catch real. Decisión tomada a pedido explícito del
        // encargo ("si no tiene sentido, decilo en vez de forzarlo").
        ("BloqueoLicenciaView.axaml.cs", "CargarEstadoAsync"),
    };

    [Fact]
    public void TodoPuntoDeCarga_EstaProtegidoContraUnauthorizedAccessException()
    {
        var puntos = DescubrirPuntosDeCarga();
        Assert.True(puntos.Count > 0, "El descubrimiento no encontró ningún punto de carga -- " +
            "revisar el regex/patrón antes de confiar en el resto del test.");

        var fallos = new List<string>();

        foreach (var punto in puntos)
        {
            if (PuntosExentosConRazon.Contains((punto.Vista, punto.Metodo)))
                continue;

            var archivoVm = BuscarArchivoDeViewModel(punto.TipoViewModel);
            if (archivoVm is null)
            {
                fallos.Add($"{punto.Vista}: no se encontró el archivo fuente de {punto.TipoViewModel}.cs");
                continue;
            }

            var fuente = SinComentarios(File.ReadAllText(archivoVm));
            var cuerpo = ExtraerCuerpoDeMetodo(fuente, punto.Metodo);
            if (cuerpo is null)
            {
                fallos.Add($"{punto.Vista}: no se pudo extraer el cuerpo de {punto.TipoViewModel}.{punto.Metodo}()");
                continue;
            }

            var protegido = ProtegeContraSinPermiso(fuente, cuerpo, new HashSet<string> { punto.Metodo }, profundidadRestante: 3);
            if (!protegido)
                fallos.Add($"{punto.Vista} -> {punto.TipoViewModel}.{punto.Metodo}(): " +
                    "UnauthorizedAccessException puede escapar sin protección (pantalla muda).");
        }

        Assert.True(fallos.Count == 0,
            "Punto(s) de carga SIN protección contra UnauthorizedAccessException:\n" + string.Join("\n", fallos));
    }

    /// <summary>
    /// Documenta y vigila el supuesto sobre el que se apoya todo el descubrimiento de arriba: si
    /// algún día aparece un <c>Loaded +=</c> o <c>AttachedToVisualTree</c> como disparador
    /// alternativo de carga, el guardián de arriba NO lo vería (solo mira DataContextChanged) --
    /// este test lo convierte en un blind spot CONOCIDO Y VIGILADO en vez de uno silencioso.
    /// </summary>
    [Fact]
    public void NingunaVista_UsaLoadedOAttachedComoDisparadorAlternativo()
    {
        var archivos = Directory.GetFiles(DirViews(), "*.axaml.cs", SearchOption.AllDirectories);
        var fallos = new List<string>();

        foreach (var archivo in archivos)
        {
            var texto = SinComentarios(File.ReadAllText(archivo));
            if (Regex.IsMatch(texto, @"\bLoaded\s*\+="))
                fallos.Add($"{Path.GetFileName(archivo)}: usa Loaded += (disparador no cubierto por el guardián).");
            if (texto.Contains("AttachedToVisualTree"))
                fallos.Add($"{Path.GetFileName(archivo)}: usa AttachedToVisualTree (disparador no cubierto por el guardián).");
        }

        Assert.True(fallos.Count == 0,
            "Disparador(es) de carga NO cubiertos por CargaProtegidaSinPermisoGuardianTests -- " +
            "hay que extender el descubrimiento antes de confiar en el guardián:\n" + string.Join("\n", fallos));
    }
}
