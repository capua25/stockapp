# GATE de la Fase 3c-3: mockups de Ingreso por factura y sus dos diálogos

Fecha: 2026-10-11. Rama `feat/ui-v2`. Plan: `docs/superpowers/plans/2026-10-11-ui-v2-fase-3c3-ingreso-factura.md` (Task 3).

Para aprobar o cambiar ANTES de escribir XAML de diálogo o de pantalla. Respuesta esperada: "aprobado", o la lista de D# que cambian.

## Cómo mirar

Los PNG están en `artifacts/ui-v2/mockups/` (carpeta ignorada por git: se regeneran con `capturar-mockup` desde los HTML, que sí están commiteados). Cada mockup en los dos tamaños, 1280×760 y 1366×768 (sufijo `@1366x768`).

| Mockup (HTML en `docs/ui-v2/mockups/`) | PNG |
|---|---|
| `inventario/ingreso-factura.html` (vacío) | `inventario-ingreso-factura.png` |
| `inventario/ingreso-factura-renglones.html` (6 renglones) | `inventario-ingreso-factura-renglones.png` |
| `inventario/ingreso-factura-diferencia.html` (crédito, diferencia, error de carga) | `inventario-ingreso-factura-diferencia.png` |
| `inventario/ingreso-factura-guardado.html` (card de adjuntos, Finalizar) | `inventario-ingreso-factura-guardado.png` |
| `dialogos/alta-producto.html` | `dialogos-alta-producto.png` |
| `dialogos/alta-producto-faltantes.html` | `dialogos-alta-producto-faltantes.png` |
| `dialogos/confirmacion-precios.html` | `dialogos-confirmacion-precios.png` |
| Alternativa de D4, render REAL de la app (no mockup) | `gate-3c3-carga-una-fila.png` y `gate-3c3-carga-dos-lineas.png` |

Los mockups se armaron contra un banco de medición: la vista v2 prevista en la Task 8 (su XAML tal cual, sin lo que todavía no existe) y el área cliente de los dos diálogos de las Tasks 4 y 5, renderizados en headless con Skia en los dos tamaños. Cada mockup se comparó por píxel contra ese render. Fuera de lo que este documento propone cambiar, la geometría coincide. Lo que queda es antialias de texto: entre 9 y 15 teselas densas en las pantallas y entre 10 y 13 en el alta, sobre todo en los títulos en negrita (ver Riesgos).

## Medidas (headless, banco de la Task 8)

| Qué | 1280×760 | 1366×768 |
|---|---|---|
| Ancho interno de la card (984/1070 menos 2 de borde y 48 de padding) | 934 | 1020 |
| Columnas de "Datos de la factura" (`*,16,*,16,*`) | 301 / 301 / 300 | 329 / 330 / 329 |
| Grilla: CANTIDAD / PRECIO UNITARIO / SUBTOTAL (medidas con el conversor de la celda, "$" y tnum) | 99 / 141 / 99 | 99 / 141 / 99 |
| Grilla: ACT. COSTO (fija) / Quitar (MinWidth; botón ghost de 73) | 120 / 96 | 120 / 96 |
| Grilla: PRODUCTO (`*`) | 377 | 463 |
| Grilla: header / fila / alto total con 6 filas | 36 / 40 / 278 | igual |
| Fila de carga en UNA fila: Producto `*` | **175** | 261 |
| Fila de carga en UNA fila: tilde / Producto nuevo / Agregar artículo | 169 / 134 / 136 | igual |
| Fila de carga en DOS líneas: Producto `*` | **502** | 588 |
| Pie fijo (borde 1 + 16 + campo Total de 55 + 16) / alto del scroll | 88 / 672 | 88 / 680 |
| `AdjuntosPanelView` v1 con un adjunto, dentro de la card v2 | 934 × **120** | 1020 × **120** |
| Offset del scroll en el estado guardado, dos líneas | **494** | **486** |
| Offset del scroll en el estado guardado, una fila | 446 | 438 |
| Pulgar del scroll vertical (Fluent, 2 px #7A7A7A a 4 del borde) | medido por estado | medido por estado |

Offset del estado guardado (ruling F7). `BringIntoView`, posteado en Background, ya está aplicado cuando `RenderizadorVista` captura: alcanzan el `UpdateLayout` y el `RunJobs` que siguen a `prepararApp`. Más vueltas del dispatcher no lo mueven. La card de adjuntos queda con su borde inferior en el borde inferior del scroll: 454 + 218 = 672. El mockup `-guardado` lo fija con `translateY(-494px)` a 1280 y `-486px` a 1366.

## Fila de carga: una fila o dos líneas (D4 y ruling F2)

| | Una fila (D4 tal como está) | Dos líneas (desvío de la sección 2 de la spec) |
|---|---|---|
| Producto a 1280 | **175 px**: menos que el piso de 240 de D4; "Elegí el producto" entra, la mayoría de los nombres no | **502 px** |
| Producto a 1366 | 261 px | 588 px |
| Alto que agrega | 0 | +48 (una línea de 36 + 12) |
| Orden de tabulación | Producto, Cantidad, Precio, tilde, Producto nuevo, Agregar | Igual: "Agregar artículo" se declara al final aunque se dibuje arriba a la derecha |
| Dónde se ve | `gate-3c3-carga-una-fila*.png` | Los 4 mockups de la pantalla, y `gate-3c3-carga-dos-lineas*.png` |

**Recomendación: dos líneas.** Arriba van Producto `*`, Cantidad 120, Precio 140 y "Agregar artículo" alineado abajo a la derecha. En la segunda línea, alineados a la izquierda con 16 de separación, van el tilde "Actualizar precio costo" y "Producto nuevo". Con esto la regla de D4 se aplica tal cual. Los `x:Name` (`FilaCarga`, `ComboProducto`, `CantidadEnCarga`, `PrecioEnCarga`, `CheckActualizarPrecio`, `BotonProductoNuevo`, `BotonAgregar`) son los mismos en los dos layouts. Para la Task 8 cambia la estructura: `FilaCarga` pasa a ser `Grid ColumnDefinitions="*,12,120,12,140,12,Auto" RowDefinitions="Auto,12,Auto"`, y la segunda línea es un `StackPanel` horizontal con `Spacing="16"` en `Grid.Row="2"` y `Grid.ColumnSpan="7"`. En una fila, el `Margin="0,0,0,6"` que el plan le pone al tilde lo deja 6 px más arriba que el centro de los controles: el CheckBox v2 ya mide 36. En dos líneas el tilde va centrado y sin margen.

## Decisiones D1 a D16

Formato: **qué se decide**. Opciones. → Recomendación. *(dónde se ve)*

- **D1 — Encabezado.** Sin breadcrumb, título "Ingreso por factura" (hoy dice "Ingreso de stock por factura" con el eyebrow "MOVIMIENTOS") y el subtítulo "Cargá la factura del proveedor y los artículos que entran al stock." / Mantener el título viejo. → Como el plan. *(los 4 de pantalla)*
- **D2 — Una sola card a todo el ancho** con tres secciones separadas por el separador de 1 px y 20 de margen. Alternativa: tres cards. → Una card. *(los 4 de pantalla)*. Observación: a 1280, con la cabecera de 3×3 + crédito y la carga en dos líneas, en `-renglones` la grilla asoma solo por el header y en `-diferencia` queda entera debajo del pie. El usuario ve los renglones después de scrollear. No cambia la recomendación: la pantalla se usa de arriba hacia abajo y el pie fijo muestra la suma.
- **D3 — "Datos de la factura"** en tres columnas: Proveedor* \| N.º factura \| Orden de compra; Fecha \| Detalle* \| Destino; Fuente* \| Rubro* \| Línea POA; Compra a crédito \| Vencimiento. Asterisco solo en lo que exige `PuedeGuardar`. → Como el plan. *(`ingreso-factura`, `-diferencia`)*
- **D4 — "Cargar artículo".** Una fila o dos líneas (ver la sección anterior). → **Dos líneas** (Producto mide 175 < 240 a 1280). *(los 4 de pantalla; `gate-3c3-carga-*`)*
- **D5 — Error de carga:** `Alerta` Error debajo de la fila de carga, 8 arriba. → Como el plan. *(`-diferencia`)*
- **D6 — Grilla "Artículos cargados"** de solo lectura, sin orden. Precios y subtotal con "$" ("$ 480,00"): **cambio visual**, hoy van sin símbolo. "Sí" en ACT. COSTO como insignia neutra, Quitar `ghost`, estado vacío propio. Sin scroll propio: scrollea la página. → Como el plan, con los anchos medidos. *(`-renglones`, `-guardado`, `ingreso-factura` para el vacío)*
- **D7 — Pie fijo** fuera del scroll: Suma, Total de factura* (160) y Diferencia a la izquierda; Cancelar y Guardar a la derecha. **Guardar es el único primario de la pantalla.** Enter en la fila de carga agrega el artículo y nunca guarda (U2). El error general va en una `Alerta` entre el scroll y el pie, también fuera del scroll. Diferencia neutra en 0 y en color advertencia (#92400E) si no da 0. → Como el plan. *(los 4; advertencia en `-diferencia`)*
- **D8 — Después de guardar:** el formulario queda deshabilitado, aparece la card "Adjuntá la factura" con `AdjuntosPanelView` v1 y **la página scrollea sola hasta ella**. En el pie queda solo "Finalizar". Alternativa: la card arriba, sin scroll automático. → Como el plan (offset medido: 494/486). *(`-guardado`)*
- **D9 — Diálogo de alta** de 440: Código* y Nombre* a todo el ancho, Categoría \| Unidad de medida*, alerta de faltantes y Cancelar / Confirmar. Foco inicial en Código. → Como el plan. *(`dialogos-alta-producto`, `-faltantes`)*
- **D10 — Diálogo de precios** de 520, con encabezados, un tilde por fila (el primero tildado) y los dos precios a la derecha; Cancelar / "Confirmar y guardar". Foco inicial en "Confirmar y guardar". **Ajuste que propongo:** con el code-behind del plan los precios salen en el TextBlock por defecto (14 px, texto primario, sin cifras tabulares) y se ven más grandes y oscuros que el nombre del tilde (13 px, texto etiqueta). Opciones: (a) como el plan; (b) precios con la tipografía del tilde (`FontSize` de control, `V2TextoEtiquetaBrush`) y `FontFeatures="+tnum,-calt"`. Es lo que el plan pedía con "precios con `v2-num`". → **(b)**: el mockup ya la muestra, verificada contra el banco con (b) aplicada. *(`dialogos-confirmacion-precios`)*
- **D11 — Foco después del alta confirmada:** pasa a Cantidad. Al cancelar, el comportamiento nativo. Alternativa: el nativo en los dos casos. → Como el plan. *(no se ve en un PNG: lo custodian los tests de la Task 8)*
- **D12 — Estados a mockear:** los 7 de la tabla, a los dos tamaños, sin desplegables ni tooltips. → Como el plan. Detalle: en `-renglones` y `-diferencia` el combo de Producto se ve con el borde de foco (acento), igual que en el render real, porque después de agregar el foco vuelve ahí.
- **D13 — Datos fijos:** `DatosIngresoFactura` (Task 1), suma $ 44.940,00 y diferencia $ 60,00. → Como el plan.
- **D14 — El interior v1 de la card de adjuntos no se compara.** En la app se oculta con `Opacity = 0`; en el mockup es un bloque de alto medido (120) con `visibility: hidden`. → Como el plan. *(`-guardado`: el hueco blanco dentro de la card es ese bloque)*
- **D15 — Hallazgo H1 (preexistente, funcional).** El tilde "Actualizar precio costo" de la fila de carga se sobrescribe en la confirmación de precios, que arranca con todo destildado. Opciones: (a) dejarlo como está y anotarlo como deuda; (b) que cada tilde arranque con el `ActualizarPrecioCosto` de su renglón (cambio funcional). → **(a)**: el alcance de la fase es sin cambios funcionales (U3). Va a la deuda en la Task 10. *(el mockup de precios muestra el primer tilde marcado solo como dato de prueba)*
- **D16 — `GuardianDePatronTests` L48:** sale la fila que custodia el `HeaderVista` v1 y la vista pasa a `VistasMigradasAV2`. Lo reemplaza `Raiz_TieneUi2_EncabezadoSinBreadcrumb_YTresSecciones` (convención 13). → Como el plan. *(no visual)*

## Hallazgos del banco que piden decisión

- **H2 — ComboBox y CheckBox deshabilitados caen en el gris de Fluent.** En el estado guardado, el combo de Producto deshabilitado se pinta #CCCCCC y el texto del tilde #9F9F9F: no hay estilo `.ui2 ComboBox:disabled` ni `.ui2 CheckBox:disabled`, y `TextBox:disabled` y los botones sí usan los tokens v2. Opciones: (a) agregar los dos estilos v2 con los tokens de deshabilitado (fondo #F5F5F4, borde `V2Borde`, texto #A8A29E; casilla con fondo deshabilitado y borde `V2Borde`), con su espejo en `base.css`, en la Task 7 o la 8; (b) dejar Fluent y copiar el gris en el mockup como compensación. → **(a)**: es solo visual y el mockup `-guardado` ya la muestra. Afecta a todas las pantallas v2 que deshabilitan un combo o un tilde. *(`-guardado`, arriba: combo de Producto y tilde)*
- Clase de mockup: el brief nombra `v2-tilde`, que no existe. Los tildes usan `v2-casilla-fila`, la clase del catálogo. No hace falta una clase nueva en `base.css`.

## Lo que el plan pide señalar

| Punto | Dónde |
|---|---|
| Un solo primario (Guardar) y Enter en la fila de carga (U2) | Pie de los 4 mockups; "Agregar artículo" es secundario |
| Pie fijo y alerta general fuera del scroll (D7) | Los 4 mockups: el contenido se corta arriba del pie |
| Una o dos líneas con el ancho medido (D4) | Sección "Fila de carga" |
| Precios con "$" en la grilla (D6, cambio visual) | `-renglones`, `-guardado` |
| Scroll automático a la card de adjuntos (D8) | `-guardado` (offset 494/486) |
| Foco en Cantidad tras el alta (D11) | Tests de la Task 8 |
| H1 (D15): no cambiarlo, va a la deuda | D15 |
| El interior v1 de adjuntos no se compara (D14) | `-guardado` |

## Riesgos para las Tasks 4, 5 y 8

- Antialias. Contra el banco quedan entre 9 y 15 teselas densas a 1280 en las pantallas (tope 11): título en negrita, subtítulos de sección y valores del pie. En el alta quedan entre 10 y 13. La geometría coincide: no hay corrimientos de layout. Si una comparación da rojo por 1 a 4 teselas en esos textos, se diagnostica con `teselas.py` antes de tocar nada, como manda el plan.
- Los mockups `-renglones` y `-diferencia` dibujan el combo de Producto con foco. El banco en dos líneas no lo reprodujo, por un artefacto del banco: tenía dos combos ligados al mismo pedido de foco. La vista real tiene uno solo, y el render en una fila sí mostró el foco. Si la Task 8 no lo reproduce, se ajusta el mockup.
