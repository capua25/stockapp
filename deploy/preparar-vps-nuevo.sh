#!/usr/bin/env bash
# =======================================================================================
# deploy/preparar-vps-nuevo.sh -- deja un VPS Ubuntu 24.04 DEDICADO listo para StockApp.
# Corre en la MÁQUINA DEL OPERADOR y opera el servidor por ssh.
# =======================================================================================
#
# Uso:
#   STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version <v> \
#       [--env-viejo <archivo>] [--verificar] [--probar-renovacion]
#
#   STOCKAPP_ENV_LOCAL  OBLIGATORIA. El .env del operador para el VPS NUEVO (VPS_HOST, VPS_USER,
#                       VPS_SSH_PORT, API_SCHEME=https, API_HOSTNAME, API_PORT). No puede ser
#                       deploy/.env (el default de los demás scripts, que apunta al VPS viejo).
#   --version           OBLIGATORIA, sin default. La misma para el deploy y para el feed.
#   --env-viejo         De donde se lee SOLO LICENCIA_CLAVE_PUBLICA_BASE64, línea por línea y sin
#                       `source`. Default: deploy/.env.
#   --verificar         Solo lectura: informa HECHO o PENDIENTE en cada paso, sin aplicar nada.
#   --probar-renovacion Prueba la renovación del certificado (gasta cupo de Let's Encrypt): solo
#                       con este flag explícito, una vez y a mano.
#
# Principio de idempotencia: manda el ESTADO OBSERVADO, no un marcador. Cada paso es
# ¿ya está? (lectura) -> aplicar -> re-verificar. Si ya estaba, informa YA ESTABA y no aplica
# nada. Un paso que falla deja hecho lo que hizo, y re-correr el script retoma: los pasos
# anteriores dan YA ESTABA.
#
# El VPS es DEDICADO: este script instala paquetes y maneja el firewall y el certificado. Por
# eso se niega a operar (exit 9) si el destino tiene contenedores que no son de StockApp o si el
# puerto 80 ya está escuchando, y si el VPS_HOST es el de deploy/.env (el VPS viejo, compartido
# con otra aplicación).
#
# Pasos: 0 pre-vuelo | 1 Docker | 2 usuario y directorios | 3 firewall | 4 TLS | 5 .env del
# servidor | 6 Postgres | 7 primer deploy | 8 feed | 9 renovación | 10 verificación final.
#
# Códigos de salida (estables):
#   0  el VPS quedó listo (o ya lo estaba)
#   2  uso / argumentos
#   3  precondición (config, `sudo -n`, versión del SO, DNS, falta `releases/win`, lock): no se tocó nada
#   5  falló un paso: lo hecho queda hecho y re-correr continúa
#   6  el re-chequeo de acceso después de `ufw enable` falló: la reversa automática deshace el firewall en N segundos; esperar y re-correr
#   8  no se pudo consultar (ssh 255, salida ilegible)
#   9  alcance: el destino no parece un VPS dedicado (contenedores ajenos, puerto 80 ocupado) o es el VPS viejo
# =======================================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
# shellcheck source=lib/orquestacion.sh
source "$REPO_ROOT/deploy/lib/orquestacion.sh"

PASOS_TOTAL=10

uso() {
    echo "Uso: STOCKAPP_ENV_LOCAL=<.env-del-vps-nuevo> $0 --version <v> [--env-viejo <archivo>] [--verificar] [--probar-renovacion]" >&2
}

# ---------------------------------------------------------------------------------------
# Argumentos
# ---------------------------------------------------------------------------------------

VERSION=""
ENV_VIEJO="$REPO_ROOT/deploy/.env"
VERIFICAR=0
PROBAR_RENOVACION=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)
            if [[ $# -lt 2 ]]; then
                echo "preparar: --version necesita un valor" >&2
                uso
                exit 2
            fi
            VERSION="$2"
            shift 2
            ;;
        --env-viejo)
            if [[ $# -lt 2 || -z "$2" ]]; then
                echo "preparar: --env-viejo necesita un valor" >&2
                uso
                exit 2
            fi
            ENV_VIEJO="$2"
            shift 2
            ;;
        --verificar)
            VERIFICAR=1
            shift
            ;;
        --probar-renovacion)
            PROBAR_RENOVACION=1
            shift
            ;;
        *)
            echo "preparar: argumento desconocido '$1'" >&2
            uso
            exit 2
            ;;
    esac
done

if [[ -z "$VERSION" ]]; then
    echo "preparar: falta --version (obligatoria, sin default)" >&2
    uso
    exit 2
fi
# La versión viaja a rutas y a otros scripts: solo números separados por puntos y, opcional,
# un sufijo de pre-release.
if [[ ! "$VERSION" =~ ^[0-9]+(\.[0-9]+){1,3}([-+][0-9A-Za-z.-]+)?$ ]]; then
    echo "preparar: --version '$VERSION' no tiene forma de versión (por ejemplo 1.2.3)" >&2
    uso
    exit 2
fi

# ---------------------------------------------------------------------------------------
# Salida y motor de pasos
# ---------------------------------------------------------------------------------------

# Detalle del último fallo de un paso (lo llenan las funciones de cada paso).
PASO_DETALLE=""
# Pasos 1 a 8 que --verificar encontró PENDIENTE: son los únicos que deciden su exit.
PENDIENTES=0

# linea_estado <n> <nombre> <HECHO|PENDIENTE>: la misma línea que orq_fin_paso, para los dos
# resultados que solo existen en --verificar (orq_fin_paso no los acepta).
linea_estado() {
    local n="$1" nombre="$2" estado="$3" puntos relleno hora
    puntos=$((45 - ${#nombre}))
    if [[ "$puntos" -lt 3 ]]; then
        puntos=3
    fi
    printf -v relleno '%*s' "$puntos" ''
    relleno="${relleno// /.}"
    printf -v hora '%(%H:%M:%S)T' -1
    printf '[%s] (%*d/%d) %s %s %s\n' "$hora" "${#PASOS_TOTAL}" "$n" "$PASOS_TOTAL" "$nombre" "$relleno" "$estado"
}

# volcar_error: lo que dijo el servidor, a stderr y con sangría (nunca un secreto: estos pasos
# no manejan ninguno).
volcar_error() {
    local texto
    for texto in "$ORQ_STDERR" "$ORQ_SALIDA"; do
        if [[ -n "$texto" ]]; then
            printf '%s\n' "$texto" | sed 's/^/    | /' >&2
        fi
    done
}

# abortar <codigo> <mensaje>: cierra el paso abierto como FALLÓ y sale.
abortar() {
    local codigo="$1"
    shift
    echo "preparar: $*" >&2
    orq_fin_paso FALLÓ "exit $codigo"
    exit "$codigo"
}

# correr_paso <n> <nombre>
# Pide el estado (paso<n>_hecho: 0 hecho, 1 pendiente, 8 no pude consultar); si falta, aplica
# (paso<n>_aplicar: 0 ok, 6 el re-chequeo de acceso falló, 8 no pude consultar, otro: falló) y
# re-verifica (paso<n>_reverificar si existe; si no, paso<n>_hecho). Con --verificar solo lee.
correr_paso() {
    local n="$1" nombre="$2" rc=0
    orq_paso "$n" "$PASOS_TOTAL" "$nombre"
    PASO_DETALLE=""

    # El paso 9 gasta cupo de Let's Encrypt: solo con el flag explícito. Sin él, nunca se renueva.
    if [[ "$n" -eq 9 && "$PROBAR_RENOVACION" -eq 0 ]]; then
        orq_fin_paso OMITIDO "sin --probar-renovacion"
        return 0
    fi

    "paso${n}_hecho" || rc=$?
    case "$rc" in
        0)
            if [[ "$VERIFICAR" -eq 1 ]]; then
                linea_estado "$n" "$nombre" HECHO
            else
                orq_fin_paso "YA ESTABA"
            fi
            return 0
            ;;
        1) ;;
        *) abortar 8 "paso $n ($nombre): no pude consultar el estado. ${PASO_DETALLE}" ;;
    esac

    if [[ "$VERIFICAR" -eq 1 ]]; then
        linea_estado "$n" "$nombre" PENDIENTE
        # Los pasos 9 y 10 se informan pero no deciden el exit de --verificar.
        if [[ "$n" -le 8 ]]; then
            PENDIENTES=$((PENDIENTES + 1))
        fi
        return 0
    fi

    rc=0
    "paso${n}_aplicar" || rc=$?
    case "$rc" in
        0) ;;
        6) abortar 6 "paso $n ($nombre): ${PASO_DETALLE}" ;;
        8) abortar 8 "paso $n ($nombre): no pude consultar. ${PASO_DETALLE}" ;;
        *) abortar 5 "paso $n ($nombre): falló. ${PASO_DETALLE}" ;;
    esac

    rc=0
    if declare -F "paso${n}_reverificar" >/dev/null; then
        "paso${n}_reverificar" || rc=$?
    else
        "paso${n}_hecho" || rc=$?
    fi
    case "$rc" in
        0) orq_fin_paso OK ;;
        1) abortar 5 "paso $n ($nombre): se aplicó pero el estado no quedó como se esperaba. ${PASO_DETALLE}" ;;
        *) abortar 8 "paso $n ($nombre): no pude re-verificar. ${PASO_DETALLE}" ;;
    esac
}

# ---------------------------------------------------------------------------------------
# Paso 0: pre-vuelo (local + lectura remota). No aplica nada: si algo falla, sale con 3 o 9.
# ---------------------------------------------------------------------------------------

# resolver_ipv4 <nombre-o-ip> -> las IPv4 únicas, ordenadas, una por línea.
# rc 0 resolvió; 2 el nombre no existe; 8 no pude resolver / respuesta ilegible.
resolver_ipv4() {
    local nombre="$1" salida rc=0
    if [[ "$nombre" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        printf '%s\n' "$nombre"
        return 0
    fi
    salida="$(getent ahostsv4 "$nombre")" || rc=$?
    case "$rc" in
        0) ;;
        2) return 2 ;;
        *) return 8 ;;
    esac
    salida="$(printf '%s\n' "$salida" | awk 'NF { print $1 }' | sort -u)"
    if [[ -z "$salida" ]]; then
        return 8
    fi
    printf '%s\n' "$salida"
}

prevuelo_configuracion() {
    local env_defecto real_nuevo real_defecto clave

    # A9: el .env del VPS nuevo se pide explícito y nunca es el default de los demás scripts.
    if [[ -z "${STOCKAPP_ENV_LOCAL:-}" ]]; then
        abortar 3 "falta STOCKAPP_ENV_LOCAL: este script solo opera el VPS NUEVO y no tiene default (el default de los demás scripts, deploy/.env, es el viejo)."
    fi
    env_defecto="$REPO_ROOT/deploy/.env"
    real_nuevo="$(realpath -m -- "$STOCKAPP_ENV_LOCAL")"
    real_defecto="$(realpath -m -- "$env_defecto")"
    if [[ "$real_nuevo" == "$real_defecto" ]] \
        || { [[ -e "$STOCKAPP_ENV_LOCAL" && -e "$env_defecto" && "$STOCKAPP_ENV_LOCAL" -ef "$env_defecto" ]]; }; then
        abortar 3 "STOCKAPP_ENV_LOCAL apunta a deploy/.env, que es el del VPS viejo. Usá el .env del VPS nuevo."
    fi

    orq_leer_env "$STOCKAPP_ENV_LOCAL" NUEVO VPS_HOST VPS_USER VPS_SSH_PORT API_SCHEME API_HOSTNAME API_PORT
    for clave in VPS_HOST VPS_USER VPS_SSH_PORT API_SCHEME API_HOSTNAME API_PORT; do
        local nombre="NUEVO_${clave}"
        if [[ -z "${!nombre:-}" ]]; then
            abortar 3 "falta $clave en $STOCKAPP_ENV_LOCAL."
        fi
    done
    if [[ ! "$NUEVO_VPS_HOST" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
        abortar 3 "VPS_HOST en $STOCKAPP_ENV_LOCAL no parece un nombre ni una IP."
    fi
    if [[ ! "$NUEVO_VPS_USER" =~ ^[a-z_][a-z0-9_-]*$ ]]; then
        abortar 3 "VPS_USER en $STOCKAPP_ENV_LOCAL no parece un usuario de Linux."
    fi
    if [[ ! "$NUEVO_VPS_SSH_PORT" =~ ^[0-9]+$ ]] || ((NUEVO_VPS_SSH_PORT < 1 || NUEVO_VPS_SSH_PORT > 65535)); then
        abortar 3 "VPS_SSH_PORT en $STOCKAPP_ENV_LOCAL no es un puerto."
    fi
    if [[ "$NUEVO_API_SCHEME" != "https" ]]; then
        abortar 3 "API_SCHEME en $STOCKAPP_ENV_LOCAL tiene que ser exactamente https (el VPS nuevo sirve con TLS)."
    fi
    if [[ ! "$NUEVO_API_HOSTNAME" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]]; then
        abortar 3 "API_HOSTNAME en $STOCKAPP_ENV_LOCAL no parece un nombre de host."
    fi
    if [[ ! "$NUEVO_API_PORT" =~ ^[0-9]+$ ]] || ((NUEVO_API_PORT < 1 || NUEVO_API_PORT > 65535)); then
        abortar 3 "API_PORT en $STOCKAPP_ENV_LOCAL no es un puerto."
    fi

    # A9: si deploy/.env define VPS_HOST, tiene que ser OTRO servidor.
    if [[ -f "$env_defecto" ]]; then
        orq_leer_env "$env_defecto" VIEJO VPS_HOST
        if [[ -n "${VIEJO_VPS_HOST:-}" && "$VIEJO_VPS_HOST" == "$NUEVO_VPS_HOST" ]]; then
            abortar 9 "el VPS_HOST de $STOCKAPP_ENV_LOCAL es el mismo que el de deploy/.env: ese es el VPS viejo, compartido con otra aplicación. Este script nunca opera sobre él."
        fi
    fi
}

prevuelo_local() {
    local json mayor clave rc ips_vps ips_api

    # Los releases de la versión: publicar-release.sh falla al final si no están.
    json="$REPO_ROOT/releases/win/releases.win.json"
    if [[ ! -f "$json" ]]; then
        abortar 3 "falta releases/win/releases.win.json: ahí tiene que estar la versión $VERSION (¿corrió pack-win.ps1?)."
    fi
    if ! command -v jq >/dev/null 2>&1; then
        abortar 3 "falta 'jq' en esta máquina: hace falta para leer releases/win/releases.win.json."
    fi
    rc=0
    mayor="$(jq -r '.Assets[] | select(.Type == "Full") | .Version' "$json" | sort -V | tail -n 1)" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        abortar 3 "releases/win/releases.win.json no es un feed válido (jq no pudo leerlo)."
    fi
    if [[ "$mayor" != "$VERSION" ]]; then
        abortar 3 "releases/win no tiene la versión $VERSION como la mayor del feed (la mayor es '${mayor:-ninguna}'): publicar-release.sh fallaría al final."
    fi

    # La clave pública de licencias: solo el NOMBRE de la clave se imprime, nunca el valor.
    clave="LICENCIA_CLAVE_PUBLICA_BASE64"
    orq_leer_env "$ENV_VIEJO" VIEJO "$clave"
    if [[ -z "${VIEJO_LICENCIA_CLAVE_PUBLICA_BASE64:-}" ]]; then
        abortar 3 "falta $clave en $ENV_VIEJO (--env-viejo)."
    fi
    if [[ "$VIEJO_LICENCIA_CLAVE_PUBLICA_BASE64" == *CAMBIAR* ]]; then
        abortar 3 "$clave en $ENV_VIEJO todavía es el valor de ejemplo (CAMBIAR...)."
    fi
    if [[ ! "$VIEJO_LICENCIA_CLAVE_PUBLICA_BASE64" =~ ^[A-Za-z0-9+/]+={0,2}$ ]] \
        || ((${#VIEJO_LICENCIA_CLAVE_PUBLICA_BASE64} % 4 != 0)); then
        abortar 3 "$clave en $ENV_VIEJO no tiene formato base64."
    fi

    # DNS (antes de gastar un intento de Let's Encrypt): API_HOSTNAME tiene que resolver a la
    # misma IP que VPS_HOST. Los dos se resuelven antes de comparar: ninguno es necesariamente una IP.
    rc=0
    ips_vps="$(resolver_ipv4 "$NUEVO_VPS_HOST")" || rc=$?
    case "$rc" in
        0) ;;
        2) abortar 3 "VPS_HOST ($NUEVO_VPS_HOST) no resuelve a ninguna IPv4." ;;
        *) abortar 8 "no pude resolver VPS_HOST ($NUEVO_VPS_HOST)." ;;
    esac
    rc=0
    ips_api="$(resolver_ipv4 "$NUEVO_API_HOSTNAME")" || rc=$?
    case "$rc" in
        0) ;;
        2) abortar 3 "API_HOSTNAME ($NUEVO_API_HOSTNAME) no resuelve a ninguna IPv4: Let's Encrypt no podría validarlo." ;;
        *) abortar 8 "no pude resolver API_HOSTNAME ($NUEVO_API_HOSTNAME)." ;;
    esac
    if [[ "$ips_vps" != "$ips_api" ]]; then
        abortar 3 "API_HOSTNAME ($NUEVO_API_HOSTNAME) resuelve a [$(printf '%s' "$ips_api" | tr '\n' ' ')] y VPS_HOST ($NUEVO_VPS_HOST) a [$(printf '%s' "$ips_vps" | tr '\n' ' ')]: tienen que ser la misma IP."
    fi
}

# puerto_escuchando <puerto> <salida-de-ss>: 0 si alguna línea LISTEN tiene ese puerto local.
puerto_escuchando() {
    local puerto="$1" salida="$2" linea campos
    while IFS= read -r linea; do
        read -r -a campos <<< "$linea"
        if [[ "${campos[0]:-}" == "LISTEN" && "${campos[3]:-}" == *":${puerto}" ]]; then
            return 0
        fi
    done <<< "$salida"
    return 1
}

prevuelo_remoto() {
    local linea ss_salida ajenos="" nombres

    # Acceso por ssh y sudo sin contraseña.
    orq_ssh NUEVO "sudo -n true"
    if [[ "$ORQ_RC" -eq 255 ]]; then
        volcar_error
        abortar 8 "no pude conectar por ssh a $NUEVO_VPS_USER@$NUEVO_VPS_HOST:$NUEVO_VPS_SSH_PORT."
    elif [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_error
        abortar 3 "'sudo -n true' falló en el servidor: $NUEVO_VPS_USER necesita sudo sin contraseña."
    fi

    # Ubuntu 24.04.
    orq_ssh NUEVO "cat /etc/os-release"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_error
        abortar 8 "no pude leer /etc/os-release del servidor."
    fi
    local es_2404=0
    while IFS= read -r linea; do
        if [[ "$linea" == 'VERSION_ID="24.04"' ]]; then
            es_2404=1
        fi
    done <<< "$ORQ_SALIDA"
    if [[ "$es_2404" -ne 1 ]]; then
        abortar 3 "el servidor no es Ubuntu 24.04 (VERSION_ID de /etc/os-release)."
    fi

    # Puertos: el de ssh tiene que estar escuchando (si no, el firewall dejaría afuera al
    # operador) y el 80 tiene que estar libre (certbot lo necesita para sí).
    orq_ssh NUEVO "ss -ltn"
    if [[ "$ORQ_RC" -ne 0 || -z "$ORQ_SALIDA" ]]; then
        volcar_error
        abortar 8 "no pude listar los puertos del servidor (ss -ltn)."
    fi
    ss_salida="$ORQ_SALIDA"
    if ! puerto_escuchando "$NUEVO_VPS_SSH_PORT" "$ss_salida"; then
        abortar 3 "el servidor no tiene a nadie escuchando en el puerto $NUEVO_VPS_SSH_PORT (VPS_SSH_PORT): habilitar el firewall dejaría afuera al operador."
    fi

    # Alcance (A9): un VPS dedicado no tiene contenedores ajenos ni el puerto 80 ocupado.
    orq_ssh NUEVO "command -v docker"
    case "$ORQ_RC" in
        0)
            orq_ssh NUEVO "sudo -n docker ps -a --format '{{.Names}}'"
            if [[ "$ORQ_RC" -ne 0 ]]; then
                volcar_error
                abortar 8 "no pude listar los contenedores del servidor."
            fi
            while IFS= read -r nombres; do
                if [[ -n "$nombres" && ! "$nombres" =~ ^stockapp ]]; then
                    ajenos+="${ajenos:+, }$nombres"
                fi
            done <<< "$ORQ_SALIDA"
            if [[ -n "$ajenos" ]]; then
                abortar 9 "el servidor tiene contenedores que no son de StockApp ($ajenos): no parece un VPS dedicado. No se tocó nada."
            fi
            ;;
        1) ;;   # Docker todavía no está instalado: no puede haber contenedores.
        *)
            volcar_error
            abortar 8 "no pude consultar si el servidor tiene Docker."
            ;;
    esac
    if puerto_escuchando 80 "$ss_salida"; then
        abortar 9 "el puerto 80 del servidor ya está escuchando: no parece un VPS dedicado, y certbot lo necesita libre. No se tocó nada."
    fi
}

paso0_prevuelo() {
    orq_paso 0 "$PASOS_TOTAL" "pre-vuelo"
    prevuelo_configuracion
    prevuelo_local
    prevuelo_remoto
    orq_fin_paso OK
}

# ---------------------------------------------------------------------------------------
# Paso 1: Docker
# ---------------------------------------------------------------------------------------

# Una conexión ssh NUEVA (sin reutilizar un maestro abierto): los grupos del usuario se leen al
# abrir la sesión, así que una conexión vieja no vería un `usermod -aG docker` reciente.
paso1_hecho() {
    orq_ssh_nueva_conexion NUEVO "docker ps >/dev/null 2>&1 && docker compose version >/dev/null 2>&1"
    case "$ORQ_RC" in
        0) return 0 ;;
        255)
            PASO_DETALLE="${ORQ_STDERR}"
            return 8
            ;;
        *) return 1 ;;
    esac
}

paso1_aplicar() {
    orq_ssh NUEVO "sudo -n apt-get update && sudo -n env DEBIAN_FRONTEND=noninteractive apt-get install -y docker.io docker-compose-v2 && sudo -n usermod -aG docker ${NUEVO_VPS_USER}"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_error
        if [[ "$ORQ_RC" -eq 255 ]]; then
            return 8
        fi
        PASO_DETALLE="apt-get/usermod salió con $ORQ_RC."
        return 1
    fi
}

# ---------------------------------------------------------------------------------------
# Paso 2: usuario `stockapp` y directorios
# ---------------------------------------------------------------------------------------

# Los dos scripts corren como root (sudo) con el usuario de deploy como argumento: /var/lib/stockapp
# no es traversable por VPS_USER (su home es 750). Viajan por stdin de ssh.
PASO2_LEER='
set -u
u="$1"
ok=1
id stockapp >/dev/null 2>&1 || ok=0
for r in /var/lib/stockapp/.local /var/lib/stockapp/.local/share /var/lib/stockapp/.local/share/StockApp; do
    [ "$(stat -c "%U:%G %a" -- "$r" 2>/dev/null)" = "stockapp:stockapp 750" ] || ok=0
done
home="$(getent passwd "$u" | cut -d: -f6)"
[ -n "$home" ] || ok=0
for r in "$home/stockapp-deploy" "$home/stockapp-deploy/migracion"; do
    [ "$(stat -c "%U %a" -- "$r" 2>/dev/null)" = "$u 700" ] || ok=0
done
if [ "$ok" = 1 ]; then echo hecho; else echo pendiente; fi
'

PASO2_APLICAR='
set -eu
u="$1"
# El mismo comando que install.sh, que después detecta el usuario existente y no lo recrea.
if ! id stockapp >/dev/null 2>&1; then
    useradd --system --create-home --home-dir /var/lib/stockapp --shell /usr/sbin/nologin stockapp
fi
install -d -o stockapp -g stockapp -m 750 /var/lib/stockapp/.local /var/lib/stockapp/.local/share /var/lib/stockapp/.local/share/StockApp
home="$(getent passwd "$u" | cut -d: -f6)"
[ -n "$home" ]
install -d -o "$u" -g "$(id -gn "$u")" -m 700 "$home/stockapp-deploy" "$home/stockapp-deploy/migracion"
'

paso2_hecho() {
    orq_ssh NUEVO "sudo -n bash -s -- ${NUEVO_VPS_USER}" "$PASO2_LEER"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        PASO_DETALLE="${ORQ_STDERR}"
        return 8
    fi
    case "$ORQ_SALIDA" in
        hecho) return 0 ;;
        pendiente) return 1 ;;
        *)
            PASO_DETALLE="respuesta ilegible del servidor."
            return 8
            ;;
    esac
}

paso2_aplicar() {
    orq_ssh NUEVO "sudo -n bash -s -- ${NUEVO_VPS_USER}" "$PASO2_APLICAR"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_error
        if [[ "$ORQ_RC" -eq 255 ]]; then
            return 8
        fi
        PASO_DETALLE="el script remoto salió con $ORQ_RC."
        return 1
    fi
}

# ---------------------------------------------------------------------------------------
# Paso 3: firewall con reversa automática (A8)
# ---------------------------------------------------------------------------------------

# Nombre EXACTO del timer transitorio de la reversa y su plazo.
FW_REVERSA="stockapp-ufw-reversa"
FW_REVERSA_SEGUNDOS=120
FW_MENSAJE_REVERSA="la reversa deshace el firewall en ${FW_REVERSA_SEGUNDOS} s; esperá y re-corré"

# fw_estado: deja FW_ACTIVO (1 activo, 0 inactivo) y FW_REGLAS_FALTAN (las reglas que no están,
# separadas por espacio). rc 0 / 8 (no pude consultar o la salida no es la de ufw).
fw_estado() {
    local linea primera="" campos regla
    FW_ACTIVO=0
    FW_REGLAS_FALTAN=""
    orq_ssh NUEVO "sudo -n ufw status"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        PASO_DETALLE="'ufw status' salió con $ORQ_RC. ${ORQ_STDERR}"
        return 8
    fi
    primera="${ORQ_SALIDA%%$'\n'*}"
    case "$primera" in
        "Status: active") FW_ACTIVO=1 ;;
        "Status: inactive") FW_ACTIVO=0 ;;
        *)
            PASO_DETALLE="la salida de 'ufw status' no es la esperada."
            return 8
            ;;
    esac
    for regla in "${NUEVO_VPS_SSH_PORT}/tcp" "80/tcp" "${NUEVO_API_PORT}/tcp"; do
        local esta=0
        while IFS= read -r linea; do
            read -r -a campos <<< "$linea"
            if [[ "${campos[0]:-}" == "$regla" && "${campos[1]:-}" == "ALLOW" ]]; then
                esta=1
            fi
        done <<< "$ORQ_SALIDA"
        if [[ "$esta" -eq 0 ]]; then
            FW_REGLAS_FALTAN+="${FW_REGLAS_FALTAN:+ }$regla"
        fi
    done
    # Con el firewall inactivo las reglas guardadas no figuran en `status`: se vuelven a pedir.
    if [[ "$FW_ACTIVO" -eq 0 ]]; then
        FW_REGLAS_FALTAN="${NUEVO_VPS_SSH_PORT}/tcp 80/tcp ${NUEVO_API_PORT}/tcp"
    fi
}

# fw_reversa_armada: deja FW_ARMADA (1 el timer existe, 0 no existe). rc 0 / 8.
fw_reversa_armada() {
    FW_ARMADA=0
    orq_ssh NUEVO "systemctl show -p LoadState --value ${FW_REVERSA}.timer"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        PASO_DETALLE="no pude consultar el timer ${FW_REVERSA}.timer (salió con $ORQ_RC). ${ORQ_STDERR}"
        return 8
    fi
    case "$ORQ_SALIDA" in
        not-found) FW_ARMADA=0 ;;
        loaded) FW_ARMADA=1 ;;
        *)
            PASO_DETALLE="respuesta ilegible sobre el timer ${FW_REVERSA}.timer."
            return 8
            ;;
    esac
}

# Hecho = activo + las tres reglas + SIN reversa armada (una reversa pendiente deshace el firewall).
paso3_hecho() {
    local rc=0
    fw_estado || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        return "$rc"
    fi
    if [[ "$FW_ACTIVO" -ne 1 || -n "$FW_REGLAS_FALTAN" ]]; then
        return 1
    fi
    fw_reversa_armada || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        return "$rc"
    fi
    if [[ "$FW_ARMADA" -eq 1 ]]; then
        return 1
    fi
    return 0
}

# fw_permitir <puerto>: `ufw allow` es idempotente (si la regla ya existe, la saltea). Nunca se
# reinicia el firewall ni se borra ninguna regla: solo se agregan.
fw_permitir() {
    orq_ssh NUEVO "sudo -n ufw allow ${1}/tcp"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_error
        if [[ "$ORQ_RC" -eq 255 ]]; then
            return 8
        fi
        PASO_DETALLE="'ufw allow ${1}/tcp' salió con $ORQ_RC."
        return 1
    fi
}

# El orden es el de la spec (6.4, Firewall) y no se altera: SSH primero, reversa armada, enable,
# re-chequeo con una conexión NUEVA y, solo si respondió, se desarma la reversa.
paso3_aplicar() {
    local rc=0

    # 1. Las tres reglas, la de ssh primero.
    fw_permitir "$NUEVO_VPS_SSH_PORT" || return $?
    fw_permitir 80 || return $?
    fw_permitir "$NUEVO_API_PORT" || return $?

    fw_estado || return $?
    fw_reversa_armada || return $?

    # 2. Si ya estaba activo y sin reversa pendiente, se termina acá (las reglas ya se re-verificaron).
    if [[ "$FW_ACTIVO" -eq 1 && "$FW_ARMADA" -eq 0 ]]; then
        return 0
    fi

    # 3. La reversa, ANTES del enable.
    #    Con el firewall ACTIVO, una reversa que quedó armada de una corrida anterior se conserva
    #    (systemd-run falla si el nombre existe) y se desarma después del re-chequeo.
    #    Con el firewall INACTIVO no se la puede reusar: no se sabe cuánto le queda y si vence entre
    #    acá y el enable, el firewall se habilitaría sin reversa. Con el firewall inactivo nadie
    #    queda afuera, así que parar la vieja es seguro: se para, se confirma que ya no existe y
    #    se arma una nueva de plazo completo.
    if [[ "$FW_ACTIVO" -eq 0 && "$FW_ARMADA" -eq 1 ]]; then
        orq_ssh NUEVO "sudo -n systemctl stop ${FW_REVERSA}.timer"
        rc="$ORQ_RC"
        if [[ "$rc" -ne 0 ]]; then
            volcar_error
        fi
        fw_reversa_armada || return $?
        if [[ "$FW_ARMADA" -ne 0 ]]; then
            PASO_DETALLE="no pude parar la reversa vieja del firewall (systemctl stop salió con $rc): no se habilita el firewall sin una reversa de plazo conocido."
            return 1
        fi
    fi
    if [[ "$FW_ARMADA" -eq 0 ]]; then
        orq_ssh NUEVO "sudo -n systemd-run --unit=${FW_REVERSA} --on-active=${FW_REVERSA_SEGUNDOS} /usr/sbin/ufw disable"
        if [[ "$ORQ_RC" -ne 0 ]]; then
            volcar_error
            if [[ "$ORQ_RC" -eq 255 ]]; then
                return 8
            fi
            PASO_DETALLE="no pude armar la reversa del firewall (systemd-run salió con $ORQ_RC): no se habilita el firewall sin ella."
            return 1
        fi
        # Un systemd-run que sale 0 y no dejó el timer no es una red de seguridad.
        fw_reversa_armada || return $?
        if [[ "$FW_ARMADA" -ne 1 ]]; then
            PASO_DETALLE="la reversa del firewall no quedó armada: no se habilita el firewall sin ella."
            return 1
        fi
    fi

    # 4. Habilitar (si ya estaba activo, con la reversa de una corrida anterior, no se repite).
    if [[ "$FW_ACTIVO" -eq 0 ]]; then
        orq_ssh NUEVO "sudo -n ufw --force enable"
        if [[ "$ORQ_RC" -ne 0 ]]; then
            volcar_error
            PASO_DETALLE="'ufw --force enable' salió con $ORQ_RC y el estado del firewall es incierto: ${FW_MENSAJE_REVERSA}."
            return 6
        fi
    fi

    # 5. Re-chequeo con una conexión ssh NUEVA (sin maestro, ConnectTimeout corto, BatchMode).
    orq_ssh_nueva_conexion NUEVO "sudo -n true"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_error
        PASO_DETALLE="con el firewall habilitado no respondió una conexión ssh nueva (salió con $ORQ_RC): ${FW_MENSAJE_REVERSA}."
        return 6
    fi

    # 6. Respondió: se desarma la reversa con el nombre exacto y se verifica que ya no exista.
    orq_ssh NUEVO "sudo -n systemctl stop ${FW_REVERSA}.timer"
    rc="$ORQ_RC"
    if [[ "$rc" -ne 0 ]]; then
        volcar_error
    fi
    if ! fw_reversa_armada; then
        PASO_DETALLE="no pude confirmar que la reversa quedó desarmada: ${FW_MENSAJE_REVERSA}."
        return 6
    fi
    if [[ "$FW_ARMADA" -eq 1 ]]; then
        PASO_DETALLE="el timer ${FW_REVERSA}.timer sigue armado después de 'systemctl stop' (salió con $rc): ${FW_MENSAJE_REVERSA}."
        return 6
    fi
}

# ---------------------------------------------------------------------------------------
# Paso 4: TLS (certbot + deploy-hook)
# ---------------------------------------------------------------------------------------

# Donde vive el hook en el servidor: es el `renew_hook` que certbot guarda en el .conf del dominio.
TLS_HOOK="/usr/local/sbin/stockapp-tls-deploy-hook.sh"
TLS_HOOK_LOCAL="$REPO_ROOT/deploy/tls/stockapp-tls-deploy-hook.sh"
# Resultado de paso4_hecho: hecho | sin-cert | sin-pem.
P4_ESTADO=""

# Corre como root con el dominio como argumento y viaja por stdin de ssh.
#   sin-cert  falta live/<dominio>, el certificado vence en menos de 30 días o el renew_hook
#             del .conf no es el hook
#   sin-pem   el certificado está bien pero los PEM de /var/lib/stockapp/tls no son
#             stockapp:stockapp 0600
PASO4_LEER='
set -u
d="$1"
hook="'"$TLS_HOOK"'"
le=/etc/letsencrypt
cert=1
[ -f "$le/live/$d/fullchain.pem" ] || cert=0
if [ "$cert" = 1 ]; then
    openssl x509 -checkend 2592000 -noout -in "$le/live/$d/fullchain.pem" >/dev/null 2>&1 || cert=0
fi
if [ "$cert" = 1 ]; then
    [ "$(sed -n "s/^renew_hook = //p" "$le/renewal/$d.conf" 2>/dev/null | head -n 1)" = "$hook" ] || cert=0
fi
if [ "$cert" = 0 ]; then echo sin-cert; exit 0; fi
pem=1
for f in fullchain.pem privkey.pem; do
    [ "$(stat -c "%U:%G %a" -- "/var/lib/stockapp/tls/$f" 2>/dev/null)" = "stockapp:stockapp 600" ] || pem=0
done
if [ "$pem" = 1 ]; then echo hecho; else echo sin-pem; fi
'

paso4_hecho() {
    orq_ssh NUEVO "sudo -n bash -s -- ${NUEVO_API_HOSTNAME}" "$PASO4_LEER"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        PASO_DETALLE="${ORQ_STDERR}"
        return 8
    fi
    case "$ORQ_SALIDA" in
        hecho) P4_ESTADO="$ORQ_SALIDA"; return 0 ;;
        sin-cert|sin-pem) P4_ESTADO="$ORQ_SALIDA"; return 1 ;;
        *)
            PASO_DETALLE="respuesta ilegible del servidor."
            return 8
            ;;
    esac
}

# paso4_hook_a_mano: el hook corre con RENEWED_LINEAGE como lo haría certbot. Su exit no cuenta
# (el `systemctl restart stockapp-api` falla mientras la API no existe): cuenta el resultado.
paso4_hook_a_mano() {
    orq_ssh NUEVO "sudo -n env RENEWED_LINEAGE=/etc/letsencrypt/live/${NUEVO_API_HOSTNAME} ${TLS_HOOK}"
    if [[ "$ORQ_RC" -eq 255 ]]; then
        volcar_error
        return 8
    fi
    return 0
}

# Aplicar: certbot, el hook y el estado en el que quedó la emisión. P4_ESTADO viene de la lectura
# que correr_paso hizo justo antes.
paso4_aplicar() {
    local rc=0 certbot_rc=""

    if [[ "$P4_ESTADO" != "sin-pem" ]]; then
        orq_ssh NUEVO "sudo -n apt-get update && sudo -n env DEBIAN_FRONTEND=noninteractive apt-get install -y certbot"
        if [[ "$ORQ_RC" -ne 0 ]]; then
            volcar_error
            if [[ "$ORQ_RC" -eq 255 ]]; then
                return 8
            fi
            PASO_DETALLE="apt-get install certbot salió con $ORQ_RC."
            return 1
        fi
    fi

    # El hook: scp al staging del usuario (el paso 2 creó ~/stockapp-deploy) e install root:root 0755.
    scp -P "$NUEVO_VPS_SSH_PORT" -o BatchMode=yes -q "$TLS_HOOK_LOCAL" \
        "${NUEVO_VPS_USER}@${NUEVO_VPS_HOST}:stockapp-deploy/stockapp-tls-deploy-hook.sh" \
        9>&- </dev/null 2>/dev/null || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        if [[ "$rc" -eq 255 ]]; then
            return 8
        fi
        PASO_DETALLE="scp del hook salió con $rc."
        return 1
    fi
    orq_ssh NUEVO 'sudo -n install -m 0755 -o root -g root "$HOME/stockapp-deploy/stockapp-tls-deploy-hook.sh" '"${TLS_HOOK}"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_error
        if [[ "$ORQ_RC" -eq 255 ]]; then
            return 8
        fi
        PASO_DETALLE="no pude instalar el hook en ${TLS_HOOK} (salió con $ORQ_RC)."
        return 1
    fi

    if [[ "$P4_ESTADO" == "sin-cert" ]]; then
        # Sin mirar el exit de certbot: en la primera emisión la API todavía no existe, el
        # `systemctl restart` del hook falla y certbot sale distinto de cero aunque emitió.
        # Con -n y una cuenta ACME nueva certbot necesita --register-unsafely-without-email.
        orq_ssh NUEVO "sudo -n certbot certonly --standalone -n --agree-tos --register-unsafely-without-email -d ${NUEVO_API_HOSTNAME} --deploy-hook ${TLS_HOOK}"
        certbot_rc="$ORQ_RC"
        if [[ "$certbot_rc" -eq 255 ]]; then
            volcar_error
            return 8
        fi
        if [[ "$certbot_rc" -ne 0 ]]; then
            volcar_error
        fi
        rc=0
        paso4_hecho || rc=$?
        case "$rc" in
            0) return 0 ;;
            1) ;;
            *) return 8 ;;
        esac
        if [[ "$P4_ESTADO" == "sin-cert" ]]; then
            PASO_DETALLE="certbot salió con ${certbot_rc} y no quedó un certificado vigente con el hook registrado."
            return 1
        fi
    fi

    # El certificado existe y los PEM no (o no son los esperados): se corre el hook a mano.
    paso4_hook_a_mano || return $?
}

# ---------------------------------------------------------------------------------------
# Paso 9: prueba de la renovación (solo con --probar-renovacion, una única vez)
# ---------------------------------------------------------------------------------------

# Cuántos arranques de stockapp-api registra el journal. Un arranque posterior a la renovación
# es un recuento mayor, sin depender de relojes (el del servidor y el del journal).
PASO9_ARRANQUES='
set -u
out="$(journalctl -u stockapp-api.service --no-pager -o cat)" || exit 3
printf "%s\n" "$out" | awk "/^Started /{n++} END{print n+0}"
'

# paso9_arranques: deja P9_ARRANQUES (número). rc 0 / 8.
paso9_arranques() {
    orq_ssh NUEVO "sudo -n bash -s" "$PASO9_ARRANQUES"
    if [[ "$ORQ_RC" -ne 0 || ! "$ORQ_SALIDA" =~ ^[0-9]+$ ]]; then
        PASO_DETALLE="no pude contar los arranques de stockapp-api en el journal (salió con $ORQ_RC). ${ORQ_STDERR}"
        return 8
    fi
    P9_ARRANQUES="$ORQ_SALIDA"
}

# El marcador es lo único que no se puede observar: que la renovación ya se probó.
paso9_hecho() {
    orq_ssh NUEVO 'test -f "$HOME/stockapp-deploy/.preparacion/renovacion-probada"'
    case "$ORQ_RC" in
        0) return 0 ;;
        1) return 1 ;;
        *)
            PASO_DETALLE="no pude consultar el marcador de la renovación (salió con $ORQ_RC). ${ORQ_STDERR}"
            return 8
            ;;
    esac
}

paso9_aplicar() {
    local antes renew_rc

    paso9_arranques || return $?
    antes="$P9_ARRANQUES"

    # Forzada: gasta cupo de Let's Encrypt. El exit no decide; decide lo que se observa después.
    orq_ssh NUEVO "sudo -n certbot renew --force-renewal --cert-name ${NUEVO_API_HOSTNAME}"
    renew_rc="$ORQ_RC"
    if [[ "$renew_rc" -eq 255 ]]; then
        volcar_error
        return 8
    fi
    if [[ "$renew_rc" -ne 0 ]]; then
        volcar_error
    fi

    orq_ssh NUEVO "systemctl is-active stockapp-api"
    if [[ "$ORQ_RC" -eq 255 ]]; then
        PASO_DETALLE="${ORQ_STDERR}"
        return 8
    fi
    if [[ "$ORQ_SALIDA" != "active" ]]; then
        PASO_DETALLE="después de renovar (certbot salió con ${renew_rc}) stockapp-api no está active (dice '${ORQ_SALIDA}')."
        return 1
    fi
    paso9_arranques || return $?
    if [[ "$P9_ARRANQUES" -le "$antes" ]]; then
        PASO_DETALLE="certbot salió con ${renew_rc} y el journal no registra un arranque de stockapp-api posterior a la renovación (el hook no reinició la API)."
        return 1
    fi

    orq_ssh NUEVO 'umask 077 && install -d -m 700 "$HOME/stockapp-deploy/.preparacion" && date -u +%Y-%m-%dT%H:%M:%SZ > "$HOME/stockapp-deploy/.preparacion/renovacion-probada"'
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_error
        if [[ "$ORQ_RC" -eq 255 ]]; then
            return 8
        fi
        PASO_DETALLE="la renovación se probó pero no pude escribir el marcador (salió con $ORQ_RC)."
        return 1
    fi
}

# ---------------------------------------------------------------------------------------
# Programa principal
# ---------------------------------------------------------------------------------------

# Cada paso es el par pasoN_hecho / pasoN_aplicar; se registra acá con su nombre.
PASOS=(1 2 3 4 9)
declare -A PASOS_NOMBRE=(
    [1]="Docker"
    [2]="usuario y directorios"
    [3]="firewall"
    [4]="certificado TLS"
    [9]="renovación del certificado"
)

main() {
    local n

    # El lock se toma al inicio (los dos orquestadores comparten ~/stockapp-backups-vps/.migracion.lock),
    # salvo en --verificar: es de solo lectura, y migrar lo invoca como bloque teniendo ya el lock.
    if [[ "$VERIFICAR" -eq 0 ]]; then
        orq_lock
    fi

    paso0_prevuelo
    for n in "${PASOS[@]}"; do
        correr_paso "$n" "${PASOS_NOMBRE[$n]}"
    done

    if [[ "$VERIFICAR" -eq 1 ]]; then
        if [[ "$PENDIENTES" -gt 0 ]]; then
            echo "preparar: hay $PENDIENTES paso(s) PENDIENTE. Correr sin --verificar los aplica."
            exit 5
        fi
        echo "preparar: todos los pasos están HECHO."
        exit 0
    fi
    echo "preparar: el VPS quedó listo."
}

main
