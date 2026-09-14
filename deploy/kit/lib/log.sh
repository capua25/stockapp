#!/usr/bin/env bash
# Salida unificada de los scripts del kit. Regla transversal del diseño 2026-09-10 (línea 55):
# todo lo que se imprime va TAMBIÉN a /var/log/stockapp-kit-<fecha>.log.
#
# POR QUÉ importa: el proveedor no va a poder volver al servidor. Si algo sale raro, el log es
# la única evidencia de qué pasó -- y tiene que quedar EN el servidor (para quien tenga acceso
# local después) y poder copiarse al pendrive antes de irse.

KIT_LOG="${KIT_LOG:-/var/log/stockapp-kit-$(date +%Y%m%d).log}"

# iniciar_log -> redirige stdout y stderr a tee, para que todo quede en el log sin tener que
# acordarse de pipear cada echo. Idempotente: llamarla dos veces no duplica la redirección
# porque se invoca una sola vez por script, al principio.
iniciar_log() {
    if ! touch "$KIT_LOG" 2>/dev/null; then
        echo "AVISO: no se puede escribir en '${KIT_LOG}' (¿hace falta sudo?). Sigo sin log a archivo." >&2
        return 0
    fi
    chmod 600 "$KIT_LOG" 2>/dev/null || true
    exec > >(tee -a "$KIT_LOG") 2>&1
    echo "=== $(date -Is) — $(basename "${BASH_SOURCE[1]:-script}") ==="
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
