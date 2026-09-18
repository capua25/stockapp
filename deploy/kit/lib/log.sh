#!/usr/bin/env bash
# Salida unificada de los scripts del kit. Regla transversal del diseño 2026-09-10 (línea 55):
# todo lo que se imprime va TAMBIÉN a /var/log/stockapp-kit-<fecha>.log.
#
# POR QUÉ importa: el proveedor no va a poder volver al servidor. Si algo sale raro, el log es
# la única evidencia de qué pasó -- y tiene que quedar EN el servidor (para quien tenga acceso
# local después) y poder copiarse al pendrive antes de irse.

KIT_LOG="${KIT_LOG:-/var/log/stockapp-kit-$(date +%Y%m%d).log}"

# agregar_trap_exit <comando> -> encadena <comando> a los que ya se corren al salir del script.
# bash NO encadena traps: `trap ... EXIT` pisa cualquier trap EXIT anterior. iniciar_log necesita
# uno (esperar al tee) y 03-verificar.sh/04-licencia.sh ya declaran el suyo (borrar un temporal);
# como iniciar_log se llama primero, un `trap ... EXIT` directo en esos scripts pisaría el
# nuestro y el tee quedaría sin esperar. Por eso todo el que necesite limpiar algo al salir tiene
# que pasar por acá en vez de llamar `trap ... EXIT` a mano.
_KIT_TRAP_EXIT_CMDS=()

agregar_trap_exit() {
    _KIT_TRAP_EXIT_CMDS+=("$1")
    trap '_ejecutar_traps_exit' EXIT
}

_ejecutar_traps_exit() {
    local cmd
    for cmd in "${_KIT_TRAP_EXIT_CMDS[@]}"; do
        eval "$cmd" || true
    done
}

# iniciar_log -> redirige stdout y stderr a tee, para que todo quede en el log sin tener que
# acordarse de pipear cada echo. Idempotente: llamarla dos veces no duplica la redirección
# porque se invoca una sola vez por script, al principio.
#
# `exec > >(tee -a "$KIT_LOG") 2>&1` arranca un tee en un proceso aparte que bash NUNCA espera
# por sí solo. Si el script termina (incluso por error_fatal/exit 1) antes de que ese tee drene
# lo que tenía pendiente, lo que faltaba escribir se pierde o pisa lo que se escriba después en
# el mismo stream -- ver _cerrar_tee_log. Por eso encadenamos el wait acá mismo, vía
# agregar_trap_exit.
iniciar_log() {
    if ! touch "$KIT_LOG" 2>/dev/null; then
        echo "AVISO: no se puede escribir en '${KIT_LOG}' (¿hace falta sudo?). Sigo sin log a archivo." >&2
        return 0
    fi
    chmod 600 "$KIT_LOG" 2>/dev/null || true
    exec {_KIT_STDOUT_ORIG}>&1
    exec > >(tee -a "$KIT_LOG") 2>&1
    _KIT_TEE_PID=$!
    agregar_trap_exit '_cerrar_tee_log'
    echo "=== $(date -Is) — $(basename "${BASH_SOURCE[1]:-script}") ==="
}

# _cerrar_tee_log -> se corre al salir del script (via agregar_trap_exit). El orden importa: hay
# que restaurar el stdout/stderr original PRIMERO -- así el script deja de escribirle al pipe y
# tee recibe EOF -- y recién DESPUÉS esperarlo con wait. Al revés (wait antes de restaurar) cuelga
# para siempre: tee nunca ve EOF mientras el pipe siga abierto en este mismo proceso.
_cerrar_tee_log() {
    if [[ -n "${_KIT_STDOUT_ORIG:-}" ]]; then
        exec 1>&"${_KIT_STDOUT_ORIG}" 2>&1
        exec {_KIT_STDOUT_ORIG}>&-
    fi
    if [[ -n "${_KIT_TEE_PID:-}" ]]; then
        wait "$_KIT_TEE_PID" 2>/dev/null || true
    fi
}

info()  { echo "      $*"; }
ok()    { echo "  OK  $*"; }
aviso() { echo "  !!  $*"; }
rojo()  { echo "  XX  $*"; }

# error_fatal <msg> -> imprime y termina con 1.
error_fatal() {
    echo
    echo "ERROR: $*" >&2
    echo "       Nada más se ejecutó. El log está en: ${KIT_LOG}" >&2
    exit 1
}
