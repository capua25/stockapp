#!/usr/bin/env bash
# Librería compartida (se SOURCEA, no se ejecuta) de capturar.sh / click.sh / escribir.sh.
#
# REGLA DE ORO DEL TOOLKIT (2026-10-01, incidente de privacidad): NUNCA se captura el
# escritorio ni se manda input sin una ventana CONFIRMADA. Todo es fail-closed: ante la duda,
# se aborta con exit != 0 sin generar PNG y sin enviar una sola tecla/click.
#
# Esta librería concentra la parte delicada:
#   - Enumera las ventanas de nivel superior con EnumWindows (handle, PID, proceso, rectángulo,
#     título). NO usa Get-Process.MainWindowTitle: msrdc.exe (el proceso que muestra TODAS las
#     ventanas de WSLg) expone un solo MainWindowTitle, que cambia al abrirse un popup o un
#     diálogo, y por eso la ventana "dejaba de matchear Municipal".
#   - La decisión (¿existe?, ¿es única?, ¿es la que está al frente?, ¿el rectángulo es
#     razonable?) se toma ACÁ, en bash, sobre hechos que devuelve PowerShell. Así los tests
#     pueden stubear powershell.exe con hechos canned y ejercitar la lógica real.
#   - El chequeo de primer plano se repite DENTRO del propio proceso PowerShell que captura o
#     envía, inmediatamente antes de la acción (ver PS_OP_CAPTURAR / PS_OP_ENVIAR).
#
# POPUPS DE AVALONIA (ComboBox, AutoCompleteBox): se dibujan como ventanas X11 aparte, y WSLg
# las muestra como ventanas Windows aparte. Verificado empíricamente: TODAS las ventanas de
# WSLg (de cualquier app Linux) pertenecen al MISMO proceso Windows (msrdc.exe), así que el PID
# del lado Windows no distingue una app de otra. El PID que sí las distingue es el de Linux,
# que se lee por X11 (_NET_WM_PID, `xdotool getwindowpid`). Regla: una ventana en primer plano
# que no sea la principal se acepta SOLO si (a) es del mismo proceso host (msrdc) que la
# principal Y (b) la ventana X11 con foco pertenece al PID de Linux de la app. Si no se puede
# demostrar (sin xdotool, sin foco X11, PID distinto) se falla cerrado.
#
# Códigos de salida (los mismos en los tres scripts):
#   0  OK
#   1  uso incorrecto / entorno (falta powershell.exe, título vacío, argumentos inválidos)
#   2  no se encontró la ventana (o desapareció antes de actuar)
#   3  ventana ambigua (más de una coincide: no se adivina cuál)
#   4  rectángulo sospechoso, ventana minimizada, o click fuera de la ventana
#   5  la ventana en primer plano NO es la app (no se envía/captura nada)
#   6  falló el interop con PowerShell / respuesta ilegible
#   7  (solo capturar.sh) el PNG salió de un solo color; el archivo queda pero no es confiable

GUI_HOST_PROC="${GUI_HOST_PROC:-msrdc}"
GUI_TAG="${GUI_TAG:-gui}"

EXIT_USO=1
EXIT_NO_ENCONTRADA=2
EXIT_AMBIGUA=3
EXIT_RECT=4
EXIT_PRIMER_PLANO=5
EXIT_INTEROP=6

gui_log() { echo "[$GUI_TAG] $*" >&2; }

# gui_abortar <codigo> <mensaje...>: único camino de salida con error. NO usar dentro de $(...)
# (el exit moriría en el subshell).
gui_abortar() {
    local codigo="$1"
    shift
    echo "[$GUI_TAG] ERROR: $*" >&2
    echo "[$GUI_TAG] No se capturó ni se envió NADA (fail-closed, código $codigo)." >&2
    exit "$codigo"
}

gui_pista_recuperacion() {
    echo "[$GUI_TAG]   Pistas: ¿hay un popup, desplegable o diálogo abierto? Cerralo con Escape y reintentá." >&2
    echo "[$GUI_TAG]          ¿La app sigue corriendo? ¿El título es el correcto? (la ventana se llama \"Gestión Municipal\"; usá \"Municipal\")." >&2
}

gui_validar_titulo() {
    local titulo="$1"
    if [[ -z "${titulo//[[:space:]]/}" ]]; then
        gui_abortar "$EXIT_USO" "el título de ventana no puede ser vacío (un título vacío matchearía cualquier ventana)."
    fi
}

gui_exigir_powershell() {
    command -v powershell.exe >/dev/null 2>&1 ||
        gui_abortar "$EXIT_USO" "no se encontró powershell.exe en el PATH. Este toolkit requiere WSL con interop habilitado hacia Windows."
}

gui_es_entero() { [[ "$1" =~ ^-?[0-9]+$ ]]; }

# ---------------------------------------------------------------------------------------
# PowerShell: tipos Win32 compartidos. Cada operación es este preámbulo + un cuerpo, en un
# solo proceso. La primera línea del cuerpo es un marcador "# GUI_OP=<op>" (los tests lo usan
# para saber qué operación se pidió).
# ---------------------------------------------------------------------------------------
read -r -d '' PS_TIPOS <<'PSEOF' || true
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class GuiWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    public static uint PidDe(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
    public static List<string> Todas() {
        var r = new List<string>();
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, 512);
            if (sb.Length == 0) return true;
            RECT rc; GetWindowRect(h, out rc);
            r.Add(h.ToInt64() + "|" + PidDe(h) + "|" + rc.Left + "|" + rc.Top + "|" + rc.Right + "|" + rc.Bottom + "|" + (IsIconic(h) ? 1 : 0) + "|" + sb);
            return true;
        }, IntPtr.Zero);
        return r;
    }
}
'@
function GuiNombreProc([int]$p) { try { (Get-Process -Id $p -ErrorAction Stop).ProcessName } catch { '?' } }
# Foreground aceptable: el handle exacto de la ventana principal, o (solo si $aux=1) una ventana
# del mismo proceso host (misma app WSLg; el cruce con el PID de Linux lo hizo bash por X11).
function GuiForegroundOk($hwnd, [int]$winpid, [string]$hostProc, [int]$aux) {
    $fg = [GuiWin]::GetForegroundWindow()
    if ($fg.ToInt64() -eq $hwnd.ToInt64()) { return $true }
    if ($aux -ne 1) { return $false }
    $p = [int][GuiWin]::PidDe($fg)
    return (($p -eq $winpid) -and ((GuiNombreProc $p) -eq $hostProc))
}
PSEOF

read -r -d '' PS_OP_LISTAR <<'PSEOF' || true
# GUI_OP=listar
$nombres = @{}
Get-Process | ForEach-Object { $nombres[[int]$_.Id] = $_.ProcessName }
foreach ($l in [GuiWin]::Todas()) {
    $p = $l.Split('|', 8)
    $n = $nombres[[int]$p[1]]; if (-not $n) { $n = '?' }
    'W|' + $p[0] + '|' + $p[1] + '|' + $n + '|' + $p[2] + '|' + $p[3] + '|' + $p[4] + '|' + $p[5] + '|' + $p[6] + '|' + $p[7]
}
$fg = [GuiWin]::GetForegroundWindow()
$fp = [int][GuiWin]::PidDe($fg)
'F|' + $fg.ToInt64() + '|' + $fp + '|' + (GuiNombreProc $fp)
'V|' + [GuiWin]::GetSystemMetrics(76) + '|' + [GuiWin]::GetSystemMetrics(77) + '|' + [GuiWin]::GetSystemMetrics(78) + '|' + [GuiWin]::GetSystemMetrics(79)
PSEOF

read -r -d '' PS_OP_FOCO <<'PSEOF' || true
# GUI_OP=foco
# SW_RESTORE (9) solo si está minimizada; si no, SW_SHOW (5): activa SIN cambiar tamaño ni
# des-maximizar. El retorno de SetForegroundWindow NO es confiable (puede dar False y haber
# funcionado): el éxito se verifica releyendo el foreground, no acá.
if ([GuiWin]::IsIconic($hwnd)) { [GuiWin]::ShowWindow($hwnd, 9) | Out-Null } else { [GuiWin]::ShowWindow($hwnd, 5) | Out-Null }
Start-Sleep -Milliseconds 300
# Un ALT sintético levanta el "foreground lock" de Windows (si no, SetForegroundWindow se ignora
# cuando el usuario está usando otra app). Si aun así no toma el foco, el chequeo posterior falla.
[GuiWin]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
[GuiWin]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
[GuiWin]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 300
PSEOF

read -r -d '' PS_OP_CAPTURAR <<'PSEOF' || true
# GUI_OP=capturar
# Último chequeo, en el mismo proceso que captura: primer plano y rectángulo.
if (-not (GuiForegroundOk $hwnd $winpid $hostProc $aux)) { [Console]::Error.WriteLine('GUI_ABORT_PRIMER_PLANO'); exit 5 }
$rect = New-Object GuiWin+RECT
[GuiWin]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
if (($rect.Left -ne $rl) -or ($rect.Top -ne $rt) -or (($rect.Right - $rect.Left) -ne $rw) -or (($rect.Bottom - $rect.Top) -ne $rh)) {
    [Console]::Error.WriteLine('GUI_ABORT_RECT'); exit 4
}
$bmp = New-Object System.Drawing.Bitmap($rw, $rh)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rl, $rt, 0, 0, $bmp.Size)
# El rectángulo de una ventana de WSLg incluye ~32 px de margen (sombra) alrededor de la app, donde
# se ven los píxeles de LO QUE HAYA DETRÁS (otras ventanas del usuario). Se pintan de gris sin
# mover coordenadas (click.sh calibra sobre este mismo sistema de coordenadas).
if (($margen -gt 0) -and ($rw -gt (2 * $margen)) -and ($rh -gt (2 * $margen))) {
    $br = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(128, 128, 128))
    $g.FillRectangle($br, 0, 0, $rw, $margen)
    $g.FillRectangle($br, 0, $rh - $margen, $rw, $margen)
    $g.FillRectangle($br, 0, 0, $margen, $rh)
    $g.FillRectangle($br, $rw - $margen, 0, $margen, $rh)
    $br.Dispose()
}
$dest = Join-Path $env:TEMP $nombre
$bmp.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output $dest
PSEOF

read -r -d '' PS_OP_ENVIAR <<'PSEOF' || true
# GUI_OP=enviar
Add-Type -AssemblyName System.Windows.Forms
$texto = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($textoB64))
$especiales = '+^%~(){}[]'
foreach ($ch in $texto.ToCharArray()) {
    # Antes de CADA carácter: si el primer plano dejó de ser la app, se corta acá.
    if (-not (GuiForegroundOk $hwnd $winpid $hostProc $aux)) { [Console]::Error.WriteLine('GUI_ABORT_PRIMER_PLANO'); exit 5 }
    $tok = [string]$ch
    if ($especiales.IndexOf($ch) -ge 0) { $tok = '{' + $ch + '}' }
    [System.Windows.Forms.SendKeys]::SendWait($tok)
    Start-Sleep -Milliseconds $delay
}
PSEOF

# gui_ps <op-body> [asignaciones-PowerShell...]: corre preámbulo + asignaciones + cuerpo.
# Solo se interpolan valores validados por bash (enteros, nombre de proceso saneado, base64).
gui_ps() {
    local cuerpo="$1"
    shift
    local asignaciones=""
    local a
    for a in "$@"; do asignaciones+="$a"$'\n'; done
    powershell.exe -NoProfile -NonInteractive -Command "$PS_TIPOS
$asignaciones$cuerpo" 2>/dev/null | tr -d '\r'
    return "${PIPESTATUS[0]}"
}

# ---------------------------------------------------------------------------------------
# Estado (globales, porque no se puede abortar desde un subshell)
# ---------------------------------------------------------------------------------------
GUI_W_LINEAS=()
GUI_FG_HWND="" GUI_FG_PID="" GUI_FG_PROC=""
GUI_VX="" GUI_VY="" GUI_VW="" GUI_VH=""
GUI_HWND="" GUI_WINPID="" GUI_L="" GUI_T="" GUI_R="" GUI_B="" GUI_ICONICA=0 GUI_TITULO_REAL=""
GUI_XDOTOOL=""
GUI_X11_ID="" GUI_APP_PID="" GUI_X11_AMBIGUO=0

# gui_listar: llena W_LINEAS, FG_*, V*. Aborta (6) si la respuesta es ilegible.
gui_listar() {
    local salida rc=0
    salida="$(gui_ps "$PS_OP_LISTAR")" || rc=$?
    if (( rc != 0 )) || [[ "$salida" != *$'\nV|'* && "$salida" != V\|* ]]; then
        gui_abortar "$EXIT_INTEROP" "no se pudo enumerar las ventanas de Windows vía powershell.exe (exit $rc). Revisá el interop WSL<->Windows."
    fi
    GUI_W_LINEAS=()
    GUI_FG_HWND="" GUI_FG_PID="" GUI_FG_PROC=""
    GUI_VX="" GUI_VY="" GUI_VW="" GUI_VH=""
    local linea tag a b c d
    while IFS= read -r linea; do
        case "$linea" in
            W\|*) GUI_W_LINEAS+=("$linea") ;;
            F\|*) IFS='|' read -r tag GUI_FG_HWND GUI_FG_PID GUI_FG_PROC <<<"$linea" ;;
            V\|*) IFS='|' read -r tag GUI_VX GUI_VY GUI_VW GUI_VH <<<"$linea" ;;
        esac
    done <<<"$salida"
    local n
    for n in "$GUI_FG_HWND" "$GUI_FG_PID" "$GUI_VX" "$GUI_VY" "$GUI_VW" "$GUI_VH"; do
        gui_es_entero "$n" || gui_abortar "$EXIT_INTEROP" "respuesta ilegible de powershell.exe (campo no numérico: \"$n\")."
    done
}

# gui_localizar <titulo> [hwnd-esperado]: elige LA ventana. Debe haber exactamente una de la
# app host (msrdc) cuyo título contenga <titulo> (sin distinguir mayúsculas). Si se da
# hwnd-esperado, solo vale esa (re-verificación inmediatamente antes de actuar).
gui_localizar() {
    local titulo="$1" esperado="${2:-}"
    local titulo_l="${titulo,,}" host_l="${GUI_HOST_PROC,,}"
    local linea tag hwnd pid proc l t r b ic ttl n
    local cuenta=0 listado=""
    GUI_HWND=""
    for linea in "${GUI_W_LINEAS[@]}"; do
        IFS='|' read -r tag hwnd pid proc l t r b ic ttl <<<"$linea"
        [[ "${proc,,}" == "$host_l" ]] || continue
        [[ "${ttl,,}" == *"$titulo_l"* ]] || continue
        [[ -z "$esperado" || "$hwnd" == "$esperado" ]] || continue
        for n in "$hwnd" "$pid" "$l" "$t" "$r" "$b" "$ic"; do
            gui_es_entero "$n" || gui_abortar "$EXIT_INTEROP" "respuesta ilegible de powershell.exe (campo no numérico: \"$n\")."
        done
        cuenta=$((cuenta + 1))
        listado+="    hwnd=$hwnd título=\"$ttl\""$'\n'
        GUI_HWND="$hwnd" GUI_WINPID="$pid" GUI_L="$l" GUI_T="$t" GUI_R="$r" GUI_B="$b" GUI_ICONICA="$ic" GUI_TITULO_REAL="$ttl"
    done
    if (( cuenta == 0 )); then
        if [[ -n "$esperado" ]]; then
            gui_abortar "$EXIT_NO_ENCONTRADA" "la ventana \"$titulo\" desapareció antes de actuar."
        fi
        gui_pista_recuperacion
        gui_abortar "$EXIT_NO_ENCONTRADA" "no se encontró ninguna ventana (de WSLg) que contenga \"$titulo\"."
    fi
    if (( cuenta > 1 )); then
        printf '%s' "$listado" >&2
        gui_abortar "$EXIT_AMBIGUA" "$cuenta ventanas contienen \"$titulo\"; no se adivina cuál (¿otra instancia de la app de otro agente?). Usá un título más específico o cerrá la sobrante."
    fi
}

# gui_validar_rect: el rectángulo de la ventana tiene que ser razonable. Fail-closed.
gui_validar_rect() {
    local w=$((GUI_R - GUI_L)) h=$((GUI_B - GUI_T)) tol=100
    if (( GUI_L <= -30000 || GUI_T <= -30000 )); then
        gui_abortar "$EXIT_RECT" "la ventana está minimizada (rectángulo en ($GUI_L,$GUI_T)); no se puede capturar ni clickear."
    fi
    if (( w <= 0 || h <= 0 )); then
        gui_abortar "$EXIT_RECT" "rectángulo sospechoso: tamaño ${w}x${h}."
    fi
    if (( GUI_L < GUI_VX - tol || GUI_T < GUI_VY - tol || GUI_R > GUI_VX + GUI_VW + tol || GUI_B > GUI_VY + GUI_VH + tol )); then
        gui_abortar "$EXIT_RECT" "rectángulo sospechoso: ($GUI_L,$GUI_T)-($GUI_R,$GUI_B) está fuera del escritorio virtual (${GUI_VW}x${GUI_VH} en $GUI_VX,$GUI_VY)."
    fi
    if (( w >= GUI_VW && h >= GUI_VH )); then
        gui_abortar "$EXIT_RECT" "rectángulo sospechoso: ${w}x${h} cubre todo el escritorio virtual (${GUI_VW}x${GUI_VH}). Si la ventana está maximizada, des-maximizala."
    fi
}

# ---------------------------------------------------------------------------------------
# X11 (solo para distinguir los popups de la app de ventanas de otras apps / de Windows)
# ---------------------------------------------------------------------------------------
# gui_cargar_xdotool [instalar]: setea GUI_XDOTOOL (vacío si no hay). Con "instalar" corre
# setup-toolkit.sh si no hay forma de conseguirlo.
gui_cargar_xdotool() {
    local instalar="${1:-}"
    local toolkit="${TOOLKIT_DIR:-/tmp/x11tools}"
    GUI_XDOTOOL=""
    if [[ -f "$toolkit/env.sh" ]]; then
        # shellcheck source=/dev/null
        source "$toolkit/env.sh"
        GUI_XDOTOOL="${XDOTOOL_BIN:-}"
    elif command -v xdotool >/dev/null 2>&1; then
        GUI_XDOTOOL="$(command -v xdotool)"
    elif [[ "$instalar" == "instalar" ]]; then
        local dir_lib
        dir_lib="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
        gui_log "xdotool no está preparado todavía, corriendo setup-toolkit.sh..."
        "$dir_lib/setup-toolkit.sh" >/dev/null
        if [[ -f "$toolkit/env.sh" ]]; then
            # shellcheck source=/dev/null
            source "$toolkit/env.sh"
            GUI_XDOTOOL="${XDOTOOL_BIN:-}"
        fi
    fi
    [[ -z "$GUI_XDOTOOL" || -x "$GUI_XDOTOOL" ]] || GUI_XDOTOOL=""
}

# gui_x11_identificar <titulo>: setea GUI_X11_ID y GUI_APP_PID (PID de Linux de la app). Si hay
# más de un PID distinto, GUI_X11_AMBIGUO=1 y no se asigna ninguno. Nunca aborta.
gui_x11_identificar() {
    local titulo="$1"
    GUI_X11_ID="" GUI_APP_PID="" GUI_X11_AMBIGUO=0
    [[ -n "$GUI_XDOTOOL" ]] || return 0
    local patron ids id pid pids="" unico=""
    # --name es regex: se escapan los metacaracteres del título.
    patron="$(printf '%s' "$titulo" | sed 's/[][\.*^$+?(){}|/]/\\&/g')"
    ids="$("$GUI_XDOTOOL" search --name "$patron" 2>/dev/null)" || return 0
    for id in $ids; do
        pid="$("$GUI_XDOTOOL" getwindowpid "$id" 2>/dev/null)" || continue
        gui_es_entero "$pid" || continue
        [[ -n "$GUI_X11_ID" ]] || GUI_X11_ID="$id"
        if [[ -z "$unico" ]]; then
            unico="$pid"
        elif [[ "$unico" != "$pid" ]]; then
            GUI_X11_AMBIGUO=1
        fi
    done
    if (( GUI_X11_AMBIGUO == 1 )); then
        GUI_X11_ID=""
        return 0
    fi
    GUI_APP_PID="$unico"
}

# gui_pid_foco_x11: imprime el PID de Linux de la ventana X11 con foco ("" si no se puede).
gui_pid_foco_x11() {
    [[ -n "$GUI_XDOTOOL" ]] || return 0
    local pid
    pid="$("$GUI_XDOTOOL" getwindowfocus getwindowpid 2>/dev/null)" || return 0
    gui_es_entero "$pid" && printf '%s' "$pid"
    return 0
}

# gui_foco_x11_es_la_app: ¿el foco X11 es de un proceso Linux que es LA app?
gui_foco_x11_es_la_app() {
    [[ -n "$GUI_APP_PID" ]] || return 1
    local fpid
    fpid="$(gui_pid_foco_x11)"
    [[ -n "$fpid" && "$fpid" == "$GUI_APP_PID" ]]
}

# gui_primer_plano_ok: usa FG_* (tras gui_listar) y la ventana localizada.
# Setea GUI_FG_ES_AUX=1 si el primer plano es una ventana auxiliar (popup) de la misma app.
GUI_FG_ES_AUX=0
gui_primer_plano_ok() {
    GUI_FG_ES_AUX=0
    [[ -n "$GUI_HWND" && "$GUI_FG_HWND" == "$GUI_HWND" ]] && return 0
    if [[ "${GUI_FG_PROC,,}" == "${GUI_HOST_PROC,,}" && "$GUI_FG_PID" == "$GUI_WINPID" ]] && gui_foco_x11_es_la_app; then
        GUI_FG_ES_AUX=1
        return 0
    fi
    return 1
}

gui_traer_al_frente() {
    gui_ps "$PS_OP_FOCO" "\$hwnd = [IntPtr]$GUI_HWND" >/dev/null || true
}

# gui_confirmar <titulo> [hwnd]: relee TODO y exige: ventana única, rectángulo razonable y
# primer plano correcto. No hace acciones de foco.
gui_confirmar() {
    gui_listar
    gui_localizar "$1" "${2:-}"
    gui_primer_plano_ok || gui_abortar_primer_plano
    gui_validar_rect
}

gui_abortar_primer_plano() {
    gui_pista_recuperacion
    gui_abortar "$EXIT_PRIMER_PLANO" "la ventana en primer plano es \"${GUI_FG_PROC}\" (hwnd $GUI_FG_HWND), no la app (hwnd $GUI_HWND, \"$GUI_TITULO_REAL\"). No se envía ni captura nada."
}

# gui_preparar <titulo>: localiza, restaura/trae al frente SOLO si hace falta (si ya está al
# frente o hay un popup propio al frente no se toca el foco: darle foco a la principal cerraría
# el desplegable) y confirma. Deja GUI_HWND/GUI_L.. listos.
gui_preparar() {
    local titulo="$1"
    gui_validar_titulo "$titulo"
    gui_exigir_powershell
    gui_listar
    gui_localizar "$titulo"
    gui_x11_identificar "$titulo"
    local intento
    for intento in 1 2; do
        if (( GUI_ICONICA == 1 )) || ! gui_primer_plano_ok; then
            gui_log "Trayendo al frente la ventana \"$GUI_TITULO_REAL\" (hwnd $GUI_HWND), intento $intento..."
            gui_traer_al_frente
            gui_listar
            gui_localizar "$titulo" "$GUI_HWND"
        else
            break
        fi
    done
    gui_confirmar "$titulo" "$GUI_HWND"
}
