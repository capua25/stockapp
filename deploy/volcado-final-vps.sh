#!/usr/bin/env bash
# =======================================================================================
# deploy/volcado-final-vps.sh -- en el corte de la migración: congela la API del VPS viejo,
# vuelca la base DENTRO del contenedor, arma el manifiesto y trae todo verificado.
# =======================================================================================
#
# Corre en la MÁQUINA DEL OPERADOR y opera el VPS viejo por ssh (mismo criterio que
# deploy/deploy-vps.sh). El .env del operador sale de STOCKAPP_ENV_LOCAL (default deploy/.env):
# con dos VPS en juego, ese archivo es lo que decide a cuál se apunta.
#
# Uso:
#   deploy/volcado-final-vps.sh --salida <dir> [--ensayo] [--contenedor <nombre>]
#                               [--raiz-remota <dir>]
#
#   --salida          directorio LOCAL (se crea y se deja en 700). Quedan, todos en 600:
#                     volcado-<ts>.dump, volcado-<ts>.manifiesto, backups-historial.tar y
#                     overrides-api-env.txt (solo NOMBRES de variables), con <ts> en UTC.
#   --ensayo          no enmascara la API, y al salir (pase lo que pase) la vuelve a arrancar
#                     SOLO si estaba activa al empezar: el ensayo deja el VPS como lo encontró.
#   --contenedor      default stockapp-pg; tiene que matchear ^stockapp[a-z0-9-]*$.
#   --raiz-remota     default vacío; prefijo de las rutas absolutas remotas (el historial de
#                     backups y el api.env). Si se da: ^/[A-Za-z0-9._/-]+$ y sin "..".
#
# --contenedor y --raiz-remota existen SOLO para el ensayo local (VPS viejo simulado en la
# máquina del operador, donde stockapp-pg es el contenedor de desarrollo y corren contenedores
# de otros usos): sin ellos el ensayo apuntaría a datos reales. El corte usa los defaults.
#
# ⚠️  EL VPS VIEJO ES COMPARTIDO con otra aplicación. Todo comando remoto está acotado POR
# NOMBRE a StockApp: la unit stockapp-api, el contenedor (stockapp-pg), /var/lib/stockapp y
# /etc/stockapp-api. Nada global de systemd ni de Docker, nada del proxy de la otra app, y
# NINGÚN dump pasa por el /tmp del host: el dump vive en el /tmp DEL CONTENEDOR (umask 077), se
# trae por el stdout de ssh y el trap lo borra al salir.
#
# Pasos:
#   1. Relevamiento: estado y habilitación de stockapp-api, contenedor corriendo, y la lista de
#      contenedores (docker ps --format '{{.Names}}') para compararla al final.
#   2. Congelamiento: systemctl stop stockapp-api y, en modo final, la unit queda ENMASCARADA.
#      Por qué mask: con la unit solo detenida, cualquier cosa que la arranque (un reinicio del
#      servidor, el hook de renovación del certificado, un deploy por error, un operador que no
#      sabe del corte) vuelve a abrir la base vieja a escrituras que ya no viajan al VPS nuevo;
#      enmascarada no arranca hasta un unmask explícito.
#      Cómo: install.sh deja la unit como ARCHIVO REGULAR en /etc/systemd/system, y ahí
#      'systemctl mask' falla ("already exists": el symlink a /dev/null va en esa misma ruta).
#      Por eso: se verifica con 'systemctl show -p FragmentPath' que la unit sea ese archivo, se
#      mueve a stockapp-api.service.congelado (al lado, nunca se borra), daemon-reload (relee las
#      units del disco; no reinicia ni cambia el estado de ningún otro servicio), 'systemctl -q
#      mask' (sin -q, mask escribe "Created symlink" en stderr aunque funcione) y is-enabled
#      tiene que dar masked. Después: is-active tiene que dar inactive y nadie puede estar
#      conectado a la base (pg_stat_activity, solo procesos cliente: autovacuum no cuenta).
#   3. Dump -Fc dentro del contenedor; sha256 y tamaño calculados ahí mismo.
#   4. Otra vez pg_stat_activity = 0, y los datos del manifiesto (fecha, host, versiones, sha,
#      tamaño, última migración y conteos por tabla, con la MISMA consulta que
#      restaurar-backup.sh).
#   5. Traída por el stdout de ssh; sha local == remoto y tamaño >= 1024, o se borra y aborta.
#      Recién con el dump verificado se escribe el manifiesto.
#   6. Historial de backups (tar por stdout), verificado con 'tar -tf'.
#   7. Overrides de api.env: solo los NOMBRES cruzan la red, y se listan los que install.sh no
#      escribe por su cuenta (hay que recrearlos a mano en el VPS nuevo).
#   8. (trap) Borra el dump del contenedor; con --ensayo, arranca la API si estaba activa.
#   9. docker ps final igual al inicial: el script no tocó ningún otro contenedor.
#
# Reintento: si un intento en modo final falla a mitad, la API queda detenida y enmascarada.
# Volver a correrlo funciona igual: con la unit ya enmascarada no se mueve nada más. Este script
# NUNCA hace start ni unmask en modo final: la vuelta atrás es una decisión del operador.
# Todo fallo posterior al stop, y el final feliz, informan en qué estado quedó la API y los
# comandos exactos para volver atrás (R-A), en el VPS viejo:
#   sudo systemctl unmask stockapp-api
#   sudo mv /etc/systemd/system/stockapp-api.service.congelado /etc/systemd/system/stockapp-api.service
#   sudo systemctl daemon-reload
#   sudo systemctl start stockapp-api
#
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
# =======================================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

uso() {
    sed -n '/^# Uso:/,/^#   --raiz-remota/p' "$0" | sed 's/^# \{0,1\}//' >&2
}

error_uso() {
    echo "ERROR: $1" >&2
    uso
    exit 2
}

# ---------------------------------------------------------------------------------------
# Argumentos
# ---------------------------------------------------------------------------------------
SALIDA=""
ENSAYO=0
CONTENEDOR="stockapp-pg"
RAIZ_REMOTA=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --salida|--contenedor|--raiz-remota)
            [[ $# -ge 2 ]] || error_uso "falta el valor de $1."
            case "$1" in
                --salida) SALIDA="$2" ;;
                --contenedor) CONTENEDOR="$2" ;;
                --raiz-remota) RAIZ_REMOTA="$2" ;;
            esac
            shift 2
            ;;
        --ensayo) ENSAYO=1; shift ;;
        -h|--help) uso; exit 0 ;;
        *) error_uso "argumento desconocido: $1" ;;
    esac
done

[[ -n "$SALIDA" ]] || error_uso "falta --salida <dir>."
if [[ -n "$RAIZ_REMOTA" ]]; then
    [[ "$RAIZ_REMOTA" =~ ^/[A-Za-z0-9._/-]+$ ]] || error_uso "--raiz-remota '${RAIZ_REMOTA}' no matchea ^/[A-Za-z0-9._/-]+$."
    [[ "$RAIZ_REMOTA" != *..* ]] || error_uso "--raiz-remota '${RAIZ_REMOTA}' no puede contener '..'."
fi
if [[ ! "$CONTENEDOR" =~ ^stockapp[a-z0-9-]*$ ]]; then
    echo "ERROR: el contenedor '${CONTENEDOR}' no matchea ^stockapp[a-z0-9-]*$." >&2
    echo "       Este script solo opera sobre contenedores de StockApp: ABORTO sin tocar nada." >&2
    exit 3
fi

# ---------------------------------------------------------------------------------------
# .env del operador (mismo bloque que deploy-vps.sh, con 3: config faltante es precondición)
# ---------------------------------------------------------------------------------------
ENV_LOCAL="${STOCKAPP_ENV_LOCAL:-${REPO_ROOT}/deploy/.env}"
if [[ -n "${STOCKAPP_ENV_LOCAL:-}" && ( ! -f "$ENV_LOCAL" || ! -r "$ENV_LOCAL" ) ]]; then
    echo "ERROR: STOCKAPP_ENV_LOCAL apunta a '${ENV_LOCAL}', que no existe o no se puede leer." >&2
    exit 3
fi
if [[ -f "$ENV_LOCAL" ]]; then
    set -a
    # shellcheck source=/dev/null
    source "$ENV_LOCAL"
    set +a
fi

# Sin defaults, como en deploy-vps.sh: sin configuración explícita no se apunta a ningún VPS.
for variable in VPS_HOST VPS_USER VPS_SSH_PORT; do
    if [[ -z "${!variable:-}" ]]; then
        echo "ERROR: falta ${variable} (en '${ENV_LOCAL}' o en el entorno). ABORTO sin tocar nada." >&2
        exit 3
    fi
done

POSTGRES_USER="${POSTGRES_USER:-stockapp}"
POSTGRES_DB="${POSTGRES_DB:-stockapp}"
for variable in POSTGRES_USER POSTGRES_DB; do
    if [[ ! "${!variable}" =~ ^[a-z_][a-z0-9_]*$ ]]; then
        echo "ERROR: ${variable} ('${!variable}') no matchea ^[a-z_][a-z0-9_]*$. ABORTO sin tocar nada." >&2
        exit 3
    fi
done

# Lo que install.sh escribe SIEMPRE en api.env (VARS_CONOCIDAS de install.sh más las claves fijas
# que arma con otro nombre). Cualquier otra clave es un override puesto a mano.
readonly CLAVES_QUE_ESCRIBE_INSTALL=(
    POSTGRES_USER POSTGRES_PASSWORD POSTGRES_DB API_PORT API_BIND API_SCHEME
    API_HOSTNAME JWT_SECRET BOOTSTRAP_ADMIN_USER BOOTSTRAP_PASSWORD LICENCIA_CLAVE_PUBLICA_BASE64
    Kestrel__Certificates__Default__Path Kestrel__Certificates__Default__KeyPath
    Jwt__Secret Bootstrap__AdminUser Bootstrap__Password Licencia__ClavePublicaBase64
    ConnectionStrings__Default
)

TS="$(date -u +%Y%m%dT%H%M%SZ)"
NOMBRE_DUMP="volcado-${TS}.dump"
DUMP_CONTENEDOR="/tmp/${NOMBRE_DUMP}"
DUMP_LOCAL="${SALIDA}/${NOMBRE_DUMP}"
MANIFIESTO_LOCAL="${SALIDA}/volcado-${TS}.manifiesto"
DIR_BACKUPS_REMOTO="${RAIZ_REMOTA}/var/lib/stockapp/.local/share/StockApp"
UNIT_ETC="${RAIZ_REMOTA}/etc/systemd/system/stockapp-api.service"
UNIT_CONGELADA="${UNIT_ETC}.congelado"
API_ENV_REMOTO="${RAIZ_REMOTA}/etc/stockapp-api/api.env"

# ---------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------
ssh_vps() {
    ssh -p "$VPS_SSH_PORT" -o BatchMode=yes "${VPS_USER}@${VPS_HOST}" "$@"
}

# remoto <comando> [<sql por stdin>]: deja stdout en SALIDA_REMOTA, el exit code en RC_REMOTO y
# el stderr en ERROR_REMOTO. No aborta: cada llamador decide qué es "no pude consultar".
SALIDA_REMOTA=""
RC_REMOTO=0
ERROR_REMOTO=""
remoto() {
    local cmd="$1" archivo_stderr
    archivo_stderr="$(mktemp)"
    RC_REMOTO=0
    if [[ $# -ge 2 ]]; then
        SALIDA_REMOTA="$(printf '%s\n' "$2" | ssh_vps "$cmd" 2>"$archivo_stderr")" || RC_REMOTO=$?
    else
        SALIDA_REMOTA="$(ssh_vps "$cmd" </dev/null 2>"$archivo_stderr")" || RC_REMOTO=$?
    fi
    ERROR_REMOTO="$(cat "$archivo_stderr")"
    rm -f "$archivo_stderr"
}

no_pude() {
    echo "ERROR: no se pudo consultar $1 (código ${RC_REMOTO})." >&2
    echo "       Esto NO es 'cero' ni 'vacío': la consulta FALLÓ. ABORTO." >&2
    [[ -z "$ERROR_REMOTO" ]] || printf '       stderr: %s\n' "$ERROR_REMOTO" >&2
    exit 8
}

# remoto_ok <qué> <comando> [<sql>]: como remoto, pero exit ≠ 0 o stderr no vacío abortan con 8.
remoto_ok() {
    local que="$1"
    shift
    remoto "$@"
    if [[ "$RC_REMOTO" -ne 0 || -n "$ERROR_REMOTO" ]]; then
        no_pude "$que"
    fi
}

readonly PSQL_REMOTO="docker exec -i ${CONTENEDOR} psql -U ${POSTGRES_USER} -d ${POSTGRES_DB} -tA -v ON_ERROR_STOP=1"

# Conexiones a la base que no son la de esta consulta. Un número distinto de 0 aborta con 3; una
# salida que no es un número aborta con 8.
verificar_sin_conexiones() {
    remoto_ok "las conexiones abiertas (pg_stat_activity)" "$PSQL_REMOTO" \
        "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND backend_type = 'client backend' AND pid <> pg_backend_pid();"
    if [[ ! "$SALIDA_REMOTA" =~ ^[0-9]+$ ]]; then
        echo "ERROR: la cuenta de conexiones no es un número: '${SALIDA_REMOTA}'. ABORTO." >&2
        exit 8
    fi
    if [[ "$SALIDA_REMOTA" -ne 0 ]]; then
        echo "ERROR: hay ${SALIDA_REMOTA} conexión(es) abierta(s) a '${POSTGRES_DB}' con la API detenida." >&2
        echo "       Algo más escribe o lee la base: un volcado ahora no es el estado final. ABORTO." >&2
        exit 3
    fi
}

# ---------------------------------------------------------------------------------------
# Paso 8 (trap): limpieza y, en --ensayo, dejar la API como estaba
# ---------------------------------------------------------------------------------------
DUMP_EN_CONTENEDOR=0
ARRANCAR_AL_SALIR=0
DETENIDA=0
UNIT_MOVIDA=0
ENMASCARADA=0

# Estado de stockapp-api en modo final y los comandos exactos para volver atrás (R-A).
informar_estado_final() {
    local estado="DETENIDA"
    [[ "$UNIT_MOVIDA" -eq 0 ]] || estado+=", con la unit movida a ${UNIT_CONGELADA}"
    [[ "$ENMASCARADA" -eq 0 ]] || estado+=" y ENMASCARADA"
    echo "stockapp-api queda ${estado} en el VPS viejo."
    echo "Para volver atrás (R-A), en el VPS viejo y en este orden:"
    if [[ "$ENMASCARADA" -eq 1 ]]; then
        echo "  sudo systemctl unmask stockapp-api"
    fi
    if [[ "$UNIT_MOVIDA" -eq 1 ]]; then
        echo "  sudo mv ${UNIT_CONGELADA} ${UNIT_ETC}"
        echo "  sudo systemctl daemon-reload"
    fi
    echo "  sudo systemctl start stockapp-api"
}

al_salir() {
    local rc=$?
    trap - EXIT
    if [[ "$rc" -ne 0 && "$ENSAYO" -eq 0 && "$DETENIDA" -eq 1 ]]; then
        {
            echo
            echo "ATENCIÓN: el volcado falló DESPUÉS de detener la API."
            informar_estado_final
        } >&2
    fi
    if [[ "$DUMP_EN_CONTENEDOR" -eq 1 ]]; then
        remoto "docker exec ${CONTENEDOR} rm -f ${DUMP_CONTENEDOR}"
        if [[ "$RC_REMOTO" -ne 0 ]]; then
            echo "ATENCIÓN: no se pudo borrar ${DUMP_CONTENEDOR} dentro de '${CONTENEDOR}' (código ${RC_REMOTO})." >&2
            echo "          Borralo a mano: es una copia completa de la base." >&2
            [[ "$rc" -ne 0 ]] || rc=8
        fi
    fi
    if [[ "$ARRANCAR_AL_SALIR" -eq 1 ]]; then
        remoto "sudo systemctl start stockapp-api"
        if [[ "$RC_REMOTO" -ne 0 ]]; then
            echo "ATENCIÓN: --ensayo no pudo volver a arrancar stockapp-api (código ${RC_REMOTO})." >&2
            [[ -z "$ERROR_REMOTO" ]] || printf '          stderr: %s\n' "$ERROR_REMOTO" >&2
            echo "          La API estaba activa al empezar: arrancala a mano." >&2
            [[ "$rc" -ne 0 ]] || rc=8
        else
            echo "  (--ensayo) stockapp-api arrancada otra vez, como estaba al empezar."
        fi
    fi
    exit "$rc"
}
trap al_salir EXIT
trap 'exit 130' INT TERM

# ---------------------------------------------------------------------------------------
# Salida local y sudo remoto
# ---------------------------------------------------------------------------------------
umask 077
mkdir -p "$SALIDA"
chmod 700 "$SALIDA"

# Con stderr no vacío también aborta (aunque el rc sea 0): una advertencia de sudo (p. ej. "unable
# to resolve host") aparece en CADA sudo, y remoto_ok la tomaría como fallo recién DESPUÉS del
# stop, con la API ya detenida. Mejor frenar acá, sin haber tocado nada, que aceptar una lista
# de advertencias "conocidas" que nunca va a estar completa.
remoto "sudo -n true"
if [[ "$RC_REMOTO" -ne 0 || -n "$ERROR_REMOTO" ]]; then
    echo "ERROR: '${VPS_USER}' no puede usar sudo sin contraseña en el VPS (o el ssh falló, código ${RC_REMOTO})." >&2
    [[ -z "$ERROR_REMOTO" ]] || printf '       stderr: %s\n' "$ERROR_REMOTO" >&2
    exit 3
fi

# ---------------------------------------------------------------------------------------
# Paso 1 -- Relevamiento
# ---------------------------------------------------------------------------------------
echo "== Paso 1: relevamiento =="
# is-active sale ≠ 0 cuando la unit no está activa: lo que vale es la palabra que imprime.
remoto "systemctl is-active stockapp-api"
ESTADO_INICIAL="$SALIDA_REMOTA"
[[ "$ESTADO_INICIAL" =~ ^[a-z-]+$ ]] || no_pude "el estado de stockapp-api (systemctl is-active)"
remoto "systemctl is-enabled stockapp-api"
HABILITACION_INICIAL="$SALIDA_REMOTA"
[[ "$HABILITACION_INICIAL" =~ ^[a-z-]+$ ]] || no_pude "la habilitación de stockapp-api (systemctl is-enabled)"
echo "  stockapp-api: ${ESTADO_INICIAL} / ${HABILITACION_INICIAL}"

remoto "docker inspect -f '{{.State.Running}}' ${CONTENEDOR}"
if [[ "$RC_REMOTO" -ne 0 || "$SALIDA_REMOTA" != "true" ]]; then
    echo "ERROR: el contenedor '${CONTENEDOR}' no está corriendo en el VPS (código ${RC_REMOTO}, salida '${SALIDA_REMOTA}')." >&2
    [[ -z "$ERROR_REMOTO" ]] || printf '       stderr: %s\n' "$ERROR_REMOTO" >&2
    exit 3
fi

remoto_ok "la lista de contenedores (docker ps)" "docker ps --format '{{.Names}}'"
CONTENEDORES_INICIALES="$(printf '%s\n' "$SALIDA_REMOTA" | LC_ALL=C sort)"

if [[ "$ENSAYO" -eq 1 && "$ESTADO_INICIAL" == "active" ]]; then
    ARRANCAR_AL_SALIR=1
fi

# ---------------------------------------------------------------------------------------
# Paso 2 -- Congelamiento
# ---------------------------------------------------------------------------------------
echo "== Paso 2: congelamiento de stockapp-api =="

# congelar_unit: deja stockapp-api enmascarada aunque install.sh la haya instalado como archivo
# regular en /etc/systemd/system (ver el encabezado). Con la unit ya enmascarada (reintento) no
# toca nada.
congelar_unit() {
    remoto "systemctl is-enabled stockapp-api"
    [[ "$SALIDA_REMOTA" =~ ^[a-z-]+$ ]] || no_pude "la habilitación de stockapp-api (systemctl is-enabled)"
    if [[ "$SALIDA_REMOTA" == "masked" ]]; then
        ENMASCARADA=1
        remoto "sudo test -e ${UNIT_CONGELADA}"
        if [[ "$RC_REMOTO" -eq 0 && -z "$ERROR_REMOTO" ]]; then
            UNIT_MOVIDA=1
        elif [[ "$RC_REMOTO" -ne 1 || -n "$ERROR_REMOTO" ]]; then
            no_pude "si existe ${UNIT_CONGELADA}"
        fi
        echo "  stockapp-api ya estaba enmascarada (reintento): no se mueve nada."
        return
    fi

    remoto_ok "la ruta de la unit (systemctl show -p FragmentPath)" "systemctl show -p FragmentPath stockapp-api"
    if [[ "$SALIDA_REMOTA" != "FragmentPath=${UNIT_ETC}" ]]; then
        echo "ERROR: la unit stockapp-api no es '${UNIT_ETC}' (la que deja install.sh): systemd dice '${SALIDA_REMOTA}'." >&2
        echo "       No se mueve ni se enmascara un archivo que este script no conoce. ABORTO." >&2
        exit 8
    fi
    remoto "sudo test -e ${UNIT_CONGELADA}"
    if [[ "$RC_REMOTO" -eq 0 && -z "$ERROR_REMOTO" ]]; then
        echo "ERROR: ya existe '${UNIT_CONGELADA}' (¿un intento anterior a medias?). No se pisa. ABORTO." >&2
        exit 8
    elif [[ "$RC_REMOTO" -ne 1 || -n "$ERROR_REMOTO" ]]; then
        no_pude "si existe ${UNIT_CONGELADA}"
    fi

    remoto_ok "el mv de la unit a ${UNIT_CONGELADA}" "sudo mv ${UNIT_ETC} ${UNIT_CONGELADA}"
    UNIT_MOVIDA=1
    remoto_ok "el daemon-reload" "sudo systemctl daemon-reload"
    remoto_ok "el mask de stockapp-api" "sudo systemctl -q mask stockapp-api"
    ENMASCARADA=1
    remoto "systemctl is-enabled stockapp-api"
    [[ -n "$SALIDA_REMOTA" ]] || no_pude "la habilitación de stockapp-api tras el mask"
    if [[ "$SALIDA_REMOTA" != "masked" ]]; then
        echo "ERROR: tras el mask, stockapp-api está '${SALIDA_REMOTA}' (se esperaba masked). ABORTO sin volcar." >&2
        exit 3
    fi
}

# Desde acá, en modo final, todo fallo informa el estado de la API (al_salir).
[[ "$ENSAYO" -eq 1 ]] || DETENIDA=1
remoto_ok "el stop de stockapp-api" "sudo systemctl stop stockapp-api"
if [[ "$ENSAYO" -eq 0 ]]; then
    congelar_unit
fi
remoto "systemctl is-active stockapp-api"
[[ -n "$SALIDA_REMOTA" ]] || no_pude "el estado de stockapp-api tras el stop"
if [[ "$SALIDA_REMOTA" != "inactive" ]]; then
    echo "ERROR: tras el stop, stockapp-api está '${SALIDA_REMOTA}' (se esperaba inactive). ABORTO sin volcar." >&2
    exit 3
fi
verificar_sin_conexiones
echo "  stockapp-api inactive, sin conexiones a '${POSTGRES_DB}'."

# ---------------------------------------------------------------------------------------
# Paso 3 -- Dump dentro del contenedor
# ---------------------------------------------------------------------------------------
echo "== Paso 3: pg_dump dentro de '${CONTENEDOR}' =="
DUMP_EN_CONTENEDOR=1
remoto_ok "el pg_dump" "docker exec ${CONTENEDOR} sh -c 'umask 077; pg_dump -U ${POSTGRES_USER} -d ${POSTGRES_DB} --format=custom -f ${DUMP_CONTENEDOR}'"
remoto_ok "el sha256 del dump" "docker exec ${CONTENEDOR} sha256sum ${DUMP_CONTENEDOR}"
SHA_REMOTO="${SALIDA_REMOTA%% *}"
[[ "$SHA_REMOTO" =~ ^[0-9a-f]{64}$ ]] || no_pude "el sha256 del dump (salida '${SALIDA_REMOTA}')"
remoto_ok "el tamaño del dump" "docker exec ${CONTENEDOR} stat -c %s ${DUMP_CONTENEDOR}"
TAMANO_REMOTO="$SALIDA_REMOTA"
[[ "$TAMANO_REMOTO" =~ ^[0-9]+$ ]] || no_pude "el tamaño del dump (salida '${TAMANO_REMOTO}')"
FECHA_DUMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "  ${NOMBRE_DUMP}: ${TAMANO_REMOTO} bytes, sha256 ${SHA_REMOTO}"

# ---------------------------------------------------------------------------------------
# Paso 4 -- Manifiesto
# ---------------------------------------------------------------------------------------
echo "== Paso 4: manifiesto =="
verificar_sin_conexiones

# Consulta de conteos: IDÉNTICA a la de restaurar-backup.sh (la custodia MigracionVpsSincroniaTests).
# >>> SQL-CONTEOS
SQL_CONTEOS="$(cat <<'SQL'
SELECT table_name,
       (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text::bigint
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
ORDER BY table_name;
SQL
)"
# <<< SQL-CONTEOS
readonly SQL_CONTEOS

remoto_ok "los conteos por tabla" "$PSQL_REMOTO" "$SQL_CONTEOS"
CONTEOS="$SALIDA_REMOTA"
if [[ -z "$CONTEOS" ]]; then
    echo "ERROR: la base '${POSTGRES_DB}' no tiene tablas: no es una base de StockApp. ABORTO." >&2
    exit 3
fi
while IFS= read -r linea; do
    [[ "$linea" =~ ^[^|]+\|[0-9]+$ ]] || no_pude "los conteos por tabla (línea ilegible '${linea}')"
done <<< "$CONTEOS"

remoto_ok "la última migración" "$PSQL_REMOTO" 'SELECT max("MigrationId") FROM "__EFMigrationsHistory";'
ULTIMA_MIGRACION="$SALIDA_REMOTA"
[[ -n "$ULTIMA_MIGRACION" ]] || no_pude "la última migración (__EFMigrationsHistory vacía)"
remoto_ok "la versión del servidor" "$PSQL_REMOTO" "SELECT version();"
VERSION_SERVIDOR="$SALIDA_REMOTA"
remoto_ok "la versión de pg_dump" "docker exec ${CONTENEDOR} pg_dump --version"
VERSION_PG_DUMP="$SALIDA_REMOTA"
remoto_ok "el hostname del VPS" "hostname"
HOST_ORIGEN="$SALIDA_REMOTA"

escribir_manifiesto() {
    {
        echo "# manifiesto-migracion v1"
        echo "fecha_utc=${FECHA_DUMP}"
        echo "host_origen=${HOST_ORIGEN}"
        echo "version_servidor=${VERSION_SERVIDOR}"
        echo "version_pg_dump=${VERSION_PG_DUMP}"
        echo "base=${POSTGRES_DB}"
        echo "dump=${NOMBRE_DUMP}"
        echo "sha256=${SHA_REMOTO}"
        echo "tamano_bytes=${TAMANO_REMOTO}"
        echo "ultima_migracion=${ULTIMA_MIGRACION}"
        echo "[conteos]"
        printf '%s\n' "$CONTEOS" | tr '|' '\t'
    } > "$MANIFIESTO_LOCAL"
    chmod 600 "$MANIFIESTO_LOCAL"
}

# ---------------------------------------------------------------------------------------
# Paso 5 -- Traída por el stdout de ssh, verificada
# ---------------------------------------------------------------------------------------
echo "== Paso 5: traída del dump =="
descartar_dump_local() {
    rm -f "$DUMP_LOCAL"
    echo "       Se borró la copia local: no queda un dump que no coincide con el del VPS." >&2
    exit 4
}
RC_TRAIDA=0
ssh_vps "docker exec ${CONTENEDOR} cat ${DUMP_CONTENEDOR}" </dev/null > "$DUMP_LOCAL" || RC_TRAIDA=$?
chmod 600 "$DUMP_LOCAL"
if [[ "$RC_TRAIDA" -ne 0 ]]; then
    echo "ERROR: la transferencia del dump falló (código ${RC_TRAIDA})." >&2
    descartar_dump_local
fi
SHA_LOCAL="$(sha256sum "$DUMP_LOCAL")"
SHA_LOCAL="${SHA_LOCAL%% *}"
TAMANO_LOCAL="$(stat -c %s "$DUMP_LOCAL")"
if [[ "$SHA_LOCAL" != "$SHA_REMOTO" ]]; then
    echo "ERROR: el sha256 local (${SHA_LOCAL}) no es el del VPS (${SHA_REMOTO})." >&2
    descartar_dump_local
fi
if [[ "$TAMANO_LOCAL" -lt 1024 ]]; then
    echo "ERROR: el dump pesa ${TAMANO_LOCAL} bytes (< 1024): no es un volcado de StockApp." >&2
    descartar_dump_local
fi
echo "  sha256 local == remoto, ${TAMANO_LOCAL} bytes."
# El manifiesto se escribe recién ahora: nunca queda uno describiendo un dump que se descartó.
escribir_manifiesto
echo "  ${MANIFIESTO_LOCAL}"

# ---------------------------------------------------------------------------------------
# Paso 6 -- Historial de backups
# ---------------------------------------------------------------------------------------
echo "== Paso 6: historial de backups =="
TAR_LOCAL="${SALIDA}/backups-historial.tar"
RC_TAR=0
ssh_vps "sudo tar -C ${DIR_BACKUPS_REMOTO} -cpf - backups" </dev/null > "$TAR_LOCAL" || RC_TAR=$?
chmod 600 "$TAR_LOCAL"
if [[ "$RC_TAR" -ne 0 ]]; then
    rm -f "$TAR_LOCAL"
    echo "ERROR: no se pudo traer el historial de backups de ${DIR_BACKUPS_REMOTO}/backups (código ${RC_TAR})." >&2
    exit 8
fi
if ! tar -tf "$TAR_LOCAL" >/dev/null; then
    rm -f "$TAR_LOCAL"
    echo "ERROR: el historial de backups traído no es un tar legible ('tar -tf' falla). Se borró." >&2
    exit 4
fi
echo "  ${TAR_LOCAL}"

# ---------------------------------------------------------------------------------------
# Paso 7 -- Overrides de api.env (solo nombres)
# ---------------------------------------------------------------------------------------
echo "== Paso 7: overrides de api.env =="
remoto_ok "los nombres de ${API_ENV_REMOTO}" "sudo sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' ${API_ENV_REMOTO}"
OVERRIDES_LOCAL="${SALIDA}/overrides-api-env.txt"
: > "$OVERRIDES_LOCAL"
while IFS= read -r nombre; do
    [[ -n "$nombre" ]] || continue
    conocida=0
    for clave in "${CLAVES_QUE_ESCRIBE_INSTALL[@]}"; do
        [[ "$nombre" == "$clave" ]] && conocida=1
    done
    [[ "$conocida" -eq 1 ]] || echo "$nombre" >> "$OVERRIDES_LOCAL"
done <<< "$SALIDA_REMOTA"
chmod 600 "$OVERRIDES_LOCAL"
if [[ -s "$OVERRIDES_LOCAL" ]]; then
    echo "  Overrides a recrear a mano en el VPS nuevo (solo nombres):"
    sed 's/^/    /' "$OVERRIDES_LOCAL"
else
    echo "  Sin overrides."
fi

# ---------------------------------------------------------------------------------------
# Paso 9 -- Alcance: los contenedores son los mismos que al empezar
# ---------------------------------------------------------------------------------------
echo "== Paso 9: alcance =="
remoto_ok "la lista de contenedores (docker ps)" "docker ps --format '{{.Names}}'"
CONTENEDORES_FINALES="$(printf '%s\n' "$SALIDA_REMOTA" | LC_ALL=C sort)"
if [[ "$CONTENEDORES_FINALES" != "$CONTENEDORES_INICIALES" ]]; then
    echo "ERROR: la lista de contenedores del VPS cambió durante el volcado." >&2
    echo "       Al empezar: $(printf '%s' "$CONTENEDORES_INICIALES" | tr '\n' ' ')" >&2
    echo "       Al final:   $(printf '%s' "$CONTENEDORES_FINALES" | tr '\n' ' ')" >&2
    exit 9
fi

echo
echo "OK: volcado en ${SALIDA}"
echo "  dump:       ${NOMBRE_DUMP} (${TAMANO_LOCAL} bytes, sha256 ${SHA_LOCAL})"
echo "  manifiesto: $(basename "$MANIFIESTO_LOCAL")"
if [[ "$ENSAYO" -eq 0 ]]; then
    echo
    informar_estado_final
fi
