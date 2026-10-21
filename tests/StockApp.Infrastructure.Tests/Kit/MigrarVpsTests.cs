using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/migrar-vps.sh (spec 6.5.1 a 6.5.5 y 6.3): modos, corrida, identidad de los
/// hosts, pre-vuelo de solo lectura, confirmación, la secuencia del corte (pasos 3 a 13, A11) y la
/// verificación automática. Cada test corre el script REAL en un árbol
/// temporal con la forma del repo, con HOME temporal y SIN terminal de control (setsid): nunca
/// puede quedarse leyendo el teclado de quien corre la suite.
///
/// Los dos servidores son un <c>ssh</c> falso con estado en archivos (uno por host: machine-id, estado
/// de la unit, contenedores, tamaños de disco, login 200/401/403, claves del .env del nuevo) y un
/// <c>curl</c> falso del operador (la API pública del servidor nuevo) y un <c>scp</c> falso que copia al
/// home simulado del nuevo, donde el ssh falso ejecuta de verdad lo que opera sobre $HOME (mkdir,
/// chmod, sha256sum, find y la copia de restaurar-backup.sh). Los bloques (preparar, volcado,
/// restaurar) son ejecutables falsos del árbol que registran sus argumentos y el STOCKAPP_ENV_LOCAL
/// con el que los llamaron. Ningún test toca un servidor real ni el stockapp-pg de desarrollo.
///
/// El rastro marca cada comando remoto como LECTURA o ESCRITURA según una lista blanca de lecturas
/// del propio test (spec 5): lo que no está en la lista es una escritura, y el pre-vuelo no puede
/// tener ninguna. Todo comando al VPS viejo, que es compartido con otra aplicación, además tiene
/// que estar en la lista blanca estrecha del viejo.
/// </summary>
public class MigrarVpsTests : IDisposable
{
    private const string HostViejo = "vps-viejo.test.invalid";
    private const string HostNuevo = "vps-nuevo.test.invalid";
    private const string Contrasena = "Secreta-Admin-123-Zx9";
    private const string MachineViejo = "aaaaaaaa11111111aaaaaaaa11111111";
    private const string MachineNuevo = "bbbbbbbb22222222bbbbbbbb22222222";
    private const string Version = "1.2.3";

    // El corte completo, con todas las V automáticas OK: GO técnico.
    private const int ExitGo = 0;

    // --ensayo todavía termina después de la confirmación omitida, con este código (nunca 0: un 0
    // sería un GO técnico que no ocurrió). Su secuencia (volcado --ensayo, V10 invertido) es aparte.
    private const int ExitEnsayoSinSecuencia = 1;

    private const string TokenNuevo = "TOKEN-NUEVO-DEF";
    private const string TokenViejo = "TOKEN-FALSO-ABC";
    private const string UnitCongelada = "/etc/systemd/system/stockapp-api.service.congelado";

    private readonly string _dir;
    private readonly List<Process> _procesos = [];

    public MigrarVpsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "stockapp-migrar-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Tmp);
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(Path.Combine(Estado, "viejo"));
        Directory.CreateDirectory(Path.Combine(Estado, "nuevo"));
        ArmarArbol();
        ArmarStubs();
        EstadoInicialDeLosHosts();
        EscribirEnvs();
    }

    public void Dispose()
    {
        foreach (var p in _procesos)
        {
            try
            {
                if (!p.HasExited) p.Kill();
            }
            catch (InvalidOperationException)
            {
            }

            p.Dispose();
        }

        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ---------- Rutas ----------

    private string Raiz => Path.Combine(_dir, "repo");
    private string Home => Path.Combine(_dir, "home");
    private string Tmp => Path.Combine(_dir, "tmp");
    private string Bin => Path.Combine(_dir, "bin");
    private string Estado => Path.Combine(_dir, "estado");
    private string Rastro => Path.Combine(_dir, "rastro.txt");
    private string EnvViejo => Path.Combine(_dir, "env-viejo.txt");
    private string EnvNuevo => Path.Combine(_dir, "env-nuevo.txt");
    private string Script => Path.Combine(Raiz, "deploy", "migrar-vps.sh");
    private string CorridasDir => Path.Combine(Home, "stockapp-backups-vps", "corridas");
    private string LockFile => Path.Combine(Home, "stockapp-backups-vps", ".migracion.lock");

    private string Host(string lado, string archivo) => Path.Combine(Estado, lado, archivo);

    /// <summary>~/stockapp-deploy/migracion del servidor nuevo simulado.</summary>
    private string MigracionNuevo => Path.Combine(Estado, "nuevo", "home", "stockapp-deploy", "migracion");

    private void PonerViejo(string archivo, string contenido) => File.WriteAllText(Host("viejo", archivo), contenido);
    private void PonerNuevo(string archivo, string contenido) => File.WriteAllText(Host("nuevo", archivo), contenido);

    // ---------- Árbol, stubs y estado ----------

    private void ArmarArbol()
    {
        var repo = BashLimpio.RaizRepo();
        foreach (var rel in new[] { "deploy/migrar-vps.sh", "deploy/lib/orquestacion.sh" })
        {
            var destino = Path.Combine(Raiz, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.Copy(Path.Combine(repo, rel), destino, overwrite: true);
            File.SetUnixFileMode(destino, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Bloques falsos: registran argumentos y STOCKAPP_ENV_LOCAL, y salen con STUB_<NOMBRE>_EXIT.
        BashLimpio.EscribirEjecutable(Path.Combine(Raiz, "deploy/preparar-vps-nuevo.sh"),
            "#!/usr/bin/env bash\n" +
            "{ printf 'preparar\\tENV=%s\\tARGS:' \"${STOCKAPP_ENV_LOCAL-}\"; printf ' %s' \"$@\"; printf '\\n'; } >> \"$RASTRO\"\n" +
            "exit \"${STUB_PREPARAR_EXIT:-0}\"\n");
        BashLimpio.EscribirEjecutable(Path.Combine(Raiz, "deploy/volcado-final-vps.sh"), VolcadoFalso);
        // restaurar-backup.sh viaja por scp al VPS nuevo y se ejecuta ahí (el ssh falso corre la copia).
        BashLimpio.EscribirEjecutable(Path.Combine(Raiz, "deploy/restaurar-backup.sh"), RestaurarFalso);
    }

    private const string EntornoDeHijos =
        """printf '%s\tPW=%s\tTOKEN=%s\tcuerpo=%s\tlistado=%s\tCURL_CUERPO=%s\tORQ_SALIDA=%s\n' "$0" "${PW-}" "${TOKEN-}" "${cuerpo-}" "${listado-}" "${CURL_CUERPO-}" "${ORQ_SALIDA-}" >> "$ESTADO/entorno-hijos.txt" """;

    /// <summary>
    /// volcado-final-vps.sh falso. Como el real en modo final: detiene y enmascara la API vieja en
    /// los primeros segundos (STUB_VOLCADO_CONGELA=0 lo evita: "falló antes del stop") y, solo si sale
    /// con 0, deja en --salida el dump, su manifiesto (mismo formato que escribir_manifiesto), el tar
    /// del historial y los overrides. Registra si el estado de la corrida ya tenía congelado_utc.
    /// </summary>
    private const string VolcadoFalso = "#!/usr/bin/env bash\n" + EntornoDeHijos + "\n" + """
        salida=""
        args=("$@")
        while [[ $# -gt 0 ]]; do
            case "$1" in
                --salida) salida="$2"; shift 2 ;;
                *) shift ;;
            esac
        done
        previo=no
        if grep -q '^congelado_utc=' "${salida%/*}/estado" 2>/dev/null; then previo=si; fi
        { printf 'volcado\tENV=%s\tCONGELADO_PREVIO=%s\tARGS:' "${STOCKAPP_ENV_LOCAL-}" "$previo"; printf ' %s' "${args[@]}"; printf '\n'; } >> "$RASTRO"
        if [[ -n "${STUB_VOLCADO_FIFO-}" ]]; then
            echo "$PPID" > "$STUB_VOLCADO_PID"
            read -r _ < "$STUB_VOLCADO_FIFO" || :
        fi
        if [[ "${STUB_VOLCADO_CONGELA:-1}" == 1 ]]; then
            echo inactive > "$ESTADO/viejo/active"
            echo masked > "$ESTADO/viejo/enabled"
        fi
        # El directorio remoto lleva el nombre de la corrida, que se conoce recién al correr.
        if [[ -n "${STUB_PLANTAR_DIR_REMOTO-}" ]]; then
            d="${salida%/*}"
            mkdir -p "$STUB_PLANTAR_DIR_REMOTO/${d##*/}"
        fi
        if [[ -n "${STUB_VOLCADO_CONTENEDORES-}" ]]; then
            printf '%b' "$STUB_VOLCADO_CONTENEDORES" > "$ESTADO/viejo/contenedores"
        fi
        rc="${STUB_VOLCADO_EXIT:-0}"
        if [[ "$rc" != 0 ]]; then exit "$rc"; fi
        mkdir -p "$salida"
        ts="$(date -u +%Y%m%dT%H%M%SZ)"
        printf 'PGDMP-falso-%s\n' "$ts" > "$salida/volcado-$ts.dump"
        sha="$(sha256sum "$salida/volcado-$ts.dump" | cut -d' ' -f1)"
        # El reloj de pared de WSL salta para atrás bajo carga (observado: 11 a 14 s en plena suite): el
        # volcado real se hace siempre después del inicio de la corrida, así que el falso no fecha antes.
        fecha="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        inicio="$(sed -n 's/^inicio_utc=//p' "${salida%/*}/estado" 2>/dev/null)"
        if [[ -n "$inicio" && "$fecha" < "$inicio" ]]; then fecha="$inicio"; fi
        {
            echo "# manifiesto-migracion v1"
            echo "fecha_utc=${STUB_VOLCADO_FECHA:-$fecha}"
            echo "dump=${STUB_VOLCADO_DUMP:-volcado-$ts.dump}"
            echo "sha256=${STUB_VOLCADO_SHA:-$sha}"
            echo "ultima_migracion=20260101000000_Inicial"
            echo "[conteos]"
            printf 'Usuarios\t3\n'
        } > "$salida/volcado-$ts.manifiesto"
        tar -cf "$salida/backups-historial.tar" --files-from /dev/null
        : > "$salida/overrides-api-env.txt"
        # El extra se ordena DESPUÉS del de esta corrida: así solo lo detecta el control de "exactamente uno".
        if [[ -n "${STUB_VOLCADO_EXTRA-}" ]]; then printf 'otro\n' > "$salida/volcado-29991231T235959Z.dump"; fi
        """;

    /// <summary>restaurar-backup.sh falso: el paso 9 sale con STUB_RESTAURAR_EXIT y V6 (--verificar-post-arranque) con STUB_V6_EXIT.</summary>
    private const string RestaurarFalso = "#!/usr/bin/env bash\n" + """
        { printf 'restaurar\tENV=%s\tARGS:' "${STOCKAPP_ENV_LOCAL-}"; printf ' %s' "$@"; printf '\n'; } >> "$RASTRO"
        for a in "$@"; do
            if [[ "$a" == --verificar-post-arranque ]]; then exit "${STUB_V6_EXIT:-0}"; fi
        done
        exit "${STUB_RESTAURAR_EXIT:-0}"
        """;

    private const string SshFalso = "#!/usr/bin/env bash\n" + """
        # ssh falso con un host simulado por archivos. Dos servidores: el viejo y el nuevo.
        # Reproduce de ssh lo que importa: -p y -o se aceptan, lo que sigue a user@host es UN
        # comando, el stdin de la sesión llega al comando remoto y un host inalcanzable sale 255.
        # Reproduce de systemctl lo que importa: is-active imprime la palabra y sale 3 si no es
        # active; is-enabled sale 1 con masked/disabled. Lo que en el servidor opera sobre el HOME
        # remoto (mkdir, chmod, sha256sum, find de migracion/, la copia de restaurar-backup.sh) se
        # ejecuta DE VERDAD con HOME apuntando al home simulado del host.
        while [[ $# -gt 0 ]]; do
            case "$1" in
                -p) shift 2 ;;
                -o) shift 2 ;;
                *) break ;;
            esac
        done
        destino="$1"
        cmd="${2-}"
        host="${destino#*@}"
        case "$host" in
            vps-viejo.test.invalid) E="$ESTADO/viejo"; lado=viejo ;;
            vps-nuevo.test.invalid) E="$ESTADO/nuevo"; lado=nuevo ;;
            *)
                printf 'DESCONOCIDO\t%s\n' "$host" >> "$RASTRO"
                echo "ssh: Could not resolve hostname $host: Name or service not known" >&2
                exit 255
                ;;
        esac
        printf '%s\t%s\n' "$lado" "$cmd" >> "$RASTRO"
        """ + "\n" + EntornoDeHijos + "\n" + """
        if [[ -f "$E/ssh-caido" ]]; then
            echo "ssh: connect to host $host port 22: Connection refused" >&2
            exit 255
        fi
        entrada="$(cat)"
        df_linea() {
            printf 'Filesystem     1024-blocks     Used Available Capacity Mounted on\n'
            printf 'overlay           62914560 52428800 %s      84%% %s\n' "$1" "$2"
        }
        falla() { [[ -f "$E/falla-$1" ]]; }

        case "$cmd" in
            "cat "*"/etc/machine-id")
                if [[ -f "$E/machine-id" ]]; then cat "$E/machine-id"; else
                    echo "cat: /etc/machine-id: No such file or directory" >&2; exit 1
                fi
                ;;
            "sudo -n true")
                if [[ -f "$E/sin-sudo" ]]; then echo "sudo: a password is required" >&2; exit 1; fi
                ;;
            "systemctl is-active stockapp-api")
                estado="$(cat "$E/active")"
                echo "$estado"
                [[ "$estado" == active ]] || exit 3
                ;;
            "systemctl is-enabled stockapp-api")
                estado="$(cat "$E/enabled")"
                echo "$estado"
                [[ "$estado" == enabled ]] || exit 1
                ;;
            "systemctl show -p FragmentPath stockapp-api")
                echo "FragmentPath=$(cat "$E/fragment")"
                ;;
            "test -e "*)
                ruta="${cmd#test -e }"
                if grep -qxF -- "$ruta" "$E/existen"; then exit 0; else exit 1; fi
                ;;
            "docker ps --format '{{.Names}}'")
                cat "$E/contenedores"
                ;;
            "sudo -n systemctl stop stockapp-api")
                if falla stop; then echo "Failed to stop stockapp-api.service: Access denied" >&2; exit 1; fi
                if [[ ! -f "$E/no-se-detiene" ]]; then echo inactive > "$E/active"; fi
                ;;
            "sudo -n systemctl start stockapp-api")
                if falla start; then
                    echo "Job for stockapp-api.service failed because the control process exited with error code." >&2
                    echo failed > "$E/active"
                    exit 1
                fi
                cat "$E/al-arrancar" > "$E/active"
                ;;
            "sudo -n journalctl -u stockapp-api "*)
                cat "$E/journal" 2>/dev/null || :
                ;;
            "sudo -n test -d "*"/var/lib/stockapp/.local/share/StockApp/backups")
                if [[ -f "$E/backups-existe" ]]; then exit 0; else exit 1; fi
                ;;
            "sudo -n find "*"/var/lib/stockapp/.local/share/StockApp/backups -maxdepth 1 -type f \\( -name 'backup_*.dump' -o -name '*.tmp' \\) -print -delete")
                cat "$E/restos" 2>/dev/null || :
                : > "$E/restos"
                ;;
            "sudo -n tar -C "*"/var/lib/stockapp/.local/share/StockApp -xpf "*"/backups-historial.tar\"")
                if falla tar; then echo "tar: This does not look like a tar archive" >&2; exit 2; fi
                ;;
            "sudo -n chown -R stockapp:stockapp "*"/var/lib/stockapp/.local/share/StockApp/backups")
                ;;
            *"docker exec -i "*" psql -U "*)
                case "$entrada" in
                    *pg_database_size*)
                        if [[ -f "$E/dbsize-falla" ]]; then echo "psql: error: connection refused" >&2; exit 2; fi
                        cat "$E/dbsize"
                        ;;
                    *__EFMigrationsHistory*) cat "$E/migracion" ;;
                    *LogsAuditoria*) cat "$E/logs-auditoria" ;;
                    *) echo "stub: SQL no prevista" >&2; exit 99 ;;
                esac
                ;;
            *"docker exec "*" df -P /var/lib/postgresql/data")
                df_linea "$(cat "$E/df-volumen-kb")" /var/lib/postgresql/data
                ;;
            "df -P "*)
                df_linea "$(cat "$E/df-fs-kb")" /home/deploy
                ;;
            "sed -n "*"/stockapp-deploy/.env"*)
                cat "$E/env-claves"
                ;;
            "curl -fsS "*"/auth/login "*)
                printf '%s' "$entrada" > "$ESTADO/login-stdin.txt"
                modo="$(cat "$E/login")"
                case "$modo" in
                    ok)
                        if ! printf '%s' "$entrada" | jq -e '(.nombreUsuario | length > 0) and (.contrasena | length > 0)' >/dev/null 2>&1; then
                            echo 'curl: (22) The requested URL returned error: 400' >&2; exit 22
                        fi
                        echo '{"token":"TOKEN-FALSO-ABC","usuario":{"id":1}}'
                        ;;
                    fallo) echo 'curl: (7) Failed to connect to 127.0.0.1 port 8443' >&2; exit 7 ;;
                    *) echo "curl: (22) The requested URL returned error: $modo" >&2; exit 22 ;;
                esac
                ;;
            "curl -fsS "*"/backups -K -")
                printf '%s' "$entrada" > "$ESTADO/backups-stdin.txt"
                if [[ "$entrada" != *"Authorization: Bearer TOKEN-FALSO-ABC"* ]]; then
                    echo 'curl: (22) The requested URL returned error: 401' >&2; exit 22
                fi
                modo="$(cat "$E/backups")"
                case "$modo" in
                    ok) echo '[{"id":9,"finalizadaEn":"2026-10-19T03:00:00Z","resultado":"Exitosa","nombreArchivo":"backup_x.dump","tamanioBytes":2048}]' ;;
                    json-malo) echo 'no soy json' ;;
                    fallo) echo 'curl: (7) Failed to connect to 127.0.0.1 port 8443' >&2; exit 7 ;;
                    *) echo "curl: (22) The requested URL returned error: $modo" >&2; exit 22 ;;
                esac
                ;;
            "curl -fsS "*)
                # GET / de la API nueva por loopback, desde el propio servidor nuevo.
                if [[ "$lado" != nuevo ]]; then echo "stub ssh: curl no previsto en el $lado: $cmd" >&2; exit 99; fi
                if [[ "$(cat "$E/http-raiz")" == ok ]]; then echo '<html></html>'; else
                    echo 'curl: (7) Failed to connect to 127.0.0.1 port 8443' >&2; exit 7
                fi
                ;;
            'mkdir -m 700 "$HOME/'* | 'chmod 600 "$HOME/'* | 'sha256sum "$HOME/'* | 'find "$HOME/stockapp-deploy/migracion" '* | '"$HOME/stockapp-deploy/migracion/'*)
                if [[ "$lado" != nuevo ]]; then echo "stub ssh: comando no previsto en el $lado: $cmd" >&2; exit 99; fi
                HOME="$E/home" bash -c "$cmd" < /dev/null
                ;;
            *)
                echo "stub ssh: comando no previsto: $cmd" >&2
                exit 99
                ;;
        esac
        """;

    /// <summary>
    /// scp falso: -P y -o se aceptan; lo último es user@host:destino, relativo al HOME remoto (como
    /// el scp real). Copia al home simulado del host y, como el real, sale 1 si el directorio destino
    /// no existe. El rastro lleva solo los nombres de los archivos, nunca las rutas locales.
    /// </summary>
    private const string ScpFalso = "#!/usr/bin/env bash\n" + """
        archivos=()
        while [[ $# -gt 0 ]]; do
            case "$1" in
                -P | -o) shift 2 ;;
                --) shift ;;
                *) archivos+=("$1"); shift ;;
            esac
        done
        destino="${archivos[-1]}"
        unset 'archivos[-1]'
        host="${destino%%:*}"
        host="${host#*@}"
        ruta="${destino#*:}"
        case "$host" in
            vps-viejo.test.invalid) lado=viejo ;;
            vps-nuevo.test.invalid) lado=nuevo ;;
            *) echo "ssh: Could not resolve hostname $host" >&2; exit 255 ;;
        esac
        nombres=""
        for f in "${archivos[@]}"; do nombres+=" ${f##*/}"; done
        printf 'scp\t%s\t%s\t%s\n' "$lado" "$ruta" "${nombres# }" >> "$RASTRO"
        if [[ -f "$ESTADO/$lado/falla-scp" ]]; then echo "scp: write failed" >&2; exit 1; fi
        dir="$ESTADO/$lado/home/${ruta%/}"
        if [[ ! -d "$dir" ]]; then echo "scp: $ruta: No such file or directory" >&2; exit 1; fi
        cp -- "${archivos[@]}" "$dir/"
        """;

    /// <summary>
    /// curl del operador contra la API PÚBLICA del servidor nuevo: el feed (pre-vuelo y V7), la raíz
    /// (V3), /licencia/estado (V4), el login (V5) y /backups (V8). Reproduce de curl -f: un 4xx/5xx sale
    /// 22 con "returned error: NNN", nadie escuchando sale 7, un certificado inválido sale 60.
    /// </summary>
    private const string CurlFalso = "#!/usr/bin/env bash\n" + """
        url="${!#}"
        printf 'operador\tcurl %s\n' "$url" >> "$RASTRO"
        printf 'operador-args\t%s\n' "$*" >> "$RASTRO"
        """ + "\n" + EntornoDeHijos + "\n" + """
        entrada="$(cat)"
        metodo=GET
        cabeceras=0
        for a in "$@"; do
            case "$a" in
                POST) metodo=POST ;;
                -I | -fsSI) cabeceras=1 ;;
            esac
        done
        modo() { if [[ -f "$ESTADO/$1" ]]; then cat "$ESTADO/$1"; else echo "${2:-ok}"; fi; }
        con_token() {
            if [[ "$entrada" != *"Authorization: Bearer TOKEN-NUEVO-DEF"* ]]; then
                echo 'curl: (22) The requested URL returned error: 401' >&2; exit 22
            fi
        }
        case "$url" in
            */updates/releases.win.json)
                case "$(modo feed)" in
                    ausente) echo 'curl: (22) The requested URL returned error: 404' >&2; exit 22 ;;
                    sin-escucha) echo 'curl: (7) Failed to connect' >&2; exit 7 ;;
                    timeout) echo 'curl: (28) Operation timed out' >&2; exit 28 ;;
                    ilegible) echo 'no soy json' ;;
                    *) cat "$ESTADO/feed-json" ;;
                esac
                ;;
            */updates/*.nupkg)
                [[ "$cabeceras" == 1 ]] || { echo "stub curl: el nupkg se pide solo con -I" >&2; exit 99; }
                case "$(modo nupkg)" in
                    ok) printf 'HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: 10\r\n\r\n' ;;
                    largo-distinto) printf 'HTTP/1.1 200 OK\r\nContent-Length: 99\r\n\r\n' ;;
                    *) echo 'curl: (22) The requested URL returned error: 404' >&2; exit 22 ;;
                esac
                ;;
            */licencia/estado)
                case "$(modo licencia)" in
                    ok) echo '{"activada":true,"codigoMaquina":"MAQ-1"}' ;;
                    *) echo '{"activada":false,"codigoMaquina":"MAQ-1"}' ;;
                esac
                ;;
            */auth/login)
                printf '%s' "$entrada" > "$ESTADO/login-operador-stdin.txt"
                case "$(modo login-operador)" in
                    ok) echo '{"token":"TOKEN-NUEVO-DEF","usuario":{"id":1}}' ;;
                    *) echo "curl: (22) The requested URL returned error: $(modo login-operador)" >&2; exit 22 ;;
                esac
                ;;
            */backups/*/contenido)
                con_token
                printf '%s\n' "$url" >> "$ESTADO/contenido-pedido.txt"
                case "$(modo contenido)" in
                    ok) echo 'PGDMP' ;;
                    *) echo 'curl: (22) The requested URL returned error: 404' >&2; exit 22 ;;
                esac
                ;;
            */backups)
                con_token
                printf '%s' "$entrada" > "$ESTADO/backups-operador-stdin.txt"
                if [[ "$metodo" == POST ]]; then : > "$ESTADO/post-backups"; exit 0; fi
                nueva=""
                if [[ -f "$ESTADO/post-backups" ]]; then
                    nueva=',{"id":12,"finalizadaEn":"2026-10-21T03:00:00Z","resultado":"'"$(modo v8-resultado Exitosa)"'","nombreArchivo":"backup_n.dump","tamanioBytes":2048}'
                fi
                echo '[{"id":9,"finalizadaEn":"2026-10-19T03:00:00Z","resultado":"Exitosa","nombreArchivo":"backup_x.dump","tamanioBytes":2048},{"id":5,"finalizadaEn":"2026-09-01T03:00:00Z","resultado":"Exitosa","nombreArchivo":"backup_v.dump","tamanioBytes":2048}'"$nueva"']'
                ;;
            */)
                case "$(modo v3)" in
                    ok) echo '<html></html>' ;;
                    *) echo 'curl: (60) SSL certificate problem: unable to get local issuer certificate' >&2; exit 60 ;;
                esac
                ;;
            *)
                echo "stub curl: url no prevista: $url" >&2
                exit 99
                ;;
        esac
        """;

    private void ArmarStubs()
    {
        BashLimpio.EscribirEjecutable(Path.Combine(Bin, "ssh"), SshFalso);
        BashLimpio.EscribirEjecutable(Path.Combine(Bin, "curl"), CurlFalso);
        BashLimpio.EscribirEjecutable(Path.Combine(Bin, "scp"), ScpFalso);
    }

    private void EstadoInicialDeLosHosts()
    {
        PonerViejo("machine-id", MachineViejo + "\n");
        PonerViejo("active", "active\n");
        PonerViejo("fragment", "/etc/systemd/system/stockapp-api.service\n");
        PonerViejo("existen", "");
        PonerViejo("contenedores", "stockapp-pg\npinar-db\n");
        PonerViejo("dbsize", "104857600\n");
        PonerViejo("df-volumen-kb", "10485760\n");
        PonerViejo("login", "ok\n");
        PonerViejo("backups", "ok\n");
        PonerViejo("enabled", "enabled\n");

        PonerNuevo("machine-id", MachineNuevo + "\n");
        PonerNuevo("dbsize", "5242880\n");
        PonerNuevo("df-volumen-kb", "10485760\n");
        PonerNuevo("df-fs-kb", "10485760\n");
        PonerNuevo("env-claves", "POSTGRES_USER\nPOSTGRES_PASSWORD\nPOSTGRES_DB\nJWT_SECRET\n");
        PonerNuevo("active", "active\n");
        PonerNuevo("al-arrancar", "active\n");
        PonerNuevo("http-raiz", "ok\n");
        PonerNuevo("migracion", "20260101000000_Inicial\n");
        PonerNuevo("logs-auditoria", "42\n");
        Directory.CreateDirectory(MigracionNuevo);

        File.WriteAllText(Path.Combine(Estado, "feed"), "ok\n");
        File.WriteAllText(Path.Combine(Estado, "feed-json"),
            $"{{\"Assets\":[{{\"Type\":\"Full\",\"Version\":\"{Version}\",\"FileName\":\"StockApp-{Version}-full.nupkg\",\"Size\":10,\"SHA256\":\"{new string('a', 64)}\"}}]}}\n");
    }

    private void EscribirEnvs(string hostViejo = HostViejo, string hostNuevo = HostNuevo, string saltoDeLinea = "\n")
    {
        string Env(string host, int puerto, string api, bool admin) =>
            string.Join(saltoDeLinea, new[]
            {
                $"VPS_HOST={host}", "VPS_USER=deploy", $"VPS_SSH_PORT={puerto}",
                "API_SCHEME=https", $"API_HOSTNAME={api}", "API_PORT=8443",
                "POSTGRES_USER=stockapp", "POSTGRES_DB=stockapp",
            }.Concat(admin ? [$"VPS_ADMIN_USER=martin", $"VPS_ADMIN_PASSWORD={Contrasena}"] : [])) + saltoDeLinea;

        File.WriteAllText(EnvViejo, Env(hostViejo, 2201, "viejo.test.invalid", admin: true));
        File.WriteAllText(EnvNuevo, Env(hostNuevo, 2202, "nuevo.test.invalid", admin: false));
    }

    /// <summary>Un env sin una clave (para probar que falta, sin caer a ningún default).</summary>
    private void QuitarDelEnv(string archivo, string clave) =>
        File.WriteAllLines(archivo, File.ReadAllLines(archivo).Where(l => !l.StartsWith(clave + "=")));

    // ---------- Ejecución ----------

    internal sealed record Resultado(int Exit, string Stdout, string Stderr, string Rastro)
    {
        // Después de orq_log_iniciar el stderr del script pasa por el mismo tee que el stdout: los
        // mensajes de la fase con log salen por stdout y los anteriores por stderr.
        public string Salida => Stdout + "\n" + Stderr;
        public string Todo => $"exit={Exit}\n--- stdout ---\n{Stdout}\n--- stderr ---\n{Stderr}\n--- rastro ---\n{Rastro}";
    }

    private static string Sq(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Corre el orquestador sin terminal de control. <paramref name="stdin"/> es lo que lee (la
    /// contraseña, si no está en el .env, y la palabra de confirmación del arnés).
    /// </summary>
    private Resultado Correr(string[]? args = null, string? stdin = null, IDictionary<string, string>? env = null,
        bool conArgumentosBase = true)
    {
        args ??= [];
        var todos = conArgumentosBase ? new[] { "--env-viejo", EnvViejo, "--env-nuevo", EnvNuevo }.Concat(args) : args;
        var entrada = Path.Combine(_dir, "stdin-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        File.WriteAllText(entrada, stdin ?? "");

        var sb = new StringBuilder();
        sb.Append($"export PATH={Sq(Bin)}\":$PATH\"\n");
        sb.Append($"export RASTRO={Sq(Rastro)} ESTADO={Sq(Estado)} TMPDIR={Sq(Tmp)}\n");
        if (env is not null)
            foreach (var (k, v) in env) sb.Append($"export {k}={Sq(v)}\n");
        sb.Append($"cd {Sq(Raiz)}\n");
        sb.Append($"setsid -w bash {Sq(Script)} {string.Join(' ', todos.Select(Sq))} < {Sq(entrada)}\n");

        var r = BashLimpio.Ejecutar(sb.ToString(), Home);
        return new Resultado(r.ExitCode, r.Stdout, r.Stderr, File.Exists(Rastro) ? File.ReadAllText(Rastro) : "");
    }

    private Resultado CorrerCorte(string? stdin = "CONGELAR\n", IDictionary<string, string>? env = null) =>
        Correr([], stdin, env);

    // ---------- Rastro: LECTURA o ESCRITURA ----------

    // Raíz remota del VPS viejo con la que corre el test (--raiz-remota-viejo): las rutas exactas de
    // la lista blanca cuelgan de ella (R33).
    private string _raizViejo = "";

    /// <summary>
    /// Lista blanca del VPS viejo, compartido con otra aplicación (spec 5, R22 y R33): lecturas
    /// acotadas por nombre a StockApp, con las rutas EXACTAS (test -e solo del .congelado y cat solo
    /// del machine-id, bajo la raíz remota del test). Lo que hace el bloque de volcado no cuenta: es un
    /// proceso aparte con sus propios tests.
    /// </summary>
    private Regex[] LecturasViejo =>
    [
        new(@"^sudo -n true$"),
        new("^cat " + Regex.Escape(_raizViejo + "/etc/machine-id") + "$"),
        new(@"^systemctl (is-active|is-enabled|show -p FragmentPath) stockapp-api$"),
        new("^test -e " + Regex.Escape(_raizViejo + UnitCongelada) + "$"),
        new(@"^docker ps --format '\{\{\.Names\}\}'$"),
        new(@"^docker exec (-i )?stockapp[a-z0-9-]* (psql -U [a-z_][a-z0-9_]* -d [a-z_][a-z0-9_]* -tA -v ON_ERROR_STOP=1|df -P /var/lib/postgresql/data)$"),
        new(@"^curl -fsS (--resolve \S+ )?(-X POST \S+/auth/login -H 'Content-Type: application/json' --data @-|\S+/backups -K -)$"),
    ];

    private static readonly Regex[] LecturasNuevoExtra =
    [
        new(@"^systemctl (is-active|is-enabled) stockapp-api$"),
        new(@"^sudo -n docker exec (-i )?stockapp[a-z0-9-]* (psql -U [a-z_][a-z0-9_]* -d [a-z_][a-z0-9_]* -tA -v ON_ERROR_STOP=1|df -P /var/lib/postgresql/data)$"),
        new("^" + Regex.Escape("df -P \"$HOME/stockapp-deploy/migracion\"") + "$"),
        new("^" + Regex.Escape("sed -n 's/^\\([A-Za-z_][A-Za-z0-9_]*\\)=.*/\\1/p' \"$HOME/stockapp-deploy/.env\"") + "$"),
    ];

    private static IEnumerable<(string Lado, string Cmd)> ComandosRemotos(string rastro) =>
        rastro.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t', 2))
            .Where(p => p.Length == 2 && p[0] is "viejo" or "nuevo")
            .Select(p => (p[0], p[1]));

    private bool EsLectura(string lado, string cmd) =>
        LecturasViejo.Any(r => r.IsMatch(cmd)) || (lado == "nuevo" && LecturasNuevoExtra.Any(r => r.IsMatch(cmd)));

    private IEnumerable<string> Escrituras(string rastro) =>
        ComandosRemotos(rastro).Where(c => !EsLectura(c.Lado, c.Cmd)).Select(c => $"{c.Lado}: {c.Cmd}");

    /// <summary>Índice de la primera línea del rastro que cumple <paramref name="condicion"/>, o -1.</summary>
    private static int Linea(string rastro, Func<string, bool> condicion)
    {
        var lineas = rastro.Split('\n');
        for (var i = 0; i < lineas.Length; i++)
            if (condicion(lineas[i])) return i;
        return -1;
    }

    private static int UltimaLinea(string rastro, Func<string, bool> condicion)
    {
        var lineas = rastro.Split('\n');
        for (var i = lineas.Length - 1; i >= 0; i--)
            if (condicion(lineas[i])) return i;
        return -1;
    }

    private const string StopNuevo = "nuevo\tsudo -n systemctl stop stockapp-api";
    private const string StartNuevo = "nuevo\tsudo -n systemctl start stockapp-api";

    private static IEnumerable<string> LineasDeBloque(string rastro, string bloque) =>
        rastro.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith(bloque + "\t"));

    // ---------- Corridas ----------

    private string[] Corridas() =>
        Directory.Exists(CorridasDir) ? Directory.GetDirectories(CorridasDir).OrderBy(d => d, StringComparer.Ordinal).ToArray() : [];

    private string UnaCorrida(string prefijo)
    {
        var c = Corridas().Where(d => Path.GetFileName(d).StartsWith(prefijo + "-")).ToArray();
        Assert.Single(c);
        return c[0];
    }

    private static string? ClaveDeEstado(string corrida, string clave)
    {
        var archivo = Path.Combine(corrida, "estado");
        if (!File.Exists(archivo)) return null;
        var linea = File.ReadAllLines(archivo).LastOrDefault(l => l.StartsWith(clave + "="));
        return linea?[(clave.Length + 1)..];
    }

    /// <summary>Planta una corrida anterior (ensayo, corte...) con su estado y, si se pide, los overrides.</summary>
    private string PlantarCorrida(string nombre, string estado, string? overrides = null)
    {
        var dir = Path.Combine(CorridasDir, nombre);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "estado"), estado);
        if (overrides is not null)
        {
            Directory.CreateDirectory(Path.Combine(dir, "volcado"));
            File.WriteAllText(Path.Combine(dir, "volcado", "overrides-api-env.txt"), overrides);
        }

        return dir;
    }

    private string EstadoDeEnsayo(string machineViejo = MachineViejo, string machineNuevo = MachineNuevo, string extra = "") =>
        $"modo=ensayo\ninicio_utc=2026-01-01T09:00:00Z\nmachine_id_viejo={machineViejo}\nmachine_id_nuevo={machineNuevo}\n{extra}resultado=GO\n";

    // =====================================================================================
    // Camino feliz: hasta donde llega lo implementado
    // =====================================================================================

    [Fact]
    public void CaminoFeliz_PrevueloYConfirmacion_SinEscrituras()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.Contains("pre-vuelo", r.Stdout);
        Assert.Contains("confirmación", r.Stdout);
        // Hasta la confirmación, solo lecturas: la primera escritura es el stop de la API NUEVA (paso 3)
        // y ningún bloque corrió antes.
        Assert.Equal($"nuevo: sudo -n systemctl stop stockapp-api", Escrituras(r.Rastro).First());
        var stop = Linea(r.Rastro, l => l == StopNuevo);
        Assert.True(stop > 0 && stop < Linea(r.Rastro, l => l.StartsWith("volcado\t")), r.Todo);
    }

    [Fact]
    public void Corrida_RegistraModoIdentidadYPasos_SinSecretos()
    {
        var r = CorrerCorte();

        var corrida = UnaCorrida("corte");
        Assert.True(ClaveDeEstado(corrida, "modo") == "corte", r.Todo);
        Assert.Equal(MachineViejo, ClaveDeEstado(corrida, "machine_id_viejo"));
        Assert.Equal(MachineNuevo, ClaveDeEstado(corrida, "machine_id_nuevo"));
        Assert.Equal(EnvViejo, ClaveDeEstado(corrida, "env_viejo"));
        Assert.Equal(EnvNuevo, ClaveDeEstado(corrida, "env_nuevo"));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", ClaveDeEstado(corrida, "inicio_utc") ?? "");
        Assert.Matches(@"^ok \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z \d+$", ClaveDeEstado(corrida, "paso_1") ?? "");
        Assert.Matches(@"^ok \d{4}", ClaveDeEstado(corrida, "paso_2") ?? "");

        Assert.Equal("700", Permisos(corrida));
        Assert.Equal("600", Permisos(Path.Combine(corrida, "registro.log")));
        Assert.DoesNotContain(Contrasena, File.ReadAllText(Path.Combine(corrida, "estado")));
    }

    private static string Sha256(string ruta) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(ruta))).ToLowerInvariant();

    private static string Permisos(string ruta) =>
        Convert.ToString((int)(File.GetUnixFileMode(ruta) & (UnixFileMode)0b111_111_111), 8);

    [Fact]
    public void Ensayo_NoPideConfirmacion_YLaCorridaEsDeEnsayo()
    {
        var r = Correr(["--ensayo"], stdin: "");

        Assert.True(r.Exit == ExitEnsayoSinSecuencia, r.Todo);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Matches(@"confirmación \.+ OMITIDO", r.Stdout);
        var corrida = UnaCorrida("ensayo");
        Assert.Equal("ensayo", ClaveDeEstado(corrida, "modo"));
        Assert.Empty(Escrituras(r.Rastro));
    }

    // =====================================================================================
    // Identidad de los hosts (A9, spec 6.3.4)
    // =====================================================================================

    [Fact]
    public void MachineIdIguales_Aborta9SinEscrituras()
    {
        PonerNuevo("machine-id", MachineViejo + "\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 9, r.Todo);
        Assert.Empty(Escrituras(r.Rastro));
        Assert.Empty(Corridas());
        Assert.Empty(LineasDeBloque(r.Rastro, "preparar"));
        Assert.DoesNotContain("CONGELAR", r.Salida);
    }

    [Fact]
    public void LosDosEnvApuntanAlMismoHost_Aborta9SinSsh()
    {
        EscribirEnvs(hostNuevo: HostViejo);

        var r = CorrerCorte();

        Assert.True(r.Exit == 9, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    [Theory]
    [InlineData("viejo")]
    [InlineData("nuevo")]
    public void MachineIdIlegible_Aborta8(string lado)
    {
        File.Delete(Host(lado, "machine-id"));

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void MachineIdQueNoEsUnMachineId_Aborta8()
    {
        PonerViejo("machine-id", "no soy un machine-id\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Theory]
    [InlineData("viejo")]
    [InlineData("nuevo")]
    public void SshCaido_Aborta8_NuncaCero(string lado)
    {
        File.WriteAllText(Host(lado, "ssh-caido"), "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_EnsayoPrevioDeOtrosServidores_Aborta3EnElCorte_PeroNoEnElEnsayo()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(machineNuevo: "cccccccc33333333cccccccc33333333"));

        var corte = CorrerCorte();
        Assert.True(corte.Exit == 3, corte.Todo);
        Assert.Contains("ensayo", corte.Salida);

        var ensayo = Correr(["--ensayo"], stdin: "");
        Assert.True(ensayo.Exit == ExitEnsayoSinSecuencia, ensayo.Todo);
    }

    // =====================================================================================
    // Configuración y .env
    // =====================================================================================

    [Fact]
    public void Prevuelo_EnvConCrLf_Aborta3SinSsh()
    {
        EscribirEnvs(saltoDeLinea: "\r\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Equal("", r.Rastro);
        Assert.Empty(Corridas());
    }

    [Fact]
    public void Prevuelo_EnvInexistente_Aborta3SinSsh()
    {
        File.Delete(EnvNuevo);

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    [Theory]
    [InlineData("viejo", "VPS_HOST")]
    [InlineData("viejo", "VPS_USER")]
    [InlineData("viejo", "VPS_SSH_PORT")]
    [InlineData("viejo", "VPS_ADMIN_USER")]
    [InlineData("nuevo", "VPS_HOST")]
    [InlineData("nuevo", "API_HOSTNAME")]
    public void Prevuelo_FaltaUnaClave_Aborta3SinSsh(string env, string clave)
    {
        QuitarDelEnv(env == "viejo" ? EnvViejo : EnvNuevo, clave);

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains(clave, r.Salida);
        Assert.Equal("", r.Rastro);
    }

    [Fact]
    public void Prevuelo_UsuarioDeVerificacion_NoCaeABootstrapAdminUser()
    {
        QuitarDelEnv(EnvViejo, "VPS_ADMIN_USER");
        File.AppendAllText(EnvViejo, "BOOTSTRAP_ADMIN_USER=admin\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("VPS_ADMIN_USER", r.Salida);
        Assert.Equal("", r.Rastro);
    }

    // =====================================================================================
    // Pre-vuelo del viejo
    // =====================================================================================

    [Fact]
    public void Prevuelo_UnitNoEsLaDeInstall_Aborta3()
    {
        PonerViejo("fragment", "/usr/lib/systemd/system/stockapp-api.service\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("FragmentPath", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void Prevuelo_CongeladoExistente_Aborta3()
    {
        PonerViejo("existen", "/etc/systemd/system/stockapp-api.service.congelado\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("congelado", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Empty(LineasDeBloque(r.Rastro, "preparar"));
    }

    [Fact]
    public void Prevuelo_ViejoInactivo_Aborta3()
    {
        PonerViejo("active", "inactive\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("active", r.Salida);
    }

    [Fact]
    public void Prevuelo_RespuestaDeIsActiveIlegible_Aborta8()
    {
        PonerViejo("active", "quién sabe\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_SudoSinContrasena_Aborta3()
    {
        File.WriteAllText(Host("viejo", "sin-sudo"), "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("sudo", r.Salida);
    }

    [Fact]
    public void Prevuelo_ContenedorDePostgresNoCorre_Aborta3()
    {
        PonerViejo("contenedores", "pinar-db\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("stockapp-pg", r.Salida);
    }

    [Fact]
    public void Prevuelo_ListaDeContenedoresVacia_Aborta8()
    {
        PonerViejo("contenedores", "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_DiscoDelViejoInsuficienteParaElDump_Aborta3()
    {
        PonerViejo("dbsize", "104857600\n");        // 100 MiB
        PonerViejo("df-volumen-kb", "1024\n");        // 1 MiB libre

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("disco", r.Salida);
    }

    [Fact]
    public void Prevuelo_TamanoDeLaBaseIlegible_Aborta8()
    {
        PonerViejo("dbsize", "mucho\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_PsqlFalla_Aborta8_NuncaCero()
    {
        File.WriteAllText(Host("viejo", "dbsize-falla"), "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    // =====================================================================================
    // Pre-vuelo del nuevo
    // =====================================================================================

    [Fact]
    public void Prevuelo_LlamaALaVerificacionDeLaPreparacion_ConElEnvDelNuevo()
    {
        var r = CorrerCorte();

        var llamadas = LineasDeBloque(r.Rastro, "preparar").ToList();
        Assert.True(llamadas.Count == 1, r.Todo);
        Assert.Contains($"ENV={EnvNuevo}", llamadas[0]);
        Assert.Contains($"--version {Version}", llamadas[0]);
        Assert.Contains("--verificar", llamadas[0]);
        Assert.Contains($"--env-viejo {EnvViejo}", llamadas[0]);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    [InlineData(9, 9)]
    public void Prevuelo_PreparacionIncompleta_AbortaConSuClase(int salidaDelBloque, int esperado)
    {
        var r = CorrerCorte(env: new Dictionary<string, string> { ["STUB_PREPARAR_EXIT"] = salidaDelBloque.ToString() });

        Assert.True(r.Exit == esperado, r.Todo);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void Prevuelo_SinVerificarPreparacion_EsSoloDeArnes_YSeInformaOmitido()
    {
        var r = Correr(["--sin-verificar-preparacion"], stdin: "CONGELAR\n",
            env: new Dictionary<string, string> { ["STUB_PREPARAR_EXIT"] = "5" });

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.Contains("OMITIDO (arnés)", r.Stdout);
        Assert.Empty(LineasDeBloque(r.Rastro, "preparar"));
    }

    [Fact]
    public void Prevuelo_ElNuevoNoSirveElFeed_Aborta3()
    {
        File.WriteAllText(Path.Combine(Estado, "feed"), "ausente\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("feed", r.Salida);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("ilegible")]
    public void Prevuelo_FeedQueNoSePuedeConsultarOInterpretar_Aborta8(string modo)
    {
        File.WriteAllText(Path.Combine(Estado, "feed"), modo + "\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_DiscoDelNuevoInsuficiente_Aborta3()
    {
        PonerNuevo("df-fs-kb", "1024\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("disco", r.Salida);
    }

    [Fact]
    public void Prevuelo_VolumenDelNuevoInsuficiente_Aborta3()
    {
        PonerNuevo("df-volumen-kb", "1024\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("disco", r.Salida);
    }

    // =====================================================================================
    // Overrides (spec 6.5.3 punto 4)
    // =====================================================================================

    [Fact]
    public void Prevuelo_OverrideFaltanteEnElNuevo_Aborta3()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(), overrides: "Licencia__Algo\nOTRO_OVERRIDE\n");
        PonerNuevo("env-claves", "POSTGRES_USER\nOTRO_OVERRIDE\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("Licencia__Algo", r.Salida);
        Assert.DoesNotContain("OTRO_OVERRIDE", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
    }

    [Fact]
    public void Prevuelo_OverridesPresentesEnElNuevo_Sigue()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(), overrides: "Licencia__Algo\nOTRO_OVERRIDE\n");
        PonerNuevo("env-claves", "POSTGRES_USER\nOTRO_OVERRIDE\nLicencia__Algo\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
    }

    [Fact]
    public void Prevuelo_SinEnsayoPrevio_InformaQueNoHayLista_YSigue()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.Contains("no hay un ensayo anterior", r.Stdout);
    }

    [Fact]
    public void Prevuelo_ElListadoDeClavesDelNuevoNoPuedeLeerse_Aborta8()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(), overrides: "Licencia__Algo\n");
        File.Delete(Host("nuevo", "env-claves"));

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    // =====================================================================================
    // Credencial (spec 6.5.3 puntos 5 y 6)
    // =====================================================================================

    [Fact]
    public void Prevuelo_CredencialRechazada_Aborta3AntesDeConfirmar()
    {
        PonerViejo("login", "401\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Contains("401", r.Salida);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void Prevuelo_SinPermisoParaBackups_Aborta3()
    {
        PonerViejo("backups", "403\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("403", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
    }

    [Theory]
    [InlineData("login", "429")]
    [InlineData("login", "500")]
    [InlineData("login", "fallo")]
    [InlineData("backups", "500")]
    [InlineData("backups", "fallo")]
    [InlineData("backups", "json-malo")]
    public void Prevuelo_CredencialOPermisoQueNoSePuedenConsultar_Aborta8(string que, string modo)
    {
        PonerViejo(que, modo + "\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
        Assert.DoesNotContain("CONGELAR", r.Salida);
    }

    [Fact]
    public void Credencial_SinContrasenaEnNingunLado_EnArnesSeLeeDeStdinAntesDeLaPalabra()
    {
        QuitarDelEnv(EnvViejo, "VPS_ADMIN_PASSWORD");

        var r = CorrerCorte(stdin: $"{Contrasena}\nCONGELAR\n");

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.Contains(Contrasena, File.ReadAllText(Path.Combine(Estado, "login-stdin.txt")));
        Assert.DoesNotContain(Contrasena, r.Stdout + r.Stderr + r.Rastro);
    }

    [Fact]
    public void Credencial_SinContrasenaYSinNadaEnStdin_Aborta3()
    {
        QuitarDelEnv(EnvViejo, "VPS_ADMIN_PASSWORD");

        var r = CorrerCorte(stdin: "");

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("VPS_ADMIN_PASSWORD", r.Salida);
    }

    [Fact]
    public void Credencial_ElEntornoGanaSobreElArchivo()
    {
        const string delEntorno = "Del-Entorno-987";

        var r = CorrerCorte(env: new Dictionary<string, string> { ["VPS_ADMIN_PASSWORD"] = delEntorno });

        Assert.True(r.Exit == ExitGo, r.Todo);
        var enviado = File.ReadAllText(Path.Combine(Estado, "login-stdin.txt"));
        Assert.Contains(delEntorno, enviado);
        Assert.DoesNotContain(Contrasena, enviado);
    }

    [Fact]
    public void PasswordNuncaEnArgvNiLog()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        // Control positivo: la contraseña SÍ viajó, y por el stdin del login.
        Assert.Contains(Contrasena, File.ReadAllText(Path.Combine(Estado, "login-stdin.txt")));

        var corrida = UnaCorrida("corte");
        var archivos = Directory.GetFiles(corrida, "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Tmp, "*", SearchOption.AllDirectories))
            .Select(File.ReadAllText);
        var todo = r.Stdout + r.Stderr + r.Rastro + string.Join("\n", archivos);
        Assert.DoesNotContain(Contrasena, todo);
        Assert.DoesNotContain("TOKEN-FALSO-ABC", todo);
        // El token tampoco viaja en el comando: solo por el stdin de curl -K -.
        Assert.Contains("Bearer TOKEN-FALSO-ABC", File.ReadAllText(Path.Combine(Estado, "backups-stdin.txt")));
    }

    // =====================================================================================
    // Confirmación (A12, spec 6.3.6)
    // =====================================================================================

    [Fact]
    public void Confirmacion_PalabraDistinta_Sale4SinEscrituras()
    {
        var r = CorrerCorte(stdin: "congelar\n");

        Assert.True(r.Exit == 4, r.Todo);
        Assert.Empty(Escrituras(r.Rastro));
        Assert.Empty(LineasDeBloque(r.Rastro, "volcado"));
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
        var corrida = UnaCorrida("corte");
        Assert.Null(ClaveDeEstado(corrida, "paso_2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CONGELAR \n")]
    [InlineData("si\n")]
    [InlineData("CONGELARX\n")]
    public void Confirmacion_CualquierOtraCosa_Sale4(string stdin)
    {
        var r = CorrerCorte(stdin: stdin);

        Assert.True(r.Exit == 4, r.Todo);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void Confirmacion_EsLoUnicoEntrePrevueloYLaPrimeraEscritura()
    {
        var r = CorrerCorte();

        var finPrevuelo = r.Stdout.IndexOf("pre-vuelo", StringComparison.Ordinal);
        var palabra = r.Stdout.IndexOf("CONGELAR", StringComparison.Ordinal);
        Assert.True(finPrevuelo >= 0 && palabra > finPrevuelo, "la confirmación tiene que venir después del pre-vuelo.\n" + r.Todo);
        // La línea del paso 3 (la primera escritura) sale DESPUÉS de la palabra.
        var paso3 = r.Stdout.IndexOf("detener la API nueva", StringComparison.Ordinal);
        Assert.True(paso3 > palabra, "el paso 3 tiene que venir después de la confirmación.\n" + r.Todo);
        // Con la palabra equivocada no hay ninguna escritura (Confirmacion_PalabraDistinta_Sale4SinEscrituras);
        // con la correcta, la primera es el stop del nuevo.
        Assert.Equal("nuevo: sudo -n systemctl stop stockapp-api", Escrituras(r.Rastro).First());
    }

    [Fact]
    public void Confirmacion_MuestraHostsVersionIndisponibilidadYComandosDeVueltaAtras_YQuedaEnElLog()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(
            extra: "congelado_utc=2026-01-01T10:00:00Z\napi_nueva_activa_utc=2026-01-01T10:03:12Z\n"));

        var r = CorrerCorte();

        var corrida = UnaCorrida("corte");
        var log = File.ReadAllText(Path.Combine(corrida, "registro.log"));
        foreach (var texto in new[] { r.Stdout, log })
        {
            Assert.Contains(HostViejo, texto);
            Assert.Contains(HostNuevo, texto);
            Assert.Contains(MachineViejo[..8], texto);
            Assert.Contains(MachineNuevo[..8], texto);
            Assert.DoesNotContain(MachineViejo, texto);
            Assert.Contains(Version, texto);
            Assert.Contains("3 min 12 s", texto);
            Assert.Contains("sudo systemctl unmask stockapp-api", texto);
            Assert.Contains("sudo mv /etc/systemd/system/stockapp-api.service.congelado /etc/systemd/system/stockapp-api.service", texto);
            Assert.Contains("sudo systemctl daemon-reload", texto);
            Assert.Contains("sudo systemctl start stockapp-api", texto);
            Assert.Contains("--revertir", texto);
        }
    }

    [Fact]
    public void Confirmacion_SinEnsayoPrevio_DiceQueNoHayDato()
    {
        var r = CorrerCorte();

        Assert.Contains("sin ensayo previo", r.Stdout);
    }

    [Fact]
    public void SinTerminalConHostsReales_Sale3()
    {
        EscribirEnvs(hostViejo: "viejo-real.example.org", hostNuevo: "nuevo-real.example.org");

        var r = CorrerCorte(stdin: "CONGELAR\n");

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("terminal", r.Salida);
        Assert.Equal("", r.Rastro);
        Assert.DoesNotContain("pre-vuelo", r.Salida);
    }

    [Fact]
    public void HostsReales_NoUsanElDesvioDeStdin_AunqueSoloUnoSeaInvalid()
    {
        EscribirEnvs(hostNuevo: "nuevo-real.example.org");

        var r = CorrerCorte(stdin: "CONGELAR\n");

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    public static TheoryData<string, string?> ParametrosDeArnes() => new()
    {
        { "--contenedor-viejo", "stockapp-ensayo-x" },
        { "--contenedor-nuevo", "stockapp-ensayo-y" },
        { "--raiz-remota-viejo", "/hosts/viejo" },
        { "--raiz-remota-nuevo", "/hosts/nuevo" },
        { "--sin-verificar-preparacion", null },
    };

    [Theory]
    [MemberData(nameof(ParametrosDeArnes))]
    public void ParametrosDeArnesConHostReal_Rechaza2(string parametro, string? valor)
    {
        EscribirEnvs(hostViejo: "viejo-real.example.org", hostNuevo: "nuevo-real.example.org");

        var r = Correr(valor is null ? [parametro] : [parametro, valor], stdin: "CONGELAR\n");

        Assert.True(r.Exit == 2, r.Todo);
        Assert.Contains(parametro, r.Salida);
        Assert.Equal("", r.Rastro);
    }

    [Fact]
    public void InyeccionDeFallosConHostReal_Rechaza2()
    {
        EscribirEnvs(hostViejo: "viejo-real.example.org", hostNuevo: "nuevo-real.example.org");

        var r = CorrerCorte(env: new Dictionary<string, string> { ["STOCKAPP_ENSAYO_FALLAR_EN"] = "7" });

        Assert.True(r.Exit == 2, r.Todo);
        Assert.Contains("STOCKAPP_ENSAYO_FALLAR_EN", r.Salida);
        Assert.Equal("", r.Rastro);
    }

    [Fact]
    public void ParametrosDeArnes_ConHostsInvalid_ReenvianLosNombresDeContenedor()
    {
        PonerViejo("contenedores", "stockapp-ensayo-origen\n");
        PonerViejo("fragment", "/hosts/viejo/etc/systemd/system/stockapp-api.service\n");

        var r = Correr(["--contenedor-viejo", "stockapp-ensayo-origen", "--contenedor-nuevo", "stockapp-ensayo-destino",
            "--raiz-remota-viejo", "/hosts/viejo", "--raiz-remota-nuevo", "/hosts/nuevo"], stdin: "CONGELAR\n");

        Assert.True(r.Exit == ExitGo, r.Todo);
        var cmds = ComandosRemotos(r.Rastro).ToList();
        Assert.Contains(cmds, c => c.Lado == "viejo" && c.Cmd.Contains("docker exec -i stockapp-ensayo-origen psql"));
        Assert.Contains(cmds, c => c.Lado == "nuevo" && c.Cmd.Contains("docker exec -i stockapp-ensayo-destino psql"));
        Assert.Contains(cmds, c => c.Lado == "viejo" && c.Cmd == "cat /hosts/viejo/etc/machine-id");
        Assert.Contains(cmds, c => c.Lado == "nuevo" && c.Cmd == "cat /hosts/nuevo/etc/machine-id");
        Assert.DoesNotContain(cmds, c => c.Cmd.Contains("stockapp-pg ") && c.Cmd.Contains("docker exec"));
        // R13: los bloques reciben el contenedor y la raíz del arnés; nunca stockapp-pg.
        var volcado = Assert.Single(LineasDeBloque(r.Rastro, "volcado"));
        Assert.Contains("--contenedor stockapp-ensayo-origen --raiz-remota /hosts/viejo", volcado);
        var restaurar = LineasDeBloque(r.Rastro, "restaurar").ToList();
        Assert.Equal(2, restaurar.Count);
        Assert.All(restaurar, l => Assert.Contains("--contenedor stockapp-ensayo-destino", l));
        Assert.DoesNotContain("stockapp-pg", string.Join("\n", restaurar.Append(volcado)));
        // Las rutas absolutas del nuevo cuelgan de su raíz; el chown no existe en el arnés (R12).
        Assert.Contains(cmds, c => c.Lado == "nuevo" && c.Cmd.StartsWith("sudo -n tar -C /hosts/nuevo/var/lib/stockapp/.local/share/StockApp -xpf "));
        Assert.DoesNotContain(cmds, c => c.Cmd.Contains("chown"));
        Assert.Contains("OMITIDO (arnés)", r.Stdout);
        // Toda orden al viejo, en la lista blanca con las rutas exactas de SU raíz.
        _raizViejo = "/hosts/viejo";
        Assert.All(cmds.Where(c => c.Lado == "viejo"), c => Assert.True(LecturasViejo.Any(x => x.IsMatch(c.Cmd)), c.Cmd));
    }

    [Theory]
    [InlineData("--contenedor-viejo", "otra-cosa")]
    [InlineData("--contenedor-nuevo", "stockapp pg")]
    [InlineData("--raiz-remota-viejo", "relativa/ruta")]
    [InlineData("--raiz-remota-nuevo", "/a/../b")]
    public void ParametrosDeArnes_ConValorFueraDePatron_Rechaza2(string parametro, string valor)
    {
        var r = Correr([parametro, valor]);

        Assert.True(r.Exit == 2, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    [Theory]
    [InlineData("--continuar-ya")]
    [InlineData("--no-existe")]
    public void ArgumentoDesconocido_Rechaza2(string arg)
    {
        var r = Correr([arg]);

        Assert.True(r.Exit == 2, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    // =====================================================================================
    // Corridas sin terminar (spec 6.3.3, R24) y lock
    // =====================================================================================

    [Fact]
    public void CorridaSinTerminar_CorteNuevoSeNiega3ConLasDosSalidas()
    {
        var sinTerminar = PlantarCorrida("corte-20260101T090000Z", "modo=corte\ninicio_utc=2026-01-01T09:00:00Z\npaso_1=ok 2026-01-01T09:00:10Z 3\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains($"--continuar {sinTerminar}", r.Salida);
        Assert.Contains("--revertir", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Empty(Escrituras(r.Rastro));
        Assert.Empty(LineasDeBloque(r.Rastro, "preparar"));
    }

    [Fact]
    public void CorridaTerminada_NoBloquea()
    {
        PlantarCorrida("corte-20260101T090000Z", "modo=corte\ninicio_utc=2026-01-01T09:00:00Z\nresultado=FALLO-5\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
    }

    [Fact]
    public void CorridaSinEstado_NoBloquea_PorqueNoHizoNada()
    {
        // R24: quien pierde el lock deja una corrida sin estado; contarla bloquearía todos los cortes.
        Directory.CreateDirectory(Path.Combine(CorridasDir, "corte-20260101T090000Z"));

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
    }

    [Fact]
    public void CorridaDeRevertirSinTerminar_NoBloquea_PorqueRevertirEsIdempotente()
    {
        PlantarCorrida("revertir-20260101T090000Z", "modo=revertir\ninicio_utc=2026-01-01T09:00:00Z\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
    }

    [Fact]
    public async Task Lock_Tomado_Sale3()
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
        _procesos.Add(titular);
        try
        {
            var tomado = await EsperaMonotonica.HastaAsync(
                () => BashLimpio.Ejecutar($"exec 8>>{Sq(LockFile)}; ! flock -n 8").ExitCode == 0,
                TimeSpan.FromSeconds(10));
            Assert.True(tomado, "el titular nunca llegó a tomar el lock");

            var r = CorrerCorte();

            Assert.True(r.Exit == 3, r.Todo);
            Assert.Contains("migración", r.Salida);
            Assert.DoesNotContain("CONGELAR", r.Salida);
            // Quien pierde el lock no deja estado: no cuenta como corrida sin terminar.
            Assert.All(Corridas(), c => Assert.False(File.Exists(Path.Combine(c, "estado"))));
        }
        finally
        {
            titular.StandardInput.Close();
            if (!titular.WaitForExit(10000)) titular.Kill();
        }
    }

    // =====================================================================================
    // Cierre de la corrida y limpieza (R30)
    // =====================================================================================

    [Fact]
    public void UnFalloDelPrevuelo_CierraLaCorridaConResultadoFallo()
    {
        PonerViejo("login", "401\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        var corrida = UnaCorrida("corte");
        Assert.Equal("FALLO-3", ClaveDeEstado(corrida, "resultado"));
    }

    [Fact]
    public void Confirmacion_PalabraDistinta_CierraLaCorridaConFallo4()
    {
        CorrerCorte(stdin: "no\n");

        Assert.Equal("FALLO-4", ClaveDeEstado(UnaCorrida("corte"), "resultado"));
    }

    [Fact]
    public void Trap_EncadenaLaLimpiezaDeLaLib_NoQuedanTemporalesEnTmp()
    {
        // El pre-vuelo lee la identidad con ssh ANTES de que exista la corrida: ahí el stderr de ssh
        // va a un directorio privado que la lib borra en su trap EXIT. El trap propio de migrar tiene
        // que encadenar esa limpieza, no reemplazarla.
        var r = CorrerCorte(stdin: "no\n");

        Assert.True(r.Exit == 4, r.Todo);
        Assert.Empty(Directory.GetFileSystemEntries(Tmp));
    }

    // =====================================================================================
    // Alcance: el viejo es compartido (spec 5) y lo que migrar nunca hace
    // =====================================================================================

    private static Dictionary<string, string> Entorno(params (string Clave, string Valor)[] pares) =>
        pares.ToDictionary(p => p.Clave, p => p.Valor);

    [Fact]
    public void Corte_TodoComandoAlViejoEnListaBlanca()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        var cmds = ComandosRemotos(r.Rastro).ToList();
        Assert.NotEmpty(cmds.Where(c => c.Lado == "viejo"));
        foreach (var (_, cmd) in cmds.Where(c => c.Lado == "viejo"))
            Assert.True(LecturasViejo.Any(x => x.IsMatch(cmd)), $"comando al VPS viejo fuera de la lista blanca: {cmd}");
        // Nada que modifique ni que sea global: ni systemd, ni docker fuera de stockapp, ni el proxy de
        // la otra app, ni archivos. Lo único que congela el viejo es el bloque de volcado, aparte.
        Assert.DoesNotContain(cmds, c => c.Lado == "viejo" && Regex.IsMatch(c.Cmd,
            @"\b(restart|stop|start|mask|unmask|daemon-reload|reload|enable|disable|kill|rm|mv|cp|chmod|chown|tar|find|mkdir|tee|prune|compose|journalctl|sha256sum|scp)\b|>"));
        // Nada viaja al viejo por scp, y el volcado se llama con el .env del VIEJO.
        Assert.DoesNotContain("scp\tviejo", r.Rastro);
        Assert.Contains($"ENV={EnvViejo}\t", Assert.Single(LineasDeBloque(r.Rastro, "volcado")));
    }

    [Fact]
    public void Corte_NingunTmpFueraDeDockerExec()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        foreach (var (_, cmd) in ComandosRemotos(r.Rastro))
            if (!cmd.Contains("docker exec")) Assert.DoesNotContain("/tmp/", cmd);
        Assert.DoesNotContain("/tmp/", string.Join("\n", r.Rastro.Split('\n').Where(l => l.StartsWith("operador") || l.StartsWith("scp\t"))));
        Assert.Empty(Directory.GetFileSystemEntries(Tmp));
    }

    [Fact]
    public void Corte_NuncaDeployVps()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.DoesNotContain("deploy-vps", r.Rastro);
        Assert.DoesNotContain("deploy-vps", File.ReadAllText(Script));
    }

    [Fact]
    public void ElFeedSeLeeDesdeElOperador_ConLaUrlDelNuevo_SinInsecure()
    {
        var r = CorrerCorte();

        var feed = r.Rastro.Split('\n').Where(l => l.StartsWith("operador\tcurl ") && l.Contains("releases.win.json")).ToList();
        Assert.NotEmpty(feed);
        Assert.All(feed, l => Assert.Equal("operador\tcurl https://nuevo.test.invalid:8443/updates/releases.win.json", l));
        Assert.DoesNotMatch(@"curl[^\n]*\s-k\b|--insecure", File.ReadAllText(Script));
        Assert.DoesNotMatch(@"(?m)^operador-args\t.*(\s-k\b|--insecure)", r.Rastro);
    }

    // =====================================================================================
    // Secuencia del corte (spec 6.5.4)
    // =====================================================================================

    [Fact]
    public void Corte_Orden_VolcadoAntesQueRestaurarAntesQueStart()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        var volcado = Linea(r.Rastro, l => l.StartsWith("volcado\t"));
        var scp = Linea(r.Rastro, l => l.StartsWith("scp\tnuevo\t"));
        var restaurar = Linea(r.Rastro, l => l.StartsWith("restaurar\t") && !l.Contains("--verificar-post-arranque"));
        var start = Linea(r.Rastro, l => l == StartNuevo);
        var verificar = Linea(r.Rastro, l => l.StartsWith("restaurar\t") && l.Contains("--verificar-post-arranque"));
        Assert.True(volcado >= 0 && scp > volcado && restaurar > scp && start > restaurar && verificar > start, r.Todo);
    }

    [Fact]
    public void Corte_DetieneLaApiNuevaPrimero()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        var stop = Linea(r.Rastro, l => l == StopNuevo);
        // Antes de cualquier escritura en el viejo (el bloque de volcado) y de cualquier otra escritura.
        Assert.True(stop >= 0 && stop < Linea(r.Rastro, l => l.StartsWith("volcado\t")), r.Todo);
        Assert.Equal("nuevo: sudo -n systemctl stop stockapp-api", Escrituras(r.Rastro).First());
        // Y se confirma que quedó detenida antes de seguir.
        var isActive = Linea(r.Rastro, l => l == "nuevo\tsystemctl is-active stockapp-api");
        Assert.True(isActive > stop, r.Todo);
    }

    [Fact]
    public void Corte_ApiNuevaQueNoSeDetiene_Sale5SinTocarElViejo()
    {
        PonerNuevo("no-se-detiene", "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Empty(LineasDeBloque(r.Rastro, "volcado"));
        Assert.Equal("FALLO-5", ClaveDeEstado(UnaCorrida("corte"), "resultado"));
    }

    [Fact]
    public void Corte_CongeladoUtcSeRegistraAntesDelVolcado_YElRelojCorreDesdeAhi()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        // El bloque ya encontró congelado_utc en el estado cuando lo llamaron: su paso 2 detiene la API
        // en los primeros segundos.
        Assert.Contains("CONGELADO_PREVIO=si", Assert.Single(LineasDeBloque(r.Rastro, "volcado")));
        var corrida = UnaCorrida("corte");
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", ClaveDeEstado(corrida, "congelado_utc") ?? "");
        Assert.Matches(@"\( 6/13\) congelar y volcar el VPS viejo \.+ OK .*viejo sin API hace \d+ min \d+ s", r.Stdout);
        Assert.DoesNotMatch(@"\( 5/13\)[^\n]*viejo sin API", r.Stdout);
    }

    [Fact]
    public void Corte_ElViejoSeVerificaCongeladoAntesDeTransferir()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        var volcado = Linea(r.Rastro, l => l.StartsWith("volcado\t"));
        var enabled = Linea(r.Rastro, l => l == "viejo\tsystemctl is-enabled stockapp-api" );
        var habilitacionTrasVolcado = Linea(r.Rastro.Split('\n').Skip(volcado + 1).Aggregate("", (a, l) => a + l + "\n"),
            l => l == "viejo\tsystemctl is-enabled stockapp-api");
        var scp = Linea(r.Rastro, l => l.StartsWith("scp\t"));
        Assert.True(enabled >= 0 && habilitacionTrasVolcado >= 0 && volcado + 1 + habilitacionTrasVolcado < scp, r.Todo);
    }

    [Fact]
    public void Corte_VolcadoOkPeroElViejoSigueActivo_Sale6SinTransferir()
    {
        // Volcar sin congelar: el bloque dice 0 pero la API vieja sigue sirviendo. Restaurar eso
        // perdería toda escritura posterior al dump.
        var r = CorrerCorte(env: Entorno(("STUB_VOLCADO_CONGELA", "0")));

        Assert.True(r.Exit == 6, r.Todo);
        Assert.DoesNotContain("scp\t", r.Rastro);
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
        Assert.Null(ClaveDeEstado(UnaCorrida("corte"), "resultado"));
        Assert.Contains("--revertir", r.Salida);
    }

    [Fact]
    public void Corte_LimpiaRestosDelEnsayo_PorPatronYApartaLoSueltoEnLegado()
    {
        PonerNuevo("backups-existe", "");
        PonerNuevo("restos", "/var/lib/stockapp/.local/share/StockApp/backups/backup_20261001.dump\n");
        File.WriteAllText(Path.Combine(MigracionNuevo, "volcado-20261001T000000Z.dump"), "viejo");
        File.WriteAllText(Path.Combine(MigracionNuevo, "backups-historial.tar"), "viejo");
        Directory.CreateDirectory(Path.Combine(MigracionNuevo, "ensayo-20261001T000000Z"));

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.Contains(ComandosRemotos(r.Rastro), c => c.Lado == "nuevo" && c.Cmd ==
            "sudo -n find /var/lib/stockapp/.local/share/StockApp/backups -maxdepth 1 -type f \\( -name 'backup_*.dump' -o -name '*.tmp' \\) -print -delete");
        Assert.Contains("backup_20261001.dump", r.Stdout);
        var legado = Assert.Single(Directory.GetDirectories(MigracionNuevo, "legado-*"));
        Assert.True(File.Exists(Path.Combine(legado, "volcado-20261001T000000Z.dump")));
        Assert.True(File.Exists(Path.Combine(legado, "backups-historial.tar")));
        Assert.False(File.Exists(Path.Combine(MigracionNuevo, "backups-historial.tar")));
        // Las corridas del orquestador viven cada una en su directorio y no se mueven.
        Assert.True(Directory.Exists(Path.Combine(MigracionNuevo, "ensayo-20261001T000000Z")));
        Assert.DoesNotContain("rm -", string.Join("\n", ComandosRemotos(r.Rastro).Select(c => c.Cmd)));
    }

    [Fact]
    public void Corte_SinRestosNiLegado_InformaYaEstaba()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.Matches(@"\( 4/13\) limpiar restos del ensayo \.+ YA ESTABA", r.Stdout);
        Assert.Empty(Directory.GetDirectories(MigracionNuevo, "legado-*"));
        Assert.DoesNotContain(ComandosRemotos(r.Rastro), c => c.Cmd.StartsWith("sudo -n find "));
    }

    [Fact]
    public void Corte_ListaDeContenedoresDelViejo_SeGuardaOrdenadaEnLaCorrida()
    {
        PonerViejo("contenedores", "stockapp-pg\npinar-db\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.Equal("pinar-db\nstockapp-pg\n", File.ReadAllText(Path.Combine(UnaCorrida("corte"), "contenedores-viejo.txt")));
    }

    // ---------- A11: el volcado de ESTA corrida (spec 6.5.4 paso 7) ----------

    [Fact]
    public void Corte_DosVolcadosEnLaCarpeta_Sale6SinRestaurar()
    {
        var r = CorrerCorte(env: Entorno(("STUB_VOLCADO_EXTRA", "1")));

        AfirmarA11(r);
    }

    [Fact]
    public void Corte_FechaUtcAnteriorAlInicio_Sale6SinRestaurar()
    {
        var r = CorrerCorte(env: Entorno(("STUB_VOLCADO_FECHA", "2020-01-01T00:00:00Z")));

        AfirmarA11(r);
        Assert.Contains("fecha_utc", r.Salida);
    }

    [Fact]
    public void Corte_DumpDelManifiestoDistinto_Sale6SinRestaurar()
    {
        var r = CorrerCorte(env: Entorno(("STUB_VOLCADO_DUMP", "volcado-20200101T000000Z.dump")));

        AfirmarA11(r);
        Assert.Contains("dump=", r.Salida);
    }

    [Fact]
    public void Corte_ShaLocalDistinto_Sale6()
    {
        var r = CorrerCorte(env: Entorno(("STUB_VOLCADO_SHA", new string('0', 64))));

        AfirmarA11(r);
        Assert.Contains("sha256", r.Salida);
    }

    private void AfirmarA11(Resultado r)
    {
        Assert.True(r.Exit == 6, r.Todo);
        Assert.DoesNotContain("scp\t", r.Rastro);
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
        Assert.DoesNotContain(StartNuevo, r.Rastro);
        var corrida = UnaCorrida("corte");
        Assert.Null(ClaveDeEstado(corrida, "volcado"));
        Assert.Null(ClaveDeEstado(corrida, "paso_7"));
        Assert.Null(ClaveDeEstado(corrida, "resultado"));
    }

    [Fact]
    public void Corte_VolcadoIdentificado_SeGuardaYSeTransfiereVerificado()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        var corrida = UnaCorrida("corte");
        var volcado = ClaveDeEstado(corrida, "volcado") ?? "";
        Assert.Matches(@"^volcado-\d{8}T\d{6}Z\.dump$", volcado);
        var dumpLocal = Path.Combine(corrida, "volcado", volcado);
        Assert.Equal(Sha256(dumpLocal), ClaveDeEstado(corrida, "sha256"));

        // Directorio remoto NUEVO de esta corrida, 700, con los cuatro archivos y los permisos.
        var nombre = Path.GetFileName(corrida);
        Assert.Equal($"~/stockapp-deploy/migracion/{nombre}", ClaveDeEstado(corrida, "dir_remoto"));
        var remoto = Path.Combine(MigracionNuevo, nombre);
        Assert.Equal("700", Permisos(remoto));
        Assert.Equal("600", Permisos(Path.Combine(remoto, volcado)));
        Assert.Equal("600", Permisos(Path.Combine(remoto, volcado.Replace(".dump", ".manifiesto"))));
        Assert.Equal("600", Permisos(Path.Combine(remoto, "backups-historial.tar")));
        Assert.Equal("755", Permisos(Path.Combine(remoto, "restaurar-backup.sh")));
        Assert.Contains(ComandosRemotos(r.Rastro), c => c.Lado == "nuevo" && c.Cmd == $"sha256sum \"$HOME/stockapp-deploy/migracion/{nombre}/{volcado}\"");

        // Restaurar recibe ESE volcado y su manifiesto, con --recrear-base y la copia de seguridad.
        var restaurar = LineasDeBloque(r.Rastro, "restaurar").First();
        Assert.Contains($"/{nombre}/{volcado} --manifiesto ", restaurar);
        Assert.Contains($"/{nombre}/{volcado.Replace(".dump", ".manifiesto")} --recrear-base stockapp --dir-seguridad ", restaurar);
        Assert.EndsWith($"/{nombre}/seguridad", restaurar);
    }

    [Fact]
    public void Corte_DirRemotoQueYaExiste_Sale6SinPisarlo()
    {
        // mkdir sin -p: si el directorio ya existe, no se reutiliza ni se mezcla con lo que tenga.
        var r = CorrerCorte(env: Entorno(("STUB_PLANTAR_DIR_REMOTO", MigracionNuevo)));

        Assert.True(r.Exit == 6, r.Todo);
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
        Assert.DoesNotContain("scp\t", r.Rastro);
    }

    // ---------- Restauración (nota 2 de spec 6.5.4) ----------

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void Corte_RestaurarCon6o7_NoArrancaYSale6(int salida)
    {
        var r = CorrerCorte(env: Entorno(("STUB_RESTAURAR_EXIT", salida.ToString())));

        Assert.True(r.Exit == 6, r.Todo);
        Assert.DoesNotContain(StartNuevo, r.Rastro);
        Assert.Contains("--reintentar-restauracion", r.Salida);
        Assert.Contains("--revertir", r.Salida);
        Assert.Contains($"restaurar-backup.sh salió con {salida}", r.Salida);
        var corrida = UnaCorrida("corte");
        Assert.Null(ClaveDeEstado(corrida, "paso_9"));
        Assert.Null(ClaveDeEstado(corrida, "resultado"));
    }

    [Fact]
    public void Corte_RestaurarCon5_Sale6YContinuarLoReintenta()
    {
        var r = CorrerCorte(env: Entorno(("STUB_RESTAURAR_EXIT", "5")));

        Assert.True(r.Exit == 6, r.Todo);
        Assert.DoesNotContain(StartNuevo, r.Rastro);
        var corrida = UnaCorrida("corte");
        Assert.Matches(@"^ok ", ClaveDeEstado(corrida, "paso_8") ?? "");
        Assert.Null(ClaveDeEstado(corrida, "paso_9"));
        Assert.Equal("5", ClaveDeEstado(corrida, "restaurar_salio"));
        Assert.Null(ClaveDeEstado(corrida, "resultado"));
        Assert.Contains($"--continuar {corrida}", r.Salida);
    }

    [Theory]
    [InlineData(3, 6)]
    [InlineData(4, 6)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    [InlineData(2, 8)]
    public void Corte_RestaurarConOtroCodigo_SeClasificaSinArrancar(int salida, int esperado)
    {
        var r = CorrerCorte(env: Entorno(("STUB_RESTAURAR_EXIT", salida.ToString())));

        Assert.True(r.Exit == esperado, r.Todo);
        Assert.DoesNotContain(StartNuevo, r.Rastro);
        Assert.Null(ClaveDeEstado(UnaCorrida("corte"), "resultado"));
    }

    // ---------- Volcado (nota 1 de spec 6.5.4) ----------

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(2)]
    public void Corte_VolcadoFalla3AntesDelStop_Sale5ViejoActivo(int salida)
    {
        var r = CorrerCorte(env: Entorno(("STUB_VOLCADO_EXIT", salida.ToString()), ("STUB_VOLCADO_CONGELA", "0")));

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
        Assert.Contains($"volcado-final-vps.sh salió con {salida}", r.Salida);
        // R31: el 5 es "antes de congelar": la corrida se cierra aunque ya tenga congelado_utc.
        var corrida = UnaCorrida("corte");
        Assert.NotNull(ClaveDeEstado(corrida, "congelado_utc"));
        Assert.Equal("FALLO-5", ClaveDeEstado(corrida, "resultado"));
        Assert.Equal("active", File.ReadAllText(Host("viejo", "active")).Trim());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(4)]
    public void Corte_VolcadoFallaDespuesDelStop_Sale6(int salida)
    {
        var r = CorrerCorte(env: Entorno(("STUB_VOLCADO_EXIT", salida.ToString())));

        Assert.True(r.Exit == 6, r.Todo);
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
        var corrida = UnaCorrida("corte");
        Assert.Null(ClaveDeEstado(corrida, "resultado"));
        Assert.Contains($"--continuar {corrida}", r.Salida);
        Assert.Contains("--revertir", r.Salida);
        Assert.Contains("enmascarad", r.Salida);
    }

    [Fact]
    public void Corte_VolcadoSale9_Sale9SinOfrecerContinuar()
    {
        var r = CorrerCorte(env: Entorno(("STUB_VOLCADO_EXIT", "9")));

        Assert.True(r.Exit == 9, r.Todo);
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
        Assert.DoesNotContain("--continuar", r.Salida);
        Assert.Contains("--revertir", r.Salida);
        Assert.Null(ClaveDeEstado(UnaCorrida("corte"), "resultado"));
    }

    // ---------- Historial, arranque (pasos 10 y 11) ----------

    [Fact]
    public void Corte_Historial_SeExtraeYSeLeDaDuenoAStockapp()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        var cmds = ComandosRemotos(r.Rastro).Where(c => c.Lado == "nuevo").Select(c => c.Cmd).ToList();
        var nombre = Path.GetFileName(UnaCorrida("corte"));
        var tar = cmds.FindIndex(c => c == $"sudo -n tar -C /var/lib/stockapp/.local/share/StockApp -xpf \"$HOME/stockapp-deploy/migracion/{nombre}/backups-historial.tar\"");
        var chown = cmds.FindIndex(c => c == "sudo -n chown -R stockapp:stockapp /var/lib/stockapp/.local/share/StockApp/backups");
        Assert.True(tar >= 0 && chown > tar, r.Todo);
        Assert.True(chown < cmds.IndexOf("sudo -n systemctl start stockapp-api"), r.Todo);
    }

    [Fact]
    public void Corte_HistorialQueNoSeExtrae_Sale6SinArrancar()
    {
        PonerNuevo("falla-tar", "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 6, r.Todo);
        Assert.DoesNotContain(StartNuevo, r.Rastro);
    }

    [Fact]
    public void Corte_ApiNuevaQueQuedaFailed_Sale7ConElJournalSinVerificar()
    {
        PonerNuevo("al-arrancar", "failed\n");
        PonerNuevo("journal", "Unhandled exception. System.InvalidOperationException: boom-del-journal\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 7, r.Todo);
        Assert.Contains("boom-del-journal", r.Salida);
        Assert.DoesNotContain("/licencia/estado", r.Rastro);
        Assert.Null(ClaveDeEstado(UnaCorrida("corte"), "api_nueva_activa_utc"));
    }

    [Fact]
    public void Corte_ArranqueVerificaPorLoopbackConResolve()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.Contains(ComandosRemotos(r.Rastro), c => c.Lado == "nuevo" &&
            c.Cmd == "curl -fsS --max-time 10 -o /dev/null --resolve nuevo.test.invalid:8443:127.0.0.1 https://nuevo.test.invalid:8443/");
        Assert.NotNull(ClaveDeEstado(UnaCorrida("corte"), "api_nueva_activa_utc"));
    }

    // ---------- Verificación y resumen (pasos 12 y 13, spec 6.5.5) ----------

    public static TheoryData<string> VerificacionesQueBloquean() => new() { "V2", "V3", "V4", "V5", "V6", "V7", "V8", "V10" };

    private Dictionary<string, string>? RomperV(string v)
    {
        switch (v)
        {
            case "V2": PonerNuevo("migracion", "20250101000000_Anterior\n"); return null;
            case "V3": File.WriteAllText(Path.Combine(Estado, "v3"), "certificado-invalido\n"); return null;
            case "V4": File.WriteAllText(Path.Combine(Estado, "licencia"), "no\n"); return null;
            case "V5": File.WriteAllText(Path.Combine(Estado, "login-operador"), "401\n"); return null;
            case "V6": return Entorno(("STUB_V6_EXIT", "6"));
            case "V7": File.WriteAllText(Path.Combine(Estado, "nupkg"), "largo-distinto\n"); return null;
            case "V8": File.WriteAllText(Path.Combine(Estado, "contenido"), "404\n"); return null;
            case "V10": return Entorno(("STUB_VOLCADO_CONTENEDORES", "stockapp-pg\\npinar-db\\npinar-nuevo\\n"));
            default: throw new ArgumentOutOfRangeException(nameof(v));
        }
    }

    [Theory]
    [MemberData(nameof(VerificacionesQueBloquean))]
    public void Corte_FallaUnaV_Sale7NoGoConApiArriba(string v)
    {
        var env = RomperV(v);

        var r = CorrerCorte(env: env);

        Assert.True(r.Exit == 7, r.Todo);
        var corrida = UnaCorrida("corte");
        Assert.Equal("NO-GO", ClaveDeEstado(corrida, "resultado"));
        Assert.Matches($@"(?m)^\s*{v} .* FALLÓ", r.Stdout);
        Assert.Contains($"--revertir {corrida}", r.Stdout);
        // La API nueva queda arriba para diagnosticar: después del start no hay ningún stop.
        Assert.True(UltimaLinea(r.Rastro, l => l == StopNuevo) < Linea(r.Rastro, l => l == StartNuevo), r.Todo);
        Assert.NotNull(ClaveDeEstado(corrida, "fin_rollback"));
    }

    [Fact]
    public void Corte_TodoOk_Sale0GoConFinRollback()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        var corrida = UnaCorrida("corte");
        Assert.Equal("GO", ClaveDeEstado(corrida, "resultado"));
        Assert.Equal("resultado=GO", File.ReadAllLines(Path.Combine(corrida, "estado")).Last());
        for (var n = 1; n <= 13; n++)
            Assert.Matches(@"^ok \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z \d+$", ClaveDeEstado(corrida, $"paso_{n}") ?? $"(falta paso_{n})");

        var congelado = DateTime.Parse(ClaveDeEstado(corrida, "congelado_utc")!, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var fin = DateTime.Parse(ClaveDeEstado(corrida, "fin_rollback")!, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.Equal(congelado.AddDays(14), fin);
        Assert.Equal("42", ClaveDeEstado(corrida, "logs_auditoria_post_verificacion"));

        // Resumen: la tabla de V, lo pendiente, las fechas y el comando de vuelta atrás.
        foreach (var v in new[] { "V1", "V2", "V3", "V4", "V5", "V6", "V7", "V8", "V10" })
            Assert.Matches($@"(?m)^\s*{v} .* OK", r.Stdout);
        Assert.Matches(@"(?m)^\s*V9 .*PENDIENTE \(agente con scripts/gui-verificacion\)", r.Stdout);
        Assert.Contains("GO de Martin", r.Stdout);
        Assert.Contains("PCs", r.Stdout);
        Assert.Contains(ClaveDeEstado(corrida, "fin_rollback")!, r.Stdout);
        Assert.Contains($"deploy/migrar-vps.sh --revertir {corrida}", r.Stdout);
        Assert.Contains("GO técnico", r.Stdout);
    }

    [Fact]
    public void Corte_V8_DescargaLaCorridaMasViejaYEsperaUnaExitosaNueva()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        Assert.Equal("https://nuevo.test.invalid:8443/backups/5/contenido\n", File.ReadAllText(Path.Combine(Estado, "contenido-pedido.txt")));
        Assert.True(File.Exists(Path.Combine(Estado, "post-backups")));
    }

    [Fact]
    public void Corte_V8_LaCorridaNuevaFallida_EsNoGo()
    {
        File.WriteAllText(Path.Combine(Estado, "v8-resultado"), "Fallida\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 7, r.Todo);
        Assert.Matches(@"(?m)^\s*V8 .* FALLÓ", r.Stdout);
    }

    // ---------- Interrupción (R6): Ctrl-C después de congelar ----------

    [Fact]
    public async Task Corte_InterrumpidoTrasCongelar_EstadoSinResultadoYCorteNuevoSeNiega3()
    {
        var fifo = Path.Combine(_dir, "volcado.fifo");
        var pidArchivo = Path.Combine(_dir, "volcado.pid");
        Assert.Equal(0, BashLimpio.Ejecutar($"mkfifo {Sq(fifo)}").ExitCode);

        var corte = Task.Run(() => CorrerCorte(env: Entorno(("STUB_VOLCADO_FIFO", fifo), ("STUB_VOLCADO_PID", pidArchivo))));
        var listo = await EsperaMonotonica.HastaAsync(
            () => File.Exists(pidArchivo) && Regex.IsMatch(File.ReadAllText(pidArchivo), @"^\d+\s*$"),
            TimeSpan.FromSeconds(60));
        if (!listo)
        {
            // Destraba el bloque falso antes de fallar: si no, el Task.Run quedaría colgado.
            DestrabarFifo(fifo);
            Assert.Fail("el bloque de volcado nunca arrancó: " + (await corte).Todo);
        }

        var pid = File.ReadAllText(pidArchivo).Trim();
        // El SIGINT va al PID de migrar (el padre del bloque), y recién después se cierra el FIFO.
        Assert.Equal(0, BashLimpio.Ejecutar($"kill -INT {pid}").ExitCode);
        DestrabarFifo(fifo);
        var r = await corte;

        Assert.True(r.Exit == 130, r.Todo);
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
        Assert.DoesNotContain(StartNuevo, r.Rastro);
        var corrida = UnaCorrida("corte");
        Assert.NotNull(ClaveDeEstado(corrida, "congelado_utc"));
        Assert.Null(ClaveDeEstado(corrida, "resultado"));
        Assert.Equal(1, BashLimpio.Ejecutar($"ps -p {pid} >/dev/null").ExitCode);

        // El nombre de una corrida lleva el segundo UTC: una corrida nueva en el MISMO segundo se negaría
        // por "ya existe" (exit 3 por la razón equivocada). Se espera a que cambie el segundo.
        var ts = Path.GetFileName(corrida)["corte-".Length..];
        Assert.True(await EsperaMonotonica.HastaAsync(
            () => DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") != ts, TimeSpan.FromSeconds(30)));
        File.Delete(Rastro);
        var otro = CorrerCorte();

        Assert.True(otro.Exit == 3, otro.Todo);
        Assert.DoesNotContain("no puedo crear", otro.Salida);
        Assert.Contains($"--continuar {corrida}", otro.Salida);
        Assert.Contains("--revertir", otro.Salida);
        Assert.Empty(LineasDeBloque(otro.Rastro, "volcado"));
    }

    // Abre el FIFO en lectura+escritura (en Linux no bloquea aunque no haya lector) y lo cierra: el
    // lector que estaba esperando ve EOF. Nunca se cuelga si el bloque falso no llegó a abrirlo.
    private static void DestrabarFifo(string fifo) =>
        Assert.Equal(0, BashLimpio.Ejecutar($"exec 3<>{Sq(fifo)}; exec 3>&-").ExitCode);

    // ---------- Secretos (spec 6.3.5, R34) ----------

    [Fact]
    public void Corte_SinSecretosEnLaSalida()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitGo, r.Todo);
        // Controles positivos: la contraseña viajó por el stdin del login de V5 y el token por el stdin
        // de curl -K - de V8.
        Assert.Contains(Contrasena, File.ReadAllText(Path.Combine(Estado, "login-operador-stdin.txt")));
        Assert.Contains($"Bearer {TokenNuevo}", File.ReadAllText(Path.Combine(Estado, "backups-operador-stdin.txt")));

        var corrida = UnaCorrida("corte");
        var archivos = Directory.GetFiles(corrida, "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Tmp, "*", SearchOption.AllDirectories))
            .Select(File.ReadAllText);
        var todo = r.Stdout + r.Stderr + r.Rastro + File.ReadAllText(Path.Combine(Estado, "entorno-hijos.txt")) + string.Join("\n", archivos);
        foreach (var secreto in new[] { Contrasena, TokenNuevo, TokenViejo })
            Assert.DoesNotContain(secreto, todo);
    }

    [Fact]
    public void Corte_VariablesExportadasPorElOperador_NoLlevanSecretosALosHijos()
    {
        // R34: si el shell del operador ya exportaba PW, TOKEN o cualquier nombre que migrar usa para un
        // secreto, asignarlas sin unset conserva el export y el secreto llega al entorno de ssh, curl y
        // los bloques.
        var r = CorrerCorte(env: Entorno(("PW", "heredada"), ("TOKEN", "heredada"), ("cuerpo", "heredada"),
            ("listado", "heredada"), ("CURL_CUERPO", "heredada"), ("ORQ_SALIDA", "heredada")));

        Assert.True(r.Exit == ExitGo, r.Todo);
        var entorno = File.ReadAllText(Path.Combine(Estado, "entorno-hijos.txt"));
        Assert.Contains("ssh\t", entorno.Replace(Bin + "/", ""));
        foreach (var secreto in new[] { Contrasena, TokenNuevo, TokenViejo })
            Assert.DoesNotContain(secreto, entorno);
    }
}
