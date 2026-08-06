#!/usr/bin/env bash
# Tecleo de texto en la ventana de la app, vía PowerShell
# System.Windows.Forms.SendKeys::SendWait, enviado desde el lado Windows.
#
# POR QUÉ NO xdotool: en más de una sesión (ver README.md / discovery
# original), `xdotool key`/`type` (protocolo XTEST) resultó NO confiable
# bajo WSLg -- los keysyms llegaban re-mapeados/desincronizados (ej.
# "BackSpace" tecleaba "d", "a b c" tecleaba "e a w"). El mouse de xdotool
# SÍ es confiable; el teclado no. Por eso el tecleo va siempre por
# SendKeys, nunca por xdotool.
#
# Uso:
#   ./escribir.sh "texto a tipear" [substring-del-titulo-de-ventana]
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

if [[ $# -lt 1 ]]; then
    echo "Uso: $0 \"texto\" [substring-del-titulo-de-ventana]" >&2
    exit 1
fi

TEXTO="$1"
WINDOW_TITLE="${2:-StockApp}"
DELAY_MS="${ESCRIBIR_DELAY_MS:-300}"

if ! command -v powershell.exe >/dev/null 2>&1; then
    echo "ERROR: no se encontró powershell.exe en el PATH." >&2
    exit 1
fi

echo "[escribir] tipeando \"$TEXTO\" en ventana que matchea \"$WINDOW_TITLE\" (${DELAY_MS}ms entre caracteres)..." >&2

powershell.exe -NoProfile -NonInteractive -Command "
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32Focus {
    [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport(\"user32.dll\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@

\$title = '$WINDOW_TITLE'
\$proc = Get-Process | Where-Object { \$_.MainWindowTitle -like \"*\$title*\" -and \$_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not \$proc) {
    Write-Error \"No se encontro ninguna ventana con titulo que matchee '\$title'\"
    exit 1
}
[Win32Focus]::ShowWindow(\$proc.MainWindowHandle, 9) | Out-Null
[Win32Focus]::SetForegroundWindow(\$proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300

\$texto = @'
$TEXTO
'@
# SendKeys.SendWait interpreta +^%~(){} como especiales -- escapamos los
# caracteres literales comunes envolviéndolos en llaves. No cubre TODO el
# alfabeto de SendKeys (ver doc oficial) pero sí lo típico en datos de
# prueba (usuarios, contraseñas, texto libre simple).
\$especiales = '+^%~(){}[]'
foreach (\$ch in \$texto.ToCharArray()) {
    \$tok = [string]\$ch
    if (\$especiales.IndexOf(\$ch) -ge 0) {
        \$tok = '{' + \$ch + '}'
    }
    [System.Windows.Forms.SendKeys]::SendWait(\$tok)
    Start-Sleep -Milliseconds $DELAY_MS
}
"
