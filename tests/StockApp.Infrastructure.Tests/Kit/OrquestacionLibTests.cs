using System.Diagnostics;
using System.Text.RegularExpressions;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/lib/orquestacion.sh, la librería común de los dos orquestadores de la
/// migración (preparar-vps-nuevo.sh y migrar-vps.sh). Cada test hace `source` de la lib en un
/// bash con HOME temporal y bajo `set -euo pipefail`, que es como la cargan los orquestadores:
/// un fallo mal manejado dentro de la lib mata el script de prueba igual que mataría al real.
///
/// El ssh de los tests es un ejecutable falso al frente del PATH (mismo patrón que
/// DeployVpsTests.SshFalso): nada sale de la máquina.
/// </summary>
public class OrquestacionLibTests : IDisposable
{
    private readonly string _dir;

    public OrquestacionLibTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "orq-lib-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Home);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Home => Path.Combine(_dir, "home");
    private string Bin => Path.Combine(_dir, "bin");
    private string Rastro => Path.Combine(_dir, "rastro.txt");
    private string Corridas => Path.Combine(Home, "stockapp-backups-vps", "corridas");
    private string LockFile => Path.Combine(Home, "stockapp-backups-vps", ".migracion.lock");

    private const string Estricto = "set -euo pipefail\n";
    private static string Cargar => $"source '{BashLimpio.Orquestacion}'\n";

    private (int ExitCode, string Stdout, string Stderr) Correr(string script) =>
        BashLimpio.Ejecutar(Estricto + Cargar + script, Home);

    /// <summary>
    /// Corre el script en una sesión SIN terminal de control (setsid): ni /dev/tty existe. Así un
    /// test de confirmación nunca puede quedarse leyendo el teclado de quien corre la suite.
    /// </summary>
    private (int ExitCode, string Stdout, string Stderr) CorrerSinTerminal(string script, string prefijo = "")
    {
        var interno = Path.Combine(_dir, $"interno-{Guid.NewGuid():N}.sh");
        File.WriteAllText(interno, Estricto + Cargar + script);
        return BashLimpio.Ejecutar(prefijo + $"setsid -w bash '{interno}'", Home);
    }

    private static string Diag((int ExitCode, string Stdout, string Stderr) r) =>
        $"exit={r.ExitCode}\nstdout={r.Stdout}\nstderr={r.Stderr}";

    /// <summary>Un directorio de corrida ya creado (ORQ_CORRIDA apuntando a él), sin pasar por la lib.</summary>
    private string Corrida() => $"ORQ_CORRIDA='{Path.Combine(Home, "corrida")}'\nmkdir -p \"$ORQ_CORRIDA\"\n";

    private string ArchivoEnv(string contenido)
    {
        var ruta = Path.Combine(_dir, $"a-{Guid.NewGuid():N}.env");
        File.WriteAllText(ruta, contenido);
        return ruta;
    }

    // ---------- orq_leer_env ----------

    [Fact]
    public void LeerEnv_LeeSinEjecutar()
    {
        var marca = Path.Combine(_dir, "marca");
        var env = ArchivoEnv($"VPS_HOST=a.invalid\nX=$(touch {marca})\n");

        var r = Correr($"orq_leer_env '{env}' V VPS_HOST X AUSENTE\n" +
            "echo \"HOST=$V_VPS_HOST\"\necho \"X=$V_X\"\necho \"AUSENTE=[${V_AUSENTE+definida}]\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Contains("HOST=a.invalid", r.Stdout);
        Assert.Contains($"X=$(touch {marca})", r.Stdout);
        Assert.Contains("AUSENTE=[]", r.Stdout);
        Assert.False(File.Exists(marca), "el .env se ejecutó: la lib hizo source o eval del valor");
    }

    [Fact]
    public void LeerEnv_ClaveAusente_NoUsaLaHeredadaDelEntorno()
    {
        // Un export olvidado en la shell del operador no puede satisfacer el "falta la clave".
        var env = ArchivoEnv("VPS_USER=operador\n");

        var r = Correr("export V_VPS_HOST=produccion.ejemplo\n" +
            $"orq_leer_env '{env}' V VPS_HOST VPS_USER\n" +
            "echo \"HOST=[${V_VPS_HOST+definida}]\"\necho \"USER=$V_VPS_USER\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("HOST=[]\nUSER=operador", r.Stdout);
    }

    [Fact]
    public void LeerEnv_LaUltimaGana_YElValorConservaElIgual()
    {
        var env = ArchivoEnv("TOKEN=viejo\nTOKEN=a=b=c\n");

        var r = Correr($"orq_leer_env '{env}' V TOKEN\necho \"TOKEN=$V_TOKEN\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("TOKEN=a=b=c", r.Stdout);
    }

    [Theory]
    [InlineData("VPS_HOST=a.invalid\r\n")]
    [InlineData("VPS_HOST=\"a.invalid\"\n")]
    [InlineData("VPS_HOST='a.invalid'\n")]
    public void LeerEnv_ValorConCrLfOComillas_Aborta3NombrandoLaClave(string contenido)
    {
        var env = ArchivoEnv(contenido);

        var r = Correr($"orq_leer_env '{env}' V VPS_HOST\necho llego-a-usarlo");

        Assert.True(r.ExitCode == 3, Diag(r));
        Assert.Contains("VPS_HOST", r.Stderr);
        Assert.DoesNotContain("a.invalid", r.Stderr);
        Assert.DoesNotContain("llego-a-usarlo", r.Stdout);
    }

    [Fact]
    public void LeerEnv_ArchivoAusente_Aborta3ConLaRuta()
    {
        var ruta = Path.Combine(_dir, "no-existe.env");

        var r = Correr($"orq_leer_env '{ruta}' V VPS_HOST");

        Assert.True(r.ExitCode == 3, Diag(r));
        Assert.Contains(ruta, r.Stderr);
    }

    // ---------- orq_hosts_de_ensayo ----------

    [Theory]
    [InlineData("a.invalid", 0)]
    [InlineData("a.invalid b.invalid", 0)]
    [InlineData("a.invalid vps.ejemplo.test", 1)]
    [InlineData("a.invalid.test", 1)]
    [InlineData("", 1)]
    public void HostsDeEnsayo_SoloSiTodosTerminanEnInvalid(string hosts, int esperado)
    {
        var r = Correr($"rc=0; orq_hosts_de_ensayo {hosts} || rc=$?\necho \"rc=$rc\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal($"rc={esperado}", r.Stdout);
    }

    // ---------- orq_estado_* ----------

    [Fact]
    public void EstadoPoner_EsAtomicoYResultadoQuedaUltimo()
    {
        var r = Correr(Corrida() +
            "orq_estado_poner paso_3 'ok 2026-10-20T10:00:00Z 4'\n" +
            "orq_estado_poner resultado GO\n" +
            "rc=0; orq_estado_poner paso_4 $'a\\nb' || rc=$?\n" +
            "echo \"rc=$rc\"\n" +
            "ls -A \"$ORQ_CORRIDA\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        var lineas = r.Stdout.Split('\n');
        Assert.NotEqual("rc=0", lineas[0]);
        Assert.StartsWith("rc=", lineas[0]);
        // Ningún temporal: solo el archivo de estado.
        Assert.Equal(["estado"], lineas[1..]);
        Assert.Equal("paso_3=ok 2026-10-20T10:00:00Z 4\nresultado=GO\n",
            File.ReadAllText(Path.Combine(Home, "corrida", "estado")));
    }

    [Fact]
    public void EstadoPoner_ConResultadoPrevio_ReemplazaEnSuLugarYResultadoSigueUltimo()
    {
        var r = Correr(Corrida() +
            "orq_estado_poner modo corte\n" +
            "orq_estado_poner paso_3 uno\n" +
            "orq_estado_poner resultado NO-GO\n" +
            "orq_estado_poner paso_3 dos\n" +
            "orq_estado_poner paso_4 tres\n" +
            "cat \"$ORQ_CORRIDA/estado\"\n" +
            "echo ---\n" +
            "orq_estado_poner resultado GO\n" +
            "cat \"$ORQ_CORRIDA/estado\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        // Una clave nueva o repetida después de `resultado` no lo desplaza de la última línea.
        Assert.Equal("modo=corte\npaso_3=dos\npaso_4=tres\nresultado=NO-GO\n---\n" +
                     "modo=corte\npaso_3=dos\npaso_4=tres\nresultado=GO", r.Stdout);
    }

    [Fact]
    public void EstadoQuitar_Resultado_DejaLaCorridaSinTerminar()
    {
        var r = Correr(Corrida() +
            "orq_estado_poner paso_3 uno\n" +
            "orq_estado_poner resultado NO-GO\n" +
            "orq_estado_quitar resultado\n" +
            "orq_estado_quitar clave_que_no_estaba\n" +
            "rc=0; orq_estado_leer resultado >/dev/null || rc=$?\n" +
            "echo \"rc=$rc\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=1", r.Stdout);
        Assert.Equal("paso_3=uno\n", File.ReadAllText(Path.Combine(Home, "corrida", "estado")));
    }

    [Theory]
    [InlineData("Paso-3")]
    [InlineData("a=b")]
    [InlineData("")]
    public void EstadoPoner_ClaveInvalida_RechazaYNoToca(string clave)
    {
        var r = Correr(Corrida() +
            "orq_estado_poner paso_1 ok\n" +
            $"rc=0; orq_estado_poner '{clave}' x || rc=$?\n" +
            "echo \"rc=$rc\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=2", r.Stdout);
        Assert.Equal("paso_1=ok\n", File.ReadAllText(Path.Combine(Home, "corrida", "estado")));
    }

    [Fact]
    public void EstadoLeer_YEstadoDe_DevuelvenElValorYDistinguenFaltaDeNoPudeLeer()
    {
        var otro = ArchivoEnv("sha256=a=b\nmodo=corte\n");

        var r = Correr(Corrida() +
            "orq_estado_poner volcado 'volcado 1.dump'\n" +
            "echo \"leer=$(orq_estado_leer volcado)\"\n" +
            $"echo \"de=$(orq_estado_de '{otro}' sha256)\"\n" +
            "rc=0; orq_estado_leer no_esta >/dev/null || rc=$?; echo \"falta=$rc\"\n" +
            $"rc=0; orq_estado_de '{_dir}/no-existe' modo >/dev/null 2>&1 || rc=$?; echo \"ilegible=$rc\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("leer=volcado 1.dump\nde=a=b\nfalta=1\nilegible=2", r.Stdout);
    }

    // ---------- orq_lock ----------

    [Fact]
    public void Lock_Libre_LoTomaEnElFd9()
    {
        var r = Correr("orq_lock\n" +
            $"rc=0; ( flock -n 8 ) 8>>'{LockFile}' || rc=$?\n" +
            "echo \"otro=$rc\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("otro=1", r.Stdout);
    }

    [Fact]
    public async Task Lock_Tomado_Aborta3()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LockFile)!);
        var titular = new Process
        {
            StartInfo = new ProcessStartInfo("flock")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        titular.StartInfo.ArgumentList.Add(LockFile);
        titular.StartInfo.ArgumentList.Add("-c");
        titular.StartInfo.ArgumentList.Add("cat");
        titular.Start();
        try
        {
            // Sin sleep: se espera con reloj monotónico a que el lock del titular sea observable.
            var tomado = await EsperaMonotonica.HastaAsync(
                () => BashLimpio.Ejecutar($"exec 8>>'{LockFile}'; ! flock -n 8").ExitCode == 0,
                TimeSpan.FromSeconds(10));
            Assert.True(tomado, "el titular nunca llegó a tomar el lock");

            var r = Correr("orq_lock\necho llego-a-correr");

            Assert.True(r.ExitCode == 3, Diag(r));
            Assert.DoesNotContain("llego-a-correr", r.Stdout);
            Assert.Contains("migración", r.Stderr);
        }
        finally
        {
            titular.StandardInput.Close();
            if (!titular.WaitForExit(10000)) titular.Kill();
            titular.Dispose();
        }
    }

    // ---------- orq_dir_corrida ----------

    [Theory]
    [InlineData("REPO/home-falso")]
    [InlineData("REPO")]
    [InlineData("/mnt/home-falso")]
    public void DirCorrida_HomeDentroDelRepoOBajoMnt_Rechaza3(string home)
    {
        home = home.Replace("REPO", BashLimpio.RaizRepo());
        var r = BashLimpio.Ejecutar(Estricto + Cargar + "orq_dir_corrida corte\necho llego-a-correr", home);

        Assert.True(r.ExitCode == 3, Diag(r));
        Assert.Contains("HOME", r.Stderr);
        Assert.DoesNotContain("llego-a-correr", r.Stdout);
        Assert.False(Directory.Exists(Path.Combine(home, "stockapp-backups-vps")),
            "creó el directorio de corridas dentro del repo o bajo /mnt");
    }

    [Fact]
    public void DirCorrida_Crea700BajoHome_YDefineOrqCorridaEnElShellActual()
    {
        // Sin $(): ORQ_CORRIDA y el exit 3 tienen que vivir en el shell del orquestador.
        var r = Correr("orq_dir_corrida corte\necho \"ORQ_CORRIDA=$ORQ_CORRIDA\"\nstat -c %a \"$ORQ_CORRIDA\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        var lineas = r.Stdout.Split('\n');
        Assert.Matches(new Regex($"^{Regex.Escape(Corridas)}/corte-\\d{{8}}T\\d{{6}}Z$"), lineas[0]);
        Assert.Equal("ORQ_CORRIDA=" + lineas[0], lineas[1]);
        Assert.Equal("700", lineas[2]);
        Assert.True(Directory.Exists(lineas[0]));
    }

    [Fact]
    public void DirCorrida_ModoInvalido_Aborta2()
    {
        var r = Correr("orq_dir_corrida cualquiera");

        Assert.True(r.ExitCode == 2, Diag(r));
        Assert.False(Directory.Exists(Corridas));
    }

    [Fact]
    public void DirCorrida_YaExiste_Falla3SinTocarLaExistente()
    {
        BashLimpio.EscribirEjecutable(Path.Combine(Bin, "date"), "#!/usr/bin/env bash\necho 20261020T100000Z\n");

        var r = Correr($"export PATH='{Bin}':\"$PATH\"\n" +
            "orq_dir_corrida corte >/dev/null\n" +
            "touch \"$ORQ_CORRIDA/estado\"\n" +
            "rc=0; ( orq_dir_corrida corte ) >/dev/null 2>&1 || rc=$?\n" +
            "echo \"rc=$rc\"\n" +
            "ls \"$ORQ_CORRIDA\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=3\nestado", r.Stdout);
    }

    [Fact]
    public void RepoRoot_SeDerivaDeLaUbicacionDeLaLib()
    {
        var r = Correr("echo \"$REPO_ROOT\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal(BashLimpio.RaizRepo().TrimEnd('/'), r.Stdout);
    }

    // ---------- orq_log_iniciar ----------

    [Fact]
    public void Log_IniciarCopiaStdoutYStderrAlRegistroEnModo600()
    {
        var r = Correr("orq_dir_corrida ensayo >/dev/null\norq_log_iniciar\n" +
            "echo en-stdout\necho en-stderr >&2");

        Assert.True(r.ExitCode == 0, Diag(r));
        // Todo sale por stdout (y al log): el stderr del script también pasa por el tee.
        Assert.Contains("en-stdout", r.Stdout);
        Assert.Contains("en-stderr", r.Stdout);
        var log = Directory.GetFiles(Corridas, "registro.log", SearchOption.AllDirectories).Single();
        var texto = File.ReadAllText(log);
        Assert.Contains("en-stdout", texto);
        Assert.Contains("en-stderr", texto);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(log));
    }

    // ---------- orq_confirmar ----------

    [Fact]
    public void Confirmar_PalabraExacta_Ok()
    {
        var r = CorrerSinTerminal("ORQ_ENSAYO=1\n" +
            "orq_confirmar CONGELAR 'Se va a congelar el VPS viejo' <<< 'CONGELAR'\n" +
            "echo confirmado");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Contains("Se va a congelar el VPS viejo", r.Stdout);
        Assert.Contains("confirmado", r.Stdout);
    }

    [Theory]
    [InlineData("congelar")]
    [InlineData("CONGELAR ")]
    [InlineData(" CONGELAR")]
    [InlineData("")]
    public void Confirmar_MinusculasOEspacios_Rechaza4(string escrito)
    {
        var r = CorrerSinTerminal("ORQ_ENSAYO=1\n" +
            $"orq_confirmar CONGELAR 'texto' <<< '{escrito}'\n" +
            "echo no-debia-llegar");

        Assert.True(r.ExitCode == 4, Diag(r));
        Assert.DoesNotContain("no-debia-llegar", r.Stdout);
    }

    [Fact]
    public void Confirmar_SinTerminalNiEnsayo_Rechaza3()
    {
        var r = CorrerSinTerminal("orq_confirmar CONGELAR 'texto' <<< 'CONGELAR'\necho no-debia-llegar");

        Assert.True(r.ExitCode == 3, Diag(r));
        Assert.DoesNotContain("no-debia-llegar", r.Stdout);
    }

    [Fact]
    public void Confirmar_OrqEnsayoHeredadoDelEntorno_NoHabilitaStdin()
    {
        // ORQ_ENSAYO=1 viene de afuera (export): la lib lo pone en 0 al cargarse, así que el
        // desvío por stdin solo existe si el orquestador lo activó después de mirar los hosts.
        var r = CorrerSinTerminal("orq_confirmar CONGELAR 'texto' <<< 'CONGELAR'\necho no-debia-llegar",
            prefijo: "export ORQ_ENSAYO=1\n");

        Assert.True(r.ExitCode == 3, Diag(r));
        Assert.DoesNotContain("no-debia-llegar", r.Stdout);
    }

    [Fact]
    public void Bloque_NoConsumeElStdinDelOrquestador_YLaConfirmacionSigueLeyendo()
    {
        var r = CorrerSinTerminal("ORQ_ENSAYO=1\n" + BloqueFalso("lee.sh",
                "if IFS= read -r x; then echo \"leyo=[$x]\"; else echo sin-stdin; fi\n") +
            "{\n" +
            "    orq_bloque deploy/lee.sh /nada\n" +
            "    orq_confirmar CONGELAR 'texto'\n" +
            "    echo confirmado\n" +
            "} <<< CONGELAR");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Contains("    │ sin-stdin", r.Stdout);
        Assert.Contains("confirmado", r.Stdout);
    }

    // ---------- orq_ssh ----------

    private const string SshFalso = """
        #!/usr/bin/env bash
        {
            printf 'ARGS:'
            printf ' %q' "$@"
            printf '\n'
            printf 'STDERR_EN:%s\n' "$(readlink /proc/self/fd/2)"
            if [ -e /proc/self/fd/9 ]; then echo 'FD9:abierto'; else echo 'FD9:cerrado'; fi
        } >> "$RASTRO"
        if [ -n "${ENTRADA_A:-}" ]; then cat > "$ENTRADA_A"; fi
        if [ -z "${SSH_SOLO_STDERR:-}" ]; then echo "salida-falsa"; fi
        echo "error-falso" >&2
        exit "${SSH_RC:-0}"
        """;

    private string PrepararSsh()
    {
        BashLimpio.EscribirEjecutable(Path.Combine(Bin, "ssh"), SshFalso);
        return $"export PATH='{Bin}':\"$PATH\" RASTRO='{Rastro}'\n" +
               "VIEJO_VPS_HOST=viejo.invalid\nVIEJO_VPS_USER=operador\nVIEJO_VPS_SSH_PORT=2222\n";
    }

    private string RastroSsh() => File.Exists(Rastro) ? File.ReadAllText(Rastro) : "";

    [Fact]
    public void Ssh_ArmaElArgvExactoYDejaRcSalidaYStderr()
    {
        var r = Correr(PrepararSsh() +
            "orq_ssh VIEJO 'echo hola mundo'\n" +
            "echo \"rc=$ORQ_RC\"\necho \"salida=$ORQ_SALIDA\"\necho \"stderr=$ORQ_STDERR\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=0\nsalida=salida-falsa\nstderr=error-falso", r.Stdout);
        Assert.Contains("ARGS: -p 2222 -o BatchMode=yes operador@viejo.invalid echo\\ hola\\ mundo\n", RastroSsh());
    }

    [Fact]
    public void Ssh_255_EsNoPudeConsultar_YNoSaleDelShell()
    {
        var r = Correr(PrepararSsh() +
            "export SSH_RC=255 SSH_SOLO_STDERR=1\n" +
            "orq_ssh VIEJO 'systemctl is-active stockapp-api'\n" +
            "echo \"rc=$ORQ_RC\"\necho \"salida=[$ORQ_SALIDA]\"\necho \"stderr=$ORQ_STDERR\"\necho SIGUE");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=255\nsalida=[]\nstderr=error-falso\nSIGUE", r.Stdout);
    }

    [Fact]
    public void Ssh_255ConSalidaParcial_NoDevuelveSalida()
    {
        var r = Correr(PrepararSsh() +
            "export SSH_RC=255\n" +
            "orq_ssh VIEJO 'ver'\n" +
            "echo \"rc=$ORQ_RC\"\necho \"salida=[$ORQ_SALIDA]\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=255\nsalida=[]", r.Stdout);
    }

    [Fact]
    public void Ssh_CodigoDistintoDe255_SeDevuelveTalCual()
    {
        var r = Correr(PrepararSsh() +
            "export SSH_RC=3\n" +
            "orq_ssh VIEJO 'ver'\n" +
            "echo \"rc=$ORQ_RC\"\necho \"salida=$ORQ_SALIDA\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=3\nsalida=salida-falsa", r.Stdout);
    }

    [Fact]
    public void Ssh_StdinPorTercerArgumento_LlegaIntactoYSinSaltoDeLineaExtra()
    {
        var recibido = Path.Combine(_dir, "stdin-recibido");

        var r = Correr(PrepararSsh() + $"export ENTRADA_A='{recibido}'\n" +
            "cuerpo=$'{\"u\":\"x\"}\\nlinea \"dos\" $HOME `ls`'\n" +
            "orq_ssh VIEJO 'curl --data @-' \"$cuerpo\"\n" +
            "echo \"rc=$ORQ_RC\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("{\"u\":\"x\"}\nlinea \"dos\" $HOME `ls`", File.ReadAllText(recibido));
        // El cuerpo nunca viaja en el argv (podría llevar credenciales).
        Assert.DoesNotContain("linea", RastroSsh().Split('\n').First(l => l.StartsWith("ARGS:")));
    }

    [Fact]
    public void Ssh_SinStdinExplicito_NoLeeDelDeLaSesion()
    {
        var recibido = Path.Combine(_dir, "stdin-recibido");

        var r = Correr(PrepararSsh() + $"export ENTRADA_A='{recibido}'\n" +
            "printf 'DEL-LLAMADOR' | { orq_ssh VIEJO 'ver'; }");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("", File.ReadAllText(recibido));
    }

    [Fact]
    public void Ssh_StderrVaAUnTemporalDentroDeLaCorrida_YNoQuedaNada()
    {
        var r = Correr(PrepararSsh() + Corrida() +
            "orq_ssh VIEJO 'ver'\n" +
            "ls -A \"$ORQ_CORRIDA\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("", r.Stdout);
        Assert.Matches($"STDERR_EN:{Regex.Escape(Path.Combine(Home, "corrida"))}/", RastroSsh());
    }

    [Fact]
    public void Ssh_SinCorrida_EsteTemporalEsLocalYNoQuedaNada()
    {
        var tmp = Path.Combine(_dir, "tmp");
        Directory.CreateDirectory(tmp);

        var r = Correr(PrepararSsh() + $"export TMPDIR='{tmp}'\n" +
            "orq_ssh VIEJO 'ver'\n" +
            $"ls -A '{tmp}'");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("", r.Stdout);
        Assert.Matches($"STDERR_EN:{Regex.Escape(tmp)}/", RastroSsh());
    }

    [Fact]
    public void Ssh_NoHeredaElFd9DelLock_ConNiSinStdinNiConNuevaConexion()
    {
        var r = Correr(PrepararSsh() + "orq_lock\n" +
            "orq_ssh VIEJO 'sin-stdin'\n" +
            "orq_ssh VIEJO 'con-stdin' 'cuerpo'\n" +
            "orq_ssh_nueva_conexion VIEJO 'nueva'");

        Assert.True(r.ExitCode == 0, Diag(r));
        var rastro = RastroSsh();
        Assert.Equal(3, Regex.Matches(rastro, "FD9:cerrado").Count);
        Assert.DoesNotContain("FD9:abierto", rastro);
    }

    [Fact]
    public void Ssh_ConfiguracionIncompleta_EsNoPudeConsultar_SinLlamarASsh()
    {
        var r = Correr(PrepararSsh() +
            "unset VIEJO_VPS_USER\n" +
            "orq_ssh VIEJO 'ver'\n" +
            "echo \"rc=$ORQ_RC\"\necho \"stderr=$ORQ_STDERR\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.StartsWith("rc=255\nstderr=", r.Stdout);
        Assert.Contains("VIEJO_VPS_USER", r.Stdout);
        Assert.Equal("", RastroSsh());
    }

    [Fact]
    public void SshNuevaConexion_DesactivaElMaestro()
    {
        var r = Correr(PrepararSsh() +
            "orq_ssh_nueva_conexion VIEJO 'docker ps'\n" +
            "echo \"rc=$ORQ_RC\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        var rastro = RastroSsh();
        Assert.Contains("ControlMaster=no", rastro);
        Assert.Contains("ControlPath=none", rastro);
        Assert.Contains("ConnectTimeout=10", rastro);
        Assert.Contains("-p 2222 -o BatchMode=yes", rastro);
    }

    // ---------- orq_bloque ----------

    private string BloqueFalso(string nombre, string cuerpo)
    {
        var repo = Path.Combine(_dir, "repo");
        BashLimpio.EscribirEjecutable(Path.Combine(repo, "deploy", nombre), "#!/usr/bin/env bash\n" + cuerpo);
        return $"REPO_ROOT='{repo}'\n";
    }

    [Fact]
    public void Bloque_NoHeredaStockappEnvLocalDeLaSesion()
    {
        var env = Path.Combine(_dir, "bloque.env");
        var preludio = BloqueFalso("falso.sh",
            "echo \"ENV=${STOCKAPP_ENV_LOCAL-<sin>}\"\n" +
            "echo \"ARGS=$*\"\n" +
            "echo error-del-bloque >&2\n" +
            "exit 7\n");

        var r = Correr(preludio +
            $"orq_bloque deploy/falso.sh '{env}' --uno 'dos tres'\n" +
            "echo \"RC_BLOQUE=$ORQ_RC_BLOQUE\"\n" +
            "echo \"SESION=[${STOCKAPP_ENV_LOCAL+definida}]\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        var lineas = r.Stdout.Split('\n');
        Assert.Equal(
        [
            $"    │ ENV={env}",
            "    │ ARGS=--uno dos tres",
            "    │ error-del-bloque",
            "RC_BLOQUE=7",
            "SESION=[]",
        ], lineas);
        Assert.DoesNotContain("error-del-bloque", r.Stderr);
    }

    [Fact]
    public void Bloque_ExitCero_RcCero()
    {
        var r = Correr(BloqueFalso("bien.sh", "echo ok\n") +
            "orq_bloque deploy/bien.sh /nada\necho \"RC_BLOQUE=$ORQ_RC_BLOQUE\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("    │ ok\nRC_BLOQUE=0", r.Stdout);
    }

    [Fact]
    public void Bloque_NoHeredaElFd9DelLock()
    {
        var r = Correr(BloqueFalso("fd.sh",
                "if [ -e /proc/self/fd/9 ]; then echo FD9=abierto; else echo FD9=cerrado; fi\n") +
            "orq_lock\norq_bloque deploy/fd.sh /nada");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Contains("FD9=cerrado", r.Stdout);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bloque_DejaElErrexitComoLoEncontro(bool conErrexit)
    {
        var r = BashLimpio.Ejecutar(
            Cargar + BloqueFalso("falla.sh", "exit 4\n") +
            (conErrexit ? "set -e\n" : "set +e\n") +
            "orq_bloque deploy/falla.sh /nada >/dev/null\n" +
            "echo \"rc=${ORQ_RC_BLOQUE-sin-definir}\"\n" +
            "case $- in *e*) echo errexit=on ;; *) echo errexit=off ;; esac", Home);

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=4\n" + (conErrexit ? "errexit=on" : "errexit=off"), r.Stdout);
    }

    // ---------- trap de señales (R6) ----------

    [Fact]
    public void Trap_Instalado_ParaIntYTerm()
    {
        var r = Correr("trap -p INT TERM");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Contains("trap -- 'exit 130' SIGINT", r.Stdout);
        Assert.Contains("trap -- 'exit 130' SIGTERM", r.Stdout);
    }

    [Fact]
    public void Trap_SigintMientrasCorreUnBloque_Sale130SinSeguirEjecutando()
    {
        // El bloque le manda SIGINT al orquestador y termina bien: sin el trap, bash (no
        // interactivo) no murió por la señal y seguiría ejecutando el script.
        var r = Correr(BloqueFalso("mata-al-padre.sh", "kill -INT \"$PPID\"\nexit 0\n") +
            "orq_bloque deploy/mata-al-padre.sh /nada\necho SIGUE-EJECUTANDO");

        Assert.True(r.ExitCode == 130, Diag(r));
        Assert.DoesNotContain("SIGUE-EJECUTANDO", r.Stdout);
    }

    // ---------- orq_paso / orq_fin_paso ----------

    private static string SinHora(string linea) => Regex.Replace(linea, @"^\[\d\d:\d\d:\d\d\] ", "");

    [Fact]
    public void Paso_LineaConNumeroResultadoYDuracion()
    {
        var r = Correr("SECONDS=0\norq_paso 4 12 'volcado del VPS viejo'\nSECONDS=41\norq_fin_paso OK\n" +
            "SECONDS=0\norq_paso 5 12 'transferencia al VPS nuevo'\nSECONDS=3\norq_fin_paso OK");

        Assert.True(r.ExitCode == 0, Diag(r));
        var lineas = r.Stdout.Split('\n');
        Assert.Matches(@"^\[\d\d:\d\d:\d\d\] ", lineas[0]);
        Assert.Equal("( 4/12) volcado del VPS viejo ........................ OK      41 s", SinHora(lineas[0]));
        Assert.Equal("( 5/12) transferencia al VPS nuevo ................... OK       3 s", SinHora(lineas[1]));
    }

    [Fact]
    public void Paso_FalloConDetalle_YResultadosConEspacio()
    {
        var r = Correr("SECONDS=0\norq_paso 6 12 'restauración (--recrear-base)'\nSECONDS=12\n" +
            "orq_fin_paso FALLÓ 'restaurar-backup.sh: 6'\n" +
            "SECONDS=0\norq_paso 7 12 'docker'\norq_fin_paso 'YA ESTABA'\n" +
            "orq_paso 8 12 'cert'\norq_fin_paso OMITIDO 'sin --probar-renovacion'");

        Assert.True(r.ExitCode == 0, Diag(r));
        var lineas = r.Stdout.Split('\n').Select(SinHora).ToArray();
        Assert.Matches(@"^\( 6/12\) restauración \(--recrear-base\) \.+ FALLÓ\s+12 s  \(restaurar-backup\.sh: 6\)$", lineas[0]);
        Assert.Matches(@"^\( 7/12\) docker \.+ YA ESTABA\s+0 s$", lineas[1]);
        Assert.Matches(@"^\( 8/12\) cert \.+ OMITIDO\s+0 s  \(sin --probar-renovacion\)$", lineas[2]);
    }

    [Fact]
    public void Paso_ResultadoDesconocido_Rechaza2()
    {
        var r = Correr("orq_paso 1 2 x\nrc=0; orq_fin_paso BIEN >/dev/null 2>&1 || rc=$?\necho \"rc=$rc\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=2", r.Stdout);
    }

    [Fact]
    public void Paso_ConCongelado_AgregaElReloj()
    {
        var r = Correr("SECONDS=0\nORQ_CONGELADO_SEG=0\norq_paso 6 12 'restauración'\nSECONDS=192\n" +
            "orq_fin_paso OK");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.EndsWith("OK     192 s  viejo sin API hace 3 min 12 s", r.Stdout);
    }

    [Fact]
    public void Paso_SinCongelado_NoAgregaReloj()
    {
        var r = Correr("orq_paso 6 12 'restauración'\norq_fin_paso OK");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.DoesNotContain("viejo sin API", r.Stdout);
    }

    [Fact]
    public void Paso_CongeladoQueNoEsUnNumero_Rechaza2()
    {
        var r = Correr("ORQ_CONGELADO_SEG=abc\norq_paso 6 12 x\nrc=0; orq_fin_paso OK >/dev/null 2>&1 || rc=$?\necho \"rc=$rc\"");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("rc=2", r.Stdout);
    }
}
