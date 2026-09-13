#!/usr/bin/env bash
# Librería sourceable: calcula el código de máquina de licenciamiento sin necesidad de que la
# API esté instalada. Eso es lo que permite que 00-preflight.sh imprima el fingerprint apenas
# llegás al servidor, y que la licencia viaje en paralelo mientras seguís instalando.
#
# PARIDAD CON C#: tiene que dar EXACTAMENTE lo mismo que
# FingerprintMaquinaBase.CodigoAgrupado sobre el mismo id crudo
# (src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaBase.cs:13-29), que es lo que
# la API usa para validar la licencia. El guardián que lo ata es
# tests/StockApp.Infrastructure.Tests/Licenciamiento/FingerprintParidadBashTests.cs -- si
# tocás este archivo, ese test tiene que seguir verde.
#
# Algoritmo (los 3 pasos importan y ninguno es decorativo):
#   1. TRIM del contenido. FingerprintMaquinaLinux.cs:18 hace .Trim() y systemd escribe
#      /etc/machine-id CON newline final. Sin trim el hash sale completamente distinto
#      (verificado: 4617-8EFE-... con trim, DBDA-82E1-... sin trim, mismo archivo) -- no es un
#      "casi", es un fingerprint inútil.
#   2. SHA-256 de los bytes UTF-8 de ese string (no del archivo: ojo, 'sha256sum archivo'
#      hashea el newline; hay que pipear el string trimeado).
#   3. Hex en MAYÚSCULAS (Convert.ToHexString de C# devuelve mayúsculas) agrupado de a 4 con
#      guiones -> 16 grupos para los 64 chars del SHA-256.

# Rutas del id crudo, en el MISMO orden que FingerprintMaquinaLinux.Rutas (:6-10).
readonly KIT_RUTAS_MACHINE_ID=(
    "/etc/machine-id"
    "/var/lib/dbus/machine-id"
)

# fingerprint_de_archivo <ruta> -> imprime el código agrupado por stdout.
fingerprint_de_archivo() {
    local ruta="$1" id

    if [[ ! -f "$ruta" ]]; then
        echo "ERROR: no se pudo leer el machine-id en '${ruta}'." >&2
        return 1
    fi

    # $(<archivo) descarta los newlines finales; las dos expansiones siguientes completan el
    # trim de cualquier whitespace al principio y al final, igual que String.Trim() de C#.
    id="$(<"$ruta")"
    id="${id#"${id%%[![:space:]]*}"}"
    id="${id%"${id##*[![:space:]]}"}"

    if [[ -z "$id" ]]; then
        echo "ERROR: el machine-id en '${ruta}' está vacío." >&2
        return 1
    fi

    printf '%s' "$id" \
        | sha256sum \
        | cut -d' ' -f1 \
        | tr 'a-f' 'A-F' \
        | sed 's/.\{4\}/&-/g; s/-$//'
}

# fingerprint_de_esta_maquina -> recorre KIT_RUTAS_MACHINE_ID como hace FingerprintMaquinaLinux.
fingerprint_de_esta_maquina() {
    local ruta
    for ruta in "${KIT_RUTAS_MACHINE_ID[@]}"; do
        if [[ -f "$ruta" && -s "$ruta" ]]; then
            fingerprint_de_archivo "$ruta"
            return 0
        fi
    done

    echo "ERROR: no se pudo leer /etc/machine-id (ni el fallback de dbus)." >&2
    return 1
}
