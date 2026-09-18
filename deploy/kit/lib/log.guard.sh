#!/usr/bin/env bash
# Guardián determinista de iniciar_log/_cerrar_tee_log (deploy/kit/lib/log.sh).
#
# QUÉ prueba: que cuando el proceso que llamó a iniciar_log TERMINA, el archivo de log ya tiene
# TODO lo que se escribió -- en particular la última línea. `exec > >(tee -a "$KIT_LOG") 2>&1`
# arranca un tee en un proceso hijo que bash no espera por sí solo; si el proceso padre termina
# antes de que ese tee drene lo que tenía pendiente, lo último que se escribió puede faltar en el
# archivo en el momento justo en que alguien (un test, un instalador, un operador) lo revisa --
# exactamente la ventana que explota tests/StockApp.Infrastructure.Tests/Kit/BootstrapTests.cs al
# hacer `echo "EXIT=$?" >> salida` justo después de que el script del kit corta.
#
# CÓMO fuerza la condición de forma DETERMINISTA (no depende de que el scheduler tenga suerte):
# un volumen grande de líneas por sí solo no alcanza -- en un disco local rápido y sin
# contención, tee lee del pipe y escribe casi al mismo ritmo que bash produce, así que nunca
# queda backlog real al momento de salir (esto se probó empíricamement con 200.000 líneas: 0
# fallos). Lo que sí fuerza la condición de manera 100% reproducible es un `tee` más lento que el
# disco real: un stub en $PATH (solo para el subproceso de esta prueba) que agrega latencia real
# ANTES de cada escritura. Con eso, cuando el proceso hijo termina, el tee real SIEMPRE tiene
# trabajo pendiente -- y solo el wait del fix garantiza que el chequeo posterior lo vea completo.
#
# Uso: ./log.guard.sh          (sale 0 si el log llegó completo, 1 si faltó algo)
set -uo pipefail

DIR_LIB="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TMPDIR_GUARD="$(mktemp -d -t log-guard.XXXXXX)"
trap 'rm -rf "$TMPDIR_GUARD"' EXIT

KIT_LOG_GUARD="${TMPDIR_GUARD}/kit.log"
N_LINEAS=30
DEMORA_POR_LINEA="0.05" # segundos; 30 líneas * 0.05s = 1.5s de trabajo pendiente garantizado

# --- stub de tee: mismo contrato que `tee -a ARCHIVO`, pero con latencia real por línea ---------
mkdir -p "${TMPDIR_GUARD}/bin"
cat > "${TMPDIR_GUARD}/bin/tee" <<'STUB'
#!/usr/bin/env bash
# Stub de tee para forzar determinísticamente la ventana de carrera del guardián: simula un
# disco lento agregando latencia real ANTES de escribir cada línea. Solo entiende `-a ARCHIVO`,
# que es el único uso real en deploy/kit/lib/log.sh.
set -uo pipefail
[[ "$1" == "-a" ]] || { echo "stub-tee: solo soporta -a ARCHIVO" >&2; exit 1; }
archivo="$2"
while IFS= read -r linea || [[ -n "$linea" ]]; do
    sleep "${STUB_TEE_DEMORA:-0.05}"
    printf '%s\n' "$linea"                 # passthrough, igual que el tee real
    # 2>/dev/null: si el guardián ya limpió su tmpdir (caso SIN el fix, donde este proceso queda
    # huérfano y sigue vivo más allá de lo que el guardián espera), el open() de este append va a
    # fallar -- es ruido esperado del propio bug que se está probando, no un error real.
    printf '%s\n' "$linea" >> "$archivo" 2>/dev/null
done
STUB
chmod +x "${TMPDIR_GUARD}/bin/tee"

# El subproceso corre con set -e para que cualquier fallo interno (ej: iniciar_log no pudo tocar
# el archivo) aborte con un código distinto de 0, en vez de seguir de largo silenciosamente. El
# PATH con el stub de tee AL FRENTE solo aplica a este subproceso (y a lo que él arranque, como
# el tee del process substitution) -- no toca el resto del sistema.
PATH="${TMPDIR_GUARD}/bin:${PATH}" STUB_TEE_DEMORA="${DEMORA_POR_LINEA}" bash -c '
    set -euo pipefail
    source "'"${DIR_LIB}/log.sh"'"
    KIT_LOG="'"${KIT_LOG_GUARD}"'"
    iniciar_log
    for i in $(seq 1 '"${N_LINEAS}"'); do
        echo "linea de relleno numero ${i}"
    done
    echo "ULTIMA-LINEA-CRITICA"
'
codigo_subproceso=$?

# Sin sleep acá: el chequeo pasa en el instante exacto en que el proceso hijo ya cortó. Si
# _cerrar_tee_log no esperó al tee (stub, con ~1.5s de trabajo pendiente), esto corre ANTES de
# que termine de volcar el buffer -- de forma determinista, no por suerte del scheduler.
if [[ "$codigo_subproceso" -ne 0 ]]; then
    echo "FALLO: el subproceso de prueba terminó con código ${codigo_subproceso} (no se pudo escribir el log de prueba)." >&2
    exit 1
fi

if [[ ! -f "$KIT_LOG_GUARD" ]]; then
    echo "FALLO: '${KIT_LOG_GUARD}' ni siquiera existe." >&2
    exit 1
fi

lineas_totales="$(wc -l < "$KIT_LOG_GUARD")"
lineas_esperadas=$((N_LINEAS + 2)) # + encabezado de iniciar_log + ULTIMA-LINEA-CRITICA

if ! grep -q "^ULTIMA-LINEA-CRITICA$" "$KIT_LOG_GUARD" 2>/dev/null; then
    echo "FALLO: se perdió la última línea escrita antes de que el script terminara." >&2
    echo "       Líneas en el log: ${lineas_totales} (se esperaban ${lineas_esperadas})." >&2
    echo "       Últimas líneas encontradas:" >&2
    tail -3 "$KIT_LOG_GUARD" >&2 || true
    exit 1
fi

if [[ "$lineas_totales" -ne "$lineas_esperadas" ]]; then
    echo "FALLO: el log tiene ${lineas_totales} líneas, se esperaban ${lineas_esperadas} (se perdió contenido en el medio, no solo al final)." >&2
    exit 1
fi

echo "OK: iniciar_log esperó al tee -- las ${lineas_totales} líneas llegaron antes de que el proceso terminara."
exit 0
