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

Un filtro que no coincide con ningún test (p. ej. un typo en el nombre de pantalla) hace que el
script termine con exit distinto de cero y el mensaje "ningún test coincide con el filtro", en vez
de mostrar el link a un `index.html` viejo como si fuera un resultado válido.

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
4. Celdas numéricas de grilla: la celda lleva `v2-num` y el valor va SIEMPRE dentro de un `<span>`
   (`.v2-num > span`), que hace de TextBlock de la celda:

   ```html
   <div class="v2-grilla-celda v2-num"><span>12.50</span></div>
   ```

   `.v2-num` alinea a la derecha con cifras tabulares; el `span` redondea su ancho hacia arriba
   como Avalonia (el TextBlock mide el ceil del ancho del texto y lo dibuja pegado a su izquierda).
   Sin el `span` la cifra queda hasta 1px corrida a la derecha. El redondeo usa `calc-size()`, que
   solo funciona en Chromium (el navegador de la comparación).

## Test por pantalla

En `tests/StockApp.Presentation.VisualTests`, con `[Trait("Categoria", "ComparacionVisual")]`:

```csharp
[AvaloniaFact]
public void Productos_IgualAlMockup() =>
    ComparacionVisual.CompararConMockup(new ProductoListView(), vmConLosMismosDatosDelMockup, "inventario/productos.html");
```

El umbral por defecto es el calibrado en la Fase 0 (spec §6.5). No se sube por pantalla sin
aprobación del usuario, y la aprobación queda en el código:

1. Abrir la página de comparación de la pantalla y revisar con el usuario CADA tesela densa
   (contorno magenta): tiene que ser solo tono de texto, sin nada corrido ni de otro color.
2. Registrar el valor aprobado (pasar `teselasDensasMax` mayor a 11 o `umbral` mayor a 1.68 sin
   esto falla antes de renderizar). Si también hay que superar el porcentaje, se agrega
   `PorcentajeMaximo: <valor>` al mismo registro:

   ```csharp
   ComparacionVisual.CompararConMockup(new ProductoListView(), vm, "inventario/productos.html",
       aprobado: new UmbralAprobado(14, "Martin", "2026-10-02", "grilla de 12 filas: 14 teselas, todas tono de texto"));
   ```

3. Si el valor es 16 o más (lo que dio M2, relleno pálido equivocado, en el catálogo), antes hay
   que correr en esa pantalla la mutación equivalente a M2 (quitar la variante de color de un
   relleno pálido), ver el rojo y registrarlo en el quinto parámetro, `MutacionPalidaEnRojo`
   (qué se quitó y cuántas teselas dio). Sin eso falla. (El último parámetro es `PorcentajeMaximo`,
   del paso 2.)

Además de los píxeles, `CompararConMockup` falla si algún `TextBlock` visible no se ve entero:
pide más lugar del que tiene, o cae fuera de un ancestro con `ClipToBounds` o de la ventana (un
glifo que falta no supera el ruido de píxeles). El mensaje lista cada texto. El viewport de un
`ScrollViewer` no cuenta en el eje en que se puede scrollear, y lo mismo el `DataGrid` (la última
fila parcial o una columna fuera de vista no se reportan; una celda angosta con texto largo sí). `TextTrimming` no exime: si el
mockup usa `text-overflow: ellipsis`, el test declara esos textos en `recortesPermitidos`. Puntos
ciegos en spec §6.5.

Diferencia de tono aceptada: el texto de la app sale entre 7% y 18% más claro que en el mockup
(rasterización de Skia frente a Chromium) con la geometría idéntica. El usuario la aceptó el
2026-09-30 y es el grueso del ruido que absorbe el umbral: en la página de comparación se ve como
naranja y rojo fino en los bordes de los glifos. Lo que se exige igual es la geometría.

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
- Headers de `DataGridColumn` en MAYÚSCULAS (spec §2.3): Avalonia no tiene `text-transform`, así que
  el `Header` de cada columna se escribe ya en mayúsculas a mano (en el mockup, `--v2-*-transformacion:
  uppercase` sí lo hace vía CSS).
