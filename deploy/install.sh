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

# Exactamente 2 argumentos (review deploy-vps-linux, IMPORTANTE 2): un glob ambiguo como
# 'stockapp-api-*-linux-x64.tar.gz .env' contra un deploy/dist/ con más de un tarball
# (publish-api.sh no borra los anteriores a propósito -- son el mecanismo de rollback, ver
# DEPLOY.md) expande a MÁS de 2 argumentos. Sin esta guarda, el .env real terminaba
# corriéndose a $3 (ignorado en silencio) y ENV_FILE quedaba siendo un segundo tarball --
# que pasaba el chequeo '[[ -f ]]' de abajo y llegaba intacto hasta el 'source' más abajo:
# un 'source' de un binario gzip, COMO ROOT.
if [[ "$#" -ne 2 ]]; then
    echo "Uso: sudo $0 <ruta-al-tar.gz> <ruta-al-.env>" >&2
    echo "Recibí $# argumento(s); deben ser exactamente 2. Si le pasaste un glob y" >&2
    echo "'deploy/dist/' tiene más de un tarball, escribí la ruta exacta del que" >&2
    echo "querés instalar en vez de un patrón." >&2
    exit 1
fi

TARBALL="$1"
ENV_FILE="$2"

if [[ ! -f "$TARBALL" ]]; then
    echo "ERROR: no existe el tarball '${TARBALL}'." >&2
    exit 1
fi

if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: no existe el archivo de entorno '${ENV_FILE}'. Copiá deploy/.env.example y completalo." >&2
    exit 1
fi

# Defensa en profundidad además del chequeo de argumentos de arriba: que ENV_FILE no sea,
# él mismo, un tarball -- y que tenga forma de archivo de entorno -- ANTES de hacer
# 'source' sobre él más abajo.
if [[ "$ENV_FILE" == *.tar.gz || "$ENV_FILE" == *.tgz ]]; then
    echo "ERROR: '${ENV_FILE}' tiene extensión de tarball, no de archivo de entorno." >&2
    echo "       Uso: sudo $0 <ruta-al-tar.gz> <ruta-al-.env>" >&2
    exit 1
fi

if ! grep -qE '^[A-Za-z_][A-Za-z0-9_]*=' "$ENV_FILE"; then
    echo "ERROR: '${ENV_FILE}' no parece un archivo de entorno (ninguna línea con forma" >&2
    echo "       CLAVE=valor). ¿Es realmente tu .env?" >&2
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
readonly BACKUPS_A_CONSERVAR=3

# Variables que este script ya conoce y escribe explícitamente en $ENV_TARGET. Cualquier
# otra variable definida en $ENV_FILE se pasa TAL CUAL (ver "Passthrough" más abajo,
# IMPORTANTE 6 del review deploy-vps-linux) -- para que un override puesto a mano (p.ej.
# RateLimiting__Login__PermitLimit) sobreviva a la próxima actualización.
readonly VARS_CONOCIDAS=(POSTGRES_USER POSTGRES_PASSWORD POSTGRES_DB API_PORT JWT_SECRET \
    BOOTSTRAP_ADMIN_USER BOOTSTRAP_PASSWORD LICENCIA_CLAVE_PUBLICA_BASE64)

es_var_conocida() {
    local nombre="$1" v
    for v in "${VARS_CONOCIDAS[@]}"; do
        [[ "$nombre" == "$v" ]] && return 0
    done
    return 1
}

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

echo "== Deteniendo ${SERVICE_NAME} (si está corriendo) antes de tocar ${APP_DIR} =="
# IMPORTANTE 3 (review deploy-vps-linux): sin esto, en una actualización el proceso
# self-contained sigue vivo mientras se borra/repuebla /opt/stockapp-api -- ensambles
# todavía no cargados se resuelven contra los archivos NUEVOS, y el resultado es un
# MissingMethodException/TypeLoadException impredecible hasta el próximo restart.
# 'is-active' en vez de 'stop' directo: en la primera instalación la unit ni siquiera
# existe todavía, y no queremos que eso sea un error bajo 'set -e'.
if systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
    systemctl stop "$SERVICE_NAME"
    echo "  Detenido."
else
    echo "  No estaba corriendo (primera instalación, o ya estaba detenido) -- nada que parar."
fi

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

echo "== Podando backups viejos (conservando los últimos ${BACKUPS_A_CONSERVAR}) =="
# IMPORTANTE 5 (review deploy-vps-linux): cada corrida copia el release completo (~100 MB)
# y nada lo borraba nunca -- 20 updates ~ 2 GB. Es el único disco compartido con 'pinar' en
# este VPS: si se llena, Postgres/Redis de 'pinar' dejan de escribir -- es el único camino
# por el que este despliegue podría tumbar producción ajena. Cuidado extremo con el
# borrado: BACKUP_ROOT es readonly y absoluto; "$ts" sale de nombres de directorio que YA
# existen bajo BACKUP_ROOT (find -mindepth 1 -maxdepth 1); "${DEST_VIEJO:?}" es un
# cinturón extra contra un 'rm -rf' con variable vacía.
if [[ -d "$BACKUP_ROOT" ]]; then
    mapfile -t BACKUPS_VIEJOS < <(find "$BACKUP_ROOT" -mindepth 1 -maxdepth 1 -type d -printf '%f\n' | sort -r | tail -n "+$((BACKUPS_A_CONSERVAR + 1))")
    for ts in "${BACKUPS_VIEJOS[@]}"; do
        [[ -n "$ts" ]] || continue
        DEST_VIEJO="${BACKUP_ROOT}/${ts}"
        echo "  Borrando backup viejo '${DEST_VIEJO}' (fuera de los últimos ${BACKUPS_A_CONSERVAR})."
        rm -rf -- "${DEST_VIEJO:?}"
    done
fi

echo "== Extrayendo release =="
echo "  Verificando integridad del tarball..."
# IMPORTANTE 4 (review deploy-vps-linux): esta verificación, y toda la extracción, corren
# contra un directorio de STAGING -- nunca contra $APP_DIR directamente. Antes, 'find
# -delete' corría ANTES de 'tar -xzf': un tarball corrupto/truncado (p.ej. un scp cortado a
# mitad de camino, escenario realista con un tarball de ~100MB) dejaba $APP_DIR vacío y el
# script salía por 'set -e', con la instalación rota y sólo recuperable a mano. Ahora un
# tarball corrupto falla ACÁ, antes de tocar un solo byte de la instalación existente.
if ! tar -tzf "$TARBALL" >/dev/null; then
    echo "ERROR: '${TARBALL}' está corrupto o incompleto (falló 'tar -tzf')." >&2
    echo "       No se tocó ${APP_DIR}. Si el servicio anterior estaba corriendo y lo" >&2
    echo "       necesitás arriba ya: sudo systemctl start ${SERVICE_NAME}" >&2
    exit 1
fi

STAGING_DIR="${APP_DIR}.new"
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"
tar -xzf "$TARBALL" -C "$STAGING_DIR"

if [[ ! -x "${STAGING_DIR}/StockApp.Api" ]]; then
    echo "ERROR: el tarball extraído no contiene un ejecutable 'StockApp.Api' en su raíz." >&2
    echo "       ¿Se generó con deploy/publish-api.sh? No se tocó ${APP_DIR}." >&2
    rm -rf "$STAGING_DIR"
    exit 1
fi
chown -R stockapp:stockapp "$STAGING_DIR"

echo "  Swap atómico: reemplazando ${APP_DIR} por la release nueva..."
# 'mv' entre dos directorios del mismo filesystem (ambos bajo /opt) es un simple rename --
# prácticamente instantáneo, minimiza a casi cero la ventana sin binarios en $APP_DIR.
OLD_DIR=""
if [[ -d "$APP_DIR" ]]; then
    OLD_DIR="${APP_DIR}.previo.$$"
    mv "$APP_DIR" "$OLD_DIR"
fi
mv "$STAGING_DIR" "$APP_DIR"
[[ -n "$OLD_DIR" ]] && rm -rf "$OLD_DIR"

echo "== Generando ${ENV_TARGET} (secretos, 600) =="
mkdir -p "$ENV_DIR"
CONNECTION_STRING="Host=127.0.0.1;Port=5433;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
# MENOR 8 (review deploy-vps-linux): umask 077 ANTES de crear el archivo. Sin esto,
# '> "$ENV_TARGET"' lo crea con el umask por defecto (0644) y hay una ventana -- hasta el
# 'chmod 600' de más abajo -- en la que otro usuario local de este VPS compartido podría
# leer JWT_SECRET y las contraseñas. El 'chmod 600' se mantiene igual, como cinturón y
# tiradores.
UMASK_PREVIO="$(umask)"
umask 077
{
    echo "# Generado por install.sh a partir de $(basename "$ENV_FILE") — NO editar a mano,"
    echo "# volver a correr install.sh con el .env correcto en su lugar."
    echo "Jwt__Secret=${JWT_SECRET}"
    echo "Bootstrap__AdminUser=${BOOTSTRAP_ADMIN_USER}"
    echo "Bootstrap__Password=${BOOTSTRAP_PASSWORD}"
    echo "Licencia__ClavePublicaBase64=${LICENCIA_CLAVE_PUBLICA_BASE64}"
    echo "ConnectionStrings__Default=${CONNECTION_STRING}"

    # Passthrough (IMPORTANTE 6, review deploy-vps-linux): cualquier variable EXTRA que el
    # operador haya agregado a mano en $ENV_FILE (p.ej. RateLimiting__Login__PermitLimit
    # para ajustar el límite de login sin recompilar -- ver deploy/.env.example) se copia
    # tal cual acá. Sin esto, esta sección solo escribía estas 5 claves fijas y CUALQUIER
    # override manual se perdía en la próxima corrida de install.sh (que regenera
    # $ENV_TARGET desde cero).
    while IFS='=' read -r NOMBRE _; do
        [[ -z "$NOMBRE" || "$NOMBRE" == \#* ]] && continue
        es_var_conocida "$NOMBRE" || echo "${NOMBRE}=${!NOMBRE}"
    done < <(grep -E '^[A-Za-z_][A-Za-z0-9_]*=' "$ENV_FILE")
} > "$ENV_TARGET"
umask "$UMASK_PREVIO"
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
# CRÍTICO (review deploy-vps-linux): antes se pedía '/', que NO está en la allowlist de
# BloqueoLicenciaMiddleware -- con la licencia recién instalada (siempre desactivada al
# principio) '/' devuelve 423 y 'curl -f' sale con código 22, así que este healthcheck
# fallaba SIEMPRE en una instalación de cero, aunque el servicio estuviera perfectamente
# sano. '/licencia/estado' sí está en la allowlist (LicenciaEndpoints.cs, anónimo, sin rate
# limit) y responde 200 tanto con la licencia activada como desactivada -- sirve como
# healthcheck real antes y después del paso 4 de deploy/DEPLOY.md.
# MENOR 9 (review deploy-vps-linux): 90 intentos x 2s = 180s, alineado con
# TimeoutStartSec=180 de stockapp-api.service (antes: 20 x 2s = 40s, muy por debajo --
# daba un "ERROR" falso en un primer arranque con migración lenta, aunque el servicio
# terminara arrancando bien segundos después).
INTENTOS=90
OK=0
for i in $(seq 1 "$INTENTOS"); do
    if curl -fsS "http://127.0.0.1:${API_PORT}/licencia/estado" >/dev/null 2>&1; then
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
