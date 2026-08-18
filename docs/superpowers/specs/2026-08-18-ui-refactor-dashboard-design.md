# StockApp — Refactor visual a dirección "Dashboard de datos"

**Fecha:** 2026-08-18
**Estado:** aprobado por el usuario, pendiente de plan de implementación
**Alcance:** las 58 vistas de `src/StockApp.Presentation/`

## 1. Problema

El usuario reporta que la interfaz "se ve como la mierda". Al acotarlo, señaló tres síntomas: **se siente apretada y desordenada**, **se ve vieja y genérica**, e **incoherente entre pantallas**. Explícitamente NO señaló problemas de UX ni de flujo: la app funciona, lo que falla es la capa visual.

### Diagnóstico con evidencia

Existe un UI Kit desde el 2026-07-04 (`src/StockApp.Presentation/Themes/`, plan `docs/superpowers/plans/2026-07-04-ui-uplift.md`), pero está **incompleto y a medio adoptar**:

1. **El sistema de diseño solo define colores.** `Themes/Tokens.axaml` tiene 24 colores y **cero tokens de espaciado, corner radius, sombra o escala tipográfica**. Consecuencia medida: **9 valores distintos de `Spacing`** y **32 de `Margin`** en el código; márgenes exteriores que van de 16 a 40 según la vista; y `Views/Finanzas/NuevaImportacionView.axaml` (509 líneas, la vista más grande) **sin margen exterior**.

2. **La adopción quedó a mitad de camino.** La Tarea 5 de aquel plan aplicó tokens a 3 vistas (Login, Inicio, ConfirmacionDialog). La app creció después de 20 a **58 vistas** — Finanzas sola tiene 16 — y lo nuevo se construyó a mano.

3. **Las grillas están sin estilar.** `Themes/DataGrid.axaml` tiene 39 líneas y lo único que hace es apagar dos indicadores de foco. No estila headers, alto de fila, alternancia ni padding. Hay **21 DataGrids en 14 vistas**, con dos convenciones de configuración conviviendo, y el estilo local `DataGridCell.num` **copiado 7 veces**.

4. **Fugas del sistema.** 86 botones sin clase de estilo; 9 vistas con `Foreground="Red"` literal existiendo `DangerBrush`; el módulo `Actualizaciones/` entero fuera del kit con paleta Material antigua (`#2196F3`, `#FF9800`); `UsuariosAdminView.axaml:90` con `BorderBrush="Gray"` y radio 4 ajeno a la escala.

5. **El "texto secundario" está hecho con `Opacity`.** 60 usos de `Opacity="0.5|0.6|0.7"`, a veces apilados sobre `Classes="caption"` que ya baja el contraste. Esa es la causa directa del aspecto lavado y sin jerarquía.

6. **26 bloques de navegación copiados literalmente** en `ShellMainView.axaml`. No existe carpeta `Controls/` ni `Components/`: sin componentes, se copia.

## 2. Decisiones aprobadas

### 2.1 Dirección visual: "Dashboard de datos"

Elegida sobre "Institucional sobrio" (evolución conservadora) y "Claro y espacioso" (baja densidad).

- El sidebar deja de estar teñido de verde y pasa a **pizarra media**. El verde de marca `#16A34A` **no cambia**: deja de ser fondo y pasa a ser **acento de acción** (botón principal, ítem activo, barra de acento).
- Cada vista gana un **header propio**: eyebrow de sección, título, línea de resumen y acciones jerarquizadas a la derecha.
- Fila de **métricas/KPIs** sobre las grillas donde aporte.
- **Grillas legibles**: encabezados en versalitas, números alineados a la derecha con cifras tabulares, filas alternadas sutiles, y estado comunicado con **badge (palabra + color)**, no solo con color — hoy un daltónico no distingue el stock negativo.

Razón: la app **son sus 21 grillas**. Con el sidebar teñido, el verde no señala nada. Con sidebar neutro, el verde guía. La opción de baja densidad se descartó porque un historial de 500 movimientos con filas altas obliga a scrollear de más.

### 2.2 Tema fijo claro

`App.axaml:5` pasa de `RequestedThemeVariant="Default"` a `"Light"`.

Hoy la app sigue el tema del sistema operativo pero **todos los tokens son de tema claro** y no hay `ThemeDictionaries`. En una máquina con el SO en modo oscuro, FluentTheme renderiza en oscuro lo que él controla (fondo de ventana, `TextBox`, `ComboBox`, `DataGrid`, scrollbars) mientras los tokens propios siguen claros: resultado ilegible. Decisión del usuario: la app tiene aspecto fijo, no negociado con el SO.

**No se construye variante oscura.** Queda explícitamente fuera de alcance.

### 2.3 Fundación de tokens

**Escala de espaciado, base 4.** Los 9 valores de `Spacing` y 32 de `Margin` colapsan a 7 tokens:

| Token | Valor | Uso |
|---|---|---|
| `Espacio1` | 4 | label ↔ campo |
| `Espacio2` | 8 | interno de controles agrupados |
| `Espacio3` | 12 | entre campos de formulario |
| `Espacio4` | 16 | padding interno de cards |
| `Espacio5` | 24 | **margen exterior estándar de toda vista** |
| `Espacio6` | 32 | entre bloques mayores |
| `Espacio7` | 48 | respiro de pantallas centradas (Login) |

Los valores sueltos `2`, `6` y `20` se eliminan.

**Radios.** De 4/6/8/10 sin criterio a tres: `RadioChico` 4 (badges), `RadioBase` 6 (botones e inputs), `RadioCard` 10 (cards y contenedores).

**Sombras.** De una sola hardcodeada en `Controls.axaml:224` a tres tokens: `SombraCard`, `SombraElevada` (popups, dropdowns), `SombraModal` (diálogos).

**Escala tipográfica.** Se suma el nivel faltante `.micro` (11px, versalitas, letter-spacing) para headers de tabla y eyebrows. Los 29 `FontSize` literales se mapean a las clases y desaparecen.

**Token nuevo `TextoTerciarioBrush` (`#94A3B8`).** Reemplaza los 60 usos de `Opacity`. El color se declara, no se atenúa — así el contraste es medible y testeable.

**Paleta del sidebar (variante "pizarra media", elegida entre tres profundidades):**

| Token | Antes | Ahora |
|---|---|---|
| `ColorSidebar` | `#14532D` | `#1E293B` |
| `ColorSidebarActivo` | `#166534` | `#334155` |
| `ColorSidebarAccent` | `#4ADE80` | `#16A34A` |
| `ColorSidebarTexto` | `#FFFFFF` | `#CBD5E1` |

Contrastes calculados: texto `#CBD5E1` sobre `#1E293B` = **9.8:1**; blanco sobre activo `#334155` = **10.3:1**. Ambos superan WCAG **AAA** y pasan `ButtonGhostContrasteTests.cs` sin modificarlo.

**Restricción derivada:** el verde `#16A34A` sobre `#1E293B` da **4.44:1**. Sirve para barra de acento e íconos (umbral gráfico 3:1) y **NO sirve como texto** (umbral 4.5:1). Prohibido usar verde como texto sobre el sidebar.

### 2.4 Componentes reutilizables

Se crea `src/StockApp.Presentation/Controls/` — hoy no existe — con cinco componentes:

1. **Header de vista** — eyebrow, título, línea de resumen, slot de acciones. Hoy cada vista improvisa y **15 no tienen ni título**.
2. **Tarjeta de métrica** — para la fila de KPIs.
3. **Badge de estado** — OK / Bajo mínimo / Negativo / Vencida, etc.
4. **Campo de formulario** — etiqueta, control, error. Reemplaza los 9 `Foreground="Red"` sueltos y marca el campo en error, no solo el texto.
5. **Estado vacío** — hoy una grilla sin datos y una grilla que falló al cargar se ven idénticas.

**Regla de jerarquía de acción:** una vista tiene **un solo botón primario (verde)**. Si hay dos acciones principales, no hay ninguna.

### 2.5 Sidebar con grupos colapsables

26 ítems planos en 7 grupos es un muro. Se agrupa en secciones desplegables.

**Comportamiento elegido:** varios grupos pueden estar abiertos a la vez; el estado se **recuerda entre sesiones**; el grupo que contiene la sección activa **se abre solo**. `Inicio` queda fijo arriba, fuera de todo grupo.

Implicancias:
- `ShellMainViewModel` gana estado de expansión por grupo, con sus tests.
- Los 26 bloques copiados colapsan a un `ItemsControl` con template.
- **Persistencia:** archivo local de preferencias de UI en el directorio de datos de la aplicación. Es preferencia por máquina y por usuario del SO; no va al servidor ni a la base. Coherente con la restricción del proyecto de no depender de configuración del servidor. *El mecanismo exacto se confirma en el plan de implementación.*

### 2.6 Política de tests de UI

Clasificación real de los 122 tests de `tests/StockApp.Presentation.UiTests/`:

| Categoría | Cantidad |
|---|---|
| A puro — guardián robusto | 25 |
| A pero frágil — verifica bien, localiza mal | 89 |
| B — accesibilidad (contraste WCAG) | 3 |
| C pura — frágil sin custodiar nada | 5 |

**31 tests tocan permisos o rol** (24 directos, 2 secundarios, 5 indirectos).

Política aprobada:

- **Los 5 de categoría C pura se borran**, sin reemplazo. Ejemplo de lo que son: `DocumentoListViewTests.cs:150` verifica que un botón diga `"Anular…"` con puntos suspensivos en vez de `"Anular"` — la diferencia bajo prueba es un carácter Unicode.
- **Los 89 "A pero frágil" se reescriben para localizar por identidad estable**: `x:Name`, o `ReferenceEquals(b.Command, comandoDelVm)`. No hay que diseñar nada nuevo: `InicioViewTests.cs` ya es el modelo correcto (localiza por `x:Name`) y `IngresoPorFacturaViewTests.cs` ya inventó los helpers `BotonPorCommand` (`:115`), `ComboPorItemsSource` (`:122`) e identidad por `DataContext` (`:172`). Se generaliza lo que ya funciona.
- **Los 25 A puro y los 3 de contraste se mantienen y se amplían.**
- **Regla única e innegociable: un test se reescribe para que verifique mejor, NUNCA para que pase.** Cada test reescrito se valida **por mutación**: se reintroduce el bug que custodiaba, se comprueba el rojo, se saca, se comprueba el verde. Un test que no se vio fallar no es un guardián.

**Por qué esto importa en Avalonia:** un `{Binding PuedeXxx}` con un typo evalúa a `null`, y entonces `IsVisible` **se queda en su default `true`** — el control gateado se muestra a un usuario sin permisos, en silencio. Los 5 tests de "reflexión de bindings" (`ReflexionVistaViewModelTests.cs`) son la única red contra ese modo de falla, y este refactor es exactamente el tipo de trabajo que lo dispara.

### 2.7 Huecos de seguridad preexistentes que este refactor debe cubrir antes de avanzar

Detectados durante el relevamiento, ya presentes hoy:

1. **`ShellMainView.axaml`: ~26 gates de permisos, cero tests de UI.** Es la vista que el refactor reescribe por completo.
2. **`DocumentoListView.axaml:67,71,75,84,164`: 5 gates por fila** (`PuedeIniciar`, `PuedeVolverAPendiente`, `PuedeFinalizar`, `PuedeAnular`, `PuedeReabrir`). `DocumentoListViewTests.cs` monta **siempre** con `RolUsuario.Admin` (`:156, :172, :187`), y el rol Admin cortocircuita el chequeo antes de mirar los permisos: los tests están verdes sin probar el gate.
3. **`DocumentoFormView.axaml:53,63-67`: 5 gates, cero cobertura de UI.**

Estos tests se escriben **antes** de tocar el sidebar, siempre **con usuario de permisos mixtos, nunca con Admin**.

## 3. Plan de ejecución

Barrido completo de las 58 vistas, partido en tandas. **Cada tanda es un commit con la suite en verde.**

| # | Tanda | Contenido |
|---|---|---|
| 0 | Blindaje del andamiaje | `RequestedThemeVariant="Light"`; incluir `Themes/DataGrid.axaml` en `TestAppBuilder.cs` (hoy falta: los tests validan grillas sin el estilo real de la app); verificar si Avalonia 12.0.5 respeta `FontFeatures="tnum"` sobre Inter; borrar los 5 tests C pura; unificar el helper `EsVisibleEnArbol`, hoy duplicado 4 veces |
| 1 | Fundación | Tokens de espaciado, radios, sombras, `TextoTerciarioBrush`, paleta de sidebar. `.micro` en Typography |
| 2 | Controles base | `Controls.axaml` al día y `DataGrid.axaml` de verdad: headers en versalitas, alto de fila, zebra, padding de celda, alineación numérica |
| 3 | Componentes | Carpeta `Controls/` con los 5 componentes, cada uno con test |
| 4 | **Red de seguridad** | Tests de gate faltantes para sidebar, `DocumentoListView` y `DocumentoFormView`. Con permisos mixtos. Validados por mutación |
| 5 | Shell | Sidebar pizarra media, grupos colapsables con estado persistido, 26 bloques colapsados a `ItemsControl` |
| 6 | Operación | Inicio, Productos (list + form), los 5 de Movimientos |
| 7 | Maestros | Categorías, Proveedores, Unidades de medida (6 vistas) |
| 8 | Finanzas | Las 16. La tanda más grande, va sola |
| 9 | Documentos y Tareas | 5 vistas |
| 10 | Reportes | Las 5, más eliminar los 7 estilos de celda numérica duplicados |
| 11 | Administración y acceso | Mantenimiento, Usuarios, Login, Reset, Bloqueo, Acceso limitado |
| 12 | Actualizaciones y diálogos | Los 3 de `Actualizaciones/` (hoy 100% fuera del sistema) y los 3 diálogos |
| 13 | Limpieza | Borrar `Views/MainWindowView.axaml` (vestigial, 20 líneas muertas, única con `FontSize="28"`); unificar `AdjuntosPanelView` con `AdjuntosDocumentoPanelView`, gemelos de 63 y 62 líneas |

### Validación

- Cada tanda: `dotnet test StockApp.sln` en verde.
- Al cierre de las tandas 5, 8 y 13: **verificación orgánica** con la app real corriendo, según la convención del proyecto. Toolkit disponible en `scripts/gui-verificacion/`. Un test verde no dice si la app se ve bien.

## 4. Riesgos y verificaciones pendientes

| Riesgo | Mitigación |
|---|---|
| `FontFeatures="tnum"` puede no funcionar en Avalonia 12.0.5 sobre Inter | Se verifica en la tanda 0. Fallback: ancho de columna fijo con alineación derecha — se ve bien igual |
| Reescribir el sidebar toca 26 gates de permisos | Tanda 4 escribe la red antes. No se toca el shell sin tests de gate |
| Mecanismo de persistencia de preferencias de UI sin confirmar | Se resuelve en el plan de implementación, antes de la tanda 5 |
| `MantenimientoViewTests.cs:379` compara posiciones geométricas con `TranslatePoint` | Se rompe ante cualquier rediseño correcto del layout de Mantenimiento. Se decide en la tanda 11: reescribir o borrar |
| Los 30 asserts `Content as string` se romperían al meter íconos en botones | Los íconos van con `i:Attached.Icon` (propiedad adjunta), que **no toca `Content`**. Es el patrón que el sidebar ya usa |

## 5. Fuera de alcance

- **Variante de tema oscuro.** La app queda en claro fijo.
- **Textos y copy.** No se cambia ni una palabra: hay tests que dependen de literales de UI y corregir textos es otro trabajo.
- **Renombrar o mover Views.** `ViewLocatorTests` custodia la convención de nombres.
- **Cambios de flujo de navegación**, más allá de agrupar el sidebar en secciones desplegables.
- **Caché de reportes con invalidación por movimiento de stock.** Deuda preexistente, sin relación con esto.
