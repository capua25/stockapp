#!/usr/bin/env bash
set -euo pipefail

# 01-bootstrap.sh — prepara el servidor: Docker, Postgres, secretos y firewall.
#
# Offline-first (decisión 4 del diseño 2026-09-10): NUNCA asume que hay internet. Instala desde
# offline/ y solo cae a apt si hay internet CONFIRMADO.
#
# Uso:
#   sudo ./01-bootstrap.sh                # puerto de API por defecto (5080)
#   sudo ./01-bootstrap.sh --puerto 8080  # override del puerto (Decisión 1: configurable)

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"
# shellcheck source=lib/validaciones.sh
source "${DIR_KIT}/lib/validaciones.sh"

iniciar_log

readonly ENV_DIR="/etc/stockapp"
readonly ENV_SERVIDOR="${ENV_DIR}/.env"
readonly COMPOSE_DIR="/opt/stockapp"
readonly PG_PORT=5433

API_PORT=5080
API_BIND="0.0.0.0"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --puerto) API_PORT="${2:-}"; shift 2 ;;
        *) error_fatal "Opción desconocida: $1" ;;
    esac
done

[[ "${EUID}" -eq 0 ]] || error_fatal "Este script necesita root: sudo $0"
es_puerto_valido "$API_PORT" || error_fatal "'${API_PORT}' no es un puerto válido."

echo
echo "======================================================================"
echo "  BOOTSTRAP — Gestión Municipal / StockApp"
echo "  Puerto de la API: ${API_PORT}   Bind: ${API_BIND}"
echo "======================================================================"

# ── 1. Docker ──────────────────────────────────────────────────────────────
echo
echo "-- Docker --"
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    ok "Docker ya está instalado y corriendo — no se toca."
else
    if compgen -G "${DIR_KIT}/offline/docker/*.deb" >/dev/null; then
        info "Instalando Docker desde offline/docker/ (sin red)."
        # Todos juntos en un solo dpkg: las dependencias entre containerd.io, docker-ce-cli,
        # docker-ce y docker-compose-plugin se resuelven entre sí en la misma invocación. Uno
        # por uno fallaría por orden de dependencias.
        dpkg -i "${DIR_KIT}"/offline/docker/*.deb \
            || error_fatal "Falló la instalación de Docker desde los .deb del kit.
       Casi siempre significa que el kit se armó para otra versión de Ubuntu.
       Este servidor: $( . /etc/os-release && echo "${VERSION_ID}" )
       El kit espera : $(tr -d '[:space:]' < "${DIR_KIT}/VERSION")"
    elif curl -fsS --max-time 5 https://download.docker.com >/dev/null 2>&1; then
        aviso "No hay .deb en offline/docker/, pero SÍ hay internet: instalando por apt."
        apt-get update && apt-get install -y docker.io docker-compose-v2
    else
        error_fatal "Docker no está instalado, no hay .deb en offline/docker/ y no hay internet.
       No hay forma de continuar. El kit está incompleto."
    fi

    systemctl enable --now docker
    docker info >/dev/null 2>&1 || error_fatal "Docker quedó instalado pero el daemon no responde."
    ok "Docker instalado y corriendo."
fi

# ── 2. postgresql-client-16 y curl ─────────────────────────────────────────
# ESTO es lo que hace que install.sh no toque la red después: install.sh:243-258 solo llama a
# apt si alguno de los dos falta.
echo
echo "-- postgresql-client-16 y curl --"
FALTA=0
dpkg -s postgresql-client-16 >/dev/null 2>&1 || FALTA=1
command -v curl >/dev/null 2>&1 || FALTA=1

if (( FALTA == 0 )); then
    ok "Ya están instalados."
elif compgen -G "${DIR_KIT}/offline/pgclient/*.deb" >/dev/null; then
    info "Instalando desde offline/pgclient/ (sin red)."
    dpkg -i "${DIR_KIT}"/offline/pgclient/*.deb \
        || error_fatal "Falló la instalación de postgresql-client-16/curl desde los .deb del kit."
    ok "Instalados."
else
    error_fatal "Faltan postgresql-client-16 y/o curl, y no hay .deb en offline/pgclient/.
       Sin ellos install.sh va a intentar usar apt (install.sh:252-255) y va a fallar sin internet."
fi

command -v pg_isready >/dev/null 2>&1 || error_fatal "pg_isready no quedó disponible."
command -v pg_dump    >/dev/null 2>&1 || error_fatal "pg_dump no quedó disponible (los backups lo necesitan)."

# ── 3. Imagen de Postgres ──────────────────────────────────────────────────
echo
echo "-- Imagen de Postgres --"
if docker image inspect postgres:16-alpine >/dev/null 2>&1; then
    ok "La imagen postgres:16-alpine ya está cargada."
elif [[ -f "${DIR_KIT}/offline/postgres-16-alpine.tar" ]]; then
    info "Cargando la imagen desde offline/ (sin red)."
    docker load < "${DIR_KIT}/offline/postgres-16-alpine.tar"
    ok "Imagen cargada."
else
    error_fatal "No está la imagen postgres:16-alpine ni offline/postgres-16-alpine.tar."
fi

# ── 4. Secretos ────────────────────────────────────────────────────────────
# LA GUARDIA MÁS IMPORTANTE DEL KIT. Ver diseño 2026-09-10, línea 88.
echo
echo "-- Secretos (${ENV_SERVIDOR}) --"
if [[ -f "$ENV_SERVIDOR" ]]; then
    ok "Ya existe ${ENV_SERVIDOR} — NO se toca."
    aviso "Regenerarlo sería un error grave: el volumen de Postgres ya se creó con la"
    info  "contraseña que está en ese archivo. Con una contraseña nueva la API no puede"
    info  "conectar, y Npgsql NO incluye la connection string en sus excepciones -- el error"
    info  "que verías no dice nada de contraseñas y se pierden horas buscando en otro lado."
    info  "Si de verdad querés empezar de cero, hay que borrar TAMBIÉN el volumen:"
    info  "    docker compose -f ${COMPOSE_DIR}/docker-compose.postgres.yml down -v"
    info  "    rm ${ENV_SERVIDOR}"
    info  "Eso BORRA LA BASE DE DATOS. En un servidor ya en uso, no lo hagas."
else
    info "Generando secretos nuevos."
    mkdir -p "$ENV_DIR"
    chmod 700 "$ENV_DIR"

    JWT_SECRET="$(openssl rand -base64 48)"
    POSTGRES_PASSWORD="$(openssl rand -base64 24 | tr -d '/+=;#')"

    echo
    info "Ahora elegí el usuario y la contraseña del ADMINISTRADOR del sistema."
    info "Con estos datos entra la primera vez desde el desktop, y después los cambia."
    read -rp "      Usuario administrador [admin]: " ADMIN_USER
    ADMIN_USER="${ADMIN_USER:-admin}"

    while true; do
        read -rsp "      Contraseña (mín. 8, con letra y número): " ADMIN_PASS; echo
        if ! password_admin_valida "$ADMIN_PASS"; then
            aviso "No cumple las reglas de la API: mínimo 8 caracteres, al menos una letra y un número."
            continue
        fi
        read -rsp "      Repetila: " ADMIN_PASS2; echo
        [[ "$ADMIN_PASS" == "$ADMIN_PASS2" ]] && break
        aviso "No coinciden."
    done

    echo
    info "Pegá la CLAVE PÚBLICA de licenciamiento en Base64 (la que generaste con"
    info "'generar-claves'). La clave PRIVADA nunca va acá ni a este servidor."
    read -rp "      Clave pública: " CLAVE_PUBLICA
    [[ -n "$CLAVE_PUBLICA" ]] || error_fatal "La clave pública es obligatoria: sin ella la API no puede validar la licencia."

    UMASK_PREVIO="$(umask)"
    umask 077
    cat > "$ENV_SERVIDOR" <<ENVEOF
# Generado por 01-bootstrap.sh el $(date -Is) en $(hostname).
#
# NO REGENERAR este archivo mientras exista el volumen de Postgres: la contraseña de abajo es
# la que quedó grabada en la base la primera vez. Cambiarla acá deja a la API sin poder
# conectar, con un error que no menciona la contraseña.
#
# COPIALO AL PENDRIVE ANTES DE IRTE. No vas a tener acceso a este servidor de nuevo.

POSTGRES_USER=stockapp
POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
POSTGRES_DB=stockapp

API_BIND=${API_BIND}
API_PORT=${API_PORT}

JWT_SECRET=${JWT_SECRET}

BOOTSTRAP_ADMIN_USER=${ADMIN_USER}
BOOTSTRAP_PASSWORD=${ADMIN_PASS}

LICENCIA_CLAVE_PUBLICA_BASE64=${CLAVE_PUBLICA}
ENVEOF
    umask "$UMASK_PREVIO"
    chmod 600 "$ENV_SERVIDOR"
    ok "Secretos generados en ${ENV_SERVIDOR} (permisos 600)."
fi

# ── 5. Postgres ────────────────────────────────────────────────────────────
echo
echo "-- Postgres --"
mkdir -p "$COMPOSE_DIR"
install -m 0644 "${DIR_KIT}/servidor/docker-compose.postgres.yml" "${COMPOSE_DIR}/docker-compose.postgres.yml"

docker compose --env-file "$ENV_SERVIDOR" \
    -f "${COMPOSE_DIR}/docker-compose.postgres.yml" up -d

info "Esperando a que Postgres acepte conexiones..."
bash "${DIR_KIT}/servidor/wait-for-postgres.sh" \
    || error_fatal "Postgres no llegó a responder en 127.0.0.1:${PG_PORT}.
       Revisá:  docker compose -f ${COMPOSE_DIR}/docker-compose.postgres.yml logs"
ok "Postgres arriba en 127.0.0.1:${PG_PORT} (nunca expuesto a la red)."

# ── 6. Firewall ────────────────────────────────────────────────────────────
# Decisión 12 (RESUELTA 2026-09-13): el kit LAN NO configura ufw, nunca. Mismo modelo que
# install.sh usa para el VPS (install.sh:211-221,471-483): imprime los comandos, no los
# ejecuta. Razón: el servidor del municipio probablemente sirve más cosas que esta API
# (impresoras de red, compartidos, accesos de terceros) -- un 'ufw enable' con solo las 2
# reglas que conoce este instalador tira abajo todo lo demás. A diferencia de un VPS, acá NO
# hay operador leyendo la terminal más tarde: por eso el aviso se persiste TAMBIÉN en un
# archivo (junto al .env, que es lo único de este servidor que el proveedor se va a llevar
# al pendrive) con los valores ya expandidos, no variables literales.
echo
echo "-- Firewall --"
SUBRED="$(ip -4 -o addr show scope global 2>/dev/null | awk '{print $4}' | head -1)"
[[ -n "$SUBRED" ]] || SUBRED="<no se pudo detectar la subred>"

readonly POST_INSTALACION="${ENV_DIR}/POST-INSTALACION.txt"
cat > "$POST_INSTALACION" <<POSTEOF
StockApp — pendientes después de la instalación
Generado por 01-bootstrap.sh el $(date -Is) en $(hostname).

FIREWALL: este kit NO activa ufw automáticamente. La API va a quedar accesible en
${API_BIND}:${API_PORT} sin filtro hasta que lo actives vos.

El tráfico es HTTP PLANO: usuario, contraseña y JWT viajan sin cifrar por la LAN. Si este
servidor tiene ufw, corré esto en este orden (SSH PRIMERO -- si no, quien administre el
servidor remotamente pierde el acceso apenas lo actives):

    sudo ufw allow 22/tcp
    sudo ufw allow from ${SUBRED} to any port ${API_PORT} proto tcp
    sudo ufw enable

Si ya tenías ufw activo, alcanza con la segunda línea.
POSTEOF
chmod 600 "$POST_INSTALACION"

aviso "La API va a quedar accesible en ${API_BIND}:${API_PORT} sin filtro."
info  "El tráfico es HTTP PLANO: usuario, contraseña y JWT viajan sin cifrar por la LAN."
info  "Este kit NO activa el firewall por vos. Si este servidor tiene ufw, corré (SSH primero):"
info  "    sudo ufw allow 22/tcp"
info  "    sudo ufw allow from ${SUBRED} to any port ${API_PORT} proto tcp"
info  "    sudo ufw enable"
info  "Estos comandos también quedaron guardados en: ${POST_INSTALACION}"

echo
echo "======================================================================"
echo "  BOOTSTRAP COMPLETO."
echo "  Siguiente paso:  sudo ./02-instalar.sh"
echo "======================================================================"
