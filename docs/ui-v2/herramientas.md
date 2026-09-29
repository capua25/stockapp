# Herramientas de la UI v2

## Una vez por máquina

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- instalar-navegador
```

Instala el Chromium que usa Microsoft.Playwright 1.63.0 (rev 1243) en `~/.cache/ms-playwright`.

## Comando único de comparación

```bash
scripts/ui-v2/comparar.sh            # todas las pantallas
scripts/ui-v2/comparar.sh Productos  # solo los tests cuyo nombre contiene "Productos"
```

Abre `artifacts/ui-v2/comparacion/index.html`: una fila por pantalla con el porcentaje de píxeles
distintos, las teselas densas y el veredicto. Cada pantalla tiene su página con mockup, app y
diferencias lado a lado (rojo = diferencia fuerte; naranja = diferencia leve de color; contorno
magenta = tesela densa de 16x16: 25% o más de diferencias fuertes, o 90% o más de leves).

## Tokens

`docs/ui-v2/tokens.json` es la única fuente. Después de editarlo:

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens
```

Regenera `src/StockApp.Presentation/Themes/V2/Tokens.axaml` y `docs/ui-v2/mockups/_base/tokens.css`.
Nunca se editan a mano: `ArchivosGeneradosSincronizadosTests` falla si difieren del JSON.

## Mockup de una pantalla

1. `docs/ui-v2/mockups/<modulo>/<pantalla>.html`, con `../_base/tokens.css` y `../_base/base.css`,
   todo adentro de `<div class="v2-ventana">` (1280x760), datos fijos.
2. Íconos: `dotnet run --project tools/StockApp.UiV2.Herramientas -- icono-svg mdi-<nombre>` y pegar el SVG.
3. Solo técnicas de la spec §6.1. Las clases `v2-*` de `base.css` replican la geometría de los
   componentes de `Controls/V2`.

## Test por pantalla

En `tests/StockApp.Presentation.VisualTests`, con `[Trait("Categoria", "ComparacionVisual")]`:

```csharp
[AvaloniaFact]
public void Productos_IgualAlMockup() =>
    ComparacionVisual.CompararConMockup(new ProductoListView(), vmConLosMismosDatosDelMockup, "inventario/productos.html");
```

El umbral por defecto es el calibrado en la Fase 0 (spec §6.5). No se sube por pantalla sin
aprobación del usuario.

## Otros comandos

- `tokens --verificar`, `fuentes`, `fuentes --verificar`
- `capturar-mockup <html> <png>`: captura suelta de un mockup.
- `comparar-png <referencia> <candidata> <diff>`: diff suelto de dos PNG del mismo tamaño (sirve
  para comparar capturas de la app real antes y después).

## Convenciones de la convivencia (hasta la Fase 9)

- Estilos base v2 solo bajo `.ui2`: la RAÍZ de una vista migrada lleva `Classes="ui2"`. Nunca en un
  contenedor que hospede vistas no migradas.
- Componentes v2: `xmlns:v2="using:StockApp.Presentation.Controls.V2"`.
- Tokens v2: claves `V2*` y variables `--v2-*`.
