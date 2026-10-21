# =======================================================================================
# lib.sh -- piezas comunes de los shims del host simulado (se instala en
# /usr/local/lib/hostsim/lib.sh y la hacen `source` los shims).
# =======================================================================================
#
# El host simulado es un contenedor ubuntu:24.04 que hace de VPS para testear
# preparar-vps-nuevo.sh. Cada shim guarda su estado en archivos bajo HS_ESTADO y se comporta
# como la herramienta real EN LO QUE IMPORTA a preparar; el encabezado de cada shim dice qué
# herramienta y versión reproduce y qué NO simula.
#
# Versiones de referencia (Ubuntu 24.04 LTS): ufw 0.36.2, certbot 2.9.0, systemd 255,
# docker.io 24.0.7 / docker-compose-v2 2.24.6, iproute2 6.1.0, sudo 1.9.15, apt 2.7.14.
#
# Estado (todo bajo HS_ESTADO, lo escriben los shims corriendo como root):
#   puertos            un puerto por línea (o ADDR:PUERTO para loopback): lo que "escucha"
#   ufw/activo         existe == ufw activo;  ufw/reglas  una regla por línea ("22/tcp", "80")
#   instalado/<pkg>    marcador de apt-get install
#   journal            epoch<TAB>unidad<TAB>mensaje
#   rastro-host        una línea por invocación de un shim ("ufw allow 22/tcp")
#   docker/<nombre>/   un "contenedor": estado, salud, psql/*
#   http/<puerto>/...  respuestas que el operador obtiene por curl
#   sudo-pide-contrasena   si existe, `sudo -n` falla como un sudoers sin NOPASSWD
# =======================================================================================

HS_LIB=/usr/local/lib/hostsim
HS_ESTADO=/var/lib/hostsim

# Registra la invocación (nombre del shim + argumentos) en el rastro del host. Cualquier
# usuario puede escribir ahí: el rastro es de los tests, no del sistema simulado.
hs_rastro() {
    { printf '%s' "${0##*/}"; printf ' %q' "$@"; printf '\n'; } >> "${HS_ESTADO}/rastro-host" 2>/dev/null || true
}

hs_es_root() { [[ "$(id -u)" == "0" ]]; }

# Mensaje de systemd cuando un no-root intenta modificar sin polkit (sesión sin tty).
hs_sin_autenticacion() {
    echo "Failed to $1: Interactive authentication required." >&2
    echo "See system logs and 'systemctl status $2' for details." >&2
    exit 1
}

# Línea de journal: epoch<TAB>unidad<TAB>mensaje.
hs_journal() { printf '%s\t%s\t%s\n' "$(date +%s)" "$1" "$2" >> "${HS_ESTADO}/journal"; }

# ¿El paquete está "instalado" por apt-get install?
hs_instalado() { [[ -e "${HS_ESTADO}/instalado/$1" ]]; }

# ¿El usuario actual puede hablar con el demonio de Docker? root, o miembro del grupo docker
# en ESTA sesión (los grupos se leen al abrir la sesión: un `usermod -aG` solo vale para las
# sesiones nuevas, como con sshd real).
hs_en_grupo_docker() { [[ " $(id -nG) " == *" docker "* ]]; }
