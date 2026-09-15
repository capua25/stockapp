#!/usr/bin/env bash
set -euo pipefail

# 02-instalar.sh — resuelve rutas y le pasa el trabajo a servidor/install.sh.
#
# install.sh NO SE MODIFICA (diseño 2026-09-10, línea 92): está verificado en producción en el
# VPS, es idempotente, hace swap atómico, respalda la instalación previa y poda backups viejos.
# Este wrapper existe solo para dos cosas: (1) que no tengas que tipear dos rutas largas y
# exactas parado en el servidor, y (2) que correr 02 sin haber corrido 01 falle con un mensaje
# que diga qué hacer, en vez de con un error de install.sh sobre un .env que no existe.
#
# Uso:  sudo ./02-instalar.sh

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"

iniciar_log

readonly ENV_SERVIDOR="/etc/stockapp/.env"
readonly INSTALL_SH="${DIR_KIT}/servidor/install.sh"

[[ "${EUID}" -eq 0 ]] || error_fatal "Este script necesita root: sudo $0"

# Guardia principal: 01 tiene que haber corrido.
if [[ ! -f "$ENV_SERVIDOR" ]]; then
    error_fatal "No existe ${ENV_SERVIDOR}.
       Eso significa que 01-bootstrap.sh todavía no corrió (o falló antes de generarlo).
       Corré primero:  sudo ./01-bootstrap.sh
       Ese script instala Docker, levanta Postgres y genera el archivo de secretos que
       install.sh necesita para instalar la API."
fi

if ! docker ps --format '{{.Names}}' 2>/dev/null | grep -qx 'stockapp-pg'; then
    error_fatal "El contenedor 'stockapp-pg' no está corriendo.
       install.sh arranca la API, y la API corre las migraciones contra Postgres ANTES de
       empezar a responder: sin la base arriba, el servicio queda colgado o falla.
       Revisá:  docker ps -a --filter name=stockapp-pg
       Y si hace falta, volvé a correr:  sudo ./01-bootstrap.sh"
fi

[[ -f "$INSTALL_SH" ]] || error_fatal "No se encontró ${INSTALL_SH}. ¿El pendrive está completo?"

# El tarball: uno solo, y su nombre exacto. install.sh exige EXACTAMENTE 2 argumentos
# (install.sh:40-46) justamente porque un glob que expande a varios tarballs terminaba
# corriendo el .env a $3 y haciendo 'source' de un gzip como root. Acá resolvemos el nombre
# nosotros y abortamos si hay ambigüedad, en vez de pasarle un patrón.
mapfile -t TARBALLS < <(find "${DIR_KIT}/offline" -maxdepth 1 -name 'stockapp-api-*-linux-x64.tar.gz' | sort)

case "${#TARBALLS[@]}" in
    0) error_fatal "No encontré ningún stockapp-api-*-linux-x64.tar.gz en ${DIR_KIT}/offline/." ;;
    1) TARBALL="${TARBALLS[0]}" ;;
    *) error_fatal "Hay ${#TARBALLS[@]} tarballs en ${DIR_KIT}/offline/ y no sé cuál querés:
$(printf '         %s\n' "${TARBALLS[@]}")
       Dejá uno solo. El kit se arma con una única versión de la API (armar-kit.sh)." ;;
esac

echo
info "Tarball : ${TARBALL}"
info "Secretos: ${ENV_SERVIDOR}"
info "Delegando en servidor/install.sh (no se modifica; es el mismo que corre en el VPS)."
echo

# Sin 'exec': queremos que el tee del log siga vivo hasta el final.
bash "$INSTALL_SH" "$TARBALL" "$ENV_SERVIDOR"

echo
ok "install.sh terminó bien."
info "Siguiente paso: activar la licencia."
info "  sudo ./04-licencia.sh activar <archivo-de-licencia>"
info "y recién después:  sudo ./03-verificar.sh"
