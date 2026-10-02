#!/usr/bin/env bash
# Captura de pantalla de UNA VENTANA IDENTIFICADA de la app (Avalonia) bajo WSLg.
#
# REGLA (2026-10-01, incidente de privacidad): este script NUNCA captura el escritorio. Antes,
# si no encontraba la ventana, caía a capturar todos los monitores del usuario (que tenía una
# videollamada abierta) y salía con 0. Ese modo fue ELIMINADO. Ahora, si la ventana no se
# encuentra, no es la que está al frente, o su rectángulo es sospechoso: exit != 0, ningún PNG,
# mensaje claro a stderr. Detalle de códigos de salida en lib-ventana.sh y README.md.
#
# GOTCHA CRÍTICO (ver README.md): las herramientas X11 clásicas (scrot, xwd, import, XGetImage
# sobre la ventana o el root window) devuelven pantalla NEGRA para ventanas de WSLg, porque WSLg
# compone vía RDP hacia el lado Windows. Por eso esta captura se hace desde el lado Windows, vía
# powershell.exe + System.Drawing.Graphics.CopyFromScreen, recortando SOLO el rectángulo de la
# ventana confirmada.
#
# Uso:
#   ./capturar.sh <ruta-salida.png> [substring-del-titulo-de-ventana=Municipal]
#
# La ventana de la app se llama "Gestión Municipal"; "StockApp" NO matchea. Popups de la app
# (ComboBox / AutoCompleteBox abiertos): se capturan el rectángulo de la ventana principal si el
# popup en primer plano es de la MISMA app (ver lib-ventana.sh).
set -euo pipefail

GUI_TAG="capturar"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib-ventana.sh
source "$SCRIPT_DIR/lib-ventana.sh"

if [[ $# -lt 1 ]]; then
    echo "Uso: $0 <ruta-salida.png> [substring-del-titulo-de-ventana=Municipal]" >&2
    exit "$EXIT_USO"
fi

OUTPUT_PATH="$(realpath -m "$1")"
WINDOW_TITLE="${2-Municipal}"
OUTPUT_DIR="$(dirname "$OUTPUT_PATH")"

gui_validar_titulo "$WINDOW_TITLE"
gui_exigir_powershell

# Un PNG viejo en la ruta de salida podría confundirse con una captura nueva si esta falla.
rm -f "$OUTPUT_PATH"

gui_cargar_xdotool   # opcional: solo habilita aceptar popups de la misma app
gui_preparar "$WINDOW_TITLE"

RECT_W=$((GUI_R - GUI_L))
RECT_H=$((GUI_B - GUI_T))
AUX=$GUI_FG_ES_AUX
HOST_SEGURO="${GUI_HOST_PROC//[^A-Za-z0-9._-]/}"
MARGEN="${GUI_MARGEN_SOMBRA:-32}"
gui_es_entero "$MARGEN" && (( MARGEN >= 0 )) || gui_abortar "$EXIT_USO" "GUI_MARGEN_SOMBRA debe ser un entero >= 0."
WIN_TMP_NAME="stockapp-shot-$$-$(date +%s).png"

gui_log "Capturando \"$GUI_TITULO_REAL\" (hwnd $GUI_HWND, ${RECT_W}x${RECT_H} en $GUI_L,$GUI_T)..."

rc=0
SALIDA="$(gui_ps "$PS_OP_CAPTURAR" \
    "\$hwnd = [IntPtr]$GUI_HWND" "\$winpid = $GUI_WINPID" "\$hostProc = '$HOST_SEGURO'" "\$aux = $AUX" \
    "\$rl = $GUI_L" "\$rt = $GUI_T" "\$rw = $RECT_W" "\$rh = $RECT_H" "\$margen = $MARGEN" "\$nombre = '$WIN_TMP_NAME'")" || rc=$?

case "$rc" in
    0) ;;
    4) gui_abortar "$EXIT_RECT" "el rectángulo de la ventana cambió entre la verificación y la captura." ;;
    5) gui_pista_recuperacion; gui_abortar "$EXIT_PRIMER_PLANO" "la ventana dejó de estar en primer plano justo antes de capturar." ;;
    *) gui_abortar "$EXIT_INTEROP" "powershell.exe falló al capturar (exit $rc)." ;;
esac

WIN_PATH="$(printf '%s\n' "$SALIDA" | tail -n1)"
if [[ -z "$WIN_PATH" ]]; then
    gui_abortar "$EXIT_INTEROP" "powershell.exe no devolvió una ruta de archivo. Revisá el interop WSL<->Windows."
fi

WSL_SRC_PATH="$(wslpath -u "$WIN_PATH" 2>/dev/null || true)"
if [[ -z "$WSL_SRC_PATH" || ! -f "$WSL_SRC_PATH" ]]; then
    gui_abortar "$EXIT_INTEROP" "no se pudo resolver/leer el PNG generado en Windows ($WIN_PATH)."
fi

mkdir -p "$OUTPUT_DIR"
cp "$WSL_SRC_PATH" "$OUTPUT_PATH"
rm -f "$WSL_SRC_PATH"

if [[ ! -s "$OUTPUT_PATH" ]]; then
    rm -f "$OUTPUT_PATH"
    gui_abortar "$EXIT_INTEROP" "el PNG resultante está vacío."
fi

# Chequeo simple anti-pantalla-negra: si PIL está disponible, calculamos el rango de valores de
# píxeles. Una imagen de un solo color es el síntoma del bug de scrot/X11 (o de una ventana que
# no estaba pintada). Sale con 7 (NO con 2: 2 es "ventana no encontrada").
PIL_STATUS=0
if python3 -c "import PIL" >/dev/null 2>&1; then
    python3 - "$OUTPUT_PATH" <<'PYEOF' || PIL_STATUS=7
import sys
from PIL import Image

path = sys.argv[1]
img = Image.open(path).convert("L")
lo, hi = img.getextrema()
if hi - lo < 3:
    print(f"[capturar] ADVERTENCIA: la imagen parece de un solo color (rango {lo}-{hi}). "
          f"Podria ser pantalla negra/vacia. Revisala con la herramienta Read antes de confiar en ella.", file=sys.stderr)
    sys.exit(1)
print(f"[capturar] OK: imagen con contenido variado (rango de luminancia {lo}-{hi}).", file=sys.stderr)
PYEOF
else
    echo "[capturar] python3-PIL no disponible: no se pudo chequear automáticamente que la imagen no sea negra." >&2
    echo "[capturar] Verificación manual: abrí $OUTPUT_PATH con la herramienta Read y confirmá a ojo que se ve la app." >&2
fi

gui_log "Guardado en $OUTPUT_PATH"
exit "$PIL_STATUS"
