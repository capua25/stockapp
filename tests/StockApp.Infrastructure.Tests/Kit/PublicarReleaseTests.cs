using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de build/publicar-release.sh (feed de actualizaciones propio, Task 8).
///
/// Mismo armado que <see cref="DeployVpsTests"/>: repo falso con el script real copiado en
/// build/, deploy/.env dummy y releases/win/ con un releases.win.json generado acá. Los stubs de
/// PATH NO mienten: 'ssh' registra la invocación y EJECUTA el comando remoto de verdad contra un
/// servidor falso local ($SERVIDOR_FALSO: reescribe /var/lib/stockapp y VPS_DIR a rutas bajo él y
/// saca 'sudo' y '-o stockapp -g stockapp'); 'scp' copia al staging falso; 'curl' sirve desde la
/// carpeta del feed falsa. Así los guardianes miran el EFECTO (qué quedó en el servidor, en qué
/// orden), no la declaración del comando.
///
/// Todos los valores de host/puerto/dominio son dummy (.invalid): el script no hereda ningún
/// default de un VPS real, ver <see cref="NingunHostNiPuertoCableadoEnElScript"/>.
/// </summary>
public class PublicarReleaseTests
{
    private const string Full04 = "GestionMunicipal-0.4.0-full.nupkg";
    private const string Full05 = "GestionMunicipal-0.5.0-full.nupkg";
    private const string Delta05 = "GestionMunicipal-0.5.0-delta.nupkg";

    private static string RutaScriptReal([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "build", "publicar-release.sh"));
    }

    // ---------- Stubs ----------

    private const string SshFalso = """
        #!/usr/bin/env bash
        {
            printf 'ARGS:'
            printf ' %q' "$@"
            printf '\n'
        } >> "${RASTRO_SSH:-/dev/null}"

        args=("$@")
        cmd="${args[-1]}"

        if [[ "$cmd" == "sudo -n true" && -n "${SUDO_FALLA:-}" ]]; then
            echo "sudo: a password is required (simulado)" >&2
            exit 1
        fi
        if [[ "$cmd" == *"find "* && -n "${RELEVAMIENTO_FALLA:-}" ]]; then
            echo "ssh: connect to host timed out (simulado)" >&2
            exit 255
        fi

        # El comando remoto corre de verdad, contra el servidor falso.
        cmd="${cmd//sudo -n /}"
        cmd="${cmd//sudo /}"
        cmd="${cmd//-o stockapp -g stockapp /}"
        cmd="${cmd//\/var\/lib\/stockapp/$SERVIDOR_FALSO/var/lib/stockapp}"
        cmd="${cmd//$VPS_DIR/$SERVIDOR_FALSO$VPS_DIR}"

        rc=0
        bash -c "$cmd" || rc=$?

        # Paquete que queda truncado al instalarse (disco lleno, corte, etc.).
        if [[ -n "${TRUNCAR_AL_INSTALAR:-}" && "$cmd" == *"install -m 640"* && "$cmd" == *"updates/${TRUNCAR_AL_INSTALAR}"* ]]; then
            truncate -s 1 "$SERVIDOR_FALSO/var/lib/stockapp/updates/${TRUNCAR_AL_INSTALAR}"
        fi
        # Paquete que queda del MISMO tamaño pero con otros bytes.
        if [[ -n "${CORROMPER_AL_INSTALAR:-}" && "$cmd" == *"install -m 640"* && "$cmd" == *"updates/${CORROMPER_AL_INSTALAR}"* ]]; then
            f="$SERVIDOR_FALSO/var/lib/stockapp/updates/${CORROMPER_AL_INSTALAR}"
            tam="$(stat -c %s "$f")"
            head -c "$tam" /dev/zero | tr '\0' '\1' > "$f"
        fi
        exit "$rc"
        """;

    private const string ScpFalso = """
        #!/usr/bin/env bash
        {
            printf 'ARGS:'
            printf ' %q' "$@"
            printf '\n'
        } >> "${RASTRO_SCP:-/dev/null}"

        n="$(wc -l < "$RASTRO_SCP")"
        if [[ -n "${SCP_FALLA_EN:-}" && "$n" -eq "$SCP_FALLA_EN" ]]; then
            echo "scp: connection lost (simulado)" >&2
            exit 1
        fi

        args=("$@")
        origen="${args[-2]}"
        destino="${args[-1]}"
        destino="$SERVIDOR_FALSO${destino#*:}"
        cp "$origen" "$destino"
        """;

    private const string CurlFalso = """
        #!/usr/bin/env bash
        url=""
        cabeceras=0
        for a in "$@"; do
            case "$a" in
                -fsSI) cabeceras=1 ;;
                http*) url="$a" ;;
            esac
        done
        ruta="${url#*://}"
        ruta="${ruta#*/}"
        archivo="$SERVIDOR_FALSO/var/lib/stockapp/$ruta"
        if [[ -n "${CURL_404:-}" || ! -f "$archivo" ]]; then
            echo "curl: (22) The requested URL returned error: 404" >&2
            exit 22
        fi
        if [[ "$cabeceras" -eq 1 ]]; then
            printf 'HTTP/1.1 %s\r\nContent-Length: %s\r\n\r\n' "${CURL_ESTADO:-200 OK}" "${CURL_LARGO:-$(stat -c %s "$archivo")}"
        elif [[ -n "${CURL_FEED_SIN_VERSION:-}" ]]; then
            echo '{"Assets":[]}'
        else
            cat "$archivo"
        fi
        """;

    private static void EscribirEjecutable(string ruta, string contenido)
    {
        File.WriteAllText(ruta, contenido);
        File.SetUnixFileMode(ruta,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    // ---------- Entorno de cada test ----------

    private sealed class Entorno : IDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "publicar-release-" + Guid.NewGuid());
        public string Repo => Path.Combine(_raiz, "repo");
        public string Origen => Path.Combine(Repo, "releases", "win");
        public string Servidor => Path.Combine(_raiz, "servidor");
        public string Testbin => Path.Combine(_raiz, "testbin");
        public string RastroSsh => Path.Combine(_raiz, "rastro-ssh");
        public string RastroScp => Path.Combine(_raiz, "rastro-scp");
        public string CarpetaFeed => Path.Combine(Servidor, "var", "lib", "stockapp", "updates");
        public string StagingFalso => Path.Combine(Servidor, "srv", "operador-deploy", "updates-staging");

        public Entorno(string? fileNameDelta = null, int tamanoFull05 = 200)
        {
            _tamanoFull05 = tamanoFull05;
            Directory.CreateDirectory(Path.Combine(Repo, "build"));
            Directory.CreateDirectory(Path.Combine(Repo, "deploy"));
            Directory.CreateDirectory(Origen);
            Directory.CreateDirectory(Servidor);
            Directory.CreateDirectory(Testbin);

            EscribirEjecutable(Path.Combine(Repo, "build", "publicar-release.sh"), File.ReadAllText(RutaScriptReal()));
            File.WriteAllText(Path.Combine(Repo, "deploy", ".env"), "API_PORT=5080\n");

            EscribirEjecutable(Path.Combine(Testbin, "ssh"), SshFalso);
            EscribirEjecutable(Path.Combine(Testbin, "scp"), ScpFalso);
            EscribirEjecutable(Path.Combine(Testbin, "curl"), CurlFalso);

            File.WriteAllBytes(Path.Combine(Origen, Full04), new byte[100]);
            File.WriteAllBytes(Path.Combine(Origen, Full05), new byte[tamanoFull05]);
            File.WriteAllBytes(Path.Combine(Origen, Delta05), new byte[50]);
            File.WriteAllText(Path.Combine(Origen, "releases.win.json"), JsonDelFeed(fileNameDelta ?? Delta05));
        }

        // SHA256 en mayúsculas como lo escribe Velopack; el script compara sin distinguir caso.
        private static string Sha256(int tamano) => Convert.ToHexString(SHA256.HashData(new byte[tamano]));

        private string JsonDelFeed(string fileNameDelta) =>
            "{\"Assets\":[" +
            $"{{\"PackageId\":\"GestionMunicipal\",\"Version\":\"0.4.0\",\"Type\":\"Full\",\"FileName\":\"{Full04}\",\"SHA1\":\"a\",\"SHA256\":\"{Sha256(100)}\",\"Size\":100}}," +
            $"{{\"PackageId\":\"GestionMunicipal\",\"Version\":\"0.5.0\",\"Type\":\"Full\",\"FileName\":\"{Full05}\",\"SHA1\":\"a\",\"SHA256\":\"{Sha256(_tamanoFull05)}\",\"Size\":200}}," +
            $"{{\"PackageId\":\"GestionMunicipal\",\"Version\":\"0.5.0\",\"Type\":\"Delta\",\"FileName\":\"{fileNameDelta}\",\"SHA1\":\"a\",\"SHA256\":\"{Sha256(50)}\",\"Size\":50}}" +
            "]}";

        private readonly int _tamanoFull05;

        public void PonerEnServidor(string nombre, byte[] contenido)
        {
            Directory.CreateDirectory(CarpetaFeed);
            File.WriteAllBytes(Path.Combine(CarpetaFeed, nombre), contenido);
        }

        public string[] ArchivosEnServidor() =>
            Directory.Exists(CarpetaFeed)
                ? Directory.GetFiles(CarpetaFeed).Select(Path.GetFileName).OfType<string>().OrderBy(x => x).ToArray()
                : [];

        public (int ExitCode, string Stdout, string Stderr, string[] Ssh, string[] Scp) Correr(
            string argumentos, IDictionary<string, string>? env = null, params string[] sin)
        {
            File.WriteAllText(RastroSsh, "");
            File.WriteAllText(RastroScp, "");
            var sb = new StringBuilder();
            sb.Append($"export PATH=\"{Testbin}:$PATH\"\n");
            sb.Append($"export RASTRO_SSH=\"{RastroSsh}\" RASTRO_SCP=\"{RastroScp}\" SERVIDOR_FALSO=\"{Servidor}\"\n");
            sb.Append("export VPS_HOST=\"vps.test.invalid\" VPS_USER=\"operador\" VPS_SSH_PORT=\"2222\" VPS_DIR=\"/srv/operador-deploy\"\n");
            foreach (var s in sin) sb.Append($"unset {s}\n");
            if (env is not null)
                foreach (var (k, v) in env) sb.Append($"export {k}=\"{v}\"\n");
            sb.Append($"bash \"{Repo}/build/publicar-release.sh\" {argumentos}");
            var (exit, stdout, stderr) = EjecutorBash.Ejecutar(sb.ToString());
            return (exit, stdout, stderr, Lineas(RastroSsh), Lineas(RastroScp));
        }

        private static string[] Lineas(string ruta) =>
            File.ReadAllLines(ruta).Select(l => l.Replace("\\ ", " ")).ToArray();

        public void Dispose()
        {
            if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true);
        }
    }

    private static string Diagnostico((int ExitCode, string Stdout, string Stderr, string[] Ssh, string[] Scp) r) =>
        $"exit={r.ExitCode}\nstdout={r.Stdout}\nstderr={r.Stderr}\nssh=\n{string.Join('\n', r.Ssh)}\nscp=\n{string.Join('\n', r.Scp)}";

    // ---------- Camino feliz ----------

    [Fact]
    public void CaminoFeliz_PaquetesInstaladosYVerificadosAntesDelRenameDelFeed()
    {
        using var e = new Entorno();
        var r = e.Correr("0.5.0");

        Assert.True(r.ExitCode == 0, Diagnostico(r));

        var ssh = r.Ssh.ToList();
        var indiceMv = ssh.FindIndex(l => l.Contains("mv -f") && l.Contains("releases.win.json.tmp"));
        Assert.True(indiceMv >= 0, "no hay rename del feed en el rastro:\n" + Diagnostico(r));

        var installsDePaquetes = ssh.Select((l, i) => (l, i))
            .Where(x => x.l.Contains("install -o") && x.l.Contains(".nupkg")).Select(x => x.i).ToList();
        Assert.Equal(3, installsDePaquetes.Count);
        Assert.All(installsDePaquetes, i => Assert.True(i < indiceMv, "un install de paquete va DESPUÉS del rename:\n" + Diagnostico(r)));

        var relevamientos = ssh.Select((l, i) => (l, i)).Where(x => x.l.Contains("find /var/lib/stockapp/updates")).Select(x => x.i).ToList();
        Assert.Equal(2, relevamientos.Count);
        Assert.True(relevamientos[1] > installsDePaquetes.Max() && relevamientos[1] < indiceMv,
            "el re-relevamiento tiene que ir después de instalar y antes del rename:\n" + Diagnostico(r));

        Assert.Equal(new[] { Full04, Delta05, Full05, "releases.win.json" }.OrderBy(x => x), e.ArchivosEnServidor().OrderBy(x => x));
        Assert.Equal(File.ReadAllText(Path.Combine(e.Origen, "releases.win.json")),
            File.ReadAllText(Path.Combine(e.CarpetaFeed, "releases.win.json")));
        Assert.False(Directory.Exists(e.StagingFalso), "el staging remoto tiene que quedar borrado");
    }

    [Fact]
    public void PrimeraPublicacion_CarpetaRemotaInexistente_SubeTodo()
    {
        using var e = new Entorno();
        Assert.False(Directory.Exists(e.CarpetaFeed));

        var r = e.Correr("0.5.0");

        Assert.True(r.ExitCode == 0, Diagnostico(r));
        Assert.Equal(4, e.ArchivosEnServidor().Length);
        Assert.Equal(3, r.Scp.Count(l => l.Contains(".nupkg")));
        Assert.Contains(r.Ssh, l => l.Contains("install -d") && l.Contains("-m 750") && l.Contains("/var/lib/stockapp/updates"));
    }

    [Fact]
    public void YaPublicadoConMismoTamano_SoloSubeLoQueFalta()
    {
        using var e = new Entorno();
        e.PonerEnServidor(Full04, new byte[100]);

        var r = e.Correr("0.5.0");

        Assert.True(r.ExitCode == 0, Diagnostico(r));
        Assert.DoesNotContain(r.Scp, l => l.Contains(Full04));
        Assert.Contains(r.Scp, l => l.Contains(Full05));
        Assert.Contains(r.Scp, l => l.Contains(Delta05));
    }

    [Fact]
    public void RepublicarLaMismaVersion_NoSubeNingunPaquete()
    {
        using var e = new Entorno();
        var primera = e.Correr("0.5.0");
        Assert.True(primera.ExitCode == 0, Diagnostico(primera));

        var segunda = e.Correr("0.5.0");

        Assert.True(segunda.ExitCode == 0, Diagnostico(segunda));
        Assert.Single(segunda.Scp);
        Assert.Contains("releases.win.json", segunda.Scp[0]);
        Assert.DoesNotContain(segunda.Scp, l => l.Contains(".nupkg"));
    }

    // ---------- Validación local: no toca el servidor ----------

    [Fact]
    public void PaqueteLocalFaltante_AbortaSinTocarElServidor()
    {
        using var e = new Entorno();
        File.Delete(Path.Combine(e.Origen, Delta05));

        var r = e.Correr("0.5.0");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains(Delta05, r.Stderr);
        Assert.Empty(r.Ssh);
        Assert.Empty(r.Scp);
    }

    [Fact]
    public void TamanoLocalDistinto_AbortaSinTocarElServidor()
    {
        using var e = new Entorno(tamanoFull05: 199);

        var r = e.Correr("0.5.0");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains(Full05, r.Stderr);
        Assert.Empty(r.Ssh);
        Assert.Empty(r.Scp);
    }

    [Fact]
    public void VersionNoEsLaMayorDelFeed_Aborta()
    {
        using var e = new Entorno();

        var r = e.Correr("0.4.0");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no es la mayor", r.Stderr);
        Assert.Empty(r.Ssh);
        Assert.Empty(r.Scp);
    }

    [Fact]
    public void FileNameInseguro_Aborta()
    {
        using var e = new Entorno(fileNameDelta: "../x.nupkg");

        var r = e.Correr("0.5.0");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("inseguro", r.Stderr);
        Assert.Empty(r.Ssh);
        Assert.Empty(r.Scp);
    }

    // ---------- Configuración obligatoria (ruling R2) ----------

    [Theory]
    [InlineData("VPS_HOST")]
    [InlineData("VPS_SSH_PORT")]
    [InlineData("VPS_USER")]
    public void ConfiguracionDelVpsObligatoria_SinDefaults(string variable)
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0", env: null, sin: variable);

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains(variable, r.Stderr);
        Assert.Empty(r.Ssh);
        Assert.Empty(r.Scp);
    }

    [Fact]
    public void NingunHostNiPuertoCableadoEnElScript()
    {
        var texto = File.ReadAllText(RutaScriptReal());

        // Ni una IPv4 literal ni un default (':-') para host/puerto del VPS: un default subiría el feed
        // al servidor equivocado en silencio.
        Assert.DoesNotMatch(new Regex(@"\b\d{1,3}(\.\d{1,3}){3}\b"), texto.Replace("127.0.0.1", ""));
        Assert.DoesNotMatch(new Regex(@"VPS_HOST:-"), texto);
        Assert.DoesNotMatch(new Regex(@"VPS_SSH_PORT:-"), texto);
    }

    // ---------- Fallas del servidor ----------

    [Fact]
    public void RelevamientoFalla_AbortaSinSubirNada()
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0", new Dictionary<string, string> { ["RELEVAMIENTO_FALLA"] = "1" });

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no se pudo relevar", r.Stderr);
        Assert.Contains("ssh exit 255", r.Stderr);
        Assert.Empty(r.Scp);
        Assert.Empty(e.ArchivosEnServidor());
    }

    [Fact]
    public void SudoSinContrasenaNoDisponible_AbortaSinSubirNada()
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0", new Dictionary<string, string> { ["SUDO_FALLA"] = "1" });

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no se pudo relevar", r.Stderr);
        Assert.Empty(r.Scp);
    }

    [Fact]
    public void VerificacionRemotaFalla_AbortaSinRename_YElFeedVigenteQuedaIntacto()
    {
        using var e = new Entorno();
        const string feedViejo = "{\"Assets\":[],\"Nota\":\"VIEJO\"}";
        e.PonerEnServidor("releases.win.json", Encoding.UTF8.GetBytes(feedViejo));

        var r = e.Correr("0.5.0", new Dictionary<string, string> { ["TRUNCAR_AL_INSTALAR"] = Full05 });

        Assert.NotEqual(0, r.ExitCode);
        Assert.DoesNotContain(r.Ssh, l => l.Contains("mv -f"));
        Assert.Equal(feedViejo, File.ReadAllText(Path.Combine(e.CarpetaFeed, "releases.win.json")));
        Assert.Contains(Full05, r.Stderr);
    }

    [Fact]
    public void ScpFalla_NoTocaElFeedYLimpiaElStaging()
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0", new Dictionary<string, string> { ["SCP_FALLA_EN"] = "2" });

        Assert.NotEqual(0, r.ExitCode);
        Assert.DoesNotContain(r.Ssh, l => l.Contains("mv -f"));
        Assert.False(File.Exists(Path.Combine(e.CarpetaFeed, "releases.win.json")));
        Assert.False(Directory.Exists(e.StagingFalso), "el staging remoto tiene que borrarse también en falla");
    }

    [Fact]
    public void VerificacionFinalCurlFalla_ExitDistintoDeCero()
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0", new Dictionary<string, string> { ["CURL_404"] = "1" });

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("verificación remota", r.Stderr);
    }

    // ---------- Contenido (sha256), no solo tamaño ----------

    [Fact]
    public void MismoNombreMismoTamanoDistintoContenido_AbortaSinPublicarNiPisar()
    {
        using var e = new Entorno();
        const string feedViejo = "{\"Assets\":[],\"Nota\":\"VIEJO\"}";
        e.PonerEnServidor("releases.win.json", Encoding.UTF8.GetBytes(feedViejo));
        var otroContenido = Enumerable.Repeat((byte)1, 200).ToArray(); // mismo tamaño que Full05, otros bytes
        e.PonerEnServidor(Full05, otroContenido);

        var r = e.Correr("0.5.0");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("contenido distinto", r.Stderr);
        Assert.Contains(Full05, r.Stderr);
        Assert.Empty(r.Scp);
        Assert.DoesNotContain(r.Ssh, l => l.Contains("mv -f"));
        Assert.DoesNotContain(r.Ssh, l => l.Contains("install -o") && l.Contains(".nupkg"));
        Assert.Equal(otroContenido, File.ReadAllBytes(Path.Combine(e.CarpetaFeed, Full05)));
        Assert.Equal(feedViejo, File.ReadAllText(Path.Combine(e.CarpetaFeed, "releases.win.json")));
    }

    [Fact]
    public void MismoNombreDistintoTamano_TambienAbortaEnVezDePisarEnElLugar()
    {
        using var e = new Entorno();
        var otro = new byte[7];
        e.PonerEnServidor(Delta05, otro);

        var r = e.Correr("0.5.0");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("contenido distinto", r.Stderr);
        Assert.Empty(r.Scp);
        Assert.Equal(otro, File.ReadAllBytes(Path.Combine(e.CarpetaFeed, Delta05)));
    }

    [Fact]
    public void VerificacionDePaso5_ExigeElHash_NoSoloElTamano()
    {
        using var e = new Entorno();

        // El paquete queda del mismo tamaño pero con otros bytes DESPUÉS de instalarse.
        var r = e.Correr("0.5.0", new Dictionary<string, string> { ["CORROMPER_AL_INSTALAR"] = Full05 });

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("sha256", r.Stderr);
        Assert.Contains(Full05, r.Stderr);
        Assert.DoesNotContain(r.Ssh, l => l.Contains("mv -f"));
        Assert.False(File.Exists(Path.Combine(e.CarpetaFeed, "releases.win.json")));
    }

    [Fact]
    public void FileNameVacio_AbortaSinTocarElServidor()
    {
        using var e = new Entorno(fileNameDelta: "");

        var r = e.Correr("0.5.0");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("FileName vacío", r.Stderr);
        Assert.Empty(r.Ssh);
        Assert.Empty(r.Scp);
    }

    // ---------- Verificación final por HTTP (paso 7) ----------

    [Fact]
    public void Paso7_ElFeedServidoNoTieneLaVersion_Falla()
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0", new Dictionary<string, string> { ["CURL_FEED_SIN_VERSION"] = "1" });

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no contiene la versión 0.5.0", r.Stderr);
    }

    [Fact]
    public void Paso7_ElFullNoResponde200_Falla()
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0", new Dictionary<string, string> { ["CURL_ESTADO"] = "206 Partial Content" });

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no respondió 200", r.Stderr);
    }

    [Fact]
    public void Paso7_ContentLengthDistinto_Falla()
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0", new Dictionary<string, string> { ["CURL_LARGO"] = "199" });

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("Content-Length", r.Stderr);
    }

    [Fact]
    public void ApiSchemeHttp_AvisaQueSeOmiteElCurlDelOperador()
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0");

        Assert.True(r.ExitCode == 0, Diagnostico(r));
        Assert.Contains("se omite la verificación desde la máquina del operador", r.Stdout);
    }

    // ---------- No retroceder el feed ----------

    private static void PonerFeedServido(Entorno e, string version) =>
        e.PonerEnServidor("releases.win.json", Encoding.UTF8.GetBytes(
            $"{{\"Assets\":[{{\"PackageId\":\"GestionMunicipal\",\"Version\":\"{version}\",\"Type\":\"Full\",\"FileName\":\"GestionMunicipal-{version}-full.nupkg\",\"SHA1\":\"a\",\"SHA256\":\"b\",\"Size\":1}}]}}"));

    [Fact]
    public void VersionMenorQueLaServida_AbortaSinSubirNada()
    {
        using var e = new Entorno();
        PonerFeedServido(e, "0.6.0");
        var antes = File.ReadAllText(Path.Combine(e.CarpetaFeed, "releases.win.json"));

        var r = e.Correr("0.5.0");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("MENOR", r.Stderr);
        Assert.Contains("0.6.0", r.Stderr);
        Assert.Empty(r.Scp);
        Assert.DoesNotContain(r.Ssh, l => l.Contains("mv -f"));
        Assert.Equal(antes, File.ReadAllText(Path.Combine(e.CarpetaFeed, "releases.win.json")));
    }

    [Fact]
    public void VersionMenorQueLaServida_ConForzar_Publica()
    {
        using var e = new Entorno();
        PonerFeedServido(e, "0.6.0");

        var r = e.Correr("0.5.0 --forzar");

        Assert.True(r.ExitCode == 0, Diagnostico(r));
        Assert.Contains("--forzar", r.Stderr);
        Assert.Equal(File.ReadAllText(Path.Combine(e.Origen, "releases.win.json")),
            File.ReadAllText(Path.Combine(e.CarpetaFeed, "releases.win.json")));
    }

    [Fact]
    public void FeedServidoIlegible_AbortaSalvoForzar()
    {
        using var e = new Entorno();
        e.PonerEnServidor("releases.win.json", Encoding.UTF8.GetBytes("esto no es json"));

        var sin = e.Correr("0.5.0");
        Assert.NotEqual(0, sin.ExitCode);
        Assert.Contains("no es un feed válido", sin.Stderr);
        Assert.Empty(sin.Scp);

        var con = e.Correr("0.5.0 --forzar");
        Assert.True(con.ExitCode == 0, Diagnostico(con));
    }

    [Fact]
    public void MismaVersionQueLaServida_NoSeConsideraRetroceso()
    {
        using var e = new Entorno();
        PonerFeedServido(e, "0.5.0");

        var r = e.Correr("0.5.0");

        Assert.True(r.ExitCode == 0, Diagnostico(r));
    }

    [Fact]
    public void ApiSchemeHttps_LaVerificacionRemotaUsaResolve()
    {
        using var e = new Entorno();

        var r = e.Correr("0.5.0", new Dictionary<string, string>
        {
            ["API_SCHEME"] = "https",
            ["API_HOSTNAME"] = "api.test.invalid",
        });

        Assert.True(r.ExitCode == 0, Diagnostico(r));
        var curlRemoto = r.Ssh.Where(l => l.Contains("curl ")).ToList();
        Assert.NotEmpty(curlRemoto);
        Assert.All(curlRemoto, l => Assert.Contains("--resolve api.test.invalid:5080:127.0.0.1", l));
        Assert.All(curlRemoto, l => Assert.DoesNotContain(" -k", l));
        Assert.All(curlRemoto, l => Assert.Contains("https://api.test.invalid:5080/updates/", l));
    }
}
