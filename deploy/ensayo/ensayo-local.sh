#!/usr/bin/env bash
# =======================================================================================
# deploy/ensayo/ensayo-local.sh -- ensayo LOCAL de la migración: el VPS viejo simulado en la
# máquina del operador, para correr volcado-final-vps.sh y restaurar-backup.sh REALES de punta
# a punta sin tocar ningún servidor.
# =======================================================================================
#
# Uso:
#   deploy/ensayo/ensayo-local.sh preparar <dump-real> --trabajo <dir>
#                                 [--historial <backup_*.dump>]... [--sufijo <s>] [--puerto <n>]
#   deploy/ensayo/ensayo-local.sh volcar    --trabajo <dir>
#   deploy/ensayo/ensayo-local.sh restaurar --trabajo <dir> [--recrear-base]
#   deploy/ensayo/ensayo-local.sh limpiar   --trabajo <dir>
#
#   Dos hosts (el ensayo de migrar-vps.sh de punta a punta):
#   deploy/ensayo/ensayo-local.sh preparar-migracion --trabajo <dir> [--dump <dump-real>]
#                                 [--sufijo <s>] [--puerto-viejo <n>] [--puerto-nuevo <n>]
#   deploy/ensayo/ensayo-local.sh migrar    --trabajo <dir> -- <argumentos de migrar-vps.sh>
#
#   --trabajo    directorio del ensayo (700). 'preparar' y 'preparar-migracion' lo exigen vacío o
#                inexistente. Nunca dentro del repo, bajo /mnt/ (ahí no rigen los permisos), ni el
#                HOME entero o uno de sus padres: exit 2.
#   --historial  backups que hacen de historial del VPS viejo (repetible).
#   --sufijo     ^[a-z0-9]{0,12}$, default vacío: se agrega como -<s> / _<s> a los nombres de
#                abajo, para que dos ensayos (o los tests) no choquen.
#   --puerto     default 5434: el destino publica 127.0.0.1:<puerto>.
#   --dump       el viejo arranca con ese dump real restaurado (si no, con la base que migra y
#                siembra su primer arranque). Con --dump, VPS_ADMIN_USER tiene que venir en el
#                entorno (es el usuario real de verificación) y la contraseña, VPS_ADMIN_PASSWORD,
#                en el entorno de 'migrar': nunca se guarda.
#   --puerto-viejo, --puerto-nuevo  defaults 5291 y 5292: las APIs del viejo y del nuevo atienden
#                en 127.0.0.1:<n>.
#   --sufijo y los puertos se dan en 'preparar' o 'preparar-migracion', que los guardan en
#   <trabajo>/config; los demás subcomandos los leen de ahí.
#
# Qué hace cada subcomando:
#   preparar   aborta si ya existe algún contenedor o volumen del ensayo (nunca reutiliza uno)
#              o si el puerto está ocupado; guarda la lista de contenedores corriendo; levanta
#              el origen (el "VPS viejo", sin puertos publicados) y el destino (el "VPS
#              nuevo"), los dos postgres:16-alpine con contraseña aleatoria
#              (<trabajo>/pg-password, 600); carga el dump real en el origen con
#              restaurar-backup.sh; arma la raíz del VPS viejo (<trabajo>/vps-viejo: historial,
#              api.env y la unit como archivo regular, como la deja install.sh), el estado de la
#              API (active/enabled) y el .env del operador apuntando a vps-viejo.ensayo.invalid.
#              Si falla después de escribir la config, borra al salir (por nombre exacto) los
#              contenedores y volúmenes de este sufijo que se alcanzaron a crear.
#   volcar     volcado-final-vps.sh en modo FINAL (stop + mask) contra el origen, con los shims
#              de ssh/sudo/systemctl al frente del PATH (el ssh falso rechaza todo host que no
#              sea *.invalid).
#   restaurar  restaurar-backup.sh del volcado sobre el destino, con su manifiesto y SIN shims:
#              el systemctl real no conoce la unit en esta máquina y cuenta como inactiva.
#   preparar-migracion
#              los dos hosts de migrar-vps.sh, en <trabajo>/hosts/<host>/{estado,raiz,home}, con
#              vps-viejo.ensayo.invalid y vps-nuevo.ensayo.invalid: publica la API una vez
#              (dotnet publish a <trabajo>/api), levanta los mismos dos Postgres que 'preparar'
#              (publicados en un puerto libre de 127.0.0.1) y lanza una API real en cada host
#              con el systemctl falso. El viejo, con la base migrada y el admin sembrado por su
#              primer arranque (contraseña aleatoria en <trabajo>/admin-password, 600) y un backup
#              hecho por la API (el historial), o con el --dump restaurado. El nuevo, en el estado
#              que deja preparar-vps-nuevo.sh: base migrada por un primer arranque, feed mínimo,
#              unit, directorios y ~/stockapp-deploy/.env. Cada host con su /etc/machine-id. Los
#              .env del operador, <trabajo>/env-vps-viejo-ensayo y env-vps-nuevo-ensayo, apuntan a
#              los hosts .invalid con API_SCHEME=http y API_HOSTNAME=127.0.0.1. Si falla después
#              de escribir la config, mata las APIs y borra los contenedores que alcanzó a crear.
#   migrar     corre migrar-vps.sh REAL contra los dos hosts: los shims al frente del PATH, el
#              HOME del operador en <trabajo>/operador (corridas, lock y ~/stockapp-backups-vps
#              quedan ahí), los .env del ensayo, --contenedor-*, --raiz-remota-* y
#              --sin-verificar-preparacion según el modo, y su stdin pasa tal cual (las palabras
#              de confirmación). Devuelve el código de migrar-vps.sh.
#   limpiar    detiene las APIs de los hosts (por su PID exacto, verificado con ps), borra los dos
#              contenedores (con sus volúmenes) por sus nombres EXACTOS y compara la lista de
#              contenedores con la de 'preparar'.
#
# Nombres (con sufijo s): contenedores stockapp-ensayo-origen-pg-<s> y stockapp-ensayo-pg-<s>;
# volúmenes stockapp_ensayo_origen_pgdata_<s> y stockapp_ensayo_pgdata_<s>. El script no nombra
# ningún otro contenedor ni volumen: en esta máquina corren el de desarrollo y los de otras
# aplicaciones.
#
# El directorio de trabajo queda con una COPIA DE LOS DATOS del dump: 'limpiar' no lo borra
# (el operador puede querer revisar la salida); borralo a mano al terminar. 'preparar' (al
# terminar bien) y 'limpiar' (salga como salga) recuerdan la ruta y cuánto ocupa.
#
# Códigos de salida:
#   0  OK
#   2  uso / argumentos inválidos
#   3  precondición: contenedor o volumen del ensayo ya existente, puerto ocupado, directorio
#      de trabajo no vacío o sin preparar (o preparado para el otro tipo de ensayo), volcado
#      ausente o repetido
#   8  no se pudo consultar o crear (docker con error, Postgres o una API que no arranca, una
#      API que no se pudo detener)
#   9  alcance: al limpiar, la lista de contenedores no es la de antes de preparar
#   volcar, restaurar y migrar devuelven el código del script que corren.
# =======================================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIR_SHIMS="${REPO_ROOT}/deploy/ensayo/shims"

uso() {
    sed -n '/^# Uso:/,/subcomandos los leen de ahí/p' "$0" | sed 's/^# \{0,1\}//' >&2
}

error_uso() {
    echo "ERROR: $1" >&2
    uso
    exit 2
}

SUBCOMANDO=""
INICIO=$SECONDS
TRABAJO=""
CONFIG_ESCRITA=0

# recordar_datos: dónde quedan los datos del ensayo y cuánto ocupan. El directorio de trabajo
# tiene una copia de los datos del dump (en el corte, datos REALES de producción).
recordar_datos() {
    [[ -n "$TRABAJO" && -d "$TRABAJO" ]] || return 0
    local tamano
    tamano="$(du -sh -- "$TRABAJO" 2>/dev/null | cut -f1)" || tamano=""
    [[ -n "$tamano" ]] || tamano="tamaño desconocido: du falló"
    echo "Datos del ensayo en '${TRABAJO}' (${tamano}): tienen una copia de los datos del dump, borralo a mano al terminar."
}

al_salir() {
    local rc=$?
    trap - EXIT
    # preparar que falla después de escribir la config: se borra lo que se alcanzó a crear (y, con
    # dos hosts, se detienen las APIs que se alcanzaron a lanzar).
    if [[ "$SUBCOMANDO" == preparar* && "$rc" -ne 0 && "$CONFIG_ESCRITA" -eq 1 ]]; then
        local apis=0
        detener_apis || apis=$?
        if [[ "$apis" -eq 0 ]] && borrar_recursos; then
            echo "  preparar falló: se borraron los contenedores y volúmenes de este ensayo." >&2
        else
            echo "ATENCIÓN: no se pudo borrar todo; corré: $0 limpiar --trabajo ${TRABAJO}" >&2
        fi
    fi
    # limpiar recuerda los datos en TODOS sus caminos de salida, también con error.
    [[ "$SUBCOMANDO" != "limpiar" ]] || recordar_datos
    [[ -z "$SUBCOMANDO" ]] || echo "ensayo-local ${SUBCOMANDO}: $(( SECONDS - INICIO )) s"
    exit "$rc"
}
trap al_salir EXIT

# ---------------------------------------------------------------------------------------
# Argumentos
# ---------------------------------------------------------------------------------------
[[ $# -ge 1 ]] || error_uso "falta el subcomando."
case "$1" in
    preparar|volcar|restaurar|limpiar|preparar-migracion|migrar) SUBCOMANDO="$1"; shift ;;
    -h|--help) uso; exit 0 ;;
    *) error_uso "subcomando desconocido: $1" ;;
esac

DUMP_REAL=""
HISTORIAL=()
SUFIJO=""
PUERTO="5434"
PUERTO_VIEJO="5291"
PUERTO_NUEVO="5292"
TIPO="e1"
RECREAR=0
MIGRAR_ARGS=()
SEPARADOR=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --trabajo|--historial|--sufijo|--puerto)
            [[ $# -ge 2 ]] || error_uso "falta el valor de $1."
            if [[ "$1" == "--sufijo" && "$SUBCOMANDO" == "preparar-migracion" ]]; then
                :
            elif [[ "$SUBCOMANDO" != "preparar" && "$1" != "--trabajo" ]]; then
                error_uso "$1 solo va con 'preparar' (los demás subcomandos lo leen de <trabajo>/config)."
            fi
            case "$1" in
                --trabajo) TRABAJO="$2" ;;
                --historial) HISTORIAL+=("$2") ;;
                --sufijo) SUFIJO="$2" ;;
                --puerto) PUERTO="$2" ;;
            esac
            shift 2
            ;;
        --dump|--puerto-viejo|--puerto-nuevo)
            [[ $# -ge 2 ]] || error_uso "falta el valor de $1."
            [[ "$SUBCOMANDO" == "preparar-migracion" ]] \
                || error_uso "$1 solo va con 'preparar-migracion' (los demás subcomandos lo leen de <trabajo>/config)."
            case "$1" in
                --dump) DUMP_REAL="$2" ;;
                --puerto-viejo) PUERTO_VIEJO="$2" ;;
                --puerto-nuevo) PUERTO_NUEVO="$2" ;;
            esac
            shift 2
            ;;
        --)
            [[ "$SUBCOMANDO" == "migrar" ]] || error_uso "'--' solo va con 'migrar' (separa los argumentos de migrar-vps.sh)."
            shift
            MIGRAR_ARGS=("$@")
            SEPARADOR=1
            break
            ;;
        --recrear-base)
            [[ "$SUBCOMANDO" == "restaurar" ]] || error_uso "--recrear-base solo va con 'restaurar'."
            RECREAR=1
            shift
            ;;
        -*) error_uso "opción desconocida: $1" ;;
        *)
            [[ "$SUBCOMANDO" == "preparar" && -z "$DUMP_REAL" ]] || error_uso "sobra el argumento '$1'."
            DUMP_REAL="$1"
            shift
            ;;
    esac
done

[[ -n "$TRABAJO" ]] || error_uso "falta --trabajo <dir>."
if [[ "$SUBCOMANDO" == "migrar" && "$SEPARADOR" -eq 0 ]]; then
    error_uso "migrar necesita '--' y después los argumentos de migrar-vps.sh (por ejemplo: -- --ensayo)."
fi
TRABAJO="$(realpath -m -- "$TRABAJO")"
# La raíz del VPS viejo simulado se pasa a volcado-final-vps.sh como --raiz-remota, que exige
# este patrón.
[[ "$TRABAJO" =~ ^/[A-Za-z0-9._/-]+$ && "$TRABAJO" != *..* ]] \
    || error_uso "--trabajo '${TRABAJO}' tiene que matchear ^/[A-Za-z0-9._/-]+$ (sin '..')."
# Va a tener datos reales: nunca dentro del repo (terminaría en un commit) ni bajo /mnt/ (el
# disco de Windows en WSL ignora el chmod 700: cualquiera los podría leer).
REPO_REAL="$(realpath -m -- "$REPO_ROOT")"
if [[ "$TRABAJO" == "$REPO_REAL" || "$TRABAJO" == "$REPO_REAL"/* ]]; then
    error_uso "--trabajo '${TRABAJO}' está dentro del repo: elegí un directorio fuera de '${REPO_REAL}'."
fi
if [[ "$TRABAJO" == /mnt || "$TRABAJO" == /mnt/* ]]; then
    error_uso "--trabajo '${TRABAJO}' está bajo /mnt/, donde no rigen los permisos 700/600."
fi
# Ni el HOME entero ni uno de sus padres: el ensayo vive en un directorio propio.
HOME_REAL="$(realpath -m -- "${HOME:-/}")"
if [[ "$TRABAJO" == "$HOME_REAL" || "$HOME_REAL" == "$TRABAJO"/* ]]; then
    error_uso "--trabajo '${TRABAJO}' es el HOME o lo contiene: elegí un directorio propio para el ensayo."
fi

CONFIG="${TRABAJO}/config"
ESTADO="${TRABAJO}/estado"
RAIZ="${TRABAJO}/vps-viejo"
SALIDA="${TRABAJO}/salida"
ENV_OPERADOR="${TRABAJO}/env-vps-viejo-ensayo"
ENV_NUEVO_OPERADOR="${TRABAJO}/env-vps-nuevo-ensayo"
PS_ANTES="${TRABAJO}/docker-ps-antes.txt"
HOST_VIEJO="vps-viejo.ensayo.invalid"
HOST_NUEVO="vps-nuevo.ensayo.invalid"
DIR_VIEJO="${TRABAJO}/hosts/${HOST_VIEJO}"
DIR_NUEVO="${TRABAJO}/hosts/${HOST_NUEVO}"
HOME_OPERADOR="${TRABAJO}/operador"

# ---------------------------------------------------------------------------------------
# Config (preparar la escribe; el resto la lee línea por línea, nunca con 'source')
# ---------------------------------------------------------------------------------------
validar_config() {
    local p
    [[ "$SUFIJO" =~ ^[a-z0-9]{0,12}$ ]] || error_uso "--sufijo '${SUFIJO}' no matchea ^[a-z0-9]{0,12}$."
    for p in PUERTO PUERTO_VIEJO PUERTO_NUEVO; do
        if [[ ! "${!p}" =~ ^[1-9][0-9]{0,4}$ ]] || (( ${!p} > 65535 )); then
            error_uso "--$(tr '_A-Z' '-a-z' <<< "$p") '${!p}' no es un puerto válido."
        fi
    done
    [[ "$TIPO" == "e1" || "$TIPO" == "migracion" ]] || error_uso "config con TIPO='${TIPO}' desconocido."
}

leer_config() {
    if [[ ! -f "$CONFIG" ]]; then
        echo "ERROR: '${TRABAJO}' no tiene config: corré antes 'preparar'." >&2
        exit 3
    fi
    local linea
    while IFS= read -r linea; do
        case "$linea" in
            SUFIJO=*) SUFIJO="${linea#SUFIJO=}" ;;
            PUERTO=*) PUERTO="${linea#PUERTO=}" ;;
            PUERTO_VIEJO=*) PUERTO_VIEJO="${linea#PUERTO_VIEJO=}" ;;
            PUERTO_NUEVO=*) PUERTO_NUEVO="${linea#PUERTO_NUEVO=}" ;;
            TIPO=*) TIPO="${linea#TIPO=}" ;;
        esac
    done < "$CONFIG"
    validar_config
}

# exigir_tipo <tipo>: el subcomando solo sirve para el ensayo de ese tipo.
exigir_tipo() {
    if [[ "$TIPO" != "$1" ]]; then
        echo "ERROR: '${TRABAJO}' está preparado para el ensayo '${TIPO}' y '${SUBCOMANDO}' es del ensayo '$1'." >&2
        exit 3
    fi
}

case "$SUBCOMANDO" in
    preparar) validar_config ;;
    preparar-migracion) TIPO="migracion"; validar_config ;;
    *) leer_config ;;
esac

SUF_G="${SUFIJO:+-${SUFIJO}}"
SUF_U="${SUFIJO:+_${SUFIJO}}"
ORIGEN="stockapp-ensayo-origen-pg${SUF_G}"
DESTINO="stockapp-ensayo-pg${SUF_G}"
VOL_ORIGEN="stockapp_ensayo_origen_pgdata${SUF_U}"
VOL_DESTINO="stockapp_ensayo_pgdata${SUF_U}"

# ---------------------------------------------------------------------------------------
# Helpers de docker: "no pude consultar" nunca es "no existe"
# ---------------------------------------------------------------------------------------
# lista_docker <qué> <comando...>: deja la salida en LISTA o aborta con 8.
LISTA=""
lista_docker() {
    local que="$1" err rc=0
    shift
    err="$(mktemp)"
    LISTA="$("$@" 2>"$err")" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        echo "ERROR: no se pudo consultar ${que} (código ${rc}): $(cat "$err")" >&2
        rm -f "$err"
        exit 8
    fi
    rm -f "$err"
}

contiene() {
    local buscado="$1" linea
    while IFS= read -r linea; do
        [[ "$linea" == "$buscado" ]] && return 0
    done <<< "$LISTA"
    return 1
}

contenedores_ps() {
    lista_docker "los contenedores corriendo (docker ps)" docker ps --format '{{.Names}}'
    printf '%s\n' "$LISTA" | LC_ALL=C sort
}

esperar_postgres() {
    local nombre="$1" _
    # Por TCP: durante initdb el entrypoint levanta un servidor temporal solo por socket, y un
    # pg_isready por socket da "listo" antes del reinicio.
    for _ in $(seq 1 120); do
        if docker exec "$nombre" pg_isready -h 127.0.0.1 -U stockapp -d stockapp >/dev/null 2>&1; then
            return 0
        fi
        sleep 0.5
    done
    fallo_preparar 8 "Postgres no quedó listo en 60 s en '${nombre}'."
}

# ---------------------------------------------------------------------------------------
# preparar
# ---------------------------------------------------------------------------------------
# Desde que se escribe la config, un fallo lo atrapa al_salir y borra lo que se creó.
fallo_preparar() {
    echo "ERROR: $2" >&2
    exit "$1"
}

preparar() {
    [[ -n "$DUMP_REAL" ]] || error_uso "falta el <dump-real>."
    [[ -f "$DUMP_REAL" && -r "$DUMP_REAL" ]] || error_uso "el dump '${DUMP_REAL}' no existe o no se puede leer."
    local h
    for h in "${HISTORIAL[@]}"; do
        [[ -f "$h" && -r "$h" ]] || error_uso "el historial '${h}' no existe o no se puede leer."
        [[ "$(basename "$h")" =~ ^backup_.*\.dump$ ]] || error_uso "el historial '${h}' no se llama backup_*.dump."
    done
    if [[ -e "$TRABAJO" && -n "$(ls -A "$TRABAJO")" ]]; then
        echo "ERROR: '${TRABAJO}' no está vacío: el ensayo nunca reutiliza un directorio. ABORTO." >&2
        exit 3
    fi

    local n
    lista_docker "los contenedores (docker ps -a)" docker ps -a --format '{{.Names}}'
    for n in "$ORIGEN" "$DESTINO"; do
        if contiene "$n"; then
            echo "ERROR: ya existe el contenedor '${n}': el ensayo nunca reutiliza uno. ABORTO sin tocarlo." >&2
            echo "       Si es de un ensayo anterior, corré 'limpiar' con su --trabajo." >&2
            exit 3
        fi
    done
    lista_docker "los volúmenes (docker volume ls)" docker volume ls --format '{{.Name}}'
    for n in "$VOL_ORIGEN" "$VOL_DESTINO"; do
        if contiene "$n"; then
            echo "ERROR: ya existe el volumen '${n}': el ensayo nunca reutiliza uno. ABORTO sin tocarlo." >&2
            exit 3
        fi
    done
    if (exec 3<>"/dev/tcp/127.0.0.1/${PUERTO}") 2>/dev/null; then
        echo "ERROR: el puerto 127.0.0.1:${PUERTO} está ocupado. Elegí otro con --puerto. ABORTO." >&2
        exit 3
    fi

    umask 077
    mkdir -p "$TRABAJO"
    chmod 700 "$TRABAJO"
    printf 'SUFIJO=%s\nPUERTO=%s\n' "$SUFIJO" "$PUERTO" > "$CONFIG"
    chmod 600 "$CONFIG"
    CONFIG_ESCRITA=1
    contenedores_ps > "$PS_ANTES"
    head -c 24 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' > "${TRABAJO}/pg-password"
    chmod 600 "${TRABAJO}/pg-password"

    echo "== Contenedores del ensayo =="
    # La contraseña viaja por el entorno de docker (-e sin valor), nunca por argv.
    POSTGRES_PASSWORD="$(cat "${TRABAJO}/pg-password")" docker run -d --name "$ORIGEN" \
        -v "${VOL_ORIGEN}:/var/lib/postgresql/data" \
        -e POSTGRES_USER=stockapp -e POSTGRES_DB=stockapp -e POSTGRES_PASSWORD \
        postgres:16-alpine >/dev/null || fallo_preparar 8 "no se pudo crear '${ORIGEN}'."
    POSTGRES_PASSWORD="$(cat "${TRABAJO}/pg-password")" docker run -d --name "$DESTINO" \
        -v "${VOL_DESTINO}:/var/lib/postgresql/data" -p "127.0.0.1:${PUERTO}:5432" \
        -e POSTGRES_USER=stockapp -e POSTGRES_DB=stockapp -e POSTGRES_PASSWORD \
        postgres:16-alpine >/dev/null || fallo_preparar 8 "no se pudo crear '${DESTINO}'."
    esperar_postgres "$ORIGEN"
    esperar_postgres "$DESTINO"
    echo "  ${ORIGEN} (VPS viejo) y ${DESTINO} (127.0.0.1:${PUERTO})"

    echo "== Dump real en el origen =="
    local rc=0
    "${REPO_ROOT}/deploy/restaurar-backup.sh" "$DUMP_REAL" --contenedor "$ORIGEN" \
        --base stockapp --usuario stockapp --dir-seguridad "${TRABAJO}/seguridad-origen" || rc=$?
    [[ "$rc" -eq 0 ]] || fallo_preparar "$rc" "restaurar-backup.sh no pudo cargar el dump en '${ORIGEN}'."

    echo "== Raíz del VPS viejo =="
    local backups="${RAIZ}/var/lib/stockapp/.local/share/StockApp/backups"
    mkdir -p "$backups" "${RAIZ}/etc/stockapp-api" "${RAIZ}/etc/systemd/system" "$ESTADO"
    for h in "${HISTORIAL[@]}"; do
        cp -- "$h" "$backups/"
    done
    printf 'Jwt__Secret=ensayo\nConnectionStrings__Default=ensayo\nRateLimiting__Login__PermitLimit=10\n' \
        > "${RAIZ}/etc/stockapp-api/api.env"
    # Archivo REGULAR, como lo deja install.sh: el volcado tiene que moverlo antes del mask.
    cp -- "${REPO_ROOT}/deploy/stockapp-api.service" "${RAIZ}/etc/systemd/system/stockapp-api.service"
    echo active > "${ESTADO}/stockapp-api.activo"
    echo enabled > "${ESTADO}/stockapp-api.habilitado"
    printf '%s\n' \
        "VPS_HOST=vps-viejo.ensayo.invalid" "VPS_USER=ensayo" "VPS_SSH_PORT=22" \
        "POSTGRES_USER=stockapp" "POSTGRES_DB=stockapp" > "$ENV_OPERADOR"
    echo "  ${RAIZ} (historial: ${#HISTORIAL[@]} backup(s)); stockapp-api active/enabled"
    echo "OK: ensayo preparado en ${TRABAJO}"
    recordar_datos
}

# ---------------------------------------------------------------------------------------
# volcar
# ---------------------------------------------------------------------------------------
volcar() {
    local rc=0
    exigir_tipo e1
    PATH="${DIR_SHIMS}:${PATH}" ENSAYO_ESTADO="$ESTADO" ENSAYO_RAIZ="$RAIZ" \
        STOCKAPP_ENV_LOCAL="$ENV_OPERADOR" \
        "${REPO_ROOT}/deploy/volcado-final-vps.sh" --salida "$SALIDA" \
        --contenedor "$ORIGEN" --raiz-remota "$RAIZ" || rc=$?
    return "$rc"
}

# ---------------------------------------------------------------------------------------
# restaurar
# ---------------------------------------------------------------------------------------
restaurar() {
    local dumps=() manifiesto rc=0 path_sin_shims="" d
    exigir_tipo e1
    shopt -s nullglob
    dumps=("${SALIDA}"/volcado-*.dump)
    shopt -u nullglob
    if [[ ${#dumps[@]} -ne 1 ]]; then
        echo "ERROR: en '${SALIDA}' tiene que haber exactamente un volcado-*.dump (hay ${#dumps[@]}). ABORTO." >&2
        exit 3
    fi
    manifiesto="${dumps[0]%.dump}.manifiesto"
    if [[ ! -f "$manifiesto" ]]; then
        echo "ERROR: falta el manifiesto '${manifiesto}'. ABORTO." >&2
        exit 3
    fi
    # PATH sin los shims: acá corre el systemctl de esta máquina.
    IFS=':' read -r -a partes <<< "$PATH"
    for d in "${partes[@]}"; do
        [[ "$d" == "$DIR_SHIMS" ]] || path_sin_shims+="${path_sin_shims:+:}${d}"
    done
    local recrear=()
    [[ "$RECREAR" -eq 0 ]] || recrear=(--recrear-base stockapp)
    PATH="$path_sin_shims" "${REPO_ROOT}/deploy/restaurar-backup.sh" "${dumps[0]}" \
        --manifiesto "$manifiesto" --contenedor "$DESTINO" --base stockapp --usuario stockapp \
        --dir-seguridad "${TRABAJO}/seguridad" "${recrear[@]}" || rc=$?
    return "$rc"
}

# ---------------------------------------------------------------------------------------
# preparar-migracion: dos hosts con la API real
# ---------------------------------------------------------------------------------------
VERSION_FEED="0.0.1-ensayo"

# aleatorio <n>: n caracteres alfanuméricos (sin pipes que puedan cortar con SIGPIPE).
aleatorio() {
    local x=""
    while [[ "${#x}" -lt "$1" ]]; do
        x+="$(head -c 48 /dev/urandom | base64 | tr -dc 'A-Za-z0-9')"
    done
    printf '%s' "${x:0:$1}"
}

# puerto_libre <opción> <n>: aborta con 3 si 127.0.0.1:<n> ya atiende.
puerto_libre() {
    if (exec 3<>"/dev/tcp/127.0.0.1/$2") 2>/dev/null; then
        echo "ERROR: el puerto 127.0.0.1:$2 está ocupado. Elegí otro con $1. ABORTO." >&2
        exit 3
    fi
}

# crear_pg <nombre> <volumen>: Postgres del ensayo publicado en un puerto libre de 127.0.0.1 ->
# PUERTO_PG (el que eligió docker).
PUERTO_PG=""
crear_pg() {
    local nombre="$1" volumen="$2" mapeo
    # La contraseña viaja por el entorno de docker (-e sin valor), nunca por argv.
    POSTGRES_PASSWORD="$(cat "${TRABAJO}/pg-password")" docker run -d --name "$nombre" \
        -v "${volumen}:/var/lib/postgresql/data" -p "127.0.0.1::5432" \
        -e POSTGRES_USER=stockapp -e POSTGRES_DB=stockapp -e POSTGRES_PASSWORD \
        postgres:16-alpine >/dev/null || fallo_preparar 8 "no se pudo crear '${nombre}'."
    esperar_postgres "$nombre"
    mapeo="$(docker port "$nombre" 5432/tcp)" || fallo_preparar 8 "no pude leer el puerto publicado de '${nombre}'."
    PUERTO_PG="${mapeo##*:}"
    [[ "$mapeo" == 127.0.0.1:* && "$PUERTO_PG" =~ ^[1-9][0-9]{0,4}$ ]] \
        || fallo_preparar 8 "'${nombre}' no publicó 5432 en 127.0.0.1 (docker port dice '${mapeo}')."
}

# armar_host <dir> <puerto-api> <puerto-pg> <jwt> <admin> <contraseña-admin>: la raíz como la deja
# install.sh (unit como archivo regular, api.env 600, el directorio de datos de la API con
# .local/share para que LocalApplicationData resuelva ahí) más el feed, el machine-id y el estado.
armar_host() {
    local dir="$1" puerto="$2" puerto_pg="$3" jwt="$4" admin="$5" contrasena="$6"
    local raiz="${dir}/raiz" datos="${dir}/raiz/var/lib/stockapp"
    mkdir -p "${dir}/estado" "${dir}/home" "${raiz}/etc/stockapp-api" "${raiz}/etc/systemd/system" \
        "${datos}/.local/share/StockApp/backups" "${datos}/updates"
    # 32 hex, como /etc/machine-id; uno por host, así migrar-vps.sh los ve distintos (A9).
    head -c 16 /dev/urandom | od -An -tx1 | tr -d ' \n' > "${raiz}/etc/machine-id"
    printf '%s\n' \
        "Jwt__Secret=${jwt}" \
        "Bootstrap__AdminUser=${admin}" \
        "Bootstrap__Password=${contrasena}" \
        "ConnectionStrings__Default=Host=127.0.0.1;Port=${puerto_pg};Database=stockapp;Username=stockapp;Password=$(cat "${TRABAJO}/pg-password")" \
        "Licencia__Habilitado=false" \
        "RateLimiting__Login__PermitLimit=10" \
        "Actualizaciones__Directorio=${datos}/updates" > "${raiz}/etc/stockapp-api/api.env"
    chmod 600 "${raiz}/etc/stockapp-api/api.env"
    sed -e "s/__API_PORT__/${puerto}/g" -e "s/__API_BIND__/127.0.0.1/g" -e "s/__API_SCHEME__/http/g" \
        "${REPO_ROOT}/deploy/stockapp-api.service" > "${raiz}/etc/systemd/system/stockapp-api.service"
    echo inactive > "${dir}/estado/stockapp-api.activo"
    echo enabled > "${dir}/estado/stockapp-api.habilitado"
    echo "${TRABAJO}/api/StockApp.Api.dll" > "${dir}/estado/api.lanzar"
}

# arrancar_api <dir> <puerto> <host>: systemctl start (el falso, que lanza la API publicada) y
# espera acotada a que GET / responda; una API que muere en el arranque aborta en el acto.
arrancar_api() {
    local dir="$1" puerto="$2" host="$3" pid _
    ENSAYO_ESTADO="${dir}/estado" ENSAYO_RAIZ="${dir}/raiz" "${DIR_SHIMS}/systemctl" start stockapp-api \
        || fallo_preparar 8 "systemctl start de la API de ${host} falló."
    pid="$(cat "${dir}/estado/api.pid")"
    for _ in $(seq 1 240); do
        if curl -fsS --max-time 5 -o /dev/null "http://127.0.0.1:${puerto}/" 2>/dev/null; then
            echo "  API de ${host} atendiendo en 127.0.0.1:${puerto} (PID ${pid})"
            return 0
        fi
        if ! ps -p "$pid" -o pid= >/dev/null 2>&1; then
            tail -n 30 "${dir}/estado/api.log" >&2 || :
            fallo_preparar 8 "la API de ${host} murió al arrancar (log: ${dir}/estado/api.log)."
        fi
        sleep 0.5
    done
    tail -n 30 "${dir}/estado/api.log" >&2 || :
    fallo_preparar 8 "la API de ${host} no respondió GET / en 120 s (log: ${dir}/estado/api.log)."
}

# backup_en_el_viejo: el historial del viejo lo hace la propia API (POST /backups como admin), así
# V8 encuentra una corrida Exitosa con su archivo. El token vive en una variable y viaja por el
# stdin de curl; la contraseña, por el entorno de jq.
backup_en_el_viejo() {
    local url="http://127.0.0.1:${PUERTO_VIEJO}" cuerpo respuesta token config listado _
    cuerpo="$(PW="$(cat "${TRABAJO}/admin-password")" jq -nc '{nombreUsuario: "admin", contrasena: env.PW}')"
    respuesta="$(curl -fsS --max-time 30 -X POST "${url}/auth/login" -H 'Content-Type: application/json' \
        --data @- <<< "$cuerpo")" || fallo_preparar 8 "login de admin en la API vieja falló."
    token="$(jq -r '.token // empty' <<< "$respuesta")"
    [[ -n "$token" ]] || fallo_preparar 8 "el login de admin en la API vieja no devolvió token."
    config="$(printf 'header = "Authorization: Bearer %s"' "$token")"
    curl -fsS --max-time 30 -o /dev/null -K - -X POST "${url}/backups" <<< "$config" \
        || fallo_preparar 8 "POST /backups en la API vieja falló."
    for _ in $(seq 1 180); do
        listado="$(curl -fsS --max-time 30 -K - "${url}/backups" <<< "$config")" \
            || fallo_preparar 8 "GET /backups en la API vieja falló."
        if [[ "$(jq -r '[.[] | select(.resultado == "Exitosa")] | length' <<< "$listado")" -gt 0 ]]; then
            echo "  historial del viejo: una corrida de backup Exitosa"
            return 0
        fi
        sleep 0.5
    done
    fallo_preparar 8 "el backup de la API vieja no terminó Exitoso en 90 s."
}

preparar_migracion() {
    local admin="admin" n jwt pwbase feed nupkg tam
    if [[ -n "$DUMP_REAL" ]]; then
        [[ -f "$DUMP_REAL" && -r "$DUMP_REAL" ]] || error_uso "el dump '${DUMP_REAL}' no existe o no se puede leer."
        [[ -n "${VPS_ADMIN_USER:-}" ]] \
            || error_uso "con --dump, VPS_ADMIN_USER tiene que venir en el entorno: es el usuario real con el que migrar-vps.sh verifica."
        [[ "$VPS_ADMIN_USER" =~ ^[A-Za-z0-9._@-]+$ ]] || error_uso "VPS_ADMIN_USER '${VPS_ADMIN_USER}' tiene caracteres no válidos."
        admin="$VPS_ADMIN_USER"
    fi
    [[ "$PUERTO_VIEJO" != "$PUERTO_NUEVO" ]] || error_uso "--puerto-viejo y --puerto-nuevo tienen que ser distintos."
    if [[ -e "$TRABAJO" && -n "$(ls -A "$TRABAJO")" ]]; then
        echo "ERROR: '${TRABAJO}' no está vacío: el ensayo nunca reutiliza un directorio. ABORTO." >&2
        exit 3
    fi
    lista_docker "los contenedores (docker ps -a)" docker ps -a --format '{{.Names}}'
    for n in "$ORIGEN" "$DESTINO"; do
        if contiene "$n"; then
            echo "ERROR: ya existe el contenedor '${n}': el ensayo nunca reutiliza uno. ABORTO sin tocarlo." >&2
            echo "       Si es de un ensayo anterior, corré 'limpiar' con su --trabajo." >&2
            exit 3
        fi
    done
    lista_docker "los volúmenes (docker volume ls)" docker volume ls --format '{{.Name}}'
    for n in "$VOL_ORIGEN" "$VOL_DESTINO"; do
        if contiene "$n"; then
            echo "ERROR: ya existe el volumen '${n}': el ensayo nunca reutiliza uno. ABORTO sin tocarlo." >&2
            exit 3
        fi
    done
    puerto_libre --puerto-viejo "$PUERTO_VIEJO"
    puerto_libre --puerto-nuevo "$PUERTO_NUEVO"

    umask 077
    mkdir -p "$TRABAJO"
    chmod 700 "$TRABAJO"
    printf 'TIPO=migracion\nSUFIJO=%s\nPUERTO_VIEJO=%s\nPUERTO_NUEVO=%s\n' "$SUFIJO" "$PUERTO_VIEJO" "$PUERTO_NUEVO" > "$CONFIG"
    chmod 600 "$CONFIG"
    CONFIG_ESCRITA=1
    contenedores_ps > "$PS_ANTES"
    aleatorio 32 > "${TRABAJO}/pg-password"
    chmod 600 "${TRABAJO}/pg-password"
    mkdir -p "$HOME_OPERADOR"

    echo "== API publicada (dotnet publish, una vez) =="
    # HOME y DOTNET_CLI_HOME dentro del trabajo: el SDK escribe en el HOME (~/.dotnet/corefx,
    # ~/.local/share/NuGet; verificado) y el ensayo no toca el de quien lo corre. Los paquetes salen
    # de la caché de NuGet de siempre, solo lectura en la práctica (si no, publicar los volvería a
    # bajar).
    # TMPDIR también: el SDK deja directorios temporales propios en /tmp.
    local cache_nuget="${NUGET_PACKAGES:-${HOME}/.nuget/packages}"
    mkdir -p "${TRABAJO}/dotnet-cli" "${TRABAJO}/tmp"
    HOME="${TRABAJO}/dotnet-cli" DOTNET_CLI_HOME="${TRABAJO}/dotnet-cli" TMPDIR="${TRABAJO}/tmp" \
        DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        NUGET_PACKAGES="$cache_nuget" \
        dotnet publish "${REPO_ROOT}/src/StockApp.Api/StockApp.Api.csproj" -c Release -o "${TRABAJO}/api" \
        -nodeReuse:false -p:UseSharedCompilation=false > "${TRABAJO}/publicar.log" 2>&1 \
        || { tail -n 30 "${TRABAJO}/publicar.log" >&2; fallo_preparar 8 "dotnet publish de la API falló (log: ${TRABAJO}/publicar.log)."; }
    [[ -f "${TRABAJO}/api/StockApp.Api.dll" ]] || fallo_preparar 8 "dotnet publish no dejó ${TRABAJO}/api/StockApp.Api.dll."
    echo "  ${TRABAJO}/api"

    echo "== Contenedores del ensayo =="
    crear_pg "$ORIGEN" "$VOL_ORIGEN"
    local pg_viejo="$PUERTO_PG"
    crear_pg "$DESTINO" "$VOL_DESTINO"
    local pg_nuevo="$PUERTO_PG"
    echo "  ${ORIGEN} (VPS viejo, 127.0.0.1:${pg_viejo}) y ${DESTINO} (VPS nuevo, 127.0.0.1:${pg_nuevo})"

    if [[ -n "$DUMP_REAL" ]]; then
        echo "== Dump real en el origen =="
        local rc=0
        "${REPO_ROOT}/deploy/restaurar-backup.sh" "$DUMP_REAL" --contenedor "$ORIGEN" \
            --base stockapp --usuario stockapp --dir-seguridad "${TRABAJO}/seguridad-origen" || rc=$?
        [[ "$rc" -eq 0 ]] || fallo_preparar "$rc" "restaurar-backup.sh no pudo cargar el dump en '${ORIGEN}'."
    else
        pwbase="Ens$(aleatorio 20)9"
        printf '%s' "$pwbase" > "${TRABAJO}/admin-password"
        chmod 600 "${TRABAJO}/admin-password"
    fi

    echo "== VPS viejo (${HOST_VIEJO}) =="
    # Con --dump ya hay usuarios y el seed no hace nada: la contraseña de bootstrap es solo de relleno.
    local contrasena_viejo
    if [[ -f "${TRABAJO}/admin-password" ]]; then
        contrasena_viejo="$(cat "${TRABAJO}/admin-password")"
    else
        contrasena_viejo="Ens$(aleatorio 20)9"
    fi
    armar_host "$DIR_VIEJO" "$PUERTO_VIEJO" "$pg_viejo" "$(aleatorio 64)" "$admin" "$contrasena_viejo"
    arrancar_api "$DIR_VIEJO" "$PUERTO_VIEJO" "$HOST_VIEJO"
    [[ -n "$DUMP_REAL" ]] || backup_en_el_viejo

    echo "== VPS nuevo (${HOST_NUEVO}), como lo deja preparar-vps-nuevo.sh =="
    jwt="$(aleatorio 64)"
    pwbase="Ens$(aleatorio 20)9"
    armar_host "$DIR_NUEVO" "$PUERTO_NUEVO" "$pg_nuevo" "$jwt" admin "$pwbase"
    feed="${DIR_NUEVO}/raiz/var/lib/stockapp/updates"
    nupkg="StockApp-${VERSION_FEED}-full.nupkg"
    head -c 4096 /dev/urandom > "${feed}/${nupkg}"
    tam="$(stat -c %s "${feed}/${nupkg}")"
    jq -n --arg v "$VERSION_FEED" --arg f "$nupkg" --argjson t "$tam" \
        --arg sha1 "$(sha1sum "${feed}/${nupkg}" | cut -d' ' -f1)" \
        --arg sha256 "$(sha256sum "${feed}/${nupkg}" | cut -d' ' -f1)" \
        '{Assets: [{PackageId: "StockApp", Version: $v, Type: "Full", FileName: $f, SHA1: $sha1, SHA256: $sha256, Size: $t, NotesMarkdown: "", NotesHtml: ""}]}' \
        > "${feed}/releases.win.json"
    mkdir -p "${DIR_NUEVO}/home/stockapp-deploy/migracion"
    chmod 700 "${DIR_NUEVO}/home/stockapp-deploy" "${DIR_NUEVO}/home/stockapp-deploy/migracion"
    # El .env del servidor, con las claves que escribe preparar-vps-nuevo.sh y los overrides que
    # el api.env del viejo tiene fuera de lo de install.sh (así el pre-vuelo de overrides pasa).
    printf '%s\n' \
        "POSTGRES_USER=stockapp" "POSTGRES_PASSWORD=$(cat "${TRABAJO}/pg-password")" "POSTGRES_DB=stockapp" \
        "BOOTSTRAP_ADMIN_USER=admin" "BOOTSTRAP_PASSWORD=${pwbase}" "JWT_SECRET=${jwt}" \
        "API_BIND=127.0.0.1" "API_SCHEME=http" "API_PORT=${PUERTO_NUEVO}" "API_HOSTNAME=127.0.0.1" \
        "Licencia__Habilitado=false" "RateLimiting__Login__PermitLimit=10" \
        "Actualizaciones__Directorio=${feed}" > "${DIR_NUEVO}/home/stockapp-deploy/.env"
    chmod 600 "${DIR_NUEVO}/home/stockapp-deploy/.env"
    arrancar_api "$DIR_NUEVO" "$PUERTO_NUEVO" "$HOST_NUEVO"
    curl -fsS --max-time 10 -o /dev/null "http://127.0.0.1:${PUERTO_NUEVO}/updates/releases.win.json" \
        || fallo_preparar 8 "la API nueva no sirve el feed (/updates/releases.win.json)."
    echo "  feed ${VERSION_FEED}; unit, directorios y ~/stockapp-deploy/.env en su lugar"

    echo "== .env del operador =="
    local h archivo puerto
    for h in viejo nuevo; do
        if [[ "$h" == viejo ]]; then archivo="$ENV_OPERADOR" puerto="$PUERTO_VIEJO"; else archivo="$ENV_NUEVO_OPERADOR" puerto="$PUERTO_NUEVO"; fi
        printf '%s\n' \
            "VPS_HOST=vps-${h}.ensayo.invalid" "VPS_USER=ensayo" "VPS_SSH_PORT=22" \
            "API_SCHEME=http" "API_HOSTNAME=127.0.0.1" "API_PORT=${puerto}" \
            "POSTGRES_USER=stockapp" "POSTGRES_DB=stockapp" "VPS_ADMIN_USER=${admin}" > "$archivo"
        chmod 600 "$archivo"
    done
    echo "  ${ENV_OPERADOR} y ${ENV_NUEVO_OPERADOR}"
    echo "OK: ensayo de migración preparado en ${TRABAJO}"
    recordar_datos
}

# ---------------------------------------------------------------------------------------
# migrar
# ---------------------------------------------------------------------------------------
migrar() {
    local modo="corte" a extra=() rc=0 contrasena=""
    exigir_tipo migracion
    for a in "${MIGRAR_ARGS[@]}"; do
        case "$a" in
            --ensayo) modo="ensayo" ;;
            --continuar) modo="continuar" ;;
            --revertir) modo="revertir" ;;
            --desde-backup) modo="desde-backup" ;;
            --post-corte) modo="post-corte" ;;
        esac
    done
    # Los contenedores y las raíces del arnés (R13, R11) en todo modo que opere los hosts: sin ellos,
    # migrar-vps.sh operaría el contenedor de desarrollo y el /etc de esta máquina. --continuar y --revertir no
    # aceptan --env-* (salen del estado de la corrida); --post-corte no acepta nada más.
    case "$modo" in
        corte | ensayo)
            extra=(--env-viejo "$ENV_OPERADOR" --env-nuevo "$ENV_NUEVO_OPERADOR"
                --contenedor-viejo "$ORIGEN" --contenedor-nuevo "$DESTINO"
                --raiz-remota-viejo "${DIR_VIEJO}/raiz" --raiz-remota-nuevo "${DIR_NUEVO}/raiz"
                --sin-verificar-preparacion) ;;
        continuar | revertir)
            extra=(--contenedor-viejo "$ORIGEN" --contenedor-nuevo "$DESTINO"
                --raiz-remota-viejo "${DIR_VIEJO}/raiz" --raiz-remota-nuevo "${DIR_NUEVO}/raiz"
                --sin-verificar-preparacion) ;;
        desde-backup)
            extra=(--env-viejo "$ENV_OPERADOR" --env-nuevo "$ENV_NUEVO_OPERADOR"
                --contenedor-nuevo "$DESTINO" --raiz-remota-nuevo "${DIR_NUEVO}/raiz"
                --sin-verificar-preparacion) ;;
        post-corte) extra=() ;;
    esac
    # La contraseña del admin del ensayo, en el entorno de ESTE comando (nunca en argv); con --dump
    # no hay archivo y VPS_ADMIN_PASSWORD viene del entorno de quien llama.
    if [[ -f "${TRABAJO}/admin-password" ]]; then
        contrasena="$(cat "${TRABAJO}/admin-password")"
        VPS_ADMIN_PASSWORD="$contrasena" PATH="${DIR_SHIMS}:${PATH}" ENSAYO_TRABAJO="$TRABAJO" HOME="$HOME_OPERADOR" \
            "${REPO_ROOT}/deploy/migrar-vps.sh" "${MIGRAR_ARGS[@]}" "${extra[@]}" || rc=$?
    else
        PATH="${DIR_SHIMS}:${PATH}" ENSAYO_TRABAJO="$TRABAJO" HOME="$HOME_OPERADOR" \
            "${REPO_ROOT}/deploy/migrar-vps.sh" "${MIGRAR_ARGS[@]}" "${extra[@]}" || rc=$?
    fi
    return "$rc"
}

# ---------------------------------------------------------------------------------------
# limpiar
# ---------------------------------------------------------------------------------------
# detener_apis: las APIs de los dos hosts, con el systemctl falso (mata por el PID guardado) y
# verificado acá con ps sobre ese mismo PID. Devuelve 0 u 8.
detener_apis() {
    local rc=0 dir pid
    for dir in "$DIR_VIEJO" "$DIR_NUEVO"; do
        [[ -f "${dir}/estado/api.pid" ]] || continue
        pid="$(cat "${dir}/estado/api.pid")"
        if ! ENSAYO_ESTADO="${dir}/estado" ENSAYO_RAIZ="${dir}/raiz" "${DIR_SHIMS}/systemctl" stop stockapp-api; then
            rc=8
        fi
        if [[ "$pid" =~ ^[1-9][0-9]*$ ]] && ps -p "$pid" -o pid= >/dev/null 2>&1; then
            echo "ERROR: la API de ${dir##*/} (PID ${pid}) sigue viva." >&2
            rc=8
        fi
    done
    return "$rc"
}

# borrar_recursos: los dos contenedores (con -v) y los dos volúmenes de este sufijo, por nombre
# EXACTO y solo si existen; después verifica que no quede ninguno. Devuelve 0 u 8.
borrar_recursos() {
    local rc=0
    lista_docker "los contenedores (docker ps -a)" docker ps -a --format '{{.Names}}'
    if contiene "$ORIGEN"; then
        docker rm -f -v "stockapp-ensayo-origen-pg${SUF_G}" >/dev/null || rc=8
    fi
    if contiene "$DESTINO"; then
        docker rm -f -v "stockapp-ensayo-pg${SUF_G}" >/dev/null || rc=8
    fi
    lista_docker "los volúmenes (docker volume ls)" docker volume ls --format '{{.Name}}'
    if contiene "$VOL_ORIGEN"; then
        docker volume rm "stockapp_ensayo_origen_pgdata${SUF_U}" >/dev/null || rc=8
    fi
    if contiene "$VOL_DESTINO"; then
        docker volume rm "stockapp_ensayo_pgdata${SUF_U}" >/dev/null || rc=8
    fi

    # Verificación: no queda nada con esos nombres (no alcanza con el código de salida del borrado).
    local n
    lista_docker "los contenedores (docker ps -a)" docker ps -a --format '{{.Names}}'
    for n in "$ORIGEN" "$DESTINO"; do
        if contiene "$n"; then echo "ERROR: el contenedor '${n}' sigue existiendo." >&2; rc=8; fi
    done
    lista_docker "los volúmenes (docker volume ls)" docker volume ls --format '{{.Name}}'
    for n in "$VOL_ORIGEN" "$VOL_DESTINO"; do
        if contiene "$n"; then echo "ERROR: el volumen '${n}' sigue existiendo." >&2; rc=8; fi
    done
    return "$rc"
}

limpiar() {
    local rc=0
    detener_apis || rc=$?
    [[ "$rc" -eq 0 ]] || exit "$rc"
    [[ ! -d "${TRABAJO}/hosts" ]] || echo "  APIs del ensayo detenidas."
    borrar_recursos || rc=$?
    [[ "$rc" -eq 0 ]] || exit "$rc"
    echo "  Contenedores y volúmenes del ensayo borrados."

    if [[ ! -f "$PS_ANTES" ]]; then
        echo "ERROR: falta '${PS_ANTES}': no se puede comparar el alcance." >&2
        exit 3
    fi
    local despues
    despues="$(contenedores_ps)"
    if [[ "$despues" != "$(cat "$PS_ANTES")" ]]; then
        echo "ERROR: la lista de contenedores corriendo no es la de antes de preparar." >&2
        echo "       Antes:   $(tr '\n' ' ' < "$PS_ANTES")" >&2
        echo "       Después: $(printf '%s' "$despues" | tr '\n' ' ')" >&2
        exit 9
    fi
    echo "OK: contenedores corriendo iguales a los de antes del ensayo."
}

case "$SUBCOMANDO" in
    preparar-migracion) preparar_migracion ;;
    *) "$SUBCOMANDO" ;;
esac
