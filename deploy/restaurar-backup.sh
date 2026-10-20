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
#   --manifiesto      el que arma volcado-final-vps.sh. Opcional para el script, OBLIGATORIO en
#                     la migración (lo exige el runbook): sin él solo se verifica estructura.
#
# Qué verifica, en orden: API inactiva; contenedor corriendo; sha256 y tamaño contra el
# manifiesto (antes de copiar nada); base vacía; pg_restore --list y sus tablas contra el
# manifiesto; restauración; conteos tabla por tabla y última migración; secuencias (el próximo
# valor de cada una supera el máximo de su columna); duplicados case-insensitive en los 7
# catálogos con Nombre único (la remediación se SUGIERE, nunca se ejecuta).
#
# Códigos de salida (estables):
#   0  OK
#   2  uso/argumentos/manifiesto inválidos
#   3  precondición: API activa, contenedor fuera de patrón/ausente/detenido, base no vacía
#   4  dump inválido: sha256/tamaño ≠ manifiesto, pg_restore --list falla, tablas ≠ manifiesto
#   5  falló pg_restore: la base quedó VACÍA (transacción única), se puede reintentar tal cual
#   6  falló la verificación de conteos, última migración o secuencias (la base QUEDA restaurada)
#   7  duplicados en catálogos con Nombre único (la base QUEDA restaurada)
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

# ---------------------------------------------------------------------------------------
# Manifiesto (formato "manifiesto-migracion v1"): cabecera clave=valor hasta "[conteos]", después
# una línea "tabla<TAB>filas" por tabla. Claves desconocidas se ignoran.
# ---------------------------------------------------------------------------------------
MANIFIESTO_SHA=""
MANIFIESTO_TAMANO=""
MANIFIESTO_ULTIMA_MIGRACION=""
MANIFIESTO_TABLAS=()
declare -A MANIFIESTO_CONTEOS=()
leer_manifiesto() {
    local linea numero=0 en_conteos=0 clave valor
    local regex_conteo=$'^([^\t]+)\t([0-9]+)$'
    [[ -f "$MANIFIESTO" && -r "$MANIFIESTO" ]] || error_uso "el manifiesto '${MANIFIESTO}' no existe o no se puede leer."
    while IFS= read -r linea || [[ -n "$linea" ]]; do
        numero=$((numero + 1))
        linea="${linea%$'\r'}"
        if [[ "$en_conteos" -eq 0 ]]; then
            [[ -z "$linea" || "$linea" == \#* ]] && continue
            if [[ "$linea" == "[conteos]" ]]; then
                en_conteos=1
                continue
            fi
            [[ "$linea" == *=* ]] || error_uso "manifiesto inválido: línea ${numero} '${linea}' (se esperaba clave=valor)."
            clave="${linea%%=*}"
            valor="${linea#*=}"
            case "$clave" in
                sha256) MANIFIESTO_SHA="$valor" ;;
                tamano_bytes) MANIFIESTO_TAMANO="$valor" ;;
                ultima_migracion) MANIFIESTO_ULTIMA_MIGRACION="$valor" ;;
            esac
        else
            [[ -z "$linea" ]] && continue
            [[ "$linea" =~ $regex_conteo ]] || \
                error_uso "manifiesto inválido: línea de conteo ${numero} mal formada: '${linea}' (se esperaba tabla<TAB>filas)."
            [[ -z "${MANIFIESTO_CONTEOS[${BASH_REMATCH[1]}]+x}" ]] || \
                error_uso "manifiesto inválido: la tabla '${BASH_REMATCH[1]}' aparece dos veces."
            MANIFIESTO_TABLAS+=("${BASH_REMATCH[1]}")
            MANIFIESTO_CONTEOS["${BASH_REMATCH[1]}"]="${BASH_REMATCH[2]}"
        fi
    done < "$MANIFIESTO"
    [[ "$en_conteos" -eq 1 ]] || error_uso "manifiesto inválido: falta la línea [conteos]."
    [[ "${#MANIFIESTO_TABLAS[@]}" -gt 0 ]] || error_uso "manifiesto inválido: no tiene conteos."
    [[ "$MANIFIESTO_SHA" =~ ^[0-9a-f]{64}$ ]] || error_uso "manifiesto inválido: sha256 falta o no son 64 hex minúsculas."
    [[ "$MANIFIESTO_TAMANO" =~ ^[0-9]+$ ]] || error_uso "manifiesto inválido: tamano_bytes falta o no es un entero."
    [[ -n "$MANIFIESTO_ULTIMA_MIGRACION" ]] || error_uso "manifiesto inválido: falta ultima_migracion."
}
[[ -z "$MANIFIESTO" ]] || leer_manifiesto

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
# Paso 3 -- Con manifiesto: el dump es el que se volcó (antes de copiar nada)
# ---------------------------------------------------------------------------------------
SHA_DUMP="$(sha256sum "$DUMP")"
SHA_DUMP="${SHA_DUMP%% *}"
TAMANO_DUMP="$(stat -c %s "$DUMP")"
if [[ -n "$MANIFIESTO" ]]; then
    if [[ "$SHA_DUMP" != "$MANIFIESTO_SHA" ]]; then
        echo "ERROR: el sha256 del dump no coincide con el manifiesto. La base NO se tocó." >&2
        echo "       manifiesto: ${MANIFIESTO_SHA}" >&2
        echo "       dump:       ${SHA_DUMP}" >&2
        exit 4
    fi
    if [[ "$TAMANO_DUMP" != "$MANIFIESTO_TAMANO" ]]; then
        echo "ERROR: el tamaño del dump (${TAMANO_DUMP} bytes) no coincide con el manifiesto (${MANIFIESTO_TAMANO}). La base NO se tocó." >&2
        exit 4
    fi
    echo "OK: sha256 y tamaño coinciden con el manifiesto."
fi

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
if [[ -n "$MANIFIESTO" ]]; then
    TABLAS_DEL_MANIFIESTO="$(printf '%s\n' "${MANIFIESTO_TABLAS[@]}" | LC_ALL=C sort)"
    if [[ "$TABLAS_DEL_DUMP" != "$TABLAS_DEL_MANIFIESTO" ]]; then
        {
            echo "ERROR: las tablas del dump (pg_restore --list) no son las del manifiesto. La base NO se tocó."
            echo "       Solo en el dump:"
            LC_ALL=C comm -23 <(printf '%s\n' "$TABLAS_DEL_DUMP") <(printf '%s\n' "$TABLAS_DEL_MANIFIESTO") | sed 's/^/         /'
            echo "       Solo en el manifiesto:"
            LC_ALL=C comm -13 <(printf '%s\n' "$TABLAS_DEL_DUMP") <(printf '%s\n' "$TABLAS_DEL_MANIFIESTO") | sed 's/^/         /'
        } >&2
        exit 4
    fi
fi
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

# A partir de acá la base QUEDA restaurada pase lo que pase: si una verificación falla, se
# informa y decide un humano (reintentar con --recrear-base, que guarda antes un dump de
# seguridad). Las verificaciones 8-10 corren todas, para dar el cuadro completo de una vez.
FALLO_VERIFICACION=0

# ---------------------------------------------------------------------------------------
# Paso 8 -- Conteos tabla por tabla y última migración
# ---------------------------------------------------------------------------------------
echo
echo "== Conteos =="
listar_conteos "$BASE"
CONTEOS_RESTAURADOS="$SALIDA_CONSULTA"
declare -A CONTEOS_REALES=()
total_tablas=0
total_filas=0
while IFS='|' read -r tabla filas; do
    [[ -n "$tabla" ]] || continue
    CONTEOS_REALES["$tabla"]="$filas"
    total_tablas=$((total_tablas + 1))
    total_filas=$((total_filas + filas))
done <<< "$CONTEOS_RESTAURADOS"

ultima_migracion=""
consultar "$BASE" "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL;" "si existe __EFMigrationsHistory"
if [[ "$SALIDA_CONSULTA" == "t" ]]; then
    consultar "$BASE" "SELECT coalesce(max(\"MigrationId\"), '') FROM \"__EFMigrationsHistory\";" "la última migración"
    ultima_migracion="$SALIDA_CONSULTA"
fi

if [[ -n "$MANIFIESTO" ]]; then
    for tabla in "${MANIFIESTO_TABLAS[@]}"; do
        esperado="${MANIFIESTO_CONTEOS[$tabla]}"
        if [[ -z "${CONTEOS_REALES[$tabla]+x}" ]]; then
            echo "  ${tabla}: esperado ${esperado}, no existe en la base restaurada" >&2
            FALLO_VERIFICACION=1
        elif [[ "${CONTEOS_REALES[$tabla]}" == "$esperado" ]]; then
            printf '  %-40s %12s  OK\n' "$tabla" "$esperado"
        else
            echo "  ${tabla}: esperado ${esperado}, real ${CONTEOS_REALES[$tabla]}" >&2
            FALLO_VERIFICACION=1
        fi
    done
    for tabla in "${!CONTEOS_REALES[@]}"; do
        if [[ -z "${MANIFIESTO_CONTEOS[$tabla]+x}" ]]; then
            echo "  ${tabla}: no está en el manifiesto, real ${CONTEOS_REALES[$tabla]}" >&2
            FALLO_VERIFICACION=1
        fi
    done
    if [[ "$ultima_migracion" == "$MANIFIESTO_ULTIMA_MIGRACION" ]]; then
        echo "  última migración: ${ultima_migracion}  OK"
    else
        echo "  última migración: esperada ${MANIFIESTO_ULTIMA_MIGRACION}, real ${ultima_migracion:-(ninguna)}" >&2
        FALLO_VERIFICACION=1
    fi
else
    # Sin manifiesto solo se puede verificar estructura: las tablas restauradas son las del --list.
    TABLAS_RESTAURADAS="$(printf '%s\n' "${!CONTEOS_REALES[@]}" | LC_ALL=C sort)"
    if [[ "$TABLAS_RESTAURADAS" != "$TABLAS_DEL_DUMP" ]]; then
        echo "  las tablas restauradas no son las del pg_restore --list del dump" >&2
        FALLO_VERIFICACION=1
    fi
    while IFS='|' read -r tabla filas; do
        [[ -n "$tabla" ]] || continue
        printf '  %-40s %12s\n' "$tabla" "$filas"
    done <<< "$CONTEOS_RESTAURADOS"
    echo "  (sin manifiesto: los conteos se informan, no se comparan)"
fi

# ---------------------------------------------------------------------------------------
# Paso 9 -- Secuencias: el PRÓXIMO valor de cada una supera el máximo de su columna
# ---------------------------------------------------------------------------------------
# "last_value >= max" no alcanza: con is_called = false el próximo valor ES last_value. Se
# compara el último valor ya entregado (last_value, o last_value - 1 si is_called = false).
# Solo lectura: no se inserta ninguna fila de prueba.
echo
echo "== Secuencias =="
consultar "$BASE" "WITH cols AS (
  SELECT table_name, column_name,
         pg_get_serial_sequence(format('%I.%I', table_schema, table_name), column_name) AS seq
  FROM information_schema.columns WHERE table_schema = 'public')
SELECT table_name, column_name,
  (xpath('/row/v/text()', query_to_xml(format('SELECT coalesce(max(%I),0) AS v FROM public.%I', column_name, table_name), false, true, '')))[1]::text::bigint,
  (xpath('/row/v/text()', query_to_xml(format('SELECT CASE WHEN is_called THEN last_value ELSE last_value - 1 END AS v FROM %s', seq), false, true, '')))[1]::text::bigint
FROM cols WHERE seq IS NOT NULL ORDER BY 1, 2;" "las secuencias"
secuencias_atrasadas=0
while IFS='|' read -r tabla columna maximo entregado; do
    [[ -n "$tabla" ]] || continue
    if [[ "$entregado" -lt "$maximo" ]]; then
        echo "  ${tabla}.${columna}: la secuencia está atrasada (último valor entregado ${entregado}, máximo ${maximo}): el próximo alta chocaría con la PK" >&2
        secuencias_atrasadas=$((secuencias_atrasadas + 1))
    fi
done <<< "$SALIDA_CONSULTA"
if [[ "$secuencias_atrasadas" -eq 0 ]]; then
    echo "  OK: ninguna secuencia atrasada."
else
    FALLO_VERIFICACION=1
fi

# ---------------------------------------------------------------------------------------
# Paso 10 -- Duplicados case-insensitive en los catálogos con Nombre único
# ---------------------------------------------------------------------------------------
# Corre SIEMPRE: si el dump es anterior a la migración AgregaIndiceFuncionalNombreCatalogos,
# esa migración corre en el primer arranque y aborta con duplicados (la unit queda 'failed' y
# bloqueada). Si es posterior, el índice único los hace imposibles y esto pasa trivialmente.
#
# Los 7 catálogos con índice único sobre Nombre. TERCERA copia del mismo array: las otras dos
# son src/StockApp.Infrastructure/Migrations/NormalizacionCatalogosSql.cs y deploy/deploy-vps.sh
# (este script es autocontenido: se copia solo al servidor). MigracionVpsSincroniaTests compara
# las tres; si se agrega o saca un catálogo, hay que tocar las tres.
readonly TABLAS_CON_NOMBRE_UNICO=(
    Categorias Proveedores UnidadesMedida Zonas
    DimensionesTematicas OrganismosResponsables OrigenesFinanciamiento
)
echo
echo "== Catálogos con Nombre único =="
hubo_duplicados=0
remediacion=""
for tabla in "${TABLAS_CON_NOMBRE_UNICO[@]}"; do
    consultar "$BASE" "SELECT to_regclass('public.\"${tabla}\"') IS NOT NULL;" "si existe ${tabla}"
    if [[ "$SALIDA_CONSULTA" == "f" ]]; then
        echo "  ${tabla}: no existe en este dump (anterior al catálogo): nada que verificar."
        continue
    fi
    consultar "$BASE" "SELECT LOWER(\"Nombre\") AS nombre_normalizado, STRING_AGG(\"Id\"::text, ', ' ORDER BY \"Id\") AS ids, COUNT(*) AS cantidad FROM \"${tabla}\" GROUP BY LOWER(\"Nombre\") HAVING COUNT(*) > 1;" "los duplicados de ${tabla}"
    if [[ -z "$SALIDA_CONSULTA" ]]; then
        echo "  ${tabla}: OK"
        continue
    fi
    hubo_duplicados=1
    echo "  ${tabla}: DUPLICADOS" >&2
    while IFS='|' read -r nombre ids _cantidad; do
        [[ -n "$nombre" ]] || continue
        echo "    '${nombre}' (IDs: ${ids})" >&2
        remediacion+="-- Tabla \"${tabla}\", nombre en conflicto: '${nombre}' (IDs: ${ids})"$'\n'
        remediacion+="-- Revisá cada ID a mano en el ABM y renombrá o dá de baja los que"$'\n'
        remediacion+="-- correspondan. Ejemplo de UPDATE sugerido (NO se ejecuta automáticamente):"$'\n'
        remediacion+="UPDATE \"${tabla}\" SET \"Nombre\" = \"Nombre\" || ' (duplicado ' || \"Id\" || ')'"$'\n'
        remediacion+="  WHERE \"Id\" IN (${ids})"$'\n'
        remediacion+="  AND \"Id\" <> (SELECT MIN(\"Id\") FROM \"${tabla}\" WHERE LOWER(\"Nombre\") = '${nombre}');"$'\n\n'
    done <<< "$SALIDA_CONSULTA"
done

# ---------------------------------------------------------------------------------------
# Paso 11 -- Resumen
# ---------------------------------------------------------------------------------------
echo
echo "== Resumen =="
echo "  dump:              ${DUMP}"
echo "  sha256:            ${SHA_DUMP}"
echo "  tablas:            ${total_tablas}"
echo "  filas en total:    ${total_filas}"
echo "  última migración:  ${ultima_migracion:-(sin __EFMigrationsHistory)}"
echo
echo "API NO arrancada: el siguiente paso es 'sudo systemctl start stockapp-api' (o el que indique el runbook)."

if [[ "$FALLO_VERIFICACION" -eq 1 ]]; then
    echo >&2
    echo "ERROR: la verificación de conteos/migración/secuencias FALLÓ (detalle arriba). La base quedó" >&2
    echo "       restaurada pero NO se puede arrancar la API: decide un humano. Para reintentar, usar" >&2
    echo "       --recrear-base ${BASE} (guarda antes un dump de seguridad de lo que haya)." >&2
    exit 6
fi
if [[ "$hubo_duplicados" -eq 1 ]]; then
    {
        echo
        echo "ERROR: hay duplicados case-insensitive en catálogos con Nombre único. La base quedó"
        echo "       restaurada, pero la migración AgregaIndiceFuncionalNombreCatalogos abortaría el"
        echo "       primer arranque. NO arranques la API. La deduplicación es juicio humano sobre"
        echo "       datos del municipio: esta remediación NO se ejecuta, solo se sugiere:"
        echo
        printf '%s' "$remediacion"
    } >&2
    exit 7
fi
