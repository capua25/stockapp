#!/usr/bin/env bash
set -euo pipefail

# publicar-release.sh -- sube una versión empaquetada por build/pack-win.ps1 al feed de
# actualizaciones propio (el que sirve StockApp.Api en /updates, carpeta del servidor
# /var/lib/stockapp/updates, dueño stockapp:stockapp, 750). Corre en la MÁQUINA DEL OPERADOR.
#
# Uso:
#   build/publicar-release.sh <version> [--origen <dir>] [--forzar]
#
#   --origen  carpeta con lo que generó pack-win.ps1 (default: <repo>/releases/win). Sirve
#             cuando el empaquetado corrió en otro checkout (p. ej. el de Windows) y se copió
#             la carpeta acá.
#   --forzar  permite publicar una versión MENOR que la que sirve hoy el servidor. Es una
#             salida de emergencia, no el camino normal de un rollback (ver más abajo).
#
# STOCKAPP_ENV_LOCAL (opcional): ruta del .env del operador a cargar en lugar de deploy/.env.
# Con dos VPS en juego: STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo build/publicar-release.sh ...
# Como el .env se carga con 'source' DESPUÉS del entorno, exportar VPS_HOST no alcanza. Si está
# definida y el archivo no existe o no se puede leer, ABORTA (exit 1) antes de cualquier ssh/scp.
#
# Configuración (deploy/.env local o entorno). OBLIGATORIAS, sin default a propósito: un
# host por defecto subiría el feed al servidor equivocado en silencio.
#   VPS_HOST, VPS_USER, VPS_SSH_PORT
# Opcionales: VPS_DIR (default ~/stockapp-deploy, carpeta de staging del usuario SSH),
#   API_SCHEME (http|https), API_HOSTNAME, API_PORT (default 8080) -- igual que deploy-vps.sh.
#   FEED_VERSIONES_MAX (default 5, entero positivo): cuántas versiones (las MAYORES por
#   sort -V) conserva el releases.win.json que se publica; ver "Poda del feed" abajo.
#
# ⚠️  EL VPS PUEDE SER COMPARTIDO: todo lo remoto está acotado por nombre a las rutas de
# StockApp (/var/lib/stockapp/updates y <VPS_DIR>/updates-staging). Nunca se toca nginx ni
# se reinicia la API. Se asume sudo sin contraseña para el usuario SSH, como deploy-vps.sh.
#
# Pasos, en este orden:
#   1. Validación LOCAL con jq: <version> es el Full de mayor versión del releases.win.json,
#      poda del feed (ver abajo) y, sobre el feed PODADO, cada FileName (nombre seguro,
#      existe en --origen con su Size).
#   2. Relevamiento del servidor (nombres + tamaños + sha256) y lectura del releases.win.json
#      vigente. Salida vacía = primera publicación; ssh que falla = "no pude relevar" ->
#      aborta sin subir nada. Si la versión nueva es MENOR que la que sirve el servidor,
#      aborta (salvo --forzar).
#   3. Un nombre que el servidor ya tiene con OTRO contenido (sha256 distinto al del feed
#      nuevo) aborta: nunca se pisa en el lugar un paquete que el feed vigente describe con
#      otro hash. Luego scp al staging de los .nupkg que el servidor no tiene, y del JSON.
#   4. sudo install de los paquetes en la carpeta del feed.
#   5. Re-relevamiento: CADA FileName del JSON nuevo tiene que estar con su Size y su SHA256.
#      Si no, aborta SIN tocar el feed vigente.
#   6. El feed (releases.win.json) va ÚLTIMO, con rename atómico: nunca hay un feed que
#      apunte a paquetes que todavía no están.
#   7. Verificación final por HTTP: el feed publicado dice <version> y el full responde 200
#      con su Content-Length.
#
# Poda del feed: el releases.win.json crece con las notas de cada versión. Se publica una
# copia PODADA (en un temporal; el archivo de --origen NO se modifica) que conserva solo los
# assets (Full y Delta) de las FEED_VERSIONES_MAX versiones mayores (sort -V; con
# prerelease el orden es el de sort -V, no el de SemVer). Es NO destructiva: nunca se borra
# ningún archivo del servidor, los paquetes viejos siguen ahí. Todo lo posterior (relevamiento,
# verificación de tamaño+sha256 del paso 5, subida) opera sobre el feed podado. Un cliente
# más viejo que la ventana no tiene delta hacia la versión actual y baja el último Full.
#
# Excepción -- críticas viejas: además de la ventana, se CONSERVA SIEMPRE la entrada Full de
# toda versión más vieja cuyas NotesMarkdown tengan por primera línea `severity: critical`
# (los Delta de esas versiones sí se podan). Motivo: VelopackUpdateService calcula la
# severidad efectiva como el MÁXIMO entre las releases que hay entre la instalada y la
# destino, leídas del feed; sin esa entrada un cliente más de N versiones atrasado perdería
# la marca crítica. El parseo espeja a SeverityParser (primera línea, `severity:` sin
# distinguir mayúsculas, valor recortado; ausente o inválido = no crítica). Ese Full viejo
# ya está en el servidor de publicaciones anteriores, así que NO se exige en --origen: si
# falta ahí, tiene que estar en el servidor con el tamaño y el sha256 del feed (si no, aborta
# sin subir nada). Si está en --origen se valida como cualquier otro paquete.
#
# Retirar una versión mala: Velopack no hace downgrade. Se republica la última versión BUENA
# con un número MAYOR (empaquetar de nuevo desde ese código con una versión nueva). Por eso
# publicar una versión menor que la servida aborta: --forzar existe solo como escape de
# emergencia (p. ej. el feed vigente quedó apuntando a algo irrecuperable), no para el rollback.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

uso() {
    echo "Uso: $0 <version> [--origen <dir>] [--forzar]" >&2
}

VERSION=""
ORIGEN="${REPO_ROOT}/releases/win"
FORZAR=0

while [[ "$#" -gt 0 ]]; do
    case "$1" in
        --origen)
            if [[ "$#" -lt 2 || -z "$2" ]]; then
                echo "ERROR: --origen necesita una carpeta." >&2
                uso
                exit 1
            fi
            ORIGEN="$2"
            shift 2
            ;;
        --forzar)
            FORZAR=1
            shift
            ;;
        --*)
            echo "ERROR: opción desconocida '$1'." >&2
            uso
            exit 1
            ;;
        *)
            if [[ -n "$VERSION" ]]; then
                echo "ERROR: versión ya especificada como '${VERSION}'; argumento inesperado '$1'." >&2
                exit 1
            fi
            VERSION="$1"
            shift
            ;;
    esac
done

if [[ -z "$VERSION" ]]; then
    uso
    exit 1
fi
if [[ ! "$VERSION" =~ ^[0-9A-Za-z.+-]+$ ]]; then
    echo "ERROR: la versión '${VERSION}' tiene caracteres no válidos." >&2
    exit 1
fi

# deploy/.env se carga ANTES de exigir las variables: VPS_HOST/VPS_SSH_PORT pueden venir de ahí.
ENV_LOCAL="${STOCKAPP_ENV_LOCAL:-${REPO_ROOT}/deploy/.env}"
if [[ -n "${STOCKAPP_ENV_LOCAL:-}" && ( ! -f "$ENV_LOCAL" || ! -r "$ENV_LOCAL" ) ]]; then
    echo "ERROR: STOCKAPP_ENV_LOCAL apunta a '${ENV_LOCAL}', que no existe o no se puede leer." >&2
    exit 1
fi
if [[ -f "$ENV_LOCAL" ]]; then
    set -a
    # shellcheck source=/dev/null
    source "$ENV_LOCAL"
    set +a
fi

VPS_HOST="${VPS_HOST:?Definí VPS_HOST (host del VPS) en deploy/.env o en el entorno}"
VPS_USER="${VPS_USER:?Definí VPS_USER (usuario SSH del VPS) en deploy/.env o en el entorno}"
VPS_SSH_PORT="${VPS_SSH_PORT:?Definí VPS_SSH_PORT (puerto SSH del VPS) en deploy/.env o en el entorno}"
# El "~" queda literal a propósito (ver deploy-vps.sh): lo expande el shell/sftp REMOTO.
VPS_DIR="${VPS_DIR:-~/stockapp-deploy}"
if [[ ! "$VPS_DIR" =~ ^[A-Za-z0-9._~/-]+$ ]]; then
    echo "ERROR: VPS_DIR ('${VPS_DIR}') tiene caracteres no válidos (se usa en comandos remotos)." >&2
    exit 1
fi

FEED_VERSIONES_MAX="${FEED_VERSIONES_MAX:-5}"
if [[ ! "$FEED_VERSIONES_MAX" =~ ^[1-9][0-9]*$ ]]; then
    echo "ERROR: FEED_VERSIONES_MAX ('${FEED_VERSIONES_MAX}') no es válido -- debe ser un entero positivo." >&2
    exit 1
fi

API_PORT="${API_PORT:-8080}"
if [[ -z "${API_SCHEME+x}" ]]; then
    API_SCHEME="http"
fi
if [[ "$API_SCHEME" != "http" && "$API_SCHEME" != "https" ]]; then
    echo "ERROR: API_SCHEME ('${API_SCHEME}') no es válido -- debe ser exactamente 'http' o 'https'." >&2
    exit 1
fi
if [[ "$API_SCHEME" == "https" ]] && [[ -z "${API_HOSTNAME:-}" ]]; then
    echo "ERROR: falta API_HOSTNAME en '${ENV_LOCAL}' (obligatoria porque API_SCHEME=https)." >&2
    exit 1
fi

CURL_RESOLVE_REMOTO=""
if [[ "$API_SCHEME" == "https" ]]; then
    API_URL_BASE="https://${API_HOSTNAME}:${API_PORT}"
    CURL_RESOLVE_REMOTO="--resolve ${API_HOSTNAME}:${API_PORT}:127.0.0.1 "
else
    API_URL_BASE="http://127.0.0.1:${API_PORT}"
fi

if ! command -v jq >/dev/null 2>&1; then
    echo "ERROR: falta 'jq' (necesario para validar releases.win.json). Instalalo e intentá de nuevo." >&2
    exit 1
fi

DIR_FEED="/var/lib/stockapp/updates"
STAGING="${VPS_DIR}/updates-staging"
JSON_NOMBRE="releases.win.json"

ssh_vps() {
    ssh -p "$VPS_SSH_PORT" -o BatchMode=yes "${VPS_USER}@${VPS_HOST}" "$@"
}

scp_vps() {
    scp -P "$VPS_SSH_PORT" -o BatchMode=yes "$@"
}

# El staging remoto se borra pase lo que pase (trap EXIT, también en falla). Si el borrado
# falla se avisa, pero no se pisa el código de salida del script.
STAGING_TOCADO=0
DIR_PODADO=""
limpiar_staging_remoto() {
    if [[ -n "$DIR_PODADO" ]]; then
        rm -rf -- "$DIR_PODADO"
    fi
    if [[ "$STAGING_TOCADO" -eq 1 ]]; then
        if ! ssh_vps "rm -rf -- ${STAGING}"; then
            echo "AVISO: no se pudo borrar el staging remoto '${STAGING}'; borralo a mano." >&2
        fi
    fi
}
trap limpiar_staging_remoto EXIT

fallar() {
    echo "ERROR: $*" >&2
    exit 1
}

# ---------------------------------------------------------------------------------------
# Paso 1 -- validación local
# ---------------------------------------------------------------------------------------

NOMBRES=()
TAMANOS=()
HASHES=()
# Críticas viejas conservadas fuera de la ventana que NO están en --origen: deben estar en el servidor.
SOLO_SERVIDOR=()
FULL_NUEVO=""
FULL_NUEVO_TAMANO=""
# Feed que se publica de verdad: copia podada del de --origen (que no se toca).
JSON_PUBLICAR=""

# Espeja SeverityParser.Parse == Critical: primera línea (split por \n, trim), empieza con
# "severity:" sin distinguir mayúsculas y el valor recortado es "critical". Acepta .NotesMarkdown
# ausente/null/vacío (= no crítica).
JQ_CRITICA='def critica: ((.NotesMarkdown // "") | tostring | ((split("\n")[0]) // "") | gsub("^\\s+|\\s+$"; "")) as $p
    | ($p | ascii_downcase | startswith("severity:")) and (($p[9:] | gsub("^\\s+|\\s+$"; "") | ascii_downcase) == "critical");'

paso1_validar_local() {
    local json="${ORIGEN}/${JSON_NOMBRE}"
    [[ -d "$ORIGEN" ]] || fallar "no existe la carpeta de origen '${ORIGEN}'."
    [[ -f "$json" ]] || fallar "falta '${json}' (¿corrió pack-win.ps1?)."

    local versiones mayor
    if ! versiones="$(jq -r '.Assets[] | select(.Type == "Full") | .Version' "$json")"; then
        fallar "'${json}' no es un feed válido de Velopack (jq falló)."
    fi
    mayor="$(printf '%s\n' "$versiones" | sort -V | tail -n 1)"
    if [[ "$mayor" != "$VERSION" ]]; then
        fallar "la versión pedida '${VERSION}' no es la mayor del feed (Full de mayor versión: '${mayor:-ninguna}')."
    fi

    # Poda: solo las FEED_VERSIONES_MAX versiones mayores (por sus Full) quedan en el feed,
    # más el Full de toda versión vieja cuyas notas sean `severity: critical`.
    local conservar
    conservar="$(printf '%s\n' "$versiones" | sort -uV | tail -n "$FEED_VERSIONES_MAX" | jq -R . | jq -s .)" \
        || fallar "no se pudo calcular las versiones a conservar de '${json}'."
    DIR_PODADO="$(mktemp -d)" || fallar "no se pudo crear el directorio temporal para el feed podado."
    JSON_PUBLICAR="${DIR_PODADO}/${JSON_NOMBRE}"
    jq --argjson conservar "$conservar" "${JQ_CRITICA}"'
        .Assets |= map(select((.Version as $v | $conservar | index($v)) or (.Type == "Full" and critica)))' "$json" >"$JSON_PUBLICAR" \
        || fallar "no se pudo podar '${json}'."
    # Sin nada que podar se publica el archivo de --origen tal cual (byte a byte), no el
    # re-serializado por jq.
    local antes despues
    antes="$(jq '.Assets | length' "$json")" || fallar "no se pudo contar los paquetes de '${json}'."
    despues="$(jq '.Assets | length' "$JSON_PUBLICAR")" || fallar "no se pudo contar los paquetes del feed podado."
    if [[ "$antes" == "$despues" ]]; then
        cp -- "$json" "$JSON_PUBLICAR" || fallar "no se pudo copiar '${json}' al temporal."
    else
        echo "[publicar] feed podado a las ${FEED_VERSIONES_MAX} versiones mayores: ${despues} de ${antes} paquetes (no se borra nada del servidor)."
    fi
    json="$JSON_PUBLICAR"

    # Los Full conservados por crítica (fuera de la ventana): pueden faltar en --origen.
    local fuera_ventana
    if ! fuera_ventana="$(jq -r --argjson conservar "$conservar" '.Assets[] | select((.Version as $v | $conservar | index($v)) | not) | .FileName' "$json")"; then
        fallar "no se pudo calcular las críticas viejas conservadas de '${json}'."
    fi
    if [[ -n "$fuera_ventana" ]]; then
        echo "[publicar] se conservan en el feed los Full de versiones viejas con severity: critical: ${fuera_ventana//$'\n'/ }"
    fi

    # FileName y SHA256 vacíos se rechazan ACÁ: en el TSV de abajo un campo vacío se colapsa
    # (el tab es IFS-whitespace) y correría los demás en silencio.
    local invalidos
    if ! invalidos="$(jq -r '.Assets[] | select(((.FileName // "") == "") or (((.SHA256 // "") | test("^[0-9A-Fa-f]{64}$")) | not)) | "FileName=\(.FileName // "")|SHA256=\(.SHA256 // "")"' "$json")"; then
        fallar "no se pudo validar FileName/SHA256 de '${json}'."
    fi
    if [[ -n "$invalidos" ]]; then
        fallar "'${json}' tiene paquetes con FileName vacío o SHA256 inválido (${invalidos%%$'\n'*}); no se publica."
    fi

    local pares
    if ! pares="$(jq -r '.Assets[] | [.FileName, (.Size | tostring), .Type, .Version, .SHA256] | @tsv' "$json")"; then
        fallar "no se pudo leer la lista de paquetes de '${json}'."
    fi

    local nombre tamano tipo ver sha local_tamano
    while IFS=$'\t' read -r nombre tamano tipo ver sha; do
        if [[ ! "$nombre" =~ ^[A-Za-z0-9._-]+\.nupkg$ ]]; then
            fallar "FileName inseguro en el feed: '${nombre}' (va a un comando remoto)."
        fi
        if [[ ! "$tamano" =~ ^[0-9]+$ ]]; then
            fallar "Size inválido para '${nombre}': '${tamano}'."
        fi
        if [[ ! -f "${ORIGEN}/${nombre}" ]]; then
            if [[ -n "$fuera_ventana" ]] && grep -qxF -- "$nombre" <<<"$fuera_ventana"; then
                # Crítica vieja: ya está en el servidor; se exige ahí (paso 3) en vez de acá.
                SOLO_SERVIDOR+=("$nombre")
            else
                fallar "falta el paquete '${nombre}' en '${ORIGEN}'."
            fi
        else
            local_tamano="$(stat -c %s "${ORIGEN}/${nombre}")"
            if [[ "$local_tamano" != "$tamano" ]]; then
                fallar "'${nombre}' pesa ${local_tamano} bytes y el feed dice ${tamano}."
            fi
        fi
        NOMBRES+=("$nombre")
        TAMANOS+=("$tamano")
        HASHES+=("${sha,,}")
        if [[ "$tipo" == "Full" && "$ver" == "$VERSION" ]]; then
            FULL_NUEVO="$nombre"
            FULL_NUEVO_TAMANO="$tamano"
        fi
    done <<<"$pares"

    [[ "${#NOMBRES[@]}" -gt 0 ]] || fallar "'${json}' no lista ningún paquete."
    [[ -n "$FULL_NUEVO" ]] || fallar "no se encontró el paquete Full de la versión '${VERSION}' en el feed."
}

# ---------------------------------------------------------------------------------------
# Paso 2 -- relevamiento remoto (nombres + tamaños + sha256) y feed vigente
# ---------------------------------------------------------------------------------------

RELEVAMIENTO=""
VERSION_SERVIDA=""

relevar_servidor() {
    local rc=0
    # 'sudo test -d' falso y 'sudo' que no puede elevar dan ambos exit 1 dentro del 'if' y
    # se verían igual que "carpeta inexistente": por eso se exige antes sudo sin contraseña.
    ssh_vps "sudo -n true" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        fallar "no se pudo relevar el servidor: sudo sin contraseña no está disponible para ${VPS_USER} (ssh exit ${rc}). No se subió nada."
    fi
    rc=0
    RELEVAMIENTO="$(ssh_vps "if sudo test -d ${DIR_FEED}; then sudo find ${DIR_FEED} -maxdepth 1 -type f -printf '%f %s ' -exec sha256sum {} \\;; fi")" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        fallar "no se pudo relevar el servidor (ssh exit ${rc}) -- no es lo mismo que carpeta vacía. No se subió nada."
    fi
}

tamano_remoto() {
    awk -v n="$1" '$1 == n { print $2; exit }' <<<"$RELEVAMIENTO"
}

# sha256 en minúsculas (el feed de Velopack lo trae en mayúsculas; se compara en minúsculas).
hash_remoto() {
    awk -v n="$1" '$1 == n { print tolower($3); exit }' <<<"$RELEVAMIENTO"
}

# Lee el releases.win.json que sirve hoy el servidor y deja en VERSION_SERVIDA su Full de
# mayor versión (vacía = no hay feed vigente o no lista ningún Full). Un feed ilegible NO es
# "no hay feed": aborta, salvo --forzar (que existe justamente para destrabar ese caso).
leer_feed_vigente() {
    local rc=0 cuerpo
    cuerpo="$(ssh_vps "if sudo test -f ${DIR_FEED}/${JSON_NOMBRE}; then sudo cat ${DIR_FEED}/${JSON_NOMBRE}; fi")" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        fallar "no se pudo leer el ${JSON_NOMBRE} vigente del servidor (ssh exit ${rc}) -- no es lo mismo que no tener feed. No se subió nada."
    fi
    VERSION_SERVIDA=""
    [[ -n "$cuerpo" ]] || return 0
    local versiones
    if ! versiones="$(jq -r '.Assets[] | select(.Type == "Full") | .Version' <<<"$cuerpo" 2>/dev/null)"; then
        if [[ "$FORZAR" -eq 1 ]]; then
            echo "AVISO: el ${JSON_NOMBRE} vigente del servidor no es un feed válido; sigo por --forzar." >&2
            return 0
        fi
        fallar "el ${JSON_NOMBRE} vigente del servidor no es un feed válido, no puedo saber qué versión sirve. No se subió nada (--forzar para publicar igual)."
    fi
    VERSION_SERVIDA="$(printf '%s\n' "$versiones" | sort -V | tail -n 1)"
}

verificar_no_retrocede() {
    [[ -n "$VERSION_SERVIDA" ]] || return 0
    local menor
    menor="$(printf '%s\n%s\n' "$VERSION" "$VERSION_SERVIDA" | sort -V | head -n 1)"
    if [[ "$VERSION" != "$VERSION_SERVIDA" && "$menor" == "$VERSION" ]]; then
        if [[ "$FORZAR" -eq 1 ]]; then
            echo "AVISO: ${VERSION} es MENOR que la ${VERSION_SERVIDA} que sirve el servidor; sigo por --forzar." >&2
            return 0
        fi
        fallar "la versión ${VERSION} es MENOR que la ${VERSION_SERVIDA} que sirve el servidor: los clientes no hacen downgrade y el feed retrocedería. Para retirar una versión mala republicá la última buena con un número MAYOR. No se subió nada (--forzar solo como emergencia)."
    fi
}

# ---------------------------------------------------------------------------------------
# Paso 3 -- staging + scp
# ---------------------------------------------------------------------------------------

PENDIENTES=()

paso3_subir_a_staging() {
    local i nombre remoto_tamano remoto_hash
    PENDIENTES=()
    for i in "${!NOMBRES[@]}"; do
        nombre="${NOMBRES[$i]}"
        remoto_tamano="$(tamano_remoto "$nombre")"
        if [[ -z "$remoto_tamano" ]]; then
            if [[ " ${SOLO_SERVIDOR[*]:-} " == *" ${nombre} "* ]]; then
                fallar "'${nombre}' (Full de una versión vieja con severity: critical que el feed conserva) no está en '${ORIGEN}' ni en el servidor. Copialo a --origen. No se subió nada."
            fi
            PENDIENTES+=("$nombre")
            continue
        fi
        remoto_hash="$(hash_remoto "$nombre")"
        if [[ "$remoto_tamano" == "${TAMANOS[$i]}" && "$remoto_hash" == "${HASHES[$i]}" ]]; then
            echo "[publicar] ya está en el servidor con el mismo contenido: ${nombre}"
        else
            fallar "'${nombre}' ya existe en el servidor con contenido distinto (tamaño ${remoto_tamano}, sha256 ${remoto_hash:-<ilegible>}; el feed nuevo dice tamaño ${TAMANOS[$i]}, sha256 ${HASHES[$i]}). No se pisa en el lugar: el feed vigente lo describe con otro hash. Publicá con una versión MAYOR. No se subió nada."
        fi
    done

    STAGING_TOCADO=1
    ssh_vps "rm -rf -- ${STAGING} && mkdir -p -m 700 ${STAGING}" \
        || fallar "no se pudo preparar el staging remoto '${STAGING}'."

    for nombre in "${PENDIENTES[@]}"; do
        echo "[publicar] subiendo ${nombre}"
        scp_vps "${ORIGEN}/${nombre}" "${VPS_USER}@${VPS_HOST}:${STAGING}/" \
            || fallar "falló el scp de '${nombre}'. El feed vigente no se tocó."
    done
    echo "[publicar] subiendo ${JSON_NOMBRE}"
    scp_vps "${JSON_PUBLICAR}" "${VPS_USER}@${VPS_HOST}:${STAGING}/" \
        || fallar "falló el scp de '${JSON_NOMBRE}'. El feed vigente no se tocó."
}

# ---------------------------------------------------------------------------------------
# Paso 4 -- instalar los paquetes
# ---------------------------------------------------------------------------------------

paso4_instalar_paquetes() {
    local nombre
    ssh_vps "sudo install -d -o stockapp -g stockapp -m 750 ${DIR_FEED}" \
        || fallar "no se pudo crear/asegurar la carpeta del feed '${DIR_FEED}'."
    for nombre in "${PENDIENTES[@]}"; do
        ssh_vps "sudo install -o stockapp -g stockapp -m 640 ${STAGING}/${nombre} ${DIR_FEED}/${nombre}" \
            || fallar "no se pudo instalar '${nombre}' en '${DIR_FEED}'. El feed vigente no se tocó."
    done
}

# ---------------------------------------------------------------------------------------
# Paso 5 -- verificar que TODO paquete del feed nuevo está con su tamaño y sha256
# ---------------------------------------------------------------------------------------

paso5_verificar_paquetes() {
    local i nombre remoto remoto_hash faltan=0
    relevar_servidor
    for i in "${!NOMBRES[@]}"; do
        nombre="${NOMBRES[$i]}"
        remoto="$(tamano_remoto "$nombre")"
        remoto_hash="$(hash_remoto "$nombre")"
        if [[ "$remoto" != "${TAMANOS[$i]}" ]]; then
            echo "ERROR: en el servidor '${nombre}' tiene tamaño '${remoto:-<ausente>}' y el feed dice ${TAMANOS[$i]}." >&2
            faltan=1
        elif [[ "$remoto_hash" != "${HASHES[$i]}" ]]; then
            echo "ERROR: en el servidor '${nombre}' tiene sha256 '${remoto_hash:-<ausente>}' y el feed dice ${HASHES[$i]}." >&2
            faltan=1
        fi
    done
    if [[ "$faltan" -ne 0 ]]; then
        fallar "verificación remota fallida: el feed NO se actualizó (sigue el vigente)."
    fi
}

# ---------------------------------------------------------------------------------------
# Paso 6 -- el feed, último y con rename atómico
# ---------------------------------------------------------------------------------------

paso6_publicar_feed() {
    ssh_vps "sudo install -o stockapp -g stockapp -m 640 ${STAGING}/${JSON_NOMBRE} ${DIR_FEED}/${JSON_NOMBRE}.tmp && sudo mv -f ${DIR_FEED}/${JSON_NOMBRE}.tmp ${DIR_FEED}/${JSON_NOMBRE}" \
        || fallar "no se pudo publicar '${JSON_NOMBRE}'."
}

# ---------------------------------------------------------------------------------------
# Paso 7 -- verificación final por HTTP
# ---------------------------------------------------------------------------------------

# $1 = etiqueta ("remota"/"local"); $2 = cuerpo del feed servido por la API.
verificar_feed_publicado() {
    local etiqueta="$1" cuerpo="$2"
    if ! jq -e --arg v "$VERSION" '[.Assets[] | select(.Type == "Full" and .Version == $v)] | length > 0' <<<"$cuerpo" >/dev/null 2>&1; then
        fallar "verificación ${etiqueta}: el ${JSON_NOMBRE} servido por la API no contiene la versión ${VERSION}. El feed YA se reemplazó en el servidor; revisá la API."
    fi
}

paso7_verificar_http() {
    local cuerpo cabeceras primera largo

    cuerpo="$(ssh_vps "curl -fsS ${CURL_RESOLVE_REMOTO}${API_URL_BASE}/updates/${JSON_NOMBRE}")" \
        || fallar "verificación remota: la API no sirvió '${API_URL_BASE}/updates/${JSON_NOMBRE}'. El feed YA se reemplazó en el servidor; revisá la API."
    verificar_feed_publicado "remota" "$cuerpo"

    cabeceras="$(ssh_vps "curl -fsSI ${CURL_RESOLVE_REMOTO}${API_URL_BASE}/updates/${FULL_NUEVO}")" \
        || fallar "verificación remota: la API no sirvió '${FULL_NUEVO}'."
    primera="${cabeceras%%$'\n'*}"
    if [[ "$primera" != *" 200"* ]]; then
        fallar "verificación remota: '${FULL_NUEVO}' no respondió 200 (${primera})."
    fi
    largo="$(awk 'tolower($1) == "content-length:" { gsub(/\r/, "", $2); print $2; exit }' <<<"$cabeceras")"
    if [[ "$largo" != "$FULL_NUEVO_TAMANO" ]]; then
        fallar "verificación remota: Content-Length de '${FULL_NUEVO}' es '${largo:-<ausente>}' y debería ser ${FULL_NUEVO_TAMANO}."
    fi

    if [[ "$API_SCHEME" == "https" && -n "${API_HOSTNAME:-}" ]]; then
        cuerpo="$(curl -fsS "https://${API_HOSTNAME}:${API_PORT}/updates/${JSON_NOMBRE}")" \
            || fallar "verificación local: no se pudo traer 'https://${API_HOSTNAME}:${API_PORT}/updates/${JSON_NOMBRE}'. El feed YA se reemplazó en el servidor."
        verificar_feed_publicado "local" "$cuerpo"
    else
        echo "[publicar] AVISO: API_SCHEME=http, se omite la verificación desde la máquina del operador (el curl local); la verificación en el servidor sí corrió."
    fi
}

paso1_validar_local
relevar_servidor
leer_feed_vigente
verificar_no_retrocede
paso3_subir_a_staging
paso4_instalar_paquetes
paso5_verificar_paquetes
paso6_publicar_feed
paso7_verificar_http

echo "[publicar] OK: versión ${VERSION} publicada y verificada."
