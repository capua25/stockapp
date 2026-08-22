using System.Text.RegularExpressions;
using Xunit;

namespace StockApp.Api.Tests;

/// <summary>
/// Guardián de REPOSITORIO (bugfix/backups-endpoint-tests-flaky, cierre de los 5 gemelos):
/// impide que CUALQUIER proyecto de test bajo tests/ reintroduzca un bucle de espera (while) que
/// mida un deadline con reloj de PARED (DateTime.UtcNow/DateTime.Now, CLOCK_REALTIME) en vez de
/// reloj MONOTÓNICO (Stopwatch -- ver EsperaMonotonica en tests/_Compartido). El reloj de pared
/// puede saltar hacia adelante (reajuste NTP) o hacia atrás (pausa de host/VM, step NTP
/// negativo) -- un timeout construido sobre él puede dispararse sin que haya transcurrido tiempo
/// real, o no dispararse pese a haber pasado tiempo real de sobra. Confirmado en este repo dos
/// veces: un timeout de 10s "agotado" con sólo 1,22s reales transcurridos (BackupsEndpointTests,
/// fix 611212a) y un reloj monotónico de PRODUCCIÓN que avanzaba a saltos de 3-19ms por usar
/// Environment.TickCount64 (CLOCK_MONOTONIC_COARSE) en vez de Stopwatch.GetTimestamp()
/// (CLOCK_MONOTONIC sin COARSE) -- ver RelojMonotonicoTests.
///
/// Convive con el guardián puntual <c>EsperaSinRelojDeParedGuardTests</c> (que sigue vivo,
/// acotado a BackupsEndpointTests.cs) -- éste cubre el RESTO del árbol tests/, con
/// descubrimiento DINÁMICO de archivos (Directory.EnumerateFiles), no una lista hardcodeada:
/// cualquier .cs nuevo bajo tests/ queda cubierto automáticamente, sin tocar este archivo.
///
/// Sobre el antipatrón ya conocido en este repo (una alternación con RegexOptions.Singleline
/// aplicada al contenido completo del archivo, donde un ".*" ligado a un comentario /// se vuelve
/// voraz, se come el archivo entero y el guardián pasa SIEMPRE): este guardián NO arma ningún
/// regex sobre el archivo completo. Procesa LÍNEA por línea, y cada regex que usa está acotado a
/// una sola línea (un <c>string[]</c> de <see cref="File.ReadAllLines"/>, nunca ve un '\n'), así
/// que RegexOptions.Singleline no aplicaría ni haría falta -- no está en la lista de opciones de
/// ninguno de los tres Regex de abajo. Antes de buscar el patrón peligroso en cada línea, pela:
/// (1) el contenido de literales de string (incluye <c>@"..."</c> verbatim), (2) comentarios de
/// bloque <c>/* */</c> (con estado propio entre líneas para bloques multilínea -- no hay ninguno
/// hoy en tests/, pero el algoritmo no asume que no vaya a haberlo) y (3) comentarios de línea
/// <c>//</c>/<c>///</c>. Así ni la prosa que describe el bug (como este mismo comentario, o el
/// de EsperaMonotonica) ni los literales de <c>EsperaSinRelojDeParedGuardTests.cs</c> (que
/// contienen <c>"while (DateTime.UtcNow"</c> como STRING de su lista de patrones prohibidos, no
/// como código real) generan un falso positivo. Verificado por mutación en los dos sentidos: (a)
/// reintroducir el patrón viejo en cada uno de los 4 sitios arreglados pone este guardián en
/// rojo, y (b) un archivo .cs nuevo con el patrón, agregado sólo para la prueba, también lo pone
/// en rojo sin tocar este archivo (ver reporte de la task).
///
/// Límite conocido y documentado (no un caso a exceptuar, una limitación del heurístico): sólo
/// detecta la forma "while (...) que en la MISMA línea física menciona DateTime.UtcNow/.Now" --
/// es la forma real de los 5 casos encontrados en este repo (los 4 gemelos + el ya arreglado de
/// BackupsEndpointTests). Una condición partida en varias líneas físicas no se detecta. Si
/// aparece un caso legítimo de "while" + DateTime.UtcNow que no sea un bucle de espera con
/// deadline (ninguno existe hoy -- verificado: cero coincidencias en código real antes de este
/// commit), se documenta acá mismo como excepción explícita, con la razón, en vez de debilitar el
/// patrón.
/// </summary>
public class RelojDeParedEnBuclesDeEsperaGuardTests
{
    private static readonly Regex PatronWhileConParentesis = new(@"\bwhile\b\s*\(", RegexOptions.Compiled);

    private static readonly Regex PatronLiteralDeString = new(
        @"@""(?:[^""]|"""")*""|""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);

    [Fact]
    public void NingunTestDelRepo_SondeaUnDeadlineConRelojDePared()
    {
        var raiz = ResolverRaizDelRepo();
        var directorioTests = Path.Combine(raiz, "tests");

        var archivos = Directory.EnumerateFiles(directorioTests, "*.cs", SearchOption.AllDirectories)
            .Where(EsArchivoFuenteReal)
            .ToList();

        // Guarda contra un descubrimiento roto que "pasaría" por no encontrar nada que revisar
        // (falso verde silencioso) -- al momento de escribir este guardián hay > 150 archivos.
        Assert.True(archivos.Count > 50,
            $"Sólo se encontraron {archivos.Count} archivos .cs bajo tests/ -- el descubrimiento " +
            "dinámico probablemente está mal apuntado (revisar ResolverRaizDelRepo/directorioTests).");

        var ofensores = new List<string>();
        foreach (var archivo in archivos)
        {
            foreach (var numeroDeLinea in LineasConRelojDeParedEnWhile(archivo))
                ofensores.Add($"{Path.GetRelativePath(raiz, archivo)}:{numeroDeLinea}");
        }

        Assert.True(ofensores.Count == 0,
            "Bucle(s) de espera con reloj de PARED (DateTime.UtcNow/.Now) en vez de MONOTÓNICO " +
            "(Stopwatch / EsperaMonotonica.HastaAsync): " + string.Join("; ", ofensores));
    }

    /// <summary>obj/ y bin/ pueden traer copias generadas (AssemblyInfo, recursos empaquetados)
    /// que no son código fuente versionado -- se excluyen por segmento de ruta, no por nombre de
    /// archivo, para no depender de una convención de nombres.</summary>
    private static bool EsArchivoFuenteReal(string ruta)
    {
        var segmentos = ruta.Split(Path.DirectorySeparatorChar);
        return !segmentos.Contains("obj") && !segmentos.Contains("bin");
    }

    private static IEnumerable<int> LineasConRelojDeParedEnWhile(string ruta)
    {
        var lineas = File.ReadAllLines(ruta);
        var dentroDeBloqueComentario = false;
        var ofensoras = new List<int>();

        for (var i = 0; i < lineas.Length; i++)
        {
            var activa = PelarComentariosYStrings(lineas[i], ref dentroDeBloqueComentario);

            if (PatronWhileConParentesis.IsMatch(activa)
                && (activa.Contains("DateTime.UtcNow") || activa.Contains("DateTime.Now")))
            {
                ofensoras.Add(i + 1);
            }
        }

        return ofensoras;
    }

    /// <summary>
    /// Deja sólo el código "activo" de una línea: sin literales de string, sin comentarios de
    /// bloque (con estado cruzando líneas vía <paramref name="dentroDeBloqueComentario"/>) y sin
    /// comentario de línea (// o ///, que empieza igual que //). Cada operación mira UNA línea a
    /// la vez -- ninguna de ellas ve ni podría ver un '\n', así que ninguna necesita
    /// RegexOptions.Singleline (el antipatrón documentado arriba requiere justamente que el regex
    /// vea el archivo completo de una vez).
    /// </summary>
    private static string PelarComentariosYStrings(string linea, ref bool dentroDeBloqueComentario)
    {
        if (dentroDeBloqueComentario)
        {
            var finDelBloque = linea.IndexOf("*/", StringComparison.Ordinal);
            if (finDelBloque < 0)
                return string.Empty; // toda la línea sigue dentro del comentario de bloque

            linea = linea[(finDelBloque + 2)..];
            dentroDeBloqueComentario = false;
        }

        // Literales de string primero: así un "/*" o un "//" dentro de un literal (ej. un
        // mensaje de error) no se confunde con el inicio de un comentario real.
        linea = PatronLiteralDeString.Replace(linea, "\"\"");

        // Comentario(s) de bloque que abren (y opcionalmente cierran) en esta misma línea.
        while (true)
        {
            var inicio = linea.IndexOf("/*", StringComparison.Ordinal);
            if (inicio < 0)
                break;

            var fin = linea.IndexOf("*/", inicio + 2, StringComparison.Ordinal);
            if (fin < 0)
            {
                linea = linea[..inicio];
                dentroDeBloqueComentario = true;
                break;
            }

            linea = linea[..inicio] + linea[(fin + 2)..];
        }

        var indiceComentarioDeLinea = linea.IndexOf("//", StringComparison.Ordinal);
        if (indiceComentarioDeLinea >= 0)
            linea = linea[..indiceComentarioDeLinea];

        return linea;
    }

    /// <summary>Sube desde el directorio de build (bin/Debug/netX.0/...) hasta encontrar
    /// StockApp.sln en el árbol del repo -- mismo patrón que
    /// EsperaSinRelojDeParedGuardTests.ResolverRutaBackupsEndpointTests, generalizado a la raíz
    /// del repo en vez de a un único archivo.</summary>
    private static string ResolverRaizDelRepo()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "StockApp.sln")))
            directorio = directorio.Parent;

        if (directorio is null)
        {
            throw new InvalidOperationException(
                "No se encontró StockApp.sln subiendo desde el directorio de build.");
        }

        return directorio.FullName;
    }
}
