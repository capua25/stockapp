#!/usr/bin/env bash
# Corre las comparaciones visuales mockup vs app de la UI v2 y deja las páginas en
# artifacts/ui-v2/comparacion/ (ignorado por git). Uso:
#   scripts/ui-v2/comparar.sh              todas las pantallas
#   scripts/ui-v2/comparar.sh Catalogo     solo los tests cuyo nombre contiene "Catalogo"
#                                          (los dos tamaños: Catalogo_IgualAlMockup y su gemelo _1366x768)
#   scripts/ui-v2/comparar.sh 1366x768     solo las corridas a 1366x768
# Cada pantalla se compara a 1280x760 y a 1366x768 con el mismo umbral por defecto (un UmbralAprobado es
# por tamaño, ver herramientas.md); la corrida a 1366x768 deja
# su página en <pantalla>@1366x768/ y el índice agrupa las filas por pantalla y tamaño.
# Requisito una vez por máquina:
#   dotnet run --project tools/StockApp.UiV2.Herramientas -- instalar-navegador
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FILTRO="Categoria=ComparacionVisual"
if [[ $# -gt 0 ]]; then
  FILTRO="${FILTRO}&FullyQualifiedName~$1"
fi

# --blame-hang-timeout: si un render se rompe (p. ej. corre sin UseSkia) el proceso se cuelga
# en vez de fallar (minor diferido de la Task 8); con esto, dotnet test mata el runner y reporta
# fallo en vez de colgar el script para siempre.
# RunConfiguration.TreatNoTestsAsError=true: sin esto, un filtro que no matchea ningún test
# (p. ej. un typo en el nombre de pantalla) hace que dotnet test devuelva exit 0 igual que si todo
# hubiera pasado, y el script mostraría el link a un index.html viejo como si fuera un resultado
# válido. Verificado empíricamente con el SDK instalado (dotnet 10.0.300): exit 1.
set +e
SALIDA="$(dotnet test "$RAIZ/tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj" \
  --filter "$FILTRO" --blame-hang-timeout 5m -- RunConfiguration.TreatNoTestsAsError=true 2>&1)"
CODIGO=$?
set -e

echo "$SALIDA"

if [[ "$CODIGO" -ne 0 ]]; then
  # Comparación de patrón con [[ ]] sobre la variable ya capturada, nunca grep dentro de un pipe
  # (con set -o pipefail, un grep que no matchea mataría el script).
  if [[ "$SALIDA" == *"No test matches the given testcase filter"* ]]; then
    echo "Comparaciones: ningún test coincide con el filtro '$FILTRO'." >&2
  fi
  exit "$CODIGO"
fi

echo "Comparaciones (por pantalla y tamaño): $RAIZ/artifacts/ui-v2/comparacion/index.html"
exit 0
