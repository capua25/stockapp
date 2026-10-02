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
# la app. Por eso este script restaura + trae al frente la ventana
# (vía powershell.exe, igual que capturar.sh) antes de cada click.
#
# FAIL-CLOSED (2026-10-01, incidente de privacidad): el click solo se manda si
#   1. la ventana existe, es única y su rectángulo es razonable;
#   2. es la ventana en primer plano (handle exacto, o un popup de la MISMA
#      app: ver lib-ventana.sh);
#   3. el punto cae DENTRO del rectángulo de la ventana (un click fuera se
#      rechaza);
#   4. esa verificación (gui_confirmar, al final de gui_preparar) se hace
#      inmediatamente antes de enviar; mousemove + click salen en UNA sola
#      invocación de xdotool, sin hueco entre ambos.
# Si cualquiera falla: exit != 0 SIN mover el mouse ni clickear. Códigos de
# salida en lib-ventana.sh / README.md.
#
# Uso:
#   ./click.sh <x-imagen> <y-imagen> [titulo-ventana=Municipal] [boton=1]
#   CLICK_OFFSET_X=38 CLICK_OFFSET_Y=59 ./click.sh <x> <y>
set -euo pipefail

GUI_TAG="click"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib-ventana.sh
source "$SCRIPT_DIR/lib-ventana.sh"

if [[ $# -lt 2 ]]; then
    echo "Uso: $0 <x-imagen> <y-imagen> [titulo-ventana=Municipal] [boton=1]" >&2
    exit "$EXIT_USO"
fi

IMG_X="$1"
IMG_Y="$2"
WINDOW_TITLE="${3-Municipal}"
BOTON="${4:-1}"
OFFSET_X="${CLICK_OFFSET_X:-38}"
OFFSET_Y="${CLICK_OFFSET_Y:-59}"

for v in "$IMG_X" "$IMG_Y" "$OFFSET_X" "$OFFSET_Y" "$BOTON"; do
    gui_es_entero "$v" || gui_abortar "$EXIT_USO" "argumento no numérico: \"$v\"."
done

gui_validar_titulo "$WINDOW_TITLE"
gui_exigir_powershell
gui_cargar_xdotool instalar
[[ -n "$GUI_XDOTOOL" ]] || gui_abortar "$EXIT_USO" "no hay xdotool disponible (setup-toolkit.sh falló)."

gui_log "Verificando y trayendo al frente la ventana que matchea \"$WINDOW_TITLE\"..."
gui_preparar "$WINDOW_TITLE"

if [[ -z "$GUI_X11_ID" ]]; then
    gui_abortar "$EXIT_NO_ENCONTRADA" "xdotool no encontró UNA ventana X11 inequívoca que matchee \"$WINDOW_TITLE\"$( ((GUI_X11_AMBIGUO)) && echo " (hay varias de procesos distintos)")."
fi

# El punto se mide sobre una captura de la ventana: tiene que caer dentro de su rectángulo.
verificar_punto_en_ventana() {
    local w=$((GUI_R - GUI_L)) h=$((GUI_B - GUI_T))
    if (( IMG_X < 0 || IMG_Y < 0 || IMG_X >= w || IMG_Y >= h )); then
        gui_abortar "$EXIT_RECT" "el punto ($IMG_X,$IMG_Y) está FUERA de la ventana (${w}x${h}). Medí las coordenadas sobre una captura de capturar.sh."
    fi
}
verificar_punto_en_ventana

CLIENT_X=$((IMG_X - OFFSET_X))
CLIENT_Y=$((IMG_Y - OFFSET_Y))
if (( CLIENT_X < 0 || CLIENT_Y < 0 )); then
    gui_abortar "$EXIT_RECT" "el punto ($IMG_X,$IMG_Y) cae sobre el borde/barra de título (offset $OFFSET_X,$OFFSET_Y), fuera del área de cliente."
fi

gui_log "Ventana X11 id=$GUI_X11_ID. Click en relativo ($CLIENT_X,$CLIENT_Y) = imagen ($IMG_X,$IMG_Y) - offset ($OFFSET_X,$OFFSET_Y), botón $BOTON."

# mousemove + pausa + click en UNA sola invocación (sin hueco entre ambos).
"$GUI_XDOTOOL" mousemove --window "$GUI_X11_ID" "$CLIENT_X" "$CLIENT_Y" sleep 0.15 click "$BOTON"
