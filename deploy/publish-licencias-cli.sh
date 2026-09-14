#!/usr/bin/env bash
set -euo pipefail

# Publica tools/StockApp.Licencias.Cli self-contained, para poder emitir licencias y tokens de
# reset sin tener el SDK de .NET instalado.
#
# POR QUÉ: el día de una instalación, emitir la licencia es el paso que está en el camino
# crítico (ver deploy/kit/LEEME.md: el fingerprint sale del preflight y la licencia se emite en
# paralelo mientras seguís instalando). Depender del SDK ahí es una dependencia de más en la
# única máquina que tiene la clave privada.
#
# La clave PRIVADA no se empaqueta acá ni en ningún otro lado: vive en ~/stockapp-claves y se
# le pasa por --clave en cada invocación.
#
# Uso:
#   deploy/publish-licencias-cli.sh                 # linux-x64, versión = timestamp UTC
#   deploy/publish-licencias-cli.sh 1.0.0 win-x64   # versión y RID explícitos

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROYECTO="${REPO_ROOT}/tools/StockApp.Licencias.Cli/StockApp.Licencias.Cli.csproj"
SALIDA_DIR="${REPO_ROOT}/deploy/dist"

VERSION="${1:-$(date -u +%Y%m%d%H%M%S)}"
RID="${2:-linux-x64}"
PUBLISH_DIR="${SALIDA_DIR}/licencias-cli-${RID}"
TARBALL="${SALIDA_DIR}/stockapp-licencias-${VERSION}-${RID}.tar.gz"

echo "[publish-licencias] Verificando prerrequisitos..."

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: 'dotnet' no está en el PATH. Este script necesita el SDK -- justamente para" >&2
    echo "       producir un binario que después NO lo necesite." >&2
    exit 1
fi

if [[ ! -f "$PROYECTO" ]]; then
    echo "ERROR: no se encontró el proyecto en '${PROYECTO}'." >&2
    exit 1
fi

echo "[publish-licencias] Publicando (Release, ${RID}, self-contained) — versión ${VERSION}..."
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$PROYECTO" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -o "$PUBLISH_DIR"

EJECUTABLE="${PUBLISH_DIR}/StockApp.Licencias.Cli"
[[ "$RID" == win-* ]] && EJECUTABLE="${EJECUTABLE}.exe"

if [[ ! -f "$EJECUTABLE" ]]; then
    echo "ERROR: el publish no generó '${EJECUTABLE}'. Revisá la salida de 'dotnet publish' arriba." >&2
    exit 1
fi

echo "[publish-licencias] Empaquetando en '${TARBALL}'..."
rm -f "$TARBALL"
tar -czf "$TARBALL" -C "$PUBLISH_DIR" .

echo "[publish-licencias] OK: ${TARBALL}"
echo "[publish-licencias] Ejecutable listo: ${EJECUTABLE}"
echo "[publish-licencias] Probalo:  ${EJECUTABLE}"
