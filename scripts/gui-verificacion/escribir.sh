#!/usr/bin/env bash
# Tecleo de texto en la ventana de la app, vía PowerShell
# System.Windows.Forms.SendKeys::SendWait, enviado desde el lado Windows.
#
# POR QUÉ NO xdotool: en más de una sesión (ver README.md / discovery
# original), `xdotool key`/`type` (protocolo XTEST) resultó NO confiable
# bajo WSLg -- los keysyms llegaban re-mapeados/desincronizados (ej.
# "BackSpace" tecleaba "d", "a b c" tecleaba "e a w"). El mouse de xdotool
# SÍ es confiable; el teclado no. Por eso el tecleo va siempre por
# SendKeys, nunca por xdotool (xdotool solo se usa, de lectura, para
# reconocer popups de la misma app).
#
# FAIL-CLOSED (2026-10-01, incidente de privacidad): SendKeys escribe en
# LA VENTANA QUE TENGA EL FOCO, sea la que sea. Por eso:
#   1. antes de empezar se verifica que la ventana exista, sea única y tenga
#      un rectángulo razonable, y se la trae al frente;
#   2. DENTRO del proceso PowerShell que envía, antes de CADA carácter, se
#      verifica que el primer plano sea esa ventana (handle exacto) o, si el
#      foco X11 es de la app, una ventana auxiliar (popup) de su mismo
#      proceso host;
#   3. si cualquier verificación falla se aborta con exit != 0 y no se envía
#      ni un carácter más (los ya enviados no se pueden deshacer: por eso se
#      chequea carácter por carácter). Códigos en lib-ventana.sh / README.md.
#
# Uso:
#   ./escribir.sh "texto a tipear" [substring-del-titulo-de-ventana=Municipal]
#
# Envía un carácter/token por vez con una pausa entre cada uno (más lento
# pero mucho más confiable que mandar el string entero de una sola vez,
# que en sesiones anteriores perdía caracteres al final).
#
# GOTCHA: el guion bajo "_" puede salir como "?" por un mismatch de
# VkKeyScan/layout de teclado en algunas sesiones. Si tenés que tipear un
# "_", probalo primero y verificalo con una captura antes de confiar en él;
# si falla, evitá el caracter en los datos de prueba.
set -euo pipefail

GUI_TAG="escribir"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib-ventana.sh
source "$SCRIPT_DIR/lib-ventana.sh"

if [[ $# -lt 1 ]]; then
    echo "Uso: $0 \"texto\" [substring-del-titulo-de-ventana=Municipal]" >&2
    exit "$EXIT_USO"
fi

TEXTO="$1"
WINDOW_TITLE="${2-Municipal}"
DELAY_MS="${ESCRIBIR_DELAY_MS:-300}"
gui_es_entero "$DELAY_MS" && (( DELAY_MS >= 0 )) || gui_abortar "$EXIT_USO" "ESCRIBIR_DELAY_MS debe ser un entero >= 0."

gui_validar_titulo "$WINDOW_TITLE"
gui_exigir_powershell
gui_cargar_xdotool   # opcional: solo habilita aceptar popups de la misma app

gui_preparar "$WINDOW_TITLE"

# Popups auxiliares (p. ej. desplegable del AutoCompleteBox que se abre mientras se tipea):
# aceptados solo si el foco X11 es de un proceso Linux que ES la app.
AUX=0
if gui_foco_x11_es_la_app; then AUX=1; fi

HOST_SEGURO="${GUI_HOST_PROC//[^A-Za-z0-9._-]/}"
TEXTO_B64="$(printf '%s' "$TEXTO" | base64 -w0)"

gui_log "tipeando ${#TEXTO} caracteres en \"$GUI_TITULO_REAL\" (hwnd $GUI_HWND, ${DELAY_MS}ms entre caracteres)..."

rc=0
gui_ps "$PS_OP_ENVIAR" \
    "\$hwnd = [IntPtr]$GUI_HWND" "\$winpid = $GUI_WINPID" "\$hostProc = '$HOST_SEGURO'" "\$aux = $AUX" \
    "\$delay = $DELAY_MS" "\$textoB64 = '$TEXTO_B64'" >/dev/null || rc=$?

case "$rc" in
    0) ;;
    5) gui_pista_recuperacion; gui_abortar "$EXIT_PRIMER_PLANO" "la ventana en primer plano dejó de ser la app: se cortó el tecleo (puede haberse enviado una parte del texto)." ;;
    *) gui_abortar "$EXIT_INTEROP" "powershell.exe falló al enviar teclas (exit $rc)." ;;
esac
