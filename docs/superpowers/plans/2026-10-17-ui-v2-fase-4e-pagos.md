# Fase 4e: Pagos del gasto (UI v2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar `PagosGastoView` a la UI v2 (cierra la excepción inversa del panel de adjuntos v2 dentro de un host v1), cerrar la deuda de servidor de adjuntos de pagos asignada a la 4e, limpiar los datos `PRUEBA-4D` y hacer el primer merge por bloques de `feat/ui-v2` a `main`.
**Architecture:** Cuatro bloques, el molde de la 4d reducido a UNA pantalla. (A) Servidor primero: `AdjuntoService.AgregarAPagoAsync` valida el pago (404 si no existe, 409 si está anulado) y `QuitarAsync` rechaza quitar un adjunto cuyo gasto o pago dueño está anulado (409), con un método de repositorio nuevo para leer un pago por id. (B) Medición y mockups con GATE 1 ANTES de tocar el ViewModel, para que el usuario resuelva las Q-* antes de escribir código que dependa de ellas. (C) `PagosGastoViewModel` (carga protegida, solo lectura por permiso o gasto anulado, panel solo con pago activo, origen del breadcrumb) y la vista v2 con sus datos fijos, UiTests con la View real y comparación. (D) Cierre de la fase (guardianes, suite, GATE 2 orgánico, limpieza, docs, review de la fase) y, en una task aparte, la review del bloque Finanzas y el merge `--ff-only` a `main` con push.
**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, CommunityToolkit.Mvvm, ASP.NET Core Minimal API + EF Core (`StockApp.Api.Tests` con `ApiFactory`, `StockApp.Infrastructure.Tests` con Testcontainers), Moq 4.20.72, xUnit v2 (`Application.Tests`, `Api.Tests`, `Infrastructure.Tests`, `Presentation.Tests`, `UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`Presentation.UiTests`, `Presentation.VisualTests`), Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4.
**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`: §5.1, §5.2, §6 (umbral §6.5), §7 (fila Finanzas, "Integración" con el merge por bloques), el bloque de la 4a "Sección 3. Panel de adjuntos" (excepción inversa temporal) y la "Deuda abierta tras la Fase 4d" (L586-601 en `7fd41cf`). La spec NO tiene un bloque de diseño de la 4e: lo que la spec no fija lo decide el usuario en el GATE 1 (D1 a D9 y Q-1 a Q-5, abajo). Convenciones 1 a 36: `docs/ui-v2/herramientas.md`. Molde de formato: `docs/superpowers/plans/2026-10-15-ui-v2-fase-4d-gastos.md`.

## Global Constraints

- Todos los comandos corren desde `/home/capua25/workspace/stockapp-worktrees/ui-v2` (rama `feat/ui-v2`, base: el commit de este plan), como `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <comando>`. Única excepción: la Task 6 hace el merge en el checkout de `main` (`/home/capua25/workspace/stockapp`), y solo ahí.
- Hay otros agentes escribiendo planes en este worktree: `git add` SIEMPRE por ruta exacta; prohibidos `git add -A`, `git add .`, `git add -f` y `commit -a`; antes de cada commit, `git diff --cached --name-only` muestra solo los archivos de la task.
- CodeGraph: este worktree tiene su PROPIO índice `.codegraph/`; para código C# usar primero la CLI `codegraph` dentro del worktree (`codegraph explore|query|node|callers|impact`), NUNCA el MCP `codegraph_explore` (responde con el índice del checkout principal); `grep`/Read como fallback, y Read directo para `.axaml`, markdown y HTML/CSS (CodeGraph no los indexa).
- Tema claro fijo; tokens de §2.1 sin valores nuevos; fila de grilla 40, header 36, input y botón 36; espaciado múltiplo de 4; sidebar 240 (la vista mide 1040 a 1280 y 1126 a 1366).
- `docs/ui-v2/tokens.json` es la fuente de `Tokens.axaml` (nunca a mano); toda task que toque `Themes/V2/`, `Controls/V2/` o `docs/ui-v2/` corre `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`.
- Formato es-UY: importes con `MonedaConverter` / `FormatoEsUy`, parseo con `FormatoEsUy.TryParseDecimal`, fechas "dd/MM/yyyy" con `FechaCortaConverter` (acepta el `DateTime` de fecha calendario de `PagoGasto.Fecha` sin pasarlo a hora local: un pago del 12/10 a medianoche UTC NO puede verse 11/10).
- Sin cambios de dominio ni de DTOs. Excepción declarada a "sin cambios de API" (deuda asignada a la 4e por la spec): dos reglas nuevas de `AdjuntoService` (Task 1) que NO cambian firmas, rutas ni DTOs, y el método nuevo `IGastoRepository.ObtenerPagoPorIdAsync` (interfaz interna de Application, sin migración). `GastoService` no cambia.
- VMs: cambian `PagosGastoViewModel` (Task 3), `AdjuntosPanelViewModel` SOLO en su recarga (turno de `ConsultaVigente`, Review Focus 1) y `CalendarioPagosViewModel` SOLO en la llamada a `ConfigurarVolver` (texto del origen). Si Q-1 = B, además `GastosViewModel.PagosAsync` y `CalendarioPagosViewModel.RegistrarPagoAsync` (ver Q-1). Ningún otro VM, servicio, endpoint, `ViewLocator` ni `INavigationService`.
- `x:DataType` / compiled bindings en la vista y en cada `DataTemplate` (`e:PagoGasto`); constantes con `{x:Static}`. Sin comentarios con `--` en `.axaml` ni `.csproj` (AVLN1001).
- Un solo botón primario en la vista: "Registrar pago"; todo lo demás `secundario`.
- Prohibidos en la vista v2: `Opacity`, `ActivoOpacidadConverter`, `SignoNegativoBrushConverter`, `BadgeEstado`, `HeaderVista`, `c:` del tema viejo, `Classes="primary|secondary|caption|seccion|card"`, `MargenVista`, `DangerBrush`, el estilo local `DataGridCell.sin-padding-vertical`.
- Gates de UI con la View real en `Presentation.UiTests`; permisos con un Operador de permisos MIXTOS (`new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarPagos)` y su variante sin `RegistrarPagos`), NUNCA Admin. Revocación en caliente por el camino real (`ManejadorAccesoRevocado.ManejarAsync` → `ShellMainViewModel.NotificarPermisosActualizados`) con la View real dentro de `ShellMainView` (molde: `GastoFormViewV2Tests.RevocacionDeRegistrarGastos_DentroDelShell_PasaASoloLecturaSinBorrarLoTipeado`).
- `ArbolVisual.EsVisibleEnArbol` falla fuerte con null: buscar el control por `x:Name` con `MontajeV2.Parte<T>` y dejar que un nombre que falta dé rojo.
- TDD en cada task: test primero, PEGAR el rojo real en el ledger, código mínimo, verde. Un test que nace verde se declara caracterización y su custodia es una mutación con rojo pegado. Toda task que toque UI o VM corre las suites COMPLETAS de `Presentation.Tests` y `Presentation.UiTests` (ruling R7 de la 4d).
- Mutaciones QUITANDO, nunca agregando: `export SCRATCH=~/.cache/stockapp-ui-v2-fase-4e && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas o acortadas; se corre el test y se PEGA el rojo; se restaura con `cp`; `git diff --no-index` vacío; verde. NUNCA `git checkout --` ni `git restore`.
- Stubs fieles al contrato real: `IGastoService.ObtenerPorIdAsync(int)` → `Task<Gasto>` con `Proveedor` y TODOS los `Pagos` (activos y anulados, como `GastoRepository.ObtenerPorIdAsync`); los fakes lanzan lo que lanza `ApiErrores`: `UnauthorizedAccessException` (401/403), `ServidorNoDisponibleException` (sin servidor), `ReglaDeNegocioException` con el `detail` (409), `EntidadNoEncontradaException` (404).
- Mockups (§6.1): `docs/ui-v2/mockups/finanzas/pagos*.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a los de D8 (y después a `DatosPagos`), celdas numéricas con `v2-num` y el valor en un `<span>`; se aprueban ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los calibrados (11 teselas, 1.68 %) a AMBOS tamaños; uno mayor solo con `UmbralAprobado(…, AprobadoPor: "Martin", …)` tras revisar cada tesela densa con el usuario (16 o más exige `MutacionPalidaEnRojo`); nunca para tapar un defecto.
- Convención 6: comparación DENTRO de `ShellMainView` (`DatosShell.Crear(usuario, permisos, "Gastos", vm)`), sidebar oculto en los dos renders; el modal con `ContenidoDeDialogo.Extraer(new ConfirmacionDialog(mensaje))`.
- Build obligatorio tras cada cambio de `.axaml` o de un `.cs` de `src/`: `dotnet build StockApp.sln` (la `.sln` entera). Tests de a UN `.csproj` por comando, con `--blame-hang-timeout 5m`; nunca dos `dotnet test` a la vez.
- Rojos FALSOS conocidos en este worktree: `.ods` ausentes en `Infrastructure.Tests` y `Api.Tests` (confirmar por nombre); `ApiStartupMigrationTests.Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests` intermitente; `DataGridColumnasAutoAjusteTests.F_GrillaGrande_ElBehaviorNoAgregaCostoPerceptible` flaky de tiempo. Cualquier otro rojo es real. `Api.Tests` e `Infrastructure.Tests` necesitan Docker.
- Línea base: suite 6103/6103 en `0a0cb5d` (cierre de la 4d); se re-mide en la Task 1 Step 1; solo baja por tests heredados REEMPLAZADOS (convención 13), listados con su reemplazo.
- Sin referencias al plan en el código (nada de "Task 3", "D4", "Q-2" en `.cs`/`.axaml`/`.css`/`.html`). Las referencias de línea (`L123`) son las del commit de este plan.
- Verificación GUI: toolkit `scripts/gui-verificacion/` (leer su README: gotchas de ventanas fantasma, selector GTK, `pkill -f`); `capturar.sh` NO es fail-closed: MIRAR cada captura y comparar `md5sum`; popups con `capturar-x11.py`; matar procesos por PID.
- Commits: conventional commits en español, uno por task, SIN `Co-Authored-By` ni atribución de IA; sin push, merge, rebase, amend, `git reflog expire`, `git gc` ni `git prune` (salvo el merge y el push de la Task 6, que son su objeto).
- GATE 1 (Task 2) y GATE 2 (Task 5) son paradas para el usuario: no se sigue sin un "aprobado" explícito.

## Review Focus

1. **Cambiar de pago mientras cargan los adjuntos del anterior.** Caso: el usuario elige el pago A y enseguida el B; la lista de A llega tarde y el panel muestra los comprobantes de A bajo B (y "Quitar" actuaría sobre el adjunto de otro pago). Esperado: solo la última inicialización del panel decide lo que se ve (convención 23). Test: `InicializarAsync_DosSeguidas_LaPrimeraQueTerminaTardeNoPisaLaSegunda` (Task 3, `AdjuntosPanelViewModelTests`).
2. **Registrar o anular deja seleccionado un pago que ya no existe en la lista.** Caso: tras registrar, `RefrescarAsync` reemplaza `Pagos`; el panel queda mostrando el pago viejo. Esperado: la selección se suelta y la card de adjuntos vuelve al texto de "Elegí un pago". Test: `RegistrarPago_TrasRefrescar_SueltaLaSeleccionYOcultaElPanel` (Task 3).
3. **Revocación de `finanzas.pagos` con la pantalla abierta deja los comandos ejecutables.** Caso: el 403 oculta los botones, pero `RegistrarPagoCommand`/`AnularPagoCommand` siguen con `CanExecute` true (Enter, un binding viejo). Esperado: los dos comandos re-evalúan `CanExecute` y dan false (ruling R9 de la 4d: la guarda vive en el VM). Tests: `NotificarPermisosActualizados_SinRegistrarPagos_LosComandosNoEjecutan` (Task 3) y `RevocacionDeRegistrarPagos_DentroDelShell_PasaASoloLecturaSinBorrarLoTipeado` (Task 4).
4. **La fecha del pago se ve un día antes.** Caso: `PagoGasto.Fecha` es medianoche UTC; un converter de "UTC a local" la muestra 11/10 en Uruguay. Esperado: "12/10/2026". Test: `Grilla_FechaDelPago_SinCorrimientoDeDia` (Task 4; asierta el texto pintado, no el binding).
5. **Entrar desde el Calendario y volver a Gastos.** Caso: el breadcrumb dice "Gastos y facturas" aunque `ConfigurarVolver` lleva al Calendario (o al revés). Esperado: el texto del origen y el destino de la vuelta salen de la misma llamada. Tests: `ConfigurarVolver_ConOrigen_ElBreadcrumbYLaVueltaCoinciden` (Task 3) y `DesdeElCalendario_ElBreadcrumbDiceCalendarioDePagos` (Task 4).

---

## Preguntas para el usuario (se resuelven en el GATE 1, Task 2, ANTES de tocar el ViewModel)

El plan está escrito con la opción A de cada una. Si el usuario elige otra, se aplica lo indicado en la columna B/C (con TDD) y se anota en el ledger. Las cinco están copiadas en el ledger.

| # | Pregunta (evidencia) | A (recomendada, la que implementa el plan) | Alternativa y qué cambia |
|---|---|---|---|
| Q-1 | **¿Pagos refresca permisos antes de navegar?** GATE 2 de la 4d: con solo `finanzas.pagos` quitado, el primer click en Pagos navegó a la v1 en solo lectura, sin Aviso, y después Pagos desapareció. Mecanismo (verificado): `GastosViewModel.PagosAsync` (L486-492) navega con el cache viejo; el refresco al navegar (`ShellMainViewModel.OnNavegacionCambiada` L345 → `RefrescarPermisosAsync` L393-399) corre en segundo plano y solo avisa al sidebar, no a la pantalla recién abierta (`NotificarPermisosActualizados` L423-427 reenvía solo en el camino del 403). | **No refrescar antes de navegar.** La pantalla v2 de pagos en solo lectura es un estado válido (se llega con `finanzas.ver`) y muestra la insignia "Solo lectura"; si quedara con el gate viejo, el primer Registrar da 403 → un Aviso → el formulario se oculta (el servidor protege siempre). Se anota en la spec como comportamiento aceptado. Costo: cero código. Tradeoff: el usuario cae en una pantalla que no esperaba, sin explicación más allá de la insignia. | **B:** `PagosAsync` (Gastos) y `RegistrarPagoAsync` (Calendario) refrescan con `RefrescoPermisos.DispararBestEffortAsync(() => _auth.ObtenerPermisosPropiosAsync(), …)`; si ya no hay `RegistrarPagos`, no navegan, llaman `NotificarPermisosActualizados()` y avisan con `AvisoAccesoRevocado.Resolver(antes, despues)` por `IConfirmacionService`. Inyecta `IAuthService` en los dos VMs (DI y fábricas de tests), un GET más por click; tests de VM por cada camino y su mutación. +1 task chica antes de la Task 3. **C:** `ShellMainViewModel.RefrescarPermisosAsync` además llama `CurrentContent?.NotificarPermisosActualizados()` cuando los permisos cambiaron: la pantalla recién abierta queda al día en todas las secciones, sin Aviso. Toca el shell (fuera de Finanzas); test en `ShellMainViewGatesTests` y mutación. |
| Q-2 | **Adjuntos de un pago anulado.** Hoy el panel se inicializa con cualquier pago seleccionado (`PagosGastoViewModel.OnPagoSeleccionadoChanged` L103-107) y, con la Task 1, el servidor rechaza agregar o quitar sobre un pago anulado. | **Ocultar el panel** con el pago anulado seleccionado y mostrar en su lugar "Este pago está anulado: sus adjuntos no se muestran." (coherente con U2 de la 4d, que oculta los adjuntos de un gasto anulado); el panel ni se inicializa. Tradeoff: no se puede "Ver" el comprobante de un pago anulado desde la app (sigue en la base). | **B:** mostrar el panel en solo lectura (Ver sí; Agregar y Quitar ocultos): `AdjuntosPanelViewModel` gana un flag `SoloLectura` que el host fija y `PuedeModificar` pasa a `!SoloLectura && <permiso>`; tests del panel y de la vista y una mutación más; mockup `pagos-pago-anulado` cambia. |
| Q-3 | **Pagos de un gasto anulado.** `GastosViewModel.PagosCommand` está habilitado sobre una fila anulada (`CanExecute = TieneSeleccion`, L485) y el repositorio rechaza registrar un pago en un gasto anulado (`IGastoRepository.RegistrarPagoAtomicoAsync`, doc L31-39); hoy la v1 muestra el formulario igual. | **Pantalla en solo lectura:** con el gasto anulado no se muestra la card "Registrar pago" ni el botón Anular de cada fila; insignias "Anulado" y "Solo lectura" y una `Alerta` Info "Este gasto está anulado: se pueden consultar sus pagos, pero no registrar ni anular." (mismo patrón que D12 de la 4d). Tradeoff: ninguno funcional. | **B:** además (o en lugar de) deshabilitar Pagos sobre una fila anulada en Gastos (`CanExecute` como `PuedeAnular`): el usuario ya no puede consultar los pagos anulados de un gasto anulado. Toca `GastosViewModel` y sus tests. |
| Q-4 | **Errores del formulario de registro.** La convención 31 (errores por campo con `INotifyDataErrorInfo`) rige los formularios de alta/edición v2; "Registrar pago" es una card de 3 campos dentro de una pantalla, y hoy valida al registrar (`RegistrarPagoAsync` L162-196, `MensajeError`). | **Conservar la validación al registrar** y mostrar `MensajeError` en una `v2:Alerta` Error debajo de la fila de campos (monto ilegible con `MensajeNumeroInvalido`, fecha obligatoria y los mensajes del servidor). Sin cambio de VM en esto. Tradeoff: inconsistente con Ingreso y Gasto (allá el error va debajo del campo). | **B:** errores por campo (convención 31) para Fecha y Monto: `INotifyDataErrorInfo`, estado "tocado", `ErroresDeEntradaBehavior` bloquea Registrar con su tooltip; +tests de VM y de vista con sus mutaciones (patrón Task 9 de la 4d). |
| Q-5 | **Cómo se limpian los datos `PRUEBA-4D` (y los `PRUEBA-4E` del GATE 2).** Ids en la spec: proveedor 8, fuente 2, rubro 2 (código 9940), línea POA 4; gastos 19 a 24 (20 y 23 anulados); movimientos 1599, 1600 (entrada SIN asociar: sumó 1 al stock de un producto real) y 1601; usuario `f4dmixto` (id 30). Precedentes: 4a y 4b dieron de baja al usuario porque `LogsAuditoria` lo referencia. | **Borrado físico acotado por ids** en UNA transacción contra `stockapp-pg` (nunca el VPS), con backup previo y listado de cada fila antes de borrar: adjuntos y su contenido, pagos, gastos, movimientos de stock, maestros `PRUEBA`; después "Recalcular stock" (Historial) del producto de los movimientos, comparando su stock con el de antes de la 4d; `f4dmixto` (y un `f4emixto` si se crea) queda dado de baja por la app. Tradeoff: es SQL a mano (acotado y en transacción); los logs de auditoría de esas operaciones quedan. | **B:** solo baja lógica por la app (anular gastos y pagos, desactivar maestros): sin SQL, pero las filas `PRUEBA` siguen apareciendo en los listados de anulados y en Libro caja/Control POA del ejercicio. |

## Decisiones que se confirman al aprobar los mockups (Task 2, GATE 1)

Lo que la spec no fija lo propone el mockup y lo decide el usuario en el GATE 1. Si el usuario cambia una D#, se aplica en los mockups y en las Tasks 3 y 4.

- **D1. Encabezado:** `EncabezadoPagina` con breadcrumb `"{TextoOrigen} / Pagos de la factura"` (separador " / ", como la 4d), `Titulo="Pagos de la factura"`, `Subtitulo="{Binding TituloGasto}"` (hoy: "Luminarias LED plaza Artigas — factura A-0001190 (Electro Sur S.R.L.)"). `TextoOrigen` es "Gastos y facturas" por defecto y "Calendario de pagos" cuando se entra desde el Calendario (lo fija `ConfigurarVolver`). El breadcrumb vuelve con `VolverCommand`; sale el botón "Volver" de la v1 (convención 12, §4.4). Acciones: `Insignia` "Anulado" (gasto anulado) y "Solo lectura" (sin `finanzas.pagos` o gasto anulado), neutras.
- **D2. Página con scroll:** `ScrollViewer` vertical de página completa (precedente: Calendario de pagos, 4a) con `StackPanel` de cards a todo el ancho (984 a 1280, 1070 a 1366), en este orden y con 16 entre cards: `Alerta` Info del gasto anulado (Q-3), card **Resumen**, card **Registrar pago**, card **Pagos**, card **Adjuntos**. Alternativa (se mide en la Task 2 y se muestra en el GATE solo si entra): Pagos y Adjuntos lado a lado; descartada de entrada porque el panel de adjuntos tiene cuatro columnas fijas (`AdjuntosPanelView.axaml`, `Auto,*,72,80,Auto,Auto`) y a 400 de ancho el nombre del archivo queda en ~60 px.
- **D3. Resumen:** una `TarjetaContenido` con cuatro pares etiqueta/valor en una fila (Monto total · Pagado · Saldo · Estado): etiqueta `texto-secundario`, valor con `MonedaConverter` en el estilo de valor que proponga el mockup; Estado como `Insignia` con `VarianteEstadoGastoConverter` (los tonos aprobados en la 4d: Vencida peligro, Parcial advertencia, Pagada éxito, Pendiente y Anulada neutras).
- **D4. Registrar pago:** card con título de sección "Registrar pago" y UNA fila de `CampoFormulario`: Fecha (`CalendarDatePicker` 140, requerido, con `NormalizarFechaTipeada`) · Monto (`TextBox` 160, requerido, alineado a la derecha, placeholder "Ej.: 500,00") · Nota (opcional) (`*`, placeholder "Ej.: recibo 123") · "Registrar pago" (`primario`, alineado abajo). El error va en una `v2:Alerta` Error debajo de la fila (Q-4 A). La card entera se oculta sin `PuedeRegistrar` (Q-3).
- **D5. Pagos:** card con título "Pagos · N" (N = `Pagos.Count`, `ConteoConverter`), grilla v2 FECHA (`ColumnaAuto`, `NoCede`, `FechaCortaConverter`) · MONTO (`ColumnaAuto`, `num`, `MonedaConverter`) · NOTA (`*`, elipsis y tooltip si se corta) · ESTADO (ancho fijo medido: `Insignia` "Anulado" neutra en los anulados, vacío en los activos) · acción "Anular" (`secundario`, por fila, visible con `Activo && PuedeRegistrar`). Fila anulada en texto terciario por token (`Classes.inactivo`, convención 11). Orden por click en FECHA, MONTO y NOTA. Sin pagos: `EstadoVacio` "Todavía no hay pagos" / "Los pagos que se registren van a aparecer acá.". Se seleccionan con un click (el panel sigue a la selección).
- **D6. Adjuntos:** card con el `AdjuntosPanelView` (trae su título "Adjuntos"); sin pago seleccionado, en lugar del panel, `texto-secundario` "Elegí un pago de la lista para ver sus adjuntos."; con un pago anulado, "Este pago está anulado: sus adjuntos no se muestran." (Q-2 A). Al seleccionar un pago la card se trae a la vista (`beh:TraerALaVista.AlMostrarse`, convención 27).
- **D7. Estados de pantalla** (convenciones 18 y 22): cargando `EstadoCargando` "Cargando los pagos…"; sin servidor, `EstadoVacio` de error con los textos de la convención 18 y "Reintentar" (`ReintentarCargaCommand`) en lugar de las cards; tras un 403, `EstadoVacio` "Sin permiso" / `MensajeSinPermisoPagos` en lugar de las cards. Sin mockup propio para estos tres (precedente D13 de la 4d): los custodian UiTests.
- **D8. Datos fijos** (los del mockup y de `DatosPagos`): el gasto `DatosGastos.Gastos[1]` (Id 6, Electro Sur S.R.L., A-0001190, "Luminarias LED plaza Artigas", $ 310.000,00, a crédito, vence 30/11/2030) con tres pagos: Id 61, 12/10/2026, $ 100.000,00, "Recibo 2231", activo; Id 62, 13/10/2026, $ 50.000,00, "Cargado dos veces", ANULADO; Id 63, 14/10/2026, $ 50.000,00, "Recibo 2240", activo → Pagado $ 150.000,00, Saldo $ 160.000,00, Parcial. Adjunto del pago 61: `recibo-2231.pdf`, application/pdf, 96.300 bytes, 12/10/2026 15:20 UTC. Sin pagos: `DatosGastos.Gastos[0]` (Constructora Oriental S.A., Pendiente). Gasto anulado: `DatosGastos.Anulado` con su pago automático de contado anulado (10/10/2026, $ 92.300,00, "Pago contado (automático)"). El registro con error: monto "1.5".
- **D9. Mockups** (8, 16 PNG): `pagos` (Admin, sin selección), `pagos-seleccionado` (pago 61 seleccionado con su adjunto, la card de adjuntos traída a la vista: SCROLLEADO si no entra), `pagos-sin-pagos`, `pagos-error` (Alerta con "El monto del pago no es un número válido. Usá coma para los decimales: 1,5"), `pagos-solo-lectura` (Operador sin `finanzas.pagos`: sin card de registro ni Anular, insignia, pago 61 seleccionado con el panel solo con Ver), `pagos-gasto-anulado` (Q-3 A), `pagos-pago-anulado` (pago 62 seleccionado, Q-2 A) y `pagos-confirmar-anular` (el `ConfirmacionDialog` con el texto que arma el VM: "¿Confirma anular el pago de 100.000,00 del 12/10/2026?"). No se mockean tooltips ni el calendario.

## Convenciones heredadas que aplican (de `herramientas.md`)

6, 10 (encabezado + cards), 11 (fila anulada por token), 12 (breadcrumb, sin permiso en lugar del contenido, deshabilitado por tokens), 13 (tests heredados se adaptan, nunca se borran sin reemplazo), 14 y 19 (columnas medidas en headless antes del mockup; ESTADO fijo por ser conjunto cerrado), 18 (`Cargando`, `SinPermiso`, `SinConexion` con Reintentar), 20 (tooltip solo si se corta), 21 (permisos con operador mixto y `ShellMainView`), 23 (`ConsultaVigente`, ahora también en el panel), 25 (el modal es `ConfirmacionDialog`), 27 (`TraerALaVista`), 29 (el host reenvía `NotificarPermisosActualizados` al panel: ya lo hace, L40-44), 36 (solo lectura por permiso o por estado del registro). NO aplican: 15, 16, 17, 22 (no hay carga de combos), 24, 26, 28, 30, 31 (salvo Q-4 B), 32, 33, 34, 35.

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (commit de este plan) |
|---|---|---|
| Validar el pago en el servidor | `AgregarAPagoAsync`: tras `Verificar(RegistrarPagos)`, `var pago = await _gastos.ObtenerPagoPorIdAsync(pagoGastoId) ?? throw new EntidadNoEncontradaException($"No existe el pago {pagoGastoId}.");` y `if (!pago.Activo) throw new ReglaDeNegocioException("No se puede adjuntar a un pago anulado.");`, ANTES de `AdjuntoValidador.Validar` (mismo orden que el gasto, E9 de la 4d). Un pago activo de un gasto anulado no existe (anular el gasto exige no tener pagos activos y da de baja el automático: `GastoService.AnularAsync` L163-179), así que no se mira `pago.Gasto.Activo`. | `AdjuntoService.cs` L31-83; `GastoService.cs` L163-179, L277-294. |
| Método de repositorio | `IGastoRepository.ObtenerPagoPorIdAsync(int pagoId)` → `Task<PagoGasto?>`: `_ctx.PagosGasto.FirstOrDefaultAsync(p => p.Id == pagoId)` (sin includes). Se pone en `IGastoRepository` porque `AdjuntoService` ya lo recibe (sin cambio de DI). | `IGastoRepository.cs`; `AppDbContext.cs` L22; `AdjuntoService.cs` L12-29. |
| Quitar con el dueño anulado | `QuitarAsync`: tras `Verificar(...)` (L122, sin cambio de orden: sin permiso sigue dando 403 aunque el dueño esté anulado), si `adjunto.EsDePago`: pago por `ObtenerPagoPorIdAsync(adjunto.PagoGastoId!.Value)`; anulado → `ReglaDeNegocioException("No se puede quitar un adjunto de un pago anulado.")`; si es de gasto: `_gastos.ObtenerPorIdAsync(adjunto.GastoId!.Value)`; anulado → `"No se puede quitar un adjunto de un gasto anulado."`. Dueño inexistente: imposible por FK; se trata como el adjunto inexistente (`EntidadNoEncontradaException`). | `AdjuntoService.cs` L116-130; `Adjunto.cs` L18-28. |
| 409 y 404 en la API | `DomainExceptionHandler` mapea `ReglaDeNegocioException` → 409 con `detail` y `EntidadNoEncontradaException` → 404; el cliente los muestra por el `catch` del panel (`AdjuntosPanelViewModel.cs` L111-114, L143-146) sin código nuevo. | `DomainExceptionHandler.cs` L31; Task 3 de la 4d. |
| Carga protegida | `InicializarAsync` pasa por `EjecutarCargaProtegidaAsync(RefrescarAsync, MensajeSinPermisoPagos)` con `AvisaSinConexionEnLinea => true`; se borra el `catch (UnauthorizedAccessException)` silencioso (L129-139); el `catch (ReglaDeNegocioException or EntidadNoEncontradaException)` → `InformarAsync` se conserva alrededor. `RegistrarPagoAsync` y `AnularPagoAsync` siguen llamando `RefrescarAsync` directo (una caída ahí sigue al modal global, como el guardado del formulario de gasto). | `PagosGastoViewModel.cs` L119-156; `GastoFormViewModel.cs` L286-341. |
| Solo lectura | `[ObservableProperty] bool _esAnulado` (= `!gasto.Activo` en `RefrescarAsync`); `public bool PuedeRegistrar => PuedeRegistrarPagos && !EsAnulado;` (getter sobre la sesión, convención 36); `SoloLectura => !PuedeRegistrar`. `RegistrarPagoCommand` = `ComandoDeGuardado(RegistrarPagoAsync, () => PuedeRegistrar && !string.IsNullOrWhiteSpace(MontoTexto))`; `AnularPagoCommand` con `CanExecute = nameof(PuedeAnularPago)`, `private bool PuedeAnularPago(PagoGasto? pago) => pago is { Activo: true } && PuedeRegistrar;`. `NotificarPermisosActualizados` (L40-44) además llama `NotifyCanExecuteChanged()` de los dos comandos; `EsAnulado` lleva `[NotifyPropertyChangedFor(nameof(PuedeRegistrar), nameof(SoloLectura))]` y `[NotifyCanExecuteChangedFor]` de los dos. El método privado `PuedeRegistrar()` (L158) se reemplaza (choca de nombre). | `PagosGastoViewModel.cs` L60-62, L158-160, L198-214; lección "un test de CanExecute no custodia un NotifyCanExecuteChangedFor" del ledger 4d. |
| Panel y selección | `MostrarAdjuntos => PagoSeleccionado is { Activo: true }`, `PagoSeleccionadoAnulado => PagoSeleccionado is { Activo: false }`, `SinPagoSeleccionado => PagoSeleccionado is null` (`[NotifyPropertyChangedFor]` en `_pagoSeleccionado`); `OnPagoSeleccionadoChanged` inicializa el panel SOLO con un pago activo. `RefrescarAsync` suelta la selección (`PagoSeleccionado = null`) antes de reemplazar `Pagos`. | `PagosGastoViewModel.cs` L82, L103-107, L142-156. |
| Turno del panel | `AdjuntosPanelViewModel.RecargarAsync` pide `var turno = _consultas.Empezar();` (`private readonly ConsultaVigente _consultas = new();`), limpia `Items`, espera la lista y la aplica solo si `turno.EsLaUltima`; el `catch` informa solo si es la última. | `AdjuntosPanelViewModel.cs` L73-91; `ConsultaVigente.cs`. |
| Origen del breadcrumb | `[ObservableProperty] string _textoOrigen = "Gastos y facturas";` y `public void ConfigurarVolver(Action volver, string textoOrigen = "Gastos y facturas")` (el parámetro opcional mantiene compilando a los llamadores de tests); `CalendarioPagosViewModel.RegistrarPagoAsync` (L113) pasa `"Calendario de pagos"`. | `PagosGastoViewModel.cs` L110-116; `CalendarioPagosViewModel.cs` L105-115. |
| Guardianes | `PagosGastoView` sale de la `InlineData` del `HeaderVista` (`GuardianDePatronTests.cs` L64) y de `VistasDeLaTanda` (L109) y entra a `VistasMigradasAV2`; lo reemplaza `Raiz_TieneUi2_BreadcrumbConElOrigen_RegistrarPagoEsElUnicoPrimario` (convención 13). La excepción inversa del panel queda cerrada: ya ningún host del panel es v1. | `GuardianDePatronTests.cs`. |
| Tests heredados que tocan Pagos | `PagosGastoViewModelTests` (18 tests; `InicializarAsync_SiObtenerPorIdLanzaUnauthorized_NoPropagaLaExcepcion` L90 se renombra a `InicializarAsync_Un403_MarcaSinPermiso`), `PagosGastoViewTests` (UiTests, 11 tests sobre la v1: se adaptan a los `x:Name` v2 o se reemplazan por los de `PagosGastoViewV2Tests` con el mismo gate, listados en el ledger), `GrillasSinRecorteBarridoTests` (escenario "Pagos de gasto", L106), `EspaciadoBotonesEnFilaDeGridTests` (comentario L30), `FinanzasRevocacionPermisosTests`, `CalendarioPagosViewModelTests` (`RegistrarPago_ConfiguraVolverAlCalendario`), `GastosViewModelTests` (Pagos navega). | `grep -rln PagosGasto tests/`. |

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `docs/ui-v2/mockups/finanzas/pagos*.html` | Los 8 mockups de D9. |
| `tests/StockApp.Presentation.UiTests/V2/PagosGastoViewV2Tests.cs` | La View real: estructura, resumen, registro, grilla, insignias, panel y selección, gates, gasto y pago anulados, estados, breadcrumb, revocación y 403 del panel en el shell. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosPagos.cs`, `DatosPagosTests.cs`, `PagosComparacionTests.cs` | Datos fijos de D8, sus textos y las 16 comparaciones. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Application/Interfaces/IGastoRepository.cs`, `src/StockApp.Infrastructure/Repositories/GastoRepository.cs` | `ObtenerPagoPorIdAsync`. |
| `src/StockApp.Application/Finanzas/AdjuntoService.cs` | Las dos reglas de la Task 1. |
| `src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs` | Task 3. |
| `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs` | Turno en `RecargarAsync`. |
| `src/StockApp.Presentation/ViewModels/Finanzas/CalendarioPagosViewModel.cs` | Texto del origen en `ConfigurarVolver`. |
| `src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml` (+ `.axaml.cs`) | Reescrita en v2; code-behind: carga (sin cambio de mecanismo). |
| `tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs`, `tests/StockApp.Api.Tests/AdjuntosEndpointTests.cs`, `tests/StockApp.Infrastructure.Tests/Repositories/GastoRepositoryTests.cs` | Task 1. |
| `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs`, `AdjuntosPanelViewModelTests.cs`, `CalendarioPagosViewModelTests.cs` | Task 3. |
| `tests/StockApp.Presentation.UiTests/V2/GastosFakes.cs` | `PagosFake` (Task 4). |
| Tests heredados (convención 13) | `GuardianDePatronTests`, `PagosGastoViewTests`, y solo si fallan con motivo `GrillasSinRecorteBarridoTests`, `FinanzasRevocacionPermisosTests`. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, `docs/ui-v2/herramientas.md` | Cierre (Task 5) y merge (Task 6). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln --blame-hang-timeout 5m`
- Servicio: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --blame-hang-timeout 5m`
- API: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --blame-hang-timeout 5m` (Docker)
- Infraestructura: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m` (Docker)
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Comparación con página: `scripts/ui-v2/comparar.sh Pagos` y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-17-ui-v2-fase-4e-pagos/progress.md` (ya creado con BASE y las Q-*; git-ignored). Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa.

---

## Bloque A — Servidor

### Task 1: El servidor valida el pago al adjuntar y no deja quitar adjuntos de un gasto o pago anulado

**Files:**
- Modify: `src/StockApp.Application/Interfaces/IGastoRepository.cs` (después de `ObtenerPorIdAsync`, L9)
- Modify: `src/StockApp.Infrastructure/Repositories/GastoRepository.cs` (después de `ObtenerPorIdAsync`, L43-44)
- Modify: `src/StockApp.Application/Finanzas/AdjuntoService.cs` (L61-65 y L116-125)
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/GastoRepositoryTests.cs`, `tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs`, `tests/StockApp.Api.Tests/AdjuntosEndpointTests.cs`

**Interfaces:**
- Consumes: `ReglaDeNegocioException(string)`, `EntidadNoEncontradaException(string)`, `DomainExceptionHandler` (409/404 con `detail`).
- Produces: `Task<PagoGasto?> IGastoRepository.ObtenerPagoPorIdAsync(int pagoId)`; los mensajes `"No existe el pago {id}."`, `"No se puede adjuntar a un pago anulado."`, `"No se puede quitar un adjunto de un pago anulado."`, `"No se puede quitar un adjunto de un gasto anulado."` (los verifica la Task 5 por la API). Firmas, rutas y DTOs de `IAdjuntoService` sin cambio.

- [ ] **Step 1: Línea base**

`dotnet build StockApp.sln` y `dotnet test StockApp.sln --blame-hang-timeout 5m`. Anotar en el ledger el total por proyecto (esperado 6103 en `0a0cb5d` más lo que haya entrado de docs) y los falsos conocidos por nombre.

- [ ] **Step 2: Tests (rojo)**

`GastoRepositoryTests.cs`, después de `AgregarPago_Y_AnularPago_Roundtrip`:

```csharp
    [Fact]
    public async Task ObtenerPagoPorIdAsync_DevuelveElPagoConSuEstado_ONullSiNoExiste()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var id = await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, DateTime.UtcNow));
        var pago = new PagoGasto { GastoId = id, Fecha = DateTime.UtcNow, Monto = 300m, Activo = false };
        Context.Add(pago);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var leido = await _repo.ObtenerPagoPorIdAsync(pago.Id);

        Assert.NotNull(leido);
        Assert.Equal(id, leido!.GastoId);
        Assert.False(leido.Activo);
        Assert.Null(await _repo.ObtenerPagoPorIdAsync(pago.Id + 1000));
    }
```

`AdjuntoServiceTests.cs`: en el constructor, junto al setup de `ObtenerPorIdAsync`, `_gastos.Setup(g => g.ObtenerPagoPorIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => new PagoGasto { Id = id, GastoId = 1, Activo = true });` (sin él, los tres tests de pago existentes caerían por el pago inexistente). Tests nuevos después de `AgregarAPagoAsync_VerificaPermisoRegistrarPagos`:
- `AgregarAPagoAsync_PagoInexistente_LanzaEntidadNoEncontrada`: `ObtenerPagoPorIdAsync(5)` → null; `ThrowsAsync<EntidadNoEncontradaException>` con mensaje `"No existe el pago 5."`; `AgregarAsync` y `RegistrarAsync` de auditoría `Times.Never`.
- `AgregarAPagoAsync_PagoAnulado_LanzaReglaDeNegocio`: pago 5 con `Activo = false`; mensaje `"No se puede adjuntar a un pago anulado."`; sin persistir ni auditar.
- `AgregarAPagoAsync_PagoAnuladoYArchivoInvalido_RechazaPorAnulado`: pago anulado y bytes `{ 0x00, 0x01, 0x02 }` con "archivo.exe" → el mensaje es el del pago anulado.
- `AgregarAPagoAsync_SinPermiso_NoConsultaElPago`: `Verificar(..., RegistrarPagos)` lanza `UnauthorizedAccessException` → se propaga y `ObtenerPagoPorIdAsync` `Times.Never` (el permiso va primero).
- `QuitarAsync_AdjuntoDeUnPagoAnulado_LanzaReglaDeNegocioYNoDaDeBaja`: adjunto `{ Id = 8, PagoGastoId = 5, Activo = true }`, pago 5 anulado → `"No se puede quitar un adjunto de un pago anulado."`; `adjunto.Activo` sigue true; `ActualizarAsync` y auditoría `Times.Never`.
- `QuitarAsync_AdjuntoDeUnGastoAnulado_LanzaReglaDeNegocioYNoDaDeBaja`: adjunto `{ Id = 7, GastoId = 1 }`, `ObtenerPorIdAsync(1)` → `new Gasto { Id = 1, Activo = false }` → `"No se puede quitar un adjunto de un gasto anulado."`; sin baja ni auditoría.
- `QuitarAsync_DeUnPagoAnulado_SinPermiso_DaElPermisoAntesQueElEstado`: pago anulado y `Verificar` que lanza → `UnauthorizedAccessException`.

`AdjuntosEndpointTests.cs`: `SembrarPagoAsync(bool activo = true)` (L67) siembra el pago con `Activo = activo`. Tests nuevos después de `PostAdjuntoPago_SinToken_Devuelve401`:
- `PostAdjuntoPago_PagoInexistente_Devuelve404`: `POST /finanzas/pagos/999999/adjuntos` como Admin → `NotFound` y `detail` `"No existe el pago 999999."`.
- `PostAdjuntoPago_PagoAnulado_Devuelve409`: → `Conflict` y `detail` `"No se puede adjuntar a un pago anulado."`.
- `DeleteAdjunto_DeUnGastoAnulado_Devuelve409YSigueListado`: gasto activo, adjunto creado por la API; después se marca el gasto `Activo = false` con `Factory.CrearContexto()`; `DELETE` → `Conflict` con `"No se puede quitar un adjunto de un gasto anulado."`; el adjunto sigue en la base con `Activo = true` (leído por contexto, no por el GET de la API: el listado de un gasto anulado no es lo que se prueba).
- `DeleteAdjunto_DeUnPagoAnulado_Devuelve409`: idem con un pago.

Run (de a uno): Infrastructure con `--filter "FullyQualifiedName~GastoRepositoryTests"`, Application con `--filter "FullyQualifiedName~AdjuntoServiceTests"`, Api con `--filter "FullyQualifiedName~AdjuntosEndpointTests"`. Expected: rojo de compilación por `ObtenerPagoPorIdAsync` (pegarlo); tras agregar SOLO la firma a la interfaz y un `throw new NotImplementedException()` en el repositorio, rojo de comportamiento de cada test nuevo (pegarlos).

- [ ] **Step 3: Implementación**

Repositorio: `public Task<PagoGasto?> ObtenerPagoPorIdAsync(int pagoId) => _ctx.PagosGasto.FirstOrDefaultAsync(p => p.Id == pagoId);` con su `/// <summary>` ("Un pago por id, activo o anulado, sin includes. Null si no existe."). Servicio: las reglas de la tabla "Decisiones de diseño del plan" (validar el pago entre `Verificar` y `AdjuntoValidador.Validar`; en `QuitarAsync`, después de `Verificar`).

- [ ] **Step 4: Verde y build**

`dotnet build StockApp.sln` y las tres clases. Expected: verde (los `.ods` no aplican a esas clases).

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (1a) Quitar las dos líneas de `if (!pago.Activo) throw …` de `AgregarAPagoAsync` → rojo `AgregarAPagoAsync_PagoAnulado_…`, `…YArchivoInvalido_…` (Application) y `PostAdjuntoPago_PagoAnulado_Devuelve409` (Api). Restaurar.
- (1b) Quitar `?? throw new EntidadNoEncontradaException(...)` del pago (la expresión queda `var pago = await …;` y el siguiente acceso falla con otra excepción) → rojo `AgregarAPagoAsync_PagoInexistente_…` y `PostAdjuntoPago_PagoInexistente_Devuelve404`. Restaurar.
- (1c) Quitar el bloque del pago anulado de `QuitarAsync` → rojo `QuitarAsync_AdjuntoDeUnPagoAnulado_…` y `DeleteAdjunto_DeUnPagoAnulado_Devuelve409`. Restaurar.
- (1d) Quitar el bloque del gasto anulado de `QuitarAsync` → rojo `QuitarAsync_AdjuntoDeUnGastoAnulado_…` y `DeleteAdjunto_DeUnGastoAnulado_…`. Restaurar.
- (1e) En el repositorio, quitar `.FirstOrDefaultAsync(p => p.Id == pagoId)` dejando `_ctx.PagosGasto.FirstOrDefaultAsync()` (se acorta el predicado) → rojo `ObtenerPagoPorIdAsync_…` (el null del inexistente). Restaurar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Interfaces/IGastoRepository.cs src/StockApp.Infrastructure/Repositories/GastoRepository.cs src/StockApp.Application/Finanzas/AdjuntoService.cs tests/StockApp.Infrastructure.Tests/Repositories/GastoRepositoryTests.cs tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs tests/StockApp.Api.Tests/AdjuntosEndpointTests.cs
git diff --cached --name-only
git commit -m "feat(finanzas): adjuntos de pagos validan el pago y no se quitan con el dueño anulado"
```

---

## Bloque B — Medición, mockups y GATE 1 (antes de tocar el ViewModel)

### Task 2: Medición y mockups de Pagos del gasto — GATE 1 con el usuario (Q-1 a Q-5 y D1 a D9)

**Files:**
- Create: los 8 `docs/ui-v2/mockups/finanzas/pagos*.html` de D9
- Create (temporal, NO se commitea): `tests/StockApp.Presentation.VisualTests/MedicionTemporal4e.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: los datos de D8; `ColumnaAuto`/`DataGridColumnasAutoBehavior.AnchoMedido`; `AdjuntosPanelView` v2; `VarianteEstadoGastoConverter`.
- Produces: los anchos y altos medidos en el ledger (reemplazan a D2-D5 si difieren) y las respuestas Q-1 a Q-5, que fijan las Tasks 3 a 5.

- [ ] **Step 1: Medir en headless**

`MedicionTemporal4e.cs` siguiendo `MedicionTemporal4d` (plan 4d, Task 12 Step 1, árbol armado en C# con el tema v2), a 1280×760 y 1366×768, con `--logger "console;verbosity=detailed"`:
1. Grilla de D5 con los tres pagos de D8: `AnchoMedido` de FECHA y MONTO, ancho de la insignia "Anulado" + 16 para ESTADO, ancho del botón "Anular" + padding de celda para la acción (que entre en 40 de fila sin recortar: el problema que la v1 resolvía con `sin-padding-vertical`), y el espacio que queda para NOTA.
2. Fila de D4: que Fecha 140 · Monto 160 · Nota `*` · botón entren en el interior de la card a 1040 con 8 de separación.
3. Alto de cada card y el total de `pagos` y `pagos-seleccionado`: si a 1280×760 la card de adjuntos no entra, cuánto scrollea (`pagos-seleccionado` se mockea scrolleado al final, como `gasto-editar`).
4. Encabezado: que el subtítulo de D1 con el gasto de D8 y las dos insignias entren en una línea.

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~MedicionTemporal4e" --logger "console;verbosity=detailed"`. Anotar en el ledger.

- [ ] **Step 2: Mockups**

Partir de `finanzas/calendario-pagos.html` (página con scroll y cards apiladas), `finanzas/gasto-editar.html` (breadcrumb, insignias, panel de adjuntos con un archivo, contenido scrolleado), `finanzas/gastos-con-anulado.html` (fila anulada por token, insignias de estado) y `dialogos/confirmacion.html` (modal). Cada uno con `<title>Finanzas: Pagos de la factura[ estado] (UI v2)</title>`, `<!-- Datos: DatosPagos.<Pantalla>: … -->` y el sidebar con "Gastos y facturas" activo. Contenido de cada uno: D9, con los textos de D1 a D8.

- [ ] **Step 3: Capturas y verificación de la herramienta**

```bash
for p in pagos pagos-seleccionado pagos-sin-pagos pagos-error pagos-solo-lectura pagos-gasto-anulado pagos-pago-anulado pagos-confirmar-anular; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p@1366x768.png --tamano 1366x768
done
ls -l --time-style=full-iso artifacts/ui-v2/mockups/finanzas-pagos*.png | wc -l
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
rm tests/StockApp.Presentation.VisualTests/MedicionTemporal4e.cs && git status --short tests/
```

Expected: 16 PNG con fecha de ahora; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0; la medición borrada. MIRAR cada PNG.

- [ ] **Step 4: GATE 1 — aprobación del usuario (PARADA)**

PARAR. Presentar los 16 PNG (LOS DOS TAMAÑOS), D1 a D9 con los anchos medidos y las cinco preguntas Q-1 a Q-5 tal como están en el ledger (cada una con su recomendación y su tradeoff). Señalar: D1 (sale el botón Volver, el breadcrumb cambia según el origen), D2 (scroll de página; la alternativa lado a lado y por qué no), D4 + Q-4, D6 + Q-2, D7 (sin mockups de cargando, sin conexión y sin permiso). No seguir sin "aprobado" explícito. Si cambia una D# o una Q-* sale distinta de A, aplicarla en los mockups y volver a presentar; anotar en el ledger: `GATE 1 4e: aprobado por <quién> <yyyy-MM-dd>; Q-1=<A|B|C> Q-2=<A|B> Q-3=<A|B> Q-4=<A|B> Q-5=<A|B>; cambios: <lista o "ninguno">`. Una Q-1 = B o C agrega su task chica (TDD + mutación) antes de la Task 3, con el detalle de la columna "Alternativa".

- [ ] **Step 5: Commit**

```bash
git add docs/ui-v2/mockups/finanzas/pagos.html docs/ui-v2/mockups/finanzas/pagos-*.html
git diff --cached --name-only
git commit -m "docs(ui-v2): mockups aprobados de pagos del gasto"
```

---

## Bloque C — ViewModel y vista v2

### Task 3: Pagos — carga protegida, solo lectura, panel con pago activo y origen del breadcrumb

**Decisiones aplicadas:** Q-2, Q-3 y Q-4 como las aprobó el usuario (el texto asume A).

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs` (L73-91)
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/CalendarioPagosViewModel.cs` (L113)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs`, `AdjuntosPanelViewModelTests.cs`, `CalendarioPagosViewModelTests.cs`

**Interfaces:**
- Consumes: `ViewModelBase.EjecutarCargaProtegidaAsync(Func<Task>, string)`, `AvisaSinConexionEnLinea`, `ReintentarCargaCommand`, `SinPermiso`, `SinConexion`, `Cargando`; `ConsultaVigente`.
- Produces (los usa la Task 4): `const string MensajeSinPermisoPagos = "No tenés permiso para ver finanzas, así que no se pueden abrir los pagos de la factura."`; `bool EsAnulado`; `bool PuedeRegistrar`; `bool SoloLectura`; `bool MostrarAdjuntos`; `bool PagoSeleccionadoAnulado`; `bool SinPagoSeleccionado`; `string TextoOrigen`; `void ConfigurarVolver(Action volver, string textoOrigen = "Gastos y facturas")`; `static string MensajeConfirmarAnular(PagoGasto pago)` (extrae el texto de L201-202 sin cambiarlo, para `DatosPagos`); `RegistrarPagoCommand`, `AnularPagoCommand` (con `CanExecute`), `VolverCommand`, `ReintentarCargaCommand`.

- [ ] **Step 1: Tests (rojo)**

En `PagosGastoViewModelTests.cs` el `Crear` gana un parámetro `HashSet<string>? permisosMutables` (la sesión devuelve ESE set, para revocar en el test) y un `Gasto? gasto` (por defecto `GastoConPago()`). Tests nuevos:
- `InicializarAsync_SinServidor_MarcaSinConexionSinPropagar` (`ObtenerPorIdAsync` lanza `ServidorNoDisponibleException`): `SinConexion` true, `Pagos` vacía, sin `InformarAsync`.
- `ReintentarCarga_TrasUnaCaida_CargaElGasto`: primero falla, después responde; `ReintentarCargaCommand.ExecuteAsync(null)` → `SinConexion` false y un pago.
- `InicializarAsync_Un403_MarcaSinPermiso` (REEMPLAZA y renombra `InicializarAsync_SiObtenerPorIdLanzaUnauthorized_NoPropagaLaExcepcion`, convención 13): no propaga, `SinPermiso` true, `MensajeSinPermiso == PagosGastoViewModel.MensajeSinPermisoPagos`.
- `GastoAnulado_EsSoloLecturaYNoRegistraNiAnula`: gasto con `Activo = false` y Admin → `EsAnulado`, `SoloLectura`, `!PuedeRegistrar`, `RegistrarPagoCommand.CanExecute(null)` false con `MontoTexto = "10"`, `AnularPagoCommand.CanExecute(pagoActivo)` false.
- `AnularPago_SobreUnPagoAnulado_NoEjecuta`: `AnularPagoCommand.CanExecute(new PagoGasto { Activo = false })` false con Admin.
- `NotificarPermisosActualizados_SinRegistrarPagos_LosComandosNoEjecutan` (Review Focus 3): operador con `VerFinanzas` + `RegistrarPagos`; suscribir `CanExecuteChanged` de los dos comandos; quitar `RegistrarPagos` del set; `vm.NotificarPermisosActualizados()` → los dos eventos se levantaron y los dos `CanExecute` dan false.
- `SeleccionarPagoAnulado_NoInicializaElPanelYLoMarca`: seleccionar un pago anulado → `MostrarAdjuntos` false, `PagoSeleccionadoAnulado` true, `ListarPorPagoAsync` `Times.Never`.
- `RegistrarPago_TrasRefrescar_SueltaLaSeleccionYOcultaElPanel` (Review Focus 2): con un pago seleccionado, registrar → `PagoSeleccionado` null, `SinPagoSeleccionado` true.
- `ConfigurarVolver_ConOrigen_ElBreadcrumbYLaVueltaCoinciden` (Review Focus 5): `TextoOrigen` es "Gastos y facturas" por defecto; `ConfigurarVolver(() => volvio = true, "Calendario de pagos")` → `TextoOrigen == "Calendario de pagos"` y `VolverCommand` llama a esa acción.
- `MensajeConfirmarAnular_EsElTextoDelDialogo`: `PagosGastoViewModel.MensajeConfirmarAnular(new PagoGasto { Monto = 100_000m, Fecha = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc) })` == `"¿Confirma anular el pago de 100.000,00 del 12/10/2026?"`.

En `AdjuntosPanelViewModelTests.cs`: `InicializarAsync_DosSeguidas_LaPrimeraQueTerminaTardeNoPisaLaSegunda` (Review Focus 1): `ListarPorPagoAsync(1)` devuelve un `TaskCompletionSource` pendiente y `ListarPorPagoAsync(2)` una lista con "b.pdf"; inicializar con 1, después con 2; completar la de 1 con "a.pdf" → `Items` tiene solo "b.pdf".

En `CalendarioPagosViewModelTests.cs`, ampliar `RegistrarPago_ConfiguraVolverAlCalendario`: tras navegar, el VM destino tiene `TextoOrigen == "Calendario de pagos"`.

Run: VM completa. Expected: rojo de compilación (pegarlo); tras declarar las firmas vacías, rojo de comportamiento de cada test (pegarlos).

- [ ] **Step 2: Implementación**

Según la tabla "Decisiones de diseño del plan" (filas Carga protegida, Solo lectura, Panel y selección, Turno del panel, Origen del breadcrumb), con `protected override bool AvisaSinConexionEnLinea => true;` y `protected override void AlPerderConexion() => Pagos.Clear();`. El `ReintentarAsync` de la base alcanza (repite `RefrescarAsync` con el mismo `_gastoId`). `AnularPagoAsync` usa `MensajeConfirmarAnular(pago)`.

- [ ] **Step 3: Verde y build**

`dotnet build StockApp.sln`; suites COMPLETAS de `Presentation.Tests` y `Presentation.UiTests` (las vistas v1 siguen compilando: `PagosGastoView.axaml` bindea `PuedeRegistrarPagos`, que no cambia). Expected: verde. Anotar el total.

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (3a) Quitar `protected override bool AvisaSinConexionEnLinea => true;` → rojo `InicializarAsync_SinServidor_…`.
- (3b) Quitar ` && !EsAnulado` de `PuedeRegistrar` → rojo `GastoAnulado_EsSoloLecturaYNoRegistraNiAnula`.
- (3c) Quitar `PuedeRegistrarPagos && ` de `PuedeRegistrar` → rojo `Operador_ConVerFinanzasSinRegistrarPagos_…` (adaptado para afirmar también `PuedeRegistrar` false) y `NotificarPermisosActualizados_SinRegistrarPagos_…`.
- (3d) Quitar las dos llamadas `NotifyCanExecuteChanged()` del override de `NotificarPermisosActualizados` → rojo `NotificarPermisosActualizados_SinRegistrarPagos_…`.
- (3e) Quitar `pago is { Activo: true } && ` de `PuedeAnularPago` → rojo `AnularPago_SobreUnPagoAnulado_NoEjecuta`.
- (3f) Quitar la condición de pago activo en `OnPagoSeleccionadoChanged` (queda `if (value is not null)`) → rojo `SeleccionarPagoAnulado_NoInicializaElPanel…`.
- (3g) Quitar `PagoSeleccionado = null;` de `RefrescarAsync` → rojo `RegistrarPago_TrasRefrescar_…`.
- (3h) Quitar `if (!turno.EsLaUltima) return;` del panel → rojo `InicializarAsync_DosSeguidas_…`.
- (3i) Quitar `TextoOrigen = textoOrigen;` de `ConfigurarVolver` → rojo `ConfigurarVolver_ConOrigen_…` y el de Calendario.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/CalendarioPagosViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/CalendarioPagosViewModelTests.cs
git diff --cached --name-only
git commit -m "feat(finanzas): pagos del gasto con carga protegida y solo lectura por permiso o gasto anulado"
```

### Task 4: Pagos del gasto v2 — datos fijos, vista, UiTests, guardianes y comparación

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosPagos.cs`, `DatosPagosTests.cs`, `PagosComparacionTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/PagosGastoViewV2Tests.cs`
- Modify: `src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml` (reescrita) y `.axaml.cs` (sin cambio de mecanismo)
- Modify: `tests/StockApp.Presentation.UiTests/V2/GastosFakes.cs` (`PagosFake`), `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`, `tests/StockApp.Presentation.UiTests/PagosGastoViewTests.cs`; y solo si fallan con motivo `GrillasSinRecorteBarridoTests.cs`, `FinanzasRevocacionPermisosTests.cs`

**Interfaces:**
- Consumes: la API de la Task 3; `DatosGastos.Gastos`, `DatosGastos.Anulado`, `DatosShell.Crear`, `DatosProductos.UsuarioAdmin`, `DatosIngresos.UsuarioOperador`; `MontajeV2`; `SesionFake`, `AuthServiceFake`, `NavigationServiceFake`, `ConfirmacionServiceFake`, `PreferenciasSidebarFake`, `InfoAppFake`.
- Produces: `DatosPagos.Gasto` (D8), `GastoSinPagos`, `GastoAnulado`, `ReciboDelPago61`; `Pantalla(Gasto gasto, bool soloLectura = false, int? seleccionarPagoId = null, string? montoTexto = null, bool registrar = false)` → `ShellMainViewModel`; `ConPagos()`, `Seleccionado()`, `SinPagos()`, `ConError()`, `SoloLectura()`, `GastoAnuladoPantalla()`, `PagoAnulado()`; `MensajeConfirmarAnular()`. `x:Name` de la vista que usan los tests: `Raiz`, `Desplazamiento`, `Encabezado`, `InsigniaAnulado`, `InsigniaSoloLectura`, `AlertaAnulado`, `IndicadorCarga`, `EstadoSinConexion`, `EstadoSinPermiso`, `Contenido`, `TarjetaResumen`, `ValorMontoTotal`, `ValorPagado`, `ValorSaldo`, `InsigniaEstado`, `TarjetaRegistrar`, `SelectorFecha`, `CajaMonto`, `CajaNota`, `BotonRegistrar`, `AlertaError`, `TarjetaPagos`, `TituloPagos`, `Grilla`, `EstadoSinPagos`, `TarjetaAdjuntos`, `PanelAdjuntos`, `TextoElegiPago`, `TextoPagoAnulado`.

- [ ] **Step 1: Datos fijos (rojo → verde)**

`DatosPagosTests.cs` (molde `DatosGastosTests`): el VM de `ConPagos()` tras cargar tiene tres pagos, `MontoTotal` 310.000, `TotalPagado` 150.000, `SaldoPendiente` 160.000, `Estado` "Parcial", `TituloGasto` "Luminarias LED plaza Artigas — factura A-0001190 (Electro Sur S.R.L.)"; `GastoAnuladoPantalla()` con `EsAnulado`; `SoloLectura()` con `!PuedeRegistrar`; `MensajeConfirmarAnular()` == el texto de D9; cada fábrica de pantalla con su aserción propia (lección T11 de la 4d). Rojo de compilación pegado; `DatosPagos.cs` lo pone verde. `DatosPagos` arma el VM con un `Mock<IGastoService>` (`ObtenerPorIdAsync` devuelve el gasto) y un `AdjuntosPanelViewModel` con `ListarPorPagoAsync(61)` → `[ReciboDelPago61]`; VisualTests no ve los `internal`, así que la selección y el monto se ponen con las propiedades públicas DESPUÉS de cargar, dentro de la fábrica.

- [ ] **Step 2: UiTests de la View real (rojo)**

`PagosGastoViewV2Tests.cs` (molde `GastoFormViewV2Tests`, montaje con `MontajeV2`, ancho 1040 y alto 760 salvo que se diga). Tests, cada uno con sus aserciones sobre los `x:Name` de arriba:
- `Raiz_TieneUi2_BreadcrumbConElOrigen_RegistrarPagoEsElUnicoPrimario` (REEMPLAZA la `InlineData(typeof(PagosGastoView), "Pagos de la factura", "FINANZAS")` del guardián): `Classes` de la raíz contiene `ui2`; `Encabezado.BreadcrumbOrigen == "Gastos y facturas"`, `Titulo == "Pagos de la factura"`, `Subtitulo == vm.TituloGasto`; un solo `Button` con clase `primario` en el árbol visible ("Registrar pago").
- `DesdeElCalendario_ElBreadcrumbDiceCalendarioDePagos` (Review Focus 5): `vm.ConfigurarVolver(() => { }, "Calendario de pagos")` antes de montar → `BreadcrumbOrigen == "Calendario de pagos"`.
- `Resumen_MuestraLosImportesYLaInsigniaDeEstadoConSuTono`: textos "$ 310.000,00", "$ 150.000,00", "$ 160.000,00"; `InsigniaEstado.Texto == "Parcial"`, `Variante == VarianteInsignia.Advertencia`.
- `Grilla_FechaDelPago_SinCorrimientoDeDia` (Review Focus 4): el `TextBlock` pintado en la celda FECHA del pago 61 dice "12/10/2026".
- `Grilla_PagoAnulado_TextoTerciarioInsigniaYSinAnular`: la fila del pago 62 tiene sus `TextBlock` con `Classes.inactivo` y `Foreground` `#78716C`, `Insignia` "Anulado" visible y el botón "Anular" de su celda NO visible; el del pago 61 visible.
- `SinSeleccion_LaCardDeAdjuntosPideElegirUnPago` / `ConUnPagoActivoSeleccionado_MuestraElPanelConSuAdjunto` / `ConUnPagoAnuladoSeleccionado_OcultaElPanelYLoDice` (Q-2 A).
- `OperadorSinRegistrarPagos_SoloLectura`: con `SesionFake(Operador, VerFinanzas)`: `TarjetaRegistrar` no visible, `InsigniaSoloLectura` visible, ningún "Anular" visible, y con el pago 61 seleccionado el `BotonAgregar` del panel no visible y "Ver" visible.
- `OperadorConRegistrarPagos_RegistraYAnula`: `SesionFake(Operador, VerFinanzas, RegistrarPagos)`: `TarjetaRegistrar` visible, sin insignia, "Anular" del 61 visible.
- `GastoAnulado_InsigniasAlertaYSinRegistrar` (Q-3 A), con Admin: `InsigniaAnulado`, `InsigniaSoloLectura` y `AlertaAnulado` visibles con su texto; `TarjetaRegistrar` no visible.
- `MontoIlegible_AlertaDeErrorDebajoDeLaFila` (Q-4 A): `CajaMonto.Text = "1.5"`, click real en `BotonRegistrar` → `AlertaError` visible con el texto de D9.
- `SinPagos_EstadoVacioEnLaCardDePagos`; `CargaPendiente_IndicadorDeCarga`; `SinServidor_EstadoSinConexionConReintentar_EnLugarDeLasCards`; `Un403_SinPermisoEnLugarDelContenido`.
- `SeleccionarUnPago_TraeLaCardDeAdjuntosALaVista` a 1280×760: tras seleccionar el 61, el borde inferior de `TarjetaAdjuntos` queda dentro del viewport de `Desplazamiento`.
- `AccionAnular_EntraEnLaFilaSinRecortar`: el botón "Anular" del 61 mide su alto natural y cabe en la celda (reemplaza a `Montar_PagoActivoConPermiso_IconoAnular_TieneAltoRenderizadoVisible_DentroDeLaCeldaDelDataGrid`).
- `RevocacionDeRegistrarPagos_DentroDelShell_PasaASoloLecturaSinBorrarLoTipeado` (Review Focus 3; molde `GastoFormViewV2Tests` L438-478): operador con `VerFinanzas` + `RegistrarPagos`, `CajaNota` con "recibo 9"; `ManejadorAccesoRevocado.ManejarAsync` con `tras = { VerFinanzas }` → `TarjetaRegistrar` y los "Anular" ocultos, `InsigniaSoloLectura` visible, `vm.Nota == "recibo 9"`.
- `Un403DelPanel_DentroDelShell_OcultaAgregarYQuitar` (cierra el minor de la 4a "el test UI del 403 del panel solo en el host de Gasto"): pago 61 seleccionado, revocación por el shell → `BotonAgregar` y "Quitar" del panel ocultos.

`PagosFake` en `GastosFakes.cs`: `IGastoService` con `Gasto` (lo que devuelve `ObtenerPorIdAsync`), `Pendiente` (`TaskCompletionSource<Gasto>?`), `Falla`, `Registrados` (`List<PagoGasto>`), `AnuladosPagos` (`List<(int, int)>`); el resto `NotSupportedException`.

`PagosGastoViewTests.cs` (v1, 11 tests): cada uno se adapta a los `x:Name` v2 o se BORRA solo si un test de `PagosGastoViewV2Tests` custodia lo mismo; anotar en el ledger cada par (heredado → reemplazo). `GuardianDePatronTests`: sacar L64 y la línea `typeof(PagosGastoView),` de `VistasDeLaTanda` (L109); agregar `typeof(PagosGastoView)` a `VistasMigradasAV2`.

Run: UiTests completa. Expected: rojos de los tests v2 (pegarlos; la vista todavía es la v1).

- [ ] **Step 3: La vista v2**

Reescribir `PagosGastoView.axaml` según D1 a D7 y los mockups aprobados: raíz `UserControl Classes="ui2"` con `Background="{DynamicResource V2FondoBrush}"`; `ScrollViewer x:Name="Desplazamiento"` (vertical `Auto`, horizontal `Disabled`) con un `StackPanel` de margen 28,24; los estados de D7 con el `MultiBinding` de la convención 18; las cards con `v2:TarjetaContenido`; el panel en un `ContentControl x:Name="PanelAdjuntos" Content="{Binding AdjuntosPanel}" IsVisible="{Binding MostrarAdjuntos}"`; el botón "Anular" de la celda con `Command="{Binding $parent[UserControl].((vm:PagosGastoViewModel)DataContext).AnularPagoCommand}"` y `CommandParameter="{Binding}"`, visible con el `MultiBinding` `Activo && PuedeRegistrar` (el `$parent[UserControl]` ya funciona en la v1; el panel de adjuntos es otro `UserControl` pero no es ancestro de la grilla). Sin el estilo `sin-padding-vertical`: el botón v2 de 36 entra en la fila de 40 (lo confirma la medición; si no, se resuelve en el padding de la CELDA, memoria "botón invisible en celda de DataGrid"). Code-behind: el mismo `DataContextChanged` → `InicializarAsync()`.

`dotnet build StockApp.sln`.

- [ ] **Step 4: Verde**

UiTests y VM completas. Expected: verde. Anotar los totales y los reemplazos.

- [ ] **Step 5: Comparación**

`PagosComparacionTests.cs` (molde `GastoFormComparacionTests`): las 7 pantallas de `DatosPagos` dentro del shell con el sidebar oculto y el modal con `ContenidoDeDialogo.Extraer(new ConfirmacionDialog(DatosPagos.MensajeConfirmarAnular()))`, a los dos tamaños (16 tests), con el umbral calibrado. `pagos-seleccionado` con el scroll que fijó la medición.

Run: `scripts/ui-v2/comparar.sh Pagos`. Expected: 16 filas APROBADAS con el umbral por defecto. Si una no entra: diagnosticar con `teselas.py`/`tinta.py` (copiados a `$SCRATCH/diag/`, nunca se commitean), corregir vista o mockup (≤ 4 px no visible se anota; mayor vuelve al usuario); un `UmbralAprobado` solo con el usuario.

- [ ] **Step 6: Mutaciones (QUITANDO)**

- (4a) Quitar `Classes="ui2"` de la raíz → rojo `Raiz_TieneUi2_…`.
- (4b) Quitar `IsVisible` de `TarjetaRegistrar` → rojo `OperadorSinRegistrarPagos_SoloLectura` y `GastoAnulado_…`.
- (4c) Quitar el término `PuedeRegistrar` del `MultiBinding` del botón Anular → rojo `OperadorSinRegistrarPagos_SoloLectura` y `RevocacionDeRegistrarPagos_…`.
- (4d) Quitar el término `Activo` de ese `MultiBinding` → rojo `Grilla_PagoAnulado_…`.
- (4e) Quitar `IsVisible` de `PanelAdjuntos` → rojo `ConUnPagoAnuladoSeleccionado_…` y `SinSeleccion_…`.
- (4f) Quitar `IsVisible` de `InsigniaSoloLectura` → rojo `OperadorConRegistrarPagos_RegistraYAnula` (afirma la insignia NO visible).
- (4g) Quitar `Classes.inactivo` de una celda de texto → rojo `Grilla_PagoAnulado_…`.
- (4h) Quitar `IsVisible="{Binding SinPermiso}"` de `EstadoSinPermiso` → rojo `Un403_…`.
- (4i) Quitar `beh:TraerALaVista.AlMostrarse` de `TarjetaAdjuntos` → rojo `SeleccionarUnPago_TraeLaCardDeAdjuntosALaVista`.
- (4j) Quitar `_adjuntosPanel.NotificarPermisosActualizados();` del override del VM → rojo `Un403DelPanel_DentroDelShell_…`.
- (4k) Quitar `Converter={x:Static conv:FechaCortaConverter.Instance}` de FECHA dejando `StringFormat` ausente → rojo `Grilla_FechaDelPago_SinCorrimientoDeDia` (texto con hora).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml.cs tests/StockApp.Presentation.UiTests/V2/PagosGastoViewV2Tests.cs tests/StockApp.Presentation.UiTests/V2/GastosFakes.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.UiTests/PagosGastoViewTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosPagos.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosPagosTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/PagosComparacionTests.cs
git diff --cached --name-only
git commit -m "feat(ui-v2): pagos del gasto en v2"
```

(Agregar por ruta, también, cada test heredado que haya hecho falta adaptar.)

---

## Bloque D — Cierre de la fase y del bloque Finanzas

### Task 5: Cierre de la 4e — guardianes, suite, GATE 2 orgánico, limpieza de datos, docs y review de la fase

**Files:**
- Modify: `docs/ui-v2/herramientas.md`, `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`
- Modify (local): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 4e cerrada y aprobada en `feat/ui-v2`, con la suite verde anotada (entrada de la Task 6).

- [ ] **Step 1: Guardianes y mutaciones**

Confirmar con `grep` y la CLI `codegraph`, y anotar en el ledger:
1. `PagosGastoView` en `VistasMigradasAV2` y en ninguna otra lista de `GuardianDePatronTests`; `Guardian_CubreTodasLasVistasDelEnsamblado`, `VistaV2_NoUsaPiezasDelTemaViejo`, `VistaV2_NoTieneOpacidadesLiterales` y `NingunaVista_TieneOverlaysDeModal` verdes.
2. `grep -n 'Opacity\|Classes="primary"\|Classes="secondary"\|Classes="caption"\|Classes="card"\|<c:\|HeaderVista\|MargenVista\|DangerBrush\|BadgeEstado\|sin-padding-vertical' src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml` vacío.
3. Ningún host del panel de adjuntos es v1: `grep -rln "AdjuntosPanel" src/StockApp.Presentation/Views/` y cada uno con `Classes="ui2"` en su raíz.
4. `git diff <BASE>..HEAD --stat -- src/StockApp.Api src/StockApp.Domain` VACÍO; `src/StockApp.Application` muestra solo `AdjuntoService.cs` e `IGastoRepository.cs`; `src/StockApp.Infrastructure` solo `GastoRepository.cs`; sin migraciones.
5. Los rojos pegados de 1a-1e, 3a-3i y 4a-4k están en el ledger; uno sin rojo pegado se corre ahora.

- [ ] **Step 2: Build, suite completa y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; verde salvo los falsos conocidos (por nombre); total ≥ línea base + nuevos − reemplazados (listar reemplazos); los dos `--verificar` exit 0; TODAS las filas de `index.html` APROBADAS (las de fases anteriores sin cambios).

- [ ] **Step 3: Verificación orgánica (spec §6.4) — la ejecuta un agente con la app real contra Postgres local, WSLg a 1366×768 y 1280×760**

Preparación: `docker start stockapp-pg`; backup ANTES de tocar datos: `mkdir -p ~/stockapp-backups-local && docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-4e-$(date +%Y%m%d-%H%M%S).sql` (usuario y base del `.env` local o `~/.config/GestionMunicipal/conexion.json`), `ls -l` no vacío, ruta al ledger; anotar el stock actual del producto de los movimientos 1599-1601 (para Q-5). API: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http`; app: `dotnet run --project src/StockApp.Presentation`; `admin` / `Admin1234`. Datos: reusar los `PRUEBA-4D` (gasto 22 parcial con el pago 13; gasto 21 vencido sin pagos; gasto 20 anulado); a `f4dmixto` (id 30) darle `finanzas.ver` + `finanzas.pagos`. Lo nuevo lleva el prefijo `PRUEBA-4E` en la nota; anotar ids.

Pasos (capturar cada pantalla; 1 y 3 también a 1280×760; MIRAR cada captura y comparar `md5sum`):
1. Gastos → gasto 22 → Pagos: breadcrumb "Gastos y facturas / Pagos de la factura", resumen con la insignia Parcial, grilla; comparar a ojo con `pagos.html`. Breadcrumb vuelve a Gastos.
2. Calendario de pagos → "Registrar pago" del gasto 21 → breadcrumb "Calendario de pagos"; vuelve al Calendario.
3. Registrar en el 21: monto "1.5" → Alerta con la sugerencia; monto válido + nota `PRUEBA-4E` → la fila aparece, resumen al día, selección suelta. Fecha con el calendario (popup con `capturar-x11.py`): la fila muestra el día elegido, no el anterior.
4. Seleccionar el pago → la card de adjuntos baja a la vista; Agregar un PDF, "Ver" (si WSLg no tiene visor, anotarlo), Quitar con confirmación.
5. Anular ese pago (comparar el modal con `pagos-confirmar-anular.html`) → fila en terciario con "Anulado", sin "Anular"; seleccionarla → "Este pago está anulado…" sin panel.
6. Por la API (sin pegar el token): `POST /finanzas/pagos/<id anulado>/adjuntos` → 409 "No se puede adjuntar a un pago anulado."; `POST /finanzas/pagos/999999/adjuntos` → 404; `DELETE /finanzas/adjuntos/<adjunto de un pago anulado o del gasto 20>` → 409 con su mensaje. Pegar las salidas en el ledger (login: `curl -s -X POST http://localhost:<puerto>/auth/login -H 'Content-Type: application/json' -d '{"nombreUsuario":"admin","contrasena":"Admin1234"}'`). Para tener un adjunto de pago anulado: adjuntar al pago ANTES de anularlo en el paso 5.
7. Gasto 20 (anulado) → Pagos: insignias Anulado y Solo lectura, la Alerta, sin card de registro (Q-3).
8. `f4dmixto`: registra y anula; desde Admin quitarle `finanzas.pagos` con su pantalla de pagos abierta → una acción que pegue al servidor (Registrar) → un Aviso y pasa a solo lectura sin perder lo tipeado. Repetir el escenario de la deuda de la 4d (Pagos desde Gastos tras quitar `finanzas.pagos`) y registrar qué pasa según Q-1.
9. Sin servidor: `kill` SOLO del pid de `StockApp.Api`; abrir Pagos → "No se pudo conectar" con Reintentar; levantar la API y Reintentar → carga.

Si un paso no se puede hacer por el entorno (WSLg, contenedor, API), PARAR con la evidencia (stderr, `docker ps -a`, md5) sin declarar la fase cerrada.

- [ ] **Step 4: GATE 2 — aprobación final de la fase (PARADA)**

PARAR. Presentar: las 16 filas de `index.html`, las capturas junto a los mockups, las salidas de la API, los rojos de las mutaciones, los tests heredados adaptados o reemplazados, cualquier `UmbralAprobado` y lo observado en el paso 8. Lo que el usuario observe se arregla en commits propios con TDD (rojo pegado) o va a la deuda con su OK. Registrar: `Fase 4e: aprobada por <quién> <fecha>`.

- [ ] **Step 5: Limpieza de los datos de prueba (según Q-5)**

Con Q-5 = A, contra `stockapp-pg` y NUNCA contra el VPS:
1. Backup nuevo (`pre-limpieza-4e-<fecha>.sql`) y listar en el ledger, con un `SELECT` por tabla acotado por ids, cada fila a borrar: `Adjuntos` (y su contenido) de los gastos 19-24 y de sus pagos; `PagosGasto` de los gastos 19-24 (incluidos los `PRUEBA-4E`); `MovimientosStock` 1599, 1600, 1601; `Gastos` 19-24; `LineasPoa` 4, `RubrosGasto` 2, `FuentesFinanciamiento` 2, `Proveedores` 8 (antes, `SELECT` de que ningún otro registro los referencia). Nombres de tablas y columnas, del `AppDbContext`/migraciones, no supuestos.
2. Borrar en UNA transacción (`BEGIN … COMMIT`), en orden de FK, cada `DELETE` con `WHERE id IN (…)` explícito; si un `DELETE` afecta otro número de filas que el listado, `ROLLBACK` y PARAR.
3. En la app: Historial de movimientos → "Recalcular stock" del producto de los movimientos; su stock queda igual al anotado antes de la 4d (dato del backup `pre-4d-20261017-110108.sql`); si difiere, PARAR y reportar.
4. `f4dmixto` (y cualquier usuario `f4e*`) dado de baja por Administración de usuarios.
5. Anotar en el ledger los conteos borrados y el stock antes y después.

Con Q-5 = B: anular por la app lo que quede activo, desactivar los maestros `PRUEBA` y dar de baja a los usuarios; anotar.

- [ ] **Step 6: `herramientas.md` y spec**

`herramientas.md`: si la fase dejó una regla nueva reutilizable (p. ej. "un panel embebido que se re-inicializa por selección pide turno de `ConsultaVigente`", Review Focus 1), agregarla como convención 37 bajo "Convenciones que agrega la Fase 4e (Pagos del gasto)"; si no, nada.

Spec:
1. `**Estado:**` y la fila de Finanzas de §7: "4e" implementada y "Fase 4e (Pagos del gasto) APROBADA por el usuario el <fecha>"; el bloque Finanzas completo.
2. §7, después de "Estado de la Fase 4d": "**Estado de la Fase 4e (aprobada el <fecha>).**" con: Pagos del gasto v2 (D1 a D9 como quedaron), las respuestas Q-1 a Q-5, las reglas de servidor de adjuntos de pagos (404/409) y de quitar con dueño anulado (409), las 16 comparaciones (máximo de teselas), la suite en `<hash>`, el resumen de la verificación orgánica y la limpieza hecha.
3. La viñeta de la 4a "La raíz lleva `Classes="ui2"` … excepción INVERSA … TEMPORAL" y las de la deuda de la 4a y la 4d sobre `PagosGastoView` v1, `AgregarAPagoAsync`, `QuitarAsync`, el refresco de permisos de Pagos y los datos `PRUEBA-4D`: tachadas con "Cerrada en la 4e (…)".
4. "**Deuda abierta tras la Fase 4e:**" con lo que deje Q-1 (si A: el aterrizaje en solo lectura sin Aviso, aceptado), Q-2/Q-4 si se eligió B, cualquier `UmbralAprobado`, los minors diferidos del ledger y lo que no se pudo verificar en WSLg.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git diff --cached --name-only
git commit -m "docs(ui-v2): cierre de la Fase 4e (pagos del gasto)"
```

- [ ] **Step 7: Review final de la fase**

Review del rango `<commit de este plan>..HEAD` (skill de ejecución: review de rama completa con el modelo más capaz), con la suite y las comparaciones verdes. Foco: los cinco puntos de "Review Focus"; que el cambio de VMs no exceda lo declarado en Global Constraints; que las reglas de servidor no cambiaron firmas, rutas ni DTOs; que ninguna pieza del tema viejo quedó en la vista; que un pago o gasto anulado no deja registrar, anular, adjuntar ni quitar ni en la UI ni en el servidor. Hallazgos: commits propios (TDD, rojo pegado) o deuda con el OK del usuario. Sin push ni merge.

### Task 6: Bloque Finanzas — review final del bloque y primer merge a `main`

**Files:**
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§7 "Integración": registro del primer merge)
- Modify (local): el ledger

**Interfaces:**
- Consumes: la Fase 4e aprobada (Task 5) y la regla de §7 "Integración": merge por bloques, el primero al cerrar la 4e, con suite verde y review final del bloque, `--ff-only` + push.
- Produces: `main` = `feat/ui-v2` en `origin`.

- [ ] **Step 1: Precondiciones**

```bash
git -C /home/capua25/workspace/stockapp status --short
git -C /home/capua25/workspace/stockapp rev-parse --abbrev-ref HEAD
git fetch origin
git merge-base --is-ancestor main feat/ui-v2 && echo FF_POSIBLE
git merge-base --is-ancestor origin/main main && echo MAIN_AL_DIA_CON_ORIGIN
git log --oneline main..feat/ui-v2 | wc -l
git diff main..feat/ui-v2 --stat -- src/StockApp.Infrastructure/Migrations
git log main..feat/ui-v2 --format='%H %s%n%b' | grep -n "Co-Authored-By\|Generated with" || echo SIN_ATRIBUCIONES
git ls-files .superpowers | head -1
```

Expected: el checkout de `main` en la rama `main` y sin cambios en archivos versionados (los untracked `.codegraph/`, `err.txt`, `out.txt` no molestan a un fast-forward; si alguno colisiona con un archivo que trae la rama, PARAR); `FF_POSIBLE` y `MAIN_AL_DIA_CON_ORIGIN` impresos; migraciones nuevas listadas (o ninguna) y anotadas; `SIN_ATRIBUCIONES` (si aparecen, reportarlo al usuario: NO se reescribe historia); `.superpowers` sin archivos versionados. Si `main` avanzó y ya no es ancestro: merge de `main` hacia `feat/ui-v2` en el worktree, suite completa verde, y recién ahí seguir (spec §7: "merge periódico de main hacia feat/ui-v2").

- [ ] **Step 2: Review final del bloque**

Review del rango `main..feat/ui-v2` con el modelo más capaz, enfocada en lo que las reviews por fase no ven: integración entre fases del bloque Finanzas (4a a 4e: panel de adjuntos en sus tres hosts, navegación Gastos ↔ Pagos ↔ Calendario ↔ Control POA, gates de `finanzas.ver`/`gastos`/`pagos` coherentes entre pantallas, `RecargaConDebounce` en sus cuatro VMs), restos del tema viejo en las vistas de Finanzas (todas en `VistasMigradasAV2`), archivos temporales o de medición versionados por error (`git ls-files | grep -i "medicion\|temporal\|scratch\|\.bak"`), `docs/` coherente (spec, `herramientas.md`, mockups) y el riesgo de convivencia v1/v2 en `main` (aceptado por la spec). Hallazgos bloqueantes: commits propios en `feat/ui-v2` con TDD y suite; los demás a la deuda con el OK del usuario. Anotar el veredicto en el ledger.

- [ ] **Step 3: Suite verde sobre el HEAD a mergear**

`dotnet build StockApp.sln` y `dotnet test StockApp.sln --blame-hang-timeout 5m` en el worktree, sobre el mismo `HEAD` que se va a mergear (si la Step 2 agregó commits, se corre de nuevo). Expected: verde salvo los falsos conocidos por nombre; total anotado con el hash.

- [ ] **Step 4: Registro en la spec (antes del merge, para que viaje en él)**

En §7 "Integración", agregar: "**Primer merge por bloques (bloque Finanzas, <fecha>):** `main` avanza de `<hash de main>` a `<hash>` por fast-forward; suite <total> verde en `<hash>`; review del bloque: <veredicto>; migraciones incluidas: <lista o ninguna>. El próximo bloque se define al cerrarlo."

```bash
git add docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git diff --cached --name-only
git commit -m "docs(ui-v2): registro del primer merge por bloques (Finanzas)"
```

(Este commit es solo de docs: no invalida la suite de la Step 3.)

- [ ] **Step 5: Merge `--ff-only` y push**

```bash
git -C /home/capua25/workspace/stockapp merge --ff-only feat/ui-v2
git -C /home/capua25/workspace/stockapp log --oneline -1
git -C /home/capua25/workspace/stockapp push origin main
git -C /home/capua25/workspace/stockapp status --short --branch
```

Expected: "Fast-forward"; el HEAD de `main` igual al de `feat/ui-v2`; push aceptado; `## main...origin/main` sin "ahead". Si el `--ff-only` se rechaza, PARAR (nada de merge con commit ni rebase). Convención del repo: merge directo a `main` con `--ff-only` + push, sin PR. No se despliega al VPS en esta task (decisión aparte del usuario). Anotar en el ledger `Merge bloque Finanzas: main <antes> → <después>, push OK`.

---

## Fuera de alcance

- Cambios de dominio, de DTOs y de API más allá de las dos reglas de `AdjuntoService` de la Task 1.
- Errores por campo en "Registrar pago" (salvo Q-4 B) y validación de saldo en el cliente (la hace el servidor).
- El doble Aviso tras un 403 en una acción del panel de adjuntos (deuda de la 4a), CS0114 del shell y el override copiado en los hosts del panel.
- Control POA (cabecera SALDO) y el combo de Proveedor de Gastos (hallados en el GATE 2 de la 4d): no son de la 4e.
- Desplegar al VPS tras el merge.
- Modo oscuro, back-stack (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| El panel muestra adjuntos de otro pago y Quitar actúa sobre el equivocado. | Turno de `ConsultaVigente` en el panel (Task 3, 3h); selección que se suelta al refrescar (3g); servidor que rechaza quitar con dueño anulado (Task 1). |
| Un pago o gasto anulado sigue aceptando adjuntos o pagos. | UI: panel oculto y solo lectura (Tasks 3 y 4); servidor: 404/409 (Task 1); verificado por la API en el GATE 2. |
| La migración rompe el camino desde el Calendario. | `TextoOrigen` y `ConfigurarVolver` con tests de VM y de vista (Review Focus 5); paso 2 del GATE 2. |
| La limpieza borra datos reales o deja el stock corrido. | Backup antes; listado y conteo por id; una transacción con `ROLLBACK` ante cualquier diferencia; Recalcular stock contra el valor anterior a la 4d; nunca el VPS. |
| El merge a `main` arrastra algo no revisado o choca con el checkout de `main`. | Review del bloque `main..feat/ui-v2`, suite sobre el mismo HEAD, `--ff-only` (nunca merge con commit), chequeo de untracked y de atribuciones antes de tocar `main`. |
| WSLg se congela o `capturar.sh` devuelve el escritorio de Windows con exit 0. | md5 de capturas, mirar cada una, `capturar-x11.py` para popups; si se congela, PARAR y pedir el reinicio al usuario. |
