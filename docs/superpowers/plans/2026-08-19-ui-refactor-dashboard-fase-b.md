I have everything I need. One constraint to flag up front:

**I cannot create the plan file.** This session runs in read-only planning mode with no file-writing tools. The full plan document is below — save it to `docs/superpowers/plans/2026-08-19-ui-refactor-dashboard-fase-b.md`.

---

# Resumen ejecutivo

## Inventario real (contado, no heredado de la spec)

**58 vistas confirmadas**, pero repartidas distinto de como dice la spec: **55 en `src/StockApp.Presentation/Views/`** + **3 en `src/StockApp.Presentation/Actualizaciones/Views/`** (esta carpeta no está bajo `Views/`; si contás solo `Views/` te dan 55 y parece que la spec miente).

Correcciones verificadas contra el código:

| Dato de la spec | Verificado | Realidad |
|---|---|---|
| "Finanzas sola tiene 16" | conté los archivos | **19** vistas en `Views/Finanzas/` |
| "`DataGridCell.num` copiado 7 veces" | `grep '<Style Selector='` en `Views/` | **6** copias locales (7ma = la canónica de `Themes/DataGrid.axaml`, creada en tanda 2) |
| "15 vistas sin título" | `grep -L titulo-vista` | **15 exactas** ✅ (12 en `Views/` + las 3 de `Actualizaciones/`) |
| "9 `Foreground="Red"`" | grep | **10** |
| "60 usos de `Opacity`" | grep | **58** |
| "21 DataGrids en 14 vistas" | grep | **21 en 14** ✅ |
| "26 bloques de navegación duplicados" | leí `ShellMainView.axaml` (131 líneas) | **cero sueltos.** Colapsados a `ItemsControl`. Ninguna otra vista tiene bloques de nav ✅ |

## Agrupación: módulo para el commit, patrón para la task

Mantengo la numeración 6-13 de la spec (agrupación por módulo) **como frontera de commit**, y meto la afinidad de patrón **como frontera de task**. Justificación:

- **Por qué módulo manda en el commit:** la validación orgánica es por módulo. El usuario verifica "Finanzas anda bien" recorriendo Finanzas. Un commit que toca 12 listados repartidos en 6 módulos no se puede verificar orgánicamente sin recorrer los 6, y si rompe algo, el revert se lleva puesto medio refactor. Además la spec fija los números y el usuario ya los usa como referencia ("la tanda 10 tiene una deuda").
- **Por qué patrón manda en la task:** dentro de cada módulo las vistas son sorprendentemente homogéneas. Los 6 maestros (3 de Catálogo + 3 de Finanzas) son **estructuralmente idénticos** — mismo `DockPanel Margin="24"` → `titulo-vista` → `Border.card` → barra de 3 botones → `ListBox` con badge `Inactiva`. Los 5 reportes también. Ahí la repetición es real y se valida en bloque.

Concretamente: se define un **catálogo de 8 patrones (P0-P7)** una sola vez, en la Task 6.0, y cada task posterior es "aplicar Pn a estas N vistas", con un test de patrón que monta las N y asserta los invariantes de golpe.

| Tanda | Módulo | Vistas | Patrones dominantes |
|---|---|---|---|
| 6 | Operación | **8** | P0 dashboard, P1 listado-grilla ×2, P3 formulario ×2, P5 wrapper ×2, P1' compuesto |
| 7 | Maestros | **6** | P2 listado-ListBox ×3, P3 formulario ×3 |
| 8 | Finanzas | **19** | P1 ×5, P2 ×3, P3 ×5, P4 contenedor ×2, P5 ×1, especiales ×3 |
| 9 | Documentos y Tareas | **5** | P1 ×2, P3 ×2, P5 ×1 |
| 10 | Reportes | **5** | P1 ×5 (+ borrado de 5 de los 6 `DataGridCell.num`) |
| 11 | Administración y acceso | **6** | P6 centrada ×3, P1' ×2, P4 ×1 |
| 12 | Actualizaciones y diálogos | **6** | P5 overlay ×3, P7 diálogo ×3 |
| 13 | Limpieza | 3 archivos | — |

## Decisión de tamaño: tres sub-fases, B1 en detalle completo

La Fase A fueron 3435 líneas para 6 tandas y **19 archivos tocados**. La Fase B toca **55**. A la misma densidad serían ~6000 líneas: un documento que nadie ejecuta.

**Divido en B1 / B2 / B3 y escribo B1 completo:**

- **B1 = tandas 6 + 7 (14 vistas).** Detalle TDD completo. Su producto real no son las 14 vistas: es el **catálogo de patrones probado en producción** — las tablas de sustitución exactas, los `x:Name` que hay que preservar, y el `GuardianDePatronTests` reutilizable. B2 y B3 se ejecutan mecánicamente contra ese molde.
- **B2 = tandas 8 + 9 + 10 (29 vistas).** Esbozada: alcance por vista, patrón asignado, riesgos y deuda heredada. Se escribe en detalle **después** de B1, cuando los patrones estén validados. Motivo idéntico al que la Fase A dio para no planificar la B por adelantado: planificar contra un molde que no existe garantiza reescribir.
- **B3 = tandas 11 + 12 + 13 (12 vistas + limpieza).** Esbozada. Son los casos que **no** entran en ningún patrón (diálogos `Window` sin `x:DataType`, overlays con paleta Material, pantallas centradas sin sidebar).

## Riesgos principales

1. **Deuda de seguridad heredada, no cerrada.** Las Tasks **4.3 y 4.4 de la Fase A nunca se ejecutaron** — el ledger registra "Task 4.1-4.2: complete" y nada más; `DocumentoFormViewGatesTests.cs` no existe. La tanda 9 no puede tocar Documentos sin cerrarlas primero.
2. **Diagnóstico de los gates de Documentos: la premisa del brief es parcialmente falsa.** Leí las fórmulas. `PuedeIniciar`/`PuedeVolverAPendiente`/`PuedeFinalizar` **no consultan permisos ni rol** — son puro autómata de estado. Solo `PuedeAnular` y `PuedeReabrir` miran rol, y con `_rol == RolUsuario.Admin` a secas: **no hay ningún `OR` de permisos que cortocircuitar**. El pedido de "usuario de permisos mixtos" no aplica acá; lo que hace falta es montar como **Operador** para que `PuedeAnular`/`PuedeReabrir` se vean alguna vez en `false`, y variar el **estado del documento** para los otros tres. Un test de permisos mixtos sobre estos cinco gates daría verde sin probar nada — el mismo error, de nuevo.
3. **`MovimientoHistorialView`: 3 gates reales de permiso, cero tests de UI, y entra en la tanda 6.** `PuedeFiltrarPorProducto` (:30) y `PuedeRecalcularStock` (:86, :92) custodian dos bugfixes documentados de 2026-08-16. Hay que escribir su red **antes** de reescribir el `WrapPanel` de filtros.
4. **`NuevaImportacionView` (509 líneas, 4 grillas, 31 bindings condicionales, 2 `<Style Selector="DataGridRow" x:CompileBindings="False">`)** custodiada por 3 archivos de test. Es la vista más peligrosa del repo. Va sola, en su propia task, al final de la tanda 8.
5. **`MantenimientoViewTests.cs` usa `TranslatePoint` en 4 sitios** (:181, :391, :392, :406-407), no en uno como decía la spec. La Fase A ya dictaminó no borrarlos (custodian una regresión real de `DockPanel`/`LastChildFill`). Cualquier `HeaderVista` en Mantenimiento mueve geometría → 4 asserts a adaptar en la tanda 11.
6. **`AdjuntosPanelView` vs `AdjuntosDocumentoPanelView` no son gemelos cosméticos.** Los diffeé: bindean a **dos tipos de ViewModel distintos con APIs distintas** (`PuedeAgregar`+`PuedeQuitar` vs `PuedeModificar`) y a DTOs de namespaces distintos. Unificarlos exige unificar los ViewModels y renombrar una View — y la spec pone "renombrar o mover Views" **fuera de alcance** (`ViewLocatorTests` custodia la convención). La tanda 13 de la spec se contradice a sí misma acá.

## Lo bueno: dos palancas que abaratan todo

- **`AvaloniaUseCompiledBindingsByDefault=true`** (`StockApp.Presentation.csproj:7`) y **55 de 58 vistas tienen `x:DataType`**. Un typo de binding es error de **build**, no null silencioso. Las únicas 3 sin `x:DataType` son los diálogos de `Views/Dialogs/`, que son `Window` manejados por code-behind. Esto significa que **no hace falta un test por vista** para la integridad de bindings: el compilador es la red.
- **La adopción de tokens en vistas es literalmente cero** — un solo uso (`ShellMainView.axaml:45`). No hay migración parcial que reconciliar: toda sustitución es greenfield.

## Preguntas abiertas para el usuario

1. **`Requerido` vs "(obligatorio)".** Las etiquetas hoy dicen `"SKU (obligatorio)"`. `CampoFormulario` marca lo requerido con asterisco rojo. Pasar a `Etiqueta="SKU" Requerido="True"` **cambia el texto renderizado** — y el comentario de `Typography.axaml:47-55` que escribió la Fase A dice explícitamente que "labels de formulario no se tocan". Recomiendo tratarlo como cambio de *forma de marcado*, no de copy, y usar el asterisco (si no, el componente no sirve para nada en 12 formularios). **Ningún test depende de esos literales** (verificado). Necesito tu OK.
2. **Stock negativo: ¿badge o color?** La spec pide badge por daltonismo. `SignoNegativoBrushConverter` está en **11 sitios de 8 vistas** repartidas en 4 tandas distintas, con tests propios (`SignoNegativoBrushConverterTests`, `TareaListViewTests:174`). Recomiendo **una task transversal propia**, no repartirla — pero eso rompe la frontera modular de las tandas. ¿Task transversal al final de B2, o dejarlo como está y descoparlo?
3. **`FontFeatures="+tnum"` sigue INCONCLUSO.** La sonda original de la Fase A era inválida (medía el ancho del `StackPanel` por falta de `HorizontalAlignment="Left"`). Si querés cifras tabulares en las 21 grillas, hay que rehacer la sonda **antes** de la tanda 10. ¿La rehacemos, o cerramos la deuda declarando que la alineación a la derecha alcanza?
4. **Tanda 13 / unificación de Adjuntos:** ¿descopar (recomendado: solo armonización visual, sin fusionar), o abrir el refactor de ViewModels?
5. **Token `Thickness` de 8 faltante.** Hace falta para `Padding` de barras de acción en ~20 vistas. ¿Lo agrego en una Task 6.0 (`Espacio2T`/`PaddingCompacto`), o seguimos escribiendo `Padding="8"` literal como se decidió en la tanda 5?

---
---

# Contenido del plan (guardar en `docs/superpowers/plans/2026-08-19-ui-refactor-dashboard-fase-b.md`)

# Refactor visual "Dashboard de datos" — Fase B (tandas 6-13)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** aplicar a las 55 vistas restantes la fundación construida en la Fase A — tokens, controles base, grillas estiladas y los 5 componentes de `Controls/` — de modo que la app entera hable un solo lenguaje visual y las 15 vistas que hoy no tienen ni título ganen un `HeaderVista`.

**Architecture:** la Fase A trabajó de adentro hacia afuera y dejó el molde. La Fase B es barrido. Se organiza en dos ejes cruzados: **el módulo es la frontera de commit** (revert acotado + verificación orgánica módulo por módulo) y **el patrón es la frontera de task** (cada task repite un solo movimiento sobre N vistas estructuralmente idénticas y se valida en bloque con un solo test). El catálogo de patrones se define y se prueba en la tanda 6; de ahí en adelante todo es aplicación mecánica.

**Tech Stack:** .NET 10, Avalonia 12.0.5, Avalonia.Controls.DataGrid 12.0.1, CommunityToolkit.Mvvm, Avalonia.Headless.XUnit, Optris.Icons.Avalonia (prefijo `mdi`), Inter vía `Avalonia.Fonts.Inter`. `AvaloniaUseCompiledBindingsByDefault=true`.

**Spec:** `docs/superpowers/specs/2026-08-18-ui-refactor-dashboard-design.md`
**Fase previa:** `docs/superpowers/plans/2026-08-18-ui-refactor-dashboard-fase-a.md`
**Ledger previo:** `.superpowers/sdd/2026-08-18-ui-refactor-dashboard-fase-a/progress.md` (7 rulings — leerlos antes de tocar nada)

**Línea base al arrancar:** 3096 tests verdes, rama `feat/ui-refactor-dashboard`, HEAD `4dda179`.

---

## División en sub-fases

La Fase A fueron 3435 líneas para 6 tandas y 19 archivos. La Fase B toca 55 archivos de vista. A la misma densidad de escritura serían ~6000 líneas — un documento que nadie ejecuta.

| Sub-fase | Tandas | Vistas | Estado de este documento |
|---|---|---|---|
| **B1** | 6, 7 | 14 | **Detalle completo.** Produce el catálogo de patrones |
| **B2** | 8, 9, 10 | 29 | Esbozada: alcance, patrón asignado, riesgos, deuda. Se detalla al cerrar B1 |
| **B3** | 11, 12, 13 | 12 + limpieza | Esbozada. Son los casos que no entran en ningún patrón |

**Por qué B1 se escribe completa y las otras no:** B1 no vale por sus 14 vistas, vale por el molde. Detallar B2 hoy sería especificar sustituciones contra patrones que todavía no se probaron contra el compilador ni contra la suite — exactamente el error que la Fase A evitó al no planificar la Fase B por adelantado ("ese plan se escribe consumiendo componentes que todavía no existen, y planificarlo a ciegas garantiza reescribirlo").

---

## Global Constraints

Aplican a TODAS las tareas. No se repiten por tanda.

- **Tema fijo claro.** Sin variante oscura ni `ThemeDictionaries`.
- **El verde `#16A34A` NO se usa como texto sobre el sidebar `#1E293B`** (4.44:1, bajo el umbral de texto).
- **Una vista, un solo botón primario.** Si hay dos acciones principales, no hay ninguna.
- **No se cambia copy de negocio.** Títulos de vista, textos de botón y mensajes quedan **idénticos**. Excepción acordada y acotada: los headers de columna y eyebrows en versalitas (ya decidido en la Fase A) y las etiquetas de campo obligatorio (ver **Ruling B-1**).
- **No se renombran ni se mueven Views.** `ViewLocatorTests` custodia la convención.
- **Un test se reescribe para que verifique MEJOR, NUNCA para que pase.** Todo test nuevo o reescrito que custodie un gate se valida por mutación: reintroducir el bug, ver el rojo, sacarlo, ver el verde.
- **Cada tanda cierra con `dotnet test StockApp.sln` en verde y UN commit.**

### Trampas conocidas — leer antes de escribir XAML

| # | Trampa | Consecuencia | Antídoto |
|---|---|---|---|
| T1 | `Espacio1..7` son `x:Double`, **no** `Thickness` | `Padding="{DynamicResource Espacio2}"` compila y **explota en runtime** con `InvalidCastException` | `Espacio*` solo en `StackPanel.Spacing` / `Grid.RowSpacing`. Para `Padding`/`Margin`: `MargenVista`, `PaddingCard`, `PaddingCelda`, o literal |
| T2 | Falta un token `Thickness` de 8 | Ver Ruling B-2 | `Padding="8"` literal, documentado |
| T3 | `Application.Current` sin calificar no compila | CS0234 por colisión con el namespace `StockApp.Application` | `Avalonia.Application.Current` |
| T4 | Un comentario XAML con `--` rompe el build | AVLN1001 → cascada MSB4025 | usar `—` o reformular |
| T5 | Los selectores globales de `Typography.axaml` ganan a los `Style` anidados de un `ControlTheme` | override silencioso (Ruling 7 de Fase A) | no poner `Classes="seccion"`/`.body` donde un `ControlTheme` quiera pisar el `Foreground` |
| T6 | Las Views de Avalonia **no se auto-inicializan** | la vista monta vacía | `DataContextChanged` en el code-behind — **37 de las 55 vistas ya lo tienen**; no romperlo |
| T7 | Un `UserControl` trae su propio `NameScope`; `Window.FindControl` no lo atraviesa | test que no encuentra el control | por eso los componentes son `TemplatedControl`. Para localizar: `window.GetVisualDescendants().OfType<T>()` |
| T8 | Subagentes que corren `dotnet test StockApp.sln` mueren por timeout | falso rojo | `timeout: 600000` |
| T9 | `DataGridCell.num` quedó **sin** `FontFeatures="+tnum"` | cifras no tabulares | veredicto INCONCLUSO; la sonda original de Fase A era inválida (medía el ancho del contenedor). Ver Ruling B-4 |

### Palanca a favor: el compilador es la red de bindings

`AvaloniaUseCompiledBindingsByDefault=true` (`StockApp.Presentation.csproj:7`) y **55 de 58 vistas declaran `x:DataType`**. Un `{Binding PuedeXxx}` mal escrito es **error de build (AVLN2000)**, no un `null` silencioso que deja `IsVisible` en `true`.

**Consecuencia de planificación:** no se escribe un test por vista para validar bindings. Se escriben tests solo donde hay (a) un **gate de permiso** sin cobertura, o (b) un **invariante de patrón** que el compilador no ve (existencia de `HeaderVista`, margen exterior, ausencia de `Opacity` literal).

Las 3 vistas **sin** `x:DataType` — `Views/Dialogs/ConfirmacionDialog.axaml`, `MensajeDialog.axaml`, `PedirTextoDialog.axaml` — son `Window` manejados por code-behind y **no** tienen la red del compilador. Tanda 12 las trata con cuidado extra.

**Comandos de verificación:**
- Suite completa: `dotnet test StockApp.sln`
- Solo UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Puntual: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~NombreDelTest"`

---

## Rulings de la Fase B

Decisiones tomadas al planificar, con su costo si me equivoco. Se toman acá para que quien ejecute no las re-litigue.

**Ruling B-1 — "(obligatorio)" pasa a asterisco.** `<TextBlock Text="SKU (obligatorio)" />` se convierte en `<c:CampoFormulario Etiqueta="SKU" Requerido="True">`. Esto **cambia el texto renderizado**, y el comentario de `Typography.axaml:47-55` dice que "labels de formulario no se tocan".
*Por qué igual:* el sufijo "(obligatorio)" **es** el marcado de obligatoriedad, y `CampoFormulario` existe precisamente para estandarizarlo. Conservarlo Y poner `Requerido="True"` marca lo mismo dos veces; conservarlo con `Requerido="False"` deja el componente sin razón de ser en los 12 formularios. Verificado: **ningún test depende de esos literales** (`grep -rn "obligatorio" tests/` → cero coincidencias en localización de controles de UI).
*Costo si me equivoco:* revertir es mecánico — `Etiqueta="SKU (obligatorio)" Requerido="False"` en cada sitio.
**APROBADO por el usuario el 2026-08-19.** Ya no bloquea la tanda 6.

**Ruling B-2 — se agrega el token `Thickness` faltante de 8.** `Tokens.axaml` tiene `MargenVista` (24), `PaddingCard` (16) y `PaddingCelda` (12,8), pero no un `Thickness` de 8. La barra de acciones de ~20 vistas lo necesita.
*Por qué:* la tanda 5 ya se comió esta deuda (`Padding="8"` literal en `ShellMainView.axaml:22`, documentado en el ledger). Repetirla 20 veces convierte una deuda puntual en un patrón.
*Cómo:* `<Thickness x:Key="PaddingCompacto">8</Thickness>` en `Tokens.axaml`, custodiado por un caso nuevo en `TokensDisenioTests`.
*Costo si me equivoco:* un token de más; trivial de borrar.

**Ruling B-3 — las Tasks 4.3 y 4.4 de la Fase A se ejecutan en la tanda 9, no antes.** Nunca se ejecutaron (el ledger registra 4.1-4.2 y salta a 1.x; `DocumentoFormViewGatesTests.cs` no existe).
*Por qué en la 9 y no ya:* la regla de la Fase A es "no se toca una vista sin su red de gates", no "todas las redes primero". Ponerlas en la 9 las deja pegadas a la tanda que las necesita.
*Costo si me equivoco:* ninguno mientras nadie toque Documentos antes de la 9.

**Ruling B-4 — la sonda de `tnum` NO se rehace en la Fase B.** Queda como deuda abierta, declarada.
*Por qué:* la sonda original medía el ancho del `StackPanel` contenedor en vez del glifo (por falta de `HorizontalAlignment="Left"`), así que su veredicto era ruido. Rehacerla bien es una investigación de tipografía, no un refactor de vistas, y bloquearía 8 tandas por un detalle que la alineación a la derecha ya resuelve funcionalmente.
*Costo si me equivoco:* si más adelante se quiere `+tnum`, es **un `Setter` en un solo archivo** (`Themes/DataGrid.axaml:97`) — el refactor de las 55 vistas no lo hace ni más ni menos caro.

**Ruling B-5 — el diagnóstico de los gates de Documentos que trae el brief está mal, y se corrige acá.** El brief pide "tests con usuario de permisos mixtos, no Admin" para los 10 gates de `DocumentoListView`/`DocumentoFormView`. Leí las fórmulas:

| Gate | Fórmula real | ¿Mira permisos? |
|---|---|---|
| `PuedeIniciar` | `Estado == Pendiente && PuedeTransicionarA(EnProceso)` | **no** |
| `PuedeVolverAPendiente` | `PuedeTransicionarA(Pendiente)` | **no** |
| `PuedeFinalizar` | `PuedeTransicionarA(Finalizado)` | **no** |
| `PuedeAnular` | `_rol == RolUsuario.Admin && PuedeTransicionarA(Anulado)` | **rol, no permiso** |
| `PuedeReabrir` | `_rol == RolUsuario.Admin && EsCerrado` | **rol, no permiso** |

(`DocumentoListViewModel.cs:49-57`; en `DocumentoFormViewModel.cs:102-106` son las mismas más `!EsNuevoDocumento`, con `EsAdmin => _session.RolActual == RolUsuario.Admin` en `:86`.)

**No hay ningún `OR` de permisos que cortocircuitar.** El problema real de montar con Admin es otro: `PuedeAnular`/`PuedeReabrir` quedan **fijos en `true`** y nunca se ve el gate en `false`. Un test de "permisos mixtos" sobre estos cinco daría verde sin probar nada — el mismo error, con otro disfraz.
*Corrección:* la matriz correcta cruza **rol** (Admin / Operador) con **estado del documento** (Pendiente / EnProceso / Finalizado / Anulado). Detalle en la tanda 9.
*Costo si me equivoco:* ninguno; es lectura directa de tres archivos.

**Ruling B-6 — Stock negativo: task transversal propia al final de B2.** `SignoNegativoBrushConverter` está en 11 sitios de 8 vistas repartidas en 4 tandas distintas (6, 8, 10 y potencialmente otras). En vez de migrar cada sitio a `BadgeEstado` en la tanda que toca ese archivo, se agrupan los 11 en una sola task dedicada, al cierre de B2 (tras la tanda 10).
*Por qué:* repartir un cambio de semántica (color→badge) en 4 commits distintos deja la app comunicando lo mismo de dos formas diferentes durante todo B2/B3 — algunas vistas con badge, otras todavía con el color solo, durante semanas de refactor. Es peor que la inconsistencia visual que el refactor busca eliminar. Un cambio de semántica se hace de una vez o no se hace.
*Costo si me equivoco:* ninguno funcional — es una decisión de secuenciación de commits, no de diseño. Si hiciera falta revertir, es un solo commit a revertir en vez de cuatro.
**Decisión del usuario, 2026-08-19.**

---

## Catálogo de patrones (P0-P7)

Se define en la Task 6.0. Cada task posterior referencia un patrón en vez de repetir la receta.

### P0 — Dashboard (1 vista)
`InicioView` (317 líneas). Única. Cards de aviso + accesos rápidos. Gana `HeaderVista` + fila de `TarjetaMetrica`.

### P1 — Listado con grilla (14 vistas)
**Forma actual:**
```xml
<DockPanel Margin="24">
    <TextBlock DockPanel.Dock="Top" Text="X" Classes="titulo-vista" Margin="0,0,0,16" />
    <Border Classes="card">
        <!-- barra de acciones + DataGrid -->
```
**Forma destino:**
```xml
<DockPanel Margin="{DynamicResource MargenVista}">
    <c:HeaderVista DockPanel.Dock="Top" Eyebrow="MÓDULO" Titulo="X">
        <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Espacio2}">
            <!-- acciones: máximo UN Classes="primary" -->
        </StackPanel>
    </c:HeaderVista>
    <Border Classes="card">
        <!-- DataGrid, sin barra de acciones propia -->
```
Notas: `Acciones` es la propiedad `[Content]` de `HeaderVista` (`HeaderVista.cs:51`), así que el `StackPanel` va como hijo directo, sin sintaxis de elemento-propiedad. `HeaderVista` ya trae `Margin="0,0,0,24"` en su `ControlTheme`, así que el `Margin="0,0,0,16"` del `TextBlock` desaparece.
Vistas: `ProductoListView`, `MovimientoHistorialView`, `GastosView`, `IngresosView`, `ControlPoaView`, `LibroCajaView`, `HistorialImportacionesView`, `DocumentoListView`, `TareaListView`, `AuditoriaLogView`, `HistorialPorProductoView`, `MasMovidosView`, `StockCategoriaView`, `ValorizacionView`.

### P2 — Listado con ListBox (maestros, 6 vistas)
Idéntico a P1 pero con `ListBox` y badge `Inactiva`. **Las 6 son estructuralmente la misma vista.**
Cambio adicional: `<Border Classes="badge-inactiva"><TextBlock Classes="badge-inactiva-texto"/></Border>` → `<c:BadgeEstado Texto="Inactiva" Tono="Neutro" IsVisible="{Binding !Activo}" />`.
Vistas: `CategoriaListView`, `ProveedorListView`, `UnidadMedidaListView`, `FuenteFinanciamientoListView`, `RubroGastoListView`, `LineaPoaListView`.
**Ojo:** las 3 de Finanzas están embebidas como pestañas de `MaestrosFinanzasView` → **NO llevan `HeaderVista`** (el título ya lo pone el contenedor y el `TabItem.Header`). Las 3 de Catálogo son vistas de primer nivel → **sí** llevan.

### P3 — Formulario (12 vistas)
**Forma actual:**
```xml
<Border Classes="card" Padding="24" Margin="24" MaxWidth="380" HorizontalAlignment="Center" VerticalAlignment="Center">
    <StackPanel Spacing="12">
        <TextBlock Text="{Binding Titulo}" Classes="titulo-vista" Margin="0,0,0,8" />
        <StackPanel Spacing="4">
            <TextBlock Text="Nombre (obligatorio)" />
            <TextBox Text="{Binding Nombre}" />
        </StackPanel>
```
**Forma destino:**
```xml
<Border Classes="card" Padding="{DynamicResource MargenVista}" Margin="{DynamicResource MargenVista}"
        MaxWidth="380" HorizontalAlignment="Center" VerticalAlignment="Center">
    <StackPanel Spacing="{DynamicResource Espacio3}">
        <c:HeaderVista Titulo="{Binding Titulo}" />
        <c:CampoFormulario Etiqueta="Nombre" Requerido="True">
            <TextBox Text="{Binding Nombre}" />
        </c:CampoFormulario>
```
Notas: `<TextBlock Foreground="Red">` de error → `Foreground="{DynamicResource DangerBrush}"` (10 sitios en toda la app).
**El seam de validación NO se toca.** `Controls.axaml:185` (TextBox) y `:203` (ComboBox) definen `(DataValidationErrors.ErrorConverter)`; los custodian los 2 tests de `MovimientoFormControlValidacionTests.cs`. `CampoFormulario` es un `ContentControl` con `ContentPresenter` pelado justamente para no interceptarlo (comentario en `Componentes.axaml:151-153`). **Prohibido** agregarle `ErrorTemplate` o un `Setter` de `ErrorConverter`.
Vistas: `CategoriaFormView`, `ProductoFormView`, `ProveedorFormView`, `UnidadMedidaFormView`, `MovimientoFormControl`, `GastoFormView`, `IngresoFormView`, `LineaPoaFormView`, `RubroGastoFormView`, `FuenteFinanciamientoFormView`, `DocumentoFormView`, `TareaFormView`.

### P4 — Contenedor con TabControl (3 vistas)
`MaestrosFinanzasView`, `ImportacionView`, `AccesoLimitadoView`. El `titulo-vista` pasa a `HeaderVista`; el `TabControl` no se toca. `AccesoLimitadoView` no tiene título hoy — gana uno.

### P5 — Panel/wrapper embebido (8 vistas) — **NO lleva `HeaderVista`**
`EntradaRegistroView`, `SalidaRegistroView` (13 líneas cada uno, wrappers de `MovimientoFormControl`), `AdjuntosPanelView`, `AdjuntosDocumentoPanelView`, los 3 de `Actualizaciones/`.
Excepción argumentada: `EntradaRegistroView`/`SalidaRegistroView` renderizan a `MovimientoFormControl`, que ya toma su título de `{Binding Titulo}` del VM correspondiente. Ponerles un `HeaderVista` propio duplicaría el título. **Cuentan como "sin título" en el conteo de 15, pero se resuelven vía el `HeaderVista` de `MovimientoFormControl`.**

### P6 — Pantalla centrada sin sidebar (3 vistas)
`LoginView`, `ResetAdminView`, `BloqueoLicenciaView`. Respiro `Espacio7` (48). Tanda 11.

### P7 — Diálogo `Window` (3 vistas)
`ConfirmacionDialog`, `MensajeDialog`, `PedirTextoDialog`. **Sin `x:DataType`, sin bindings, todo code-behind con `x:Name`.** No hay red del compilador. Se tokenizan `Padding`/`Spacing`/`CornerRadius`; **no** llevan `HeaderVista` (tienen `Title` de ventana). Los `x:Name` (`MensajeText`, `CancelarButton`, `ConfirmarButton`, …) son **intocables**: `ConfirmacionServiceDialogosConsecutivosTests` los usa.

---

## Mapa de cobertura de tests por vista

Fuente: `tests/StockApp.Presentation.UiTests/`, 45 archivos, 3096 tests en la suite completa.

**Vistas CON tests de UI (13 de 55):**

| Vista | Archivo(s) | Tests | Riesgo al refactorizar |
|---|---|---|---|
| `InicioView` | `InicioViewTests`, `InicioPanelTareasTests` | 15 | **Alto** — localiza por `x:Name` (`BorderPanelTareas`, `BorderAvisoBackupProblema`, `BorderAvisoBackupDesconocido`, `BorderAccesosRapidos`, `BotonAcceso*`). Preservar TODOS |
| `IngresoPorFacturaView` | `IngresoPorFacturaViewTests`, `IngresoPorFacturaLocaleDecimalTests` | 14 | **Alto** — helpers `BotonPorCommand`, `ComboPorItemsSource`, identidad por `DataContext` |
| `MovimientoFormControl` | `MovimientoFormControlValidacionTests`, `...CantidadCulturaTests` | 5 | **Crítico** — seam del `ErrorConverter`. Localiza por `t.Name == "PrecioUnitarioBox"` |
| `NuevaImportacionView` | `NuevaImportacionGastosGridTests`, `...LineasPoaGridTests`, `...CondicionCreditoTests` | 10 | **Crítico** — 509 líneas, 4 grillas, `x:CompileBindings="False"` en 2 estilos |
| `MantenimientoView` | `MantenimientoViewTests` | 17 | **Alto** — 4 asserts con `TranslatePoint` (:181, :391-392, :406-407) |
| `TareaListView` / `TareaFormView` | `TareaListViewTests`, `TareaFormViewTests` | 19 | Medio |
| `DocumentoListView` | `DocumentoListViewTests` | 6 | **Alto** — 3 montajes con Admin, gates sin cubrir (Ruling B-5) |
| `GastosView` | `GastosViewTests` | 2 | Bajo — gates ya cubiertos con Operador |
| `IngresosView` | `IngresosViewTests` | 5 | Bajo — ídem |
| `PagosGastoView` | `PagosGastoViewTests` | 6 | Bajo — ídem |
| `AccesoLimitadoView` | `AccesoLimitadoViewTests`, `ViewLocatorTests` | 4 | Bajo |
| `AdjuntosPanelView` | `ViewLocatorTests` | 2 | Bajo (solo resolución del locator) |
| `ShellMainView` | `ShellMainViewGatesTests` | 15 | **Ya refactorizada (tanda 5)** |

**Las otras 42 vistas no tienen ni un test de UI.** Su red es: el compilador (bindings) + el `GuardianDePatronTests` que crea la Task 6.0 + `dotnet test StockApp.sln`.

**Deuda de infraestructura de tests heredada de la Fase A:** `GastosViewTests.cs:51`, `IngresosViewTests.cs:47` y `PagosGastoViewTests.cs:49` siguen declarando su propio `CurrentSessionFake` privado con `EstablecerPermisos` **no-op** — exactamente el bug que el Ruling 6 de la Fase A arregló en `SesionFake`. La Task 4.1 creó `SesionFake` pero no migró estos tres. Se migran en la tanda 8.

---

## Deuda transversal a repartir

Cada ítem se asigna a la tanda que toca el archivo, para no abrir commits transversales.

| Deuda | Sitios | Tanda que la cierra |
|---|---|---|
| `<Style Selector="DataGridCell.num">` local (redundante con `Themes/DataGrid.axaml:97`) | **6, no 7:** `MovimientoHistorialView:108`, `StockCategoriaView:36`, `ValorizacionView:37`, `AuditoriaLogView:60`, `HistorialPorProductoView:60`, `MasMovidosView:60` | 1 en tanda **6**, 5 en tanda **10** |
| `Foreground="Red"` literal | **10:** `DocumentoFormView:105`, `TareaFormView:94`, `RubroGastoFormView:27`, `IngresoFormView:48`, `FuenteFinanciamientoFormView:24`, `LineaPoaFormView:70`, `UsuariosAdminView:143`, `GastoFormView:117`, `PagosGastoView:81`, `IngresoPorFacturaView:178` | 6, 8, 9, 11 |
| `Opacity="0.x"` literal → `TextoTerciarioBrush` | **58** en 24 vistas | la tanda de cada vista |
| `Classes="badge-inactiva"` → `c:BadgeEstado` | **10 vistas** | 6, 7, 8, 11 |
| `Classes="badge-inactiva"` / `badge-inactiva-texto` en `Controls.axaml:234,242` | quedan muertos tras lo anterior | **13** |
| `Views/MainWindowView.axaml` + `.axaml.cs` + `ViewModels/MainWindowViewModel.cs` | **muertos** (verificado: cero referencias fuera de sí mismos; `ViewLocatorTests` no enumera) | **13** |
| Paleta Material en `Actualizaciones/` (`#2196F3`, `#FF9800`, `#F44336`, `#FFFDF0`, `#E8F4FD`, `#B71C1C`, `#E65100`, `#FFEBEE`) | 9 sitios en 3 archivos | **12** |
| `CurrentSessionFake` privados con `EstablecerPermisos` no-op | 3 archivos de test | **8** |
| `SignoNegativoBrushConverter` (color sin palabra) | 11 sitios en 8 vistas | **task transversal propia, al cierre de B2 (Ruling B-6)** |

---

# SUB-FASE B1 — El molde

## Tanda 6: Operación

**Objetivo:** definir y probar el catálogo de patrones contra las 8 vistas del núcleo operativo. Es la tanda que más piensa y menos vistas toca por unidad de esfuerzo. Todo lo que sale mal acá sale mal 55 veces después.

**Vistas (8):** `InicioView` (P0), `ProductoListView` (P1), `ProductoFormView` (P3), `MovimientoHistorialView` (P1), `MovimientoFormControl` (P3), `EntradaRegistroView` (P5), `SalidaRegistroView` (P5), `IngresoPorFacturaView` (P1 compuesto).

**Riesgo de la tanda: ALTO.** 4 de las 8 vistas tienen tests que localizan por `x:Name` o por identidad de `Command`, y `MovimientoFormControl` es el guardián del seam de validación.

---

### Task 6.0: Catálogo de patrones y guardián de bloque

**Files:**
- Modify: `src/StockApp.Presentation/Themes/Tokens.axaml` (+`PaddingCompacto`, Ruling B-2)
- Modify: `tests/StockApp.Presentation.UiTests/TokensDisenioTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/PatronHelpers.cs`

**Interfaces:**
- Consumes: los 5 componentes de `Controls/` (API en `Controls/*.cs`), `ArbolVisual.EsVisibleEnArbol`, `SesionFake`.
- Produces: `PatronHelpers.HeaderDe(Control)`, `PatronHelpers.MargenExteriorDe(Control)`, `PatronHelpers.OpacidadesLiteralesDe(Control)` — usados por TODAS las tandas 6-12.

**Por qué este guardián y no un test por vista:** 42 de las 55 vistas no tienen ningún test. Escribir uno por vista serían 42 archivos nuevos, un plan más grande que el refactor. El compilador ya cubre los bindings (`AvaloniaUseCompiledBindingsByDefault=true`). Lo que el compilador NO ve es "esta vista tiene un `HeaderVista` con el título correcto", "su margen exterior es `MargenVista`" y "no quedó ningún `Opacity` literal". Eso es lo que este guardián mide, sobre N vistas de golpe.

- [ ] **Step 1: Escribir el guardián que falla**

Crear `tests/StockApp.Presentation.UiTests/PatronHelpers.cs` con:

```csharp
/// <summary>
/// Localiza el HeaderVista de una vista. Devuelve null si no hay ninguno.
/// HeaderVista es TemplatedControl (no UserControl), asi que vive en el mismo
/// arbol visual que la vista y GetVisualDescendants lo alcanza — ver T7.
/// </summary>
public static HeaderVista? HeaderDe(Control vista)
    => vista.GetVisualDescendants().OfType<HeaderVista>().FirstOrDefault();

/// <summary>
/// Recorre el arbol LOGICO buscando cualquier control con Opacity != 1.0 fijada
/// como valor local. La opacidad via converter (ActivoOpacidadConverter) NO cuenta:
/// es semantica de dominio, no atenuacion decorativa. Se distingue por
/// GetDiagnostic(OpacityProperty).Priority == BindingPriority.LocalValue con
/// valor constante.
/// </summary>
public static IReadOnlyList<Control> OpacidadesLiteralesDe(Control vista) { /* ... */ }
```

Crear `GuardianDePatronTests.cs` con un `[AvaloniaTheory]` alimentado por `[InlineData]`, una fila por vista de la tanda:

```csharp
[AvaloniaTheory]
[InlineData(typeof(ProductoListView), "Productos", "CATÁLOGO")]
[InlineData(typeof(MovimientoHistorialView), "Historial de movimientos", "MOVIMIENTOS")]
// ... una linea por vista, y las tandas 7-12 agregan las suyas a esta misma tabla
public void Vista_TieneHeaderVistaConElTituloEsperado(Type tipoVista, string titulo, string eyebrow)
```

Más tres tests de invariante que corren sobre **todas** las filas de la tabla:
1. `Vista_TieneMargenExteriorEstandar` — el `Margin` del panel raíz es `MargenVista` (24).
2. `Vista_NoTieneOpacidadesLiterales` — `OpacidadesLiteralesDe(vista)` es vacío.
3. `Vista_NoTieneUnSegundoBotonPrimario` — a lo sumo un `Button` con `Classes.Contains("primary")` visible.

- [ ] **Step 2: Correr y ver el rojo**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~GuardianDePatron"` (timeout 600000)
Expected: **FAIL**. Concretamente `Vista_TieneHeaderVistaConElTituloEsperado` debe fallar con `HeaderDe(vista) == null` para las 8 vistas — hoy ninguna usa el componente. Si alguna pasa en verde, el helper está mal.

- [ ] **Step 3: Agregar `PaddingCompacto` (Ruling B-2)**

En `Tokens.axaml`, junto a `PaddingCard`/`PaddingCelda`:
```xml
<Thickness x:Key="PaddingCompacto">8</Thickness>
```
Agregar el caso correspondiente a `TokensDisenioTests`. Correr solo ese test: debe pasar de rojo a verde.

- [ ] **Step 4: Documentar los 8 patrones en este mismo plan**

No es paso de código: es el paso donde quien ejecuta **confirma o corrige** el catálogo P0-P7 de arriba contra lo que efectivamente vio al abrir los archivos. Si un patrón no calza, se corrige acá, no en la vista.

---

### Task 6.1: P1 sobre `ProductoListView` — la vista de referencia

**Files:**
- Modify: `src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml`
- Test: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (fila ya agregada en 6.0)

**Interfaces:**
- Consumes: `c:HeaderVista`, `c:BadgeEstado`, tokens `MargenVista`/`Espacio2`.
- Produces: **la referencia de P1.** Las otras 13 vistas P1 copian de acá.

**Por qué esta primero:** 149 líneas, 1 grilla, cero tests de UI que la localicen. Máxima señal, mínimo riesgo. Si P1 está mal, se descubre acá y no en `LibroCajaView` con sus 5 grillas.

**Tabla de sustitución:**

| Línea | Hoy | Pasa a |
|---|---|---|
| `:12` | `<DockPanel Margin="24">` | `<DockPanel Margin="{DynamicResource MargenVista}">` |
| `:14-17` | `<TextBlock DockPanel.Dock="Top" Text="Productos" Classes="titulo-vista" Margin="0,0,0,16" />` | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="CATÁLOGO" Titulo="Productos">` + el `StackPanel` de acciones migrado desde `:23-46` |
| `:23-46` | `<Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto" ...>` con `TextBox` de búsqueda + 4 botones | el `TextBox` de búsqueda **se queda** en la card (es filtro de la grilla, no acción de vista); los 4 botones migran al slot `Acciones` |
| `:135-136` | `<Border Classes="badge-inactiva"><TextBlock Text="Inactiva" Classes="badge-inactiva-texto" /></Border>` | `<c:BadgeEstado Texto="Inactiva" Tono="Neutro" IsVisible="{Binding !Activo}" />` |
| 7 usos de `Opacity` | `Opacity="{Binding Activo, Converter=...}"` | **se quedan** — son semántica de dominio vía converter, no atenuación decorativa (ver comentario en `:50-55` del archivo) |

**Decisión de jerarquía:** hay un `primary` ("Nuevo") y tres `secondary`. Cumple la regla. **No agregar** un segundo primario.

- [ ] **Step 1: Ver el rojo del guardián para esta vista**

Run: `--filter "FullyQualifiedName~GuardianDePatron&DisplayName~ProductoListView"`
Expected: FAIL — `HeaderDe` devuelve null.

- [ ] **Step 2: Aplicar la tabla de sustitución**

Agregar `xmlns:c="using:StockApp.Presentation.Controls"` al `UserControl`.
**Cuidado T1:** `Spacing="{DynamicResource Espacio2}"` sí (es `double`); `Padding="{DynamicResource Espacio2}"` **no** (explota en runtime).
**Cuidado T4:** ningún comentario nuevo con `--`.

- [ ] **Step 3: Ver el verde**

Run: `--filter "FullyQualifiedName~GuardianDePatron&DisplayName~ProductoListView"`
Expected: PASS en los 4 invariantes.

- [ ] **Step 4: Validar por mutación**

Tres mutaciones, tres rojos, revertir cada una:
1. Cambiar `Titulo="Productos"` por `Titulo="Product"` → `Vista_TieneHeaderVistaConElTituloEsperado` rojo.
2. Cambiar `Margin="{DynamicResource MargenVista}"` por `Margin="16"` → `Vista_TieneMargenExteriorEstandar` rojo.
3. Poner `Classes="primary"` en el botón "Editar" → `Vista_NoTieneUnSegundoBotonPrimario` rojo.

Si alguna mutación NO pone el test en rojo, el guardián no custodia nada y hay que arreglarlo antes de seguir.

- [ ] **Step 5: Suite completa**

Run: `dotnet test StockApp.sln` (timeout 600000)
Expected: PASS, 3096 + los tests nuevos.

---

### Task 6.2: Red de gates de `MovimientoHistorialView` (ANTES de tocarla)

**Files:**
- Create: `tests/StockApp.Presentation.UiTests/MovimientoHistorialGatesTests.cs`

**Interfaces:**
- Consumes: `SesionFake(rol, permisos)`, `ArbolVisual.EsVisibleEnArbol`.
- Produces: cobertura de los 3 gates que hoy no tiene ninguna.

**El problema:** `MovimientoHistorialView.axaml` tiene 3 `IsVisible` gateados — `PuedeFiltrarPorProducto` (`:30`) y `PuedeRecalcularStock` (`:86` y `:92`) — y **cero tests de UI**. Ambas propiedades custodian bugfixes documentados de 2026-08-16 (ver los comentarios en `MovimientoHistorialViewModel.cs:82-93` y `:162-190`). La tanda 6 reescribe el `WrapPanel` de filtros donde vive el primero.

**Diferencia con los demás gates:** `PuedeFiltrarPorProducto` **no es una propiedad calculada** — es un campo que `CargarAsync` setea en `false` cuando `GET /productos` devuelve 403 (`:173`, `:186`, `:190`). El test tiene que provocar el 403 en el fake, no configurar un permiso.

- [ ] **Step 1: Anotar las tres fórmulas**

Antes de escribir un test, abrir `MovimientoHistorialViewModel.cs` y escribir acá, en el plan, la fórmula exacta de `PuedeFiltrarPorProducto` y `PuedeRecalcularStock`, y **cómo se las hace dar `false`** desde un fake. Sin eso no se puede diseñar la matriz.

- [ ] **Step 2: Escribir la matriz**

Por cada gate, dos casos como mínimo: condición presente → visible; condición ausente → oculto. Usar `ArbolVisual.EsVisibleEnArbol`, **no** `control.IsVisible` (los controles viven dentro de un `WrapPanel`/`Grid` que puede estar oculto).
Rol `Operador` con permisos explícitos. **Nunca Admin.**

- [ ] **Step 3: Ver el verde (la red se escribe sobre la vista sin tocar)**

Run: `--filter "FullyQualifiedName~MovimientoHistorialGates"`
Expected: PASS. La red se escribe contra el XAML **actual**, no contra el refactorizado — así, si el refactor rompe un gate, se ve.

- [ ] **Step 4: Validar por mutación**

Borrar cada uno de los 3 `IsVisible` del XAML, comprobar el rojo, revertir. Tres mutaciones, tres rojos.
**Esta es la mutación que importa:** borrar el `IsVisible` deja el control visible siempre — es exactamente el modo de falla que el refactor puede introducir con un typo (aunque con compiled bindings el typo sea error de build, borrarlo por accidente al mover el bloque no lo es).

---

### Task 6.3: P1 sobre `MovimientoHistorialView` + borrar su `DataGridCell.num` local

**Files:**
- Modify: `src/StockApp.Presentation/Views/Movimientos/MovimientoHistorialView.axaml`

**Interfaces:**
- Consumes: P1 (Task 6.1), la red de gates (Task 6.2), `Themes/DataGrid.axaml:97`.

**Tabla de sustitución:**

| Línea | Hoy | Pasa a |
|---|---|---|
| `:13` | `<DockPanel Margin="24">` | `Margin="{DynamicResource MargenVista}"` |
| `:15-18` | `TextBlock` "Historial de movimientos" `titulo-vista` | `<c:HeaderVista Eyebrow="MOVIMIENTOS" Titulo="Historial de movimientos">` |
| `:86`, `:92` | botón y bloque de "Recalcular stock", gateados | migran al slot `Acciones` **conservando su `IsVisible="{Binding PuedeRecalcularStock}"` textual, sin reescribirlo** |
| `:30` | `<StackPanel Spacing="4" Margin="0,0,12,8" IsVisible="{Binding PuedeFiltrarPorProducto}">` | se queda en la barra de filtros dentro de la card; solo cambia `Spacing="4"` → `{DynamicResource Espacio1}` |
| `:107-110` | ```<Style Selector="DataGridCell.num"><Setter Property="HorizontalContentAlignment" Value="Right" /></Style>``` dentro de `<DataGrid.Styles>` | **se borra.** Lo cubre `Themes/DataGrid.axaml:97` |
| `:128`, `:131` | `CellStyleClasses="num"` | **se quedan** — son lo que activa el estilo global |

**Por qué se puede borrar el estilo local:** `Themes/DataGrid.axaml:97` define `DataGridCell.num` con el mismo `HorizontalContentAlignment="Right"` y ya está cargado tanto en `App.axaml` como en `TestAppBuilder.cs` (la tanda 0 lo agregó al banco de pruebas). El estilo local es redundancia pura.

- [ ] **Step 1: Correr la red de gates (línea base verde)**
- [ ] **Step 2: Aplicar la tabla**
- [ ] **Step 3: Correr la red de gates otra vez**
  Expected: los 3 gates siguen en verde **sin tocar un solo assert**. Si hubo que tocar un assert, el refactor cambió comportamiento — parar y revisar.
- [ ] **Step 4: Verificar el borrado del estilo local por mutación inversa**
  Sacar el `StyleInclude` de `Themes/DataGrid.axaml` de `TestAppBuilder.cs` y comprobar que `DataGridEstiloRealTests` se pone rojo. Revertir. Eso prueba que la alineación derecha viene del tema global y no de otro lado.
- [ ] **Step 5: Guardián de patrón + suite completa**

---

### Task 6.4: P3 sobre `ProductoFormView` y `MovimientoFormControl` — con el seam intacto

**Files:**
- Modify: `src/StockApp.Presentation/Views/Catalogo/ProductoFormView.axaml`
- Modify: `src/StockApp.Presentation/Views/Movimientos/MovimientoFormControl.axaml`

**Interfaces:**
- Consumes: `c:CampoFormulario`, `c:HeaderVista`.
- Produces: la referencia de P3 para los otros 10 formularios.

**⚠ Esta es la task más delicada de B1.** `MovimientoFormControl` es el guardián del seam de validación: `MovimientoFormControlValidacionTests.cs` (2 tests, categoría "A puro") depende de que el `TextBox` de "Precio unitario" conserve el `(DataValidationErrors.ErrorConverter)` que `Controls.axaml:185` le pone por selector de tipo.

**Lo que ya sabemos del seam (ledger de Fase A, tanda 3):** envolver un `TextBox` en un `CampoFormulario` **no rompe el `ErrorConverter`** — el `ContentPresenter` pelado deja el control como hijo lógico y los `Setter` por selector de tipo lo siguen alcanzando. Esto se validó con una mutación real (`OnApplyTemplate` que hacía `SetValue(ErrorConverterProperty, null)`) que puso el test en rojo. **Pero se validó con un `CampoFormulario` montado sinteticamente, no dentro de esta vista.**

- [ ] **Step 1: Línea base del seam**

Run: `--filter "FullyQualifiedName~MovimientoFormControlValidacion"`
Expected: PASS (2/2). **Anotar el resultado.** Si no está verde antes de tocar nada, parar.

- [ ] **Step 2: Aplicar P3 a `ProductoFormView` (el de menor riesgo, sin tests)**

| Línea | Hoy | Pasa a |
|---|---|---|
| `:11` | `Padding="24" Margin="24"` | `Padding="{DynamicResource MargenVista}" Margin="{DynamicResource MargenVista}"` |
| `:12` | `<StackPanel Spacing="12">` | `Spacing="{DynamicResource Espacio3}"` |
| `:14-16` | `TextBlock {Binding Titulo}` `titulo-vista` | `<c:HeaderVista Titulo="{Binding Titulo}" />` |
| `:18-25` | `StackPanel Spacing="4"` + `TextBlock "SKU (obligatorio)"` + `TextBox` | `<c:CampoFormulario Etiqueta="SKU" Requerido="True"><TextBox .../></c:CampoFormulario>` (Ruling B-1) |
| ídem ×4 más | "Nombre (obligatorio)", "Código de barras", "Descripción", … | mismo movimiento, `Requerido` según diga o no "(obligatorio)" |

- [ ] **Step 3: Aplicar P3 a `MovimientoFormControl`, preservando `x:Name`**

**Regla dura:** el `x:Name="PrecioUnitarioBox"` (y cualquier otro `x:Name` del archivo) queda **en el `TextBox`**, nunca se mueve al `CampoFormulario`. `MovimientoFormControlValidacionTests.cs:55-57` busca `t.Name == "PrecioUnitarioBox"` sobre `window.GetVisualDescendants()`; el `TextBox` sigue en el mismo `NameScope` de la vista porque `CampoFormulario` lo proyecta por `ContentPresenter`, no por template propio (T7).

- [ ] **Step 4: Correr el seam otra vez**

Run: `--filter "FullyQualifiedName~MovimientoFormControl"`
Expected: PASS (5/5: 2 de validación + 3 de cultura), **sin tocar un solo assert**.

- [ ] **Step 5: Validar el seam por mutación DENTRO de esta vista**

Poner temporalmente en `MovimientoFormControl.axaml`, sobre el `TextBox` de precio, `(DataValidationErrors.ErrorConverter)="{x:Null}"` (valor local, gana precedencia). Comprobar que `MovimientoFormControlValidacionTests` se pone rojo. Revertir.
**Nota del ledger:** la mutación que el plan de Fase A sugería (ponerla en el `ContentPresenter` del `ControlTheme`) **no funciona** — el combinador `/template/` no alcanza contenido proyectado. Usar la mutación de valor local, que sí es representativa.

- [ ] **Step 6: Suite completa**

---

### Task 6.5: P5 sobre `EntradaRegistroView` y `SalidaRegistroView`

**Files:**
- Modify: los dos `.axaml` (13 líneas cada uno)

**Decisión:** **no** llevan `HeaderVista` propio. Renderizan a `MovimientoFormControl`, que en la Task 6.4 ya ganó uno atado a `{Binding Titulo}` — y `EntradaRegistroViewModel`/`SalidaRegistroViewModel` ya proveen títulos distintos. Ponerles uno propio duplicaría el título en pantalla.

**Cambio real:** ninguno en el XAML. **Verificación**, no modificación: montar ambas y comprobar que el `HeaderVista` heredado muestra el título correcto para cada una.

- [ ] **Step 1: Agregar las dos filas al `GuardianDePatronTests`** con los títulos esperados de cada VM.
- [ ] **Step 2: Correr.** Expected: PASS sin tocar los `.axaml`. Si falla, el `HeaderVista` de `MovimientoFormControl` no está tomando el `Titulo` del VM — bug real que hay que arreglar en 6.4.

**Nota sobre T6:** ambos code-behind ya enganchan `DataContextChanged` (`EntradaRegistroView.axaml.cs`, `SalidaRegistroView.axaml.cs`). No tocarlos.

---

### Task 6.6: P0 sobre `InicioView` — dashboard con `TarjetaMetrica`

**Files:**
- Modify: `src/StockApp.Presentation/Views/InicioView.axaml` (317 líneas)

**⚠ 15 tests dependen de esta vista** (`InicioViewTests` 8 + `InicioPanelTareasTests` 7), y localizan por `x:Name`.

**`x:Name` INTOCABLES** (verificados en el archivo): `BorderPanelTareas` (`:69`), `BorderAvisoBackupProblema` (`:141`), `BorderAvisoBackupDesconocido` (`:158`), `BorderAccesosRapidos` (`:174`), `BotonAccesoProductos` (`:183`), `BotonAccesoRegistrarEntrada` (`:205`), `BotonAccesoRegistrarSalida` (`:227`), `BotonAccesoHistorialMovimientos` (`:249`), y los dos de reportes (`:268`, `:288`).

**Gates a preservar textualmente** (`:175`, `:185`, `:206`, `:227`, `:248`, `:268`, `:288`): `PuedeVerAccesosRapidos`, `PuedeGestionarProductos`, `PuedeRegistrarEntradaSalida` (×2), `PuedeRegistrarMovimientos`, `PuedeVerReportes` (×2).

**Cambios:**

| Línea | Hoy | Pasa a |
|---|---|---|
| `:25-35` | `Border.card` con `titulo-vista` + 2 `TextBlock FontSize="14"` | `<c:HeaderVista Eyebrow="INICIO" Titulo="..." Resumen="..." />` — sale de la card, pasa a ser header de vista |
| `:54`, `:77`, `:146`, `:163`, `:179` | `TextBlock Classes="seccion"` como título de card | **se quedan.** Son títulos de sección dentro de la vista, no el título de la vista (T5: no meter `HeaderVista` acá) |
| `:199`, `:221`, `:243`, `:265`, `:286`, `:307` | `FontSize="12"` literal | `Classes="caption"` |
| 13 `Opacity` | literales | `Foreground="{DynamicResource TextoTerciarioBrush}"` |
| **nuevo** | — | fila de `TarjetaMetrica` sobre las cards de aviso, si el `InicioViewModel` ya expone las cifras. **Si no las expone, NO se agrega el VM en esta tanda** — se anota como deuda y se cierra en B3 |

**Regla:** esta task **no toca el ViewModel**. Si `TarjetaMetrica` no tiene datos que mostrar sin cambiar el VM, se omite y se documenta. Meter cambios de VM en una tanda de barrido visual mezcla dos riesgos distintos.

- [ ] **Step 1: Línea base** — `--filter "FullyQualifiedName~InicioView|FullyQualifiedName~InicioPanelTareas"` → PASS 15/15. Anotar.
- [ ] **Step 2: Aplicar la tabla, preservando los 10 `x:Name` y los 7 gates.**
- [ ] **Step 3: Correr los 15** → PASS **sin tocar un assert.** Si alguno pide tocarse, es porque el refactor movió un `x:Name` o cambió un `Content`. Volver atrás.
- [ ] **Step 4: Mutación** — borrar `IsVisible="{Binding PuedeVerAccesosRapidos}"` de `:175` → debe ponerse rojo algún test de `InicioViewTests`. Si **no** se pone rojo, ese gate no tiene guardián y hay que escribirlo antes de seguir. Revertir.
- [ ] **Step 5: Suite completa.**

---

### Task 6.7: P1 compuesto sobre `IngresoPorFacturaView`

**Files:**
- Modify: `src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml` (262 líneas)

**⚠ 14 tests** (`IngresoPorFacturaViewTests` 11 + `IngresoPorFacturaLocaleDecimalTests` 3). Esta vista es el **modelo de localización estable** que la spec cita: sus helpers `BotonPorCommand` (`:115`), `ComboPorItemsSource` (`:122`) e identidad por `DataContext` (`:172`) son lo que el resto de la suite debería imitar. **Localizan por identidad, no por texto** → sobreviven al refactor.

Va última en la tanda porque es la que más superficie tiene: 1 grilla, 8 bindings condicionales, formulario + listado en la misma pantalla.

| Línea | Hoy | Pasa a |
|---|---|---|
| header | `titulo-vista` (4 ocurrencias en el archivo) | el de vista → `c:HeaderVista`; los otros 3 son títulos de sub-bloque → `Classes="seccion"` |
| `:178` | `Foreground="Red"` | `{DynamicResource DangerBrush}` |
| `:87`, `:125` | `IsVisible="{Binding EsCredito}"`, `{Binding EsProductoNuevo}` | **intocables** — son lógica de dominio, no gates de permiso |
| 12 `Margin` literales | varios valores | tokens |

- [ ] **Step 1-5:** mismo ciclo: línea base 14/14 → aplicar → 14/14 sin tocar asserts → mutación de un `IsVisible` → suite completa.

---

### Task 6.8: Cierre de la tanda 6

- [ ] **Step 1: Suite completa** — `dotnet test StockApp.sln` (timeout 600000). Expected: PASS.
- [ ] **Step 2: Grep de residuos en las 8 vistas**
```bash
grep -n 'Margin="24"\|Opacity="0\.\|Foreground="Red"\|titulo-vista\|badge-inactiva\|FontSize="1' \
  src/StockApp.Presentation/Views/InicioView.axaml \
  src/StockApp.Presentation/Views/Catalogo/Producto*View.axaml \
  src/StockApp.Presentation/Views/Movimientos/*.axaml
```
Expected: cero coincidencias, salvo las excepciones documentadas (los `Opacity` vía converter de `ProductoListView`).
- [ ] **Step 3: Verificación orgánica.** La app real, corriendo. Toolkit en `scripts/gui-verificacion/`. Recorrer las 8 pantallas. Un test verde no dice si se ve bien.
- [ ] **Step 4: Commit.**

```
feat(ui): aplica el sistema de diseno a las 8 vistas de operacion

Define el catalogo de patrones P0-P7 y lo prueba contra el nucleo operativo:
Inicio, Productos (list+form), los 5 de Movimientos.

- HeaderVista reemplaza el TextBlock titulo-vista suelto en 6 vistas
- CampoFormulario absorbe los pares label+control de los 2 formularios,
  conservando el seam de DataValidationErrors.ErrorConverter (validado por
  mutacion de valor local dentro de MovimientoFormControl, no con la mutacion
  del ControlTheme que el plan de Fase A sugeria y que es estructuralmente
  imposible con ContentPresenter)
- BadgeEstado reemplaza el par Border.badge-inactiva + TextBlock
- MovimientoHistorialView pierde su DataGridCell.num local: lo cubre
  Themes/DataGrid.axaml:97 desde la tanda 2 (1 de 6 copias, no de 7 como
  decia la spec)
- Red de gates nueva para MovimientoHistorialView: 3 IsVisible que custodian
  dos bugfixes de 2026-08-16 y no tenian un solo test de UI
- GuardianDePatronTests: un [AvaloniaTheory] con una fila por vista, que las
  tandas 7-12 amplian. 42 de las 55 vistas no tienen tests propios y no se
  les va a escribir uno a cada una: el compilador cubre los bindings
  (AvaloniaUseCompiledBindingsByDefault) y este guardian cubre lo que el
  compilador no ve

Los 15 tests de Inicio y los 14 de IngresoPorFactura pasan sin tocar un solo
assert: localizan por x:Name y por identidad de Command, no por texto.
```

---

## Tanda 7: Maestros

**Objetivo:** cerrar los 6 maestros de catálogo. Es la tanda más mecánica de todo el refactor: las 3 vistas de listado son **estructuralmente idénticas entre sí** y los 3 formularios también.

**Vistas (6):** `CategoriaListView` + `CategoriaFormView`, `ProveedorListView` + `ProveedorFormView`, `UnidadMedidaListView` + `UnidadMedidaFormView`.

**Riesgo: BAJO.** Cero tests de UI sobre estas 6. Cero gates de permiso. Cero `DataGrid`. El compilador cubre los bindings.

**Por qué van juntas y separadas de Finanzas:** los 3 maestros de Finanzas (`FuenteFinanciamientoListView`, `RubroGastoListView`, `LineaPoaListView`) son P2 igual que estos — mismo XAML casi carácter por carácter — **pero están embebidos como pestañas de `MaestrosFinanzasView`**, así que no llevan `HeaderVista` y su commit tiene que ir con el resto de Finanzas para que la verificación orgánica del módulo tenga sentido. La afinidad de patrón manda en la *receta*; el módulo manda en el *commit*.

### Task 7.1: P2 sobre los 3 listados

**Files:** los 3 `*ListView.axaml`.

Las 3 comparten esta tabla exacta (los números de línea son idénticos ±3 entre archivos):

| Hoy | Pasa a |
|---|---|
| `<DockPanel Margin="24">` | `Margin="{DynamicResource MargenVista}"` |
| `TextBlock` `titulo-vista` + `Margin="0,0,0,16"` | `<c:HeaderVista Eyebrow="CATÁLOGO" Titulo="…">` con los 3 botones adentro |
| `<StackPanel ... Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">` con Nuevo/Editar/Dar de baja | migra al slot `Acciones`; `Spacing="{DynamicResource Espacio2}"` |
| `<StackPanel Orientation="Horizontal" Spacing="16" Margin="4">` del `ItemTemplate` | `Spacing="{DynamicResource Espacio4}"`, `Margin="{DynamicResource Espacio1}"` → **NO** (T1: `Margin` necesita `Thickness`). Dejar `Margin="4"` literal o usar un token nuevo |
| `<Border Classes="badge-inactiva"><TextBlock Classes="badge-inactiva-texto"/></Border>` | `<c:BadgeEstado Texto="Inactiva" Tono="Neutro" IsVisible="{Binding !Activo}" />` |
| `Opacity="{Binding Activo, Converter=...}"` | **se queda** (semántica de dominio) |

Eyebrows: `CATÁLOGO` para las tres.

- [ ] Steps: rojo del guardián (3 filas nuevas) → aplicar a las 3 → verde → mutación de título en una de las tres → suite.

### Task 7.2: P3 sobre los 3 formularios

**Files:** los 3 `*FormView.axaml`.

Mismo movimiento que la Task 6.4, sin el riesgo del seam (ninguno de los tres tiene tests). `CategoriaFormView` (37 líneas) tiene **un solo campo** — es el caso más chico de P3 y sirve de sanity check del patrón.

`ProveedorFormView` y `UnidadMedidaFormView` no tienen `Foreground="Red"`; usan `{DynamicResource DangerBrush}` ya (verificado). No hay nada que corregir ahí.

### Task 7.3: Cierre de la tanda 7

- [ ] Suite completa → grep de residuos sobre `Views/Catalogo/` → verificación orgánica de los 3 maestros → commit.

**Al cerrar la tanda 7 se cierra B1.** El catálogo de patrones está probado sobre 14 vistas: 1 dashboard, 3 listados-grilla, 3 listados-ListBox, 5 formularios, 2 wrappers. Todo lo que viene es aplicación.

---

# SUB-FASE B2 — El volumen (esbozo)

> **Se detalla al cerrar B1**, con los patrones ya validados contra el compilador y la suite. Lo que sigue fija alcance, asignación y riesgos, no los pasos.

## Tanda 8: Finanzas (19 vistas, no 16)

La spec dice 16. Conté los archivos de `Views/Finanzas/`: son **19**. La tanda más grande del refactor por un margen enorme (la que sigue tiene 6).

**Estructura propuesta: 4 tasks por patrón, 4 commits, no uno.** La spec dice "va sola"; interpreto que significa "no se mezcla con otro módulo", no "un solo commit de 19 archivos". Un commit de 19 vistas es irrevisable e irreverteable.

| Task | Patrón | Vistas | Riesgo |
|---|---|---|---|
| 8.1 | P2 maestros embebidos (**sin `HeaderVista`**) + P4 contenedores | `FuenteFinanciamientoListView`, `RubroGastoListView`, `LineaPoaListView`, `MaestrosFinanzasView`, `ImportacionView` | Bajo |
| 8.2 | P3 formularios | `FuenteFinanciamientoFormView`, `RubroGastoFormView`, `LineaPoaFormView`, `GastoFormView`, `IngresoFormView` | Bajo-medio (4 de los 10 `Foreground="Red"` están acá) |
| 8.3 | P1 listados-grilla | `GastosView`, `IngresosView`, `ControlPoaView`, `LibroCajaView`, `CalendarioPagosView`, `HistorialImportacionesView`, `PagosGastoView` | **Medio-alto** — `LibroCajaView` tiene 5 grillas; `GastosView`/`IngresosView`/`PagosGastoView` tienen 13 tests con gates ya cubiertos que hay que preservar |
| 8.4 | Especial | `NuevaImportacionView` (509 líneas) + `AdjuntosPanelView` (P5) | **CRÍTICO** |

**Riesgos de la tanda 8:**
- `NuevaImportacionView`: 509 líneas, **4 grillas**, 31 bindings condicionales, 10 `FontSize` literales, 10 `Opacity`, y **dos `<Style Selector="DataGridRow" x:CompileBindings="False">`** (`:20`, `:31`) — bindings sin red del compilador. Custodiada por 10 tests en 3 archivos. **Es la única vista de la app sin margen exterior** (la spec la señala). Va sola en su propia task, al final.
- `LibroCajaView`: 5 `DataGrid` en 116 líneas, 9 bindings condicionales, `SignoNegativoBrushConverter` en `:29` y `:68`.
- **Deuda a cerrar acá:** migrar los `CurrentSessionFake` privados de `GastosViewTests.cs:51`, `IngresosViewTests.cs:47` y `PagosGastoViewTests.cs:49` a `SesionFake`. Los tres tienen `EstablecerPermisos` **no-op** — el bug que el Ruling 6 de la Fase A arregló en `SesionFake` y que estos tres siguen teniendo. Sin migrar, no se puede testear revocación de permiso en caliente en Finanzas.

## Tanda 9: Documentos y Tareas (5 vistas)

**Vistas:** `DocumentoListView` (P1), `DocumentoFormView` (P3), `AdjuntosDocumentoPanelView` (P5), `TareaListView` (P1), `TareaFormView` (P3).

**⚠ La tanda arranca cerrando la deuda de la Fase A (Ruling B-3).** Las Tasks 4.3 y 4.4 nunca se ejecutaron. `DocumentoFormViewGatesTests.cs` no existe. **No se toca una línea de XAML de Documentos antes de que la red esté verde y validada por mutación.**

**La matriz correcta (Ruling B-5).** El brief pide "permisos mixtos"; las fórmulas reales no consultan permisos. La matriz es rol × estado:

| Gate | Caso visible | Caso oculto A | Caso oculto B |
|---|---|---|---|
| `PuedeIniciar` | Operador + `Pendiente` | Operador + `Finalizado` | — (no depende de rol) |
| `PuedeVolverAPendiente` | Operador + `EnProceso` | Operador + `Pendiente` | — |
| `PuedeFinalizar` | Operador + `EnProceso` | Operador + `Anulado` | — |
| `PuedeAnular` | **Admin** + `Pendiente` | **Operador** + `Pendiente` ← *el caso que hoy no existe* | Admin + `Anulado` |
| `PuedeReabrir` | **Admin** + `Finalizado` | **Operador** + `Finalizado` ← *ídem* | Admin + `Pendiente` |

Los tres montajes actuales con `rol: RolUsuario.Admin` (`DocumentoListViewTests.cs:44` por default, `:157` explícito) **no se borran**: verifican navegación y filtros, no gates. La regla de la Fase A sigue en pie — "no borres un test que verifica comportamiento solo porque monta con Admin".

`DocumentoFormView` suma un gate de **habilitación**, no de visibilidad: `IsEnabled="{Binding PuedeEditarCampos}"` en `:32`, `:36`, `:40`, `:45`, `:50`. `ArbolVisual.EsVisibleEnArbol` **no lo detecta** — se verifica con `control.IsEnabled`. Y `PuedeEditarCampos => EsNuevoDocumento || PuedeEditar` es un OR: hay que cubrir las dos ramas por separado o el test da verde por la equivocada.

Mutaciones: 5 gates de `DocumentoListView` (`:67`, `:71`, `:75`, `:84`, `:164` — **`:164`, no `:165` como decía el plan de Fase A**), 5 de `DocumentoFormView` (`:53`, `:63-67`), 1 de `IsEnabled`. **Once mutaciones, once rojos.**

`TareaListView` tiene 4 gates por fila (`PuedeTomar`, `PuedeCancelar` ×2, `PuedeSoltar`, `PuedeTerminar`) **ya cubiertos** por `TareaListViewTests` (10 tests). Preservar.

**Deuda de la tanda 9:** `Foreground="Red"` en `DocumentoFormView:105` y `TareaFormView:94`.

## Tanda 10: Reportes (5 vistas)

**Vistas:** `ValorizacionView`, `StockCategoriaView`, `HistorialPorProductoView`, `MasMovidosView`, `AuditoriaLogView`. Las 5 son P1 puro y **son casi el mismo archivo**.

**Riesgo: BAJO.** Cero tests de UI, cero gates de permiso, cero `x:Name`. Es la tanda ideal para hacer en un solo movimiento.

**La deuda que el brief llama "los 7 `DataGridCell.num` duplicados": son 6, y 1 ya se cerró en la tanda 6.** Acá se borran los 5 restantes:

| Archivo | Línea | Contenido |
|---|---|---|
| `Views/Reportes/StockCategoriaView.axaml` | `:36` | `<Style Selector="DataGridCell.num">` con `HorizontalContentAlignment="Right"` |
| `Views/Reportes/ValorizacionView.axaml` | `:37` | ídem |
| `Views/Reportes/AuditoriaLogView.axaml` | `:60` | ídem |
| `Views/Reportes/HistorialPorProductoView.axaml` | `:60` | ídem |
| `Views/Reportes/MasMovidosView.axaml` | `:60` | ídem |

(El 6º, `Views/Movimientos/MovimientoHistorialView.axaml:108`, lo cierra la Task 6.3.)

Los `CellStyleClasses="num"` de las columnas **se quedan** — son lo que engancha con `Themes/DataGrid.axaml:97`.

**Verificación del borrado:** un test que monte las 5 vistas con datos y assertee `HorizontalContentAlignment == Right` en las celdas `.num`, más la mutación inversa (sacar el `StyleInclude` de `DataGrid.axaml` de `TestAppBuilder.cs` → rojo).

**Nota `tnum` (Ruling B-4):** no se resuelve acá. La alineación a la derecha ya está; las cifras tabulares quedan como deuda declarada.

**Los 5 usan `SignoNegativoBrushConverter`** (`StockCategoriaView:58`, `ValorizacionView:67`, `HistorialPorProductoView:78`/`:87`). Si la pregunta abierta sobre badges se resuelve a favor de `BadgeEstado`, esta tanda es donde más impacta.

---

# SUB-FASE B3 — Los bordes y la limpieza (esbozo)

## Tanda 11: Administración y acceso (6 vistas)

`MantenimientoView` (P1'), `UsuariosAdminView` (P1'), `LoginView` (P6), `ResetAdminView` (P6), `BloqueoLicenciaView` (P6), `AccesoLimitadoView` (P4).

**Riesgo: ALTO, concentrado en `MantenimientoView`.** 229 líneas, **17 tests**, y **4 asserts geométricos con `TranslatePoint`**: `:181`, `:391`, `:392`, `:406-407`. La spec mencionaba uno (`:379`); son cuatro, en dos grupos:
- `:391-392` compara el **orden vertical** de "Logs" vs "Guardar" — se rompe si el `HeaderVista` reordena el `DockPanel`.
- `:406-407` compara el origen de dos tarjetas — se rompe si se cambia el layout de tarjetas.

La Fase A ya dictaminó: **no se borran** (custodian una regresión real de `DockPanel`/`LastChildFill`, validada por mutación por su autor). Se **adaptan**, conservando el criterio geométrico. Es la única task del refactor donde está permitido tocar asserts, y solo estos cuatro.

**`UsuariosAdminView:90`:** `BorderBrush="Gray"` + radio 4 fuera de escala (señalado por la spec). Más `Foreground="Red"` en `:143` y `badge-inactiva` en `:51-52`.

**P6 (`LoginView`, `ResetAdminView`, `BloqueoLicenciaView`):** son pantallas centradas sin sidebar. `Espacio7` (48) como respiro. `LoginView` ya fue tocada en el uplift de 2026-07-04 (es una de las 3 vistas que sí adoptaron tokens entonces) — verificar antes de reescribir.

**`AccesoLimitadoView`:** 28 líneas, sin título hoy. Es la barrera física del modo licencia-vencida — el comentario del archivo (`:14-17`) advierte que **a propósito** no tiene sidebar ni `ContentControl` genérico. **No agregar navegación de ningún tipo.** Solo gana `HeaderVista`. Custodiada por `AccesoLimitadoViewTests` + 2 casos de `ViewLocatorTests`.

## Tanda 12: Actualizaciones y diálogos (6 vistas)

**`Actualizaciones/Views/` (3):** `ActualizacionBannerView`, `ActualizacionModalView`, `ActualizacionBloqueoView`. **El único módulo 100% fuera del sistema de diseño.** Paleta Material vieja, 9 sitios:

| Archivo | Colores a reemplazar | Token destino |
|---|---|---|
| `ActualizacionBannerView:11,31` | `#E8F4FD`, `#2196F3` | `InfoBrush` + fondo suave |
| `ActualizacionModalView:11,18,30` | `#FFFDF0`, `#FF9800`, `#E65100` | `WarningBrush` |
| `ActualizacionBloqueoView:16,23,31,40` | `#FFEBEE`, `#F44336`, `#B71C1C` | `DangerBrush`/`DangerPressedBrush` |

Riesgo bajo (cero tests) pero **hay un hueco**: no hay `Fondo*Suave` para Info/Warning/Danger en `Tokens.axaml` — solo existe `BrandSuaveBrush`. Puede hacer falta agregar 3 tokens. **Decidir en la task, no inventarlos ahora.**

**`Views/Dialogs/` (3):** `ConfirmacionDialog`, `MensajeDialog`, `PedirTextoDialog`. **Las 3 vistas sin `x:DataType` de toda la app** — son `Window` con code-behind puro, sin red del compilador. Solo tokenización de `Padding`/`Spacing`/`CornerRadius`; **no** llevan `HeaderVista` (tienen `Title` de ventana). `SombraModal` es el token que les corresponde.
**Intocables:** los `x:Name` (`MensajeText`, `CancelarButton`, `ConfirmarButton`, …) y los `Content` de botón — `ConfirmacionServiceDialogosConsecutivosTests` depende de ellos.

## Tanda 13: Limpieza

- [ ] **Borrar `Views/MainWindowView.axaml` + `.axaml.cs` + `ViewModels/MainWindowViewModel.cs`.** Verificado: **cero referencias** fuera de sí mismos; `ViewLocatorTests` no enumera ViewModels, es específico. Es la única vista con `FontSize="28"`. **Antes de borrar, re-correr el grep** — puede haber aparecido un uso durante las tandas 6-12.
- [ ] **Tokenizar `Views/MainWindow.axaml`** (29 líneas, la `Window` host). Mínimo: verificar que no queda nada literal.
- [ ] **Borrar `Border.badge-inactiva` y `TextBlock.badge-inactiva-texto` de `Themes/Controls.axaml:234,242`,** una vez que las 10 vistas migraron a `c:BadgeEstado`. Verificar con grep que no queda ningún uso antes de borrar.
- [ ] **`AdjuntosPanelView` vs `AdjuntosDocumentoPanelView`: RECOMENDACIÓN — descopar la unificación.**

  La spec dice "unificar los gemelos de 63 y 62 líneas". Los diffeé: **no son gemelos**. Bindean a **dos tipos de ViewModel distintos** (`AdjuntosPanelViewModel` en `ViewModels.Finanzas`, `AdjuntosDocumentoPanelViewModel` en `ViewModels.Documentos`), con **APIs distintas** (`PuedeAgregar` + `PuedeQuitar` vs un único `PuedeModificar`) y DTOs de namespaces distintos.

  Unificarlos exige (a) reconciliar dos contratos de ViewModel y (b) renombrar una View — y **"renombrar o mover Views" está explícitamente fuera de alcance** en la sección 5 de la spec, custodiado por `ViewLocatorTests` (que tiene 2 casos dedicados a `AdjuntosPanelView` justamente porque este panel ya se creó una vez sin su View y hubo regresión).

  **La tanda 13 de la spec se contradice con la sección 5 de la spec.** Recomendación: en esta tanda, **solo armonización visual** (que los dos se vean idénticos), y abrir la unificación real como trabajo aparte con su propia decisión de alcance. **Requiere OK del usuario.**

- [ ] **Verificación orgánica final** de la app entera, con la app real corriendo.
- [ ] **Auditoría final de residuos** sobre las 58 vistas:
```bash
grep -rn 'Opacity="0\.\|Foreground="Red"\|Margin="24"\|Margin="16"\|Margin="40"\|FontSize="\|#[0-9A-Fa-f]\{6\}' \
  src/StockApp.Presentation/Views src/StockApp.Presentation/Actualizaciones
```
Expected: solo las excepciones documentadas (`Opacity` vía converter, hex dentro de `Tokens.axaml`).

---

## Criterios de aceptación de la Fase B completa

1. **Las 58 vistas tienen margen exterior `MargenVista`** o una excepción documentada (paneles embebidos, diálogos).
2. **Las 15 vistas sin título tienen `HeaderVista`**, salvo las 4 excepciones argumentadas (los 2 wrappers de Movimientos que heredan el de `MovimientoFormControl`, y los 3 diálogos que usan `Window.Title`) — que quedan en 15 − 2 wrappers − 3 diálogos − `MainWindow` − `MainWindowView` (borrada) = **8 ganan `HeaderVista` propio**.
3. **Cero `Opacity` literal decorativa.** Los usos vía `ActivoOpacidadConverter` se conservan y se documentan.
4. **Cero `Foreground="Red"`.** Los 10 pasan a `DangerBrush`.
5. **Cero `DataGridCell.num` local.** Los 6 se apoyan en `Themes/DataGrid.axaml:97`.
6. **Cero bloques de navegación duplicados.** Ya cumplido en la tanda 5 — verificado: `ShellMainView.axaml` 131 líneas, `ItemsControl`, y ninguna otra vista tiene botones de nav.
7. **Los 10 gates de Documentos tienen red validada por mutación**, con la matriz rol × estado.
8. **`dotnet test StockApp.sln` verde** al cierre de cada tanda.
9. **Verificación orgánica** al cierre de las tandas 8 y 13 (la spec pide 5, 8 y 13; la 5 ya se hizo).

---

## Notas finales para el agente que ejecute

- **Antes de la tanda 6, pedile al usuario el OK sobre el Ruling B-1** ("(obligatorio)" → asterisco). Sin eso, los 12 formularios quedan bloqueados o hay que rehacerlos.
- **El ledger de la Fase B** debería vivir en `.superpowers/sdd/2026-08-19-ui-refactor-dashboard-fase-b/progress.md`, siguiendo el formato del de Fase A (pre-flight scan de conflictos + rulings numerados + progreso por task).
- **Lo que no pude verificar** y queda pendiente: (a) si `InicioViewModel` ya expone cifras aptas para `TarjetaMetrica` — no leí el ViewModel completo; (b) si `LoginView` ya adoptó tokens en el uplift de julio — vi que tiene 3 `Opacity` y 1 `FontSize`, así que probablemente solo parcialmente; (c) el conteo exacto de tests que agrega cada tanda; (d) si hacen falta tokens `Fondo*Suave` para Info/Warning en la tanda 12 — no busqué exhaustivamente en `Tokens.axaml` si hay equivalentes con otro nombre.

### Critical Files for Implementation

- `/home/capua25/workspace/stockapp/docs/superpowers/specs/2026-08-18-ui-refactor-dashboard-design.md`
- `/home/capua25/workspace/stockapp/docs/superpowers/plans/2026-08-18-ui-refactor-dashboard-fase-a.md`
- `/home/capua25/workspace/stockapp/.superpowers/sdd/2026-08-18-ui-refactor-dashboard-fase-a/progress.md`
- `/home/capua25/workspace/stockapp/src/StockApp.Presentation/Controls/Componentes.axaml`
- `/home/capua25/workspace/stockapp/src/StockApp.Presentation/Themes/Tokens.axaml`
- `/home/capua25/workspace/stockapp/tests/StockApp.Presentation.UiTests/TestAppBuilder.cs`