#!/usr/bin/env bash
set -euo pipefail

# 00-preflight.sh — diagnóstico READ-ONLY del servidor. NO TOCA NADA.
#
# Correlo apenas llegás. Además de decirte si vas a poder terminar la instalación, imprime el
# CÓDIGO DE MÁQUINA: con eso la licencia se puede emitir en paralelo (desde la laptop del
# proveedor, que es la única que tiene la clave privada) mientras seguís con 01-bootstrap.
# Cero tiempo muerto.
#
# Uso:  sudo ./00-preflight.sh
#
# Salida: 0 si no hay bloqueantes, 1 si hay al menos uno. Los AVISOS no bloquean.

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"
# shellcheck source=lib/validaciones.sh
source "${DIR_KIT}/lib/validaciones.sh"
# shellcheck source=lib/fingerprint.sh
source "${DIR_KIT}/lib/fingerprint.sh"

iniciar_log

BLOQUEANTES=0
marcar_bloqueante() { BLOQUEANTES=$((BLOQUEANTES + 1)); rojo "$*"; }

# El puerto de la API es configurable (Decisión 1): sale de API_PORT en /etc/stockapp/.env, el
# .env que genera 01-bootstrap.sh (Task 3.4) -- NO del api.env que arma install.sh, que nunca
# lo persiste (API_PORT es una de las VARS_CONOCIDAS que install.sh consume para inyectar en la
# unit de systemd por sed y excluye del passthrough, ver install.sh:104-115,412). En una
# instalación virgen ese archivo todavía no existe: no es un error, es el camino feliz, y cae
# al default 5080 sin ruido. Si ya existe (re-corrida del preflight tras 01-bootstrap), se
# respeta el valor real. resolver_api_port vive en lib/validaciones.sh (testeada en
# tests/StockApp.Infrastructure.Tests/Kit/PreflightTests.cs).
#
# ENV_KIT y PG_PORT son overridables por variable de entorno solo para poder testear este
# script sin tocar el sistema real (mismo patrón que KIT_LOG en lib/log.sh); nunca se exponen
# como flag de CLI ni se documentan para el operador. En producción ambos caen a su valor por
# defecto.
ENV_KIT="${ENV_KIT:-/etc/stockapp/.env}"
API_PORT="$(resolver_api_port "$ENV_KIT")"
PG_PORT="${PG_PORT:-5433}"

echo
echo "======================================================================"
echo "  PREFLIGHT — Gestión Municipal / StockApp"
echo "  Este script NO modifica nada. Solo mira."
echo "======================================================================"
echo

# ── Sistema operativo ──────────────────────────────────────────────────────
echo "-- Sistema operativo --"
VERSION_ESPERADA="$(tr -d '[:space:]' < "${DIR_KIT}/VERSION")"
if [[ -r /etc/os-release ]]; then
    # shellcheck disable=SC1091  # /etc/os-release no existe en la máquina de desarrollo
    . /etc/os-release
    info "Detectado: ${PRETTY_NAME:-desconocido}"
    if [[ "${VERSION_ID:-}" == "$VERSION_ESPERADA" ]]; then
        ok "Coincide con la versión fijada en el kit (${VERSION_ESPERADA})."
    else
        marcar_bloqueante "El kit está armado para Ubuntu ${VERSION_ESPERADA} y este servidor es ${VERSION_ID:-desconocido}."
        info "Los paquetes .deb de offline/ están compilados contra ${VERSION_ESPERADA} y no van a instalar acá."
    fi
else
    marcar_bloqueante "No se pudo leer /etc/os-release: no puedo confirmar la versión del sistema."
fi

ARQ="$(uname -m)"
if [[ "$ARQ" == "x86_64" ]]; then
    ok "Arquitectura x86_64."
else
    marcar_bloqueante "Arquitectura '${ARQ}': el kit trae binarios linux-x64 self-contained."
fi

if [[ -d /run/systemd/system ]]; then
    ok "systemd presente."
else
    marcar_bloqueante "systemd no está corriendo: install.sh instala una unit y no hay camino alternativo."
fi

# ── Recursos ───────────────────────────────────────────────────────────────
echo
echo "-- Recursos --"
RAM_MB="$(awk '/MemTotal/ {print int($2/1024)}' /proc/meminfo)"
if (( RAM_MB > 2048 )); then
    ok "RAM: ${RAM_MB} MB."
else
    marcar_bloqueante "RAM: ${RAM_MB} MB (se necesitan más de 2048)."
fi

for punto in /opt /var/lib /var/backups; do
    mkdir -p "$punto" 2>/dev/null || true
    LIBRE_GB="$(df -BG --output=avail "$punto" 2>/dev/null | tail -1 | tr -dc '0-9')"
    if [[ -n "$LIBRE_GB" ]] && (( LIBRE_GB > 10 )); then
        ok "Disco en ${punto}: ${LIBRE_GB} GB libres."
    else
        marcar_bloqueante "Disco en ${punto}: ${LIBRE_GB:-?} GB libres (se necesitan más de 10)."
    fi
done

# ── Puertos ────────────────────────────────────────────────────────────────
echo
echo "-- Puertos --"
if puerto_libre "$API_PORT"; then
    ok "Puerto ${API_PORT} (API) libre."
else
    aviso "Puerto ${API_PORT} (API) OCUPADO por:"
    quien_escucha "$API_PORT"
    info "El puerto es configurable (Decisión 1): definí otro API_PORT en ${ENV_KIT}"
    info "antes de correr 01-bootstrap.sh."
fi

# Idempotencia: en una segunda corrida el 5433 lo ocupa NUESTRO contenedor. Eso es OK, no un
# conflicto -- distinguirlo es lo que permite correr el preflight de nuevo sin falsos rojos.
if puerto_libre "$PG_PORT"; then
    ok "Puerto ${PG_PORT} (Postgres) libre."
elif docker ps --format '{{.Names}}' 2>/dev/null | grep -qx 'stockapp-pg'; then
    ok "Puerto ${PG_PORT} ocupado por el contenedor stockapp-pg (nuestro): esperado en una re-corrida."
else
    marcar_bloqueante "Puerto ${PG_PORT} (Postgres) OCUPADO por algo que no es stockapp-pg:"
    quien_escucha "$PG_PORT"
fi

# ── Software que instala 01-bootstrap (ausente = esperado en servidor virgen) ──
echo
echo "-- Software (lo instala 01-bootstrap.sh si falta) --"
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    ok "Docker instalado y corriendo."
elif command -v docker >/dev/null 2>&1; then
    aviso "Docker instalado pero el daemon no responde. 01-bootstrap.sh va a intentar arrancarlo."
else
    aviso "Docker no está instalado. Lo instala 01-bootstrap.sh desde offline/docker/."
fi

dpkg -s postgresql-client-16 >/dev/null 2>&1 \
    && ok "postgresql-client-16 instalado." \
    || aviso "postgresql-client-16 no está. Lo instala 01-bootstrap.sh desde offline/pgclient/."

command -v curl >/dev/null 2>&1 \
    && ok "curl instalado." \
    || aviso "curl no está. Lo instala 01-bootstrap.sh desde offline/pgclient/."

# Decisión 13 (deuda anotada, no bloqueante): pg_dump corre DESDE EL HOST, nunca por 'docker
# exec' (src/StockApp.Infrastructure/Backups/EjecutorPgDumpProceso.cs:77-88, Process.Start()
# por TCP a 127.0.0.1:5433), así que su versión y la de la imagen del compose viven en archivos
# separados sin validación cruzada. Si difieren, la instalación completa sin avisar y el backup
# pre-deploy revienta después con "server version mismatch".
if command -v pg_dump >/dev/null 2>&1; then
    # '|| true' en ambas asignaciones: bajo 'set -o pipefail', un grep sin coincidencias (por
    # ejemplo cuando todavía no existe servidor/docker-compose.postgres.yml, que recién arma
    # armar-kit.sh en la Fase 4) deja el pipeline en no-cero y aborta TODO el preflight bajo
    # 'set -e' -- justo lo contrario de "INFO/AVISO no bloqueante" que pide la Decisión 13.
    PG_DUMP_MAJOR="$(pg_dump --version | grep -oE '[0-9]+' | head -1 || true)"
    IMAGEN_MAJOR="$(grep -oE 'postgres:[0-9]+' "${DIR_KIT}/servidor/docker-compose.postgres.yml" 2>/dev/null | grep -oE '[0-9]+' | head -1 || true)"
    if [[ -n "$PG_DUMP_MAJOR" && -n "$IMAGEN_MAJOR" && "$PG_DUMP_MAJOR" != "$IMAGEN_MAJOR" ]]; then
        aviso "pg_dump es v${PG_DUMP_MAJOR} pero la imagen del compose es postgres:${IMAGEN_MAJOR}-alpine: pueden no coincidir."
    fi
fi

# ── Instalación previa ─────────────────────────────────────────────────────
echo
echo "-- Instalación previa --"
if systemctl list-unit-files 2>/dev/null | grep -q '^stockapp-api\.service'; then
    aviso "Ya hay una instalación de stockapp-api en este servidor."
    info "Estado: $(systemctl is-active stockapp-api 2>/dev/null || echo desconocido)"
    info "No es un error: install.sh actualiza y respalda la anterior. Pero confirmá que es lo que querés."
    if [[ -f /etc/stockapp/.env ]]; then
        aviso "Existe /etc/stockapp/.env — 01-bootstrap.sh NO lo va a pisar (y hace bien: regenerarlo"
        info "contra un volumen de Postgres ya creado deja la API sin poder conectar, en silencio)."
    fi
else
    ok "No hay instalación previa: este es un servidor limpio."
fi

# ── Red ────────────────────────────────────────────────────────────────────
echo
echo "-- Red --"
IP_LAN="$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="src") print $(i+1)}' | head -1)"
IFAZ="$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="dev") print $(i+1)}' | head -1)"
if [[ -n "$IP_LAN" ]] && es_ipv4_valida "$IP_LAN"; then
    ok "IP de este servidor: ${IP_LAN} (interfaz ${IFAZ})."
else
    marcar_bloqueante "No pude determinar la IP de este servidor."
fi

# DHCP: el riesgo principal de todo el despliegue (diseño 2026-09-10, línea 113).
ES_DHCP=0
if command -v networkctl >/dev/null 2>&1 && networkctl status "$IFAZ" 2>/dev/null | grep -qi 'DHCP'; then
    ES_DHCP=1
elif ls /run/systemd/netif/leases/* >/dev/null 2>&1; then
    ES_DHCP=1
elif grep -rqs 'dhcp4:[[:space:]]*true' /etc/netplan/ 2>/dev/null; then
    ES_DHCP=1
fi

if (( ES_DHCP == 1 )); then
    echo
    echo "  **********************************************************************"
    echo "  *  ATENCIÓN — ESTE SERVIDOR TOMA SU IP POR DHCP                      *"
    echo "  *                                                                    *"
    echo "  *  Si la IP cambia, TODAS las PC clientes pierden la conexión y hay  *"
    echo "  *  que pasar el Configurador por cada una, a mano. Y vos no vas a    *"
    echo "  *  tener acceso a este servidor para arreglarlo.                     *"
    echo "  *                                                                    *"
    echo "  *  ANTES DE SEGUIR: pedile al área de sistemas del municipio una     *"
    echo "  *  RESERVA DHCP POR MAC en el router para esta máquina.              *"
    echo "  *  MAC de ${IFAZ}: $(cat "/sys/class/net/${IFAZ}/address" 2>/dev/null || echo '?')"
    echo "  *                                                                    *"
    echo "  *  Este script NO toca la configuración de red a propósito: tocar    *"
    echo "  *  netplan en una red ajena es cómo te quedás sin conectividad, y    *"
    echo "  *  la política de IPs es del área de sistemas. Ver LEEME.md.         *"
    echo "  **********************************************************************"
    echo
else
    ok "La IP parece estática (no detecté DHCP). Confirmalo igual con el área de sistemas."
fi

if curl -fsS --max-time 5 https://deb.debian.org >/dev/null 2>&1; then
    info "Internet saliente: HAY. (Informativo: el kit funciona igual sin internet.)"
else
    info "Internet saliente: NO detectado. (Informativo: el kit está preparado para esto.)"
fi

# ── Código de máquina ──────────────────────────────────────────────────────
echo
echo "-- Licenciamiento --"
if CODIGO="$(fingerprint_de_esta_maquina)"; then
    echo
    echo "======================================================================"
    echo "  CÓDIGO DE MÁQUINA DE ESTE SERVIDOR"
    echo
    echo "      ${CODIGO}"
    echo
    echo "  Pasáselo YA a la máquina que tiene la clave privada y emití la"
    echo "  licencia mientras seguís con 01-bootstrap.sh:"
    echo
    echo "      StockApp.Licencias.Cli emitir-licencia \\"
    echo "        --clave ~/stockapp-claves/clave-privada.pem \\"
    echo "        --cliente \"Municipio de Carmelo\" \\"
    echo "        --maquina ${CODIGO}"
    echo
    echo "  OJO: este código se REGENERA si alguien reinstala el sistema"
    echo "  operativo (systemd crea un /etc/machine-id nuevo). Si eso pasa,"
    echo "  hay que emitir una licencia nueva."
    echo "======================================================================"
    echo
else
    marcar_bloqueante "No se pudo calcular el código de máquina: sin esto no hay licencia posible."
fi

# ── Resultado ──────────────────────────────────────────────────────────────
echo "======================================================================"
if (( BLOQUEANTES == 0 )); then
    echo "  PREFLIGHT OK — no hay bloqueantes. Siguiente paso: sudo ./01-bootstrap.sh"
    echo "======================================================================"
    exit 0
else
    echo "  PREFLIGHT CON ${BLOQUEANTES} BLOQUEANTE(S) — resolvelos antes de seguir."
    echo "  No se modificó nada en este servidor."
    echo "======================================================================"
    exit 1
fi
