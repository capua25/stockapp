#!/usr/bin/env bash
# Click de mouse en coordenadas medidas sobre una captura de VENTANA
# (la que devuelve `capturar.sh <salida.png> <titulo>` -- un recorte por
# GetWindowRect de Win32, título incluido) usando xdotool en modo
# `--window <id> X Y` (relativo al área de cliente X11 de esa ventana).
#
# GOTCHA 1 -- offset de "chrome" (ver README.md, sección "Calibrar
# click.sh"): la captura de Win32 (GetWindowRect) incluye el borde y la
# barra de título dibujados por el compositor de WSLg; el "--window" de
# xdotool en cambio es relativo al área de CLIENTE X11 (sin esa barra).
# Hay que restar un offset fijo (borde_x, borde_y_superior) de la
# coordenada medida en la imagen para obtener la coordenada real de
# xdotool. Ese offset se midió empíricamente en esta sesión como
# (38, 59) -- funcionó igual para la ventana principal y para un diálogo
# modal, pero NO es necesariamente una constante universal entre sesiones
# de WSLg distintas (puede depender de la versión de WSLg/RDP). Si los
# clicks no caen donde deberían, recalibrá con la receta del README antes
# de asumir que el offset por defecto sigue sirviendo.
#
# GOTCHA 2 -- ventana minimizada por actividad concurrente en el host
# Windows: si alguien está usando la máquina real al mismo tiempo, la
# ventana de WSLg puede terminar minimizada (sentinel Win32 L=-32000) o
# perder el foreground entre una captura y la siguiente, aun cuando xdotool
# la sigue viendo "mapeada" en X11 con geometría normal -- los clicks
# entonces caen sobre lo que sea que esté realmente en pantalla, no sobre
# la app. Por eso este script SIEMPRE restaura + trae al frente la ventana
# (vía powershell.exe, igual que capturar.sh) inmediatamente antes de cada
# click.
#
# Uso:
#   ./click.sh <x-imagen> <y-imagen> [titulo-ventana=StockApp] [boton=1]
#   CLICK_OFFSET_X=38 CLICK_OFFSET_Y=59 ./click.sh <x> <y>
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOOLKIT_DIR="${TOOLKIT_DIR:-/tmp/x11tools}"
ENV_FILE="$TOOLKIT_DIR/env.sh"

if [[ $# -lt 2 ]]; then
    echo "Uso: $0 <x-imagen> <y-imagen> [titulo-ventana=StockApp] [boton=1]" >&2
    exit 1
fi

IMG_X="$1"
IMG_Y="$2"
WINDOW_TITLE="${3:-StockApp}"
BOTON="${4:-1}"
OFFSET_X="${CLICK_OFFSET_X:-38}"
OFFSET_Y="${CLICK_OFFSET_Y:-59}"

log() { echo "[click] $*" >&2; }

if [[ ! -f "$ENV_FILE" ]]; then
    log "xdotool no está preparado todavía, corriendo setup-toolkit.sh..."
    "$SCRIPT_DIR/setup-toolkit.sh" >/dev/null
fi
# shellcheck source=/dev/null
source "$ENV_FILE"

if ! command -v powershell.exe >/dev/null 2>&1; then
    echo "ERROR: no se encontró powershell.exe en el PATH." >&2
    exit 1
fi

log "Restaurando y trayendo al frente la ventana que matchea \"$WINDOW_TITLE\"..."
powershell.exe -NoProfile -NonInteractive -Command "
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32ClickFocus {
    [DllImport(\"user32.dll\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@
\$proc = Get-Process | Where-Object { \$_.MainWindowTitle -like '*$WINDOW_TITLE*' -and \$_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not \$proc) { Write-Error \"No se encontro ventana con titulo '*$WINDOW_TITLE*'\"; exit 1 }
[Win32ClickFocus]::ShowWindow(\$proc.MainWindowHandle, 9) | Out-Null
Start-Sleep -Milliseconds 200
[Win32ClickFocus]::SetForegroundWindow(\$proc.MainWindowHandle) | Out-Null
" >/dev/null

WINDOW_ID="$("$XDOTOOL_BIN" search --name "$WINDOW_TITLE" | head -1)"
if [[ -z "$WINDOW_ID" ]]; then
    echo "ERROR: xdotool no encontró ninguna ventana X11 que matchee \"$WINDOW_TITLE\"." >&2
    exit 1
fi

CLIENT_X=$((IMG_X - OFFSET_X))
CLIENT_Y=$((IMG_Y - OFFSET_Y))

log "Ventana X11 id=$WINDOW_ID. Click en relativo ($CLIENT_X,$CLIENT_Y) = imagen ($IMG_X,$IMG_Y) - offset ($OFFSET_X,$OFFSET_Y), botón $BOTON."

"$XDOTOOL_BIN" mousemove --window "$WINDOW_ID" "$CLIENT_X" "$CLIENT_Y"
sleep 0.15
"$XDOTOOL_BIN" click "$BOTON"
