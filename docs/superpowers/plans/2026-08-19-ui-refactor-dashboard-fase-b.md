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
- **B2 = tandas 8 + 9 + 10 (29 vistas). DETALLADA el 2026-08-19, al cerrar B1** (14 tasks, 14 commits). Se escribió después de B1 a propósito, por el motivo que la Fase A dio para no planificar la B por adelantado: planificar contra un molde que no existe garantiza reescribir. El barrido de verificación contra el código encontró **once** afirmaciones falsas en el esbozo previo (sección 0 de B2).
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
| **B2** | 8, 9, 10 | 29 | **Detalle completo** (2026-08-19). 14 tasks, 14 commits. Amplía el catálogo con P3-b, P*-emb y P8 |
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

**Ruling B-13 — Tanda 13 / unificación de Adjuntos: se descopa. Solo armonización visual, sin fusionar ViewModels ni renombrar Views.** `AdjuntosPanelView` y `AdjuntosDocumentoPanelView` no son gemelos cosméticos: bindean a dos tipos de ViewModel distintos con APIs distintas (`AdjuntosPanelViewModel`, `ViewModels.Finanzas`, con `PuedeAgregar`+`PuedeQuitar`; `AdjuntosDocumentoPanelViewModel`, `ViewModels.Documentos`, con `PuedeModificar`), sobre DTOs de namespaces distintos.
*Por qué:* unificarlos exige reconciliar dos contratos de ViewModel y renombrar una View — y "renombrar o mover Views" está explícitamente fuera de alcance en la sección 5 de la spec, custodiado por `ViewLocatorTests`. La Task 13 de la spec se contradecía a sí misma acá (pedía unificar pero también prohibía renombrar/mover Views); este ruling resuelve la contradicción a favor de la sección 5. En la tanda 13 se hace únicamente que los dos paneles se vean idénticos (tokens, espaciado, estilo); la fusión real de los ViewModels queda como trabajo aparte, sin fecha.
*Costo si me equivoco:* ninguno inmediato — no se pierde nada al descopar, la fusión sigue disponible como iniciativa futura si se decide abrirla.
**APROBADO por el usuario el 2026-08-19.** Ya no requiere OK — la tanda 13 puede ejecutarse solo con armonización visual.

---

## Catálogo de patrones (P0-P7)

Se define en la Task 6.0. Cada task posterior referencia un patrón en vez de repetir la receta.

> **Ampliado por B2 (2026-08-19).** Al abrir las 29 vistas de B2 aparecieron tres formas que este
> catálogo no cubría: **P3-b** (formulario de página, distinto de la tarjeta centrada de Catálogo),
> **P2-emb / P1-emb** (vista embebida en un `TabControl`: sin `HeaderVista` y sin `MargenVista`) y
> **P8** (wizard de pasos mutuamente excluyentes). Están definidas en la **sección 1 de la sub-fase
> B2**; no se repiten acá.

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
Vistas **P3-a** (tarjeta centrada, las 5 de B1): `CategoriaFormView`, `ProductoFormView`, `ProveedorFormView`, `UnidadMedidaFormView`, `MovimientoFormControl`.
Vistas **P3-b** (formulario de página, las 7 de B2 — ver sección 1 de B2): `GastoFormView`, `IngresoFormView`, `LineaPoaFormView`, `RubroGastoFormView`, `FuenteFinanciamientoFormView`, `DocumentoFormView`, `TareaFormView`. **No son el mismo patrón**: su `titulo-vista` está fuera de la card y su card no está centrada.

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

# SUB-FASE B2 — El volumen (29 vistas)

> **Escrita el 2026-08-19, al cerrar B1.** El esbozo anterior fijaba alcance y riesgos; esto fija
> los pasos. **Todo número de línea de esta sub-fase se verificó abriendo el archivo** en el árbol
> de trabajo con `d92b4fb` (cierre de la tanda 7) como último commit. Donde no pude verificar algo,
> está marcado explícitamente como **NO VERIFICADO** — no hay ningún número inventado.

**Línea base al arrancar B2:** 3176 tests verdes (cierre de la tanda 7, commit `d92b4fb`).

---

## 0. Correcciones al esbozo (lo que el barrido de verificación encontró)

El esbozo de B2 se escribió sin abrir los 29 archivos. Al abrirlos aparecieron **once** afirmaciones
falsas o incompletas. Se corrigen acá para que quien ejecute no trabaje contra datos viejos.

| # | Lo que decía el esbozo | Lo verificado en el código | Impacto |
|---|---|---|---|
| C1 | `DataGridCell.num` local en `AuditoriaLogView:60` y `HistorialPorProductoView:60` | están en **`:78`** las dos (`grep -rn 'Selector="DataGridCell.num"' Views/`) | tabla de la tanda 10 corregida |
| C2 | `SignoNegativoBrushConverter` en **11 sitios / 8 vistas** | **12 sitios / 8 vistas** (grep completo abajo) | la task transversal migra 12, no 11 |
| C3 | "`NuevaImportacionView` es la única vista sin margen exterior" | **tiene** `<Grid Margin="24">` en `:37` — y como se renderiza dentro del `TabControl` de `ImportacionView`, que ya trae `Margin="24"` en `:11`, el margen está **duplicado**. Lo mismo `HistorialImportacionesView:12` | es el defecto inverso al que decía el plan; ver **Ruling B-17** |
| C4 | "dos `<Style Selector="DataGridRow" x:CompileBindings="False">`" | **6 ocurrencias** de `x:CompileBindings="False"`: 2 en `<Style>` (`:20`, `:31`) **y 4 en `DataGridTemplateColumn`** (`:106`, `:184`, `:212`, `:353`) | 4 columnas enteras sin red del compilador que el plan no contaba |
| C5 | "los `CurrentSessionFake` privados con `EstablecerPermisos` no-op son **3** archivos" | **6**: `GastosViewTests.cs:51`, `IngresosViewTests.cs:47`, `PagosGastoViewTests.cs:49`, `InicioViewTests.cs:55`, `InicioPanelTareasTests.cs:51`, `TareaFakes.cs:118` (`TareaSessionFake`, compartido por Documentos **y** Tareas) | ver **Ruling B-19** |
| C6 | `badge-inactiva` / `badge-inactiva-texto` en `Controls.axaml:234,242` | están en **`:258`** y **`:266`** | la tanda 13 (B3) borra esas líneas, no las 234/242 |
| C7 | "P3 formulario: `Border.card` centrado `MaxWidth=380`" para los 12 formularios | los formularios de **Finanzas, Documentos y Tareas** NO son ese patrón: son `DockPanel Margin="24"` → `titulo-vista` arriba → `Border.card VerticalAlignment="Top"` con `MaxWidth` 420/480/560/620/680 y `HorizontalAlignment="Left"` | patrón nuevo **P3-b**, ver sección 1 |
| C8 | los maestros embebidos de Finanzas "no llevan `HeaderVista`" (correcto) | además su raíz es `<Border Classes="card" Margin="0,12,0,0">`, **no** `Margin="24"` → `Vista_TieneMargenExteriorEstandar` daría rojo por una razón estructural | ver **Ruling B-17** |
| C9 | nada sobre `Opacity` dentro de `ItemTemplate` | **30 `Opacity` literales en B2, de los cuales 25 son invisibles para el guardián** (Ruling B-15) | ver **Ruling B-16**, es el riesgo #1 de B2 |
| C10 | nada sobre `x:Name="Root"` | `NuevaImportacionView:13` declara `x:Name="Root"` y **5 bindings dependen de él** (`{Binding #Root.DataContext.…}` en `:124`, `:198`, `:234`, `:367`, `:435`), 4 de ellos dentro de columnas con `x:CompileBindings="False"` → romperlo es **null silencioso, sin error de build** | ver **Ruling B-21** |
| C11 | nada sobre segundos botones primarios en B2 | `GastosView` tiene **dos** `Classes="primary"` literales simultáneos (`:100` "Filtrar" y `:113` "Nuevo gasto"); `NuevaImportacionView` tiene **tres** (`:59`, `:74`, `:492`); `DocumentoListView:74` y `TareaListView:81` tienen un `primary` **por fila** | ver **Ruling B-18** |

### Los 12 sitios de `SignoNegativoBrushConverter` (verificados, `grep -rn` sobre `Views/`)

| Vista | Líneas | Propiedad | Tanda |
|---|---|---|---|
| `Views/Catalogo/ProductoListView.axaml` | `:90` | `StockActual` | 6 (ya migrada de patrón, el converter sigue) |
| `Views/Movimientos/MovimientoHistorialView.axaml` | `:155`, `:164` | `StockAnterior`, `StockNuevo` | 6 (ídem) |
| `Views/Finanzas/ControlPoaView.axaml` | `:37` | `Saldo` | 8 |
| `Views/Finanzas/LibroCajaView.axaml` | `:35`, `:74` | `SaldoFinal`, `Neto` | 8 |
| `Views/Tareas/TareaListView.axaml` | `:33`, `:66` | `DiasParaVencer` | 9 |
| `Views/Reportes/ValorizacionView.axaml` | `:67` | `StockActual` | 10 |
| `Views/Reportes/StockCategoriaView.axaml` | `:58` | `StockTotal` | 10 |
| `Views/Reportes/HistorialPorProductoView.axaml` | `:96`, `:105` | `StockAnterior`, `StockNuevo` | 10 |

**Total: 12 sitios, 8 vistas, 4 módulos.** Los cierra la **Task B2-T**, al final de B2 (Ruling B-6).

### Los 9 `Foreground="Red"` que quedan (verificados)

| Archivo | Línea | Tanda que lo cierra |
|---|---|---|
| `Views/Finanzas/FuenteFinanciamientoFormView.axaml` | `:24` | 8.2 |
| `Views/Finanzas/RubroGastoFormView.axaml` | `:27` | 8.2 |
| `Views/Finanzas/IngresoFormView.axaml` | `:48` | 8.2 |
| `Views/Finanzas/LineaPoaFormView.axaml` | `:70` | 8.2 |
| `Views/Finanzas/GastoFormView.axaml` | `:117` | 8.2 |
| `Views/Finanzas/PagosGastoView.axaml` | `:81` | 8.3 |
| `Views/Documentos/DocumentoFormView.axaml` | `:105` | 9.2 |
| `Views/Tareas/TareaFormView.axaml` | `:94` | 9.4 |
| `Views/Administracion/UsuariosAdminView.axaml` | `:143` | 11 (B3) |

(El 10º del conteo original, `IngresoPorFacturaView:178`, ya lo cerró la Task 6.7.)

---

## 1. Ampliaciones del catálogo de patrones que B2 obliga

El catálogo P0-P7 de la Task 6.0 se probó contra 14 vistas de Catálogo/Movimientos/Inicio. B2 mete
tres formas que ese catálogo no cubre. **No son patrones nuevos inventados: son variantes que
existen en el código y que hay que nombrar para no forzarlas al molde equivocado.**

### P3-b — Formulario de página (10 vistas)

Distinto de **P3-a** (la tarjeta centrada de Catálogo: `ProductoFormView`, `CategoriaFormView`,
`ProveedorFormView`, `UnidadMedidaFormView`, `MovimientoFormControl`).

**Forma actual** (verificada idéntica en las 4 de Finanzas — `FuenteFinanciamientoFormView:10-18`,
`RubroGastoFormView:10-18`, `IngresoFormView:11-19`, `LineaPoaFormView:11-19`, y con un
`ScrollViewer` de más en `GastoFormView:11-20`):

```xml
<DockPanel Margin="24">
    <TextBlock DockPanel.Dock="Top" Text="{Binding Titulo}" Classes="titulo-vista" Margin="0,0,0,16" />
    <Border Classes="card" VerticalAlignment="Top">
        <StackPanel Spacing="12" MaxWidth="480" HorizontalAlignment="Left">
            <TextBlock Text="Concepto" />
            <TextBox Text="{Binding Concepto}" Watermark="..." />
```

**Forma destino:**

```xml
<DockPanel Margin="{DynamicResource MargenVista}">
    <c:HeaderVista DockPanel.Dock="Top" Eyebrow="FINANZAS" Titulo="{Binding Titulo}" />
    <Border Classes="card" VerticalAlignment="Top">
        <StackPanel Spacing="{DynamicResource Espacio3}" MaxWidth="480" HorizontalAlignment="Left">
            <c:CampoFormulario Etiqueta="Concepto">
                <TextBox Text="{Binding Concepto}" Watermark="..." />
            </c:CampoFormulario>
```

Diferencias con P3-a que importan al ejecutar:
1. El `titulo-vista` está **fuera** de la card → el `HeaderVista` también va fuera, con
   `DockPanel.Dock="Top"`. En P3-a el header va **dentro** de la card.
2. `MaxWidth` y `HorizontalAlignment="Left"` del `StackPanel` interior **se conservan tal cual**
   (varían por vista: 420/480/560/620/680). No unificarlos: son anchos de formulario pensados por
   cantidad de campos.
3. **`Requerido` NO se activa en P3-b.** Ninguna etiqueta de estos formularios dice "(obligatorio)"
   (verificado: `grep -n 'obligator' Views/Finanzas/*.axaml Views/Documentos/*.axaml
   Views/Tareas/*.axaml` → cero coincidencias). El **Ruling B-1** convertía un sufijo existente en
   asterisco; acá no hay sufijo que convertir, así que poner `Requerido="True"` **agregaría** una
   marca que hoy no existe — eso sí sería cambio de copy. Dejar `Requerido` sin declarar (default
   `False`). **Excepción posible:** si al ejecutar aparece una etiqueta con "(opcional)" —
   `TareaFormView:26` "Descripción (opcional)", `:30` "Fecha límite (opcional)",
   `PagosGastoView:76` "Nota (opcional)" — **tampoco se toca**: "(opcional)" es información, no
   marcado de obligatoriedad, y `CampoFormulario` no tiene forma de expresarlo.

Vistas P3-b: `FuenteFinanciamientoFormView`, `RubroGastoFormView`, `LineaPoaFormView`,
`GastoFormView`, `IngresoFormView` (tanda 8); `DocumentoFormView`, `TareaFormView` (tanda 9).

### P2-emb / P1-emb — Vista embebida en un `TabControl` (7 vistas)

Vistas que **no se navegan directo**: se renderizan como contenido de un `TabItem` de otra vista.
Verificado leyendo los contenedores:

| Contenedor | Línea | Vista embebida |
|---|---|---|
| `MaestrosFinanzasView.axaml` | `:20` | `FuenteFinanciamientoListView` |
| `MaestrosFinanzasView.axaml` | `:23` | `RubroGastoListView` |
| `MaestrosFinanzasView.axaml` | `:26` | `LineaPoaListView` |
| `ImportacionView.axaml` | `:20` | `NuevaImportacionView` |
| `ImportacionView.axaml` | `:23` | `HistorialImportacionesView` |
| `GastoFormView.axaml` | `:112` | `AdjuntosPanelView` (vía `ContentControl` + `ViewLocator`) |
| `DocumentoFormView.axaml` | `:101` | `AdjuntosDocumentoPanelView` |

**Reglas duras de P*-emb:**
- **NO llevan `HeaderVista`.** El título lo pone el `TabItem.Header` del contenedor (o, en los dos
  paneles de adjuntos, el `TextBlock` propio que pasa a `Classes="seccion"`).
- **NO llevan `MargenVista`.** El contenedor ya lo puso. Duplicarlo da 48 px de aire (bug C3).
- Entran al guardián por una lista distinta (`VistasEmbebidas`, ver sección 2), con un invariante
  **invertido**: el margen exterior NO puede ser `MargenVista`.

### P8 — Wizard de pasos mutuamente excluyentes (1 vista)

`NuevaImportacionView`. Tres `StackPanel`/`DockPanel` hermanos dentro de un mismo `Grid` (`:40`,
`:65`, `:479`), cada uno con `IsVisible="{Binding PasoActual, Converter={x:Static
ObjectConverters.Equal}, ConverterParameter={x:Static vm:PasoWizardImportacion.X}}"` — verificado
en `:41`, `:65`, `:480`, los tres contra la **misma** propiedad `PasoActual` y con los tres valores
del enum `PasoWizardImportacion` (`Cargar`/`Revisar`/`Resultado`). Cada paso tiene **su propio
botón primario**, y eso es correcto: son estados de pantalla, no acciones que compitan.
Consecuencia: `Vista_NoTieneUnSegundoBotonPrimario` **no aplica** — ver Ruling B-18.

---

## 2. Cómo se amplía el guardián en B2

`GuardianDePatronTests.cs` hoy tiene **14 filas** de `[InlineData]` (`:37-50`) y una
`TheoryData<Type> VistasDeLaTanda` (`:65-81`) que **repite los mismos 14 tipos**. Los cuatro
métodos son `Vista_TieneHeaderVistaConElTituloEsperado(Type, string?, string?)` (`:51`),
`Vista_TieneMargenExteriorEstandar(Type)` (`:85`), `Vista_NoTieneOpacidadesLiterales(Type)` (`:97`)
y `Vista_NoTieneUnSegundoBotonPrimario(Type)` (`:109`).

**Trampa de mantenimiento: son DOS listas, no una.** Agregar una fila a `[InlineData]` sin
agregar el tipo a `VistasDeLaTanda` deja la vista con 1 invariante custodiado en vez de 4, **y no
falla nada** — el test simplemente no corre. Cada task de B2 tiene un Step explícito para tocar
las dos.

### B2 agrega una tercera lista: `VistasEmbebidas`

```csharp
/// <summary>
/// Vistas que se renderizan como contenido de un TabItem/ContentControl de otra vista (P2-emb,
/// P1-emb, P5). No llevan HeaderVista propio ni margen de vista: el contenedor ya los puso.
/// Corren los invariantes que SI les aplican, mas uno propio e invertido.
/// </summary>
public static readonly TheoryData<Type> VistasEmbebidas = new() { /* ... */ };

[AvaloniaTheory]
[MemberData(nameof(VistasEmbebidas))]
public void VistaEmbebida_NoDuplicaElMargenDeVista(Type tipoVista)
{
    var vista = PatronHelpers.Montar(tipoVista);
    var margen = PatronHelpers.MargenExteriorDe(vista);
    Assert.True(margen != new Thickness(24),
        $"{tipoVista.Name} es embebida y trae MargenVista propio: el contenedor ya lo aplica, "
        + "queda 48px de aire duplicado.");
}
```

Este invariante **atrapa hoy mismo** el bug C3 (`NuevaImportacionView:37` y
`HistorialImportacionesView:12` con `Margin="24"`). Es la primera fila roja real de la tanda 8.

`VistasEmbebidas` corre además `Vista_NoTieneOpacidadesLiterales` y
`Vista_NoTieneUnSegundoBotonPrimario` (agregando `[MemberData(nameof(VistasEmbebidas))]` como
segundo atributo de esos dos métodos — xUnit acumula los `MemberData`), **salvo** las excepciones
del Ruling B-18.

---

## 3. Rulings de B2

**Ruling B-16 — el guardián NO ve dentro de un `ItemTemplate`, y en B2 eso es la regla, no la
excepción. La revisión de plantillas es MANUAL y obligatoria.**

El Ruling B-15 lo descubrió en la tanda 7 con dos `Opacity="0.7"` de `ListBox.ItemTemplate`. En B2
el fenómeno es masivo. Clasifiqué los 30 `Opacity` literales de las 29 vistas según estén dentro o
fuera de un `DataTemplate` (script de conteo de apertura/cierre de `<DataTemplate>` sobre cada
archivo):

| Vista | Líneas dentro de `DataTemplate` (**el guardián NO las ve**) | Líneas fuera (**el guardián sí**) |
|---|---|---|
| `Finanzas/NuevaImportacionView` | `:93`, `:111`, `:156`, `:172`, `:189`, `:217`, `:307`, `:325`, `:341`, `:358` (10× `0.5`) | — |
| `Tareas/TareaListView` | `:34`, `:36`, `:67`, `:69`, `:72` (`0.7`), `:103` (`0.8`), `:121` (`0.6`) | — |
| `Finanzas/PagosGastoView` | `:125` (`0.8`) | `:30`, `:35`, `:40`, `:45` (4× `0.7`) |
| `Documentos/DocumentoListView` | `:60`, `:154` (`0.8`); `:61`, `:155` (`0.7`) | — |
| `Documentos/DocumentoFormView` | `:81` (`0.7`) | — |
| `Tareas/TareaFormView` | `:80` (`0.7`) | `:46` (`0.85`) |
| `Finanzas/LineaPoaListView` | `:35` (`0.7`) | — |
| **Reportes (las 5)** | — | — (cero `Opacity` literal, verificado) |
| **Total** | **25** | **5** |

**El verde del guardián NO significa "no quedan opacidades" en B2.** Significa "no quedan
opacidades fuera de plantillas". **B2 toca 21 grillas y 8 `ItemsControl`/`ListBox`.**

*Procedimiento obligatorio al cerrar cada task de B2:*
```bash
grep -n 'Opacity="0\.' <cada archivo tocado por la task>
```
Expected: **cero coincidencias**, salvo las declaradas como semántica de dominio vía converter
(`ActivoOpacidadConverter`), que se listan una por una en la tabla de sustitución de la task. Este
grep es el guardián real de las plantillas; el `[AvaloniaTheory]` es el guardián de lo demás.

*Criterio de migración* (el mismo que fijaron la Task 6.6 y el Ruling B-15): `Opacity` literal
sobre un `TextBlock` = atenuación decorativa → `Foreground="{DynamicResource TextoTerciarioBrush}"`.
`Opacity` literal sobre algo que **no** es texto (el `i:Icon mdi-lock` ×10 de
`NuevaImportacionView`) → **Ruling B-11**: `<Style Selector="...">` con `Setter Property="Opacity"`
en `UserControl.Styles`, para que su `Priority` sea `Style` y no `LocalValue`.

*Costo si me equivoco:* ninguno funcional — el grep es barato y el criterio ya está probado en dos
tandas.

**Ruling B-17 — las 7 vistas embebidas pierden su margen exterior propio; no ganan `HeaderVista`;
y entran al guardián por `VistasEmbebidas`.**

Verificado abriendo los contenedores (tabla de la sección 1). `MaestrosFinanzasView:11` y
`ImportacionView:11` ya traen `<DockPanel Margin="24">`; sus hijos de pestaña que además declaran
`Margin="24"` (`NuevaImportacionView:37`, `HistorialImportacionesView:12`) rinden 48 px. Los 3
maestros embebidos usan `<Border Classes="card" Margin="0,12,0,0">` — ese `0,12,0,0` **se conserva**
(es la separación con el borde de la pestaña, no un margen de vista) y por eso el invariante
embebido asserta "distinto de `Thickness(24)`", no "sin margen".

*Costo si me equivoco:* si alguna de las 7 se navegara directo en el futuro, quedaría sin título y
pegada al borde. Verificado que hoy no pasa: `grep -rn 'NuevaImportacionView\|HistorialImportaciones
View\|FuenteFinanciamientoListView\|RubroGastoListView\|LineaPoaListView' src/StockApp.Presentation/`
solo las encuentra en sus contenedores y en `ViewLocator` — **NO VERIFIQUÉ** si algún ViewModel las
resuelve por `ViewLocator` fuera de esas pestañas; la Task 8.1 tiene un Step para confirmarlo antes
de sacar los márgenes.

**Ruling B-18 — "una vista, un solo botón primario" se mide sobre el CHROME de la vista, no sobre
las filas de una lista ni sobre pasos mutuamente excluyentes.**

Tres casos distintos, con tres tratamientos distintos:

1. **Dos primarios reales, simultáneos, en el chrome → se arregla.** `GastosView:100` ("Filtrar",
   dentro de la card de filtros) y `GastosView:113` ("Nuevo gasto", en la barra de acciones) son
   `Classes="primary"` literales, sin `IsVisible` que los excluya entre sí. **"Nuevo gasto" es la
   acción principal de la vista; "Filtrar" pasa a `secondary`.** Es la misma jerarquía que ya tienen
   las 5 vistas de Reportes, donde "Buscar" es el único primario porque no compite con un "Nuevo".
   *Verificado:* `GastosViewTests` localiza por `Content` (`"Nuevo gasto"`, `"Editar"`), nunca por
   `Classes` → el cambio no toca un assert.

2. **Primario por fila dentro de un `ItemTemplate` → se degrada a `secondary`.**
   `DocumentoListView:74` ("Finalizar") y `TareaListView:81` ("Terminar"). El guardián no los ve
   (plantilla no realizada), pero en runtime coexisten N veces con el primario del chrome
   ("Nuevo documento" `DocumentoListView:14`, "Nueva tarea" `TareaListView:17`). El resto de los
   botones de fila de esas dos vistas ya usa `ghost`/`secondary`/`danger` — estos dos son la
   excepción, no la regla. *Verificado:* `DocumentoListViewTests` y `TareaListViewTests` localizan
   por `Content` y por `ArbolVisual.EsVisibleEnArbol`, nunca por `Classes`.

3. **Primarios de pasos mutuamente excluyentes (P8) → NO se tocan y la vista se exime del
   invariante genérico, con un test propio que lo reemplaza.** `NuevaImportacionView:59`
   ("Analizar", paso Cargar), `:74` ("Confirmar", paso Revisar), `:492` ("Nueva importación", paso
   Resultado). Cada uno ES la acción principal de su pantalla. La salida del Ruling B-12
   (`Classes.primary="{Binding Bool}"`) exigiría **tres propiedades nuevas** en
   `NuevaImportacionViewModel`, y esta vista es la más peligrosa del repo: no se le tocan
   ViewModels en una tanda de barrido visual. En su lugar, la **Task 8.4** escribe un test
   dedicado que monta la vista **con ViewModel real**, recorre los tres valores de
   `PasoWizardImportacion` y asserta que en cada uno hay **exactamente un** botón primario visible.
   Eso custodia más que el invariante genérico, no menos.

*Costo si me equivoco:* en (1) y (2) es un `Classes` de vuelta, un `sed`. En (3), si el test propio
no se escribe, la vista queda sin guardián de jerarquía — por eso es un Step bloqueante de 8.4.

**Ruling B-19 — la deuda de `EstablecerPermisos` no-op son SEIS archivos, no tres, y B2 cierra
cinco: los tres de Finanzas en la Task 8.0 y el compartido de Tareas/Documentos en la Task 9.0.**

Verificado con `grep -n 'EstablecerPermisos' tests/StockApp.Presentation.UiTests/*.cs`:

| Archivo | Línea | Clase | ¿No-op? | Quién la usa | Cierra en |
|---|---|---|---|---|---|
| `SesionFakes.cs` | `:39` | `SesionFake` | **NO** — aplica el set (Ruling 6 de Fase A) | la buena | — |
| `GastosViewTests.cs` | `:51` | `CurrentSessionFake` privada | sí | solo ese archivo | **8.0** |
| `IngresosViewTests.cs` | `:47` | `CurrentSessionFake` privada | sí | solo ese archivo | **8.0** |
| `PagosGastoViewTests.cs` | `:49` | `CurrentSessionFake` privada | sí | solo ese archivo | **8.0** |
| `TareaFakes.cs` | `:118` | `TareaSessionFake` | sí | `TareaListViewTests`, `TareaFormViewTests`, **`DocumentoListViewTests`** | **9.0** |
| `InicioViewTests.cs` | `:55` | privada | sí | solo ese archivo | **B3** (tanda 6 ya cerrada; no se reabre) |
| `InicioPanelTareasTests.cs` | `:51` | privada | sí | solo ese archivo | **B3** (ídem) |

Corrección adicional: el comentario de clase de `SesionFakes.cs:12-14` **afirma que `SesionFake`
"reemplaza los `CurrentSessionFake` duplicados en `GastosViewTests`, `PagosGastoViewTests`,
`IngresosViewTests` e `InicioViewTests`"**. Eso es **falso hoy**: los cuatro siguen con su clase
privada. La Task 8.0 corrige el comentario además del código.

*Por qué importa:* mientras `EstablecerPermisos` sea no-op no se puede escribir un test de
**revocación de permiso en caliente** (el `AuthServiceFake` de `SesionFakes.cs:89` llama
`_session.EstablecerPermisos(permisos)` como efecto de borde; contra un fake no-op ese llamado no
hace nada y el test da verde sin probar nada). Es el mismo modo de falla del Ruling 6 de la Fase A.

*Costo si me equivoco:* la migración es mecánica (borrar la clase privada, cambiar el tipo en el
helper `Montar`) y la suite lo detecta al toque. Riesgo real: `TareaSessionFake` tiene un ctor
`(RolUsuario)` de un solo argumento (`TareaFakes.cs:100`) que `SesionFake` **no** tiene
(`SesionFakes.cs:23` es `(RolUsuario rol, params string[] permisos)`); `params` cubre la llamada de
un argumento, así que los call sites compilan sin cambios — **verificado leyendo las dos firmas**.

**Ruling B-20 — `DocumentoFormView` y `TareaFormView` llevan DOS `HeaderVista`, no uno con
`{Binding Titulo}`. No se toca el ViewModel.**

Las dos vistas tienen dos `titulo-vista` mutuamente excluyentes:
`DocumentoFormView:19-20` ("Nuevo documento" / "Detalle del documento", con
`IsVisible="{Binding EsNuevoDocumento}"` y `{Binding !EsNuevoDocumento}`) y `TareaFormView:18-19`
("Nueva tarea" / "Detalle de la tarea", ídem con `EsNuevaTarea`).

*Verificado:* **ni `DocumentoFormViewModel` ni `DocumentoListViewModel` exponen una propiedad
`Titulo`** (grep sobre los dos archivos → cero coincidencias). Colapsar a un solo
`HeaderVista Titulo="{Binding Titulo}"` exigiría agregarla.

*Decisión:* dos `<c:HeaderVista …>` conservando **textualmente** los mismos dos `IsVisible`. Es el
mismo criterio que la Task 6.3 aplicó a los dos bloques de `PuedeRecalcularStock`: no se
"reescribe" un binding de gating al moverlo. La regla de la Task 6.6 ("una tanda de barrido visual
no toca ViewModels") sigue en pie; la excepción del Ruling B-12 se abrió solo cuando la
alternativa rompía jerarquía real, y acá no rompe nada.

*Consecuencia para el guardián:* `PatronHelpers.HeaderDe` devuelve el **primero** del árbol
(`FirstOrDefault`, `PatronHelpers.cs:51-52`), y `PatronHelpers.Montar` no asigna DataContext, así
que los dos `IsVisible` quedan sin resolver y caen al default `true` de la propiedad. La fila del
guardián para estas dos vistas verifica el **primer** header: `"Nuevo documento"` y `"Nueva tarea"`.

*Alternativa descartada, por si el usuario la prefiere:* agregar `public string Titulo =>
EsNuevoDocumento ? "Nuevo documento" : "Detalle del documento";` a cada VM y colapsar a un solo
header. Es más limpio visualmente pero mete un cambio de VM en una tanda de barrido, y obliga a
`[NotifyPropertyChangedFor(nameof(Titulo))]` sobre `EsNuevoDocumento` — que ya tiene 7
`NotifyPropertyChangedFor` encima (`DocumentoFormViewModel.cs:65-73`). **Queda como pregunta
abierta.**

**Ruling B-21 — `x:Name="Root"` de `NuevaImportacionView` es intocable, y romperlo NO da error de
build.**

`NuevaImportacionView.axaml:13` declara `x:Name="Root"` sobre el `UserControl`. Cinco bindings lo
usan para escapar del `DataContext` de fila y llegar al del ViewModel:

| Línea | Binding | ¿Con red del compilador? |
|---|---|---|
| `:124` | `ItemsSource="{Binding #Root.DataContext.ProveedoresDisponibles}"` | **NO** — está dentro de `DataGridTemplateColumn x:CompileBindings="False"` (`:106`) |
| `:198` | `…FuentesDisponibles` | **NO** — columna `:184` |
| `:234` | `…RubrosDisponibles` | **NO** — columna `:212` |
| `:367` | `…FuentesDisponibles` | **NO** — columna `:353` |
| `:435` | `…ProgramasExistentes` | sí (dentro de `GridLineasPoa`, sin `x:CompileBindings="False"` en su columna) |

Cuatro de los cinco viven en columnas con los bindings compilados **apagados**. Renombrar, mover o
borrar `x:Name="Root"` deja esos cuatro `ComboBox` **vacíos en silencio**, sin AVLN2000 y sin
excepción en runtime — el modo de falla más caro posible en la vista más grande del repo.

Los otros dos `x:Name` de la vista (`GridGastos` `:84`, `GridLineasPoa` `:398`) también son
intocables: `NuevaImportacionGastosGridTests` y `NuevaImportacionLineasPoaGridTests` los usan para
localizar sus grillas. **NO VERIFIQUÉ línea por línea** cómo los localizan esos dos archivos de
test; la Task 8.4 tiene un Step de línea base que lo obliga a confirmarlo antes de tocar el XAML.

*Costo si me equivoco:* alto y silencioso. Por eso 8.4 va sola, al final, y arranca con un grep de
`#Root` que se repite al cerrar.

---

## Tanda 8: Finanzas (19 vistas)

**Vistas (19, contadas con `ls Views/Finanzas/*.axaml`):** `MaestrosFinanzasView`,
`ImportacionView`, `FuenteFinanciamientoListView`, `RubroGastoListView`, `LineaPoaListView`,
`FuenteFinanciamientoFormView`, `RubroGastoFormView`, `LineaPoaFormView`, `GastoFormView`,
`IngresoFormView`, `GastosView`, `IngresosView`, `ControlPoaView`, `LibroCajaView`,
`CalendarioPagosView`, `HistorialImportacionesView`, `PagosGastoView`, `NuevaImportacionView`,
`AdjuntosPanelView`.

**Eyebrow de todo el módulo: `FINANZAS`.** (Las embebidas no llevan.)

**Riesgo: ALTO.** Es la tanda más grande del refactor por un factor de 3. 13 tests de UI a
preservar, la vista más peligrosa del repo, y 4 columnas de grilla sin red del compilador.

> **⚠ Desvío del brief, para tu OK.** El brief fijó "4 tasks, 4 commits". Escribí **5 tasks / 5
> commits**: agregué la **Task 8.0**, que es *solo tests* (migración de los 3 `CurrentSessionFake`
> no-op a `SesionFake`) y no toca una línea de XAML. El motivo es la regla que ya rige toda la Fase
> A y B — *"no se toca una vista sin su red de gates"*: `GastosView`, `IngresosView` y
> `PagosGastoView` tienen 13 tests de gating montados sobre un fake cuyo `EstablecerPermisos` es
> no-op, y la Task 8.3 los va a mover de lugar. Meterla dentro de 8.3 mezclaría "arreglé el banco
> de pruebas" con "moví los botones" en un solo commit, y si algo se pone rojo no se sabe cuál de
> los dos fue. **Si preferís 4 commits, la 8.0 se pliega al principio de la 8.3 y el resto del plan
> no cambia.**

---

### Task 8.0: cerrar la deuda de banco de pruebas de Finanzas (SIN tocar XAML)

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/GastosViewTests.cs` (borra la clase privada de `:36-54`)
- Modify: `tests/StockApp.Presentation.UiTests/IngresosViewTests.cs` (ídem, `:32-50`)
- Modify: `tests/StockApp.Presentation.UiTests/PagosGastoViewTests.cs` (ídem, `:34-52`)
- Modify: `tests/StockApp.Presentation.UiTests/SesionFakes.cs` (corregir el comentario de `:12-18`)
- Create: `tests/StockApp.Presentation.UiTests/FinanzasRevocacionPermisosTests.cs`

**Interfaces:**
- Consumes: `SesionFake(RolUsuario, params string[])` (`SesionFakes.cs:23`), `AuthServiceFake`
  (`SesionFakes.cs:60-92`), `ArbolVisual.EsVisibleEnArbol` (`ArbolVisualHelpers.cs:19`).
- Produces: la capacidad de testear revocación de permiso en caliente en Finanzas. **Ningún**
  cambio de producción.

**Contexto verificado.** Las tres clases privadas son **idénticas carácter por carácter** salvo el
nombre del archivo: `private sealed class CurrentSessionFake : ICurrentSession` con
`public void EstablecerPermisos(IReadOnlySet<string> permisos) { }`. Los helpers `Montar` de los
tres reciben `(RolUsuario rol, IReadOnlySet<string> permisos)` y construyen el fake ahí adentro.

- [ ] **Step 1: escribir el test que falla — revocación en caliente**

Crear `FinanzasRevocacionPermisosTests.cs` con **un** caso, sobre `IngresosView` (la más barata de
montar de las tres, 76 líneas, sin `AdjuntosPanelViewModel`):

Montar como `Operador` con `{VerFinanzas, RegistrarIngresos}` → assertear que el botón
`"Nuevo ingreso"` **está visible** y el `TextBlock "Solo lectura"` (`IngresosView:44-45`) **no**.
Después llamar `EstablecerPermisos(new HashSet<string>{ "VerFinanzas" })` sobre la sesión, correr
`Dispatcher.UIThread.RunJobs()`, y assertear lo inverso.

- [ ] **Step 2: correr y ver el rojo**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~FinanzasRevocacionPermisos"` (timeout 600000)
Expected: **FAIL**, y el motivo tiene que ser el correcto: el segundo bloque de asserts falla
porque el botón **sigue visible** — el `EstablecerPermisos` del fake privado no hizo nada. Si falla
por otra razón (excepción de montaje, `null`), el test está mal escrito, no el fake.

- [ ] **Step 3: migrar los tres archivos a `SesionFake`**

Por archivo: borrar la clase privada `CurrentSessionFake` completa y reemplazar su uso en `Montar`
por `new SesionFake(rol, permisos.ToArray())`. **No tocar ningún assert ni ningún helper de
localización** (`BuscarBotonPorTexto`, `BuscarTextoPorContenido`, `BuscarPanelFormularioPago`).

Corregir el comentario de `SesionFakes.cs:12-18`: hoy afirma que ya reemplazó a los cuatro
duplicados. Tras esta task habrá reemplazado a **tres**; `InicioViewTests.cs:55` y
`InicioPanelTareasTests.cs:51` siguen con el suyo y se cierran en B3.

- [ ] **Step 4: ver el verde, y que los 13 preexistentes NO se muevan**

Run: `--filter "FullyQualifiedName~GastosViewTests|FullyQualifiedName~IngresosViewTests|FullyQualifiedName~PagosGastoViewTests|FullyQualifiedName~FinanzasRevocacionPermisos"`
Expected: PASS **14/14** (2 + 5 + 6 preexistentes, sin tocar un solo assert, + 1 nuevo).
Si hubo que tocar un assert de los 13, la migración cambió comportamiento: parar.

- [ ] **Step 5: validar por mutación**

Mutación: en `SesionFakes.cs:39`, cambiar
`public void EstablecerPermisos(IReadOnlySet<string> permisos) => _permisos = permisos;`
por `public void EstablecerPermisos(IReadOnlySet<string> permisos) { }`.
Expected: `FinanzasRevocacionPermisosTests` **rojo**. Revertir, verde.
*Esto prueba que el test nuevo mide exactamente la deuda que se cerró, y no otra cosa.*

- [ ] **Step 6: suite completa + commit**

Run: `dotnet test StockApp.sln` (timeout 600000, polling activo). Expected: PASS, 3176 + 1.

```
test(ui): migra los fakes de sesion de Finanzas a SesionFake

GastosViewTests, IngresosViewTests y PagosGastoViewTests declaraban cada uno
su propia CurrentSessionFake con EstablecerPermisos no-op: el mismo bug que el
Ruling 6 de la Fase A arreglo en SesionFake y que la Task 4.1 no migro. Con el
no-op no se podia escribir un test de revocacion de permiso en caliente en
Finanzas -- AuthServiceFake llama EstablecerPermisos como efecto de borde y
contra el fake viejo ese llamado no hacia nada.

El comentario de SesionFakes.cs afirmaba que ya los habia reemplazado a los
cuatro. Era falso; ahora reemplaza a tres (los dos de Inicio quedan para B3).

Los 13 tests preexistentes pasan sin tocar un solo assert.
```

**Riesgo específico:** ninguno de producción — es un commit solo de tests. El único riesgo es que
`SesionFake` tenga un comportamiento distinto en `UsuarioActual`/`RolActual` que algún assert lea.
Verificado: las dos implementaciones devuelven `new(1, "prueba"/"operador", rol, …)` — **el segundo
campo difiere** (`"prueba"` en `SesionFake:30`, `"operador"` en las privadas). **NO VERIFIQUÉ** si
algún assert de los 13 lee ese nombre de usuario; el Step 4 lo detecta al toque si lo lee.

---

### Task 8.1: P4 contenedores + P2-emb maestros embebidos (5 vistas)

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/MaestrosFinanzasView.axaml` (32 l)
- Modify: `src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml` (29 l)
- Modify: `src/StockApp.Presentation/Views/Finanzas/FuenteFinanciamientoListView.axaml` (44 l)
- Modify: `src/StockApp.Presentation/Views/Finanzas/RubroGastoListView.axaml` (47 l)
- Modify: `src/StockApp.Presentation/Views/Finanzas/LineaPoaListView.axaml` (48 l)
- Test: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (+2 `InlineData`,
  +2 en `VistasDeLaTanda`, +3 en la nueva `VistasEmbebidas`)

**Interfaces:**
- Consumes: `c:HeaderVista`, `c:BadgeEstado`, tokens `MargenVista`/`Espacio2`/`Espacio4`.
- Produces: la referencia de **P2-emb** y de **P4**, que la Task 9.3 y la tanda 11 copian.

**Por qué va primera:** son las 5 vistas más chicas del módulo (200 líneas entre las 5), cero tests
de UI, cero gates de permiso (verificado: los 9 botones de los 3 maestros **no tienen ni un
`IsVisible`**). Si `VistasEmbebidas` está mal diseñada, se descubre acá y no en 8.4.

#### Tabla de sustitución — `MaestrosFinanzasView.axaml` (P4)

| Línea | Hoy | Pasa a |
|---|---|---|
| `:11` | `<DockPanel Margin="24">` | `<DockPanel Margin="{DynamicResource MargenVista}">` |
| `:13-16` | `<TextBlock DockPanel.Dock="Top" Text="Maestros de finanzas" Classes="titulo-vista" Margin="0,0,0,16" />` | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="FINANZAS" Titulo="Maestros de finanzas" />` (el `Margin="0,0,0,16"` desaparece: el `ControlTheme` de `HeaderVista` ya trae `Margin="0,0,0,24"`) |
| `:18-28` | `<TabControl>` con 3 `TabItem` | **no se toca** |
| header | — | agregar `xmlns:c="using:StockApp.Presentation.Controls"` al `UserControl` |

#### Tabla de sustitución — `ImportacionView.axaml` (P4)

| Línea | Hoy | Pasa a |
|---|---|---|
| `:11` | `<DockPanel Margin="24">` | `<DockPanel Margin="{DynamicResource MargenVista}">` |
| `:13-16` | `<TextBlock … Text="Importar planillas" Classes="titulo-vista" Margin="0,0,0,16" />` | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="FINANZAS" Titulo="Importar planillas" />` |
| `:18-25` | `<TabControl>` con 2 `TabItem` | **no se toca** |
| header | — | agregar `xmlns:c` |

#### Tabla de sustitución — los 3 maestros embebidos (P2-emb)

Las 3 son **estructuralmente idénticas** (verificado línea por línea; los números coinciden exacto
en `FuenteFinanciamientoListView` y `RubroGastoListView`, y `LineaPoaListView` tiene una línea de
más en el `ItemTemplate`).

| Línea (Fuente / Rubro / LineaPoa) | Hoy | Pasa a |
|---|---|---|
| `:11` / `:11` / `:11` | `<Border Classes="card" Margin="0,12,0,0">` | **se queda igual** (Ruling B-17: es separación de pestaña, no margen de vista) |
| `:14` / `:14` / `:14` | `<StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">` | `Spacing="{DynamicResource Espacio2}"`; el `Margin="0,0,0,12"` se queda literal (T1: `Espacio3` es `x:Double`, no sirve en `Margin`) |
| `:15-23` / `:15-23` / `:15-23` | 3 `Button` (`primary` "Nueva fuente"/"Nuevo rubro"/"Nueva línea POA", `secondary` "Editar", `secondary` "Dar de baja") | **se quedan donde están** — sin `HeaderVista` no hay slot `Acciones` al que migrarlos. Solo se tokeniza el `Spacing` del contenedor |
| `:30` / `:30` / `:30` | `<StackPanel Orientation="Horizontal" Spacing="16" Margin="4">` | `Spacing="{DynamicResource Espacio4}"`; **`Margin="4"` se queda literal** (T1 otra vez; precedente ya tomado en la Task 7.1) |
| — / `:31-32` / `:31-32` | `<StackPanel … Opacity="{Binding Activo, Converter=…ActivoOpacidadConverter…}">` | **se queda** — semántica de dominio |
| `:32` / — / — | `<TextBlock Text="{Binding Nombre}" Opacity="{Binding Activo, Converter=…}" />` | **se queda** |
| — / — / `:35` | `<TextBlock Text="{Binding Programa}" Opacity="0.7" />` | `Foreground="{DynamicResource TextoTerciarioBrush}"` — **atenuación decorativa, el guardián NO la ve (Ruling B-16)** |
| `:33-35` / `:36-38` / `:37-39` | `<Border Classes="badge-inactiva" IsVisible="{Binding !Activo}"><TextBlock Text="Inactiva"/"Inactivo" Classes="badge-inactiva-texto" /></Border>` | `<c:BadgeEstado Texto="Inactiva" Tono="Neutro" IsVisible="{Binding !Activo}" />` — **conservar el texto exacto de cada uno: "Inactiva" en Fuente y LineaPoa, "Inactivo" en Rubro** (género gramatical, es copy) |
| header | — | agregar `xmlns:c` a los 3 |

- [ ] **Step 1: escribir el guardián que falla**

En `GuardianDePatronTests.cs`:
1. Agregar a `[InlineData]` (`:37-50`) y a `VistasDeLaTanda` (`:65-81`):
   `[InlineData(typeof(MaestrosFinanzasView), "Maestros de finanzas", "FINANZAS")]` y
   `[InlineData(typeof(ImportacionView), "Importar planillas", "FINANZAS")]`.
2. Crear la `TheoryData<Type> VistasEmbebidas` con los 3 maestros, y el método
   `VistaEmbebida_NoDuplicaElMargenDeVista` (código en la sección 2).
3. Agregar `[MemberData(nameof(VistasEmbebidas))]` como **segundo** atributo de
   `Vista_NoTieneOpacidadesLiterales` y `Vista_NoTieneUnSegundoBotonPrimario`.
4. Agregar `using StockApp.Presentation.Views.Finanzas;`.

- [ ] **Step 2: correr y ver el rojo — y verificar CUÁL rojo**

Run: `--filter "FullyQualifiedName~GuardianDePatron"` (timeout 600000)
Expected: **exactamente 2 fallos nuevos**, los dos de
`Vista_TieneHeaderVistaConElTituloEsperado` para `MaestrosFinanzasView` e `ImportacionView`
("no tiene un HeaderVista en su arbol visual").

**Los 3 maestros embebidos tienen que pasar en VERDE desde el Step 2**, y eso NO es una falla del
guardián: su `Margin="0,12,0,0"` ya cumple el invariante invertido, no tienen `primary` duplicado, y
su único `Opacity="0.7"` (`LineaPoaListView:35`) vive dentro del `ItemTemplate` → invisible
(Ruling B-16). **Anotar esto explícitamente en el ledger**, como hizo el Ruling B-15, para que
nadie lo lea como "el guardián no custodia nada".

- [ ] **Step 3: aplicar las tres tablas**

**Cuidado T1:** `Spacing="{DynamicResource Espacio2}"` ✅ (es `x:Double`);
`Margin="{DynamicResource Espacio2}"` ❌ (revienta en runtime).
**Cuidado T4:** ningún comentario XAML nuevo con `--`.
**Cuidado T3:** si tocás code-behind, `Avalonia.Application.Current`, nunca `Application.Current`.

- [ ] **Step 4: ver el verde**

Run: `--filter "FullyQualifiedName~GuardianDePatron"`. Expected: PASS en todas las filas.

- [ ] **Step 5: grep de plantillas (Ruling B-16, obligatorio)**

```bash
grep -n 'Opacity="0\.\|Margin="24"\|titulo-vista\|badge-inactiva\|Foreground="Red"\|FontSize="' \
  src/StockApp.Presentation/Views/Finanzas/MaestrosFinanzasView.axaml \
  src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml \
  src/StockApp.Presentation/Views/Finanzas/{Fuente,}*ListView.axaml \
  src/StockApp.Presentation/Views/Finanzas/RubroGastoListView.axaml \
  src/StockApp.Presentation/Views/Finanzas/LineaPoaListView.axaml
```
Expected: **cero coincidencias.** El `Opacity` vía `ActivoOpacidadConverter` no matchea el patrón
`Opacity="0\.` porque su valor es un binding, no un literal — si aparece algo, es residuo real.

- [ ] **Step 6: validar por mutación (3 mutaciones, 3 rojos, revertir cada una)**

1. `Titulo="Maestros de finanzas"` → `"Maestros"` → `Vista_TieneHeaderVistaConElTituloEsperado`
   rojo para esa fila.
2. En `RubroGastoListView:11`, `Margin="0,12,0,0"` → `Margin="{DynamicResource MargenVista}"` →
   **`VistaEmbebida_NoDuplicaElMargenDeVista` rojo.** *Esta es la mutación que importa*: es
   exactamente el bug C3 que la tanda 8 tiene que impedir que vuelva.
3. Poner `Classes="primary"` en el botón "Editar" de `FuenteFinanciamientoListView:18` →
   `Vista_NoTieneUnSegundoBotonPrimario` rojo (2 primarios en una vista embebida).

Si alguna mutación **no** pone el test en rojo, `VistasEmbebidas` está mal cableada (lo más
probable: se agregó a la `TheoryData` pero falta el `[MemberData]` en el método). Arreglarlo antes
de seguir.

- [ ] **Step 7: confirmar que las 3 embebidas no se navegan directo (Ruling B-17)**

```bash
grep -rn 'FuenteFinanciamientoListViewModel\|RubroGastoListViewModel\|LineaPoaListViewModel' \
  src/StockApp.Presentation/ --include=*.cs
```
Expected: aparecen solo como propiedades de `MaestrosFinanzasViewModel` y en su propio archivo.
**Si alguna aparece en `INavigationService` / en el sidebar de `ShellMainViewModel`, PARAR**: esa
vista se navega directo y necesita `HeaderVista` + `MargenVista`, no P2-emb.

- [ ] **Step 8: suite completa + commit**

Run: `dotnet test StockApp.sln` (timeout 600000). Expected: PASS. **No dejar la suite en rojo entre
commits** (error cometido en la Task 6.0).

```
feat(ui): aplica el sistema de diseno a los contenedores y maestros de Finanzas

- MaestrosFinanzasView e ImportacionView (P4): HeaderVista reemplaza al
  TextBlock titulo-vista suelto; el TabControl no se toca
- Los 3 maestros embebidos (P2-emb) conservan su Margin="0,12,0,0" y NO ganan
  HeaderVista: el titulo lo pone el TabItem del contenedor. BadgeEstado
  reemplaza el par Border.badge-inactiva + TextBlock, conservando el genero
  del texto ("Inactiva"/"Inactivo")
- LineaPoaListView pierde un Opacity="0.7" que el guardian no puede ver:
  vive dentro del ListBox.ItemTemplate y PatronHelpers.Montar no asigna
  ItemsSource, asi que la plantilla nunca se realiza (Ruling B-15/B-16)
- GuardianDePatronTests gana una tercera lista, VistasEmbebidas, con un
  invariante invertido: una vista embebida NO puede traer MargenVista propio
  porque el contenedor ya lo aplico
```

**Riesgos específicos de esta task:**
- **Bajo, salvo el Step 7.** Si alguno de los 3 maestros se navega directo (no verificado al
  planificar), sacarle el título lo deja sin encabezado en esa ruta.
- `LineaPoaListView` es la única de las 3 que no es copia exacta: tiene un `StackPanel` anidado de
  más (`:31-36`) y el `Opacity="0.7"` de `:35`. No aplicar la tabla a ciegas por número de línea.

---

### Task 8.2: P3-b sobre los 5 formularios de Finanzas

**Files:**
- Modify: `Views/Finanzas/FuenteFinanciamientoFormView.axaml` (38 l)
- Modify: `Views/Finanzas/RubroGastoFormView.axaml` (41 l)
- Modify: `Views/Finanzas/IngresoFormView.axaml` (68 l)
- Modify: `Views/Finanzas/LineaPoaFormView.axaml` (84 l)
- Modify: `Views/Finanzas/GastoFormView.axaml` (137 l)
- Test: `GuardianDePatronTests.cs` (+5 `InlineData`, +5 en `VistasDeLaTanda`)

**Interfaces:**
- Consumes: `c:HeaderVista`, `c:CampoFormulario`, `DangerBrush`, tokens.
- Produces: la referencia de **P3-b**, que copian `DocumentoFormView` y `TareaFormView` (tanda 9).

**Riesgo: BAJO-MEDIO.** Cero tests de UI sobre las 5 (verificado: no existe
`GastoFormViewTests.cs`, `IngresoFormViewTests.cs`, ni equivalente para los 3 maestros). 5 de los 9
`Foreground="Red"` que quedan en la app están acá. El riesgo real es el **seam de validación**:
`Controls.axaml:185` (TextBox) y `:203` (ComboBox) definen `(DataValidationErrors.ErrorConverter)`
por selector de tipo.

**Regla dura heredada de la Task 6.4 (no re-litigar):** `CampoFormulario` es un `ContentControl` con
`ContentPresenter` pelado, y envolver un `TextBox` en él **no** rompe el `ErrorConverter`. Está
validado por mutación de valor local dentro de una vista real. **Prohibido** agregarle
`ErrorTemplate` o un `Setter` de `ErrorConverter`. Y la sintaxis
`(DataValidationErrors.ErrorConverter)="…"` como atributo XML **no compila** (Ruling B-10): como
valor local va sin paréntesis.

#### Tabla de sustitución común a las 5

El movimiento es idéntico; solo cambian los números de línea y el `MaxWidth`.

| Elemento | Hoy | Pasa a |
|---|---|---|
| `DockPanel` raíz | `<DockPanel Margin="24">` | `<DockPanel Margin="{DynamicResource MargenVista}">` |
| `TextBlock` de título | `<TextBlock DockPanel.Dock="Top" Text="{Binding Titulo}" Classes="titulo-vista" Margin="0,0,0,16" />` | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="FINANZAS" Titulo="{Binding Titulo}" />` |
| `StackPanel` interior de la card | `<StackPanel Spacing="12" MaxWidth="N" HorizontalAlignment="Left">` | `Spacing="{DynamicResource Espacio3}"`, **`MaxWidth` y `HorizontalAlignment` intactos** |
| cada par etiqueta+control | `<TextBlock Text="X" />` + `<TextBox …/>` o `<ComboBox …/>` o `<CalendarDatePicker …/>` | `<c:CampoFormulario Etiqueta="X"><TextBox …/></c:CampoFormulario>` — **sin `Requerido`** (P3-b, sección 1) |
| mensaje de error | `Foreground="Red"` | `Foreground="{DynamicResource DangerBrush}"` |
| barra de botones | `<StackPanel Orientation="Horizontal" Spacing="8">` | `Spacing="{DynamicResource Espacio2}"` |

#### Líneas exactas por archivo (verificadas)

| Archivo | `DockPanel` | `titulo-vista` | `StackPanel Spacing="12"` | `Foreground="Red"` | barra de botones |
|---|---|---|---|---|---|
| `FuenteFinanciamientoFormView` | `:10` | `:12-15` | `:18` (`MaxWidth=420`) | `:24` | `:28` |
| `RubroGastoFormView` | `:10` | `:12-15` | `:18` (`MaxWidth=420`) | `:27` | `:31` |
| `IngresoFormView` | `:11` | `:13-16` | `:19` (`MaxWidth=480`) | `:48` | `:52` |
| `LineaPoaFormView` | `:11` | `:13-16` | `:19` (`MaxWidth=560`) | `:70` | `:74` |
| `GastoFormView` | `:12` (dentro de un `<ScrollViewer>` en `:11`) | `:14-17` | `:20` (`MaxWidth=620`) | `:117` | `:121` |

#### Pares etiqueta+control a envolver, por archivo (verificados)

| Archivo | Campos |
|---|---|
| `FuenteFinanciamientoFormView` | `:20-21` "Nombre" |
| `RubroGastoFormView` | `:20-21` "Código", `:23-24` "Nombre" |
| `IngresoFormView` | `:21-26` "Fecha" (`CalendarDatePicker`), `:28-29` "Concepto", `:31-41` "Fuente de financiamiento" (`ComboBox` con `ItemTemplate`), `:43-45` "Monto" |
| `LineaPoaFormView` | `:21-22` "Nombre", `:24-25` "Programa", `:27-28` "Ejercicio" |
| `GastoFormView` | `:22-…` "Proveedor" (`ComboBox`) y los siguientes — **NO VERIFIQUÉ los pares de `:31` a `:110`**; abrir el archivo entero antes de aplicar |

**Lo que NO se envuelve en `CampoFormulario`:**
- `LineaPoaFormView:31` `<TextBlock Text="Asignaciones presupuestales" FontWeight="SemiBold" Margin="0,8,0,0" />`
  → es un **título de sección**, no una etiqueta de campo: pasa a `Classes="seccion"` y pierde el
  `FontWeight` literal. **Cuidado T5:** el selector global `TextBlock.seccion` de
  `Typography.axaml:33` le gana a cualquier `Style` anidado; no hace falta nada más.
- `LineaPoaFormView:33-62` el `ItemsControl` de asignaciones: queda tal cual. Los `ComboBox`/`TextBox`
  de cada fila **no** se envuelven (están en una grilla de 3 columnas, no en un formulario vertical).
- `GastoFormView:112-114` el `ContentControl` del `AdjuntosPanelView`: no se toca acá (lo toca 8.4).

- [ ] **Step 1: escribir el guardián que falla**

Agregar 5 filas con `titulo: null` (el título es `{Binding Titulo}` y `PatronHelpers.Montar` no
asigna DataContext — mismo criterio que las filas P3 de la Task 6.0) y `eyebrow: "FINANZAS"`:
```csharp
[InlineData(typeof(FuenteFinanciamientoFormView), null, "FINANZAS")]
[InlineData(typeof(RubroGastoFormView), null, "FINANZAS")]
[InlineData(typeof(IngresoFormView), null, "FINANZAS")]
[InlineData(typeof(LineaPoaFormView), null, "FINANZAS")]
[InlineData(typeof(GastoFormView), null, "FINANZAS")]
```
y los 5 tipos a `VistasDeLaTanda`.

- [ ] **Step 2: correr y ver el rojo**

Expected: **5 fallos nuevos**, todos `Vista_TieneHeaderVistaConElTituloEsperado` ("no tiene un
HeaderVista"). `Vista_TieneMargenExteriorEstandar` tiene que pasar **desde ya** en las 5: las 5
tienen `Margin="24"` literal, que produce el mismo `Thickness` que `MargenVista` (fenómeno ya
documentado en los Rulings B-9 y B-15). **No es falso verde**; anotarlo.

- [ ] **Step 3: aplicar la tabla a las 5, de la más chica a la más grande**

Orden sugerido: `FuenteFinanciamientoFormView` (1 campo, sanity check de P3-b) →
`RubroGastoFormView` → `IngresoFormView` → `LineaPoaFormView` → `GastoFormView`.

- [ ] **Step 4: ver el verde**

Run: `--filter "FullyQualifiedName~GuardianDePatron"`. Expected: PASS.

- [ ] **Step 5: validar el seam por mutación, DENTRO de una de estas vistas**

`IngresoFormView` es la mejor candidata: tiene `TextBox` (`:29`, `:44`), `ComboBox` (`:32`) y
`CalendarDatePicker` (`:22`), o sea los tres tipos que `Controls.axaml` alcanza por selector.

Poner temporalmente, sobre el `TextBox` de "Monto" **ya envuelto en `CampoFormulario`**:
`DataValidationErrors.ErrorConverter="{x:Null}"` (**sin paréntesis** — Ruling B-10).
Expected: el `TextBox` deja de mostrar el error de validación.

> **⚠ Problema conocido, resolvelo en la task:** las 5 vistas de esta task **no tienen ningún test
> de UI**, así que no hay ningún assert que se ponga rojo con esa mutación. La mutación sola no
> prueba nada. Dos salidas, elegí una y anotala en el ledger:
> **(a)** correr `--filter "FullyQualifiedName~MovimientoFormControlValidacion"` como red *ajena*
> — prueba que el seam global sigue vivo, pero **no** que sobrevivió en estas 5 vistas;
> **(b, recomendada)** escribir en esta task un test nuevo,
> `FinanzasFormValidacionTests.cs`, que monte `IngresoFormView` con un
> `IngresoFormViewModel` real, meta un monto inválido y assertee que
> `DataValidationErrors.GetHasErrors(textBox)` es `true` — y **recién ahí** hacer la mutación y ver
> el rojo. Es un test más que la tanda no tenía prevista, y es la única forma de que el Step 5
> signifique algo.

- [ ] **Step 6: grep de residuos y de plantillas (Ruling B-16)**

```bash
grep -n 'Opacity="0\.\|Foreground="Red"\|Margin="24"\|titulo-vista\|FontSize="' \
  src/StockApp.Presentation/Views/Finanzas/{FuenteFinanciamiento,RubroGasto,LineaPoa,Gasto,Ingreso}Form*.axaml
```
Expected: cero coincidencias. (Ninguna de las 5 tiene `Opacity` literal hoy — verificado — pero el
grep se corre igual: es la red de las plantillas.)

- [ ] **Step 7: suite completa + commit**

```
feat(ui): aplica el sistema de diseno a los 5 formularios de Finanzas

Los formularios de Finanzas NO son el P3 de Catalogo (tarjeta centrada de
MaxWidth 380): son DockPanel Margin=24 + titulo-vista arriba + Border.card
VerticalAlignment=Top con MaxWidth por vista y HorizontalAlignment=Left. Se
nombra P3-b y se toma esta task como su referencia.

- HeaderVista sale FUERA de la card (DockPanel.Dock="Top"), a diferencia de
  P3-a donde va adentro
- CampoFormulario absorbe los pares label+control, sin Requerido: ninguna
  etiqueta de estos 5 dice "(obligatorio)", asi que activarlo AGREGARIA una
  marca que hoy no existe (el Ruling B-1 convertia un sufijo existente, no
  inventaba uno)
- Los 5 Foreground="Red" pasan a DangerBrush
- "Asignaciones presupuestales" (LineaPoaFormView) es titulo de seccion, no
  etiqueta de campo: pasa a Classes="seccion"
```

**Riesgos específicos:**
- **El seam sin red propia.** Ver el aviso del Step 5. Es el riesgo #1 de esta task.
- `GastoFormView` es la única con `<ScrollViewer>` envolviendo al `DockPanel` (`:11-12`). El
  `HeaderVista` va dentro del `DockPanel`, con `DockPanel.Dock="Top"` — **no** afuera del
  `ScrollViewer`, o el título dejaría de scrollear con el contenido y `MargenExteriorDe` (que busca
  el primer `Layoutable` con `Margin != default`) empezaría a devolver otra cosa.
- `IngresoFormView:58-59` y `GastoFormView:126-127` tienen el botón "Guardar" gateado por
  `PuedeRegistrarIngresos` / `PuedeRegistrarGastos`. **Conservar el `IsVisible` textual**, sin
  reescribirlo, al mover el `StackPanel` de botones (misma regla que la Task 6.3).

---

### Task 8.3: P1 sobre los 7 listados de Finanzas

**Files:**
- Modify: `Views/Finanzas/GastosView.axaml` (181 l), `IngresosView.axaml` (76 l),
  `ControlPoaView.axaml` (46 l), `LibroCajaView.axaml` (122 l), `CalendarioPagosView.axaml` (108 l),
  `PagosGastoView.axaml` (155 l), `HistorialImportacionesView.axaml` (46 l)
- Test: `GuardianDePatronTests.cs` (+6 `InlineData` + 6 en `VistasDeLaTanda`,
  +1 en `VistasEmbebidas`)

**Interfaces:**
- Consumes: `c:HeaderVista`, `c:BadgeEstado`, P1 (Task 6.1), P2-emb/P1-emb (Task 8.1), la red de
  gates migrada en la Task 8.0.
- Produces: nada nuevo — es aplicación.

**Riesgo: MEDIO-ALTO.** 13 tests de UI a preservar (`GastosViewTests` 2, `IngresosViewTests` 5,
`PagosGastoViewTests` 6), 5 grillas en `LibroCajaView`, 3 raíces con `Margin="16"` en vez de `"24"`,
y el segundo botón primario de `GastosView`.

**Cómo localizan los 13 tests (verificado — esto es lo que NO se puede tocar):**

| Test file | Helper | Depende de |
|---|---|---|
| `GastosViewTests` | `BuscarBotonPorTexto` → `.OfType<Button>().First(b => (b.Content as string) == texto)` | los literales **`"Nuevo gasto"`**, **`"Editar"`** |
| `IngresosViewTests` | ídem + `BuscarTextoPorContenido` → `.OfType<TextBlock>().First(t => t.Text == texto)` | **`"Nuevo ingreso"`**, **`"Solo lectura"`** |
| `PagosGastoViewTests` | ídem + `BuscarPanelFormularioPago` → `.OfType<Control>().First(c => c.Name == "PanelRegistrarPago")` | **`"Registrar pago"`**, **`"Volver"`**, **`"Solo lectura"`**, y el **`x:Name="PanelRegistrarPago"`** de `PagosGastoView:59` |

**Ninguno localiza por `Classes` ni por posición**, así que degradar un `primary` a `secondary` o
mover un botón de contenedor es seguro. **Lo que NO se puede hacer:** cambiar un `Content` de
botón, cambiar el texto `"Solo lectura"`, o mover/renombrar `x:Name="PanelRegistrarPago"`.

#### `GastosView.axaml` (P1)

| Línea | Hoy | Pasa a |
|---|---|---|
| `:13` | `<DockPanel Margin="24">` | `<DockPanel Margin="{DynamicResource MargenVista}">` |
| `:15-18` | `<TextBlock DockPanel.Dock="Top" Text="Gastos y facturas" Classes="titulo-vista" Margin="0,0,0,16" />` | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="FINANZAS" Titulo="Gastos y facturas">` + el `StackPanel` de acciones de `:107-134` migrado al slot `Acciones` |
| `:21-104` | `<Border DockPanel.Dock="Top" Classes="card" Margin="0,0,0,12">` con el `WrapPanel` de filtros | **se queda entero en la card** — son filtros de grilla, no acciones de vista (mismo criterio que el `TextBox` de búsqueda de `ProductoListView`) |
| `:100` | `<Button Classes="primary" Content="Filtrar" …/>` | **`Classes="secondary"`** — Ruling B-18, caso (1): compite con "Nuevo gasto" |
| `:101` | `<Button Classes="secondary" Content="Limpiar" …/>` | se queda |
| `:99` | `<StackPanel Orientation="Horizontal" Spacing="8">` | `Spacing="{DynamicResource Espacio2}"` |
| `:107` | `<StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">` | migra al slot `Acciones` del `HeaderVista`; `Spacing="{DynamicResource Espacio2}"`; el `DockPanel.Dock` y el `Margin` desaparecen |
| `:113-127` | 4 `Button` con `IsVisible="{Binding PuedeRegistrarGastos}"` / `{Binding PuedeRegistrarPagos}` | migran **conservando cada `IsVisible` textual, uno por uno** (regla de la Task 6.3) |
| `:128-133` | `<Button Classes="secondary" Command="{Binding ExportarCsvCommand}">` con `i:Icon` + `TextBlock` adentro | migra igual; `Spacing="6"` → literal (T1) o `{DynamicResource Espacio1}` en el `StackPanel` interno solo si querés 4 en vez de 6 — **recomendado dejarlo en 6**, no vale un cambio visual por un token |
| `:24`, `:32`, `:40`, `:53`, … | `Margin="0,0,12,8"` de cada filtro del `WrapPanel` | **se quedan literales** (T1) |

**Jerarquía resultante:** un solo `primary` ("Nuevo gasto"), gateado. Cuando
`PuedeRegistrarGastos` es `false`, la vista queda sin primario — es correcto y ya es el
comportamiento actual.

#### `IngresosView.axaml` (P1)

| Línea | Hoy | Pasa a |
|---|---|---|
| `:12` | `<DockPanel Margin="24">` | `Margin="{DynamicResource MargenVista}"` |
| `:14-17` | `TextBlock` "Ingresos de caja" `titulo-vista` | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="FINANZAS" Titulo="Ingresos de caja">` |
| `:19-46` | `<StackPanel DockPanel.Dock="Top" … Spacing="8" Margin="0,0,0,12">` con 3 botones + el `TextBlock` "Solo lectura" | migra **entero** al slot `Acciones`, incluido el `TextBlock` de `:44-45` — es un indicador de estado de la barra de acciones, no de la grilla. `Spacing="{DynamicResource Espacio2}"` |
| `:26-35` | los 3 `IsVisible="{Binding PuedeRegistrarIngresos}"` | textuales, uno por uno |
| `:44-45` | `IsVisible="{Binding !PuedeRegistrarIngresos}"` | textual |
| `:48-72` | `<Border Classes="card">` con el `DataGrid` | no se toca |

#### `PagosGastoView.axaml` (P1)

| Línea | Hoy | Pasa a |
|---|---|---|
| `:15` | `<DockPanel Margin="24">` | `Margin="{DynamicResource MargenVista}"` |
| `:17-20` | `TextBlock` "Pagos de la factura" `titulo-vista` `Margin="0,0,0,8"` | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="FINANZAS" Titulo="Pagos de la factura" Resumen="{Binding TituloGasto}">` |
| `:21-24` | `<TextBlock DockPanel.Dock="Top" Text="{Binding TituloGasto}" TextWrapping="Wrap" Margin="0,0,0,16" />` | **se borra** — su contenido pasa a `Resumen` del `HeaderVista` (mismo movimiento que la Task 6.6 hizo con "Sesión iniciada como X" en `InicioView`). **Antes de borrarlo, grep:** `grep -rn 'TituloGasto' tests/` → si algún test lo localiza como `TextBlock` suelto, no se borra |
| `:27-49` | `<Border Classes="card">` de resumen, con 4 `Opacity="0.7"` | ver abajo |
| `:30`, `:35`, `:40`, `:45` | `<TextBlock Text="Monto total"/"Pagado"/"Saldo"/"Estado" Opacity="0.7" />` | `Classes="caption"` (que ya aplica `TextoSecundarioBrush`) — **estos 4 SÍ los ve el guardián**, son los únicos 4 de las 19 vistas de Finanzas fuera de una plantilla |
| `:28` | `<StackPanel Orientation="Horizontal" Spacing="32">` | `Spacing="{DynamicResource Espacio6}"` (32) |
| `:59` | `<StackPanel x:Name="PanelRegistrarPago" Spacing="8" IsVisible="{Binding PuedeRegistrarPagos}">` | **`x:Name` e `IsVisible` intocables.** `Spacing="{DynamicResource Espacio2}"` |
| `:61` | `<TextBlock Text="Registrar pago" FontWeight="SemiBold" />` | `Classes="seccion"` (T5: el selector global gana; sacar el `FontWeight` literal) |
| `:63`, `:71`, `:75` | `Margin="0,0,12,8"` de los 3 campos del `WrapPanel` | se quedan literales (T1) |
| `:81` | `Foreground="Red"` | `{DynamicResource DangerBrush}` |
| `:88-89`, `:93`, `:97-98` | botones y "Solo lectura" | **Content e `IsVisible` intocables** (los 3 los localizan los tests) |
| `:125` | `Opacity="0.8"` **dentro de un `DataTemplate`** | `Foreground="{DynamicResource TextoTerciarioBrush}"` — **el guardián NO lo ve** (Ruling B-16) |
| `:127-129` | `<Border Grid.Column="3" Classes="badge-inactiva" IsVisible="{Binding !Activo}"><TextBlock Text="Anulado" Classes="badge-inactiva-texto" /></Border>` | `<c:BadgeEstado Grid.Column="3" Texto="Anulado" Tono="Neutro" IsVisible="{Binding !Activo}" />` — conservar `"Anulado"` literal |

#### `ControlPoaView.axaml` y `LibroCajaView.axaml` y `CalendarioPagosView.axaml` — las 3 con `Margin="16"`

Son las únicas 3 vistas de primer nivel de Finanzas cuya raíz **no** es `Margin="24"`. Su fila de
`Vista_TieneMargenExteriorEstandar` es **rojo real desde el Step 2** — a diferencia de todo lo demás
de B2, donde el margen ya coincidía por valor.

| Archivo | Línea | Hoy | Pasa a |
|---|---|---|---|
| `ControlPoaView` | `:12` | `<Grid RowDefinitions="Auto,Auto,*" Margin="16">` | `Margin="{DynamicResource MargenVista}"` |
| `ControlPoaView` | `:14` | `<TextBlock Grid.Row="0" Text="Control POA" Classes="titulo-vista" />` | `<c:HeaderVista Grid.Row="0" Eyebrow="FINANZAS" Titulo="Control POA">` + los 2 botones de `:18-19` en el slot `Acciones` |
| `ControlPoaView` | `:16-20` | `<StackPanel Grid.Row="1" … Spacing="12" Margin="0,12">` con `NumericUpDown` + 2 `Button` **sin `Classes`** | el `NumericUpDown` (`:17`) **se queda** como filtro en `Grid.Row="1"`; los 2 botones migran al header. `Classes="primary"` a **"Actualizar"** (es la acción que ejecuta la consulta, igual que "Buscar" en Reportes) y `Classes="secondary"` a "Exportar CSV" |
| `ControlPoaView` | `:37` | `SignoNegativoBrushConverter` | **no se toca acá** — lo cierra la Task B2-T |
| `LibroCajaView` | `:12` | `<Grid RowDefinitions="Auto,Auto,*,Auto" Margin="16">` | `Margin="{DynamicResource MargenVista}"` |
| `LibroCajaView` | `:14` | `<TextBlock Grid.Row="0" Text="Libro caja" Classes="titulo-vista" />` | `<c:HeaderVista Grid.Row="0" Eyebrow="FINANZAS" Titulo="Libro caja">` + `:27` "Actualizar" (`primary`) y `:28` "Exportar CSV" (`secondary`) en `Acciones` — **`:28` conserva su `IsVisible="{Binding !VerAnioCompleto}"` textual** |
| `LibroCajaView` | `:16` | `<StackPanel Grid.Row="1" … Spacing="12" Margin="0,12">` | `Spacing="{DynamicResource Espacio3}"`; `Margin="0,12"` literal (T1). Quedan adentro: `:17-25` (Año/Mes), `:26` (CheckBox "Año completo"), `:29-35` (los 4 `TextBlock` de saldo) |
| `LibroCajaView` | `:18`, `:22` | `<TextBlock Text="Año"/"Mes" Classes="caption" />` | ya están bien, no se tocan |
| `LibroCajaView` | `:35`, `:74` | `SignoNegativoBrushConverter` | **no se toca acá** — Task B2-T |
| `CalendarioPagosView` | `:12` | `<StackPanel Margin="16" Spacing="20">` (dentro de `<ScrollViewer>` en `:11`) | `Margin="{DynamicResource MargenVista}"`, `Spacing="{DynamicResource Espacio5}"` (24, **cambia de 20 a 24** — es el paso de escala más cercano; si preferís no mover píxeles, dejá `Spacing="20"` literal y anotalo) |
| `CalendarioPagosView` | `:14` | `<TextBlock Text="Calendario de pagos" Classes="titulo-vista" />` | `<c:HeaderVista Eyebrow="FINANZAS" Titulo="Calendario de pagos">` + el botón de `:15` en `Acciones` |
| `CalendarioPagosView` | `:15` | `<Button Content="Actualizar" … HorizontalAlignment="Left" />` sin `Classes` | `Classes="primary"`, sin el `HorizontalAlignment` (el slot `Acciones` ya alinea) |
| `CalendarioPagosView` | `:33-36`, y sus 2 gemelos | 3 `<Button Content="Registrar pago">` **sin `Classes`**, dentro de `ItemsControl.ItemTemplate`, con `IsVisible="{Binding $parent[UserControl].(…).PuedeRegistrarPagos}"` | `Classes="secondary"` a los 3 (Ruling B-18 caso 2: acción de fila, nunca `primary`). **`IsVisible` textual, intocable.** **NO VERIFIQUÉ** las líneas exactas de los otros 2 (`:55-58` y `:77-80` según el barrido); abrir el archivo |

#### `HistorialImportacionesView.axaml` (P1-emb)

| Línea | Hoy | Pasa a |
|---|---|---|
| `:12` | `<DockPanel Margin="24">` | **`<DockPanel>`, sin `Margin`** — Ruling B-17: está embebida en el `TabControl` de `ImportacionView`, que ya trae `Margin="24"` en `:11`. Hoy son 48 px |
| `:14` | `<StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">` | `Spacing="{DynamicResource Espacio2}"`; `Margin` literal |
| `:15` | `<Button Classes="secondary" Content="Revertir" …/>` | se queda `secondary` (única acción; no hay `primary` porque revertir no es la acción deseable por defecto) |
| — | no tiene título | **no gana `HeaderVista`**: el `TabItem Header="Historial"` de `ImportacionView:22` ya lo pone |

- [ ] **Step 1: línea base de los 13 tests**

Run: `--filter "FullyQualifiedName~GastosViewTests|FullyQualifiedName~IngresosViewTests|FullyQualifiedName~PagosGastoViewTests"` (timeout 600000)
Expected: PASS **13/13**. **Anotar el resultado.** Si no está verde antes de tocar nada, parar (y
revisar si la Task 8.0 dejó algo).

- [ ] **Step 2: escribir el guardián que falla**

`[InlineData]` + `VistasDeLaTanda`:
```csharp
[InlineData(typeof(GastosView), "Gastos y facturas", "FINANZAS")]
[InlineData(typeof(IngresosView), "Ingresos de caja", "FINANZAS")]
[InlineData(typeof(ControlPoaView), "Control POA", "FINANZAS")]
[InlineData(typeof(LibroCajaView), "Libro caja", "FINANZAS")]
[InlineData(typeof(CalendarioPagosView), "Calendario de pagos", "FINANZAS")]
[InlineData(typeof(PagosGastoView), "Pagos de la factura", "FINANZAS")]
```
`VistasEmbebidas` += `typeof(HistorialImportacionesView)`.

- [ ] **Step 3: correr y ver el rojo — y verificar que sean EXACTAMENTE estos**

Expected, **11 fallos nuevos**:
- 6 × `Vista_TieneHeaderVistaConElTituloEsperado` (las 6 de primer nivel, "no tiene un HeaderVista")
- 3 × `Vista_TieneMargenExteriorEstandar` (`ControlPoaView`, `LibroCajaView`, `CalendarioPagosView`:
  `Expected: 24,24,24,24 / Actual: 16,16,16,16`) — **el único rojo de margen real de todo B2**
- 1 × `Vista_NoTieneUnSegundoBotonPrimario` para `GastosView` ("2 botones primarios visibles a la
  vez") — Ruling B-18 caso (1)
- 1 × `VistaEmbebida_NoDuplicaElMargenDeVista` para `HistorialImportacionesView`

`Vista_NoTieneOpacidadesLiterales` da rojo para `PagosGastoView` (**4** literales, los de `:30`,
`:35`, `:40`, `:45`) — si reporta 5, el helper está contando también el de `:125`, que vive en una
plantilla no realizada: eso sería un cambio de comportamiento de `PatronHelpers` y hay que
investigarlo antes de seguir.

- [ ] **Step 4: aplicar las 7 tablas**

Orden: `HistorialImportacionesView` (la más chica) → `IngresosView` → `ControlPoaView` →
`CalendarioPagosView` → `LibroCajaView` → `PagosGastoView` → `GastosView` (la más grande).

- [ ] **Step 5: los 13 tests, otra vez**

Run: el mismo filtro del Step 1. Expected: PASS **13/13 sin tocar un solo assert.**
Si alguno pide tocarse, el refactor cambió un `Content`, un `x:Name` o un `IsVisible`. Volver atrás.

- [ ] **Step 6: ver el verde del guardián**

- [ ] **Step 7: grep de plantillas (Ruling B-16, obligatorio — esta task toca 4 `ItemTemplate`)**

```bash
grep -n 'Opacity="0\.\|Foreground="Red"\|Margin="24"\|Margin="16"\|titulo-vista\|badge-inactiva\|FontSize="' \
  src/StockApp.Presentation/Views/Finanzas/{Gastos,Ingresos,ControlPoa,LibroCaja,CalendarioPagos,PagosGasto,HistorialImportaciones}View.axaml
```
Expected: cero coincidencias.

- [ ] **Step 8: validar por mutación (4 mutaciones, 4 rojos, revertir cada una)**

1. **La que importa de verdad** — borrar `IsVisible="{Binding PuedeRegistrarPagos}"` del
   `x:Name="PanelRegistrarPago"` (`PagosGastoView:59-60`) →
   `Montar_OperadorSinRegistrarPagos_OcultaFormularioCompleto` **rojo**. Revertir.
   *Es el modo de falla exacto que este refactor puede introducir: perder un `IsVisible` al mover
   un bloque.*
2. Borrar `IsVisible="{Binding PuedeRegistrarGastos}"` de `GastosView:113-114` ("Nuevo gasto") →
   `Montar_OperadorSinRegistrarGastos_OcultaNuevoYEditar` **rojo**. Revertir.
3. Volver `GastosView:100` ("Filtrar") a `Classes="primary"` →
   `Vista_NoTieneUnSegundoBotonPrimario` **rojo** para `GastosView`. Revertir.
4. Poner `Margin="{DynamicResource MargenVista}"` en `HistorialImportacionesView:12` →
   `VistaEmbebida_NoDuplicaElMargenDeVista` **rojo**. Revertir.

- [ ] **Step 9: suite completa + commit**

```
feat(ui): aplica el sistema de diseno a los 7 listados de Finanzas

- HeaderVista en las 6 vistas de primer nivel; las barras de accion migran al
  slot Acciones conservando cada IsVisible de gating TEXTUALMENTE
- ControlPoaView, LibroCajaView y CalendarioPagosView pasan de Margin="16" a
  MargenVista: son las 3 unicas raices de Finanzas que no coincidian ya por
  valor con el token, y las unicas filas de margen que dieron rojo REAL en
  todo B2
- GastosView tenia DOS botones primarios simultaneos ("Filtrar" en la card de
  filtros y "Nuevo gasto" en la barra de acciones). "Filtrar" pasa a
  secondary: la accion principal de la vista es dar de alta, no filtrar
- HistorialImportacionesView pierde su Margin="24": esta embebida en el
  TabControl de ImportacionView, que ya lo aplica -- eran 48px
- Los 4 Opacity="0.7" del resumen de PagosGastoView pasan a Classes="caption".
  El quinto (:125) vive dentro de un DataTemplate y el guardian no lo ve
  (Ruling B-16): se migro a mano, verificado por grep

Los 13 tests de gating de Gastos/Ingresos/Pagos pasan sin tocar un solo
assert: localizan por Content y por x:Name, no por Classes.
```

**Riesgos específicos:**
- **`PagosGastoView:21-24`** — borrar el `TextBlock` de `{Binding TituloGasto}` para pasarlo a
  `Resumen` es el único borrado de control de la task. El grep del Step del cuadro es bloqueante.
- **`LibroCajaView` tiene 5 `DataGrid`** (`:38`, `:62`, `:84`, `:99`, `:110`) y 3 bloques
  alternativos gobernados por `IsVisible="{Binding VerAnioCompleto}"` / `{Binding
  !VerAnioCompleto}` (`:39`, `:59`, `:95`). No consolidar esos bindings; moverlos textualmente si
  hay que moverlos.
- **`CalendarioPagosView`** tiene el mismo `Content="Registrar pago"` **tres veces**, en tres
  `ItemTemplate` distintos. Cualquier helper de test que busque "el primer botón con ese texto"
  encontraría uno arbitrario. No hay test hoy — pero si escribís uno, no lo localices por texto.

---

### Task 8.4: `NuevaImportacionView` (P8) + `AdjuntosPanelView` (P5) — la task crítica

**Files:**
- Modify: `Views/Finanzas/NuevaImportacionView.axaml` (509 l)
- Modify: `Views/Finanzas/AdjuntosPanelView.axaml` (63 l)
- Create: `tests/StockApp.Presentation.UiTests/NuevaImportacionJerarquiaBotonesTests.cs`
- Test: `GuardianDePatronTests.cs` (+2 en `VistasEmbebidas`, con exclusión documentada)

**Interfaces:**
- Consumes: P8 (sección 1), P5, `TextoTerciarioBrush`, `Classes="seccion"`.
- Produces: el único guardián de jerarquía de pasos del repo.

**Riesgo: CRÍTICO.** Es la vista más grande y más frágil del repo. Va **sola**, al final de la
tanda, con su propio commit.

#### Análisis de riesgo de `NuevaImportacionView` (todo verificado abriendo el archivo)

| Qué | Dónde | Por qué es peligroso |
|---|---|---|
| `x:Name="Root"` | `:13` | 5 bindings `{Binding #Root.DataContext.…}` (`:124`, `:198`, `:234`, `:367`, `:435`). **4 de los 5 están dentro de columnas con `x:CompileBindings="False"`** → romperlo es null silencioso, sin AVLN2000. **Ruling B-21** |
| `x:CompileBindings="False"` | `:20`, `:31` (dos `<Style Selector="DataGridRow">`), `:106`, `:184`, `:212`, `:353` (cuatro `DataGridTemplateColumn`) | **6 zonas sin red del compilador**, no 2 como decía el esbozo |
| 4 `DataGrid` | `x:Name="GridGastos"` `:84`, el de ingresos `:298`, `x:Name="GridLineasPoa"` `:398`, el de conflictos `:498` | 2 tienen `x:Name` que usan `NuevaImportacionGastosGridTests` y `NuevaImportacionLineasPoaGridTests` |
| 3 pasos mutuamente excluyentes | `:40-62` (Cargar), `:65-476` (Revisar), `:479-506` (Resultado) | los `IsVisible` de `:41`, `:65`, `:480` comparan `PasoActual` contra los 3 valores de `PasoWizardImportacion` |
| 3 `Classes="primary"` | `:59` "Analizar", `:74` "Confirmar", `:492` "Nueva importación" | uno por paso; correctos. **Ruling B-18 caso (3)** |
| 3 `Classes="titulo-vista"` | `:42` "Paso 1 · Cargar planillas", `:66` "Paso 2 · Revisar", `:481` "Paso 3 · Resultado" | **son títulos de paso, no de vista.** Los 3 pasan a `Classes="seccion"` |
| 10 `Opacity="0.5"` + 10 `FontSize="12"` | `:93`, `:111`, `:156`, `:172`, `:189`, `:217`, `:307`, `:325`, `:341`, `:358` (`<i:Icon Value="mdi-lock" …>`) | **los 10 dentro de `CellTemplate`: el guardián NO los ve** (Ruling B-16) |
| `Margin="24"` | `:37` (`<Grid Margin="24">`) | embebida en `ImportacionView` (`:20`), que ya trae `Margin="24"` en `:11` → **48 px** (bug C3) |
| 10 tests, 3 archivos | `NuevaImportacionGastosGridTests`, `NuevaImportacionLineasPoaGridTests`, `NuevaImportacionCondicionCreditoTests` | **NO VERIFIQUÉ** cómo localizan sus controles. Step 1 lo obliga |

#### Tabla de sustitución — `NuevaImportacionView.axaml`

| Línea | Hoy | Pasa a |
|---|---|---|
| `:13` | `x:Name="Root"` | **INTOCABLE.** Ruling B-21 |
| `:16-35` | `<UserControl.Styles>` con los 2 `<Style Selector="DataGridRow" x:CompileBindings="False">` | **no se tocan** (y sus comentarios `:17-19` y `:24-30` tampoco: documentan por qué existen). **Se les AGREGA** el `<Style Selector="i|Icon.candado">` del renglón de abajo |
| **nuevo, dentro de `:16-35`** | — | `<Style Selector="i\|Icon.candado"><Setter Property="Opacity" Value="0.5" /><Setter Property="FontSize" Value="12" /></Style>` — Ruling B-11: como `Setter` de `Style`, su `Priority` es `Style`, no `LocalValue`, así que el guardián lo exime **y sigue aplicándose** |
| `:37` | `<Grid Margin="24">` | **`<Grid>`, sin `Margin`** — Ruling B-17 |
| `:41`, `:65`, `:480` | los 3 `IsVisible="{Binding PasoActual, Converter=…, ConverterParameter=…}"` | **INTOCABLES, textuales** |
| `:42`, `:66`, `:481` | `Classes="titulo-vista"` | `Classes="seccion"` (T5: el selector global de `Typography.axaml:33` gana solo) |
| `:66` | `Margin="0,0,0,8"` en el título del paso 2 | se queda literal (T1) |
| `:40`, `:45`, `:69`, `:79`, `:479`, `:484` | `Spacing="12"` / `Spacing="8"` | `{DynamicResource Espacio3}` / `{DynamicResource Espacio2}` |
| `:46`, `:50`, `:54`, `:490` | `Spacing="8"` de las filas horizontales | `{DynamicResource Espacio2}` |
| `:70` | `Spacing="16"` | `{DynamicResource Espacio4}` |
| `:59`, `:74`, `:492` | los 3 `Classes="primary"` | **se quedan.** Ruling B-18 caso (3) |
| `:93`, `:111`, `:156`, `:172`, `:189`, `:217`, `:307`, `:325`, `:341`, `:358` | `<i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableX}" FontSize="12" Opacity="0.5" />` | `<i:Icon Value="mdi-lock" Classes="candado" IsVisible="{Binding !EsEditableX}" />` — **`IsVisible` textual, uno por uno.** Los 10 son el bloque que el guardián no ve |
| `:451` | `<StackPanel Orientation="Horizontal" Spacing="24" Margin="12">` | `Spacing="{DynamicResource Espacio5}"`; `Margin="12"` literal (T1) |
| `:465` | `Margin="0,4"` | literal (T1) |
| `:84`, `:398` | `x:Name="GridGastos"`, `x:Name="GridLineasPoa"` | **INTOCABLES** |
| `:106`, `:184`, `:212`, `:353` | `x:CompileBindings="False"` en 4 columnas | **no se tocan** — sacarlos rompe el build (los bindings de adentro no tipe-chequean contra un `x:DataType` único) |
| `:498` | `<DataGrid ItemsSource="{Binding Conflictos}" …>` | no se toca |
| `:497` | `<TextBlock Text="Conflictos (no se escribieron — resolvé a mano)" FontWeight="SemiBold" />` | `Classes="seccion"`, sacando el `FontWeight` literal. **El texto no se toca** (copy) |

#### Tabla de sustitución — `AdjuntosPanelView.axaml` (P5, armonización con su gemelo)

Ruling B-13 (aprobado): **no se fusiona nada.** Solo que los dos paneles se vean idénticos.

| Línea | Hoy | Pasa a |
|---|---|---|
| `:13` | `<DockPanel>` | se queda **sin margen** (P5 embebido) |
| `:15` | `<Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,8">` | `Margin` literal (T1) |
| `:16-18` | `<TextBlock Grid.Column="0" Text="Adjuntos" Classes="titulo-vista" />` | **`Classes="seccion"`** — es un título de sección dentro de un formulario, no el título de una vista. Mismo movimiento que la Task 6.6 hizo con los `Classes="seccion"` de `InicioView` |
| `:19-23` | `<Button Classes="secondary" Content="Agregar" IsEnabled="{Binding PuedeModificar}" />` | `IsEnabled` **textual, intocable** |
| `:28-29` | `<Border Classes="card" Margin="0,0,0,8">` | se queda |
| `:35`, `:39`, `:44`, `:50` | `Margin="8,0"` / `Margin="8,0,0,0"` | se quedan literales (T1) |
| `:54` | `IsEnabled="{Binding $parent[UserControl].(…).PuedeModificar}"` | **textual, intocable** |

**El mismo cambio de `titulo-vista` → `seccion` se aplica a `AdjuntosDocumentoPanelView:17` en la
Task 9.3.** Eso es toda la "armonización" que el Ruling B-13 pide: los dos paneles quedan
idénticos salvo por los nombres de propiedad de su ViewModel, que es exactamente lo que el ruling
decidió no reconciliar.

- [ ] **Step 1: línea base de los 10 tests, Y documentar cómo localizan**

Run: `--filter "FullyQualifiedName~NuevaImportacion"` (timeout 600000)
Expected: PASS 10/10. **Anotar el número exacto** (`grep -c '\[AvaloniaFact\]'` sobre los 3
archivos — el esbozo dice 10 y **no lo verifiqué**).

**Step bloqueante:** abrir los 3 archivos de test y escribir en el ledger, por cada uno, **cómo
localiza sus controles** (`x:Name`? `Content`? índice de tipo? `DataContext`?) y **qué strings
literales** son intocables. Sin esa tabla no se toca el XAML. *Precedente: la Task 6.7 pudo mover
todo `IngresoPorFacturaView` sin romper 14 tests precisamente porque esa tabla existía.*

- [ ] **Step 2: grep de `#Root` — línea base**

```bash
grep -n '#Root' src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml
```
Expected: **5 coincidencias**, en `:124`, `:198`, `:234`, `:367`, `:435`. Anotarlas. Este grep se
repite idéntico en el Step 8.

- [ ] **Step 3: escribir el test de jerarquía de pasos que falla**

Crear `NuevaImportacionJerarquiaBotonesTests.cs`. Montar `NuevaImportacionView` **con un
`NuevaImportacionViewModel` real** (los fakes ya existen: `NuevaImportacionFakes.cs`), y para cada
valor de `PasoWizardImportacion` (`Cargar`, `Revisar`, `Resultado`):
1. setear `vm.PasoActual`, correr `Dispatcher.UIThread.RunJobs()`;
2. contar `window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primary") && ArbolVisual.EsVisibleEnArbol(b))`;
3. `Assert.Single(...)` y assertear **cuál** es (`"Analizar"` / `"Confirmar"` / `"Nueva importación"`).

**3 casos, uno por paso.** Esto reemplaza al invariante genérico
`Vista_NoTieneUnSegundoBotonPrimario`, que para esta vista sería un falso rojo permanente.

- [ ] **Step 4: correr y ver el rojo**

Expected: **FAIL**, y por la razón correcta: con la vista **sin tocar**, los 3 casos tienen que
pasar en verde (los `IsVisible` de paso funcionan de verdad contra un VM real). **Si pasan en
verde desde el Step 4, está bien**: es una red escrita sobre el XAML actual, igual que la Task 6.2
hizo con `MovimientoHistorialGatesTests`. **El rojo se busca en el Step 5, por mutación.**

- [ ] **Step 5: validar la red por mutación, ANTES de tocar el XAML**

Mutación: borrar `IsVisible="{Binding PasoActual, …Resultado}"` de `:480`.
Expected: el caso de `PasoActual = Revisar` pasa a contar **2** primarios ("Confirmar" y "Nueva
importación") → **rojo real**. Revertir, verde.
*Si no da rojo, el test no custodia nada y no se toca el XAML.*

- [ ] **Step 6: agregar las 2 filas embebidas al guardián, con la exclusión documentada**

`VistasEmbebidas` += `typeof(NuevaImportacionView)`, `typeof(AdjuntosPanelView)`.
**`NuevaImportacionView` se excluye de `Vista_NoTieneUnSegundoBotonPrimario`** (Ruling B-18 caso 3).
Como xUnit no tiene exclusión por fila, la forma limpia es un `if` con `return` temprano y un
comentario, o una `TheoryData` aparte para ese método. **Elegir una y documentarla en el XML doc**:
un `Assert.True(true)` silencioso es exactamente la clase de test que este plan prohíbe.

Expected en el Step 6: `VistaEmbebida_NoDuplicaElMargenDeVista` **rojo** para
`NuevaImportacionView` (`Margin="24"` en `:37`). `AdjuntosPanelView` verde (no tiene margen raíz).

- [ ] **Step 7: aplicar las dos tablas**

`AdjuntosPanelView` primero (63 líneas, cambio trivial), después `NuevaImportacionView`.
**Cuidado T4:** los comentarios existentes de `:17-19`, `:24-30`, `:26-32` tienen guiones; no
introducir ninguno **doble** al editar alrededor.

- [ ] **Step 8: los 4 greps de cierre**

```bash
# 1. #Root sigue intacto: 5 coincidencias, mismas lineas +-
grep -n '#Root' src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml
# 2. los x:Name siguen: Root, GridGastos, GridLineasPoa
grep -n 'x:Name=' src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml
# 3. las 6 zonas sin compiled bindings siguen
grep -n 'x:CompileBindings' src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml
# 4. residuos (Ruling B-16: los 10 Opacity vivian en CellTemplate)
grep -n 'Opacity="0\.\|FontSize="\|titulo-vista\|Margin="24"' \
  src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml \
  src/StockApp.Presentation/Views/Finanzas/AdjuntosPanelView.axaml
```
Expected: (1) 5, (2) 3, (3) 6, (4) **cero**.

- [ ] **Step 9: los 10 tests + el nuevo + el guardián**

Run: `--filter "FullyQualifiedName~NuevaImportacion|FullyQualifiedName~ViewLocator|FullyQualifiedName~GuardianDePatron"`
Expected: PASS. Los 10 preexistentes **sin tocar un solo assert**. `ViewLocatorTests` verde (sus 2
casos de `AdjuntosPanelView`, `:24-31` y `:34-41`, resuelven el tipo por convención de nombre — el
refactor no renombra nada, así que no deberían moverse; si se mueven, algo se renombró).

- [ ] **Step 10: mutación final sobre el XAML ya refactorizado**

Borrar `IsVisible="{Binding !EsEditableProveedor}"` del `i:Icon` de `:111` (ya migrado a
`Classes="candado"`). Expected: **rojo** en algún test de `NuevaImportacionGastosGridTests`.
**Si NO da rojo**, ese gate de edición no tiene guardián: anotarlo como deuda explícita en el
ledger, no inventar un test a las apuradas al final de la task crítica. Revertir.

- [ ] **Step 11: suite completa + commit**

```
feat(ui): aplica el sistema de diseno a NuevaImportacionView y AdjuntosPanelView

NuevaImportacionView es la vista mas grande y mas fragil del repo: 509 lineas,
4 grillas, SEIS zonas con x:CompileBindings="False" (dos Style de DataGridRow y
cuatro DataGridTemplateColumn, no dos como decia el plan) y un x:Name="Root" del
que cuelgan 5 bindings, cuatro de ellos justamente dentro de las columnas sin
compiled bindings: romperlo dejaria los ComboBox vacios en silencio.

- Pierde su Grid Margin="24": esta embebida en el TabControl de ImportacionView,
  que ya lo aplica. Eran 48px
- Los 3 titulo-vista eran titulos de PASO, no de vista: pasan a Classes="seccion"
- Los 10 Opacity="0.5" + FontSize="12" de los iconos mdi-lock viven dentro de
  CellTemplate, donde el guardian no puede verlos (Ruling B-16). Se resuelven con
  un Style de clase (Ruling B-11) para que su Priority sea Style y no LocalValue
- Los 3 botones primarios se QUEDAN: son pasos mutuamente excluyentes de un
  wizard, no acciones que compitan (Ruling B-18). El invariante generico habria
  sido un falso rojo permanente; en su lugar va un test propio que monta la vista
  con ViewModel real, recorre los 3 valores de PasoWizardImportacion y asserta un
  unico primario visible en cada uno. Validado por mutacion: borrar el IsVisible
  del paso Resultado lo pone en rojo con 2 primarios
- AdjuntosPanelView: el TextBlock "Adjuntos" pasa de titulo-vista a seccion.
  Ruling B-13 aprobado: los dos paneles de adjuntos NO se fusionan, solo se
  armonizan visualmente
```

---

### Task 8.5: cierre de la tanda 8

- [ ] **Step 1: suite completa** — `dotnet test StockApp.sln` (timeout 600000, `nohup … &` +
  polling activo sobre el log). Expected: PASS.
- [ ] **Step 2: auditoría de residuos sobre las 19 vistas**
```bash
grep -rn 'Opacity="0\.\|Foreground="Red"\|Margin="24"\|Margin="16"\|titulo-vista\|badge-inactiva\|FontSize="' \
  src/StockApp.Presentation/Views/Finanzas/
```
Expected: **cero coincidencias**, salvo el `Margin="0,12,0,0"` de los 3 maestros embebidos (que no
matchea el patrón) y los `Opacity` vía `ActivoOpacidadConverter` (que tampoco, por ser bindings).
- [ ] **Step 3: verificación orgánica.** La app real, corriendo. Toolkit en
  `scripts/gui-verificacion/`. Recorrer las 19 pantallas — en particular las 5 pestañas
  (`MaestrosFinanzasView` ×3, `ImportacionView` ×2), donde el cambio de margen es lo único que se
  ve. **La spec pide verificación orgánica al cierre de la tanda 8 explícitamente.** Las tandas 6 y
  7 la dejaron pendiente; acá no.
- [ ] **Step 4: commit de cierre** (si el Step 2 o 3 encontró algo; si no, la tanda ya cerró con el
  commit de 8.4).

---

## Tanda 9: Documentos y Tareas (5 vistas)

**Vistas (5):** `DocumentoListView` (P1), `DocumentoFormView` (P3-b), `AdjuntosDocumentoPanelView`
(P5), `TareaListView` (P1), `TareaFormView` (P3-b).

**Eyebrows:** `DOCUMENTOS` para las 3 de Documentos, `TAREAS` para las 2 de Tareas.

**Riesgo: ALTO por deuda, no por tamaño.** Son 599 líneas entre las 5 (menos que
`NuevaImportacionView` sola), pero **10 gates sin red** y 25 tests que preservar.

> **⚠ La tanda arranca cerrando la deuda de la Fase A (Ruling B-3).** Las Tasks 4.3 y 4.4 nunca se
> ejecutaron. **Verificado: `DocumentoFormViewGatesTests.cs` NO existe, y `DocumentoFormViewTests.cs`
> tampoco** — `DocumentoFormView` tiene **cero** cobertura de UI. **No se toca una línea de XAML de
> Documentos antes de que la Task 9.0 esté verde y validada por mutación.**

### Las fórmulas reales, resueltas contra la tabla de transiciones (Ruling B-5, ampliado)

El brief pedía "permisos mixtos". **No aplica**: las 5 fórmulas no leen `PermisosActuales` ni una
vez. Verificado abriendo los tres archivos:

`DocumentoListViewModel.cs:49-57` (clase `DocumentoFila`):
```csharp
public bool PuedeIniciar          => Documento.Estado == EstadoDocumento.Pendiente && Documento.PuedeTransicionarA(EstadoDocumento.EnProceso);
public bool PuedeVolverAPendiente => Documento.PuedeTransicionarA(EstadoDocumento.Pendiente);
public bool PuedeFinalizar        => Documento.PuedeTransicionarA(EstadoDocumento.Finalizado);
public bool PuedeAnular  => _rol == RolUsuario.Admin && Documento.PuedeTransicionarA(EstadoDocumento.Anulado);
public bool PuedeReabrir => _rol == RolUsuario.Admin && Documento.EsCerrado;
```
El rol entra por el constructor `DocumentoFila(DocumentoAdministrativo documento, RolUsuario rol)`
(`:28-32`), y el que lo pasa es `DocumentoListViewModel.CargarAsync` vía
`RolActualODefault => _session.RolActual ?? RolUsuario.Operador` (`:151`).

`DocumentoFormViewModel.cs:86-106`: las mismas cinco, cada una con `!EsNuevoDocumento` de más, y
con `EsAdmin => _session.RolActual == RolUsuario.Admin` (`:86`) en lugar del `_rol` de campo. Más
dos que la lista no tiene:
```csharp
public bool PuedeEditar       => !EsNuevoDocumento && _documento is { EsActivo: true };
public bool PuedeEditarCampos => EsNuevoDocumento || PuedeEditar;
```

`Domain/Entities/DocumentoAdministrativo.cs:34-40, 43, 46, 66`:
```csharp
private static readonly Dictionary<EstadoDocumento, EstadoDocumento[]> TransicionesValidas = new()
{
    [EstadoDocumento.Pendiente]  = new[] { EstadoDocumento.EnProceso, EstadoDocumento.Anulado },
    [EstadoDocumento.EnProceso]  = new[] { EstadoDocumento.Pendiente, EstadoDocumento.Finalizado, EstadoDocumento.Anulado },
    [EstadoDocumento.Finalizado] = new[] { EstadoDocumento.EnProceso },
    [EstadoDocumento.Anulado]    = new[] { EstadoDocumento.EnProceso },
};
public bool EsActivo  => Estado is EstadoDocumento.Pendiente or EstadoDocumento.EnProceso;
public bool EsCerrado => Estado is EstadoDocumento.Finalizado or EstadoDocumento.Anulado;
public bool PuedeTransicionarA(EstadoDocumento destino) => TransicionesValidas[Estado].Contains(destino);
```

**Resolviendo las fórmulas contra esa tabla, los 5 gates se reducen a esto** (no a "depende de la
transición" — a un valor concreto por estado):

| Gate | `Pendiente` | `EnProceso` | `Finalizado` | `Anulado` | ¿mira rol? |
|---|---|---|---|---|---|
| `PuedeIniciar` | **✔** | ✗ | ✗ | ✗ | no |
| `PuedeVolverAPendiente` | ✗ | **✔** | ✗ | ✗ | no |
| `PuedeFinalizar` | ✗ | **✔** | ✗ | ✗ | no |
| `PuedeAnular` | ✔ **solo Admin** | ✔ **solo Admin** | ✗ | ✗ | **sí** |
| `PuedeReabrir` | ✗ | ✗ | ✔ **solo Admin** | ✔ **solo Admin** | **sí** |

Dos consecuencias que la matriz del esbozo no tenía:
1. **`PuedeVolverAPendiente` y `PuedeFinalizar` son la misma condición** (`Estado == EnProceso`).
   Un test que solo cubra `EnProceso` los verifica a los dos juntos y no distingue si uno se
   rompió. **Hacen falta casos negativos distintos para cada uno** — y no los hay: los dos son
   `false` exactamente en los mismos 3 estados. La única forma de distinguirlos es assertear sobre
   **botones distintos** (`"Volver a pendiente"` vs `"Finalizar"`), no sobre estados distintos.
2. **`PuedeAnular` y `PuedeReabrir` son complementarios sobre el rol Admin** (activos vs cerrados).
   Con Admin, siempre hay exactamente uno de los dos visible. Ese es un invariante propio, y vale
   un caso.

### La matriz de casos de la Task 9.0 (rol × estado, 14 casos)

`DocumentoListView` — 5 gates × los estados que importan, montando **con datos reales** (los 5
gates viven dentro de `ItemsControl.ItemTemplate`, `:67`/`:71`/`:75`/`:84`/`:164`, así que sin
`ItemsSource` no se realiza ninguno):

| # | Rol | Estado del doc | Solapa | Qué asserta |
|---|---|---|---|---|
| 1 | Operador | `Pendiente` | Activos | `"Iniciar"` **visible**; `"Volver a pendiente"`, `"Finalizar"`, `"Anular…"` **ocultos** |
| 2 | Operador | `EnProceso` | Activos | `"Volver a pendiente"` y `"Finalizar"` **visibles**; `"Iniciar"` y `"Anular…"` **ocultos** |
| 3 | **Admin** | `Pendiente` | Activos | `"Anular…"` **visible** ← *el caso que hoy existe* |
| 4 | **Operador** | `Pendiente` | Activos | `"Anular…"` **oculto** ← **el caso que hoy NO existe. Es el que prueba el gate de rol.** |
| 5 | **Admin** | `EnProceso` | Activos | `"Anular…"` **visible** |
| 6 | **Admin** | `Finalizado` | Historial | `"Reabrir…"` **visible** |
| 7 | **Operador** | `Finalizado` | Historial | `"Reabrir…"` **oculto** ← **el segundo caso que hoy NO existe** |
| 8 | **Admin** | `Anulado` | Historial | `"Reabrir…"` **visible** |

`DocumentoFormView` — los 5 anteriores más los 2 de edición. **Acá los gates NO están en una
plantilla** (`:53`, `:63`, `:64`, `:65`, `:66`, `:67`), así que se pueden verificar montando la
vista con el VM directo:

| # | Rol | `EsNuevoDocumento` | Estado | Qué asserta |
|---|---|---|---|---|
| 9 | Operador | `true` (alta) | — | los 5 botones de transición (`:63-67`) **ocultos** (`!EsNuevoDocumento` los apaga a todos); `"Guardar"` (`:57`) **visible** |
| 10 | Operador | `false` | `Pendiente` | `"Iniciar"` visible; `"Anular"` **oculto** |
| 11 | **Admin** | `false` | `Pendiente` | `"Anular"` **visible** |
| 12 | **Operador** | `false` | `Finalizado` | `"Reabrir"` **oculto** |
| 13 | **Admin** | `false` | `Finalizado` | `"Reabrir"` **visible** |

`PuedeEditarCampos` — **el `OR` que hay que abrir en dos ramas** (`:97`:
`EsNuevoDocumento || PuedeEditar`):

| # | Rama | `EsNuevoDocumento` | Estado | `PuedeEditar` | `PuedeEditarCampos` | Qué asserta |
|---|---|---|---|---|---|---|
| 14a | **izquierda** | `true` | — | `false` | **`true`** | los 5 controles de `:32`, `:36`, `:40`, `:45`, `:50` **habilitados** |
| 14b | **derecha** | `false` | `EnProceso` | `true` | **`true`** | ídem habilitados |
| 14c | **ninguna** | `false` | `Finalizado` | `false` | **`false`** | los 5 controles **deshabilitados** |

**Sin 14a y 14b por separado, el test pasa por la rama equivocada y el `OR` queda sin custodiar.**

> **⚠ `IsEnabled` NO se verifica con `ArbolVisual.EsVisibleEnArbol`.** `ArbolVisualHelpers.cs:19-26`
> recorre el árbol mirando `IsVisible`, no `IsEnabled`. Para los casos 14a-c hay que leer
> `control.IsEnabled` directamente. **Y ojo:** `IsEnabled` en Avalonia es efectivo/heredado — un
> control dentro de un padre deshabilitado devuelve `false` aunque su propio `IsEnabled` local sea
> `true`. Acá eso juega a favor (queremos el valor efectivo), pero anotalo: si en el futuro alguien
> deshabilita un contenedor, el test seguiría verde por la razón equivocada. Si querés blindarlo,
> assertea también que el padre inmediato está habilitado.

### Deuda de banco de pruebas de esta tanda (Ruling B-19)

`DocumentoListViewTests`, `TareaListViewTests` y `TareaFormViewTests` **no** usan `SesionFake`: los
tres usan `TareaSessionFake` (`TareaFakes.cs:96-119`), cuyo `EstablecerPermisos` (`:118`) es
**no-op**. Como los 5 gates de Documentos no leen permisos, eso no invalida los casos de arriba —
pero sí bloquea cualquier test futuro de revocación en caliente en Documentos/Tareas. **La Task 9.0
lo migra a `SesionFake`**, que ya tiene la firma compatible
(`SesionFakes.cs:23`, `(RolUsuario rol, params string[] permisos)` cubre por `params` las llamadas
de un solo argumento que hoy hace `TareaSessionFake(rol)`).

Además: `DocumentoListViewTests` monta con `RolUsuario.Admin` en sus 6 tests (`:44` por default,
`:157` explícito). **Esos 6 no se borran** — verifican navegación y filtros, no gates. Regla de la
Fase A: *"no borres un test que verifica comportamiento solo porque monta con Admin"*.

---

### Task 9.0: la red de gates de Documentos (cierra las Tasks 4.3 y 4.4 de la Fase A) — SIN tocar XAML

**Files:**
- Create: `tests/StockApp.Presentation.UiTests/DocumentoListViewGatesTests.cs` (casos 1-8)
- Create: `tests/StockApp.Presentation.UiTests/DocumentoFormViewGatesTests.cs` (casos 9-14c)
- Modify: `tests/StockApp.Presentation.UiTests/TareaFakes.cs` (borra `TareaSessionFake`, `:96-119`)
- Modify: `DocumentoListViewTests.cs`, `TareaListViewTests.cs`, `TareaFormViewTests.cs` (cambian el
  tipo del fake en su helper `Montar`)

**Interfaces:**
- Consumes: `SesionFake` (`SesionFakes.cs:19-46`), `DocumentoServiceFake` /
  `AdjuntoDocumentoServiceFake` (`DocumentoFakes.cs`), `ArbolVisual.EsVisibleEnArbol`,
  `AvaloniaRuntimeXamlLoader.Parse<Window>` (el patrón de montaje de `DocumentoListViewTests:42-57`).
- Produces: cobertura de los 12 gates de Documentos (10 de visibilidad + `PuedeEditarCampos` en sus
  2 ramas). **Ningún** cambio de producción.

**La red se escribe contra el XAML ACTUAL, sin tocarlo.** Así, si las Tasks 9.1/9.2 rompen un gate,
se ve. Es la misma secuencia que la Task 6.2 usó con `MovimientoHistorialGatesTests`.

- [ ] **Step 1: anotar las fórmulas en el ledger**

No es paso de código. Abrir `DocumentoListViewModel.cs`, `DocumentoFormViewModel.cs` y
`DocumentoAdministrativo.cs`, y **confirmar o corregir** la tabla "gate × estado" de arriba contra
lo que efectivamente diga el código en ese momento. Si `TransicionesValidas` cambió, la matriz
entera cambia. Sin ese chequeo, los 14 casos pueden estar custodiando una tabla vieja.

- [ ] **Step 2: escribir los 14 casos**

`DocumentoListViewGatesTests.cs`: montar `DocumentoListView` con un `DocumentoListViewModel` real
alimentado por `DocumentoServiceFake`, con **un** documento en el estado del caso.
Localizar por texto **exacto** de botón — verificado en el XAML: `"Iniciar"` (`:66`),
`"Volver a pendiente"` (`:70`), `"Finalizar"` (`:74`), `"Anular…"` (`:83`, **con puntos
suspensivos tipográficos `…`, no tres puntos**), `"Reabrir…"` (`:163`, ídem).
Visibilidad con **`ArbolVisual.EsVisibleEnArbol`**, nunca `control.IsVisible`: los 5 viven dentro de
un `ItemsControl.ItemTemplate` → `Border.card` → `Grid`, y cualquiera de esos ancestros puede estar
oculto (la solapa no seleccionada, por ejemplo).

**Trampa de las solapas:** los gates de `PuedeReabrir` viven en la solapa **Historial**
(`DocumentoListView:96-174`), y los otros 4 en **Activos** (`:19-94`). El `TabControl` (`x:Name="Solapas"`,
`:17`) tiene `SelectionChanged="OnSolapaSeleccionada"`. Un `TabItem` no seleccionado **no realiza
su contenido** — `GetVisualDescendants` no lo ve (gotcha ya documentado en la memoria del repo
sobre `TabControl` en headless). Los casos 6, 7 y 8 tienen que **seleccionar la solapa Historial**
antes de buscar el botón, y correr `Dispatcher.UIThread.RunJobs()` después.

`DocumentoFormViewGatesTests.cs`: montar `DocumentoFormView` con un `DocumentoFormViewModel` real.
Localizar por texto: `"Iniciar"`, `"Volver a pendiente"`, `"Finalizar"`, `"Anular"` (`:66`, **sin**
puntos suspensivos, a diferencia de la lista), `"Reabrir"` (`:67`, ídem), `"Guardar"` (`:57`),
`"Guardar cambios"` (`:52`).
Para 14a/14b/14c: localizar los 5 controles de `:32` (`TextBox`), `:36` (`NumericUpDown`), `:40`
(`ComboBox`), `:43-46` (`CalendarDatePicker`), `:49` (`TextBox`) y leer **`control.IsEnabled`**.

- [ ] **Step 3: correr y ver el VERDE (la red se escribe sobre el XAML sin tocar)**

Run: `--filter "FullyQualifiedName~DocumentoListViewGates|FullyQualifiedName~DocumentoFormViewGates"` (timeout 600000)
Expected: **PASS 14/14**. Si algún caso falla, o la matriz está mal (volver al Step 1) o el gate
está roto en producción (hallazgo real: anotarlo y frenar).

- [ ] **Step 4: migrar `TareaSessionFake` → `SesionFake` (Ruling B-19)**

Borrar la clase de `TareaFakes.cs:96-119` y cambiar el tipo en los helpers `Montar` de los tres
archivos que la usan. **No tocar ningún assert.**
Run: `--filter "FullyQualifiedName~DocumentoListViewTests|FullyQualifiedName~TareaListViewTests|FullyQualifiedName~TareaFormViewTests"`
Expected: PASS **25/25** (6 + 10 + 9, contados con `grep -c '\[AvaloniaFact\]'`) sin tocar un assert.

> Ojo con `UsuarioActual`: `TareaSessionFake:109-110` devuelve `new(1, "prueba", …)` y `SesionFake:30`
> también `new(1, "prueba", …)` — **iguales, verificado**. (Los tres fakes de Finanzas que migró la
> Task 8.0 devolvían `"operador"`; ese sí era un cambio posible de valor.)

- [ ] **Step 5: validar por mutación — ONCE mutaciones, once rojos**

Una por gate. Borrar el atributo, correr el filtro, ver el rojo, revertir. **Sin excepción: si una
mutación no da rojo, ese gate no tiene guardián y hay que arreglar el test antes de seguir.**

| # | Mutación | Test que debe ponerse rojo |
|---|---|---|
| 1 | borrar `IsVisible="{Binding PuedeIniciar}"` de `DocumentoListView:67` | caso 1 (y el negativo del caso 2) |
| 2 | ídem `PuedeVolverAPendiente` de `:71` | caso 2 |
| 3 | ídem `PuedeFinalizar` de `:75` | caso 2 |
| 4 | ídem `PuedeAnular` de `:84` | **caso 4** (Operador + Pendiente: el botón aparece donde no debe) |
| 5 | ídem `PuedeReabrir` de `:164` | **caso 7** (Operador + Finalizado) |
| 6 | ídem `PuedeIniciar` de `DocumentoFormView:63` | caso 10 |
| 7 | ídem `PuedeVolverAPendiente` de `:64` | caso 10 o 11 |
| 8 | ídem `PuedeFinalizar` de `:65` | ídem |
| 9 | ídem `PuedeAnular` de `:66` | **caso 10** |
| 10 | ídem `PuedeReabrir` de `:67` | **caso 12** |
| 11 | borrar `IsEnabled="{Binding PuedeEditarCampos}"` de **uno** de `:32`/`:36`/`:40`/`:45`/`:50` | **caso 14c** |

**Mutación extra, la que prueba que el `OR` está bien abierto:** cambiar `PuedeEditarCampos` en
`DocumentoFormViewModel.cs:97` de `EsNuevoDocumento || PuedeEditar` a **`PuedeEditar` a secas**.
Expected: **el caso 14a se pone rojo y el 14b sigue verde.** Si los dos siguen verdes, el caso 14a
está mal escrito y el `OR` sigue sin custodiar. Revertir.

- [ ] **Step 6: suite completa + commit**

```
test(ui): cierra la deuda de gates de Documentos de la Fase A

Las Tasks 4.3 y 4.4 de la Fase A nunca se ejecutaron: DocumentoFormView no
tenia UN SOLO test de UI y los 5 gates de DocumentoListView solo se montaban
con Admin, o sea que PuedeAnular/PuedeReabrir nunca se veian en false.

El brief original pedia "usuario de permisos mixtos". Las formulas no leen
permisos ni una vez: tres son puro automata de estado y dos comparan el rol
contra Admin a secas. Un test de permisos mixtos habria dado verde sin probar
nada. La matriz correcta es rol x estado, resuelta contra la tabla
TransicionesValidas del dominio: 14 casos, incluidos los dos que no existian
(Operador+Pendiente no ve "Anular", Operador+Finalizado no ve "Reabrir").

PuedeEditarCampos es un OR (EsNuevoDocumento || PuedeEditar) y se cubre en sus
dos ramas por separado, con IsEnabled -- ArbolVisual.EsVisibleEnArbol no lo
detecta, mira IsVisible.

TareaSessionFake (usado por Documentos Y Tareas) tenia EstablecerPermisos
no-op: migrado a SesionFake. Es la cuarta copia de la deuda del Ruling 6 de la
Fase A; quedan dos, las de Inicio, para B3.

Once mutaciones, once rojos. Ninguna linea de XAML tocada.
```

**Riesgo específico:** la trampa del `TabControl` del Step 2. Si los casos 6-8 se escriben sin
seleccionar la solapa, `First(b => b.Content == "Reabrir…")` tira `InvalidOperationException`
("Sequence contains no matching element") en vez de dar un assert claro — parece un test roto y es
en realidad la solapa sin realizar.

---

### Task 9.1: P1 sobre `DocumentoListView`

**Files:**
- Modify: `src/StockApp.Presentation/Views/Documentos/DocumentoListView.axaml` (180 l)
- Test: `GuardianDePatronTests.cs` (+1 `InlineData`, +1 en `VistasDeLaTanda`)

**Interfaces:** Consumes P1 (Task 6.1), la red de la Task 9.0.

| Línea | Hoy | Pasa a |
|---|---|---|
| `:10` | `<DockPanel Margin="24">` | `Margin="{DynamicResource MargenVista}"` |
| `:12-15` | `<Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,16">` con `TextBlock` "Documentos administrativos" `titulo-vista` (`:13`) + `Button primary` "Nuevo documento" (`:14`) | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="DOCUMENTOS" Titulo="Documentos administrativos">` con el botón en el slot `Acciones`. El `Grid` de 2 columnas **desaparece**: `HeaderVista` ya hace ese layout |
| `:17` | `<TabControl x:Name="Solapas" SelectionChanged="OnSolapaSeleccionada">` | **INTOCABLE** (`x:Name` + handler de code-behind) |
| `:20`, `:97` | `<DockPanel Margin="0,12,0,0">` de cada solapa | se quedan literales (T1) |
| `:22-23`, `:99-100` | `<StackPanel … Spacing="12" VerticalAlignment="Bottom" Margin="0,0,0,12">` de filtros | `Spacing="{DynamicResource Espacio3}"`; `Margin` literal |
| `:25`, `:37`, `:102`, `:107`, `:119`, `:131` | `<TextBlock Text="Tipo"/"Buscar"/"Año"/"Estado" />` sueltos sobre su control | `<c:CampoFormulario Etiqueta="Tipo">…</c:CampoFormulario>` — **son pares etiqueta+control idénticos a los de un formulario**; envolverlos unifica el espaciado con P3-b. *Alternativa conservadora: dejarlos como están. Elegí una y aplicala a las 6, no a tres sí y tres no* |
| `:41`, `:135` | `<Button Classes="secondary" Content="Buscar" …/>` | se quedan; `Content` **intocable** (`DocumentoListViewTests` lo localiza) |
| `:48`, `:142` | `<Border Classes="card" Margin="0,0,0,8">` de cada fila | se quedan |
| `:60`, `:154` | `<TextBlock Text="{Binding Descripcion}" Classes="caption" Opacity="0.8" />` | quitar `Opacity`; `Classes="caption"` ya da `TextoSecundarioBrush`. **Dentro de `DataTemplate` → el guardián NO lo ve** (Ruling B-16) |
| `:61`, `:155` | `<TextBlock Text="{Binding EstadoTexto}" Classes="caption" Opacity="0.7" />` | `Foreground="{DynamicResource TextoTerciarioBrush}"` sin `Opacity`. **Ídem, ciego** |
| `:63`, `:157` | `<Button Classes="ghost" Content="Ver" …/>` | se quedan |
| `:66`, `:70`, `:83`, `:163` | botones `secondary`/`danger` de transición | `Classes` y `Content` **intocables**; los `IsVisible` de `:67`, `:71`, `:84`, `:164` **textuales** |
| `:74` | `<Button Grid.Column="4" Classes="primary" Content="Finalizar" IsVisible="{Binding PuedeFinalizar}" …/>` | **`Classes="secondary"`** — Ruling B-18 caso (2): es un primario **por fila**, coexiste en runtime con "Nuevo documento" N veces. `TareaListView` ya usa `ghost`/`secondary`/`danger` en sus filas; este es la excepción |
| `:78-82`, `:160-162` | comentarios "I3" sobre "Anular…"/"Reabrir…" | **se conservan** (documentan una decisión de producto) |

- [ ] **Step 1: línea base** — `--filter "FullyQualifiedName~DocumentoListView"` → PASS **6 + 8 = 14**
  (los 6 de `DocumentoListViewTests` + los 8 de `DocumentoListViewGatesTests` de la Task 9.0). Anotar.
- [ ] **Step 2: guardián en rojo** — agregar
  `[InlineData(typeof(DocumentoListView), "Documentos administrativos", "DOCUMENTOS")]` a las dos
  listas. Expected: 1 fallo, `Vista_TieneHeaderVistaConElTituloEsperado`. El margen ya pasa
  (`Margin="24"` literal ≡ `MargenVista`); `NoTieneOpacidadesLiterales` **también pasa desde ya** —
  los 4 `Opacity` de esta vista están dentro de plantillas (Ruling B-16). Anotarlo.
- [ ] **Step 3: aplicar la tabla.**
- [ ] **Step 4: los 14, otra vez** → PASS **sin tocar un solo assert.**
- [ ] **Step 5: grep de plantillas (obligatorio)** —
  `grep -n 'Opacity="0\.\|titulo-vista\|Margin="24"\|FontSize="' …/DocumentoListView.axaml` → cero.
- [ ] **Step 6: mutación** — borrar `IsVisible="{Binding PuedeAnular}"` de `:84` (ya movido/renumerado)
  → **caso 4 de `DocumentoListViewGatesTests` rojo**. Revertir. *Es la prueba de que la red de 9.0
  sobrevivió al refactor.*
- [ ] **Step 7: suite completa.**

**Riesgos:** el `TabControl` con `x:Name` + handler es lo único delicado. **No mover el
`TabControl` fuera del `DockPanel`** ni cambiarle el `DockPanel.Dock` implícito: hoy es el último
hijo del `DockPanel`, o sea el que llena (`LastChildFill`). Meter el `HeaderVista` con
`DockPanel.Dock="Top"` **antes** de él lo preserva; meterlo después lo rompería (gotcha de
`DockPanel` ya conocido en el repo).

---

### Task 9.2: P3-b sobre `DocumentoFormView`

**Files:** Modify `src/StockApp.Presentation/Views/Documentos/DocumentoFormView.axaml` (115 l);
Test: `GuardianDePatronTests.cs` (+1 fila).

**Interfaces:** Consumes P3-b (Task 8.2), la red de la Task 9.0 (casos 9-14c).

| Línea | Hoy | Pasa a |
|---|---|---|
| `:13-14` | `<ScrollViewer><DockPanel Margin="24">` | `Margin="{DynamicResource MargenVista}"`. El `ScrollViewer` se queda (mismo caso que `GastoFormView`) |
| `:16` | `<Border Classes="card" VerticalAlignment="Top">` | se queda |
| `:17` | `<StackPanel Spacing="12" MaxWidth="680" HorizontalAlignment="Left">` | `Spacing="{DynamicResource Espacio3}"`; `MaxWidth`/`HorizontalAlignment` intactos |
| `:19-20` | dos `<TextBlock Classes="titulo-vista">` mutuamente excluyentes ("Nuevo documento" / "Detalle del documento") | **dos `<c:HeaderVista Eyebrow="DOCUMENTOS" Titulo="…" IsVisible="…"/>`, conservando cada `IsVisible` textual** — Ruling B-20. Van **dentro** de la card, donde están hoy (es P3-b con el header adentro porque el `titulo-vista` original ya estaba adentro; **no** moverlo afuera del `Border`, eso cambiaría el layout y no hay ningún test que lo custodie) |
| `:22-25` | dos `TextBlock Classes="caption"` de estado y "Registrado por" | se quedan; sus `IsVisible` textuales |
| `:27-29` | comentario sobre `PuedeEditarCampos` | **se conserva** |
| `:30` | `<StackPanel Spacing="12">` de campos | `Spacing="{DynamicResource Espacio3}"` |
| `:31-32` | `TextBlock "Número"` + `TextBox IsEnabled="{Binding PuedeEditarCampos}"` | `<c:CampoFormulario Etiqueta="Número"><TextBox … IsEnabled="{Binding PuedeEditarCampos}" /></c:CampoFormulario>` — **el `IsEnabled` queda en el control, NO se sube al `CampoFormulario`** (misma regla que el `x:Name` de la Task 6.4: los 5 casos 14a-c leen `control.IsEnabled` sobre el control concreto) |
| `:34-36` | "Año" + `NumericUpDown` | ídem |
| `:38-40` | "Tipo" + `ComboBox` | ídem |
| `:42-46` | "Fecha de emisión" + `CalendarDatePicker` | ídem |
| `:48-50` | "Descripción" + `TextBox AcceptsReturn` | ídem |
| `:52-53` | `<Button Classes="primary" Content="Guardar cambios" IsVisible="{Binding PuedeEditar}" …/>` | `Content` e `IsVisible` intocables |
| `:56-59` | `StackPanel Spacing="8"` con "Guardar"/"Volver", `IsVisible="{Binding EsNuevoDocumento}"` | `Spacing="{DynamicResource Espacio2}"`; el resto intocable |
| `:62-69` | `StackPanel Spacing="8" Margin="0,8,0,0"` con los 5 de transición + "Volver" | `Spacing="{DynamicResource Espacio2}"`; `Margin` literal; **los 5 `IsVisible` de `:63-67` textuales** |
| `:66` | `<Button Classes="danger" Content="Anular" …/>` | se queda `danger` |
| `:72` | `<StackPanel Spacing="6" … Margin="0,8,0,0">` | `Spacing="{DynamicResource Espacio1}"` da 4, no 6 — **dejar `Spacing="6"` literal** y anotarlo, o subirlo a `Espacio2` (8). No inventar un token de 6 |
| `:73` | `<TextBlock Text="Historial del trámite" Classes="seccion" />` | ya está bien |
| `:77` | `<Border Classes="card" Margin="0,0,0,6" Padding="8">` (dentro del `ItemTemplate`) | `Padding="{DynamicResource PaddingCompacto}"` (Thickness 8, Ruling B-2); `Margin` literal |
| `:81` | `<TextBlock … Classes="caption" Opacity="0.7" />` **dentro de `DataTemplate`** | `Foreground="{DynamicResource TextoTerciarioBrush}"` sin `Opacity`. **El guardián NO lo ve** |
| `:88` | `<TextBlock Text="Nueva nota" />` | `<c:CampoFormulario Etiqueta="Nueva nota">` envolviendo el `TextBox` de `:89-90` |
| `:94-99` | comentario largo sobre `AVLN2000` y el `Panel` envolvente | **se conserva íntegro** — documenta un error de compilación real |
| `:100-102` | `<Panel IsVisible="{Binding !EsNuevoDocumento}" Margin="0,8,0,0"><adj:AdjuntosDocumentoPanelView DataContext="{Binding AdjuntosPanel}" /></Panel>` | **INTOCABLE.** Mover `IsVisible` o `Margin` al `AdjuntosDocumentoPanelView` reproduce el AVLN2000 que el comentario describe |
| `:104-107` | `Foreground="Red"` | `{DynamicResource DangerBrush}` |

- [ ] **Step 1: línea base** — `--filter "FullyQualifiedName~DocumentoFormViewGates"` → PASS
  (casos 9-14c). **Anotar.** Esta vista no tenía ningún test antes de la Task 9.0.
- [ ] **Step 2: guardián en rojo** — `[InlineData(typeof(DocumentoFormView), "Nuevo documento", "DOCUMENTOS")]`
  (Ruling B-20: `HeaderDe` devuelve el primero del árbol, y sin DataContext los dos `IsVisible`
  quedan en el default `true`). Expected: 1 fallo.
- [ ] **Step 3: aplicar la tabla.**
- [ ] **Step 4: los casos 9-14c, otra vez** → PASS **sin tocar un assert.** Si 14a/14b/14c fallan, el
  `IsEnabled` se subió al `CampoFormulario` en vez de quedarse en el control. Volver atrás.
- [ ] **Step 5: grep** — `grep -n 'Opacity="0\.\|Foreground="Red"\|Margin="24"\|titulo-vista' …/DocumentoFormView.axaml` → cero.
- [ ] **Step 6: dos mutaciones** —
  (a) borrar el `IsEnabled` de `:32` → **caso 14c rojo**;
  (b) borrar `IsVisible="{Binding PuedeReabrir}"` de `:67` → **caso 12 rojo**. Revertir las dos.
- [ ] **Step 7: suite completa.**

**Riesgos:** los dos `HeaderVista` son el punto discutible (ver la alternativa del Ruling B-20). Y
el `Panel` de `:100-102`: es el único lugar de las 29 vistas donde mover un atributo de contenedor
al hijo **rompe el build**.

---

### Task 9.3: P5 sobre `AdjuntosDocumentoPanelView` (armonización, Ruling B-13)

**Files:** Modify `src/StockApp.Presentation/Views/Documentos/AdjuntosDocumentoPanelView.axaml` (62 l);
Test: `GuardianDePatronTests.cs` (+1 en `VistasEmbebidas`).

Es el gemelo de `AdjuntosPanelView` (Task 8.4). **Diffeados: son idénticos línea por línea salvo
por el namespace del ViewModel (`ViewModels.Documentos` vs `ViewModels.Finanzas`), el DTO
(`AdjuntoDocumentoDto` vs `AdjuntoDto`) y los nombres de las propiedades de gating
(`PuedeAgregar`+`PuedeQuitar` vs un único `PuedeModificar`).** El Ruling B-13 (aprobado por el
usuario) decidió **no** reconciliarlos: solo que se vean igual.

| Línea | Hoy | Pasa a |
|---|---|---|
| `:12` | `<DockPanel>` | sin margen (P5 embebido) |
| `:14` | `<Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,8">` | `Margin` literal |
| `:15-17` | `<TextBlock Grid.Column="0" Text="Adjuntos" Classes="titulo-vista" />` | **`Classes="seccion"`** — **el mismo cambio, carácter por carácter, que la Task 8.4 le hace a `AdjuntosPanelView:16-18`.** Esto ES la armonización |
| `:18-22` | `<Button Classes="secondary" Content="Agregar" IsEnabled="{Binding PuedeAgregar}" />` | `IsEnabled` **textual** — ojo: acá es `PuedeAgregar`, no `PuedeModificar` |
| `:28` | `<Border Classes="card" Margin="0,0,0,8">` | se queda |
| `:35`, `:39`, `:44`, `:50` | `Margin="8,0"` / `Margin="8,0,0,0"` | literales |
| `:53` | `IsEnabled="{Binding $parent[UserControl].(…).PuedeQuitar}"` | **textual** |

- [ ] **Step 1: diff con el gemelo, ANTES y DESPUÉS**
```bash
diff <(sed 's/Documentos/X/g;s/AdjuntoDocumentoDto/D/g;s/PuedeAgregar\|PuedeQuitar/P/g' \
        src/StockApp.Presentation/Views/Documentos/AdjuntosDocumentoPanelView.axaml) \
     <(sed 's/Finanzas/X/g;s/AdjuntoDto/D/g;s/PuedeModificar/P/g' \
        src/StockApp.Presentation/Views/Finanzas/AdjuntosPanelView.axaml)
```
Anotar el diff de **antes**. Después de aplicar las dos tablas (8.4 y 9.3), el diff tiene que ser
**igual o más chico**. Si creció, la armonización falló.
- [ ] **Step 2:** `VistasEmbebidas` += `typeof(AdjuntosDocumentoPanelView)`. Expected: verde
  (no tiene margen raíz, no tiene primario, sus `Opacity`… no tiene).
- [ ] **Step 3: aplicar la tabla. Step 4: el diff del Step 1, otra vez. Step 5:**
  `--filter "FullyQualifiedName~ViewLocator"` → PASS (nada se renombró).
- [ ] **Step 6: suite completa.**

**Riesgo: bajo.** Es un cambio de una clase CSS en un archivo de 62 líneas.
**NO VERIFIQUÉ** si existe un test de UI propio de `AdjuntosDocumentoPanelView` (para
`AdjuntosPanelView` sí: los 2 casos de `ViewLocatorTests:24-41`). El Step 1 de la task lo confirma
con `ls tests/StockApp.Presentation.UiTests/ | grep -i AdjuntosDocumento`.

---

### Task 9.4: P1 + P3-b sobre las 2 vistas de Tareas

**Files:**
- Modify: `src/StockApp.Presentation/Views/Tareas/TareaListView.axaml` (138 l)
- Modify: `src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml` (104 l)
- Test: `GuardianDePatronTests.cs` (+2 filas)

**Interfaces:** Consumes P1, P3-b. **19 tests a preservar**
(`TareaListViewTests` 10 + `TareaFormViewTests` 9, contados con `grep -c '\[AvaloniaFact\]'`).

**Cómo localizan los 19 (verificado — intocables):**
- `TareaListViewTests:73-74`:
  `BotonPorTexto(window, texto) => …OfType<Button>().First(b => Equals(b.Content, texto) && b.IsVisible)`
  → depende de los literales `"Tomar"`, `"Soltar"`, `"Terminar"`, `"Cancelar"`, `"Nueva tarea"`,
  `"Ver"`, y de los textos `"Pendientes"`, `"En curso"`, `"Terminadas"`, `"Canceladas"`,
  `"Vencida"`, `"Al dia"`, `"Tomada por juan"`.
  También usa `Clickear` (`:62-71`) con `window.MouseMove/MouseDown/MouseUp` → **depende de la
  GEOMETRÍA**: si un botón cambia de posición o de tamaño, el click cae en otro lado.
- `TareaFormViewTests:82-83`:
  `BotonVisiblePorTexto(window, texto) => …First(b => Equals(b.Content, texto) && ArbolVisual.EsVisibleEnArbol(b))`
  → `"Guardar"`, `"Volver"`, `"Agregar nota"`, `"Actualizar prioridad"`.
  Localiza `TextBox`/`CalendarDatePicker`/`ComboBox` **por tipo, filtrando por
  `ArbolVisual.EsVisibleEnArbol`** → **envolverlos en `CampoFormulario` no los saca del árbol**
  (el `ContentPresenter` los proyecta, T7), pero **sí cambia su índice si el filtro por visibilidad
  cambia**. Correr los 9 después de cada archivo, no al final.
- Ninguno de los 19 usa `x:Name`. Ninguno usa `Classes`.

> **⚠ `TareaListViewTests:181-194` asserta un COLOR.**
> `Montar_TareaPendienteVencida_ElTituloQuedaEnRojo` busca el `TextBlock` cuyo `Text == "Vencida"`
> y asserta `Assert.Equal(Color.Parse("#DC2626"), brushVencida.Color)` — o sea, custodia
> `SignoNegativoBrushConverter` en `TareaListView:33`. **Esta task NO toca ese converter**
> (lo hace la Task B2-T), pero cualquier cambio de `Foreground` en ese `TextBlock` rompe el test.
> Ver la advertencia de la Task B2-T sobre por qué el color **se conserva** y la palabra **se
> agrega**.

#### `TareaListView.axaml` (P1)

| Línea | Hoy | Pasa a |
|---|---|---|
| `:11` | `<DockPanel Margin="24">` | `Margin="{DynamicResource MargenVista}"` |
| `:13-19` | `<Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,16">` con `TextBlock` "Tareas" (`:14`) + `StackPanel` con `CheckBox` "Mostrar canceladas" (`:16`) y `Button primary` "Nueva tarea" (`:17`) | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="TAREAS" Titulo="Tareas">` con el `StackPanel` de `:15-18` **entero** (CheckBox incluido) en el slot `Acciones`; `Spacing="{DynamicResource Espacio3}"`. El `Grid` desaparece |
| `:22` | `<StackPanel Spacing="24">` (dentro del `ScrollViewer` de `:21`) | `Spacing="{DynamicResource Espacio5}"` |
| `:24`, `:57`, `:96`, `:114` | `<StackPanel Spacing="8">` de cada sección | `Spacing="{DynamicResource Espacio2}"` |
| `:25`, `:58`, `:97`, `:115` | `<TextBlock Text="Pendientes"/"En curso"/"Terminadas"/"Canceladas" Classes="seccion" />` | **intocables** — los 4 textos los localizan los tests |
| `:29`, `:62`, `:101`, `:119` | `<Border Classes="card" Margin="0,0,0,8">` | se quedan |
| `:31`, `:64` | `<StackPanel Grid.Column="0" Spacing="2">` | `Spacing="2"` literal (no hay token de 2) |
| `:33`, `:66` | `Foreground="{Binding DiasParaVencer, Converter=…SignoNegativoBrushConverter…}"` | **NO SE TOCA acá.** Task B2-T |
| `:34`, `:67` | `<TextBlock Text="{Binding PrioridadTexto}" Classes="caption" Opacity="0.7" />` | quitar `Opacity` (`caption` ya atenúa). **Ciego al guardián** |
| `:36`, `:69`, `:72` | `Classes="caption" Opacity="0.7"` | ídem, quitar `Opacity` |
| `:103` | `<TextBlock Grid.Column="0" Text="{Binding Titulo}" Opacity="0.8" />` (sección Terminadas) | `Foreground="{DynamicResource TextoSecundarioBrush}"`. **Ciego** |
| `:121-122` | `<TextBlock … Opacity="0.6" TextDecorations="Strikethrough" />` (Canceladas) | `Foreground="{DynamicResource TextoTerciarioBrush}"`, **conservando el `TextDecorations`**. **Ciego** |
| `:39`, `:74`, `:104`, `:123` | `<Button Classes="ghost" Content="Ver" …/>` | intocables |
| `:42`, `:77` | `Classes="secondary"` "Tomar"/"Soltar" | intocables; `IsVisible` textuales |
| `:81` | `<Button Grid.Column="3" Classes="primary" Content="Terminar" IsVisible="{Binding PuedeTerminar}" …/>` | **`Classes="secondary"`** — Ruling B-18 caso (2), mismo movimiento que `DocumentoListView:74`. Los otros 3 botones de esa misma fila ya son `ghost`/`secondary`/`danger` |
| `:46`, `:85` | `Classes="danger"` "Cancelar" | intocables |

#### `TareaFormView.axaml` (P3-b)

| Línea | Hoy | Pasa a |
|---|---|---|
| `:12-13` | `<ScrollViewer><DockPanel Margin="24">` | `Margin="{DynamicResource MargenVista}"` |
| `:15-16` | `<Border Classes="card" VerticalAlignment="Top"><StackPanel Spacing="12" MaxWidth="620" HorizontalAlignment="Left">` | `Spacing="{DynamicResource Espacio3}"` |
| `:18-19` | dos `titulo-vista` excluyentes ("Nueva tarea" / "Detalle de la tarea") | dos `<c:HeaderVista Eyebrow="TAREAS" Titulo="…" IsVisible="…"/>` — Ruling B-20, `IsVisible` textuales |
| `:22` | `<StackPanel Spacing="12" IsVisible="{Binding EsNuevaTarea}">` | `Spacing="{DynamicResource Espacio3}"` |
| `:23-24` | "Título" + `TextBox` | `<c:CampoFormulario Etiqueta="Título">…</c:CampoFormulario>` |
| `:26-28` | "Descripción (opcional)" + `TextBox` | `<c:CampoFormulario Etiqueta="Descripción (opcional)">` — **el "(opcional)" se conserva**: es información, no marcado de obligatoriedad (P3-b, sección 1) |
| `:30-35` | "Fecha límite (opcional)" + `CalendarDatePicker` | ídem |
| `:37-40` | `StackPanel Spacing="8"` "Guardar"/"Volver" | `Spacing="{DynamicResource Espacio2}"`; `Content` intocables |
| `:44` | `<StackPanel Spacing="6" IsVisible="{Binding !EsNuevaTarea}">` | `Spacing="6"` literal (ver nota de la Task 9.2) |
| `:45` | `<TextBlock Text="{Binding Titulo}" FontWeight="SemiBold" FontSize="16" />` | **`Classes="seccion"`**, sacando `FontWeight` y `FontSize` literales. **T5:** el selector global de `Typography.axaml:33` gana; verificar visualmente que 16 px sea el tamaño de `seccion` — si no lo es, es un cambio de escala tipográfica **deliberado**, anotalo |
| `:46-47` | `<TextBlock Text="{Binding Descripcion}" TextWrapping="Wrap" Opacity="0.85" …/>` | `Foreground="{DynamicResource TextoSecundarioBrush}"` sin `Opacity`. **Este SÍ lo ve el guardián** — es el único de las 2 vistas de Tareas fuera de una plantilla |
| `:48-53` | 3 `TextBlock Classes="caption"` | se quedan |
| `:55-56` | `<Button Classes="secondary" Content="Volver" … Margin="0,8,0,0" />` | `Margin` literal; `Content` intocable |
| `:60` | `<StackPanel Spacing="6" IsVisible="{Binding MuestraCambioPrioridad}" Margin="0,8,0,0">` | `IsVisible` **textual** (`TareaFormViewTests:229` monta como `Operador` justamente para verlo en `false`) |
| `:61` | `<TextBlock Text="Prioridad" Classes="seccion" />` | ya está bien |
| `:63-66` | `ComboBox` + `Button "Actualizar prioridad"` | `Content` intocable |
| `:80` | `Classes="caption" Opacity="0.7"` **dentro de `DataTemplate`** | quitar `Opacity`. **Ciego** |
| `:94` | `Foreground="Red"` | `{DynamicResource DangerBrush}` |

- [ ] **Step 1: línea base** — `--filter "FullyQualifiedName~TareaListViewTests|FullyQualifiedName~TareaFormViewTests"` → PASS **19/19**. Anotar.
- [ ] **Step 2: guardián en rojo** —
  `[InlineData(typeof(TareaListView), "Tareas", "TAREAS")]` y
  `[InlineData(typeof(TareaFormView), "Nueva tarea", "TAREAS")]`, más las dos entradas en
  `VistasDeLaTanda`. Expected: **3 fallos** — los 2 de `HeaderVista` y **1 de
  `Vista_NoTieneOpacidadesLiterales` para `TareaFormView`** (el `Opacity="0.85"` de `:46`, el único
  visible). `TareaListView` pasa `NoTieneOpacidadesLiterales` **desde ya**: sus 7 están todos en
  plantillas (Ruling B-16).
- [ ] **Step 3: aplicar `TareaFormView` primero** (104 líneas, 9 tests) → correr los 9 → PASS.
- [ ] **Step 4: aplicar `TareaListView`** → correr los 10 → PASS.
  **Si `Clickear` empieza a fallar, es geometría**: el `HeaderVista` cambió la altura del encabezado
  y el `MouseDown` cae fuera del botón. Ese es el modo de falla previsible de esta task.
- [ ] **Step 5: los 19 juntos** → PASS **sin tocar un solo assert.**
- [ ] **Step 6: grep de plantillas (obligatorio — 8 de los 9 `Opacity` de esta task son ciegos)**
```bash
grep -n 'Opacity="0\.\|Foreground="Red"\|Margin="24"\|titulo-vista\|FontSize="' \
  src/StockApp.Presentation/Views/Tareas/TareaListView.axaml \
  src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml
```
Expected: **cero**. (`SignoNegativoBrushConverter` no matchea: no es `Foreground="Red"`.)
- [ ] **Step 7: dos mutaciones** —
  (a) borrar `IsVisible="{Binding PuedeCancelar}"` de `TareaListView:47` → rojo en el test de gating
  de Cancelar (el que monta como `Operador`, `TareaListViewTests:155`);
  (b) volver `TareaListView:81` a `Classes="primary"` → **el guardián NO se pone rojo** (está en una
  plantilla). **Anotá ese verde como evidencia del Ruling B-16**: es la demostración de que el
  guardián no cubre las filas, y de por qué el grep del Step 6 es obligatorio.
- [ ] **Step 8: suite completa.**

---

### Task 9.5: cierre de la tanda 9

- [ ] **Step 1: suite completa** (timeout 600000, polling activo). Expected: PASS.
- [ ] **Step 2: auditoría de residuos**
```bash
grep -rn 'Opacity="0\.\|Foreground="Red"\|Margin="24"\|titulo-vista\|badge-inactiva\|FontSize="' \
  src/StockApp.Presentation/Views/Documentos/ src/StockApp.Presentation/Views/Tareas/
```
Expected: cero coincidencias.
- [ ] **Step 3: recuento de la red nueva.** Anotar en el ledger: cuántos tests tenía Documentos +
  Tareas al arrancar (25: 6 + 10 + 9) y cuántos al cerrar (25 + 14 de la Task 9.0 = 39).
- [ ] **Step 4: verificación orgánica** de las 5 pantallas en la app real. En particular: un
  documento en cada uno de los 4 estados, con un usuario **Operador** y con un **Admin**, mirando
  qué botones aparecen. *Es la única forma de confirmar que la matriz rol × estado que escribimos
  es la que el usuario ve.*
- [ ] **Step 5: commit.**

```
feat(ui): aplica el sistema de diseno a Documentos y Tareas

- DocumentoListView y TareaListView (P1): HeaderVista con la barra de acciones
  en el slot Acciones. El CheckBox "Mostrar canceladas" de Tareas va con los
  botones: es un control de la barra, no de la grilla
- DocumentoFormView y TareaFormView (P3-b): CampoFormulario absorbe los pares
  label+control. El IsEnabled="{Binding PuedeEditarCampos}" queda en cada
  control, NO se sube al CampoFormulario: los tests de la Task 9.0 leen
  control.IsEnabled sobre el control concreto
- Las dos vistas de detalle llevan DOS HeaderVista mutuamente excluyentes, no
  uno con {Binding Titulo}: ninguno de los dos ViewModels expone esa propiedad
  y una tanda de barrido visual no toca ViewModels (Ruling B-20)
- Los Classes="primary" POR FILA de DocumentoListView y TareaListView pasan a
  secondary: en runtime coexisten N veces con el primario del chrome, y el
  guardian no puede verlos porque viven dentro de un ItemTemplate. El resto de
  los botones de fila de las dos vistas ya usaba ghost/secondary/danger
- 12 Opacity literales migrados; 10 de los 12 son invisibles para el guardian
  (Ruling B-16) y se verificaron por grep, no por test
- AdjuntosDocumentoPanelView: "Adjuntos" pasa de titulo-vista a seccion, el
  mismo cambio exacto que la Task 8.4 le hizo a su gemelo de Finanzas. Es toda
  la armonizacion que el Ruling B-13 pide
```

---

## Tanda 10: Reportes (5 vistas)

**Vistas (5):** `ValorizacionView` (106 l), `StockCategoriaView` (77 l), `MasMovidosView` (104 l),
`HistorialPorProductoView` (136 l), `AuditoriaLogView` (115 l). **Las 5 son P1 puro.**

**Eyebrow: `REPORTES` para las 5.**

**Riesgo: BAJO — el más bajo de todo el refactor.** Verificado:
- **Cero tests de UI.** No existe ningún `*ViewTests.cs` para las 5.
- **Cero gates de permiso.** Ni un `IsVisible` que lea una propiedad `Puede*` (los `IsVisible` que
  hay son de datos: `MensajeError` no vacío, `Items` vacío, `Totales` no nulo).
- **Cero `x:Name`.**
- **Cero `Opacity` literal.** Único módulo de B2 sin un solo caso — el Ruling B-16 no aplica acá.
- **Cero `Foreground="Red"`.** Las 5 ya usan `{DynamicResource DangerBrush}` para el mensaje de
  error (`MasMovidosView:47`, `HistorialPorProductoView:65`, `AuditoriaLogView:65`).

**Son casi el mismo archivo.** Las 5 comparten: `DockPanel Margin="24"` → `TextBlock titulo-vista
Margin="0,0,0,16"` → `Border Classes="card"` → `Grid` con una barra de filtros+acciones en
`Grid.Row="0"` (`Orientation="Horizontal" Spacing="8" Margin="0,0,0,12"`) y un `DataGrid`.

### La deuda que cierra: los 5 `DataGridCell.num` locales

**Verificados con `grep -rn 'Selector="DataGridCell.num"' src/StockApp.Presentation/Views/` — son
exactamente 5, todos en Reportes** (el 6º, `MovimientoHistorialView:108`, lo cerró la Task 6.3):

| Archivo | Línea | Corrección respecto del esbozo |
|---|---|---|
| `Views/Reportes/StockCategoriaView.axaml` | `:36-38` | — |
| `Views/Reportes/ValorizacionView.axaml` | `:37-39` (con un comentario propio en `:36`) | — |
| `Views/Reportes/MasMovidosView.axaml` | `:60-62` | — |
| `Views/Reportes/AuditoriaLogView.axaml` | **`:78-80`** | el esbozo decía `:60` — **falso** |
| `Views/Reportes/HistorialPorProductoView.axaml` | **`:78-80`** | el esbozo decía `:60` — **falso** |

Los 5 son idénticos:
```xml
<DataGrid.Styles>
    <Style Selector="DataGridCell.num">
        <Setter Property="HorizontalContentAlignment" Value="Right" />
    </Style>
</DataGrid.Styles>
```
Los 5 se **borran** (junto con el `<DataGrid.Styles>` que los envuelve, que queda vacío). Lo cubre
`Themes/DataGrid.axaml:97`, ya cargado en `App.axaml` **y** en `TestAppBuilder.cs` desde la tanda 0.

Los `CellStyleClasses="num"` de las columnas **se quedan** — son lo que engancha con el estilo
global. Están en: `ValorizacionView:74`, `:77`, `:80`, `:83`; `StockCategoriaView:53`, `:65`, `:68`;
`MasMovidosView:77`, `:83`; `HistorialPorProductoView:88`, `:91`; `AuditoriaLogView:88`.

**Nota `tnum` (Ruling B-4):** no se resuelve acá. La alineación a la derecha ya está; las cifras
tabulares quedan como deuda declarada, cerrable con **un `Setter` en `Themes/DataGrid.axaml:97`**
el día que se quiera.

---

### Task 10.1: el guardián del borrado de los `DataGridCell.num` (ANTES de borrarlos)

**Files:** Create `tests/StockApp.Presentation.UiTests/ReportesAlineacionNumericaTests.cs`

**Interfaces:** Consumes `Themes/DataGrid.axaml` (vía `TestAppBuilder`), `DataGridCell`.

**El problema:** borrar 5 estilos porque "el global los cubre" es una hipótesis. Si el global
**no** los cubriera, la app perdería la alineación derecha de 12 columnas de dinero y nadie se
enteraría — no hay ni un test de UI en Reportes. La Task 6.3 ya validó esto para
`MovimientoHistorialView` con una mutación inversa; acá se hace un test positivo, porque son 5
archivos y 12 columnas.

- [ ] **Step 1: escribir el test que falla**

Montar **una** de las 5 (`StockCategoriaView`, la más chica, 77 líneas) con un
`StockCategoriaViewModel` real que devuelva ≥1 fila, correr el layout, buscar las
`DataGridCell` cuya `Classes` contenga `"num"` y assertear
`HorizontalContentAlignment == HorizontalAlignment.Right` en todas.

**Sin datos no hay celdas** — mismo fenómeno del Ruling B-8/B-16: un `DataGrid` sin `ItemsSource`
no realiza ninguna fila, así que no hay `DataGridCell` que inspeccionar y el test daría verde
vacuo. **Assertear primero que la cantidad de celdas `.num` encontradas es > 0.**

- [ ] **Step 2: correr y ver el verde (red escrita sobre el XAML sin tocar)**

Expected: PASS. Hoy pasa por el estilo **local**; no distingue de dónde viene la alineación. Eso lo
resuelve el Step 3.

- [ ] **Step 3: la mutación que da sentido al test — sacar el estilo GLOBAL**

Sacar el `StyleInclude` de `Themes/DataGrid.axaml` de `TestAppBuilder.cs`.
Expected: `DataGridEstiloRealTests` **rojo** (la Task 6.3 ya confirmó que se cae por
`FontSize`/`RowBackground`), y `ReportesAlineacionNumericaTests` **verde todavía** — porque el
estilo local de `StockCategoriaView:36` sigue ahí. **Ese verde es la prueba de que el test aún no
custodia lo que hace falta.** Revertir.

- [ ] **Step 4: la mutación al revés — sacar el estilo LOCAL**

Borrar `StockCategoriaView:35-39` (el `<DataGrid.Styles>` entero).
Expected: `ReportesAlineacionNumericaTests` **sigue verde** → **el global efectivamente cubre**, y
los 5 borrados de la Task 10.2 son seguros.
**Si se pone rojo, PARAR**: el global no cubre y los 5 estilos locales no son redundantes. Sería un
hallazgo que invalida la deuda entera. Revertir de todos modos (el borrado real es la Task 10.2).

- [ ] **Step 5: suite completa.**

---

### Task 10.2: P1 sobre las 5 vistas de Reportes + borrado de los 5 estilos locales

**Files:** Modify los 5 `.axaml`; Test: `GuardianDePatronTests.cs` (+5 filas).

#### Tabla de sustitución común a las 5

| Elemento | Hoy | Pasa a |
|---|---|---|
| raíz | `<DockPanel Margin="24">` | `<DockPanel Margin="{DynamicResource MargenVista}">` |
| título | `<TextBlock DockPanel.Dock="Top" Text="…" Classes="titulo-vista" Margin="0,0,0,16" />` | `<c:HeaderVista DockPanel.Dock="Top" Eyebrow="REPORTES" Titulo="…">` con los **botones** (no los filtros) en el slot `Acciones` |
| barra de `Grid.Row="0"` | `<StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="8" [VerticalAlignment="Center"] Margin="0,0,0,12">` con filtros **y** botones mezclados | los **filtros se quedan** en la card (mismo criterio que el `TextBox` de búsqueda de `ProductoListView` y el `WrapPanel` de `GastosView`); los 2 `Button` migran al header. `Spacing="{DynamicResource Espacio2}"` |
| `<DataGrid.Styles>` con `DataGridCell.num` | presente | **borrado, junto con el `<DataGrid.Styles>` vacío** |
| `CellStyleClasses="num"` | presente | **se quedan** |
| namespace | — | agregar `xmlns:c="using:StockApp.Presentation.Controls"` a las 5 |

#### Líneas exactas por archivo (verificadas)

| Archivo | raíz | `titulo-vista` | Texto del título | barra de acciones | `Button primary` | `Button secondary` | `DataGrid.Styles` a borrar |
|---|---|---|---|---|---|---|---|
| `ValorizacionView` | `:12` | `:14-17` | "Valorización de inventario" | `:23-26` (solo botones, **sin filtros**) | `:24` "Buscar" | `:25` "Exportar CSV" | `:35-40` |
| `StockCategoriaView` | `:12` | `:14-17` | "Stock por categoría" | `:23-26` (solo botones) | `:24` "Buscar" | `:25` "Exportar CSV" | `:35-39` |
| `MasMovidosView` | `:13` | `:15-18` | "Productos más movidos" | `:24-42` (**filtros + botones**) | `:40` "Buscar" | `:41` "Exportar CSV" | `:59-63` |
| `HistorialPorProductoView` | `:14` | `:16-19` | "Historial por producto" | `:25-60` (**filtros + botones**) | `:58` "Buscar" | `:59` "Exportar CSV" | `:77-81` |
| `AuditoriaLogView` | `:13` | `:15-18` | "Auditoría" | `:24-60` (**filtros + botones**) | `:58` "Buscar" | `:59` "Exportar CSV" | `:77-81` |

**Las 2 primeras son el caso fácil** (`ValorizacionView`, `StockCategoriaView`): su `StackPanel` de
`Grid.Row="0"` tiene **solo los 2 botones**, así que migra entero al slot `Acciones` y la fila
`Auto` del `Grid` se puede eliminar — **pero eso renumera todos los `Grid.Row` de abajo**.
**Recomendado: dejar la fila `Auto` vacía o cambiar `RowDefinitions` con cuidado**, y en cualquier
caso hacer estas dos primero, verificar visualmente, y recién después las otras tres.

**Las 3 últimas son el caso con filtros** (`MasMovidosView`, `HistorialPorProductoView`,
`AuditoriaLogView`): el `StackPanel` tiene `TextBlock` de etiqueta + `CalendarDatePicker` /
`AutoCompleteBox` / `NumericUpDown` **y después** los 2 botones. **Se parte en dos**: los filtros
quedan en `Grid.Row="0"`, los 2 botones se van al header.

**Cosas que NO se tocan en las 3 con filtros (verificadas, todas custodian bugfixes de 2026-08-19
documentados en comentarios in-line del propio archivo):**
- `HistorialPorProductoView:26-32` y `:34-45`: el `AutoCompleteBox` con `AsyncPopulator`,
  `ValueMemberBinding="{Binding Nombre, DataType={x:Type cat:ProductoDto}}"` y
  **`FilterMode="None"`**. Sacar `FilterMode="None"` reintroduce el filtro client-side de
  "empieza con" sobre resultados que el servidor ya matcheó por SKU/código/nombre.
- `AuditoriaLogView:25-32` y `:34-45`: el `AutoCompleteBox` con `FilterMode="Contains"` y
  `MinimumPrefixLength="0"`. El `0` es lo que permite ver "Todos" sin tipear.
- Los `beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True"` de los 6
  `CalendarDatePicker`.
- Los `<TextBlock Grid.Row="2" Text="Realizá una búsqueda para ver resultados" Classes="caption" …
  IsVisible="{Binding Items, Converter=…ColeccionVaciaConverter…}" />` (empty state) de
  `MasMovidosView:92-97`, `HistorialPorProductoView:124-129`, `AuditoriaLogView:103-108`.
  *Nota: podrían migrar a `c:EstadoVacio` — **queda fuera de alcance de B2**, no lo abras acá.*
- `MasMovidosView:78-80`, `:84-86` y las demás `<DataGridTextColumn.Header>` con un `TextBlock`
  `HorizontalAlignment="Right"` adentro: son encabezados alineados a mano para las columnas
  numéricas. **No los conviertas a `Header="…"` simple**, perderías la alineación del encabezado
  (el estilo global alinea la *celda*, no el *header*).

- [ ] **Step 1: guardián en rojo**

```csharp
[InlineData(typeof(ValorizacionView), "Valorización de inventario", "REPORTES")]
[InlineData(typeof(StockCategoriaView), "Stock por categoría", "REPORTES")]
[InlineData(typeof(MasMovidosView), "Productos más movidos", "REPORTES")]
[InlineData(typeof(HistorialPorProductoView), "Historial por producto", "REPORTES")]
[InlineData(typeof(AuditoriaLogView), "Auditoría", "REPORTES")]
```
+ los 5 tipos en `VistasDeLaTanda`. Expected: **5 fallos, todos de `HeaderVista`**. Margen: pasa
desde ya (`Margin="24"` ≡ `MargenVista`). Opacidades: pasa (no hay ninguna). Segundo primario:
pasa (una `primary` "Buscar" por vista). **Anotar los tres verdes** para que no se lean como falso
positivo.

**Cuidado con los acentos y los caracteres exactos:** `"Valorización de inventario"`,
`"Stock por categoría"`, `"Productos más movidos"`, `"Auditoría"`. Un `Assert.Equal` de string no
perdona.

- [ ] **Step 2: aplicar `ValorizacionView` sola, y verificarla**

Es la referencia de la tanda. Aplicar → guardián verde para su fila →
`ReportesAlineacionNumericaTests` verde → mirar el `Grid.RowDefinitions` resultante.

- [ ] **Step 3: aplicar las otras 4.**

- [ ] **Step 4: guardián + `ReportesAlineacionNumericaTests` + suite.**

- [ ] **Step 5: verificar el borrado de los 5 estilos**

```bash
grep -rn 'Selector="DataGridCell.num"' src/StockApp.Presentation/Views/
```
Expected: **cero coincidencias en todo `Views/`.** Es el criterio de aceptación #5 de la Fase B.

```bash
grep -rn 'CellStyleClasses="num"' src/StockApp.Presentation/Views/ | wc -l
```
Expected: el mismo número que antes de la task (**anotarlo en el Step 1**). Si bajó, se borró una
clase de columna por error.

- [ ] **Step 6: tres mutaciones**

1. `Titulo="Auditoría"` → `"Auditoria"` (sin tilde) → `Vista_TieneHeaderVistaConElTituloEsperado`
   rojo para esa fila. *Prueba que el guardián compara el string exacto, acentos incluidos.*
2. `Margin="{DynamicResource MargenVista}"` → `Margin="16"` en `MasMovidosView` →
   `Vista_TieneMargenExteriorEstandar` rojo.
3. `Classes="secondary"` → `Classes="primary"` en "Exportar CSV" de `ValorizacionView` →
   `Vista_NoTieneUnSegundoBotonPrimario` rojo.

- [ ] **Step 7: suite completa.**

---

### Task 10.3: cierre de la tanda 10

- [ ] **Step 1: suite completa.**
- [ ] **Step 2: auditoría de residuos**
```bash
grep -rn 'Opacity="0\.\|Foreground="Red"\|Margin="24"\|titulo-vista\|FontSize="\|Selector="DataGridCell.num"' \
  src/StockApp.Presentation/Views/Reportes/
```
Expected: cero coincidencias.
- [ ] **Step 3: verificación orgánica** — los 5 reportes en la app real, con datos. Mirar
  especialmente que las columnas de dinero **sigan alineadas a la derecha** tras el borrado de los
  5 estilos locales. Un test verde y una columna torcida conviven mal.
- [ ] **Step 4: commit.**

```
feat(ui): aplica el sistema de diseno a los 5 reportes

Las 5 son casi el mismo archivo: mismo DockPanel Margin=24, mismo titulo-vista,
misma card con barra de Grid.Row=0 y DataGrid. Cero tests de UI, cero gates de
permiso, cero x:Name, cero Opacity literal -- la tanda mas mecanica del refactor.

- HeaderVista con Buscar/Exportar CSV en el slot Acciones; los filtros (fechas,
  AutoCompleteBox, Top N) se quedan en la card: son filtros de la grilla, no
  acciones de la vista
- Se borran los 5 DataGridCell.num locales que quedaban. El esbozo los ubicaba
  en AuditoriaLogView:60 y HistorialPorProductoView:60; estan en :78 los dos.
  Con esto queda CERO en todo Views/ (criterio de aceptacion 5 de la Fase B):
  los cubre Themes/DataGrid.axaml:97 desde la tanda 2
- El borrado se valida con un test nuevo que monta StockCategoriaView CON datos
  (sin datos no hay DataGridCell que inspeccionar) y asserta la alineacion, mas
  las dos mutaciones cruzadas: sacar el estilo global vs. sacar el local
- No se tocan: FilterMode="None" de HistorialPorProducto (busqueda server-side),
  FilterMode="Contains" + MinimumPrefixLength="0" de Auditoria, los
  NormalizarFechaTipeada, ni los Header con TextBlock alineado a la derecha de
  las columnas numericas
```

---

## Task B2-T: stock/saldo negativo — color + palabra (transversal, cierra B2)

**Ruling B-6** (decisión del usuario, 2026-08-19): los sitios de `SignoNegativoBrushConverter` no se
migran repartidos por tanda, sino **de una vez**, al cierre de B2. Un cambio de semántica se hace
completo o no se hace: repartirlo en 4 commits deja la app comunicando lo mismo de dos formas
distintas durante semanas.

**Files:**
- Modify: `Views/Catalogo/ProductoListView.axaml`, `Views/Movimientos/MovimientoHistorialView.axaml`,
  `Views/Finanzas/ControlPoaView.axaml`, `Views/Finanzas/LibroCajaView.axaml`,
  `Views/Tareas/TareaListView.axaml`, `Views/Reportes/ValorizacionView.axaml`,
  `Views/Reportes/StockCategoriaView.axaml`, `Views/Reportes/HistorialPorProductoView.axaml`
- Create: `tests/StockApp.Presentation.UiTests/SignoNegativoBadgeTests.cs`
- **NO** modificar: `SignoNegativoBrushConverter.cs` ni `SignoNegativoBrushConverterTests.cs`

**Interfaces:** Consumes `c:BadgeEstado` (`Texto`, `Tono`), `SignoNegativoBrushConverter`.

> ### ⚠ Desvío del brief, para tu OK: es **color + palabra**, no color → palabra.
>
> El brief dice "migrar los sitios de `SignoNegativoBrushConverter` a `BadgeEstado` con palabra".
> Escribí **agregar** la palabra **conservando** el color. Tres razones, la primera es dura:
>
> 1. **Hay un test que asserta el color.** `TareaListViewTests:181-194`
>    (`Montar_TareaPendienteVencida_ElTituloQuedaEnRojo`) busca el `TextBlock` con `Text == "Vencida"`
>    y asserta `Assert.Equal(Color.Parse("#DC2626"), brushVencida.Color)`. Sacar el `Foreground` lo
>    rompe. La regla global de la Fase B es *"un test se reescribe para que verifique MEJOR, NUNCA
>    para que pase"* — y borrarlo para poder sacar el color sería exactamente lo contrario.
> 2. **El problema de accesibilidad es "color SIN palabra", no "color".** Sacar el color no ayuda a
>    nadie y le saca información al 92 % de los usuarios que sí lo distinguen. Agregar la palabra
>    cierra el hueco para el otro 8 %.
> 3. **`SignoNegativoBrushConverterTests` sigue teniendo sentido** y no hay que tocarlo.
>
> **Si preferís el reemplazo puro**, hay que (a) borrar o reescribir `TareaListViewTests:181-194`
> y (b) decidir qué pasa con `SignoNegativoBrushConverter` y su suite. Decilo y lo reescribo.

### La palabra NO es la misma en los 12 sitios

Un solo texto de badge no sirve: `StockActual` negativo significa *"stock negativo"*, `Saldo`
negativo en POA significa *"sobreejecutado"*, `DiasParaVencer` negativo significa *"vencida"*.

| # | Vista | Línea | Propiedad | Palabra propuesta | `Tono` |
|---|---|---|---|---|---|
| 1 | `Catalogo/ProductoListView` | `:90` | `StockActual` | `"Stock negativo"` | `Peligro` |
| 2 | `Movimientos/MovimientoHistorialView` | `:155` | `StockAnterior` | — **ninguna**, ver abajo | — |
| 3 | `Movimientos/MovimientoHistorialView` | `:164` | `StockNuevo` | `"Stock negativo"` | `Peligro` |
| 4 | `Finanzas/ControlPoaView` | `:37` | `Saldo` | `"Sobreejecutado"` | `Peligro` |
| 5 | `Finanzas/LibroCajaView` | `:35` | `SaldoFinal` | `"Saldo negativo"` | `Peligro` |
| 6 | `Finanzas/LibroCajaView` | `:74` | `Neto` (por mes) | `"Déficit"` | `Peligro` |
| 7 | `Tareas/TareaListView` | `:33` | `DiasParaVencer` (Pendientes) | `"Vencida"` | `Peligro` |
| 8 | `Tareas/TareaListView` | `:66` | `DiasParaVencer` (En curso) | `"Vencida"` | `Peligro` |
| 9 | `Reportes/ValorizacionView` | `:67` | `StockActual` | `"Stock negativo"` | `Peligro` |
| 10 | `Reportes/StockCategoriaView` | `:58` | `StockTotal` | `"Stock negativo"` | `Peligro` |
| 11 | `Reportes/HistorialPorProductoView` | `:96` | `StockAnterior` | — **ninguna** | — |
| 12 | `Reportes/HistorialPorProductoView` | `:105` | `StockNuevo` | `"Stock negativo"` | `Peligro` |

**Los sitios 2 y 11 (`StockAnterior`) NO llevan badge.** En las dos vistas de historial,
`StockAnterior` y `StockNuevo` están en columnas contiguas de la misma fila: poner el badge en las
dos daría dos badges por fila diciendo lo mismo. El estado que importa es el **resultante**
(`StockNuevo`). `StockAnterior` **conserva su `Foreground`** y nada más. *Es una decisión de diseño,
no un olvido: anotala en el ledger.*

> **PREGUNTA ABIERTA para el usuario:** los textos de arriba son **copy nuevo** — el Global
> Constraint dice "no se cambia copy de negocio". Estos no reemplazan copy existente, lo agregan,
> pero igual son palabras que el usuario va a leer en pantalla. **Necesito tu OK sobre las cuatro
> palabras: "Stock negativo", "Sobreejecutado", "Saldo negativo", "Déficit", "Vencida".**

**NO VERIFIQUÉ** los valores válidos del enum `TonoBadge` (`Controls/BadgeEstado.cs:25`
declara `TonoProperty` de tipo `TonoBadge`, pero no leí sus miembros). Las tandas 6 y 7 usaron
`Tono="Neutro"`. **Step 1 de la task: abrir `BadgeEstado.cs` y anotar los miembros reales del
enum.** Si no hay un tono de peligro, la task tiene que decidir si agregarlo o usar el que haya.

### Forma del cambio (los 10 sitios que llevan badge)

Hoy:
```xml
<TextBlock Text="{Binding StockActual, Converter={x:Static conv:CantidadConverter.Instance}}"
           Foreground="{Binding StockActual, Converter={x:Static conv:SignoNegativoBrushConverter.Instance}}"
           VerticalAlignment="Center" HorizontalAlignment="Right" Margin="4,0" />
```
Pasa a:
```xml
<StackPanel Orientation="Horizontal" Spacing="{DynamicResource Espacio1}"
            HorizontalAlignment="Right" VerticalAlignment="Center">
    <c:BadgeEstado Texto="Stock negativo" Tono="Peligro"
                   IsVisible="{Binding StockActual, Converter={x:Static conv:EsNegativoConverter.Instance}}" />
    <TextBlock Text="{Binding StockActual, Converter={x:Static conv:CantidadConverter.Instance}}"
               Foreground="{Binding StockActual, Converter={x:Static conv:SignoNegativoBrushConverter.Instance}}"
               VerticalAlignment="Center" Margin="4,0" />
</StackPanel>
```

**Hace falta un converter nuevo, `EsNegativoConverter`** (`decimal`/`int` → `bool`), porque
`SignoNegativoBrushConverter` devuelve un `IBrush`, no un `bool`, y `IsVisible` necesita un `bool`.
**NO VERIFIQUÉ** si ya existe algo equivalente en `Converters/`. Step 2 de la task:
`ls src/StockApp.Presentation/Converters/` y `grep -rn 'Negativo' src/StockApp.Presentation/Converters/`.
Si no existe, se crea con su propia suite (es lógica pura, va en `StockApp.Presentation.Tests`, no
en `UiTests`).

**Cuidado con el ancho de columna:** meter un badge al lado de un número en una celda de
`DataGrid` con `Width="Auto" MinWidth="100"` puede desbordar. **Verificación orgánica obligatoria**
antes del commit: las 8 vistas, con al menos un valor negativo real en cada una.

### Steps

- [ ] **Step 1:** abrir `Controls/BadgeEstado.cs` y anotar los miembros de `TonoBadge`. Abrir
  `Controls/Componentes.axaml` y anotar cómo se ve cada tono.
- [ ] **Step 2:** buscar si ya existe un converter a `bool` de signo. Si no, crearlo con su suite en
  `StockApp.Presentation.Tests` (casos: negativo → `true`; cero → `false`; positivo → `false`;
  `null` → `false`), en rojo primero.
- [ ] **Step 3: escribir el test que falla.** `SignoNegativoBadgeTests.cs`: montar **una** vista de
  cada módulo con datos negativos y positivos, y assertear que el `BadgeEstado` con el `Texto`
  esperado **está visible** en la fila negativa y **no** en la positiva. Mínimo 4 vistas:
  `ProductoListView` (Catálogo), `LibroCajaView` (Finanzas), `TareaListView` (Tareas),
  `StockCategoriaView` (Reportes). **Con datos** — los 10 sitios viven dentro de `CellTemplate` o
  `ItemTemplate`, o sea que sin `ItemsSource` no se realiza ninguno (Ruling B-16).
- [ ] **Step 4: correr y ver el rojo.** Expected: FAIL, "no hay ningún `BadgeEstado` con
  `Texto == 'Stock negativo'`".
- [ ] **Step 5: aplicar los 10 sitios.**
- [ ] **Step 6: ver el verde.**
- [ ] **Step 7: los tests preexistentes que tocan estos sitios**
  Run: `--filter "FullyQualifiedName~TareaListViewTests|FullyQualifiedName~SignoNegativoBrushConverter"`
  Expected: PASS **sin tocar un solo assert** — incluido
  `Montar_TareaPendienteVencida_ElTituloQuedaEnRojo`, que es el que obliga a conservar el color.
- [ ] **Step 8: validar por mutación**
  1. Borrar el `IsVisible` del `BadgeEstado` de `TareaListView:33` → el badge queda **siempre**
     visible → el caso "fila al día NO tiene badge" se pone **rojo**. Revertir.
  2. Borrar el `Foreground="{Binding …SignoNegativoBrushConverter…}"` de `TareaListView:33` →
     **`Montar_TareaPendienteVencida_ElTituloQuedaEnRojo` rojo**. Revertir.
     *Las dos mutaciones juntas prueban que el color Y la palabra están custodiados por separado —
     que es exactamente la razón de este ruling.*
- [ ] **Step 9: grep de cierre**
```bash
grep -rn 'SignoNegativoBrushConverter' src/StockApp.Presentation/Views/
```
Expected: **12 coincidencias** (las mismas 12 de antes: el color se conserva).
```bash
grep -rn 'BadgeEstado' src/StockApp.Presentation/Views/ | grep -c 'Peligro'
```
Expected: **10**.
- [ ] **Step 10: verificación orgánica de las 8 vistas con datos negativos reales.** Mirar el ancho
  de las columnas. **Es el paso que ningún test puede reemplazar.**
- [ ] **Step 11: suite completa + commit.**

```
feat(ui): stock y saldo negativos comunican con palabra, no solo con color

El color rojo de SignoNegativoBrushConverter era el UNICO indicador de un valor
negativo en 12 sitios de 8 vistas de 4 modulos: ilegible para daltonismo rojo-
verde, que es el 8% de los hombres.

Se AGREGA un BadgeEstado con palabra y se CONSERVA el color, en vez de
reemplazarlo. El problema es "color sin palabra", no "color": sacar el color le
quita informacion a quien si lo distingue, y ademas romperia
TareaListViewTests.Montar_TareaPendienteVencida_ElTituloQuedaEnRojo, que asserta
#DC2626 -- un test que verifica comportamiento real y que no se toca para que
pase otra cosa.

La palabra NO es la misma en los 12 sitios: un StockActual negativo es "Stock
negativo", un Saldo POA negativo es "Sobreejecutado", un Neto mensual negativo
es "Deficit", DiasParaVencer negativo es "Vencida".

Los dos sitios de StockAnterior (MovimientoHistorialView e
HistorialPorProductoView) NO llevan badge: estan en la columna contigua a
StockNuevo y darian dos badges por fila diciendo lo mismo. El estado que importa
es el resultante.

Va en un solo commit al cierre de B2 y no repartido por tanda (Ruling B-6):
repartir un cambio de semantica deja la app comunicando lo mismo de dos formas
distintas durante semanas de refactor.
```

---

## Criterios de aceptación de B2

Al cerrar B2 (tandas 8, 9 y 10 + Task B2-T), tienen que valer los 10:

1. **`dotnet test StockApp.sln` verde**, y verde **también entre commits** — no se deja la suite en
   rojo entre tasks (error cometido en la Task 6.0).
2. **Las 22 vistas de primer nivel de B2 tienen `HeaderVista`** con su `Eyebrow` de módulo
   (`FINANZAS` ×12, `DOCUMENTOS` ×2, `TAREAS` ×2, `REPORTES` ×5 — el 22º es
   `MaestrosFinanzasView`/`ImportacionView`, ya contados en los 12).
3. **Las 7 vistas embebidas NO tienen `HeaderVista` ni `MargenVista`**, custodiado por
   `VistaEmbebida_NoDuplicaElMargenDeVista`.
4. **Cero `Selector="DataGridCell.num"` en todo `Views/`** (criterio 5 de la Fase B, cerrado en la
   tanda 10).
5. **Cero `Foreground="Red"` en Finanzas, Documentos y Tareas.** Queda 1 en toda la app
   (`UsuariosAdminView:143`), para la tanda 11.
6. **Cero `Opacity="0.x"` literal en las 29 vistas**, verificado **por grep archivo por archivo**,
   no por el guardián (Ruling B-16: 25 de los 30 son invisibles para él).
7. **Cero `Classes="badge-inactiva"` en Finanzas** (los 4 sitios migrados a `c:BadgeEstado`).
8. **Los 12 gates de Documentos tienen red validada por mutación** con la matriz rol × estado
   (Task 9.0, 11 mutaciones + la del `OR`).
9. **`EstablecerPermisos` no-op reducido de 6 archivos a 2** (quedan los dos de Inicio, para B3).
10. **Verificación orgánica hecha al cierre de las tandas 8, 9 y 10**, con la app real corriendo.
    La spec la pide explícitamente para la 8. Las tandas 6 y 7 la dejaron pendiente; **B2 no.**

## Preguntas abiertas de B2 (requieren decisión del usuario)

1. **Task 8.0 = quinto commit de la tanda 8, o se pliega dentro de la 8.3.** El brief dijo 4 tasks.
2. **Task B2-T: ¿color + palabra (lo que escribí) o palabra en lugar de color (lo que dice el
   brief)?** Lo segundo obliga a reescribir `TareaListViewTests:181-194`.
3. **Las 5 palabras nuevas de la Task B2-T**: "Stock negativo", "Sobreejecutado", "Saldo negativo",
   "Déficit", "Vencida". Es copy nuevo en pantalla.
4. **Ruling B-20: dos `HeaderVista` por vista de detalle, o una propiedad `Titulo` nueva en
   `DocumentoFormViewModel` y `TareaFormViewModel`.**
5. **`DocumentoListView`: ¿los 6 pares etiqueta+control de los filtros se envuelven en
   `CampoFormulario` o se dejan como están?** (Task 9.1, fila `:25/:37/:102/:107/:119/:131`.)
6. **`CalendarioPagosView:12`: `Spacing="20"` → `Espacio5` (24), o se deja el 20 literal?** Es el
   único valor de la app fuera de la escala.
7. **`TareaFormView:45`: el `FontSize="16"` del título de detalle pasa a `Classes="seccion"`.** Si
   `seccion` no es 16 px, es un cambio de escala tipográfica visible.

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
- [ ] **`AdjuntosPanelView` vs `AdjuntosDocumentoPanelView`: DESCOPADA la unificación (Ruling B-13, aprobado por el usuario el 2026-08-19).**

  La spec dice "unificar los gemelos de 63 y 62 líneas". Los diffeé: **no son gemelos**. Bindean a **dos tipos de ViewModel distintos** (`AdjuntosPanelViewModel` en `ViewModels.Finanzas`, `AdjuntosDocumentoPanelViewModel` en `ViewModels.Documentos`), con **APIs distintas** (`PuedeAgregar` + `PuedeQuitar` vs un único `PuedeModificar`) y DTOs de namespaces distintos.

  Unificarlos exige (a) reconciliar dos contratos de ViewModel y (b) renombrar una View — y **"renombrar o mover Views" está explícitamente fuera de alcance** en la sección 5 de la spec, custodiado por `ViewLocatorTests` (que tiene 2 casos dedicados a `AdjuntosPanelView` justamente porque este panel ya se creó una vez sin su View y hubo regresión).

  **La tanda 13 de la spec se contradice con la sección 5 de la spec; Ruling B-13 la resuelve a favor de la sección 5.** En esta tanda se hace **solo armonización visual** (que los dos se vean idénticos). La unificación real de los ViewModels queda como trabajo aparte, sin fecha.

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