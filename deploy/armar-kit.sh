#!/usr/bin/env bash
set -euo pipefail

# armar-kit.sh — arma el pendrive de instalación. Corre en la máquina del proveedor, CON
# internet (es el único momento del proceso en que hay internet garantizado).
#
# PRINCIPIO: todo lo que pueda fallar en el municipio tiene que fallar ACÁ. Este script no se
# limita a copiar archivos: BAJA los .deb dentro de un contenedor de la versión exacta de Ubuntu
# destino, y después VERIFICA que se instalen en un contenedor limpio y SIN RED. Si eso pasa
# acá, no va a sorprender allá.
#
# Uso:  deploy/armar-kit.sh <version-del-kit> [--tarball <ruta-al-tarball-de-la-api>]
# Ejemplo:  deploy/armar-kit.sh 1.0.0
#           deploy/armar-kit.sh 1.0.0 --tarball deploy/dist/stockapp-api-1.0.0-linux-x64.tar.gz
#
# --tarball existe porque el criterio del propio plan (y de deploy/kit/02-instalar.sh) es
# ABORTAR ante ambigüedad, nunca elegir el "más reciente" en silencio: empaquetar la versión
# equivocada significa instalarla en un servidor al que no se vuelve, sin forma de saberlo.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIR_FUENTE="${REPO_ROOT}/deploy/kit"
SALIDA_BASE="${REPO_ROOT}/deploy/kit-dist"

VERSION_KIT=""
TARBALL_EXPLICITO=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --tarball)
            [[ $# -ge 2 ]] || { echo "ERROR: --tarball necesita un argumento (la ruta al tarball)." >&2; exit 1; }
            TARBALL_EXPLICITO="$2"
            shift 2
            ;;
        -*)
            echo "ERROR: opción desconocida: $1" >&2
            exit 1
            ;;
        *)
            if [[ -z "$VERSION_KIT" ]]; then
                VERSION_KIT="$1"
            else
                echo "ERROR: argumento inesperado: $1" >&2
                exit 1
            fi
            shift
            ;;
    esac
done

[[ -n "$VERSION_KIT" ]] || { echo "Uso: $0 <version-del-kit> [--tarball <ruta>]   (ej: $0 1.0.0)" >&2; exit 1; }

UBUNTU_VER="$(tr -d '[:space:]' < "${DIR_FUENTE}/VERSION")"
IMAGEN="ubuntu:${UBUNTU_VER}"
DESTINO="${SALIDA_BASE}/stockapp-kit-${VERSION_KIT}"

echo "[armar-kit] Versión del kit : ${VERSION_KIT}"
echo "[armar-kit] Ubuntu destino  : ${UBUNTU_VER}  (imagen ${IMAGEN})"

for cmd in docker tar; do
    command -v "$cmd" >/dev/null 2>&1 || { echo "ERROR: falta '${cmd}'." >&2; exit 1; }
done

# ── Artefactos que tienen que existir antes ────────────────────────────────
echo "[armar-kit] Buscando el tarball de la API..."
if [[ -n "$TARBALL_EXPLICITO" ]]; then
    if [[ ! -f "$TARBALL_EXPLICITO" ]]; then
        echo "ERROR: --tarball apunta a un archivo que no existe: ${TARBALL_EXPLICITO}" >&2
        exit 1
    fi
    NOMBRE_TARBALL="$(basename "$TARBALL_EXPLICITO")"
    if [[ ! "$NOMBRE_TARBALL" =~ ^stockapp-api-.+-linux-x64\.tar\.gz$ ]]; then
        echo "ERROR: --tarball no matchea el patrón esperado (stockapp-api-<version>-linux-x64.tar.gz):" >&2
        echo "       ${TARBALL_EXPLICITO}" >&2
        exit 1
    fi
    TARBALL_API="$TARBALL_EXPLICITO"
    echo "[armar-kit]   -> $(basename "$TARBALL_API")  (elegido con --tarball)"
else
    mapfile -t TARBALLS < <(find "${REPO_ROOT}/deploy/dist" -maxdepth 1 -name 'stockapp-api-*-linux-x64.tar.gz' 2>/dev/null | sort)
    case "${#TARBALLS[@]}" in
        0)
            echo "ERROR: no hay ningún tarball de la API en deploy/dist/." >&2
            echo "       Corré primero:  deploy/publish-api.sh ${VERSION_KIT}" >&2
            exit 1
            ;;
        1)
            TARBALL_API="${TARBALLS[0]}"
            ;;
        *)
            # Desviación #1 respecto del plan: NUNCA elegir el más reciente en silencio. Empaquetar
            # el tarball equivocado significa instalar la versión equivocada en un servidor al que
            # no se vuelve, sin forma de saberlo -- el mismo criterio que 02-instalar.sh ya aplica.
            echo "ERROR: hay ${#TARBALLS[@]} tarballs candidatos en deploy/dist/ y no sé cuál usar:" >&2
            printf '       %s\n' "${TARBALLS[@]}" >&2
            echo "       Elegí uno con --tarball <ruta>, o dejá un único tarball en deploy/dist/." >&2
            exit 1
            ;;
    esac
    echo "[armar-kit]   -> $(basename "$TARBALL_API")"
fi

echo "[armar-kit] Buscando el instalador de Windows..."
# pack-win.ps1:161 lo genera como "Setup.exe"; el diseño lo llamaba
# GestionMunicipal-win-Setup.exe. No adivinamos: buscamos cualquier *Setup.exe y exigimos uno.
mapfile -t SETUPS < <(find "${REPO_ROOT}/releases/win" -maxdepth 1 -iname '*Setup.exe' 2>/dev/null | sort)
case "${#SETUPS[@]}" in
    0) echo "ERROR: no hay ningún *Setup.exe en releases/win/." >&2
       echo "       Corré, en Windows:  .\\build\\pack-win.ps1" >&2
       exit 1 ;;
    1) SETUP_WIN="${SETUPS[0]}" ;;
    *) echo "ERROR: hay ${#SETUPS[@]} instaladores en releases/win/ y no sé cuál:" >&2
       printf '       %s\n' "${SETUPS[@]}" >&2
       echo "       Dejá uno solo." >&2
       exit 1 ;;
esac
echo "[armar-kit]   -> $(basename "$SETUP_WIN")"

# ── Estructura ─────────────────────────────────────────────────────────────
echo "[armar-kit] Armando ${DESTINO}..."
rm -rf "$DESTINO"
mkdir -p "${DESTINO}"/{servidor,offline/docker,offline/pgclient,clientes}

# Scripts del kit + librería + VERSION
install -m 0755 "${DIR_FUENTE}"/0*.sh "$DESTINO/"
mkdir -p "${DESTINO}/lib"
install -m 0755 "${DIR_FUENTE}"/lib/*.sh "${DESTINO}/lib/"
install -m 0644 "${DIR_FUENTE}/VERSION" "$DESTINO/"

# LEEME.md y clientes/LEEME-clientes.md son producto de la Task 4.2, que todavía no corrió en
# este repo. No abortamos por esto (bloquearía el build real que esta misma tarea exige
# completar), pero tampoco lo dejamos pasar en silencio: si falta, el kit se arma igual pero
# queda marcado a los gritos, arriba y en el resumen final.
FALTA_DOCUMENTACION=0
if [[ -f "${DIR_FUENTE}/LEEME.md" ]]; then
    install -m 0644 "${DIR_FUENTE}/LEEME.md" "$DESTINO/"
else
    echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" >&2
    echo "AVISO: falta deploy/kit/LEEME.md (Task 4.2 todavía no se ejecutó)." >&2
    echo "       El kit se arma IGUAL, pero sin el procedimiento del día ni el rescate." >&2
    echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" >&2
    FALTA_DOCUMENTACION=1
fi

# servidor/ = copia fiel de deploy/ (install.sh NO se modifica: viaja tal cual). Se instala por
# nombre exacto (no por glob): así el kit nunca puede terminar copiando algo que no pedimos --
# en particular, nunca puede terminar copiando deploy/.env (el archivo con los secretos reales).
install -m 0755 "${REPO_ROOT}/deploy/install.sh"           "${DESTINO}/servidor/"
install -m 0755 "${REPO_ROOT}/deploy/wait-for-postgres.sh" "${DESTINO}/servidor/"
install -m 0644 "${REPO_ROOT}/deploy/stockapp-api.service" "${DESTINO}/servidor/"
install -m 0644 "${REPO_ROOT}/deploy/docker-compose.postgres.yml" "${DESTINO}/servidor/"
install -m 0644 "${REPO_ROOT}/deploy/.env.example"         "${DESTINO}/servidor/"
install -m 0644 "${REPO_ROOT}/deploy/DEPLOY.md"            "${DESTINO}/servidor/"

# El .env REAL nunca viaja: se genera en el servidor. Guardia explícita de defensa en
# profundidad (el bloque de arriba no debería poder producir esto nunca, pero si alguien lo
# rompe en el futuro -- por ejemplo cambiando un nombre exacto por un glob -- esto lo frena acá,
# en la máquina del proveedor, en vez de que el secreto viaje al pendrive).
if [[ -e "${DESTINO}/servidor/.env" ]]; then
    echo "ERROR: se copió un .env real al kit. Eso no puede pasar." >&2
    exit 1
fi

if [[ -f "${DIR_FUENTE}/clientes/LEEME-clientes.md" ]]; then
    install -m 0644 "${DIR_FUENTE}/clientes/LEEME-clientes.md" "${DESTINO}/clientes/"
else
    echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" >&2
    echo "AVISO: falta deploy/kit/clientes/LEEME-clientes.md (Task 4.2 todavía no se ejecutó)." >&2
    echo "       El kit se arma IGUAL, pero sin la guía para quien instala las PC." >&2
    echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" >&2
    FALTA_DOCUMENTACION=1
fi

install -m 0644 "$TARBALL_API" "${DESTINO}/offline/"
install -m 0644 "$SETUP_WIN"   "${DESTINO}/clientes/"
[[ -f "${DIR_FUENTE}/clientes/configurar-cliente.cmd" ]] \
    && install -m 0644 "${DIR_FUENTE}/clientes/configurar-cliente.cmd" "${DESTINO}/clientes/"

# ── Imagen de Postgres ─────────────────────────────────────────────────────
echo "[armar-kit] Guardando la imagen postgres:16-alpine..."
docker pull postgres:16-alpine
docker save postgres:16-alpine -o "${DESTINO}/offline/postgres-16-alpine.tar"
# Desviación #2 respecto del plan: 'docker save -o' deja el permiso a merced del umask, a
# diferencia de todos los demás artefactos (que pasan por 'install -m 0644'). Lo fijamos acá para
# que el kit sea reproducible sin importar el umask de quien lo arma.
chmod 0644 "${DESTINO}/offline/postgres-16-alpine.tar"

# ── .deb, bajados DENTRO de la imagen de la versión destino ────────────────
# POR QUÉ en contenedor y no en el host: este host es WSL2, NO es el Ubuntu Server destino.
# 'apt-get download' acá resolvería contra los repos y la versión de ESTA distro, y el set
# saldría silenciosamente equivocado -- para descubrirlo en el municipio, sin internet.
echo "[armar-kit] Bajando los .deb de Docker dentro de ${IMAGEN}..."
docker run --rm -v "${DESTINO}/offline/docker:/salida" "$IMAGEN" bash -c '
    set -euo pipefail
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl gnupg >/dev/null
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
        | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
    . /etc/os-release
    echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
        > /etc/apt/sources.list.d/docker.list
    apt-get update -qq
    cd /salida
    # --reinstall fuerza la descarga aunque algo ya esté presente en la imagen base.
    #
    # La lista de paquetes "extra" al final (libgssapi-krb5-2 en adelante) son la CLAUSURA
    # DE EJECUCIÓN REAL de curl y de postgresql-client-16 (libpq5 en rigor): son shared libraries
    # que apt NO baja con --download-only porque asume que ya están presentes -- cierto en un
    # Ubuntu Server real ("Priority: important", vienen en cualquier instalación estándar), FALSO
    # en la imagen ubuntu:24.04 "pelada" de Docker Hub que usan tanto este script como la
    # verificación sin red de más abajo.
    #
    # OJO -- esto NO se descubre con "command -v" ni mirando si dpkg terminó con errores: un
    # "apt-get install --download-only" corto de cierre y un "dpkg --configure -a" con errores
    # cosméticos en paquetes que ni usamos (docker-ce-rootless-extras, systemd-resolved -- piden
    # un D-Bus/systemd VIVOS que un contenedor efímero nunca tiene) dejan el BINARIO en el disco
    # igual, pero roto: "pg_isready: error while loading shared libraries: libgssapi_krb5.so.2".
    # Se encontró ejecutando pg_isready/pg_dump/curl de verdad (no solo "command -v") contra el
    # payload -- por eso la verificación de más abajo también corre "--version" de cada uno.
    #
    # No se agrega "adduser" ni "ubuntu-minimal": una ronda con ubuntu-minimal SÍ resuelve
    # adduser, pero arrastra 150+ paquetes irrelevantes (netplan, rsyslog, ubuntu-pro-client,
    # python3 completo) y aun así deja "dependency problems" cosméticos -- ninguno de los 4
    # binarios que el kit necesita depende de adduser para EJECUTAR, solo para que dpkg lo
    # anote como "configurado" en su base de datos, cosa que a este script no le importa.
    apt-get install -y --reinstall --download-only \
        docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin \
        libgssapi-krb5-2 libkrb5-3 libk5crypto3 libkrb5support0 libldap2 libsasl2-2 \
        libssh-4 libnghttp2-14 libpsl5t64 librtmp1 libbrotli1 libcurl4t64 libkeyutils1
    cp /var/cache/apt/archives/*.deb /salida/
    ls -1 /salida/*.deb | wc -l'
# Autoguardia: 'apt-get install --download-only' puede salir 0 sin que el bind mount termine con
# nada adentro (visto en este mismo build: el 'docker run' volvía a 0 con offline/docker/ vacío,
# sin ningún error visible). No confiamos en el exit code solo -- confirmamos contra el
# FILESYSTEM DEL HOST, que es la verdad de lo que realmente va a viajar en el pendrive.
NDEBS_DOCKER=$(find "${DESTINO}/offline/docker" -maxdepth 1 -name '*.deb' | wc -l)
[[ "$NDEBS_DOCKER" -gt 0 ]] || {
    echo "ERROR: la descarga de .deb de Docker no dejó ningún archivo en offline/docker/." >&2
    echo "       El 'docker run' de la descarga terminó bien pero no produjo nada -- volvé a" >&2
    echo "       correr armar-kit.sh; si se repite, revisá el estado del daemon de Docker." >&2
    exit 1
}
echo "[armar-kit]   -> ${NDEBS_DOCKER} .deb de Docker"

echo "[armar-kit] Bajando los .deb de postgresql-client-16 y curl dentro de ${IMAGEN}..."
docker run --rm -v "${DESTINO}/offline/pgclient:/salida" "$IMAGEN" bash -c '
    set -euo pipefail
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl gnupg >/dev/null
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
        | gpg --dearmor -o /etc/apt/keyrings/pgdg.gpg
    . /etc/os-release
    echo "deb [signed-by=/etc/apt/keyrings/pgdg.gpg] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list
    apt-get update -qq
    cd /salida
    apt-get install -y --reinstall --download-only postgresql-client-16 curl
    cp /var/cache/apt/archives/*.deb /salida/
    ls -1 /salida/*.deb | wc -l'
NDEBS_PGCLIENT=$(find "${DESTINO}/offline/pgclient" -maxdepth 1 -name '*.deb' | wc -l)
[[ "$NDEBS_PGCLIENT" -gt 0 ]] || {
    echo "ERROR: la descarga de .deb de postgresql-client-16/curl no dejó ningún archivo en offline/pgclient/." >&2
    exit 1
}
echo "[armar-kit]   -> ${NDEBS_PGCLIENT} .deb de postgresql-client-16/curl"

# ── VERIFICACIÓN del payload: instalarlo SIN RED ───────────────────────────
# Acá es donde este script gana su sueldo. --network none garantiza que si falta una
# dependencia, falla -- no la baja por atrás y nos miente.
echo "[armar-kit] VERIFICANDO el payload offline (contenedor limpio, SIN RED)..."
docker run --rm --network none -e DEBIAN_FRONTEND=noninteractive \
    -v "${DESTINO}/offline:/offline:ro" "$IMAGEN" bash -c '
    set -euo pipefail
    echo "  Instalando los .deb del kit (Docker + postgresql-client-16 + curl)..."
    # dpkg -i instala en el orden en que el glob expande los archivos, sin resolver el ORDEN de
    # dependencias (a diferencia de apt): un paquete puede "unpack"-earse antes que su Depends y
    # queda "unconfigured" (no porque falte el .deb, sino porque dpkg -i no reintenta) -- eso lo
    # arregla "dpkg --configure -a". Pero un Pre-Depends (systemd-sysv->systemd,
    # python3-minimal->python3.12-minimal) es más estricto: dpkg ni siquiera DESEMPAQUETA el
    # paquete si el Pre-Depends no está ya CONFIGURADO, así que "dpkg --configure -a" no tiene
    # nada que configurar para esos -- hace falta una SEGUNDA pasada de "dpkg -i" (ahora que la
    # primera "dpkg --configure -a" ya configuró systemd/python3.12-minimal) para que los
    # paquetes bloqueados por Pre-Depends puedan desempaquetarse recién ahí. Verificado por
    # mutación (Paso 6, build real): con una sola pasada de "dpkg -i" + "dpkg --configure -a",
    # python3-minimal/python3/systemd-sysv (y todo lo que depende de ellos) quedaban sin instalar.
    dpkg -i /offline/docker/*.deb /offline/pgclient/*.deb || true
    dpkg --configure -a || true
    dpkg -i /offline/docker/*.deb /offline/pgclient/*.deb || true
    # El "|| true" de acá también es a propósito: docker-ce-rootless-extras, systemd-resolved,
    # libpam-systemd, etc. piden un D-Bus/systemd VIVOS que un "docker run" efímero nunca tiene
    # -- van a quedar "dependency problems - leaving unconfigured" SIEMPRE, y no nos importa: no
    # son binarios que el kit necesita. El gate real de esta verificación NO es "dpkg terminó sin
    # errores" (ese criterio es más estricto que lo que un Ubuntu Server real puede garantizar
    # dentro de un contenedor de prueba) -- es que los CUATRO binarios de abajo EXISTAN Y
    # CORRAN. "command -v" no alcanza: un binario puede estar en el PATH y aun así estar roto por
    # una shared library que falta ("error while loading shared libraries") -- exactamente lo que
    # pasó acá con pg_isready/pg_dump/curl antes de agregar la clausura de libs de más arriba.
    dpkg --configure -a || true
    docker     --version >/dev/null || { echo "FALLO: docker no corre"; exit 1; }
    pg_isready --version >/dev/null || { echo "FALLO: pg_isready no corre"; exit 1; }
    pg_dump    --version >/dev/null || { echo "FALLO: pg_dump no corre"; exit 1; }
    curl       --version >/dev/null || { echo "FALLO: curl no corre"; exit 1; }
    echo "  OK: el payload offline se instala sin red y deja los 4 binarios funcionando."' \
    || {
        echo "ERROR: el payload offline NO se instala en un contenedor limpio sin red." >&2
        echo "       Esto es exactamente el fallo que el kit no puede tener en el municipio." >&2
        echo "       Revisá qué dependencia falta y agregala al set de .deb." >&2
        exit 1
    }

# ── Empaquetado ────────────────────────────────────────────────────────────
echo "[armar-kit] Empaquetando..."
TAR_KIT="${SALIDA_BASE}/stockapp-kit-${VERSION_KIT}.tar.gz"
rm -f "$TAR_KIT"
tar -czf "$TAR_KIT" -C "$SALIDA_BASE" "stockapp-kit-${VERSION_KIT}"

echo
echo "[armar-kit] OK."
echo "[armar-kit]   Directorio: ${DESTINO}"
echo "[armar-kit]   Tarball   : ${TAR_KIT}"
echo "[armar-kit]   Tamaño    : $(du -sh "$DESTINO" | cut -f1)"
if [[ "$FALTA_DOCUMENTACION" -eq 1 ]]; then
    echo
    echo "[armar-kit] AVISO: falta documentación de la Task 4.2 (ver arriba). NO lo lleves al"
    echo "[armar-kit] municipio así."
fi
echo
echo "[armar-kit] Copiá el DIRECTORIO al pendrive (no el tarball: en el servidor no querés"
echo "[armar-kit] depender de poder descomprimir nada)."
echo
echo "[armar-kit] ANTES DE IR AL MUNICIPIO: ensayá el kit completo en una VM limpia."
echo "[armar-kit] Un kit que nunca se probó en una máquina limpia no es un kit."
