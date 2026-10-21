# =======================================================================================
# lib-red.sh -- la "red" entre la máquina del operador y el host simulado, para el ssh, el
# scp y el curl falsos (se hace `source` desde ellos; vive en DirOperador junto a hostsim.env).
# =======================================================================================
# hostsim.env (lo escribe HostSimulado.Crear) define: HS_CONTENEDOR, HS_HOST, HS_IP,
# HS_PUERTO_SSH, HS_USUARIO, HS_RASTRO (el archivo del rastro del operador).
#
# Modelo del ControlMaster: el ssh real, con ControlMaster auto en el ~/.ssh/config del
# operador, reutiliza la conexión maestra ya establecida y NO abre una conexión TCP nueva; el
# firewall no la ve (conntrack deja pasar las ya establecidas). El falso supone ese peor caso
# (config del operador con ControlMaster auto + ControlPersist) y SOLO lo desactivan
# `-o ControlMaster=no` o `-o ControlPath=none`, que son los que un script prudente pasa.
# Ver hs_conectar para cuándo queda establecido el maestro.
# =======================================================================================

HS_AQUI="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
source "${HS_AQUI}/hostsim.env"

# Devuelve 0 si algún -o recibido desactiva el maestro.
hs_maestro_desactivado() {
    local o
    for o in "$@"; do
        o="$(printf '%s' "$o" | tr 'A-Z' 'a-z' | tr -s ' =' '=' )"
        case "$o" in
            controlmaster=no|controlpath=none) return 0 ;;
        esac
    done
    return 1
}

# hs_red PUERTO -> exit de red-permite (0, 1 o 2)
hs_red() {
    local rc=0
    docker exec "$HS_CONTENEDOR" /usr/local/lib/hostsim/red-permite "$1" >/dev/null 2>&1 || rc=$?
    return "$rc"
}

# hs_conectar PUERTO DESACTIVADO(0|1) USUARIO -> devuelve 255 (e imprime el error del cliente)
# si la conexión no se establece; 0 si sí.
#   1. con maestro activo para (usuario, puerto) y sin desactivar: viaja por él, sin red ni auth;
#   2. si no: conexión TCP nueva (red-permite) y autenticación (solo entra HS_USUARIO);
#   3. si no se desactivó el maestro, la conexión establecida queda de maestro para (usuario,
#      puerto) -- archivo `maestro-USUARIO-PUERTO` en DirOperador.
hs_conectar() {
    local puerto="$1" desactivado="$2" usuario="$3" rc=0
    local maestro="${HS_AQUI}/maestro-${usuario}-${puerto}"
    if [[ "$desactivado" == 0 && -e "$maestro" ]]; then
        return 0
    fi
    hs_red "$puerto" || rc=$?
    case "$rc" in
        0) ;;
        1)
            echo "ssh: connect to host ${HS_HOST} port ${puerto}: Connection timed out" >&2
            return 255
            ;;
        *)
            echo "ssh: connect to host ${HS_HOST} port ${puerto}: Connection refused" >&2
            return 255
            ;;
    esac
    if [[ "$usuario" != "$HS_USUARIO" ]]; then
        echo "${usuario}@${HS_HOST}: Permission denied (publickey)." >&2
        return 255
    fi
    [[ "$desactivado" == 0 ]] && touch "$maestro"
    return 0
}

# hs_sesion USUARIO -- comando... : corre el comando en el host como en una sesión de sshd
# nueva (PATH de Ubuntu, HOME y cwd del usuario, grupos leídos de /etc/group en este momento).
hs_sesion() {
    local usuario="$1"
    shift
    docker exec -i -u "$usuario" -w "/home/${usuario}" \
        -e "HOME=/home/${usuario}" -e "USER=${usuario}" -e "LOGNAME=${usuario}" \
        -e "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/games:/usr/local/games:/snap/bin" \
        "$HS_CONTENEDOR" "$@"
}
