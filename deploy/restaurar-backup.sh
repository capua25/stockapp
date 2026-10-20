#!/usr/bin/env bash
# =======================================================================================
# deploy/restaurar-backup.sh -- restaura un dump de StockApp en una base VACÍA del contenedor
# Postgres destino, en una sola transacción, y verifica el resultado.
# =======================================================================================
#
# Corre EN EL SERVIDOR donde está el contenedor destino (el VPS nuevo, o la máquina del
# operador en el ensayo local). Usa 'docker exec' y 'docker cp': el usuario tiene que estar en
# el grupo docker; no hace falta sudo.
#
# Uso:
#   deploy/restaurar-backup.sh <dump> [--manifiesto <archivo>] [--contenedor <nombre>]
#                              [--base <nombre>] [--usuario <rol>] [--env <archivo>]
#                              [--recrear-base <nombre>] [--dir-seguridad <dir>]
#                              [--verificar-post-arranque]
#
#   <dump>            archivo 'pg_dump --format=custom' (volcado final, backup_*.dump o el .bin
#                     que baja deploy-vps.sh: es el mismo -Fc sin cifrar).
#   --contenedor      default stockapp-pg. Tiene que matchear ^stockapp[a-z0-9-]*$: el script
#                     nunca opera sobre un contenedor que no sea de StockApp.
#   --base/--usuario  default: POSTGRES_DB / POSTGRES_USER del --env. Validados con
#                     ^[a-z_][a-z0-9_]*$ antes de interpolarlos en SQL o en un comando.
#   --env             default ~/stockapp-deploy/.env. Solo se lee si falta --base o --usuario,
#                     y se lee línea por línea (nunca se ejecuta con 'source').
#   --dir-seguridad   default ~/stockapp-deploy/migracion.
#
# Códigos de salida (estables):
#   0  OK
#   2  uso/argumentos/manifiesto inválidos
#   3  precondición: API activa, contenedor fuera de patrón/ausente/detenido, base no vacía
#   4  dump inválido (pg_restore --list falla)
#   5  falló pg_restore: la base quedó VACÍA (transacción única), se puede reintentar tal cual
#   8  no se pudo consultar (docker/psql con error): "no pude consultar" nunca es "vacío"
#
# Este script NUNCA arranca la API: al terminar, la decisión de arrancarla es del operador.
# =======================================================================================

set -euo pipefail

uso() {
    sed -n '/^# Uso:/,/^#   --dir-seguridad/p' "$0" | sed 's/^# \{0,1\}//' >&2
}

error_uso() {
    echo "ERROR: $1" >&2
    uso
    exit 2
}

DUMP=""
MANIFIESTO=""
CONTENEDOR="stockapp-pg"
BASE=""
USUARIO=""
ENV_SERVIDOR="${HOME}/stockapp-deploy/.env"
RECREAR_BASE=""
DIR_SEGURIDAD="${HOME}/stockapp-deploy/migracion"
VERIFICAR_POST_ARRANQUE=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --manifiesto|--contenedor|--base|--usuario|--env|--recrear-base|--dir-seguridad)
            [[ $# -ge 2 ]] || error_uso "falta el valor de $1."
            case "$1" in
                --manifiesto) MANIFIESTO="$2" ;;
                --contenedor) CONTENEDOR="$2" ;;
                --base) BASE="$2" ;;
                --usuario) USUARIO="$2" ;;
                --env) ENV_SERVIDOR="$2" ;;
                --recrear-base) RECREAR_BASE="$2" ;;
                --dir-seguridad) DIR_SEGURIDAD="$2" ;;
            esac
            shift 2
            ;;
        --verificar-post-arranque) VERIFICAR_POST_ARRANQUE=1; shift ;;
        -h|--help) uso; exit 0 ;;
        -*) error_uso "opción desconocida: $1" ;;
        *)
            [[ -z "$DUMP" ]] || error_uso "sobra el argumento '$1' (ya se indicó el dump '${DUMP}')."
            DUMP="$1"
            shift
            ;;
    esac
done

if [[ -n "$MANIFIESTO" ]]; then
    echo "ERROR: --manifiesto todavía no soportado." >&2
    exit 2
fi
if [[ -n "$RECREAR_BASE" ]]; then
    echo "ERROR: --recrear-base todavía no soportado." >&2
    exit 2
fi
if [[ "$VERIFICAR_POST_ARRANQUE" -eq 1 ]]; then
    echo "ERROR: --verificar-post-arranque todavía no soportado." >&2
    exit 2
fi

[[ -n "$DUMP" ]] || error_uso "falta el dump a restaurar."
[[ -f "$DUMP" && -r "$DUMP" ]] || error_uso "el dump '${DUMP}' no existe o no se puede leer."

if [[ ! "$CONTENEDOR" =~ ^stockapp[a-z0-9-]*$ ]]; then
    echo "ERROR: el contenedor '${CONTENEDOR}' no matchea ^stockapp[a-z0-9-]*$." >&2
    echo "       Este script solo opera sobre contenedores de StockApp: ABORTO sin tocar nada." >&2
    exit 3
fi

# --base / --usuario: del .env del servidor solo si faltan. Se LEE con sed, nunca con 'source':
# es un archivo del servidor y no hay por qué ejecutar lo que tenga adentro.
leer_del_env() {
    local clave="$1" valor
    valor="$(sed -n "s/^${clave}=//p" "$ENV_SERVIDOR")"
    valor="${valor##*$'\n'}"           # si la clave aparece dos veces, gana la última
    valor="${valor%\"}"; valor="${valor#\"}"
    valor="${valor%\'}"; valor="${valor#\'}"
    printf '%s' "$valor"
}
if [[ -z "$BASE" || -z "$USUARIO" ]]; then
    if [[ ! -f "$ENV_SERVIDOR" || ! -r "$ENV_SERVIDOR" ]]; then
        error_uso "faltan --base/--usuario y no se puede leer el .env '${ENV_SERVIDOR}' (de ahí salen POSTGRES_DB / POSTGRES_USER)."
    fi
    [[ -n "$BASE" ]] || BASE="$(leer_del_env POSTGRES_DB)"
    [[ -n "$USUARIO" ]] || USUARIO="$(leer_del_env POSTGRES_USER)"
    [[ -n "$BASE" ]] || error_uso "'${ENV_SERVIDOR}' no define POSTGRES_DB y no se pasó --base."
    [[ -n "$USUARIO" ]] || error_uso "'${ENV_SERVIDOR}' no define POSTGRES_USER y no se pasó --usuario."
fi

[[ "$BASE" =~ ^[a-z_][a-z0-9_]*$ ]] || error_uso "nombre de base inválido: '${BASE}' (tiene que matchear ^[a-z_][a-z0-9_]*$)."
[[ "$USUARIO" =~ ^[a-z_][a-z0-9_]*$ ]] || error_uso "nombre de rol inválido: '${USUARIO}' (tiene que matchear ^[a-z_][a-z0-9_]*$)."

# ---------------------------------------------------------------------------------------
# Helpers de consulta
# ---------------------------------------------------------------------------------------

# consultar <base> <sql> [<qué>]: SQL por stdin a psql dentro del contenedor; deja la salida
# en SALIDA_CONSULTA. Un exit code ≠ 0 o un stderr no vacío ABORTAN con 8: "no pude consultar"
# nunca se confunde con "vacío", "cero" ni "sin duplicados".
SALIDA_CONSULTA=""
consultar() {
    local base="$1" sql="$2" que="${3:-la consulta}"
    local archivo_stderr rc=0 error_consulta
    archivo_stderr="$(mktemp)"
    SALIDA_CONSULTA="$(printf '%s\n' "$sql" | docker exec -i "$CONTENEDOR" \
        psql -U "$USUARIO" -d "$base" -tA -v ON_ERROR_STOP=1 2>"$archivo_stderr")" || rc=$?
    error_consulta="$(cat "$archivo_stderr")"
    rm -f "$archivo_stderr"
    if [[ "$rc" -ne 0 || -n "$error_consulta" ]]; then
        echo "ERROR: no se pudo consultar ${que} en la base '${base}' del contenedor '${CONTENEDOR}' (código ${rc})." >&2
        echo "       Esto NO es 'base vacía' ni 'sin datos': es que la consulta FALLÓ. ABORTO." >&2
        [[ -z "$error_consulta" ]] || printf '       stderr: %s\n' "$error_consulta" >&2
        exit 8
    fi
}

# Consulta de conteos: IDÉNTICA a la del manifiesto que arma volcado-final-vps.sh (la custodia
# MigracionVpsSincroniaTests). Una sola consulta determinística, tabla por tabla.
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

# listar_conteos <base>: deja "tabla|filas" por línea en SALIDA_CONSULTA.
listar_conteos() {
    consultar "$1" "$SQL_CONTEOS" "los conteos por tabla"
}

# ---------------------------------------------------------------------------------------
# Paso 1 -- La API no está activa
# ---------------------------------------------------------------------------------------
if command -v systemctl >/dev/null 2>&1; then
    rc_api=0
    systemctl is-active --quiet stockapp-api >/dev/null 2>&1 || rc_api=$?
    if [[ "$rc_api" -eq 0 ]]; then
        echo "ERROR: stockapp-api está activa. Restaurar con la API corriendo mezclaría escrituras" >&2
        echo "       nuevas con el dump. Detenela antes (sudo systemctl stop stockapp-api). ABORTO." >&2
        exit 3
    fi
else
    echo "AVISO: no hay systemctl en esta máquina: la API cuenta como inactiva."
fi

# ---------------------------------------------------------------------------------------
# Paso 2 -- El contenedor existe, está corriendo y Postgres responde
# ---------------------------------------------------------------------------------------
archivo_stderr="$(mktemp)"
rc_inspect=0
estado="$(docker inspect -f '{{.State.Running}}' "$CONTENEDOR" 2>"$archivo_stderr")" || rc_inspect=$?
error_inspect="$(cat "$archivo_stderr")"
rm -f "$archivo_stderr"
if [[ "$rc_inspect" -ne 0 ]]; then
    if [[ "$error_inspect" == *"No such"* ]]; then
        echo "ERROR: el contenedor '${CONTENEDOR}' no existe. ABORTO." >&2
        exit 3
    fi
    echo "ERROR: no se pudo consultar el estado del contenedor '${CONTENEDOR}' (código ${rc_inspect})." >&2
    echo "       Esto NO es 'contenedor detenido': docker no respondió. stderr: ${error_inspect}" >&2
    exit 8
fi
if [[ "$estado" != "true" ]]; then
    echo "ERROR: el contenedor '${CONTENEDOR}' existe pero no está corriendo (Running=${estado}). ABORTO." >&2
    exit 3
fi
rc_listo=0
docker exec "$CONTENEDOR" pg_isready -U "$USUARIO" -d "$BASE" >/dev/null 2>&1 || rc_listo=$?
if [[ "$rc_listo" -ne 0 ]]; then
    echo "ERROR: Postgres no responde dentro de '${CONTENEDOR}' (pg_isready salió ${rc_listo}). ABORTO." >&2
    exit 3
fi
echo "OK: contenedor '${CONTENEDOR}' corriendo y Postgres responde."

# ---------------------------------------------------------------------------------------
# Paso 6 (adelantado, de solo lectura) -- La base destino está vacía
# ---------------------------------------------------------------------------------------
# Vacía = ninguna relación fuera de pg_catalog/information_schema/pg_toast* y ningún schema
# creado por usuarios (fuera de public). Se chequea ANTES de copiar nada: es solo lectura.
consultar "$BASE" "SELECT
  (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
      AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%')
  || '|' ||
  (SELECT count(*) FROM pg_namespace
    WHERE nspname NOT IN ('public', 'pg_catalog', 'information_schema')
      AND nspname NOT LIKE 'pg_toast%' AND nspname NOT LIKE 'pg_temp%');" "si la base está vacía"
if [[ ! "$SALIDA_CONSULTA" =~ ^([0-9]+)\|([0-9]+)$ ]]; then
    echo "ERROR: respuesta inesperada al chequear si '${BASE}' está vacía: '${SALIDA_CONSULTA}'." >&2
    echo "       Esto NO es 'base vacía': no se pudo interpretar la consulta. ABORTO." >&2
    exit 8
fi
relaciones="${BASH_REMATCH[1]}"
schemas_extra="${BASH_REMATCH[2]}"

# filas_si_existe <tabla>: "no existe" o la cantidad de filas (to_regclass antes de contar: una
# tabla ausente es un resultado válido, distinto de una consulta que falla).
filas_si_existe() {
    local tabla="$1"
    consultar "$BASE" "SELECT to_regclass(format('public.%I', '${tabla}')) IS NOT NULL;" "si existe ${tabla}"
    if [[ "$SALIDA_CONSULTA" == "f" ]]; then
        printf 'no existe'
        return
    fi
    consultar "$BASE" "SELECT count(*) FROM public.\"${tabla}\";" "las filas de ${tabla}"
    printf '%s' "$SALIDA_CONSULTA"
}

if [[ "$relaciones" -ne 0 || "$schemas_extra" -ne 0 ]]; then
    consultar "$BASE" "SELECT count(*) FROM information_schema.tables
      WHERE table_schema NOT IN ('pg_catalog', 'information_schema') AND table_type = 'BASE TABLE';" "la cantidad de tablas"
    tablas_existentes="$SALIDA_CONSULTA"
    filas_auditoria="$(filas_si_existe LogsAuditoria)"
    filas_migraciones="$(filas_si_existe __EFMigrationsHistory)"
    {
        echo "ERROR: la base '${BASE}' NO está vacía (${relaciones} relaciones, ${schemas_extra} schemas extra). ABORTO sin tocar nada."
        echo "       Tablas: ${tablas_existentes}"
        echo "       LogsAuditoria: ${filas_auditoria}"
        echo "       __EFMigrationsHistory: ${filas_migraciones}"
        echo "       Restaurar encima pisaría datos (y la auditoría). Si de verdad hay que reemplazarla,"
        echo "       el procedimiento es otro: ver el runbook de la migración."
    } >&2
    exit 3
fi
echo "OK: la base '${BASE}' está vacía."

# ---------------------------------------------------------------------------------------
# Paso 4 -- Copia del dump al contenedor (y limpieza garantizada)
# ---------------------------------------------------------------------------------------
# docker cp y no stdin: pg_restore leyendo -Fc de un pipe no puede hacer seek y puede fallar
# con "could not find block ID". pg_restore corre DENTRO del contenedor: misma versión que el
# servidor y socket local, sin contraseñas por argv ni por el entorno del host.
RUTA_EN_CONTENEDOR="/tmp/restaurar-${RANDOM}${RANDOM}.dump"
TEMPORALES_EN_CONTENEDOR=()
limpiar_contenedor() {
    local ruta
    for ruta in "${TEMPORALES_EN_CONTENEDOR[@]}"; do
        docker exec "$CONTENEDOR" rm -f "$ruta" >/dev/null 2>&1 || \
            echo "AVISO: no se pudo borrar '${ruta}' dentro de '${CONTENEDOR}': borralo a mano." >&2
    done
}
trap limpiar_contenedor EXIT

TEMPORALES_EN_CONTENEDOR+=("$RUTA_EN_CONTENEDOR")
archivo_stderr="$(mktemp)"
rc_cp=0
docker cp "$DUMP" "${CONTENEDOR}:${RUTA_EN_CONTENEDOR}" >/dev/null 2>"$archivo_stderr" || rc_cp=$?
error_cp="$(cat "$archivo_stderr")"
rm -f "$archivo_stderr"
if [[ "$rc_cp" -ne 0 ]]; then
    echo "ERROR: no se pudo copiar el dump al contenedor (docker cp salió ${rc_cp}): ${error_cp}" >&2
    exit 8
fi

# ---------------------------------------------------------------------------------------
# Paso 5 -- pg_restore --list: el dump es legible
# ---------------------------------------------------------------------------------------
archivo_stderr="$(mktemp)"
rc_list=0
LISTADO="$(docker exec "$CONTENEDOR" pg_restore --list "$RUTA_EN_CONTENEDOR" 2>"$archivo_stderr")" || rc_list=$?
error_list="$(cat "$archivo_stderr")"
rm -f "$archivo_stderr"
if [[ "$rc_list" -ne 0 ]]; then
    echo "ERROR: 'pg_restore --list' rechazó el dump (código ${rc_list}): está corrupto, truncado" >&2
    echo "       o no es formato custom. La base NO se tocó. stderr: ${error_list}" >&2
    exit 4
fi
# Tablas con entrada TABLE DATA (las tablas sin filas también la tienen).
TABLAS_DEL_DUMP="$(awk '$1 !~ /^;/ && $4 == "TABLE" && $5 == "DATA" && $6 == "public" { print $7 }' <<< "$LISTADO" | LC_ALL=C sort)"
echo "OK: dump legible ($(wc -l <<< "$TABLAS_DEL_DUMP") tablas con datos)."

# ---------------------------------------------------------------------------------------
# Paso 7 -- Restauración en UNA transacción
# ---------------------------------------------------------------------------------------
# --single-transaction: si algo falla, la base queda VACÍA, nunca a medias.
# --no-owner/--no-privileges: los objetos quedan a nombre del rol que restaura; el restore no
# depende de que existan los roles del origen.
echo "Restaurando en '${BASE}' (una sola transacción)..."
archivo_stderr="$(mktemp)"
rc_restore=0
docker exec "$CONTENEDOR" pg_restore --no-owner --no-privileges --single-transaction --exit-on-error \
    -U "$USUARIO" -d "$BASE" "$RUTA_EN_CONTENEDOR" >/dev/null 2>"$archivo_stderr" || rc_restore=$?
error_restore="$(cat "$archivo_stderr")"
rm -f "$archivo_stderr"
if [[ "$rc_restore" -ne 0 ]]; then
    echo "ERROR: falló pg_restore (código ${rc_restore}): la base quedó vacía, se puede reintentar tal cual." >&2
    [[ -z "$error_restore" ]] || printf '       stderr: %s\n' "$error_restore" >&2
    exit 5
fi
echo "OK: restauración completa."

# ---------------------------------------------------------------------------------------
# Paso 11 -- Resumen
# ---------------------------------------------------------------------------------------
listar_conteos "$BASE"
CONTEOS_RESTAURADOS="$SALIDA_CONSULTA"
total_tablas=0
total_filas=0
while IFS='|' read -r _tabla filas; do
    [[ -n "$_tabla" ]] || continue
    total_tablas=$((total_tablas + 1))
    total_filas=$((total_filas + filas))
done <<< "$CONTEOS_RESTAURADOS"

ultima_migracion="(sin __EFMigrationsHistory)"
consultar "$BASE" "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL;" "si existe __EFMigrationsHistory"
if [[ "$SALIDA_CONSULTA" == "t" ]]; then
    consultar "$BASE" "SELECT coalesce(max(\"MigrationId\"), '') FROM \"__EFMigrationsHistory\";" "la última migración"
    ultima_migracion="$SALIDA_CONSULTA"
fi

sha_dump="$(sha256sum "$DUMP")"
sha_dump="${sha_dump%% *}"

echo
echo "== Resumen =="
echo "  dump:              ${DUMP}"
echo "  sha256:            ${sha_dump}"
echo "  tablas:            ${total_tablas}"
echo "  filas en total:    ${total_filas}"
echo "  última migración:  ${ultima_migracion}"
echo
echo "API NO arrancada: el siguiente paso es 'sudo systemctl start stockapp-api' (o el que indique el runbook)."
