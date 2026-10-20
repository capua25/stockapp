#!/usr/bin/env bash
# Deploy-hook de certbot para StockApp: copia el certificado renovado al directorio que lee la API
# y reinicia SOLO stockapp-api.
#
# Origen: es el hook que corría en el servidor de producción (copiado en solo lectura el
# 2026-10-20), versionado acá para poder instalarlo igual en el servidor nuevo. Diferencias con
# el original: el dominio ya no está cableado (todo sale de RENEWED_LINEAGE, que certbot exporta
# con la ruta del certificado renovado, /etc/letsencrypt/live/<dominio>), `install` en lugar de
# cp+chown+chmod (los PEM nacen ya con dueño y modo finales, nunca legibles por otros) y se
# aborta con exit 2, sin copiar ni reiniciar nada, si el origen no está completo.
#
# Instalación (una vez, en el servidor):
#   sudo install -m 0755 deploy/tls/stockapp-tls-deploy-hook.sh /usr/local/sbin/
# Registro, al emitir el certificado:
#   sudo certbot certonly --standalone -d <dominio> --deploy-hook /usr/local/sbin/stockapp-tls-deploy-hook.sh
# certbot guarda el hook como `renew_hook` en /etc/letsencrypt/renewal/<dominio>.conf, o sea
# POR CERTIFICADO: corre solo cuando se renueva el de StockApp. A propósito NO se instala en
# /etc/letsencrypt/renewal-hooks/deploy/, porque ahí correría para todos los certificados del
# servidor y reiniciaría stockapp-api cuando renueve el de cualquier otra aplicación.
#
# `certbot renew --dry-run` NO ejecuta los hooks: para probarlo de verdad hay que renovar
# a mano con `certbot renew --force-renewal --cert-name <dominio>` (gasta cupo de emisión).
#
# Códigos de salida: 0 = copiado y stockapp-api reiniciado; 2 = RENEWED_LINEAGE vacía,
# inexistente o sin fullchain.pem y privkey.pem (no se tocó nada).
set -euo pipefail

DEST_DIR="/var/lib/stockapp/tls"
ORIGEN="${RENEWED_LINEAGE:-}"

if [[ -z "$ORIGEN" ]]; then
    echo "ERROR: RENEWED_LINEAGE vacía o sin definir: este script lo debe llamar certbot." >&2
    exit 2
fi
if [[ ! -d "$ORIGEN" || ! -f "${ORIGEN}/fullchain.pem" || ! -f "${ORIGEN}/privkey.pem" ]]; then
    echo "ERROR: RENEWED_LINEAGE='${ORIGEN}' no existe o no tiene fullchain.pem y privkey.pem." >&2
    exit 2
fi

install -d -o stockapp -g stockapp -m 0750 "$DEST_DIR"
install -o stockapp -g stockapp -m 0600 "${ORIGEN}/fullchain.pem" "${DEST_DIR}/fullchain.pem"
install -o stockapp -g stockapp -m 0600 "${ORIGEN}/privkey.pem" "${DEST_DIR}/privkey.pem"

systemctl restart stockapp-api
