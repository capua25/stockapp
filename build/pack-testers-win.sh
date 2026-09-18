#!/usr/bin/env bash
# pack-testers-win.sh — Empaqueta el zip de testers de Gestión Municipal para Windows.
#
# Uso:
#   ./build/pack-testers-win.sh --api-base-url <URL> [VERSION]
#
#   --api-base-url <URL>   OBLIGATORIO. URL completa (con esquema) del servidor al que va a
#                           apuntar el artefacto publicado, ej:
#                           https://stockapp.capuanomartin.dev:8080
#                           Sin este parámetro el script ABORTA (exit != 0). No hay default:
#                           un default acá es exactamente cómo se despacha un zip apuntando a
#                           donde no corresponde (localhost, un VPS viejo, etc).
#   VERSION                 Opcional. Sobreescribe la version leida del .csproj de Presentation.
#
# Qué hace:
#   1. Publica Presentation + Configurador self-contained para win-x64, en un directorio
#      descartable (limpio en cada corrida, para que el zip sea reproducible).
#   2. Sustituye Api.BaseUrl en el appsettings.json PUBLICADO con la URL recibida por
#      parámetro. El appsettings.json del repo (src/StockApp.Presentation/appsettings.json)
#      NUNCA se toca -- ese es el default de desarrollo (localhost) y ahí se queda.
#   3. Arma el zip en deploy/dist/stockapp-desktop-testers-v<VERSION>-win-x64.zip.
#   4. Verifica el resultado extrayendo el appsettings.json DEL ZIP (no del directorio de
#      publish, no del repo) y confirmando que la URL quedó bien. Si no, aborta.
#
# Por qué no usa vpk/Velopack (a diferencia de build/pack-win.ps1):
#   El vpk instalado en esta máquina (Linux/WSL2) solo expone `pack` para AppImage de Linux --
#   no hay target Windows disponible sin pwsh + las herramientas de Windows que Velopack
#   necesita para el bootstrapper (verificado: `vpk --help` no lista ningún comando de
#   packaging Windows en este entorno). build/pack-win.ps1 sigue siendo el camino para el
#   Setup.exe real, pero necesita correrse desde Windows/pwsh. Este script cubre el caso
#   intermedio: un zip portable para testers, reproducible, sin pisar appsettings.json a mano.
#
# Prerequisitos: .NET 10 SDK, jq (ya es dependencia aceptada en scripts de este repo que
# corren en la máquina de desarrollo -- ver deploy/seed-demo.sh, deploy/armar-kit.sh), python3
# (para armar y leer el .zip: no hay binario 'zip'/'unzip' garantizado en esta máquina).
# Ejecutar desde la raiz del repositorio.

set -euo pipefail

# ── Configuracion ─────────────────────────────────────────────────────────────
CSPROJ="src/StockApp.Presentation/StockApp.Presentation.csproj"
CONFIGURADOR_CSPROJ="tools/StockApp.Configurador/StockApp.Configurador.csproj"
PUBLISH_DIR="publish/win-x64-testers"
OUTPUT_DIR="deploy/dist"
RUNTIME="win-x64"

step() { echo ""; echo "==> $*"; }
abort() { echo ""; echo "ERROR: $*" >&2; exit 1; }

# ── Parseo de argumentos ──────────────────────────────────────────────────────
API_BASE_URL=""
VERSION=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --api-base-url)
            [[ $# -ge 2 ]] || abort "--api-base-url necesita un valor."
            API_BASE_URL="$2"
            shift 2
            ;;
        --api-base-url=*)
            API_BASE_URL="${1#--api-base-url=}"
            shift
            ;;
        -*)
            abort "Opcion desconocida: $1"
            ;;
        *)
            VERSION="$1"
            shift
            ;;
    esac
done

if [[ -z "$API_BASE_URL" ]]; then
    abort "Falta el parametro obligatorio --api-base-url <URL>. Sin él este script no arma" \
          " nada: no hay una URL por default que pueda pisar en silencio -- ver el encabezado" \
          " de este archivo para el uso correcto."
fi

if [[ ! "$API_BASE_URL" =~ ^https?:// ]]; then
    abort "El valor de --api-base-url no parece una URL valida (tiene que empezar con" \
          " http:// o https://): '$API_BASE_URL'."
fi

# ── Verificacion de prerequisitos ─────────────────────────────────────────────
step "Verificando prerequisitos..."

command -v dotnet &> /dev/null || abort ".NET SDK no encontrado. Instala .NET 10 SDK desde https://dot.net"
echo "  dotnet: $(dotnet --version)"

command -v jq &> /dev/null || abort "jq no encontrado (necesario para editar el appsettings.json publicado)."
command -v python3 &> /dev/null || abort "python3 no encontrado (necesario para armar/verificar el zip)."

[[ -f "$CSPROJ" ]] || abort "No se encontro el csproj en: $CSPROJ"
[[ -f "$CONFIGURADOR_CSPROJ" ]] || abort "No se encontro el csproj del configurador en: $CONFIGURADOR_CSPROJ"

# ── Leer version del .csproj (single source of truth) ─────────────────────────
if [[ -z "$VERSION" ]]; then
    step "Leyendo version del .csproj..."
    VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$CSPROJ" | head -1)
    if [[ -z "$VERSION" ]]; then
        abort "No se encontro <Version> en $CSPROJ. Agregala antes de empaquetar."
    fi
fi

echo "  Version a empaquetar: $VERSION"
echo "  URL de destino: $API_BASE_URL"

# ── Dotnet publish (directorio limpio, para que el zip sea reproducible) ──────
step "Limpiando directorio de publish..."
rm -rf "$PUBLISH_DIR"

step "Publicando Presentation para $RUNTIME (self-contained)..."
dotnet publish "$CSPROJ" \
    --configuration Release \
    --runtime "$RUNTIME" \
    --self-contained true \
    --output "$PUBLISH_DIR"
echo "  Publicado en: $PUBLISH_DIR"

step "Publicando el configurador de conexion para $RUNTIME (self-contained)..."
dotnet publish "$CONFIGURADOR_CSPROJ" \
    --configuration Release \
    --runtime "$RUNTIME" \
    --self-contained true \
    --output "$PUBLISH_DIR"
echo "  Publicado en: $PUBLISH_DIR"

# ── Sustituir Api.BaseUrl en el appsettings.json PUBLICADO (nunca el del repo) ─
step "Sustituyendo Api.BaseUrl en el appsettings.json publicado..."

APPSETTINGS_PUBLICADO="$PUBLISH_DIR/appsettings.json"
[[ -f "$APPSETTINGS_PUBLICADO" ]] || abort "No se encontro appsettings.json en el publish: $APPSETTINGS_PUBLICADO"

APPSETTINGS_TMP="$(mktemp)"
jq --arg url "$API_BASE_URL" '.Api.BaseUrl = $url' "$APPSETTINGS_PUBLICADO" > "$APPSETTINGS_TMP"
mv "$APPSETTINGS_TMP" "$APPSETTINGS_PUBLICADO"

echo "  appsettings.json publicado actualizado:"
jq '.' "$APPSETTINGS_PUBLICADO" | sed 's/^/    /'

# ── Armar el zip ────────────────────────────────────────────────────────────
step "Armando el zip..."

mkdir -p "$OUTPUT_DIR"
ZIP_PATH="$OUTPUT_DIR/stockapp-desktop-testers-v${VERSION}-win-x64.zip"
rm -f "$ZIP_PATH"

python3 - "$PUBLISH_DIR" "$ZIP_PATH" <<'PYEOF'
import os
import sys
import zipfile

pubdir, zippath = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(zippath, "w", zipfile.ZIP_DEFLATED) as zf:
    for name in sorted(os.listdir(pubdir)):
        full = os.path.join(pubdir, name)
        if os.path.isfile(full):
            zf.write(full, name)
PYEOF

[[ -f "$ZIP_PATH" ]] || abort "El zip no se genero: $ZIP_PATH"
echo "  Generado: $ZIP_PATH ($(du -h "$ZIP_PATH" | cut -f1))"

# ── Verificar el resultado extrayendo DEL ZIP (no del publish, no del repo) ───
step "Verificando el appsettings.json DENTRO DEL ZIP..."

URL_EN_ZIP=$(python3 - "$ZIP_PATH" <<'PYEOF'
import sys
import zipfile

with zipfile.ZipFile(sys.argv[1]) as zf:
    sys.stdout.write(zf.read("appsettings.json").decode("utf-8"))
PYEOF
)
URL_EN_ZIP=$(printf '%s' "$URL_EN_ZIP" | jq -r '.Api.BaseUrl')

if [[ "$URL_EN_ZIP" != "$API_BASE_URL" ]]; then
    abort "Verificacion fallida: el appsettings.json DEL ZIP tiene Api.BaseUrl='$URL_EN_ZIP'," \
          " se esperaba '$API_BASE_URL'. El zip generado NO se debe entregar."
fi

echo "  OK: Api.BaseUrl dentro del zip = $URL_EN_ZIP"

# También confirmar que el Configurador viajó dentro del zip -- el paquete no sirve sin él.
CONFIGURADOR_EN_ZIP=$(python3 - "$ZIP_PATH" <<'PYEOF'
import sys
import zipfile

with zipfile.ZipFile(sys.argv[1]) as zf:
    print("si" if "GestionMunicipal.Configurador.exe" in zf.namelist() else "no")
PYEOF
)

if [[ "$CONFIGURADOR_EN_ZIP" != "si" ]]; then
    abort "Verificacion fallida: GestionMunicipal.Configurador.exe no esta dentro del zip."
fi

echo "  OK: GestionMunicipal.Configurador.exe presente en el zip"

# ── Resultado ──────────────────────────────────────────────────────────────
echo ""
echo "Empaquetado de testers completado exitosamente."
echo ""
echo "  $ZIP_PATH"
