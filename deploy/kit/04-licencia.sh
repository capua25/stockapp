#!/usr/bin/env bash
set -euo pipefail

# 04-licencia.sh — consulta el código de máquina y activa la licencia.
#
# Va DESPUÉS de 02-instalar.sh y ANTES de tocar las PC clientes: sin licencia la API responde
# 423 en casi todo, y no tiene sentido configurar clientes contra un servidor bloqueado.
#
# La clave PRIVADA nunca toca este servidor. Acá solo entra el texto de una licencia ya firmada
# en la máquina del proveedor.
#
# Uso:
#   sudo ./04-licencia.sh fingerprint
#   sudo ./04-licencia.sh activar /ruta/a/licencia.txt

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"
# shellcheck source=lib/fingerprint.sh
source "${DIR_KIT}/lib/fingerprint.sh"
# shellcheck source=lib/validaciones.sh
source "${DIR_KIT}/lib/validaciones.sh"

iniciar_log

readonly ENV_SERVIDOR="/etc/stockapp/.env"

# Este script corre DESPUÉS de 01-bootstrap.sh: a esta altura ${ENV_SERVIDOR} siempre existe, y
# es 600/root (a diferencia de 00-preflight.sh, cuyo "camino feliz" es que el archivo TODAVÍA no
# exista). Sin root, la lectura de ese archivo falla en silencio y resolver_api_port cae al
# puerto por defecto sin avisar del problema real -- por eso la guardia explícita, igual que
# 02-instalar.sh y 03-verificar.sh.
[[ "${EUID}" -eq 0 ]] || error_fatal "Este script necesita root (lee ${ENV_SERVIDOR}): sudo $0"

# El puerto sale del .env, nunca hardcodeado (ver Decisión 1 del plan): si se instaló en otro
# puerto, este script tiene que seguirlo. resolver_api_port vive en lib/validaciones.sh (ya
# testeada en PreflightTests.cs) -- no se duplica esa lógica acá.
API_PORT="$(resolver_api_port "$ENV_SERVIDOR")"
readonly BASE_URL="http://127.0.0.1:${API_PORT}"

estado_licencia() {
    curl -fsS --max-time 10 "${BASE_URL}/licencia/estado" 2>/dev/null
}

campo_json() {
    # Extractor mínimo: el shape de /licencia/estado es plano y conocido
    # (LicenciaEstadoResponse: activada, codigoMaquina). No metemos jq como dependencia nueva
    # del servidor solo para leer dos campos.
    sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\{0,1\}\([^,\"}]*\)\"\{0,1\}.*/\1/p"
}

cmd_fingerprint() {
    local desde_api="" desde_bash=""

    if RESPUESTA="$(estado_licencia)"; then
        desde_api="$(printf '%s' "$RESPUESTA" | campo_json codigoMaquina)"
    fi
    desde_bash="$(fingerprint_de_esta_maquina || true)"

    if [[ -n "$desde_api" ]]; then
        echo
        echo "  CÓDIGO DE MÁQUINA (según la API, que es la que valida):"
        echo
        echo "      ${desde_api}"
        echo
        if [[ -n "$desde_bash" && "$desde_bash" != "$desde_api" ]]; then
            aviso "El código calculado desde /etc/machine-id (${desde_bash}) NO coincide con el"
            info  "que reporta la API. GANA EL DE LA API: es el que se usa para validar."
            info  "Una diferencia acá suele significar que /etc/machine-id cambió después de"
            info  "instalar, o que la API está corriendo en otra máquina/contenedor de la que creés."
        fi
    elif [[ -n "$desde_bash" ]]; then
        echo
        echo "  CÓDIGO DE MÁQUINA (calculado de /etc/machine-id; la API todavía no responde):"
        echo
        echo "      ${desde_bash}"
        echo
        info "Cuando la API esté arriba, volvé a correr esto para confirmar contra ella."
    else
        error_fatal "No pude obtener el código de máquina ni de la API ni de /etc/machine-id."
    fi
}

cmd_activar() {
    local archivo="${1:-}"
    [[ -n "$archivo" ]] || error_fatal "Falta el archivo de licencia. Uso: $0 activar <archivo>"
    [[ -f "$archivo" ]] || error_fatal "No existe el archivo de licencia '${archivo}'."

    local licencia
    licencia="$(tr -d '\r\n' < "$archivo")"
    [[ -n "$licencia" ]] || error_fatal "El archivo '${archivo}' está vacío."

    estado_licencia >/dev/null 2>&1 \
        || error_fatal "La API no responde en ${BASE_URL}.
       ¿Corriste 02-instalar.sh? Revisá:  systemctl status stockapp-api --no-pager"

    info "Activando la licencia contra ${BASE_URL}..."

    local cuerpo codigo_http tmp_resp
    # Nunca una ruta fija bajo /tmp: este script corre como root y /tmp es world-writable -- una
    # ruta predecible se puede pre-crear como symlink a /etc/shadow o /etc/stockapp/.env antes de
    # que el script arranque, y 'curl -o' sigue symlinks. mktemp crea el archivo con modo 600,
    # nombre impredecible, y no sigue un symlink preexistente en esa ruta. El trap reemplaza el
    # 'rm -f' manual y además limpia si el script muere antes de llegar a esta línea.
    tmp_resp="$(mktemp -t licencia-respuesta.XXXXXX)"
    trap 'rm -f "$tmp_resp"' EXIT

    # --write-out separa cuerpo de status: necesitamos el status para distinguir 400 de 429.
    codigo_http="$(curl -sS --max-time 20 -o "$tmp_resp" -w '%{http_code}' \
        -X POST "${BASE_URL}/licencia/activar" \
        -H 'Content-Type: application/json' \
        --data-binary "$(printf '{"licencia":"%s"}' "$licencia")" || true)"
    cuerpo="$(cat "$tmp_resp" 2>/dev/null || true)"

    if [[ "$codigo_http" == "200" ]]; then
        # Confirmación independiente: no confiamos en la respuesta del POST, releemos el estado.
        local confirmacion
        confirmacion="$(estado_licencia | campo_json activada)"
        if [[ "$confirmacion" == "true" ]]; then
            echo
            ok "LICENCIA ACTIVADA. Confirmado contra GET /licencia/estado."
            info "Siguiente paso:  sudo ./03-verificar.sh"
            return 0
        fi
        error_fatal "El POST devolvió 200 pero /licencia/estado sigue diciendo activada=false.
       Revisá:  journalctl -u stockapp-api -n 100 --no-pager"
    fi

    # Traducción de errores a algo accionable. Los títulos salen de
    # LicenciaEndpoints.MotivoDe (:54-62).
    echo
    if printf '%s' "$cuerpo" | grep -q 'emitida para otra máquina'; then
        rojo "La licencia fue emitida para OTRA máquina."
        info "Casi siempre significa una de dos cosas:"
        info "  1. Se emitió contra un código viejo. Volvé a correr:  sudo $0 fingerprint"
        info "     y emití una licencia nueva contra el código que imprime AHORA."
        info "  2. Reinstalaron el sistema operativo de este servidor. systemd genera un"
        info "     /etc/machine-id nuevo en cada instalación, así que el código cambió y la"
        info "     licencia anterior quedó inservible. Hay que emitir una nueva."
    elif printf '%s' "$cuerpo" | grep -q 'firma de la licencia'; then
        rojo "La firma de la licencia no es válida."
        info "La clave PÚBLICA instalada en este servidor no corresponde a la clave PRIVADA con"
        info "la que se firmó. Verificá LICENCIA_CLAVE_PUBLICA_BASE64 en ${ENV_SERVIDOR} contra"
        info "la clave pública del par que usaste para emitir."
    elif printf '%s' "$cuerpo" | grep -q 'formato'; then
        rojo "El texto de la licencia no tiene un formato válido."
        info "¿El archivo tiene la línea completa que imprimió 'emitir-licencia', sin cortes?"
    elif [[ "$codigo_http" == "429" ]]; then
        rojo "Demasiados intentos de activación (rate limit)."
        info "Esperá un minuto y volvé a intentar."
    else
        rojo "La activación falló (HTTP ${codigo_http})."
        info "Respuesta: ${cuerpo}"
    fi
    exit 1
}

case "${1:-}" in
    fingerprint) cmd_fingerprint ;;
    activar)     shift; cmd_activar "${1:-}" ;;
    *)
        echo "Uso:"
        echo "  sudo $0 fingerprint              # imprime el código de máquina"
        echo "  sudo $0 activar <archivo>        # activa la licencia de ese archivo"
        exit 1
        ;;
esac
