#!/usr/bin/env bash
# =======================================================================================
# deploy/lib/orquestacion.sh -- librería común de los orquestadores de la migración de VPS
# (preparar-vps-nuevo.sh y migrar-vps.sh). Se carga con `source`; no hace nada por sí sola.
# =======================================================================================
#
# Los orquestadores la cargan bajo `set -euo pipefail`. Reglas que la lib hace cumplir:
#
#  - Los bloques (volcado-final-vps.sh, restaurar-backup.sh, ...) son procesos hijos, NUNCA
#    `source`. El entorno de cada llamada se arma explícito (orq_bloque): la lib no exporta
#    STOCKAPP_ENV_LOCAL ni VPS_* para toda la sesión.
#  - "No pude consultar" nunca es "vacío": orq_ssh deja el código de ssh en ORQ_RC y no sale;
#    el 255 es siempre "no pude consultar" y el llamador lo clasifica.
#  - Un .env se lee línea por línea, nunca con `source`: un valor no puede ejecutar nada.
#  - Todo lo que viaja a un servidor y puede llevar un secreto va por stdin de ssh (3er
#    argumento de orq_ssh), nunca en el comando y nunca por un pipe hacia la función.
#
# Cómo se usa (y qué NO hay que hacer):
#
#  - `orq_dir_corrida` se llama SIN `$()`: define ORQ_CORRIDA y puede hacer `exit 3`, y las dos
#    cosas se pierden dentro de un subshell. Lo que imprime por stdout es solo informativo.
#  - Todo stdin hacia orq_ssh va por el 3er argumento. Con un pipe (`x | orq_ssh ...`) la
#    función corre en un subshell y ORQ_RC/ORQ_SALIDA se pierden en silencio.
#  - Orden de arranque de un orquestador: orq_dir_corrida -> orq_log_iniciar -> orq_lock. El
#    log va ANTES del lock: el `tee` del log no debe heredar el fd 9 del lock, o un proceso
#    que sobreviva a una interrupción mantendría tomado el lock.
#  - ORQ_ENSAYO vale 0 al cargar esta lib (se ignora lo que traiga el entorno). Solo el
#    orquestador lo pone en 1, y solo después de que orq_hosts_de_ensayo dio 0 sobre TODOS
#    los hosts. Con ORQ_ENSAYO=1 orq_confirmar lee de stdin y NUNCA de /dev/tty.
#  - ORQ_CONGELADO_SEG, si está definido, es el valor de $SECONDS en el instante del
#    congelamiento; cada línea de paso agrega cuánto lleva el viejo sin API.
#
# Códigos de salida de las funciones que abortan (los orquestadores los heredan):
#   2  uso / argumentos      3  precondición (config, HOME, lock, sin terminal)
#   4  el operador no confirmó
#
# Convención: las funciones con `exit` abortan el script que las llama, así que no se las
# invoca dentro de `$()` ni de un pipe. Variables y funciones internas llevan prefijo _ORQ_/_orq_.
# =======================================================================================

# Estado de carga: nada de lo que traiga el entorno cuenta (ver ORQ_ENSAYO arriba).
ORQ_ENSAYO=0
ORQ_CORRIDA=""
ORQ_RC=0
ORQ_SALIDA=""
ORQ_STDERR=""
ORQ_RC_BLOQUE=0
unset ORQ_CONGELADO_SEG

# Raíz del repo: la define el orquestador antes de cargar la lib; si no, se deriva de dónde vive.
if [[ -z "${REPO_ROOT:-}" ]]; then
    REPO_ROOT="$(cd "${BASH_SOURCE[0]%/*}/../.." && pwd -P)"
fi

# Ctrl-C o TERM: salir con 130. Sin esto, un bash no interactivo cuyo hijo no murió por la
# señal sigue ejecutando el script, y un corte seguiría después de la interrupción.
trap 'exit 130' INT TERM

# Directorio de temporales del operador (ver orq_tmp_dir). El trap EXIT lo borra al terminar, también
# tras un exit de error o una señal (que pasan por `exit 130`). Un orquestador que necesite su propio
# trap EXIT tiene que llamar a _orq_tmp_limpiar desde él: poner otro trap reemplaza a este.
ORQ_TMP_DIR=""
_ORQ_TMP_PRIVADO=""
_orq_tmp_limpiar() {
    # Solo el directorio que creó orq_tmp_dir (nunca el de la corrida, que no es de la lib), y sin
    # borrado recursivo: lo único que guarda son archivos sueltos de stderr.
    if [[ -n "$_ORQ_TMP_PRIVADO" ]]; then
        # Es limpieza al salir: si falla no hay nada más que hacer, y no puede cambiar el exit.
        rm -f -- "$_ORQ_TMP_PRIVADO"/* "$_ORQ_TMP_PRIVADO"/.[!.]* 2>/dev/null || :
        rmdir -- "$_ORQ_TMP_PRIVADO" 2>/dev/null || :
    fi
    return 0
}
trap '_orq_tmp_limpiar' EXIT

_ORQ_PASO_N=""
_ORQ_PASO_TOTAL=""
_ORQ_PASO_NOMBRE=""
_ORQ_PASO_T0=0

# ---------------------------------------------------------------------------------------
# Configuración
# ---------------------------------------------------------------------------------------

# orq_leer_env <archivo> <PREFIJO> <CLAVE>...
# Define <PREFIJO>_<CLAVE> por cada clave presente (la última línea gana). Nunca ejecuta el
# archivo. Un valor con comillas o CR se rechaza nombrando SOLO la clave: el valor puede ser
# un secreto y no se imprime.
orq_leer_env() {
    local archivo="${1:-}" prefijo="${2:-}" clave lineas valor
    if [[ $# -lt 3 || ! "$prefijo" =~ ^[A-Z][A-Z0-9_]*$ ]]; then
        echo "orq_leer_env: uso: orq_leer_env <archivo> <PREFIJO> <CLAVE>..." >&2
        exit 2
    fi
    shift 2
    if [[ ! -f "$archivo" || ! -r "$archivo" ]]; then
        echo "configuración: no puedo leer $archivo" >&2
        exit 3
    fi
    for clave in "$@"; do
        if [[ ! "$clave" =~ ^[A-Z][A-Z0-9_]*$ ]]; then
            echo "orq_leer_env: clave inválida '$clave'" >&2
            exit 2
        fi
        # Lo que haya en el entorno no cuenta: una clave que el archivo no trae queda sin definir.
        unset "${prefijo}_${clave}"
        lineas="$(sed -n "/^${clave}=/p" "$archivo")" || {
            echo "configuración: no puedo leer $archivo" >&2
            exit 3
        }
        if [[ -z "$lineas" ]]; then
            continue
        fi
        valor="${lineas##*$'\n'}"
        valor="${valor#*=}"
        if [[ "$valor" == *$'\r'* || "$valor" == [\"\']* ]]; then
            echo "configuración: $archivo: el valor de $clave tiene comillas o fin de línea CRLF" >&2
            exit 3
        fi
        printf -v "${prefijo}_${clave}" '%s' "$valor"
    done
}

# orq_hosts_de_ensayo <host>... -> 0 si TODOS terminan en .invalid (los del arnés de ensayo).
orq_hosts_de_ensayo() {
    local host
    if [[ $# -eq 0 ]]; then
        return 1
    fi
    for host in "$@"; do
        if [[ "$host" != *.invalid ]]; then
            return 1
        fi
    done
    return 0
}

# ---------------------------------------------------------------------------------------
# Temporales del operador
# ---------------------------------------------------------------------------------------

# orq_tmp_dir -> define ORQ_TMP_DIR, el directorio donde van los temporales de esta máquina (el
# stderr de ssh, el de curl). Con una corrida (ORQ_CORRIDA, 700) son los de la corrida. Sin
# corrida (preparar no tiene) es un `mktemp -d` propio, siempre 0700, que se crea la primera vez y
# se borra al salir (trap EXIT): así ni un temporal suelto en /tmp ni un directorio compartido. Se
# llama SIN `$()`: lo que define se pierde dentro de un subshell.
orq_tmp_dir() {
    local dir
    if [[ -n "${ORQ_CORRIDA:-}" ]]; then
        ORQ_TMP_DIR="$ORQ_CORRIDA"
        return 0
    fi
    if [[ -n "$_ORQ_TMP_PRIVADO" && -d "$_ORQ_TMP_PRIVADO" ]]; then
        ORQ_TMP_DIR="$_ORQ_TMP_PRIVADO"
        return 0
    fi
    if ! dir="$(mktemp -d "${TMPDIR:-/tmp}/stockapp-orq.XXXXXX")"; then
        ORQ_TMP_DIR=""
        echo "orq_tmp_dir: no pude crear el directorio de temporales" >&2
        return 1
    fi
    chmod 700 -- "$dir"
    _ORQ_TMP_PRIVADO="$dir"
    ORQ_TMP_DIR="$dir"
}

# ---------------------------------------------------------------------------------------
# ssh
# ---------------------------------------------------------------------------------------

# _orq_ssh_correr <PREFIJO> <comando> <tiene_entrada 0|1> <entrada> [opciones de ssh...]
# Deja ORQ_RC, ORQ_SALIDA y ORQ_STDERR. Nunca sale. Un 255 (o una configuración incompleta)
# deja ORQ_SALIDA vacía: lo que haya salido junto con un 255 no es una respuesta.
# El stderr va a un temporal local (ORQ_TMP_DIR: la corrida si existe, si no un directorio 0700
# propio que se borra al salir), jamás al servidor, y se borra al leerlo. El fd 9 (lock) se cierra para ssh: un ControlMaster que sobreviva al script
# no puede dejar tomado el lock.
_orq_ssh_correr() {
    local p="$1" comando="$2" tiene_entrada="$3" entrada="$4" tmp
    shift 4
    local nombre_host="${p}_VPS_HOST" nombre_usuario="${p}_VPS_USER" nombre_puerto="${p}_VPS_SSH_PORT"
    local host="${!nombre_host-}" usuario="${!nombre_usuario-}" puerto="${!nombre_puerto-}"

    ORQ_RC=0
    ORQ_SALIDA=""
    ORQ_STDERR=""
    if [[ -z "$host" || -z "$usuario" || -z "$puerto" ]]; then
        ORQ_RC=255
        ORQ_STDERR="orq_ssh: falta ${nombre_host}, ${nombre_usuario} o ${nombre_puerto}"
        return 0
    fi
    if ! orq_tmp_dir 2>/dev/null || ! tmp="$(mktemp "$ORQ_TMP_DIR/ssh-stderr.XXXXXX")"; then
        ORQ_RC=255
        ORQ_STDERR="orq_ssh: no pude crear el temporal para el stderr"
        return 0
    fi

    if [[ "$tiene_entrada" == 1 ]]; then
        ORQ_SALIDA="$(ssh -p "$puerto" -o BatchMode=yes "$@" "$usuario@$host" "$comando" \
            9>&- 2>"$tmp" < <(printf '%s' "$entrada"))" || ORQ_RC=$?
    else
        ORQ_SALIDA="$(ssh -p "$puerto" -o BatchMode=yes "$@" "$usuario@$host" "$comando" \
            9>&- 2>"$tmp" </dev/null)" || ORQ_RC=$?
    fi

    ORQ_STDERR="$(cat "$tmp" 2>/dev/null)" || ORQ_STDERR=""
    rm -f -- "$tmp"
    if [[ "$ORQ_RC" -eq 255 ]]; then
        ORQ_SALIDA=""
    fi
    return 0
}

# orq_ssh <PREFIJO> <comando> [<stdin>]
# ssh -p "$<P>_VPS_SSH_PORT" -o BatchMode=yes "$<P>_VPS_USER@$<P>_VPS_HOST" "$comando"
# Sin <stdin> explícito usa /dev/null: nunca lee el stdin de la sesión.
orq_ssh() {
    if [[ $# -lt 2 || $# -gt 3 ]]; then
        echo "orq_ssh: uso: orq_ssh <PREFIJO> <comando> [<stdin>]" >&2
        return 2
    fi
    if [[ $# -eq 3 ]]; then
        _orq_ssh_correr "$1" "$2" 1 "$3"
    else
        _orq_ssh_correr "$1" "$2" 0 ""
    fi
}

# orq_ssh_nueva_conexion <PREFIJO> <comando>
# Igual, pero sin reutilizar un maestro de conexión: para comprobar que el acceso funciona
# desde cero (p. ej. después de cambiar el firewall o los grupos del usuario).
orq_ssh_nueva_conexion() {
    if [[ $# -ne 2 ]]; then
        echo "orq_ssh_nueva_conexion: uso: orq_ssh_nueva_conexion <PREFIJO> <comando>" >&2
        return 2
    fi
    _orq_ssh_correr "$1" "$2" 0 "" -o ControlMaster=no -o ControlPath=none -o ConnectTimeout=10
}

# ---------------------------------------------------------------------------------------
# Corrida: directorio, log, estado y lock
# ---------------------------------------------------------------------------------------

# orq_dir_corrida <modo>
# Crea ~/stockapp-backups-vps/corridas/<modo>-<AAAAMMDDTHHMMSSZ> (700; falla si ya existe),
# define ORQ_CORRIDA y lo imprime. Exit 3 si HOME resuelve dentro del repo o bajo /mnt: los
# volcados llevan datos del municipio y no pueden terminar en un checkout ni en el disco de
# Windows (permisos que no se pueden restringir).
orq_dir_corrida() {
    local modo="${1:-}" home_real repo_real base dir ts
    case "$modo" in
        ensayo|corte|desde-backup|revertir) ;;
        *)
            echo "orq_dir_corrida: modo inválido '$modo'" >&2
            exit 2
            ;;
    esac
    if [[ -z "${HOME:-}" ]]; then
        echo "HOME no está definido: no sé dónde guardar la corrida" >&2
        exit 3
    fi
    home_real="$(realpath -m -- "$HOME")"
    repo_real="$(realpath -m -- "$REPO_ROOT")"
    case "$home_real" in
        /mnt|/mnt/*)
            echo "HOME ($HOME) resuelve bajo /mnt: las corridas llevan datos y ahí no se puede restringir el acceso" >&2
            exit 3
            ;;
    esac
    case "$home_real" in
        "$repo_real"|"$repo_real"/*)
            echo "HOME ($HOME) resuelve dentro del repo: las corridas no pueden quedar en un checkout" >&2
            exit 3
            ;;
    esac

    base="$HOME/stockapp-backups-vps/corridas"
    # umask 077 solo para lo que se crea acá: lo que ya existía conserva su modo.
    ( umask 077; mkdir -p -- "$base" ) || {
        echo "no puedo crear $base" >&2
        exit 3
    }
    ts="$(date -u +%Y%m%dT%H%M%SZ)"
    dir="$base/$modo-$ts"
    if ! mkdir -m 700 -- "$dir" 2>/dev/null; then
        echo "no puedo crear $dir (¿ya existe?): no se reutiliza una corrida" >&2
        exit 3
    fi
    ORQ_CORRIDA="$dir"
    printf '%s\n' "$dir"
}

# orq_log_iniciar -> todo lo que sigue (stdout y stderr) sale por pantalla Y va a
# <corrida>/registro.log (600). Se llama antes de orq_lock (ver el encabezado).
orq_log_iniciar() {
    local log
    if [[ -z "${ORQ_CORRIDA:-}" || ! -d "$ORQ_CORRIDA" ]]; then
        echo "orq_log_iniciar: no hay corrida (llamá antes a orq_dir_corrida)" >&2
        exit 2
    fi
    log="$ORQ_CORRIDA/registro.log"
    ( umask 077; : >> "$log" )
    exec > >(tee -a "$log" 9>&-) 2>&1
}

# _orq_estado_editar <poner|quitar> <clave> [<valor>]
# Reescribe <corrida>/estado en un temporal del MISMO directorio y lo mueve encima: un corte
# de luz o un Ctrl-C no dejan un estado a medias. La clave existente se reemplaza en su lugar;
# una nueva se agrega al final, pero `resultado` es SIEMPRE la última línea (una corrida sin
# esa clave está sin terminar).
_orq_estado_editar() {
    local accion="$1" clave="$2" valor="${3-}" archivo tmp linea resultado="" tiene_resultado=0 puesto=0
    if [[ -z "${ORQ_CORRIDA:-}" || ! -d "$ORQ_CORRIDA" ]]; then
        echo "orq_estado: no hay corrida (llamá antes a orq_dir_corrida)" >&2
        return 2
    fi
    if [[ ! "$clave" =~ ^[a-z0-9_]+$ ]]; then
        echo "orq_estado: clave inválida '$clave'" >&2
        return 2
    fi
    if [[ "$valor" == *$'\n'* ]]; then
        echo "orq_estado: el valor de $clave tiene un salto de línea" >&2
        return 2
    fi
    archivo="$ORQ_CORRIDA/estado"
    tmp="$(mktemp "$ORQ_CORRIDA/.estado.XXXXXX")" || return 1
    {
        if [[ -f "$archivo" ]]; then
            while IFS= read -r linea || [[ -n "$linea" ]]; do
                if [[ "$linea" == "$clave="* ]]; then
                    if [[ "$accion" == poner && "$puesto" -eq 0 ]]; then
                        printf '%s=%s\n' "$clave" "$valor"
                        puesto=1
                    fi
                elif [[ "$linea" == resultado=* ]]; then
                    resultado="$linea"
                    tiene_resultado=1
                else
                    printf '%s\n' "$linea"
                fi
            done < "$archivo"
        fi
        if [[ "$accion" == poner && "$puesto" -eq 0 ]]; then
            printf '%s=%s\n' "$clave" "$valor"
        fi
        if [[ "$tiene_resultado" -eq 1 ]]; then
            printf '%s\n' "$resultado"
        fi
    } > "$tmp" || {
        rm -f -- "$tmp"
        return 1
    }
    mv -f -- "$tmp" "$archivo" || {
        rm -f -- "$tmp"
        return 1
    }
}

# orq_estado_poner <clave> <valor>   clave ^[a-z0-9_]+$, valor sin salto de línea.
orq_estado_poner() {
    if [[ $# -ne 2 ]]; then
        echo "orq_estado_poner: uso: orq_estado_poner <clave> <valor>" >&2
        return 2
    fi
    _orq_estado_editar poner "$1" "$2"
}

# orq_estado_quitar <clave>   (idempotente; --continuar quita `resultado` al empezar).
orq_estado_quitar() {
    if [[ $# -ne 1 ]]; then
        echo "orq_estado_quitar: uso: orq_estado_quitar <clave>" >&2
        return 2
    fi
    _orq_estado_editar quitar "$1"
}

# orq_estado_de <archivo-estado> <clave> -> el valor por stdout.
# rc 1: la clave no está. rc 2: no pude leer el archivo (no es lo mismo que "no está").
orq_estado_de() {
    local archivo="${1:-}" clave="${2:-}" linea valor="" hallada=1
    if [[ ! "$clave" =~ ^[a-z0-9_]+$ ]]; then
        echo "orq_estado_de: clave inválida '$clave'" >&2
        return 2
    fi
    if [[ ! -f "$archivo" || ! -r "$archivo" ]]; then
        echo "orq_estado_de: no puedo leer $archivo" >&2
        return 2
    fi
    while IFS= read -r linea || [[ -n "$linea" ]]; do
        if [[ "$linea" == "$clave="* ]]; then
            valor="${linea#"$clave="}"
            hallada=0
        fi
    done < "$archivo"
    if [[ "$hallada" -ne 0 ]]; then
        return 1
    fi
    printf '%s\n' "$valor"
}

# orq_estado_leer <clave> -> orq_estado_de sobre la corrida actual.
orq_estado_leer() {
    if [[ $# -ne 1 ]]; then
        echo "orq_estado_leer: uso: orq_estado_leer <clave>" >&2
        return 2
    fi
    orq_estado_de "${ORQ_CORRIDA:-}/estado" "$1"
}

# orq_lock -> flock -n sobre ~/stockapp-backups-vps/.migracion.lock en el fd 9. Se libera
# solo cuando termina el proceso. Tomado -> exit 3.
orq_lock() {
    local dir="$HOME/stockapp-backups-vps"
    ( umask 077; mkdir -p -- "$dir" ) || {
        echo "no puedo crear $dir" >&2
        exit 3
    }
    exec 9>>"$dir/.migracion.lock"
    if ! flock -n 9; then
        echo "hay otra migración en curso (lock tomado: $dir/.migracion.lock). No se tocó nada." >&2
        exit 3
    fi
}

# ---------------------------------------------------------------------------------------
# Salida para el operador
# ---------------------------------------------------------------------------------------

# orq_paso <n> <total> <nombre>   abre un paso (no imprime: la línea sale al cerrarlo).
orq_paso() {
    if [[ $# -ne 3 || ! "$1" =~ ^[0-9]+$ || ! "$2" =~ ^[0-9]+$ ]]; then
        echo "orq_paso: uso: orq_paso <n> <total> <nombre>" >&2
        return 2
    fi
    _ORQ_PASO_N="$1"
    _ORQ_PASO_TOTAL="$2"
    _ORQ_PASO_NOMBRE="$3"
    _ORQ_PASO_T0="$SECONDS"
}

# orq_fin_paso <OK|YA ESTABA|OMITIDO|FALLÓ> [detalle]
# [14:02:11] ( 4/12) volcado del VPS viejo ........................ OK      41 s
orq_fin_paso() {
    local resultado="${1:-}" detalle="${2-}" duracion puntos relleno hora linea reloj
    case "$resultado" in
        OK|"YA ESTABA"|OMITIDO|FALLÓ) ;;
        *)
            echo "orq_fin_paso: resultado inválido '$resultado'" >&2
            return 2
            ;;
    esac
    if [[ -z "$_ORQ_PASO_NOMBRE" ]]; then
        echo "orq_fin_paso: no hay un paso abierto (llamá antes a orq_paso)" >&2
        return 2
    fi
    if [[ -n "${ORQ_CONGELADO_SEG-}" && ! "$ORQ_CONGELADO_SEG" =~ ^[0-9]+$ ]]; then
        echo "orq_fin_paso: ORQ_CONGELADO_SEG no es un número de segundos" >&2
        return 2
    fi

    duracion=$((SECONDS - _ORQ_PASO_T0))
    puntos=$((45 - ${#_ORQ_PASO_NOMBRE}))
    if [[ "$puntos" -lt 3 ]]; then
        puntos=3
    fi
    printf -v relleno '%*s' "$puntos" ''
    relleno="${relleno// /.}"
    printf -v hora '%(%H:%M:%S)T' -1
    printf -v linea '[%s] (%*d/%d) %s %s %-6s %3d s' \
        "$hora" "${#_ORQ_PASO_TOTAL}" "$_ORQ_PASO_N" "$_ORQ_PASO_TOTAL" \
        "$_ORQ_PASO_NOMBRE" "$relleno" "$resultado" "$duracion"
    if [[ -n "$detalle" ]]; then
        linea+="  ($detalle)"
    fi
    if [[ -n "${ORQ_CONGELADO_SEG-}" ]]; then
        reloj=$((SECONDS - ORQ_CONGELADO_SEG))
        linea+="  viejo sin API hace $((reloj / 60)) min $((reloj % 60)) s"
    fi
    printf '%s\n' "$linea"
    _ORQ_PASO_NOMBRE=""
}

# ---------------------------------------------------------------------------------------
# Confirmación y bloques
# ---------------------------------------------------------------------------------------

# orq_confirmar <palabra> <texto>
# Imprime el texto y lee UNA línea, que tiene que ser exactamente <palabra> (sin recortar, sin
# pasar a mayúsculas). De /dev/tty; sin terminal -> exit 3 (un corte no se lanza desatendido).
# Con ORQ_ENSAYO=1 (hosts .invalid, ver el encabezado) lee de stdin y nunca toca /dev/tty.
# Cualquier otra respuesta -> exit 4.
orq_confirmar() {
    local palabra="${1:-}" texto="${2-}" respuesta=""
    if [[ -z "$palabra" ]]; then
        echo "orq_confirmar: uso: orq_confirmar <palabra> <texto>" >&2
        exit 2
    fi
    printf '%s\n' "$texto"
    if [[ "$ORQ_ENSAYO" == 1 ]]; then
        if ! IFS= read -r respuesta && [[ -z "$respuesta" ]]; then
            respuesta=""
        fi
    elif { : </dev/tty; } 2>/dev/null; then
        if ! IFS= read -r respuesta </dev/tty && [[ -z "$respuesta" ]]; then
            respuesta=""
        fi
    else
        echo "no hay una terminal para confirmar: esta operación no se lanza desatendida. No se tocó nada." >&2
        exit 3
    fi
    if [[ "$respuesta" != "$palabra" ]]; then
        echo "no confirmado (había que escribir exactamente $palabra). No se tocó nada." >&2
        exit 4
    fi
}

# orq_bloque <ruta-relativa> <archivo-env> <args>...
# Corre STOCKAPP_ENV_LOCAL=<archivo-env> "$REPO_ROOT/<ruta>" <args> como PROCESO HIJO, con la
# salida (stdout y stderr juntos) en vivo y sangrada. El código del bloque queda en
# ORQ_RC_BLOQUE; el llamador lo clasifica, así que acá no se sale por un exit distinto de cero:
# se apaga errexit solo alrededor del pipeline y se deja como se lo encontró. El fd 9 (lock) se
# cierra en el hijo, y su stdin es /dev/null: ningún bloque lee stdin, y con ORQ_ENSAYO=1 un
# hijo podría consumir la palabra de confirmación que el orquestador todavía tiene que leer.
orq_bloque() {
    local ruta="${1:-}" archivo_env="${2:-}" tenia_errexit=0 estados
    if [[ $# -lt 2 ]]; then
        echo "orq_bloque: uso: orq_bloque <ruta-relativa> <archivo-env> <args>..." >&2
        return 2
    fi
    shift 2
    if [[ $- == *e* ]]; then
        tenia_errexit=1
    fi
    set +e
    STOCKAPP_ENV_LOCAL="$archivo_env" "$REPO_ROOT/$ruta" "$@" </dev/null 9>&- 2>&1 | sed -u 's/^/    │ /'
    estados=("${PIPESTATUS[@]}")
    if [[ "$tenia_errexit" -eq 1 ]]; then
        set -e
    fi
    ORQ_RC_BLOQUE="${estados[0]}"
}
