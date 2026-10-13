# StockApp — UI v2: rediseño total de la interfaz

**Fecha:** 2026-09-28
**Estado:** aprobado por el usuario; Fases 0, 1, 2, 3a, 3b, 3c-1, 3c-2, 3c-3, 4a y 4b implementadas en feat/ui-v2; Fase 1 APROBADA por el usuario el 2026-10-05; Fase 2 APROBADA por el usuario el 2026-10-06; Fase 3a (Productos) APROBADA por el usuario el 2026-10-07; Fase 3b (Entrada/Salida) APROBADA por el usuario el 2026-10-08, con la verificación orgánica parcial; Fase 3c-1 (patrones comunes + Productos) APROBADA por el usuario el 2026-10-09; Fase 3c-2 (Historial de movimientos) APROBADA por el usuario el 2026-10-10; Fase 3c-3 (Ingreso por factura y patrón de modal v2) APROBADA por el usuario el 2026-10-11 (3c-3 aprobada por el usuario el 2026-10-11; incluye las decisiones del GATE, rulings y UmbralAprobado; ver "Para revisión del usuario" en §7); Fase 4a (Adjuntos, Calendario de pagos y Control POA) APROBADA por el usuario el 2026-10-12; Fase 4b (Ingresos de caja y su formulario) APROBADA por el usuario el 2026-10-12, con la verificación orgánica parcial (merge a main al final de todas las fases)
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
| Título de sección (card, panel, detalle de catálogo) | 15 / 600, alto de línea 19 | primario |
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

**`calt` de Inter apagado en toda la UI v2 (decisión del usuario, 2026-10-07).** Avalonia aplica la ligadura contextual de Inter (`2x1,5` se leía `2×1,5`), así que la UI v2 va con `FontFeatures` `-calt` (app: una sola vez en `:is(Control).ui2`, heredable, y repetido donde la colección se reemplaza; mockups: `font-feature-settings` de `html, body` en `base.css`). Alcanza también a todo `ToolTip` y `FlyoutPresenter` de la app, incluidos los de las vistas viejas, porque esos estilos van sin `.ui2` (inocuo). Ver la compensación equivalente en §6.5.

**Precedencia:** los valores de esta sección son los tokens canónicos. Los mockups de referencia se aprobaron como dirección y pueden diferir en detalles (letter-spacing en `em`, tamaños puntuales); los mockups por pantalla de cada fase se construyen con el CSS generado de estos tokens, así que ahí no hay conflicto posible.

## 3. Enfoque: reescritura en el lugar por módulos, mockups como contrato

- Rama `feat/ui-v2` en worktree propio.
- **Se reescribe el XAML de cada vista en su mismo archivo.** Los ViewModels quedan intactos salvo lo mínimo: hub de Reportes, Catálogos, KPIs de Inicio y datos del usuario en el pie del sidebar; y el estado de carga (decisión del usuario 2026-10-08, Fase 3c-1): `ViewModelBase` expone `Cargando` (contador de cargas en curso, prendido por `EjecutarCargaProtegidaAsync`) y cada listado que se migra pasa su carga por ese helper, para no mostrar el estado vacío durante la carga ni tras un 403; y la carrera de consultas (decisión del usuario 2026-10-09, Fase 3c-2): cada listado que se migra pide un turno a `ConsultaVigente` antes de consultar y descarta un resultado que ya no es el último (Productos e Historial); y el diálogo de Recalcular stock (decisión del usuario 2026-10-09, Fase 3c-2): `MovimientoHistorialViewModel` recibe `IRecalcularStockDialogService` y expone `AbrirRecalcularCommand`, que reusa el `RecalcularCommand` de siempre; y el patrón de modal v2 (decisión del usuario 2026-10-11, Fase 3c-3): `IngresoPorFacturaViewModel` recibe `IAltaProductoRapidaDialogService` e `IConfirmacionPreciosDialogService`, pierde los flags de los overlays (`MostrandoAltaProducto`, `MostrandoConfirmacionPrecios`, `GuardarEsAccionPrincipal`) y sus comandos Confirmar/Cancelar, y `AbrirAltaProducto` y `GuardarAsync` esperan al servicio.
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

Qué es ese ruido: la geometría es idéntica; el texto de la app sale uniformemente entre 7% y 18% más claro que en Chromium (rasterización de Skia frente a la de Chromium) y eso produce 8 teselas; las otras 3 vienen de centrados subpíxel de menos de 0.5px. El usuario aceptó esa diferencia de tono el 2026-09-30: lo que se exige idéntico es la geometría. Ese 7–18% se midió sobre el catálogo, que no tiene texto de peso 400 en color primario. Medido en la Fase 2 (Task 11, proporción de tinta app/mockup para el mismo estilo): el texto 400 sobre `#1C1917` (13 a 14 px) sale entre 23,5% y 26% más claro, tanto en las filas de tarea de Inicio (0,740 a 0,765) como en el cuerpo 14/400 del diálogo de confirmación aprobado en la Fase 1 (0,755). Para un mismo estilo la proporción se repite con dos decimales entre pantallas: título de página 22/600 0,891; título de sección 15/600 0,83; título de acceso 13/600 0,82; texto terciario 12/400 0,93; alerta 13/400 sobre fondo de color 0,78 a 0,79.

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
- `Margin="0,1,0,0"` en la `TarjetaContenido` de `ResetAdminView`: misma causa y misma paridad que en el Login (card de alto impar; sin el margen la comparación daba 75 teselas densas). `BloqueoLicenciaView` NO lo lleva: su card es de alto par y medido con y sin margen da igual (2 teselas). Medido también con `AlertaCompletado` y `AlertaError` visibles (Fase 2, Task 12): el alto de la card es impar en los tres estados (583, 657 y 641 px; `Bounds` con y sin margen). Mecanismo: un margen superior de 1 px en un hijo centrado corre la card 0,5 px; Avalonia redondea .5 al par y Chromium hacia arriba, y solo difieren cuando la parte entera del tope es par. En el estado base a 1280x760 el tope es 88,5 (Avalonia 88, Chromium 89) y el margen lo corrige; en completado (51,5) y error (59,5) la parte entera es impar, así que a ese tamaño el margen no cambia nada, pero a otras alturas de ventana sí. El margen es necesario siempre que el alto sea impar y vale para los tres estados; la neutralidad en los estados con alerta es coincidencia de la altura de ventana, y quitarlo deja verdes los 4 tests con alerta: solo lo custodia `Reset_IgualAlMockup`.
- `UseLayoutRounding="False"` en el enlace "¿Necesitás bajar un backup?" de `BloqueoLicenciaView`: el botón centrado tiene ancho fraccionario; Avalonia redondea su origen a píxel entero y Chromium no (10 teselas densas contra 2). Sin fondo ni borde.
- La etiqueta "1) Generar código de recuperación" de `ResetAdminView` va en un `TextBlock` dentro de un botón estirado: como `Content` string centrado, el origen del texto se redondeaba (16 teselas contra 3). El centrado lo pone el `TextBlock.TextAlignment="Center"` heredado del estilo común de los botones v2 (bullet siguiente); el `TextAlignment` local que llevaba se quitó.
- `TextBlock.TextAlignment="Center"` en el estilo común de los botones v2 (`ControlesV2.axaml`) y `HorizontalContentAlignment="Stretch"` en los botones estirados (Entrar del Login, "2) Resetear Admin" y "Volver al login" del Reset, Activar de `BloqueoLicenciaView`). El `TextBlock` del contenido mide el ancho del texto redondeado hacia arriba y dibuja el texto al inicio de esa caja; Chromium lo centra con su ancho fraccionario. Medido por perfil de tinta por columna (Fase 2, Task 13): "Aceptar" de `MensajeDialog` 0,45 px a la izquierda, "Posponer" del modal de actualización 0,35 px, "Sí, continuar" de Confirmación 0,20 px; con el estilo, todos entre −0,05 y 0. En un botón estirado el contenido centrado por `HorizontalContentAlignment` redondeaba su origen ("Entrar" quedaba 0,8 px a la izquierda, medido en el Login base y en el de acceso limitado a los dos tamaños); estirado y centrado por `TextAlignment` queda en 0. Sin el `Stretch`, el estilo solo empeoraba los botones estirados del Reset (3→8 teselas, el estado de error 5→11). Teselas densas antes → después: Mensaje 12→10 (1280) y 11→11 (1366), modal de actualización 10→7 y 4→0, bloqueo de actualización 4→1 y 5→0, Confirmación 1→0 y 6→3, PedirTexto 1→0 y 10→6, Login 4→3 y 7→6, Login con error 4→4 y 5→4, Login de acceso limitado 1→0 y 0→0; Reset (tres estados) y `BloqueoLicenciaView` sin cambio de teselas y con menos porcentaje. El catálogo de calibración baja de 11 a 8 (1280) y 9 (1366): eran las 3 teselas de "centrados subpíxel" de la línea de base; los umbrales calibrados no se tocan. `TextBlock.TextAlignment` es heredable: alcanza a TODO `TextBlock` descendiente del botón, no solo al del Content string. Medido (Fase 2, Task 13, botón `primario` bajo `.ui2`): un `TextBlock` anidado en un `StackPanel` de contenido, el de un `ToolTip.Tip` abierto y el de un `Flyout` abierto del botón salen todos con `Center`; sin `.ui2`, los tres con `Start`. La herencia a los popups se neutraliza en `ControlesV2.axaml` con `Style Selector="ToolTip"` y `Style Selector="FlyoutPresenter"` que fijan `TextBlock.TextAlignment` en `Start` (el default, así que no cambia nada fuera de los botones v2; sin `.ui2` porque el popup no cuelga de `.ui2`). Sin ellos, el tooltip estándar de bloqueo del guardado ("Corregí los campos marcados en rojo para continuar.", que `ErroresDeEntradaBehavior` pone en Entrar, "2) Resetear Admin" y Activar) salía envuelto en 2 líneas y centrado. Medido con los popups abiertos: botón `Center`, tooltip `Start`, flyout `Start`. Lo custodia `BotonV2_ToolTipYFlyoutNoHeredanElCentrado` (UiTests): quitar el estilo `ToolTip` o el `FlyoutPresenter` da rojo "Expected: Start, Actual: Center". `ContextMenu` y `MenuFlyout` no se usan hoy en la app; si aparecen en un botón v2, necesitan el mismo corte. Dentro del contenido del botón la herencia sigue: un botón v2 con texto envuelto o un `TextBlock` estirado en su contenido lo hereda centrado; hoy no hay ninguno, y si aparece se le pone `TextAlignment` local. El mecanismo no siempre produce diferencia: cuando la fracción del ancho del texto es casi 0 el render sale igual ("Ver calendario de pagos" de Inicio es idéntico píxel a píxel con y sin el setter, verificado por mutación). Ninguna comparación de píxeles queda en rojo al quitar la compensación (todas las pantallas pasaban antes); la custodian dos guardianes de `GuardianDePatronTests` (UiTests): `EstiloBotonV2_CentraElTextoConTextAlignment` (el TextBlock de "Entrar" centra; rojo sin el setter) y `VistaV2_BotonEstiradoEstiraSuContenido` (todo botón v2 estirado de `VistasV2Montables` estira su contenido; rojo sin el `Stretch` de Entrar). El `Stretch` no va en el estilo común: con contenido no textual (ícono, ícono y texto) estirarlo lo alinea a la izquierda. El `TextAlignment="Center"` local de "1) Generar código de recuperación" del Reset se quitó por redundante (Reset igual: 3, 4, 0, 3, 5 y 0 teselas).
- `LineHeight` = `V2FuenteControlAltoLinea` (16) en `TextBlock.texto-secundario` (`ControlesV2.axaml`): sin él, el párrafo envuelto de `BloqueoLicenciaView` salía a 15 px de interlineado y las líneas 2 y 3 caían 1 px arriba (26 teselas contra 10).
- `LineHeight` 16 (token `V2FuenteControlAltoLinea`) en `PART_Texto` de la `Alerta`: el natural de Avalonia son ~15px y el mockup usa 16, por lo que la segunda línea de una alerta envuelta salía 1px más arriba (`ComponentesV2.axaml`).
- Fase 3a (Productos), compensaciones medidas contra Chromium:
  - `Margin="0,-16,0,0"` en `EstadoSinPermiso` de `ProductoFormView`: el mockup apila el margen inferior del encabezado (16) y el superior del estado (32) y CSS los colapsa en 32; Avalonia los suma (48), así que se descuenta el de abajo del encabezado.
  - `UseLayoutRounding="False"` por instancia en `EstadoSinProductos` (`ProductoListView`) y `EstadoSinPermiso` (`ProductoFormView`): el estado va centrado y el ancho del título es fraccionario; Avalonia redondea su origen a píxel entero y Chromium no (16 teselas densas contra 11). Mismo caso que el enlace de `BloqueoLicenciaView`. En `ColumnaFormulario` del formulario también (con el redondeo una columna acotada por `MaxWidth` sale 761 en vez de 760). Va por instancia y no en el `ControlTheme` de `EstadoVacio`: en la instancia del catálogo mueve el texto 1 px en vertical (8 a 20 teselas).
  - `LineHeight` 19 (token `V2FuenteTituloSeccionAltoLinea`, ahora en `tokens.json`) en `PART_Titulo` de `EstadoVacio`: el mockup usa 19 y el natural de Avalonia para 15/600 no coincide.
  - Anchos de columnas `Auto` de la grilla de Productos, medidos en headless con el tema v2 y las 12 filas de los datos fijos (no son valores de diseño): CÓDIGO 81, COSTO 100 (su `MinWidth`), UNIDAD 97 y ESTADO 78, iguales a 1280 y 1366; STOCK 220 fijo; NOMBRE (2*) 212 y 298 y CATEGORÍA (*) en su piso de 160. ESTADO además se fija en 78 en la app: en `Auto` crecía 13 px al scrollear hasta la primera fila "Inactivo" y movía las demás columnas (`Scroll_HastaLaPrimeraFilaInactiva_NoMueveNingunaColumna`).
  - Celda de Stock con insignia + número: `.valor` con `calc-size(fit-content, round(up, size, 1px))` y `tabular-nums`, sin `v2-num` (ver convención 11 de `herramientas.md`).
- Fase 3c-3 (Ingreso por factura y diálogos de modal v2), compensaciones medidas contra Chromium:
  - Precios del diálogo de confirmación (`dialogos/confirmacion-precios.html`, `.precio`): `text-align: left` + `width: calc-size(fit-content, round(up, size, 1px))`, el mismo mecanismo de `.encabezado-derecha`. Avalonia redondea hacia arriba el ancho del `TextBlock` y dibuja el texto desde la izquierda, así que la tinta de un precio corto ("$ 38,00") quedaba hasta 1 px a la izquierda de la de Chromium. Antes → después: 19 → 12 teselas densas (1280) y 26 → 15 (1366). Las 12/15 restantes son antialias del mensaje de dos líneas (desplazamiento óptimo 0,0 y misma tinta) y van con `UmbralAprobado` (ver "Para revisión del usuario" en §7).
  - Pantalla (`inventario/ingreso-factura*.html`) y diálogo de alta: sin compensaciones nuevas. Las teselas de `-renglones` (13) y `-diferencia` (12) a 1280 son antialias de negritas de componentes ya aprobados (título, subtítulos de sección, valores del pie), con desplazamiento (0,0) y proporción de tinta dentro de las referencias de 600; el mockup no se tocó. El scroll del estado guardado (offset 494 a 1280 y 486 a 1366) se fija en el mockup con `translateY`, medido en headless (F7).
- Fase 4a (Adjuntos, Calendario de pagos y Control POA), compensaciones medidas contra Chromium:
  - `LineHeight` 16 (`V2FuenteControlAltoLinea`) en `PART_Mensaje` de `EstadoVacio`: sin él, un mensaje de dos líneas (el de sin conexión) dejaba la segunda 1 px arriba (15,73 por línea). Calendario sin conexión 12/14 teselas antes (1280/1366), 6/6 después; las demás pantallas con `EstadoVacio` quedaron iguales.
  - `MinWidth` 92 / 90 / 95 en FACTURA, VENCE y SALDO de las tres grillas de facturas del Calendario: `ColumnaAuto` mide por grilla y la de 7 días quedaba en VENCE 88 y SALDO 86; con el piso, las columnas coinciden entre cards (39 teselas antes en la réplica, 9 después). Con datos reales más anchos que el piso cada grilla vuelve a medir sola (2 px observados en la verificación orgánica, aceptado).
  - Título "Adjuntos" del panel: Avalonia redondea el centrado de 8,5 a 8; el mockup lo fija con `margin-top` 8 (antes, 4 teselas con dy=1).
- Fase 4b (Ingresos de caja y su formulario), compensaciones medidas contra Chromium:
  - `CalendarDatePicker` v2: el alto 36 (`V2AltoControl`) pasa del control a `/template/ Panel#LayoutRoot`, como el `NumericUpDown` (`2721715`): con el alto en el control, el mensaje de error entraba dentro de los 36 y la caja se aplastaba a 17 px (esperado 36, medido 17). Historial e Ingreso por factura sin cambio (`app.png` con md5 idéntico antes y después).
  - Mensaje de error de campo alineado a la izquierda (`52b9600`) aunque la caja del monto se alinee a la derecha.
  - Insignia "Inactivo" de 62 × 19 a 8 px del texto del concepto (CONCEPTO con `HorizontalAlignment=Left` en una `Grid` con columna `*`); FUENTE sin `MaxWidth` (123 px medido). Ver `UmbralAprobado` 13 de `IngresosSinResultados` en la deuda de la 4b.
- Pulgar de scroll del sidebar del Administrador en `sidebar-admin.html` (`.v2-sidebar-pulgar`): `#7A7A7A`, 2 px en x 223, desde y 84, 352 px de alto a 760 y 362 a 768 (media query). Es el indicador de Fluent medido en el render, no un token (Fase 2, Task 9).
- `calt` de Inter apagado en la app y en los mockups: ver §2.3 (decisión del usuario, 2026-10-07).
- Ancho de texto redondeado hacia arriba: `width: calc-size(fit-content, round(up, size, 1px))` en `base.css`, porque un TextBlock mide el ceil del ancho del texto. `calc-size()` solo funciona en Chromium: en otro navegador el mockup queda hasta 1px distinto. La comparación siempre usa Chromium.
- Mockups de Inicio (`docs/ui-v2/mockups/inicio/*.html`), aprobadas por el usuario el 2026-10-06, sin cambio visible:
  - A. `.fila-dias` lleva `width: calc-size(fit-content, round(up, size, 1px))` (el ancho redondeado de arriba): sin él, "-2 días" salía corrido 1 px y "3 días" 0,76 px respecto de la app, alineados a la derecha.
  - B. `.accesos` y `.metricas-3` usan `round(nearest, calc((100% - 24px) / 3), 1px) minmax(0, 1fr) round(nearest, calc((100% - 24px) / 3), 1px)` en vez de `repeat(3, minmax(0, 1fr))`: columnas de ancho entero con los bordes de `GrillaUniformeV2` (cada borde redondeado por separado; 309/308/309 a 1280 en accesos, 349/348/349 a 1366 en métricas). Con `1fr` el texto de las columnas 2 y 3 caía en x fraccionaria (±1/3 px) y la app lo dibuja en x entera. `.metricas-2` no lo necesitaba: (984 − 12) / 2 y (1070 − 12) / 2 son enteros. Desde la revisión final de la Fase 2 la receta vive en `base.css` (`.v2-grilla-uniforme--3` y `--2`, con `--v2-grilla-espaciado`) y los mockups de Inicio la toman de ahí, con render idéntico; es el espejo de `GrillaUniformeV2` para las fases siguientes (`docs/ui-v2/herramientas.md`).
  - Con A y B: Admin 56→40 (1280) y 67→46 (1366), Operador mixto 24→20 y 26→20, Administrador sin datos 40→28 y 49→32, Operador sin finanzas 10→9 y 11→9 teselas densas.

**Salvedad (§6.4).** El render headless y Windows pueden diferir en antialiasing: la aprobación final es del usuario mirando la app.

## 7. Fases

Ciclo de cada fase: mockups de sus pantallas → aprobación del usuario → XAML → comparación de capturas → verificación orgánica → commit.

| Fase | Contenido |
|---|---|
| 0 Fundaciones | `tokens.json` + generador, herramienta de comparación, componentes, pantalla catálogo-de-componentes comparada (calibra el umbral) |
| 1 Shell y acceso | sidebar, Login, Reset, BloqueoLicencia, AccesoLimitado, 4 diálogos, 3 overlays |
| 2 Inicio | KPIs, panel de tareas, accesos rápidos |
| 3 Inventario | Productos (listado y formulario), Entrada / Salida + `MovimientoFormControl`, Ingreso por factura, Historial de movimientos (3a, 3b, 3c-1 y 3c-2 aprobadas; 3c-3 Ingreso por factura y patrón de modal v2: 3c-3 aprobada por el usuario el 2026-10-11) |
| 4 Finanzas | Gastos + formulario, Pagos, Adjuntos, Ingresos + formulario, Libro caja, Control POA, Calendario de pagos. Se divide en subfases (aprobado por el usuario el 2026-10-11): 4a AdjuntosPanelView + Calendario de pagos + Control POA; 4b Ingresos de caja + formulario; 4c Libro caja; 4d Gastos y facturas + formulario; 4e Pagos del gasto (la 4e depende de la 4a y la 4d). 4a aprobada por el usuario el 2026-10-12; 4b aprobada por el usuario el 2026-10-12 |
| 5 Gestión | Tareas (listado, formulario, clasificación, diálogo de reclasificar), Documentos (listado, formulario, adjuntos) |
| 6 Reportes | hub + los 6 reportes |
| 7 Administración | Catálogos master-detail con los 11 catálogos, Usuarios, Mantenimiento |
| 8 Importación | wizard `NuevaImportacionView` + historial; apagada, verificable solo por comparación; unidad de riesgo propia |
| 9 Limpieza | borrar estilos y componentes viejos, reemplazar `GuardianDePatronTests` por el guardián del sistema nuevo, auditoría de residuos |

**Estado de la Fase 1 (aprobada el 2026-10-05).** El usuario aprobó la Fase 1 tras probar flujos reales a 1366×768: crear y editar artículo, entrada y salida, anular documento, baja de categoría y cerrar sesión. Arreglos surgidos de esa prueba: placeholder de precio de entrada (`2a5e661`, main), motivo Venta pasa a Uso o consumo (`ff91f33`, main), baja de categoría bloqueada con productos activos (`ee461b8`, main), la salida nunca muestra precio (`d858221`, main), y el hueco a la derecha en celdas y headers de las 22 grillas del tema viejo (`c808334`, feat/ui-v2; el `961dc7e` previo no tenía efecto porque el contenido no está dentro de `.ui2`).

**Decisiones del usuario de la Fase 2 (2026-10-06, tras la verificación orgánica):**
- Panel "Tareas que requieren atención" de Inicio con tope de 4 filas (vencidas primero, después próximas) y el resto en "Ver todas las tareas"; las métricas siguen contando el total.
- Con dos alertas a la vez (backup vencido y facturas vencidas, Administrador) Inicio necesita scroll y se acepta: 28 px a 1280×760 y 20 px a 1366×768 con el tope de 4 filas. Lo fija `InicioViewV2Tests.ConDosAlertasYSieteTareas_ElScrollNoPasaDelLimiteYLosAccesosSeAlcanzan`; si el contenido crece, da rojo.
- Foco de teclado de la fila de panel (`V2TemaFilaPanel`): borde de acento de 1 px (`PART_Foco`, `:focus-visible`), como TextBox y ComboBox con foco, sin el FocusAdorner de Fluent. Aprobado sobre capturas headless.
- El sidebar marca la sección de la pantalla mostrada por cualquier camino (sidebar, accesos y filas de Inicio, login, Entrada/Salida → Historial, Control POA → Gastos): la deriva `ShellMainViewModel` de la navegación real; una pantalla sin ítem propio conserva la sección.

**Estado de la Fase 2 (aprobada el 2026-10-06).** El usuario aprobó la Fase 2 (Inicio y deuda menor de la Fase 1) tras probar en la app real: operador sin finanzas (`f2sinfin`), Reset con token inválido, cerrar sesión y tamaño por defecto ("esta bien"). Arreglo surgido de la prueba: "Guardar permisos" alcanzable a 1366×768 con scroll en el panel (`c145c11`, main; mergeado a feat/ui-v2 en `5f3bf8c`). Las decisiones del usuario están arriba.

**Fase 3 partida en 3a, 3b y 3c (decisión del usuario, 2026-10-06):** 3a Productos (listado y formulario), 3b Entrada/Salida + `MovimientoFormControl`, 3c Ingreso por factura + Historial de movimientos; cada una con su ciclo completo. El patrón de modal v2 entra en 3c. La 3c se parte a su vez en 3c-1 (patrones comunes de carga y de columnas `Auto`, aplicados a Productos), 3c-2 (Historial de movimientos) y 3c-3 (Ingreso por factura y el patrón de modal v2) (decisión del usuario, 2026-10-08).

**Estado de la Fase 3a (aprobada el 2026-10-07).** Listado y formulario de Productos v2, comparados en 6 estados a los dos tamaños (12 filas aprobadas con el umbral por defecto, máximo 7 teselas densas); inactivos por token sin `Opacity` y stock negativo en `#B91C1C`; verificación orgánica a 1366×768 y 1280×760 con Administrador, operador mixto (`catalogo.productos` + `movimientos.registrar`, sin finanzas ni reportes) y un operador sin `catalogo.productos` (no ve Productos ni en el sidebar ni en Inicio). Decisiones del usuario: D1 a D11 (listado) y D12 a D18 (formulario) aprobadas; insignia "Inactivo" en masculino (D8); `calt` de Inter apagado en TODA la UI v2 (app y mockups) porque Avalonia sí aplica la ligadura (`2x1,5` se leía `2×1,5`); variante con error del formulario compactada (alerta con 8 de margen superior y pie con 20, Nuevo/Editar suben 4 px); ESTADO con ancho fijo de 78 para que las columnas no salten al scrollear hasta la primera fila inactiva; el sidebar que corta el último ítem a 1366×768 queda diferido. En formularios sin `Volver` el breadcrumb usa el comando de vuelta que el VM ya expone (`CancelarCommand` en Productos), en vez del evento `Volver` de §4.4: la navegación es idéntica y los ViewModels quedan intactos. Arreglo del cierre: el alto de línea 19 del título de sección vivía a mano en `Tokens.axaml` y no en `tokens.json` (rompía `tokens --verificar`).

**Estado de la Fase 3b (aprobada el 2026-10-08).** `MovimientoFormControl` v2 (Registrar entrada y Registrar salida), comparado en 4 estados a los dos tamaños (8 filas aprobadas con el umbral por defecto, máximo 7 teselas densas, sin `UmbralAprobado` ni compensaciones nuevas); el precio solo aparece en Entrada y la fila desaparece sin hueco en Salida; el comentario envuelve dentro de los 88 de alto y scrollea. Verificación orgánica a 1366×768 con Administrador (pasos 1 a 5 del brief: formulario, error de cantidad, asociar factura, Entrada por Ajuste, Salida con stock insuficiente con "No" y con "Sí"); el usuario aceptó la verificación parcial (ver la deuda abierta tras la 3b). Decisiones del usuario: D1 a D13 aprobadas con las recomendaciones. **D1 (sin breadcrumb) y D3 (una sola sección sin subtítulo) son desvíos aprobados de §4.4 y §5.2:** Registrar entrada/salida son pantallas del sidebar y no altas o ediciones que vuelven a un listado, así que §4.4 rige para esas; y §5.2 pide secciones tituladas sin condición, pero con un solo tema se aprobó una única sección sin subtítulo (D3 es un desvío aprobado de §5.2). D4: Cantidad | Motivo en una fila y Precio debajo; D7: placeholder "Elegí un producto"; D9: Registrar a la derecha. Cambio de ViewModel pedido por el usuario al cierre (fuera de la regla "VMs intactos"): el diálogo "Entrada registrada. ¿Desea asociar una factura de proveedor a esta entrada?" ofrece "No, continuar" y "Sí, adjuntar factura" en lugar de "Cancelar" y "Sí, continuar" ("Cancelar" confundía: la entrada ya está registrada). `IConfirmacionService` gana la sobrecarga `PreguntarAsync(mensaje, textoNegativo, textoAfirmativo)` (implementación por defecto que delega en la de un parámetro) y `ConfirmacionDialog` acepta los textos; los demás diálogos no cambian. El botón mide 154 de ancho y la fila de botones termina dentro del diálogo de 360 sin recorte ni elipsis (`DialogosV2Tests`).

**Estado de la Fase 3c-1 (aprobada el 2026-10-09).** Estado de carga (`ViewModelBase.Cargando`, `v2:EstadoCargando`) y estado sin permiso en el listado de Productos; columnas `Auto` de contenido libre medidas sobre todas las filas (`ColumnaAuto`, tope adaptable `min(medido, 240, espacio libre)` sin scroll horizontal salvo con la ventana menor a ~1218 px, reserva de 20 px de la flecha de orden, solo crecen mientras vive la grilla, ceden primero las `*` y después las medidas) con tooltip solo si se corta (`TooltipSiSeCorta`). Productos comparado en 18 estados a los dos tamaños; `scripts/ui-v2/comparar.sh` dio 81/81 aprobadas y la suite 5327 verde a la altura del commit 9b29aa5 (anterior a los fixes de abajo). Verificación orgánica a 1366×768 y 1280×760 (Administrador y operador mixto `catalogo.productos` + `movimientos.registrar`): carga lenta con la API pausada ("Cargando productos…" sin vacío ni grilla), catálogo de 1000 productos de prueba (scroll de punta a punta y orden por CÓDIGO, COSTO y UNIDAD sin que ninguna columna cambie de ancho), código largo cortado con elipsis y tooltip completo (sin tooltip si entra), búsqueda, 403 con "Sin permiso" en lugar de la card. Decisiones del usuario: mockups de Productos cargando, sin permiso y celda cortada aprobados el 2026-10-08 (D1 a D4, D6, D7, D8 y D13); **D5 reemplazado por el tope adaptable** `min(240, espacio libre)` con la regla "nunca scroll por una columna medida"; excepción U1 a §3 (ya asentada en §3). **Fixes posteriores a la verificación orgánica** (cada uno con test y verificado en la app): B1 (`ca56a8b`) si el servidor no responde al abrir Registrar entrada o Registrar salida, el combo de Producto ya no queda vacío para siempre con un modal: hay una `Alerta` de error en línea con el botón "Reintentar"; B2 (`36c269f`) la pregunta "¿Desea asociar una factura?" solo se hace a un Administrador o a quien tenga `finanzas.gastos` (la misma condición que `GastoFormViewModel.PuedeRegistrarGastos`), sin ella la entrada sigue como "No, continuar"; B3 (`050d4ca`) un 403 refresca el sidebar y la pantalla abierta en caliente vía `ManejadorAccesoRevocado`, y Nuevo, Editar y Eliminar de Productos quedan gateados por `PuedeGestionarProductos`; B4 (`73ea027`) la columna que el usuario arrastra guarda el ancho que le dio y el reparto sigue vigente alrededor de ella (las `*` no quedan bajo su piso; si no entra, scroll horizontal en vez de una columna recortada). **Decisiones del usuario que no se reabren:** (1) "Cancelar" del formulario de factura abierto desde una entrada sigue yendo a "Gastos y facturas" (se deja así); (2) los encabezados de las columnas numéricas quedan alineados a la izquierda, como en el mockup aprobado (B5 descartado).

**Estado de la Fase 3c-2 (aprobada el 2026-10-10).** Historial de movimientos v2 (listado §5.1 con barra de filtros etiquetada sin buscador libre, 10 columnas con scroll horizontal a 1280 y 1366, 7 medidas, STOCK NUEVO con insignia y stock negativo en rojo, PRECIO "—" cuando no aplica, estados de carga, vacío y sin permiso), diálogo v2 de Recalcular stock (Window del patrón de la Fase 1 con buscador de producto, no el modal de la 3c-3), carrera de consultas cerrada con `ConsultaVigente` en Historial y Productos, y las medidas vuelven a su ancho cuando igual hay scroll (cambia la regla de la 3c-1 para ese caso; FECHA no cede nunca, `ColumnaAuto.NoCede`). Dos fixes surgidos de la verificación orgánica: `8e0a671` un listado con el servidor caído muestra "No se pudo conectar con el servidor" con Reintentar dentro de la card en vez de "No hay" (`SinConexion` en `ViewModelBase`, opt-in por VM: Historial y Productos) y `a6ed0e1` una celda numérica reciclada ya no queda cortada con el ancho del número anterior (el `TextBlock` reusaba un `TextLayout` de un texto intermedio). Comparado a los dos tamaños (82 filas APROBADAS en `index.html` en el cierre de la Task 8, 0 rechazadas; suite 5460/5460 en `a6ed0e1`). Verificación orgánica con la app real: a 1366×768 (cargando, volumen de 1501 movimientos, columnas estables al scrollear y ordenar, tooltips, diálogo de Recalcular) y a 1280×760 (5 de 5 OK: "Sin permiso", diálogo con 403 del catálogo, convención 21 en Gastos, orden por FECHA, CANTIDAD y USUARIO, `SinConexion` con Reintentar y celda reciclada). Decisiones del usuario: D1 a D15 aprobadas en el GATE de la Task 4 (D4 opción a: orden FECHA, PRODUCTO, TIPO, CANTIDAD, STOCK ANT., STOCK NUEVO, MOTIVO, PRECIO, USUARIO, COMENTARIO); FECHA no cede (2026-10-09); excepciones a §3 (U1 y U3 de la 3c-2, ya asentadas en §3) y a §8.1 (`MovimientoHistorialGatesTests`, ruling H2, arriba). Tiempo de carga medido: la API responde `GET /movimientos/historial` en 12 ms (542 KB, 1556 movimientos); la UI ya mostraba la grilla dentro de la latencia de click más captura (~7 s), sin poder medirse más fino con el toolkit.

**Estado de la Fase 3c-3 (3c-3 aprobada por el usuario el 2026-10-11).** Ingreso por factura v2 (tres secciones, pie fijo fuera del scroll, un solo primario, Enter en la fila de carga con `EnterEjecutaComando`, diferencia en advertencia, card de adjuntos a la vista tras guardar) y patrón de modal v2 (`AltaProductoRapidaDialog` y `ConfirmacionPreciosDialog`, `Window` con servicio, convención 25 custodiada por `GuardianDePatronTests.NingunaVista_TieneOverlaysDeModal`). Comparado en 7 filas a los dos tamaños (14 de las 96 filas de `index.html`, todas APROBADAS; máximo 15 teselas densas en `dialogos-confirmacion-precios@1366x768`, con `UmbralAprobado` 16). Suite 5595/5595 en `c5c9a14` (línea base de la fase 5493). Convenciones 25 (confirmada), 26 y 27 en `herramientas.md`. Verificación orgánica con la app real a 1280×760 (pasos 1 y 4 también a 1366×768): factura con un producto existente, uno nuevo por el diálogo de alta y un cambio de precio; los dos diálogos con teclado (Escape cancela, Enter confirma, Tab no se escapa, Escape con el desplegable abierto lo cierra primero, Enter sobre el tilde confirma sin cambiarlo); Enter en Precio agrega y con Producto vacío abre la lista sin agregar; 30 renglones con el pie fijo y la diferencia en advertencia; guardar, card de adjuntos a la vista, PDF adjunto, Finalizar y el gasto en Gastos y facturas; operador de permisos mixtos con la API caída (alerta en línea entre el contenido y el pie, renglones intactos, sin Aviso global) y reintento que guarda. 19 de 21 ítems OK, una observación de teclado (Enter en el combo de Unidad del alta abre el desplegable y no confirma) y una FALLA parcial: el `CalendarDatePicker` de Fecha deshabilitado tras guardar sale con el gris de Fluent (deuda abajo). Datos de prueba borrados en una transacción con backup previo (`~/stockapp-backups-local/pre-3c3-20261011-041213.sql`); stock real igual antes y después. Decisiones: U1 a U4 del usuario; D1 a D16 y los rulings H2, F1 a F8 y T5 tomados por el controlador por delegación del usuario (2026-10-11), aprobados por el usuario el 2026-10-11.

**Estado de la Fase 4a (aprobada el 2026-10-12).** `AdjuntosPanelView` v2 (también dentro de los hosts v1 `GastoFormView` y `PagosGastoView`, excepción inversa hasta la 4d y la 4e, y en la card de Ingreso por factura), Calendario de pagos v2 (cuatro cards con contador y scroll de página completa, VENCE y número de factura, columna Registrar pago por permiso con la convención 28) y Control POA v2 (`v2:BarraEjecucion`, `Insignia` Sobreejecutado, Ejercicio que recarga solo con debounce y consulta vigente), con E1 a E5 (el reenvío de `NotificarPermisosActualizados` al panel es la convención 29). Comparado en 22 filas (11 pantallas a los dos tamaños, todas APROBADAS; máximo 19 teselas densas en `inventario-ingreso-factura-guardado` a 1280×760, con `UmbralAprobado` 19 del usuario del 2026-10-11, y 10 con el umbral por defecto en `finanzas-calendario-pagos-vacio`); corrida completa de `comparar.sh` 127/127. Suite 5708/5708 sobre el código de `524d863` (los dos arreglos del GATE 2; +3 tests sobre 5705) (línea base de la fase 5600). Verificación orgánica (GATE 2, 2026-10-12) con la app real contra la base local a 1280×760 y 1366×768, Admin y un Operador de permisos mixtos (`finanzas.ver` + `finanzas.gastos`): 28 OK, 1 FALLA y 9 observaciones. OK: cards, VENCE en rojo y "—", Registrar pago → Pagos → Volver, scroll de página (arrastrando la barra), columna oculta sin pagos en las tres grillas y visible con pagos, Actualizar, revocación de pagos en caliente (Pagos queda en solo lectura y el Calendario vuelve sin la columna), barra roja e insignia, Ejercicio con las flechas, orden por SALDO y % EJECUCIÓN, doble click real a Gastos filtrado, CSV y PDF (abierto), sin servidor en las dos pantallas sin modal global y Reintentar, adjuntos PDF/JPG/PNG con tamaño y fecha, Ver, Quitar con confirmación (cancelar y aceptar), y E5 en los tres hosts (Gasto v1, Pagos v1 e Ingreso por factura v2). La FALLA (el mensaje de error del Ejercicio inválido achicaba y tapaba la caja) y una observación (la card de adjuntos de Ingreso por factura no volvía a la vista al crecer) se arreglaron en la fase con TDD y mutación (`2721715`, `524d863`); la verificación GUI de esos dos arreglos no se pudo repetir porque WSLg dejó de mostrar las ventanas nuevas, y quedan custodiados por los UiTests con la View real. La rueda del mouse no se pudo probar con xdotool (en WSLg los botones 4 y 5 llegan como click). Datos de prueba borrados con backup previo (`~/stockapp-backups-local/pre-4a-20261012-004254.sql`). Decisiones: D1 a D18 aprobadas como estaban (GATE 1, 2026-10-11) con un cambio del usuario (scroll de página completa en el Calendario); en el GATE 2 el usuario aceptó la desalineación de 2 px de FACTURA, difirió a la 4d el cambio de ancho del formulario v1 de Gasto y dejó como deuda el doble Aviso tras un 403 en el panel.

**Estado de la Fase 4b (aprobada el 2026-10-12).** Ingresos de caja v2 (filtro en el cliente por concepto y Activos/Todos con la convención 30, pie con el total de los activos visibles, inactivos por token con insignia, acciones sobre la selección y doble click para editar, estados completos) y su formulario v2 (patrón `ProductoFormView`, errores por campo con estado tocado según la convención 31, Alerta del 403 de la carga de fuentes), con E1 a E5. Comparado en 24 filas a los dos tamaños (12 pantallas; máximo 13 teselas densas en `IngresosSinResultados` a 1280×760 y 1366×768, con `UmbralAprobado` 13 del usuario del 2026-10-12); corrida completa de la comparación 151/151 (140 filas APROBADA). Suite 5825/5825 sobre el código de `e109e2d`. Verificación orgánica (GATE 2, 2026-10-12) PARCIAL y sin reporte: con backup previo (`pre-4b-20261012-221044.sql`, 22:10) se usó la app real contra la base local; se crearon los ingresos `PRUEBA-4B` 3, 4 y 5 y el 5 se dio de baja desde la app, y se creó el operador de permisos mixtos `f4bmixto` (id 28). La verificación se interrumpió a pedido del usuario durante la revocación en caliente de `finanzas.ver`. Martin aprobó el GATE 2 el 2026-10-12 ("de todo lo que he visto está bien, yo lo daría por cerrado"). Lo no recorrido o no documentado (incluida la repetición a 1366×768) queda cubierto solo por los UiTests con la View real y la comparación visual. Fixes del tema fuera del plan, con TDD y mutación: `6e90a3f` (Task 6, antes del GATE 1; `CalendarDatePicker` v2: el alto 36 va en `/template/ Panel#LayoutRoot` para que el mensaje de error quede debajo de la caja; Historial e Ingreso por factura sin cambio, `app.png` con md5 idéntico) y `52b9600` (Task 8; el mensaje de error de campo arranca a la izquierda aunque la caja se alinee a la derecha). Decisiones: GATE 1 (2026-10-12) con D13=A, D14 (error del servidor con Fuente "FIGM Literal B" y Guardar habilitado) y D16 (+ mockup `ingreso-sin-permiso`); el resto de D1 a D16 como estaban. Datos de prueba `PRUEBA-4B` dados de baja con backup previo (`~/stockapp-backups-local/pre-4b-20261012-221044.sql`).

**Para revisión del usuario (Fase 3c-3).** El usuario delegó las decisiones el 2026-10-11 ("tomá las decisiones que creas correctas, mañana vemos"); lo que sigue lo firmó el controlador y queda para que el usuario lo apruebe o lo cambie:
Revisado y aprobado por el usuario el 2026-10-11 (decisiones del GATE, rulings y UmbralAprobado).
- `UmbralAprobado` (AprobadoPor "controlador SDD"): `ConfirmacionPrecios` 1280 tope 13 (medido 12) y 1366 tope 16 (medido 15), antialias del mensaje de dos líneas con desplazamiento óptimo (0,0) y la misma tinta; `IngresoFacturaRenglones` 1280 tope 13 (medido 13) e `IngresoFacturaDiferencia` 1280 tope 12 (medido 12), antialias de negritas con desplazamiento (0,0). Si no se aprueban, se bajan los topes y hay que resolver el antialias.
- GATE (Task 3): D1 título "Ingreso por factura" sin breadcrumb ni eyebrow; D2 una sola card con tres secciones; D3 cabecera en tres columnas con asterisco solo en lo que exige `PuedeGuardar`; **D4 "Cargar artículo" en DOS líneas** (en una fila Producto medía 175 px < 240 a 1280); D5 alerta de carga debajo de la fila; D6 grilla de solo lectura con "$" en precio y subtotal (cambio visual), Quitar `ghost`, estado vacío propio; D7 pie fijo con Guardar como único primario; D8 tras guardar, formulario deshabilitado, card de adjuntos con scroll automático y solo "Finalizar"; D9 diálogo de alta de 440 con foco en Código; **D10 precios del diálogo de confirmación con la tipografía del texto del tilde + `tnum`** (ajuste del controlador); D11 foco a Cantidad tras el alta; D12 y D13 estados y datos fijos como el plan; D14 el interior v1 de adjuntos no se compara; **D15 (H1) el tilde de la fila de carga se sigue pisando en la confirmación, que arranca destildada: queda como deuda** (alcance solo visual + modal); D16 reemplazo de `GuardianDePatronTests` L48.
- Rulings del controlador: **H2** ComboBox y CheckBox deshabilitados con estilo v2 acotado a `.ui2` (Task 7); teselas en el borde: no subir umbrales a ciegas, diagnosticar antialias contra diferencia real; F1 a F8 del pre-flight (medir anchos con un DataGrid v2, layouts de la fila de carga, F6 el pie fuera del scroll custodiado por `Abajo(scroll) <= Arriba(pie)`, F8 los espejos de teclado en el diálogo de precios); la limpieza de los datos de prueba se hizo sin preguntar, con backup.

**Diseño de la Fase 3c-3 (aprobado por el usuario el 2026-10-11).** Ingreso por factura v2 y el patrón de modal v2. Alcance: SOLO visual y patrón de modal; sin cambios funcionales. Plan: `docs/superpowers/plans/2026-10-11-ui-v2-fase-3c3-ingreso-factura.md`.

*Sección 1. Patrón de modal v2: Window.*
- Los dos overlays de `IngresoPorFacturaView` pasan a ser diálogos `Window` v2 con servicio, como `RecalcularStockDialog`. Hoy son `Border` con fondo `#AA000000` e `IsVisible` ligado a `MostrandoAltaProducto` / `MostrandoConfirmacionPrecios` (`IngresoPorFacturaView.axaml` ~L269-320).
- Estructura común de los diálogos:
  - `Window` con `WindowStartupLocation="CenterOwner"`, `CanResize="False"`, `ShowInTaskbar="False"` y `SizeToContent="Height"`.
  - Contenido en `Border x:Name="Contenido" Classes="ui2"`.
  - Sin bindings, con acceso por `x:Name` (§5.5, convención 24).
  - Servicio con el mecanismo de `RecalcularStockDialogService`: `ShowDialog` con owner `MainWindow` y valor seguro si no hay `App`.
- `AltaProductoRapidaDialog`:
  - Campos: Código, Nombre, Categoría (opcional, igual que hoy) y Unidad de medida.
  - El VM le pasa las categorías y unidades ya cargadas (`CategoriasDisponibles`, `UnidadesMedidaDisponibles`).
  - Devuelve `ProductoNuevoDto?` (null = cancelar).
  - Validación igual que hoy: no confirma si falta código, nombre o unidad.
  - Agregado: una `Alerta` en línea que marca los campos faltantes.
- `ConfirmacionPreciosDialog`:
  - Lista los productos con cambio de precio: tilde, nombre, precio actual y nuevo, en es-UY.
  - Devuelve la lista con los tildes, o null si se cancela.
- En los dos: Escape cancela y Enter confirma (`IsCancel` / `IsDefault`), y el foco queda dentro de la `Window`. Esto cierra un hueco que existe hoy: los overlays no atrapan el foco ni responden al teclado.
- Servicios nuevos: `IAltaProductoRapidaDialogService` e `IConfirmacionPreciosDialogService`.
- Cambios en `IngresoPorFacturaViewModel` (excepción a "VMs intactos", asentada en §3):
  - Se quitan `MostrandoAltaProducto`, `MostrandoConfirmacionPrecios`, los comandos Confirmar/Cancelar de los overlays (`ConfirmarAltaProductoCommand`, `CancelarAltaProductoCommand`, `ConfirmarPreciosYGuardarCommand`, `CancelarConfirmacionPreciosCommand`) y `GuardarEsAccionPrincipal`.
  - `AbrirAltaProducto` y `GuardarAsync` esperan al servicio.
- Mockups: `docs/ui-v2/mockups/dialogos/alta-producto.html` y `docs/ui-v2/mockups/dialogos/confirmacion-precios.html`.
- Convención 25 nueva (`docs/ui-v2/herramientas.md`): todo modal v2 es una `Window` con servicio y quedan prohibidos los overlays en pantalla. La custodia `GuardianDePatronTests`: nada de `#AA000000` ni bindings `Mostrando*` a `IsVisible`.

*Sección 2. Pantalla v2.*
- Layout: `Grid` de filas `*,Auto,Auto` (contenido con scroll, alerta de guardado y PIE FIJO abajo), con el pie fuera del scroll, como pide la spec funcional (`docs/superpowers/specs/2026-07-31-ingreso-por-factura-design.md`: "un pie fijo con suma de renglones, total de factura y diferencia"). Hoy el pie es una card dentro del `ScrollViewer`.
- Encabezado como la 3b: sin breadcrumb (hereda D1: es una pantalla del sidebar), con título "Ingreso por factura" y una frase.
- Tiene TRES secciones con título, así que NO hereda D3 (cumple §5.2):
  1. **"Datos de la factura"**, con los mismos campos de hoy:
     - Fila 1: Proveedor, N.º factura y Orden de compra.
     - Fila 2: Fecha, Detalle y Destino.
     - Fila 3: Fuente de financiamiento, Rubro de gasto y Línea POA.
     - Fila 4: "Compra a crédito", que muestra el vencimiento.
     - Los opcionales se marcan con el patrón de etiqueta v2.
  2. **"Cargar artículo"**, en DOS líneas (D4: en una fila Producto medía 175 px < 240 a 1280):
     - Línea 1: Producto (`ComboBox`, o el nombre del producto nuevo + "Cambiar"), Cantidad y Precio (con `EntradaNumericaBehavior`) y "Agregar artículo" (secundario; decisión del usuario del 2026-10-11, ver los desvíos).
     - Línea 2: "Actualizar precio costo" y "Producto nuevo" (secundario).
     - Enter dentro de la fila de carga dispara "Agregar artículo": en Cantidad, en Precio y en Producto cuando tiene un valor. Enter nunca dispara Guardar ni envía el formulario: la pantalla no tiene botón con `IsDefault` y el Enter de la fila se consume ahí. En Producto vacío, Enter conserva lo nativo del `ComboBox` (abre el desplegable) y no agrega ni muestra el error de carga; con el desplegable abierto, Enter elige el ítem y no agrega. En Cantidad o Precio con Producto vacío, Enter ejecuta "Agregar artículo", que valida igual que el botón ("Debe seleccionar un producto o cargar uno nuevo.").
     - El error de carga va en una `Alerta` en línea, y el foco vuelve al producto tras agregar.
  3. **"Artículos cargados"**:
     - Grilla v2 de solo lectura (convenciones 18 y 19) con `ColumnaAuto` en Cantidad, Precio y Subtotal (a la derecha) y Producto en `*`.
     - Indicador "Actualizar precio costo" y "Quitar" como botón fantasma (`ghost`).
     - Vacío: "Todavía no cargaste artículos".
- Pie fijo:
  - Suma de renglones, Total de factura (el `TextBox` editable existente, `MontoTotalTexto`) y Diferencia.
  - La diferencia es neutra si da 0. Si no, usa el color de advertencia v2, que reemplaza el `OrangeRed` literal de hoy, y no bloquea el guardado.
  - A la derecha, Cancelar (secundario) y Guardar (primario, el ÚNICO primario de la pantalla).
  - El error general va en una `Alerta` arriba del pie.
- Después de guardar:
  - El formulario queda deshabilitado.
  - Aparece la card v2 "Adjuntá la factura", que envuelve `AdjuntosPanelView` v1. Es una EXCEPCIÓN documentada hasta la Fase 4, donde `AdjuntosPanelView` se migra una sola vez para todas sus pantallas.
  - El pie muestra solo "Finalizar".
- Comportamiento SIN CAMBIOS:
  - Navegación: Cancelar y Finalizar van a Gastos y facturas.
  - Validaciones, `PuedeGuardar`, carga por `DataContextChanged` y gate del sidebar.
- Mockups en `docs/ui-v2/mockups/inventario/`: estados vacío, con renglones, con diferencia y guardado, a 1280 y 1366.

*Sección 3. Tests, verificación y deuda.*
- Tests de diálogos en `Presentation.UiTests/V2`:
  - Escape cancela y Enter confirma.
  - El alta no confirma si falta un campo, y la `Alerta` es visible.
  - Precios en es-UY.
  - Un solo primario.
  - Sin recorte (`DialogosV2Tests`).
  - Comparación con mockups vía `ContenidoDeDialogo.Extraer`.
- ViewModel: los tests de los flags se reescriben contra servicios fake:
  - Alta cancelada: la carga queda intacta.
  - Alta confirmada: queda como producto nuevo en carga.
  - Precios cancelados: no guarda.
  - Precios confirmados: aplica los tildes y guarda.
  - Error del servicio tras confirmar.
- Pantalla:
  - Los tests de click real se adaptan.
  - El pie es visible con 50 renglones a 1280×760.
  - La diferencia cambia de estilo si no da 0.
  - Un solo primario en la VISTA entera (Guardar), igual que cualquier formulario v2; "Agregar artículo" y "Producto nuevo" son secundarios.
  - Enter en Cantidad, en Precio y en Producto con valor agrega el renglón; Enter en Producto vacío o con el desplegable abierto no agrega ni muestra error; Enter en la fila de carga nunca ejecuta Guardar.
  - Comparación con mockups a 1280 y 1366.
- Guardianes:
  - Ingreso por factura entra a la lista v2 (`VistasMigradasAV2`).
  - Convención 25.
  - Se actualiza `GuardianDePatronTests:48` (hoy `[InlineData(typeof(IngresoPorFacturaView), "Ingreso de stock por factura", "MOVIMIENTOS")]`), por el título y el eyebrow.
- Verificación por mutación de las reglas clave: `IsCancel`, `IsDefault`, la validación del alta, el pie fuera del scroll y Enter en la fila de carga → Agregar artículo.
- Verificación orgánica en WSLg a 1280:
  - Una factura con un producto existente y uno nuevo, más un cambio de precio.
  - Los diálogos con teclado.
  - Muchos renglones, para ver el pie.
  - Guardar y adjuntar un PDF.
  - Confirmar en Gastos y facturas.
  - Backup de la DB antes y limpieza de los datos de prueba al final.
- Deuda anotada, que NO se resuelve en la 3c-3:
  - Categoría obligatoria según la spec funcional (`productoNuevo.categoriaId` sin `?`), pero opcional en el código (`ProductoNuevoDto.CategoriaId` es `int?`).
  - Anulación del lote sin botón, aunque la spec funcional la incluye en la v1.
  - Selector de producto como `ComboBox` con todo el catálogo; evaluar `AutoCompleteBox` como en Recalcular.
  - `AdjuntosPanelView` v1 hasta la Fase 4.
- Entregables: esta sección, la convención 25 en `docs/ui-v2/herramientas.md` y después el plan `docs/superpowers/plans/2026-10-11-ui-v2-fase-3c3-ingreso-factura.md`.

*Desvíos y aclaraciones respecto de secciones previas (auto-revisión del diseño):*
- **Un solo primario, sin desvío (decisión del usuario del 2026-10-11, reemplaza el desvío de "dos primarios" de la primera versión de este diseño):** "Agregar artículo" es SECUNDARIO y el único primario de la pantalla es Guardar, así que la pantalla cumple §3.3 y §5.2. La acción frecuente (agregar un renglón muchas veces) se resuelve con el teclado: Enter dentro de la fila de carga (Cantidad, Precio, y Producto cuando tiene valor) dispara "Agregar artículo". Enter no dispara Guardar ni envía el formulario por accidente: ningún botón de la pantalla lleva `IsDefault`, el Enter de la fila se marca como manejado, y en Producto vacío conserva lo nativo del `ComboBox` sin agregar. El test de "un solo primario" cuenta sobre la vista entera, como en cualquier formulario v2. Los diálogos también tienen un solo primario.
- **Grilla de campos de 3 por fila y la carga de "Cargar artículo" en dos líneas (desvío de §5.2, "grilla de 2 columnas"):** la cabecera de la factura tiene 10 campos agrupados de a tres por afinidad (identificación, fecha y destino, imputación presupuestal) y la carga de un artículo es una sola acción en dos líneas (D4: Producto, Cantidad, Precio y Agregar arriba; tilde y Producto nuevo debajo). El ancho de la card lo fijan los mockups de la fase.
- **Sin breadcrumb:** hereda D1 de la 3b (desvío ya aprobado de §4.4: es una pantalla del sidebar, no un alta que vuelve a un listado).
- **Los 3 overlays de actualización de la Fase 1 (§4.5, §5.5) no caen bajo la convención 25:** son del shell (`CoordinadorActualizacion`), no un modal de una pantalla, y no usan `#AA000000` ni bindings `Mostrando*`. La convención 25 rige para los modales de pantalla.
- **§5.5 habla de "4 diálogos `Window`"**: con Recalcular stock (3c-2) y los dos de esta fase son 7; todos siguen sin bindings.

**Diseño de la Fase 4a (aprobado por el usuario el 2026-10-11).** `AdjuntosPanelView`, Calendario de pagos y Control POA en v2, más los estados de pantalla de las convenciones 18, 22 y 23. Alcance: migración visual de las tres vistas y las excepciones E1 a E5 de abajo; sin cambios de API ni de datos. Plan: `docs/superpowers/plans/2026-10-11-ui-v2-fase-4a-adjuntos-calendario-poa.md`.

*Subfases de la Fase 4 (aprobado por el usuario el 2026-10-11).* 4a `AdjuntosPanelView` + Calendario de pagos + Control POA; 4b Ingresos de caja + formulario; 4c Libro caja; 4d Gastos y facturas + formulario; 4e Pagos del gasto. La 4e depende de la 4a y de la 4d. Cada una con su ciclo completo (§7).

*Excepciones declaradas a "VMs intactos" (§3).*
- **E1.** `CalendarioPagosViewModel` y `ControlPoaViewModel` ganan `Cargando`, `SinConexion` con Reintentar y estado vacío, usando `ConsultaVigente`, `ReintentarAsync` y `AlPerderConexion` de `ViewModelBase`. Hoy no usan nada de eso: el `catch (UnauthorizedAccessException)` es silencioso y una caída sale por el modal global.
- **E2.** `ControlPoaViewModel`: cambiar Ejercicio recarga solo, con una espera debounce inyectable (como `EsperaDebounce` de Productos). `RecargarCommand` sale del encabezado.
- **E3.** `AdjuntosPanelViewModel.QuitarCommand` pide confirmación con `IConfirmacionService`. Hoy borra sin preguntar (es baja lógica).
- **E4.** Adjuntos muestra el tamaño legible en es-UY ("1,2 MB", "850 KB") con un converter. Hoy es "{0:N0} B".
- **E5 (decisión del usuario, 2026-10-11).** `AdjuntosPanelViewModel.PuedeModificar` se refresca al revocar permisos con el panel abierto, como los demás gates (convención 21). Hoy se calcula una sola vez en `InicializarAsync` y queda viejo. Pasa a ser un getter sobre la sesión (con el permiso que corresponde a gasto o a pago, fijado en `InicializarAsync`), y los tres hosts (`GastoFormViewModel`, `PagosGastoViewModel` e `IngresoPorFacturaViewModel`) reenvían `NotificarPermisosActualizados` al panel. Sin el reenvío, el `OnPropertyChanged(string.Empty)` del host relee `AdjuntosPanel`, que es la misma instancia, y los bindings del panel no se reevalúan. Se custodia con un test en `Presentation.UiTests` con la View real dentro de `ShellMainView`, un 403 real (`ManejadorAccesoRevocado`) y un Operador de permisos MIXTOS (nunca Admin), más la mutación que quita el reenvío.

*Sección 1. Calendario de pagos (opción A).*
- `EncabezadoPagina` "Calendario de pagos" con "Actualizar" SECUNDARIO. Se mantiene: la vista depende de la fecha de hoy y no tiene filtros.
- Cuatro `TarjetaContenido` apiladas dentro de un scroll, con el contador en el título: "Vencidas · N", "A vencer en 7 días · N", "A vencer en 30 días · N" y "Pagos recientes · N".
- Grilla v2 compacta SIN orden por click: la lista ya viene ordenada por vencimiento.
- Facturas (Vencidas, 7 y 30 días): PROVEEDOR · FACTURA · VENCE (dd/MM/yyyy) · SALDO (numérico, `MonedaConverter` / `FormatoEsUy`) · acción "Registrar pago" (secundario, `RegistrarPagoCommand`; la columna se oculta con `IsVisible` sin `PuedeRegistrarPagos`). En Vencidas, VENCE va en tono peligro por token.
- Pagos recientes: PROVEEDOR · FECHA (dd/MM/yyyy; hoy sale sin formato) · MONTO.
- Vacío por sección: "No hay facturas vencidas", "No hay facturas que venzan en los próximos 7 días", "No hay facturas que venzan en los próximos 30 días" y "No hay pagos en los últimos 7 días".
- Antes no se mostraban la fecha de vencimiento ni el número de factura aunque `FacturaCalendarioDto` ya los trae: no hay cambio de API.
- Estados de pantalla (E1): cargando, sin conexión con Reintentar y vacío, según las convenciones 18, 22 y 23.

*Sección 2. Control POA (opción A).*
- Encabezado solo con Exportar PDF (custodiado por `ExportarPdfBotonesGuardianTests` y `ColumnasPdfContraDtosRealesTests`, sin cambios). Decisión 2026-10-13: se elimina la exportación CSV de toda la app (decisión de producto del usuario); queda solo PDF.
- `BarraFiltros` con `CampoFormulario` Ejercicio (`NumericUpDown`, 2020 a 2100) que recarga solo (E2).
- Grilla: LÍNEA · PROGRAMA · PRESUPUESTO · GASTADO · SALDO · % EJECUCIÓN.
- % EJECUCIÓN en es-UY ("47,30 %") más una barra fina de progreso en la celda: tono normal hasta 100 y tono peligro por encima de 100.
- Saldo negativo: `Insignia` "Sobreejecutado" (tono peligro) y el número con el token de negativo. Quedan prohibidos `Opacity`, `SignoNegativoBrushConverter` y `BadgeEstado` (hoy la vista usa `BadgeEstado` y `SignoNegativoBrushConverter`).
- El doble click sigue abriendo Gastos filtrado por la línea (`AbrirGastosDeLaLineaCommand`).
- Estados de pantalla (E1): cargando, sin conexión con Reintentar y vacío.

*Sección 3. Panel de adjuntos (opción A).*
- La raíz lleva `Classes="ui2"`, así que se ve v2 también dentro de los hosts v1 `GastoFormView` y `PagosGastoView` hasta la 4d y la 4e. Es la excepción INVERSA a la de la 3c-3 (allá un panel v1 dentro de una card v2) y es TEMPORAL: se cierra cuando esos hosts migren.
- Encabezado: título de sección "Adjuntos" y "Agregar", que se oculta con `IsVisible` sin `PuedeModificar` (antes quedaba deshabilitado).
- Lista compacta: ícono por tipo (PDF o imagen) · nombre (elipsis y tooltip solo si se corta, convención 20) · tamaño legible (E4) · fecha dd/MM/yyyy · "Ver" (siempre visible) · "Quitar" (oculto sin `PuedeModificar`; pide confirmación, E3).
- Vacío: "Sin adjuntos. PDF, JPG o PNG hasta 10 MB."
- Los errores siguen por `IConfirmacionService.InformarAsync`. La inicialización no cambia: el host llama `InicializarAsync`.

*Sección 4. Tests, verificación y deuda.*
- **Comportamiento SIN CAMBIOS:** datos de `FinanzasVistasService`; gates base (`VerFinanzas`); navegación Registrar pago → `PagosGastoViewModel` con `ConfigurarVolver`; exportes CSV y PDF y el aviso de volumen mayor a 500; validación de adjuntos (magic bytes, 10 MB); Ver por archivo temporal.
- **Mockups** (`docs/ui-v2/mockups/`):
  - `finanzas/calendario-pagos.html`, `-cargando`, `-vacio` y `-sin-conexion`.
  - `finanzas/control-poa.html`, `-cargando`, `-vacio`, `-sin-conexion` y `-sobreejecutado`.
  - `finanzas/adjuntos-vacio.html`.
  - `inventario/ingreso-factura-guardado.html` pasa a dibujar el panel real (se quita `.panel-adjuntos-v1`).
- **Guardianes:** las tres vistas pasan a `VistasMigradasAV2` (salen de `VistasDeLaTanda` y `VistasEmbebidas` en `GuardianDePatronTests`).
- **Comparación visual** por mockup a 1280×760 y 1366×768 con el mismo umbral. Se quita la exclusión `.panel-adjuntos-v1` / `DatosIngresoFactura.OcultarPanelAdjuntosV1` de `IngresoFacturaComparacionTests`.
- **Tests de ViewModel con fakes:** recarga por cambio de ejercicio con debounce; confirmación al quitar (aceptar y cancelar); la consulta vigente gana (convención 23); sin conexión y reintentar.
- **Tests de gates en `Presentation.UiTests`** con la View real y un usuario de permisos MIXTOS (no Admin: Admin cortocircuita el gate): la columna "Registrar pago" del Calendario y Agregar / Quitar de Adjuntos, los dos también tras un 403 con la pantalla abierta (E5 para Adjuntos).
- `SignoNegativoBadgeTests` se adapta a `Insignia` (convención 13). Se mantienen `AccesoRevocadoPantallasFinanzasTests` y `FinanzasRevocacionPermisosTests`.
- **Verificación por mutación, quitando (nunca agregando) las reglas clave:** el gate de la acción, la confirmación al quitar, el tono peligro de la barra por encima de 100 y el reenvío de `NotificarPermisosActualizados` al panel de adjuntos (E5); cada una tiene que dar rojo.
- **Verificación orgánica** a 1280 con backup previo de la DB local (contenedor `stockapp-pg`).
- **Deuda anotada, que NO se resuelve en la 4a:**
  - Revisado en el código: `CalendarioPagosViewModel.PuedeRegistrarPagos` es un getter sobre la sesión y un 403 lo refresca en caliente (`NotificarPermisosActualizados`), así que esa parte no es deuda. El refresco de `AdjuntosPanelViewModel.PuedeModificar` dejó de ser deuda: entra en la 4a como E5 (decisión del usuario, 2026-10-11).
  - Los hosts `GastoFormView` y `PagosGastoView` siguen v1 hasta la 4d y la 4e.

**Deuda abierta tras la Fase 4b:**
- GATE 2 aprobado por Martin el 2026-10-12 con la verificación orgánica PARCIAL y sin reporte (interrumpida a pedido del usuario durante la revocación en caliente). Constan: backup `pre-4b-20261012-221044.sql` (22:10), ingresos `PRUEBA-4B` 3, 4 y 5 creados (el 5 dado de baja desde la app) y el operador `f4bmixto` (id 28). Lo no recorrido o no documentado (incluida la repetición a 1366×768) queda cubierto solo por UiTests con la View real y la comparación visual.
- Fixes del tema fuera del plan: `6e90a3f` (Task 6, antes del GATE 1: `CalendarDatePicker` v2 con el error debajo de la caja) y `52b9600` (Task 8: mensaje de error de campo a la izquierda).
- `UmbralAprobado` 13 para `IngresosSinResultados` (1280 y 1366), aprobado por Martin el 2026-10-12.
- Al anular un gasto su adjunto queda activo (hallado el 2026-10-12 al limpiar los datos de la 4a). Va a la 4d, junto con el ancho del formulario v1 de Gasto.
- El doble Aviso en un 403 de adjuntos sigue preexistente (ver la deuda de la 4a).
- La guarda del popup del calendario (Task 8) no tiene test ni verificación manual: en headless el foco no entra al popup, así que no se puede ejercitar; el comentario se apoya en el decompilado. Meterla en la próxima verificación orgánica (1 minuto).
- El formulario de ingreso sin estado "sin conexión" en la carga de fuentes (sigue el modal global; convención 22 no aplicada).
- Minors diferidos del ledger de la 4b sin cerrar. Task 1: el test del 403 no afirma `Items` intacto ni `SinConexion` false; sin test de `Cargando=false` tras un fallo; el converter solo cubre `DateTimeKind.Utc`; rojo de compilación no pegado; sin test de una carga vieja fallida que no pise la `SinConexion` de la vigente. Task 2: `RecalcularPie` O(n²) en cada Add de la carga; `IgnoreNonSpace` iguala n/ñ (documentado en la convención 30); sin test de Todos→Activos con inactivo seleccionado. Task 4: `HasErrors` sin `PropertyChanged`; `using System.ComponentModel` sin uso en el test; mutación (n) deja `_tocados.Add` sin `Invoke` en vez de quitar la línea. Task 5: las 11 fábricas de estado de `DatosIngresos` sin aserción propia; `Formulario_AltaConFechaFija` usa `GetAwaiter().GetResult()` en un Fact síncrono y aserta una fecha que el propio `Formulario()` fija. Task 7: `HorizontalAlignment=Left` en CONCEPTO excluye sus celdas del barrido de espacio derecho sin comentario; `BuscarTextoPorContenido` devuelve Insignia; `BuscarBotonPorTexto` con parámetro `texto` redundante; `Fecha_DeMedianocheUtc` solo custodia si la TZ del runner no es UTC. Task 8: el comentario de la guarda del calendario no aclara que se apoya en el decompilado; el test del tema mide X relativa al TextBlock y no al borde de la caja; `SalirDeLaFecha` observa el evento y no un error visible. Otros: se perdió el comentario histórico del bugfix 2026-08-15 en el catch de `IngresoFormViewModel`. Cerrados: usings desordenados (fix wave de la review final) y `CalendarDatePicker_ConError` con `SetError` (lo cerró `FechaBorrada_MensajeEnElSelectorDeFecha` en la Task 8); los 2 px de la card no suman teselas.
- Rulings ⚠️ vigentes: Task 1, la semántica por turno de `EjecutarCargaProtegidaAsync` se aceptó sin test nuevo (código preexistente de `ViewModelBase`; si está mal, una carga vieja fallida podría mostrar Sin conexión sobre datos vigentes). Task 2, `IgnoreNonSpace` depende de que el desktop no use `InvariantGlobalization` (solo `StockApp.Api` y `Licencias.Cli` lo usan); si cambia, la búsqueda deja de ignorar acentos.
- Ruling vigente: el único 403 posible en `InicializarAsync` del formulario viene de `VerFinanzas` (`FuentesFinanciamientoEndpoints.cs:46`); si el endpoint cambia de permiso, el texto de la Alerta queda inexacto.
- Datos de prueba: ninguno activo. Ingresos 3, 4 y 5 (`PRUEBA-4B`) dados de baja lógica (LogsAuditoria los referencia) y `f4bmixto` (id 28) dado de baja; sin borrado físico (pendiente del OK del usuario). Un ingreso previo (id 2, "sdasd", 2026-08-15) no es de la 4b y no se tocó.

**Deuda abierta tras la Fase 4a:**
- Los hosts `GastoFormView` y `PagosGastoView` siguen v1 hasta la 4d y la 4e, con el panel v2 adentro (excepción inversa). Diferido a la 4d por el usuario (GATE 2): la columna del formulario v1 de Gasto cambia de ancho cuando aparecen o se ocultan filas del panel de adjuntos (el host no tiene ancho fijo).
- Aceptado por el usuario (GATE 2): con datos reales la columna FACTURA del Calendario queda hasta 2 px corrida entre grillas cuando un número de factura es más ancho que el piso de 92 (cada grilla mide su `ColumnaAuto`).
- Preexistente: un 403 en una acción del panel de adjuntos muestra dos Avisos (el del panel, que informa `ex.Message` desde su `catch (Exception)`, y el global de permisos cambiados).
- El botón de la celda "Registrar pago" sin permiso no tiene custodia automática propia (mutación (q) verde porque la columna entera ya se oculta) y tampoco se puede probar a mano.
- Con el Calendario abierto no hay camino real para un 403 de pagos (todo lo que pide exige solo `finanzas.ver`); el refresco en caliente se verificó por Pagos → 403 → Volver y lo custodia el UiTest del camino del shell.
- Aviso global con la API caída: no apareció en ninguna de las dos pantallas (el estado en línea lo reemplaza). En la sesión de verificación WSLg dejó de dibujar las ventanas nuevas (Aviso, diálogos de precio, selector GTK) aunque existían y respondían por X11; no se pudo confirmar si en una PC real el Aviso queda detrás de la ventana.
- Toolkit GUI: la rueda del mouse no se puede simular (xdotool 4/5 llega como click en WSLg); `pkill -f` con un patrón que aparece en la propia línea de comando mata al agente (matar por PID).
- Minors diferidos del ledger de la 4a sin cerrar: override de `NotificarPermisosActualizados` copiado igual en los tres hosts; el test UI del 403 del panel solo en el host de Gasto (Pagos e Ingreso por factura con tests de VM; verificados a mano en el GATE 2); `TamanoLegibleConverter` con negativos; `FechaCortaConverterTests` sin cultura `en-US`; el debounce de Control POA (excepciones no 403/no servidor tragadas, test con 50 ms reales, Reintentar con debounce pendiente consulta dos veces); el code-behind del Calendario resuelve la columna por `Columns[^1]`; sin indicador de carga durante Actualizar con datos; doble click en el encabezado de Control POA navega si hay fila seleccionada (preexistente); CSS muerto en mockups de estados; `Margin="0"` en Quitar sin efecto. Del review final de la 4a: el debounce de Control POA puede mostrar una carga vieja con el Ejercicio nuevo; `FechaCortaConverter` devuelve "—" en silencio ante cualquier tipo que no sea `DateOnly`; `TraerALaVista` no tiene guarda para un control desconectado (de la 3c-3) y ahora corre también con cada cambio de Bounds, así que un redimensionado de la ventana con la card visible vuelve a scrollear; la mutación (t) de la T9 reemplazó en vez de quitar; `ComposicionDIApiTests` no cubre `ControlPoaViewModel`; `AdjuntosDocumentoPanelView` y `MantenimientoView` siguen con "{0:N0} B" en vez de `TamanoLegibleConverter`; hay tres implementaciones de UTC a local (`FechaUtcALocalConverter`, `FechaHoraLocalConverter`, `FechaUtcACortaConverter`) y conviene un helper común en `FormatoEsUy`; el botón de la celda "Registrar pago" sin custodia propia; `LineHeight` 16 en `EstadoVacio.PART_Mensaje` alcanza a las 32 pantallas con `EstadoVacio` (también vistas v1 sin mockup; un mensaje de dos líneas sube unos 0,27 px por línea); CSS muerto en los mockups de estados y `#7A7A7A` literal en `.pulgar`; `RegistrarPagoAsync` del Calendario deja escapar errores de `ObtenerPorIdAsync` (preexistente); las tres grillas de facturas del Calendario están duplicadas en XAML (ruling preflight que lo acepta); los exportes de Control POA siguen habilitados durante el debounce (rotulan bien con el ejercicio de las filas cargadas; se deshabilitan con `SinPermiso` o `Cargando`, cerrado tras el review final). Sin verificar a mano: la rueda del mouse en WSLg (xdotool 4/5 llega como click), cubierta por un UiTest headless.
- Datos de prueba: ninguno (borrados el 2026-10-12; `f4amixto`, id 27, quedó dado de baja porque LogsAuditoria lo referencia).

**Diseño de la Fase 4b (aprobado por el usuario el 2026-10-12).** Ingresos de caja (`IngresosView`, el listado) e `IngresoFormView` (alta y edición) en v2. Alcance: migración visual de las dos vistas conservando el comportamiento (Nuevo, Editar, Dar de baja; gate `PuedeRegistrarIngresos` que oculta con `IsVisible` y deja "Solo lectura" sin el permiso; acceso con `finanzas.ver`) y las excepciones E1 a E5 de abajo; sin cambios de API ni de datos, y las reglas del servidor (`IngresoCajaService`) no cambian. El plan se escribe a continuación de este diseño.

*Excepciones declaradas a "VMs intactos" (§3).*
- **E1.** `IngresosViewModel.CargarAsync` pasa a `EjecutarCargaProtegidaAsync` + `ConsultaVigente` de `ViewModelBase`, con `Cargando`, `SinConexion` (con Reintentar) y `SinPermiso`. Hoy el 403 se traga en silencio y la pantalla queda vacía sin explicación.
- **E2.** Filtro en el cliente sobre el `DataGridCollectionView`: `TextoBusqueda` (busca en Concepto) y `MostrarInactivos` (combo Activos / Todos, arranca en Activos). Sin tocar la API.
- **E3.** `CantidadVisible`, `CantidadDadosDeBaja` y `TotalActivos` para el pie. El total suma SOLO los ingresos activos visibles: un ingreso dado de baja no es plata que entró.
- **E4.** `IngresoFormViewModel`: un 403 en `InicializarAsync` (permiso revocado) deja de tragarse. Muestra la Alerta "Ya no tenés permiso para registrar ingresos" y deja Guardar deshabilitado. Hoy el `catch (UnauthorizedAccessException)` queda vacío y el formulario se ve usable. *Desvío aprobado en el GATE 1 (D13=A, 2026-10-12): el texto de la Alerta es "Ya no tenés permiso para ver finanzas, así que no se puede registrar el ingreso.", porque el 403 viene de `VerFinanzas`.*
- **E5.** `IngresoFormViewModel` gana la validación por campo que pide `CampoFormulario` (el mensaje lo dibuja el `DataValidationErrors` del propio control): un error y un estado "tocado" por Fecha, Monto, Concepto y Fuente, de modo que el mensaje aparezca tras tocar el campo o al intentar guardar y no con el formulario recién abierto. Hoy el VM tiene un único `MensajeError` y no expone errores por campo. Las reglas del cliente son las que ya existen (concepto obligatorio, monto obligatorio y numérico es-UY, fecha obligatoria, fuente obligatoria); "el monto debe ser mayor a cero" sigue siendo del servidor y sale por la Alerta de error.

*Sección 1. Listado de ingresos.*
- `EncabezadoPagina` "Ingresos de caja" con un subtítulo corto. Acciones en el encabezado, sobre la fila seleccionada (patrón de Productos v2): "Nuevo ingreso" (primario), "Editar" y "Dar de baja" (secundarios). El doble click en una fila equivale a Editar.
- Sobre una fila dada de baja, Editar y Dar de baja quedan deshabilitados. No hay reactivar (no existe en la v1).
- Sin permiso de registrar ingresos: los tres botones se ocultan con `IsVisible` y aparece la `Insignia` "Solo lectura".
- `TarjetaContenido` con `BarraFiltros`: buscador "Buscar por concepto…" y combo Activos / Todos (E2).
- Grilla (desvío aprobado en el GATE 1, D4/R4: CONCEPTO elástica y FUENTE con `ColumnaAuto`): FECHA · CONCEPTO (`ColumnaAuto`, `TooltipSiSeCorta`) · FUENTE · MONTO (numérico, es-UY). Desaparece la columna con el checkbox Activo: la fila inactiva usa el token de inactivo más una `Insignia` "Inactivo" junto al concepto (convención 11; `Opacity` prohibido). Orden por click, fecha descendente por defecto.
- Pie: "N ingresos · M dados de baja · Total $ X" (E3).
- Estados de pantalla (E1): Cargando; Sin conexión (Alerta en línea con Reintentar, convención 22; desvío aprobado en el GATE 1, R5: `EstadoVacio` con Reintentar dentro de la card según la convención 18, no la Alerta de la 22); Sin permiso (`finanzas.ver`); Vacío en dos variantes: sin datos ("Todavía no hay ingresos" y, si hay permiso, el botón "Nuevo ingreso") y sin resultados del filtro ("Ningún ingreso coincide con la búsqueda").
- Dar de baja confirma con el modal v2 (patrón de la 3c-3) mostrando concepto y monto.

*Sección 2. Formulario de ingreso (patrón `ProductoFormView`).*
- Breadcrumb "Ingresos de caja › Nuevo ingreso" o "Ingresos de caja › Editar ingreso"; el click en el primer tramo vuelve al listado y equivale a Cancelar.
- Card de ancho máximo 760 (`V2AnchoFormularioMax`) con un único bloque "Datos del ingreso", en grilla `*,16,*`: fila 1 FECHA (`CalendarDatePicker` v2) | MONTO (`TextBox` es-UY alineado a la derecha); fila 2 CONCEPTO a todo el ancho; fila 3 FUENTE DE FINANCIAMIENTO en la columna izquierda.
- Pie con separador: Cancelar (secundario) y Guardar (primario, `ComandoDeGuardado`).
- Validación: los errores de campo van debajo de cada campo vía `CampoFormulario` y se ven tras tocar el campo o al intentar guardar, no en rojo al abrir (E5); los errores del servidor salen en una Alerta de error arriba del pie; Guardar queda deshabilitado mientras falte algo obligatorio.
- Se conserva: la navegación al listado al guardar o cancelar y la carga de las fuentes activas.
- Se conserva, revisado en el código (2026-10-12): al editar un ingreso cuya fuente fue dada de baja, `InicializarAsync` carga las fuentes con `ListarActivasAsync` pero selecciona la fuente actual (`FirstOrDefault(...) ?? _ingresoParaEditar.FuenteFinanciamiento`) y la agrega al combo si no está. La fuente actual se ve y se puede guardar sin cambiarla; el servidor solo rechaza una fuente de baja si la fuente cambió (`fuenteCambio` en `IngresoCajaService.ValidarAsync`). Elegir otra fuente de baja no es posible desde el combo, que ofrece solo las activas más la actual.
- Fuera de alcance: "Guardar y cargar otro" y adjuntos.

*Sección 3. Tests, verificación y deuda.*
- **Comportamiento SIN CAMBIOS:** reglas de `IngresoCajaService` (permisos, validaciones, auditoría, no modificar un ingreso de baja); gate base `VerFinanzas`; navegación de alta, edición y cancelación; carga de fuentes activas.
- **Mockups** (`docs/ui-v2/mockups/finanzas/`): `ingresos`, `ingresos-cargando`, `ingresos-vacio`, `ingresos-sin-resultados`, `ingresos-sin-conexion`, `ingresos-solo-lectura`, `ingresos-con-inactivos`, `ingreso-nuevo`, `ingreso-editar`, `ingreso-error-campo` e `ingreso-error-servidor`. GATE 1 con las decisiones Dn en el plan.
- **Comparación visual** dentro de `ShellMainView` a 1280×760 y 1366×768, con tope de 11 teselas; un tope mayor solo con `UmbralAprobado` aprobado por el usuario.
- **Tests de ViewModel con fakes** para E1 a E5: estados Cargando, Sin conexión, Sin permiso y la consulta vigente; filtro y total sin inactivos; 403 en `InicializarAsync` del formulario; errores por campo tras tocar o al guardar.
- **Tests en `Presentation.UiTests` con la View real** (`IngresosViewV2Tests`, `IngresoFormViewV2Tests`). Los gates se prueban con un usuario de permisos MIXTOS (`finanzas.ver` sí, `RegistrarIngresos` no), nunca Admin. Se mantienen `FinanzasRevocacionPermisosTests` y se adapta `IngresosViewTests`.
- **Guardianes:** `IngresosView` e `IngresoFormView` pasan de `VistasDeLaTanda` a `VistasMigradasAV2` en `GuardianDePatronTests`.
- **Verificación por mutación, quitando (nunca agregando) las reglas clave:** el `IsVisible` del gate, el filtro de inactivos en el total y el estado `SinPermiso`; cada una tiene que dar rojo.
- **Verificación orgánica (GATE 2)** con backup previo de la DB local (contenedor `stockapp-pg`): alta, edición, baja, filtros, solo lectura con un usuario mixto y revocación en caliente. Los datos `PRUEBA-4B` se limpian al cierre.
- **Deuda anotada, que NO se resuelve en la 4b:**
  - Al anular un gasto su adjunto queda activo (hallado el 2026-10-12 al limpiar los datos de la 4a). Va a la 4d, junto con el ancho del formulario v1 de Gasto.
  - El doble Aviso en un 403 de adjuntos sigue preexistente (ver la deuda de la 4a).

**Deuda abierta tras la Fase 3a:**
- Sidebar del shell v2 corta el último ítem a 1366x768 (detectado en mockups de Fase 3a, 2026-10-07); el usuario lo difirió: no se arregla en 3a.
- ~~Mientras corre la carga inicial (y tras un 403 silenciado en `CargarAsync`) el listado de Productos muestra el estado vacío "No hay productos para mostrar… creá uno con Nuevo", porque `EstadoSinProductos` depende solo de `Items.Count == 0`; afirma algo falso durante la carga. Arreglarlo pide un flag de carga en el ViewModel, que choca con "VMs intactos" (§3). Se decide en la 3c (Historial de movimientos es el próximo listado): la 3b no tiene listados ni estado vacío (decisión del 2026-10-07).~~ Cerrada (Fase 3c-1): `ViewModelBase.Cargando` + `v2:EstadoCargando`, y "Sin permiso" tras un 403; el vacío solo aparece con la carga terminada bien y sin filas.
- ~~`GrillasSinRecorteBarridoTests` mide con el sidebar viejo de 232 (`SidebarPx`); el v2 mide 240 (8 px menos para la vista).~~ Cerrada (Fase 3c-1): `SidebarPx` pasó a 240 y los barridos siguen verdes.
- `SignoNegativoBrushConverter` y `ActivoOpacidadConverter` siguen en las vistas del tema viejo (se borran en la Fase 9 cuando no quede ninguna).
- El buscador de Productos conserva el texto tras "Eliminar" aunque el listado se recarga completo (preexistente, del ViewModel).
- "Registrar entrada/salida" del sidebar exige también `catalogo.productos` además de `movimientos.registrar`: un usuario con solo `movimientos.registrar` no ve la entrada (preexistente).
- ~~`UseLayoutRounding="False"` va por instancia en `EstadoVacio` (no en su `ControlTheme`, que movería el catálogo de 8 a 20 teselas): evaluar una clase opt-in si 3b o 3c lo repiten. La 3b no usa `EstadoVacio`; se evalúa en la 3c.~~ Evaluada (Fase 3c-1): el control nuevo `EstadoCargando` lo lleva en su `ControlTheme`; `EstadoVacio` sigue por instancia para no mover el catálogo de componentes.
- WSLg pierde el foco de teclado tras un diálogo modal de la app y `escribir.sh`/`click.sh` igual salen con exit 0 (el `-` llegó a salir como `/`): el toolkit no es fail-closed en este caso; reiniciar el desktop lo arregla.
- ~~Las columnas `Auto` de contenido libre (CÓDIGO, COSTO, UNIDAD) siguen creciendo al scrollear hasta una fila con un valor más ancho (medido: 65→140, 100→115, 63→97); no se fijan porque truncarían datos reales. Solo se fijó ESTADO, cuyo contenido es cerrado. Decisión del usuario (2026-10-07): se resuelve con UN criterio común para todas las grillas al migrar la 3c (la 3b no tiene grillas); no se parchea solo Productos.~~ Cerrada (Fase 3c-1): `ColumnaAuto` mide sobre todas las filas, con el test de más de 1000 filas que exige que ninguna columna se mueva al scrollear.
- `tokens --verificar` y `UiV2.Herramientas.Tests` no estaban en la lista de pruebas de las reviews por task: un token editado a mano en `Tokens.axaml` solo se detectó en la suite completa de cierre.
- Minors diferidos del ledger de la Fase 3a sin cerrar (comentarios y helpers de test duplicados, tests que solo custodian por UiTest el estilo `.inactivo`, `DatosProductosTests` solo con la tabla de productos).

**Deuda abierta tras la Fase 3c-3:**
- CERRADO en `df2ed3b`: **Fecha deshabilitada en gris de Fluent** (verificación orgánica, capturas 47 y 58 del ledger): tras guardar, el `CalendarDatePicker` de Fecha no toma el estilo v2 de deshabilitado. `ControlesV2.axaml` define `.ui2 CalendarDatePicker` (L307) sin `:disabled`; H2 cubrió solo ComboBox y CheckBox. La comparación `-guardado` no lo ve porque está scrolleada a la card de adjuntos. No arreglado en la fase.
- **D15 (H1, funcional, preexistente):** el tilde "Actualizar precio costo" de la fila de carga se pisa en la confirmación de precios, que arranca destildada.
- **Categoría opcional en el código y obligatoria en la spec funcional** del alta rápida (`AltaProductoRapidaDialog` deja "Sin categoría").
- **Anulación del lote sin botón** en la pantalla.
- **Selector de producto:** sigue siendo un `ComboBox` con todo el catálogo (sin búsqueda; `AutoCompleteBox` fuera de alcance). Observado además: la rueda del mouse sobre el combo cambia el producto elegido aunque el foco esté en otro campo (scrollear la página con el puntero encima dejó otro producto en la fila de carga; Avalonia, preexistente).
- CERRADO en la Fase 4a (`AdjuntosPanelView` v2): **`AdjuntosPanelView` v1** dentro de la card v2 (otra tipografía y botones verdes v1) hasta la Fase 4.
- CERRADO en `ea4f016`: Enter con el foco en el combo de Unidad del alta (último campo del orden natural) abre el desplegable y nunca confirma: el `ComboBox` marca el Enter antes que `IsDefault`; `Enter_EnUnCampoConTodoCompleto_Confirma` solo cubre un `TextBox`.
- El adorno de foco del tilde en la confirmación de precios es el rectángulo negro de Fluent y pisa el final del nombre del producto.
- Al hacer click en Cantidad o Precio el "0" no se selecciona (tipear deja "03"); con Tab sí (preexistente).
- Sin servidor, Guardar muestra la alerta en línea y NO sale el Aviso global (verificado: ninguna otra ventana mapeada); la carga de los combos en `InicializarAsync` sigue sin estado "sin conexión" (convención 22 no aplica, fuera de alcance).
- Los 3 overlays de actualización del shell quedan fuera de la convención 25, y el guardián barre solo `Views/` (un overlay nuevo en `Actualizaciones/Views` o `Controls/` no lo detectaría).
- Minors diferidos del ledger de la 3c-3 sin cerrar: `ConfirmacionPreciosDialog.IsDefault` custodiado solo por propiedad (falta el test de efecto); topes de `UmbralAprobado` "medido+1" en ConfirmacionPrecios (CERRADO en `0f946f1`: 12 y 15, tope exacto); `IngresoFacturaVacio` 1280 justo en el tope de 11 teselas; el detector de la convención 25 (CERRADO en `4742b0a` para `{Binding Path=Mostrando…}`, `CompiledBinding`/`ReflectionBinding` y cualquier alfa de negro; sigue sin detectar `Parent.`/`$parent.`) lee texto crudo (un comentario XML lo hace fallar, lado seguro); `AltaProductoRapidaDialog` devuelve Código y Nombre sin `Trim()`; `Tab_NoSacaElFocoDelDialogo` es débil; `GuardarAsync` indexa `tildes[i]` sin chequear el largo; `Verify(Times.Once)` tautológicos en `Guardar_MientrasLaConfirmacionEstaAbierta_…`; CheckBox marcado y deshabilitado solo afirma `Background` (CERRADO en `df2ed3b`); `TraerALaVista` sin guarda si el control se desconectó; `DistintoDeCeroConverter` sin test de null; filtro de Quitar por `DataContext` en `IngresoPorFacturaViewV2Tests` (~L1187); sin test del foco al cancelar el alta o volver de la confirmación; mutaciones de la fila de carga en dos líneas no corridas; `DatosIngresoFactura.ConDiferencia` con el mensaje de error literal; `EnterEjecutaComando` no mira modificadores y `OtraTecla_NoEjecuta` no afirma `Handled`.
- Toolkit GUI: `xdotool key --window <diálogo>` que cierra el diálogo pierde el key-up y deja los clicks de la ventana principal sin efecto hasta mandar `xdotool keyup --window <principal> <tecla>`; tras el selector de archivos GTK, `click.sh` puede apuntar a una ventana X11 oculta del mismo proceso y perder el click con exit 0.
- Datos de prueba: ninguno (borrados en la Task 10; backup previo en `~/stockapp-backups-local/pre-3c3-20261011-041213.sql`).

**Deuda abierta tras la Fase 3c-2:**
- ~~`Cargando` contaba también las consultas viejas descartadas por turno: con la carga inicial colgada y una búsqueda que fallaba rápido (o devolvía 0 filas), Historial y Productos mostraban "Cargando…" junto a "No se pudo conectar" (o en vez de "No hay …").~~ Cerrada (00fba5f): las cargas con turno (`ConsultaVigente`) cuentan en `Cargando` solo mientras su turno sea el último; las cargas sin turno (Mantenimiento, `EmpezarCarga`) siguen en el contador. 4 tests en `Presentation.UiTests` (2 por pantalla) con mutación; `BusquedaYCargaSuperpuestas_*` pasó a afirmar el contrato nuevo (convención 23).
- **(U2) Historial carga TODO el historial sin límite**: `CargarAsync` pide `new HistorialMovimientoFiltro()` y `ColumnaAuto` mide toda la fuente en el hilo de UI en cada carga; probado hasta 5000 filas (`ColumnaAutoTests.CincoMilFilas_MedirNoAgregaCostoPerceptible`). Umbral orientativo **5000 movimientos** (a ~15-20 movimientos por día hábil, unos 250 días hábiles por año: entre uno y un año y medio de uso); por encima, evaluar un rango por defecto (p. ej. últimos 90 días) o paginado en la API. Medido con 1556 movimientos: API 12 ms, UI dentro de ~7 s de latencia del toolkit. Decisión del usuario: no se resuelve en la 3c-2.
- ~~**Gastos: Exportar CSV y Exportar PDF siguen visibles tras revocar los permisos de finanzas, y la grilla conserva la fila vieja** (observado en la verificación orgánica, convención 21). Verificado en el código: la API NO tiene endpoint de exportación (`src/StockApp.Api` no contiene ninguna ruta ni código de export); `ExportarCsvCommand` y `ExportarPdfCommand` de `GastosViewModel` serializan en el cliente las `Filas` ya cargadas, sin llamar al servidor ni verificar permiso, y los dos botones de `GastosView.axaml` (líneas 38 y 44) no están gateados por ningún `Puede*`. No es una fuga de datos nueva (solo exporta lo que la pantalla ya tenía), pero el 403 no limpia `Filas` ni oculta los botones. No se arregla en la 3c-2.~~ Cerrada (137b7bf): Exportar CSV y PDF se ocultan (`IsVisible` sobre `PuedeVerFinanzas`) y su `CanExecute` exige el permiso; `NotificarPermisosActualizados` re-evalúa el `CanExecute` y vacía `Filas` si ya no hay `finanzas.ver`, y un 403 en `FiltrarAsync`/`CargarAsync` también vacía la grilla. Tests de VM y de la View real con usuario de permisos mixtos (revocación en caliente), con mutación. Actualización 2026-10-13: la exportación CSV se eliminó de toda la app (decisión de producto del usuario); hoy solo queda Exportar PDF, con este mismo gate.
- ~~D14, preexistente: en el diálogo de Recalcular, quien tiene `stock.recalcular` sin `catalogo.productos` recibe un Aviso por cada tecla tipeada en el buscador (el 403 de `GET /productos`); el Aviso sale encima del diálogo, la lista queda vacía y nada se rompe.~~ Cerrada (926a5ba): el primer 403 del buscador marca `BuscadorRecalcularSinPermiso`; el diálogo deshabilita el buscador y muestra en línea (`v2:Alerta` Advertencia) "No tenés permiso para buscar productos."; las búsquedas siguientes no llaman a la API. Queda un solo Aviso global (el del primer 403, que sale del transporte y se conserva); cada apertura del diálogo reintenta. Tests de VM y del diálogo real con mutación.
- ~~Reintentar (`SinConexion`) no lee el filtro igual en las dos pantallas: Historial repite el filtro capturado y Productos relee `FiltroBusqueda`; elegir uno y documentarlo en la convención 18.~~ Cerrada (b78187f): decisión del usuario, Reintentar relee el filtro ACTUAL de la pantalla; Historial sobrescribe `ReintentarAsync` y busca con los filtros vigentes (Productos ya lo hacía). Documentado en la convención 18.
- ~~Reintentar de Historial no consultaba `HayErroresDeEntrada`: con un campo inválido buscaba con el valor viejo del VM.~~ Cerrada (6bbdf6e): con errores de entrada Reintentar no busca (mismo criterio que `BuscarCommand` y la recarga tras Recalcular, 42ee8ce) y la card de `SinConexion` queda. Test rojo sin el fix (`Reintentar_ConErroresDeEntradaEnElFiltro_NoBuscaConElValorViejo`) y mutación quitando la guarda.
- ~~Durante el reintento la grilla vuelve a mostrar las filas de la consulta anterior (~15 s de timeout) porque `Items` no se vacía al fallar.~~ Cerrada (5a0fef0): gancho `AlPerderConexion` en `ViewModelBase`, llamado solo con la consulta vigente (una vieja que falla no vacía la grilla de la nueva, convención 23); Historial y Productos lo sobrescriben con `Items.Clear()`. Tests de VM, de base y de la View real durante el reintento, con mutación.
- ~~`_cargaParaReintentar` no se limpia tras un éxito (retiene el closure).~~ Cerrada (62de2bb): se suelta cuando termina bien la carga vigente (no una vieja). Test de VM sobre `ReintentarCargaCommand`, con mutación.
- ~~Doble `RunJobs` en `MovimientoHistorialViewV2Tests` (bug 2, líneas ~1009-1010), sensible al scheduler.~~ Cerrada (739918d): `BombearHastaAsentar` bombea el dispatcher hasta que las filas se re-bindearon a la lista nueva y el texto y ancho de las celdas no cambian entre dos pasadas (con tope de 200). Mutación: sin `TextoDeCeldaReciclada.Activo` en `DataGridV2.axaml` el test da rojo (`'2,2' en 9 px`); 10 corridas seguidas en verde con el fix.
- Las celdas de texto de la v1 (`Themes/DataGrid.axaml`, `.num`) y las columnas de texto con `TooltipSiSeCorta` pueden tener el mismo corte de `TextLayout` que se arregló en la celda numérica v2 (`a6ed0e1`); no verificado.
- Los ~20 ViewModels con `catch (UnauthorizedAccessException)` propio siguen sin `ConsultaVigente` ni `Cargando`; cada uno se migra al llegar su listado.
- Minors diferidos del ledger de la 3c-2 sin cerrar: ~~`DosBusquedasEnOrdenInverso` depende del `Task.Delay(300)` real del debounce; nombre de ese test (describe búsqueda vs recarga)~~ Cerrada (9240f01): `ProductoListViewModel.EsperaDebounce` (internal, 300 ms por defecto) y el test de Productos la baja a cero, el orden lo fijan los `TaskCompletionSource`; el de Historial (que no tiene debounce) pasó a `RecargaTrasRecalcularMientrasHayUnaBusquedaPendiente_LaBusquedaVieja_NoPisaLaLista`. Mutación: sin la guarda `EsLaUltima` de cada VM, cada test da rojo; ~~la recarga tras recalcular llama a `BuscarAsync` directo e ignora `HayErroresDeEntrada`~~ Cerrada (42ee8ce): con errores de entrada en el filtro no recarga (igual que `BuscarCommand`) pero informa "Stock recalculado."; dos productos del mismo nombre no se distinguen en el dropdown del diálogo; `EntraJustoConLasMedidasEnSuPiso` custodia sobre todo el piso de COSTO; `GrillasSinRecorteBarridoTests` sigue admitiendo cortes por ceder con scroll; ~~`DatosHistorialTests` no fija los saldos~~ Cerrada (f3aee67): `Movimientos_FijanLosSaldosAnteriorYNuevoDeCadaFila` lista STOCK ANT. y STOCK NUEVO de las doce filas (mutación: cambiar un saldo del fixture da rojo); `GrillaEspacioDerechoVistaRealTests` quedó en ventana 925 (reubicar cuando `HistorialPorProductoView` pase a v2).
- Tests flaky conocidos, no causados por la fase: ~~`CadenaTlsCompletaTests` (redondeo de 1 s en los certificados del propio test; verde aislado)~~ Cerrada (bdcbca2): cada certificado leía `UtcNow` por su cuenta y X509 trunca a segundos; al cruzar un cambio de segundo el `notAfter` pedido del hijo quedaba 1 s después del del emisor y `CertificateRequest.Create` lanzaba `ArgumentException`. Ahora un solo instante truncado a segundos para toda la cadena (reproducido forzando `Thread.Sleep(1100)`, verde con el fix; 40/40 corridas); y el de 5000 filas de `ColumnaAutoTests` (~50 ms con build en paralelo).
- No verificado en la app: el orden de llegada de dos búsquedas superpuestas (solo se probó Tipo "Salida" durante la carga inicial; lo custodian los tests de las Tasks 2 y 7), Productos con U3 y FECHA entera con la ventana en el rango ~1480-1620.
- Observación de datos: filas viejas de Salida "Uso o consumo"/Merma/Ajuste muestran PRECIO "$ …" y las nuevas "—" (dato legado; `d858221` aplica al registrar).
- Toolkit GUI: tras cerrar un diálogo modal la ventana principal queda sin foco de teclado en WSLg (se recupera con `xdotool windowminimize` + `windowmap`); el tooltip del comentario se corta en el borde derecho de la ventana.
- Datos de prueba sin limpiar en la base local: productos `PRUEBA-3C2-0001` a `PRUEBA-3C2-0030` (ids 1034 a 1063), movimientos 62 a 1562 y usuarios `f3c2mixto` (24) y `f3c2sinperm` (25); backup previo en `~/stockapp-backups-local/pre-3c2-20261009-203615.sql`. Se limpian tras el review final de la fase.

**Deuda abierta tras la Fase 3c-1:**
- Los ~20 ViewModels con `catch (UnauthorizedAccessException)` propio no usan el helper `EjecutarCargaProtegidaAsync`, así que no tienen `Cargando`; cada uno se migra al llegar su listado.
- ~~Carrera de resultados de dos búsquedas superpuestas en Productos: la más vieja puede pisar a la más nueva (preexistente; el contador de `Cargando` solo arregla el flag).~~ Cerrada (Fase 3c-2): `ConsultaVigente` + `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)` en Productos y en Historial, con test de orden inverso y mutación (convención 23).
- El formulario de Productos no tiene indicador de carga (fuera de alcance de la 3c-1).
- ~~Por debajo de 886 px internos en Productos las columnas medidas quedan en su título y además hay scroll horizontal (alternativa: devolverles su ancho medido); a consultar con el usuario.~~ Cerrada (Fase 3c-2, D5): las medidas vuelven a su ancho medido cuando igual hay scroll horizontal (convención 19).
- La suscripción del behavior a la colección fuente no se suelta al salir del árbol (sin fuga hoy: los VMs con grilla son transient).
- Minors diferidos del ledger de la 3c-1 sin cerrar (medir solo las filas nuevas al agregar, helpers de test duplicados, condición `SinPermiso` del vacío sin custodia por mutación).
- Sin permiso `catalogo.productos` también desaparecen Registrar entrada y Registrar salida del sidebar, porque `PuedeRegistrarEntradaSalida` exige ambos permisos (por diseño, observado en la verificación orgánica).
- No verificado en la app: Mantenimiento ("Cargando backups...", ahora sobre el `Cargando` de la base), el parpadeo de la grilla durante la búsqueda (una captura no lo distingue) y los movimientos de 1280×760 con Administrador (se usó el operador mixto).
- Datos de prueba sin limpiar en la base local: productos `PRUEBA-3C1-0001` a `PRUEBA-3C1-1000` (ids 34 a 1033; el 500 con un código de 49 caracteres), movimientos 59, 60 y 61 y el usuario 23 `f3c1mixto`.
- Toolkit GUI: `escribir.sh` no manda Tab, Enter ni Escape (escapa las teclas especiales); se usaron clicks.

**Deuda abierta tras la Fase 3b:**
- ~~Tras reiniciar la API, el combo de Producto de Registrar entrada salió vacío y no abría. Sin confirmar si es un bug real (el VM no recarga la lista tras un reinicio del servidor) o un artefacto de la ventana de WSLg congelada; reproducir con una ventana sana.~~ Cerrada (Fase 3c-1, B1 `ca56a8b`): se reproducía si la API no respondía al abrir el formulario; ahora hay alerta en línea con "Reintentar".
- ~~No verificado: un operador sin permisos de finanzas que acepta "asociar factura" llega a `GastoFormViewModel`, que no puede cargar (preexistente; ni `GastoFormViewModel` ni la navegación hacia él cambiaron en la 3b: el único cambio de ViewModel fue el de los textos del diálogo en `EntradaRegistroViewModel`).~~ Cerrada (Fase 3c-1, B2 `36c269f`): la pregunta de factura solo se hace con Admin o `finanzas.gastos`. Decisión del usuario: "Cancelar" del formulario de factura abierto desde una entrada sigue yendo a "Gastos y facturas".
- La verificación orgánica de la 3b no repitió los pasos 1 y 5 a 1280×760 ni hizo los pasos 6 a 9 (error real con la API detenida, asociar factura con "Sí", operador mixto `f3bmixto`, baja del producto de prueba); el usuario aceptó la verificación parcial.
- Toolkit GUI: `escribir.sh` escapa las teclas especiales, así que no se puede seleccionar ni borrar texto de un campo (hace falta una variante de `escribir.sh` que mande teclas crudas, p. ej. `{BACKSPACE}`, sin escaparlas; se probó un prototipo en la sesión pero no se versionó), y WSLg puede dejar la ventana congelada (mismo hwnd, frame viejo) tras relanzar la app; capturas idénticas con procesos nuevos son la señal.
- ~~Los textos nuevos del diálogo de factura ("No, continuar" / "Sí, adjuntar factura", commit ed9e4e4) se cambiaron DESPUÉS de la verificación orgánica: nunca se vieron en la app real, solo en headless (`DialogosV2Tests`). Confirmarlos a ojo en la próxima pasada con la app.~~ Cerrada (Fase 3c-1): los botones "No, continuar" y "Sí, adjuntar factura" se vieron en la app real el 2026-10-09.
- La cantidad arranca en "0" sin marca de agua visible (D8; pedir un `decimal?` al VM) y vaciada muestra "Ingresá un número válido".
- El wrap del comentario (mutación j) quedó custodiado con test de efecto al cierre (`Comentario_Largo_EnvuelveDentroDeLaCajaYNoSeSaleDelAncho`); minors diferidos del ledger de la 3b sin cerrar: `Requeridos_SegunTipoYMotivo` lee la propiedad y no el asterisco pintado, y `Clickear`/`EnVentana` duplicados respecto de `ProductoFormViewV2Tests`.

**Deuda abierta tras la Fase 2:**
- `EsAdmin` de Importación sin guardián propio mientras `ImportacionHabilitada=false` (Fase 8).
- `capturar.sh` no es fail-closed con WSLg caído: devolvió exit 0 capturando ventanas del escritorio del usuario. Fuera de alcance de esta fase.
- Minors diferidos del ledger sin cerrar: `TextoAVencer7Dias` sigue en el VM de Inicio (borrar en Fase 9); en OperadorMixto una sola insignia "Vencida" pálida no se detecta (límite de la métrica); umbrales de Inicio sin margen (un cambio de rasterizador los pone en rojo, intencional); `Mensaje_IgualAlMockup_1366x768` sin margen (11 de 11, tono); pulgar del sidebar a re-medir en las Fases 6 y 7; `ContextMenu`/`MenuFlyout` sin corte de la herencia del centrado (hoy no se usan); lista `DialogosV2` manual en `GuardianDePatronTests`; `CargarAsync` de Inicio sin cancelación (preexistente).

**Deuda menor de la Fase 1 (no bloquea):**
- Sin guardián propio de `EsAdmin` en Importación (hoy lo tapa `ImportacionHabilitada=false`); resolver al reactivarla (Fase 8).
- ~~Las comparaciones de píxeles no custodian el gate `IsVisible=EsVisible` del sidebar ni la ubicación de `ui2`.~~ Cerrada (Fase 2, Task 5): la custodia es de los UiTests, que montan la `ShellMainView` real, y se probó por mutación:
  - (a) Sin el gate del header: rojo en `ShellMainViewV2Tests.OperadorMixto_SeccionesSinItemsVisibles_OcultanSuHeader` y `RevocacionEnCaliente_OcultaElHeaderDeLaSeccionQueSeVacia`. `SidebarOperadorMixto_IgualAlMockup` (y su variante 1366x768) quedan verdes: es un LÍMITE del umbral, no un diseño. Los 2 headers de sección de más agregan píxeles, pero un header (texto chico, gris claro) queda bajo el umbral calibrado (11 teselas densas / 1,68 %). Ningún cambio de umbral o de mockup debe asumir que la comparación custodia headers de sección: el gate del header lo custodian solo esos 2 UiTests.
  - (b) Sin `ui2` en el sidebar: rojo en `Ui2_VaEnElSidebarYNoEnLaRaiz` y en las cuatro comparaciones del shell.
  - (c) Con `ui2` movido a la raíz: rojo en `ContenidoHospedado_NoTieneAncestroUi2` y `Ui2_VaEnElSidebarYNoEnLaRaiz`; las comparaciones del shell quedan verdes (no lo ven, por diseño: mueven la clase, no los píxeles del estado comparado).
- ~~Pulgar de scroll de Fluent (2 px) en el sidebar del Administrador que el mockup no tiene (P4); dentro del umbral.~~ Cerrada (Fase 2, Task 9): el mockup lo replica.
- ~~`Mensaje_IgualAlMockup` (umbral 12 aprobado), `Modal_IgualAlMockup` y `LoginAccesoLimitado_IgualAlMockup_1366x768` pasan sin margen (tono de rasterizador); frágiles ante cambios de render.~~ Cerrada (Fase 2, Task 13), tesela por tesela y con perfil de tinta por fila y columna de cada texto. Geometría: el texto de los botones (hasta 0,45 px en botones de ancho propio, 0,8 px en el Entrar estirado del Login), corregida con la compensación de botones de §6.5. Quedan: Mensaje en 10 de 12 (1280) y 11 de 11 (1366), modal en 7 de 11 y 0 de 11, Login de acceso limitado en 0 de 11 en los dos tamaños (antes 1 y 0: a la fecha de esta revisión ya no estaba sin margen). Todas las teselas densas que quedan son tono, con corrimiento óptimo 0,0 y bordes de tinta idénticos: el párrafo 14/400 del Mensaje (proporción de tinta app/mockup 0,753, la misma 0,755 del cuerpo de Confirmación) y el título 15/600 del modal (0,835, la de título de sección). `Mensaje_IgualAlMockup_1366x768` sigue sin margen (11 de 11) solo por tono: un cambio de versión de Skia o de Chromium que mueva el tono puede ponerla en rojo, y ese rojo se diagnostica con el script de teselas y el perfil de tinta antes de tocar ningún umbral. El `UmbralAprobado` de 12 de `Mensaje_IgualAlMockup` (aprobado el 2026-10-02) se retiró: mide 10 a 1280 y 11 a 1366, ambos dentro del calibrado de 11, y volver al umbral por defecto es más estricto, no relaja nada.
- ~~Margin `0,1,0,0` de la card de Reset es estático: con `AlertaCompletado`/`AlertaError` visibles cambia la paridad del alto (sin mockup de esos estados).~~ Cerrada (Fase 2, Task 12).
- `V2FuenteTituloPaginaAltoLinea` sin test de control real equivalente al de la Alerta; `% 16` sobre double puede dar falso rojo con escalado fraccional.
- Guardián de recorte: cobertura "la ventana" del presentador de filas de DataGrid sin cubrir; TextBox multilínea de altura fija con valor largo se reporta aunque scrollee (usar `recortesPermitidos` si aparece); `.v2-ventana` sin chequeo fail-closed de que mida el lienzo.
- ~~Sin chequeo de `Opacity` literal en vistas migradas a v2.~~ Cerrada: `GuardianDePatronTests.VistaV2_NoTieneOpacidadesLiterales` ya corre sobre `VistasMigradasAV2`, que desde la Fase 3a incluye `ProductoListView` y `ProductoFormView`.
- Cosméticos: comentario de `GrupoNavegacion.cs:11-14` menciona Finanzas/Maestros; comentario de `ControlesV2.axaml` dice `.v2-boton` (real `.v2-boton--icono`); `reset.html` declara `.mt-12` y `.enlace-centrado` sin uso.

**Integración:** merge periódico de `main` hacia `feat/ui-v2`. El merge a `main` se hace **una sola vez, al final**, con `--ff-only` + push (convención del repo). No se integra por fase: dejaría la app mitad vieja y mitad nueva.

## 8. Tests

### 8.1 Se mantienen tal cual (red de seguridad)

- Gates de permisos: `ShellMainViewGatesTests` (localiza por identidad de `Command`, sobrevive al cambio de XAML), `DocumentoListViewGatesTests`, `DocumentoFormViewGatesTests`, `MovimientoHistorialGatesTests` (excepción: en la Fase 3c-2 se adapta su `Montar` al constructor nuevo del ViewModel, se renombran 2 tests y se quita el assert de "Producto a recalcular:", porque ese campo se muda al diálogo de Recalcular stock por la decisión U1 de la 3c-2; no se borra ninguno y los gates de permisos del Historial siguen custodiados, convención 13), `FinanzasRevocacionPermisosTests`.
- Validación: `MovimientoFormControlValidacionTests` y el seam `ErrorConverter`.
- `CargaProtegidaSinPermisoGuardianTests`.
- `ReflexionVistaViewModelTests`: las rutas de los `.axaml` se conservan (no se renombran ni se mueven vistas).
- `ViewLocatorTests`.
- Guardianes de exportación PDF (la exportación CSV se eliminó de toda la app el 2026-10-13, decisión de producto del usuario).

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
