#!/usr/bin/env bash
# Obtiene xdotool sin acceso root, extrayéndolo del .deb de Ubuntu con
# `apt-get download` + `dpkg-deb -x` (no requiere sudo ni instalar nada
# a nivel de sistema). Idempotente: si el binario ya está listo y
# funciona, no hace nada.
#
# Contexto completo del método (por qué esto hace falta, y por qué las
# capturas de pantalla NO usan estas mismas herramientas X11) está en
# scripts/gui-verificacion/README.md.
set -euo pipefail

TOOLKIT_DIR="${TOOLKIT_DIR:-/tmp/x11tools}"
EXTRACT_DIR="$TOOLKIT_DIR/extracted"
LIB_DIR="$EXTRACT_DIR/usr/lib/x86_64-linux-gnu"
XDOTOOL_BIN="$EXTRACT_DIR/usr/bin/xdotool"
ENV_FILE="$TOOLKIT_DIR/env.sh"

log() { echo "[setup-toolkit] $*" >&2; }

check_xdotool() {
    [[ -x "$XDOTOOL_BIN" ]] || return 1
    LD_LIBRARY_PATH="$LIB_DIR" "$XDOTOOL_BIN" --version >/dev/null 2>&1
}

if check_xdotool; then
    log "xdotool ya está listo en $XDOTOOL_BIN (nada que hacer)."
    echo "$ENV_FILE"
    exit 0
fi

log "xdotool no está disponible o no funciona. Extrayendo del .deb sin root..."

if ! command -v apt-get >/dev/null 2>&1; then
    log "ERROR: no se encontró apt-get. Este script asume Ubuntu/Debian."
    log "Alternativa manual: conseguir un binario de xdotool estático o compilarlo."
    exit 1
fi

if ! command -v dpkg-deb >/dev/null 2>&1; then
    log "ERROR: no se encontró dpkg-deb (debería venir con dpkg base). Sin esto no se puede extraer el .deb sin root."
    exit 1
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

log "Descargando xdotool y libxdo3 (apt-get download, sin root)..."
if ! (cd "$WORK_DIR" && apt-get download xdotool libxdo3) ; then
    log "ERROR: apt-get download falló. Revisá conectividad de red o que el paquete exista"
    log "para tu release (apt-cache policy xdotool)."
    exit 1
fi

DEB_COUNT=$(find "$WORK_DIR" -maxdepth 1 -name '*.deb' | wc -l)
if [[ "$DEB_COUNT" -ne 2 ]]; then
    log "ERROR: se esperaban 2 archivos .deb (xdotool, libxdo3) y se encontraron $DEB_COUNT."
    exit 1
fi

mkdir -p "$EXTRACT_DIR"
log "Extrayendo .deb con dpkg-deb -x (no instala nada a nivel de sistema)..."
for deb in "$WORK_DIR"/*.deb; do
    dpkg-deb -x "$deb" "$EXTRACT_DIR"
done

if ! check_xdotool; then
    log "ERROR: xdotool se extrajo pero no corre. Puede faltar otra librería."
    log "Probá: LD_LIBRARY_PATH=$LIB_DIR ldd $XDOTOOL_BIN"
    log "y extraé del mismo modo (apt-get download <paquete> && dpkg-deb -x <paquete>.deb $EXTRACT_DIR) lo que falte."
    exit 1
fi

cat > "$ENV_FILE" <<EOF
# Generado por setup-toolkit.sh — sourceá este archivo para usar xdotool.
export XDOTOOL_BIN="$XDOTOOL_BIN"
export LD_LIBRARY_PATH="$LIB_DIR\${LD_LIBRARY_PATH:+:\$LD_LIBRARY_PATH}"
EOF

log "Listo. xdotool funcional en $XDOTOOL_BIN"
log "Otros scripts de este toolkit (click.sh) lo usan automáticamente."
echo "$ENV_FILE"
