#!/usr/bin/env bash
# Validaciones puras del kit de instalación. Sin efectos secundarios y sin imprimir nada: cada
# función comunica por exit code, para que el script que la llama decida la severidad y el
# mensaje. Testeadas en
# tests/StockApp.Infrastructure.Tests/Kit/ValidacionesBashTests.cs.

# es_puerto_valido <n> -> 0 si es un puerto TCP en rango 1-65535.
es_puerto_valido() {
    local p="${1:-}"
    [[ "$p" =~ ^[0-9]+$ ]] || return 1
    (( 10#$p >= 1 && 10#$p <= 65535 ))
}

# es_ipv4_valida <ip> -> 0 si es una IPv4 bien formada.
#
# Copiado LITERALMENTE de install.sh:187-197, incluido el "10#$octeto": sin forzar base 10, un
# octeto con cero a la izquierda (p.ej. "008") se interpreta como octal y "8" no es un dígito
# octal válido -- error de runtime feo en vez de un rechazo limpio. Es duplicación deliberada:
# hacer source de install.sh desde el kit tendría efectos al cargarse (valida argumentos, toca
# el sistema), que es exactamente lo que no queremos en una función de validación.
es_ipv4_valida() {
    local ip="${1:-}" octeto
    [[ "$ip" =~ ^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$ ]] || return 1
    for octeto in ${ip//./ }; do
        (( 10#$octeto <= 255 )) || return 1
    done
    return 0
}

# password_admin_valida <pass> -> 0 si la API la va a aceptar.
#
# Espeja ContrasenaValidator.Validar (src/StockApp.Application/Auth/ContrasenaValidator.cs:
# 9,23-25): mínimo 8 caracteres, al menos una letra y al menos un número. Validar acá, ANTES de
# escribir el .env, evita que el bootstrap del admin falle recién cuando la API arranca -- con
# Postgres ya levantado y el .env ya generado, que es el estado más incómodo para corregir.
password_admin_valida() {
    local pass="${1:-}"
    [[ -n "${pass//[[:space:]]/}" ]] || return 1
    (( ${#pass} >= 8 )) || return 1
    [[ "$pass" == *[[:alpha:]]* ]] || return 1
    [[ "$pass" == *[[:digit:]]* ]] || return 1
    return 0
}

# puerto_libre <n> -> 0 si NADIE escucha en ese puerto TCP.
#
# ss viene en iproute2, presente en Ubuntu Server base (no necesita el netstat de net-tools,
# que NO está instalado por defecto desde hace varias releases).
puerto_libre() {
    local p="$1"
    ! ss -lntH "sport = :${p}" 2>/dev/null | grep -q .
}

# quien_escucha <n> -> imprime el/los procesos que escuchan en ese puerto (para diagnóstico).
quien_escucha() {
    local p="$1"
    ss -lntpH "sport = :${p}" 2>/dev/null || true
}

# resolver_api_port <archivo_env> -> imprime por stdout el puerto de la API a usar.
#
# Implementa la Decisión 1 (RESUELTA: el puerto de la API es configurable). Si <archivo_env>
# existe y define API_PORT=, se usa ese valor (la última ocurrencia si hay más de una, vía
# 'tail -1'). Si el archivo no existe o no define la variable, cae a la variable de entorno
# API_PORT si está seteada, o a 5080 por defecto.
#
# Que <archivo_env> no exista es el CAMINO FELIZ de una instalación virgen, no un error: ese
# archivo (/etc/stockapp/.env en 00-preflight.sh) lo crea 01-bootstrap.sh (Task 3.4), que corre
# DESPUÉS de este preflight. Tratar su ausencia como rojo sería un falso positivo en toda
# instalación nueva.
resolver_api_port() {
    local archivo_env="$1"
    if [[ -f "$archivo_env" ]] && grep -q '^API_PORT=' "$archivo_env"; then
        grep '^API_PORT=' "$archivo_env" | tail -1 | cut -d= -f2-
    else
        echo "${API_PORT:-5080}"
    fi
}
