#!/usr/bin/env bash
set -euo pipefail

# publicar-release.sh -- sube una versión empaquetada por build/pack-win.ps1 al feed de
# actualizaciones propio (el que sirve StockApp.Api en /updates, carpeta del servidor
# /var/lib/stockapp/updates, dueño stockapp:stockapp, 750). Corre en la MÁQUINA DEL OPERADOR.
#
# Uso:
#   build/publicar-release.sh <version> [--origen <dir>]
#
#   --origen  carpeta con lo que generó pack-win.ps1 (default: <repo>/releases/win). Sirve
#             cuando el empaquetado corrió en otro checkout (p. ej. el de Windows) y se copió
#             la carpeta acá.
#
# Configuración (deploy/.env local o entorno). OBLIGATORIAS, sin default a propósito: un
# host por defecto subiría el feed al servidor equivocado en silencio.
#   VPS_HOST, VPS_USER, VPS_SSH_PORT
# Opcionales: VPS_DIR (default ~/stockapp-deploy, carpeta de staging del usuario SSH),
#   API_SCHEME (http|https), API_HOSTNAME, API_PORT (default 8080) -- igual que deploy-vps.sh.
#
# ⚠️  EL VPS PUEDE SER COMPARTIDO: todo lo remoto está acotado por nombre a las rutas de
# StockApp (/var/lib/stockapp/updates y <VPS_DIR>/updates-staging). Nunca se toca nginx ni
# se reinicia la API. Se asume sudo sin contraseña para el usuario SSH, como deploy-vps.sh.
#
# Pasos, en este orden:
#   1. Validación LOCAL con jq: <version> es el Full de mayor versión del releases.win.json y
#      cada FileName (nombre seguro, existe en --origen con su Size).
#   2. Relevamiento del servidor (nombres + tamaños). Salida vacía = primera publicación;
#      ssh que falla = "no pude relevar" -> aborta sin subir nada.
#   3. scp al staging de los .nupkg que el servidor no tiene con ese tamaño, y del JSON.
#   4. sudo install de los paquetes en la carpeta del feed.
#   5. Re-relevamiento: CADA FileName del JSON nuevo tiene que estar con su Size. Si no,
#      aborta SIN tocar el feed vigente.
#   6. El feed (releases.win.json) va ÚLTIMO, con rename atómico: nunca hay un feed que
#      apunte a paquetes que todavía no están.
#   7. Verificación final por HTTP: el feed publicado dice <version> y el full responde 200
#      con su Content-Length.
#
# Retirar una versión mala: Velopack no hace downgrade. Se republica la última versión BUENA
# con un número MAYOR (empaquetar de nuevo desde ese código con una versión nueva).

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

uso() {
    echo "Uso: $0 <version> [--origen <dir>]" >&2
}

VERSION=""
ORIGEN="${REPO_ROOT}/releases/win"

while [[ "$#" -gt 0 ]]; do
    case "$1" in
        --origen)
            if [[ "$#" -lt 2 || -z "$2" ]]; then
                echo "ERROR: --origen necesita una carpeta." >&2
                uso
                exit 1
            fi
            ORIGEN="$2"
            shift 2
            ;;
        --*)
            echo "ERROR: opción desconocida '$1'." >&2
            uso
            exit 1
            ;;
        *)
            if [[ -n "$VERSION" ]]; then
                echo "ERROR: versión ya especificada como '${VERSION}'; argumento inesperado '$1'." >&2
                exit 1
            fi
            VERSION="$1"
            shift
            ;;
    esac
done

if [[ -z "$VERSION" ]]; then
    uso
    exit 1
fi
if [[ ! "$VERSION" =~ ^[0-9A-Za-z.+-]+$ ]]; then
    echo "ERROR: la versión '${VERSION}' tiene caracteres no válidos." >&2
    exit 1
fi

# deploy/.env se carga ANTES de exigir las variables: VPS_HOST/VPS_SSH_PORT pueden venir de ahí.
ENV_LOCAL="${REPO_ROOT}/deploy/.env"
if [[ -f "$ENV_LOCAL" ]]; then
    set -a
    # shellcheck source=/dev/null
    source "$ENV_LOCAL"
    set +a
fi

VPS_HOST="${VPS_HOST:?Definí VPS_HOST (host del VPS) en deploy/.env o en el entorno}"
VPS_USER="${VPS_USER:?Definí VPS_USER (usuario SSH del VPS) en deploy/.env o en el entorno}"
VPS_SSH_PORT="${VPS_SSH_PORT:?Definí VPS_SSH_PORT (puerto SSH del VPS) en deploy/.env o en el entorno}"
# El "~" queda literal a propósito (ver deploy-vps.sh): lo expande el shell/sftp REMOTO.
VPS_DIR="${VPS_DIR:-~/stockapp-deploy}"
if [[ ! "$VPS_DIR" =~ ^[A-Za-z0-9._~/-]+$ ]]; then
    echo "ERROR: VPS_DIR ('${VPS_DIR}') tiene caracteres no válidos (se usa en comandos remotos)." >&2
    exit 1
fi

API_PORT="${API_PORT:-8080}"
if [[ -z "${API_SCHEME+x}" ]]; then
    API_SCHEME="http"
fi
if [[ "$API_SCHEME" != "http" && "$API_SCHEME" != "https" ]]; then
    echo "ERROR: API_SCHEME ('${API_SCHEME}') no es válido -- debe ser exactamente 'http' o 'https'." >&2
    exit 1
fi
if [[ "$API_SCHEME" == "https" ]] && [[ -z "${API_HOSTNAME:-}" ]]; then
    echo "ERROR: falta API_HOSTNAME en '${ENV_LOCAL}' (obligatoria porque API_SCHEME=https)." >&2
    exit 1
fi

CURL_RESOLVE_REMOTO=""
if [[ "$API_SCHEME" == "https" ]]; then
    API_URL_BASE="https://${API_HOSTNAME}:${API_PORT}"
    CURL_RESOLVE_REMOTO="--resolve ${API_HOSTNAME}:${API_PORT}:127.0.0.1 "
else
    API_URL_BASE="http://127.0.0.1:${API_PORT}"
fi

if ! command -v jq >/dev/null 2>&1; then
    echo "ERROR: falta 'jq' (necesario para validar releases.win.json). Instalalo e intentá de nuevo." >&2
    exit 1
fi

DIR_FEED="/var/lib/stockapp/updates"
STAGING="${VPS_DIR}/updates-staging"
JSON_NOMBRE="releases.win.json"

ssh_vps() {
    ssh -p "$VPS_SSH_PORT" -o BatchMode=yes "${VPS_USER}@${VPS_HOST}" "$@"
}

scp_vps() {
    scp -P "$VPS_SSH_PORT" -o BatchMode=yes "$@"
}

# El staging remoto se borra pase lo que pase (trap EXIT, también en falla). Si el borrado
# falla se avisa, pero no se pisa el código de salida del script.
STAGING_TOCADO=0
limpiar_staging_remoto() {
    if [[ "$STAGING_TOCADO" -eq 1 ]]; then
        if ! ssh_vps "rm -rf -- ${STAGING}"; then
            echo "AVISO: no se pudo borrar el staging remoto '${STAGING}'; borralo a mano." >&2
        fi
    fi
}
trap limpiar_staging_remoto EXIT

fallar() {
    echo "ERROR: $*" >&2
    exit 1
}

# ---------------------------------------------------------------------------------------
# Paso 1 -- validación local
# ---------------------------------------------------------------------------------------

NOMBRES=()
TAMANOS=()
FULL_NUEVO=""
FULL_NUEVO_TAMANO=""

paso1_validar_local() {
    local json="${ORIGEN}/${JSON_NOMBRE}"
    [[ -d "$ORIGEN" ]] || fallar "no existe la carpeta de origen '${ORIGEN}'."
    [[ -f "$json" ]] || fallar "falta '${json}' (¿corrió pack-win.ps1?)."

    local versiones mayor
    if ! versiones="$(jq -r '.Assets[] | select(.Type == "Full") | .Version' "$json")"; then
        fallar "'${json}' no es un feed válido de Velopack (jq falló)."
    fi
    mayor="$(printf '%s\n' "$versiones" | sort -V | tail -n 1)"
    if [[ "$mayor" != "$VERSION" ]]; then
        fallar "la versión pedida '${VERSION}' no es la mayor del feed (Full de mayor versión: '${mayor:-ninguna}')."
    fi

    local pares
    if ! pares="$(jq -r '.Assets[] | [.FileName, (.Size | tostring), .Type, .Version] | @tsv' "$json")"; then
        fallar "no se pudo leer la lista de paquetes de '${json}'."
    fi

    local nombre tamano tipo ver local_tamano
    while IFS=$'\t' read -r nombre tamano tipo ver; do
        [[ -n "$nombre" ]] || continue
        if [[ ! "$nombre" =~ ^[A-Za-z0-9._-]+\.nupkg$ ]]; then
            fallar "FileName inseguro en el feed: '${nombre}' (va a un comando remoto)."
        fi
        if [[ ! "$tamano" =~ ^[0-9]+$ ]]; then
            fallar "Size inválido para '${nombre}': '${tamano}'."
        fi
        [[ -f "${ORIGEN}/${nombre}" ]] || fallar "falta el paquete '${nombre}' en '${ORIGEN}'."
        local_tamano="$(stat -c %s "${ORIGEN}/${nombre}")"
        if [[ "$local_tamano" != "$tamano" ]]; then
            fallar "'${nombre}' pesa ${local_tamano} bytes y el feed dice ${tamano}."
        fi
        NOMBRES+=("$nombre")
        TAMANOS+=("$tamano")
        if [[ "$tipo" == "Full" && "$ver" == "$VERSION" ]]; then
            FULL_NUEVO="$nombre"
            FULL_NUEVO_TAMANO="$tamano"
        fi
    done <<<"$pares"

    [[ "${#NOMBRES[@]}" -gt 0 ]] || fallar "'${json}' no lista ningún paquete."
    [[ -n "$FULL_NUEVO" ]] || fallar "no se encontró el paquete Full de la versión '${VERSION}' en el feed."
}

# ---------------------------------------------------------------------------------------
# Paso 2 -- relevamiento remoto (nombres + tamaños)
# ---------------------------------------------------------------------------------------

RELEVAMIENTO=""

relevar_servidor() {
    local rc=0
    # 'sudo test -d' falso y 'sudo' que no puede elevar dan ambos exit 1 dentro del 'if' y
    # se verían igual que "carpeta inexistente": por eso se exige antes sudo sin contraseña.
    ssh_vps "sudo -n true" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        fallar "no se pudo relevar el servidor: sudo sin contraseña no está disponible para ${VPS_USER} (ssh exit ${rc}). No se subió nada."
    fi
    rc=0
    RELEVAMIENTO="$(ssh_vps "if sudo test -d ${DIR_FEED}; then sudo find ${DIR_FEED} -maxdepth 1 -type f -printf '%f %s\n'; fi")" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        fallar "no se pudo relevar el servidor (ssh exit ${rc}) -- no es lo mismo que carpeta vacía. No se subió nada."
    fi
}

tamano_remoto() {
    awk -v n="$1" '$1 == n { print $2; exit }' <<<"$RELEVAMIENTO"
}

# ---------------------------------------------------------------------------------------
# Paso 3 -- staging + scp
# ---------------------------------------------------------------------------------------

PENDIENTES=()

paso3_subir_a_staging() {
    local i nombre
    PENDIENTES=()
    for i in "${!NOMBRES[@]}"; do
        nombre="${NOMBRES[$i]}"
        if [[ "$(tamano_remoto "$nombre")" == "${TAMANOS[$i]}" ]]; then
            echo "[publicar] ya está en el servidor con el mismo tamaño: ${nombre}"
        else
            PENDIENTES+=("$nombre")
        fi
    done

    STAGING_TOCADO=1
    ssh_vps "rm -rf -- ${STAGING} && mkdir -p -m 700 ${STAGING}" \
        || fallar "no se pudo preparar el staging remoto '${STAGING}'."

    for nombre in "${PENDIENTES[@]}"; do
        echo "[publicar] subiendo ${nombre}"
        scp_vps "${ORIGEN}/${nombre}" "${VPS_USER}@${VPS_HOST}:${STAGING}/" \
            || fallar "falló el scp de '${nombre}'. El feed vigente no se tocó."
    done
    echo "[publicar] subiendo ${JSON_NOMBRE}"
    scp_vps "${ORIGEN}/${JSON_NOMBRE}" "${VPS_USER}@${VPS_HOST}:${STAGING}/" \
        || fallar "falló el scp de '${JSON_NOMBRE}'. El feed vigente no se tocó."
}

# ---------------------------------------------------------------------------------------
# Paso 4 -- instalar los paquetes
# ---------------------------------------------------------------------------------------

paso4_instalar_paquetes() {
    local nombre
    ssh_vps "sudo install -d -o stockapp -g stockapp -m 750 ${DIR_FEED}" \
        || fallar "no se pudo crear/asegurar la carpeta del feed '${DIR_FEED}'."
    for nombre in "${PENDIENTES[@]}"; do
        ssh_vps "sudo install -o stockapp -g stockapp -m 640 ${STAGING}/${nombre} ${DIR_FEED}/${nombre}" \
            || fallar "no se pudo instalar '${nombre}' en '${DIR_FEED}'. El feed vigente no se tocó."
    done
}

# ---------------------------------------------------------------------------------------
# Paso 5 -- verificar que TODO paquete del feed nuevo está con su tamaño
# ---------------------------------------------------------------------------------------

paso5_verificar_paquetes() {
    local i nombre remoto faltan=0
    relevar_servidor
    for i in "${!NOMBRES[@]}"; do
        nombre="${NOMBRES[$i]}"
        remoto="$(tamano_remoto "$nombre")"
        if [[ "$remoto" != "${TAMANOS[$i]}" ]]; then
            echo "ERROR: en el servidor '${nombre}' tiene tamaño '${remoto:-<ausente>}' y el feed dice ${TAMANOS[$i]}." >&2
            faltan=1
        fi
    done
    if [[ "$faltan" -ne 0 ]]; then
        fallar "verificación remota fallida: el feed NO se actualizó (sigue el vigente)."
    fi
}

# ---------------------------------------------------------------------------------------
# Paso 6 -- el feed, último y con rename atómico
# ---------------------------------------------------------------------------------------

paso6_publicar_feed() {
    ssh_vps "sudo install -o stockapp -g stockapp -m 640 ${STAGING}/${JSON_NOMBRE} ${DIR_FEED}/${JSON_NOMBRE}.tmp && sudo mv -f ${DIR_FEED}/${JSON_NOMBRE}.tmp ${DIR_FEED}/${JSON_NOMBRE}" \
        || fallar "no se pudo publicar '${JSON_NOMBRE}'."
}

# ---------------------------------------------------------------------------------------
# Paso 7 -- verificación final por HTTP
# ---------------------------------------------------------------------------------------

# $1 = etiqueta ("remota"/"local"); $2 = cuerpo del feed servido por la API.
verificar_feed_publicado() {
    local etiqueta="$1" cuerpo="$2"
    if ! jq -e --arg v "$VERSION" '[.Assets[] | select(.Type == "Full" and .Version == $v)] | length > 0' <<<"$cuerpo" >/dev/null 2>&1; then
        fallar "verificación ${etiqueta}: el ${JSON_NOMBRE} servido por la API no contiene la versión ${VERSION}. El feed YA se reemplazó en el servidor; revisá la API."
    fi
}

paso7_verificar_http() {
    local cuerpo cabeceras primera largo

    cuerpo="$(ssh_vps "curl -fsS ${CURL_RESOLVE_REMOTO}${API_URL_BASE}/updates/${JSON_NOMBRE}")" \
        || fallar "verificación remota: la API no sirvió '${API_URL_BASE}/updates/${JSON_NOMBRE}'. El feed YA se reemplazó en el servidor; revisá la API."
    verificar_feed_publicado "remota" "$cuerpo"

    cabeceras="$(ssh_vps "curl -fsSI ${CURL_RESOLVE_REMOTO}${API_URL_BASE}/updates/${FULL_NUEVO}")" \
        || fallar "verificación remota: la API no sirvió '${FULL_NUEVO}'."
    primera="${cabeceras%%$'\n'*}"
    if [[ "$primera" != *" 200"* ]]; then
        fallar "verificación remota: '${FULL_NUEVO}' no respondió 200 (${primera})."
    fi
    largo="$(awk 'tolower($1) == "content-length:" { gsub(/\r/, "", $2); print $2; exit }' <<<"$cabeceras")"
    if [[ "$largo" != "$FULL_NUEVO_TAMANO" ]]; then
        fallar "verificación remota: Content-Length de '${FULL_NUEVO}' es '${largo:-<ausente>}' y debería ser ${FULL_NUEVO_TAMANO}."
    fi

    if [[ "$API_SCHEME" == "https" && -n "${API_HOSTNAME:-}" ]]; then
        cuerpo="$(curl -fsS "https://${API_HOSTNAME}:${API_PORT}/updates/${JSON_NOMBRE}")" \
            || fallar "verificación local: no se pudo traer 'https://${API_HOSTNAME}:${API_PORT}/updates/${JSON_NOMBRE}'. El feed YA se reemplazó en el servidor."
        verificar_feed_publicado "local" "$cuerpo"
    fi
}

paso1_validar_local
relevar_servidor
paso3_subir_a_staging
paso4_instalar_paquetes
paso5_verificar_paquetes
paso6_publicar_feed
paso7_verificar_http

echo "[publicar] OK: versión ${VERSION} publicada y verificada."
