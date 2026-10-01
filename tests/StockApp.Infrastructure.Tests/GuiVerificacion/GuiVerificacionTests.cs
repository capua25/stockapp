using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.GuiVerificacion;

/// <summary>
/// Guardianes FAIL-CLOSED de scripts/gui-verificacion/ (capturar.sh, click.sh, escribir.sh).
///
/// Contexto (2026-10-01, incidente de privacidad): capturar.sh, al no encontrar la ventana
/// (había un popup abierto, entonces Get-Process ya no matcheaba "Municipal"), capturaba el
/// ESCRITORIO COMPLETO de todos los monitores del usuario -- que tenía una videollamada abierta
/// -- y salía con 0. Dos veces. escribir.sh y click.sh podían mandar teclas/clicks a la ventana
/// que el usuario tuviera en primer plano.
///
/// La regla que custodian estos tests: el toolkit NUNCA captura el escritorio ni manda input sin
/// una ventana CONFIRMADA. Con la ventana ausente / no en primer plano / con rectángulo sospechoso /
/// con el click fuera de la ventana: exit != 0, ningún PNG, ninguna tecla, ningún click.
///
/// Patrón (el mismo del kit de instalación): 'powershell.exe', 'xdotool' y 'wslpath' stubeados por
/// PATH. El stub de powershell.exe devuelve HECHOS canned (lista de ventanas, foreground, escritorio
/// virtual) y registra qué operación se le pidió; la decisión (¿existe?, ¿es única?, ¿está al
/// frente?, ¿el rectángulo es razonable?) vive en bash y es lo que se ejercita acá. Lo que NO cubre
/// un stub (el PowerShell real: EnumWindows, SendKeys, CopyFromScreen, el chequeo de primer plano
/// dentro del proceso PowerShell) se verificó contra la app real; ver README.md del toolkit.
///
/// Mutación (2026-10-01): reponer en capturar.sh el fallback "si no hay ventana, capturar el
/// escritorio completo" pone en rojo los guardianes de ventana ausente (ver el resumen del commit).
/// </summary>
public class GuiVerificacionTests
{
    private const string Pid = "35462";

    private static string Toolkit([CallerFilePath] string archivo = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivo)!, "..", "..", "..",
            "scripts", "gui-verificacion"));

    // ---------- Hechos canned del lado Windows ----------

    private const string VentanaApp = "W|4392688|18112|msrdc|100|100|900|700|0|Gestión Municipal (Ubuntu)";
    private const string VirtualScreen = "V|0|0|1920|1080";
    private const string ForegroundApp = "F|4392688|18112|msrdc";

    private static string Listado(string[] ventanas, string foreground, string virtualScreen = VirtualScreen) =>
        string.Join("\n", ventanas.Append(foreground).Append(virtualScreen)) + "\n";

    private static string ListadoNormal() => Listado([VentanaApp], ForegroundApp);

    private sealed record Escenario(
        string Listado,
        string XdotoolSearchId = "6291471",
        string XdotoolAppPid = Pid,
        string XdotoolFocusPid = Pid,
        int CapturarRc = 0,
        int EnviarRc = 0,
        int ListarRc = 0);

    private sealed record Resultado(
        int ExitCode, string Salida, string RastroPs, string RastroX, string[] Pngs);

    // ---------- Stubs ----------

    private const string PowershellStub = """
        #!/usr/bin/env bash
        all="$*"
        case "$all" in
          *"GUI_OP=listar"*)
            echo "OP=listar" >> "$STUB_RASTRO_PS"
            [ "${STUB_LISTAR_RC:-0}" -ne 0 ] && exit "$STUB_LISTAR_RC"
            cat "$STUB_LISTADO" ;;
          *"GUI_OP=foco"*)
            echo "OP=foco" >> "$STUB_RASTRO_PS" ;;
          *"GUI_OP=capturar"*)
            echo "OP=capturar" >> "$STUB_RASTRO_PS"
            printf '%s\n' "$all" | grep -E '^\$(aux|hwnd|winpid|rw|rh) ' >> "$STUB_RASTRO_PS"
            [ "${STUB_CAPTURAR_RC:-0}" -ne 0 ] && exit "$STUB_CAPTURAR_RC"
            dest="$STUB_PNG_ORIGEN/captura-stub.png"
            python3 - "$dest" <<'PY'
        import sys, zlib, struct
        w, h = 16, 16
        raw = b''.join(b'\x00' + bytes((x * 16 + y) % 256 for x in range(w)) for y in range(h))
        def ch(t, d):
            c = struct.pack('>I', len(d)) + t + d
            return c + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
        open(sys.argv[1], 'wb').write(b'\x89PNG\r\n\x1a\n' + ch(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 0, 0, 0, 0)) + ch(b'IDAT', zlib.compress(raw)) + ch(b'IEND', b''))
        PY
            echo "$dest" ;;
          *"GUI_OP=enviar"*)
            echo "OP=enviar" >> "$STUB_RASTRO_PS"
            printf '%s\n' "$all" | grep -E '^\$(aux|hwnd|winpid) ' >> "$STUB_RASTRO_PS"
            exit "${STUB_ENVIAR_RC:-0}" ;;
          *)
            echo "OP=DESCONOCIDA" >> "$STUB_RASTRO_PS"
            exit 99 ;;
        esac
        """;

    // wslpath -u <ruta>: el stub ya devuelve una ruta Linux.
    private const string WslpathStub = """
        #!/usr/bin/env bash
        printf '%s\n' "$2"
        """;

    private const string XdotoolStub = """
        #!/usr/bin/env bash
        case "$1" in
          search)
            [ -n "$XD_SEARCH_ID" ] || exit 1
            echo "$XD_SEARCH_ID" ;;
          getwindowpid)
            # 6291999: una segunda ventana X11 de OTRO proceso Linux (para el caso ambiguo).
            if [ "$2" = "6291999" ]; then echo 11111; else echo "$XD_APP_PID"; fi ;;
          getwindowfocus)
            [ -n "$XD_FOCUS_PID" ] || exit 1
            echo "$XD_FOCUS_PID" ;;
          mousemove)
            echo "mousemove $*" >> "$XD_RASTRO" ;;
          *)
            exit 1 ;;
        esac
        """;

    private static void EscribirEjecutable(string ruta, string contenido)
    {
        File.WriteAllText(ruta, contenido.Replace("\r\n", "\n"));
        File.SetUnixFileMode(ruta,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Corre <paramref name="comando"/> (relativo al toolkit) con los tres stubs por PATH.</summary>
    private static Resultado Correr(string comando, Escenario e, string? salidaPng = null)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "gui-verif-" + Guid.NewGuid());
        var testbin = Path.Combine(raiz, "bin");
        var pngOrigen = Path.Combine(raiz, "png-origen");
        var toolkitVacio = Path.Combine(raiz, "toolkit-sin-env"); // sin env.sh: xdotool sale del PATH
        Directory.CreateDirectory(testbin);
        Directory.CreateDirectory(pngOrigen);
        Directory.CreateDirectory(toolkitVacio);
        try
        {
            EscribirEjecutable(Path.Combine(testbin, "powershell.exe"), PowershellStub);
            EscribirEjecutable(Path.Combine(testbin, "wslpath"), WslpathStub);
            EscribirEjecutable(Path.Combine(testbin, "xdotool"), XdotoolStub);
            var listado = Path.Combine(raiz, "listado.txt");
            File.WriteAllText(listado, e.Listado);
            var rastroPs = Path.Combine(raiz, "rastro-ps.txt");
            var rastroX = Path.Combine(raiz, "rastro-xdotool.txt");

            var script =
                $"export PATH=\"{testbin}:$PATH\"\n" +
                $"export TOOLKIT_DIR=\"{toolkitVacio}\"\n" +
                $"export STUB_LISTADO=\"{listado}\" STUB_RASTRO_PS=\"{rastroPs}\" STUB_PNG_ORIGEN=\"{pngOrigen}\"\n" +
                $"export STUB_CAPTURAR_RC={e.CapturarRc} STUB_ENVIAR_RC={e.EnviarRc} STUB_LISTAR_RC={e.ListarRc}\n" +
                $"export XD_SEARCH_ID=\"{e.XdotoolSearchId}\" XD_APP_PID=\"{e.XdotoolAppPid}\" " +
                $"XD_FOCUS_PID=\"{e.XdotoolFocusPid}\" XD_RASTRO=\"{rastroX}\"\n" +
                $"export ESCRIBIR_DELAY_MS=0\n" +
                $"cd \"{Toolkit()}\"\n" +
                $"{comando.Replace("{SALIDA}", salidaPng ?? "/dev/null")} 2>&1";

            var (exitCode, stdout, _) = EjecutorBash.Ejecutar(script);
            return new Resultado(
                exitCode,
                stdout,
                File.Exists(rastroPs) ? File.ReadAllText(rastroPs) : "",
                File.Exists(rastroX) ? File.ReadAllText(rastroX) : "",
                []);
        }
        finally
        {
            Directory.Delete(raiz, recursive: true);
        }
    }

    private static string RutaPng() =>
        Path.Combine(Path.GetTempPath(), "gui-verif-salida-" + Guid.NewGuid() + ".png");

    private static void AfirmarNadaEnviado(Resultado r)
    {
        Assert.DoesNotContain("OP=capturar", r.RastroPs);
        Assert.DoesNotContain("OP=enviar", r.RastroPs);
        Assert.DoesNotContain("mousemove", r.RastroX);
    }

    // ===================== capturar.sh =====================

    [Fact]
    public void Capturar_VentanaPresenteYAlFrente_GeneraElPngYSaleConCero()
    {
        var png = RutaPng();
        try
        {
            var r = Correr($"bash capturar.sh '{png}' Municipal", new Escenario(ListadoNormal()));

            Assert.True(r.ExitCode == 0, r.Salida);
            Assert.True(File.Exists(png));
            Assert.Contains("OP=capturar", r.RastroPs);
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_VentanaAusente_FallaSinPngYSinCapturarNada()
    {
        var png = RutaPng();
        try
        {
            // Solo hay un Brave con "Municipal" en el título (no es de WSLg) y el escritorio virtual:
            // exactamente el escenario del incidente, la app no matchea.
            var listado = Listado(
                ["W|2164642|19428|brave|0|0|1900|1000|0|Municipal - hoja de calculo - Brave"],
                "F|2164642|19428|brave");

            var r = Correr($"bash capturar.sh '{png}' Municipal", new Escenario(listado));

            Assert.NotEqual(0, r.ExitCode);
            Assert.Equal(2, r.ExitCode);
            Assert.False(File.Exists(png), "NO debe generarse ningún PNG si la ventana no se encuentra.");
            AfirmarNadaEnviado(r);
            Assert.Contains("Escape", r.Salida);          // pista de recuperación
            Assert.Contains("popup", r.Salida);
            Assert.DoesNotContain("ESCRITORIO", r.Salida.Replace("NO capturar el escritorio", ""));
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_VentanaAusente_BorraUnPngViejoParaNoConfundirlo()
    {
        var png = RutaPng();
        try
        {
            File.WriteAllText(png, "captura vieja");
            var r = Correr($"bash capturar.sh '{png}' Municipal", new Escenario(Listado([], "F|1|1|explorer")));

            Assert.Equal(2, r.ExitCode);
            Assert.False(File.Exists(png));
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_TituloVacio_NoCapturaNada()
    {
        var png = RutaPng();
        var r = Correr($"bash capturar.sh '{png}' ''", new Escenario(ListadoNormal()));

        Assert.Equal(1, r.ExitCode);
        Assert.False(File.Exists(png));
        AfirmarNadaEnviado(r);
        Assert.DoesNotContain("OP=listar", r.RastroPs);
    }

    [Fact]
    public void Capturar_SinTitulo_UsaMunicipalYNuncaElEscritorio()
    {
        var png = RutaPng();
        try
        {
            // Sin segundo argumento: la ventana sigue siendo identificada ("Municipal").
            var r = Correr($"bash capturar.sh '{png}'", new Escenario(ListadoNormal()));
            Assert.True(r.ExitCode == 0, r.Salida);
            Assert.Contains("hwnd 4392688", r.Salida);
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_VentanaPresentePeroOtraAppAlFrente_FallaSinPng()
    {
        var png = RutaPng();
        try
        {
            var listado = Listado([VentanaApp], "F|2164642|19428|brave");

            var r = Correr($"bash capturar.sh '{png}' Municipal", new Escenario(listado));

            Assert.Equal(5, r.ExitCode);
            Assert.False(File.Exists(png));
            Assert.DoesNotContain("OP=capturar", r.RastroPs);
            Assert.Contains("OP=foco", r.RastroPs); // intentó traerla al frente, y no pudo
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_RectanguloDelTamanoDelEscritorioVirtual_FallaSinPng()
    {
        var png = RutaPng();
        try
        {
            var listado = Listado(
                ["W|4392688|18112|msrdc|0|0|1920|1080|0|Gestión Municipal (Ubuntu)"], ForegroundApp);

            var r = Correr($"bash capturar.sh '{png}' Municipal", new Escenario(listado));

            Assert.Equal(4, r.ExitCode);
            Assert.False(File.Exists(png));
            Assert.DoesNotContain("OP=capturar", r.RastroPs);
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Theory]
    [InlineData("-32000|-32000|-31840|-31600", "minimizada")]       // sentinel Win32
    [InlineData("5000|5000|5800|5600", "fuera del escritorio")]     // coordenadas fuera de rango
    [InlineData("100|100|100|600", "tamaño 0")]                     // ancho cero
    public void Capturar_RectanguloSospechoso_FallaSinPng(string rect, string _)
    {
        var png = RutaPng();
        try
        {
            var listado = Listado(
                [$"W|4392688|18112|msrdc|{rect}|0|Gestión Municipal (Ubuntu)"], ForegroundApp);

            var r = Correr($"bash capturar.sh '{png}' Municipal", new Escenario(listado));

            Assert.Equal(4, r.ExitCode);
            Assert.False(File.Exists(png));
            Assert.DoesNotContain("OP=capturar", r.RastroPs);
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_DosVentanasQueMatchean_FallaPorAmbigua()
    {
        var png = RutaPng();
        try
        {
            var listado = Listado(
                [VentanaApp, "W|555|18112|msrdc|50|50|800|600|0|Gestión Municipal 2 (Ubuntu)"], ForegroundApp);

            var r = Correr($"bash capturar.sh '{png}' Municipal", new Escenario(listado));

            Assert.Equal(3, r.ExitCode);
            Assert.False(File.Exists(png));
            AfirmarNadaEnviado(r);
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_PopupDeLaMismaAppAlFrente_Captura()
    {
        var png = RutaPng();
        try
        {
            // Primer plano: ventana SIN título (el popup de Avalonia), mismo proceso host (msrdc,
            // 18112); el foco X11 es de la app (PID Linux 35462).
            var listado = Listado([VentanaApp], "F|11339494|18112|msrdc");

            var r = Correr($"bash capturar.sh '{png}' Municipal", new Escenario(listado));

            Assert.True(r.ExitCode == 0, r.Salida);
            Assert.True(File.Exists(png));
            Assert.Contains("$aux = 1", r.RastroPs);
            Assert.DoesNotContain("OP=foco", r.RastroPs); // no se roba el foco: cerraría el desplegable
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_VentanaDeOtraAppDeWslgAlFrente_FallaCerrado()
    {
        var png = RutaPng();
        try
        {
            // Otra app Linux (mismo msrdc, mismo PID Windows): el PID de Windows no la distingue,
            // el foco X11 sí (PID Linux 77777 != 35462).
            var listado = Listado([VentanaApp], "F|9999|18112|msrdc");

            var r = Correr($"bash capturar.sh '{png}' Municipal",
                new Escenario(listado, XdotoolFocusPid: "77777"));

            Assert.Equal(5, r.ExitCode);
            Assert.False(File.Exists(png));
            Assert.DoesNotContain("OP=capturar", r.RastroPs);
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_SinXdotool_UnPopupNoSePuedeDemostrar_FallaCerrado()
    {
        var png = RutaPng();
        try
        {
            var listado = Listado([VentanaApp], "F|11339494|18112|msrdc");

            // Sin ventana X11 identificable (search no devuelve nada) no hay PID de Linux que cruzar.
            var r = Correr($"bash capturar.sh '{png}' Municipal",
                new Escenario(listado, XdotoolSearchId: ""));

            Assert.Equal(5, r.ExitCode);
            Assert.False(File.Exists(png));
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_PowershellFalla_SaleConInteropYSinPng()
    {
        var png = RutaPng();
        try
        {
            var r = Correr($"bash capturar.sh '{png}' Municipal", new Escenario(ListadoNormal(), ListarRc: 1));

            Assert.Equal(6, r.ExitCode);
            Assert.False(File.Exists(png));
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    [Fact]
    public void Capturar_ElProcesoPowershellAbortaAlCapturar_NoDejaPng()
    {
        var png = RutaPng();
        try
        {
            // El chequeo de primer plano DENTRO del proceso que captura (exit 5) corta la captura.
            var r = Correr($"bash capturar.sh '{png}' Municipal",
                new Escenario(ListadoNormal(), CapturarRc: 5));

            Assert.Equal(5, r.ExitCode);
            Assert.False(File.Exists(png));
        }
        finally { if (File.Exists(png)) File.Delete(png); }
    }

    // ===================== click.sh =====================

    [Fact]
    public void Click_VentanaPresenteYPuntoDentro_Clickea()
    {
        var r = Correr("bash click.sh 400 300 Municipal", new Escenario(ListadoNormal()));

        Assert.True(r.ExitCode == 0, r.Salida);
        Assert.Contains("mousemove --window 6291471 362 241", r.RastroX); // 400-38, 300-59
        Assert.Contains("click 1", r.RastroX);
    }

    [Fact]
    public void Click_VentanaAusente_FallaSinMoverNiClickear()
    {
        var r = Correr("bash click.sh 400 300 Municipal", new Escenario(Listado([], "F|1|1|explorer")));

        Assert.Equal(2, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    [Fact]
    public void Click_VentanaPresentePeroOtraAppAlFrente_FallaSinClickear()
    {
        var listado = Listado([VentanaApp], "F|2164642|19428|brave");

        var r = Correr("bash click.sh 400 300 Municipal", new Escenario(listado));

        Assert.Equal(5, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    [Theory]
    [InlineData("800 100")]   // x == ancho de la ventana (800): fuera
    [InlineData("100 600")]   // y == alto (600): fuera
    [InlineData("-5 100")]
    [InlineData("5000 5000")]
    public void Click_FueraDelRectanguloDeLaVentana_SeRechaza(string coords)
    {
        var r = Correr($"bash click.sh {coords} Municipal", new Escenario(ListadoNormal()));

        Assert.Equal(4, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    [Fact]
    public void Click_SobreLaBarraDeTitulo_SeRechaza()
    {
        // Dentro del rectángulo pero con coordenada de cliente negativa (offset 38,59).
        var r = Correr("bash click.sh 400 20 Municipal", new Escenario(ListadoNormal()));

        Assert.Equal(4, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    [Fact]
    public void Click_PopupDeLaMismaAppAlFrente_Clickea()
    {
        var listado = Listado([VentanaApp], "F|11339494|18112|msrdc");

        var r = Correr("bash click.sh 400 300 Municipal", new Escenario(listado));

        Assert.True(r.ExitCode == 0, r.Salida);
        Assert.Contains("click 1", r.RastroX);
        Assert.DoesNotContain("OP=foco", r.RastroPs);
    }

    [Fact]
    public void Click_ElFocoX11EsDeOtraApp_NoClickea()
    {
        var listado = Listado([VentanaApp], "F|9999|18112|msrdc");

        var r = Correr("bash click.sh 400 300 Municipal",
            new Escenario(listado, XdotoolFocusPid: "77777"));

        Assert.Equal(5, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    [Fact]
    public void Click_VariasVentanasX11DeProcesosDistintos_NoClickea()
    {
        // search devuelve dos ids con PIDs distintos: no se adivina.
        var r = Correr("bash click.sh 400 300 Municipal",
            new Escenario(ListadoNormal(), XdotoolSearchId: "6291471\n6291999"));

        Assert.NotEqual(0, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    [Fact]
    public void Click_TituloVacio_FallaSinHacerNada()
    {
        var r = Correr("bash click.sh 400 300 ''", new Escenario(ListadoNormal()));

        Assert.Equal(1, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    // ===================== escribir.sh =====================

    [Fact]
    public void Escribir_VentanaPresenteYAlFrente_EnvíaElTexto()
    {
        var r = Correr("bash escribir.sh 'admin' Municipal", new Escenario(ListadoNormal()));

        Assert.True(r.ExitCode == 0, r.Salida);
        Assert.Contains("OP=enviar", r.RastroPs);
    }

    [Fact]
    public void Escribir_VentanaAusente_FallaSinEnviarNada()
    {
        var r = Correr("bash escribir.sh 'admin' Municipal", new Escenario(Listado([], "F|1|1|explorer")));

        Assert.Equal(2, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    [Fact]
    public void Escribir_VentanaPresentePeroOtraAppAlFrente_FallaSinEnviarNada()
    {
        // El caso que motivó el guardián: SendKeys escribe en lo que tenga el foco.
        var listado = Listado([VentanaApp], "F|2164642|19428|brave");

        var r = Correr("bash escribir.sh 'Admin1234' Municipal", new Escenario(listado));

        Assert.Equal(5, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    [Fact]
    public void Escribir_ElPrimerPlanoCambiaMientrasTipea_PropagaElCorte()
    {
        // El chequeo por carácter vive en el proceso PowerShell (exit 5 = primer plano perdido).
        var r = Correr("bash escribir.sh 'admin' Municipal", new Escenario(ListadoNormal(), EnviarRc: 5));

        Assert.Equal(5, r.ExitCode);
        Assert.Contains("se cortó el tecleo", r.Salida);
    }

    [Fact]
    public void Escribir_TituloVacio_FallaSinHacerNada()
    {
        var r = Correr("bash escribir.sh 'admin' ''", new Escenario(ListadoNormal()));

        Assert.Equal(1, r.ExitCode);
        AfirmarNadaEnviado(r);
        Assert.DoesNotContain("OP=listar", r.RastroPs);
    }

    [Fact]
    public void Escribir_RectanguloSospechoso_FallaSinEnviarNada()
    {
        var listado = Listado(
            ["W|4392688|18112|msrdc|0|0|1920|1080|0|Gestión Municipal (Ubuntu)"], ForegroundApp);

        var r = Correr("bash escribir.sh 'admin' Municipal", new Escenario(listado));

        Assert.Equal(4, r.ExitCode);
        AfirmarNadaEnviado(r);
    }

    [Fact]
    public void Escribir_TextoConComillasYMetacaracteres_NoInyectaNadaEnPowershell()
    {
        // El texto viaja en base64: una comilla simple o un ';' no pueden cerrar el literal.
        var r = Correr("bash escribir.sh \"a'; calc.exe; '\" Municipal", new Escenario(ListadoNormal()));

        Assert.True(r.ExitCode == 0, r.Salida);
        Assert.Contains("OP=enviar", r.RastroPs);
    }
}
