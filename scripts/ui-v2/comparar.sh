#!/usr/bin/env bash
# Corre las comparaciones visuales mockup vs app de la UI v2 y deja las páginas en
# artifacts/ui-v2/comparacion/ (ignorado por git). Uso:
#   scripts/ui-v2/comparar.sh              todas las pantallas
#   scripts/ui-v2/comparar.sh Catalogo     solo los tests cuyo nombre contiene "Catalogo"
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
set +e
dotnet test "$RAIZ/tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj" \
  --filter "$FILTRO" --blame-hang-timeout 5m
CODIGO=$?
set -e

echo "Comparaciones: $RAIZ/artifacts/ui-v2/comparacion/index.html"
exit "$CODIGO"
