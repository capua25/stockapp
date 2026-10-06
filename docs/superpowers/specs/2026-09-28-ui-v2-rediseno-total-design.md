# StockApp — UI v2: rediseño total de la interfaz

**Fecha:** 2026-09-28
**Estado:** aprobado por el usuario; Fase 0 y Fase 1 implementadas en feat/ui-v2; Fase 1 APROBADA por el usuario el 2026-10-05 (merge a main al final de todas las fases)
**Rama:** `feat/ui-v2` (worktree propio)
**Alcance:** todas las vistas de `src/StockApp.Presentation/` (shell, ~55 de contenido, pantallas fuera del shell, 4 diálogos, 3 overlays de actualización)
**Mockups de referencia:** `docs/ui-v2/mockups/referencia/` — `direccion-visual-aprobada.html` (variante `.di-azul`), `navegacion-aprobada.html` (opción A), `patrones-1.html`

## 1. Contexto y problema

El refactor visual de agosto de 2026 (`feat/ui-refactor-dashboard`, spec `2026-08-18-ui-refactor-dashboard-design.md`, 55 vistas, patrones P0-P9) ordenó la estructura pero no dio una dirección estética: la app terminada **no quedó igual a los mockups**. El usuario marca cuatro problemas:

1. Navegación y estructura.
2. Inconsistencia entre pantallas.
3. Estética general.
4. Densidad y espaciado.

**Objetivo:** una interfaz nueva desde cero que conserve **todas** las características actuales, con **fidelidad visual 1:1 al mockup aprobado** como requisito central. Regla de cierre: **si no queda igual al mockup, no está terminado.**

## 2. Dirección visual

Intermedio entre "institucional sobrio" y "cálido aireado", con acento azul. Reemplaza el verde `#16A34A` y el sidebar oscuro `#1E293B` actuales. **Tema claro fijo**, sin modo oscuro.

### 2.1 Color

| Token | Valor | Uso |
|---|---|---|
| Fondo app | `#F7F7F5` | fondo de ventana y de pantallas fuera del shell |
| Superficie | `#FFFFFF` | cards, sidebar, inputs |
| Borde | `#E7E5E4` | cards, sidebar, separadores estructurales |
| Borde input | `#D6D3D1` | inputs, combos, botón secundario |
| Separador de grilla | `#F0EFED` | líneas entre filas y bajo el header |
| Header de grilla | `#FAFAF9` | fondo del header de columnas |
| Acento | `#1D4ED8` | botón primario, links, ícono del ítem activo |
| Acento hover | `#1E40AF` | hover del primario |
| Acento suave | `#DBEAFE` | pastilla del ítem activo del sidebar |
| Acento oscuro | `#1E3A8A` | texto del ítem activo |
| Texto primario | `#1C1917` | títulos, valores |
| Texto secundario | `#57534E` | subtítulos, fecha, textos de apoyo |
| Texto terciario | `#78716C` | breadcrumb, pie de conteo, header de columna, descripciones |
| Texto etiqueta | `#44403C` | etiquetas de campo, texto de celda, texto de input |
| Texto grupo | `#A8A29E` | headers de sección del sidebar, versión, KPI sin dato |
| Peligro texto | `#B91C1C` | número en rojo (stock negativo), texto destructivo |
| Borde error | `#DC2626` | borde de input inválido y mensaje de error del campo |

Insignias (pill, fondo/texto):

| Variante | Fondo | Texto |
|---|---|---|
| Neutro | `#F5F5F4` | `#44403C` |
| Advertencia | `#FEF3C7` | `#92400E` |
| Peligro | `#FEE2E2` | `#991B1B` |
| Éxito | `#DCFCE7` | `#14532D` |

El valor de texto de Éxito se toma de los mockups (`#14532D`), no del `#166534` del brief: en `direccion-intermedia.html` `#166534` es el hover del primario de la variante verde descartada.

Alertas (fondo / borde / texto):

| Variante | Fondo | Borde | Texto |
|---|---|---|---|
| Advertencia | `#FEF3C7` | `#FDE68A` | `#92400E` |
| Error | `#FEE2E2` | `#FCA5A5` | `#991B1B` |
| Info | `#DBEAFE` | `#DBEAFE` | `#1E3A8A` |

Advertencia y Error salen de `patrones-1.html`. Info no aparece en los mockups de referencia: se arma con la familia de acento ya aprobada (suave como fondo y borde, oscuro como texto) y se valida en la pantalla catálogo-de-componentes de la Fase 0.

### 2.2 Forma, sombra y medidas

- **Radios:** 8 (inputs, botones, pastilla activa, alertas, `TarjetaAcceso` y el borde de la grilla), 10 (`TarjetaContenido` y `TarjetaMetrica`), 999 (insignias), 4 (casilla del `CheckBox`). El 8 de `TarjetaAcceso` y de la grilla lo aprobó el usuario el 2026-09-30 mirando el catálogo de componentes.
- **Sombra de card:** `0 1px 2px rgba(0,0,0,0.05)` — la única sombra del sistema.
- **Alturas:** input y botón 36; fila de grilla 40; header de grilla 36; ítem de sidebar 32.
- **Espaciado:** base 4; todo margen, padding y gap de DISEÑO es múltiplo de 4. Excepción de diseño: el padding vertical de 2 de la `Insignia` (valor del mockup aprobado). Los valores que compensan la geometría medida de las plantillas de Avalonia (Padding 13, RowHeight 39, márgenes del mockup que no son enteros ni múltiplos de 4) están listados en §6.5.
- **Anchos fijos:** sidebar 240; card de formulario máx. 760; card de login 400. A 1366 el sidebar de 240 le deja 1126 px a la vista de contenido.
- **Sidebar en 240 (decisión del usuario, 2026-10-02):** el sidebar queda en 240 aunque a 1366 deja 8 px menos que el sidebar actual (232); las grillas que desbordan resuelven con scroll horizontal y el piso de 160 de las columnas proporcionales.

### 2.3 Tipografía

Inter, ya embebida vía `Avalonia.Fonts.Inter`. Cifras tabulares y alineadas a la derecha en toda columna numérica.

| Rol | Tamaño / peso | Color |
|---|---|---|
| Título de página | 22 / 600, alto de línea 26 | primario |
| Título de sección (card, panel, detalle de catálogo) | 15 / 600 | primario |
| Marca en sidebar | 15 / 700 | primario |
| Valor de TarjetaMetrica | 20 / 600 | primario |
| Título de login | 20 / 600 | primario |
| Cuerpo (texto corrido) | 14 / 400 | primario o secundario |
| Texto de controles (input, botón, celda, subtítulo, breadcrumb, alerta) | 13 | según el control |
| Subtítulo de sección dentro de formulario | 13 / 600 | primario |
| Etiqueta de campo | 12 / 500 | etiqueta |
| Ítem de sidebar | 12 / 500 | etiqueta; activo en acento oscuro |
| Insignia | 12 / 500, padding 2×8 | según variante |
| Mensaje de error de campo | 12 | borde error |
| Header de columna | 11 / 600, MAYÚSCULAS, letter-spacing 0.6 | terciario, sobre `#FAFAF9` |
| Header de sección del sidebar | 11 / 600, MAYÚSCULAS, letter-spacing 0.6 | grupo |

**Grillas sin zebra**: filas blancas separadas por `#F0EFED`.

**Precedencia:** los valores de esta sección son los tokens canónicos. Los mockups de referencia se aprobaron como dirección y pueden diferir en detalles (letter-spacing en `em`, tamaños puntuales); los mockups por pantalla de cada fase se construyen con el CSS generado de estos tokens, así que ahí no hay conflicto posible.

## 3. Enfoque: reescritura en el lugar por módulos, mockups como contrato

- Rama `feat/ui-v2` en worktree propio.
- **Se reescribe el XAML de cada vista en su mismo archivo.** Los ViewModels quedan intactos salvo lo mínimo: hub de Reportes, Catálogos, KPIs de Inicio y datos del usuario en el pie del sidebar.
- **No cambian:** `ViewLocator`, `INavigationService`, el wiring de `DataContextChanged` en code-behind (44 vistas), el seam de validación `ErrorConverter`, la lógica de permisos.
- Los compiled bindings (`x:DataType`) son la red del compilador: un binding roto en la reescritura es error de build, no un null silencioso.

### 3.1 Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| Solo tema (restilar sin tocar vistas) | Ya se intentó en agosto y falló; no arregla navegación ni estructura. |
| Vistas paralelas con feature flag | Choca con `ViewLocator`, que resuelve por convención de nombre; duplica 69 vistas. |

### 3.2 Tokens como fuente única

Un archivo de tokens, `docs/ui-v2/tokens.json`, del que se **generan** dos salidas:

- `Themes/Tokens.axaml` (recursos de Avalonia).
- El CSS que usan los mockups por pantalla.

Nunca se mantienen dos copias a mano. Un cambio de token se hace en el JSON y se regeneran ambas salidas.

### 3.3 Componentes

Componentes nuevos en `Controls/`, con sus templates en un `.axaml` de componentes:

| Componente | Responsabilidad |
|---|---|
| `EncabezadoPagina` | título, subtítulo, breadcrumb opcional, slot de acciones a la derecha |
| `TarjetaContenido` | card blanca, borde, radio 10, sombra única |
| `BarraFiltros` | buscador y filtros de un listado |
| Tema de `DataGrid` | reescrito completo: header, filas de 40, separadores, sin zebra, numéricos a la derecha |
| `CampoFormulario` | etiqueta arriba, asterisco si es requerido, error debajo. **No intercepta `ErrorConverter`**: muestra lo que el seam ya produce. |
| `Insignia` | pill con variantes neutro/advertencia/peligro/éxito; reemplaza `BadgeEstado` |
| `Alerta` | info / advertencia / error |
| `TarjetaMetrica` | KPI de Inicio: valor, etiqueta, estado sin dato |
| `TarjetaAcceso` | acceso rápido con ícono (Inicio) y tarjeta del hub de Reportes |
| `EstadoVacio` | rediseñado |

Estilos de controles base: `Button` con variantes **primario** (uno solo por vista), **secundario**, **ghost** y **peligro**; inputs (TextBox, ComboBox, DatePicker, NumericUpDown, CheckBox) a 36 de alto con borde input y radio 8.

## 4. Shell y navegación

Opción A aprobada: sidebar plano reorganizado más consolidación de pantallas.

### 4.1 Sidebar

240 de ancho, fondo blanco con borde derecho. Secciones **siempre visibles, sin acordeones**. Ítem activo en pastilla (acento suave, texto acento oscuro).

- **Inicio**
- **INVENTARIO:** Productos · Registrar entrada · Registrar salida · Ingreso por factura · Historial de movimientos
- **FINANZAS:** Gastos y facturas · Ingresos de caja · Libro caja · Control POA · Calendario de pagos
- **GESTIÓN:** Tareas · Documentos
- **ANÁLISIS:** Reportes
- **ADMINISTRACIÓN:** Catálogos · Usuarios · Mantenimiento

Reglas:

- Cada ítem conserva su gate actual (`Puede*` / `EsAdmin`), incluidos los AND compuestos y los ítems estructuralmente Admin-only.
- El header de una sección se oculta si no tiene ningún ítem visible.
- La revocación de permisos en caliente se mantiene: al perder un permiso el ítem desaparece sin reiniciar.
- **Importación:** sigue apagada con `ImportacionHabilitada=false`; el ítem existe pero queda oculto.

**Pie del sidebar:** avatar con iniciales, nombre y rol del usuario (nuevo: hoy no se muestra), cerrar sesión (con la confirmación actual) y versión.

### 4.2 Hub de Reportes (pantalla nueva)

Seis tarjetas: Valorización de inventario, Stock por categoría, Historial por producto, Productos más movidos, Log de auditoría, Estadística de tareas.

- Cada tarjeta conserva el gate actual de su reporte; **Historial por producto exige además `RegistrarMovimientos`**.
- El ítem "Reportes" del sidebar es visible si hay al menos una tarjeta visible.

### 4.3 Catálogos (pantalla nueva, master-detail)

Lista izquierda agrupada, detalle a la derecha:

| Grupo | Catálogos | Gate | Origen actual |
|---|---|---|---|
| INVENTARIO | Categorías, Proveedores, Unidades de medida | `GestionarTablasMaestras` | ítems propios del sidebar |
| FINANZAS | Fuentes de financiamiento, Rubros de gasto, Líneas POA | `GestionarMaestrosFinanzas` | "Maestros de finanzas" |
| TAREAS | Zonas, Dimensiones, Organismos responsables, Orígenes de financiamiento | `GestionarTablasMaestras` | "Catálogos de tareas" |

- Cada grupo se muestra solo con su gate, que es el de la pantalla de la que sale (verificado en `ShellMainViewModel`: "Catálogos de tareas" usa `PuedeGestionarTablasMaestras`, "Maestros de finanzas" usa `PuedeGestionarMaestrosFinanzas`).
- Se eliminan las pestañas anidadas actuales.
- Los listados de catálogo ganan buscador y columna de estado con `Insignia`.
- El ítem "Catálogos" del sidebar es visible si hay al menos un grupo visible.

### 4.4 Breadcrumb y navegación

- Altas y ediciones muestran breadcrumb en `EncabezadoPagina`.
- El primer tramo del breadcrumb dispara el evento `Volver` que el VM ya expone. **No se agrega back-stack.**

### 4.5 Sin cambios

Overlay de actualizaciones (se rediseña visualmente, no cambia su lógica), flujo de licencia / reset / acceso limitado, persistencia de geometría de ventana.

## 5. Patrones de pantalla

Ver `docs/ui-v2/mockups/referencia/`. El patrón de listado está en `direccion-visual-aprobada.html`; formulario, Catálogos, Inicio y Login en `patrones-1.html`.

### 5.1 Listado

`EncabezadoPagina` + `TarjetaContenido` con `BarraFiltros`, grilla y pie de conteo.

**Productos** conserva sus columnas actuales (Código, Nombre, Costo, Stock, Unidad, Categoría, Estado) y su tratamiento de stock negativo: `Insignia` peligro "Stock negativo" y el número en `#B91C1C`.

**No se implementa "stock bajo".** Decisión explícita del usuario (2026-09-28): `StockMinimo` existe en el dominio pero no se usa ni se expone.

### 5.2 Formulario

- Breadcrumb.
- Card de ancho máximo 760 con secciones tituladas.
- Grilla de 2 columnas donde los campos lo permitan (campos cortos y emparentados); campos largos a ancho completo.
- Cada campo es un `CampoFormulario`.
- Un solo botón primario; botones al pie, alineados a la derecha.
- Los textos reales se conservan (el botón dice "Guardar", no otra cosa).

### 5.3 Inicio (dashboard)

- Saludo y fecha.
- Fila de 3 `TarjetaMetrica`, cada una visible solo con su gate actual:

| Métrica | Fuente | Gate |
|---|---|---|
| Facturas a vencer en 7 días | `CalendarioPagosDto.AVencer7Dias` vía `IFinanzasVistasService` | `VerFinanzas` o Admin |
| Tareas vencidas | `ITareaService` + `PanelVencimientosTareas` | `GestionarTareas` o Admin |
| Último backup | `IBackupsService.ObtenerSaludAsync` | solo Admin |

- Panel "Tareas que requieren atención" con `Insignia` Vencida / Próxima (reemplaza las viñetas ●/○).
- Accesos rápidos: los 6 actuales, con sus gates, como `TarjetaAcceso` con ícono.
- Avisos de backup como `Alerta`, **manteniendo la distinción actual** entre salud desconocida y backup vencido.

### 5.4 Login y pantallas fuera del shell

Login, Reset, BloqueoLicencia y AccesoLimitado: card centrada de 400 sobre fondo app, marca, textos reales ("Entrar", "No puedo entrar"), `Alerta` de error y **todos** los estados actuales, incluido el aviso de "Modo de acceso limitado".

### 5.5 Diálogos y overlays

Los 4 diálogos `Window` y los 3 overlays de actualización se rediseñan con el mismo sistema (tokens, botones, `Alerta`). Los diálogos siguen sin bindings (acceso por `x:Name`).

## 6. Fidelidad al mockup

Requisito central de este rediseño.

### 6.1 Un mockup por pantalla

- Uno por pantalla: ~55 de contenido, más shell, pantallas de acceso, diálogos y overlays.
- Contenido real y datos de ejemplo fijos, a **1280×760 y a 1366×768**. Varias PCs del municipio tienen pantalla de 1366×768; es un tamaño de uso real, no un caso borde.
- Ubicación: `docs/ui-v2/mockups/<modulo>/<pantalla>.html`, usando el CSS generado de tokens.
- **Solo técnicas que Avalonia replica 1:1:** colores sólidos, bordes de 1px, radios, una única `BoxShadow`, Inter, px enteros, múltiplos de 4.
- **Prohibido:** gradientes, blur, transparencias apiladas, transiciones, pseudo-elementos decorativos, `text-shadow`.
- Se aprueban por módulo.

### 6.2 Herramienta de comparación (se construye en Fase 0)

1. Render headless de la vista Avalonia real a 1280×760 y a 1366×768 (Avalonia.Headless con Skia, captura de frame), con un ViewModel cargado con **los mismos datos fijos del mockup** → PNG.
2. Captura del mockup con Playwright al mismo tamaño (a cada uno de los dos) → PNG.
3. Página de comparación por pantalla: ambas imágenes lado a lado más una imagen de diferencias resaltadas.
4. Reporte de porcentaje de píxeles distintos.

La comparación corre a ambos tamaños con el mismo umbral (no se relaja para 1366).

### 6.3 Aceptación

- El usuario aprueba cada pantalla mirando la comparación.
- Umbral bajo de diferencia: absorbe el antialiasing de texto; cualquier diferencia de layout, color o tamaño lo supera.
- Ese umbral queda como **test de regresión por pantalla**.
- El valor inicial se calibra en la Fase 0 con la pantalla catálogo-de-componentes y se documenta en la spec de esa fase.

### 6.4 Verificación orgánica

Obligatoria por módulo, con la app real contra la base: login, navegar, crear y editar. Salvedad: el render headless y Windows pueden diferir en antialiasing; por eso el umbral no es 0 y **la aprobación final es del usuario mirando la app**. La verificación orgánica incluye una ventana de 1366×768.

### 6.5 Calibración del umbral (Fase 0)

Medido el 2026-09-30 con la pantalla catálogo-de-componentes (`CatalogoComponentesTests.Catalogo_IgualAlMockup`, `componentes/catalogo.html`), sobre la base `ade444a`.

**Métrica.** Un píxel es distinto fuerte si algún canal RGB difiere en más de 32 (tolerancia de canal) y leve si difiere en más de 4. Se agrupa en teselas de 16×16: una tesela es densa si tiene 25% o más de píxeles fuertes o 90% o más de leves. Las compuertas son el porcentaje de píxeles fuertes y la cantidad de teselas densas. El mockup se captura con Chromium headless (Playwright) con `--font-render-hinting=none`, para que el ancho y la posición de los glifos sean los de Skia.

**Línea de base (3 corridas).** Determinista:

| Corrida | Porcentaje | Teselas densas |
|---|---|---|
| 1 | 0.837% | 11 |
| 2 | 0.837% | 11 |
| 3 | 0.837% | 11 |

Qué es ese ruido: la geometría es idéntica; el texto de la app sale uniformemente entre 7% y 18% más claro que en Chromium (rasterización de Skia frente a la de Chromium) y eso produce 8 teselas; las otras 3 vienen de centrados subpíxel de menos de 0.5px. El usuario aceptó esa diferencia de tono el 2026-09-30: lo que se exige idéntico es la geometría.

**Defectos chicos (mutando el catálogo, QUITANDO):**

| # | Se quitó | Defecto que representa | Porcentaje | Teselas densas |
|---|---|---|---|---|
| M1 | `Classes="primario"` del botón "Guardar" | color equivocado en un control chico | 1.153% | 28 |
| M2 | `Variante="Peligro"` de la insignia "Stock negativo" | relleno pálido equivocado (delta < 32) | 0.870% | 16 |
| M3 | `Margin="0,0,0,12"` del subtítulo "Datos del producto" | layout corrido 12px | 2.295% | 92 |
| M4 | `Spacing="8"` del StackPanel de las alertas | layout corrido 8px en bloques chicos | 2.689% | 262 |

**Regla.**

- Teselas densas máximas = las de la línea de base (el máximo de las tres corridas). Cada mutación tiene que superarlo; si alguna no lo supera, la métrica no la detecta y no se sube el umbral para taparlo.
- Porcentaje máximo = el mayor entre (base × 2) y (base + 0.05), redondeado hacia arriba a 0.01. Es una red secundaria contra diferencias difusas grandes (fondo equivocado en toda la pantalla); el detector de defectos chicos son las teselas.

**Valores elegidos** (`UmbralComparacion`): `TeselasDensasPorDefecto = 11` y `PorcentajePorDefecto = 1.68` (0.837 × 2 = 1.674 → 1.68). Con ellos el catálogo pasa y M1 a M4 fallan. No se suben por pantalla sin aprobación del usuario.

**Guardián de texto recortado.** Ninguna métrica de píxeles ve un glifo que falta: son 40 a 60 píxeles de tinta, lo mismo que pesa el ruido. En la Task 17 los encabezados "CÓDIG" y "STOC" pasaron la comparación (arreglarlos dejó las teselas en 11 → 11), y bajar el umbral no sirve porque el defecto pesa igual que el ruido. Por eso `CompararConMockup` también recorre los `TextBlock` visibles de la vista renderizada, con la ventana todavía abierta (cerrada, la vista pierde los estilos de la ventana y mide distinto), y falla si alguno no se ve entero:

- **Recorte propio:** el texto pide más lugar del que el layout le asignó (sin restricción de ancho si no hace wrap; con su ancho si hace wrap, comparando el alto). Se mide sin el `Width`/`MaxWidth`/`Height`/`MaxHeight` propios del TextBlock, que también limitan su Measure.
- **Recorte por un ancestro:** el texto cae fuera de un ancestro con `ClipToBounds` o de la ventana (1280×760). En un StackPanel horizontal el TextBlock siempre recibe lo que pide (breadcrumb de `EncabezadoPagina`), así que solo esto lo detecta. Excepción: el viewport de un `ScrollViewer` no cuenta en el eje en que se puede scrollear (el texto sigue alcanzable); desde ahí hacia afuera se controla la caja del viewport en ese eje, que sí tiene que verse entera (un ScrollViewer o una grilla que se salen de la ventana se reportan, Fase 1 Task 2). En un eje con scroll `Disabled` cuenta el texto. El `DataGrid` scrollea sin `ScrollViewer` y su plantilla recorta (`Border#DataGridBorder`, la Grid interna de Fluent): desde su presentador de filas (vertical y horizontal) o de headers (horizontal) hacia afuera vale la misma excepción, así la última fila parcial o una columna fuera de vista no se reportan. El texto recortado DENTRO de una celda (celda angosta, texto largo) sí se reporta.
- **`TextTrimming` no exime:** un texto con "…" que efectivamente recorta se reporta, salvo que el test lo permita por su texto (`recortesPermitidos`), alineado con un mockup que use `text-overflow: ellipsis`.

Verificado por mutación: con la plantilla de header de Fluent (que reserva 32px para la flecha de orden) el catálogo queda en 0.843% y 11 teselas —pasaría los píxeles— y el guardián lo rechaza con "CÓDIGO" pide 49 y tiene 44, "STOCK" pide 42 y tiene 34.

Cubre también el valor de `TextBox`, `NumericUpDown`, `CalendarDatePicker` y `ComboBox` editable (lo dibuja un `TextPresenter`: se mide su `TextLayout`, y el `ScrollViewer` interno del `TextBox` no relaja ejes porque nadie lo scrollea para leer), el placeholder, un `TextBlock` con `Inlines` y `Text` nulo, y `MaxLines` (se mide sin el límite). Un valor de contraseña se reporta como "(contraseña)", nunca en claro (Fase 1, Task 1). Un `TextBox` multilínea (`AcceptsReturn` o con wrap) relaja solo el eje vertical: se lee scrolleando; el horizontal no (Fase 2, Task 1). Desde el presentador de filas del `DataGrid` el último chequeo es la ventana: probado con una raíz que no recorta (Fase 2, Task 1).

**Umbral por pantalla.** En una pantalla nueva no hay "base sin defecto", y las teselas densas cuentan en absoluto, sin normalizar por área de texto: el ruido de tono crece con la cantidad de texto, y un listado denso puede superar las 11 del catálogo sin tener ningún defecto. Un umbral mayor nunca es un número suelto:

1. Sin aprobación, `CompararConMockup` solo acepta `teselasDensasMax` y `umbral` (porcentaje) menores o iguales a lo calibrado (11 y 1.68%); uno mayor falla antes de renderizar. `umbral: 100` apagaría la red secundaria.
2. Para subirlo, el usuario revisa en la página de comparación CADA tesela densa (contorno magenta) y confirma que es solo tono de texto, sin geometría distinta. Se registra con `UmbralAprobado(TeselasDeTono, AprobadoPor, Fecha "yyyy-MM-dd", Motivo)`, que `CompararConMockup` recibe en lugar de los números; un registro incompleto falla. Si también hace falta superar el porcentaje, va en el mismo registro (`PorcentajeMaximo`); sin él queda el calibrado.
3. Tope: M2 (relleno pálido equivocado) dio 16 teselas en el catálogo. Un umbral aprobado de 16 o más dejaría pasar ese defecto, así que además exige `MutacionPalidaEnRojo`: correr en ESA pantalla la mutación equivalente a M2 (quitar la variante de color de un relleno pálido), ver el rojo y registrar qué se quitó y cuántas teselas dio. Sin eso, falla.

**Métrica descartada: caja de la tinta por tesela.** Se evaluó comparar, por tesela, el rectángulo que ocupa la tinta (píxeles que difieren del color de fondo de la tesela) y fallar si algún borde difiere más de 1px. No es robusta a la gamma: dónde termina la tinta depende de qué tan oscuro sale el borde del glifo, que es justamente lo que difiere entre Skia y Chromium. Sobre la línea de base da 19, 35 y 79 teselas falsas con umbral de tinta 48, 96 y 128 (frente a 11 de la métrica de teselas densas).

**Excepciones a "px enteros, múltiplos de 4" (§2.2, §6.1).** No son valores de diseño: compensan la geometría medida de las plantillas de Avalonia para que app y mockup coincidan al píxel.

- `Padding 13` horizontal en ComboBox, NumericUpDown y los TextBox internos de sus plantillas: las plantillas Fluent no descuentan el borde de 1; 13 = los 12 de diseño + 1 de borde (`ControlesV2.axaml`).
- `RowHeight 39` de la grilla: la fila mide RowHeight + 1 de `PART_BottomGridLine`, así el paso es 40 = `V2AltoFilaGrilla` (`DataGridV2.axaml`).
- Márgenes del mockup `-3px` (glifo del ComboBox) y `-7px` (ícono del CalendarDatePicker): ubican el glifo donde lo dibuja la plantilla de Fluent (`base.css`).
- `4.375px` (y `5.82px`, `6.69px`) del tilde del CheckBox: posición y tamaño del Path de Fluent medidos en el render (`base.css`).
- `Margin="0,1,0,0"` en `PART_Titulo` de `ItemSidebar`: compensa 1px de baseline de Skia contra Chromium (`ComponentesV2.axaml`).
- `Margin="0,1,0,0"` en la `TarjetaContenido` del Login: con alto de card impar el centrado da medio píxel, Avalonia redondea al par y Chromium hacia arriba; el píxel extra los alinea. Depende de la paridad del alto de la card: si un estado cambia de alto, se vuelve a medir (`LoginView.axaml`).
- `Margin="0,1,0,0"` en la `TarjetaContenido` de `ResetAdminView`: misma causa y misma paridad que en el Login (card de alto impar; sin el margen la comparación daba 75 teselas densas). `BloqueoLicenciaView` NO lo lleva: su card es de alto par y medido con y sin margen da igual (2 teselas).
- `UseLayoutRounding="False"` en el enlace "¿Necesitás bajar un backup?" de `BloqueoLicenciaView`: el botón centrado tiene ancho fraccionario; Avalonia redondea su origen a píxel entero y Chromium no (10 teselas densas contra 2). Sin fondo ni borde.
- La etiqueta "1) Generar código de recuperación" de `ResetAdminView` va en un `TextBlock` con `TextAlignment="Center"` dentro de un botón estirado: como `Content` string centrado, el origen del texto se redondeaba (16 teselas contra 3).
- `LineHeight` = `V2FuenteControlAltoLinea` (16) en `TextBlock.texto-secundario` (`ControlesV2.axaml`): sin él, el párrafo envuelto de `BloqueoLicenciaView` salía a 15 px de interlineado y las líneas 2 y 3 caían 1 px arriba (26 teselas contra 10).
- `LineHeight` 16 (token `V2FuenteControlAltoLinea`) en `PART_Texto` de la `Alerta`: el natural de Avalonia son ~15px y el mockup usa 16, por lo que la segunda línea de una alerta envuelta salía 1px más arriba (`ComponentesV2.axaml`).
- Ancho de texto redondeado hacia arriba: `width: calc-size(fit-content, round(up, size, 1px))` en `base.css`, porque un TextBlock mide el ceil del ancho del texto. `calc-size()` solo funciona en Chromium: en otro navegador el mockup queda hasta 1px distinto. La comparación siempre usa Chromium.

**Salvedad (§6.4).** El render headless y Windows pueden diferir en antialiasing: la aprobación final es del usuario mirando la app.

## 7. Fases

Ciclo de cada fase: mockups de sus pantallas → aprobación del usuario → XAML → comparación de capturas → verificación orgánica → commit.

| Fase | Contenido |
|---|---|
| 0 Fundaciones | `tokens.json` + generador, herramienta de comparación, componentes, pantalla catálogo-de-componentes comparada (calibra el umbral) |
| 1 Shell y acceso | sidebar, Login, Reset, BloqueoLicencia, AccesoLimitado, 4 diálogos, 3 overlays |
| 2 Inicio | KPIs, panel de tareas, accesos rápidos |
| 3 Inventario | Productos (listado y formulario), Entrada / Salida + `MovimientoFormControl`, Ingreso por factura, Historial de movimientos |
| 4 Finanzas | Gastos + formulario, Pagos, Adjuntos, Ingresos + formulario, Libro caja, Control POA, Calendario de pagos |
| 5 Gestión | Tareas (listado, formulario, clasificación, diálogo de reclasificar), Documentos (listado, formulario, adjuntos) |
| 6 Reportes | hub + los 6 reportes |
| 7 Administración | Catálogos master-detail con los 11 catálogos, Usuarios, Mantenimiento |
| 8 Importación | wizard `NuevaImportacionView` + historial; apagada, verificable solo por comparación; unidad de riesgo propia |
| 9 Limpieza | borrar estilos y componentes viejos, reemplazar `GuardianDePatronTests` por el guardián del sistema nuevo, auditoría de residuos |

**Estado de la Fase 1 (aprobada el 2026-10-05).** El usuario aprobó la Fase 1 tras probar flujos reales a 1366×768: crear y editar artículo, entrada y salida, anular documento, baja de categoría y cerrar sesión. Arreglos surgidos de esa prueba: placeholder de precio de entrada (`2a5e661`, main), motivo Venta pasa a Uso o consumo (`ff91f33`, main), baja de categoría bloqueada con productos activos (`ee461b8`, main), la salida nunca muestra precio (`d858221`, main), y el hueco a la derecha en celdas y headers de las 22 grillas del tema viejo (`c808334`, feat/ui-v2; el `961dc7e` previo no tenía efecto porque el contenido no está dentro de `.ui2`).

**Deuda menor de la Fase 1 (no bloquea):**
- Sin guardián propio de `EsAdmin` en Importación (hoy lo tapa `ImportacionHabilitada=false`); resolver al reactivarla (Fase 8).
- Las comparaciones de píxeles no custodian el gate `IsVisible=EsVisible` del sidebar ni la ubicación de `ui2` (lo custodian los UiTests).
- Pulgar de scroll de Fluent (2 px) en el sidebar del Administrador que el mockup no tiene (P4); dentro del umbral.
- `Mensaje_IgualAlMockup` (umbral 12 aprobado), `Modal_IgualAlMockup` y `LoginAccesoLimitado_IgualAlMockup_1366x768` pasan sin margen (tono de rasterizador); frágiles ante cambios de render.
- Margin `0,1,0,0` de la card de Reset es estático: con `AlertaCompletado`/`AlertaError` visibles cambia la paridad del alto (sin mockup de esos estados).
- `V2FuenteTituloPaginaAltoLinea` sin test de control real equivalente al de la Alerta; `% 16` sobre double puede dar falso rojo con escalado fraccional.
- Guardián de recorte: cobertura "la ventana" del presentador de filas de DataGrid sin cubrir; TextBox multilínea de altura fija con valor largo se reporta aunque scrollee (usar `recortesPermitidos` si aparece); `.v2-ventana` sin chequeo fail-closed de que mida el lienzo.
- Sin chequeo de `Opacity` literal en vistas migradas a v2.
- Cosméticos: comentario de `GrupoNavegacion.cs:11-14` menciona Finanzas/Maestros; comentario de `ControlesV2.axaml` dice `.v2-boton` (real `.v2-boton--icono`); `reset.html` declara `.mt-12` y `.enlace-centrado` sin uso.

**Integración:** merge periódico de `main` hacia `feat/ui-v2`. El merge a `main` se hace **una sola vez, al final**, con `--ff-only` + push (convención del repo). No se integra por fase: dejaría la app mitad vieja y mitad nueva.

## 8. Tests

### 8.1 Se mantienen tal cual (red de seguridad)

- Gates de permisos: `ShellMainViewGatesTests` (localiza por identidad de `Command`, sobrevive al cambio de XAML), `DocumentoListViewGatesTests`, `DocumentoFormViewGatesTests`, `MovimientoHistorialGatesTests`, `FinanzasRevocacionPermisosTests`.
- Validación: `MovimientoFormControlValidacionTests` y el seam `ErrorConverter`.
- `CargaProtegidaSinPermisoGuardianTests`.
- `ReflexionVistaViewModelTests`: las rutas de los `.axaml` se conservan (no se renombran ni se mueven vistas).
- `ViewLocatorTests`.
- Guardianes de exportación PDF / CSV.

### 8.2 Se reemplazan

- `GuardianDePatronTests` (patrones P0-P9 viejos) → guardián del sistema nuevo.
- Los 4 asserts `TranslatePoint` de `MantenimientoViewTests`.
- Tests de tokens y contraste viejos (los valores cambian) → equivalentes sobre los tokens nuevos.
- Tests `DataGrid*` de estilo.

### 8.3 Nuevos

- Gates de las tarjetas del hub de Reportes.
- Gates de los grupos de Catálogos.
- Headers de sección del sidebar ocultos cuando no tienen ítems visibles.
- Gates de las tarjetas KPI de Inicio.
- Test de comparación visual por pantalla (umbral de 6.3).

## 9. Riesgos y partes frágiles

| Riesgo | Cómo se respeta |
|---|---|
| Wiring de `DataContextChanged` en code-behind (44 vistas) | se reproduce en cada vista reescrita; las vistas de Avalonia no se autoinicializan |
| `ViewLocator` por convención de nombre | no se renombran ni se mueven vistas; las nuevas (hub de Reportes, Catálogos) siguen la convención |
| `INavigationService` sin back-stack | el breadcrumb usa el evento `Volver` existente |
| Desuscripción de `Cambiado` | se conserva en cada vista que la tenga |
| `AdjuntosPanelView` vs `AdjuntosDocumentoPanelView` | **no se unifican**: tienen ViewModels distintos |
| `NuevaImportacionView` (509 líneas, 4 grillas, `CompileBindings=False` en 2 estilos) | fase propia (8); sin red del compilador en esos estilos |
| Diálogos `Window` sin bindings | se mantiene el acceso por `x:Name` |
| `MinWidth` por columna + scroll horizontal en grillas (fix `3548397`) | el tema nuevo de `DataGrid` lo preserva |
| Decimales y formato es-UY | converters actuales sin cambios |
| Rama larga | merges de `main` frecuentes |
| Worktrees sin fixtures `.ods` | los rojos de Infrastructure.Tests / Api.Tests por `.ods` en el worktree son falsos conocidos |

## 10. Fuera de alcance

- Modo oscuro.
- Stock bajo / `StockMinimo`.
- Unificar ViewModels.
- Cambiar la navegación a back-stack.
- Reactivar Importación.
- Cambios de API, salvo lo necesario para los KPIs de Inicio (los datos ya existen).
