# Fase 7: Administración (UI v2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar a la UI v2 el bloque ADMINISTRACIÓN: la pantalla nueva **Catálogos** (master-detail con los catálogos de §4.3 en UN listado genérico parametrizado), los formularios de alta y edición de esos catálogos, **Usuarios** (con el panel de permisos) y **Mantenimiento**, comparados contra mockups aprobados a 1280×760 y 1366×768, en un worktree propio que corre en paralelo con las Fases 5 y 6.

**Architecture:** Cinco bloques. (A) ViewModels sin tocar vistas: una base `CatalogoListadoViewModel<T>` que da a los listados de catálogo carga protegida, sin conexión en línea, consulta vigente, búsqueda en el cliente y filas uniformes (`FilaCatalogo`) para UNA grilla genérica; los listados existentes pasan a heredar de ella sin tocar sus tests; `CatalogosViewModel` arma los grupos con sus gates y la selección, y los formularios vuelven a Catálogos con su catálogo preseleccionado. (B) Datos fijos, medición y TODOS los mockups de la fase con UN GATE 1. (C) Vistas v2: `CatalogosView` + `CatalogoListadoView` (genérica) y el sidebar con un solo ítem "Catálogos"; los formularios de catálogo; Usuarios; Mantenimiento. (D) Guardianes, mutaciones, suite, UN GATE 2 orgánico hecho por un agente y review final. (E) Integración en `feat/ui-v2` por rebase y `--ff-only`.

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1 (`DataGridCollectionView`), CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`), Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4.

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`: §4.1 (sidebar, ADMINISTRACIÓN), §4.3 (Catálogos master-detail), §4.4, §5.1, §5.2, §6 (mockups, comparación, umbral §6.5), §7 (fila "7 Administración" y la deuda abierta tras cada fase), §8 (tests: "Los 4 asserts `TranslatePoint` de `MantenimientoViewTests`" se reemplazan; "Gates de los grupos de Catálogos" son nuevos), §9 y §10. Convenciones 1 a 36: `docs/ui-v2/herramientas.md`. Molde de formato: `docs/superpowers/plans/2026-10-15-ui-v2-fase-4d-gastos.md`. La spec NO tiene un bloque "Diseño de la Fase 7": lo que no fija va como D# (se confirma en el GATE 1) o como Q-* (se pregunta ANTES de la Task 1, ver ledger).

## Global Constraints

- **Worktree propio:** todo corre en `/home/capua25/workspace/stockapp-worktrees/ui-v2-fase7`, rama `feat/ui-v2-fase-7`, que sale de `feat/ui-v2` DESPUÉS del merge de la 4e (Task 0). Todos los comandos: `cd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase7 && <comando>`. Nunca se trabaja en `/home/capua25/workspace/stockapp` (main) ni en el worktree `ui-v2` salvo en la Task 12 (integración).
- **Corre en paralelo con las Fases 5 y 6.** Cada archivo compartido con ellas se toca solo en las líneas de esta fase (ver "Riesgos de conflicto"). Ningún reformateo, reordenamiento ni "limpieza" de líneas ajenas.
- **PROHIBIDO tocar el tema compartido:** `src/StockApp.Presentation/Themes/V2/` (`ControlesV2.axaml`, `DataGridV2.axaml`, `Tokens.axaml`), `src/StockApp.Presentation/Controls/V2/` (componentes y `ComponentesV2.axaml`), `src/StockApp.Presentation/App.axaml`, `docs/ui-v2/tokens.json`, `docs/ui-v2/mockups/_base/` (`base.css`, `tokens.css`), los estilos globales y `src/StockApp.Presentation/Behaviors/`. Las vistas usan SOLO lo que el tema ya ofrece (y estilos locales de la propia vista si hiciera falta, dentro de su `UserControl.Styles`). Si una task necesita algo de ahí: PARAR, anotarlo en el ledger y en la sección "Necesidades de tema compartido (para el integrador)"; no se resuelve en la fase.
- CodeGraph: el worktree de la fase tiene su PROPIO índice `.codegraph/` (lo crea la Task 0; nunca se copia ni se reusa el de `ui-v2` ni el de main). Para código C# usar primero la CLI `codegraph` dentro del worktree (`codegraph query|explore|node|callers|impact`); NO el MCP `codegraph_explore` (responde con el índice del checkout principal). `grep`/Read como fallback; Read directo para `.axaml`, markdown y HTML/CSS (CodeGraph no los indexa).
- Tema claro fijo; tokens de §2.1 sin valores nuevos: fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, separador `#F0EFED`, peligro texto `#B91C1C`, texto primario `#1C1917`, secundario `#57534E`, terciario `#78716C`; fila de grilla 40, header 36, input y botón 36; espaciado múltiplo de 4; sidebar 240 (la vista mide 1040 a 1280 y 1126 a 1366); card de formulario 760 (`V2AnchoFormularioMax`).
- Formato es-UY: importes con `MonedaConverter` / `FormatoEsUy`; fechas "dd/MM/yyyy"; nunca `CultureInfo.GetCultureInfo("es-UY")`.
- **Sin cambios de dominio, DTOs, servicios de Application, API ni endpoints.** VMs que cambian, y solo por esto: los 10 listados de catálogo (heredan de la base, Task 1), los 10 formularios de catálogo (solo el destino de Guardar y Cancelar, Task 2), `ShellMainViewModel` (sidebar, Task 6), `UsuariosAdminViewModel` (sin conexión en línea, Task 8). Nuevos: `CatalogoListadoViewModel`, `CatalogosViewModel` y sus tipos. Se BORRAN `CatalogosTareaViewModel` y `MaestrosFinanzasViewModel` (Task 6, Q-BORRAR). `MantenimientoViewModel`, `PanelPermisosViewModel`, `ViewLocator` e `INavigationService` no cambian.
- Los tests de VM de los 10 listados (`CategoriaViewModelTests`, `ProveedorViewModelTests`, `UnidadMedidaViewModelTests`, `ZonaViewModelTests`, `DimensionTematicaViewModelTests`, `OrganismoResponsableViewModelTests`, `OrigenFinanciamientoViewModelTests`, `FuenteFinanciamientoViewModelTests`, `RubroGastoViewModelTests`, `LineaPoaViewModelTests`) son los guardianes de que la base no cambió el comportamiento: en la Task 1 NO se tocan. La Task 2 adapta SOLO los tests de navegación de los formularios (`GuardarCommand_Exitoso_NavegaAListado` y equivalentes), con el destino nuevo. Si otro test de esos archivos necesita cambiar, PARAR y reportar.
- `x:DataType` / compiled bindings en todas las vistas y en cada `DataTemplate`; constantes con `{x:Static}`. Sin comentarios con `--` en `.axaml` ni `.csproj` (AVLN1001).
- Un solo botón primario por vista (ver D3, D11, D15, D18).
- Prohibidos en las vistas v2: `Opacity`, `ActivoOpacidadConverter`, `SignoNegativoBrushConverter`, `BadgeEstado`, `HeaderVista`, `c:` del tema viejo, `Classes="primary|secondary|danger|caption|seccion|card|micro"`, `MargenVista`, `DangerBrush`, `TabControl`.
- Gates de UI con la View real en `Presentation.UiTests` (un test de VM no custodia un `IsVisible` del XAML). Permisos de Catálogos con un Operador de permisos MIXTOS (`new SesionFake(RolUsuario.Operador, Permisos.GestionarTablasMaestras)` y la variante con `Permisos.GestionarMaestrosFinanzas`), NUNCA con Admin (cortocircuita). Usuarios y Mantenimiento son estructuralmente Admin-only: su custodia es que un Operador con TODOS los permisos no los ve (`ShellMainViewGatesTests`, se conserva). Revocación en caliente por el camino real (`ManejadorAccesoRevocado.ManejarAsync` → `ShellMainViewModel.NotificarPermisosActualizados`) con la View real dentro de `ShellMainView`.
- `ArbolVisual.EsVisibleEnArbol` falla fuerte con null: buscar el control por `x:Name` y dejar que un nombre que falta dé rojo.
- TDD en cada task: test primero, correrlo y PEGAR el rojo real en el ledger, código mínimo, verde. Un test que nace verde (caracterización) se declara así y su custodia es una mutación con rojo pegado.
- Mutaciones QUITANDO, nunca agregando: `export SCRATCH=~/.cache/stockapp-ui-v2-fase-7 && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas o acortadas; correr y PEGAR el rojo; restaurar con `cp "$SCRATCH/<nombre>.bak" <archivo>`; `git diff --no-index` vacío; verde. NUNCA `git checkout --` ni `git restore`.
- Stubs fieles al contrato real: los servicios de catálogo devuelven `Task<IReadOnlyList<T>>` (`ICategoriaService.ListarTodasAsync`, `IProveedorService.ListarTodosAsync`, `IUnidadMedidaService.ListarTodasAsync`, `IZonaService.ListarTodasAsync`, `IDimensionTematicaService.ListarTodasAsync`, `IOrganismoResponsableService.ListarTodasAsync`, `IOrigenFinanciamientoService.ListarTodasAsync`, `IFuenteFinanciamientoService.ListarTodasAsync`, `IRubroGastoService.ListarTodosAsync`, `ILineaPoaService.ListarTodasAsync`) y `BajaLogicaAsync(int)`; los fakes lanzan lo que lanza `ApiErrores`: `UnauthorizedAccessException` (401/403), `ServidorNoDisponibleException`, `ReglaDeNegocioException` (409), `EntidadNoEncontradaException` (404). Fakes nuevos de la fase en `tests/StockApp.Presentation.UiTests/V2/AdministracionFakes.cs` (NO se edita `TareaFakes.cs`, que la Fase 5 puede tocar).
- Mockups (§6.1): `docs/ui-v2/mockups/administracion/*.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a los `Datos*` de la Task 3, celdas numéricas con `v2-num` y el valor en un `<span>`; CSS propio de una pantalla en un `<style>` del propio HTML (nunca en `base.css`). Se aprueban TODOS juntos en el GATE 1 (Task 4) ANTES de cualquier XAML de las vistas.
- Umbral (§6.5): `CompararConMockup` con los calibrados (`TeselasDensasPorDefecto = 11`, `PorcentajePorDefecto = 1.68`) a AMBOS tamaños; uno mayor solo con `UmbralAprobado(…, AprobadoPor: "Martin", …)` tras revisar cada tesela densa con el usuario (16 o más exige `MutacionPalidaEnRojo`); nunca para tapar un defecto. Los `UmbralAprobado` que hagan falta se juntan y se presentan en el GATE 2 (no hay otra parada).
- Convención 6: comparación DENTRO de `ShellMainView` (`DatosShell.Crear(usuario, permisos, seccion, vm)`), sidebar oculto en los dos renders (`ocultarEnMockup: [".v2-sidebar"]`, `prepararApp` que oculta `Border#Sidebar`).
- Build obligatorio tras cada cambio de `.axaml` o de un `.cs` de `src/`: `dotnet build StockApp.sln`. Tests de a UN `.csproj` por comando con `--blame-hang-timeout 5m`; nunca dos `dotnet test` a la vez. Toda task que toque UI o VM corre las suites COMPLETAS de `Presentation.Tests` y `Presentation.UiTests` (Ruling R7 de la 4d), no filtros.
- Rojos falsos conocidos: fixtures `.ods` ausentes en `Infrastructure.Tests` y `Api.Tests` del worktree (confirmar por nombre); `ApiStartupMigrationTests.Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests` intermitente; `DataGridColumnasAutoAjusteTests.F_GrillaGrande_ElBehaviorNoAgregaCostoPerceptible` flaky de tiempo. Cualquier otro rojo es real. Stale de `.axaml`: `dotnet build-server shutdown`, y si sigue, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- Sin referencias al plan en el código (nada de "Task 6", "D9", "Q-BORRAR" en `.cs`/`.axaml`/`.css`/`.html`).
- Las referencias de línea (`L123`) son las de `feat/ui-v2` en `7fd41cf`; tras el merge de la 4e pueden correrse: ubicar por el texto citado.
- Verificación GUI: ventana "Gestión Municipal" (`pgrep -x GestionMunicipa`), toolkit `scripts/gui-verificacion/`; `capturar.sh` NO es fail-closed: MIRAR cada captura y comparar `md5sum`; popups con `scripts/gui-verificacion/capturar-x11.py`; matar procesos por PID, nunca `pkill -f`. La PC del usuario se usa por turnos con las otras fases: el GATE 2 se coordina con el orquestador.
- Commits: conventional commits en español, uno por task, SIN `Co-Authored-By` ni línea de atribución; `git add` por ruta (nunca `-A`, `.codegraph/` queda fuera); sin push, amend, `git reflog expire`, `git gc` ni `git prune`. Merge y rebase SOLO en la Task 12.
- GATE 1 (Task 4) y GATE 2 (Task 11) son las únicas paradas para el usuario: no se sigue sin un "aprobado" explícito. Las Q-* del ledger se contestan ANTES de la Task 1; el plan está escrito con la recomendación de cada una.

## Review Focus

1. **La búsqueda oculta la fila seleccionada y "Dar de baja" actúa sobre una fila que no se ve.** Caso: en Proveedores el usuario selecciona "Barraca Carmelo" y escribe "ferre" en el buscador; la fila desaparece pero sigue seleccionada y Dar de baja la da de baja. Esperado: la selección se limpia y Editar y Dar de baja quedan deshabilitados (convención 30). Test: `Busqueda_QueOcultaLaFilaSeleccionada_LimpiaLaSeleccionYDeshabilitaAcciones` (Task 1).
2. **Se pierde el permiso del grupo del catálogo abierto.** Caso: un operador con `catalogo.maestras` y `finanzas.maestros` está en "Líneas POA"; le quitan `finanzas.maestros`; el grupo FINANZAS desaparece del master pero el detalle sigue mostrando las líneas POA con Nueva, Editar y Dar de baja. Esperado: se selecciona el primer catálogo visible (Categorías) y, si no queda ninguno, la pantalla muestra "Sin permiso" en lugar del master-detail. Tests: `RevocarElGrupoDelCatalogoAbierto_SeleccionaElPrimerVisible` y `RevocarTodosLosGrupos_MarcaSinPermiso` (Task 2) y `RevocacionDeMaestrosFinanzas_DentroDelShell_ElGrupoDesapareceYElDetalleCambia` (Task 5).
3. **Volver de un formulario abre otro catálogo.** Caso: el usuario edita una unidad de medida, guarda, y Catálogos abre en "Categorías"; tiene que volver a buscar la unidad. Esperado: Guardar y Cancelar (y el breadcrumb) vuelven a Catálogos con el mismo catálogo seleccionado y su listado recargado. Tests: `GuardarYCancelar_VuelvenACatalogosConSuClave` (Task 2, teoría sobre los 10 formularios) y `Breadcrumb_VuelveACatalogosConElCatalogoDelFormulario` (Task 6).
4. **Una baja recarga y la búsqueda deja de aplicarse.** Caso: con "ferre" en el buscador el usuario da de baja un proveedor; la recarga muestra TODOS los proveedores con "ferre" todavía escrito, y el pie dice "1 proveedor". Esperado: tras la recarga el filtro sigue aplicado, el pie cuenta las visibles y la fila dada de baja aparece con la insignia "Inactivo" si coincide. Test: `Baja_RecargaYConservaLaBusqueda` (Task 1).
5. **"Guardar permisos" inalcanzable a 1366×768 con el usuario seleccionado en una lista larga de permisos (bug de producción `c145c11`).** Esperado: el botón se alcanza SIEMPRE, sin depender de scrollear la card (pie fijo, Q-USU-PIE), a 1366×700 útiles, para un Operador, para un Admin ("Acceso total") y para un usuario dado de baja. Test: `GuardarPermisos_A1366x768_SeAlcanzaSinScrollYNoTapaElUltimoPermiso` (Task 8, reemplaza al heredado `GuardarPermisos_A1366x768_QuedaAlcanzableScrolleandoElPanel`).

---

## Preguntas previas (Q-*, se contestan ANTES de la Task 1)

Están en el ledger, cada una con su recomendación. El plan está escrito con la recomendación (marcada "R"); si el usuario elige otra cosa, el orquestador ajusta la task indicada antes de despacharla y lo anota en el ledger.

| # | Pregunta | R (la que implementa el plan) | Tasks |
|---|---|---|---|
| Q-DIEZ | §7 dice "11 catálogos" pero la tabla de §4.3 lista 10 y el código tiene 10 listados de catálogo (7 en `ViewModels/Catalogo` sin Productos, 3 en `ViewModels/Finanzas`). | 10, los de §4.3. | 1, 2, 6 |
| Q-BASE | Cómo se hace el listado genérico. | Base `CatalogoListadoViewModel<T>`; los 10 VMs heredan y quedan como configuración (servicio, textos, columnas, navegación). Sus tests no se tocan. | 1 |
| Q-FORM | Los formularios: pantalla completa v2 (convención 12) o edición dentro del detalle. | Pantalla completa v2, breadcrumb "{Catálogo} / {Título}". | 2, 7 |
| Q-VOLVER | A dónde vuelven Guardar, Cancelar y el breadcrumb del formulario. | A Catálogos con su catálogo preseleccionado (`Navegar<CatalogosViewModel>(vm => vm.Seleccionar(clave))`). | 2 |
| Q-BORRAR | Qué se hace con las 10 vistas de listado, las 2 pantallas con pestañas (`CatalogosTareaView`, `MaestrosFinanzasView`), sus 2 VMs y los 5 comandos del sidebar que quedan sin uso. | Se borran en la Task 6, y cada test que los usaba se REEMPLAZA por uno equivalente sobre la vista genérica (convención 13), con la lista de reemplazos en el ledger. | 6 |
| Q-ESTADO | Columna ESTADO del catálogo: solo "Inactivo/Inactiva" (convención 11) o también "Activa" en éxito (mockup de referencia `patrones-1.html`). | Solo la inactiva, neutra, con el género del catálogo (convención 11). | 1, 6 |
| Q-REVOCA | Qué pasa si se pierde el permiso del grupo del catálogo abierto. | Se selecciona el primer catálogo visible; sin ninguno, "Sin permiso" en lugar del master-detail. | 2, 6 |
| Q-FORM-MOCKUPS | Un mockup por cada uno de los 10 formularios o mockups representativos. | 5 representativos (Categoría nueva, Categoría con error, Proveedor, Rubro, Línea POA) + un UiTest parametrizado que exige la MISMA estructura en los 10. | 4, 7 |
| Q-USU-ALTA | Alta de usuario: en línea (card "Nuevo usuario" en la columna izquierda, como hoy) o modal. | En línea; VM intacto. | 8 |
| Q-USU-PIE | "Guardar permisos": dentro del scroll (como el fix `c145c11`) o en un pie fijo de la card de detalle (convención 27). | Pie fijo; el test heredado se reemplaza por uno más estricto (Review Focus 5). | 8 |
| Q-USU-GRILLA | Lista de usuarios: `DataGrid` v2 o lista de tarjetas. | `DataGrid` v2 (USUARIO, NOMBRE, ROL, ESTADO). | 8 |
| Q-MANT-GRILLA | Backups de Mantenimiento: `DataGrid` v2 con columna de acción (precedente: "Registrar pago" del Calendario, convención 28) o filas de tarjeta. | `DataGrid` v2. | 9 |
| Q-SIDEBAR | Las Fases 6 y 7 cambian el sidebar (ANÁLISIS y ADMINISTRACIÓN) y los mismos mockups `shell/sidebar-*.html`, y el pulgar de scroll del Administrador depende del alto TOTAL. | Cada fase edita solo su bloque del mockup y su grupo de `ShellMainViewModel`; la fase que integre SEGUNDA re-mide el pulgar, regenera los PNG del shell y corre `ShellComparacionTests` (Task 12). | 6, 12 |
| Q-APPCS | Registrar `CatalogosViewModel` en el DI exige tocar `App.axaml.cs` (no `App.axaml`). | Permitido: se agrega una línea junto a L378 (Task 2) y se quitan las de `CatalogosTareaViewModel` y `MaestrosFinanzasViewModel` (L378 y L381, Task 6). | 2, 6 |
| Q-CONV | Las tres fases agregan convenciones a `herramientas.md` (todas empezarían en la 37). | Esta fase las escribe como "7-A", "7-B"…; el integrador las renumera en la Task 12. | 11, 12 |

## Decisiones que se confirman al aprobar los mockups (Task 4, GATE 1)

Lo que la spec no fija lo propone el mockup y lo decide el usuario en el GATE 1. El plan está escrito con estos valores; si el usuario cambia uno, se aplica en los mockups, en los tests que lo fijan (con su rojo y su verde) y en las Tasks 5 a 9.

**Catálogos**

- **D1. Encabezado:** `EncabezadoPagina` sin breadcrumb, `Titulo="Catálogos"`, `Subtitulo="Tablas maestras del sistema."` (el del mockup de referencia, con punto como los demás subtítulos v2). Sin acciones en el encabezado de la página: las acciones van en el detalle.
- **D2. Master:** columna izquierda de 240 (`V2AnchoSidebar`) dentro de una `v2:TarjetaContenido`; por grupo, un `TextBlock Classes="header-seccion-sidebar"` (INVENTARIO, FINANZAS, TAREAS) y un `v2:ItemSidebar` por catálogo con `EstaActivo` en el seleccionado (la pastilla del sidebar; sin estilos nuevos). Íconos (los que tenían los ítems viejos del sidebar donde existían): Categorías `mdi-shape`, Proveedores `mdi-truck`, Unidades de medida `mdi-ruler`, Fuentes de financiamiento `mdi-cash-multiple`, Rubros de gasto `mdi-format-list-numbered`, Líneas POA `mdi-chart-donut`, Zonas `mdi-map-marker`, Dimensiones `mdi-tag-multiple`, Organismos responsables `mdi-domain`, Orígenes de financiamiento `mdi-bank`. Un grupo sin gate no se muestra (ni su header). Orden de grupos y de catálogos: el de §4.3.
- **D3. Detalle:** card a la derecha (`*`) con encabezado propio: título del catálogo en `titulo-seccion` y, a la derecha, "Editar", "Dar de baja" (`secundario`) y "Nueva {x}" (`primario`, el ÚNICO primario de la pantalla; texto de la tabla D6). Debajo `BarraFiltros` con el buscador ("Buscar…", sin filtros extra), la grilla y el pie de conteo.
- **D4. Grilla genérica:** primera columna NOMBRE a lo ancho (`*`, `MinWidth` 200, elipsis + `TooltipSiSeCorta`), hasta cuatro columnas medidas (`ColumnaAuto`, convención 19) según el catálogo (tabla D6) y ESTADO de ancho fijo (insignia "Inactivo"/"Inactiva" medida + 16, conjunto cerrado, convención 14). Orden por click en todas; CÓDIGO del rubro ordena como texto (se anota en la deuda si el usuario lo objeta). Fila inactiva en terciario por token (`Classes.inactivo`).
- **D5. Estados del detalle** (dentro de la card, buscador a mano): "Cargando {título en minúscula}…" (`EstadoCargando`); sin servidor, `EstadoVacio` de error con los textos de la convención 18 y "Reintentar"; tras un 403, `EstadoVacio` "Sin permiso" con el `MensajeSinPermiso` del catálogo EN LUGAR de la card de detalle (el master queda); vacío sin datos "Todavía no hay {título en minúscula}" / "Tocá {TextoNuevo} para cargar el primero." con el botón `secundario`; sin resultados "Nada coincide con la búsqueda" / "Probá con otro texto.".
- **D6. Textos y columnas por catálogo** (constantes de cada VM, Task 1):

| Clave | Título | Botón nuevo | Inactiva | Columnas además de NOMBRE | Sin permiso |
|---|---|---|---|---|---|
| Categorias | Categorías | Nueva categoría | Inactiva | — | No tenés permiso para ver las categorías. |
| Proveedores | Proveedores | Nuevo proveedor | Inactivo | TELÉFONO, EMAIL, DIRECCIÓN, NOTAS | No tenés permiso para ver los proveedores. |
| UnidadesMedida | Unidades de medida | Nueva unidad | Inactiva | ABREVIATURA | No tenés permiso para ver las unidades de medida. |
| FuentesFinanciamiento | Fuentes de financiamiento | Nueva fuente | Inactiva | — | No tenés permiso para ver las fuentes de financiamiento. (la de hoy) |
| RubrosGasto | Rubros de gasto | Nuevo rubro | Inactivo | CÓDIGO | No tenés permiso para ver los rubros de gasto. (la de hoy) |
| LineasPoa | Líneas POA | Nueva línea POA | Inactiva | PROGRAMA, EJERCICIO | No tenés permiso para ver las líneas POA. (la de hoy) |
| Zonas | Zonas | Nueva zona | Inactiva | — | No tenés permiso para ver las zonas. |
| Dimensiones | Dimensiones | Nueva dimensión | Inactiva | — | No tenés permiso para ver las dimensiones. |
| Organismos | Organismos responsables | Nuevo organismo | Inactivo | — | No tenés permiso para ver los organismos responsables. |
| Origenes | Orígenes de financiamiento | Nuevo origen | Inactivo | — | No tenés permiso para ver los orígenes de financiamiento. |

- **D7. Pie:** "N {singular|plural}" (`ConteoConverter` sobre `CantidadVisible`; p. ej. `'categoría|categorías'`), oculto sin filas visibles o sin conexión.
- **D8. Doble click en una fila = Editar**, con el mismo `CanExecute` (activa y seleccionada; precedente Ingresos 4b y Gastos 4d). Sobre el encabezado no edita.
- **D9. Selección inicial:** el primer catálogo visible; al cambiar de catálogo se recarga su listado, se limpia la búsqueda y se suelta la selección.

**Formularios de catálogo**

- **D10. Formulario v2 (convención 12):** breadcrumb "{Título del catálogo} / {Titulo del VM}" con `BreadcrumbComando` = `CancelarCommand`; card de 760 a la izquierda; una sola sección SIN subtítulo (como la convención 16), salvo Línea POA (D12); `Alerta` error con `MensajeError` arriba del pie; pie con Cancelar (`secundario`) y Guardar (`primario`). Asterisco SOLO en lo que exige el `PuedeGuardar` del VM (Nombre en todos; además Abreviatura en Unidad, lo que exija `RubroGastoFormViewModel.PuedeGuardar` y `LineaPoaFormViewModel.PuedeGuardar`: el implementador lo lee y lo anota en el ledger). Placeholders: los de hoy.
- **D11. Grilla de campos:** Proveedor: Nombre (ancho completo); Teléfono · Email; Dirección (completo); Notas (completo). Unidad: Nombre · Abreviatura. Rubro: Código · Nombre. Los de un campo: Nombre a ancho completo.
- **D12. Línea POA:** dos secciones con `subtitulo-formulario`: "Datos de la línea" (Nombre completo; Programa · Ejercicio) y "Asignaciones presupuestales" (encabezados de columna "Fuente de financiamiento" y "Monto" como hoy, `EtiquetadoDeCamposTests`; una fila por asignación: combo de fuente, monto, botón ghost de ícono "Quitar"; debajo "Agregar asignación" `secundario`). Sin permiso: `EstadoVacio` "Sin permiso" en lugar de la card (también Fuente y Rubro, que ya tienen `SinPermiso`).

**Usuarios**

- **D13. Encabezado:** `Titulo="Usuarios"`, `Subtitulo="Cuentas y permisos de acceso al sistema."`, sin acciones.
- **D14. Columna izquierda (360):** card "Nuevo usuario" (`titulo-seccion`; Nombre de usuario · Rol en una fila, Nombre completo (opcional), Contraseña; "Crear usuario" `secundario` a la derecha; `Alerta` error con `MensajeError`) y debajo card con la grilla de usuarios: USUARIO (`*`), ROL (medida, "Administrador"/"Operador"), ESTADO (insignia "Inactivo" neutra, fijo); fila inactiva en terciario.
- **D15. Detalle (`*`):** sin selección, `EstadoVacio` "Elegí un usuario" / "Seleccioná un usuario de la lista para ver sus permisos." (reemplaza al `IsEnabled=false` de hoy). Con selección: título = nombre de usuario (`titulo-seccion`), insignia de rol (neutra) y "Inactivo" si corresponde; acciones "Hacer Admin" / "Hacer Operador" (`secundario`) y "Dar de baja" (`peligro`); fila "Nueva contraseña" (`CampoFormulario` + "Cambiar contraseña" `secundario`); sección "Permisos" con los grupos (nombre del grupo en `subtitulo-formulario`, un `CheckBox` por permiso y su advertencia en `texto-secundario` debajo) DENTRO de un `ScrollViewer`; Admin seleccionado: `Alerta` Info "Acceso total: los administradores tienen todos los permisos." y casillas deshabilitadas; dado de baja: insignia "Solo lectura" y casillas deshabilitadas. **Pie fijo** de la card (fuera del scroll, convención 27): `Alerta` error del panel y "Guardar permisos" `primario` (el ÚNICO primario de la pantalla).

**Mantenimiento**

- **D16. Encabezado:** `Titulo="Mantenimiento"`, `Subtitulo="Backups, diagnóstico y alertas del servidor."`, acción "Hacer backup ahora" `primario` (con `IniciandoBackup`, en su lugar "Iniciando…" deshabilitado).
- **D17. Card "Backups"** (alto `*`): grilla FECHA ("dd/MM/yyyy HH:mm" local, medida), RESULTADO (insignia "Exitosa" éxito / "Fallida" peligro, fijo), DETALLE (`MotivoFallo`: en peligro si es fallo, secundario si es nota; `*`, elipsis + tooltip), TAMAÑO (`TamanoLegibleConverter`, `num`), y una columna de acción con "Descargar" (`secundario`, deshabilitado sin archivo) o "Cancelar" mientras descarga. Vacío "Todavía no hay backups registrados."; error de carga, `Alerta` error "No se pudieron cargar los backups." (los textos de hoy).
- **D18. Fila inferior, dos cards iguales (`GrillaUniformeV2`, convención de tarjetas):** "Diagnóstico" (resumen de logs en `texto-secundario` y "Descargar logs" `secundario` / "Descargando…") y "Alertas" (texto de hoy, `Alerta` error de carga con el texto de hoy, URL del webhook, "Habilitado", "Guardar" y "Probar" `secundario`).
- **D19. Acceso limitado:** `AccesoLimitadoView` ya es v2 y hospeda `MantenimientoView`; con la raíz `ui2` en Mantenimiento la pantalla queda entera en v2 (sin mockup propio: la custodia un UiTest, Task 9, y el GATE 2).

**Sidebar**

- **D20.** ADMINISTRACIÓN queda: "Catálogos" (`mdi-tag-multiple`, sección `Catalogos`, gate `PuedeGestionarTablasMaestras || PuedeGestionarMaestrosFinanzas`), "Usuarios", "Mantenimiento" y el ítem oculto que ya existe, igual. Se van "Categorías", "Proveedores", "Unidades de medida", "Catálogos de tareas" y "Maestros de finanzas".

- **D21. Mockups** (22 nuevos + 2 del shell modificados; 48 PNG): `administracion/catalogos.html` (Admin, Categorías con 6 filas, una inactiva), `catalogos-proveedores.html` (5 columnas, un texto largo cortado), `catalogos-lineas-poa.html`, `catalogos-operador.html` (operador con `catalogo.maestras`: grupos INVENTARIO y TAREAS, detalle Zonas), `catalogos-cargando.html`, `catalogos-sin-conexion.html`, `catalogos-sin-permiso.html`, `catalogos-vacio.html`, `catalogos-sin-resultados.html`; formularios `categoria-nueva.html`, `categoria-error.html`, `proveedor-editar.html`, `rubro-nuevo.html`, `linea-poa-editar.html` (dos asignaciones); `usuarios-sin-seleccion.html`, `usuarios.html` (operador seleccionado), `usuarios-admin.html`, `usuarios-inactivo.html`, `usuarios-alta-error.html`; `mantenimiento.html` (exitosa, fallida, exitosa con nota; logs; alertas), `mantenimiento-vacio.html`, `mantenimiento-error.html` (error de backups y de alertas); y `shell/sidebar-admin.html` y `shell/sidebar-operador.html` con D20. No se mockean tooltips, desplegables ni los diálogos de confirmación (ya aprobados en la Fase 1).

## Convenciones heredadas que aplican

6, 10, 11 (inactivo por token), 12 (formulario v2), 13 (tests heredados se adaptan o se reemplazan, nunca se borran sin reemplazo), 14 y 19 (columnas medidas antes del mockup; `ColumnaAuto`; ESTADO fijo), 18 (`Cargando`, `SinPermiso`, `SinConexion`), 20, 21 (permisos con operador mixto y `ShellMainView`), 23 (`ConsultaVigente`), 27 (pie fijo de Usuarios), 28 (columna de acción en Mantenimiento; headers y visibilidad de columnas desde el code-behind en la grilla genérica), 29 (Catálogos reenvía `NotificarPermisosActualizados` al listado abierto), 30 (búsqueda en el cliente, selección oculta se limpia, dos vacíos). NO aplican: 16, 17, 22, 24, 25 (no hay modales nuevos), 26, 31 (los formularios de catálogo conservan `MensajeError`, sin errores por campo: fuera de alcance), 32 a 36.

## Convenciones que agrega esta fase (para `herramientas.md`, Task 11; numeración provisional, Q-CONV)

- **7-A. Listado de catálogo genérico:** un catálogo nuevo hereda `CatalogoListadoViewModel<T>` y declara `Clave`, `Titulo`, `TextoNuevo`, `TextoInactivo`, `MensajeSinPermiso`, `Columnas` (máx. 4 además de NOMBRE) y `Valores(T)`; la vista es SIEMPRE `CatalogoListadoView` (no se escribe una vista por catálogo) y se agrega su `EntradaCatalogo` en `CatalogosViewModel`.
- **7-B. Columnas declaradas por el VM en una grilla de XAML fijo:** la grilla tiene las columnas máximas en XAML (bindings compilados a `Valor0`…`Valor4` de la fila) y el code-behind sincroniza `Header` e `IsVisible` desde `Columnas` al cambiar el `DataContext` (mismo mecanismo que la convención 28).
- **7-C. Master-detail con gates por grupo:** cada grupo es un getter sobre la sesión; `NotificarPermisosActualizados` recalcula los grupos, re-selecciona el primer visible si el abierto se fue, marca `SinPermiso` si no queda ninguno y reenvía al listado abierto.

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (`7fd41cf`) |
|---|---|---|
| Los 10 listados son iguales | Mismo esqueleto: `Items` + `ItemSeleccionado` con `[NotifyCanExecuteChangedFor]` de Editar y Baja, `CargarAsync`, `NuevoCommand`, `EditarCommand` / `BajaCommand` con `CanExecute` = seleccionado y activo, Baja con `PreguntarAsync` + `InformarAsync` ante `ReglaDeNegocioException or EntidadNoEncontradaException` y recarga. 7 atrapan el 403 en silencio; Fuente, Rubro y Línea POA ya usan `EjecutarCargaProtegidaAsync`. | `ViewModels/Catalogo/*ListViewModel.cs`, `ViewModels/Finanzas/{FuenteFinanciamiento,RubroGasto,LineaPoa}ListViewModel.cs` |
| La base conserva la API pública | `Items`, `ItemSeleccionado`, `CargarAsync`, `NuevoCommand`, `EditarCommand`, `BajaCommand` con los mismos nombres y tipos efectivos, así los tests de los 10 VMs compilan y pasan sin cambios. El 403 deja de ser silencioso: marca `SinPermiso` (los tests `CargarAsync_SiElServicioLanzaUnauthorized_NoPropaga…` siguen verdes: no propaga). | `CategoriaViewModelTests.cs` L43-201, `LineaPoaViewModelTests.cs` L16-35 |
| Grilla genérica con bindings compilados | `FilaCatalogo` con `Valor0..Valor4` (string) + `Activo` + `Entidad`; XAML fijo, `x:DataType="vmc:FilaCatalogo"`; `SortMemberPath="Valor{i}"` (una ruta con índice no ordena en `DataGridCollectionView`). | `IngresosViewModel.cs` (`FilasView`), `CalendarioPagosView.axaml.cs` (columnas desde el code-behind) |
| Formularios vuelven a Catálogos | Los 10 `*FormViewModel` cambian `Navegar<XListViewModel>()` / `Navegar<CatalogosTareaViewModel>()` / `Navegar<MaestrosFinanzasViewModel>()` por `Navegar<CatalogosViewModel>(vm => vm.Seleccionar(ClaveCatalogo.X))` en Guardar y en Cancelar (20 llamadas). | grep `Navegar<` en los `*FormViewModel.cs` (Categoria L61/L70, Zona L61/L70, LineaPoa L181/L190, …) |
| Sidebar | 5 `CrearItem` (L286-290) → 1; 5 `[RelayCommand] Nav*` (L522-546, L678-681) → `NavCatalogos`; `SeccionPorTipo` (L375-379) → `[typeof(CatalogosViewModel)] = "Catalogos"`. Los formularios no tienen sección propia: conservan "Catalogos" (Fase 2). | `ShellMainViewModel.cs` |
| Gates | Los mismos de hoy: INVENTARIO y TAREAS con `catalogo.maestras`, FINANZAS con `finanzas.maestros`, Admin todo (spec §4.3). | `ShellMainViewModel.cs` L86-90, L286-290 |
| DI | Task 2 agrega `CatalogosViewModel`; Task 6 quita L378 (`CatalogosTareaViewModel`) y L381 (`MaestrosFinanzasViewModel`) (Q-APPCS); los 10 listados siguen registrados (los inyecta `CatalogosViewModel`). | `App.axaml.cs` L362-387 |
| Carga del detalle | La carga la dispara `CatalogosViewModel` (al inicializar y al seleccionar); `CatalogoListadoView` NO carga en `DataContextChanged` (evita la doble consulta). `CatalogosView` sí cablea `DataContextChanged` → `CargarAsync` (memoria "Vista wiring DataContextChanged"). | `CatalogosTareaView.axaml.cs` (hoy cargaba cada sub-vista) |
| Usuarios | `UsuariosAdminViewModel.CargarAsync` ya usa `EjecutarCargaProtegidaAsync`; solo se agrega `AvisaSinConexionEnLinea => true` y `AlPerderConexion => Items.Clear()`. `PanelPermisosViewModel` intacto. | `UsuariosAdminViewModel.cs` L69-78 |
| Mantenimiento | VM intacto: ya tiene `Cargando`, `ErrorAlCargar`, `MostrarListaVacia`, `ErrorAlCargarAlertas`, `PuedeGuardarAlertas`, `PuedeProbarAlertas`. | `MantenimientoViewModel.cs` L29-101 |
| Tests que se reemplazan (convención 13) | `ProveedorListViewColumnasTests`, `UnidadMedidaListViewColumnasTests`, los escenarios de catálogos de `GrillasSinRecorteBarridoTests` (L93-102), `CargaProtegidaEstadoVacioUiTests.FuenteFinanciamientoListView_…` (L101), `CatalogosTareaViewModelTests`, las 14 `InlineData`/entradas de catálogos y las 2 de Usuarios y Mantenimiento en `GuardianDePatronTests` (L48-76, L93-121, L134-140), las de `ShellMainViewModelTests`/`ShellMainViewGatesTests`/`ShellMainFinanzasTests` que usan los 5 `Nav*` viejos, `UsuariosAdminPermisosScrollTests`, `EtiquetadoDeCamposTests` (Usuarios), los 4 `TranslatePoint` de `MantenimientoViewTests` (spec §8.2) y `CatalogoFormAnchoYCancelarTests` (destino y helpers). Cada uno con su reemplazo NOMBRADO en la task que lo toca. | grep en `tests/` |

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/ViewModels/Catalogo/CatalogoListadoViewModel.cs` | Base no genérica (lo que bindea la vista) + base genérica `CatalogoListadoViewModel<T>`. |
| `src/StockApp.Presentation/ViewModels/Catalogo/FilaCatalogo.cs`, `ColumnaCatalogo.cs`, `ClaveCatalogo.cs` | Fila uniforme, columna declarada, clave de cada catálogo. |
| `src/StockApp.Presentation/ViewModels/Administracion/CatalogosViewModel.cs`, `GrupoCatalogo.cs`, `EntradaCatalogo.cs` | Master-detail, gates por grupo, selección. |
| `src/StockApp.Presentation/Views/Administracion/CatalogosView.axaml` (+ `.cs`) | Pantalla Catálogos. |
| `src/StockApp.Presentation/Views/Catalogo/CatalogoListadoView.axaml` (+ `.cs`) | Detalle genérico (grilla, buscador, estados, acciones). |
| `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/CatalogoListadoViewModelTests.cs` | Contrato de la base y textos de los 10. |
| `tests/StockApp.Presentation.Tests/ViewModels/Administracion/CatalogosViewModelTests.cs` | Grupos, gates, selección, revocación, `Seleccionar`. |
| `tests/StockApp.Presentation.UiTests/V2/AdministracionFakes.cs` | Fakes de servicios de catálogo con `Pendiente`/`Falla`, `UsuarioServiceFake`, fakes de Mantenimiento si los de `MantenimientoViewTests` no alcanzan. |
| `tests/StockApp.Presentation.UiTests/V2/CatalogosViewV2Tests.cs`, `FormulariosCatalogoV2Tests.cs`, `UsuariosAdminViewV2Tests.cs`, `MantenimientoViewV2Tests.cs` | View real de cada pantalla. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Administracion/DatosCatalogos.cs`, `DatosUsuarios.cs`, `DatosMantenimiento.cs`, `DatosAdministracionTests.cs` | Datos fijos de los mockups. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Administracion/CatalogosComparacionTests.cs`, `FormulariosCatalogoComparacionTests.cs`, `UsuariosComparacionTests.cs`, `MantenimientoComparacionTests.cs` | 18 + 10 + 10 + 6 comparaciones. |
| `docs/ui-v2/mockups/administracion/*.html` | Los 22 mockups de D21. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| Los 10 `*ListViewModel.cs` de catálogo | Heredan de la base (Task 1). |
| Los 10 `*FormViewModel.cs` de catálogo | Destino de Guardar y Cancelar (Task 2). |
| Las 10 `*FormView.axaml` de catálogo | Reescritas en v2 (Task 7). |
| `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` | Grupo ADMINISTRACIÓN, `NavCatalogos`, `SeccionPorTipo` (Task 6). |
| `src/StockApp.Presentation/App.axaml.cs` | DI (Task 2, Q-APPCS). |
| `src/StockApp.Presentation/ViewModels/Administracion/UsuariosAdminViewModel.cs`, `Views/Administracion/UsuariosAdminView.axaml` | Task 8. |
| `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml` | Task 9. |
| `docs/ui-v2/mockups/shell/sidebar-admin.html`, `sidebar-operador.html` | D20 (Task 4). |
| Tests heredados de la tabla "Tests que se reemplazan" | Tasks 2, 6, 7, 8, 9. |
| `docs/ui-v2/herramientas.md`, la spec | Cierre (Task 11). |

**Se borran (Task 6, Q-BORRAR):** `Views/Catalogo/{Categoria,Proveedor,UnidadMedida,Zona,DimensionTematica,OrganismoResponsable,OrigenFinanciamiento}ListView.axaml(.cs)`, `Views/Finanzas/{FuenteFinanciamiento,RubroGasto,LineaPoa}ListView.axaml(.cs)`, `Views/Catalogo/CatalogosTareaView.axaml(.cs)`, `Views/Finanzas/MaestrosFinanzasView.axaml(.cs)`, `ViewModels/Catalogo/CatalogosTareaViewModel.cs`, `ViewModels/Finanzas/MaestrosFinanzasViewModel.cs`.

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln --blame-hang-timeout 5m`
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Comparación con página: `scripts/ui-v2/comparar.sh Catalogos` (o `Usuarios`, `Mantenimiento`, `FormularioCatalogo`, `Shell`) y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-17-ui-v2-fase-7-administracion/progress.md` (ya creado con el plan; `.superpowers/` está en `.gitignore`, así que la Task 0 lo COPIA al worktree de la fase). Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa.

---

## Bloque 0 — Worktree de la fase

### Task 0: Worktree, índice CodeGraph y línea base

**Precondición:** la 4e está mergeada en `feat/ui-v2` y el bloque Finanzas en `main` (cronograma, día 1). Si no, PARAR.

**Files:**
- Create (local): `/home/capua25/workspace/stockapp-worktrees/ui-v2-fase7/` (worktree) y su `.codegraph/`
- Modify (local): el ledger (BASE, línea base, respuestas a las Q-*)

- [ ] **Step 1: Crear el worktree y la rama**

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && git status --short   # limpio salvo .codegraph/
git -C /home/capua25/workspace/stockapp-worktrees/ui-v2 worktree add \
  /home/capua25/workspace/stockapp-worktrees/ui-v2-fase7 -b feat/ui-v2-fase-7 feat/ui-v2
mkdir -p /home/capua25/workspace/stockapp-worktrees/ui-v2-fase7/.superpowers/sdd
cp -r /home/capua25/workspace/stockapp-worktrees/ui-v2/.superpowers/sdd/2026-10-17-ui-v2-fase-7-administracion \
  /home/capua25/workspace/stockapp-worktrees/ui-v2-fase7/.superpowers/sdd/
```

Expected: `git -C …/ui-v2-fase7 branch --show-current` = `feat/ui-v2-fase-7`; `git -C …/ui-v2-fase7 log -1 --format=%H` = el HEAD de `feat/ui-v2` (anotarlo como BASE en el ledger).

- [ ] **Step 2: Índice CodeGraph propio**

`gentle-ai codegraph init --cwd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase7` y después `cd …/ui-v2-fase7 && codegraph status`. Expected: "Project: /home/capua25/workspace/stockapp-worktrees/ui-v2-fase7" con nodos > 0. Nunca copiar `.codegraph/` de otro checkout.

- [ ] **Step 3: Fixtures y herramientas**

Los fixtures `.ods` no viajan al worktree (rojos falsos conocidos, no se copian). `dotnet run --project tools/StockApp.UiV2.Herramientas -- instalar-navegador` si el Chromium no está en `~/.cache/ms-playwright` (es por máquina: normalmente ya está).

- [ ] **Step 4: Línea base**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 0 errores; total por proyecto anotado en el ledger (referencia: 6103 en `7fd41cf` más lo que haya sumado la 4e); solo los rojos falsos conocidos, confirmados por nombre. Anotar también las respuestas del usuario a las Q-* (las trae el orquestador) y, si alguna difiere de la R, qué task cambia.

Sin commit (no hay cambios versionados).

---

## Bloque A — ViewModels, sin tocar las vistas

### Task 1: Base `CatalogoListadoViewModel<T>` y los 10 listados heredan de ella

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/CatalogoListadoViewModel.cs`, `FilaCatalogo.cs`, `ColumnaCatalogo.cs`, `ClaveCatalogo.cs`
- Modify: los 10 `*ListViewModel.cs` de catálogo
- Create: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/CatalogoListadoViewModelTests.cs`

**Interfaces:**
- Produces:
  - `public enum ClaveCatalogo { Categorias, Proveedores, UnidadesMedida, FuentesFinanciamiento, RubrosGasto, LineasPoa, Zonas, Dimensiones, Organismos, Origenes }`
  - `public sealed record ColumnaCatalogo(string Encabezado);` (siempre medida, convención 19; NOMBRE no se declara: es fija)
  - `public sealed class FilaCatalogo { public object Entidad { get; } public bool Activo { get; } public string Valor0 { get; } public string? Valor1 … Valor4 { get; } }` (`Valor0` = nombre)
  - `public abstract partial class CatalogoListadoViewModel : ViewModelBase` con: `abstract ClaveCatalogo Clave`, `abstract string Titulo`, `abstract string TextoNuevo`, `abstract string TextoInactivo`, `abstract string MensajeSinPermiso`, `abstract IReadOnlyList<ColumnaCatalogo> Columnas` (0 a 4), `ObservableCollection<FilaCatalogo> Filas`, `DataGridCollectionView FilasView`, `[ObservableProperty] string _busqueda = ""`, `int CantidadVisible` (notifica), `bool SinCoincidencias` (`Filas.Count > 0 && CantidadVisible == 0`, notifica), `[ObservableProperty] FilaCatalogo? _filaSeleccionada`, `abstract IAsyncRelayCommand NuevoCommand`, `abstract IAsyncRelayCommand EditarCommand`, `abstract IAsyncRelayCommand BajaCommand`, `abstract Task CargarAsync()`, `protected override bool AvisaSinConexionEnLinea => true`.
  - `public abstract partial class CatalogoListadoViewModel<T> : CatalogoListadoViewModel where T : class` con: `protected CatalogoListadoViewModel(INavigationService navigation, IConfirmacionService confirmacion)`, `ObservableCollection<T> Items`, `T? ItemSeleccionado` (sincronizado con `FilaSeleccionada` en los dos sentidos, notifica Editar y Baja), abstractos `Task<IReadOnlyList<T>> ListarAsync()`, `Task DarDeBajaAsync(T item)`, `string MensajeConfirmarBaja(T item)`, `bool EsActivo(T item)`, `IReadOnlyList<string?> Valores(T item)` (nombre primero, después uno por columna), `void NavegarANuevo()`, `void NavegarAEditar(T item)`.
  - Los 10 VMs: mismos nombres públicos de hoy (`Items`, `ItemSeleccionado`, `CargarAsync`, `NuevoCommand`, `EditarCommand`, `BajaCommand`) y los textos y columnas de D6.

- [ ] **Step 1: Tests de la base (rojo)**

En `CatalogoListadoViewModelTests.cs` (Moq, como `CategoriaViewModelTests`), con una fábrica `Crear(ClaveCatalogo, IEnumerable<object> items)` que arma el VM real de cada clave con su servicio mockeado:

```csharp
[Theory] [MemberData(nameof(LasDiezClaves))]
public void Textos_SonLosAprobados(ClaveCatalogo clave, string titulo, string nuevo, string inactivo, string sinPermiso, string[] columnas)
// Assert.Equal sobre Titulo, TextoNuevo, TextoInactivo, MensajeSinPermiso y Columnas.Select(c => c.Encabezado) con la tabla D6.

[Fact] public async Task Cargar_ArmaUnaFilaPorItemConLosValoresDeLasColumnas()
// Proveedor { Nombre="Barraca Carmelo", Telefono="4542 2210", Email="ventas@barraca.uy", Direccion="18 de Julio 455", Notas=null, Activo=false }
// → Filas.Single(): Valor0 "Barraca Carmelo", Valor1 "4542 2210", Valor2 "ventas@barraca.uy", Valor3 "18 de Julio 455", Valor4 null, Activo false, Entidad el mismo objeto.

[Fact] public async Task Busqueda_FiltraSinMayusculasNiTildes_YCuentaLasVisibles()
// Categorías "Electricidad", "Plomería", "Pinturería"; Busqueda = "PLOMERIA" → CantidadVisible 1 y la única visible de FilasView es Plomería; Busqueda = "" → 3.

[Fact] public async Task Busqueda_BuscaEnTodasLasColumnas()
// Proveedor con Email "compras@ferreteria.uy"; Busqueda "ferreteria" → CantidadVisible 1.

[Fact] public async Task Busqueda_QueOcultaLaFilaSeleccionada_LimpiaLaSeleccionYDeshabilitaAcciones()   // Review Focus 1
// ItemSeleccionado = Plomería; Busqueda = "elec" → ItemSeleccionado null, FilaSeleccionada null, EditarCommand.CanExecute(null) false, BajaCommand.CanExecute(null) false.

[Fact] public async Task FilaSeleccionada_EItemSeleccionado_SeSincronizan()
// FilaSeleccionada = Filas[1] → ItemSeleccionado == Items[1]; ItemSeleccionado = Items[0] → FilaSeleccionada == Filas[0].

[Fact] public async Task SinCoincidencias_SoloConFilasYNingunaVisible()
// sin datos → false; con datos y Busqueda "zzz" → true; Busqueda "" → false.

[Fact] public async Task Baja_RecargaYConservaLaBusqueda()   // Review Focus 4
// Busqueda "pin"; ItemSeleccionado Pinturería; confirmación true; el servicio devuelve en la recarga Pinturería con Activo=false
// → Busqueda sigue "pin", CantidadVisible 1, Filas visibles: Pinturería con Activo false.

[Fact] public async Task Cargar_Un403_MarcaSinPermisoConSuMensaje_YVaciaFilas()
// tras una carga con datos, la siguiente lanza UnauthorizedAccessException → SinPermiso true, MensajeSinPermiso de D6 visible en la base, Filas vacía.

[Fact] public async Task Cargar_SinServidor_MarcaSinConexion_YReintentarRecarga()
// ServidorNoDisponibleException → SinConexion true, Filas vacía; el servicio vuelve; ReintentarCargaCommand → SinConexion false, Filas con datos.

[Fact] public async Task DosCargas_LaQueTerminaTarde_NoPisaALaUltima()
// dos TaskCompletionSource; la segunda completa primero con [B], la primera después con [A] → Filas = [B] (convención 23).
```

- [ ] **Step 2: Correr y ver el rojo**

`dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~CatalogoListadoViewModelTests"`. Expected: no compila (`ClaveCatalogo`, `Filas`, `Busqueda` no existen). Pegar el error en el ledger.

- [ ] **Step 3: Implementar la base y migrar los 10 VMs**

La base: la carga pide turno (`ConsultaVigente`), pasa por `EjecutarCargaProtegidaAsync(cargar, MensajeSinPermiso, turno)` y, si `turno.EsLaUltima`, reemplaza `Items` y reconstruye `Filas` (`AlPerderConexion` y el 403 vacían ambas); `FilasView.Filter` = predicado `EsVisible(fila)` con `CultureInfo.InvariantCulture.CompareInfo.IndexOf(valor, Busqueda, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0` sobre `Valor0..Valor4`; `OnBusquedaChanged` → `FilasView.Refresh()`, recalcular `CantidadVisible`/`SinCoincidencias` y soltar la selección si quedó oculta; Baja = el cuerpo de hoy (confirmar con `MensajeConfirmarBaja`, `DarDeBajaAsync`, `CargarAsync`, `InformarAsync(ex.Message)` ante `ReglaDeNegocioException or EntidadNoEncontradaException`); `CanExecute` de Editar y Baja = `ItemSeleccionado is not null && EsActivo(ItemSeleccionado)`; Nuevo y Editar siguen navegando con `Task.Run(() => …)` como hoy. Cada VM queda con su constructor de hoy (servicio, navegación, confirmación), su `MensajeConfirmarBaja` de hoy (literal, ver la tabla de decisiones) y los valores de D6. Borrar de cada VM lo que pasa a la base (nada de lógica duplicada).

- [ ] **Step 4: Verde y guardianes heredados SIN tocar**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m
git diff <BASE> --stat -- tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ tests/StockApp.Presentation.Tests/ViewModels/Finanzas/
```

Expected: todo verde (las vistas v1 de los listados siguen compilando: bindean `Items` e `ItemSeleccionado`, que no cambiaron); el `git diff --stat` muestra SOLO `CatalogoListadoViewModelTests.cs`. `CargaProtegidaSinPermisoGuardianTests` verde (los 7 `catch (UnauthorizedAccessException)` silenciosos desaparecen).

- [ ] **Step 5: Mutaciones (QUITANDO)**

1a quitar el `Refresh()`/recálculo de `OnBusquedaChanged` → rojo `Busqueda_FiltraSinMayusculasNiTildes…`. 1b quitar la línea que suelta la selección oculta → rojo `Busqueda_QueOcultaLaFilaSeleccionada…`. 1c quitar `CompareOptions.IgnoreNonSpace` → rojo `Busqueda_FiltraSinMayusculasNiTildes…`. 1d quitar el `if (turno.EsLaUltima)` → rojo `DosCargas_…`. 1e quitar el override `AvisaSinConexionEnLinea` → rojo `Cargar_SinServidor_…`. 1f quitar `&& EsActivo(ItemSeleccionado)` del `CanExecute` → rojo en los heredados `EditarCommand_ItemInactivo_EstaDeshabilitado` y `BajaCommand_ItemInactivo_EstaDeshabilitado` (de los 10 archivos). Pegar cada rojo.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Catalogo/ src/StockApp.Presentation/ViewModels/Finanzas/FuenteFinanciamientoListViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/RubroGastoListViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/LineaPoaListViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Catalogo/CatalogoListadoViewModelTests.cs
git commit -m "feat(catalogos): listado de catálogo genérico con búsqueda, carga protegida y consulta vigente"
```

(`git add src/StockApp.Presentation/ViewModels/Catalogo/` incluye `ProductoListViewModel.cs`/`ProductoFormViewModel.cs`: verificar con `git status --short` que NO aparecen modificados.)

### Task 2: `CatalogosViewModel`, gates por grupo y los formularios vuelven a Catálogos

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Administracion/CatalogosViewModel.cs`, `GrupoCatalogo.cs`, `EntradaCatalogo.cs`
- Create: `tests/StockApp.Presentation.Tests/ViewModels/Administracion/CatalogosViewModelTests.cs`
- Modify: los 10 `*FormViewModel.cs` de catálogo (solo Guardar y Cancelar), `src/StockApp.Presentation/App.axaml.cs` (agrega `services.AddTransient<CatalogosViewModel>();` junto a L378; las líneas de `CatalogosTareaViewModel` y `MaestrosFinanzasViewModel` se quitan recién en la Task 6, porque el sidebar sigue navegando a ellos hasta entonces; Q-APPCS)
- Modify (convención 13, solo el destino): los tests de navegación de los formularios en los 10 `*ViewModelTests.cs` y `CatalogoFormAnchoYCancelarTests.cs` (`DestinoEsperado` de cada caso → `typeof(CatalogosViewModel)` y la clave)

**Interfaces:**
- Consumes: Task 1 (`CatalogoListadoViewModel`, `ClaveCatalogo`).
- Produces:
  - `public sealed partial class EntradaCatalogo : ObservableObject` — `ClaveCatalogo Clave`, `string Titulo`, `string Icono`, `CatalogoListadoViewModel Listado`, `[ObservableProperty] bool _estaSeleccionada`.
  - `public sealed partial class GrupoCatalogo : ObservableObject` — `string Titulo` (MAYÚSCULAS), `IReadOnlyList<EntradaCatalogo> Entradas`, `bool EsVisible` (getter sobre un `Func<bool>`; `Recalcular()` lo notifica).
  - `public partial class CatalogosViewModel : ViewModelBase` — ctor `(ICurrentSession session, CategoriaListViewModel, ProveedorListViewModel, UnidadMedidaListViewModel, FuenteFinanciamientoListViewModel, RubroGastoListViewModel, LineaPoaListViewModel, ZonaListViewModel, DimensionTematicaListViewModel, OrganismoResponsableListViewModel, OrigenFinanciamientoListViewModel)`; `IReadOnlyList<GrupoCatalogo> Grupos` (INVENTARIO, FINANZAS, TAREAS); `[ObservableProperty] CatalogoListadoViewModel? _catalogoActual`; `void Seleccionar(ClaveCatalogo clave)` (solo marca la pendiente, sin cargar); `Task CargarAsync()` (aplica la pendiente o el primer visible y carga ese listado); `IAsyncRelayCommand<EntradaCatalogo> SeleccionarCommand` (selecciona, limpia la búsqueda del listado y lo carga); `const string MensajeSinPermiso = "No tenés permiso para ver los catálogos."`; override `NotificarPermisosActualizados`.
  - En los 10 formularios: `Navegar<CatalogosViewModel>(vm => vm.Seleccionar(ClaveCatalogo.<X>))` en Guardar y en Cancelar.

- [ ] **Step 1: Tests (rojo)**

`CatalogosViewModelTests.cs` (sesión con `SesionFake`-equivalente de Presentation.Tests o `Mock<ICurrentSession>`; listados reales con servicios mockeados):

```csharp
[Fact] public void Admin_VeLosTresGruposConLosDiezCatalogosEnElOrdenDeLaSpec()
// Grupos.Select(g => g.Titulo) == ["INVENTARIO","FINANZAS","TAREAS"]; Entradas por grupo == las de §4.3 en orden; todos EsVisible.

[Fact] public void OperadorConTablasMaestras_VeInventarioYTareas_NoFinanzas()       // operador mixto
[Fact] public void OperadorConMaestrosFinanzas_SoloVeFinanzas()                     // operador mixto
[Fact] public async Task Cargar_SinPendiente_SeleccionaElPrimerVisibleYLoCarga()
// operador con finanzas.maestros → CatalogoActual.Clave == FuentesFinanciamiento, su EstaSeleccionada true y su servicio llamado UNA vez.
[Fact] public async Task Seleccionar_YDespuesCargar_AbreEseCatalogo()
// Seleccionar(UnidadesMedida); CargarAsync → CatalogoActual.Clave == UnidadesMedida; Categorías NO se consultó.
[Fact] public async Task Seleccionar_UnCatalogoDeUnGrupoSinPermiso_AbreElPrimerVisible()
[Fact] public async Task SeleccionarCommand_CambiaDeCatalogo_LimpiaLaBusquedaYRecarga()
// en Categorías con Busqueda "x"; SeleccionarCommand(Zonas) → la entrada de Categorías ya no está seleccionada, Zonas sí, Zonas.Busqueda "" y su servicio consultado.
[Fact] public async Task RevocarElGrupoDelCatalogoAbierto_SeleccionaElPrimerVisible()   // Review Focus 2
// operador con las dos; abierto LineasPoa; se quita finanzas.maestros de la sesión; NotificarPermisosActualizados() → FINANZAS.EsVisible false, CatalogoActual.Clave == Categorias.
[Fact] public async Task RevocarTodosLosGrupos_MarcaSinPermiso()
// se quitan las dos → SinPermiso true con MensajeSinPermiso, CatalogoActual null.
[Fact] public async Task NotificarPermisosActualizados_ReenviaAlListadoAbierto()   // convención 29
// el listado abierto recibe PropertyChanged con nombre vacío (suscribirse a su PropertyChanged).
```

La navegación de los formularios se prueba con los tests heredados adaptados más esta teoría, también en `CatalogosViewModelTests.cs`:

```csharp
[Theory] [MemberData(nameof(LosDiezFormularios))]
public async Task GuardarYCancelar_VuelvenACatalogosConSuClave(Func<INavigationService, ViewModelBase> crearFormulario, ClaveCatalogo esperada)  // Review Focus 3
// con un INavigationService fake que ejecuta el inicializador sobre un CatalogosViewModel real: tras Cancelar y tras un Guardar exitoso,
// el destino es CatalogosViewModel y su Seleccionar recibió `esperada`.
```

- [ ] **Step 2: Rojo**

Correr el filtro `CatalogosViewModelTests` y los heredados de navegación ya adaptados (`GuardarCommand_Exitoso_NavegaAListado` y equivalentes, que ahora esperan `CatalogosViewModel`). Expected: no compila / falla porque los formularios siguen navegando al listado viejo. Pegar.

- [ ] **Step 3: Implementar**

`CatalogosViewModel`: gates con las mismas fórmulas que `ShellMainViewModel.PuedeGestionarTablasMaestras` y `PuedeGestionarMaestrosFinanzas` (L86-90: Admin o el permiso), como getters sobre `_session.PermisosActuales` (convención 36: la revocación los re-evalúa). Iconos de las entradas: D2. `NotificarPermisosActualizados`: `base…()`, `Recalcular()` de cada grupo, y si el grupo del `CatalogoActual` ya no es visible: el primer visible (seleccionar y cargar) o, sin ninguno, `CatalogoActual = null` + `MarcarSinPermiso(MensajeSinPermiso)`; si sigue visible, reenviar `CatalogoActual.NotificarPermisosActualizados()`. Formularios: reemplazar las 20 llamadas. DI: Q-APPCS. `CatalogosTareaViewModel`/`MaestrosFinanzasViewModel` NO se borran todavía (sus vistas v1 existen hasta la Task 6).

- [ ] **Step 4: Verde**

Build y las suites completas de `Presentation.Tests` y `Presentation.UiTests` (incluye `CatalogoFormAnchoYCancelarTests` adaptado y `ComposicionDIApiTests`). Expected: verde. Anotar en el ledger cada test heredado adaptado (archivo, nombre, línea vieja → nueva).

- [ ] **Step 5: Mutaciones**

2a quitar la línea que re-selecciona el primer visible en `NotificarPermisosActualizados` → rojo `RevocarElGrupoDelCatalogoAbierto_…`. 2b quitar `MarcarSinPermiso(MensajeSinPermiso)` → rojo `RevocarTodosLosGrupos_…`. 2c quitar el reenvío al listado → rojo `NotificarPermisosActualizados_ReenviaAlListadoAbierto`. 2d quitar el argumento `vm => vm.Seleccionar(ClaveCatalogo.UnidadesMedida)` del Cancelar de `UnidadMedidaFormViewModel` (queda `Navegar<CatalogosViewModel>()`: es acortar, no reemplazar) → rojo de la teoría para UnidadesMedida. 2e quitar `|| _session.PermisosActuales.Contains(Permisos.GestionarMaestrosFinanzas)` del gate de FINANZAS → rojo `OperadorConMaestrosFinanzas_SoloVeFinanzas`. Pegar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Administracion/ src/StockApp.Presentation/ViewModels/Catalogo/ src/StockApp.Presentation/ViewModels/Finanzas/FuenteFinanciamientoFormViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/RubroGastoFormViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/LineaPoaFormViewModel.cs src/StockApp.Presentation/App.axaml.cs tests/StockApp.Presentation.Tests/ tests/StockApp.Presentation.UiTests/CatalogoFormAnchoYCancelarTests.cs
git commit -m "feat(catalogos): pantalla de catálogos con gates por grupo y vuelta desde los formularios"
```

---

## Bloque B — Datos fijos, medición y mockups (UN GATE 1)

### Task 3: Datos fijos de Catálogos, Usuarios y Mantenimiento

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Administracion/DatosCatalogos.cs`, `DatosUsuarios.cs`, `DatosMantenimiento.cs`, `DatosAdministracionTests.cs`

**Interfaces:**
- Consumes: Tasks 1 y 2; `DatosShell.Crear(UsuarioSesion, string[] permisos, string seccionActiva, ViewModelBase? contenido)`; `DatosProductos.UsuarioAdmin` / `DatosIngresos.UsuarioOperador` (o los equivalentes que existan tras la 4e).
- Produces (cada uno devuelve el `ShellMainViewModel` con el contenido ya cargado, sin tocar `internal`):
  - `DatosCatalogos`: `Categorias()` (Admin; 6 categorías, "Herramientas fuera de uso" inactiva), `Proveedores()` (4 proveedores, uno con dirección larga que se corta), `LineasPoa()` (4 líneas 2026, una inactiva), `Operador()` (permisos `[Permisos.GestionarTablasMaestras]`, detalle Zonas con 5 zonas), `Cargando()`, `SinConexion()`, `SinPermiso()` (403 en Fuentes), `Vacio()` (Unidades sin datos), `SinResultados()` (Categorías con búsqueda "zzz"); formularios `CategoriaNueva()`, `CategoriaError()` (`MensajeError` = "Ya existe una categoría con ese nombre."), `ProveedorEditar()`, `RubroNuevo()`, `LineaPoaEditar()` (dos asignaciones: "Rentas Generales" 1.250.000,00 y "FIGM Literal B" 380.000,00).
  - `DatosUsuarios`: `SinSeleccion()`, `OperadorSeleccionado()` (permisos tildados mixtos, una advertencia de dependencia blanda visible), `AdminSeleccionado()`, `InactivoSeleccionado()`, `AltaConError()` (`MensajeError` = el de `ReglaDeNegocioException` "Ya existe un usuario con ese nombre.").
  - `DatosMantenimiento`: `ConCorridas()` (3: exitosa con archivo, fallida con motivo, exitosa con nota; logs: 12 archivos; alertas con URL y habilitado), `Vacio()` (sin corridas, "No hay archivos de log todavía."), `ConErrores()` (falla `ListarAsync` y la de alertas). Fechas fijas en UTC con zona fija como `DatosInicio.Ahora`.

- [ ] **Step 1: Tests (rojo)** — `DatosAdministracionTests.cs`: una aserción por fábrica sobre lo que el mockup va a mostrar (conteos, textos de D6, `SinPermiso`/`SinConexion`/`Cargando`, `CatalogoActual.Clave`, `UsuarioSeleccionado.NombreUsuario`, `Corridas.Count`, `ErrorAlCargar`, `ErrorAlCargarAlertas`, el `MensajeError` exacto). Rojo: no compila.
- [ ] **Step 2: Implementar** las fábricas con fakes locales del proyecto VisualTests (patrón de `DatosGastos`), fechas y reloj fijos.
- [ ] **Step 3: Verde** — `dotnet test tests/StockApp.Presentation.VisualTests/… --filter "FullyQualifiedName~DatosAdministracionTests"`.
- [ ] **Step 4: Mutación** 3a: quitar `Activo = false` de "Herramientas fuera de uso" → rojo `DatosAdministracionTests.Categorias_SeisFilasYUnaInactiva` (el test de la fábrica `Categorias()` del Step 1 se llama así). Pegar.
- [ ] **Step 5: Commit** — `git add tests/StockApp.Presentation.VisualTests/Pantallas/Administracion/ && git commit -m "test(ui-v2): datos fijos de catálogos, usuarios y mantenimiento"`

### Task 4: Medición y mockups de TODA la fase — GATE 1 con el usuario

**Decisiones aplicadas:** D1 a D21 y las Q-* como las contestó el usuario.

**Files:**
- Create: los 22 `docs/ui-v2/mockups/administracion/*.html` de D21
- Modify: `docs/ui-v2/mockups/shell/sidebar-admin.html` y `sidebar-operador.html` (solo el bloque ADMINISTRACIÓN; el pulgar NO se toca acá: ver Q-SIDEBAR y Task 6)
- Modify (local): el ledger (medidas)

**Interfaces:**
- Consumes: Task 3; `ColumnaAuto` y `DataGridColumnasAutoBehavior.AnchoMedido`.
- Produces: 48 PNG (`artifacts/ui-v2/mockups/administracion-*.png` y `@1366x768`) y en el ledger los anchos que usan las Tasks 6 a 9.

- [ ] **Step 1: Medir en headless (archivo temporal, no se commitea)**

`tests/StockApp.Presentation.VisualTests/MedicionTemporal7.cs` siguiendo `MedicionTemporal4d` (plan de la 4d, Task 12 Step 1). Con el tema v2, a 1280×760 y 1366×768, escribe: (1) por cada catálogo de D21 con datos, `AnchoMedido` de cada columna medida y el ancho de la insignia "Inactivo"/"Inactiva" (+16 = ESTADO); si NOMBRE con `MinWidth` 200 entra o la grilla de Proveedores scrollea en horizontal y desde qué columna; (2) el alto del master con los 10 ítems (que entra sin scroll a 760); (3) Usuarios: alto de la card "Nuevo usuario", alto útil de la grilla de usuarios, alto del panel de permisos completo y del pie fijo, a los dos tamaños; anchos de USUARIO/ROL/ESTADO; (4) Mantenimiento: anchos de FECHA, RESULTADO, TAMAÑO y de la columna de acción ("Descargar" y "Cancelar"), alto de las cards inferiores; (5) cada formulario: que el texto de los botones y los placeholders no se corten en 347 (media card). Run con `--logger "console;verbosity=detailed"`; anotar en el ledger.

- [ ] **Step 2: Mockups**

Partir de `finanzas/ingresos*.html` (listado, insignia, vacíos), `inventario/historial.html` (grilla ancha con scroll horizontal), `finanzas/ingreso-nuevo.html` e `ingreso-error-servidor.html` (formulario v2), `finanzas/gasto-editar.html` (pie fijo), `shell/sidebar-admin.html` (`.v2-item-sidebar`, `.v2-header-seccion-sidebar` para el master) y `inicio/inicio-admin.html` (`v2-grilla-uniforme--2`). Cada mockup: `<title>Administración: {pantalla} (UI v2)</title>`, `<!-- Datos: Datos*.{Fábrica} -->`, sidebar con el ítem de la pantalla activo (ADMINISTRACIÓN según D20), anchos MEDIDOS como `flex: 0 0 Npx`, celdas numéricas con `v2-num` + `<span>`. CSS propio en `<style>` del HTML.

- [ ] **Step 3: Capturas y verificación de la herramienta**

```bash
for p in $(ls docs/ui-v2/mockups/administracion/*.html | xargs -n1 basename | sed 's/\.html$//'); do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/administracion/$p.html artifacts/ui-v2/mockups/administracion-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/administracion/$p.html artifacts/ui-v2/mockups/administracion-$p@1366x768.png --tamano 1366x768
done
for p in sidebar-admin sidebar-operador; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/shell/$p.html artifacts/ui-v2/mockups/shell-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/shell/$p.html artifacts/ui-v2/mockups/shell-$p@1366x768.png --tamano 1366x768
done
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 48 PNG con fecha de ahora; Herramientas.Tests verde (`MockupsDelRepo_RespetanLaConvencion`); exit 0. MIRAR cada PNG.

- [ ] **Step 4: Borrar la medición temporal** — `rm tests/StockApp.Presentation.VisualTests/MedicionTemporal7.cs` y `git status --short tests/` no la lista.

- [ ] **Step 5: GATE 1 — aprobación del usuario (PARADA, única de mockups de la fase)**

PARAR. Presentar los 48 PNG (LOS DOS TAMAÑOS) agrupados por pantalla, D1 a D21, las respuestas a las Q-* y las medidas del ledger. Señalar explícitamente: D2 (master con `ItemSidebar` e íconos; la alternativa sin ícono exige un cambio de tema, ver "Necesidades de tema compartido"), D4 (NOMBRE siempre primero: Rubro muestra NOMBRE antes que CÓDIGO y Línea POA NOMBRE · PROGRAMA · EJERCICIO, distinto de la v1), D5 (textos de los estados), D8 (doble click, nuevo), D14/D15 (Usuarios: alta en línea, detalle vacío sin selección, pie fijo), D17/D18 (Mantenimiento en grilla y dos cards), D20 (sidebar) y Q-FORM-MOCKUPS (5 formularios representativos). No seguir sin "aprobado" explícito. Si cambia una D#, aplicarla en los mockups y en los tests de las Tasks 1 a 3 que la fijan (con su rojo y su verde) y volver a presentar. Registrar en el ledger: `Mockups Fase 7: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 6: Commit**

```bash
git add docs/ui-v2/mockups/administracion/ docs/ui-v2/mockups/shell/sidebar-admin.html docs/ui-v2/mockups/shell/sidebar-operador.html
git commit -m "docs(ui-v2): mockups aprobados de catálogos, usuarios y mantenimiento"
```

---

## Bloque C — Vistas v2 (arrancan solo con los mockups aprobados)

### Task 5: Catálogos v2 — `CatalogosView` y `CatalogoListadoView` genérica

**Files:**
- Create: `src/StockApp.Presentation/Views/Administracion/CatalogosView.axaml` (+ `.cs`), `src/StockApp.Presentation/Views/Catalogo/CatalogoListadoView.axaml` (+ `.cs`)
- Create: `tests/StockApp.Presentation.UiTests/V2/AdministracionFakes.cs`, `tests/StockApp.Presentation.UiTests/V2/CatalogosViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Administracion/CatalogosComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (`VistasMigradasAV2`: agregar `CatalogosView` y `CatalogoListadoView`; solo esas dos líneas)

**Interfaces:**
- Consumes: Tasks 1 a 4.
- Produces: `x:Name` en `CatalogosView`: `Encabezado`, `Master`, `GrupoInventario`, `GrupoFinanzas`, `GrupoTareas` (cada uno el contenedor del header + sus ítems), `Detalle` (el `CatalogoListadoView`), `EstadoSinPermiso`. En `CatalogoListadoView`: `TituloCatalogo`, `BotonEditar`, `BotonBaja`, `BotonNuevo`, `Tarjeta`, `Filtros`, `Grilla`, `ColumnaNombre`, `Columna1`…`Columna4`, `ColumnaEstado`, `PieConteo`, `IndicadorCarga`, `EstadoSinConexion`, `EstadoSinPermiso`, `EstadoSinDatos`, `EstadoSinResultados`. Fakes de catálogo con `Lista`, `Pendiente`, `Falla`, `Bajas` (los usa también la Task 7).

- [ ] **Step 1: Tests de la vista (rojo)** — `CatalogosViewV2Tests.cs`, montando `ShellMainView` con `CurrentContent` = `CatalogosViewModel` real (como `GastosViewV2Tests`):

```csharp
Raiz_TieneUi2_EncabezadoCatalogos_YNuevaEsElUnicoPrimario()
Master_Admin_MuestraLosTresGruposConSusDiezItemsEnOrden()
Master_OperadorConTablasMaestras_OcultaFinanzasConSuHeader()            // operador mixto
Master_OperadorConMaestrosFinanzas_SoloFinanzas()                       // operador mixto
Master_ClickEnUnItem_LoMarcaActivoYCambiaElDetalle()                   // ItemSidebar.EstaActivo + TituloCatalogo.Text
Detalle_Proveedores_MuestraLasCincoColumnasConSusEncabezados_YOcultaLasDemas()   // Header y IsVisible de Columna1..4
Detalle_Categorias_SoloNombreYEstado()
Detalle_FilaInactiva_InsigniaConElGeneroDelCatalogo_YTextoInactivo()   // "Inactiva" en Categorías, "Inactivo" en Proveedores; Classes.inactivo
Detalle_Cargando_MuestraIndicadorSinGrillaNiPie()
Detalle_SinConexion_EstadoConReintentar_DentroDeLaCard()
Detalle_SinPermiso_ReemplazaLaCardYElMasterQueda()
Detalle_SinDatos_VacioConBotonNuevoSecundario()
Detalle_SinResultados_NoMuestraElVacioDeSinDatos()
Busqueda_EscribirEnElBuscador_FiltraLaGrillaYElPie()
DobleClickEnFila_Edita_SoloSiEstaActiva()
DobleClickEnElEncabezado_NoEdita()
Pie_CuentaLasVisibles_ConSingularYPlural()
RevocacionDeMaestrosFinanzas_DentroDelShell_ElGrupoDesapareceYElDetalleCambia()   // Review Focus 2, camino real del 403
SidebarItemCatalogos_SeccionActivaCatalogos()                          // SeccionActiva = "Catalogos" y el ItemSidebar del shell activo
```

- [ ] **Step 2: Rojo** — no compila (vistas inexistentes). Pegar.
- [ ] **Step 3: Implementar las dos vistas** según los mockups aprobados y D1-D9. `CatalogosView.axaml.cs`: `DataContextChanged` → `await vm.CargarAsync()`. `CatalogoListadoView`: la grilla de XAML fijo (D4, convención 7-B); el code-behind sincroniza `Header`/`IsVisible` de `Columna1..4` desde `Columnas` al cambiar el `DataContext` y en `PropertyChanged` con nombre vacío (desuscribiendo el VM anterior); doble click en fila → `EditarCommand` si `CanExecute`; NO carga en `DataContextChanged`. Estados con `MultiBinding` + `BoolConverters.And` (convención 18) y `ConteoConverter` sobre `CantidadVisible`.
- [ ] **Step 4: Comparaciones** — `CatalogosComparacionTests.cs`: las 9 pantallas de catálogos de D21 × 2 tamaños (18 tests), dentro del shell con sidebar oculto. Run `scripts/ui-v2/comparar.sh Catalogos`. Expected: 18 APROBADAS con el umbral por defecto; si una no entra, diagnosticar con `teselas.py`/`tinta.py` (copiados a `$SCRATCH/diag/`, nunca se commitean) y, si es solo tono, juntarla para el GATE 2.
- [ ] **Step 5: Verde** — build y suites completas de `Presentation.Tests`, `Presentation.UiTests` y `VisualTests`.
- [ ] **Step 6: Mutaciones** — 5a quitar `IsVisible` del contenedor `GrupoFinanzas` → rojo `Master_OperadorConTablasMaestras_OcultaFinanzasConSuHeader`; 5b quitar la sincronización de `IsVisible` de columnas en el code-behind → rojo `Detalle_Categorias_SoloNombreYEstado`; 5c quitar `Classes.inactivo` de la celda de NOMBRE → rojo `Detalle_FilaInactiva_…`; 5d quitar el término `!SinPermiso` del `MultiBinding` de `EstadoSinDatos` → rojo `Detalle_SinPermiso_…`; 5e quitar el handler de doble click → rojo `DobleClickEnFila_Edita_…`; 5f quitar `Classes="ui2"` de la raíz → rojo `Raiz_TieneUi2_…` y las comparaciones. Pegar.
- [ ] **Step 7: Commit** — `git add src/StockApp.Presentation/Views/Administracion/CatalogosView.axaml* src/StockApp.Presentation/Views/Catalogo/CatalogoListadoView.axaml* tests/StockApp.Presentation.UiTests/V2/AdministracionFakes.cs tests/StockApp.Presentation.UiTests/V2/CatalogosViewV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Administracion/CatalogosComparacionTests.cs && git commit -m "feat(ui-v2): catálogos master-detail con listado genérico"`

### Task 6: Sidebar con un solo "Catálogos" y baja de las pantallas viejas

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` (L284-293 grupo ADMINISTRACIÓN, los 5 `Nav*` de L522-546 y L678-681, `SeccionPorTipo` L375-379; comentario de L248-249 solo en la mitad que habla de Catálogos)
- Delete: las 12 vistas y 2 VMs de "Se borran" (Q-BORRAR), y en `src/StockApp.Presentation/App.axaml.cs` las dos líneas que los registran (L378 y L381)
- Modify (convención 13, cada uno con su reemplazo nombrado en el ledger): `ShellMainViewModelTests.cs`, `ShellMainFinanzasTests.cs`, `ShellMainViewGatesTests.cs` (los tests que usan `NavCategoriasCommand`, `NavProveedoresCommand`, `NavUnidadesMedidaCommand`, `NavCatalogosTareaCommand`, `NavMaestrosFinanzasCommand` pasan a `NavCatalogosCommand` con el gate compuesto), `GuardianDePatronTests.cs` (sacar las `InlineData` y entradas de las vistas borradas, L48-59 y L93-104, L134-140, sin tocar otras líneas), `GrillasSinRecorteBarridoTests.cs` (L93-102: los escenarios de catálogos pasan a `CatalogoListadoView` con un VM de cada clave), `CargaProtegidaEstadoVacioUiTests.cs` (L101 → `CatalogoListadoView` con Fuentes y un 403), `ProveedorListViewColumnasTests.cs` y `UnidadMedidaListViewColumnasTests.cs` (se reemplazan por los tests `Detalle_*` de la Task 5 y por `Detalle_Proveedor_SinDireccionNiNotas_NoRompe` y `Detalle_SeleccionarFila_ActualizaItemSeleccionado`, que se agregan a `CatalogosViewV2Tests` en esta task), `CatalogosTareaViewModelTests.cs` (lo reemplaza `Admin_VeLosTresGruposConLosDiezCatalogosEnElOrdenDeLaSpec`), `tests/StockApp.Presentation.VisualTests/Pantallas/Shell/ShellComparacionTests.cs` (solo si un nombre de comando o de ítem cambia)

**Interfaces:**
- Consumes: Task 5.
- Produces: `ShellMainViewModel.NavCatalogosCommand`; sección `"Catalogos"`; `PuedeGestionarCatalogos => PuedeGestionarTablasMaestras || PuedeGestionarMaestrosFinanzas` (notificada en `NotificarPermisos`).

- [ ] **Step 1: Tests (rojo)** — en `ShellMainViewGatesTests.cs` (con la View real):

```csharp
OperadorConGestionarTablasMaestras_VeCatalogos_NoVeUsuariosNiMantenimiento()     // reemplaza OperadorConGestionarTablasMaestras_VeLosTresCatalogos
OperadorConSoloGestionarMaestrosFinanzas_VeCatalogosYSuSeccionAdministracion()     // reemplaza …_VeMaestrosYSuSeccionAdministracionCONSuTitulo
OperadorSinNingunoDeLosDos_NoVeCatalogos()
Administracion_TieneCatalogosUsuariosMantenimiento_EnEseOrden()                   // Admin; Seccion de los visibles == ["Catalogos","Usuarios","Mantenimiento"]
RevocarAmbos_EnCaliente_CatalogosDesaparece()
```

y en `ShellMainViewModelTests.cs`: `NavCatalogos_NavegaACatalogosYMarcaLaSeccion()` y `Formulario_DeCatalogo_ConservaLaSeccionCatalogos()`. Rojo: no compila.

- [ ] **Step 2: Implementar** D20 en `ShellMainViewModel`, borrar los 5 `Nav*`, las 12 vistas y los 2 VMs. Adaptar los tests heredados de la lista (ninguno se borra sin su reemplazo; anotar el par en el ledger).
- [ ] **Step 3: Shell** — `scripts/ui-v2/comparar.sh Shell`: si `SidebarAdmin_*` u `SidebarOperador_*` dan rojo solo por el pulgar (D20 acorta el sidebar), re-medir el pulgar (alto y top del indicador de Fluent en el render, como la Fase 2 Task 9) y ajustar SOLO `.v2-sidebar-pulgar` del mockup del Admin; anotar antes → después. Q-SIDEBAR: este valor vuelve a medirse en la Task 12 si la Fase 6 integra primero o después.
- [ ] **Step 4: Verde** — build y la suite COMPLETA (`dotnet test StockApp.sln --blame-hang-timeout 5m`): `Guardian_CubreTodasLasVistasDelEnsamblado`, `ViewLocatorTests`, `ReflexionVistaViewModelTests` y `ComposicionDIApiTests` verdes.
- [ ] **Step 5: Mutaciones** — 6a quitar `|| PuedeGestionarMaestrosFinanzas` del gate de Catálogos → rojo `OperadorConSoloGestionarMaestrosFinanzas_…`; 6b quitar `[typeof(CatalogosViewModel)] = "Catalogos"` → rojo `NavCatalogos_…MarcaLaSeccion` o el de sección conservada; 6c quitar `OnPropertyChanged(nameof(PuedeGestionarCatalogos))` de `NotificarPermisos` → rojo `RevocarAmbos_EnCaliente_…`. Pegar.
- [ ] **Step 6: Commit** — `git add` por ruta (incluye los `git rm` de las 12 vistas y 2 VMs, el VM del shell, los tests adaptados y el mockup del shell si cambió) y `git commit -m "feat(ui-v2): sidebar con un solo Catálogos y baja de las pantallas de catálogo viejas"`

### Task 7: Formularios de catálogo v2

**Files:**
- Modify: las 10 `*FormView.axaml` de catálogo (completas) y sus `.axaml.cs` solo si hace falta
- Create: `tests/StockApp.Presentation.UiTests/V2/FormulariosCatalogoV2Tests.cs`, `tests/StockApp.Presentation.VisualTests/Pantallas/Administracion/FormulariosCatalogoComparacionTests.cs`
- Modify (convención 13): `GuardianDePatronTests.cs` (las 10 formularios a `VistasMigradasAV2`, fuera de las listas v1), `CatalogoFormAnchoYCancelarTests.cs` (helpers a solo v2: todas sus vistas quedan v2), `EtiquetadoDeCamposTests.LineaPoaFormView_FilaDeAsignaciones_TieneEncabezadoDeColumnas` (solo si su búsqueda de `Classes="caption"` deja de aplicar: se adapta para aceptar el `TextBlock` v2)

**Interfaces:**
- Consumes: Task 2 (`CancelarCommand` vuelve a Catálogos); fakes de la Task 5.
- Produces: en cada formulario `x:Name` `Encabezado`, `Tarjeta`, `AlertaError`, `BotonCancelar`, `BotonGuardar`, `EstadoSinPermiso` (Fuente, Rubro, Línea POA).

- [ ] **Step 1: Tests (rojo)** — teoría sobre los 10 (`[AvaloniaTheory]` con `MemberData` de fábricas `(Func<Control>, string origenBreadcrumb, ClaveCatalogo)`):

```csharp
Raiz_TieneUi2_BreadcrumbConElCatalogoYElTituloDelVm(…)        // BreadcrumbOrigen == D6.Titulo, BreadcrumbActual == vm.Titulo, en alta y en edición
Estructura_CardDe760_GuardarEsElUnicoPrimario_CancelarSecundario(…)
AlertaError_SoloConMensajeError(…)
Breadcrumb_VuelveACatalogosConElCatalogoDelFormulario(…)       // Review Focus 3: click real en el tramo
Asteriscos_SoloEnLoQueExigePuedeGuardar(…)                    // compara con PuedeGuardar vaciando cada campo
```

y específicos: `LineaPoa_DosSecciones_YFilasDeAsignacion`, `LineaPoa_QuitarAsignacion_EsGhostIcono`, `Fuente_SinPermiso_ReemplazaLaCard`, `Proveedor_GrillaDeCampos_TelefonoYEmailEnUnaFila`. Rojo: fallan sobre las vistas v1.

- [ ] **Step 2: Implementar** las 10 vistas según D10-D12 y los 5 mockups (los otros 5 copian EXACTAMENTE la estructura de `CategoriaFormView`, cambiando solo los bindings y placeholders: la teoría lo exige).
- [ ] **Step 3: Comparaciones** — 5 formularios × 2 tamaños (10 tests). `scripts/ui-v2/comparar.sh FormularioCatalogo`. Expected: aprobadas o juntadas para el GATE 2.
- [ ] **Step 4: Verde** — build y suites completas de `Presentation.UiTests` y `VisualTests`; `GuardarBloqueadoConErroresDeEntradaTests` y `NumericUpDownVistasRealesTests` verdes.
- [ ] **Step 5: Mutaciones** — 7a quitar `BreadcrumbComando` de `ZonaFormView` → rojo `Breadcrumb_Vuelve…` para Zonas; 7b quitar `Classes="primario"` de Guardar en `ProveedorFormView` → rojo `Estructura_…` y `ProveedorEditar_IgualAlMockup`; 7c quitar el `IsVisible` de `AlertaError` en `UnidadMedidaFormView` → rojo `AlertaError_SoloConMensajeError` (Unidades); 7d quitar `IsVisible="{Binding !SinPermiso}"` de la card de `FuenteFinanciamientoFormView` → rojo `Fuente_SinPermiso_…`. Pegar.
- [ ] **Step 6: Commit** — `git commit -m "feat(ui-v2): formularios de catálogo en v2"` (con `git add` por ruta).

### Task 8: Usuarios v2

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Administracion/UsuariosAdminViewModel.cs` (`AvisaSinConexionEnLinea`, `AlPerderConexion`), `src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml` (completa)
- Create: `tests/StockApp.Presentation.UiTests/V2/UsuariosAdminViewV2Tests.cs`, `tests/StockApp.Presentation.VisualTests/Pantallas/Administracion/UsuariosComparacionTests.cs`
- Modify (convención 13): `UsuariosAdminPermisosScrollTests.cs` (el test se REEMPLAZA por el de Review Focus 5 en el mismo archivo), `EtiquetadoDeCamposTests.UsuariosAdminView_*` (aceptan `v2:CampoFormulario`), `GuardianDePatronTests.cs` (`UsuariosAdminView`: de la `InlineData` L75 y la lista L120 a `VistasMigradasAV2`), `tests/StockApp.Presentation.Tests/ViewModels/Administracion/UsuariosAdminViewModelTests.cs` (solo agrega)

**Interfaces:**
- Produces: `x:Name` `Encabezado`, `CardNuevoUsuario`, `BotonCrear`, `AlertaAlta`, `GrillaUsuarios`, `DetalleVacio`, `Detalle`, `TituloUsuario`, `InsigniaRol`, `InsigniaInactivo`, `InsigniaSoloLectura`, `BotonHacerAdmin`, `BotonHacerOperador`, `BotonBaja`, `CampoNuevaContrasena`, `BotonCambiarContrasena`, `AlertaAccesoTotal`, `ScrollPermisos`, `PiePermisos`, `AlertaPermisos`, `BotonGuardarPermisos`, `EstadoSinConexion`, `EstadoSinPermiso`.

- [ ] **Step 1: Tests (rojo)**

VM (`UsuariosAdminViewModelTests`): `Cargar_SinServidor_MarcaSinConexionYVaciaItems`, `Reintentar_TrasSinConexion_CargaLosUsuarios`.

View (`UsuariosAdminViewV2Tests`, Admin dentro de `ShellMainView`; la pantalla es Admin-only):

```csharp
Raiz_TieneUi2_EncabezadoUsuarios_GuardarPermisosEsElUnicoPrimario()
SinSeleccion_MuestraElVacioYNoElDetalle()
OperadorSeleccionado_DetalleConRolAccionesYPermisosHabilitados()
AdminSeleccionado_AccesoTotalYCasillasDeshabilitadas_HacerOperadorVisible()
InactivoSeleccionado_SoloLecturaYAccionesDeshabilitadas()
Alta_ConError_MuestraLaAlertaEnLaCardDeAlta()
Grilla_FilaInactiva_InsigniaYTextoTerciario()
SinConexion_EstadoConReintentar()
SinPermiso_ReemplazaElContenido()
GuardarPermisos_A1366x768_SeAlcanzaSinScrollYNoTapaElUltimoPermiso()   // Review Focus 5: ventana 1366x700; el botón dentro del área visible SIN scrollear; ScrollToEnd y el último CheckBox queda por encima del borde superior del pie
```

- [ ] **Step 2: Rojo**; pegar.
- [ ] **Step 3: Implementar** VM (dos overrides) y vista según D13-D15 y los mockups; el `IsEnabled` del panel sigue en `PuedeEditar` (bugfix 2026-08-16, comentario de hoy) aplicado al contenedor de las casillas, no a la card; `PasswordChar` en las dos contraseñas.
- [ ] **Step 4: Comparaciones** — 5 × 2 (10 tests): `scripts/ui-v2/comparar.sh Usuarios`.
- [ ] **Step 5: Verde** — suites completas de `Presentation.Tests`, `Presentation.UiTests`, `VisualTests`; `PermisoAdvertenciaContrasteTests` verde.
- [ ] **Step 6: Mutaciones** — 8a quitar la `RowDefinition` `Auto` del pie y el `Grid.Row` de `PiePermisos` (el pie se superpone al scroll en la misma fila; solo se quitan atributos) → rojo `GuardarPermisos_A1366x768_…`; 8b quitar `IsEnabled="{Binding PuedeEditar}"` → rojo `AdminSeleccionado_…` e `InactivoSeleccionado_…`; 8c quitar el `IsVisible` de `DetalleVacio` → rojo `SinSeleccion_…`; 8d quitar el override `AvisaSinConexionEnLinea` → rojo `Cargar_SinServidor_…`. Pegar.
- [ ] **Step 7: Commit** — `git commit -m "feat(ui-v2): usuarios con panel de permisos de pie fijo"`.

### Task 9: Mantenimiento v2

**Files:**
- Modify: `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml` (completa; code-behind igual)
- Create: `tests/StockApp.Presentation.UiTests/V2/MantenimientoViewV2Tests.cs`, `tests/StockApp.Presentation.VisualTests/Pantallas/Administracion/MantenimientoComparacionTests.cs`
- Modify (convención 13): `MantenimientoViewTests.cs` (los 4 asserts `TranslatePoint`, spec §8.2, y `Montar_LaSeccionDeAlertasQuedaDebajoDeLaDeDiagnostico` se reemplazan por `DiagnosticoYAlertas_LadoALado_DebajoDeBackups`; los que buscan el `ItemsControl` pasan a la grilla), `AccesoLimitadoViewTests.cs` (solo agrega), `GuardianDePatronTests.cs` (`MantenimientoView`: L76 y L121 → `VistasMigradasAV2`)

**Interfaces:**
- Consumes: `MantenimientoViewModel` intacto (`Corridas`, `IniciandoBackup`, `IniciarBackupCommand`, `DescargarCommand`, `CancelarCommand`, `MostrarListaVacia`, `ErrorAlCargar`, `TextoResumenLogs`, `HayLogs`, `DescargandoLogs`, `DescargarLogsCommand`, `UrlWebhook`, `AlertasHabilitadas`, `ErrorAlCargarAlertas`, `GuardarAlertasCommand`, `ProbarAlertasCommand`, `PuedeGuardarAlertas`, `PuedeProbarAlertas`, `Cargando`).
- Produces: `x:Name` `Encabezado`, `BotonBackup`, `BotonIniciando`, `CardBackups`, `GrillaBackups`, `ColumnaAccion`, `IndicadorCarga`, `VacioBackups`, `AlertaErrorBackups`, `CardDiagnostico`, `BotonDescargarLogs`, `CardAlertas`, `AlertaErrorAlertas`, `BotonGuardarAlertas`, `BotonProbarAlertas`.

- [ ] **Step 1: Tests (rojo)**

```csharp
Raiz_TieneUi2_HacerBackupEsElUnicoPrimario()
Iniciando_ReemplazaElBotonPorIniciandoDeshabilitado()
Grilla_ResultadoConInsigniaPorTono_ExitosaExitoFallidaPeligro()
Grilla_Detalle_FalloEnPeligro_NotaEnSecundario()
Grilla_Descargar_DeshabilitadoSinArchivo_CancelarMientrasDescarga()      // reemplaza a los heredados equivalentes del ItemsControl
ConMuchasCorridas_LaUltimaFilaSeAlcanzaScrolleandoLaGrilla()             // reemplaza Montar_ConMuchasCorridas_ElScrollViewerHaceAlcanzableLaUltimaFila
Vacio_YErrorDeCarga_SonExcluyentes()
DiagnosticoYAlertas_LadoALado_DebajoDeBackups()                          // reemplaza los 4 TranslatePoint (spec §8.2)
ErrorDeAlertas_AlertaYGuardarProbarDeshabilitados()
AccesoLimitado_HospedaMantenimientoV2_ConLaRaizUi2YLaGrillaCargada()     // en AccesoLimitadoViewTests (Review: D19)
```

- [ ] **Step 2: Rojo**; pegar.
- [ ] **Step 3: Implementar** según D16-D18 y los mockups. La columna de acción: botón en la celda con el padding de celda de la grilla v2 (memoria "Botón invisible en celda de DataGrid": se verifica que el texto del botón se vea entero; el guardián de recorte de la comparación lo exige).
- [ ] **Step 4: Comparaciones** — 3 × 2 (6 tests): `scripts/ui-v2/comparar.sh Mantenimiento`.
- [ ] **Step 5: Verde** — suites completas de `Presentation.UiTests` y `VisualTests`.
- [ ] **Step 6: Mutaciones** — 9a quitar `IsVisible="{Binding !IniciandoBackup}"` de `BotonBackup` → rojo `Iniciando_…`; 9b quitar `IsEnabled` del Descargar de la celda → rojo `Grilla_Descargar_…`; 9c quitar el `IsVisible` de `VacioBackups` → rojo `Vacio_YErrorDeCarga_…`; 9d quitar `Classes="ui2"` de la raíz → rojo `AccesoLimitado_HospedaMantenimientoV2_…` y `Raiz_TieneUi2_…`. Pegar.
- [ ] **Step 7: Commit** — `git commit -m "feat(ui-v2): mantenimiento en v2"`.

---

## Bloque D — Guardianes, verificación orgánica y cierre

### Task 10: Repaso de guardianes y mutaciones; suite y comparación completas

**Files:** Modify (local): el ledger; Modify: los tests que haga falta ajustar (cada ajuste con su rojo y su motivo).

- [ ] **Step 1: Guardianes** — confirmar con `grep` y la CLI `codegraph`, y anotar:
  1. `CatalogosView`, `CatalogoListadoView`, los 10 formularios, `UsuariosAdminView` y `MantenimientoView` están en `VistasMigradasAV2` y en ninguna otra lista; `Guardian_CubreTodasLasVistasDelEnsamblado` verde.
  2. `VistaV2_NoUsaPiezasDelTemaViejo` y `VistaV2_NoTieneOpacidadesLiterales` verdes sobre todas.
  3. `grep -rn 'Opacity\|Classes="primary"\|Classes="secondary"\|Classes="danger"\|Classes="caption"\|Classes="card"\|Classes="micro"\|<c:\|HeaderVista\|MargenVista\|DangerBrush\|ActivoOpacidadConverter\|BadgeEstado\|TabControl' src/StockApp.Presentation/Views/Administracion/ src/StockApp.Presentation/Views/Catalogo/*Form*.axaml src/StockApp.Presentation/Views/Catalogo/CatalogoListadoView.axaml src/StockApp.Presentation/Views/Finanzas/{FuenteFinanciamiento,RubroGasto,LineaPoa}FormView.axaml` vacío (salvo `ProductoFormView`, que no es de esta fase).
  4. `grep -rn "CatalogosTareaViewModel\|MaestrosFinanzasViewModel\|NavCategoriasCommand\|NavMaestrosFinanzasCommand\|CategoriaListView\b" src/ tests/` vacío.
  5. `git diff <BASE>..HEAD --stat -- src/StockApp.Presentation/Themes src/StockApp.Presentation/Controls src/StockApp.Presentation/Behaviors src/StockApp.Presentation/App.axaml docs/ui-v2/tokens.json docs/ui-v2/mockups/_base` VACÍO (tema compartido intacto).
  6. `git diff <BASE>..HEAD --stat -- src/StockApp.Api src/StockApp.Domain src/StockApp.Application src/StockApp.Infrastructure` VACÍO.
  7. `git diff <BASE>..HEAD -- src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs src/StockApp.Presentation/ViewModels/Administracion/PanelPermisosViewModel.cs` VACÍO.
  8. `ComandosDeGuardadoGuardianTests` y `CargaProtegidaSinPermisoGuardianTests` verdes.
- [ ] **Step 2: Repaso de mutaciones** — en el ledger tienen que estar PEGADOS los rojos de 1a-1f, 2a-2e, 3a, 5a-5f, 6a-6c, 7a-7d, 8a-8d, 9a-9d. Una sin rojo pegado se corre ahora.
- [ ] **Step 3: Build, suite completa y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; verde salvo los falsos conocidos (por nombre); total ≥ línea base + nuevos − reemplazados (anotar número y CADA par heredado → reemplazo); `--verificar` exit 0; TODAS las filas de `index.html` aprobadas a los dos tamaños (las de fases anteriores sin cambios; las del shell con el pulgar de la Task 6).

- [ ] **Step 4: Commit (solo si hubo ajustes)** — `git commit -m "test(ui-v2): guardianes de la Fase 7 al día"`.

### Task 11: Verificación orgánica (GATE 2 con el usuario), docs de cierre y review final

**Files:** Modify: `docs/ui-v2/herramientas.md` (7-A a 7-C), la spec (Estado, §7, deuda, §6.5); Modify (local): el ledger.

- [ ] **Step 1: Verificación orgánica (§6.4) — la ejecuta un agente con la app real contra la base local, en WSLg a 1280×760 y 1366×768, en el turno de la PC que asigne el orquestador**

Preparación: `docker start stockapp-pg`; **backup ANTES de crear datos** (`docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-fase7-$(date +%Y%m%d-%H%M%S).sql`, `ls -l` no vacío, ruta al ledger); API del worktree de la fase (`ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http`, puerto contra `conexion.json`); app (`dotnet run --project src/StockApp.Presentation`); `admin` / `Admin1234`. Operadores MIXTOS creados por la pantalla de Usuarios (así se verifica el alta): `f7tablas` (`catalogo.maestras`) y `f7finanzas` (`finanzas.maestros`); anotar ids. Datos con prefijo `PRUEBA-7`. Toolkit `scripts/gui-verificacion/`, md5 de cada captura, popups con `capturar-x11.py`.

Pasos (capturar cada pantalla; 1, 4, 6 y 7 se repiten a 1366×768):
1. **Catálogos (Admin):** sidebar con "Catálogos", "Usuarios", "Mantenimiento" (y sin los 5 viejos); master con los tres grupos; recorrer los 10 catálogos; ordenar Proveedores por EMAIL; tooltip de una dirección cortada; buscar con y sin tildes; búsqueda sin coincidencias. Comparar a ojo con los mockups.
2. **Alta, edición y baja** en Categorías, Proveedores (los 5 campos), Unidades, Rubros (código), Líneas POA (dos asignaciones) y Zonas, con prefijo `PRUEBA-7`: tras Guardar y tras Cancelar vuelve a Catálogos con ESE catálogo abierto (Review Focus 3); el breadcrumb también; un nombre duplicado muestra la `Alerta`; doble click en una fila activa edita y en una inactiva no; dar de baja con la búsqueda puesta (Review Focus 4); la baja de una categoría con productos activos muestra el aviso (regla de `ee461b8`).
3. **Operadores mixtos:** `f7tablas` ve INVENTARIO y TAREAS, no FINANZAS ni Usuarios ni Mantenimiento; `f7finanzas` solo FINANZAS. Con `f7tablas` y `f7finanzas` sumados en un usuario y "Líneas POA" abierta, quitarle `finanzas.maestros` desde la sesión de Admin y hacer una acción → un solo Aviso, FINANZAS desaparece y se abre Categorías (Review Focus 2); quitarle todo → "Sin permiso" y el ítem del sidebar desaparece.
4. **Usuarios:** alta (error de usuario duplicado y alta correcta), grilla con roles y un inactivo; seleccionar operador → permisos; tildar y "Guardar permisos" a 1366×768 SIN scrollear (Review Focus 5); seleccionar Admin → "Acceso total"; cambiar contraseña de otro (confirmación) y de uno mismo (el aviso); Hacer Admin / Hacer Operador; dar de baja → "Solo lectura".
5. **Sin servidor:** `kill` SOLO del pid de `StockApp.Api` → en Catálogos, cambiar de catálogo → "No se pudo conectar" con Reintentar dentro de la card, sin modal; levantar y Reintentar. En Usuarios, lo mismo.
6. **Mantenimiento:** grilla de backups con insignias; "Hacer backup ahora" (si la API local lo soporta; si no, anotar por qué) → "Iniciando…"; Descargar un backup (selector GTK con ruta absoluta) y Cancelar una descarga; Descargar logs; alertas: Guardar con una URL de prueba y Probar (anotar la respuesta).
7. **Acceso limitado:** si el entorno permite simular la licencia vencida (sin tocar la clave privada ni el VPS), verificar que la pantalla hospeda Mantenimiento v2; si no, anotar que lo custodia el UiTest de la Task 9.

Si un paso no se puede hacer por el entorno, PARAR y avisar con evidencia, sin declarar la fase cerrada. La limpieza de los datos `PRUEBA-7` y de `f7tablas`/`f7finanzas` la decide el usuario (listar ids y PREGUNTAR); nunca contra el VPS.

- [ ] **Step 2: GATE 2 — aprobación final del usuario (PARADA)**

PARAR. Presentar: `index.html` (las filas de la fase y las del shell), las capturas orgánicas junto a los mockups, los rojos de las mutaciones, los tests heredados adaptados o reemplazados (pares), los `UmbralAprobado` que hagan falta (revisión tesela por tesela en este mismo GATE), los cambios respecto de los mockups aprobados, las necesidades de tema compartido que hayan aparecido y la pregunta de la limpieza de datos. No seguir sin "aprobado". Registrar `Fase 7: aprobada por <quién> <fecha>`. Lo que el usuario observe se arregla en commits propios con TDD o va a la deuda con su OK.

- [ ] **Step 3: `herramientas.md` y spec**

`herramientas.md`, después de la 36: "Convenciones que agrega la Fase 7 (Administración)" con 7-A, 7-B y 7-C (numeración provisional, Q-CONV). Spec: (1) `**Estado:**` con "7" y "Fase 7 (Administración) APROBADA por el usuario el <fecha>"; la fila 7 de la tabla de §7; (2) "**Estado de la Fase 7 (aprobada el <fecha>).**" con el resumen (Catálogos master-detail con listado genérico y gates por grupo; formularios v2 que vuelven a su catálogo; Usuarios con pie fijo; Mantenimiento en grilla; sidebar consolidado; 12 vistas y 2 VMs borrados; comparaciones a los dos tamaños; suite <total> en `<hash>`; verificación orgánica; Q-* elegidas); (3) §6.5 con cada compensación nueva medida; (4) "**Deuda abierta tras la Fase 7:**" con, como mínimo: CÓDIGO del rubro ordena como texto (D4), formularios de catálogo sin errores por campo (convención 31 fuera de alcance), los `UmbralAprobado` si hubo, los minors diferidos del ledger, los datos de prueba según lo decidido, y tachar en "Deuda abierta tras la Fase 2" el "pulgar del sidebar a re-medir en las Fases 6 y 7" solo si la Task 12 lo cerró.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 7 (administración)"
```

- [ ] **Step 4: Review final de la rama** — review de `<BASE>..HEAD` con el modelo más capaz, con la suite y las comparaciones verdes. Foco: los cinco puntos de "Review Focus"; que la excepción a "VMs intactos" no exceda la lista de Global Constraints; que el tema compartido no se tocó (Task 10 Step 1.5); que ningún test heredado se borró sin reemplazo; que ninguna pieza del tema viejo quedó en las vistas de la fase; que los gates se prueban con operadores mixtos. Hallazgos: commits propios con TDD o deuda con el usuario. Sin push ni merge (eso es la Task 12).

---

## Bloque E — Integración

### Task 12: Integrar `feat/ui-v2-fase-7` en `feat/ui-v2`

**Precondición:** GATE 2 aprobado y review final cerrada. Coordinar con el orquestador el ORDEN respecto de las Fases 5 y 6 (una integración a la vez).

- [ ] **Step 1: Rebase sobre `feat/ui-v2`** — en el worktree de la fase: `git rebase feat/ui-v2`. Conflictos esperables (ver "Riesgos de conflicto"): resolver conservando las líneas de las DOS fases (nunca "ours"/"theirs" en bloque); cada archivo resuelto se anota en el ledger. Si la Fase 6 ya integró: el grupo ANÁLISIS es el de ella y ADMINISTRACIÓN el de esta; `SeccionPorTipo` y los `Nav*` de las dos; `GuardianDePatronTests` con las entradas de las dos; `herramientas.md` con las convenciones de las dos, RENUMERADAS en orden de integración (Q-CONV; actualizar las referencias "7-A/7-B/7-C" en la spec y en el propio `herramientas.md`).
- [ ] **Step 2: Sidebar y pulgar (Q-SIDEBAR)** — con las dos fases adentro, re-medir el pulgar del sidebar del Administrador (alto y top a 760 y 768) y ajustar SOLO `.v2-sidebar-pulgar` de `shell/sidebar-admin.html`; regenerar los PNG del shell; `scripts/ui-v2/comparar.sh Shell` aprobado. Anotar antes → después y cerrar la deuda de la Fase 2 en la spec.
- [ ] **Step 3: Verificación completa tras el rebase** — `dotnet build StockApp.sln`, `dotnet test StockApp.sln --blame-hang-timeout 5m`, `tokens --verificar`, `fuentes --verificar`, `scripts/ui-v2/comparar.sh` (todo aprobado). Si algo de otra fase se rompe, PARAR y reportar al orquestador (no se arregla código ajeno sin acuerdo).
- [ ] **Step 4: Fast-forward** — `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && git status --short` (limpio salvo `.codegraph/`) y `git merge --ff-only feat/ui-v2-fase-7`. Si no es fast-forward, volver al Step 1. Sin push (el merge a `main` es por bloques y lo decide el usuario).
- [ ] **Step 5: Cierre del worktree** — solo con el OK del usuario: `git worktree remove /home/capua25/workspace/stockapp-worktrees/ui-v2-fase7` y `git branch -d feat/ui-v2-fase-7` (`-d`, nunca `-D`).

---

## Necesidades de tema compartido (para el integrador)

La fase está planeada para NO necesitar ningún cambio de tema: todo se arma con `EncabezadoPagina`, `TarjetaContenido`, `BarraFiltros`, `CampoFormulario`, `Insignia`, `Alerta`, `EstadoVacio`, `EstadoCargando`, `ItemSidebar`, `GrillaUniformeV2`, el tema de `DataGrid` v2 y los estilos `.ui2` existentes. Necesidades CONDICIONALES (aparecen solo si el GATE 1 decide algo distinto de la R, o si la medición lo exige); si una aparece, la fase NO la implementa: se anota en el ledger y se le pasa al orquestador con esto:

| # | Cuándo aparece | Cambio exacto | Por qué |
|---|---|---|---|
| N1 | Si en el GATE 1 se prefieren los ítems del master SIN ícono (D2). | `Controls/V2/ComponentesV2.axaml`, `ControlTheme` de `v2:ItemSidebar`: `Style Selector="^[Icono=null] /template/ i|Icon#PART_Icono"` con `IsVisible=False` (o un `Margin` condicional), y su espejo en `base.css` (`.v2-item-sidebar--sin-icono`). | Hoy `PART_Icono` reserva 16 + 8 px aunque no haya ícono: el texto quedaría corrido 24 px. |
| N2 | Si la columna de acción de Mantenimiento (D17) corta "Descargar"/"Cancelar" con la fila de 40. | `Themes/V2/DataGridV2.axaml`: un selector `.ui2 DataGridCell.accion` con `Padding` vertical 2 (en lugar del de la celda de texto). | Memoria "Botón invisible en celda de DataGrid": el padding va en la celda, no en el botón; el precedente del Calendario (convención 28) se midió con un botón; si con dos estados no entra, hace falta el estilo. |
| N3 | Si el usuario quiere el texto del `CheckBox` de permisos envuelto en dos líneas (etiquetas largas en 1366). | `Themes/V2/ControlesV2.axaml`: `.ui2 CheckBox /template/ ContentPresenter` con `TextWrapping=Wrap` heredable. | El `CheckBox` v2 no envuelve hoy. |
| N4 | Siempre (no es tema, es documentación compartida). | Renumerar las convenciones 7-A a 7-C en `docs/ui-v2/herramientas.md` según el orden de integración de las Fases 5, 6 y 7 (Q-CONV). | Las tres fases agregan convenciones en paralelo. |

## Riesgos de conflicto con las Fases 5 y 6

| Archivo / recurso | Quién más lo toca | Qué toca esta fase | Mitigación |
|---|---|---|---|
| `ViewModels/ShellMainViewModel.cs` | Fase 6 (grupo ANÁLISIS → hub de Reportes, `Nav*` de reportes, `SeccionPorTipo`). | Solo el grupo ADMINISTRACIÓN, 5 `Nav*` de catálogos y 5 entradas de `SeccionPorTipo`. | Bloques distintos del mismo archivo; resolver en la Task 12 conservando los dos; el comentario de L248-249 menciona a las dos fases: cada una edita su mitad. |
| `docs/ui-v2/mockups/shell/sidebar-*.html`, `ShellComparacionTests`, pulgar del sidebar | Fase 6 (ANÁLISIS). | Bloque ADMINISTRACIÓN. | Q-SIDEBAR: el pulgar se re-mide con las dos fases adentro (Task 12 Step 2). |
| `ShellMainViewGatesTests.cs`, `ShellMainViewModelTests.cs`, `ShellMainFinanzasTests.cs` | Fase 6 (gates de reportes); posiblemente Fase 5. | Tests de catálogos y de ADMINISTRACIÓN. | Solo métodos propios; ningún helper compartido se cambia. |
| `GuardianDePatronTests.cs` (`InlineData`, `VistasDeLaTanda`, `VistasMigradasAV2`) | Fases 5 y 6 (sus vistas pasan a `VistasMigradasAV2`). | Las líneas de las vistas de la fase (y la baja de 12). | Editar línea por línea; en el rebase conservar las entradas de las tres. |
| `App.axaml.cs` (DI) | Fase 6 (hub de Reportes); quizás Fase 5. | L378 y L381. | Q-APPCS; regiones distintas. |
| `GrillasSinRecorteBarridoTests.cs`, `CargaProtegidaEstadoVacioUiTests.cs`, `EtiquetadoDeCamposTests.cs` | Fase 6 (escenario y test de Stock por categoría, adyacentes: L74 y L129); Fase 5 (Tareas). | Escenarios de catálogos (L93-102), test de Fuentes (L101), tests de Usuarios. | Tocar solo esas líneas. |
| `tests/StockApp.Presentation.UiTests/TareaFakes.cs` (hoy tiene los fakes de Zonas, Dimensiones, Organismos y Orígenes) | Fase 5 (Tareas). | NADA: los fakes de la fase van en `V2/AdministracionFakes.cs`. `CatalogoFormAnchoYCancelarTests` los sigue usando sin cambiarlos. | Si la Fase 5 cambia esos fakes, el rebase lo muestra en `CatalogoFormAnchoYCancelarTests`. |
| `docs/ui-v2/herramientas.md` (convenciones 37+), spec (Estado, §7, deuda, §6.5) | Fases 5 y 6. | Su sección propia. | Q-CONV; en la Task 12 se renumera y se fusionan las líneas de Estado. |
| Base de datos local `stockapp-pg` y la PC del usuario (GATE 2) | Fases 5 y 6 (sus GATE 2). | Datos `PRUEBA-7`, usuarios `f7*`. | Turnos de PC con el orquestador; backup propio antes; prefijos distintos por fase; nunca se borra dato de otra fase. |
| `artifacts/ui-v2/comparacion/` | Fases 5 y 6 (en sus worktrees: carpetas distintas). | — | Cada worktree tiene su `artifacts/`; no hay choque salvo en el worktree `ui-v2` tras integrar. |

## Fuera de alcance

- Errores por campo en los formularios de catálogo (convención 31), "Guardar y cargar otro", reactivar un registro dado de baja.
- Filtros por estado en los catálogos (solo búsqueda), exportar catálogos a PDF.
- Alta de usuario en modal (Q-USU-ALTA), reactivar usuarios, cambio de contraseña propia desde esta pantalla.
- Cambios de dominio, DTOs, servicios de Application y API.
- Cambios al tema compartido (ver "Necesidades de tema compartido").
- Merge a `main` y push; modo oscuro; back-stack (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| La base cambia sin querer el comportamiento de un catálogo. | API pública conservada; los tests de los 10 VMs no se tocan en la Task 1 (`git diff --stat` en el Step 4); mutación 1f. |
| La grilla genérica pierde la red del compilador. | XAML fijo con bindings compilados a `FilaCatalogo`; solo `Header`/`IsVisible` desde el code-behind (7-B); tests de columnas por catálogo. |
| Doble consulta al abrir Catálogos (vista genérica y host cargan). | Solo carga `CatalogosViewModel`; `Cargar_SinPendiente_…` exige UNA llamada al servicio. |
| Revocación en caliente deja un detalle sin permiso. | 7-C; tests de VM y de la View real dentro del shell (Review Focus 2). |
| Borrar 12 vistas deja tests sin reemplazo. | Pares heredado → reemplazo nombrados en la Task 6 y en el ledger; Task 10 Step 3. |
| "Guardar permisos" vuelve a quedar inalcanzable (regresión de `c145c11`). | Pie fijo y test más estricto a 1366×700 (Review Focus 5), mutación 8a. |
| Conflictos con las Fases 5 y 6. | Tabla de arriba; integración de a una con rebase y suite completa (Task 12). |
| El pulgar del sidebar no coincide tras integrar las dos fases. | Q-SIDEBAR; re-medición en la Task 12 Step 2. |
| WSLg se congela o `capturar.sh` captura el escritorio de Windows con exit 0. | md5 y mirar cada captura; si se congela, PARAR y pedir el reinicio. |
| La verificación orgánica ensucia la base local o choca con otra fase. | Backup antes; prefijo `PRUEBA-7`; ids anotados; limpieza decidida por el usuario; turnos de PC. |
