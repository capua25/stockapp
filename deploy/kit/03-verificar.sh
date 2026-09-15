#!/usr/bin/env bash
set -euo pipefail

# 03-verificar.sh — healthcheck end-to-end. Read-only: no arregla nada, solo comprueba.
#
# Corré esto DESPUÉS de activar la licencia (04-licencia.sh). Es lo último que mirás antes de
# empezar con las PC clientes, y lo último que mirás antes de irte del municipio.

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"
# shellcheck source=lib/validaciones.sh
source "${DIR_KIT}/lib/validaciones.sh"

iniciar_log

readonly ENV_SERVIDOR="/etc/stockapp/.env"
readonly SERVICIO="stockapp-api"

[[ "${EUID}" -eq 0 ]] || error_fatal "Este script necesita root (lee ${ENV_SERVIDOR}): sudo $0"
[[ -f "$ENV_SERVIDOR" ]] || error_fatal "No existe ${ENV_SERVIDOR}. ¿Corrieron 01 y 02?"

# Leemos el .env en una subshell para no contaminar el entorno de este script.
leer_env() { grep -E "^$1=" "$ENV_SERVIDOR" | tail -1 | cut -d= -f2-; }

API_PORT="$(leer_env API_PORT | tr -d '[:space:]')"; API_PORT="${API_PORT:-5080}"
API_BIND="$(leer_env API_BIND | tr -d '[:space:]')"; API_BIND="${API_BIND:-127.0.0.1}"
ADMIN_USER="$(leer_env BOOTSTRAP_ADMIN_USER)"
ADMIN_PASS="$(leer_env BOOTSTRAP_PASSWORD)"

# '|| true' al final del pipe: bajo 'set -o pipefail', si este servidor no tiene ruta de red
# configurada, 'ip route get' sale con "Network is unreachable" (exit 2) y el pipeline entero
# queda en no-cero, abortando TODO el script bajo 'set -e' -- ANTES de correr un solo chequeo.
# Mismo modo de falla, mismo fix, que 00-preflight.sh (Task 2.2). El chequeo 4 ya sabe tratar
# IP_LAN vacía como fallo -- eso no cambia, solo dejamos de morir en silencio antes de llegar ahí.
IP_LAN="$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="src") print $(i+1)}' | head -1 || true)"

FALLOS=0
fallo() { FALLOS=$((FALLOS + 1)); rojo "$*"; }

echo
echo "======================================================================"
echo "  VERIFICACIÓN — Gestión Municipal / StockApp"
echo "======================================================================"

# ── 1 ──────────────────────────────────────────────────────────────────────
echo
echo "[1/8] El servicio está activo"
if systemctl is-active --quiet "$SERVICIO"; then
    ok "stockapp-api activo."
else
    fallo "stockapp-api NO está activo (estado: $(systemctl is-active "$SERVICIO" 2>/dev/null || echo '?'))."
    info "  systemctl status ${SERVICIO} --no-pager"
    info "  journalctl -u ${SERVICIO} -n 100 --no-pager"
fi

# ── 2 ──────────────────────────────────────────────────────────────────────
# El chequeo que distingue "instalaste la versión nueva" de "quedó la vieja andando": un curl
# OK no prueba nada porque una API previa también respondía. Asertamos el VALOR esperado, no la
# mera presencia de la línea: install.sh:425 siempre escribe una, lo que importa es cuál.
echo
echo "[2/8] La unit de systemd declara el bind y el puerto correctos"
ESPERADO="ASPNETCORE_URLS=http://${API_BIND}:${API_PORT}"
if systemctl cat "$SERVICIO" 2>/dev/null | grep -qF "$ESPERADO"; then
    ok "La unit declara ${ESPERADO}."
else
    fallo "La unit NO declara '${ESPERADO}'."
    info "Lo que declara:"
    systemctl cat "$SERVICIO" 2>/dev/null | grep -F 'ASPNETCORE_URLS' || info "  (ninguna línea ASPNETCORE_URLS)"
    info "Si esto sorprende: install.sh inyecta API_BIND/API_PORT del .env por sed (install.sh:424-425)."
fi

# ── 3 ──────────────────────────────────────────────────────────────────────
echo
echo "[3/8] La API responde en loopback"
if curl -fsS --max-time 10 "http://127.0.0.1:${API_PORT}/licencia/estado" >/dev/null; then
    ok "Responde en 127.0.0.1:${API_PORT}."
else
    fallo "No responde en 127.0.0.1:${API_PORT}."
fi

# ── 4 ──────────────────────────────────────────────────────────────────────
# EL chequeo que importa para la LAN: es el único que detecta un API_BIND en loopback. Con
# API_BIND=127.0.0.1 el chequeo 3 pasa igual y ninguna PC del municipio puede conectarse.
echo
echo "[4/8] La API responde desde la IP de la LAN (esto es lo que van a usar las PC)"
if [[ -z "$IP_LAN" ]]; then
    fallo "No pude determinar la IP de la LAN de este servidor."
elif curl -fsS --max-time 10 "http://${IP_LAN}:${API_PORT}/licencia/estado" >/dev/null; then
    ok "Responde en ${IP_LAN}:${API_PORT}."
else
    fallo "NO responde en ${IP_LAN}:${API_PORT} (sí en loopback)."
    info "Casi siempre es una de dos:"
    info "  a) API_BIND quedó en 127.0.0.1 -> la API solo escucha loopback. Corregí"
    info "     ${ENV_SERVIDOR} (API_BIND=0.0.0.0) y volvé a correr 02-instalar.sh."
    info "  b) El firewall bloquea el puerto ${API_PORT}. Revisá:  ufw status"
fi

# ── 5 ──────────────────────────────────────────────────────────────────────
echo
echo "[5/8] Las migraciones de la base están al día"
POSTGRES_USER="$(leer_env POSTGRES_USER)"
POSTGRES_DB="$(leer_env POSTGRES_DB)"
POSTGRES_PASSWORD="$(leer_env POSTGRES_PASSWORD)"
PG_PORT="$(leer_env POSTGRES_PORT | tr -d '[:space:]')"; PG_PORT="${PG_PORT:-5433}"

APLICADAS="$(PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "$PG_PORT" \
    -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc \
    'SELECT count(*) FROM "__EFMigrationsHistory"' 2>/dev/null || echo "")"

if [[ -z "$APLICADAS" ]]; then
    fallo "No pude consultar __EFMigrationsHistory. ¿Postgres está arriba y las credenciales son las correctas?"
elif (( APLICADAS > 0 )); then
    ok "${APLICADAS} migraciones aplicadas."
    info "Última: $(PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "$PG_PORT" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1' 2>/dev/null)"
else
    fallo "La tabla __EFMigrationsHistory está vacía: la API no llegó a migrar."
fi

# ── 6 ──────────────────────────────────────────────────────────────────────
# Decisión 6 del plan (opción C): probamos con las credenciales del .env y, si dan 401,
# preguntamos -- porque al operador se le indica cambiar esa contraseña apenas entra, y desde
# ese momento el .env deja de reflejar la realidad. Un 401 acá NO es un fallo del sistema.
#
# Shape verificado contra el código real (src/StockApp.Api/Endpoints/AuthEndpoints.cs:11,
# record LoginRequest(string? NombreUsuario, string? Contrasena) -- serializado en camelCase
# por ConfigureHttpJsonOptions/JsonSerializerDefaults.Web, sin PropertyNamingPolicy propio, ver
# Program.cs:163) y contra deploy/DEPLOY.md:345-347: los campos son "nombreUsuario"/"contrasena",
# NO "usuario"/"password". La respuesta trae el token en "token" (LoginResponse.Token
# camelCased), eso sí coincide con lo asumido.
echo
echo "[6/8] Login del administrador"

# Nunca una ruta fija bajo /tmp: este script corre como root y /tmp es world-writable -- una
# ruta predecible se puede pre-crear como symlink antes de que el script arranque, y 'curl -o'
# sigue symlinks. Mismo fix que 04-licencia.sh:105 (mktemp + trap en vez de un nombre fijo).
TMP_LOGIN="$(mktemp -t verificar-login.XXXXXX)"
trap 'rm -f "$TMP_LOGIN"' EXIT

intentar_login() {
    curl -sS --max-time 15 -o "$TMP_LOGIN" -w '%{http_code}' \
        -X POST "http://127.0.0.1:${API_PORT}/auth/login" \
        -H 'Content-Type: application/json' \
        --data-binary "$(printf '{"nombreUsuario":"%s","contrasena":"%s"}' "$1" "$2")" 2>/dev/null || echo "000"
}

TOKEN=""
HTTP="$(intentar_login "$ADMIN_USER" "$ADMIN_PASS")"
if [[ "$HTTP" == "401" ]]; then
    aviso "Las credenciales de bootstrap ya no sirven — eso es LO ESPERADO si el admin ya cambió"
    info  "su contraseña (que es lo que se le pide hacer apenas entra). No es un error del sistema."
    read -rp "      Usuario admin actual [${ADMIN_USER}]: " U; U="${U:-$ADMIN_USER}"
    read -rsp "      Contraseña: " P; echo
    HTTP="$(intentar_login "$U" "$P")"
fi

case "$HTTP" in
    200)
        TOKEN="$(sed -n 's/.*"token"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$TMP_LOGIN")"
        [[ -n "$TOKEN" ]] && ok "Login correcto." || fallo "Login devolvió 200 pero sin token en la respuesta."
        ;;
    401) fallo "Login rechazado (401): usuario o contraseña incorrectos." ;;
    423) fallo "Login bloqueado (423): la licencia NO está activada. Corré 04-licencia.sh primero." ;;
    429) fallo "Login limitado (429): demasiados intentos. Esperá un minuto." ;;
    000) fallo "La API no respondió al login." ;;
    *)   fallo "Login devolvió HTTP ${HTTP}." ;;
esac

# ── 7 ──────────────────────────────────────────────────────────────────────
echo
echo "[7/8] Salud del sistema de backups"
if [[ -z "$TOKEN" ]]; then
    fallo "No se pudo verificar: hace falta el login del chequeo 6."
else
    SALUD="$(curl -fsS --max-time 15 -H "Authorization: Bearer ${TOKEN}" \
        "http://127.0.0.1:${API_PORT}/backups/salud" 2>/dev/null || echo "")"
    if [[ -z "$SALUD" ]]; then
        fallo "GET /backups/salud no respondió."
    else
        ok "Responde: ${SALUD}"
        info "Recién instalado puede no haber corrido todavía el primer backup (cada 12h)."
        info "Lo que NO puede pasar es que quede así para siempre: ver deploy/PROCEDIMIENTOS.md."
    fi
fi

# ── 8 ──────────────────────────────────────────────────────────────────────
echo
echo "[8/8] Licencia activada"
ESTADO="$(curl -fsS --max-time 10 "http://127.0.0.1:${API_PORT}/licencia/estado" 2>/dev/null || echo "")"
if printf '%s' "$ESTADO" | grep -q '"activada"[[:space:]]*:[[:space:]]*true'; then
    ok "Licencia activada."
else
    fallo "La licencia NO está activada. La API va a devolver 423 en casi todo."
    info "  sudo ./04-licencia.sh fingerprint      # para emitirla"
    info "  sudo ./04-licencia.sh activar <archivo>"
fi

# ── Cierre ─────────────────────────────────────────────────────────────────
echo
echo "======================================================================"
if (( FALLOS == 0 )); then
    echo "  LOS 8 CHEQUEOS PASARON."
else
    echo "  ${FALLOS} CHEQUEO(S) FALLARON — resolvelos antes de instalar clientes."
fi
echo "======================================================================"
echo
echo "  URL PARA EL CONFIGURADOR (esto es lo que se carga en CADA PC):"
echo
echo "      http://${IP_LAN}:${API_PORT}"
echo
echo "======================================================================"
echo
echo "  !!!  ANTES DE IRTE DEL MUNICIPIO  !!!"
echo
echo "  COPIATE ${ENV_SERVIDOR} AL PENDRIVE."
echo
echo "  Ese archivo tiene la contraseña de Postgres, el JWT_SECRET y la clave"
echo "  pública de licencia. NO vas a tener acceso a este servidor de nuevo."
echo "  Si lo perdés y algún día hace falta entrar a la base, no hay forma de"
echo "  recuperarlo."
echo
echo "      cp ${ENV_SERVIDOR} /media/<pendrive>/env-carmelo-\$(date +%F).txt"
echo
echo "  Copiate también el log de esta instalación:"
echo "      ${KIT_LOG}"
echo
echo "======================================================================"

(( FALLOS == 0 )) && exit 0 || exit 1
