#!/usr/bin/env bash
# =======================================================================================
# instalar-en-el-host.sh -- arma el host simulado DENTRO del contenedor ubuntu:24.04, como root.
# =======================================================================================
# Lo corre HostSimulado.Crear después de copiar los fuentes a /opt/hostsim-src. Deja:
#   - el usuario stockapp-deploy-user (uid 1001, el de las sesiones ssh del operador);
#   - los shims en /usr/local/sbin (por delante de /usr/sbin y /usr/bin en el PATH de las
#     sesiones), salvo `ufw`, que va en /usr/sbin/ufw donde vive el real, y `certbot` y
#     `docker`, que se enlazan recién cuando apt-get "instala" el paquete;
#   - `elevar`: copia setuid-root de setpriv, con la que el shim de sudo eleva de verdad;
#   - el estado vacío bajo /var/lib/hostsim, con sshd "escuchando" en el puerto indicado;
#   - el certificado autofirmado que "emite" el certbot falso (lo copió HostSimulado.Crear
#     a /opt/hostsim-cert; el openssl REAL del operador se copia a /usr/local/bin).
# Uso: instalar-en-el-host.sh PUERTO_SSH
# =======================================================================================
set -euo pipefail

SRC=/opt/hostsim-src
LIB=/usr/local/lib/hostsim
ESTADO=/var/lib/hostsim
puerto_ssh="${1:?uso: instalar-en-el-host.sh PUERTO_SSH}"

useradd --create-home --shell /bin/bash --uid 1001 stockapp-deploy-user

install -d -m 0755 "$LIB" "${LIB}/paquetes"
install -m 0644 "${SRC}/lib.sh" "${LIB}/lib.sh"
install -m 0755 "${SRC}/red-permite" "${LIB}/red-permite"
install -m 4755 -o root -g root /usr/bin/setpriv "${LIB}/elevar"

for shim in sudo apt-get systemctl systemd-run ss journalctl; do
    install -m 0755 "${SRC}/${shim}" "/usr/local/sbin/${shim}"
done
install -m 0755 "${SRC}/ufw" /usr/sbin/ufw
install -m 0755 "${SRC}/certbot" "${LIB}/paquetes/certbot"
install -m 0755 "${SRC}/docker" "${LIB}/paquetes/docker"

install -d -m 0755 "$ESTADO" "${ESTADO}/instalado" "${ESTADO}/ufw" "${ESTADO}/systemd" "${ESTADO}/cert"
install -d -m 1777 "${ESTADO}/docker" "${ESTADO}/psql"
install -d -m 0755 /run/systemd/transient
install -m 0644 /opt/hostsim-cert/cert.pem "${ESTADO}/cert/cert.pem"
install -m 0600 /opt/hostsim-cert/key.pem "${ESTADO}/cert/key.pem"
: > "${ESTADO}/rastro-host";  chmod 0666 "${ESTADO}/rastro-host"
: > "${ESTADO}/journal";      chmod 0644 "${ESTADO}/journal"
: > "${ESTADO}/ufw/reglas"
echo "$puerto_ssh" > "${ESTADO}/puertos"; chmod 0666 "${ESTADO}/puertos"
