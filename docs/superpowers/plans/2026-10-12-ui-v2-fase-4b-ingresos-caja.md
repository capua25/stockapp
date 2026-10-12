# Fase 4b: Ingresos de caja y su formulario. Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar a la UI v2 el listado de Ingresos de caja (`IngresosView`) y su formulario de alta y edición (`IngresoFormView`), con los estados de pantalla de las convenciones 18, 22 y 23 y las excepciones E1 a E5 de la spec, comparados contra mockups aprobados a 1280×760 y 1366×768.

**Architecture:** Cuatro bloques, como la 4a. (A) Los ViewModels primero, sin tocar vistas: `IngresosViewModel` carga con `EjecutarCargaProtegidaAsync` + `ConsultaVigente` y opta por `SinConexion` (E1), filtra en el cliente por concepto y por Activos/Todos (E2) y expone el pie con el total de los activos visibles (E3); `IngresoFormViewModel` muestra el 403 de la inicialización en vez de tragarlo (E4) y gana validación por campo con estado "tocado" vía `INotifyDataErrorInfo` (E5); `FechaCortaConverter` acepta además la fecha calendario `DateTime` de un ingreso. (B) Datos fijos, medición y 12 mockups (11 del plan + el 403 del formulario agregado en el GATE 1), con GATE 1 del usuario antes de todo XAML. (C) Las dos vistas v2 con sus UiTests (gates con un Operador de permisos MIXTOS y el 403 en caliente por el camino real del shell) y sus comparaciones. (D) Repaso de guardianes y mutaciones, verificación orgánica con GATE 2 y docs de cierre.

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, CommunityToolkit.Mvvm, Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, bloque "Diseño de la Fase 4b (aprobado por el usuario el 2026-10-12)" (L583-622 en `68085ac`: excepciones E1 a E5; sección 1, listado; sección 2, formulario; sección 3, tests, verificación y deuda), §5.1 y §5.2 (L204-212) y, como contexto, el bloque de la 4a y su "Estado de la Fase 4a". Convenciones 1 a 29: `docs/ui-v2/herramientas.md` (en especial 10, 11, 12, 13, 14, 18, 19, 20, 21, 22, 23 y 25). Molde de formato: `docs/superpowers/plans/2026-10-11-ui-v2-fase-4a-adjuntos-calendario-poa.md`.

Todos los comandos se corren desde la raíz del worktree, con `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <comando>` (rama `feat/ui-v2`, base: el commit de este plan). Ese worktree tiene su propio índice `.codegraph/`: usar CodeGraph MCP (`codegraph_explore`) ANTES de grep/Read para todo lo que sea código C#; nunca el índice de otro checkout. CodeGraph NO indexa `.axaml`, markdown ni HTML/CSS: para eso, Read directo. Al escribir este plan CodeGraph marcó como "changed on disk" (sin fuente) `IngresoFormViewModel.cs`, `IngresosViewModel.cs`, `ViewModelBase.cs`, `GastoFormViewModel.cs` y otros: si vuelve a pasar, leerlos con Read y decirlo en el reporte.

## Decisiones del usuario (2026-10-12, vinculantes)

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | **Alcance de la 4b:** `IngresosView` (listado) e `IngresoFormView` (alta y edición) en v2, conservando Nuevo, Editar, Dar de baja, el gate `PuedeRegistrarIngresos` (oculta con `IsVisible`, "Solo lectura" sin el permiso) y el acceso con `finanzas.ver`. Sin cambios de API ni de datos; `IngresoCajaService` no cambia. | Fuera de alcance: API, dominio, Libro caja (4c), Gastos (4d), Pagos (4e). | todas |
| U2 | **Excepciones a "VMs intactos" E1 a E5** (spec). E1: carga protegida del listado con `Cargando`, `SinConexion` con Reintentar y `SinPermiso`. E2: filtro en el cliente (`TextoBusqueda` en Concepto, `MostrarInactivos` con combo Activos / Todos que arranca en Activos). E3: `CantidadVisible`, `CantidadDadosDeBaja` y `TotalActivos` (el total suma SOLO los activos visibles). E4: un 403 en `IngresoFormViewModel.InicializarAsync` deja de tragarse: Alerta y Guardar deshabilitado. E5: error y estado "tocado" por Fecha, Monto, Concepto y Fuente. | E1: Task 1. E2, E3: Task 2. E4: Task 3. E5: Task 4. Ningún otro cambio de VM. | 1 a 4 |
| U3 | **Filtro A del listado:** buscador por concepto + combo Activos / Todos; el total solo de los activos. **Acciones en el encabezado sobre la fila seleccionada** (patrón de Productos v2) y **doble click edita**. **Estados completos.** **Formulario con el patrón `ProductoFormView` y errores por campo.** | D1 a D16 abajo, a confirmar en el GATE 1. | 6 a 8 |
| U4 | **Lo que la spec no fija lo propone el mockup y lo decide el usuario en el GATE 1** (criterio de 3a a 4a). | D1 a D16. Si el usuario cambia una, se ajustan mockups y los tests que la fijan en la Task 6, antes de las Tasks 7 y 8. | 6 |
| U5 | **Ejecución con subagent-driven-development** (convención del proyecto: los planes se ejecutan con subagentes sin preguntar el método). | Header del plan. | — |

## Decisiones que se confirman al aprobar los mockups (Task 6, GATE 1)

> **GATE 1 aprobado por Martin el 2026-10-12** (AprobadoPor: Martin, Fecha: 2026-10-12). D1-D3, D5-D12 y D15 como se presentaron; D4, D13, D14 y D16 como quedan abajo (marcadas "Aprobada 2026-10-12"). Mockups: 12 pantallas, 24 PNG (`ad11910` y el commit de cierre de la Task 6).

- **D1. Listado, encabezado:** `EncabezadoPagina` sin breadcrumb (pantalla del sidebar), `Titulo="Ingresos de caja"`, `Subtitulo="Partidas, multas, préstamos y demás entradas de la caja municipal."`. Acciones a la derecha, en este orden (el de Productos v2): "Editar" (`secundario`), "Dar de baja" (`secundario`, no `peligro`: la spec la pide secundaria) y "Nuevo ingreso" (`primario`, el único de la vista). Sin `finanzas.ingresos` las tres se ocultan con `IsVisible` y en su lugar aparece una `Insignia` `Neutro` "Solo lectura".
- **D2. Listado, filtros:** `BarraFiltros` con el buscador (`MarcaAguaBusqueda="Buscar por concepto…"`) y, en la ranura, un `ComboBox` SIN etiqueta de 140 de ancho con "Activos" y "Todos" (arranca en "Activos"). Sin botón "Buscar": filtra en memoria al tipear.
- **D3. Búsqueda:** "contiene" sobre el Concepto, sin distinguir mayúsculas ni tildes ("prestamo" encuentra "Préstamo BROU"), con los espacios de los extremos recortados, inmediata (sin debounce: no hay consulta al servidor).
- **D4. Listado, columnas:** FECHA (`Auto` medida, `NoCede`, "dd/MM/yyyy" con `FechaCortaConverter`, SIN conversión de zona horaria: la fecha de un ingreso es una fecha calendario guardada a medianoche UTC) · CONCEPTO (`*`, `MinWidth` 240, elipsis + tooltip solo si se corta, y en la fila dada de baja una `Insignia` `Neutro` "Inactivo" 8 px a la derecha del texto) · FUENTE (`Auto` medida con `ColumnaAuto`, elipsis + tooltip si se corta, "—" sin fuente) · MONTO (`Auto` medida, `num`, `MonedaConverter`). Fila inactiva: `Classes.inactivo` en cada `TextBlock` (convención 11, `Opacity` prohibido). **Desvío de la spec a confirmar:** la spec pide CONCEPTO con `ColumnaAuto`, pero la convención 19 excluye las columnas con insignia (`ColumnaAuto` mide el texto y no la insignia: el "Inactivo" quedaría cortado) y CONCEPTO es la única columna de texto libre largo que puede absorber el ancho sobrante. Se propone CONCEPTO `*` y FUENTE `ColumnaAuto`. **Aprobada 2026-10-12 (Martin):** CONCEPTO `*` y FUENTE `ColumnaAuto` SIN `MaxWidth` (medido: FECHA 90, FUENTE 123, MONTO 115; CONCEPTO 620 a 1280 y 706 a 1366, la grilla entra sin scroll con CONCEPTO en su piso). La insignia "Inactivo" mide 62 (no 78: el 78 es la columna ESTADO de Productos), 8 px después del texto.
- **D5. Orden:** por click en los cuatro encabezados (`CanUserSortColumns="True"` sobre `ItemsView`). Orden inicial: el del servidor (`IngresoCajaRepository.ListarTodosAsync`: fecha descendente y, a igual fecha, id descendente), sin flecha de orden dibujada al abrir. No se agrega una `SortDescription`: perdería el desempate por id. **Aprobada 2026-10-12 (Martin).**
- **D6. Pie:** "6 ingresos · Total $ 6.610.730,55"; con dados de baja visibles, "8 ingresos · 2 dados de baja · Total $ 6.610.730,55" (el tramo del medio solo si hay alguno). Singulares "1 ingreso" y "1 dado de baja". El total suma solo los activos visibles (con el filtro aplicado).
- **D7. Listado, estados:** dentro de la card y con los filtros a mano (patrón de Productos v2): `EstadoCargando` "Cargando ingresos…"; sin servidor, `EstadoVacio` de error con "Reintentar" (textos de la convención 18); vacío sin datos, `EstadoVacio` "Todavía no hay ingresos" / "Cuando se registre un ingreso, va a aparecer acá." con un botón "Nuevo ingreso" `secundario` (el primario sigue siendo el del encabezado) que solo se ve con `finanzas.ingresos`; vacío sin resultados, `EstadoVacio` "Ningún ingreso coincide con la búsqueda" / "Probá con otro concepto o elegí Todos." (también cuando "Activos" oculta una lista que solo tiene dados de baja). Tras un 403, `EstadoVacio` "Sin permiso" con "No tenés permiso para ver los ingresos de caja." en lugar de la card (sin mockup propio: el componente ya está comparado en Productos e Historial). **Aprobada 2026-10-12 (Martin):** el sin conexión dentro de la card con Reintentar sigue la convención 18, no una Alerta de la convención 22.
- **D8. Selección y filtro:** si un cambio de filtro oculta la fila seleccionada, la selección se limpia (Editar y Dar de baja quedan deshabilitados): nunca se edita ni se da de baja una fila que no se ve.
- **D9. Doble click:** sobre una FILA (no sobre el encabezado de la grilla) equivale a "Editar": solo con `finanzas.ingresos` y sobre una fila activa. Sin permiso o sobre una inactiva no hace nada.
- **D10. Dar de baja:** confirma con el modal v2 de `IConfirmacionService` y el texto de hoy: `¿Confirma dar de baja el ingreso "<concepto>" (<monto es-UY>)?`, con los botones por defecto ("Cancelar" / "Sí, continuar"; la convención 17 no aplica: todavía no se hizo nada).
- **D11. Formulario, estructura:** breadcrumb "Ingresos de caja › Nuevo ingreso" / "Ingresos de caja › Editar ingreso" (el primer tramo ejecuta `CancelarCommand`); card de 760 a la izquierda con padding 24 y un único bloque `subtitulo-formulario` "Datos del ingreso"; grilla `*,16,*`: fila 1 FECHA (`CalendarDatePicker`, "dd/mm/aaaa") | MONTO (`TextBox` alineado a la derecha, marca de agua "0,00"); fila 2 CONCEPTO a todo el ancho (marca de agua "Ej.: Partida mensual FIGM julio"); fila 3 FUENTE DE FINANCIAMIENTO en la columna izquierda (marca de agua "Elegí la fuente"). Los cuatro con `Requerido="True"` (asterisco). Pie con borde superior: "Cancelar" (`secundario`) y "Guardar" (`primario`, oculto sin `finanzas.ingresos`, como hoy).
- **D12. Formulario, errores por campo (E5):** textos "La fecha del ingreso es obligatoria.", "El monto es obligatorio.", "El monto no es un número válido." + el detalle es-UY de `FormatoEsUy` (ej.: "El monto no es un número válido. Usá coma para los decimales: 1,5"), "El concepto es obligatorio.", "Elegí la fuente de financiamiento.". Un campo queda "tocado" cuando cambia su valor o, en los `TextBox` Concepto y Monto, cuando pierde el foco; al intentar guardar se tocan los cuatro. Guardar queda deshabilitado mientras alguna regla del cliente falle (falte algo o el monto no sea un número); con un campo en rojo, el botón muestra el tooltip "Corregí los campos marcados en rojo para continuar." (lo pone el tema, `ErroresDeEntradaBehavior`).
- **D13. Formulario, 403 en la inicialización (E4):** **hallazgo que contradice la spec, a decidir:** la spec pide la Alerta "Ya no tenés permiso para registrar ingresos", pero `InicializarAsync` solo llama `GET /finanzas/fuentes/activas`, que exige `finanzas.ver` (`FuentesFinanciamientoEndpoints.cs` L44-46; `FuenteFinanciamientoService.ListarActivasAsync` L114), no `finanzas.ingresos`. El único 403 posible ahí es por haber perdido `finanzas.ver`, y perder solo `finanzas.ingresos` no produce ningún 403 en la inicialización (eso ya lo cubre el gate `PuedeRegistrarIngresos`, que oculta Guardar). Se propone el texto "Ya no tenés permiso para ver finanzas, así que no se puede registrar el ingreso." (constante `IngresoFormViewModel.MensajeSinPermisoFormulario`). Si el usuario prefiere el texto de la spec, se cambia la constante y el test que la fija (Task 3) antes de la Task 8. Presentación: `Alerta` `Error` arriba del pie (8 de margen superior), campos visibles, Guardar deshabilitado; no reemplaza la card (la spec pide la Alerta, no el estado "Sin permiso" de la convención 12). **Aprobada 2026-10-12 (Martin): texto A**, "Ya no tenés permiso para ver finanzas, así que no se puede registrar el ingreso." (la constante actual queda); tiene mockup propio, `ingreso-sin-permiso.html` (D16).
- **D14. Formulario, error del servidor:** `Alerta` `Error` arriba del pie con `MensajeError` (convención 12). Ejemplo del mockup: "El monto del ingreso debe ser mayor a cero." (lo lanza `IngresoCajaService.ValidarAsync`). **Aprobada 2026-10-12 (Martin) con corrección:** el mockup muestra el formulario COMPLETO (Fuente "FIGM Literal B") y Guardar HABILITADO: el servidor solo rechaza lo que el cliente dejó guardar (`DatosIngresos.ConErrorDelServidor` carga la fuente).
- **D15. Datos fijos** (Task 5): los de `DatosIngresos` (ocho ingresos, dos dados de baja, tres fuentes). Fecha del alta fija 06/10/2026.
- **D16. Mockups (a 1280×760 y 1366×768, 24 PNG; aprobada 2026-10-12 por Martin con un agregado):** `finanzas/ingresos.html`, `-cargando`, `-vacio`, `-sin-resultados`, `-sin-conexion`, `-solo-lectura`, `-con-inactivos`; `finanzas/ingreso-nuevo.html`, `ingreso-editar.html`, `ingreso-error-campo.html` (Concepto tocado y vacío + Monto "1.5"), `ingreso-error-servidor.html` e `ingreso-sin-permiso.html` (AGREGADO en el GATE 1: el 403 de la inicialización del formulario, Alerta con `MensajeSinPermisoFormulario` y Guardar deshabilitado; datos `DatosIngresos.SinPermisoAlInicializar()`). No se mockean tooltips, desplegables ni el "Sin permiso" del listado (componente compartido ya aprobado; lo custodian UiTests).

## Global Constraints

- Tema claro fijo; tokens de §2.1 sin valores nuevos: fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, separador `#F0EFED`, acento `#1D4ED8`, peligro texto `#B91C1C`, texto primario `#1C1917`, etiqueta `#44403C`, texto secundario `#57534E`, texto terciario `#78716C`. Fila de grilla 40, header de grilla 36, input y botón 36. Espaciado múltiplo de 4. Sidebar 240: la vista mide 1040 a 1280 y 1126 a 1366. Card de formulario 760 (`V2AnchoFormularioMax`).
- **`tokens.json` es la fuente de `Tokens.axaml`; nunca editar `Tokens.axaml` a mano.** Esta fase no prevé tokens nuevos. Toda task que toque `Themes/V2/`, `Controls/V2/` o `docs/ui-v2/` corre `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`.
- **Formato es-UY** (decisión del usuario, 2026-10-01; el cliente es un municipio uruguayo): importes con `MonedaConverter` / `FormatoEsUy.Moneda` ("$ 1.250.000,00"), cantidades con `FormatoEsUy.Cantidad`, monto tipeado con `FormatoEsUy.TryParseDecimal`, fechas "dd/MM/yyyy" con barras fijas (`FechaCortaConverter`). Nunca `CultureInfo.GetCultureInfo("es-UY")` (la API corre con `InvariantGlobalization` y tira; `FormatoEsUy` arma su `NumberFormatInfo` a mano).
- **`x:DataType` / compiled bindings:** un binding mal escrito es error de build, no un null silencioso; cada `DataTemplate` lleva su `x:DataType` (`e:IngresoCaja`, `e:FuenteFinanciamiento`). Una lista estática se bindea con `{x:Static}`, no con `{Binding}`.
- **Sin comentarios con `--` en `.axaml` ni `.csproj`** (AVLN1001, cascadea a MSB4025).
- **Un solo primario por vista** (§3.3): "Nuevo ingreso" en el listado (el del estado vacío es `secundario`); "Guardar" en el formulario.
- **VMs: solo E1 a E5.** `IngresosViewModel` (E1, E2, E3 y D8) e `IngresoFormViewModel` (E4, E5). Ningún otro VM. No cambian `IngresoCajaService`, los endpoints, `ViewLocator`, `INavigationService`, la navegación al formulario y de vuelta al listado, la carga de fuentes activas ni la regla de la fuente dada de baja en edición (spec, sección 2).
- **Gates de UI con la View real y gates de permisos con un Operador de permisos MIXTOS** (`new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas)`: `finanzas.ver` sí, `finanzas.ingresos` no), NUNCA con Admin (cortocircuita el OR). El 403 en caliente se prueba con el camino real: `ManejadorAccesoRevocado.ManejarAsync(...)` → `ShellMainViewModel.NotificarPermisosActualizados` con la vista dentro de `ShellMainView` (patrón de `CalendarioPagosViewV2Tests.Un403_QueQuitaPagos_OcultaLaColumnaSinNavegar`). Un test de VM NO custodia un `IsVisible` del XAML.
- **TDD en cada task:** test primero, correrlo y PEGAR el rojo real en el ledger, código mínimo, verde.
- **Mutaciones QUITANDO, nunca agregando:** `export SCRATCH=~/.cache/stockapp-ui-v2-fase-4b && mkdir -p "$SCRATCH"` (dentro del home, nunca `/tmp`); `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas (o una línea acortada, nunca un efecto nuevo); se corre el test y se PEGA el rojo; se restaura con `cp`; `git diff --no-index` vacío; verde. **NUNCA `git checkout --` ni `git restore`.**
- **Stubs fieles al contrato real** (confirmar con `src/StockApp.Application/Finanzas/IIngresoCajaService.cs`, `IFuenteFinanciamientoService.cs`, `src/StockApp.Domain/Entities/IngresoCaja.cs` y `src/StockApp.ApiClient/IngresoCajaApiClient.cs` antes de escribir uno): `IngresoCaja { int Id; DateTime Fecha (UTC, medianoche); string Concepto; int FuenteFinanciamientoId; FuenteFinanciamiento? FuenteFinanciamiento (null si el servidor no manda FuenteNombre); decimal Monto; bool Activo; Guid? IdImportacion }`; `IIngresoCajaService { AltaAsync(IngresoCaja) → int; ModificarAsync; BajaLogicaAsync(int); ListarTodosAsync() → IReadOnlyList<IngresoCaja> }`; `IFuenteFinanciamientoService.ListarActivasAsync()`. Los fakes lanzan lo que lanza `ApiErrores`: `UnauthorizedAccessException` para 401/403, `ServidorNoDisponibleException` sin servidor, `ArgumentException` / `ReglaDeNegocioException` para las reglas del servidor.
- Mockups (§6.1): `docs/ui-v2/mockups/finanzas/ingreso*.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a `DatosIngresos`. Solo colores sólidos, bordes de 1 px, radios, una `BoxShadow`, Inter, px enteros, múltiplos de 4. Celdas numéricas con `v2-num` y el valor dentro de un `<span>`. Error de campo con `.v2-input--error` + `.v2-error-campo` (ya en `base.css`, L164-180). Se aprueban ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los calibrados (`TeselasDensasPorDefecto = 11`, `PorcentajePorDefecto = 1.68`) a AMBOS tamaños. Uno mayor solo con `UmbralAprobado(TeselasDeTono, AprobadoPor: "Martin", Fecha: "yyyy-MM-dd", Motivo)` tras revisar CADA tesela densa con el usuario en un GATE; 16 o más exige además `MutacionPalidaEnRojo` en ESA pantalla. Nunca se sube para tapar un defecto; antialias contra diferencia real se diagnostica con `teselas.py` y `tinta.py` (plan de la 3a, "Herramientas de diagnóstico"; copiarlos a `$SCRATCH/diag/`, nunca se commitean).
- Convención 6: las pantallas se comparan DENTRO del shell real (`ShellMainView`, `DatosShell.Crear(usuario, permisos, "Ingresos", vm)`) con el sidebar oculto en los dos renders (`ProductosComparacionTests.OcultarSidebar`, `ocultarEnMockup: [".v2-sidebar"]`).
- Compensaciones conocidas Chromium/Avalonia (§6.5): Chromium colapsa márgenes verticales y Avalonia los suma (el `Margin="0,-16,0,0"` del estado sin permiso de `ProductoFormView`); texto centrado con ancho fraccionario pide `UseLayoutRounding="False"` (los `EstadoVacio` lo llevan por instancia); una columna `*` acotada por `MaxWidth` pide `UseLayoutRounding="False"` en su `Grid` (`ProductoFormView` L39-43). Una compensación nueva se mide, se anota con su antes y después y entra a §6.5 en la Task 10.
- **Build obligatorio** tras cada cambio de XAML o de un `.cs` de `Presentation`: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde). **Tests con `dotnet test StockApp.sln` o con UN `.csproj` por comando, nunca varios `.csproj` juntos.** Nunca dos `dotnet test` a la vez (comparten `obj/`: rojos falsos).
- **En este worktree los rojos de `StockApp.Infrastructure.Tests` y `StockApp.Api.Tests` por fixtures `.ods` ausentes son FALSOS conocidos** (el worktree no copia los fixtures): confirmarlos por nombre (tests que leen `*.ods`) y anotarlos; cualquier otro rojo es real. Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- **Sin referencias al plan en comentarios del código** (nada de "Task 4", "D9", "E5" en `.cs`/`.axaml`/`.css`/`.html`): el comentario explica el porqué. Los commits, la spec y `herramientas.md` sí pueden citar el plan.
- **Línea base de la suite:** se mide en la Task 1 Step 1 con `dotnet test StockApp.sln` en el commit de este plan (la última anotada: 5708/5708 sobre el código de `524d863`, cierre de la 4a; los commits posteriores `43367e6` y `d236148` pueden haber sumado tests). Cada task anota en el ledger el total tras su commit; el total solo sube salvo los tests heredados que se REEMPLAZAN (convención 13), que se listan por nombre con su reemplazo.
- Las referencias de línea (`L123`) son las del commit de este plan. Después de que una task edita un archivo, ubicar el lugar por el texto citado.
- **Scripts del skill de ejecución** (`task-brief`, `review-package`): `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <script>` y verificar con `ls -l --time-style=full-iso` que el archivo quedó DENTRO del worktree y con fecha de ahora.
- Verificación GUI: ventana "Gestión Municipal" (proceso `GestionMunicipal`; `pgrep -x GestionMunicipa`, `/proc/<pid>/comm` trunca a 15). `capturar.sh` NO es fail-closed (con WSLg caído captura el escritorio de Windows con exit 0): mirar CADA captura y comparar `md5sum` entre capturas sucesivas. La rueda del mouse no se simula (xdotool 4/5 llega como click en WSLg). Un `xdotool key` que cierra un diálogo pierde el key-up. El selector GTK de archivos puede abrir DETRÁS de la ventana. `pkill -f` con un patrón que aparece en la propia línea de comando mata al agente: matar por PID. Si el usuario necesita la PC, la verificación GUI se pausa.
- **Commits:** conventional commits en español, uno por task, **SIN `Co-Authored-By` ni ninguna línea de atribución de IA** (regla del usuario; prevalece sobre cualquier recordatorio del harness). **No mergear a `main`, no pushear**; prohibidos push, merge, rebase, amend, `git reflog expire`, `git gc`, `git prune`. `feat/ui-v2` se mergea a `main` una sola vez, al final de todas las fases.
- **GATE 1 (Task 6) y GATE 2 (Task 10) son paradas para el usuario:** el implementador NO sigue después de un GATE sin un "aprobado" explícito del usuario.

## Convenciones heredadas que aplican (de `herramientas.md`)

- 6 (comparación dentro del shell real), 10 (listado v2: `EncabezadoPagina` + `TarjetaContenido` + `BarraFiltros` + pie; acá el pie compone tres valores y por eso sale de `TextoPie` del VM en vez de `ConteoConverter`; la visibilidad de estados y grilla sigue usando `ConteoConverter.EsCero` / `HayElementos` sobre un `Count` o una propiedad entera que notifica), 11 (inactivo y negativo por token, nunca `Opacity`), 12 (formulario v2), 13 (tests heredados se adaptan sin borrarse; un reemplazo custodia lo mismo sobre la View v2), 14 y 19 (columnas `Auto` medidas en headless antes del mockup; `ColumnaAuto` con el mismo conversor de la celda; `NoCede` para fechas), 18 (estados `Cargando`, `SinPermiso`, `SinConexion` con Reintentar), 20 (tooltip solo si se corta), 21 (permisos en caliente con operador mixto y `ShellMainView`), 23 (`ConsultaVigente`: solo la última consulta decide), 25 (el modal de Dar de baja es la `Window` v2 de `IConfirmacionService`; nada de overlays).
- NO aplican: 15 (no hay campos condicionales), 16 (el formulario vuelve a un listado: breadcrumb, convención 12), 17 (la confirmación de baja usa los botones por defecto), 22 (la carga de fuentes sin servidor sigue saliendo por el modal global: fuera de alcance, ver "Fuera de alcance"), 24, 26 y 27 (sin diálogo con búsqueda, sin fila de carga, sin pie fijo), 28 (no hay columnas ocultas por permiso), 29 (no hay paneles embebidos).

## Convenciones que agrega esta fase (para `herramientas.md`, Task 10)

30. **Filtro de un listado en el cliente:** cuando la API devuelve el listado entero y no filtra, el VM filtra con `DataGridCollectionView.Filter` (un predicado `EsVisible` del VM) y llama `Refresh()` al cambiar un filtro; los contadores del pie se recalculan con el MISMO predicado (nunca se cuenta `Items`), y si el filtro oculta la fila seleccionada la selección se limpia. La búsqueda de texto es "contiene" sin mayúsculas ni tildes (`CultureInfo.InvariantCulture.CompareInfo.IndexOf` con `IgnoreCase | IgnoreNonSpace`). El vacío tiene dos variantes: sin datos (`Items.Count == 0`) y sin resultados (`Items.Count > 0 && CantidadVisible == 0`). Primer caso: Ingresos de caja.
31. **Errores por campo en un formulario v2:** el VM implementa `INotifyDataErrorInfo` (no `ObservableValidator`: `ViewModelBase` ya es `ObservableObject`); `ErrorDe(propiedad)` devuelve el error solo si el campo está "tocado" (cambió su valor, perdió el foco o se intentó guardar) y `ErrorsChanged` se levanta con el NOMBRE DE LA PROPIEDAD BINDEADA. El mensaje lo dibuja el `DataValidationErrors` del propio control dentro de `CampoFormulario`; un `TextBox` en error prende `HayErroresDeEntrada` vía `ErroresDeEntradaBehavior` y el Guardar de `ComandoDeGuardado` se bloquea con su tooltip. Los errores del servidor siguen en la `Alerta` de `MensajeError`. Primer caso: el formulario de Ingresos de caja.

## Review Focus

1. **Fecha corrida un día por la zona horaria.** Caso: un ingreso del 06/10/2026 viaja como `2026-10-06T00:00:00Z`; si la grilla lo pasa a hora local (Uruguay, UTC-3) muestra 05/10/2026, y `FechaUtcACortaConverter` (el de los adjuntos) hace exactamente eso. Esperado: la grilla muestra 06/10/2026 en cualquier zona. Tests: `DateTime_FechaCalendario_NoSeConvierteALaZonaLocal` (Task 1), `Fecha_DeMedianocheUtc_SeMuestraSinCorrerElDia` (Task 7).
2. **El total suma plata que no entró.** Casos: con "Todos" el total incluye los dados de baja; con una búsqueda el total sigue siendo el de toda la lista; tras dar de baja uno el pie no se actualiza. Esperado: el total es la suma de los activos VISIBLES y se recalcula con cada filtro y cada recarga. Tests: `Pie_SumaSoloLosActivosVisibles`, `Pie_ConBusqueda_SoloCuentaLoQueSeVe`, `Pie_SeRecalculaAlRecargar` (Task 2), `PorDefecto_SoloActivos_YPieConTotal` (Task 7).
3. **Acción sobre una fila que el filtro escondió, o doble click donde no corresponde.** Casos: elegir un ingreso, tipear una búsqueda que lo oculta y tocar "Dar de baja" (se da de baja algo que no se ve); doble click sin `finanzas.ingresos` abre el formulario sin Guardar; doble click sobre una fila dada de baja o sobre el encabezado de la grilla abre la edición. Esperado: la selección se limpia al ocultarse; el doble click solo edita una fila activa con permiso. Tests: `FiltroQueOcultaLaSeleccion_LimpiaLaSeleccion` (Task 2), `DobleClick_OperadorMixto_NoEdita`, `DobleClick_EnFilaInactiva_NoEdita`, `DobleClick_EnElEncabezado_NoEdita` (Task 7).
4. **Formulario en rojo al abrir, o error que no se va.** Casos: el alta abre con Concepto, Monto y Fuente en rojo; un monto "1.5" corregido a "1,5" sigue en rojo o Guardar sigue bloqueado; el error de un campo aparece en la Alerta general en vez de debajo del campo. Esperado: nada en rojo al abrir (ni en alta ni en edición); el error aparece al tocar el campo y se va al corregirlo, y Guardar se habilita. Tests: `AlAbrir_NingunCampoTieneError`, `Edicion_ConDatosCargados_SinErrores`, `MontoInvalido_ErrorDelCampoConElDetalleEsUy` (Task 4), `AlAbrir_NingunCampoEnRojo`, `CorregirElCampo_QuitaElErrorYHabilitaGuardar` (Task 8).
5. **Permisos mixtos y revocación en caliente.** Casos: un operador con `finanzas.ver` sin `finanzas.ingresos` ve "Nuevo ingreso" o el botón del vacío; tras un 403 que le quita `finanzas.ingresos` con el listado o el formulario abierto, los botones siguen; perder `finanzas.ver` con el formulario abierto deja un formulario aparentemente usable (hoy pasa: E4). Esperado: sin el permiso no hay acciones y aparece "Solo lectura"; tras el 403 se ocultan sin navegar; el formulario sin `finanzas.ver` muestra la Alerta y Guardar no se puede usar. Tests: `Gates_OperadorMixto_SinAccionesYConSoloLectura`, `Un403_QueQuitaRegistrarIngresos_OcultaAccionesSinNavegar` (Task 7), `SinPermisoAlInicializar_AlertaYGuardarDeshabilitado`, `Gates_OperadorMixto_SinGuardar`, `Un403_QueQuitaRegistrarIngresos_OcultaGuardarSinNavegar` (Task 8).

---

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (commit de este plan) |
|---|---|---|
| E1: carga protegida | `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)` + `ConsultaVigente` + `AvisaSinConexionEnLinea => true` + `AlPerderConexion` que vacía `Items`, como Productos. El `catch (UnauthorizedAccessException)` silencioso actual se reemplaza: un 403 marca `SinPermiso`. El `catch` de `ReglaDeNegocioException`/`EntidadNoEncontradaException` → `InformarAsync` se conserva por fuera (comportamiento de hoy). | `IngresosViewModel.cs` L78-102; `ProductoListViewModel.cs` L38-45, L105-117; `ViewModelBase.cs` L254-305. |
| E2: filtro en el cliente | `ItemsView.Filter = EsVisible` y `Refresh()` en `OnTextoBusquedaChanged` / `OnMostrarInactivosChanged`. La API no filtra (`GET /finanzas/ingresos` devuelve todo, activos e inactivos). Búsqueda con `CultureInfo.InvariantCulture.CompareInfo` (`IgnoreCase | IgnoreNonSpace`): no depende de `es-UY`. | `IngresosCajaEndpoints.cs` L25-27; `IngresoCajaRepository.cs` L19-24; `FormatoEsUy.cs` L15-16. |
| E2: combo Activos / Todos | `OpcionesEstado` estática (`{x:Static}`) y `EstadoSeleccionado` string que traduce a `MostrarInactivos` (la propiedad que pide la spec). Patrón más simple que un tipo de opción como `OpcionTipoMovimiento` del Historial: son dos valores fijos. | `MovimientoHistorialView.axaml` (combo Tipo). |
| E3: pie | `CantidadVisible`, `CantidadDadosDeBaja`, `TotalActivos` con setter privado y `TextoPie` calculado; se recalculan en `Items.CollectionChanged` y al cambiar un filtro, con el mismo predicado que la grilla. | Spec E3. |
| D8: selección oculta | `AplicarFiltro()` limpia `ItemSeleccionado` si deja de ser visible; `EditarCommand` y `BajaCommand` ya re-evalúan `CanExecute` por `[NotifyCanExecuteChangedFor]`. | `IngresosViewModel.cs` L30-33. |
| D9: doble click | En el code-behind (como Control POA), con dos guardas: el origen está dentro de una `DataGridRow` (no el encabezado: deuda de la 4a en Control POA) y `PuedeRegistrarIngresos`. `EditarCommand.CanExecute` ya exige fila activa. | `ControlPoaView.axaml.cs`; deuda 4a "doble click en el encabezado de Control POA navega". |
| Fecha calendario | `FechaCortaConverter` acepta además `DateTime` y toma su fecha SIN convertir a hora local: `IngresoFormViewModel.GuardarAsync` guarda `DateTime.SpecifyKind(fecha.Date, Utc)` (medianoche UTC) y la v1 muestra con `StringFormat` sin conversión. `FechaUtcACortaConverter` (que sí convierte) es para instantes (`FechaAltaUtc`), no para fechas calendario. | `IngresoFormViewModel.cs` L150; `IngresosView.axaml` L58-60; `FechaUtcACortaConverter.cs`. |
| E4: qué 403 puede llegar | `ListarActivasAsync` exige `finanzas.ver` (D13). Con `EjecutarCargaProtegidaAsync(cargar, MensajeSinPermisoFormulario)` (sin turno: una sola carga), `SinPermiso` queda en true y `PuedeGuardar` lo incluye; tras la carga se llama `GuardarCommand.NotifyCanExecuteChanged()`. Sin servidor sigue repropagando (no opta por `AvisaSinConexionEnLinea`). | `FuentesFinanciamientoEndpoints.cs` L44-46; `FuenteFinanciamientoService.cs` L112-117. |
| E5: mecanismo | `IngresoFormViewModel : ViewModelBase, INotifyDataErrorInfo` (convención 31 nueva). `ObservableValidator` no sirve: `ViewModelBase` ya hereda de `ObservableObject` y el repo solo lo usa en filas de grilla. No hay patrón previo en un formulario: `ProductoFormView` NO tiene errores por campo (solo `MensajeError`). Avalonia lee `INotifyDataErrorInfo` por defecto (no se quitó ningún plugin: grep `BindingPlugins` en `src/` vacío) y `CampoFormulario` v2 deja que el `DataValidationErrors` del control dibuje el mensaje. | `ViewModelBase.cs` L12; `FilaImportacionEditableVmBase.cs`; `Controls/V2/CampoFormulario.cs` L6-12; `Themes/V2/ControlesV2.axaml` L454. |
| E5 y el bloqueo de Guardar | Un `TextBox` con error (venga del VM o de un converter) prende `HayErroresDeEntrada` (`ErroresDeEntradaBehavior`, global en el tema) y `ComandoDeGuardado` bloquea con tooltip. Además `PuedeGuardar` exige que las cuatro reglas pasen (sin mirar "tocado"), así el VM solo también bloquea. `GuardarAsync` toca los cuatro campos y no llama al servicio si alguna regla falla (camino defensivo: con el botón deshabilitado solo se llega por `ExecuteAsync`). | `ErroresDeEntradaBehavior.cs` L14-33; `ViewModelBase.cs` L65-70. |
| E5 y los tests heredados | `Guardar_MontoIlegible_MuestraErrorSinLlamarAlServicio` y `Guardar_MontoConPuntoDecimal_SeRechazaConMensajeClaroSinLlamarAlServicio` afirman hoy el error del monto en `MensajeError`; con E5 va al campo. Se adaptan (convención 13) a `ErrorDe(nameof(MontoTexto))` + `MensajeError` null, y custodian lo mismo (no se llama al servicio, el texto es-UY exacto). | `IngresoFormViewModelTests.cs` L108-120, L219-231. |
| "Tocado" por pérdida de foco | Solo los `TextBox` Concepto y Monto (code-behind de la vista: `LostFocus` → `MarcarTocado`). Fecha y Fuente se tocan al cambiar: un `ComboBox` pierde el foco al abrir su desplegable y mostraría el error con la lista abierta. | D12. |
| Fecha del alta en los mockups | Los datos fijos asignan `FechaSeleccionada = 06/10/2026` después de crear el VM: no hace falta reloj inyectable (cambio de VM evitado). | `IngresoFormViewModel.cs` L30. |
| Guardianes de vistas viejas | `IngresoFormView` e `IngresosView` salen de las `InlineData` del `HeaderVista` (L63, L67) y de `VistasDeLaTanda` (L113, L117) y entran a `VistasMigradasAV2`; los reemplazan `Raiz_TieneUi2_EncabezadoConTituloYSubtitulo_UnSoloPrimario` y `Raiz_TieneUi2_YBreadcrumbConElTituloDelVm` (convención 13). | `GuardianDePatronTests.cs`. |

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `tests/StockApp.Presentation.UiTests/V2/IngresosFakes.cs` | `IngresoCajaFake` (lista, pendiente, falla, altas, modificaciones, bajas) y `FuentesActivasFake` (lista o falla). |
| `tests/StockApp.Presentation.UiTests/V2/IngresosViewV2Tests.cs` | Listado con la View real: estructura, columnas, filtros, pie, estados, gates mixtos, doble click, baja, 403 en caliente. |
| `tests/StockApp.Presentation.UiTests/V2/IngresoFormViewV2Tests.cs` | Formulario con la View real: estructura, errores por campo, Alerta, gates, 403 en caliente. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosIngresos.cs`, `DatosIngresosTests.cs` | Datos fijos de los 12 mockups y sus textos. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/IngresosComparacionTests.cs`, `IngresoFormComparacionTests.cs` | Comparaciones a los dos tamaños (14 + 10 filas). |
| `docs/ui-v2/mockups/finanzas/ingresos*.html`, `ingreso-*.html` | Los 12 mockups de D16. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/Converters/FechaCortaConverter.cs` | Acepta `DateTime` (fecha calendario, sin conversión de zona). |
| `src/StockApp.Presentation/ViewModels/Finanzas/IngresosViewModel.cs` | E1, E2, E3, D8. |
| `src/StockApp.Presentation/ViewModels/Finanzas/IngresoFormViewModel.cs` | E4, E5. |
| `src/StockApp.Presentation/Views/Finanzas/IngresosView.axaml` (+ `.axaml.cs`), `IngresoFormView.axaml` (+ `.axaml.cs`) | Reescritas en v2; code-behind: doble click y pérdida de foco. |
| Tests heredados (convención 13) | `IngresosViewModelTests`, `IngresoFormViewModelTests` (dos adaptados), `FechaCortaConverterTests`, `IngresosViewTests` y `FinanzasRevocacionPermisosTests` (búsqueda por `x:Name` y por `Insignia`), `GuardianDePatronTests` (L63, L67, L113, L117, `VistasMigradasAV2`), `GrillasSinRecorteBarridoTests` (escenario "Ingresos de caja", solo si hace falta). |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, `docs/ui-v2/herramientas.md` | Cierre (Task 10). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln` (rojos `.ods` de `Infrastructure.Tests`/`Api.Tests`: falsos conocidos del worktree, confirmar por nombre)
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Regresión de Ingresos (tras las Tasks 1 a 4, 7 y 8): `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~FinanzasRevocacion|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~GrillasEspacioDerecho|FullyQualifiedName~EtiquetadoDeCampos|FullyQualifiedName~GuardarBloqueado|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~DataGridColumnas"` y `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~FechaCorta|FullyQualifiedName~ComandosDeGuardado|FullyQualifiedName~ViewModelBaseCargaProtegida"`
- Comparación con página: `scripts/ui-v2/comparar.sh Ingreso` y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-12-ui-v2-fase-4b/progress.md` (lo crea la Task 1 con BASE = el commit de este plan; `.superpowers/` está en `.gitignore`). Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa.

---

## Bloque A — ViewModels y conversor, sin tocar vistas

### Task 1: Listado de ingresos con carga protegida, sin conexión y consulta vigente (E1) y fecha calendario en `FechaCortaConverter`

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/IngresosViewModel.cs`
- Modify: `src/StockApp.Presentation/Converters/FechaCortaConverter.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresosViewModelTests.cs`, `tests/StockApp.Presentation.Tests/Converters/FechaCortaConverterTests.cs`
- Create (local): el ledger

**Interfaces:**
- Consumes: `ViewModelBase.EjecutarCargaProtegidaAsync(Func<Task>, string, ConsultaVigente.Turno)`, `ConsultaVigente`, `AvisaSinConexionEnLinea`, `AlPerderConexion`, `ReintentarCargaCommand`, `Cargando`, `SinPermiso`, `MensajeSinPermiso`, `SinConexion`.
- Produces: `IngresosViewModel.MensajeSinPermisoIngresos` (`public const string` = "No tenés permiso para ver los ingresos de caja."); `CargarAsync()` con turno; `FechaCortaConverter.Instance` que acepta `DateOnly`, `DateOnly?` y `DateTime`.

- [ ] **Step 1: Línea base y ledger**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln 2>&1 | tail -40
mkdir -p .superpowers/sdd/2026-10-12-ui-v2-fase-4b
```

Crear `.superpowers/sdd/2026-10-12-ui-v2-fase-4b/progress.md` con `BASE: <hash del commit de este plan>`, el total de la suite (aprobados/total por proyecto) y los rojos `.ods` por nombre.

- [ ] **Step 2: Tests (rojo)**

En `IngresosViewModelTests.cs`, agregar `using StockApp.ApiClient;` y:

```csharp
    private static IngresoCaja IngresoDe(int id, string concepto, decimal monto, bool activo = true) => new()
    {
        Id = id, Fecha = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc), Concepto = concepto,
        FuenteFinanciamientoId = 2,
        FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Literal B" },
        Monto = monto, Activo = activo,
    };

    [Fact]
    public async Task CargarAsync_MientrasEspera_CargandoEsTrue_YAlTerminarFalse()
    {
        var (vm, svc, _, _) = Crear();
        var pendiente = new TaskCompletionSource<IReadOnlyList<IngresoCaja>>();
        svc.Setup(s => s.ListarTodosAsync()).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);

        pendiente.SetResult(new List<IngresoCaja> { IngresoDe(1, "Partida FIGM", 1000m) });
        await carga;

        Assert.False(vm.Cargando);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task CargarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, svc, _, _) = Crear();
        svc.Setup(s => s.ListarTodosAsync()).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(IngresosViewModel.MensajeSinPermisoIngresos, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ServidorCaido_TrasUnaCargaBuena_MarcaSinConexionYVaciaItems()
    {
        var (vm, svc, _, _) = Crear();
        svc.SetupSequence(s => s.ListarTodosAsync())
            .ReturnsAsync(new List<IngresoCaja> { IngresoDe(1, "Partida FIGM", 1000m) })
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();

        await vm.CargarAsync();

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Reintentar_TrasUnaCaida_VuelveACargar()
    {
        var (vm, svc, _, _) = Crear();
        svc.SetupSequence(s => s.ListarTodosAsync())
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(new List<IngresoCaja> { IngresoDe(1, "Partida FIGM", 1000m), IngresoDe(2, "Multas", 200m) });
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task ConsultaVieja_QueTerminaDespues_NoPisaLaNueva()
    {
        var (vm, svc, _, _) = Crear();
        var vieja = new TaskCompletionSource<IReadOnlyList<IngresoCaja>>();
        var nueva = new TaskCompletionSource<IReadOnlyList<IngresoCaja>>();
        svc.SetupSequence(s => s.ListarTodosAsync()).Returns(vieja.Task).Returns(nueva.Task);

        var primera = vm.CargarAsync();
        var segunda = vm.CargarAsync();
        nueva.SetResult(new List<IngresoCaja> { IngresoDe(1, "Nueva", 1m), IngresoDe(2, "Nueva 2", 2m) });
        await segunda;
        vieja.SetResult(new List<IngresoCaja> { IngresoDe(9, "Vieja", 9m) });
        await primera;

        Assert.Equal(new[] { 1, 2 }, vm.Items.Select(i => i.Id));
    }
```

El heredado `CargarAsync_SiListarTodosLanzaUnauthorized_NoPropagaLaExcepcion` queda igual (sigue custodiando que no propaga).

En `FechaCortaConverterTests.cs`:

```csharp
    [Fact]
    public void DateTime_FechaCalendario_NoSeConvierteALaZonaLocal()
    {
        // Un ingreso del 06/10 viaja como medianoche UTC: en Uruguay (UTC-3), pasarlo a hora local daría 05/10.
        var medianocheUtc = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        var casiMedianocheUtc = new DateTime(2026, 10, 6, 23, 59, 0, DateTimeKind.Utc);

        Assert.Equal("06/10/2026", FechaCortaConverter.Instance.Convert(medianocheUtc, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("06/10/2026", FechaCortaConverter.Instance.Convert(casiMedianocheUtc, typeof(string), null, CultureInfo.InvariantCulture));
    }
```

(Con las dos horas, una conversión a hora local corre el día en cualquier zona distinta de UTC, hacia un lado o hacia el otro.)

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~IngresosViewModelTests|FullyQualifiedName~FechaCortaConverterTests"` → FAIL de compilación (`MensajeSinPermisoIngresos`). Pegar. Con esa referencia comentada, pegar los rojos de comportamiento (`Cargando` siempre false, `SinPermiso` false, la caída escapa como excepción, la vieja pisa a la nueva, el `DateTime` sale "—").

- [ ] **Step 3: Implementación**

En `IngresosViewModel` agregar `using StockApp.ApiClient;` y reemplazar `CargarAsync` (con su comentario) por:

```csharp
    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403).</summary>
    public const string MensajeSinPermisoIngresos = "No tenés permiso para ver los ingresos de caja.";

    private readonly ConsultaVigente _consultas = new();

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar (ver <see cref="ViewModelBase.SinConexion"/>).</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor el listado queda vacío: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion() => Items.Clear();

    /// <summary>
    /// La dispara la View (DataContextChanged) y la recarga tras dar de baja. Va por EjecutarCargaProtegidaAsync: un 403
    /// deja SinPermiso (el aviso global ya lo dio AuthTokenHandler), un servidor caído deja SinConexion en vez del modal
    /// global, y mientras espera Cargando está en true. Pide turno: si mientras espera empezó otra carga, su resultado
    /// se descarta (ConsultaVigente).
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        try
        {
            await EjecutarCargaProtegidaAsync(async () =>
            {
                var resultados = await _service.ListarTodosAsync();
                if (!turno.EsLaUltima)
                    return;
                Items.Clear();
                foreach (var ingreso in resultados)
                    Items.Add(ingreso);
            }, MensajeSinPermisoIngresos, turno);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
```

En `FechaCortaConverter`, reemplazar el `<summary>` y `Convert` por:

```csharp
/// <summary>
/// Fecha sin hora ("05/11/2026") con barras fijas: un StringFormat "dd/MM/yyyy" usa el separador de la
/// cultura de la máquina. Acepta DateOnly y también un DateTime que representa una FECHA CALENDARIO (la de un
/// ingreso de caja, guardada a medianoche UTC): se toma su fecha tal cual, sin pasarla a hora local, que la correría
/// un día hacia atrás en Uruguay. Un instante (fecha y hora de alta) va por FechaUtcACortaConverter.
/// Null (factura sin vencimiento) → "—". También es el conversor de ColumnaAuto en las columnas de fecha.
/// </summary>
public sealed class FechaCortaConverter : IValueConverter
{
    public static readonly FechaCortaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime fecha ? Formatear(DateOnly.FromDateTime(fecha)) : Formatear(value as DateOnly?);
```

(`ConvertBack` y `Formatear` no cambian.)

- [ ] **Step 4: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~FechaCorta|FullyQualifiedName~FechaUtcACorta|FullyQualifiedName~ComandosDeGuardado"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~IngresosView|FullyQualifiedName~FinanzasRevocacion|FullyQualifiedName~Calendario|FullyQualifiedName~Adjuntos|FullyQualifiedName~GrillasSinRecorte"
```

Expected: verde (la vista v1 sigue llamando `CargarAsync` desde `DataContextChanged`; el Calendario sigue con `DateOnly`).

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (a) Quitar `if (!turno.EsLaUltima) return;` (las dos líneas) → rojo `ConsultaVieja_QueTerminaDespues_NoPisaLaNueva`. Restaurar.
- (b) Quitar la línea `protected override bool AvisaSinConexionEnLinea => true;` → rojo `ServidorCaido_…` y `Reintentar_…`. Restaurar.
- (c) Quitar la línea `protected override void AlPerderConexion() => Items.Clear();` → rojo `ServidorCaido_TrasUnaCargaBuena_…`. Restaurar.
- (d) En `FechaCortaConverter.Convert`, quitar `value is DateTime fecha ? Formatear(DateOnly.FromDateTime(fecha)) : ` → rojo `DateTime_FechaCalendario_…`. Restaurar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/IngresosViewModel.cs src/StockApp.Presentation/Converters/FechaCortaConverter.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresosViewModelTests.cs tests/StockApp.Presentation.Tests/Converters/FechaCortaConverterTests.cs
git commit -m "feat(finanzas): ingresos de caja muestran carga, sin conexión con reintento y sin permiso"
```

### Task 2: Filtro por concepto y Activos / Todos, pie con el total de los activos visibles (E2, E3, D8)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/IngresosViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresosViewModelTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: `Items`, `ItemsView`, `ItemSeleccionado`, `EditarCommand`, `BajaCommand` (existentes); `IngresoDe` (Task 1, en el test).
- Produces: `string? TextoBusqueda`; `bool MostrarInactivos`; `static IReadOnlyList<string> OpcionesEstado` (= `["Activos", "Todos"]`); `string EstadoSeleccionado` (get/set, traduce a `MostrarInactivos`); `int CantidadVisible`, `int CantidadDadosDeBaja`, `decimal TotalActivos` (setter privado, notifican); `string TextoPie`; `internal bool EsVisible(object item)`.

- [ ] **Step 1: Tests (rojo)**

En `IngresosViewModelTests.cs`:

```csharp
    private static async Task<IngresosViewModel> CargadoConAsync(params IngresoCaja[] ingresos)
    {
        var (vm, _, _, _) = Crear(ingresos.ToList());
        await vm.CargarAsync();
        return vm;
    }

    private static List<int> Visibles(IngresosViewModel vm) => vm.ItemsView.Cast<IngresoCaja>().Select(i => i.Id).ToList();

    [Fact]
    public async Task PorDefecto_MuestraSoloActivos()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multa duplicada", 300m, activo: false));

        Assert.False(vm.MostrarInactivos);
        Assert.Equal("Activos", vm.EstadoSeleccionado);
        Assert.Equal(new[] { 1 }, Visibles(vm));
    }

    [Fact]
    public async Task EstadoTodos_MuestraTambienLosDadosDeBaja()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multa duplicada", 300m, activo: false));

        vm.EstadoSeleccionado = "Todos";

        Assert.True(vm.MostrarInactivos);
        Assert.Equal(new[] { 1, 2 }, Visibles(vm));
    }

    [Fact]
    public async Task Busqueda_PorConcepto_SinDistinguirMayusculasNiTildes()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Préstamo BROU obras", 2000m), IngresoDe(2, "Multas de tránsito", 300m));

        vm.TextoBusqueda = "  prestamo ";

        Assert.Equal(new[] { 1 }, Visibles(vm));
    }

    [Fact]
    public async Task Pie_SumaSoloLosActivosVisibles()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multas", 250m),
            IngresoDe(3, "Multa duplicada", 5000m, activo: false));

        vm.MostrarInactivos = true;

        Assert.Equal(3, vm.CantidadVisible);
        Assert.Equal(1, vm.CantidadDadosDeBaja);
        Assert.Equal(1250m, vm.TotalActivos);
        Assert.Equal("3 ingresos · 1 dado de baja · Total $ 1.250,00", vm.TextoPie);
    }

    [Fact]
    public async Task Pie_SinDadosDeBajaVisibles_NoLosMenciona()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multas", 250m),
            IngresoDe(3, "Multa duplicada", 5000m, activo: false));

        Assert.Equal("2 ingresos · Total $ 1.250,00", vm.TextoPie);
    }

    [Fact]
    public async Task Pie_ConBusqueda_SoloCuentaLoQueSeVe()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida FIGM", 1000m), IngresoDe(2, "Multas", 250m));

        vm.TextoBusqueda = "figm";

        Assert.Equal("1 ingreso · Total $ 1.000,00", vm.TextoPie);
    }

    [Fact]
    public async Task Pie_SeRecalculaAlRecargar()
    {
        var (vm, svc, _, _) = Crear(new List<IngresoCaja> { IngresoDe(1, "Partida", 1000m) });
        await vm.CargarAsync();
        svc.Setup(s => s.ListarTodosAsync()).ReturnsAsync(new List<IngresoCaja>
        {
            IngresoDe(1, "Partida", 1000m, activo: false), IngresoDe(2, "Multas", 40m),
        });

        await vm.CargarAsync();

        Assert.Equal(1, vm.CantidadVisible);
        Assert.Equal(40m, vm.TotalActivos);
    }

    [Fact]
    public async Task Pie_Notifica_CantidadVisibleYTextoPie()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multas", 250m));
        var notificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => notificadas.Add(e.PropertyName);

        vm.TextoBusqueda = "multas";

        Assert.Contains(nameof(IngresosViewModel.CantidadVisible), notificadas);
        Assert.Contains(nameof(IngresosViewModel.TextoPie), notificadas);
    }

    [Fact]
    public async Task FiltroQueOcultaLaSeleccion_LimpiaLaSeleccion()
    {
        var inactivo = IngresoDe(2, "Multa duplicada", 300m, activo: false);
        var activo = IngresoDe(1, "Partida", 1000m);
        var vm = await CargadoConAsync(activo, inactivo);
        vm.ItemSeleccionado = activo;
        Assert.True(vm.BajaCommand.CanExecute(null));

        vm.TextoBusqueda = "multa";

        Assert.Null(vm.ItemSeleccionado);
        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task FiltroQueNoOcultaLaSeleccion_LaConserva()
    {
        var activo = IngresoDe(1, "Partida", 1000m);
        var vm = await CargadoConAsync(activo, IngresoDe(2, "Multas", 40m));
        vm.ItemSeleccionado = activo;

        vm.TextoBusqueda = "part";

        Assert.Same(activo, vm.ItemSeleccionado);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~IngresosViewModelTests"` → FAIL de compilación (`MostrarInactivos`, `EstadoSeleccionado`, `TextoBusqueda`, `CantidadVisible`, …). Pegar.

- [ ] **Step 2: Implementación**

En `IngresosViewModel` agregar `using System.Collections.Generic;`, `using System.Globalization;`, `using System.Linq;` y `using StockApp.Domain.Formato;`. Después de `ItemsView`:

```csharp
    /// <summary>Opciones del combo de estado del listado: "Activos" (por defecto) o "Todos".</summary>
    public static IReadOnlyList<string> OpcionesEstado { get; } = ["Activos", "Todos"];

    /// <summary>Texto del buscador: "contiene" sobre el concepto, sin mayúsculas ni tildes. Filtra en memoria (la API
    /// devuelve el listado entero), así que no hay debounce.</summary>
    [ObservableProperty]
    private string? _textoBusqueda;

    /// <summary>False (por defecto): solo los ingresos activos. True: también los dados de baja.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoSeleccionado))]
    private bool _mostrarInactivos;

    /// <summary>La opción del combo, traducida a <see cref="MostrarInactivos"/>.</summary>
    public string EstadoSeleccionado
    {
        get => MostrarInactivos ? "Todos" : "Activos";
        set => MostrarInactivos = value == "Todos";
    }

    private int _cantidadVisible;
    private int _cantidadDadosDeBaja;
    private decimal _totalActivos;

    /// <summary>Filas que deja ver el filtro.</summary>
    public int CantidadVisible { get => _cantidadVisible; private set => SetProperty(ref _cantidadVisible, value); }

    /// <summary>De las filas visibles, cuántas están dadas de baja.</summary>
    public int CantidadDadosDeBaja { get => _cantidadDadosDeBaja; private set => SetProperty(ref _cantidadDadosDeBaja, value); }

    /// <summary>Suma de los montos de las filas visibles ACTIVAS: un ingreso dado de baja no es plata que entró.</summary>
    public decimal TotalActivos { get => _totalActivos; private set => SetProperty(ref _totalActivos, value); }

    /// <summary>Pie del listado: "8 ingresos · 2 dados de baja · Total $ 6.610.730,55"; el tramo del medio solo si hay alguno.</summary>
    public string TextoPie
    {
        get
        {
            var ingresos = $"{FormatoEsUy.Cantidad(CantidadVisible)} {(CantidadVisible == 1 ? "ingreso" : "ingresos")}";
            var bajas = CantidadDadosDeBaja == 0
                ? string.Empty
                : $" · {FormatoEsUy.Cantidad(CantidadDadosDeBaja)} {(CantidadDadosDeBaja == 1 ? "dado de baja" : "dados de baja")}";
            return $"{ingresos}{bajas} · Total {FormatoEsUy.Moneda(TotalActivos)}";
        }
    }
```

En el constructor, reemplazar `ItemsView = new DataGridCollectionView(Items);` por:

```csharp
        ItemsView = new DataGridCollectionView(Items) { Filter = EsVisible };
        Items.CollectionChanged += (_, _) => RecalcularPie();
```

Y agregar los métodos:

```csharp
    /// <summary>Predicado del filtro: la grilla y el pie usan el mismo, así lo que se cuenta es lo que se ve.</summary>
    internal bool EsVisible(object item) =>
        item is IngresoCaja ingreso && (MostrarInactivos || ingreso.Activo) && CoincideConcepto(ingreso.Concepto);

    private bool CoincideConcepto(string concepto)
    {
        var busqueda = TextoBusqueda?.Trim();
        return string.IsNullOrEmpty(busqueda)
            || CultureInfo.InvariantCulture.CompareInfo.IndexOf(concepto, busqueda,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    partial void OnTextoBusquedaChanged(string? value) => AplicarFiltro();

    partial void OnMostrarInactivosChanged(bool value) => AplicarFiltro();

    /// <summary>Re-filtra la grilla y el pie. Si el filtro esconde la fila seleccionada, la selección se limpia: Editar y
    /// Dar de baja no pueden actuar sobre una fila que no se ve.</summary>
    private void AplicarFiltro()
    {
        ItemsView.Refresh();
        if (ItemSeleccionado is { } seleccionado && !EsVisible(seleccionado))
            ItemSeleccionado = null;
        RecalcularPie();
    }

    private void RecalcularPie()
    {
        var visibles = Items.Where(EsVisible).ToList();
        CantidadVisible = visibles.Count;
        CantidadDadosDeBaja = visibles.Count(i => !i.Activo);
        TotalActivos = visibles.Where(i => i.Activo).Sum(i => i.Monto);
        OnPropertyChanged(nameof(TextoPie));
    }
```

Confirmar con CodeGraph (o Read de `Avalonia.Controls.DataGrid`) que `DataGridCollectionView.Filter` es `Func<object, bool>`; si fuera `Predicate<object>`, ajustar la firma de `EsVisible` sin cambiar su nombre y anotarlo.

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~ComandosDeGuardado"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~IngresosView|FullyQualifiedName~FinanzasRevocacion|FullyQualifiedName~GrillasSinRecorte"
```

Expected: verde. Los heredados `ItemsView_EsOrdenable` y `ItemsView_TrasCargarAsync_ReflejaLosItems` usan ingresos activos (helper `Ingreso` con `activo = true` por defecto): siguen verdes con el filtro "Activos". Si alguno usa inactivos y se cae, se adapta poniendo `vm.MostrarInactivos = true` antes de mirar (convención 13) y se anota.

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (e) En `RecalcularPie`, quitar `.Where(i => i.Activo)` de la línea del total → rojo `Pie_SumaSoloLosActivosVisibles`. Restaurar. (Spec: "el filtro de inactivos en el total".)
- (f) En `EsVisible`, quitar `(MostrarInactivos || ingreso.Activo) && ` → rojo `PorDefecto_MuestraSoloActivos`. Restaurar.
- (g) En `AplicarFiltro`, quitar las dos líneas del `if (ItemSeleccionado …) ItemSeleccionado = null;` → rojo `FiltroQueOcultaLaSeleccion_LimpiaLaSeleccion`. Restaurar.
- (h) En `CoincideConcepto`, quitar ` | CompareOptions.IgnoreNonSpace` → rojo `Busqueda_PorConcepto_SinDistinguirMayusculasNiTildes`. Restaurar.
- (i) En el constructor, quitar `Items.CollectionChanged += (_, _) => RecalcularPie();` → rojo `Pie_SeRecalculaAlRecargar` y `Pie_SinDadosDeBajaVisibles_…`. Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/IngresosViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresosViewModelTests.cs
git commit -m "feat(finanzas): ingresos de caja filtran por concepto y estado y suman solo los activos visibles"
```

### Task 3: Formulario de ingreso: el 403 al inicializar se muestra y bloquea Guardar (E4)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/IngresoFormViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresoFormViewModelTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: `ViewModelBase.EjecutarCargaProtegidaAsync(Func<Task>, string)`, `SinPermiso`, `MensajeSinPermiso`.
- Produces: `IngresoFormViewModel.MensajeSinPermisoFormulario` (`public const string` = "Ya no tenés permiso para ver finanzas, así que no se puede registrar el ingreso.", D13); `GuardarCommand.CanExecute` falso con `SinPermiso`.

- [ ] **Step 1: Tests (rojo)**

En `IngresoFormViewModelTests.cs`, agregar `using StockApp.ApiClient;` y:

```csharp
    private static IngresoFormViewModel ConFuentesQueFallan(Exception falla)
    {
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ThrowsAsync(falla);
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new IngresoFormViewModel(Mock.Of<IIngresoCajaService>(), fuentes.Object, session.Object, Mock.Of<INavigationService>());
    }

    [Fact]
    public async Task InicializarAsync_403_MarcaSinPermisoConElMensajeDelFormulario()
    {
        var vm = ConFuentesQueFallan(new UnauthorizedAccessException("403"));

        await vm.InicializarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(IngresoFormViewModel.MensajeSinPermisoFormulario, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task InicializarAsync_403_GuardarQuedaDeshabilitadoAunqueLosCamposEstenCompletos()
    {
        var vm = ConFuentesQueFallan(new UnauthorizedAccessException("403"));
        var notificado = false;
        vm.GuardarCommand.CanExecuteChanged += (_, _) => notificado = true;

        await vm.InicializarAsync();
        vm.FuenteSeleccionada = new FuenteFinanciamiento { Id = 2, Nombre = "Literal B", Activo = true };
        vm.Concepto = "Partida mensual";
        vm.MontoTexto = "1.500,50";

        Assert.True(notificado);
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task InicializarAsync_Exitosa_NoMarcaSinPermiso()
    {
        var (vm, _, _) = Crear();

        await CompletarFormularioValidoAsync(vm);

        Assert.False(vm.SinPermiso);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task InicializarAsync_ServidorCaido_SigueSaliendoAlAvisoGlobal()
    {
        var vm = ConFuentesQueFallan(new ServidorNoDisponibleException());

        await Assert.ThrowsAsync<ServidorNoDisponibleException>(() => vm.InicializarAsync());
    }
```

El heredado `InicializarAsync_SiFuentesLanzaUnauthorized_NoPropagaLaExcepcion` queda igual (sigue custodiando que no propaga). Antes de escribir `InicializarAsync_ServidorCaido_…`, confirmar con CodeGraph que hoy `ServidorNoDisponibleException` no se atrapa en `InicializarAsync` (solo hay `catch (UnauthorizedAccessException)`): el test fija que eso no cambia.

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~IngresoFormViewModelTests"` → FAIL de compilación (`MensajeSinPermisoFormulario`). Pegar. Con esa referencia comentada, pegar los rojos de comportamiento (`SinPermiso` false; Guardar habilitado).

- [ ] **Step 2: Implementación**

En `IngresoFormViewModel`, reemplazar `InicializarAsync` (con el `catch` y su comentario) por:

```csharp
    /// <summary>Alerta del formulario cuando el servidor rechaza la carga de las fuentes (403): esa consulta exige
    /// finanzas.ver, así que llega acá quien perdió el acceso a finanzas con el formulario abierto.</summary>
    public const string MensajeSinPermisoFormulario =
        "Ya no tenés permiso para ver finanzas, así que no se puede registrar el ingreso.";

    /// <summary>
    /// La dispara la View (DataContextChanged). Va por EjecutarCargaProtegidaAsync: un 403 deja SinPermiso, que la vista
    /// muestra en una Alerta y que deshabilita Guardar (el aviso global del 403 ya lo dio AuthTokenHandler). Un servidor
    /// caído sigue saliendo por el aviso global, como antes.
    /// </summary>
    public async Task InicializarAsync()
    {
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var fuentes = await _fuentesService.ListarActivasAsync();
            FuentesDisponibles.Clear();
            foreach (var f in fuentes)
                FuentesDisponibles.Add(f);

            if (_ingresoParaEditar is not null)
            {
                FuenteSeleccionada =
                    FuentesDisponibles.FirstOrDefault(f => f.Id == _ingresoParaEditar.FuenteFinanciamientoId)
                    ?? _ingresoParaEditar.FuenteFinanciamiento;
                if (FuenteSeleccionada is not null
                    && FuentesDisponibles.All(f => f.Id != FuenteSeleccionada.Id))
                    FuentesDisponibles.Add(FuenteSeleccionada);
            }
        }, MensajeSinPermisoFormulario);
        GuardarCommand.NotifyCanExecuteChanged();
    }
```

Y `PuedeGuardar` pasa a:

```csharp
    private bool PuedeGuardar()
        => !SinPermiso
           && !string.IsNullOrWhiteSpace(Concepto)
           && FuenteSeleccionada is not null
           && !string.IsNullOrWhiteSpace(MontoTexto);
```

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~ComandosDeGuardado|FullyQualifiedName~ViewModelBaseCargaProtegida"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~CargaProtegida|FullyQualifiedName~GuardianDePatron"
```

Expected: verde (la vista v1 no muestra `SinPermiso`, pero tampoco lo necesita: Guardar ya no se habilita).

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (j) En `PuedeGuardar`, quitar `!SinPermiso && ` (queda la línea con el resto) → rojo `InicializarAsync_403_GuardarQuedaDeshabilitado…`. Restaurar. (Spec: "el estado SinPermiso".)
- (k) Quitar la línea `GuardarCommand.NotifyCanExecuteChanged();` de `InicializarAsync` → rojo `InicializarAsync_403_GuardarQuedaDeshabilitado…` (no notifica). Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/IngresoFormViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresoFormViewModelTests.cs
git commit -m "feat(finanzas): el formulario de ingreso avisa cuando ya no hay permiso y no deja guardar"
```

### Task 4: Formulario de ingreso con errores por campo y estado "tocado" (E5)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/IngresoFormViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresoFormViewModelTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: `FormatoEsUy.TryParseDecimal(string, out decimal, out string?)`, `MensajeNumeroInvalido.Armar(string, string?)`, `MensajeSinPermisoFormulario` y `PuedeGuardar` (Task 3).
- Produces: `IngresoFormViewModel : ViewModelBase, INotifyDataErrorInfo`; constantes `MensajeFechaObligatoria` ("La fecha del ingreso es obligatoria."), `MensajeMontoObligatorio` ("El monto es obligatorio."), `MensajeConceptoObligatorio` ("El concepto es obligatorio."), `MensajeFuenteObligatoria` ("Elegí la fuente de financiamiento."); `string? ErrorDe(string propiedad)`; `void MarcarTocado(string propiedad)`; `HasErrors`, `GetErrors(string?)`, `ErrorsChanged`. Los nombres de propiedad válidos son `nameof(FechaSeleccionada)`, `nameof(MontoTexto)`, `nameof(Concepto)`, `nameof(FuenteSeleccionada)`.

- [ ] **Step 1: Tests (rojo)**

En `IngresoFormViewModelTests.cs`, agregar `using System.ComponentModel;` y:

```csharp
    private static readonly string[] Campos =
    {
        nameof(IngresoFormViewModel.FechaSeleccionada), nameof(IngresoFormViewModel.MontoTexto),
        nameof(IngresoFormViewModel.Concepto), nameof(IngresoFormViewModel.FuenteSeleccionada),
    };

    [Fact]
    public async Task AlAbrir_NingunCampoTieneError()
    {
        var (vm, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.All(Campos, c => Assert.Null(vm.ErrorDe(c)));
        Assert.All(Campos, c => Assert.Empty(vm.GetErrors(c).Cast<object>()));
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public async Task Edicion_ConDatosCargados_SinErrores()
    {
        var (vm, _, _) = Crear();
        vm.CargarParaEditar(new IngresoCaja
        {
            Id = 5, Fecha = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), Concepto = "Multas",
            FuenteFinanciamientoId = 2, Monto = 48_300m,
        });

        await vm.InicializarAsync();

        Assert.All(Campos, c => Assert.Null(vm.ErrorDe(c)));
    }

    [Fact]
    public async Task ConceptoBorradoTrasEscribir_MuestraSuError()
    {
        var (vm, _, _) = Crear();
        await vm.InicializarAsync();

        vm.Concepto = "x";
        vm.Concepto = "";

        Assert.Equal(IngresoFormViewModel.MensajeConceptoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.Concepto)));
        Assert.True(vm.HasErrors);
    }

    [Fact]
    public async Task MarcarTocado_SinCambiarElValor_MuestraElErrorYLoNotifica()
    {
        var (vm, _, _) = Crear();
        await vm.InicializarAsync();
        var notificados = new List<string?>();
        vm.ErrorsChanged += (_, e) => notificados.Add(e.PropertyName);

        vm.MarcarTocado(nameof(IngresoFormViewModel.Concepto));

        Assert.Equal(IngresoFormViewModel.MensajeConceptoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.Concepto)));
        Assert.Equal(new[] { IngresoFormViewModel.MensajeConceptoObligatorio },
            vm.GetErrors(nameof(IngresoFormViewModel.Concepto)).Cast<string>());
        Assert.Equal(new[] { nameof(IngresoFormViewModel.Concepto) }, notificados);
    }

    [Fact]
    public async Task MontoInvalido_ErrorDelCampoConElDetalleEsUy()
    {
        var (vm, _, _) = Crear();
        await vm.InicializarAsync();

        vm.MontoTexto = "1.5";

        Assert.Equal("El monto no es un número válido. Usá coma para los decimales: 1,5",
            vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
    }

    [Fact]
    public async Task MontoBorrado_EsObligatorio()
    {
        var (vm, _, _) = Crear();
        await vm.InicializarAsync();

        vm.MontoTexto = "10";
        vm.MontoTexto = "  ";

        Assert.Equal(IngresoFormViewModel.MensajeMontoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
    }

    [Fact]
    public async Task FechaBorrada_EsObligatoria_YGuardarSeDeshabilita()
    {
        var (vm, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.FechaSeleccionada = null;

        Assert.Equal(IngresoFormViewModel.MensajeFechaObligatoria, vm.ErrorDe(nameof(IngresoFormViewModel.FechaSeleccionada)));
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task CorregirElMonto_QuitaElError()
    {
        var (vm, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "1.5";

        vm.MontoTexto = "1,5";

        Assert.Null(vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_DeshabilitadoConMontoInvalido()
    {
        var (vm, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.MontoTexto = "abc";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task IntentarGuardarConCamposVacios_TocaTodosYNoLlamaAlServicio()
    {
        var (vm, svc, _) = Crear();
        await vm.InicializarAsync();

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(IngresoFormViewModel.MensajeConceptoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.Concepto)));
        Assert.Equal(IngresoFormViewModel.MensajeMontoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
        Assert.Equal(IngresoFormViewModel.MensajeFuenteObligatoria, vm.ErrorDe(nameof(IngresoFormViewModel.FuenteSeleccionada)));
        Assert.Null(vm.ErrorDe(nameof(IngresoFormViewModel.FechaSeleccionada)));   // arranca en hoy
        Assert.Null(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<IngresoCaja>()), Times.Never);
    }

    [Fact]
    public async Task ErrorDelServidor_VaALaAlertaYNoAUnCampo()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<IngresoCaja>()))
            .ThrowsAsync(new ArgumentException("El monto del ingreso debe ser mayor a cero."));
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "0,00";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("El monto del ingreso debe ser mayor a cero.", vm.MensajeError);
        Assert.Null(vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
    }
```

Adaptar los heredados (convención 13; anotar en el ledger el antes y el después):
- `Guardar_MontoIlegible_MuestraErrorSinLlamarAlServicio`: `Assert.NotNull(vm.MensajeError);` pasa a `Assert.NotNull(vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto))); Assert.Null(vm.MensajeError);` (el `Verify(... Times.Never)` queda).
- `Guardar_MontoConPuntoDecimal_SeRechazaConMensajeClaroSinLlamarAlServicio`: `Assert.Equal("…: 1,5", vm.MensajeError);` pasa a `Assert.Equal("El monto no es un número válido. Usá coma para los decimales: 1,5", vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));` (el `Verify` queda).

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~IngresoFormViewModelTests"` → FAIL de compilación (`ErrorDe`, `MarcarTocado`, `GetErrors`, `HasErrors`, constantes). Pegar.

- [ ] **Step 2: Implementación**

En `IngresoFormViewModel`: agregar `using System.Collections;`, `using System.Collections.Generic;` y `using System.ComponentModel;`; cambiar la declaración a `public partial class IngresoFormViewModel : ViewModelBase, INotifyDataErrorInfo`; agregar `[NotifyCanExecuteChangedFor(nameof(GuardarCommand))]` a `_fechaSeleccionada`. Después de `FuentesDisponibles`:

```csharp
    public const string MensajeFechaObligatoria = "La fecha del ingreso es obligatoria.";
    public const string MensajeMontoObligatorio = "El monto es obligatorio.";
    public const string MensajeConceptoObligatorio = "El concepto es obligatorio.";
    public const string MensajeFuenteObligatoria = "Elegí la fuente de financiamiento.";

    /// <summary>Propiedades bindeadas que se validan en el cliente. ErrorsChanged se levanta con estos nombres: son los
    /// que el binding de cada control le pregunta a GetErrors.</summary>
    private static readonly string[] CamposValidados =
        [nameof(FechaSeleccionada), nameof(MontoTexto), nameof(Concepto), nameof(FuenteSeleccionada)];

    /// <summary>Campos que el usuario ya tocó (cambió el valor, salió del campo o intentó guardar). Un campo sin tocar
    /// no muestra su error: el formulario recién abierto no aparece en rojo.</summary>
    private readonly HashSet<string> _tocados = new();

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => CamposValidados.Any(c => ErrorDe(c) is not null);

    /// <summary>El error que la vista muestra debajo del campo: el de su regla, solo si el campo está tocado.</summary>
    public string? ErrorDe(string propiedad) => _tocados.Contains(propiedad) ? ReglaDe(propiedad) : null;

    public IEnumerable GetErrors(string? propertyName)
        => propertyName is not null && ErrorDe(propertyName) is { } error ? new[] { error } : Array.Empty<string>();

    /// <summary>Marca el campo como tocado sin cambiar su valor (la vista lo llama al salir de Concepto o de Monto).</summary>
    public void MarcarTocado(string propiedad)
    {
        if (_tocados.Add(propiedad))
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    private void AlCambiarCampo(string propiedad)
    {
        _tocados.Add(propiedad);
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    partial void OnFechaSeleccionadaChanged(DateTime? value) => AlCambiarCampo(nameof(FechaSeleccionada));
    partial void OnMontoTextoChanged(string value) => AlCambiarCampo(nameof(MontoTexto));
    partial void OnConceptoChanged(string value) => AlCambiarCampo(nameof(Concepto));
    partial void OnFuenteSeleccionadaChanged(FuenteFinanciamiento? value) => AlCambiarCampo(nameof(FuenteSeleccionada));

    /// <summary>Reglas del cliente (las mismas de antes: fecha, monto numérico es-UY, concepto y fuente obligatorios).
    /// "El monto debe ser mayor a cero" es del servidor y sale por la Alerta de error.</summary>
    private string? ReglaDe(string propiedad) => propiedad switch
    {
        nameof(FechaSeleccionada) => FechaSeleccionada is null ? MensajeFechaObligatoria : null,
        nameof(MontoTexto) => ReglaDelMonto(),
        nameof(Concepto) => string.IsNullOrWhiteSpace(Concepto) ? MensajeConceptoObligatorio : null,
        nameof(FuenteSeleccionada) => FuenteSeleccionada is null ? MensajeFuenteObligatoria : null,
        _ => null,
    };

    private string? ReglaDelMonto()
    {
        if (string.IsNullOrWhiteSpace(MontoTexto))
            return MensajeMontoObligatorio;
        return FormatoEsUy.TryParseDecimal(MontoTexto, out _, out var detalle)
            ? null
            : MensajeNumeroInvalido.Armar("El monto no es un número válido.", detalle);
    }
```

`PuedeGuardar` pasa a:

```csharp
    /// <summary>Guardar se habilita con todas las reglas del cliente cumplidas (estén o no tocados los campos) y con
    /// permiso.</summary>
    private bool PuedeGuardar() => !SinPermiso && CamposValidados.All(c => ReglaDe(c) is null);
```

Y el principio de `GuardarAsync` (desde `MensajeError = null;` hasta antes de `var ingreso = new IngresoCaja`) pasa a:

```csharp
        MensajeError = null;

        foreach (var campo in CamposValidados)
            MarcarTocado(campo);
        if (CamposValidados.Any(c => ReglaDe(c) is not null))
            return;

        FormatoEsUy.TryParseDecimal(MontoTexto, out var monto, out _);
```

(el resto de `GuardarAsync` no cambia: `FechaSeleccionada!.Value` ya no puede ser null ahí; cambiar `FechaSeleccionada.Value` por `FechaSeleccionada!.Value` si el compilador lo pide.)

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~ComandosDeGuardado"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~GuardarBloqueado|FullyQualifiedName~EtiquetadoDeCampos|FullyQualifiedName~GuardianDePatron"
```

Expected: verde. La vista v1 (`c:CampoFormulario` viejo) también recibe los errores por `INotifyDataErrorInfo`: si un UiTest heredado de la v1 se cae por eso, anotarlo y adaptarlo (convención 13) solo si custodia algo que la v2 vuelve a custodiar en la Task 8.

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (l) En `ErrorDe`, quitar `_tocados.Contains(propiedad) ? ` y ` : null` (queda `=> ReglaDe(propiedad);`) → rojo `AlAbrir_NingunCampoTieneError`. Restaurar.
- (m) En `GuardarAsync`, quitar las dos líneas `foreach (var campo in CamposValidados) MarcarTocado(campo);` → rojo `IntentarGuardarConCamposVacios_TocaTodosYNoLlamaAlServicio`. Restaurar.
- (n) En `MarcarTocado`, quitar la línea `ErrorsChanged?.Invoke(…)` → rojo `MarcarTocado_SinCambiarElValor_MuestraElErrorYLoNotifica`. Restaurar.
- (o) En `PuedeGuardar`, quitar ` && CamposValidados.All(c => ReglaDe(c) is null)` → rojo `GuardarCommand_DeshabilitadoConMontoInvalido` y `FechaBorrada_…`. Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/IngresoFormViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresoFormViewModelTests.cs
git commit -m "feat(finanzas): el formulario de ingreso valida por campo y marca el error al tocarlo"
```

---

## Bloque B — Datos fijos, medición y mockups (GATE 1 antes de todo XAML de vista)

### Task 5: Datos fijos de Ingresos de caja y su formulario

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosIngresos.cs`, `DatosIngresosTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: constructores y propiedades de las Tasks 1 a 4; `DatosShell.Crear(UsuarioSesion, string[], string, ViewModelBase?)`; `DatosProductos.UsuarioAdmin`.
- Produces: `DatosIngresos.Fuentes`, `Ingresos` (`IReadOnlyList<IngresoCaja>`), `UsuarioOperador`, `Listado(Func<Task<IReadOnlyList<IngresoCaja>>>, bool soloLectura = false)`, `Formulario()`; pantallas (→ `ShellMainViewModel`): `ConIngresos()`, `Cargando()`, `SinDatos()`, `SinResultados()`, `SinConexion()`, `SoloLectura()`, `ConInactivos()`, `Nuevo()`, `Editar()`, `ConErrorDeCampo()`, `ConErrorDelServidor()`; en el GATE 1 se agregó `SinPermisoAlInicializar()` (y `Formulario(bool fuentesConPermiso = true)`).

- [ ] **Step 1: Test (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosIngresosTests.cs`:

```csharp
using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Los mockups de ingresos escriben estos textos a mano: si un dato cambia y el mockup no, la comparación falla
/// por un texto distinto y el diagnóstico no lo dice.</summary>
public class DatosIngresosTests
{
    [Fact]
    public void Ingresos_TextosDelMockup()
    {
        var i = DatosIngresos.Ingresos;
        Assert.Equal(8, i.Count);
        Assert.Equal(2, i.Count(x => !x.Activo));
        Assert.Equal("06/10/2026", FechaCortaConverter.Formatear(DateOnly.FromDateTime(i[0].Fecha)));
        Assert.Equal("$ 1.250.000,00", FormatoEsUy.Moneda(i[0].Monto));
        Assert.Equal("$ 812.430,55", FormatoEsUy.Moneda(i[^1].Monto));
    }

    [Fact]
    public async Task Pie_PorDefectoYConTodos()
    {
        var vm = DatosIngresos.Listado(() => Task.FromResult(DatosIngresos.Ingresos));
        await vm.CargarAsync();
        Assert.Equal("6 ingresos · Total $ 6.610.730,55", vm.TextoPie);

        vm.MostrarInactivos = true;
        Assert.Equal("8 ingresos · 2 dados de baja · Total $ 6.610.730,55", vm.TextoPie);
    }

    [Fact]
    public void Formulario_AltaConFechaFija_YSinErrores()
    {
        var vm = DatosIngresos.Formulario();
        vm.InicializarAsync().GetAwaiter().GetResult();

        Assert.Equal(new DateTime(2026, 10, 6), vm.FechaSeleccionada);
        Assert.Equal(3, vm.FuentesDisponibles.Count);
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Formulario_EdicionConMontoEsUy()
    {
        var vm = DatosIngresos.Formulario();
        vm.CargarParaEditar(DatosIngresos.Ingresos[1]);

        Assert.Equal("48.300,00", vm.MontoTexto);
        Assert.Equal("Editar ingreso", vm.Titulo);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosIngresosTests"` → FAIL de compilación. Pegar.

- [ ] **Step 2: Implementación**

`DatosIngresos.cs`:

```csharp
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/ingreso*.html (textos fijados en DatosIngresosTests). El listado
/// viene ordenado como lo devuelve el servidor: fecha descendente y, a igual fecha, id descendente.</summary>
public static class DatosIngresos
{
    public static readonly IReadOnlyList<FuenteFinanciamiento> Fuentes =
    [
        new() { Id = 1, Nombre = "FIGM Literal B", Activo = true },
        new() { Id = 2, Nombre = "Rentas generales", Activo = true },
        new() { Id = 3, Nombre = "Préstamo BROU", Activo = true },
    ];

    private static IngresoCaja I(int id, int anio, int mes, int dia, string concepto, int fuente, decimal monto, bool activo = true) => new()
    {
        Id = id, Fecha = new DateTime(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc), Concepto = concepto,
        FuenteFinanciamientoId = fuente, FuenteFinanciamiento = Fuentes[fuente - 1], Monto = monto, Activo = activo,
    };

    public static readonly IReadOnlyList<IngresoCaja> Ingresos =
    [
        I(8, 2026, 10, 6, "Partida mensual FIGM octubre", 1, 1_250_000m),
        I(7, 2026, 10, 2, "Multas de tránsito setiembre", 2, 48_300m),
        I(6, 2026, 10, 2, "Multas de tránsito setiembre (duplicado)", 2, 48_300m, activo: false),
        I(5, 2026, 9, 15, "Préstamo BROU obras de pluviales", 3, 2_000_000m),
        I(4, 2026, 9, 5, "Partida mensual FIGM setiembre", 1, 1_250_000m),
        I(3, 2026, 8, 28, "Aporte vecinal plaza Artigas", 2, 15_000m, activo: false),
        I(2, 2026, 8, 5, "Partida mensual FIGM agosto", 1, 1_250_000m),
        I(1, 2026, 1, 2, "Saldo inicial del ejercicio", 2, 812_430.55m),
    ];

    public static readonly UsuarioSesion UsuarioOperador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");

    private static ICurrentSession Sesion(bool soloLectura)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(soloLectura ? RolUsuario.Operador : RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(soloLectura
            ? new HashSet<string> { Permisos.VerFinanzas }
            : new HashSet<string>());
        return sesion.Object;
    }

    public static IngresosViewModel Listado(Func<Task<IReadOnlyList<IngresoCaja>>> responder, bool soloLectura = false)
    {
        var servicio = new Mock<IIngresoCajaService>();
        servicio.Setup(s => s.ListarTodosAsync()).Returns(responder);
        return new IngresosViewModel(servicio.Object, Sesion(soloLectura), Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());
    }

    /// <summary>Formulario con las tres fuentes y la fecha del alta fija (el VM arranca en "hoy").</summary>
    public static IngresoFormViewModel Formulario()
    {
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(Fuentes);
        var vm = new IngresoFormViewModel(Mock.Of<IIngresoCajaService>(), fuentes.Object, Sesion(soloLectura: false), Mock.Of<INavigationService>())
        {
            FechaSeleccionada = new DateTime(2026, 10, 6),
        };
        return vm;
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm) => DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "Ingresos", vm);

    private static Task<IReadOnlyList<IngresoCaja>> Lista() => Task.FromResult(Ingresos);

    public static ShellMainViewModel ConIngresos() => EnShell(Listado(Lista));
    public static ShellMainViewModel Cargando() => EnShell(Listado(() => new TaskCompletionSource<IReadOnlyList<IngresoCaja>>().Task));
    public static ShellMainViewModel SinDatos() => EnShell(Listado(() => Task.FromResult<IReadOnlyList<IngresoCaja>>([])));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Listado(() => Task.FromException<IReadOnlyList<IngresoCaja>>(new ServidorNoDisponibleException())));

    public static ShellMainViewModel SinResultados()
    {
        var vm = Listado(Lista);
        vm.TextoBusqueda = "donación";
        return EnShell(vm);
    }

    public static ShellMainViewModel ConInactivos()
    {
        var vm = Listado(Lista);
        vm.MostrarInactivos = true;
        return EnShell(vm);
    }

    public static ShellMainViewModel SoloLectura() =>
        DatosShell.Crear(UsuarioOperador, [Permisos.VerFinanzas], "Ingresos", Listado(Lista, soloLectura: true));

    public static ShellMainViewModel Nuevo() => EnShell(Formulario());

    public static ShellMainViewModel Editar()
    {
        var vm = Formulario();
        vm.CargarParaEditar(Ingresos[1]);
        return EnShell(vm);
    }

    /// <summary>Concepto tocado y vacío, y un monto con punto decimal: los dos errores debajo de su campo.</summary>
    public static ShellMainViewModel ConErrorDeCampo()
    {
        var vm = Formulario();
        vm.MarcarTocado(nameof(IngresoFormViewModel.Concepto));
        vm.MontoTexto = "1.5";
        return EnShell(vm);
    }

    /// <summary>La regla del servidor ("mayor a cero") sale en la Alerta de error arriba del pie.</summary>
    public static ShellMainViewModel ConErrorDelServidor()
    {
        var vm = Formulario();
        vm.Concepto = "Partida mensual FIGM octubre";
        vm.MontoTexto = "0,00";
        vm.MensajeError = "El monto del ingreso debe ser mayor a cero.";
        return EnShell(vm);
    }
}
```

`ConErrorDelServidor` dejaba `FuenteSeleccionada` null; **en el GATE 1 (D14) pasó a cargar la Fuente "FIGM Literal B"** para que el formulario esté completo y Guardar habilitado. Confirmar que `MensajeError` tiene setter público (`[ObservableProperty]` lo genera) y que `UsuarioSesion` está en `StockApp.Application.Auth` (lo usa `DatosShell`).

- [ ] **Step 3: Verde**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosIngresosTests"` → PASS.

Mutación (e) de la Task 2 repetida sobre los datos: `cp` de `IngresosViewModel.cs` al `$SCRATCH`, quitar `.Where(i => i.Activo)` del total, correr el mismo filtro → rojo en `Pie_PorDefectoYConTodos` (la segunda aserción: 6.674.030,55), pegar, restaurar con `cp`, `git diff --no-index` vacío, verde.

- [ ] **Step 4: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosIngresos.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosIngresosTests.cs
git commit -m "test(ui-v2): datos fijos de ingresos de caja y su formulario"
```

### Task 6: Medición y mockups del listado y del formulario — GATE 1 con el usuario

**Decisiones aplicadas:** D1 a D16.

**Files:**
- Create: `docs/ui-v2/mockups/finanzas/ingresos.html`, `ingresos-cargando.html`, `ingresos-vacio.html`, `ingresos-sin-resultados.html`, `ingresos-sin-conexion.html`, `ingresos-solo-lectura.html`, `ingresos-con-inactivos.html`, `ingreso-nuevo.html`, `ingreso-editar.html`, `ingreso-error-campo.html`, `ingreso-error-servidor.html`
- Modify: `docs/ui-v2/mockups/_base/base.css` (solo si un mockup necesita geometría nueva; se declara en el GATE)
- Modify (local): el ledger (anchos y altos medidos)

**Interfaces:**
- Consumes: `DatosIngresos` (Task 5); `ColumnaAuto` y `DataGridColumnasAutoBehavior.AnchoMedido` (3c-1).
- Produces: las pantallas `finanzas-ingresos[-cargando|-vacio|-sin-resultados|-sin-conexion|-solo-lectura|-con-inactivos]` y `finanzas-ingreso-nuevo|-editar|-error-campo|-error-servidor`; en el ledger los anchos medidos que usan las Tasks 7 y 8.

- [ ] **Step 1: Medir en headless (archivo temporal, no se commitea)**

Crear `tests/StockApp.Presentation.VisualTests/MedicionTemporal4b.cs` siguiendo `MedicionTemporal4a` (plan de la 4a, Task 5 Step 1). Con el tema v2 (`Classes="ui2"`), a 1280×760 y 1366×768 (vista de 1040 y 1126), y `--logger "console;verbosity=detailed"`, escribe:
1. **Listado:** un `DataGrid` de prueba con las columnas de D4 sobre `DatosIngresos.Ingresos` (todas, también las inactivas): `AnchoMedido` de FECHA (`FechaCortaConverter`, `NoCede`), FUENTE y MONTO (`MonedaConverter`), y el ancho que le queda a CONCEPTO; el ancho de la `Insignia` "Inactivo" (debe dar 78, como ESTADO de Productos) y el X del inicio de la insignia en la fila 6 (texto + 8). Anotar si con CONCEPTO en su piso de 240 la grilla entra en 1040 − 48 sin scroll.
2. **Encabezado:** ancho del bloque de acciones con los tres botones y con la `Insignia` "Solo lectura" sola (para el mockup `-solo-lectura`); que el subtítulo de D1 entra en una línea a 1040.
3. **Barra de filtros:** el `ComboBox` de 140 con "Activos" y "Todos" (que el texto no se corte).
4. **Pie:** ancho de "8 ingresos · 2 dados de baja · Total $ 6.610.730,55" en `pie-conteo`.
5. **Formulario:** alto de la card en alta, en `-error-campo` (dos mensajes de error debajo de Concepto y de Monto: alto de cada `DataValidationErrors` con mensaje) y en `-error-servidor` (con la `Alerta`); X de MONTO (columna derecha) y ancho de FUENTE (columna izquierda) a 1280 y 1366. Confirmar que el `CalendarDatePicker` v2 dibuja el mensaje de `DataValidationErrors` debajo de la caja con `FechaSeleccionada = null` y el campo tocado; si NO lo dibuja, PARAR y reportarlo al controlador (es un cambio de tema que alcanza al Historial): no se parchea en silencio.

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~MedicionTemporal4b" --logger "console;verbosity=detailed"`. Anotar las líneas en el ledger.

- [ ] **Step 2: Mockups del listado**

Partir de `inventario/productos.html` (encabezado con acciones, card, buscador, grilla con `v2-num`, `.v2-grilla-fila--inactiva`, `.v2-insignia`) y de `inventario/productos-cargando.html` / `productos-vacio.html` / `historial.html` (combo con `.v2-glifo-combo`). Cada mockup:
- `<title>Finanzas: Ingresos de caja[ estado] (UI v2)</title>` y `<!-- Datos: DatosIngresos.<Pantalla>: … -->`.
- Sidebar con "Ingresos de caja" activo (se oculta en la comparación, pero el usuario aprueba la pantalla entera).
- `ingresos.html`: encabezado de D1 (Editar, Dar de baja, Nuevo ingreso), filtros de D2 (buscador vacío con su marca de agua, combo "Activos"), grilla de D4 con las 6 activas (anchos MEDIDOS como `flex: 0 0 Npx`; CONCEPTO flexible; FUENTE con elipsis si se corta), pie de D6 "6 ingresos · Total $ 6.610.730,55".
- `-con-inactivos`: combo "Todos", las 8 filas; las dos inactivas con `.v2-grilla-fila--inactiva` y la insignia "Inactivo" `.v2-insignia` neutra a 8 px del concepto; pie "8 ingresos · 2 dados de baja · Total $ 6.610.730,55".
- `-solo-lectura`: igual a `ingresos.html` pero el encabezado con solo la `Insignia` "Solo lectura" a la derecha (sin botones).
- `-sin-resultados`: buscador con "donación", combo "Activos", `.v2-vacio` "Ningún ingreso coincide con la búsqueda" / "Probá con otro concepto o elegí Todos.", sin grilla ni pie.
- `-vacio`: `.v2-vacio` "Todavía no hay ingresos" / "Cuando se registre un ingreso, va a aparecer acá." con el botón "Nuevo ingreso" `v2-boton--secundario`.
- `-cargando`: `.v2-cargando` "Cargando ingresos…" dentro de la card, con los filtros.
- `-sin-conexion`: `.v2-vacio` de error dentro de la card (título "No se pudo conectar con el servidor", mensaje de la convención 18) y "Reintentar" `secundario`, con los filtros.

- [ ] **Step 3: Mockups del formulario**

Partir de `inventario/producto-nuevo.html`, `producto-editar.html` y `producto-error.html` (breadcrumb, card de 760, `subtitulo-formulario`, separador, `.v2-alerta--error`, pie). Cada uno con el breadcrumb y el título de D11 y los campos de D11 (etiquetas "Fecha", "Monto", "Concepto", "Fuente de financiamiento" con `.v2-campo-requerido`), el ícono de calendario del `CalendarDatePicker` copiado tal cual de `inventario/historial.html` L118 (`<svg class="v2-icono v2-glifo-fecha" …>`, el mismo que usan `ingreso-factura*.html`) y el glifo del combo (`.v2-glifo-combo`, `historial.html` L114):
- `ingreso-nuevo.html`: Fecha "06/10/2026", Monto vacío con la marca de agua "0,00" a la derecha, Concepto vacío con su marca de agua, Fuente vacía con "Elegí la fuente".
- `ingreso-editar.html`: "Ingresos de caja › Editar ingreso", Fecha "02/10/2026", Monto "48.300,00", Concepto "Multas de tránsito setiembre", Fuente "Rentas generales".
- `ingreso-error-campo.html`: como el alta, con Concepto `.v2-input--error` + `.v2-error-campo` "El concepto es obligatorio." y Monto "1.5" `.v2-input--error` + `.v2-error-campo` "El monto no es un número válido. Usá coma para los decimales: 1,5" (el texto largo se mide: si no entra en la columna, el mockup lo deja partir en dos líneas igual que la app; anotar). El alto de cada mensaje, el MEDIDO en el Step 1.
- `ingreso-error-servidor.html`: Concepto "Partida mensual FIGM octubre", Monto "0,00", Fuente "FIGM Literal B" (corregido en el GATE 1, D14), Guardar habilitado y `.v2-alerta--error` "El monto del ingreso debe ser mayor a cero." arriba del pie con 8 de margen superior.
- `ingreso-sin-permiso.html` (agregado en el GATE 1, D16): como el alta, con `.v2-alerta--error` "Ya no tenés permiso para ver finanzas, así que no se puede registrar el ingreso." arriba del pie y Guardar deshabilitado.

- [ ] **Step 4: Capturas y verificación de la herramienta**

```bash
for p in ingresos ingresos-cargando ingresos-vacio ingresos-sin-resultados ingresos-sin-conexion ingresos-solo-lectura ingresos-con-inactivos ingreso-nuevo ingreso-editar ingreso-error-campo ingreso-error-servidor; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p@1366x768.png --tamano 1366x768
done
ls -l --time-style=full-iso artifacts/ui-v2/mockups/finanzas-ingreso*.png
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 22 PNG con fecha de ahora dentro del worktree; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0. MIRAR cada PNG.

- [ ] **Step 5: Borrar la medición temporal**

`rm tests/StockApp.Presentation.VisualTests/MedicionTemporal4b.cs` y `git status --short tests/` (no debe listarlo).

- [ ] **Step 6: GATE 1 — aprobación del usuario (PARADA)**

PARAR. Presentar los 22 PNG (LOS DOS TAMAÑOS), la lista D1 a D16 y los anchos medidos del ledger. Señalar explícitamente:
- D4: CONCEPTO `*` con la insignia y FUENTE `ColumnaAuto`, desvío de la spec (que pedía CONCEPTO con `ColumnaAuto`) y por qué.
- D13: el texto de la Alerta del 403 del formulario, que contradice la spec porque el único 403 posible ahí es por `finanzas.ver`. Pedir la elección: el texto propuesto o el de la spec.
- D5: el orden inicial es el del servidor y no se dibuja flecha.
- D6: el tramo "dados de baja" del pie solo aparece si hay alguno.
- D7: el botón "Nuevo ingreso" del vacío es secundario; el vacío "sin resultados" también cubre "Activos" con solo dados de baja.
- D8 y D9: selección que se limpia al filtrar; doble click solo con permiso, sobre fila activa y no en el encabezado.
- D12: cuándo se toca un campo y que Guardar se bloquea también con un monto inválido.
- Que el "Sin permiso" del listado y el 403 del formulario no tienen mockup propio.

No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en los mockups, en la constante y los tests de las Tasks 1 a 4 que la fijan (con su rojo y su verde) y en los tests de las Tasks 7 y 8, y volver a presentar. Registrar en el ledger: `Mockups 4b: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 7: Commit**

```bash
git add docs/ui-v2/mockups/finanzas/ingreso*.html
git commit -m "docs(ui-v2): mockups aprobados de ingresos de caja y su formulario"
```

(Sumar `docs/ui-v2/mockups/_base/base.css` si se tocó.)

---

## Bloque C — Vistas v2 (arranca solo con los mockups aprobados)

### Task 7: Listado de ingresos de caja v2

**Depende de:** Tasks 1, 2, 5 y 6.

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/IngresosView.axaml`, `IngresosView.axaml.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/IngresosFakes.cs`, `tests/StockApp.Presentation.UiTests/V2/IngresosViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/IngresosComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (L67, L117, `VistasMigradasAV2`), `IngresosViewTests.cs`, `FinanzasRevocacionPermisosTests.cs`, `GrillasSinRecorteBarridoTests.cs` (escenario "Ingresos de caja", solo si hace falta)

**Interfaces:**
- Consumes: `IngresosViewModel` (Tasks 1 y 2: `ItemsView`, `ItemSeleccionado`, `TextoBusqueda`, `EstadoSeleccionado`, `OpcionesEstado`, `CantidadVisible`, `TextoPie`, `Cargando`, `SinPermiso`, `MensajeSinPermiso`, `SinConexion`, `ReintentarCargaCommand`, `PuedeRegistrarIngresos`, `NuevoCommand`, `EditarCommand`, `BajaCommand`); `FechaCortaConverter`, `MonedaConverter`, `ConteoConverter`, `ColumnaAuto`, `TooltipSiSeCorta`; `SesionFake`, `NavigationRecorderFake`, `ConfirmacionServiceFake`, `NavigationServiceFake`, `AuthServiceFake`, `InfoAppFake`, `PreferenciasSidebarFake`, `ArbolVisual`, `MontajeV2`.
- Produces: `IngresoCajaFake` y `FuentesActivasFake` (`internal`, en `IngresosFakes.cs`; los usa la Task 8); en la vista, `x:Name` `Encabezado`, `InsigniaSoloLectura`, `BotonEditar`, `BotonBaja`, `BotonNuevo`, `EstadoSinPermiso`, `Tarjeta`, `Filtros`, `ComboEstado`, `PieConteo`, `IndicadorCarga`, `EstadoSinConexion`, `BotonReintentar`, `EstadoSinIngresos`, `BotonNuevoVacio`, `EstadoSinResultados`, `Grilla`.

- [ ] **Step 1: Fakes**

`tests/StockApp.Presentation.UiTests/V2/IngresosFakes.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Servicio de ingresos configurable: lista, carga que no termina, falla de la carga y falla al guardar, y
/// espías de altas, modificaciones y bajas. Lanza lo que lanza ApiErrores.</summary>
internal sealed class IngresoCajaFake : IIngresoCajaService
{
    public List<IngresoCaja> Lista { get; set; } = new();
    public TaskCompletionSource<IReadOnlyList<IngresoCaja>>? Pendiente { get; set; }
    public Exception? Falla { get; set; }
    public Exception? FallaAlGuardar { get; set; }
    public List<IngresoCaja> Altas { get; } = new();
    public List<IngresoCaja> Modificaciones { get; } = new();
    public List<int> Bajas { get; } = new();

    public Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync() =>
        Falla is not null ? Task.FromException<IReadOnlyList<IngresoCaja>>(Falla)
        : Pendiente is not null ? Pendiente.Task
        : Task.FromResult<IReadOnlyList<IngresoCaja>>(Lista.ToList());

    public Task<int> AltaAsync(IngresoCaja ingreso)
    {
        if (FallaAlGuardar is not null)
            return Task.FromException<int>(FallaAlGuardar);
        Altas.Add(ingreso);
        return Task.FromResult(99);
    }

    public Task ModificarAsync(IngresoCaja ingreso)
    {
        if (FallaAlGuardar is not null)
            return Task.FromException(FallaAlGuardar);
        Modificaciones.Add(ingreso);
        return Task.CompletedTask;
    }

    public Task BajaLogicaAsync(int id)
    {
        Bajas.Add(id);
        return Task.CompletedTask;
    }
}

/// <summary>Fuentes activas del formulario; con Falla, ListarActivasAsync la lanza (403 o servidor caído).</summary>
internal sealed class FuentesActivasFake : IFuenteFinanciamientoService
{
    public List<FuenteFinanciamiento> Activas { get; set; } = new();
    public Exception? Falla { get; set; }

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync() =>
        Falla is not null ? Task.FromException<IReadOnlyList<FuenteFinanciamiento>>(Falla)
        : Task.FromResult<IReadOnlyList<FuenteFinanciamiento>>(Activas.ToList());

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() => throw new NotSupportedException();
    public Task<int> AltaAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException();
    public Task ModificarAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException();
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
}
```

- [ ] **Step 2: Tests de la vista (rojo)**

`tests/StockApp.Presentation.UiTests/V2/IngresosViewV2Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Ingresos de caja v2 con la View REAL: estructura, columnas, filtros y pie, estados, inactivos por token,
/// gates con un Operador de permisos mixtos, doble click, baja y 403 en caliente por el camino del shell.</summary>
public class IngresosViewV2Tests
{
    private static readonly Color Terciario = Color.Parse("#78716C");

    private static readonly FuenteFinanciamiento Figm = new() { Id = 1, Nombre = "FIGM Literal B", Activo = true };

    private static IngresoCaja I(int id, string concepto, decimal monto, bool activo = true, int dia = 6) => new()
    {
        Id = id, Fecha = new DateTime(2026, 10, dia, 0, 0, 0, DateTimeKind.Utc), Concepto = concepto,
        FuenteFinanciamientoId = 1, FuenteFinanciamiento = Figm, Monto = monto, Activo = activo,
    };

    private static readonly IngresoCaja Partida = I(1, "Partida mensual FIGM octubre", 250_000m);
    private static readonly IngresoCaja Multas = I(2, "Multas de tránsito", 1_000m, dia: 2);
    private static readonly IngresoCaja Duplicado = I(3, "Multa duplicada", 5_000m, activo: false, dia: 1);

    private static SesionFake ConPermiso() => new(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarIngresos);
    private static SesionFake Mixto() => new(RolUsuario.Operador, Permisos.VerFinanzas);

    private sealed record Montaje(Window Ventana, IngresosView Vista, IngresosViewModel Vm, IngresoCajaFake Servicio,
        NavigationRecorderFake Navegacion, ConfirmacionServiceFake Confirmacion);

    private static Montaje Montar(IngresoCajaFake servicio, SesionFake? sesion = null, double ancho = 1040)
    {
        var navegacion = new NavigationRecorderFake();
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new IngresosViewModel(servicio, sesion ?? ConPermiso(), navegacion, confirmacion);
        var vista = new IngresosView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion, confirmacion);
    }

    private static Montaje Montar(params IngresoCaja[] ingresos) => Montar(new IngresoCajaFake { Lista = ingresos.ToList() });

    private static T Control<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);

    private static DataGridRow Fila(Montaje m, IngresoCaja ingreso) =>
        m.Vista.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, ingreso));

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Headless no lleva clickCount: el doble toque es la secuencia de dos clicks seguidos.</summary>
    private static void DobleClick(Window ventana, Control control)
    {
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        for (var i = 0; i < 2; i++)
        {
            ventana.MouseDown(p, MouseButton.Left);
            ventana.MouseUp(p, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock CeldaConTexto(Montaje m, string texto) =>
        Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == texto);

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTituloYSubtitulo_UnSoloPrimario()
    {
        var m = Montar(Partida);
        var encabezado = Control<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Ingresos de caja", encabezado.Titulo);
        Assert.Equal("Partidas, multas, préstamos y demás entradas de la caja municipal.", encabezado.Subtitulo);
        Assert.Contains("primario", Control<Button>(m, "BotonNuevo").Classes);
        Assert.Contains("secundario", Control<Button>(m, "BotonEditar").Classes);
        Assert.Contains("secundario", Control<Button>(m, "BotonBaja").Classes);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario") && Visible(b));
    }

    [AvaloniaFact]
    public void Columnas_EnOrdenYEnMayusculas_SinCheckDeActivo()
    {
        var m = Montar(Partida);
        var grilla = Control<DataGrid>(m, "Grilla");

        Assert.Equal(new object?[] { "FECHA", "CONCEPTO", "FUENTE", "MONTO" }, grilla.Columns.Select(c => c.Header).ToArray());
        Assert.DoesNotContain(grilla.Columns, c => c is DataGridCheckBoxColumn);
        Assert.True(grilla.CanUserSortColumns);
    }

    [AvaloniaFact]
    public void Fecha_DeMedianocheUtc_SeMuestraSinCorrerElDia_YMontoEnEsUy()
    {
        var m = Montar(Partida);

        Assert.True(Visible(CeldaConTexto(m, "06/10/2026")));
        Assert.True(Visible(CeldaConTexto(m, "$ 250.000,00")));
    }

    [AvaloniaFact]
    public void PorDefecto_SoloActivos_YPieConTotal()
    {
        var m = Montar(Partida, Multas, Duplicado);

        Assert.Equal("Activos", Control<ComboBox>(m, "ComboEstado").SelectedItem);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, Duplicado));
        Assert.Equal("2 ingresos · Total $ 251.000,00", Control<TextBlock>(m, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void ComboTodos_MuestraLosDadosDeBaja_ConTextoTerciarioEInsigniaSinOpacidad()
    {
        var m = Montar(Partida, Duplicado);

        Control<ComboBox>(m, "ComboEstado").SelectedItem = "Todos";
        Dispatcher.UIThread.RunJobs();

        var fila = Fila(m, Duplicado);
        var textos = fila.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Insignia>() is null).ToList();
        Assert.Equal(4, textos.Count); // fecha, concepto, fuente, monto
        Assert.All(textos, t => Assert.Equal(Terciario, Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color));
        var insignia = fila.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Inactivo");
        Assert.True(Visible(insignia));
        Assert.Equal(VarianteInsignia.Neutro, insignia.Variante);
        var grilla = Control<DataGrid>(m, "Grilla");
        foreach (var v in textos.Cast<Visual>().Append(insignia))
            for (Visual? a = v; a is not null && !ReferenceEquals(a, grilla); a = a.GetVisualParent())
                Assert.True(a.Opacity == 1, $"{a.GetType().Name} con Opacity {a.Opacity} en la fila inactiva");
        Assert.Equal("2 ingresos · 1 dado de baja · Total $ 250.000,00", Control<TextBlock>(m, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void InsigniaInactivo_QuedaOchoPixelesDespuesDelConcepto()
    {
        var m = Montar(Duplicado);
        m.Vm.MostrarInactivos = true;
        Dispatcher.UIThread.RunJobs();

        var fila = Fila(m, Duplicado);
        var texto = fila.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Multa duplicada");
        var insignia = fila.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Inactivo");
        var finTexto = texto.TranslatePoint(new Point(texto.Bounds.Width, 0), m.Ventana)!.Value.X;
        var inicioInsignia = insignia.TranslatePoint(new Point(0, 0), m.Ventana)!.Value.X;

        Assert.Equal(finTexto + 8, inicioInsignia, 1);
    }

    [AvaloniaFact]
    public void Buscador_FiltraPorConcepto_YActualizaElPie()
    {
        var m = Montar(Partida, Multas);
        var caja = Control<BarraFiltros>(m, "Filtros").GetVisualDescendants().OfType<TextBox>().First();

        caja.Text = "multas";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("multas", m.Vm.TextoBusqueda);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<DataGridRow>(), r => Visible(r));
        Assert.Equal("1 ingreso · Total $ 1.000,00", Control<TextBlock>(m, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void Cargando_IndicadorEnLaCard_ConLosFiltrosAMano()
    {
        var m = Montar(new IngresoCajaFake { Pendiente = new TaskCompletionSource<IReadOnlyList<IngresoCaja>>() });

        Assert.True(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.True(Visible(Control<BarraFiltros>(m, "Filtros")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinIngresos")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
    }

    [AvaloniaFact]
    public void SinDatos_VacioConBotonNuevoSecundario()
    {
        var m = Montar();

        var vacio = Control<EstadoVacio>(m, "EstadoSinIngresos");
        Assert.True(Visible(vacio));
        Assert.Equal("Todavía no hay ingresos", vacio.Titulo);
        var boton = Control<Button>(m, "BotonNuevoVacio");
        Assert.True(Visible(boton));
        Assert.Contains("secundario", boton.Classes);
        Assert.Same(m.Vm.NuevoCommand, boton.Command);
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
    }

    [AvaloniaFact]
    public void SinResultados_SuVacioYNoElDeSinDatos()
    {
        var m = Montar(Partida);

        m.Vm.TextoBusqueda = "donación";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.Equal("Ningún ingreso coincide con la búsqueda", Control<EstadoVacio>(m, "EstadoSinResultados").Titulo);
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinIngresos")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = new IngresoCajaFake { Falla = new ServidorNoDisponibleException() };
        var m = Montar(servicio);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinIngresos")));

        servicio.Falla = null;
        servicio.Lista = [Partida];
        Clickear(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLaCard()
    {
        var m = Montar(new IngresoCajaFake { Falla = new UnauthorizedAccessException("403") });

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinPermiso")));
        Assert.Equal(IngresosViewModel.MensajeSinPermisoIngresos, Control<EstadoVacio>(m, "EstadoSinPermiso").Mensaje);
        Assert.False(Visible(Control<TarjetaContenido>(m, "Tarjeta")));
    }

    [AvaloniaFact]
    public void Gates_OperadorMixto_SinAccionesYConSoloLectura()
    {
        var m = Montar(new IngresoCajaFake(), Mixto());

        Assert.False(Visible(Control<Button>(m, "BotonNuevo")));
        Assert.False(Visible(Control<Button>(m, "BotonEditar")));
        Assert.False(Visible(Control<Button>(m, "BotonBaja")));
        Assert.False(Visible(Control<Button>(m, "BotonNuevoVacio")));
        var insignia = Control<Insignia>(m, "InsigniaSoloLectura");
        Assert.True(Visible(insignia));
        Assert.Equal("Solo lectura", insignia.Texto);
    }

    [AvaloniaFact]
    public void Gates_OperadorConRegistrar_ConAccionesYSinSoloLectura()
    {
        var m = Montar(new IngresoCajaFake(), ConPermiso());

        Assert.True(Visible(Control<Button>(m, "BotonNuevo")));
        Assert.True(Visible(Control<Button>(m, "BotonEditar")));
        Assert.True(Visible(Control<Button>(m, "BotonBaja")));
        Assert.False(Visible(Control<Insignia>(m, "InsigniaSoloLectura")));
    }

    [AvaloniaFact]
    public void FilaInactivaSeleccionada_EditarYDarDeBajaDeshabilitados()
    {
        var m = Montar(Partida, Duplicado);
        m.Vm.MostrarInactivos = true;
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, Fila(m, Duplicado));

        Assert.Same(Duplicado, m.Vm.ItemSeleccionado);
        Assert.False(Control<Button>(m, "BotonEditar").IsEffectivelyEnabled);
        Assert.False(Control<Button>(m, "BotonBaja").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnFilaActiva_ConPermiso_Edita()
    {
        var m = Montar(Partida);

        DobleClick(m.Ventana, CeldaConTexto(m, Partida.Concepto));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal(typeof(IngresoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_OperadorMixto_NoEdita()
    {
        var m = Montar(new IngresoCajaFake { Lista = [Partida] }, Mixto());

        DobleClick(m.Ventana, CeldaConTexto(m, Partida.Concepto));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnFilaInactiva_NoEdita()
    {
        var m = Montar(Duplicado);
        m.Vm.MostrarInactivos = true;
        Dispatcher.UIThread.RunJobs();

        DobleClick(m.Ventana, CeldaConTexto(m, Duplicado.Concepto));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnElEncabezado_NoEdita()
    {
        var m = Montar(Partida);
        Clickear(m.Ventana, Fila(m, Partida));
        var encabezado = Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridColumnHeader>()
            .Single(h => Equals(h.Content, "CONCEPTO"));

        DobleClick(m.Ventana, encabezado);
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void DarDeBaja_ConfirmaConConceptoYMonto_YDaDeBaja()
    {
        var m = Montar(Partida);
        Clickear(m.Ventana, Fila(m, Partida));

        Clickear(m.Ventana, Control<Button>(m, "BotonBaja"));

        var pregunta = Assert.Single(m.Confirmacion.Preguntas);
        Assert.Equal("¿Confirma dar de baja el ingreso \"Partida mensual FIGM octubre\" ($ 250.000,00)?", pregunta);
        Assert.Equal(new[] { 1 }, m.Servicio.Bajas);
    }

    [AvaloniaFact]
    public async Task Un403_QueQuitaRegistrarIngresos_OcultaAccionesSinNavegar()
    {
        var sesion = ConPermiso();
        var tras = new HashSet<string> { Permisos.VerFinanzas };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = new IngresosViewModel(new IngresoCajaFake { Lista = [Partida] }, sesion, new NavigationServiceFake(),
            new ConfirmacionServiceFake());
        shell.CurrentContent = vm;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        var vista = ventana.GetVisualDescendants().OfType<IngresosView>().Single();
        Assert.True(Visible(MontajeV2.Parte<Button>(vista, "BotonNuevo")), "Precondición: con finanzas.ingresos se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(MontajeV2.Parte<Button>(vista, "BotonNuevo")));
        Assert.False(Visible(MontajeV2.Parte<Button>(vista, "BotonEditar")));
        Assert.False(Visible(MontajeV2.Parte<Button>(vista, "BotonBaja")));
        Assert.True(Visible(MontajeV2.Parte<Insignia>(vista, "InsigniaSoloLectura")));
        Assert.Same(vm, shell.CurrentContent);
    }
}
```

Antes de escribirlo, confirmar con CodeGraph: (1) `MontajeV2.Parte<T>(Control, string)` busca en el árbol VISUAL (`MontajeV2.cs` L46-47): una parte dentro de una card que nunca se mostró no está ahí; los tests de arriba solo la usan con la card visible (en `Un403_SinPermisoEnLugarDeLaCard` se busca la card misma, que sí está); si un test nuevo necesita una parte de una card oculta, usar el `Visible(Montaje, string)` por árbol lógico de `ProductoListViewV2Tests` L127-131 y anotarlo; (2) la firma de `AuthServiceFake(SesionFake, IReadOnlySet<string>)` y de `ManejadorAccesoRevocado.ManejarAsync` (las usa `CalendarioPagosViewV2Tests` L172-200, copiarlas de ahí); (3) que `ConfirmacionServiceFake.Preguntas` existe (`MovimientoRegistroFakes.cs` L88).

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~IngresosViewV2Tests"` → FAIL (las partes no existen en la vista v1). Pegar.

- [ ] **Step 3: Vista v2**

`IngresosView.axaml` completo (los anchos son los MEDIDOS en la Task 6; `MinWidth` 240 de CONCEPTO según D4):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:b="using:StockApp.Presentation.Behaviors"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Finanzas.IngresosView"
             x:DataType="vm:IngresosViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (listado §5.1). Espejo: docs/ui-v2/mockups/finanzas/ingresos*.html. Editar y Dar de baja actúan sobre la
         fila seleccionada y solo si está activa; doble click en una fila equivale a Editar (code-behind). Sin
         finanzas.ingresos los tres botones se ocultan y aparece Solo lectura. El filtro corre en memoria (la API devuelve
         todo): el pie cuenta lo visible y el total suma solo los activos. Cargando, sin servidor y los dos vacíos dentro
         de la card con los filtros a mano; tras un 403, Sin permiso en lugar de la card. -->
    <DockPanel Margin="28,24">

        <v2:EncabezadoPagina x:Name="Encabezado" DockPanel.Dock="Top" Titulo="Ingresos de caja"
                             Subtitulo="Partidas, multas, préstamos y demás entradas de la caja municipal.">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <v2:Insignia x:Name="InsigniaSoloLectura" Texto="Solo lectura" Variante="Neutro" VerticalAlignment="Center"
                             IsVisible="{Binding !PuedeRegistrarIngresos}" />
                <Button x:Name="BotonEditar" Classes="secundario" Content="Editar" Command="{Binding EditarCommand}"
                        IsVisible="{Binding PuedeRegistrarIngresos}" />
                <Button x:Name="BotonBaja" Classes="secundario" Content="Dar de baja" Command="{Binding BajaCommand}"
                        IsVisible="{Binding PuedeRegistrarIngresos}" />
                <Button x:Name="BotonNuevo" Classes="primario" Content="Nuevo ingreso" Command="{Binding NuevoCommand}"
                        IsVisible="{Binding PuedeRegistrarIngresos}" />
            </StackPanel>
        </v2:EncabezadoPagina>

        <v2:EstadoVacio x:Name="EstadoSinPermiso" DockPanel.Dock="Top" UseLayoutRounding="False"
                        Titulo="Sin permiso" Mensaje="{Binding MensajeSinPermiso}" EsError="True"
                        IsVisible="{Binding SinPermiso}" />

        <v2:TarjetaContenido x:Name="Tarjeta" IsVisible="{Binding !SinPermiso}">
            <DockPanel>
                <v2:BarraFiltros x:Name="Filtros" DockPanel.Dock="Top"
                                 TextoBusqueda="{Binding TextoBusqueda}"
                                 MarcaAguaBusqueda="Buscar por concepto…">
                    <ComboBox x:Name="ComboEstado" Width="140"
                              ItemsSource="{x:Static vm:IngresosViewModel.OpcionesEstado}"
                              SelectedItem="{Binding EstadoSeleccionado}" />
                </v2:BarraFiltros>

                <TextBlock x:Name="PieConteo" DockPanel.Dock="Bottom" Classes="pie-conteo" Margin="0,12,0,0"
                           Text="{Binding TextoPie}">
                    <TextBlock.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="CantidadVisible" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                        </MultiBinding>
                    </TextBlock.IsVisible>
                </TextBlock>

                <v2:EstadoCargando x:Name="IndicadorCarga" DockPanel.Dock="Top" Texto="Cargando ingresos…">
                    <v2:EstadoCargando.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" />
                            <Binding Path="Items.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                        </MultiBinding>
                    </v2:EstadoCargando.IsVisible>
                </v2:EstadoCargando>

                <v2:EstadoVacio x:Name="EstadoSinConexion" DockPanel.Dock="Top" UseLayoutRounding="False"
                                Titulo="No se pudo conectar con el servidor"
                                Mensaje="Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar."
                                EsError="True" IsVisible="{Binding SinConexion}">
                    <Button x:Name="BotonReintentar" Classes="secundario" Content="Reintentar" Command="{Binding ReintentarCargaCommand}" />
                </v2:EstadoVacio>

                <!-- Sin datos: el botón es secundario porque el primario de la vista ya está en el encabezado. -->
                <v2:EstadoVacio x:Name="EstadoSinIngresos" DockPanel.Dock="Top" UseLayoutRounding="False"
                                Titulo="Todavía no hay ingresos"
                                Mensaje="Cuando se registre un ingreso, va a aparecer acá.">
                    <v2:EstadoVacio.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinPermiso" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="Items.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                        </MultiBinding>
                    </v2:EstadoVacio.IsVisible>
                    <Button x:Name="BotonNuevoVacio" Classes="secundario" Content="Nuevo ingreso" Command="{Binding NuevoCommand}"
                            IsVisible="{Binding PuedeRegistrarIngresos}" />
                </v2:EstadoVacio>

                <!-- Hay ingresos pero el filtro no deja ver ninguno (también con Activos y una lista de solo dados de baja). -->
                <v2:EstadoVacio x:Name="EstadoSinResultados" DockPanel.Dock="Top" UseLayoutRounding="False"
                                Titulo="Ningún ingreso coincide con la búsqueda"
                                Mensaje="Probá con otro concepto o elegí Todos.">
                    <v2:EstadoVacio.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="Items.Count" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                            <Binding Path="CantidadVisible" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                        </MultiBinding>
                    </v2:EstadoVacio.IsVisible>
                </v2:EstadoVacio>

                <!-- Plantillas para marcar cada TextBlock con Classes.inactivo (texto terciario por token, nunca Opacity).
                     FECHA y MONTO miden su contenido sobre todas las filas; FUENTE también, con tope. CONCEPTO es la
                     columna proporcional: lleva la insignia Inactivo pegada al texto, que ColumnaAuto no sabría medir.
                     La fecha es calendario: FechaCortaConverter no la pasa a hora local. -->
                <DataGrid x:Name="Grilla" ItemsSource="{Binding ItemsView}" SelectedItem="{Binding ItemSeleccionado}"
                          DoubleTapped="OnFilaDobleClick" IsReadOnly="True" CanUserSortColumns="True" CanUserResizeColumns="True"
                          ScrollViewer.HorizontalScrollBarVisibility="Auto">
                    <DataGrid.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="CantidadVisible" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                        </MultiBinding>
                    </DataGrid.IsVisible>
                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="FECHA" Width="Auto" SortMemberPath="Fecha"
                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.NoCede="True"
                                                b:ColumnaAuto.Conversor="{x:Static conv:FechaCortaConverter.Instance}">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:IngresoCaja">
                                    <TextBlock Text="{Binding Fecha, Converter={x:Static conv:FechaCortaConverter.Instance}}"
                                               Classes.inactivo="{Binding !Activo}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="CONCEPTO" Width="*" MinWidth="240" SortMemberPath="Concepto">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:IngresoCaja">
                                    <!-- Grid a la izquierda con la columna * y la insignia Auto: el texto mide lo que pide
                                         y la insignia queda 8 px después; si no entra, el texto se corta y la insignia se ve. -->
                                    <Grid ColumnDefinitions="*,Auto" HorizontalAlignment="Left" VerticalAlignment="Center">
                                        <TextBlock Grid.Column="0" Text="{Binding Concepto}" Classes.inactivo="{Binding !Activo}"
                                                   TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Concepto}"
                                                   b:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                        <v2:Insignia Grid.Column="1" Margin="8,0,0,0" Texto="Inactivo" Variante="Neutro"
                                                     IsVisible="{Binding !Activo}" />
                                    </Grid>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="FUENTE" Width="Auto" SortMemberPath="FuenteFinanciamiento.Nombre"
                                                b:ColumnaAuto.Medir="True">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:IngresoCaja">
                                    <TextBlock Text="{Binding FuenteFinanciamiento.Nombre, FallbackValue='—', TargetNullValue='—'}"
                                               Classes.inactivo="{Binding !Activo}"
                                               TextTrimming="CharacterEllipsis"
                                               ToolTip.Tip="{Binding FuenteFinanciamiento.Nombre, FallbackValue='—', TargetNullValue='—'}"
                                               b:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="MONTO" Width="Auto" CellStyleClasses="num" SortMemberPath="Monto"
                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:IngresoCaja">
                                    <TextBlock Text="{Binding Monto, Converter={x:Static conv:MonedaConverter.Instance}}"
                                               Classes.inactivo="{Binding !Activo}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>
            </DockPanel>
        </v2:TarjetaContenido>
    </DockPanel>

</UserControl>
```

`IngresosView.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class IngresosView : UserControl
{
    public IngresosView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresosViewModel vm)
                await vm.CargarAsync();
        };
    }

    /// <summary>
    /// Doble click en una fila = Editar. Solo sobre una fila (un doble click en el encabezado ordena, no edita), solo con
    /// permiso de registrar ingresos (el botón Editar está oculto sin él) y solo si Editar se puede ejecutar (fila activa).
    /// </summary>
    private void OnFilaDobleClick(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is null)
            return;
        if (DataContext is IngresosViewModel { PuedeRegistrarIngresos: true } vm && vm.EditarCommand.CanExecute(null))
            vm.EditarCommand.Execute(null);
    }
}
```

Tests heredados (convención 13):
- `GuardianDePatronTests`: quitar `[InlineData(typeof(IngresosView), "Ingresos de caja", "FINANZAS")]` (L67) y `typeof(IngresosView),` de `VistasDeLaTanda` (L117); agregar `typeof(IngresosView),` a `VistasMigradasAV2`. Reemplazo: `Raiz_TieneUi2_EncabezadoConTituloYSubtitulo_UnSoloPrimario`.
- `IngresosViewTests`: `BuscarBotonPorTexto(window, "Nuevo ingreso")` pasa a `window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BotonNuevo")` (con la lista vacía hay dos botones "Nuevo ingreso": el del encabezado y el del vacío); `BuscarTextoPorContenido(window, "Solo lectura").IsVisible` pasa a `ArbolVisual.EsVisibleEnArbol(window.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Solo lectura"))` (el `TextBlock` interno de una insignia oculta sigue con `IsVisible` true). Los cinco tests conservan su nombre y lo que afirman.
- `FinanzasRevocacionPermisosTests`: los mismos dos helpers, mismo cambio.
- `GrillasSinRecorteBarridoTests` escenario "Ingresos de caja": correrlo. Si una columna medida o el concepto con la insignia aparecen recortados, la medición de la Task 6 estaba mal: re-medir, no relajar el test.

- [ ] **Step 4: Build, verde y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~FinanzasRevocacion|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~GrillasEspacioDerecho|FullyQualifiedName~DataGridColumnas|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~AccesoRevocado"
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~ComandosDeGuardado"
```

- [ ] **Step 5: Comparaciones**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/IngresosComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Ingresos de caja dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class IngresosComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void Ingresos_IgualAlMockup() => Comparar(DatosIngresos.ConIngresos(), "finanzas/ingresos.html");
    [AvaloniaFact] public void Ingresos_IgualAlMockup_1366x768() => Comparar(DatosIngresos.ConIngresos(), "finanzas/ingresos.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosCargando_IgualAlMockup() => Comparar(DatosIngresos.Cargando(), "finanzas/ingresos-cargando.html");
    [AvaloniaFact] public void IngresosCargando_IgualAlMockup_1366x768() => Comparar(DatosIngresos.Cargando(), "finanzas/ingresos-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosVacio_IgualAlMockup() => Comparar(DatosIngresos.SinDatos(), "finanzas/ingresos-vacio.html");
    [AvaloniaFact] public void IngresosVacio_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SinDatos(), "finanzas/ingresos-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosSinResultados_IgualAlMockup() => Comparar(DatosIngresos.SinResultados(), "finanzas/ingresos-sin-resultados.html");
    [AvaloniaFact] public void IngresosSinResultados_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SinResultados(), "finanzas/ingresos-sin-resultados.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosSinConexion_IgualAlMockup() => Comparar(DatosIngresos.SinConexion(), "finanzas/ingresos-sin-conexion.html");
    [AvaloniaFact] public void IngresosSinConexion_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SinConexion(), "finanzas/ingresos-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosSoloLectura_IgualAlMockup() => Comparar(DatosIngresos.SoloLectura(), "finanzas/ingresos-solo-lectura.html");
    [AvaloniaFact] public void IngresosSoloLectura_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SoloLectura(), "finanzas/ingresos-solo-lectura.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosConInactivos_IgualAlMockup() => Comparar(DatosIngresos.ConInactivos(), "finanzas/ingresos-con-inactivos.html");
    [AvaloniaFact] public void IngresosConInactivos_IgualAlMockup_1366x768() => Comparar(DatosIngresos.ConInactivos(), "finanzas/ingresos-con-inactivos.html", TamanoLienzo.Municipio);
}
```

Run: `scripts/ui-v2/comparar.sh Ingresos`. Expected: 14 filas APROBADAS con el umbral por defecto (11 teselas). Si una no entra: diagnosticar con `teselas.py`/`tinta.py`, corregir la vista o el mockup (un cambio de mockup ≤ 4 px no visible se anota; uno mayor vuelve al usuario); un `UmbralAprobado` mayor solo en un GATE con el usuario (`AprobadoPor = "Martin"`), nunca para tapar un defecto.

- [ ] **Step 6: Mutaciones (QUITANDO)**

- (p) Quitar `IsVisible="{Binding PuedeRegistrarIngresos}"` de `BotonEditar` → rojo `Gates_OperadorMixto_SinAccionesYConSoloLectura` y `Un403_QueQuitaRegistrarIngresos_…`. Pegar. Restaurar. (Spec: "el `IsVisible` del gate".)
- (q) Quitar `IsVisible="{Binding !SinPermiso}"` de `Tarjeta` → rojo `Un403_SinPermisoEnLugarDeLaCard`. Pegar. Restaurar. (Spec: "el estado `SinPermiso`".)
- (r) En `OnFilaDobleClick`, quitar `{ PuedeRegistrarIngresos: true } ` del patrón (queda `DataContext is IngresosViewModel vm && …`) → rojo `DobleClick_OperadorMixto_NoEdita`. Restaurar.
- (s) En `OnFilaDobleClick`, quitar las dos líneas del `if (… FindAncestorOfType<DataGridRow> …) return;` → rojo `DobleClick_EnElEncabezado_NoEdita`. Restaurar.
- (t) Quitar `Classes.inactivo="{Binding !Activo}"` del `TextBlock` de CONCEPTO → rojo `ComboTodos_MuestraLosDadosDeBaja_…`. Restaurar.
- (u) Quitar `IsVisible="{Binding PuedeRegistrarIngresos}"` de `BotonNuevoVacio` → rojo `Gates_OperadorMixto_…`. Restaurar.
- (v) Quitar la línea `<Binding Path="Items.Count" Converter="{x:Static conv:ConteoConverter.HayElementos}" />` del `MultiBinding` de `EstadoSinResultados` → rojo `SinDatos_VacioConBotonNuevoSecundario` (los dos vacíos a la vez). Restaurar.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/IngresosView.axaml src/StockApp.Presentation/Views/Finanzas/IngresosView.axaml.cs tests/StockApp.Presentation.UiTests/V2/IngresosFakes.cs tests/StockApp.Presentation.UiTests/V2/IngresosViewV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.UiTests/IngresosViewTests.cs tests/StockApp.Presentation.UiTests/FinanzasRevocacionPermisosTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/IngresosComparacionTests.cs
git commit -m "feat(ui-v2): ingresos de caja v2 con filtro, total de activos, estados y doble click para editar"
```

(Sumar `GrillasSinRecorteBarridoTests.cs` si cambió.)

### Task 8: Formulario de ingreso v2

**Depende de:** Tasks 3, 4, 5, 6 y 7 (`IngresoCajaFake`, `FuentesActivasFake`).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/IngresoFormView.axaml`, `IngresoFormView.axaml.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/IngresoFormViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/IngresoFormComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (L63, L113, `VistasMigradasAV2`)

**Interfaces:**
- Consumes: `IngresoFormViewModel` (Tasks 3 y 4: `Titulo`, `EsEdicion`, `CancelarCommand`, `GuardarCommand`, `FechaSeleccionada`, `MontoTexto`, `Concepto`, `FuenteSeleccionada`, `FuentesDisponibles`, `MensajeError`, `SinPermiso`, `MensajeSinPermiso`, `PuedeRegistrarIngresos`, `MarcarTocado`, `ErrorDe`, `MensajeSinPermisoFormulario`, constantes de error); `IngresoCajaFake`, `FuentesActivasFake` (Task 7).
- Produces: en la vista, `x:Name` `Encabezado`, `ColumnaFormulario`, `Tarjeta`, `CampoFecha`, `SelectorFecha`, `CampoMonto`, `CajaMonto`, `CampoConcepto`, `CajaConcepto`, `CampoFuente`, `ComboFuente`, `AlertaSinPermiso`, `AlertaError`, `PieFormulario`, `BotonCancelar`, `BotonGuardar`.

- [ ] **Step 1: Tests de la vista (rojo)**

`tests/StockApp.Presentation.UiTests/V2/IngresoFormViewV2Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Formulario de ingreso v2 con la View REAL: estructura del patrón ProductoFormView, errores por campo debajo
/// de cada control (nada en rojo al abrir), Alerta del servidor y del 403, gates con un Operador mixto y 403 en caliente.</summary>
public class IngresoFormViewV2Tests
{
    private static readonly FuenteFinanciamiento Figm = new() { Id = 1, Nombre = "FIGM Literal B", Activo = true };
    private static readonly FuenteFinanciamiento DeBaja = new() { Id = 9, Nombre = "Fondo de emergencia", Activo = false };

    private static readonly IngresoCaja Existente = new()
    {
        Id = 7, Fecha = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), Concepto = "Multas de tránsito setiembre",
        FuenteFinanciamientoId = 1, FuenteFinanciamiento = Figm, Monto = 48_300m, Activo = true,
    };

    private static SesionFake ConPermiso() => new(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarIngresos);

    private sealed record Montaje(Window Ventana, IngresoFormView Vista, IngresoFormViewModel Vm, IngresoCajaFake Servicio,
        NavigationRecorderFake Navegacion);

    private static Montaje Montar(IngresoCaja? edicion = null, FuentesActivasFake? fuentes = null, SesionFake? sesion = null,
        IngresoCajaFake? servicio = null)
    {
        servicio ??= new IngresoCajaFake();
        var navegacion = new NavigationRecorderFake();
        var vm = new IngresoFormViewModel(servicio, fuentes ?? new FuentesActivasFake { Activas = [Figm] }, sesion ?? ConPermiso(), navegacion);
        if (edicion is not null) vm.CargarParaEditar(edicion);
        var vista = new IngresoFormView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }

    private static T Parte<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static Rect EnVentana(Window ventana, Control c) => new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<string> Errores(Control c) => DataValidationErrors.GetErrors(c)?.Select(e => e?.ToString() ?? "") ?? [];

    private static void Completar(Montaje m)
    {
        m.Vm.FuenteSeleccionada = Figm;
        m.Vm.Concepto = "Partida mensual FIGM octubre";
        m.Vm.MontoTexto = "1.250.000,00";
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Raiz_TieneUi2_YBreadcrumbConElTituloDelVm()
    {
        var alta = Montar();
        var encabezado = Parte<EncabezadoPagina>(alta, "Encabezado");

        Assert.Contains("ui2", alta.Vista.Classes);
        Assert.Equal("Ingresos de caja", encabezado.BreadcrumbOrigen);
        Assert.Equal("Nuevo ingreso", encabezado.BreadcrumbActual);
        Assert.Equal("Nuevo ingreso", encabezado.Titulo);
        Assert.Same(alta.Vm.CancelarCommand, encabezado.BreadcrumbComando);

        Assert.Equal("Editar ingreso", Parte<EncabezadoPagina>(Montar(Existente), "Encabezado").Titulo);
    }

    [AvaloniaFact]
    public void Breadcrumb_ClickReal_VuelveAlListadoSinGuardar()
    {
        var m = Montar();
        Completar(m);
        var origen = Parte<EncabezadoPagina>(m, "Encabezado").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PART_BreadcrumbOrigen");

        Clickear(m.Ventana, origen);

        Assert.Equal(typeof(IngresosViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.Empty(m.Servicio.Altas);
    }

    [AvaloniaFact]
    public void Tarjeta_Mide760YQuedaALaIzquierda()
    {
        var m = Montar();
        var tarjeta = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta"));

        Assert.Equal(760, tarjeta.Width);
        Assert.Equal(28, tarjeta.X);
    }

    [AvaloniaFact]
    public void Campos_FechaYMontoArriba_ConceptoATodoElAncho_FuenteALaIzquierda()
    {
        var m = Montar();
        var fecha = EnVentana(m.Ventana, Parte<CampoFormulario>(m, "CampoFecha"));
        var monto = EnVentana(m.Ventana, Parte<CampoFormulario>(m, "CampoMonto"));
        var concepto = EnVentana(m.Ventana, Parte<CampoFormulario>(m, "CampoConcepto"));
        var fuente = EnVentana(m.Ventana, Parte<CampoFormulario>(m, "CampoFuente"));

        Assert.Equal(fecha.Y, monto.Y);
        Assert.Equal(fecha.Right + 16, monto.X, 1);
        Assert.Equal(fecha.X, concepto.X);
        Assert.Equal(monto.Right, concepto.Right, 1);
        Assert.True(concepto.Y > fecha.Bottom);
        Assert.Equal(fecha.X, fuente.X);
        Assert.Equal(fecha.Width, fuente.Width, 1);
        Assert.True(fuente.Y > concepto.Bottom);
    }

    [AvaloniaFact]
    public void Requeridos_LosCuatro_YMontoALaDerecha()
    {
        var m = Montar();

        Assert.All(new[] { "CampoFecha", "CampoMonto", "CampoConcepto", "CampoFuente" },
            n => Assert.True(Parte<CampoFormulario>(m, n).Requerido, n));
        Assert.Equal(Avalonia.Media.TextAlignment.Right, Parte<TextBox>(m, "CajaMonto").TextAlignment);
    }

    [AvaloniaFact]
    public void AlAbrir_NingunCampoEnRojo()
    {
        var m = Montar();

        Assert.False(DataValidationErrors.GetHasErrors(Parte<CalendarDatePicker>(m, "SelectorFecha")));
        Assert.False(DataValidationErrors.GetHasErrors(Parte<TextBox>(m, "CajaMonto")));
        Assert.False(DataValidationErrors.GetHasErrors(Parte<TextBox>(m, "CajaConcepto")));
        Assert.False(DataValidationErrors.GetHasErrors(Parte<ComboBox>(m, "ComboFuente")));
        Assert.False(m.Vm.HayErroresDeEntrada);
    }

    [AvaloniaFact]
    public void SalirDelConceptoVacio_MensajeDebajoDelCampo()
    {
        var m = Montar();
        var concepto = Parte<TextBox>(m, "CajaConcepto");

        concepto.Focus();
        Dispatcher.UIThread.RunJobs();
        Parte<TextBox>(m, "CajaMonto").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { IngresoFormViewModel.MensajeConceptoObligatorio }, Errores(concepto));
        var mensaje = concepto.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == IngresoFormViewModel.MensajeConceptoObligatorio);
        Assert.True(Visible(mensaje));
        Assert.True(EnVentana(m.Ventana, mensaje).Y >= EnVentana(m.Ventana, concepto).Y + 36 - 1);
    }

    [AvaloniaFact]
    public void MontoInvalido_MensajeEsUyDebajoDelCampo_YGuardarBloqueado()
    {
        var m = Montar();
        Completar(m);

        Parte<TextBox>(m, "CajaMonto").Text = "1.5";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "El monto no es un número válido. Usá coma para los decimales: 1,5" }, Errores(Parte<TextBox>(m, "CajaMonto")));
        Assert.True(m.Vm.HayErroresDeEntrada);
        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
        Assert.False(Visible(Parte<Alerta>(m, "AlertaError")));
    }

    [AvaloniaFact]
    public void CorregirElCampo_QuitaElErrorYHabilitaGuardar()
    {
        var m = Montar();
        Completar(m);
        var monto = Parte<TextBox>(m, "CajaMonto");
        monto.Text = "1.5";
        Dispatcher.UIThread.RunJobs();

        monto.Text = "1,5";
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(Errores(monto));
        Assert.False(m.Vm.HayErroresDeEntrada);
        Assert.True(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void FechaBorrada_MensajeEnElSelectorDeFecha()
    {
        var m = Montar();

        m.Vm.FechaSeleccionada = null;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { IngresoFormViewModel.MensajeFechaObligatoria }, Errores(Parte<CalendarDatePicker>(m, "SelectorFecha")));
    }

    [AvaloniaFact]
    public void Guardar_ClickReal_ConDatosValidos_DaDeAltaYVuelveAlListado()
    {
        var m = Montar();
        Completar(m);

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        var alta = Assert.Single(m.Servicio.Altas);
        Assert.Equal(1_250_000m, alta.Monto);
        Assert.Equal(typeof(IngresosViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Guardar_ConErrorDelServidor_AlertaArribaDelPie_YNoNavega()
    {
        var m = Montar(servicio: new IngresoCajaFake { FallaAlGuardar = new ArgumentException("El monto del ingreso debe ser mayor a cero.") });
        Completar(m);

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        var alerta = Parte<Alerta>(m, "AlertaError");
        Assert.True(Visible(alerta));
        Assert.Equal("El monto del ingreso debe ser mayor a cero.", alerta.Texto);
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.True(EnVentana(m.Ventana, alerta).Bottom <= EnVentana(m.Ventana, Parte<Border>(m, "PieFormulario")).Y);
        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Pie_CancelarSecundario_GuardarUnicoPrimario()
    {
        var m = Montar();

        Assert.Contains("secundario", Parte<Button>(m, "BotonCancelar").Classes);
        Assert.Contains("primario", Parte<Button>(m, "BotonGuardar").Classes);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario") && Visible(b));
    }

    [AvaloniaFact]
    public void SinPermisoAlInicializar_AlertaYGuardarDeshabilitado()
    {
        var m = Montar(fuentes: new FuentesActivasFake { Falla = new UnauthorizedAccessException("403") });
        Completar(m);

        var alerta = Parte<Alerta>(m, "AlertaSinPermiso");
        Assert.True(Visible(alerta));
        Assert.Equal(IngresoFormViewModel.MensajeSinPermisoFormulario, alerta.Texto);
        Assert.True(Visible(Parte<TarjetaContenido>(m, "Tarjeta")));
        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Gates_OperadorMixto_SinGuardar()
    {
        var m = Montar(sesion: new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas));

        Assert.False(Visible(Parte<Button>(m, "BotonGuardar")));
        Assert.True(Visible(Parte<Button>(m, "BotonCancelar")));
    }

    [AvaloniaFact]
    public void Edicion_ConFuenteDadaDeBaja_LaMuestraElegida()
    {
        var m = Montar(new IngresoCaja
        {
            Id = 8, Fecha = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), Concepto = "Aporte de emergencia",
            FuenteFinanciamientoId = 9, FuenteFinanciamiento = DeBaja, Monto = 10_000m, Activo = true,
        });

        Assert.Same(DeBaja, Parte<ComboBox>(m, "ComboFuente").SelectedItem);
        Assert.Contains(DeBaja, m.Vm.FuentesDisponibles);
    }

    [AvaloniaFact]
    public async Task Un403_QueQuitaRegistrarIngresos_OcultaGuardarSinNavegar()
    {
        var sesion = ConPermiso();
        var tras = new HashSet<string> { Permisos.VerFinanzas };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = new IngresoFormViewModel(new IngresoCajaFake(), new FuentesActivasFake { Activas = [Figm] }, sesion, new NavigationServiceFake());
        shell.CurrentContent = vm;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        var vista = ventana.GetVisualDescendants().OfType<IngresoFormView>().Single();
        Assert.True(Visible(MontajeV2.Parte<Button>(vista, "BotonGuardar")), "Precondición: con finanzas.ingresos se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(MontajeV2.Parte<Button>(vista, "BotonGuardar")));
        Assert.Same(vm, shell.CurrentContent);
    }
}
```

Antes de escribirlo, confirmar con CodeGraph: (1) que `DataValidationErrors.GetErrors` devuelve los objetos de error ya pasados por `ErrorValidacionConverter` (strings intactos: `ErrorValidacionConverter.cs`); (2) que `CalendarDatePicker.SelectedDate` y `ComboBox.SelectedItem` validan datos (`enableDataValidation`): si `FechaBorrada_MensajeEnElSelectorDeFecha` queda rojo por eso con la implementación hecha, PARAR y reportarlo con la evidencia (es un cambio de tema, ver Task 6 Step 1); (3) que `Focus()` en headless mueve el foco y levanta `LostFocus` (lo hace en `ProductoFormViewV2Tests` / `MovimientoFormControlV2Tests` si hay un test de foco; si no, mostrar la ventana con `ventana.Activate()` antes de enfocar y anotarlo).

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~IngresoFormViewV2Tests"` → FAIL. Pegar.

- [ ] **Step 2: Vista v2**

`IngresoFormView.axaml` completo:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Finanzas.IngresoFormView"
             x:DataType="vm:IngresoFormViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (formulario §5.2, patrón ProductoFormView). Espejo: docs/ui-v2/mockups/finanzas/ingreso-*.html. El
         breadcrumb vuelve con CancelarCommand (no guarda). Los errores de cada campo los dibuja el DataValidationErrors
         del control (el VM los publica por INotifyDataErrorInfo, solo con el campo tocado: nada en rojo al abrir); el
         error del servidor y el 403 de la carga de fuentes van en una Alerta arriba del pie. Guardar se oculta sin
         finanzas.ingresos. -->
    <ScrollViewer x:Name="Desplazamiento" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="28,24">

            <v2:EncabezadoPagina x:Name="Encabezado"
                                 Titulo="{Binding Titulo}"
                                 BreadcrumbOrigen="Ingresos de caja"
                                 BreadcrumbComando="{Binding CancelarCommand}"
                                 BreadcrumbActual="{Binding Titulo}" />

            <!-- Columna * con MaxWidth 760: la card mide 760 a la izquierda. UseLayoutRounding False en la grilla: con el
                 redondeo Avalonia infla en 1 px una columna acotada por MaxWidth; la card lo recupera. -->
            <Grid x:Name="ColumnaFormulario" UseLayoutRounding="False">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" MaxWidth="{StaticResource V2AnchoFormularioMax}" />
                </Grid.ColumnDefinitions>

                <v2:TarjetaContenido x:Name="Tarjeta" Padding="24" UseLayoutRounding="True">
                    <StackPanel>
                        <TextBlock Classes="subtitulo-formulario" Text="Datos del ingreso" Margin="0,0,0,12" />
                        <Grid ColumnDefinitions="*,16,*" RowDefinitions="Auto,16,Auto,16,Auto">
                            <v2:CampoFormulario x:Name="CampoFecha" Grid.Row="0" Grid.Column="0" Etiqueta="Fecha" Requerido="True">
                                <CalendarDatePicker x:Name="SelectorFecha"
                                                    SelectedDate="{Binding FechaSeleccionada}"
                                                    PlaceholderText="dd/mm/aaaa"
                                                    SelectedDateFormat="Custom"
                                                    CustomDateFormatString="dd/MM/yyyy"
                                                    HorizontalAlignment="Stretch"
                                                    beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoMonto" Grid.Row="0" Grid.Column="2" Etiqueta="Monto" Requerido="True">
                                <TextBox x:Name="CajaMonto" Text="{Binding MontoTexto}" PlaceholderText="0,00" TextAlignment="Right" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoConcepto" Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="3"
                                                Etiqueta="Concepto" Requerido="True">
                                <TextBox x:Name="CajaConcepto" Text="{Binding Concepto}" PlaceholderText="Ej.: Partida mensual FIGM julio" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoFuente" Grid.Row="4" Grid.Column="0"
                                                Etiqueta="Fuente de financiamiento" Requerido="True">
                                <ComboBox x:Name="ComboFuente"
                                          ItemsSource="{Binding FuentesDisponibles}"
                                          SelectedItem="{Binding FuenteSeleccionada}"
                                          PlaceholderText="Elegí la fuente"
                                          HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="e:FuenteFinanciamiento">
                                            <TextBlock Text="{Binding Nombre}" />
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                        </Grid>

                        <!-- La carga de fuentes respondió 403: se perdió finanzas.ver con el formulario abierto. Los campos
                             quedan a la vista y Guardar deshabilitado (el VM lo bloquea). -->
                        <v2:Alerta x:Name="AlertaSinPermiso" Margin="0,8,0,0" Variante="Error"
                                   Texto="{Binding MensajeSinPermiso}" IsVisible="{Binding SinPermiso}" />

                        <v2:Alerta x:Name="AlertaError" Margin="0,8,0,0" Variante="Error"
                                   Texto="{Binding MensajeError}"
                                   IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />

                        <Border x:Name="PieFormulario" Margin="0,20,0,0" Padding="0,16,0,0" BorderThickness="0,1,0,0"
                                BorderBrush="{DynamicResource V2SeparadorGrillaBrush}">
                            <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                                <Button x:Name="BotonCancelar" Classes="secundario" Content="Cancelar" Command="{Binding CancelarCommand}" />
                                <Button x:Name="BotonGuardar" Classes="primario" Content="Guardar" Command="{Binding GuardarCommand}"
                                        IsVisible="{Binding PuedeRegistrarIngresos}" />
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </v2:TarjetaContenido>
            </Grid>
        </StackPanel>
    </ScrollViewer>

</UserControl>
```

`IngresoFormView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class IngresoFormView : UserControl
{
    public IngresoFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresoFormViewModel vm)
                await vm.InicializarAsync();
        };

        // Salir de Concepto o de Monto sin escribir cuenta como tocarlo: el error de obligatorio aparece ahí y no recién
        // al guardar. Fecha y Fuente se tocan al cambiar (un combo pierde el foco al abrir su lista).
        CajaConcepto.LostFocus += (_, _) => (DataContext as IngresoFormViewModel)?.MarcarTocado(nameof(IngresoFormViewModel.Concepto));
        CajaMonto.LostFocus += (_, _) => (DataContext as IngresoFormViewModel)?.MarcarTocado(nameof(IngresoFormViewModel.MontoTexto));
    }
}
```

Tests heredados (convención 13):
- `GuardianDePatronTests`: quitar `[InlineData(typeof(IngresoFormView), null, "FINANZAS")]` (L63) y `typeof(IngresoFormView),` de `VistasDeLaTanda` (L113); agregar `typeof(IngresoFormView),` a `VistasMigradasAV2`. Reemplazo: `Raiz_TieneUi2_YBreadcrumbConElTituloDelVm`.
- `EtiquetadoDeCamposTests`, `GuardarBloqueadoConErroresDeEntradaTests` y `ComandosDeGuardadoGuardianTests`: correrlos; si alguno enumera vistas por XAML y no conoce la v2, adaptarlo como en la 4a (aceptar `(c|v2):CampoFormulario`) y anotarlo.

- [ ] **Step 3: Build, verde y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~EtiquetadoDeCampos|FullyQualifiedName~GuardarBloqueado|FullyQualifiedName~CampoFormularioV2|FullyQualifiedName~ControlesV2|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~FinanzasRevocacion"
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~Ingreso|FullyQualifiedName~ComandosDeGuardado"
```

- [ ] **Step 4: Comparaciones**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/IngresoFormComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Formulario de ingreso dentro del shell real, con el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class IngresoFormComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void IngresoNuevo_IgualAlMockup() => Comparar(DatosIngresos.Nuevo(), "finanzas/ingreso-nuevo.html");
    [AvaloniaFact] public void IngresoNuevo_IgualAlMockup_1366x768() => Comparar(DatosIngresos.Nuevo(), "finanzas/ingreso-nuevo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresoEditar_IgualAlMockup() => Comparar(DatosIngresos.Editar(), "finanzas/ingreso-editar.html");
    [AvaloniaFact] public void IngresoEditar_IgualAlMockup_1366x768() => Comparar(DatosIngresos.Editar(), "finanzas/ingreso-editar.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresoErrorCampo_IgualAlMockup() => Comparar(DatosIngresos.ConErrorDeCampo(), "finanzas/ingreso-error-campo.html");
    [AvaloniaFact] public void IngresoErrorCampo_IgualAlMockup_1366x768() => Comparar(DatosIngresos.ConErrorDeCampo(), "finanzas/ingreso-error-campo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresoErrorServidor_IgualAlMockup() => Comparar(DatosIngresos.ConErrorDelServidor(), "finanzas/ingreso-error-servidor.html");
    [AvaloniaFact] public void IngresoErrorServidor_IgualAlMockup_1366x768() => Comparar(DatosIngresos.ConErrorDelServidor(), "finanzas/ingreso-error-servidor.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresoSinPermiso_IgualAlMockup() => Comparar(DatosIngresos.SinPermisoAlInicializar(), "finanzas/ingreso-sin-permiso.html");
    [AvaloniaFact] public void IngresoSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SinPermisoAlInicializar(), "finanzas/ingreso-sin-permiso.html", TamanoLienzo.Municipio);
}
```

Run: `scripts/ui-v2/comparar.sh IngresoForm`. Expected: 10 filas APROBADAS con el umbral por defecto (mismo criterio que la Task 7 si no).

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (w) Quitar `IsVisible="{Binding PuedeRegistrarIngresos}"` de `BotonGuardar` → rojo `Gates_OperadorMixto_SinGuardar` y `Un403_QueQuitaRegistrarIngresos_OcultaGuardarSinNavegar`. Pegar. Restaurar. (Spec: "el `IsVisible` del gate".)
- (x) Quitar el elemento `<v2:Alerta x:Name="AlertaSinPermiso" … />` entero (sus dos líneas) → rojo `SinPermisoAlInicializar_AlertaYGuardarDeshabilitado` (la Alerta no está). Pegar. Restaurar. (Spec: "el estado `SinPermiso`" en el formulario; quitar solo su `IsVisible` dejaría una Alerta vacía siempre visible, que no es quitar la regla sino cambiarla.)
- (y) Quitar la línea `CajaConcepto.LostFocus += …` del code-behind → rojo `SalirDelConceptoVacio_MensajeDebajoDelCampo`. Restaurar.
- (z) Quitar `Requerido="True"` de `CampoFuente` → rojo `Requeridos_LosCuatro_YMontoALaDerecha`. Restaurar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/IngresoFormView.axaml src/StockApp.Presentation/Views/Finanzas/IngresoFormView.axaml.cs tests/StockApp.Presentation.UiTests/V2/IngresoFormViewV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/IngresoFormComparacionTests.cs
git commit -m "feat(ui-v2): formulario de ingreso v2 con errores por campo y aviso de permiso"
```

(Sumar los tests heredados adaptados si se tocaron.)

---

## Bloque D — Guardianes, verificación orgánica y cierre

### Task 9: Repaso de guardianes y de mutaciones; suite y comparación completas

**Files:**
- Modify (local): el ledger
- Modify: los tests que haga falta ajustar (cada ajuste con su rojo y su motivo)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la evidencia completa (suite, comparación, mutaciones) para el GATE 2 de la Task 10.

- [ ] **Step 1: Guardianes de la fase**

Confirmar con grep y CodeGraph, y anotar en el ledger:
1. `IngresosView` e `IngresoFormView` están en `VistasMigradasAV2` y en ninguna otra lista de `GuardianDePatronTests`; `Guardian_CubreTodasLasVistasDelEnsamblado` verde.
2. `VistaV2_NoUsaPiezasDelTemaViejo` cubre las dos (`SignoNegativoBrushConverter`, `ActivoOpacidadConverter`, `BadgeEstado`, `HeaderVista`, `c:CampoFormulario`, `c:EstadoVacio` ausentes).
3. `NingunaVista_TieneOverlaysDeModal` sigue verde (Dar de baja usa `IConfirmacionService`).
4. `ComandosDeGuardadoGuardianTests` verde con el `GuardarCommand` de `ComandoDeGuardado`.
5. Ningún `Classes="primary"`/`"secondary"`/`"caption"` ni `c:` del tema viejo en los dos `.axaml` (`grep -n 'Classes="primary"\|Classes="secondary"\|Classes="caption"\|<c:' src/StockApp.Presentation/Views/Finanzas/IngresosView.axaml src/StockApp.Presentation/Views/Finanzas/IngresoFormView.axaml` vacío).
6. Ningún `Opacity` en los dos `.axaml` (`grep -n Opacity` vacío).

Si un punto falla, se corrige en un commit propio con su rojo pegado.

- [ ] **Step 2: Repaso de mutaciones**

Confirmar en el ledger que quedaron PEGADOS los rojos de las reglas que pide la spec (sección 3): el `IsVisible` del gate (p, w), el filtro de inactivos en el total (e) y el estado `SinPermiso` (j, q, x); y las demás de la fase (a a d, f a i, k a o, r a v, y, z). Una sin rojo pegado se corre ahora.

- [ ] **Step 3: Build, suite completa, herramientas y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos `.ods` de `Infrastructure.Tests` / `Api.Tests` (confirmar por nombre contra los de la línea base); total ≥ línea base + nuevos − reemplazados (anotar número y reemplazos); los dos `--verificar` exit 0; TODAS las filas de `index.html` APROBADAS a los dos tamaños (las de fases anteriores sin cambios; si una se mueve, diagnosticarla: `FechaCortaConverter` cambió y lo usa el Calendario).

- [ ] **Step 4: Commit (solo si hubo ajustes)**

```bash
git add <archivos ajustados>
git commit -m "test(ui-v2): guardianes de la Fase 4b al día"
```

### Task 10: Verificación orgánica (GATE 2 con el usuario), docs de cierre y review final

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (convenciones 30 y 31)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, deuda, §6.5)
- Modify (local): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 4b cerrada en `feat/ui-v2` (sin push ni merge).

- [ ] **Step 1: Verificación orgánica (spec §6.4) — app real contra la base local, en WSLg a 1280×760**

Preparación:
- `docker start stockapp-pg` (queda andando al terminar). **Backup ANTES de crear datos:** `mkdir -p ~/stockapp-backups-local && docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-4b-$(date +%Y%m%d-%H%M%S).sql` (usuario y base de `~/.config/GestionMunicipal/conexion.json` o del `.env` local; verificar con `ls -l` que el archivo no está vacío). Anotar la ruta en el ledger.
- API del worktree: `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http` (escucha en `http://localhost:5043`, confirmarlo contra `conexion.json`). App: `dotnet run --project src/StockApp.Presentation`. `admin` / `Admin1234`.
- Operador de permisos MIXTOS: crear `f4bmixto` con `finanzas.ver` (sin `finanzas.ingresos`); anotar su id.
- Datos de prueba por la app (camino de dominio) con prefijo `PRUEBA-4B`: tres ingresos con conceptos que incluyan una tilde ("PRUEBA-4B Préstamo de prueba"), uno de ellos con fuente "Rentas generales" o la que exista; anotar los ids.
- Toolkit `scripts/gui-verificacion/`. MIRAR cada captura y comparar `md5sum` con la anterior (dos iguales cuando la pantalla debía cambiar = WSLg congelado: PARAR y pedir al usuario que reinicie WSL). Tras un diálogo modal la ventana principal puede quedar sin foco (recuperar con `xdotool windowminimize` + `windowmap`). El Aviso global puede quedar DETRÁS de la ventana: buscarlo con `xdotool search --name` antes de declarar un cuelgue.

Todo a **1280×760**; los pasos 1, 2 y 4 se repiten a 1366×768. Capturar cada pantalla.

1. **Alta (Admin):** Ingresos de caja → "Nuevo ingreso" → el formulario abre sin nada en rojo; salir de Concepto vacío → mensaje debajo; tipear Monto "1.5" → mensaje es-UY debajo y Guardar deshabilitado con su tooltip; corregir a "1,5" → se va; elegir fuente; Guardar → vuelve al listado con el ingreso arriba (fecha de hoy, sin corrimiento de día). Comparar a ojo con `ingreso-nuevo.html` e `ingreso-error-campo.html`.
2. **Listado (Admin):** buscar "prestamo" (sin tilde) → encuentra el de "Préstamo"; pie con el total; combo "Todos"; ordenar por FECHA, CONCEPTO, FUENTE y MONTO; doble click en una fila → edición con los datos cargados; cambiar el monto y Guardar → vuelve con el monto nuevo.
3. **Error del servidor:** editar un ingreso con monto "0,00" → Guardar → Alerta "El monto del ingreso debe ser mayor a cero." arriba del pie, sin navegar.
4. **Baja:** seleccionar un `PRUEBA-4B` → "Dar de baja" → confirmación con concepto y monto → aceptar → desaparece con "Activos"; con "Todos" aparece en gris con la insignia "Inactivo" y el pie lo cuenta como dado de baja sin sumarlo; seleccionarlo → Editar y Dar de baja deshabilitados; doble click no hace nada. Elegir una fila y tipear una búsqueda que la oculte → Editar y Dar de baja se deshabilitan.
5. **Solo lectura con `f4bmixto`:** el listado sin botones y con "Solo lectura"; doble click no abre nada.
6. **Revocación en caliente:** con Admin dando a `f4bmixto` también `finanzas.ingresos`, entrar como `f4bmixto` (con botones) y abrir el formulario de un alta; desde la sesión de Admin quitarle `finanzas.ingresos`; en la de `f4bmixto`, una acción que pegue al servidor (Guardar) dispara el 403 → tras cerrar el aviso, Guardar ya no se ve, sin navegar; volver al listado → sin botones y con "Solo lectura". Si alcanza el tiempo, repetir quitándole `finanzas.ver` con el formulario recién abierto (nuevo alta) para ver la Alerta del 403 (D13); si no hay forma de disparar la carga de fuentes después de quitar el permiso, anotarlo como no verificable a mano (la custodia el UiTest).
7. **Sin servidor:** con la API detenida (`kill` SOLO del pid de `StockApp.Api`, acotado por nombre; nunca un comando de alcance global), abrir Ingresos de caja → "No se pudo conectar con el servidor" con Reintentar en línea, sin modal global; levantar la API y "Reintentar" → carga. Anotar si salió un Aviso global y dónde.
8. **Limpieza:** dar de baja por la app (camino de dominio) los ingresos `PRUEBA-4B` que sigan activos y dar de baja al usuario `f4bmixto` por la pantalla de Administración de usuarios (los ingresos no se borran físicamente: la baja lógica es el camino del dominio, y LogsAuditoria los referencia). Listar los ids y PREGUNTAR al usuario si quiere además un borrado físico con SQL acotado por esos ids, SOLO contra el contenedor local `stockapp-pg` (nunca el VPS). Anotar en el ledger lo hecho. El contenedor queda andando.

Si un paso no se puede hacer por el entorno (WSLg caído o congelado, contenedor o API que no arrancan), PARAR y avisar con la evidencia (stderr, `docker ps -a`, md5 de las capturas), sin declarar la fase cerrada. Si el usuario necesita la PC, se pausa.

- [ ] **Step 2: GATE 2 — aprobación final del usuario (PARADA)**

PARAR. Presentar: `index.html` (las 24 filas de Ingresos y del formulario), las capturas de la verificación orgánica junto a los mockups, los rojos de las mutaciones (a) a (z), los tests heredados reemplazados o adaptados (con su reemplazo), los `UmbralAprobado` si los hubo y los cambios respecto de los mockups aprobados. No seguir sin "aprobado" explícito. Registrar en el ledger: `Fase 4b: aprobada por <quién> <fecha>`. Lo que el usuario observe se arregla en commits propios con TDD (rojo pegado) o va a la deuda con su OK.

- [ ] **Step 3: `herramientas.md` y spec**

`herramientas.md`, después de la 29: "Convenciones que agrega la Fase 4b (Ingresos de caja)" con la 30 y la 31 de este plan (como quedaron aprobadas).

Spec:
1. `**Estado:**`: "; Fase 4b (Ingresos de caja y su formulario) APROBADA por el usuario el <fecha>" y "4b" en la lista de fases implementadas; la fila de Finanzas de la tabla de §7.
2. §7, después de "Estado de la Fase 4a": "**Estado de la Fase 4b (aprobada el <fecha>).** Ingresos de caja v2 (filtro en el cliente por concepto y Activos/Todos con la convención 30, pie con el total de los activos visibles, inactivos por token con insignia, acciones sobre la selección y doble click para editar, estados completos) y su formulario v2 (patrón `ProductoFormView`, errores por campo con estado tocado según la convención 31, Alerta del 403 de la carga de fuentes), con E1 a E5. Comparado en 24 filas a los dos tamaños (<máximo de teselas densas>). Suite <total> en `<hash>`. Verificación orgánica: <resumen>. Decisiones del usuario: <D1 a D16 aprobadas o cambiadas, en especial D4 y D13>."
3. §6.5: cada compensación nueva medida en las Tasks 7 y 8 (si hubo), con su antes y después.
4. "**Deuda abierta tras la Fase 4b:**" con, como mínimo: la deuda que la spec ya anotó (adjunto activo de un gasto anulado → 4d; doble Aviso en un 403 de adjuntos, preexistente); el formulario de ingreso sin estado "sin conexión" en la carga de fuentes (sigue el modal global; convención 22 no aplicada); lo observado en el paso 7 (Aviso global); cualquier `UmbralAprobado`; los minors diferidos del ledger y los datos de prueba que hayan quedado.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 4b (ingresos de caja y su formulario)"
```

- [ ] **Step 4: Review final de la rama**

Review de todo el rango `<commit de este plan>..HEAD` (skill de ejecución: review de rama completa con el modelo más capaz), con la suite y las comparaciones ya verdes. Foco: los cinco puntos de "Review Focus", que la excepción a "VMs intactos" no exceda E1 a E5 (y D8), que ninguna pieza del tema viejo haya quedado en las dos vistas, que el cambio de `FechaCortaConverter` no movió el Calendario de pagos y que el `INotifyDataErrorInfo` no deja el formulario en rojo en ningún camino de apertura (alta, edición, edición con fuente de baja). Los hallazgos se resuelven en commits propios (TDD, con rojo pegado) o se anotan como deuda con el usuario. No se pushea ni se mergea.

---

## Fuera de alcance

- Migrar Libro caja (4c), `GastoFormView` / Gastos (4d) y `PagosGastoView` (4e).
- Cambios de API o de datos: `GET /finanzas/ingresos` sigue devolviendo todo; el filtro es del cliente. Las reglas de `IngresoCajaService` (permisos, validaciones, auditoría, no modificar un ingreso de baja, fuente de baja solo si cambió) no cambian.
- Reactivar un ingreso dado de baja (no existe en la v1).
- "Guardar y cargar otro" y adjuntos en el formulario (spec, sección 2).
- Estado "sin conexión" en línea para la carga de fuentes del formulario (sigue saliendo por el modal global; candidato a la convención 22 en una fase futura, va a la deuda).
- Modo oscuro, back-stack (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| Compiled bindings o la plantilla v2 de `CalendarDatePicker` no muestran el error de `INotifyDataErrorInfo` debajo de la caja. | Medición en la Task 6 Step 1 y `FechaBorrada_MensajeEnElSelectorDeFecha` (Task 8); si falla, PARAR y reportar (cambio de tema con impacto en el Historial), no parchear en silencio. |
| `INotifyDataErrorInfo` deja el formulario en rojo al abrir (un binding escribe de vuelta el valor inicial y lo marca tocado). | `AlAbrir_NingunCampoTieneError`, `Edicion_ConDatosCargados_SinErrores` (Task 4), `AlAbrir_NingunCampoEnRojo` (Task 8), mutación (l), comparación `ingreso-nuevo` e `ingreso-editar`. |
| La `Grid` del CONCEPTO con la insignia no corta el texto (la columna `*` de una `Grid` alineada a la izquierda no se comporta como se espera) y la insignia queda tapada o fuera de la celda. | `InsigniaInactivo_QuedaOchoPixelesDespuesDelConcepto` (Task 7), escenario "Ingresos de caja" de `GrillasSinRecorteBarridoTests` (alterna inactivos y conceptos largos), comparación `-con-inactivos`. |
| El cambio de `FechaCortaConverter` altera el Calendario de pagos. | El `DateOnly` sigue por el mismo camino; `FechaCortaConverterTests` heredados y la comparación completa de la Task 9. |
| El doble click edita sin permiso, sobre una inactiva o desde el encabezado. | Tres guardas (fila, permiso, `CanExecute`) y tres tests (Task 7), mutaciones (r) y (s). |
| Un 403 en caliente no oculta las acciones del listado o el Guardar del formulario. | `Un403_…` en las Tasks 7 y 8 con el camino real del shell y operador mixto; paso 6 de la verificación orgánica. |
| La verificación orgánica ensucia la base local. | Backup antes; prefijo `PRUEBA-4B`; ids anotados; limpieza por el camino del dominio (baja lógica por la app) y borrado físico solo con OK del usuario y SQL acotado; nunca el VPS. |
| WSLg se congela o pierde el foco y la verificación orgánica da falsos rojos. | md5 de capturas, recupero `windowminimize` + `windowmap`, distinción artefacto/bug por el UiTest headless; si se congela, PARAR y pedir el reinicio al usuario. |
| Un test heredado se borra sin reemplazo. | Convención 13: los reemplazos de `GuardianDePatronTests` L63 y L67 nombrados en las Tasks 7 y 8; `IngresosViewTests`, `FinanzasRevocacionPermisosTests` e `IngresoFormViewModelTests` se adaptan sin borrar; el cierre lista los reemplazos. |
