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

    private static readonly string[] IdsDelRunbook =
    [
        "P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8", "P9", "P10", "E1", "E2",
        "C0", "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10",
        "V1", "V2", "V3", "V4", "V5", "V6", "V7", "V8", "V9", "V10", "R-A", "R-B",
    ];

    public static TheoryData<string> CasosIdsDelRunbook()
    {
        var datos = new TheoryData<string>();
        foreach (var id in IdsDelRunbook) datos.Add(id);
        return datos;
    }

    /// <summary>
    /// Cada paso de la spec tiene que aparecer como encabezado o como casilla (`- [ ] **C5.**`):
    /// un paso que no está en el runbook es un paso que el operador se saltea el día del corte.
    /// </summary>
    [Theory]
    [MemberData(nameof(CasosIdsDelRunbook))]
    public void Runbook_CubreCadaPasoDeLaSpec(string id)
    {
        var texto = Texto(Runbook);

        var patron = $@"^(#{{1,4}}\s+|- \[ \]\s+)\**{Regex.Escape(id)}(?![0-9A-Za-z])";
        Assert.True(Regex.IsMatch(texto, patron, RegexOptions.Multiline),
            $"{Runbook} no tiene un encabezado o una casilla para '{id}'.");
    }

    [Theory]
    [InlineData("deploy/restaurar-backup.sh")]
    [InlineData("deploy/volcado-final-vps.sh")]
    [InlineData("deploy/bajar-backup-vps.sh")]
    [InlineData("deploy/ensayo/ensayo-local.sh")]
    [InlineData("--verificar-post-arranque")]
    [InlineData("--recrear-base stockapp")]
    [InlineData("--dir-seguridad ~/stockapp-deploy/migracion")]
    [InlineData("STOCKAPP_ENV_LOCAL")]
    [InlineData("systemctl mask stockapp-api")]
    [InlineData("certbot renew --force-renewal")]
    [InlineData("sudo install -m 0755 deploy/tls/stockapp-tls-deploy-hook.sh /usr/local/sbin/")]
    [InlineData("--ensayo")]
    [InlineData("build/publicar-release.sh 0.5.3")]
    [InlineData("deploy/deploy-vps.sh 0.5.3 --sin-backup")]
    public void Runbook_MencionaElComandoExacto(string fragmento)
    {
        Assert.Contains(fragmento, Texto(Runbook));
    }

    /// <summary>
    /// Lo que se arrastra de las reviews de las Tasks 6 a 8: si el runbook no lo dice, el
    /// operador lo descubre el día del corte.
    /// </summary>
    [Theory]
    [InlineData("stockapp-api.service.congelado")]       // el congelamiento mueve la unit
    [InlineData("systemctl daemon-reload")]
    [InlineData("systemctl -q mask stockapp-api")]
    [InlineData("systemctl is-enabled stockapp-api")]     // tiene que dar masked
    [InlineData("systemctl unmask stockapp-api")]         // R-A
    [InlineData("FragmentPath")]
    [InlineData("Licencia__Habilitado=false")]
    [InlineData("LICENCIA_CLAVE_PUBLICA_BASE64")]
    [InlineData("SmartScreen")]
    [InlineData("14 días")]
    [InlineData("8080")]
    [InlineData("rm -rf <trabajo>")]                       // limpieza explícita de datos reales
    [InlineData("exit 9")]                                  // EnsayoCompleto con otro proceso de test
    [InlineData("ReconciliarDumpHuerfanosAsync")]          // por qué se limpian los dumps del ensayo
    [InlineData("VPS_ADMIN_PASSWORD")]
    [InlineData("pisa")]                                    // el .env pisa al entorno
    [InlineData("Lo que Martin tiene que cargar")]
    [InlineData("## Tiempos")]
    [InlineData("deploy/.env.vps-nuevo")]
    [InlineData("~/stockapp-deploy/.env")]
    [InlineData("migracion/corte-<fecha>/")]              // el corte no comparte directorio con E2
    [InlineData("exactamente el `<ts>` que imprimió C3")]
    [InlineData("fecha_utc")]
    [InlineData("-exec mv -t ~/stockapp-deploy/migracion/ensayo-e2-<fecha>/")]  // C2.b aparta los volcados de E2
    [InlineData("sudo install -d -o stockapp -g stockapp -m 750 /var/lib/stockapp/.local /var/lib/stockapp/.local/share /var/lib/stockapp/.local/share/StockApp")]
    [InlineData("PW=\"$PW\" jq -n --arg u '<usuario-real>' '{nombreUsuario:$u,contrasena:env.PW}'")]
    [InlineData("sudo systemctl stop stockapp-api` (si C7")]  // R-A: primero se detiene el nuevo
    [InlineData("Borrar, solo con la confirmación de Martin")]                      // post-corte: copias de producción
    [InlineData("~/stockapp-backups-vps/ensayo-e2-<fecha>/")]
    [InlineData("mv volcado-<ts>.dump volcado-<ts>.manifiesto ~/stockapp-backups-vps/corte-<fecha>/")]  // D+14: el dump final se archiva, no se borra
    [InlineData("sha256sum -c /tmp/sha-corte-<fecha>.txt")]
    [InlineData("omandos se corren desde la raíz del repo")]
    [InlineData("contenedores-viejo-<fecha>.txt")]         // C2.c guarda, V10 compara
    [InlineData("no renueves certificados ni reinicies el VPS nuevo")]
    public void Runbook_ReflejaLosHallazgosDeLasReviews(string fragmento)
    {
        Assert.Contains(fragmento, Texto(Runbook));
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

    [Fact]
    public void Runbook_AdviertePorQueDeployVpsNoSeCorreContraElViejo()
    {
        var texto = Texto(Runbook);

        Assert.Contains("/tmp", texto);
        Assert.Matches(@"(?is)deploy-vps\.sh.{0,600}NUNCA.{0,100}VPS viejo", texto);
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
    public void Procedimientos_Paso7ApuntaARestaurarBackup()
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
        Assert.Contains("MIGRACION-VPS.md", paso7.Value);
        Assert.DoesNotContain("siguiendo el respaldo bajado en el Procedimiento 1, con\n   ayuda del proveedor", paso7.Value);
    }
}
