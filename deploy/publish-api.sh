#!/usr/bin/env bash
set -euo pipefail

# Publica StockApp.Api en modo self-contained (linux-x64) y empaqueta el resultado en un
# tar.gz listo para copiar al VPS.
#
# Por qué self-contained: el VPS destino NO tiene .NET instalado (relevado, decisión de
# arquitectura 2) — self-contained incluye el runtime en el propio publish, sin depender de
# lo que haya (o no) instalado en el host.
#
# Uso:
#   deploy/publish-api.sh              # versión = timestamp UTC
#   deploy/publish-api.sh 1.4.0        # versión explícita (útil para tags/releases)
#
# Salida: deploy/dist/stockapp-api-<version>-linux-x64.tar.gz
# (deploy/dist/ está en .gitignore — no se commitea).

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROYECTO="${REPO_ROOT}/src/StockApp.Api/StockApp.Api.csproj"
SALIDA_DIR="${REPO_ROOT}/deploy/dist"
PUBLISH_DIR="${SALIDA_DIR}/publish"

echo "[publish-api] Verificando prerrequisitos..."

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: 'dotnet' no está en el PATH. Instalá el .NET SDK antes de publicar." >&2
    exit 1
fi

if [[ ! -f "$PROYECTO" ]]; then
    echo "ERROR: no se encontró el proyecto en '${PROYECTO}'. ¿Corriste este script desde el repo correcto?" >&2
    exit 1
fi

VERSION="${1:-$(date -u +%Y%m%d%H%M%S)}"
TARBALL="${SALIDA_DIR}/stockapp-api-${VERSION}-linux-x64.tar.gz"

echo "[publish-api] Publicando StockApp.Api (Release, linux-x64, self-contained) — versión ${VERSION}..."
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$PROYECTO" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o "$PUBLISH_DIR"

if [[ ! -x "${PUBLISH_DIR}/StockApp.Api" ]]; then
    echo "ERROR: el publish no generó el ejecutable 'StockApp.Api' en '${PUBLISH_DIR}'. Revisá la salida de 'dotnet publish' arriba." >&2
    exit 1
fi

echo "[publish-api] Empaquetando en '${TARBALL}'..."
mkdir -p "$SALIDA_DIR"
rm -f "$TARBALL"
tar -czf "$TARBALL" -C "$PUBLISH_DIR" .

echo "[publish-api] OK: ${TARBALL}"
echo "[publish-api] Siguiente paso (ver deploy/DEPLOY.md): copiar al VPS junto con"
echo "[publish-api]   deploy/install.sh, deploy/stockapp-api.service, deploy/wait-for-postgres.sh"
echo "[publish-api]   y deploy/.env (con los secretos reales, nunca el .example), y correr install.sh allá."
