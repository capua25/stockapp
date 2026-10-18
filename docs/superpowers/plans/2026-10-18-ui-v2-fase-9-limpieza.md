# Fase 9: Limpieza (UI v2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cerrar la convivencia de los dos temas: borrar del tema viejo SOLO lo que no tiene ningún uso, reemplazar `GuardianDePatronTests` por el guardián del sistema nuevo sin perder la custodia de lo que sigue en v1 (la importación), auditar residuos con evidencia y cerrar la deuda de debounce real en los datos fijos.

**Architecture:** Cinco tasks en serie sobre `feat/ui-v2`. (1) Los datos fijos de los ViewModels con `RecargaConDebounce` bajan la espera a cero, con un guardián de cobertura por reflexión. (2) El guardián nuevo se parte en dos archivos: `GuardianSistemaV2Tests` (cobertura de todas las vistas, piezas viejas prohibidas en v2, botón con variante, opacidades, overlays) y `GuardianImportacionV1Tests` (los invariantes P0-P9 que siguen aplicando a las tres vistas de importación), más `ContrasteV2Tests` sobre los tokens nuevos (spec §8.2). (3) Antes de borrar se fija la custodia de la v1: capturas de referencia de la importación y un test de que todo recurso referenciado se resuelve; después se borra lo que la auditoría marca sin uso. (4) Residuos de código, comentarios y mockups. (5) GATE 2: smoke de navegación por todas las pantallas a los dos tamaños, incluida la importación v1, y cierre documental.

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`), SkiaSharp (`ComparadorImagenes` de `tools/StockApp.UiV2.Herramientas`), CLI `codegraph`.

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`: fila "9 Limpieza" de §7, §8.2 ("Se reemplazan"), §9 (fila `NuevaImportacionView`) y los bloques "Deuda abierta tras la Fase …" de §7 (L595-L676 y L869-L955 en el commit de este plan). Convenciones 1 a 51 de `docs/ui-v2/herramientas.md`.

## Alcance (lo decidido por Martin, 2026-10-18)

- **La Fase 8 (Importación) NO va: la importación QUEDA EN v1.** `ImportacionView`, `NuevaImportacionView` e `HistorialImportacionesView` no se tocan y conservan TODO lo que usan del tema viejo. No hay pantallas nuevas: no hay GATE 1.
- Se borra SOLO lo que la auditoría de la Task 3 muestra sin ningún uso, con la salida del comando pegada en el ledger.
- Plazo comprometido: 2026-10-20. Ejecución en el worktree `ui-v2` sobre `feat/ui-v2`, sin worktree propio (es la única fase activa). Arranca DESPUÉS de cerrar el GATE 2 integrado de las fases 4e a 7 (`docs/superpowers/plans/2026-10-18-ui-v2-gate2-integrado.md`) y de los arreglos que ese GATE dispare.

## Inventario de la auditoría previa (HEAD `f388e83`, a re-medir en la Task 3)

Vistas que siguen en v1 (raíz sin `ui2`): `Views/Finanzas/ImportacionView.axaml`, `NuevaImportacionView.axaml`, `HistorialImportacionesView.axaml`. `MainWindow.axaml` (host sin estilos propios), `EntradaRegistroView`/`SalidaRegistroView` (envuelven `MovimientoFormControl`, v2) y `MarcoReporte` (marco v2 sin raíz propia) no son v1.

Lo que la importación usa del tema viejo (evidencia: `grep -oE` sobre los tres `.axaml`):

| Pieza | Usos en la importación | Queda viva |
|---|---|---|
| `c:HeaderVista` (+ su `ControlTheme`, clases `micro`, `titulo-vista`, `body`) | `ImportacionView` | sí |
| `c:EstadoVacio` (+ su `ControlTheme`, clase `caption`) | `NuevaImportacionView`, `HistorialImportacionesView` | sí |
| `Button.primary`, `Button.secondary`, `Button.ghost` (`Themes/Controls.axaml`) | 3, 4 y 2 usos | sí |
| `Border.card`, `TextBlock.seccion` | 3 y 4 usos | sí |
| Estilos globales `Button`, `TextBox`, `ComboBox`, `NumericUpDown`, `Window, UserControl` (fuente), todo `Themes/DataGrid.axaml` salvo lo que la Task 3 pruebe muerto | grillas y formularios del asistente | sí |
| Tokens `Espacio2..5`, `MargenVista`, `PaddingCelda`, `WarningBrush` y los que consumen los estilos vivos | directos | sí |
| `EstadoFilaBrushConverter`, `ErrorServidorBrushConverter`, `EstadoRevertidaConverter` | `NuevaImportacionView`, `HistorialImportacionesView` | sí |
| Prefijo `V2` de los tokens nuevos | (consecuencia: los tokens viejos siguen vivos con las mismas claves sin prefijo) | sí (Q-3) |

Sin ningún uso de producción (candidatos a borrar en la Task 3; la Task 3 re-mide):

| Pieza | Evidencia | Usos que quedan (solo tests) |
|---|---|---|
| `Controls/BadgeEstado.cs` + `ControlTheme` | ningún `<c:BadgeEstado` en `src` | `ComponentesBasicosTests`, `SignoNegativoBadgeTests`, `GrillaStockHelpers`, `TokensDisenioTests`, 3 tests V2 que afirman su ausencia |
| `Controls/CampoFormulario.cs` (viejo) + `ControlTheme` | todos los `CampoFormulario` de `src` son `v2:` | `CampoFormularioTests` |
| `Controls/TarjetaMetrica.cs` (viejo) + `ControlTheme` | Inicio usa `v2:TarjetaMetrica` | `ComponentesBasicosTests` |
| `ActivoOpacidadConverter`, `SignoNegativoBrushConverter`, `ColeccionVaciaConverter`, `IconoChevronConverter` | ningún `.axaml` los usa; en `.cs` solo comentarios | sus tests de converter, `SignoNegativoBadgeTests`, `PatronHelpers` |
| Selectores `Border.sidebar Button.ghost*` (5), `Button.danger*` (4), `Button.compacto`, `TextBox.error`, `ComboBox.error` | la clase `sidebar` solo existe en `Controls.axaml`; `danger`, `compacto`, `error` sin uso en `.axaml` ni `Classes.Add` | ninguno |
| `DataGridCell.num TextBlock` de `Themes/DataGrid.axaml` | ninguna vista v1 usa `.num` (las 16 que la usan son v2) | ninguno; se borra solo si las comparaciones quedan verdes |
| Tokens sin consumidor tras lo anterior (`Sidebar*`, `DangerHover/Pressed`, `SuccessBrush`, `InfoBrush`, `*SuaveBrush` de la insignia, `RadioChico`, `Espacio6`, `Espacio7`, `PaddingHolgado`, `SombraElevada`, `SombraModal`, `PaddingCompacto` y sus `Color*`) | `grep -rlw` sin usos en `src` | `TokensDisenioTests`, `SidebarContrasteTests`, `ControlesConsumenTokensTests` |

Otros hallazgos con evidencia:
- `AccesoLimitadoView` (v2) usa `{DynamicResource FondoBrush}` (viejo, `#F8FAFC`) en su raíz: lo atrapa el guardián nuevo (Task 2).
- `Mostrando*`: 0 propiedades ni bindings (solo prosa en 6 comentarios).
- Mockups: 164 por pantalla, 0 huérfanos (todos citados por un test de `VisualTests`); 117 de los 140 que dibujan sidebar tienen el sidebar viejo (sin "Catálogos").
- `cref` rotos en `StockApp.Presentation` (build con `GenerateDocumentationFile`): `EjecutarCorridaAsync`, `OperationCanceledException`, `ReconciliarDumpHuerfanosAsync`, `ShowDialog{T}` (los de otros proyectos, fuera de alcance).
- VMs de listado de catálogo sin vista propia (`ZonaListViewModel` …): VIVOS, los usa `CatalogosViewModel` por DI (convención 46).
- `Contraste` v2 medido: terciario `#78716C` sobre fondo `#F7F7F5` = 4,473:1 (bajo AA 4,5) y grupo `#A8A29E` sobre superficie = 2,522:1 (Q-4).

## Global Constraints

- Todos los comandos corren desde `/home/capua25/workspace/stockapp-worktrees/ui-v2` (rama `feat/ui-v2`, base: el commit de este plan), como `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <comando>`.
- CodeGraph: el MCP `codegraph_explore` responde con el índice del checkout PRINCIPAL; en este worktree se usa la CLI `codegraph` (`codegraph sync` si está desactualizado; después `codegraph query|node|callers|impact`). `grep`/Read como fallback, y Read directo para `.axaml`, `.md`, `.html` y `.css` (no indexados).
- La importación v1 (`ImportacionView`, `NuevaImportacionView`, `HistorialImportacionesView`, sus VMs y sus tests `NuevaImportacion*Tests`) NO se edita en ninguna task.
- `docs/ui-v2/tokens.json` es la fuente de `Themes/V2/Tokens.axaml`; nunca editar los generados a mano. Los tokens nuevos NO cambian en esta fase (Q-4).
- Build obligatorio tras cada cambio de `.axaml` o de un `.cs` de `src/`: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde). Tests de a UN `.csproj` por comando, siempre con `--blame-hang-timeout 5m`; nunca dos `dotnet test` a la vez (comparten `obj/`).
- Sin comentarios con `--` en `.axaml` ni `.csproj` (AVLN1001, cascadea a MSB4025).
- TDD donde hay comportamiento nuevo (guardianes, tests nuevos): test primero, rojo REAL pegado en el ledger, código mínimo, verde. Un test que nace verde (caracterización) se declara así y su custodia es una mutación con el rojo pegado.
- Mutaciones QUITANDO, nunca agregando: `export SCRATCH=~/.cache/stockapp-ui-v2-fase-9 && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla (líneas o fragmentos); `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas; se corre el test y se PEGA el rojo; se restaura con `cp "$SCRATCH/<nombre>.bak" <archivo>` y `git diff --quiet -- <archivo>` confirma.
- Borrar un test solo si lo que custodiaba deja de existir, o si otro test (nombrado en el ledger) ya custodia lo mismo (convención 13). Nunca se borra un test de algo que sigue vivo.
- Suite verde en cada commit. En este worktree los rojos de `StockApp.Infrastructure.Tests` y `StockApp.Api.Tests` por fixtures `.ods` ausentes son FALSOS conocidos (confirmar por nombre); `ApiStartupMigrationTests.Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests` es intermitente conocido; cualquier otro rojo es real.
- Línea base: se mide en la Task 1 Step 1 (total por proyecto) y cada task anota el total tras su commit; solo baja por tests borrados con su motivo (lo custodiado no existe, o el reemplazo nombrado).
- Sin referencias al plan en el código (nada de "Task 3", "Q-4" en `.cs`/`.axaml`/`.html`).
- Las referencias de línea (`L123`) son las del commit de este plan; tras editar un archivo, ubicar por el texto citado.
- Commits: conventional commits en español, uno por task, SIN `Co-Authored-By` ni línea de atribución de IA; `git add` con rutas exactas (nunca `-A` ni `.`), verificado con `git diff --cached --name-only`; sin push, merge, rebase, amend, `git reflog expire`, `git gc` ni `git prune`.
- Ledger git-ignored: `.superpowers/sdd/2026-10-18-ui-v2-fase-9-limpieza/progress.md`. Las Q-* se contestan ANTES de la Task 1.

## Review Focus

1. **La importación v1 pierde un estilo por un borrado.** Caso: se borra un selector o un token que parecía muerto pero lo consume un estilo vivo (una `DataGridCell` del asistente toma su padding de `PaddingCeldaGrilla`); el botón "Siguiente" queda con el gris de Fluent o la grilla sin separadores. Esperado: la importación se ve igual que antes de la fase. Test: `ImportacionV1SinCambiosTests` (Task 3, capturas de referencia tomadas ANTES de borrar, a los dos tamaños).
2. **Un recurso borrado sigue citado y falla en silencio.** Caso: `{DynamicResource Espacio3}` sin definición no explota: el control cae a su valor por defecto. Esperado: rojo de test. Test: `RecursosReferenciados_SeResuelvenEnLaApp` (Task 3).
3. **Una vista v2 dependía sin saberlo de un estilo viejo global.** Caso: se borra `DataGridCell.num TextBlock` viejo y las celdas numéricas v2 pierden un setter que la v2 no repite. Esperado: si cambia un píxel, la regla queda viva y anotada. Test: la suite completa de comparaciones de `VisualTests` a los dos tamaños (Task 3 Step 5).
4. **Un ViewModel nuevo con debounce cuyos datos fijos esperan 300 ms reales.** Caso: la próxima pantalla con `RecargaConDebounce` repite el bug de Libro caja (`2de3ad5`). Esperado: rojo hasta que su fábrica fije `EsperaDebounce = TimeSpan.Zero`. Test: `TodoViewModelConDebounce_TieneSuFabricaVerificada` (Task 1).
5. **Una vista nueva (o una de importación migrada mañana) queda fuera de todo guardián.** Esperado: rojo de cobertura que pide ubicarla en una lista. Test: `Guardian_CubreTodasLasVistasDelEnsamblado` y `Listas_SonDisjuntas` (Task 2).

---

## Preguntas previas (Q-*): se contestan ANTES de la Task 1

El plan está redactado con la opción A de cada una. Si Martin elige B, se aplica el cambio indicado y se anota en el ledger.

| # | Pregunta (evidencia) | A (recomendada, la que implementa el plan) | B (alternativa) |
|---|---|---|---|
| Q-1 | **Restos del acordeón del sidebar.** `GrupoNavegacion.EstaExpandido`, `AlternarGrupo` y la persistencia `IServicioPreferenciasSidebar` (3 archivos de `src`, registro en `App.axaml.cs:328`) quedaron sin uso en la vista desde la Fase 1, pero `ShellMainViewModel` los recibe por constructor: 56 `new ShellMainViewModel(` en 34 archivos de tests, y `ShellMainViewGatesTests.Montar` (spec §8.1, "se mantiene tal cual") setea `EstaExpandido`. | Quedan VIVOS como deuda declarada (sin consumidor, inocuos). Solo se borra `IconoChevronConverter` (sin ningún uso). Borrarlos toca 34 archivos de test y una red de §8.1 para cero cambio visible: no entra en el plazo. | Se borran (propiedad, comando, servicio, registro DI, parámetro del constructor y sus tests), con una task más (~2 h) y la excepción de §8.1 para `ShellMainViewGatesTests.Montar`. |
| Q-2 | **Sidebar viejo en los mockups.** 117 de los 140 mockups con sidebar lo tienen con la navegación anterior (Q-SIDEBAR-MOCKUPS = A de la Fase 6 lo dejó para la 9). Las comparaciones lo ocultan (`ocultarEnMockup: [".v2-sidebar"]`), así que no cambia ningún test. | Se reemplaza el `<aside class="v2-sidebar">…</aside>` por el de `shell/sidebar-admin.html` con un script, SOLO en los mockups cuyo sidebar es la variante Administrador (los que tienen el ítem "Usuarios"), conservando el ítem activo de cada uno. Los de operador se listan en la deuda. | Se deja como está y se declara en la deuda (los mockups son contrato del contenido, no del sidebar). |
| Q-3 | **Prefijo `V2` de los tokens.** La Fase 0 previó vaciar `prefijo` de `tokens.json` en la 9; con la importación en v1 los tokens viejos siguen vivos con las mismas claves (`FondoBrush`, `BordeBrush` …) y el vaciado choca. | Se conserva el prefijo `V2`. Se corrigen los comentarios que prometen el vaciado (`App.axaml:21`, `GeneradorTokensTests.cs:71`). | Se renombran los tokens viejos a `V1*` en los estilos y vistas de importación y se vacía el prefijo: toca las tres vistas de importación (fuera del alcance acordado) y regenera todo. |
| Q-4 | **Contraste de los tokens v2 (spec §8.2).** Medido: terciario sobre fondo 4,473:1 (breadcrumb y pie sobre el fondo de la app; AA pide 4,5) y grupo sobre superficie 2,522:1 (headers de sección del sidebar, versión, KPI sin dato). Los tokens son los aprobados en §2.1. | `ContrasteV2Tests` exige AA en todos los pares de texto y fija esos dos como excepciones declaradas con piso igual al medido (no pueden empeorar). Sin cambio visual. Los dos van a la deuda como propuesta de ajuste de token para después del plazo. | Se ajustan `#78716C` y `#A8A29E` en `tokens.json` (p. ej. terciario `#74706B`, grupo `#78716C`), se regeneran mockups y PNG y se re-aprueban las comparaciones: pide un GATE con Martin. |
| Q-5 | **`MarcoReporteTest.MostrarContenido`** (deuda de la Fase 6: "la Fase 9 los pasa a datos por servicio"): 15 usos en `ReportesStockOrdenTests`, `DataGridColumnasAutoAjusteTests`, `GrillasSinRecorteBarridoTests`, `DataGridCeldaNumericaOrdenTests` fuerzan `HayDatos` y asignan `ItemsSource` sin ViewModel. | Se pasan a un ViewModel con servicio de datos fijos (`Mock` del servicio del reporte, como `Datos*` de `VisualTests`), se borra `MarcoReporteTestHelpers.cs` (Task 4). | Se deja el helper y se declara en la deuda. |
| Q-6 | **Cómo se verifica la importación v1 en el GATE 2.** `ShellMainViewModel.ImportacionHabilitada` es `internal const bool = false` (L63): en la app real no se llega a "Importar planillas". | Build local TEMPORAL con la constante en `true` solo para el smoke, sin commit: antes de lanzar se guarda `cp` del archivo en `$SCRATCH`, al terminar se restaura y `git diff --quiet -- src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` lo confirma (pegado en el ledger). Además queda la custodia headless de la Task 3. | Solo la custodia headless de la Task 3 (capturas de referencia); el smoke orgánico no ve la importación. |

---

### Task 1: Datos fijos sin espera de debounce

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/DatosSinDebounceTests.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductos.cs` (`Listado` L71, `ListadoQueNoTermina` L87, `ListadoSinPermiso` L97)
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosGastos.cs` (`Listado` L118)
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosControlPoa.cs` (`Vm` L37)
- Modify (según la auditoría del Step 6): los tests de `StockApp.Presentation.UiTests` que construyen `ProductoListViewModel`, `GastosViewModel`, `ControlPoaViewModel` o `LibroCajaViewModel` y cambian un filtro con debounce.

**Interfaces:**
- Consumes: `internal TimeSpan EsperaDebounce { get; set; }` de los cinco VMs (convención 34; `InternalsVisibleTo` ya incluye `VisualTests` y `UiTests`, `StockApp.Presentation.csproj` L34-L45).
- Produces: la convención 52 (Task 5): "toda fábrica de datos fijos de un VM con `RecargaConDebounce` fija `EsperaDebounce = TimeSpan.Zero`".

- [ ] **Step 1: Medir la línea base**

Run, de a uno: `dotnet test tests/<Proyecto>/<Proyecto>.csproj --blame-hang-timeout 5m` para `StockApp.Presentation.Tests`, `StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests` y `StockApp.UiV2.Herramientas.Tests`, más `dotnet test StockApp.sln --blame-hang-timeout 5m` para el total.
Expected: todo verde salvo los falsos conocidos. Pegar los totales en el ledger ("Línea base").

- [ ] **Step 2: Escribir el guardián**

`DatosSinDebounceTests` (xUnit v3, `[Fact]` como `DatosLibroCajaTests`):

```csharp
[Fact] public void Productos_Listado_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosProductos.Listado([]).EsperaDebounce);
[Fact] public void Productos_ListadoQueNoTermina_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosProductos.ListadoQueNoTermina().EsperaDebounce);
[Fact] public void Productos_ListadoSinPermiso_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosProductos.ListadoSinPermiso().EsperaDebounce);
[Fact] public void Gastos_Listado_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosGastos.Listado(_ => Task.FromResult<IReadOnlyList<Gasto>>([])).EsperaDebounce);
[Fact] public void ControlPoa_Vm_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosControlPoa.Vm(() => Task.FromResult<IReadOnlyList<ControlPoaLineaDto>>([])).EsperaDebounce);
[Fact] public void LibroCaja_Vm_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosLibroCaja.Vm(() => Task.FromResult(DatosLibroCaja.Octubre)).EsperaDebounce);
[Fact] public void Documentos_ListadoVm_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosDocumentos.ListadoVm(() => Task.FromResult(DatosDocumentos.Activos)).EsperaDebounce);

[Fact]
public void TodoViewModelConDebounce_TieneSuFabricaVerificada()
{
    // Tipos de StockApp.Presentation con una propiedad de instancia NO pública "EsperaDebounce" de tipo TimeSpan.
    var conDebounce = typeof(ViewModelBase).Assembly.GetTypes()
        .Where(t => t.GetProperty("EsperaDebounce", BindingFlags.Instance | BindingFlags.NonPublic)?.PropertyType == typeof(TimeSpan))
        .Select(t => t.Name).OrderBy(n => n).ToArray();
    Assert.Equal(new[] { "ControlPoaViewModel", "DocumentoListViewModel", "GastosViewModel", "LibroCajaViewModel", "ProductoListViewModel" }, conDebounce);
}
```

Mensaje del último `Assert` (con `Assert.True` y texto si hace falta): "un ViewModel con RecargaConDebounce nuevo necesita su fábrica de datos fijos con EsperaDebounce = TimeSpan.Zero y su fila en este archivo". Las firmas son las del commit del plan (`DatosDocumentos.Activos` L45, `DatosLibroCaja.Octubre` L34, `DatosGastos.Listado` L118); si una cambió, ajustar solo el argumento.

- [ ] **Step 3: Correrlo y pegar el rojo**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~DatosSinDebounceTests"`
Expected: FAIL en las 3 de Productos, Gastos y Control POA (`Expected: 00:00:00 Actual: 00:00:00.3000000`); verdes Libro caja, Documentos y el de cobertura (caracterización).

- [ ] **Step 4: Bajar la espera en las fábricas**

En cada `return new XViewModel(…)` de las cinco fábricas: `{ EsperaDebounce = TimeSpan.Zero }`, con el comentario de `DatosLibroCaja.Vm` ("Debounce en cero: con los 300 ms reales, un cambio de filtro dispara una carga diferida que compite por el turno con la carga explícita y el resultado depende de cuánto tarde la máquina").

- [ ] **Step 5: Verde**

Run: el filtro del Step 3, y después `VisualTests` completo.
Expected: PASS; `VisualTests` sin rojos nuevos (la espera en cero no cambia lo que se compara).

- [ ] **Step 6: Auditar los VMs construidos a mano en `UiTests`**

Run: `grep -rn "new \(ProductoListViewModel\|GastosViewModel\|ControlPoaViewModel\|LibroCajaViewModel\|DocumentoListViewModel\)(" tests/StockApp.Presentation.UiTests --include=*.cs`
Sitios conocidos sin `EsperaDebounce`: `GastosViewColumnasAnchoTests`, `AccesoRevocadoPantallasFinanzasTests`, `GastosViewTests`, `ExportarPdfBotonesGuardianTests` (Gastos, Control POA, Libro caja), `V2/SinCaltV2Tests`, `V2/ProductoListViewV2Tests` (2), `LibroCajaViewTests`, `SignoNegativoBadgeTests`. Regla por sitio: si el test cambia después de construir una propiedad con debounce (`FiltroBusqueda`; `FechaDesde`, `FechaHasta`, `ProveedorSeleccionado`, `FuenteSeleccionada`, `RubroSeleccionado`, `LineaPoaSeleccionada`; `Ejercicio`; `Anio`, `Mes`, `VerAnioCompleto`; los filtros de Documentos) y afirma sobre datos cargados → `EsperaDebounce = TimeSpan.Zero`. Si el test es SOBRE la espera (afirma que no consulta antes de tiempo) → se deja y se anota "custodia el debounce". Si no toca esas propiedades → "no aplica". Tabla sitio / decisión / motivo en el ledger.

- [ ] **Step 7: Mutación**

Quitar `{ EsperaDebounce = TimeSpan.Zero }` (las líneas agregadas) de `DatosGastos.Listado`; correr el filtro del Step 3; pegar el rojo de `Gastos_Listado_SinEspera`; restaurar.

- [ ] **Step 8: Suite y commit**

Run: `UiTests` completo (si el Step 6 tocó algo) y `VisualTests` completo. Anotar totales.

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/DatosSinDebounceTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductos.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosGastos.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosControlPoa.cs <archivos del Step 6>
git diff --cached --name-only
git commit -m "test(ui-v2): datos fijos de productos, gastos y control POA sin espera de debounce, con guardián de cobertura"
```

---

### Task 2: Guardián del sistema nuevo y contraste v2

**Files:**
- Create: `tests/StockApp.Presentation.UiTests/GuardianSistemaV2Tests.cs`
- Create: `tests/StockApp.Presentation.UiTests/GuardianImportacionV1Tests.cs`
- Create: `tests/StockApp.Presentation.UiTests/ContrasteV2Tests.cs` (usa `ContrasteHelpers.cs`)
- Delete: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`
- Modify: `src/StockApp.Presentation/Views/AccesoLimitadoView.axaml:11` (lo pide el rojo del Step 3)
- Modify: los `cref`/`nameof` a `GuardianDePatronTests` en otros tests (`grep -rn GuardianDePatronTests tests`)

**Interfaces:**
- Consumes: `PatronHelpers.Montar/HeaderDe/MargenExteriorDe/OpacidadesLiteralesDe` (sin cambios), `ArbolVisual.EsVisibleEnArbol`, `Contraste.Ratio` (`ContrasteHelpers.cs`).
- Produces: `GuardianSistemaV2Tests.VistasV2` (`HashSet<Type>`), `GuardianSistemaV2Tests.VistasFueraDelGuardian` (`HashSet<Type>`), `GuardianImportacionV1Tests.VistasV1Vivas` (`HashSet<Type>`: `ImportacionView`, `NuevaImportacionView`, `HistorialImportacionesView`). La Task 3 usa `VistasV1Vivas` para la captura de referencia.

- [ ] **Step 1: Mover los invariantes v1 a `GuardianImportacionV1Tests`**

Con el texto de hoy, restringidos a `VistasV1Vivas`: `Vista_TieneHeaderVistaConElTituloEsperado` (`ImportacionView`, "Importar planillas", "FINANZAS"), `Vista_TieneMargenExteriorEstandar` (`ImportacionView`, `Thickness(24)`), `VistaEmbebida_NoDuplicaElMargenDeVista` (`NuevaImportacionView`, `HistorialImportacionesView`), `Vista_NoTieneOpacidadesLiterales` (las tres), `Vista_NoTieneUnSegundoBotonPrimario` (las tres, con la exención `ImportacionView`/`NuevaImportacionView` y su `<summary>` que remite a `NuevaImportacionJerarquiaBotonesTests`). Summary de la clase: "Invariantes P0-P9 del tema viejo para lo único que sigue en v1, la importación (Fase 8 fuera del rediseño, decisión de Martin 2026-10-17)". `AccesoLimitadoView` NO va (es v2).

- [ ] **Step 2: Escribir `GuardianSistemaV2Tests`**

Movidos sin cambiar su lógica: `VistaV2_NoTieneOpacidadesLiterales` (sobre `VistasV2` + `ShellMainView`), `ShellV2ConViewModel_NoTieneOpacidadesLiterales`, `VistaV2_BotonEstiradoEstiraSuContenido`, `EstiloBotonV2_CentraElTextoConTextAlignment`, `BotonV2_ToolTipYFlyoutNoHeredanElCentrado`, `OpacidadesLiteralesDe_DevuelveSoloElLiteralYNoElQueVieneDeUnStyle`, `DialogoV2_NoTieneOpacidadLiteralEnSuXaml` (con `DialogosV2`), `DetectorDeOpacidadLiteral_VeUnAtributoYNoUnSetter`, `NingunaVista_TieneOverlaysDeModal`, `DetectorDeOverlays_VeElFondoYElFlagYNoUnBindingComun`.

`VistasV2` = la `VistasMigradasAV2` de hoy (L126-L179) + `AccesoLimitadoView`. `VistasFueraDelGuardian` = los 7 diálogos, `MainWindow`, `ShellMainView` (con sus comentarios de hoy).

Nuevos o cambiados:

```csharp
[AvaloniaFact] public void Guardian_CubreTodasLasVistasDelEnsamblado()
// mismo predicado de tipos que hoy (L197-L202); cubiertas = VistasV2 ∪ GuardianImportacionV1Tests.VistasV1Vivas ∪ VistasFueraDelGuardian.

[Fact] public void Listas_SonDisjuntas()
// ningún tipo en dos de las tres listas; mensaje con los repetidos.

public static TheoryData<string> AxamlV2 // rutas desde src: VistasV2 (RutaAxaml) + Views/ShellMainView.axaml + DialogosV2
[Theory, MemberData(nameof(AxamlV2))] public void VistaV2_NoUsaPiezasDelTemaViejo(string rutaDesdeSrc)
// Assert.Empty(PiezasViejasEn(texto)), donde PiezasViejasEn devuelve cada coincidencia de:
//  (a) el regex de hoy (L466-L467): SignoNegativoBrushConverter|ActivoOpacidadConverter|<c:BadgeEstado\b|<c:HeaderVista\b|<c:EstadoVacio\b
//  (b) (Dynamic|Static)Resource\s+(CLAVE)\b con CLAVE ∈ las x:Key de Themes/Tokens.axaml, Themes/Typography.axaml, Themes/DataGrid.axaml
//      y Controls/Componentes.axaml (leídas en el test con LeerDesdeSrc; las que empiezan con "{x:Type" se ignoran)
//  (c) Classes="…" que contenga como palabra primary|secondary|card|seccion|caption|micro|body|titulo-vista ("ghost" NO: es de los dos temas)

[Fact] public void DetectorDePiezasViejas_VeConversorControlTokenYClase()
// Matches: <c:BadgeEstado Texto="x" />, Background="{DynamicResource FondoBrush}", Classes="primary", <Border Classes="card x">
// No matches: Background="{DynamicResource V2FondoBrush}", Classes="primario", Classes="ghost", <v2:Insignia Texto="x" />

[Theory, MemberData(nameof(AxamlV2))] public void VistaV2_TodoBotonTieneVarianteOTema(string rutaDesdeSrc)
// cada <Button\s[^>]*> (multilínea; NO matchea <Button.IsVisible>) tiene Theme="…" o Classes con primario|secundario|ghost|peligro.

[Fact] public void DetectorDeBotonSinVariante_VeElBotonDesnudoYNoElElementoDePropiedad()
// Matches (sin variante): <Button Content="x" />, <Button Classes="primary" Content="x" />, "<Button\n  Content=\"x\" />"
// No matches: <Button.IsVisible>, <Button Classes="secundario" />, <Button Classes="ghost icono" />, <Button Theme="{StaticResource V2TemaEnlace}" />
```

Summary de la clase: el guardián del sistema nuevo (spec §8.2): cobertura de todas las vistas, piezas del tema viejo prohibidas en v2 (convención 11), botón v2 con variante (un `Button` sin clase dentro de `.ui2` no recibe estilo v2), opacidades literales y overlays (convención 25).

- [ ] **Step 3: Correrlos y pegar el rojo**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GuardianSistemaV2Tests|FullyQualifiedName~GuardianImportacionV1Tests"`
Expected: FAIL `VistaV2_NoUsaPiezasDelTemaViejo` en `Views/AccesoLimitadoView.axaml` (`{DynamicResource FondoBrush}`). Cualquier otro rojo: pegarlo; si es una pieza vieja en una vista v2, se corrige igual que el Step 4; si es un falso positivo del detector, se corrige el regex y se agrega el caso al detector.

- [ ] **Step 4: Corregir `AccesoLimitadoView`**

`Background="{DynamicResource V2FondoBrush}"` en la raíz; actualizar el comentario de L19-L20 ("la raíz sigue con el fondo viejo") a que la raíz usa el fondo v2.

- [ ] **Step 5: Escribir `ContrasteV2Tests`**

Colores leídos de los recursos de la app (`Application.Current.TryGetResource("V2…Brush", ThemeVariant.Light, …)` → `SolidColorBrush.Color`). `[AvaloniaTheory]` `TextoSobreFondo_CumpleAA(string texto, string fondo)` con `Contraste.Ratio(...) >= 4.5` para: `V2TextoPrimarioBrush`/`V2SuperficieBrush`, `V2TextoPrimarioBrush`/`V2FondoBrush`, `V2TextoSecundarioBrush`/`V2SuperficieBrush`, `V2TextoSecundarioBrush`/`V2FondoBrush`, `V2TextoEtiquetaBrush`/`V2SuperficieBrush`, `V2TextoEtiquetaBrush`/`V2HeaderGrillaBrush`, `V2TextoTerciarioBrush`/`V2SuperficieBrush`, `V2TextoTerciarioBrush`/`V2HeaderGrillaBrush`, `V2PeligroTextoBrush`/`V2SuperficieBrush`, `V2BordeErrorBrush`/`V2SuperficieBrush`, `V2AcentoBrush`/`V2SuperficieBrush`, `V2TextoSobreAcentoBrush`/`V2AcentoBrush`, `V2AcentoOscuroBrush`/`V2AcentoSuaveBrush`, las 4 insignias (`V2Insignia{Neutro,Advertencia,Peligro,Exito}TextoBrush`/`…FondoBrush`) y las 3 alertas (`V2Alerta{Advertencia,Error,Info}TextoBrush`/`…FondoBrush`).

Excepciones declaradas (Q-4 = A), `[AvaloniaTheory]` `ExcepcionDeclarada_NoEmpeora(string texto, string fondo, double piso)`: `V2TextoTerciarioBrush`/`V2FondoBrush` piso `4.47`, `V2TextoGrupoBrush`/`V2SuperficieBrush` piso `2.52`, cada una con un comentario de dónde se usa y que está en la deuda.

- [ ] **Step 6: Verde y mutaciones**

Run: los dos filtros (guardianes y `ContrasteV2Tests`) y después `UiTests` completo; `VisualTests --filter "FullyQualifiedName~AccesoLimitado"` (la comparación de la pantalla de acceso limitado a los dos tamaños tiene que seguir verde con el fondo v2; si la pantalla se mueve, pegar números y PARAR).
Mutaciones (pegar cada rojo y restaurar):
- quitar `Classes="secundario"` de UN botón de `Views/Finanzas/IngresosView.axaml` → rojo en `VistaV2_TodoBotonTieneVarianteOTema`;
- quitar `typeof(TareaListView),` de `VistasV2` → rojo en `Guardian_CubreTodasLasVistasDelEnsamblado`;
- `ContrasteV2Tests` nace verde y no tiene mutación QUITANDO que lo ponga en rojo sin tocar `tokens.json` (prohibido en la fase): se declara "caracterización" en el ledger, con la tabla de ratios medidos por par (el test los imprime con `ITestOutputHelper`).

- [ ] **Step 7: Commit**

Total de `UiTests` antes/después en el ledger (GuardianDePatronTests se reparte en los dos archivos: la cantidad de casos no baja salvo los de vistas que ya no existen en esas listas).

```bash
git add tests/StockApp.Presentation.UiTests/GuardianSistemaV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianImportacionV1Tests.cs tests/StockApp.Presentation.UiTests/ContrasteV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs src/StockApp.Presentation/Views/AccesoLimitadoView.axaml <tests con cref al guardián viejo>
git diff --cached --name-only
git commit -m "test(ui-v2): guardián del sistema nuevo y de la importación v1 en lugar de los patrones P0-P9, y contraste de los tokens v2"
```

---

### Task 3: Borrar del tema viejo lo que no tiene uso, con la importación v1 custodiada

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Importacion/DatosImportacion.cs` (fábricas con `Mock` de `IImportacionService`, `IServicioSeleccionArchivo`, `IConfirmacionService` y los cuatro servicios de catálogos; datos de las mismas formas que `tests/StockApp.Presentation.UiTests/NuevaImportacionFakes.cs`)
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Importacion/ImportacionV1SinCambiosTests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Importacion/Referencias/*.png` (6 capturas)
- Create: `tests/StockApp.Presentation.UiTests/RecursosReferenciadosTests.cs`
- Delete (si el Step 3 lo confirma): `src/StockApp.Presentation/Controls/BadgeEstado.cs`, `Controls/CampoFormulario.cs`, `Controls/TarjetaMetrica.cs`, `Converters/ActivoOpacidadConverter.cs`, `Converters/SignoNegativoBrushConverter.cs`, `Converters/ColeccionVaciaConverter.cs`, `Converters/IconoChevronConverter.cs` y sus tests (`tests/StockApp.Presentation.Tests/Converters/{ActivoOpacidad,SignoNegativoBrush}ConverterTests.cs`, `tests/StockApp.Presentation.UiTests/CampoFormularioTests.cs`, `SidebarContrasteTests.cs`)
- Modify: `src/StockApp.Presentation/Controls/Componentes.axaml` (quitar los `ControlTheme` de `TarjetaMetrica`, `BadgeEstado`, `CampoFormulario`), `Themes/Controls.axaml`, `Themes/Tokens.axaml`, `Themes/DataGrid.axaml` (según la tabla del Step 3)
- Modify (tests "de los dos sistemas" a solo v2, convención 13): `tests/StockApp.Presentation.UiTests/GrillaStockHelpers.cs` (L19, L113-L115), `CatalogoFormAnchoYCancelarTests.cs` (L140-L143, L203), `SignoNegativoBadgeTests.cs`, `ComponentesBasicosTests.cs` (quedan los de `EstadoVacio`), `TokensDisenioTests.cs` y `ControlesConsumenTokensTests.cs` (solo claves vivas), `PatronHelpers.cs`, y los asserts de ausencia de `BadgeEstado`/`HeaderVista` en `V2/MovimientoHistorialViewV2Tests.cs`, `V2/ProductoListViewV2Tests.cs`, `V2/IngresoPorFacturaViewV2Tests.cs` (los cubre `VistaV2_NoUsaPiezasDelTemaViejo` de la Task 2: se borra el assert y se nombra el guardián en el ledger)

**Interfaces:**
- Consumes: `GuardianImportacionV1Tests.VistasV1Vivas` (Task 2); `RenderizadorVista.RenderizarAPng(Control vista, object? viewModel, string rutaPng, …, TamanoLienzo? tamano = null)` (`VisualTests/Comparacion/RenderizadorVista.cs:20`); `ComparadorImagenes.Comparar(string rutaReferencia, string rutaCandidata, string rutaDiff, int toleranciaCanal = 32)` → `ResultadoComparacion(Ancho, Alto, PixelesDistintos, TeselasDensas)` (`tools/StockApp.UiV2.Herramientas/Imagenes/`); `TamanoLienzo.Estandar` (1280×760) y `TamanoLienzo.Municipio` (1366×768).
- Produces: nada que otra task consuma.

- [ ] **Step 1: Custodia de la importación v1 ANTES de borrar (caracterización)**

`ImportacionV1SinCambiosTests`, `[AvaloniaTheory]` sobre 3 pantallas × 2 tamaños: `ImportacionView` recién abierta (paso de selección), `NuevaImportacionView` en el paso de revisión con gastos y líneas POA cargados (incluye una fila con candado, una con error de servidor y una revertida si el paso la muestra; reproducir el camino de `NuevaImportacionGastosGridTests`) e `HistorialImportacionesView` con tres filas (una revertida). Cada caso renderiza con `RenderizadorVista.RenderizarAPng` a `artifacts/ui-v2/importacion-v1/<pantalla>[_1366x768].png` y compara con `Referencias/<pantalla>[_1366x768].png` usando `ComparadorImagenes.Comparar(…, toleranciaCanal: 0)`; `Assert.Equal(0, resultado.PixelesDistintos)` con el mensaje "la importación v1 cambió de aspecto: ver <diff>". Si falta la referencia y la variable de entorno `STOCKAPP_GENERAR_REFERENCIAS_V1=1` está puesta, la copia y falla con "referencia generada: revisarla y commitearla"; sin la variable, falla.

Generar las 6 referencias con la variable, MIRARLAS (Read de cada PNG: grilla con datos, botones con el verde viejo, header "Importar planillas") y correr de nuevo sin la variable: verde dos veces seguidas (determinismo; si no es determinista con tolerancia 0, pasar a la tolerancia por defecto y `TeselasDensas == 0`, anotado en el ledger).

`RecursosReferenciadosTests.RecursosReferenciados_SeResuelvenEnLaApp` (`[AvaloniaFact]`): junta toda clave de `{DynamicResource X}` y `{StaticResource X}` de los `.axaml` de `src/StockApp.Presentation` (menos `Themes/` y `Controls/*.axaml`, que se resuelven al cargarse) y de cada `FindResource("X")`/`TryGetResource("X"` de sus `.cs`; para cada una, `Application.Current!.TryGetResource(X, ThemeVariant.Light, out _)` tiene que dar `true`. Mensaje con las que faltan y el archivo. Detector propio: `RecursosReferenciados_DetectorVeDynamicStaticYFindResource` con tres textos de ejemplo.

Mutaciones (pegar cada rojo y restaurar): quitar el bloque `Style Selector="Button.secondary"` de `Themes/Controls.axaml` → rojo en `HistorialImportacionesView` de `ImportacionV1SinCambiosTests`; quitar la línea `Espacio3` de `Themes/Tokens.axaml` → rojo en `RecursosReferenciados_SeResuelvenEnLaApp`.

- [ ] **Step 2: Línea base de comparaciones**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m`
Expected: verde. Guardar `artifacts/ui-v2/comparacion/index.html` como `$SCRATCH/comparacion-antes.html` (porcentajes y teselas por pantalla para comparar en el Step 5).

- [ ] **Step 3: Auditoría con evidencia**

Script en `$SCRATCH/auditar-tema-viejo.sh` (fuera del repo), que imprime una tabla `pieza | archivo de definición | usos en src fuera de su definición | usos en tests`:
1. cada `x:Key` de `Themes/Tokens.axaml`, `Themes/Typography.axaml`, `Themes/DataGrid.axaml`, `Controls/Componentes.axaml` (un `Color*` cuenta como usado si lo usa una brush viva: iterar hasta punto fijo);
2. cada `Style Selector` de `Themes/Controls.axaml`, `Themes/Typography.axaml`, `Themes/DataGrid.axaml`, con las clases que exige (`grep` de `Classes="…clase…"`, `Classes.clase`, `Classes.Add("clase")`, `Classes.Set("clase"` en `src`);
3. cada tipo de `Controls/*.cs` (no `V2/`) por su prefijo `c:` en `.axaml` y por tipo en `.cs`;
4. cada `Converters/*.cs` por `conv:Nombre`/`Nombre.Instance` en `.axaml` y uso en código que no sea comentario.

Pegar la tabla completa en el ledger. Decisión por fila: **borrar** (0 usos en `src` fuera de comentarios y de piezas que también se borran), **vivo** (con el consumidor), o **candidato v2** (solo lo alcanzan vistas `.ui2`: se borra y decide el Step 5). Si la tabla difiere del inventario de este plan, manda la tabla.

- [ ] **Step 4: Borrar**

Lo marcado "borrar" y "candidato v2". En los tests: borrar los que custodiaban solo piezas borradas (`CampoFormularioTests` si `V2/CampoFormularioV2Tests` cubre el `ErrorConverter` del control interno: nombrar el test; `SidebarContrasteTests`, reemplazado por `ContrasteV2Tests`; los `[Fact]` de `BadgeEstado` y `TarjetaMetrica` de `ComponentesBasicosTests`; `ActivoOpacidadConverterTests`, `SignoNegativoBrushConverterTests`); pasar a solo v2 los helpers de dos sistemas (convención 13). `EsNegativoConverter` sigue vivo (7 vistas v2): corregir su `<see cref="Controls.BadgeEstado"/>` a `Insignia`. `Insignia.cs:7-8`: quitar "cuando la Fase 9 borre el sistema viejo".

Run: `dotnet build StockApp.sln`
Expected: 0 errores y sin warnings nuevos.

- [ ] **Step 5: Suite y decisión de los candidatos v2**

Run, de a uno: `StockApp.Presentation.Tests`, `StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests` (completo), `StockApp.UiV2.Herramientas.Tests`, y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`.
Expected: verde; `ImportacionV1SinCambiosTests` y `RecursosReferenciados_SeResuelvenEnLaApp` verdes; `index.html` igual a `comparacion-antes.html` en cada fila (diff de porcentajes y teselas pegado). Si una comparación cambia o da rojo: restaurar el candidato v2 que la mueve (de a uno, hasta aislarlo), anotarlo como **vivo: la v2 depende de él (<test>)** y repetir. `GrillaEspacioDerechoVistaRealTests` sigue (mide `Themes/DataGrid.axaml`, vivo por la importación; convención 44).

- [ ] **Step 6: Commit**

```bash
git add <cada archivo creado, borrado o modificado, por ruta>
git diff --cached --name-only
git commit -m "refactor(ui-v2): borra los componentes, estilos, tokens y conversores del tema viejo sin uso, con la importación v1 custodiada por capturas"
```

En el ledger: la tabla final "Se borró" y "Queda vivo (motivo)", y el total de tests antes/después con cada test borrado y su motivo.

---

### Task 4: Auditoría de residuos

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/InicioViewModel.cs` (borrar `MostrarAvisoVencimientos`, `TextoAVencer7Dias`, `TituloVencidas`, `TituloProximasAVencer` y su cálculo), `ViewModels/Tareas/TareaListViewModel.cs` (borrar `Pendientes`, `EnCurso`, `Terminadas`, `Canceladas`, `MostrarCanceladas` y su llenado)
- Modify: `tests/StockApp.Presentation.UiTests/ReflexionVistaViewModelTests.cs` (quitar esas exclusiones, L68-L76 y L181-L185), `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs` y los tests de `TareaListViewModel` que afirman sobre los miembros borrados, `tests/StockApp.Presentation.UiTests/V2/InicioViewV2Tests.cs:271` (el control que usaba `TextoAVencer7Dias`: pasar a afirmar que el texto no está en la vista)
- Create: `tests/StockApp.Presentation.VisualTests/MockupsHuerfanosTests.cs`
- Modify (Q-5 = A): `tests/StockApp.Presentation.UiTests/{ReportesStockOrdenTests,DataGridColumnasAutoAjusteTests,GrillasSinRecorteBarridoTests,DataGridCeldaNumericaOrdenTests}.cs`; Delete: `MarcoReporteTestHelpers.cs`
- Modify (Q-2 = A): los mockups de variante Administrador con sidebar viejo (`docs/ui-v2/mockups/**/*.html`); script en `$SCRATCH`, no en el repo
- Modify: comentarios con símbolos inexistentes (lista del Step 3), `docs/ui-v2/mockups/acceso/reset.html` (`.mt-12`, `.enlace-centrado` sin uso), `src/StockApp.Presentation/ViewModels/GrupoNavegacion.cs:11-14` (menciona Finanzas/Maestros), el comentario de `Themes/V2/ControlesV2.axaml` que dice `.v2-boton` (el real es `.v2-boton--icono`), `App.axaml:21` y `GeneradorTokensTests.cs:71` (Q-3), `ShellMainViewModel.cs:189` y `V2/ShellMainViewV2Tests.cs:154` (Q-1: "sin consumidor en la vista; deuda declarada")

**Interfaces:**
- Consumes: nada de otras tasks salvo que ya no existan las piezas de la Task 3.
- Produces: nada.

- [ ] **Step 1: Guardián de mockups huérfanos (caracterización)**

`MockupsHuerfanosTests.TodoMockupLoUsaUnTest`: enumera `docs/ui-v2/mockups/**/*.html` menos `referencia/`, `_base/`, `_fixtures/`; concatena el texto de los `.cs` de `tests/StockApp.Presentation.VisualTests` (sin `bin/`, `obj/`); cada ruta relativa `modulo/archivo.html` tiene que aparecer. `Assert.True(huerfanos.Count == 0, …)` con la lista; además `Assert.True(mockups.Count > 150)` (el barrido encontró los mockups). Nace verde (0 huérfanos medidos); mutación: quitar la línea del método `Productos_IgualAlMockup` de `ProductosComparacionTests` → rojo con `inventario/productos.html`; restaurar.

- [ ] **Step 2: Miembros de VM sin consumidor**

Run: `codegraph callers InicioViewModel.TextoAVencer7Dias` (y uno por miembro de la lista de Files) para confirmar que solo los usan tests y su propio VM; pegar la salida. Borrarlos. `InicioViewModel.TituloVencidas` NO es el `x:Name="TituloVencidas"` de `CalendarioPagosView.axaml:75` (otro control de otra vista): confirmarlo con `grep` y no tocar la vista. Ajustar los tests listados (los que solo afirmaban un miembro borrado se borran: lo custodiado no existe; nombrarlos en el ledger).

Run: `dotnet build StockApp.sln`; `Presentation.Tests` y `UiTests` completos. Expected: verde.

- [ ] **Step 3: Comentarios que citan símbolos inexistentes**

Evidencia, pegada en el ledger:
1. `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj -p:GenerateDocumentationFile=true -p:DocumentationFile=$SCRATCH/doc.xml -nologo -v q 2>&1 | grep -oE "warning CS(1574|1580|1584)[^[]*" | sort -u`, y lo mismo para los tres proyectos de test de Presentation. Corregir los de `StockApp.Presentation*` (los de otros proyectos, a la deuda).
2. `grep -rnwE "<lista>" src tests --include=*.cs --include=*.axaml`, donde `<lista>` son los tipos y miembros borrados en esta fase y en la Fase 7 (`git log --diff-filter=D --name-only c145c11..HEAD -- src tests` da los archivos; sumar `BadgeEstado`, `TarjetaMetrica`, `ActivoOpacidadConverter`, `SignoNegativoBrushConverter`, `ColeccionVaciaConverter`, `IconoChevronConverter`, `GuardianDePatronTests`, `MarcoReporteTest`, los miembros del Step 2, `MaestrosFinanzasView`, `FuenteFinanciamientoListView`, `CatalogosTareaView`). Hallados hoy: `CargaProtegidaEstadoVacioUiTests.cs:31` y `:89`, el `<summary>` de `GuardianDePatronTests.cs:230` (se fue con la Task 2), `ProductoListViewModel.cs:181` y `UsuariosAdminViewModel.cs:117` (citan `CategoriaListViewModel.PuedeEditar`: verificar que el miembro exista; si no, corregir).
3. `grep -rn "Fase 9" src tests tools --include=*.cs --include=*.axaml`: cada resultado se reescribe a su estado real (importación v1 o deuda declarada); al final, 0 resultados.
4. `grep -rnoE "\b_?[Mm]ostrando[A-Z]\w*" src --include=*.cs --include=*.axaml`: 0 propiedades o bindings (hoy solo prosa). Pegar.

- [ ] **Step 4: Q-5 = A, reportes con datos por servicio**

En los 4 archivos, cada `MarcoReporteTest.MostrarContenido(vista)` + `ItemsSource = …` pasa a montar la vista con su ViewModel real y un `Mock` del servicio del reporte que devuelve las mismas filas (las fábricas `Datos*` de `VisualTests` sirven de modelo; si hace falta compartir datos, copiarlos al test, no referenciar `VisualTests` desde `UiTests`). Lo que cada test afirma no cambia (orden, ancho, recorte, alineación). Borrar `MarcoReporteTestHelpers.cs`. Mutación: en `ReportesStockOrdenTests`, quitar la fila con stock negativo del mock → el test de orden da rojo; restaurar.

- [ ] **Step 5: Q-2 = A, sidebar de los mockups**

Script `$SCRATCH/sidebar-mockups.py` (`python3 -I`): toma el bloque `<aside class="v2-sidebar">…</aside>` de `docs/ui-v2/mockups/shell/sidebar-admin.html`; para cada mockup con `<aside class="v2-sidebar">` que contenga el ítem "Usuarios" y NO contenga "Catálogos", lee qué ítem tiene la clase de activo (la del `ItemSidebar` activo en `sidebar-admin.html`), reemplaza el bloque y marca activo el ítem equivalente ("Reportes" para cualquier reporte, "Catálogos" para cualquier catálogo o formulario de catálogo). Imprime: reemplazados, operador (no tocados) y sin ítem equivalente (no tocados). Pegar la salida. Después: `scripts/ui-v2/comparar.sh` completo (o `VisualTests` completo): mismos porcentajes que en la Task 3 (el sidebar está oculto en las comparaciones). Mirar 3 mockups reemplazados con `capturar-mockup` y Read.

- [ ] **Step 6: Comentarios cosméticos y CSS muerto**

Los de la lista de Files (GrupoNavegacion, ControlesV2, reset.html, App.axaml, GeneradorTokensTests, ShellMainViewModel, ShellMainViewV2Tests). En `reset.html` quitar `.mt-12` y `.enlace-centrado`: la comparación del Reset a los dos tamaños sigue verde.

- [ ] **Step 7: Suite y commit**

Run, de a uno: `Presentation.Tests`, `UiTests`, `VisualTests`, `Herramientas.Tests`; `dotnet test StockApp.sln --blame-hang-timeout 5m` para el total. Expected: verde salvo los falsos conocidos.

```bash
git add <cada archivo, por ruta>
git diff --cached --name-only
git commit -m "refactor(ui-v2): residuos de la convivencia (miembros sin vista, comentarios, sidebar de los mockups y reportes con datos por servicio)"
```

---

### Task 5: GATE 2 (smoke de navegación) y cierre

**Files:**
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (L3 `**Estado:**`, fila "9 Limpieza" de §7, nuevo "**Estado de la Fase 9 …**" después del de la Fase 7 y "**Deuda abierta tras la Fase 9:**" antes del de la Fase 7; tachar con `~~…~~ Cerrada (Fase 9, <hash>)` cada deuda cerrada: `SignoNegativoBrushConverter`/`ActivoOpacidadConverter` (3a), `TextoAVencer7Dias` (Fase 2), colecciones de `TareaListViewModel` (5), `MostrarContenido` (6, según Q-5), los 67 mockups (6, según Q-2), los cosméticos de la Fase 1, y la fila de §8.2 de `GuardianDePatronTests`)
- Modify: `docs/ui-v2/herramientas.md` (título "Convenciones de la convivencia (hasta la Fase 9)" → "Convenciones de la UI v2", con una línea: "La importación sigue en v1 (Fase 8 fuera del rediseño): sus tres vistas usan el tema viejo y las custodian `GuardianImportacionV1Tests` e `ImportacionV1SinCambiosTests`"; convención 13 en pasado; nuevas 52 y 53)

**Interfaces:** ninguna.

- [ ] **Step 1: Smoke orgánico (~45 min)**

Seguir los "Gotchas obligatorios" y la preparación P2/P3 de `docs/superpowers/plans/2026-10-18-ui-v2-gate2-integrado.md` (no se edita ese archivo). Sin datos nuevos: solo navegación, abrir y cancelar. Capturas en `artifacts/ui-v2/gate9/`, cada una MIRADA con Read y con su `md5sum` distinto de la anterior.

Antes de lanzar (Q-6 = A): `cp src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs "$SCRATCH/ShellMainViewModel.cs.bak"` y cambiar SOLO `ImportacionHabilitada = false` por `true` en L63; `dotnet build StockApp.sln`.

Admin a 1280×760, una captura por pantalla: Login, Inicio, Productos y su formulario (Nuevo → Cancelar), Registrar entrada, Registrar salida, Ingreso por factura, Historial de movimientos (+ diálogo Recalcular → Cancelar), Gastos y facturas y su formulario, Pagos de un gasto, Ingresos de caja y su formulario, Libro caja (mes y año completo), Control POA, Calendario de pagos, Tareas y su formulario, Documentos (Activos e Historial) y su formulario, Reportes (hub y los 6), Catálogos (los 3 grupos y un formulario), Usuarios, Mantenimiento, **Importar planillas** (pestaña Nueva importación y pestaña Historial). Repetir a 1366×768 (`xdotool windowsize`). Por captura, OK si: ningún botón con el gris de Fluent, ningún fondo transparente o negro, grillas con separadores, textos sin cortes nuevos, la importación con su aspecto v1 (verde viejo, header "FINANZAS / Importar planillas").

Al terminar: cerrar la app; `cp "$SCRATCH/ShellMainViewModel.cs.bak" src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`; `git diff --quiet -- src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs && echo restaurado` (pegado); `git status --short` sin cambios en `src/`.

Reporte en el ledger con OK / FALLA / NO VERIFICABLE por pantalla y tamaño. Una FALLA se arregla con TDD en un commit propio antes del Step 2 (o va a la deuda con el OK de Martin).

- [ ] **Step 2: GATE 2 — parada para Martin**

Mostrar el reporte del Step 1 (tabla y las capturas con FALLA o dudosas). No se sigue sin un "aprobado" explícito.

- [ ] **Step 3: Cierre documental**

Spec: `**Estado:**` agrega "Fase 9 (Limpieza) APROBADA por el usuario el <fecha>"; fila "9 Limpieza" con "(aprobada el <fecha>; la importación queda en v1)"; "Estado de la Fase 9" con: qué se borró y qué quedó vivo (tablas finales de la Task 3, resumidas), el guardián nuevo y el de importación, `ContrasteV2Tests`, el debounce en cero, la suite total y el GATE 2 (fecha, tamaños, Q-6). "Deuda abierta tras la Fase 9": lo que quede vivo por la importación (componentes, estilos y tokens viejos; prefijo `V2`, Q-3), Q-1 si fue A, las dos excepciones de contraste (Q-4), mockups de operador con sidebar viejo (Q-2), `cref` rotos de otros proyectos y la sección "Fuera de alcance" de este plan.

`herramientas.md`:
- 52. **Datos fijos sin espera de debounce:** toda fábrica de datos fijos (VisualTests) de un VM con `RecargaConDebounce` fija `EsperaDebounce = TimeSpan.Zero`; un test de UiTests que cambia un filtro con debounce y afirma sobre lo cargado hace lo mismo, salvo que custodie la espera. Lo exige `DatosSinDebounceTests.TodoViewModelConDebounce_TieneSuFabricaVerificada`. Origen: `2de3ad5`.
- 53. **Guardián del sistema nuevo:** toda vista nueva entra en `GuardianSistemaV2Tests.VistasV2` (o, si es v1, en `GuardianImportacionV1Tests.VistasV1Vivas`); en una vista v2 todo `Button` lleva `primario`, `secundario`, `ghost`, `peligro` o un `Theme`, y no hay tokens, clases, controles ni conversores del tema viejo. El tema viejo vive solo para la importación: borrar algo de él exige `ImportacionV1SinCambiosTests` y `RecursosReferenciados_SeResuelvenEnLaApp` en verde.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md docs/ui-v2/herramientas.md
git diff --cached --name-only
git commit -m "docs(ui-v2): cierre de la Fase 9 (limpieza), con la importación en v1 y las convenciones 52 y 53"
```

---

## Fuera de alcance (deuda que NO entra en la Fase 9)

- Borrar los adjuntos de un pago al anularlo (decisión de Martin 2026-10-17, sin fase asignada; hoy Q-2 = B de la 4e los muestra en solo lectura).
- Q-9 de la Fase 5 (la carga fallida de los combos de clasificación sin alerta con reintento) y el detalle "Prioridad" de Tareas con la elección sin guardar.
- Formularios de catálogo con errores por campo (convención 31) y la card de alta de Usuarios como botón (Fase 7).
- Warning CS0114 en `ShellMainViewModel` y `ShellViewModel` (`new` vs `override`, 4c), override de `NotificarPermisosActualizados` copiado en los hosts del panel y el doble Aviso tras un 403 de adjuntos (4a).
- Helpers `x is not null && EsVisibleEnArbol(x)` de la 4c, Control POA (SALDO desalineado, Reintentar con debounce pendiente), combo de Proveedor que corta nombres, Pagos que refresca permisos antes de navegar (4d), "Totales por rubro" vacía en un mes solo con ingresos (4c).
- Historial de movimientos sin límite (U2 de la 3c-2), selector de producto sin búsqueda, D15 del tilde de precio (3c-3), los ~20 VMs con `catch (UnauthorizedAccessException)` propio sin `ConsultaVigente`.
- Re-revisión de los `UmbralAprobado` si cambia Skia o Chromium; toolkit GUI (`capturar.sh` no fail-closed, `escribir.sh` sin teclas crudas).
- `EsAdmin` de Importación sin guardián propio (se resuelve al reactivar la importación: Fase 8).
- Minors diferidos de los ledgers de cada fase que no citan la Fase 9 (CSS muerto en mockups de estados, `#7A7A7A` literal, helpers de test duplicados, etc.): se listan tal cual siguen en la spec.
- Datos de prueba de las fases: su limpieza la decide Martin (el GATE 2 integrado tiene su paso D2).

## Estimación

| Task | Tiempo |
|---|---|
| 1 Debounce en datos fijos | 1 h |
| 2 Guardián v2, importación v1 y contraste | 2,5 h |
| 3 Borrado con custodia de la v1 | 3 h (dos corridas completas de VisualTests) |
| 4 Residuos | 2,5 h (+1 h con Q-2 = A y Q-5 = A) |
| 5 GATE 2 smoke y cierre | 1,5 h + la espera de Martin |
| Reviews por task y final | 1,5 h |
| **Total** | **~12-13 h**: tasks 1-3 el 2026-10-18 (tras el GATE 2 integrado), 4-5 el 2026-10-19, margen el 2026-10-20 |
