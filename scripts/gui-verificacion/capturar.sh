#!/usr/bin/env bash
# Captura de pantalla para verificación visual de la GUI (Avalonia) bajo WSLg.
#
# GOTCHA CRÍTICO (ver README.md): las herramientas X11 clásicas (scrot, xwd,
# import, XGetImage sobre la ventana o el root window) devuelven pantalla
# NEGRA para ventanas de WSLg, porque WSLg compone vía RDP hacia el lado
# Windows y esos píxeles nunca llegan al root pixmap de X11 aunque
# `xwininfo` reporte la ventana como IsViewable con geometría correcta.
# Por eso esta captura se hace 100% desde el lado Windows, vía
# powershell.exe + System.Drawing.Graphics.CopyFromScreen.
#
# Uso:
#   ./capturar.sh <ruta-salida.png> [substring-del-titulo-de-ventana]
#
# Si se pasa un título (ej. "StockApp"), el script busca esa ventana con
# Get-Process, la restaura si está minimizada y la trae al frente antes de
# capturar solo su rectángulo. Si no se pasa título, o no se encuentra la
# ventana, captura el escritorio virtual completo (todos los monitores).
set -euo pipefail

if [[ $# -lt 1 ]]; then
    echo "Uso: $0 <ruta-salida.png> [substring-del-titulo-de-ventana]" >&2
    exit 1
fi

OUTPUT_PATH="$(realpath -m "$1")"
WINDOW_TITLE="${2:-}"
OUTPUT_DIR="$(dirname "$OUTPUT_PATH")"
mkdir -p "$OUTPUT_DIR"

if ! command -v powershell.exe >/dev/null 2>&1; then
    echo "ERROR: no se encontró powershell.exe en el PATH. Este script requiere WSL con interop habilitado hacia Windows." >&2
    exit 1
fi

WIN_TMP_NAME="stockapp-shot-$$-$(date +%s).png"

log() { echo "[capturar] $*" >&2; }

log "Capturando vía powershell.exe (System.Drawing.CopyFromScreen)..."

# El script de PowerShell:
# 1. Si se pidió un título, busca el proceso por MainWindowTitle, lo restaura
#    (ShowWindow SW_RESTORE=9, por si arrancó minimizado -- gotcha documentado
#    en el README) y lo trae al frente (SetForegroundWindow), y captura SOLO
#    su rectángulo (GetWindowRect).
# 2. Si no hay título o no se encontró la ventana, captura
#    SystemInformation::VirtualScreen (TODOS los monitores, no solo el
#    primario -- necesario porque en hosts multi-monitor la app puede estar
#    en un monitor secundario con coordenadas negativas).
powershell.exe -NoProfile -NonInteractive -Command "
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32Capture {
    [DllImport(\"user32.dll\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport(\"user32.dll\")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@

\$title = '$WINDOW_TITLE'
\$rectFound = \$false
\$x = 0; \$y = 0; \$w = 0; \$h = 0

if (\$title -ne '') {
    \$proc = Get-Process | Where-Object { \$_.MainWindowTitle -like \"*\$title*\" -and \$_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (\$proc) {
        [Win32Capture]::ShowWindow(\$proc.MainWindowHandle, 9) | Out-Null
        Start-Sleep -Milliseconds 300
        [Win32Capture]::SetForegroundWindow(\$proc.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 300
        \$rect = New-Object Win32Capture+RECT
        [Win32Capture]::GetWindowRect(\$proc.MainWindowHandle, [ref]\$rect) | Out-Null
        if (\$rect.Right -gt \$rect.Left -and \$rect.Bottom -gt \$rect.Top) {
            \$x = \$rect.Left; \$y = \$rect.Top
            \$w = \$rect.Right - \$rect.Left; \$h = \$rect.Bottom - \$rect.Top
            \$rectFound = \$true
        }
    }
}

if (-not \$rectFound) {
    \$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
    \$x = \$vs.X; \$y = \$vs.Y; \$w = \$vs.Width; \$h = \$vs.Height
}

\$bmp = New-Object System.Drawing.Bitmap(\$w, \$h)
\$g = [System.Drawing.Graphics]::FromImage(\$bmp)
\$g.CopyFromScreen(\$x, \$y, 0, 0, \$bmp.Size)
\$dest = Join-Path \$env:TEMP '$WIN_TMP_NAME'
\$bmp.Save(\$dest, [System.Drawing.Imaging.ImageFormat]::Png)
\$g.Dispose(); \$bmp.Dispose()
Write-Output \$dest
" > /tmp/capturar-winpath-$$.txt 2>&1

WIN_PATH="$(tail -n1 /tmp/capturar-winpath-$$.txt | tr -d '\r')"
rm -f /tmp/capturar-winpath-$$.txt

if [[ -z "$WIN_PATH" ]]; then
    echo "ERROR: powershell.exe no devolvió una ruta de archivo. Revisá el interop WSL<->Windows." >&2
    exit 1
fi

WSL_SRC_PATH="$(wslpath -u "$WIN_PATH" 2>/dev/null || true)"
if [[ -z "$WSL_SRC_PATH" || ! -f "$WSL_SRC_PATH" ]]; then
    echo "ERROR: no se pudo resolver/leer el PNG generado en Windows ($WIN_PATH)." >&2
    exit 1
fi

cp "$WSL_SRC_PATH" "$OUTPUT_PATH"
rm -f "$WSL_SRC_PATH"

if [[ ! -s "$OUTPUT_PATH" ]]; then
    echo "ERROR: el PNG resultante está vacío ($OUTPUT_PATH)." >&2
    exit 1
fi

# Chequeo simple anti-pantalla-negra: si PIL está disponible, calculamos el
# rango de valores de píxeles. Una imagen completamente negra (o de un solo
# color) tiene rango 0 -- eso es exactamente el síntoma del bug de scrot/X11
# que este script evita, así que si aparece acá con powershell es señal de
# que algo más está mal (ventana no encontrada, pantalla apagada, etc.).
if python3 -c "import PIL" >/dev/null 2>&1; then
    python3 - "$OUTPUT_PATH" <<'PYEOF'
import sys
from PIL import Image

path = sys.argv[1]
img = Image.open(path).convert("L")
extrema = img.getextrema()
lo, hi = extrema
if hi - lo < 3:
    print(f"[capturar] ADVERTENCIA: la imagen parece de un solo color (rango {lo}-{hi}). "
          f"Podria ser pantalla negra/vacia. Revisala con la herramienta Read antes de confiar en ella.", file=sys.stderr)
    sys.exit(2)
print(f"[capturar] OK: imagen con contenido variado (rango de luminancia {lo}-{hi}).", file=sys.stderr)
PYEOF
    PIL_STATUS=$?
else
    echo "[capturar] python3-PIL no disponible: no se pudo chequear automáticamente que la imagen no sea negra." >&2
    echo "[capturar] Verificación manual: abrí $OUTPUT_PATH con la herramienta Read y confirmá a ojo que se ve la app." >&2
    PIL_STATUS=0
fi

log "Guardado en $OUTPUT_PATH"
exit $PIL_STATUS
