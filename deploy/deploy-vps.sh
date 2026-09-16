#!/usr/bin/env bash
set -euo pipefail

# deploy-vps.sh — orquesta el deploy de StockApp.Api al VPS Linux (Fase 6, plan
# 2026-09-12, líneas ~2805-2880). Corre en la MÁQUINA DEL OPERADOR (no en el VPS) y
# automatiza el procedimiento manual de deploy/DEPLOY.md, secciones 3 y 8 — ese
# procedimiento manual sigue siendo el fallback si este script falla, ver DEPLOY.md.
#
# Uso:
#   deploy/deploy-vps.sh <version> [--dry-run] [--sin-backup]
#
# (Sin --con-soporte: Decisión 10 del plan, un flag sin semántica definida, sacado de la
# firma a propósito — se rechaza explícitamente más abajo, nunca se ignora en silencio.)
#
# ⚠️  EL VPS ES COMPARTIDO: corre "pinar" (otra aplicación) en el mismo host. Todo lo que
# este script toca está acotado POR NOMBRE a los recursos de StockApp (servicio
# stockapp-api, contenedor stockapp-pg, deploy/dist local) — nunca un barrido global.
#
# Los 6 pasos (+ un paso 0 de relevamiento que el plan no pedía, agregado a propósito: un
# VPS compartido sin relevar antes de operar es un riesgo que el plan no cubría):
#   0. Relevamiento READ-ONLY (informativo, no bloqueante salvo disco lleno).
#   1. Pre-vuelo de migraciones (SOLO LECTURA). Duplicados case-insensitive -> ABORTA
#      mostrando una remediación sugerida, NUNCA la ejecuta (Decisión 11).
#   2. Backup con la versión vieja viva + scp de bajada, verificado en tamaño.
#   3. publish-api.sh <version>.
#   4. scp del tarball; install.sh/.service SOLO si cambiaron desde el último deploy.
#   5. ssh + install.sh, con el nombre EXACTO del tarball — nunca un glob
#      (install.sh:33-46 ya exige exactamente 2 argumentos por esto mismo).
#   6. Verificación remota: /licencia/estado + conteo de __EFMigrationsHistory +
#      systemctl is-active.
#
# --dry-run corre SOLO el paso 0 y el paso 1 (ambos de solo lectura) y termina ahí.

VPS_HOST="${VPS_HOST:-194.163.142.86}"
VPS_USER="${VPS_USER:?Definí VPS_USER (usuario SSH del VPS)}"
VPS_SSH_PORT="${VPS_SSH_PORT:-34377}"
VPS_DIR="${VPS_DIR:-~/stockapp-deploy}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# ---------------------------------------------------------------------------------------
# Parseo de argumentos
# ---------------------------------------------------------------------------------------

uso() {
    echo "Uso: $0 <version> [--dry-run] [--sin-backup]" >&2
}

VERSION=""
DRY_RUN=0
SIN_BACKUP=0

if [[ "$#" -eq 0 ]]; then
    uso
    exit 1
fi

for arg in "$@"; do
    case "$arg" in
        --dry-run) DRY_RUN=1 ;;
        --sin-backup) SIN_BACKUP=1 ;;
        --con-soporte)
            # Decisión 10 (RESUELTA 2026-09-13): --con-soporte NUNCA tuvo semántica definida
            # en el diseño. Se rechaza con error explícito -- un flag sin semántica que se
            # ignorara en silencio es peor que uno que no existe: alguien lo usaría suponiendo
            # qué hace.
            echo "ERROR: --con-soporte no está implementado (Decisión 10: flag sin semántica" >&2
            echo "       definida, sacado de la firma a propósito). No se ignora en silencio." >&2
            exit 1
            ;;
        --*)
            echo "ERROR: opción desconocida '${arg}'." >&2
            uso
            exit 1
            ;;
        *)
            if [[ -n "$VERSION" ]]; then
                echo "ERROR: versión ya especificada como '${VERSION}'; argumento inesperado '${arg}'." >&2
                exit 1
            fi
            VERSION="$arg"
            ;;
    esac
done

if [[ -z "$VERSION" ]]; then
    uso
    exit 1
fi

# ---------------------------------------------------------------------------------------
# Helpers de conexión y secretos locales
# ---------------------------------------------------------------------------------------

ssh_vps() {
    ssh -p "$VPS_SSH_PORT" -o BatchMode=yes "${VPS_USER}@${VPS_HOST}" "$@"
}

scp_vps() {
    scp -P "$VPS_SSH_PORT" -o BatchMode=yes "$@"
}

# ssh_vps_autenticado <token> <comando-curl-SIN-el--H-de-Authorization>
#
# HALLAZGO DE SEGURIDAD (fix aplicado): el header 'Authorization: Bearer <token>' viaja por
# la ENTRADA ESTÁNDAR del curl remoto ('curl -K -', config leído por stdin con una
# directiva 'header = "..."'), NUNCA como argumento. En un VPS COMPARTIDO,
# /proc/<pid>/cmdline es legible por CUALQUIER usuario del sistema (no podemos asumir
# hidepid=2 activado en un servidor que no administramos) -- un argv con el JWT de admin
# sería una fuga de credenciales hacia cualquier otro tenant del servidor (recordatorio:
# 'pinar', la otra app, corre en este mismo host). Mismo criterio para la contraseña del
# login más abajo ('curl --data @-').
ssh_vps_autenticado() {
    local token="$1" cmd="$2"
    printf 'header = "Authorization: Bearer %s"\n' "$token" | ssh_vps "${cmd} -K -"
}

ENV_LOCAL="${REPO_ROOT}/deploy/.env"
if [[ -f "$ENV_LOCAL" ]]; then
    set -a
    # shellcheck source=/dev/null
    source "$ENV_LOCAL"
    set +a
fi

POSTGRES_USER="${POSTGRES_USER:-stockapp}"
POSTGRES_DB="${POSTGRES_DB:-stockapp}"
API_PORT="${API_PORT:-5080}"

# Credenciales para autenticar el backup del paso 2. Con override explícito porque
# BOOTSTRAP_PASSWORD (de deploy/.env) solo sirve para el PRIMER arranque -- DEPLOY.md pide
# cambiarla desde el desktop apenas se activa la licencia, así que en una instalación viva
# probablemente ya no sea la real.
VPS_ADMIN_USER="${VPS_ADMIN_USER:-${BOOTSTRAP_ADMIN_USER:-admin}}"
VPS_ADMIN_PASSWORD="${VPS_ADMIN_PASSWORD:-${BOOTSTRAP_PASSWORD:-}}"

# Los 7 catálogos con índice único sobre Nombre (comparación case-insensitive) — mismo
# array que src/StockApp.Infrastructure/Migrations/NormalizacionCatalogosSql.cs. Si ese
# archivo agrega/saca un catálogo, este array tiene que actualizarse a mano (no hay forma
# de compartir la fuente entre C# y bash sin un tercer artefacto).
readonly TABLAS_CON_NOMBRE_UNICO=(
    Categorias Proveedores UnidadesMedida Zonas
    DimensionesTematicas OrganismosResponsables OrigenesFinanciamiento
)

MARCADOR_ULTIMO_DEPLOY="${REPO_ROOT}/deploy/dist/.ultimo-deploy-vps-commit"

# ---------------------------------------------------------------------------------------
# Paso 0 — Relevamiento READ-ONLY (AGREGADO, el plan no lo pedía)
# ---------------------------------------------------------------------------------------
#
# Por qué existe: el plan de la Fase 6 nunca releva el servidor antes de operar. Con un VPS
# COMPARTIDO ("pinar" corre en el mismo host) eso es negligente -- este paso muestra qué hay
# antes de tocar nada, para no confundir un contenedor/puerto ajeno con uno propio. Es
# informativo y NO bloqueante, salvo un problema real que impida el deploy (disco lleno).
paso0_relevamiento() {
    echo "== Paso 0: Relevamiento READ-ONLY del VPS =="
    echo "  ⚠️  RECORDATORIO: este VPS es COMPARTIDO. Corre 'pinar' (otra aplicación) en el"
    echo "  mismo host -- todo lo que sigue está acotado por nombre a los recursos de"
    echo "  StockApp (servicio stockapp-api, contenedor stockapp-pg). Nunca un barrido global."
    echo
    echo "  Contenedores corriendo (docker ps) -- identificá cuáles son de StockApp"
    echo "  ('stockapp-pg') y cuáles NO (todo lo demás es de 'pinar' u otra cosa):"
    ssh_vps "docker ps --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}'" || \
        echo "  (no se pudo consultar docker ps)"
    echo
    echo "  Estado del servicio stockapp-api:"
    ssh_vps "systemctl is-active stockapp-api" || echo "  (no se pudo consultar systemctl)"
    echo
    echo "  Puertos relevantes (80,443,5432,5433,${API_PORT}) y quién los ocupa:"
    ssh_vps "sudo ss -ltnp 2>/dev/null | grep -E ':80 |:443 |:5432 |:5433 |:${API_PORT} '" || \
        echo "  (ninguno ocupado, o no se pudo consultar)"
    echo
    echo "  Espacio en disco:"
    ssh_vps "df -h /" || echo "  (no se pudo consultar df)"
    echo

    # Único chequeo bloqueante de este paso: disco realmente lleno. '|| true' en cada eslabón
    # del pipe (gotcha conocido del proyecto: un pipe bajo 'set -o pipefail' aborta el script
    # entero si CUALQUIER comando del medio sale ≠0, no solo el último).
    local porcentaje
    porcentaje="$(ssh_vps "df -h / --output=pcent 2>/dev/null | tail -n1" 2>/dev/null | tr -dc '0-9' || true)"
    if [[ -n "$porcentaje" ]] && [[ "$porcentaje" =~ ^[0-9]+$ ]] && [[ "$porcentaje" -ge 95 ]]; then
        echo "ERROR: el disco del VPS está al ${porcentaje}% -- ABORTO antes de tocar nada." >&2
        echo "       Es el único disco compartido con 'pinar': liberá espacio antes de reintentar." >&2
        exit 1
    fi
    echo "  OK. Relevamiento completo (informativo, no bloqueó nada)."
}

# ---------------------------------------------------------------------------------------
# Paso 1 — Pre-vuelo de migraciones (SOLO LECTURA)
# ---------------------------------------------------------------------------------------
paso1_prevuelo_migraciones() {
    echo
    echo "== Paso 1: Pre-vuelo de migraciones (SOLO LECTURA) =="
    echo "  Verificando duplicados case-insensitive en los 7 catálogos con Nombre único"
    echo "  (mismo guardián que la migración AgregaIndiceFuncionalNombreCatalogos -- ver"
    echo "  src/StockApp.Infrastructure/Migrations/NormalizacionCatalogosSql.cs)."
    echo "  AVISO: 'docker exec' + 'psql -h 127.0.0.1' puede saltear la autenticación por la"
    echo "  regla 'trust' del propio contenedor. Para este pre-vuelo no importa (solo lee),"
    echo "  pero NO uses este éxito como prueba de que las credenciales del .env son correctas."

    local hubo_duplicados=0
    local remediacion=""

    for tabla in "${TABLAS_CON_NOMBRE_UNICO[@]}"; do
        local sql
        sql=$(cat <<'SQL'
SELECT LOWER("Nombre") AS nombre_normalizado, STRING_AGG("Id"::text, ', ' ORDER BY "Id") AS ids, COUNT(*) AS cantidad FROM "TABLA_PLACEHOLDER" GROUP BY LOWER("Nombre") HAVING COUNT(*) > 1;
SQL
)
        sql="${sql//TABLA_PLACEHOLDER/$tabla}"

        local cmd_remoto
        cmd_remoto="docker exec stockapp-pg psql -U \"${POSTGRES_USER}\" -d \"${POSTGRES_DB}\" -h 127.0.0.1 -tAc '${sql}'"

        local salida
        salida="$(ssh_vps "$cmd_remoto" || true)"

        if [[ -n "$salida" ]]; then
            hubo_duplicados=1
            echo "  DUPLICADOS en '${tabla}':"
            while IFS= read -r linea_salida; do
                echo "    ${linea_salida}"
            done <<< "$salida"
            while IFS='|' read -r nombre ids _cantidad; do
                [[ -z "$nombre" ]] && continue
                remediacion+="-- Tabla \"${tabla}\", nombre en conflicto: '${nombre}' (IDs: ${ids})"$'\n'
                remediacion+="-- Revisá cada ID a mano en el ABM y renombrá o dá de baja los que"$'\n'
                remediacion+="-- correspondan. Ejemplo de UPDATE sugerido (NO se ejecuta automáticamente):"$'\n'
                remediacion+="UPDATE \"${tabla}\" SET \"Nombre\" = \"Nombre\" || ' (duplicado ' || \"Id\" || ')'"$'\n'
                remediacion+="  WHERE \"Id\" IN (${ids})"$'\n'
                remediacion+="  AND \"Id\" <> (SELECT MIN(\"Id\") FROM \"${tabla}\" WHERE LOWER(\"Nombre\") = '${nombre}');"$'\n\n'
            done <<< "$salida"
        fi
    done

    if [[ "$hubo_duplicados" -eq 1 ]]; then
        echo
        echo "ERROR: se encontraron duplicados case-insensitive en catálogos con Nombre único." >&2
        echo "       La migración AgregaIndiceFuncionalNombreCatalogos hace RAISE EXCEPTION y" >&2
        echo "       aborta TODA la migración con estos datos. MigrateAsync() corre antes de" >&2
        echo "       app.Run() sin try/catch, y tras 5 arranques fallidos en 10 minutos systemd" >&2
        echo "       deja la unit en 'failed' y rechaza 'start' por 600s (StartLimitIntervalSec)." >&2
        echo "       Sin este pre-vuelo: deploy a medias + servicio caído + 10 min bloqueado." >&2
        echo >&2
        echo "       ABORTO el deploy. La deduplicación de nombres es juicio humano sobre datos" >&2
        echo "       del municipio -- este script NUNCA ejecuta la remediación, solo la sugiere:" >&2
        echo >&2
        echo "$remediacion" >&2
        exit 1
    fi

    echo "  OK. Sin duplicados en los 7 catálogos."
}

# ---------------------------------------------------------------------------------------
# Paso 2 — Backup con la versión vieja viva
# ---------------------------------------------------------------------------------------
paso2_backup() {
    if [[ "$SIN_BACKUP" -eq 1 ]]; then
        echo
        echo "  ################################################################################"
        echo "  ## ADVERTENCIA: --sin-backup -- SALTEANDO el backup pre-deploy.               ##"
        echo "  ## Un script que por omisión deja sin punto de retorno está mal diseñado; por  ##"
        echo "  ## eso el backup es el comportamiento DEFAULT. Estás asumiendo el riesgo de    ##"
        echo "  ## no poder volver atrás si el deploy sale mal.                                ##"
        echo "  ################################################################################"
        echo
        return 0
    fi

    echo
    echo "== Paso 2: Backup con la versión vieja viva =="
    echo "  (El backup vive DENTRO de la API -- va ANTES de tocar nada: si la API no levanta"
    echo "  después del deploy, no hay backup nuevo posible.)"

    if [[ -z "$VPS_ADMIN_PASSWORD" ]]; then
        echo "ERROR: no se pudo determinar la contraseña de admin para autenticar el backup" >&2
        echo "       (ni VPS_ADMIN_PASSWORD ni BOOTSTRAP_PASSWORD en deploy/.env)." >&2
        exit 1
    fi

    # La contraseña del admin viaja por la ENTRADA ESTÁNDAR del curl remoto ('--data @-'),
    # NUNCA como argumento -- mismo motivo que ssh_vps_autenticado más arriba: un argv con
    # la contraseña sería legible por cualquier usuario del sistema en este VPS compartido.
    local login_body token
    login_body="$(printf '{"nombreUsuario":"%s","contrasena":"%s"}' "$VPS_ADMIN_USER" "$VPS_ADMIN_PASSWORD")"
    token="$(printf '%s' "$login_body" \
        | ssh_vps "curl -fsS -X POST http://127.0.0.1:${API_PORT}/auth/login -H 'Content-Type: application/json' --data @-" \
        | sed -n 's/.*"token":"\([^"]*\)".*/\1/p' || true)"

    if [[ -z "$token" ]]; then
        echo "ERROR: no se pudo autenticar contra la API del VPS para disparar el backup." >&2
        exit 1
    fi

    # POST /backups (Api/Endpoints/BackupsEndpoints.cs:49-59) responde 202 Accepted SIN body --
    # a propósito (fire-and-forget: DisparadorBackupManual corre en background, EjecutorPgDumpProceso
    # puede tardar hasta 30 min). No hay ningún id que capturar de la respuesta del POST.
    #
    # Por eso la corrida se correlaciona por TIEMPO: 'momento_disparo_epoch' se captura ANTES de
    # disparar el POST, y de GET /backups (CorridaBackupDto: campos "resultado"/"finalizadaEn",
    # camelCase por default de ConfigureHttpJsonOptions -- NUNCA "estado"/"Exitoso", que no
    # existen en el contrato real) sólo se acepta una corrida "Exitosa" cuya "finalizadaEn" sea
    # POSTERIOR a ese momento. "head -1" sobre la lista completa (el código viejo) confiaba en que
    # el servidor la devolviera más-nuevo-primero -- con BackupProgramadoService corriendo backups
    # automáticos en paralelo, esa asunción de orden no es una garantía de la que dependa el punto
    # de retorno del deploy.
    #
    # Si hay MÁS de una corrida Exitosa posterior al disparo (ej. el job automático corrió en la
    # misma ventana), es AMBIGUO -- se ABORTA en vez de elegir en silencio (mismo criterio que
    # Decisión 11 y armar-kit.sh).
    local momento_disparo_epoch
    momento_disparo_epoch="$(date -u +%s)"

    echo "  Disparando backup manual (POST /backups)..."
    ssh_vps_autenticado "$token" "curl -fsS -X POST http://127.0.0.1:${API_PORT}/backups" >/dev/null

    echo "  Esperando a que termine..."
    local backup_id=""
    # shellcheck disable=SC2034  # 'intento' solo cuenta iteraciones, no se usa en el cuerpo.
    local -i intento
    for ((intento = 1; intento <= 30; intento++)); do
        local lista
        lista="$(ssh_vps_autenticado "$token" "curl -fsS http://127.0.0.1:${API_PORT}/backups" || true)"

        local objetos
        objetos="$(printf '%s' "$lista" | grep -o '{"id":[0-9]\+,"finalizadaEn":"[^"]*","resultado":"[^"]*"[^}]*}' || true)"

        local -a candidatos=()
        local obj
        while IFS= read -r obj; do
            [[ -z "$obj" ]] && continue
            local id_obj resultado_obj finalizada_obj finalizada_epoch
            id_obj="$(printf '%s' "$obj" | sed -n 's/.*"id":\([0-9]*\).*/\1/p')"
            resultado_obj="$(printf '%s' "$obj" | sed -n 's/.*"resultado":"\([^"]*\)".*/\1/p')"
            [[ "$resultado_obj" != "Exitosa" ]] && continue
            finalizada_obj="$(printf '%s' "$obj" | sed -n 's/.*"finalizadaEn":"\([^"]*\)".*/\1/p')"
            finalizada_epoch="$(date -u -d "$finalizada_obj" +%s 2>/dev/null || true)"
            [[ -z "$finalizada_epoch" ]] && continue
            [[ "$finalizada_epoch" -ge "$momento_disparo_epoch" ]] && candidatos+=("$id_obj")
        done <<< "$objetos"

        if [[ "${#candidatos[@]}" -gt 1 ]]; then
            echo "ERROR: hay ${#candidatos[@]} corridas de backup Exitosas posteriores al disparo de" >&2
            echo "       este deploy (ids: ${candidatos[*]}) -- AMBIGUO, no se elige en silencio." >&2
            echo "       Probablemente el backup programado (BackupProgramadoService) corrió en la" >&2
            echo "       misma ventana que este disparo manual. Esperá a que termine o revisá" >&2
            echo "       GET /backups a mano para decidir cuál es el punto de retorno correcto." >&2
            exit 1
        fi

        if [[ "${#candidatos[@]}" -eq 1 ]]; then
            backup_id="${candidatos[0]}"
            break
        fi

        sleep 2
    done

    if [[ -z "$backup_id" ]]; then
        echo "ERROR: el backup manual no terminó (o no terminó exitoso) tras esperar." >&2
        exit 1
    fi

    local ruta_remota="/tmp/stockapp-backup-pre-deploy-${VERSION}.bin"
    ssh_vps_autenticado "$token" "curl -fsS -o '${ruta_remota}' http://127.0.0.1:${API_PORT}/backups/${backup_id}/contenido"

    local dir_backups_local="${REPO_ROOT}/deploy/dist/backups"
    mkdir -p "$dir_backups_local"
    local sello_tiempo
    sello_tiempo="$(date -u +%Y%m%d%H%M%S)"
    local ruta_local="${dir_backups_local}/pre-deploy-${VERSION}-${sello_tiempo}.bin"

    echo "  Bajando backup a '${ruta_local}'..."
    scp_vps "${VPS_USER}@${VPS_HOST}:${ruta_remota}" "$ruta_local"

    # No confiamos en el exit code de scp: verificamos el archivo local, existe y pesa algo
    # razonable. Un backup de 0 bytes es peor que no tener backup -- parece que hay uno.
    if [[ ! -f "$ruta_local" ]]; then
        echo "ERROR: scp no dejó el archivo de backup en '${ruta_local}'." >&2
        exit 1
    fi
    local tamano
    tamano="$(stat -c%s "$ruta_local" 2>/dev/null || echo 0)"
    if [[ "$tamano" -lt 1024 ]]; then
        echo "ERROR: el backup bajado ('${ruta_local}') pesa ${tamano} bytes -- sospechosamente" >&2
        echo "       chico o vacío. ABORTO: un backup vacío es peor que no tener backup." >&2
        exit 1
    fi

    echo "  OK. Backup verificado: ${ruta_local} (${tamano} bytes)."
}

# ---------------------------------------------------------------------------------------
# Paso 3 — publish-api.sh
# ---------------------------------------------------------------------------------------
paso3_publish() {
    echo >&2
    echo "== Paso 3: Publicando StockApp.Api (${VERSION}) ==" >&2
    local salida
    salida="$("${REPO_ROOT}/deploy/publish-api.sh" "$VERSION")"
    echo "$salida" >&2

    local tarball
    tarball="$(printf '%s\n' "$salida" | sed -n 's/^\[publish-api\] OK: //p' | head -1)"
    if [[ -z "$tarball" ]] || [[ ! -f "$tarball" ]]; then
        echo "ERROR: no se pudo determinar la ruta del tarball publicado." >&2
        exit 1
    fi
    printf '%s' "$tarball"
}

# ---------------------------------------------------------------------------------------
# Paso 4 — Copiar artefactos al VPS
# ---------------------------------------------------------------------------------------
paso4_copiar_artefactos() {
    local tarball_local="$1"
    local nombre_tarball
    nombre_tarball="$(basename "$tarball_local")"

    echo
    echo "== Paso 4: Copiando artefactos al VPS =="
    ssh_vps "mkdir -p '${VPS_DIR}'"

    echo "  Copiando ${nombre_tarball}..."
    scp_vps "$tarball_local" "${VPS_USER}@${VPS_HOST}:${VPS_DIR}/"

    local copiar_install=1
    if [[ -f "$MARCADOR_ULTIMO_DEPLOY" ]]; then
        local commit_anterior
        commit_anterior="$(cat "$MARCADOR_ULTIMO_DEPLOY")"
        if git -C "$REPO_ROOT" rev-parse --quiet --verify "${commit_anterior}^{commit}" >/dev/null 2>&1 && \
           git -C "$REPO_ROOT" diff --quiet "${commit_anterior}" HEAD -- deploy/install.sh deploy/stockapp-api.service 2>/dev/null; then
            copiar_install=0
        fi
    fi

    if [[ "$copiar_install" -eq 1 ]]; then
        echo "  install.sh/stockapp-api.service cambiaron (o es el primer deploy conocido) -- copiando."
        scp_vps "${REPO_ROOT}/deploy/install.sh" "${REPO_ROOT}/deploy/stockapp-api.service" \
            "${REPO_ROOT}/deploy/wait-for-postgres.sh" "${VPS_USER}@${VPS_HOST}:${VPS_DIR}/"
    else
        echo "  install.sh/stockapp-api.service sin cambios desde el último deploy -- no se copian de nuevo."
    fi

    printf '%s' "$nombre_tarball"
}

# ---------------------------------------------------------------------------------------
# Paso 5 — ssh + install.sh (nombre exacto del tarball, NUNCA un glob)
# ---------------------------------------------------------------------------------------
paso5_instalar() {
    local nombre_tarball="$1"

    echo
    echo "== Paso 5: Instalando en el VPS =="
    # install.sh:33-46 exige EXACTAMENTE 2 argumentos a propósito -- un glob ambiguo contra
    # un deploy/dist/ con más de un tarball expandiría a más de 2 argumentos. Por eso acá se
    # pasa el NOMBRE EXACTO (variable ya resuelta), nunca un patrón.
    ssh_vps "cd '${VPS_DIR}' && sudo ./install.sh '${nombre_tarball}' .env"
}

# ---------------------------------------------------------------------------------------
# Paso 6 — Verificación remota
# ---------------------------------------------------------------------------------------
paso6_verificar() {
    echo
    echo "== Paso 6: Verificación remota =="

    echo "  /licencia/estado:"
    ssh_vps "curl -fsS http://127.0.0.1:${API_PORT}/licencia/estado"
    echo

    local conteo_migraciones
    conteo_migraciones="$(ssh_vps "docker exec stockapp-pg psql -U \"${POSTGRES_USER}\" -d \"${POSTGRES_DB}\" -h 127.0.0.1 -tAc 'SELECT COUNT(*) FROM \"__EFMigrationsHistory\"'" || true)"
    echo "  Migraciones aplicadas: ${conteo_migraciones}"

    local estado_servicio
    estado_servicio="$(ssh_vps "systemctl is-active stockapp-api" || true)"
    echo "  Estado de stockapp-api: ${estado_servicio}"

    if [[ "$estado_servicio" != "active" ]]; then
        echo "ERROR: stockapp-api no quedó activo tras el deploy (estado: '${estado_servicio}')." >&2
        echo "       Ver deploy/DEPLOY.md, sección 7 (Troubleshooting) antes de un rollback." >&2
        exit 1
    fi

    mkdir -p "$(dirname "$MARCADOR_ULTIMO_DEPLOY")"
    git -C "$REPO_ROOT" rev-parse HEAD > "$MARCADOR_ULTIMO_DEPLOY"

    echo
    echo "OK: deploy de ${VERSION} verificado en ${VPS_HOST}."
}

# ---------------------------------------------------------------------------------------
# Orquestación
# ---------------------------------------------------------------------------------------

echo "################################################################################"
echo "# ⚠️  VPS COMPARTIDO: ${VPS_HOST} corre 'pinar' (otra aplicación) además de     #"
echo "# StockApp. Todo lo que sigue está acotado por nombre a los recursos propios.  #"
echo "################################################################################"

paso0_relevamiento
paso1_prevuelo_migraciones

if [[ "$DRY_RUN" -eq 1 ]]; then
    echo
    echo "== --dry-run: paso 1 (pre-vuelo) completado sin duplicados. No se ejecuta nada más. =="
    exit 0
fi

paso2_backup
TARBALL_LOCAL="$(paso3_publish)"
NOMBRE_TARBALL="$(paso4_copiar_artefactos "$TARBALL_LOCAL")"
paso5_instalar "$NOMBRE_TARBALL"
paso6_verificar
