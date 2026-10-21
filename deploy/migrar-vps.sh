#!/usr/bin/env bash
# =======================================================================================
# deploy/migrar-vps.sh -- migra StockApp del VPS viejo al VPS nuevo con un corte controlado.
# Corre en la MÁQUINA DEL OPERADOR y opera los dos servidores por ssh.
# =======================================================================================
#
# Uso:
#   deploy/migrar-vps.sh [--ensayo] [--env-viejo <archivo>] [--env-nuevo <archivo>]
#
#   (sin --ensayo)  El corte. Pre-vuelo de SOLO LECTURA (config, identidad de los hosts, estado del
#                   viejo, preparación del nuevo, overrides, credencial) y, recién después, una
#                   confirmación escrita: hay que teclear exactamente CONGELAR en una terminal.
#   --ensayo        La misma secuencia sin la confirmación: no congela el viejo.
#   --env-viejo     .env del operador para el VPS viejo. Default: deploy/.env.
#                   Además de los VPS_*: VPS_ADMIN_USER (obligatorio; no cae a BOOTSTRAP_ADMIN_USER)
#                   y VPS_ADMIN_PASSWORD. La contraseña sale del entorno, si no del archivo y, si
#                   no, se pide por terminal sin eco. Es un usuario real con GestionarDiagnostico.
#   --env-nuevo     .env del operador para el VPS nuevo. Default: deploy/.env.vps-nuevo.
#
# Solo para el arnés de ensayo local. Se rechazan con exit 2 si algún VPS_HOST no termina en
# .invalid, igual que la variable STOCKAPP_ENSAYO_FALLAR_EN: ningún desvío de prueba sirve contra un
# servidor real.
#   --contenedor-viejo <n>   --contenedor-nuevo <n>     (default stockapp-pg; ^stockapp[a-z0-9-]*$)
#   --raiz-remota-viejo <d>  --raiz-remota-nuevo <d>    (prefijo de las rutas absolutas remotas)
#   --sin-verificar-preparacion                         (no llama a preparar-vps-nuevo.sh --verificar)
#
# ⚠️  EL VPS VIEJO ES COMPARTIDO con otra aplicación. Sobre él, este script solo corre lecturas
# acotadas POR NOMBRE a StockApp: systemctl is-active / is-enabled / show de stockapp-api,
# docker ps --format, docker exec del contenedor de Postgres de StockApp, cat de /etc/machine-id,
# test -e de la unit y el curl de login y de GET /backups por loopback; más los bloques
# (volcado-final-vps.sh) y los cuatro comandos de vuelta atrás. Nada global de Docker, de systemd ni
# de firewall, y nada del proxy de la otra aplicación. Ningún dump ni archivo temporal pasa por el
# /tmp del host.
#
# Secretos: nunca en argv, en archivos de esta máquina ni en el log. La contraseña de verificación
# vive en una variable del proceso y viaja por el stdin de ssh (el cuerpo del login) hacia curl; el
# token, igual, solo en una variable y por el stdin de curl -K -.
#
# Pre-vuelo (paso 1) y confirmación (paso 2):
#   - Identidad (A9): /etc/machine-id de los dos hosts, leído por ssh ANTES de crear la corrida.
#     Iguales -> 9. Ilegible -> 8. Se compara además contra el último ensayo, si existe.
#   - Viejo: sudo -n, stockapp-api active con FragmentPath igual a la unit que deja install.sh (así
#     se sabe ANTES de la indisponibilidad que el congelamiento va a poder moverla), sin .congelado,
#     el contenedor de Postgres corriendo y espacio para el dump (pg_database_size contra el df del
#     contenedor; nunca el df del directorio de volúmenes de Docker, que pide root).
#   - Nuevo: preparar-vps-nuevo.sh --verificar con la versión que sirve el feed del nuevo (un bloque,
#     como proceso hijo, con el .env del nuevo), espacio para el dump y la copia de seguridad, y los
#     overrides del último ensayo presentes en el .env del servidor (solo NOMBRES).
#   - Credencial: login contra la API vieja por ssh al loopback y GET /backups (solo lectura).
#   - Una corrida sin terminar (con estado y sin resultado=) impide empezar otra de corte.
#   - Hasta la confirmación no se modifica nada. Un fallo del pre-vuelo cierra la corrida con
#     resultado=FALLO-<n>, salvo que ya hubiera congelado el viejo.
#
# Códigos de salida (estables):
#   0  GO técnico: todas las V automáticas OK. Quedan V9 y la decisión de Martin
#   2  uso / argumentos
#   3  precondición del pre-vuelo (config, identidad, credencial, corrida sin terminar, lock): no se tocó nada
#   4  el operador no confirmó: no se tocó nada
#   5  falló un paso antes de congelar el viejo, que sigue intacto y sirviendo. Re-correr arranca una corrida nueva
#   6  falló un paso después de congelar el viejo, que quedó congelado. El resumen dice el estado exacto y la salida: --continuar <corrida> o --revertir
#   7  NO-GO: la secuencia terminó, pero falló una V. La API nueva queda arriba para diagnosticar (las PCs no están instaladas: nadie escribe). Se imprime --revertir
#   8  no se pudo consultar
#   9  alcance: la lista de contenedores del viejo cambió, o los dos .env son el mismo servidor
#
# Tablas de códigos de los bloques que este script llama (copiadas sin cambios; un test de
# sincronía las compara con las de cada bloque):
# >>> CODIGOS deploy/volcado-final-vps.sh
# Códigos de salida (estables):
#   0  OK
#   2  uso / argumentos inválidos
#   3  precondición: config faltante o inválida, sin sudo -n, contenedor fuera de patrón o
#      detenido, la API sigue activa tras el stop, conexiones abiertas a la base
#   4  traída inválida: sha local ≠ remoto, tamaño < 1024 o la transferencia falló (el archivo
#      local se borra), o el historial de backups no es un tar legible
#   8  no se pudo consultar (ssh/systemctl/docker/psql con error o salida ilegible): "no pude
#      consultar" nunca es "cero" ni "vacío"; también si --ensayo no pudo rearrancar la API, o
#      si la unit no está donde la deja install.sh (o ya existe un .congelado): no se toca
#   9  alcance: la lista de contenedores al final no es la del principio
# <<< CODIGOS deploy/volcado-final-vps.sh
# >>> CODIGOS deploy/restaurar-backup.sh
# Códigos de salida (estables):
#   0  OK
#   2  uso/argumentos/manifiesto inválidos
#   3  precondición: API activa, contenedor fuera de patrón/ausente/detenido, base no vacía,
#      conexiones abiertas al DROP DATABASE de --recrear-base
#   4  dump inválido: sha256/tamaño ≠ manifiesto, pg_restore --list falla, tablas ≠ manifiesto
#   5  falló pg_restore: la base quedó VACÍA (transacción única), se puede reintentar tal cual
#   6  falló la verificación de conteos, última migración o secuencias (la base QUEDA restaurada)
#   7  duplicados en catálogos con Nombre único (la base QUEDA restaurada)
#   8  no se pudo consultar (docker/psql con error): "no pude consultar" nunca es "vacío"
#   9  falló el dump de seguridad de --recrear-base: NO se borró nada
# <<< CODIGOS deploy/restaurar-backup.sh
# =======================================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
# shellcheck source=lib/orquestacion.sh
source "$REPO_ROOT/deploy/lib/orquestacion.sh"

PASOS_TOTAL=13

# El trap EXIT de la lib borra los temporales privados del operador (_orq_tmp_limpiar): poner otro
# trap lo reemplazaría y dejaría en /tmp el stderr de ssh. Este lo ENCADENA. Además cierra una corrida
# que falló antes de tocar el viejo: sin un resultado=, una corrida con estado cuenta como "sin
# terminar" y bloquearía todos los cortes siguientes. Con congelado_utc la decisión (--continuar o
# --revertir) es de un humano y la corrida se deja abierta.
_migrar_salir() {
    local rc=$? resultado_rc=0 congelado_rc=0 archivo
    if [[ "$rc" -ne 0 && -n "${ORQ_CORRIDA:-}" ]]; then
        archivo="$ORQ_CORRIDA/estado"
        if [[ -f "$archivo" ]]; then
            orq_estado_de "$archivo" resultado >/dev/null 2>&1 || resultado_rc=$?
            orq_estado_de "$archivo" congelado_utc >/dev/null 2>&1 || congelado_rc=$?
            if [[ "$resultado_rc" -eq 1 && "$congelado_rc" -eq 1 ]]; then
                orq_estado_poner resultado "FALLO-$rc" >/dev/null 2>&1 || :
            fi
        fi
    fi
    _orq_tmp_limpiar
}
trap '_migrar_salir' EXIT

uso() {
    sed -n '/^# Uso:/,/^#   --sin-verificar-preparacion/p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2
}

# ---------------------------------------------------------------------------------------
# Salida y abortos
# ---------------------------------------------------------------------------------------

PASO_ABIERTO=0
PASO_T0=0

abrir_paso() {
    orq_paso "$1" "$PASOS_TOTAL" "$2"
    PASO_ABIERTO=1
    PASO_T0="$SECONDS"
}

# cerrar_paso <n> <OK|YA ESTABA|OMITIDO> [detalle] [marca]: línea del paso y su clave en el estado.
cerrar_paso() {
    local n="$1" resultado="$2" detalle="${3-}" marca="${4:-ok}" duracion
    duracion=$((SECONDS - PASO_T0))
    PASO_ABIERTO=0
    orq_fin_paso "$resultado" "$detalle"
    orq_estado_poner "paso_$n" "$marca $(date -u +%Y-%m-%dT%H:%M:%SZ) $duracion"
}

# abortar <codigo> <mensaje>: cierra el paso abierto como FALLÓ y sale.
abortar() {
    local codigo="$1"
    shift
    echo "migrar: $*" >&2
    if [[ "$PASO_ABIERTO" -eq 1 ]]; then
        PASO_ABIERTO=0
        orq_fin_paso FALLÓ "exit $codigo"
    fi
    exit "$codigo"
}

# volcar_stderr: lo que dijo ssh o el comando remoto, con sangría. Solo el stderr, nunca la salida:
# la de un login lleva el token.
volcar_stderr() {
    if [[ -n "$ORQ_STDERR" ]]; then
        printf '%s\n' "$ORQ_STDERR" | sed 's/^/    | /' >&2
    fi
}

# consulta_ok <qué>: tras orq_ssh, un código distinto de cero (el 255 de ssh incluido) es "no pude
# consultar", nunca "vacío" ni "cero".
consulta_ok() {
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_stderr
        abortar 8 "no pude consultar $1 (salió con $ORQ_RC). Esto NO es 'vacío' ni 'cero'."
    fi
}

contiene_linea() {
    local texto="$1" buscado="$2" linea
    while IFS= read -r linea; do
        if [[ "$linea" == "$buscado" ]]; then
            return 0
        fi
    done <<< "$texto"
    return 1
}

ahora_utc() {
    date -u +%Y-%m-%dT%H:%M:%SZ
}

# ---------------------------------------------------------------------------------------
# Argumentos
# ---------------------------------------------------------------------------------------

MODO="corte"
ENV_VIEJO="$REPO_ROOT/deploy/.env"
ENV_NUEVO="$REPO_ROOT/deploy/.env.vps-nuevo"
CONTENEDOR_VIEJO="stockapp-pg"
CONTENEDOR_NUEVO="stockapp-pg"
RAIZ_VIEJO=""
RAIZ_NUEVO=""
SIN_VERIFICAR_PREPARACION=0
ARNES_USADO=()

valor_de() {
    if [[ $# -lt 2 || -z "$2" ]]; then
        echo "migrar: $1 necesita un valor" >&2
        uso
        exit 2
    fi
}

valor_contenedor() {
    valor_de "$@"
    if [[ ! "$2" =~ ^stockapp[a-z0-9-]*$ ]]; then
        echo "migrar: $1 '$2' no matchea ^stockapp[a-z0-9-]*\$" >&2
        exit 2
    fi
}

valor_raiz() {
    valor_de "$@"
    if [[ ! "$2" =~ ^/[A-Za-z0-9._/-]+$ || "$2" == *..* ]]; then
        echo "migrar: $1 '$2' tiene que ser una ruta absoluta sin '..'" >&2
        exit 2
    fi
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --ensayo)
            MODO="ensayo"
            shift
            ;;
        --env-viejo)
            valor_de "$@"
            ENV_VIEJO="$2"
            shift 2
            ;;
        --env-nuevo)
            valor_de "$@"
            ENV_NUEVO="$2"
            shift 2
            ;;
        --contenedor-viejo)
            valor_contenedor "$@"
            CONTENEDOR_VIEJO="$2"
            ARNES_USADO+=("$1")
            shift 2
            ;;
        --contenedor-nuevo)
            valor_contenedor "$@"
            CONTENEDOR_NUEVO="$2"
            ARNES_USADO+=("$1")
            shift 2
            ;;
        --raiz-remota-viejo)
            valor_raiz "$@"
            RAIZ_VIEJO="$2"
            ARNES_USADO+=("$1")
            shift 2
            ;;
        --raiz-remota-nuevo)
            valor_raiz "$@"
            RAIZ_NUEVO="$2"
            ARNES_USADO+=("$1")
            shift 2
            ;;
        --sin-verificar-preparacion)
            SIN_VERIFICAR_PREPARACION=1
            ARNES_USADO+=("$1")
            shift
            ;;
        -h | --help)
            uso
            exit 0
            ;;
        *)
            echo "migrar: argumento desconocido '$1'" >&2
            uso
            exit 2
            ;;
    esac
done

# ---------------------------------------------------------------------------------------
# Configuración (fase A: nada de esto crea una corrida ni escribe nada)
# ---------------------------------------------------------------------------------------

# La contraseña de verificación: el entorno gana sobre el archivo. Se copia a una variable que NO
# se exporta y se saca del entorno, para que ningún proceso hijo (ssh, los bloques) la herede.
PW="${VPS_ADMIN_PASSWORD-}"
unset VPS_ADMIN_PASSWORD

CLAVES_COMUNES=(VPS_HOST VPS_USER VPS_SSH_PORT API_SCHEME API_HOSTNAME API_PORT POSTGRES_USER POSTGRES_DB)

# exigir <PREFIJO> <archivo> <CLAVE>...: cada clave tiene que traer un valor.
exigir() {
    local p="$1" archivo="$2" clave nombre
    shift 2
    for clave in "$@"; do
        nombre="${p}_${clave}"
        if [[ -z "${!nombre:-}" ]]; then
            abortar 3 "falta $clave en $archivo."
        fi
    done
}

validar_destino() {
    local p="$1" archivo="$2" nombre_host="${1}_VPS_HOST" nombre_usuario="${1}_VPS_USER" nombre_puerto="${1}_VPS_SSH_PORT"
    local nombre_esquema="${1}_API_SCHEME" nombre_apihost="${1}_API_HOSTNAME" nombre_apipuerto="${1}_API_PORT"
    local nombre_pguser="${1}_POSTGRES_USER" nombre_pgdb="${1}_POSTGRES_DB"
    if [[ ! "${!nombre_host}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
        abortar 3 "VPS_HOST en $archivo no parece un nombre ni una IP."
    fi
    if [[ ! "${!nombre_usuario}" =~ ^[a-z_][a-z0-9_-]*$ ]]; then
        abortar 3 "VPS_USER en $archivo no parece un usuario de Linux."
    fi
    if [[ ! "${!nombre_puerto}" =~ ^[0-9]+$ ]] || (("${!nombre_puerto}" < 1 || "${!nombre_puerto}" > 65535)); then
        abortar 3 "VPS_SSH_PORT en $archivo no es un puerto."
    fi
    if [[ "${!nombre_esquema}" != "http" && "${!nombre_esquema}" != "https" ]]; then
        abortar 3 "API_SCHEME en $archivo tiene que ser exactamente http o https."
    fi
    if [[ -n "${!nombre_apihost:-}" && ! "${!nombre_apihost}" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]]; then
        abortar 3 "API_HOSTNAME en $archivo no parece un nombre de host."
    fi
    if [[ ! "${!nombre_apipuerto}" =~ ^[0-9]+$ ]] || (("${!nombre_apipuerto}" < 1 || "${!nombre_apipuerto}" > 65535)); then
        abortar 3 "API_PORT en $archivo no es un puerto."
    fi
    if [[ ! "${!nombre_pguser}" =~ ^[a-z_][a-z0-9_]*$ || ! "${!nombre_pgdb}" =~ ^[a-z_][a-z0-9_]*$ ]]; then
        abortar 3 "POSTGRES_USER o POSTGRES_DB en $archivo no matchean ^[a-z_][a-z0-9_]*\$."
    fi
}

cargar_config() {
    if [[ -z "${HOME:-}" ]]; then
        abortar 3 "HOME no está definido: no sé dónde guardar la corrida."
    fi
    ENV_VIEJO="$(realpath -m -- "$ENV_VIEJO")"
    ENV_NUEVO="$(realpath -m -- "$ENV_NUEVO")"
    if [[ "$ENV_VIEJO" == "$ENV_NUEVO" ]]; then
        abortar 9 "--env-viejo y --env-nuevo son el mismo archivo ($ENV_VIEJO): es el mismo servidor."
    fi

    orq_leer_env "$ENV_VIEJO" VIEJO "${CLAVES_COMUNES[@]}" VPS_ADMIN_USER VPS_ADMIN_PASSWORD
    orq_leer_env "$ENV_NUEVO" NUEVO "${CLAVES_COMUNES[@]}"
    if [[ -z "$PW" && -n "${VIEJO_VPS_ADMIN_PASSWORD-}" ]]; then
        PW="$VIEJO_VPS_ADMIN_PASSWORD"
    fi
    unset VIEJO_VPS_ADMIN_PASSWORD

    # Defaults del viejo (los mismos que usan bajar-backup-vps.sh y volcado-final-vps.sh). El nuevo
    # no tiene defaults: sin configuración explícita no se apunta a ningún servidor.
    VIEJO_API_SCHEME="${VIEJO_API_SCHEME:-http}"
    VIEJO_API_PORT="${VIEJO_API_PORT:-8080}"
    VIEJO_POSTGRES_USER="${VIEJO_POSTGRES_USER:-stockapp}"
    VIEJO_POSTGRES_DB="${VIEJO_POSTGRES_DB:-stockapp}"
    NUEVO_POSTGRES_USER="${NUEVO_POSTGRES_USER:-stockapp}"
    NUEVO_POSTGRES_DB="${NUEVO_POSTGRES_DB:-stockapp}"

    exigir VIEJO "$ENV_VIEJO" VPS_HOST VPS_USER VPS_SSH_PORT VPS_ADMIN_USER
    exigir NUEVO "$ENV_NUEVO" VPS_HOST VPS_USER VPS_SSH_PORT API_SCHEME API_HOSTNAME API_PORT
    if [[ "$VIEJO_API_SCHEME" == "https" ]]; then
        exigir VIEJO "$ENV_VIEJO" API_HOSTNAME
    fi

    # Los parámetros de arnés y la inyección de fallos solo existen para el ensayo local: si algún
    # host no es .invalid se rechazan. Y solo con TODOS los hosts .invalid la palabra de
    # confirmación puede leerse de stdin (ORQ_ENSAYO, que solo se pone acá, nunca el entorno).
    if [[ -n "${STOCKAPP_ENSAYO_FALLAR_EN-}" ]]; then
        ARNES_USADO+=("STOCKAPP_ENSAYO_FALLAR_EN")
    fi
    if orq_hosts_de_ensayo "$VIEJO_VPS_HOST" "$NUEVO_VPS_HOST"; then
        ORQ_ENSAYO=1
    elif [[ "${#ARNES_USADO[@]}" -gt 0 ]]; then
        abortar 2 "${ARNES_USADO[*]} solo existen para el ensayo local (hosts .invalid) y acá algún VPS_HOST es un servidor real."
    fi

    validar_destino VIEJO "$ENV_VIEJO"
    validar_destino NUEVO "$ENV_NUEVO"
    if [[ "$ORQ_ENSAYO" -ne 1 && "$NUEVO_API_SCHEME" != "https" ]]; then
        abortar 3 "API_SCHEME en $ENV_NUEVO tiene que ser https: el VPS nuevo sirve con TLS y la verificación V3 lo comprueba."
    fi

    # A9: dos .env que apuntan al mismo servidor. La identidad real (machine-id) se comprueba después.
    if [[ "${VIEJO_VPS_HOST,,}" == "${NUEVO_VPS_HOST,,}" ]]; then
        abortar 9 "los dos .env apuntan al mismo VPS_HOST ($VIEJO_VPS_HOST): migrar nunca corre con los dos .env en el mismo servidor."
    fi

    # URL de la API vieja para el login y GET /backups, desde el propio servidor (loopback). Con
    # https, --resolve fuerza el loopback validando el certificado real (nunca -k).
    if [[ "$VIEJO_API_SCHEME" == "https" ]]; then
        URL_VIEJO="https://${VIEJO_API_HOSTNAME}:${VIEJO_API_PORT}"
        RESOLVE_VIEJO="--resolve ${VIEJO_API_HOSTNAME}:${VIEJO_API_PORT}:127.0.0.1 "
    else
        URL_VIEJO="http://127.0.0.1:${VIEJO_API_PORT}"
        RESOLVE_VIEJO=""
    fi
}

exigir_terminal() {
    # A12: el corte nunca congela sin una confirmación escrita por un humano en una terminal.
    if [[ "$MODO" == "corte" && "$ORQ_ENSAYO" -ne 1 ]] && ! { : </dev/tty; } 2>/dev/null; then
        abortar 3 "no hay una terminal para confirmar: un corte no se lanza desatendido. No se tocó nada."
    fi
}

# ---------------------------------------------------------------------------------------
# Identidad de los hosts (fase A: lecturas por ssh, antes de crear la corrida)
# ---------------------------------------------------------------------------------------

MACHINE_ID_VIEJO=""
MACHINE_ID_NUEVO=""

# leer_machine_id <VIEJO|NUEVO> <raiz-remota> <variable-destino>
leer_machine_id() {
    local p="$1" raiz="$2" destino="$3" nombre_host="${1}_VPS_HOST"
    orq_ssh "$p" "cat ${raiz}/etc/machine-id"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_stderr
        abortar 8 "no pude leer /etc/machine-id de ${!nombre_host} (salió con $ORQ_RC)."
    fi
    if [[ ! "$ORQ_SALIDA" =~ ^[0-9a-f]{32}$ ]]; then
        abortar 8 "el machine-id de ${!nombre_host} no tiene el formato esperado (32 hexadecimales): no puedo interpretarlo."
    fi
    printf -v "$destino" '%s' "$ORQ_SALIDA"
}

# ensayos_previos: las corridas de ensayo anteriores, la más vieja primero.
ensayos_previos() {
    local base="$HOME/stockapp-backups-vps/corridas" dir
    if [[ ! -d "$base" ]]; then
        return 0
    fi
    for dir in "$base"/ensayo-*/; do
        if [[ -d "$dir" ]]; then
            printf '%s\n' "${dir%/}"
        fi
    done
}

# ensayo_previo_con <clave>... -> ENSAYO_PREVIO = el último ensayo cuyo estado tiene todas las claves.
ENSAYO_PREVIO=""
ensayo_previo_con() {
    local dir clave rc falta
    ENSAYO_PREVIO=""
    while IFS= read -r dir; do
        [[ -n "$dir" && -f "$dir/estado" ]] || continue
        falta=0
        for clave in "$@"; do
            rc=0
            orq_estado_de "$dir/estado" "$clave" >/dev/null 2>&1 || rc=$?
            if [[ "$rc" -ne 0 ]]; then
                falta=1
            fi
        done
        if [[ "$falta" -eq 0 ]]; then
            ENSAYO_PREVIO="$dir"
        fi
    done < <(ensayos_previos)
}

verificar_identidad() {
    local previo_viejo previo_nuevo
    leer_machine_id VIEJO "$RAIZ_VIEJO" MACHINE_ID_VIEJO
    leer_machine_id NUEVO "$RAIZ_NUEVO" MACHINE_ID_NUEVO
    # A9: los dos hosts tienen que ser servidores distintos; el nombre del .env no prueba nada.
    if [[ "$MACHINE_ID_VIEJO" == "$MACHINE_ID_NUEVO" ]]; then
        abortar 9 "los dos hosts tienen el mismo /etc/machine-id: son el mismo servidor. No se tocó nada."
    fi
    # Un corte contra otros servidores que los del último ensayo no es el que se ensayó.
    if [[ "$MODO" == "corte" ]]; then
        ensayo_previo_con machine_id_viejo machine_id_nuevo
        if [[ -n "$ENSAYO_PREVIO" ]]; then
            previo_viejo="$(orq_estado_de "$ENSAYO_PREVIO/estado" machine_id_viejo)"
            previo_nuevo="$(orq_estado_de "$ENSAYO_PREVIO/estado" machine_id_nuevo)"
            if [[ "$previo_viejo" != "$MACHINE_ID_VIEJO" || "$previo_nuevo" != "$MACHINE_ID_NUEVO" ]]; then
                abortar 3 "el último ensayo ($ENSAYO_PREVIO) se hizo contra otros servidores que los de estos .env: repetí el ensayo contra este par antes del corte."
            fi
        fi
    fi
}

# ---------------------------------------------------------------------------------------
# Corridas sin terminar (spec 6.3.3)
# ---------------------------------------------------------------------------------------

# Una corrida cuenta como sin terminar solo si tiene archivo de estado Y no tiene resultado=: quien
# pierde el lock deja una corrida sin estado, y contarla bloquearía todos los cortes. Las de
# --revertir no cuentan: revertir es idempotente y se re-corre.
verificar_corridas_sin_terminar() {
    local base="$HOME/stockapp-backups-vps/corridas" dir rc sin_terminar=()
    for dir in "$base"/*/; do
        dir="${dir%/}"
        [[ -d "$dir" && -f "$dir/estado" ]] || continue
        case "${dir##*/}" in
            revertir-*) continue ;;
        esac
        rc=0
        orq_estado_de "$dir/estado" resultado >/dev/null 2>&1 || rc=$?
        # 0 = terminada. 1 = sin resultado. 2 = no se pudo leer: tampoco se puede decir que terminó.
        if [[ "$rc" -ne 0 ]]; then
            sin_terminar+=("$dir")
        fi
    done
    if [[ "${#sin_terminar[@]}" -gt 0 ]]; then
        {
            echo "migrar: hay corridas sin terminar; no se empieza otra:"
            for dir in "${sin_terminar[@]}"; do
                echo "  $dir"
                echo "    deploy/migrar-vps.sh --continuar $dir"
            done
            echo "  o deshacer el congelamiento, si lo hubo:"
            echo "    deploy/migrar-vps.sh --revertir"
        } >&2
        abortar 3 "una corrida sin terminar tiene que retomarse (--continuar) o revertirse (--revertir) antes de empezar otra. No se tocó nada."
    fi
}

# ---------------------------------------------------------------------------------------
# Pre-vuelo (solo lectura)
# ---------------------------------------------------------------------------------------

VERSION_NUEVO=""
TAMANO_BASE_VIEJO=0
TAMANO_BASE_NUEVO=0
TOKEN=""
CODIGO_HTTP=""
CURL_RC=0
CURL_CUERPO=""
CURL_ERR=""

# curl_operador <url>: GET desde esta máquina validando el certificado de verdad (nunca se lo desactiva). Deja
# CURL_RC, CURL_CUERPO y CURL_ERR; nunca sale. El stderr va a la corrida (700).
curl_operador() {
    local err
    CURL_RC=0
    CURL_CUERPO=""
    CURL_ERR=""
    if ! orq_tmp_dir || ! err="$(mktemp "$ORQ_TMP_DIR/curl-stderr.XXXXXX")"; then
        CURL_RC=255
        CURL_ERR="no pude crear el temporal para el stderr de curl"
        return 0
    fi
    CURL_CUERPO="$(curl -fsS --connect-timeout 10 --max-time 30 "$1" 9>&- 2>"$err" </dev/null)" || CURL_RC=$?
    CURL_ERR="$(cat "$err" 2>/dev/null)" || CURL_ERR=""
    rm -f -- "$err"
}

# codigo_http: el código HTTP que curl -f deja en su stderr ("returned error: 401"), o vacío.
codigo_http() {
    CODIGO_HTTP=""
    if [[ "$ORQ_STDERR" =~ returned\ error:\ ([0-9]{3}) ]]; then
        CODIGO_HTTP="${BASH_REMATCH[1]}"
    fi
}

# tamano_base <PREFIJO> <contenedor> <docker> <usuario> <base> -> TAMANO_BASE (bytes)
TAMANO_BASE=0
tamano_base() {
    local p="$1" contenedor="$2" docker="$3" usuario="$4" base="$5"
    orq_ssh "$p" "${docker}exec -i ${contenedor} psql -U ${usuario} -d ${base} -tA -v ON_ERROR_STOP=1" \
        "SELECT pg_database_size(current_database());"
    consulta_ok "el tamaño de la base ($p)"
    if [[ ! "$ORQ_SALIDA" =~ ^[0-9]{1,18}$ ]]; then
        abortar 8 "no pude interpretar el tamaño de la base ($p): la respuesta no es un número."
    fi
    TAMANO_BASE="$ORQ_SALIDA"
}

# espacio_libre <PREFIJO> <comando-df> -> LIBRE_BYTES (columna Available de df -P, en bloques de 1 KiB)
LIBRE_BYTES=0
espacio_libre() {
    local p="$1" comando="$2" linea libre rc=0
    orq_ssh "$p" "$comando"
    consulta_ok "el espacio en disco ($p)"
    linea="$(printf '%s\n' "$ORQ_SALIDA" | sed -n '2p')"
    read -r _ _ _ libre _ <<< "$linea" || rc=$?
    if [[ ! "${libre:-}" =~ ^[0-9]{1,15}$ ]]; then
        abortar 8 "no pude interpretar el df del $p: ${linea:-(vacío)}"
    fi
    LIBRE_BYTES=$((libre * 1024))
}

prevuelo_viejo() {
    local unit="${RAIZ_VIEJO}/etc/systemd/system/stockapp-api.service"

    orq_ssh VIEJO "sudo -n true"
    if [[ "$ORQ_RC" -eq 255 ]]; then
        consulta_ok "el acceso por ssh al VPS viejo"
    elif [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_stderr
        abortar 3 "'sudo -n true' falló en el VPS viejo: $VIEJO_VPS_USER necesita sudo sin contraseña."
    fi

    orq_ssh VIEJO "systemctl is-active stockapp-api"
    case "$ORQ_RC" in
        0 | 3) ;;
        *) consulta_ok "el estado de stockapp-api en el VPS viejo" ;;
    esac
    case "$ORQ_SALIDA" in
        active)
            if [[ "$ORQ_RC" -ne 0 ]]; then
                abortar 8 "stockapp-api dice active pero el código de systemctl no coincide: no puedo interpretarlo."
            fi
            ;;
        inactive | failed | activating | deactivating | reloading)
            abortar 3 "stockapp-api en el VPS viejo no está active (dice '$ORQ_SALIDA'): no hay nada que migrar sin una API sirviendo."
            ;;
        *) abortar 8 "no pude interpretar 'systemctl is-active stockapp-api' del VPS viejo." ;;
    esac

    orq_ssh VIEJO "systemctl show -p FragmentPath stockapp-api"
    consulta_ok "la ruta de la unit de stockapp-api en el VPS viejo"
    if [[ ! "$ORQ_SALIDA" =~ ^FragmentPath=.*$ ]]; then
        abortar 8 "no pude interpretar 'systemctl show -p FragmentPath': ${ORQ_SALIDA:-(vacío)}"
    fi
    if [[ "$ORQ_SALIDA" != "FragmentPath=$unit" ]]; then
        abortar 3 "la unit stockapp-api del VPS viejo no es '$unit' (FragmentPath dice '${ORQ_SALIDA#FragmentPath=}'): el congelamiento no podría moverla. Se sabe ahora, antes de la indisponibilidad."
    fi

    orq_ssh VIEJO "test -e ${unit}.congelado"
    if [[ "$ORQ_RC" -eq 0 ]]; then
        abortar 3 "ya existe ${unit}.congelado en el VPS viejo: un congelamiento anterior sin deshacer. Revisalo (--revertir) antes de empezar."
    elif [[ "$ORQ_RC" -ne 1 || -n "$ORQ_STDERR" ]]; then
        consulta_ok "si existe ${unit}.congelado"
        abortar 8 "no pude consultar si existe ${unit}.congelado."
    fi

    orq_ssh VIEJO "docker ps --format '{{.Names}}'"
    consulta_ok "la lista de contenedores del VPS viejo"
    if [[ -z "$ORQ_SALIDA" ]]; then
        abortar 8 "la lista de contenedores del VPS viejo vino vacía: no puedo interpretarla."
    fi
    if ! contiene_linea "$ORQ_SALIDA" "$CONTENEDOR_VIEJO"; then
        abortar 3 "el contenedor $CONTENEDOR_VIEJO no está corriendo en el VPS viejo."
    fi

    tamano_base VIEJO "$CONTENEDOR_VIEJO" "docker " "$VIEJO_POSTGRES_USER" "$VIEJO_POSTGRES_DB"
    TAMANO_BASE_VIEJO="$TAMANO_BASE"
    espacio_libre VIEJO "docker exec ${CONTENEDOR_VIEJO} df -P /var/lib/postgresql/data"
    if [[ "$LIBRE_BYTES" -lt "$TAMANO_BASE_VIEJO" ]]; then
        abortar 3 "no alcanza el espacio en disco del contenedor del VPS viejo: la base pesa $TAMANO_BASE_VIEJO bytes y quedan $LIBRE_BYTES para el dump."
    fi

    echo "  - viejo: stockapp-api active, unit en su lugar, sin .congelado, $CONTENEDOR_VIEJO corriendo, base de $TAMANO_BASE_VIEJO bytes con espacio para el dump"
}

# leer_version_del_feed: la mayor versión Full que sirve el feed del VPS nuevo -> VERSION_NUEVO.
leer_version_del_feed() {
    local url="${NUEVO_API_SCHEME}://${NUEVO_API_HOSTNAME}:${NUEVO_API_PORT}/updates/releases.win.json" mayor rc=0
    curl_operador "$url"
    case "$CURL_RC" in
        0) ;;
        22 | 7)
            if [[ "$SIN_VERIFICAR_PREPARACION" -eq 1 ]]; then
                VERSION_NUEVO="desconocida"
                return 0
            fi
            abortar 3 "el VPS nuevo no sirve el feed de actualizaciones ($url): preparar-vps-nuevo.sh no terminó."
            ;;
        *)
            if [[ "$SIN_VERIFICAR_PREPARACION" -eq 1 ]]; then
                VERSION_NUEVO="desconocida"
                return 0
            fi
            abortar 8 "no pude consultar el feed del VPS nuevo (curl salió con $CURL_RC). ${CURL_ERR}"
            ;;
    esac
    mayor="$(printf '%s' "$CURL_CUERPO" | jq -r '.Assets[] | select(.Type == "Full") | .Version' | sort -V | tail -n 1)" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        abortar 8 "el feed que sirve el VPS nuevo no es un releases.win.json válido."
    fi
    if [[ -z "$mayor" ]]; then
        abortar 3 "el feed del VPS nuevo no tiene ninguna versión Full: preparar-vps-nuevo.sh no terminó."
    fi
    if [[ ! "$mayor" =~ ^[0-9]+(\.[0-9]+){1,3}([-+][0-9A-Za-z.-]+)?$ ]]; then
        abortar 8 "la versión que sirve el feed del VPS nuevo ('$mayor') no tiene forma de versión."
    fi
    VERSION_NUEVO="$mayor"
}

prevuelo_nuevo() {
    leer_version_del_feed

    if [[ "$SIN_VERIFICAR_PREPARACION" -eq 1 ]]; then
        echo "  - preparación del nuevo: OMITIDO (arnés)"
    else
        echo "  - preparación del nuevo (preparar-vps-nuevo.sh --verificar, versión $VERSION_NUEVO):"
        orq_bloque deploy/preparar-vps-nuevo.sh "$ENV_NUEVO" --version "$VERSION_NUEVO" --env-viejo "$ENV_VIEJO" --verificar
        echo "  bloque preparar-vps-nuevo.sh salió con $ORQ_RC_BLOQUE"
        case "$ORQ_RC_BLOQUE" in
            0) ;;
            8) abortar 8 "preparar-vps-nuevo.sh --verificar no pudo consultar el VPS nuevo." ;;
            9) abortar 9 "preparar-vps-nuevo.sh --verificar dice que el destino no es un VPS dedicado (alcance)." ;;
            *) abortar 3 "el VPS nuevo no está listo (preparar-vps-nuevo.sh --verificar salió con $ORQ_RC_BLOQUE): correr preparar-vps-nuevo.sh antes del corte." ;;
        esac
    fi

    tamano_base NUEVO "$CONTENEDOR_NUEVO" "sudo -n docker " "$NUEVO_POSTGRES_USER" "$NUEVO_POSTGRES_DB"
    TAMANO_BASE_NUEVO="$TAMANO_BASE"
    # El volumen de Postgres recibe la base restaurada; el directorio de la migración recibe el dump,
    # el historial y la copia de seguridad de la base que se reemplaza.
    espacio_libre NUEVO "sudo -n docker exec ${CONTENEDOR_NUEVO} df -P /var/lib/postgresql/data"
    if [[ "$LIBRE_BYTES" -lt "$TAMANO_BASE_VIEJO" ]]; then
        abortar 3 "no alcanza el espacio en disco del volumen de Postgres del VPS nuevo: la base vieja pesa $TAMANO_BASE_VIEJO bytes y quedan $LIBRE_BYTES."
    fi
    espacio_libre NUEVO 'df -P "$HOME/stockapp-deploy/migracion"'
    if [[ "$LIBRE_BYTES" -lt $((TAMANO_BASE_VIEJO + TAMANO_BASE_NUEVO)) ]]; then
        abortar 3 "no alcanza el espacio en disco del VPS nuevo para el dump y la copia de seguridad: hacen falta $((TAMANO_BASE_VIEJO + TAMANO_BASE_NUEVO)) bytes y quedan $LIBRE_BYTES."
    fi
    echo "  - nuevo: feed en la versión $VERSION_NUEVO, base de $TAMANO_BASE_NUEVO bytes, espacio para el dump y la copia de seguridad"
}

prevuelo_overrides() {
    local archivo="" dir nombre faltan="" cantidad=0 comando
    while IFS= read -r dir; do
        if [[ -n "$dir" && -f "$dir/volcado/overrides-api-env.txt" ]]; then
            archivo="$dir/volcado/overrides-api-env.txt"
        fi
    done < <(ensayos_previos)
    if [[ -z "$archivo" ]]; then
        echo "  - overrides: no hay un ensayo anterior con lista de overrides; el primer ensayo la deja al final."
        return 0
    fi
    # Solo los NOMBRES de las claves del .env del servidor cruzan la red.
    comando='sed -n '\''s/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p'\'' "$HOME/stockapp-deploy/.env"'
    orq_ssh NUEVO "$comando"
    consulta_ok "las claves del .env del VPS nuevo"
    while IFS= read -r nombre || [[ -n "$nombre" ]]; do
        [[ -n "$nombre" ]] || continue
        if [[ ! "$nombre" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then
            abortar 3 "$archivo tiene una línea que no es un nombre de variable."
        fi
        cantidad=$((cantidad + 1))
        if ! contiene_linea "$ORQ_SALIDA" "$nombre"; then
            faltan+="${faltan:+, }$nombre"
        fi
    done < "$archivo"
    if [[ -n "$faltan" ]]; then
        abortar 3 "faltan en el .env del servidor nuevo (~/stockapp-deploy/.env) los overrides del VPS viejo: $faltan. Cargalos a mano (solo se muestran los nombres)."
    fi
    echo "  - overrides: los $cantidad del último ensayo están en el .env del servidor nuevo"
}

# obtener_contrasena: la de VPS_ADMIN_PASSWORD (entorno o .env del viejo) o, si no, se pide: por
# terminal sin eco o, en el arnés, de stdin ANTES de la palabra de confirmación.
obtener_contrasena() {
    local rc=0
    if [[ -n "$PW" ]]; then
        return 0
    fi
    if [[ "$ORQ_ENSAYO" -eq 1 ]]; then
        IFS= read -r PW || rc=$?
    elif { : </dev/tty; } 2>/dev/null; then
        printf 'Contraseña de %s en el VPS viejo (no se muestra): ' "$VIEJO_VPS_ADMIN_USER" >/dev/tty
        IFS= read -rs PW </dev/tty || rc=$?
        printf '\n' >/dev/tty
    else
        abortar 3 "falta VPS_ADMIN_PASSWORD (entorno o $ENV_VIEJO) y no hay una terminal para pedirla."
    fi
    if [[ -z "$PW" ]]; then
        abortar 3 "falta VPS_ADMIN_PASSWORD (entorno, $ENV_VIEJO o terminal): no puedo validar la credencial."
    fi
}

# prevuelo_credencial: login contra la API VIEJA por ssh al loopback (la contraseña va en el cuerpo,
# por stdin, nunca en el comando) y GET /backups (solo lectura). El login no escribe en la base.
# Mismo patrón que bajar-backup-vps.sh: curl -f sale 22 ante un 4xx/5xx y el código HTTP viene en su
# stderr; solo 401/403 son una credencial rechazada, cualquier otra cosa es "no pude consultar".
prevuelo_credencial() {
    local cuerpo rc=0 listado cantidad
    obtener_contrasena

    cuerpo="$(PW="$PW" jq -nc --arg u "$VIEJO_VPS_ADMIN_USER" '{nombreUsuario: $u, contrasena: env.PW}')" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        abortar 8 "no pude armar el cuerpo del login."
    fi
    orq_ssh VIEJO "curl -fsS ${RESOLVE_VIEJO}-X POST ${URL_VIEJO}/auth/login -H 'Content-Type: application/json' --data @-" "$cuerpo"
    cuerpo=""
    case "$ORQ_RC" in
        0) ;;
        22)
            codigo_http
            volcar_stderr
            if [[ "$CODIGO_HTTP" == "401" || "$CODIGO_HTTP" == "403" ]]; then
                abortar 3 "la API del VPS viejo rechazó el login de '$VIEJO_VPS_ADMIN_USER' (HTTP $CODIGO_HTTP). Revisá VPS_ADMIN_USER y VPS_ADMIN_PASSWORD. No se tocó nada."
            fi
            abortar 8 "el login falló con ${CODIGO_HTTP:-un código HTTP que no pude leer}: no es un problema de credenciales."
            ;;
        *)
            volcar_stderr
            abortar 8 "no pude hacer el login en la API del VPS viejo (salió con $ORQ_RC)."
            ;;
    esac
    rc=0
    TOKEN="$(printf '%s' "$ORQ_SALIDA" | jq -r '.token // empty')" || rc=$?
    ORQ_SALIDA=""
    if [[ "$rc" -ne 0 || -z "$TOKEN" ]]; then
        abortar 8 "la respuesta del login no trae un token legible."
    fi

    # El token viaja solo por el stdin de curl -K -, nunca en el comando.
    listado="$(printf 'header = "Authorization: Bearer %s"' "$TOKEN")"
    TOKEN=""
    orq_ssh VIEJO "curl -fsS ${RESOLVE_VIEJO}${URL_VIEJO}/backups -K -" "$listado"
    listado=""
    case "$ORQ_RC" in
        0) ;;
        22)
            codigo_http
            volcar_stderr
            if [[ "$CODIGO_HTTP" == "403" ]]; then
                abortar 3 "'$VIEJO_VPS_ADMIN_USER' no tiene permiso para GET /backups (GestionarDiagnostico): hace falta un usuario que lo tenga para verificar el corte. No se tocó nada."
            fi
            abortar 8 "GET /backups falló con ${CODIGO_HTTP:-un código HTTP que no pude leer}."
            ;;
        *)
            volcar_stderr
            abortar 8 "no pude consultar GET /backups en la API del VPS viejo (salió con $ORQ_RC)."
            ;;
    esac
    rc=0
    cantidad="$(printf '%s' "$ORQ_SALIDA" | jq -e 'if type == "array" then length else error("no es una lista") end')" || rc=$?
    ORQ_SALIDA=""
    if [[ "$rc" -ne 0 || ! "$cantidad" =~ ^[0-9]+$ ]]; then
        abortar 8 "GET /backups no devolvió una lista JSON legible."
    fi
    echo "  - credencial: login de '$VIEJO_VPS_ADMIN_USER' y GET /backups OK ($cantidad corridas)"
}

prevuelo() {
    prevuelo_viejo
    prevuelo_nuevo
    prevuelo_overrides
    prevuelo_credencial
}

# ---------------------------------------------------------------------------------------
# Confirmación (A12)
# ---------------------------------------------------------------------------------------

# indisponibilidad_del_ultimo_ensayo: "N min M s" del último ensayo que registró el reloj, o por qué no hay dato.
indisponibilidad_del_ultimo_ensayo() {
    local desde hasta a b segundos
    ensayo_previo_con congelado_utc api_nueva_activa_utc
    if [[ -z "$ENSAYO_PREVIO" ]]; then
        printf 'sin ensayo previo con el reloj de indisponibilidad'
        return 0
    fi
    desde="$(orq_estado_de "$ENSAYO_PREVIO/estado" congelado_utc)"
    hasta="$(orq_estado_de "$ENSAYO_PREVIO/estado" api_nueva_activa_utc)"
    if ! a="$(date -u -d "$desde" +%s 2>/dev/null)" || ! b="$(date -u -d "$hasta" +%s 2>/dev/null)" || [[ "$b" -lt "$a" ]]; then
        printf 'sin dato legible en %s' "$ENSAYO_PREVIO"
        return 0
    fi
    segundos=$((b - a))
    printf '%d min %d s (%s)' $((segundos / 60)) $((segundos % 60)) "${ENSAYO_PREVIO##*/}"
}

# comandos_de_vuelta_atras: los cuatro comandos de R-A, en el VPS viejo, tal como los dejan los bloques.
comandos_de_vuelta_atras() {
    cat <<EOF
    sudo systemctl unmask stockapp-api
    sudo mv ${RAIZ_VIEJO}/etc/systemd/system/stockapp-api.service.congelado ${RAIZ_VIEJO}/etc/systemd/system/stockapp-api.service
    sudo systemctl daemon-reload
    sudo systemctl start stockapp-api
EOF
}

confirmacion() {
    local texto
    texto="$(
        cat <<EOF

== Confirmación ==
Vas a CONGELAR el VPS viejo y llevar sus datos al VPS nuevo.
  viejo: ${VIEJO_VPS_HOST} (machine-id ${MACHINE_ID_VIEJO:0:8}): stockapp-api se detiene y su unit se enmascara.
  nuevo: ${NUEVO_VPS_HOST} (machine-id ${MACHINE_ID_NUEVO:0:8}): versión instalada ${VERSION_NUEVO}; su base se reemplaza (con copia de seguridad).
Desde el congelamiento hasta que la API nueva responda, nadie puede usar el sistema.
  Indisponibilidad del último ensayo: $(indisponibilidad_del_ultimo_ensayo)
Para volver atrás en el VPS viejo (R-A), estos son los cuatro comandos; el mismo efecto tiene deploy/migrar-vps.sh --revertir:
$(comandos_de_vuelta_atras)
Para continuar escribí exactamente CONGELAR y Enter. Cualquier otra cosa cancela sin tocar nada:
EOF
    )"
    orq_confirmar CONGELAR "$texto"
}

# ---------------------------------------------------------------------------------------
# Principal
# ---------------------------------------------------------------------------------------

main() {
    cargar_config
    exigir_terminal
    verificar_identidad

    # Fase B: la corrida, su log y el lock, en este orden (el log antes del lock: el tee no debe
    # heredar el fd del lock). orq_dir_corrida se llama SIN $(): define ORQ_CORRIDA y puede salir.
    orq_dir_corrida "$MODO" >/dev/null
    orq_log_iniciar
    orq_lock

    echo "migrar-vps: $MODO"
    echo "  corrida: $ORQ_CORRIDA"
    echo "  viejo: $VIEJO_VPS_HOST (machine-id ${MACHINE_ID_VIEJO:0:8})"
    echo "  nuevo: $NUEVO_VPS_HOST (machine-id ${MACHINE_ID_NUEVO:0:8})"

    if [[ "$MODO" == "corte" ]]; then
        verificar_corridas_sin_terminar
    fi
    orq_estado_poner modo "$MODO"
    orq_estado_poner inicio_utc "$(ahora_utc)"
    orq_estado_poner env_viejo "$ENV_VIEJO"
    orq_estado_poner env_nuevo "$ENV_NUEVO"
    orq_estado_poner machine_id_viejo "$MACHINE_ID_VIEJO"
    orq_estado_poner machine_id_nuevo "$MACHINE_ID_NUEVO"

    abrir_paso 1 "pre-vuelo"
    prevuelo
    cerrar_paso 1 OK

    PASO_T0="$SECONDS"
    if [[ "$MODO" == "corte" ]]; then
        orq_paso 2 "$PASOS_TOTAL" "confirmación"
        confirmacion
        cerrar_paso 2 OK
    else
        orq_paso 2 "$PASOS_TOTAL" "confirmación"
        cerrar_paso 2 OMITIDO "ensayo: sin confirmación" omitido
    fi

    echo "migrar: el pre-vuelo y la confirmación terminaron bien, pero la secuencia del corte todavía no está en esta versión del script. No se tocó nada." >&2
    exit 1
}

main
