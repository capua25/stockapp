#!/usr/bin/env bash
# =======================================================================================
# deploy/bajar-backup-vps.sh -- baja un backup YA EXISTENTE del VPS por la API, sin escribir
# nada en el VPS.
# =======================================================================================
#
# Corre en la MÁQUINA DEL OPERADOR. Por ssh, contra la API del VPS (en loopback): login,
# GET /backups y GET /backups/<id>/contenido, con el contenido viajando por el STDOUT de ssh
# directo al archivo local. Es de solo lectura sobre el VPS: nunca POST /backups (eso dispara un
# backup nuevo), nunca mktemp ni archivos temporales en el host (es COMPARTIDO con otra
# aplicación: un dump en su /tmp sería legible por otros usuarios), nunca 'curl -o' remoto.
#
# El .env del operador sale de STOCKAPP_ENV_LOCAL (default deploy/.env), como en deploy-vps.sh.
# Credenciales: VPS_ADMIN_USER / VPS_ADMIN_PASSWORD (entorno o .env; si faltan, caen a
# BOOTSTRAP_ADMIN_USER / BOOTSTRAP_PASSWORD, que en una instalación viva probablemente ya no
# sirvan). La contraseña y el token viajan por la ENTRADA ESTÁNDAR del curl remoto, nunca por
# argv: en un VPS compartido /proc/<pid>/cmdline es legible por cualquier usuario.
#
# Uso:
#   deploy/bajar-backup-vps.sh --salida <dir> [--id <n>]
#
#   --salida   directorio LOCAL (se crea y se deja en 700). Quedan <nombreArchivo> y
#              <nombreArchivo>.sha256, en 600.
#   --id       la corrida a bajar. Sin --id: la Exitosa con finalizadaEn más reciente.
#
# Siempre imprime la tabla de corridas (id, finalizadaEn, resultado, nombreArchivo,
# tamanioBytes) antes de elegir. Verifica: nombre de archivo seguro, tamaño == tamanioBytes del
# listado y >= 1024, y 'pg_restore --list' local; si algo falla, borra lo bajado.
#
# Códigos de salida (estables):
#   0  OK
#   2  uso / argumentos inválidos
#   3  precondición: config faltante o inválida, falta jq/pg_restore/sha256sum, login rechazado (401/403),
#      sin corrida exitosa, --id inexistente o no exitoso, el archivo destino ya existe
#   4  descarga inválida: tamaño ≠ listado o < 1024, pg_restore --list falla, nombre de archivo
#      inseguro (lo bajado se borra)
#   8  no se pudo consultar (ssh/curl con error, respuesta ilegible, una Exitosa sin finalizadaEn,
#      login con un código que no es 401/403): "no pude consultar" nunca es "no hay backups"
# =======================================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

uso() {
    sed -n '/^# Uso:/,/^#   --id/p' "$0" | sed 's/^# \{0,1\}//' >&2
}

error_uso() {
    echo "ERROR: $1" >&2
    uso
    exit 2
}

# ---------------------------------------------------------------------------------------
# Argumentos
# ---------------------------------------------------------------------------------------
SALIDA=""
ID_PEDIDO=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --salida|--id)
            [[ $# -ge 2 ]] || error_uso "falta el valor de $1."
            case "$1" in
                --salida) SALIDA="$2" ;;
                --id) ID_PEDIDO="$2" ;;
            esac
            shift 2
            ;;
        -h|--help) uso; exit 0 ;;
        *) error_uso "argumento desconocido: $1" ;;
    esac
done
[[ -n "$SALIDA" ]] || error_uso "falta --salida <dir>."
[[ -z "$ID_PEDIDO" || "$ID_PEDIDO" =~ ^[0-9]+$ ]] || error_uso "--id '${ID_PEDIDO}' no es un número."

# ---------------------------------------------------------------------------------------
# .env del operador (mismo bloque que deploy-vps.sh, con 3: config faltante es precondición)
# ---------------------------------------------------------------------------------------
ENV_LOCAL="${STOCKAPP_ENV_LOCAL:-${REPO_ROOT}/deploy/.env}"
if [[ -n "${STOCKAPP_ENV_LOCAL:-}" && ( ! -f "$ENV_LOCAL" || ! -r "$ENV_LOCAL" ) ]]; then
    echo "ERROR: STOCKAPP_ENV_LOCAL apunta a '${ENV_LOCAL}', que no existe o no se puede leer." >&2
    exit 3
fi
if [[ -f "$ENV_LOCAL" ]]; then
    set -a
    # shellcheck source=/dev/null
    source "$ENV_LOCAL"
    set +a
fi

for variable in VPS_HOST VPS_USER VPS_SSH_PORT; do
    if [[ -z "${!variable:-}" ]]; then
        echo "ERROR: falta ${variable} (en '${ENV_LOCAL}' o en el entorno). ABORTO sin tocar nada." >&2
        exit 3
    fi
done

API_PORT="${API_PORT:-8080}"
[[ "$API_PORT" =~ ^[0-9]+$ ]] || { echo "ERROR: API_PORT ('${API_PORT}') no es un número." >&2; exit 3; }

# API_SCHEME/API_HOSTNAME: misma resolución que deploy-vps.sh. Con https, '--resolve' fuerza la
# conexión a loopback validando el certificado real (nunca -k/--insecure).
if [[ -z "${API_SCHEME+x}" ]]; then
    API_SCHEME="http"
fi
if [[ "$API_SCHEME" != "http" && "$API_SCHEME" != "https" ]]; then
    echo "ERROR: API_SCHEME ('${API_SCHEME}') no es válido -- debe ser exactamente 'http' o 'https'." >&2
    exit 3
fi
if [[ "$API_SCHEME" == "https" ]] && [[ -z "${API_HOSTNAME:-}" ]]; then
    echo "ERROR: falta API_HOSTNAME en '${ENV_LOCAL}' (obligatoria porque API_SCHEME=https)." >&2
    exit 3
fi
CURL_RESOLVE_REMOTO=""
if [[ "$API_SCHEME" == "https" ]]; then
    [[ "$API_HOSTNAME" =~ ^[A-Za-z0-9.-]+$ ]] || { echo "ERROR: API_HOSTNAME ('${API_HOSTNAME}') no es un nombre de host válido." >&2; exit 3; }
    API_URL_BASE="https://${API_HOSTNAME}:${API_PORT}"
    CURL_RESOLVE_REMOTO="--resolve ${API_HOSTNAME}:${API_PORT}:127.0.0.1 "
else
    API_URL_BASE="http://127.0.0.1:${API_PORT}"
fi

VPS_ADMIN_USER="${VPS_ADMIN_USER:-${BOOTSTRAP_ADMIN_USER:-admin}}"
VPS_ADMIN_PASSWORD="${VPS_ADMIN_PASSWORD:-${BOOTSTRAP_PASSWORD:-}}"
if [[ -z "$VPS_ADMIN_PASSWORD" ]]; then
    echo "ERROR: falta VPS_ADMIN_PASSWORD (ni BOOTSTRAP_PASSWORD en '${ENV_LOCAL}')." >&2
    exit 3
fi

for herramienta in jq pg_restore sha256sum; do
    if ! command -v "$herramienta" >/dev/null; then
        echo "ERROR: falta '${herramienta}' en esta máquina." >&2
        exit 3
    fi
done

# ---------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------
ssh_vps() {
    ssh -p "$VPS_SSH_PORT" -o BatchMode=yes "${VPS_USER}@${VPS_HOST}" "$@"
}

# El header de autorización viaja por stdin ('curl -K -'), nunca por argv (ver deploy-vps.sh).
ssh_vps_autenticado() {
    local token="$1" cmd="$2"
    printf 'header = "Authorization: Bearer %s"\n' "$token" | ssh_vps "${cmd} -K -"
}

# Deja el stderr de la última llamada en ERROR_REMOTO (archivo temporal LOCAL, en esta máquina).
ERROR_REMOTO=""
leer_stderr() {
    ERROR_REMOTO="$(cat "$1")"
    rm -f "$1"
}

umask 077
mkdir -p "$SALIDA"
chmod 700 "$SALIDA"

# ---------------------------------------------------------------------------------------
# Login (body por stdin)
# ---------------------------------------------------------------------------------------
echo "== Login en ${API_URL_BASE} como '${VPS_ADMIN_USER}' =="
archivo_stderr="$(mktemp)"
RC=0
RESPUESTA_LOGIN="$(VPS_ADMIN_USER="$VPS_ADMIN_USER" VPS_ADMIN_PASSWORD="$VPS_ADMIN_PASSWORD" \
        jq -nc '{nombreUsuario: env.VPS_ADMIN_USER, contrasena: env.VPS_ADMIN_PASSWORD}' \
    | ssh_vps "curl -fsS ${CURL_RESOLVE_REMOTO}-X POST ${API_URL_BASE}/auth/login -H 'Content-Type: application/json' --data @-" \
        2>"$archivo_stderr")" || RC=$?
leer_stderr "$archivo_stderr"
if [[ "$RC" -eq 22 ]]; then
    # curl -f sale 22 ante cualquier 4xx/5xx; el código HTTP viene en su stderr ("returned error:
    # 401"). Solo 401/403 son credenciales; un 429 (límite de intentos) o un 5xx no.
    CODIGO_HTTP=""
    if [[ "$ERROR_REMOTO" =~ returned\ error:\ ([0-9]{3}) ]]; then
        CODIGO_HTTP="${BASH_REMATCH[1]}"
    fi
    if [[ "$CODIGO_HTTP" == "401" || "$CODIGO_HTTP" == "403" ]]; then
        echo "ERROR: la API rechazó el login de '${VPS_ADMIN_USER}' (HTTP ${CODIGO_HTTP})." >&2
        echo "       Revisá VPS_ADMIN_USER / VPS_ADMIN_PASSWORD. No se reintenta (límite de intentos)." >&2
        exit 3
    fi
    echo "ERROR: el login falló: el servidor respondió ${CODIGO_HTTP:-un código HTTP de error que no se pudo leer}." >&2
    echo "       No es un problema de credenciales: no se reintenta." >&2
    [[ -z "$ERROR_REMOTO" ]] || printf '       stderr: %s\n' "$ERROR_REMOTO" >&2
    exit 8
fi
if [[ "$RC" -ne 0 ]]; then
    echo "ERROR: no se pudo hacer el login (código ${RC}): ssh o la API no respondieron." >&2
    [[ -z "$ERROR_REMOTO" ]] || printf '       stderr: %s\n' "$ERROR_REMOTO" >&2
    exit 8
fi
TOKEN="$(printf '%s' "$RESPUESTA_LOGIN" | jq -r '.token // empty' 2>/dev/null)" || TOKEN=""
if [[ -z "$TOKEN" ]]; then
    echo "ERROR: la respuesta del login no trae un token legible." >&2
    exit 8
fi

# ---------------------------------------------------------------------------------------
# GET /backups
# ---------------------------------------------------------------------------------------
echo "== GET /backups =="
archivo_stderr="$(mktemp)"
RC=0
LISTADO="$(ssh_vps_autenticado "$TOKEN" "curl -fsS ${CURL_RESOLVE_REMOTO}${API_URL_BASE}/backups" 2>"$archivo_stderr")" || RC=$?
leer_stderr "$archivo_stderr"
if [[ "$RC" -ne 0 ]]; then
    echo "ERROR: no se pudo consultar GET /backups (código ${RC})." >&2
    echo "       Esto NO es 'no hay backups': la consulta FALLÓ. ABORTO." >&2
    [[ -z "$ERROR_REMOTO" ]] || printf '       stderr: %s\n' "$ERROR_REMOTO" >&2
    exit 8
fi
if ! printf '%s' "$LISTADO" | jq -e 'type == "array"' >/dev/null 2>&1; then
    echo "ERROR: GET /backups no devolvió una lista JSON legible. ABORTO." >&2
    exit 8
fi

echo "  id  finalizadaEn  resultado  nombreArchivo  tamanioBytes"
printf '%s' "$LISTADO" | jq -r '.[] | "  \(.id)  \(.finalizadaEn)  \(.resultado)  \(.nombreArchivo // "-")  \(.tamanioBytes // "-")"'

if [[ -n "$ID_PEDIDO" ]]; then
    CORRIDA="$(printf '%s' "$LISTADO" | jq -c --argjson id "$ID_PEDIDO" 'map(select(.id == $id)) | first // empty')"
    if [[ -z "$CORRIDA" ]]; then
        echo "ERROR: no hay ninguna corrida con id ${ID_PEDIDO}." >&2
        exit 3
    fi
    if [[ "$(printf '%s' "$CORRIDA" | jq -r '.resultado')" != "Exitosa" ]]; then
        echo "ERROR: la corrida ${ID_PEDIDO} no es Exitosa: no hay un backup completo para bajar." >&2
        exit 3
    fi
else
    if ! printf '%s' "$LISTADO" | jq -e 'all(.[] | select(.resultado == "Exitosa"); (.finalizadaEn | type) == "string")' >/dev/null; then
        echo "ERROR: GET /backups trae una corrida Exitosa sin finalizadaEn: no se puede elegir la más" >&2
        echo "       reciente. Elegí una a mano con --id <n> (ver la tabla de arriba). ABORTO." >&2
        exit 8
    fi
    # Orden por finalizadaEn sin la fracción de segundo (el JSON no siempre trae los mismos
    # dígitos, y como texto "…00.5Z" quedaría después de "…00.12Z"); empate: el id mayor.
    CORRIDA="$(printf '%s' "$LISTADO" | jq -c '
        map(select(.resultado == "Exitosa"))
        | sort_by((.finalizadaEn | sub("\\.[0-9]+"; "")), .id)
        | last // empty')"
    if [[ -z "$CORRIDA" ]]; then
        echo "ERROR: no hay ninguna corrida Exitosa en GET /backups." >&2
        exit 3
    fi
fi

ID="$(printf '%s' "$CORRIDA" | jq -r '.id')"
NOMBRE="$(printf '%s' "$CORRIDA" | jq -r '.nombreArchivo // ""')"
TAMANO_LISTADO="$(printf '%s' "$CORRIDA" | jq -r '.tamanioBytes // ""')"
[[ "$ID" =~ ^[0-9]+$ ]] || { echo "ERROR: id ilegible en el listado: '${ID}'." >&2; exit 8; }
if [[ ! "$NOMBRE" =~ ^backup_[0-9]{8}_[0-9]{9}\.dump$ ]]; then
    echo "ERROR: el nombre de archivo de la corrida ${ID} ('${NOMBRE}') no es un backup_<fecha>_<hora>.dump." >&2
    echo "       No se usa como nombre local (podría salir de --salida). ABORTO sin bajar nada." >&2
    exit 4
fi
[[ "$TAMANO_LISTADO" =~ ^[0-9]+$ ]] || { echo "ERROR: tamanioBytes ilegible en el listado: '${TAMANO_LISTADO}'." >&2; exit 8; }

DESTINO="${SALIDA}/${NOMBRE}"
if [[ -e "$DESTINO" ]]; then
    echo "ERROR: '${DESTINO}' ya existe: no se pisa. Elegí otro --salida o movelo." >&2
    exit 3
fi

# ---------------------------------------------------------------------------------------
# GET /backups/<id>/contenido por el stdout de ssh
# ---------------------------------------------------------------------------------------
echo "== Bajando la corrida ${ID} (${NOMBRE}, ${TAMANO_LISTADO} bytes) =="
descartar() {
    rm -f "$DESTINO"
    echo "       Se borró '${DESTINO}'." >&2
    exit "$1"
}
RC=0
ssh_vps_autenticado "$TOKEN" "curl -fsS ${CURL_RESOLVE_REMOTO}${API_URL_BASE}/backups/${ID}/contenido -o -" > "$DESTINO" || RC=$?
chmod 600 "$DESTINO"
if [[ "$RC" -ne 0 ]]; then
    echo "ERROR: la descarga falló (código ${RC})." >&2
    descartar 8
fi
TAMANO_LOCAL="$(stat -c %s "$DESTINO")"
if [[ "$TAMANO_LOCAL" -ne "$TAMANO_LISTADO" ]]; then
    echo "ERROR: se bajaron ${TAMANO_LOCAL} bytes y el listado dice ${TAMANO_LISTADO}." >&2
    descartar 4
fi
if [[ "$TAMANO_LOCAL" -lt 1024 ]]; then
    echo "ERROR: el backup pesa ${TAMANO_LOCAL} bytes (< 1024): no es un dump de StockApp." >&2
    descartar 4
fi
if ! pg_restore --list "$DESTINO" >/dev/null; then
    echo "ERROR: 'pg_restore --list' no puede leer lo bajado: no es un dump -Fc válido." >&2
    descartar 4
fi

( cd "$SALIDA" && sha256sum "$NOMBRE" > "${NOMBRE}.sha256" )
chmod 600 "${DESTINO}.sha256"

echo
echo "OK: ${DESTINO} (${TAMANO_LOCAL} bytes)"
echo "    $(cat "${DESTINO}.sha256")"
