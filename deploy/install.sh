#!/usr/bin/env bash
set -euo pipefail

# install.sh — instala o actualiza StockApp.Api en este VPS Linux.
#
# Corre EN EL SERVIDOR, como root (sudo). Idempotente: correrlo de nuevo (con un tarball
# nuevo, o el mismo) actualiza la instalación existente sin romper nada — respalda la
# instalación anterior antes de sobrescribirla.
#
# Uso:
#   sudo ./install.sh <ruta-al-tar.gz-de-publish-api.sh> <ruta-al-.env>
#
# Ejemplo:
#   sudo ./install.sh ~/stockapp-deploy/stockapp-api-20260729120000-linux-x64.tar.gz \
#                      ~/stockapp-deploy/.env
#
# Qué toca este script (todo nuevo, nada de "pinar" — otro proyecto en Docker que ya corre
# en este mismo VPS con nginx en 0.0.0.0:80/:443 — se verifica y se avisa, pero NUNCA se
# modifica):
#   - usuario/grupo de sistema "stockapp"       (HOME real en /var/lib/stockapp)
#   - paquete postgresql-client-16 y curl        (apt, solo si faltan)
#   - /opt/stockapp-api/                         (binarios de la release actual)
#   - /var/backups/stockapp-api/<timestamp>/     (backup de la release anterior, si había)
#   - /etc/stockapp-api/api.env                  (secretos, 600, generado desde tu .env)
#   - /usr/local/lib/stockapp-api/wait-for-postgres.sh
#   - /etc/systemd/system/stockapp-api.service

if [[ "${EUID}" -ne 0 ]]; then
    echo "ERROR: este script necesita correr como root: sudo $0 <tar.gz> <.env>" >&2
    exit 1
fi

TARBALL="${1:-}"
ENV_FILE="${2:-}"

if [[ -z "$TARBALL" || -z "$ENV_FILE" ]]; then
    echo "Uso: sudo $0 <ruta-al-tar.gz> <ruta-al-.env>" >&2
    exit 1
fi

if [[ ! -f "$TARBALL" ]]; then
    echo "ERROR: no existe el tarball '${TARBALL}'." >&2
    exit 1
fi

if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: no existe el archivo de entorno '${ENV_FILE}'. Copiá deploy/.env.example y completalo." >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_TEMPLATE="${SCRIPT_DIR}/stockapp-api.service"
WAIT_SCRIPT_SRC="${SCRIPT_DIR}/wait-for-postgres.sh"

if [[ ! -f "$UNIT_TEMPLATE" ]]; then
    echo "ERROR: no se encontró '${UNIT_TEMPLATE}' junto a este script." >&2
    exit 1
fi

if [[ ! -f "$WAIT_SCRIPT_SRC" ]]; then
    echo "ERROR: no se encontró '${WAIT_SCRIPT_SRC}' junto a este script." >&2
    exit 1
fi

readonly APP_DIR="/opt/stockapp-api"
readonly STOCKAPP_HOME="/var/lib/stockapp"
readonly ENV_DIR="/etc/stockapp-api"
readonly ENV_TARGET="${ENV_DIR}/api.env"
readonly LIB_DIR="/usr/local/lib/stockapp-api"
readonly UNIT_TARGET="/etc/systemd/system/stockapp-api.service"
readonly SERVICE_NAME="stockapp-api"
readonly BACKUP_ROOT="/var/backups/stockapp-api"

echo "== Verificando convivencia con 'pinar' (otro proyecto en este VPS) =="
if docker ps --format '{{.Names}}' 2>/dev/null | grep -qi 'pinar'; then
    echo "  Detecté contenedores de 'pinar' corriendo. Este script no los toca -- solo gestiona"
    echo "  el usuario 'stockapp', /opt/stockapp-api, /etc/stockapp-api y su propia unit de systemd."
else
    echo "  No se detectaron contenedores de 'pinar' corriendo ahora (no es un problema; este"
    echo "  script no depende de eso, es solo informativo)."
fi

echo "== Leyendo y validando ${ENV_FILE} =="
# shellcheck disable=SC1090
set -a
source "$ENV_FILE"
set +a

for var in POSTGRES_USER POSTGRES_PASSWORD POSTGRES_DB JWT_SECRET \
    BOOTSTRAP_ADMIN_USER BOOTSTRAP_PASSWORD LICENCIA_CLAVE_PUBLICA_BASE64; do
    if [[ -z "${!var:-}" ]]; then
        echo "ERROR: falta '${var}' en '${ENV_FILE}'." >&2
        exit 1
    fi
    if [[ "${!var}" == CAMBIAR* ]]; then
        echo "ERROR: '${var}' todavía tiene el valor de ejemplo de .env.example (empieza con 'CAMBIAR')." >&2
        echo "       Editá '${ENV_FILE}' con un valor real antes de instalar." >&2
        exit 1
    fi
done

if [[ "${#JWT_SECRET}" -lt 32 ]]; then
    echo "ERROR: JWT_SECRET tiene ${#JWT_SECRET} caracteres; HS256 necesita al menos 32." >&2
    exit 1
fi

API_PORT="${API_PORT:-5080}"
if ! [[ "$API_PORT" =~ ^[0-9]+$ ]]; then
    echo "ERROR: API_PORT ('${API_PORT}') no es un número de puerto válido." >&2
    exit 1
fi
echo "  OK. API_PORT=${API_PORT}"

echo "== Paquetes del sistema (postgresql-client-16, curl) =="
NECESITA_APT_UPDATE=0
if ! dpkg -s postgresql-client-16 >/dev/null 2>&1; then
    echo "  postgresql-client-16 no está instalado -- se instala."
    NECESITA_APT_UPDATE=1
fi
if ! command -v curl >/dev/null 2>&1; then
    echo "  curl no está instalado -- se instala."
    NECESITA_APT_UPDATE=1
fi

if [[ "$NECESITA_APT_UPDATE" -eq 1 ]]; then
    apt-get update
    dpkg -s postgresql-client-16 >/dev/null 2>&1 || apt-get install -y postgresql-client-16
    command -v curl >/dev/null 2>&1 || apt-get install -y curl
else
    echo "  Ya están instalados, no se toca apt."
fi

command -v pg_isready >/dev/null 2>&1 || { echo "ERROR: pg_isready no disponible después de instalar postgresql-client-16." >&2; exit 1; }
command -v pg_dump >/dev/null 2>&1 || { echo "ERROR: pg_dump no disponible después de instalar postgresql-client-16." >&2; exit 1; }

echo "== Usuario de sistema 'stockapp' =="
if id -u stockapp >/dev/null 2>&1; then
    echo "  El usuario 'stockapp' ya existe -- no se recrea."
else
    useradd --system --create-home --home-dir "$STOCKAPP_HOME" --shell /usr/sbin/nologin stockapp
    echo "  Usuario 'stockapp' creado (HOME=${STOCKAPP_HOME})."
fi
mkdir -p "$STOCKAPP_HOME"
chown stockapp:stockapp "$STOCKAPP_HOME"
chmod 750 "$STOCKAPP_HOME"

# CRÍTICO (verificado empíricamente, no es una suposición): .NET en Linux resuelve
# Environment.SpecialFolder.LocalApplicationData a "$HOME/.local/share" SOLO SI ese
# directorio YA EXISTE y es legible -- Environment.GetFolderPathCore hace
# Interop.Sys.Access(path, R_OK) y si falla, con SpecialFolderOption.None (el default,
# que es el que usa StockApp.Api), devuelve STRING VACÍO en vez de crear el directorio o
# devolver el path igual. Con un $HOME recién creado por `useradd --create-home` (que NO
# crea .local/share), la primera vez que StockApp.Api arranca, UserDataPathProvider
# resolvería un path RELATIVO ("StockApp/logs", "StockApp/backups", "StockApp/
# licencia.lic") en vez de uno absoluto bajo /var/lib/stockapp -- que bajo
# WorkingDirectory=/opt/stockapp-api y ProtectSystem=strict (solo /var/lib/stockapp es
# escribible) rompería backups y la persistencia de la licencia en silencio. Sin este
# mkdir, la app arrancaría "bien" pero jamás podría guardar un backup ni retener la
# licencia entre reinicios.
mkdir -p "${STOCKAPP_HOME}/.local/share"
chown -R stockapp:stockapp "${STOCKAPP_HOME}/.local"
chmod 750 "${STOCKAPP_HOME}/.local" "${STOCKAPP_HOME}/.local/share"

echo "== Backup de la instalación anterior (si existe) =="
mkdir -p "$BACKUP_ROOT"
if [[ -d "$APP_DIR" ]] && [[ -n "$(ls -A "$APP_DIR" 2>/dev/null || true)" ]]; then
    TS="$(date -u +%Y%m%d%H%M%S)"
    DEST="${BACKUP_ROOT}/${TS}"
    echo "  '${APP_DIR}' ya tiene contenido -- lo respaldo en '${DEST}' antes de sobrescribir."
    mkdir -p "$DEST"
    cp -a "${APP_DIR}/." "${DEST}/"
    echo "  Backup OK (para rollback manual, ver deploy/DEPLOY.md)."
else
    echo "  No hay instalación previa en '${APP_DIR}' (primera instalación)."
fi

echo "== Extrayendo release en ${APP_DIR} =="
mkdir -p "$APP_DIR"
find "$APP_DIR" -mindepth 1 -delete
tar -xzf "$TARBALL" -C "$APP_DIR"

if [[ ! -x "${APP_DIR}/StockApp.Api" ]]; then
    echo "ERROR: el tarball extraído no contiene un ejecutable 'StockApp.Api' en la raíz de ${APP_DIR}." >&2
    echo "       ¿Se generó con deploy/publish-api.sh?" >&2
    exit 1
fi
chown -R stockapp:stockapp "$APP_DIR"

echo "== Generando ${ENV_TARGET} (secretos, 600) =="
mkdir -p "$ENV_DIR"
CONNECTION_STRING="Host=127.0.0.1;Port=5433;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
{
    echo "# Generado por install.sh a partir de $(basename "$ENV_FILE") — NO editar a mano,"
    echo "# volver a correr install.sh con el .env correcto en su lugar."
    echo "Jwt__Secret=${JWT_SECRET}"
    echo "Bootstrap__AdminUser=${BOOTSTRAP_ADMIN_USER}"
    echo "Bootstrap__Password=${BOOTSTRAP_PASSWORD}"
    echo "Licencia__ClavePublicaBase64=${LICENCIA_CLAVE_PUBLICA_BASE64}"
    echo "ConnectionStrings__Default=${CONNECTION_STRING}"
} > "$ENV_TARGET"
chown stockapp:stockapp "$ENV_TARGET"
chmod 600 "$ENV_TARGET"

echo "== Instalando script auxiliar (wait-for-postgres.sh) =="
mkdir -p "$LIB_DIR"
install -m 0755 -o root -g root "$WAIT_SCRIPT_SRC" "${LIB_DIR}/wait-for-postgres.sh"

echo "== Instalando unit de systemd (puerto ${API_PORT}) =="
sed "s/__API_PORT__/${API_PORT}/g" "$UNIT_TEMPLATE" > "$UNIT_TARGET"
chmod 0644 "$UNIT_TARGET"

systemctl daemon-reload
systemctl enable "$SERVICE_NAME" >/dev/null

echo "== Arrancando/reiniciando ${SERVICE_NAME} =="
systemctl restart "$SERVICE_NAME"

echo "  Esperando a que la API responda en 127.0.0.1:${API_PORT}..."
INTENTOS=20
OK=0
for i in $(seq 1 "$INTENTOS"); do
    if curl -fsS "http://127.0.0.1:${API_PORT}/" >/dev/null 2>&1; then
        OK=1
        break
    fi
    sleep 2
done

if [[ "$OK" -eq 1 ]]; then
    echo
    echo "OK: StockApp.Api responde en 127.0.0.1:${API_PORT}."
    echo "Siguiente paso: activar la licencia y conectar el desktop — ver deploy/DEPLOY.md."
else
    echo
    echo "ERROR: la API no respondió en 127.0.0.1:${API_PORT} tras $((INTENTOS * 2))s." >&2
    echo "Revisá:" >&2
    echo "  sudo systemctl status ${SERVICE_NAME} --no-pager" >&2
    echo "  sudo journalctl -u ${SERVICE_NAME} -n 100 --no-pager" >&2
    exit 1
fi
