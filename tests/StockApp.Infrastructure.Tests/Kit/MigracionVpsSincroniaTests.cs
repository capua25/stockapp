using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes estáticos (leen el TEXTO, comentarios incluidos) de los scripts de la migración de
/// datos al VPS nuevo. Custodian dos cosas que un test de ejecución no ve:
/// - ningún host, IP ni dominio cableado: los parámetros salen del .env del operador, y un host
///   horneado en un script haría que el corte apunte al VPS equivocado;
/// - ningún comando de alcance global: el VPS viejo es COMPARTIDO con otra aplicación.
/// </summary>
public class MigracionVpsSincroniaTests
{
    private static string RaizRepo([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
    }

    /// <summary>Scripts nuevos de la migración (rutas relativas a la raíz del repo).</summary>
    public static readonly string[] ScriptsNuevos =
    [
        "deploy/restaurar-backup.sh",
        "deploy/volcado-final-vps.sh",
        "deploy/bajar-backup-vps.sh",
        "deploy/ensayo/ensayo-local.sh",
        "deploy/ensayo/shims/ssh",
        "deploy/ensayo/shims/scp",
        "deploy/ensayo/shims/journalctl",
        "deploy/ensayo/shims/sudo",
        "deploy/ensayo/shims/systemctl",
        "deploy/lib/orquestacion.sh",
        Migrar,
        HookTls,
    ];

    /// <summary>
    /// El orquestador del corte: opera el VPS viejo (compartido con otra aplicación), así que
    /// entra en <see cref="ScriptsNuevos"/> con TODOS los tokens prohibidos, comentarios incluidos.
    /// </summary>
    private const string Migrar = "deploy/migrar-vps.sh";

    /// <summary>
    /// El hook de certbot documenta en su encabezado cómo se registra (`certbot certonly ...
    /// --deploy-hook`), así que queda exento SOLO del token "certbot". El resto de la lista
    /// aplica igual: la prohibición es de ejecutar certbot global, no de nombrar el hook propio.
    /// </summary>
    private const string HookTls = "deploy/tls/stockapp-tls-deploy-hook.sh";

    public static TheoryData<string> CasosScriptsNuevos()
    {
        var datos = new TheoryData<string>();
        foreach (var s in ScriptsNuevos) datos.Add(s);
        return datos;
    }

    private const string Runbook = "deploy/MIGRACION-VPS.md";

    /// <summary>
    /// preparar-vps-nuevo.sh opera sobre el VPS NUEVO y dedicado: instala paquetes, maneja el
    /// firewall y emite el certificado, así que no entra en <see cref="ScriptsNuevos"/> (que
    /// prohíbe esos comandos) sino en su propio test de exención. Hosts cableados, en cambio,
    /// tampoco puede tener.
    /// </summary>
    private const string Preparar = "deploy/preparar-vps-nuevo.sh";

    /// <summary>Los scripts nuevos, preparar y el runbook: ninguno puede traer un host cableado.</summary>
    public static TheoryData<string> CasosSinHosts()
    {
        var datos = CasosScriptsNuevos();
        datos.Add(Preparar);
        datos.Add(Runbook);
        return datos;
    }

    private static string Texto(string relativo) => File.ReadAllText(Path.Combine(RaizRepo(), relativo));

    private static readonly Regex Ipv4 = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");
    private static readonly Regex Dominio = new(@"\b[a-z0-9-]+(\.[a-z0-9-]+)*\.(dev|com|net|org|uy|ar|io|app)\b");

    [Theory]
    [MemberData(nameof(CasosSinHosts))]
    public void ScriptsNuevos_SinHostsNiIpsCableados(string script)
    {
        var texto = Texto(script);

        var ips = Ipv4.Matches(texto).Select(m => m.Value)
            .Where(ip => ip is not "127.0.0.1" and not "0.0.0.0").ToList();
        Assert.True(ips.Count == 0, $"{script} tiene IPs cableadas: {string.Join(", ", ips)}");

        // Única excepción, explícita y mínima (ruling F8): docker.io es el NOMBRE DEL PAQUETE apt
        // que pide el runbook (P4), no un host del VPS. Sin comodines.
        var dominios = Dominio.Matches(texto).Select(m => m.Value).Where(d => d != "docker.io").ToList();
        Assert.True(dominios.Count == 0, $"{script} tiene dominios cableados: {string.Join(", ", dominios)}");
    }

    [Theory]
    [MemberData(nameof(CasosScriptsNuevos))]
    public void ScriptsNuevos_SinComandosGlobalesProhibidos(string script)
    {
        // El hook de certbot documenta en su encabezado cómo se registra (`certbot certonly ...
        // --deploy-hook`): queda exento SOLO del token "certbot".
        AfirmarSinComandosGlobales(Texto(script), script == HookTls ? ["certbot"] : []);
    }

    /// <summary>
    /// preparar-vps-nuevo.sh está exento de `ufw` y de `certbot` (es su trabajo sobre un VPS
    /// dedicado) y de NADA más: el resto de la lista aplica igual, y además no puede resetear ni
    /// borrar reglas del firewall, actualizar el sistema ni nombrar el proxy de la otra app.
    /// </summary>
    [Fact]
    public void Preparar_ExentoSoloDeUfwCertbotYAptGet()
    {
        var texto = Texto(Preparar);

        AfirmarSinComandosGlobales(texto, "ufw", "certbot");
        Assert.DoesNotContain("ufw reset", texto);
        Assert.DoesNotContain("ufw delete", texto);
        Assert.DoesNotContain("apt-get upgrade", texto);
        Assert.DoesNotContain("nginx", texto);
    }

    /// <summary>
    /// El conocido bypass de `ufw` por Docker (Docker publica los puertos saltando las reglas)
    /// no expone Postgres solo si el compose lo publica en loopback: cualquier puerto sin
    /// `127.0.0.1:` lo dejaría abierto a Internet.
    /// </summary>
    [Fact]
    public void Compose_PostgresSoloEnLoopback()
    {
        var texto = Texto("deploy/docker-compose.postgres.yml");
        var bloque = Regex.Match(texto, @"(?m)^\s*ports:\s*\n((?:\s+-\s+.+\n)+)");
        Assert.True(bloque.Success, "el compose ya no declara `ports:`");

        var puertos = bloque.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimStart('-').Trim().Trim('"', '\'')).ToList();
        Assert.NotEmpty(puertos);
        Assert.All(puertos, p => Assert.StartsWith("127.0.0.1:", p));
        Assert.Single(Regex.Matches(texto, @"(?m)^\s*ports:"));
    }

    /// <summary>
    /// El paso 6 de preparar usa la MISMA consulta de "base vacía" que restaurar-backup.sh: si una
    /// cambia y la otra no, preparar aprobaría una base que restaurar rechaza (o al revés).
    /// </summary>
    [Fact]
    public void Preparar_ConsultaDeBaseVacia_IgualALaDeRestaurar()
    {
        static string Normalizar(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();
        var restaurar = Regex.Match(Texto("deploy/restaurar-backup.sh"),
            "consultar \"\\$BASE\" \"(SELECT\\s+\\(SELECT count.*?;)\" \"si la base está vac", RegexOptions.Singleline);
        var preparar = Regex.Match(Texto(Preparar),
            @"<<'SQL'\s*\n(SELECT\s+\(SELECT count.*?;)\s*\nSQL", RegexOptions.Singleline);

        Assert.True(restaurar.Success, "no encontré la consulta de base vacía en restaurar-backup.sh");
        Assert.True(preparar.Success, "no encontré la consulta de base vacía en preparar-vps-nuevo.sh");
        Assert.Equal(Normalizar(restaurar.Groups[1].Value), Normalizar(preparar.Groups[1].Value));
    }

    /// <summary>
    /// La lista de prohibidos, en un solo lugar. <paramref name="exentos"/> son los tokens que
    /// ese script puede nombrar (el hook, "certbot"; preparar, "ufw" y "certbot"); todo lo demás
    /// se afirma ausente, comentarios incluidos.
    /// </summary>
    private static void AfirmarSinComandosGlobales(string texto, params string[] exentos)
    {
        void Ausente(string token)
        {
            if (!exentos.Contains(token)) Assert.DoesNotContain(token, texto);
        }

        // La lista de DeployVpsTests.ElScript_NuncaContieneUnComandoDeAlcanceGlobalProhibido...
        Ausente("docker system prune");
        Ausente("image prune");
        Ausente("volume prune");
        Ausente("container prune");
        Ausente("systemctl restart docker");
        Ausente("systemctl stop docker");
        Ausente("ufw");
        Ausente("apt-get upgrade");
        Ausente("dist-upgrade");
        Ausente("autoremove");
        Ausente("reboot");
        Ausente("shutdown");
        Assert.DoesNotMatch(@"docker\s+stop\s+\$\(", texto);
        Ausente("pkill");
        Assert.DoesNotMatch(@"docker compose down(?!.*(-f|-p))", texto);
        // ...más lo propio de la migración: ni el proxy ni los certificados de la otra app, y
        // ningún borrado recursivo sobre una ruta que sale de una variable.
        Ausente("nginx");
        Ausente("certbot");
        Ausente("rm -rf \"$");
    }

    // ---------- Tablas de códigos de los bloques, copiadas en el encabezado del orquestador ----------

    /// <summary>
    /// La tabla de códigos de un archivo: desde `# Códigos de salida (estables):` hasta la primera
    /// línea `#` vacía o que empiece con `# =` (sin incluirla). Es la misma regla para el bloque y
    /// para la copia, así que una diferencia entre las dos es una diferencia real.
    /// </summary>
    private static List<string> TablaDeCodigos(IEnumerable<string> lineas)
    {
        var tabla = new List<string>();
        var dentro = false;
        foreach (var l in lineas)
        {
            if (!dentro)
            {
                if (l == "# Códigos de salida (estables):")
                {
                    dentro = true;
                    tabla.Add(l);
                }

                continue;
            }

            if (l == "#" || l.StartsWith("# =")) break;
            tabla.Add(l);
        }

        return tabla;
    }

    private static List<string> CopiaEnElOrquestador(string orquestador, string bloque)
    {
        var lineas = orquestador.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var ini = lineas.IndexOf($"# >>> CODIGOS {bloque}");
        var fin = lineas.IndexOf($"# <<< CODIGOS {bloque}");
        Assert.True(ini >= 0 && fin > ini, $"{Migrar} no tiene el par `# >>> CODIGOS {bloque}` / `# <<< CODIGOS {bloque}`.");
        return lineas.GetRange(ini + 1, fin - ini - 1);
    }

    /// <summary>
    /// La tabla de códigos de volcado-final-vps.sh y de restaurar-backup.sh está copiada, sin
    /// cambios, en el encabezado de migrar-vps.sh: el orquestador imprime "bloque X salió con N"
    /// junto con la fila de esa tabla, y si el bloque cambia un código sin que cambie la copia, el
    /// operador leería una fila falsa.
    /// </summary>
    [Theory]
    [InlineData("deploy/volcado-final-vps.sh")]
    [InlineData("deploy/restaurar-backup.sh")]
    public void Orquestadores_TablasDeCodigosIgualesALasDeLosBloques(string bloque)
    {
        var delBloque = TablaDeCodigos(Texto(bloque).Split('\n').Select(l => l.TrimEnd('\r')));
        Assert.True(delBloque.Count > 3, $"no encontré la tabla de códigos de {bloque}.");

        var copia = CopiaEnElOrquestador(Texto(Migrar), bloque);

        Assert.Equal(delBloque, copia);
    }

    /// <summary>La propia tabla de migrar (spec 6.3.2) tiene una fila por cada código que el script usa.</summary>
    [Fact]
    public void Migrar_TablaDeCodigosPropia_TieneLosNueveCodigosDeLaSpec()
    {
        var lineas = Texto(Migrar).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var propia = TablaDeCodigos(lineas);
        foreach (var codigo in new[] { "0", "2", "3", "4", "5", "6", "7", "8", "9" })
            Assert.Contains(propia, l => Regex.IsMatch(l, $@"^#   {codigo}  \S"));
        Assert.DoesNotContain(propia, l => Regex.IsMatch(l, @"^#   [1]  "));
    }

    // ---------- Los 7 catálogos con Nombre único: tres copias, un solo conjunto ----------

    private static HashSet<string> ArrayBash(string relativo)
    {
        var m = Regex.Match(Texto(relativo), @"TABLAS_CON_NOMBRE_UNICO=\(([^)]*)\)");
        Assert.True(m.Success, $"No se encontró TABLAS_CON_NOMBRE_UNICO=( ... ) en {relativo}.");
        return m.Groups[1].Value.Split((char[])[' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    }

    /// <summary>
    /// restaurar-backup.sh es autocontenido (se copia solo al servidor), así que el array de
    /// catálogos queda triplicado: la migración en C#, deploy-vps.sh y restaurar-backup.sh. Si
    /// alguno agrega o saca un catálogo sin los otros, este test se pone rojo.
    /// </summary>
    [Fact]
    public void CatalogosConNombreUnico_IgualesEnCSharpDeployVpsYRestaurar()
    {
        var sqlCSharp = StockApp.Infrastructure.Migrations.NormalizacionCatalogosSql.GuardiaColisionesDeNombre;
        var arrayCSharp = Regex.Match(sqlCSharp, @"ARRAY\[([^\]]*)\]");
        Assert.True(arrayCSharp.Success, "No se encontró ARRAY[ ... ] en NormalizacionCatalogosSql.GuardiaColisionesDeNombre.");
        var csharp = Regex.Matches(arrayCSharp.Groups[1].Value, @"'([^']+)'").Select(x => x.Groups[1].Value).ToHashSet();

        Assert.Equal(7, csharp.Count);
        Assert.True(csharp.SetEquals(ArrayBash("deploy/deploy-vps.sh")),
            $"deploy-vps.sh difiere de C#: [{string.Join(", ", ArrayBash("deploy/deploy-vps.sh"))}]");
        Assert.True(csharp.SetEquals(ArrayBash("deploy/restaurar-backup.sh")),
            $"restaurar-backup.sh difiere de C#: [{string.Join(", ", ArrayBash("deploy/restaurar-backup.sh"))}]");
    }

    // ---------- La consulta de conteos: una sola, en el volcado y en la restauración ----------

    private static string BloqueEntreMarcadores(string relativo)
    {
        var lineas = Texto(relativo).Split('\n');
        var inicio = Array.FindIndex(lineas, l => l.Trim() == "# >>> SQL-CONTEOS");
        var fin = Array.FindIndex(lineas, l => l.Trim() == "# <<< SQL-CONTEOS");
        Assert.True(inicio >= 0 && fin > inicio, $"No se encontraron los marcadores SQL-CONTEOS en {relativo}.");
        return string.Join('\n', lineas[(inicio + 1)..fin]);
    }

    /// <summary>
    /// El manifiesto lleva los conteos que calcula volcado-final-vps.sh, y restaurar-backup.sh los
    /// compara contra los que calcula él. Si las dos consultas divergen (otro filtro, otro orden,
    /// otra forma de contar), la comparación deja de medir lo mismo: el texto entre los
    /// marcadores tiene que ser IDÉNTICO.
    /// </summary>
    [Fact]
    public void SqlDeConteos_IdenticaEnVolcadoYRestaurar()
    {
        var volcado = BloqueEntreMarcadores("deploy/volcado-final-vps.sh");
        var restaurar = BloqueEntreMarcadores("deploy/restaurar-backup.sh");

        Assert.Contains("information_schema.tables", restaurar);
        Assert.Equal(restaurar, volcado);
    }



    // ---------- Runbook y documentación ----------

    /// <summary>
    /// Los títulos de las 10 secciones de spec 6.7, en orden. El encabezado es `## N. Título`; el
    /// título del documento real puede seguir con ": ..." después de estas palabras.
    /// </summary>
    private static readonly string[] TitulosDeLasSecciones =
    [
        "Qué es esto", "Prerrequisitos de Martin", "Preparar el VPS nuevo", "Ensayo", "Día D",
        "Si algo sale mal", "R-B", "Desastre", "D+14", "Tiempos y E1",
    ];

    public static TheoryData<int> CasosSecciones()
    {
        var datos = new TheoryData<int>();
        for (var n = 1; n <= TitulosDeLasSecciones.Length; n++) datos.Add(n);
        return datos;
    }

    /// <summary>La sección <paramref name="n"/> del runbook, de su encabezado `## n.` al siguiente `## m.`.</summary>
    private static string Seccion(string texto, int n)
    {
        var m = Regex.Match(texto, $@"(?ms)^## {n}\. .*?(?=^## \d+\. |\z)");
        Assert.True(m.Success, $"{Runbook} no tiene la sección '## {n}. ...'.");
        return m.Value;
    }

    private static string RunbookSinSeccion(int n)
    {
        var texto = Texto(Runbook);
        return texto.Replace(Seccion(texto, n), "");
    }

    [Fact]
    public void Runbook_TieneLasDiezSeccionesEnOrden()
    {
        var encabezados = Regex.Matches(Texto(Runbook), @"(?m)^## (\d+)\. (.+?)\s*$");

        Assert.Equal(Enumerable.Range(1, TitulosDeLasSecciones.Length).Select(n => n.ToString()),
            encabezados.Select(m => m.Groups[1].Value));
        for (var i = 0; i < TitulosDeLasSecciones.Length; i++)
            Assert.StartsWith(TitulosDeLasSecciones[i], encabezados[i].Groups[2].Value);
    }

    /// <summary>
    /// Cada sección de la spec tiene que estar y no estar vacía: una sección que desaparece es un paso
    /// que el operador se saltea el día del corte.
    /// </summary>
    [Theory]
    [MemberData(nameof(CasosSecciones))]
    public void Runbook_CadaSeccionTieneContenido(int n)
    {
        var cuerpo = Seccion(Texto(Runbook), n).Split('\n').Skip(1);
        Assert.True(cuerpo.Count(l => l.Trim().Length > 0) >= 3, $"la sección {n} del runbook está casi vacía.");
    }

    /// <summary>
    /// Los dos ids que sobreviven del runbook manual (R-B y E1) siguen siendo encabezados: el código
    /// de migrar-vps.sh apunta a "la sección R-B de deploy/MIGRACION-VPS.md".
    /// </summary>
    [Theory]
    [InlineData("R-B")]
    [InlineData("E1")]
    public void Runbook_ConservaLosIdsQueElCodigoCita(string id)
    {
        var patron = $@"^#{{1,4}}\s+(?:\d+\.\s+)?.*(?<![0-9A-Za-z-]){Regex.Escape(id)}(?![0-9A-Za-z])";
        Assert.True(Regex.IsMatch(Texto(Runbook), patron, RegexOptions.Multiline),
            $"{Runbook} no tiene un encabezado con '{id}'.");
    }

    [Theory]
    [InlineData("STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3")]
    [InlineData("--probar-renovacion")]
    [InlineData("deploy/migrar-vps.sh --ensayo")]
    [InlineData("CONGELAR")]
    [InlineData("--continuar")]
    [InlineData("--revertir")]
    [InlineData("--desde-backup")]
    [InlineData("--post-corte")]
    [InlineData("scripts/gui-verificacion")]
    [InlineData("SmartScreen")]
    [InlineData("14 días")]
    [InlineData("STOCKAPP_ENV_LOCAL")]
    [InlineData("VPS_HOST")]
    [InlineData("VPS_USER")]
    [InlineData("VPS_SSH_PORT")]
    [InlineData("VPS_ADMIN_USER")]
    [InlineData("VPS_ADMIN_PASSWORD")]
    [InlineData("deploy/.env.vps-nuevo")]
    [InlineData("~/stockapp-deploy/.env")]
    public void Runbook_MencionaElComandoExacto(string fragmento)
    {
        Assert.Contains(fragmento, Texto(Runbook));
    }

    /// <summary>
    /// Lo que arrastran las rulings y las reviews de las Tasks 3 a 14: si el runbook no lo dice, el
    /// operador lo descubre el día del corte. Cada fragmento nombra el hallazgo que custodia.
    /// </summary>
    [Theory]
    [InlineData("--register-unsafely-without-email")]                 // R25: la cuenta ACME nace sin email
    [InlineData("sudo certbot update_account -m <mail>")]             // R25: cómo se agrega después, sin reemitir
    [InlineData("certbot reconfigure")]                               // R28: cert vigente con otro hook
    [InlineData("300 s")]                                             // R35: tolerancia de A11
    [InlineData("desde el panel del proveedor")]                      // R46: nuevo inalcanzable
    [InlineData("STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3 --verificar")]  // R47
    [InlineData("cupo de Let's Encrypt")]                             // T6: re-correr --probar-renovacion gasta cupo
    [InlineData("es un archivo LOCAL")]                               // T12: --env-viejo de --desde-backup
    [InlineData("pisa")]                                              // el .env del operador pisa al entorno
    [InlineData("Licencia__Habilitado=false")]
    [InlineData("LICENCIA_CLAVE_PUBLICA_BASE64")]
    [InlineData("overrides-api-env.txt")]
    [InlineData("STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/deploy-vps.sh 0.5.3 --sin-backup")]  // los overrides llegan a api.env con install.sh
    [InlineData("sudo grep -c '^<NOMBRE-DEL-OVERRIDE>=' /etc/stockapp-api/api.env")]
    [InlineData("Más información")]                                   // SmartScreen
    [InlineData("no es fail-closed")]                                 // capturar.sh con WSLg caído
    [InlineData("~/stockapp-backups-vps/corridas/corte-<ts>")]
    [InlineData("~/stockapp-backups-vps/corte-<ts>/")]                // D+14: el dump del corte se ARCHIVA
    [InlineData("--aun-con-escrituras")]
    [InlineData("--reintentar-restauracion")]
    [InlineData("systemctl unmask stockapp-api")]                     // R-A a mano, si --revertir no puede correr
    [InlineData("stockapp-api.service.congelado")]
    [InlineData("stockapp-ufw-reversa")]                              // firewall: reversa automática
    public void Runbook_ReflejaLosHallazgosDeLasReviews(string fragmento)
    {
        Assert.Contains(fragmento, Texto(Runbook));
    }

    /// <summary>
    /// Spec 8.3: lo que los fakes del arnés no pueden probar y hay que mirar la primera vez contra los
    /// servidores reales. Vive en la sección del ensayo, donde el operador lo va a ver.
    /// </summary>
    [Theory]
    [InlineData("dry-run")]                // certbot reconfigure contra staging
    [InlineData("daemon-reload")]          // antes del start al devolver la unit
    [InlineData("stockapp-ufw-reversa")]   // systemd borra el timer transitorio
    [InlineData("SHA256")]                 // el feed de Velopack
    [InlineData("sin email")]              // el texto real de certbot
    [InlineData("docker ps")]              // el del VPS compartido, real
    public void Runbook_Ensayo_ListaLoQueLosFakesNoCubren(string fragmento)
    {
        var s4 = Seccion(Texto(Runbook), 4);
        var i = s4.IndexOf("Qué mirar la primera vez", StringComparison.Ordinal);
        Assert.True(i >= 0, "la sección 4 no tiene 'Qué mirar la primera vez'.");
        Assert.Contains(fragmento, s4[i..]);
    }

    /// <summary>
    /// Para cada script, la tabla de la sección 6 tiene las MISMAS filas (código y texto) que la del
    /// encabezado del script, más una columna de qué hacer: el operador lee la tabla del runbook con
    /// producción caída, y una fila que difiere del código es una fila falsa.
    /// </summary>
    [Theory]
    [InlineData(Preparar, "preparar-vps-nuevo.sh")]
    [InlineData(Migrar, "migrar-vps.sh")]
    public void Runbook_TablaDeCodigosDeLosOrquestadores(string script, string nombre)
    {
        var esperado = TablaDeCodigos(Texto(script).Split('\n').Select(l => l.TrimEnd('\r')))
            .Select(l => Regex.Match(l, @"^#   (\d)  (.+)$"))
            .Where(m => m.Success)
            .Select(m => (Codigo: m.Groups[1].Value, Texto: m.Groups[2].Value))
            .ToList();
        Assert.True(esperado.Count >= 7, $"no encontré la tabla de códigos de {script}.");

        var sub = Regex.Match(Seccion(Texto(Runbook), 6),
            $@"(?ms)^### `?{Regex.Escape(nombre)}`?.*?(?=^### |\z)");
        Assert.True(sub.Success, $"la sección 6 no tiene el subtítulo '### {nombre}'.");
        var filas = Regex.Matches(sub.Value, @"(?m)^\| (\d) \| (.+?) \| (.+?) \|\s*$")
            .Select(m => (Codigo: m.Groups[1].Value, Texto: m.Groups[2].Value, Accion: m.Groups[3].Value))
            .ToList();

        Assert.Equal(esperado.Select(e => e.Codigo), filas.Select(f => f.Codigo));
        Assert.Equal(esperado.Select(e => e.Texto), filas.Select(f => f.Texto));
        Assert.All(filas, f => Assert.False(string.IsNullOrWhiteSpace(f.Accion), $"código {f.Codigo} sin 'qué hacer'."));
    }

    /// <summary>
    /// Todo `--flag` de un comando de migrar-vps.sh o de preparar-vps-nuevo.sh que el runbook pone en
    /// un bloque de código existe en ese script: un flag mal escrito se descubre con producción caída.
    /// </summary>
    [Fact]
    public void Runbook_LosFlagsDeLosComandosExistenEnLosScripts()
    {
        var scripts = new[] { Migrar, Preparar };
        var textos = scripts.ToDictionary(s => s, Texto);
        var enBloque = false;
        var revisados = 0;
        foreach (var linea in Texto(Runbook).Split('\n'))
        {
            if (linea.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                enBloque = !enBloque;
                continue;
            }

            if (!enBloque) continue;
            foreach (var s in scripts.Where(s => linea.Contains(s)))
            {
                foreach (Match f in Regex.Matches(linea, @"(?<![A-Za-z0-9-])--[a-z][a-z-]*"))
                {
                    revisados++;
                    Assert.True(Regex.IsMatch(textos[s], $@"(?<![A-Za-z0-9-]){Regex.Escape(f.Value)}(?![A-Za-z0-9-])"),
                        $"el runbook usa '{f.Value}' con {s} y el script no lo conoce: {linea.Trim()}");
                }
            }
        }

        Assert.True(revisados >= 8, $"solo revisé {revisados} flags: ¿se rompió el formato de los bloques de código?");
    }

    [Fact]
    public void Runbook_EnsayoDelCorteYRevertirAntesDelDiaD()
    {
        var s4 = Seccion(Texto(Runbook), 4);

        Assert.Contains("deploy/migrar-vps.sh --ensayo", s4);
        Assert.Matches(@"(?is)ensayo del corte completo.{0,800}deploy/migrar-vps\.sh --revertir", s4);
        Assert.Matches(@"(?i)antes del día D", s4);
    }

    [Fact]
    public void Runbook_OffSiteEsElProximoProyecto()
    {
        // Sin saltos de línea: el texto del runbook se re-envuelve y la frase no puede depender de dónde corta.
        var s8 = Regex.Replace(Seccion(Texto(Runbook), 8), @"\s+", " ");

        Assert.Contains("el backup off-site programado es el próximo proyecto", s8);
        Assert.Contains("último dump que esté fuera del servidor", s8);
    }

    [Fact]
    public void Runbook_DesastreEsPrepararMasDesdeBackup()
    {
        var s8 = Seccion(Texto(Runbook), 8);

        Assert.Contains("deploy/preparar-vps-nuevo.sh --version 0.5.3", s8);
        Assert.Contains("deploy/migrar-vps.sh --desde-backup", s8);
        Assert.Contains("RESTAURAR", s8);
        Assert.Contains("--env-viejo", s8);
    }

    /// <summary>R48 a R52: lo que ofrece y lo que conserva --post-corte, y que el dump del corte se ARCHIVA.</summary>
    [Fact]
    public void Runbook_PostCorteArchivaElDumpYNoBorraLoQueNoEsDeLaMigracion()
    {
        var s9 = Seccion(Texto(Runbook), 9);

        Assert.Contains("deploy/migrar-vps.sh --post-corte", s9);
        Assert.Contains("14 días", s9);
        Assert.Contains("--antes-de-plazo", s9);
        Assert.Contains("BORRAR", s9);
        Assert.Matches(@"(?i)archiva", s9);
        Assert.Contains("se conserva", s9);
        Assert.Contains("migracion-<fecha>", s9);                 // R52
        Assert.Matches(@"(?is)la corrida del corte.{0,200}(no se borra|nunca)", s9);   // R49
        Assert.DoesNotMatch(@"(?m)^\s*rm\s", s9);                 // nada de rm copiable en el cierre
    }

    /// <summary>R47 y R46: qué se hace después de un --revertir, y qué cuando el VPS nuevo no contesta.</summary>
    [Fact]
    public void Runbook_RevertirExplicaLaVerificacionPosteriorYElNuevoInalcanzable()
    {
        var s6 = Seccion(Texto(Runbook), 6);

        Assert.Contains("deploy/migrar-vps.sh --revertir", s6);
        Assert.Contains("REVERTIR", s6);
        Assert.Contains("STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3 --verificar", s6);
        Assert.Matches(@"(?is)paso 10.{0,400}sale (con )?5|sale (con )?5.{0,400}paso 10", s6);
        Assert.Contains("desde el panel del proveedor", s6);
        Assert.Contains("R-B", s6);
    }

    /// <summary>
    /// Lo que era prosa manual (el congelamiento, la restauración, el volcado) ahora es código de
    /// migrar-vps.sh: el runbook ya no lo copia. Única excepción, la sección 7 (R-B), que la spec manda
    /// conservar tal cual: la migración inversa se hace a mano con las mismas piezas (R18).
    /// </summary>
    [Fact]
    public void Runbook_SinComandosManualesDeCorte()
    {
        var texto = RunbookSinSeccion(7);

        Assert.DoesNotContain("systemctl -q mask", texto);
        Assert.DoesNotContain("restaurar-backup.sh", texto);
        Assert.DoesNotContain("volcado-final-vps.sh", texto);
        Assert.DoesNotContain("/tmp/", texto);
    }

    /// <summary>R-B se conserva con su texto (spec 6.7.7), más una nota fuera del bloque sobre lo que cambió de nombre.</summary>
    [Theory]
    [InlineData("STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/volcado-final-vps.sh --salida ~/stockapp-backups-vps/inversa-<fecha>/")]
    [InlineData("`restaurar-backup.sh … --recrear-base stockapp` en el viejo")]
    [InlineData("El viejo sigue detenido y enmascarado")]
    [InlineData("C4 = paso 8 de `migrar-vps.sh`")]
    public void Runbook_RB_ConservaSuTexto(string fragmento)
    {
        Assert.Contains(fragmento, Seccion(Texto(Runbook), 7));
    }

    [Fact]
    public void Runbook_Tiempos_ConservaLaTablaYElResultadoDeE1()
    {
        var s10 = Seccion(Texto(Runbook), 10);

        Assert.Contains("| Paso | E1 (local) |", s10);
        Assert.Contains("**Resultado de E1 (2026-10-20): OK.**", s10);
        Assert.Contains("31 tablas, 150 filas, 25 migraciones", s10);
    }

    /// <summary>
    /// Minor de T12: el login de V5 y V8 usa VPS_ADMIN_USER (obligatorio, no cae a BOOTSTRAP_ADMIN_USER)
    /// y la plantilla del .env del operador no lo documentaba.
    /// </summary>
    [Fact]
    public void EnvExample_DocumentaVpsAdminUser()
    {
        var texto = Texto("deploy/.env.example");

        Assert.Contains("VPS_ADMIN_USER", texto);
        Assert.Contains("VPS_ADMIN_PASSWORD", texto);
        Assert.Contains("GestionarDiagnostico", texto);
    }

    [Fact]
    public void Runbook_NoPasaLaContrasenaPorArgvNiRestauraDelDirectorioDeE2()
    {
        var texto = Texto(Runbook);

        Assert.DoesNotContain("--arg p ", texto);
        Assert.DoesNotContain("rm -r ~/stockapp-backups-vps/migracion-<fecha>/volcado", texto);
        Assert.DoesNotMatch(@"rm [^\n]*volcado-<ts>", texto);
        Assert.DoesNotContain("ensayo-e2/", texto);
        Assert.DoesNotContain("migracion/volcado-<ts>", texto);
        Assert.DoesNotContain("CorridasBackup` puede crecer", texto);
    }

    [Fact]
    public void Procedimientos_RestaurarBackupSeCorreConSudo()
    {
        Assert.Contains("sudo ./restaurar-backup.sh backup_<fecha>.dump --env /etc/stockapp/.env", Texto("deploy/PROCEDIMIENTOS.md"));
    }

    [Fact]
    public void Runbook_NuncaUsaComandosGlobalesNiRmRfSobreVariables()
    {
        var texto = Texto(Runbook);

        Assert.DoesNotMatch(@"docker volume rm (?!stockapp_)", texto);
        Assert.DoesNotContain("docker compose down -v", texto);
        Assert.DoesNotContain("rm -rf $", texto);
        Assert.DoesNotContain("rm -rf \"$", texto);
        Assert.DoesNotContain("systemctl restart nginx", texto);
        Assert.DoesNotMatch(@"certbot delete(?!.*--cert-name)", texto);
        // Nada global de Docker, de systemd ni de certbot, ni siquiera "de referencia".
        Assert.DoesNotContain("docker system prune", texto);
        Assert.DoesNotContain("volume prune", texto);
        Assert.DoesNotContain("docker stop $(", texto);
        Assert.DoesNotContain("pkill", texto);
    }

    /// <summary>
    /// A14: deploy-vps.sh nunca se corre contra el VPS viejo (compartido; sin --sin-backup deja el
    /// backup en el directorio temporal del host). Se conserva el regex; ya no se exige la palabra "/tmp",
    /// porque la sección 7 es la única que puede traerla (R18).
    /// </summary>
    [Fact]
    public void Runbook_AdviertePorQueDeployVpsNoSeCorreContraElViejo()
    {
        var texto = Texto(Runbook);

        Assert.Matches(@"(?is)deploy-vps\.sh.{0,600}NUNCA.{0,100}VPS viejo", texto);
        Assert.Matches(@"(?is)directorio temporal del host", texto);
    }

    private static string Seccion8DeDeploy()
    {
        var texto = Texto("deploy/DEPLOY.md");
        var inicio = texto.IndexOf("## 8. Actualizar a una versión nueva", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "No se encontró la sección 8 de DEPLOY.md.");
        var fin = texto.IndexOf("\n## Rollback", inicio, StringComparison.Ordinal);
        Assert.True(fin > inicio, "No se encontró el fin de la sección 8 de DEPLOY.md.");
        return texto[inicio..fin];
    }

    [Fact]
    public void Deploy_Seccion8DocumentaStockappEnvLocalYElMarcadorPorHost()
    {
        var seccion = Seccion8DeDeploy();

        Assert.Contains("STOCKAPP_ENV_LOCAL", seccion);
        Assert.Contains(".ultimo-deploy-vps-commit-", seccion);
        Assert.Contains("MIGRACION-VPS.md", seccion);
    }

    [Fact]
    public void Deploy_Seccion8DocumentaLosDosOrquestadores()
    {
        var seccion = Seccion8DeDeploy();

        Assert.Contains("deploy/preparar-vps-nuevo.sh", seccion);
        Assert.Contains("deploy/migrar-vps.sh", seccion);
    }

    /// <summary>
    /// El paso 7 del Procedimiento 3 (se perdió la base) sigue restaurando con restaurar-backup.sh en el
    /// servidor del kit, y ahora apunta además al camino del VPS: preparar + --desde-backup. Ya no cita el
    /// "paso C5" del runbook manual, que dejó de existir.
    /// </summary>
    [Fact]
    public void Procedimientos_RecuperacionApuntaADesdeBackup()
    {
        var texto = Texto("deploy/PROCEDIMIENTOS.md");
        var inicio = texto.IndexOf("### Los pasos, en orden", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "No se encontró 'Los pasos, en orden' del Procedimiento 3.");
        var fin = texto.IndexOf("### El costo real de este escenario", inicio, StringComparison.Ordinal);
        Assert.True(fin > inicio);
        var pasos = texto[inicio..fin];

        var paso7 = Regex.Match(pasos, @"^7\.(?<t>.*?)(?=^\S|\z)", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(paso7.Success, "No se encontró el paso 7 del Procedimiento 3.");
        Assert.Contains("deploy/restaurar-backup.sh", paso7.Value);
        Assert.Contains("--recrear-base", paso7.Value);
        Assert.Contains("--env /etc/stockapp/.env", paso7.Value);
        Assert.Contains("deploy/preparar-vps-nuevo.sh", paso7.Value);
        Assert.Contains("deploy/migrar-vps.sh --desde-backup", paso7.Value);
        Assert.Contains("MIGRACION-VPS.md", paso7.Value);
        Assert.DoesNotContain("paso C5", paso7.Value);
        Assert.DoesNotContain("siguiendo el respaldo bajado en el Procedimiento 1, con\n   ayuda del proveedor", paso7.Value);
    }
}
