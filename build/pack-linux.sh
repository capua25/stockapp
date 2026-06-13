#!/usr/bin/env bash
# pack-linux.sh — Empaquetado de StockApp para Linux (AppImage) via Velopack
#
# Uso:
#   ./build/pack-linux.sh [VERSION]
#
#   VERSION  Opcional. Sobreescribe la version leida del .csproj de Presentation.
#            Usar solo si necesitas empaquetar una version distinta a la del csproj.
#
# Prerequisitos:
#   - .NET 10 SDK: https://dot.net
#   - vpk global tool: dotnet tool install -g vpk
# Ejecutar desde la raiz del repositorio.
# Validacion manual obligatoria: ver build/README-empaquetado.md

set -euo pipefail

# ── Configuracion ─────────────────────────────────────────────────────────────
CSPROJ="src/StockApp.Presentation/StockApp.Presentation.csproj"
PUBLISH_DIR="publish/linux-x64"
OUTPUT_DIR="releases/linux"
RELEASE_NOTES="build/RELEASE_NOTES.md"
PACK_ID="StockApp"
CHANNEL="linux"
MAIN_EXE="StockApp.Presentation"
RUNTIME="linux-x64"

# ── Funciones auxiliares ──────────────────────────────────────────────────────
step() { echo ""; echo "==> $*"; }
abort() { echo ""; echo "ERROR: $*" >&2; exit 1; }

# ── Verificacion de prerequisitos ─────────────────────────────────────────────
step "Verificando prerequisitos..."

if ! command -v dotnet &> /dev/null; then
    abort ".NET SDK no encontrado. Instala .NET 10 SDK desde https://dot.net"
fi
echo "  dotnet: $(dotnet --version)"

if ! command -v vpk &> /dev/null; then
    abort "vpk no encontrado. Instala con: dotnet tool install -g vpk"
fi
echo "  vpk: $(vpk --version 2>&1 | head -1)"

if [[ ! -f "$CSPROJ" ]]; then
    abort "No se encontro el csproj en: $CSPROJ"
fi

if [[ ! -f "$RELEASE_NOTES" ]]; then
    abort "No se encontro el archivo de release notes en: $RELEASE_NOTES"
fi

# ── Leer version del .csproj (single source of truth) ────────────────────────
VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then
    step "Leyendo version del .csproj..."
    VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$CSPROJ" | head -1)
    if [[ -z "$VERSION" ]]; then
        abort "No se encontro <Version> en $CSPROJ. Agregala antes de empaquetar."
    fi
fi

echo "  Version a empaquetar: $VERSION"

# ── Dotnet publish ────────────────────────────────────────────────────────────
step "Publicando para $RUNTIME (self-contained)..."

dotnet publish "$CSPROJ" \
    --configuration Release \
    --runtime "$RUNTIME" \
    --self-contained true \
    --output "$PUBLISH_DIR"

echo "  Publicado en: $PUBLISH_DIR"

# ── vpk pack ──────────────────────────────────────────────────────────────────
step "Empaquetando con vpk (channel: $CHANNEL, delta: BestSpeed)..."

mkdir -p "$OUTPUT_DIR"

vpk pack \
    --packId       "$PACK_ID"       \
    --packVersion  "$VERSION"       \
    --packDir      "$PUBLISH_DIR"   \
    --mainExe      "$MAIN_EXE"      \
    --channel      "$CHANNEL"       \
    --delta        BestSpeed        \
    --releaseNotes "$RELEASE_NOTES" \
    --outputDir    "$OUTPUT_DIR"
    # --signParams "..."
    # D7: code signing fuera de alcance en Fase A.
    # En Linux no existe un equivalente directo a signtool; dejar para Fase B si aplica.

# ── Resultado ─────────────────────────────────────────────────────────────────
echo ""
echo "Empaquetado completado exitosamente."
echo ""
echo "Artefactos generados en: $OUTPUT_DIR"
echo "  StockApp-$VERSION-linux.AppImage     -> portable, sin instalador"
echo "  StockApp-$VERSION-full.nupkg         -> paquete completo"
echo "  StockApp-$VERSION-delta.nupkg        -> paquete delta (si habia release previa)"
echo "  releases.linux.json                  -> feed de updates para Velopack"
echo ""
echo "IMPORTANTE: ejecuta el AppImage desde \$HOME (no desde /opt ni /usr) para evitar"
echo "el prompt de pkexec al aplicar updates. Ver build/README-empaquetado.md."
echo ""
echo "Siguiente paso: subir el contenido de $OUTPUT_DIR a un GitHub Release."
