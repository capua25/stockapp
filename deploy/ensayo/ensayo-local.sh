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
#   --trabajo    directorio del ensayo (700). 'preparar' lo exige vacío o inexistente.
#   --historial  backups que hacen de historial del VPS viejo (repetible).
#   --sufijo     ^[a-z0-9]{0,12}$, default vacío: se agrega como -<s> / _<s> a los nombres de
#                abajo, para que dos ensayos (o los tests) no choquen.
#   --puerto     default 5434: el destino publica 127.0.0.1:<puerto>.
#   --sufijo y --puerto se dan en 'preparar', que los guarda en <trabajo>/config; los demás
#   subcomandos los leen de ahí.
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
#   volcar     volcado-final-vps.sh en modo FINAL (stop + mask) contra el origen, con los shims
#              de ssh/sudo/systemctl al frente del PATH (el ssh falso rechaza todo host que no
#              sea *.invalid).
#   restaurar  restaurar-backup.sh del volcado sobre el destino, con su manifiesto y SIN shims:
#              el systemctl real no conoce la unit en esta máquina y cuenta como inactiva.
#   limpiar    borra los dos contenedores (con sus volúmenes) por sus nombres EXACTOS y compara
#              la lista de contenedores con la de 'preparar'.
#
# Nombres (con sufijo s): contenedores stockapp-ensayo-origen-pg-<s> y stockapp-ensayo-pg-<s>;
# volúmenes stockapp_ensayo_origen_pgdata_<s> y stockapp_ensayo_pgdata_<s>. El script no nombra
# ningún otro contenedor ni volumen: en esta máquina corren el de desarrollo y los de otras
# aplicaciones.
#
# El directorio de trabajo queda con una COPIA DE LOS DATOS del dump: 'limpiar' no lo borra
# (el operador puede querer revisar la salida); borralo a mano al terminar.
#
# Códigos de salida:
#   0  OK
#   2  uso / argumentos inválidos
#   3  precondición: contenedor o volumen del ensayo ya existente, puerto ocupado, directorio
#      de trabajo no vacío o sin preparar, volcado ausente o repetido
#   8  no se pudo consultar o crear (docker con error, Postgres que no arranca)
#   9  alcance: al limpiar, la lista de contenedores no es la de antes de preparar
#   volcar y restaurar devuelven el código del script que corren.
# =======================================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIR_SHIMS="${REPO_ROOT}/deploy/ensayo/shims"

uso() {
    sed -n '/^# Uso:/,/^#   subcomandos los leen/p' "$0" | sed 's/^# \{0,1\}//' >&2
}

error_uso() {
    echo "ERROR: $1" >&2
    uso
    exit 2
}

SUBCOMANDO=""
INICIO=$SECONDS
informar_duracion() {
    local rc=$?
    [[ -z "$SUBCOMANDO" ]] || echo "ensayo-local ${SUBCOMANDO}: $(( SECONDS - INICIO )) s"
    exit "$rc"
}
trap informar_duracion EXIT

# ---------------------------------------------------------------------------------------
# Argumentos
# ---------------------------------------------------------------------------------------
[[ $# -ge 1 ]] || error_uso "falta el subcomando."
case "$1" in
    preparar|volcar|restaurar|limpiar) SUBCOMANDO="$1"; shift ;;
    -h|--help) uso; exit 0 ;;
    *) error_uso "subcomando desconocido: $1" ;;
esac

TRABAJO=""
DUMP_REAL=""
HISTORIAL=()
SUFIJO=""
PUERTO="5434"
RECREAR=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --trabajo|--historial|--sufijo|--puerto)
            [[ $# -ge 2 ]] || error_uso "falta el valor de $1."
            if [[ "$SUBCOMANDO" != "preparar" && "$1" != "--trabajo" ]]; then
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
TRABAJO="$(realpath -m -- "$TRABAJO")"
# La raíz del VPS viejo simulado se pasa a volcado-final-vps.sh como --raiz-remota, que exige
# este patrón.
[[ "$TRABAJO" =~ ^/[A-Za-z0-9._/-]+$ && "$TRABAJO" != *..* ]] \
    || error_uso "--trabajo '${TRABAJO}' tiene que matchear ^/[A-Za-z0-9._/-]+$ (sin '..')."

CONFIG="${TRABAJO}/config"
ESTADO="${TRABAJO}/estado"
RAIZ="${TRABAJO}/vps-viejo"
SALIDA="${TRABAJO}/salida"
ENV_OPERADOR="${TRABAJO}/env-vps-viejo-ensayo"
PS_ANTES="${TRABAJO}/docker-ps-antes.txt"

# ---------------------------------------------------------------------------------------
# Config (preparar la escribe; el resto la lee línea por línea, nunca con 'source')
# ---------------------------------------------------------------------------------------
validar_config() {
    [[ "$SUFIJO" =~ ^[a-z0-9]{0,12}$ ]] || error_uso "--sufijo '${SUFIJO}' no matchea ^[a-z0-9]{0,12}$."
    if [[ ! "$PUERTO" =~ ^[1-9][0-9]{0,4}$ ]] || (( PUERTO > 65535 )); then
        error_uso "--puerto '${PUERTO}' no es un puerto válido."
    fi
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
        esac
    done < "$CONFIG"
    validar_config
}

if [[ "$SUBCOMANDO" == "preparar" ]]; then
    validar_config
else
    leer_config
fi

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
# Desde que se escribe la config, todo fallo deja algo que 'limpiar' sabe borrar.
fallo_preparar() {
    echo "ERROR: $2" >&2
    echo "       Para borrar lo que se haya creado: $0 limpiar --trabajo ${TRABAJO}" >&2
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
}

# ---------------------------------------------------------------------------------------
# volcar
# ---------------------------------------------------------------------------------------
volcar() {
    local rc=0
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
# limpiar
# ---------------------------------------------------------------------------------------
limpiar() {
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
    echo "Recordá borrar '${TRABAJO}' a mano: tiene una copia de los datos del dump."
}

"$SUBCOMANDO"
