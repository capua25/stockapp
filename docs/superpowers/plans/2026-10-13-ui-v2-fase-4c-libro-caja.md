# Fase 4c: Libro caja (UI v2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar a la UI v2 el Libro caja (`LibroCajaView`) con paridad funcional con la v1, los estados de pantalla de las convenciones 18 y 23, la recarga automática con debounce, el selector Mes | Año completo, los saldos y netos negativos por token con `Insignia`, y un PDF anual nuevo (E1 a E6 de la spec), comparado contra mockups aprobados a 1280×760 y 1366×768.
**Architecture:** Cuatro bloques, como la 4a y la 4b. (A) El ViewModel primero, sin tocar la vista: `LibroCajaViewModel` carga con `EjecutarCargaProtegidaAsync` + `ConsultaVigente`, opta por `SinConexion` y guarda el período CARGADO (`LibroMes` o `LibroAnual`), que es lo que la pantalla muestra y lo que el PDF exporta (E1); recarga sola con debounce al cambiar Año, Mes o modo, con `ModoSeleccionado` como selector y el año y mes iniciales de un reloj inyectable en hora local (E2, E3); calcula los saldos del año (E4); exporta un PDF anual nuevo con `PlantillaTabular` y `ResumenPdf` y habilita el exporte solo con datos (E6). (B) Datos fijos, medición y 7 mockups (14 PNG), con GATE 1 del usuario antes de todo XAML de la vista. (C) Un estilo de texto negativo fuera de la grilla y la vista v2 en dos tasks (modo mes y estados; modo año, selector y gates por modo), con UiTests de la View real y sus comparaciones. (D) Repaso de guardianes y mutaciones, verificación orgánica con GATE 2, docs de cierre y review.
**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, CommunityToolkit.Mvvm, PDFsharp-MigraDoc 6.2.4 (`StockApp.Documentos`), UglyToad.PdfPig (tests), Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`).
**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, bloque "Diseño de la Fase 4c (aprobado por el usuario el 2026-10-13)" (L643-679 en `02878a6`: excepciones E1 a E6; sección 1, pantalla; sección 2, PDF anual; sección 3, tests, verificación y deuda), §5.1 (L204-211) y, como contexto, los bloques de la 4a y la 4b. Convenciones 1 a 31: `docs/ui-v2/herramientas.md`. Molde de formato: `docs/superpowers/plans/2026-10-12-ui-v2-fase-4b-ingresos-caja.md`.

## Global Constraints

- Todos los comandos corren desde `/home/capua25/workspace/stockapp-worktrees/ui-v2` (rama `feat/ui-v2`, base: el commit de este plan), como `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <comando>`.
- CodeGraph: este worktree tiene su PROPIO índice `.codegraph/`; para código C# usar primero la CLI `codegraph` dentro del worktree (`codegraph query|explore|node|callers|impact`), NUNCA el MCP `codegraph_explore` (responde con el índice del checkout principal); `grep`/Read como fallback, y Read directo para `.axaml`, markdown y HTML/CSS (CodeGraph no los indexa).
- Tema claro fijo; tokens de §2.1 sin valores nuevos: fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, separador `#F0EFED`, peligro texto `#B91C1C`, texto primario `#1C1917`, texto secundario `#57534E`, texto terciario `#78716C`; fila de grilla 40, header 36, input y botón 36; espaciado múltiplo de 4; sidebar 240 (la vista mide 1040 a 1280 y 1126 a 1366).
- `tokens.json` es la fuente de `Tokens.axaml`: nunca editar `Tokens.axaml` a mano; toda task que toque `Themes/V2/`, `Controls/V2/` o `docs/ui-v2/` corre `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`.
- Formato es-UY (el cliente es un municipio uruguayo): importes con `MonedaConverter` / `FormatoEsUy.Moneda` ("$ 1.250.000,00", negativo "-$ 51.540,00"), fechas "dd/MM/yyyy" con `FechaCortaConverter`; nunca `CultureInfo.GetCultureInfo("es-UY")`.
- Sin cambios de API ni de DTOs (`LibroCajaMesDto`, `LibroCajaAnualDto`, `MovimientoCajaDto`, `TotalPorClaveDto`, `TotalMensualDto` intactos); sin CSV (se eliminó de toda la app en `a3ab4d4`): Exportar PDF es el único exporte.
- VMs: solo `LibroCajaViewModel` cambia, y solo por E1 a E6 (más el reloj inyectable de E2); ningún otro VM, servicio, endpoint, `ViewLocator` ni `INavigationService`.
- `x:DataType` / compiled bindings en la vista y en cada `DataTemplate` (`dto:MovimientoCajaDto`, `dto:TotalPorClaveDto`, `dto:TotalMensualDto`); listas estáticas con `{x:Static}`.
- Sin comentarios con `--` en `.axaml` ni `.csproj` (AVLN1001, cascadea a MSB4025).
- Sin botón primario en la vista (pantalla de consulta, spec sección 1): Actualizar y Exportar PDF son `secundario`.
- Prohibidos en la vista v2: `Opacity`, `SignoNegativoBrushConverter`, `BadgeEstado`, `HeaderVista`, `c:` del tema viejo, `Classes="primary|secondary|caption|seccion"`.
- Gates de UI con la View real en `Presentation.UiTests` (un test de VM no custodia un `IsVisible` del XAML); gates de permisos con un Operador de permisos MIXTOS (`new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas)`), NUNCA con Admin (cortocircuita).
- TDD en cada task: test primero, correrlo y PEGAR el rojo real en el ledger, código mínimo, verde.
- Mutaciones QUITANDO, nunca agregando: `export SCRATCH=~/.cache/stockapp-ui-v2-fase-4c && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas o acortadas; se corre el test y se PEGA el rojo; se restaura con `cp`; `git diff --no-index` vacío; verde. NUNCA `git checkout --` ni `git restore`.
- Stubs fieles al contrato: `IFinanzasVistasService.ObtenerLibroCajaMesAsync(int anio, int mes)` → `LibroCajaMesDto`, `ObtenerLibroCajaAnualAsync(int anio)` → `LibroCajaAnualDto` con los 12 meses SIEMPRE (`FinanzasVistasService.cs` L92-118); los fakes lanzan lo que lanza `ApiErrores`: `UnauthorizedAccessException` (401/403), `ServidorNoDisponibleException` (sin servidor).
- Mockups (§6.1): `docs/ui-v2/mockups/finanzas/libro-caja*.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a `DatosLibroCaja`, celdas numéricas con `v2-num` y el valor en un `<span>`; se aprueban ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los calibrados (`TeselasDensasPorDefecto = 11`, `PorcentajePorDefecto = 1.68`) a AMBOS tamaños; uno mayor solo con `UmbralAprobado(…, AprobadoPor: "Martin", …)` tras revisar cada tesela densa con el usuario en un GATE (16 o más exige `MutacionPalidaEnRojo`); nunca para tapar un defecto.
- Convención 6: comparación DENTRO de `ShellMainView` (`DatosShell.Crear(usuario, permisos, "LibroCaja", vm)`), sidebar oculto en los dos renders (`ProductosComparacionTests.OcultarSidebar`, `ocultarEnMockup: [".v2-sidebar"]`).
- Build obligatorio tras cada cambio de `.axaml` o de un `.cs` de `Presentation`: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde); tests con `dotnet test StockApp.sln` o UN `.csproj` por comando; nunca dos `dotnet test` a la vez (comparten `obj/`).
- En este worktree los rojos de `StockApp.Infrastructure.Tests` y `StockApp.Api.Tests` por fixtures `.ods` ausentes son FALSOS conocidos (confirmar por nombre); `ApiStartupMigrationTests.Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests` es intermitente conocido; cualquier otro rojo es real. Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- Línea base de la suite: 5789 verdes (anotada por el usuario tras `a3ab4d4`); se re-mide en la Task 1 Step 1 y cada task anota el total tras su commit; solo baja por tests heredados REEMPLAZADOS (convención 13), listados con su reemplazo.
- Sin referencias al plan en comentarios del código (nada de "Task 4", "D9", "E5", "P1" en `.cs`/`.axaml`/`.css`/`.html`).
- Las referencias de línea (`L123`) son las del commit de este plan; después de que una task edita un archivo, ubicar el lugar por el texto citado.
- Verificación GUI: ventana "Gestión Municipal" (`pgrep -x GestionMunicipa`), toolkit `scripts/gui-verificacion/`; `capturar.sh` NO es fail-closed (con WSLg caído captura el escritorio de Windows con exit 0): MIRAR cada captura y comparar `md5sum` con la anterior; la rueda del mouse no se simula; matar procesos por PID, nunca `pkill -f`.
- Commits: conventional commits en español, uno por task, SIN `Co-Authored-By` ni línea de atribución de IA; sin push, merge, rebase, amend, `git reflog expire`, `git gc` ni `git prune`.
- GATE 1 (Task 6) y GATE 2 (Task 11) son paradas para el usuario: no se sigue sin un "aprobado" explícito.

## Review Focus

1. **Una respuesta vieja de OTRO modo pisa la nueva.** Caso: con Octubre cargando lento, el usuario pasa a Año completo; el anual llega primero y después llega el mes viejo; la pantalla queda con el selector en "Año completo" y la grilla de movimientos de octubre (o al revés, un anual viejo tras volver a Mes). Esperado: solo la última consulta decide, sin importar el modo. Tests: `RespuestaMensualVieja_TrasPasarAAnio_NoPisaElAnual` y `RespuestaAnualVieja_TrasVolverAMes_NoPisaElMes` (Task 1).
2. **El PDF sale con un período que no es el que se ve.** Casos: el usuario cambia Mes de 9 a 10 y exporta antes de que corra el debounce (el PDF dice "Mes: 10/2026" con los movimientos de setiembre); pasa a "Año completo" y exporta antes de la recarga (sale un PDF anual vacío o con datos de otro año). Esperado: el PDF exporta y rotula SIEMPRE el período cargado. Tests: `ExportarPdf_ConMesCambiadoSinRecargar_ExportaElMesCargado` y `ExportarPdf_RecienPasadoAAnioSinRecargar_ExportaElMesCargado` (Task 4).
3. **Vacío que miente.** Casos: el anual de un año sin movimientos trae igual 12 filas en cero (`Count` nunca es 0, así que un vacío por `Count` no aparece nunca y la grilla muestra doce ceros); el "No hay movimientos" aparece durante la carga inicial o con el servidor caído. Esperado: el vacío sale solo con un período CARGADO sin movimientos (mes sin filas o doce meses en cero). Tests: `Anual_DoceMesesEnCero_EsSinMovimientos`, `SinNadaCargado_NoEsSinMovimientos` (Task 1), `Cargando_IndicadorSinElVacioNiLosSaldos` (Task 8).
4. **Un Mes inválido oculto bloquea Actualizar.** Caso: el usuario tipea "13" en Mes (Actualizar se bloquea, convención de `NumericUpDownValidacionBehavior`) y pasa a "Año completo": el campo Mes se oculta con `IsVisible`, pero el control sigue en el árbol con su error y `ErroresDeEntradaBehavior` sigue bloqueando Actualizar, sin que se vea por qué. Esperado: al ocultarse Mes su error se descarta y Actualizar se habilita; al volver a Mes el campo muestra el último mes válido sin error. Test: `MesInvalido_AlPasarAAnioCompleto_NoBloqueaActualizar` (Task 9).
5. **El mes inicial corrido por la zona horaria.** Caso: el 31/10 a las 22:00 en Uruguay (UTC-3) ya son las 01:00 del 01/11 en UTC; la v1 arranca con `DateTime.UtcNow.Month` y abre noviembre (vacío). Esperado: año y mes iniciales de la fecha LOCAL. Test: `MesInicial_SaleDeLaHoraLocal_NoDeLaUtc` (Task 2).

---

## Decisiones previas a la ejecución (contradicciones de la spec, a confirmar por el usuario ANTES de la Task 1)

La spec pide cosas que el código actual no permite tal como están escritas. El plan está redactado con la opción A de cada una; el orquestador las pregunta al usuario antes de arrancar. Si el usuario elige B, se aplica el cambio indicado (con TDD) y se anota en el ledger.

| # | Contradicción (evidencia) | A (propuesta, la que implementa el plan) | B (alternativa) y qué cambia |
|---|---|---|---|
| P1 | **Saldos del año sin DTO.** E4 pide saldo inicial y final "visibles en ambos modos" y la sección 2 los pone en el PDF anual, pero `LibroCajaAnualDto` es `(Anio, TotalesPorMes, TotalesPorRubro)` (`FinanzasVistasDtos.cs` L33-36): no trae saldos, y la spec prohíbe cambiar API y DTOs. Además, en la v1 los saldos NO se ven en modo año (`LibroCajaView.axaml` L36-47, `IsVisible="{Binding !VerAnioCompleto}"`). | El modo año hace una consulta extra `ObtenerLibroCajaMesAsync(anio, 1)` y toma su `SaldoInicial` como saldo inicial del año; saldo final = saldo inicial + Σ `Neto` de los 12 meses (es la misma cuenta del servidor: `corrido = saldoInicial + Σ(ingresos − egresos)`, `FinanzasVistasService.cs` L40-66 y L102-108). Sin cambio de API. Costo: una consulta mensual más al abrir el año. | Sin saldos en modo año: el panel de saldos se oculta en Año completo, el PDF anual sale sin la tabla de saldos y la Task 3 no se hace (sus tests tampoco). Contradice E4 y la sección 2 de la spec. |
| P2 | **Orden del PDF anual.** La sección 2 pide "Arriba, el resumen con el saldo inicial y el saldo final del año. Luego la tabla de totales por mes … y la tabla de totales por rubro", "con el mismo `PlantillaTabular` y `ResumenPdf`". `PlantillaTabular.Generar` imprime SIEMPRE la tabla principal y DESPUÉS el resumen (`PlantillaTabular.cs` L151-219, `AgregarResumen` "se imprime DESPUÉS de la tabla principal"). | Se acepta el orden de la plantilla, igual que el PDF mensual: tabla de totales por mes → saldos → "Meses con déficit" → "Totales por rubro". `PlantillaTabular` no cambia. | Cambiar `PlantillaTabular` para admitir un resumen ANTES de la tabla (parámetro nuevo en `IPdfExporter.Exportar`, que usan los 9 exportes): sale del alcance "mismo `PlantillaTabular`" y suma una task en `StockApp.Documentos` con sus tests. |
| P3 | **"Déficit marcado" en la tabla del PDF.** La plantilla imprime cada celda con `FormatearValor` sin estilos por fila (`PlantillaTabular.cs` L593-607) y la spec pide las columnas del anual contra los DTOs reales (`TotalMensualDto`: `Mes` es `int`, sale "3"). No hay forma de marcar una fila sin un tipo de fila propio. | El neto negativo sale con su signo ("-72.750,00") y el resumen agrega la sección "Meses con déficit" (nombre del mes y neto) solo si hay alguno. Columnas: Mes · Ingresos · Egresos · Neto sobre `TotalMensualDto` (Mes como número). | Un record de presentación `FilaMesLibroCajaPdf(string Mes, decimal Ingresos, decimal Egresos, decimal Neto, string Deficit)` armado desde el DTO (mes por nombre, columna "Déficit" con "Sí"): contradice "columnas contra los DTOs reales" y `ColumnasPdfContraDtosRealesTests` pasa a cubrir el record. |

## Decisiones del usuario (2026-10-13, vinculantes)

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | **Alcance de la 4c:** `LibroCajaView` en v2 con paridad funcional, estados v2, E1 a E6 y PDF anual. Sin API, sin DTOs, sin CSV, sin filtros de texto, sin edición de movimientos y sin totales por fuente en el anual. | "Fuera de alcance" al final. | todas |
| U2 | **Actualizar y Exportar PDF ambos secundarios, sin primario.** | D1. | 8 |
| U3 | **Modo año = dos `TarjetaContenido`:** totales por mes y totales por rubro. | D9. | 9 |
| U4 | **Una sola variante de vacío:** "sin movimientos en el período". | D10, `SinMovimientosEnElPeriodo`. | 1, 8, 9 |
| U5 | **Sin conexión dentro de la card con Reintentar, como en la 4b** (convención 18). | D10. | 1, 8 |
| U6 | **Lo que la spec no fija lo propone el mockup y lo decide el usuario en el GATE 1** (criterio de 3a a 4b). | D1 a D17 abajo. | 6 |
| U7 | **Ejecución con subagent-driven-development, sin preguntar el método.** | Header del plan. | — |

## Decisiones que se confirman al aprobar los mockups (Task 6, GATE 1)

> **GATE 1 aprobado por Martin el 2026-10-14** (AprobadoPor: Martin, Fecha: 2026-10-14). D1 a D17 como se presentaron, con las resoluciones de la medición de abajo; mandan sobre el texto de D7, D9 y el XAML de las Tasks 8 y 9. Mockups: 7 pantallas, 14 PNG (`cdf0991` y el commit de cierre de la Task 6).
> - **Q-NOCEDE:** INGRESO, EGRESO y SALDO CORRIDO llevan `ColumnaAuto.NoCede` además de FECHA (con solo FECHA, a 1366 las medidas cedían y cortaban los importes: INGRESO 90 de 115, EGRESO 85 de 103). Scroll horizontal a 1280 y 1366 con los anchos medidos.
> - **Q-ORDEN:** grilla del mes en el orden FECHA · TIPO · CONCEPTO · INGRESO · EGRESO · SALDO CORRIDO · PROVEEDOR · FACTURA · FUENTE · RUBRO (SALDO CORRIDO termina en 706: entero en la vista a los dos tamaños).
> - **Q-ALTO:** `MaxHeight="518"` en la grilla de movimientos (header 36 + 12 filas de 40 + borde 2).
> - **Q-FILTROS:** `Margin="0"` en la `BarraFiltros` de la card de filtros (con el margen del tema la card quedaba con 32 abajo y los saldos 16 por debajo de los inputs).
> - **Q-NETO:** NETO de ancho fijo 192 = ceil4(insignia "Déficit" 54 + 8 + "-$ 12.345.678,90" 113 + 16), el criterio de 8 cifras de SALDO de Control POA.
> - **Q-ANIO:** INGRESOS y EGRESOS del año con ancho fijo 124 (peor caso de 8 cifras "$ 12.345.678,90" + 16, redondeado a 4) en vez de `*`; MES pasa a `*` (absorbe el resto).
> - **Q-PDF:** opción (a), equivale a P3-B solo para la columna Mes: un record de presentación en el VM con el nombre del mes para las filas del PDF anual (y `ColumnasPdfContraDtosRealesTests` lo valida); `ColumnaPdf` y `PlantillaTabular` NO se tocan. Lo implementa otra task.

- **D1. Encabezado:** `EncabezadoPagina` sin breadcrumb (pantalla del sidebar), `Titulo="Libro caja"`, `Subtitulo="Ingresos y egresos de la caja municipal, con el saldo día por día."`; a la derecha "Actualizar" y "Exportar PDF", los dos `secundario`, en ese orden.
- **D2. Filtros:** una `TarjetaContenido` propia arriba (los filtros valen para todas las cards), con `BarraFiltros MostrarBusqueda="False"` y tres `CampoFormulario`: "Año" (`NumericUpDown` 120 de ancho, 2020 a 2100, `NumericUpDownValidacionBehavior.Que="año"`), "Período" (`ComboBox` 160 con "Mes" y "Año completo") y "Mes" (`NumericUpDown` 100, 1 a 12, `Que="mes"`), este último solo en modo Mes (E3, `IsVisible`).
- **D3. Selector de modo:** un `ComboBox` de dos opciones (patrón Activos/Todos de la 4b, `OpcionesModo` estática + `ModoSeleccionado` que traduce a `VerAnioCompleto`). Alternativa: un control segmentado nuevo, que la spec solo admite si el mockup revela un patrón repetido (no lo hay: es la primera pantalla con modos).
- **D4. Mes como número** (paridad v1, y conserva `LibroCajaViewTests.Mes_Invalido_…` y su validación). Alternativa: combo con los nombres de los meses (cambia el campo y adapta ese test).
- **D5. Saldos:** a la derecha de la card de filtros, alineados abajo, dos bloques "Saldo inicial" / "Saldo final" con la etiqueta en `etiqueta-campo` y el valor en `titulo-seccion`; un saldo negativo lleva el valor con `Classes.negativo` (`#B91C1C`) y, a su izquierda, la `Insignia` `Peligro` "Saldo negativo" (texto de la v1). Visibles con cualquier período cargado (también en el vacío: el saldo existe aunque no haya movimientos); ocultos mientras no hay nada cargado, sin conexión o sin permiso.
- **D6. Página:** scroll de página completa (patrón del Calendario de la 4a); la grilla de movimientos con `MaxHeight` medido (header + 12 filas = 516) y su propio scroll, así virtualiza las filas (un mes puede tener cientos); los totales por rubro y por fuente lado a lado en un `GrillaUniformeV2` de 2 columnas con `Espaciado="16"`.
- **D7. Grilla del mes:** FECHA (`ColumnaAuto` medida, `NoCede`, `FechaCortaConverter`) · TIPO (`ColumnaAuto`) · CONCEPTO (`*`, `MinWidth` 200, elipsis + tooltip si se corta) · PROVEEDOR · FACTURA · FUENTE · RUBRO (`ColumnaAuto` + `TooltipSiSeCorta`, "—" si viene null) · INGRESO · EGRESO (`ColumnaAuto`, `num`, `MonedaOGuionConverter`: "—" cuando es 0; la v1 mostraba "$ 0,00") · SALDO CORRIDO (`ColumnaAuto`, `num`, `MonedaConverter`, `Classes.negativo` si es negativo). Orden por click en las diez; orden inicial el del servidor (fecha y tipo), sin flecha. A 1280 y 1366 la grilla hace scroll horizontal (precedente: el Historial de 10 columnas de la 3c-2).
- **D8. Totales del mes:** dos cards "Totales por rubro" (RUBRO `*` · TOTAL `num`) y "Totales por fuente" (FUENTE `*` · TOTAL `num`, con `Classes.negativo` si el total es negativo, sin insignia: un total de fuente negativo no es un saldo). Orden por click, orden inicial el del servidor (total descendente).
- **D9. Modo año:** card "Totales por mes" con MES (nombre: "Enero" … "Diciembre", con "Setiembre"; ordena por número) · INGRESOS · EGRESOS · NETO (ancho fijo medido con el peor caso, como SALDO de Control POA: `Insignia` `Peligro` "Déficit" a la izquierda y el número con `Classes.negativo` a la derecha; E5); los 12 meses, también los que están en cero; pie "N meses con movimientos". Card "Totales por rubro" (RUBRO · TOTAL). Sin totales por fuente.
- **D10. Estados:** con nada cargado y `Cargando`, una card con `EstadoCargando` "Cargando el libro caja…"; sin servidor, una card con `EstadoVacio` de error (textos de la convención 18) y "Reintentar" `secundario`; tras un 403, `EstadoVacio` "Sin permiso" con "No tenés permiso para ver el libro caja." EN LUGAR de todo el contenido (filtros incluidos), con Actualizar visible y Exportar PDF visible y deshabilitado (la spec lo pide deshabilitado, no oculto; Control POA lo oculta); vacío, una card con `EstadoVacio` "No hay movimientos en el período" / "Probá con otro mes o con el año completo." en lugar de los bloques del modo, con los saldos a la vista.
- **D11. Lo que se ve es lo cargado:** los bloques de cada modo, los saldos y el PDF siguen al período CARGADO (`LibroMes` o `LibroAnual`), no a los filtros; el campo Mes se oculta al instante al cambiar el selector, pero mientras corre el debounce sigue a la vista el período anterior (mismo criterio que Control POA en la 4a). Durante una recarga con datos, los datos quedan (no se vuelve a "Cargando…").
- **D12. Textos de las insignias:** "Saldo negativo" y "Déficit", los de la v1.
- **D13. Mes inválido y Año completo:** al pasar a Año completo, el texto inválido de Mes se descarta (vuelve el mes del VM, sin error) y Actualizar se habilita (Review Focus 4).
- **D14. Mes inicial en hora local** (Review Focus 5): corrige el `DateTime.UtcNow` de la v1.
- **D15. Datos fijos** (Task 5): `DatosLibroCaja` (octubre 2026 con 6 movimientos, octubre con saldo negativo, octubre vacío, año 2026 con dos meses en déficit, enero para el saldo inicial del año). Reloj fijo 06/10/2026.
- **D16. Mockups** (a 1280×760 y 1366×768, 14 PNG): `finanzas/libro-caja.html` (mes con datos, saldo positivo), `libro-caja-saldo-negativo.html`, `libro-caja-anio.html` (año completo con déficit), `libro-caja-vacio.html`, `libro-caja-cargando.html`, `libro-caja-sin-conexion.html`, `libro-caja-sin-permiso.html`. No se mockean tooltips ni desplegables.
- **D17. Revocación de `finanzas.ver` desde otra pantalla con Libro caja abierto:** NO se replica el gate de Gastos (`137b7bf`: `PuedeVerFinanzas` en el exporte y vaciado en `NotificarPermisosActualizados`), porque E6 fija las condiciones del exporte (`Cargando`, `SinPermiso`, sin datos) y eso excede E1 a E6. Los datos y el PDF siguen hasta la próxima consulta, que da 403 y deja "Sin permiso". Si el usuario lo quiere, va a la deuda (candidato a una fase de permisos).

## Convenciones heredadas que aplican (de `herramientas.md`)

- 6 (comparación dentro del shell), 10 (`EncabezadoPagina` + cards + pie de conteo con `ConteoConverter`), 11 (negativo por token, nunca `Opacity`), 13 (tests heredados se adaptan sin borrarse), 14 y 19 (columnas `Auto` medidas en headless antes del mockup, `ColumnaAuto` con el mismo conversor de la celda, `NoCede` en FECHA), 18 (`Cargando`, `SinPermiso`, `SinConexion` con Reintentar que relee el filtro de pantalla), 20 (tooltip solo si se corta), 21 (permisos con operador mixto y `ShellMainView`), 23 (`ConsultaVigente`).
- NO aplican: 12 y 15 a 17 (no es un formulario), 22 (sin servidor va por la convención 18), 24 a 27 (sin diálogos propios, sin fila de carga, sin pie fijo), 28 (no hay columnas por permiso), 29 (no hay panel embebido), 30 (no hay filtro de texto en el cliente), 31 (no hay errores por campo del VM).

## Convenciones que agrega esta fase (para `herramientas.md`, Task 11)

32. **Lo que se ve es lo cargado:** en una pantalla con período o modo que recarga con debounce, el VM guarda el resultado de la última consulta aplicada (`LibroMes` / `LibroAnual` en Libro caja, `_ejercicioCargado` en Control POA) y la vista muestra sus bloques, sus totales y su exporte desde ESE resultado, no desde los filtros; el exporte rotula con el período del resultado. El vacío se calcula sobre el período cargado (`SinMovimientosEnElPeriodo`), es false sin nada cargado y no se deduce de un `Count` cuando el servidor devuelve filas en cero. Primer caso: Libro caja.
33. **Número negativo fuera de una grilla:** `Classes.negativo` sobre el `TextBlock` (estilo `.ui2 TextBlock.negativo`, peligro texto por token) más una `Insignia` `Peligro` con la palabra; espejo `.v2-texto-peligro` en `base.css`. Un campo de filtro que se oculta por un modo descarta su texto inválido al ocultarse, para no bloquear en silencio los comandos de guardado. Primer caso: los saldos y el campo Mes de Libro caja.

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (commit de este plan) |
|---|---|---|
| E1: carga protegida | `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)` + `ConsultaVigente` + `AvisaSinConexionEnLinea => true` + `AlPerderConexion` (vacía lo cargado) + `ReintentarAsync => CargarAsync()` (relee Año, Mes y modo de pantalla, convención 18). El `catch (UnauthorizedAccessException)` silencioso se reemplaza; el aviso global del 403 ya lo da `AuthTokenHandler` (no se duplica). | `LibroCajaViewModel.cs` L59-96; `ControlPoaViewModel.cs` L79-110; `ViewModelBase.cs` L254-305. |
| E1: lo cargado | `LibroMes` (nuevo, observable) y `LibroAnual` (existente) son excluyentes: aplicar un mes limpia el anual y viceversa; el vacío (`SinMovimientosEnElPeriodo`) y el conteo anual (`MesesConMovimientos`) se calculan al aplicar. La v1 limpiaba todo ANTES del `await` (pantalla en blanco durante cada recarga). | `LibroCajaViewModel.cs` L59-84; D11. |
| E2: debounce | Mismo mecanismo que Control POA (`EsperaDebounce` internal de 300 ms, `_tareaDebounce`, `CancellationTokenSource`), disparado por `OnAnioChanged`, `OnMesChanged` (solo en modo Mes: en Año completo el mes no cambia la consulta) y `OnVerAnioCompletoChanged`. La validación de año es la de Control POA: `NumericUpDown` 2020 a 2100 con `NumericUpDownValidacionBehavior` (un texto inválido no llega al VM). | `ControlPoaViewModel.cs` L68-137; `ControlPoaView.axaml` L37-40. |
| E2: reloj | Parámetro opcional `TimeProvider? reloj = null` al final del constructor (como Control POA; `AddTransient<LibroCajaViewModel>()` en `App.axaml.cs` L395 lo resuelve con el default). Año y mes con `GetLocalNow()`, escritos en los campos (no disparan la recarga). | `ControlPoaViewModel.cs` L46-55; Review Focus 5. |
| E3: selector | `OpcionesModo` (`["Mes", "Año completo"]`, constantes `ModoMes`/`ModoAnioCompleto`) y `ModoSeleccionado` sobre `VerAnioCompleto`, que se conserva (lo usan los tests heredados). | `IngresosViewModel` (`OpcionesEstado`/`EstadoSeleccionado`, 4b). |
| E4: saldos del año | P1-A. Ambas consultas del modo año van dentro de la misma carga protegida y del mismo turno. | P1. |
| E5: neto negativo | Solo vista (Task 9): `EsNegativoConverter` para la insignia "Déficit" y `Classes.negativo`. | `ControlPoaView.axaml` (columna SALDO). |
| E6: PDF | `ExportarPdfCommand` único (un botón): con `LibroAnual` cargado exporta el anual (`ColumnasPdfAnual` sobre `TotalMensualDto`, `ArmarResumenAnual`, `libro-caja-AAAA.pdf`, "Año completo: AAAA."); con `LibroMes` el mensual de hoy, rotulado con el mes CARGADO. `PuedeExportar => !SinPermiso && !Cargando && (LibroMes is not null \|\| LibroAnual is not null) && !SinMovimientosEnElPeriodo`, re-notificado como en Control POA. El anual tiene 4 columnas: sale vertical (`MaximoColumnasEnVertical = 6`). Sin aviso de volumen en el anual (12 filas). | `ControlPoaViewModel.cs` L139-213; `PlantillaTabular.cs` L39, L171; spec sección 2. |
| `ColumnasPdfContraDtosRealesTests` | NO cubre hoy el anual (solo `LibroCaja_ColumnasPdf_…` sobre `MovimientoCajaDto`, L139-145): la Task 4 agrega `LibroCajaAnual_ColumnasPdf_…` contra `TotalMensualDto` y un PDF real con el resumen anual (vertical, sin "Totales por fuente"). | `ColumnasPdfContraDtosRealesTests.cs` L139-152. |
| Nombre del mes | `NombreMesConverter` (nuevo, con `Nombre(int)` estático): lo usan la columna MES y la sección "Meses con déficit" del PDF. "Setiembre", como en Uruguay. | P3; D9. |
| Texto negativo fuera de la grilla | Hoy `.negativo` solo existe dentro de una celda (`DataGridV2.axaml` L174-176; `.v2-grilla-celda .v2-texto-peligro` en `base.css` L314): un saldo fuera de la grilla con `Classes.negativo` NO se pinta. La Task 7 agrega `.ui2 TextBlock.negativo` en `ControlesV2.axaml` después de los estilos de texto (L65-69) y su espejo en `base.css`. | `DataGridV2.axaml`, `ControlesV2.axaml`, `base.css`. |
| Mes inválido oculto | `IsVisible=False` no saca al control del árbol: `ErroresDeEntradaBehavior` sigue contando su error (`ErroresDeEntradaBehavior.cs` L14-33). El code-behind apaga `NumericUpDownValidacionBehavior.Validar` en el campo Mes al pasar a Año completo (su `Changed` llama `Limpiar`, que hace `DataValidationErrors.ClearErrors`, L167-173) y repone el texto del VM; al volver a Mes lo prende. | `NumericUpDownValidacionBehavior.cs` L41-56, L167-173; `Themes/Controls.axaml` L231. |
| Datos fijos en modo año | `VisualTests` no ve los `internal` del VM (`InternalsVisibleTo` solo a `Presentation.Tests` y `Presentation.UiTests`): `DatosLibroCaja.AnioCompleto()` setea `ModoSeleccionado` y el debounce de 300 ms recarga lo mismo (el `Moq` responde igual), sin cambiar el render. | `StockApp.Presentation.csproj` L34-43. |
| Guardianes de la vista vieja | `LibroCajaView` sale de la `InlineData` del `HeaderVista` (L66) y de `VistasDeLaTanda` (L114) y entra a `VistasMigradasAV2` (L197-218); la reemplaza `Raiz_TieneUi2_EncabezadoConTitulo_ActualizarYExportarSecundarios_SinPrimario` (convención 13). | `GuardianDePatronTests.cs`. |
| Tests heredados que tocan Libro caja | `LibroCajaViewModelTests` (Crear L18-40; anuales L97-128 y L149-162), `LibroCajaViewTests` (etiquetas por `Classes="caption"`, L89-111), `SignoNegativoBadgeTests` (L142-223, `BadgeEstado` "Saldo negativo"), `GuardianDePatronTests` (L66, L114), `GrillasSinRecorteBarridoTests` (escenario "Libro de caja (movimientos)", L118-121: la grilla de movimientos tiene que seguir siendo la PRIMERA `DataGrid` del árbol lógico), `ExportarPdfBotonesGuardianTests` (L388-436: afirma `IsVisible` y `Command`; con el mes vacío el botón queda visible y deshabilitado, sigue verde), `ColumnasPdfContraDtosRealesTests` (se extiende). Sin cambio, solo regresión: `NumericUpDownVistasRealesTests` (solo un comentario en L69 apunta a `LibroCajaViewTests`), `ShellMainViewGatesTests` (L148, el ítem del sidebar), `AccesoRevocadoPantallasFinanzasTests` (no toca Libro caja). | grep `LibroCaja` en `tests/`. |

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/Converters/NombreMesConverter.cs` | Mes 1 a 12 → "Enero" … "Diciembre". |
| `tests/StockApp.Presentation.Tests/Converters/NombreMesConverterTests.cs` | Sus casos. |
| `tests/StockApp.Presentation.UiTests/V2/TextoNegativoV2Tests.cs` | El estilo `.ui2 TextBlock.negativo` fuera de la grilla. |
| `tests/StockApp.Presentation.UiTests/V2/LibroCajaViewV2Tests.cs` | La View real: estructura, filtros, modos, grillas, saldos, estados, PDF, gates y 403. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosLibroCaja.cs`, `DatosLibroCajaTests.cs` | Datos fijos de los 7 mockups y sus textos. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/LibroCajaComparacionTests.cs` | 14 comparaciones. |
| `docs/ui-v2/mockups/finanzas/libro-caja*.html` | Los 7 mockups de D16. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs` | E1, E2, E3, E4 (P1), E6. |
| `src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml` (+ `.axaml.cs`) | Reescrita en v2; code-behind: carga, y el campo Mes al ocultarse. |
| `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml`, `docs/ui-v2/mockups/_base/base.css` | `.negativo` fuera de la grilla y su espejo. |
| `tests/StockApp.Presentation.UiTests/V2/FinanzasVistasFakes.cs` | `FinanzasVistasFake` responde también el libro caja (L37-38). |
| Tests heredados (convención 13) | `LibroCajaViewModelTests`, `LibroCajaViewTests`, `SignoNegativoBadgeTests`, `GuardianDePatronTests`, `ColumnasPdfContraDtosRealesTests` (extiende), `GrillasSinRecorteBarridoTests` (solo si hace falta). |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, `docs/ui-v2/herramientas.md` | Cierre (Task 11). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln` (falsos `.ods` e intermitente de arranque: confirmar por nombre)
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Regresión de Libro caja (tras cada task de los bloques A y C): `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~ColumnasPdfContraDtosReales|FullyQualifiedName~NombreMes|FullyQualifiedName~ViewModelBaseCargaProtegida|FullyQualifiedName~ControlPoa"` y `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~SignoNegativoBadge|FullyQualifiedName~NumericUpDownVistasReales|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~GrillasEspacioDerecho|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~DataGridColumnas|FullyQualifiedName~TextoNegativo|FullyQualifiedName~ControlPoa"`
- Comparación con página: `scripts/ui-v2/comparar.sh LibroCaja` y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-13-ui-v2-fase-4c/progress.md` (lo crea la Task 1 con BASE = el commit de este plan; `.superpowers/` está en `.gitignore`). Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa. Arriba de todo, la elección del usuario para P1, P2 y P3.

---

## Bloque A — ViewModel, sin tocar la vista

### Task 1: Carga protegida, consulta vigente, sin conexión y "lo que se ve es lo cargado" (E1)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs` (summary L17-20; campos L34-36; `CargarAsync` L59-96)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs` (helpers nuevos tras `Crear`, L40)
- Create (local): el ledger

**Interfaces:**
- Consumes: `ViewModelBase.EjecutarCargaProtegidaAsync(Func<Task>, string, ConsultaVigente.Turno)`, `ConsultaVigente`, `AvisaSinConexionEnLinea`, `AlPerderConexion()`, `ReintentarAsync(Func<Task>)`, `ReintentarCargaCommand`, `Cargando`, `SinPermiso`, `MensajeSinPermiso`, `SinConexion`.
- Produces: `public const string MensajeSinPermisoLibroCaja = "No tenés permiso para ver el libro caja."`; `LibroCajaMesDto? LibroMes` (observable); `bool SinMovimientosEnElPeriodo` (setter privado, notifica); `int MesesConMovimientos` (setter privado, notifica); `CargarAsync()` con turno que captura `Anio`, `Mes` y `VerAnioCompleto` al empezar; `private void AplicarMes(LibroCajaMesDto)`, `private void AplicarAnual(LibroCajaAnualDto)`, `private void LimpiarPeriodo()`.

- [ ] **Step 1: Línea base y ledger**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln 2>&1 | tail -40
mkdir -p .superpowers/sdd/2026-10-13-ui-v2-fase-4c
```

Crear `.superpowers/sdd/2026-10-13-ui-v2-fase-4c/progress.md` con `BASE: <hash del commit de este plan>`, la elección de P1, P2 y P3, el total de la suite por proyecto (esperado 5789 verdes) y los rojos `.ods` / intermitentes por nombre.

- [ ] **Step 2: Tests (rojo)**

En `LibroCajaViewModelTests.cs`, agregar `using StockApp.ApiClient;` y, después de `Crear()`:

```csharp
    private static MovimientoCajaDto Mov(int dia, decimal ingreso, decimal egreso, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), ingreso > 0m ? "Ingreso" : "Egreso", $"Movimiento {dia}",
            null, null, null, null, ingreso, egreso, corrido);

    private static LibroCajaMesDto MesDe(int anio, int mes, decimal saldoInicial, decimal saldoFinal, params MovimientoCajaDto[] movimientos) =>
        new(anio, mes, saldoInicial, saldoFinal, movimientos, new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>());

    private static LibroCajaAnualDto AnioDe(int anio, params TotalMensualDto[] meses) =>
        new(anio, meses, new List<TotalPorClaveDto>());

    private static TotalMensualDto[] DoceMeses(params TotalMensualDto[] conMovimiento) =>
        Enumerable.Range(1, 12)
            .Select(m => conMovimiento.FirstOrDefault(t => t.Mes == m) ?? new TotalMensualDto(m, 0m, 0m, 0m))
            .ToArray();

    [Fact]
    public async Task CargarAsync_MientrasEspera_CargandoEsTrue_YAlTerminarFalse()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var pendiente = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);

        pendiente.SetResult(MesDe(2026, 10, 0m, 0m));
        await carga;

        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task CargarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(LibroCajaViewModel.MensajeSinPermisoLibroCaja, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ServidorCaido_TrasUnaCargaBuena_MarcaSinConexionYVaciaLoCargado()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.SetupSequence(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(MesDe(2026, 10, 0m, 500m, Mov(1, 500m, 0m, 500m)))
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();

        await vm.CargarAsync();

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Movimientos);
        Assert.Null(vm.LibroMes);
        Assert.Null(vm.LibroAnual);
        Assert.False(vm.SinMovimientosEnElPeriodo);
    }

    [Fact]
    public async Task Reintentar_TrasUnaCaida_VuelveACargar()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.SetupSequence(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(MesDe(2026, 10, 0m, 500m, Mov(1, 500m, 0m, 500m)));
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Single(vm.Movimientos);
    }

    [Fact]
    public async Task ConsultaVieja_QueTerminaDespues_NoPisaLaNueva()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var vieja = new TaskCompletionSource<LibroCajaMesDto>();
        var nueva = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 9)).Returns(vieja.Task);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).Returns(nueva.Task);
        vm.Anio = 2026;
        vm.Mes = 9;

        var primera = vm.CargarAsync();
        vm.Mes = 10;
        var segunda = vm.CargarAsync();
        nueva.SetResult(MesDe(2026, 10, 0m, 10m, Mov(2, 10m, 0m, 10m)));
        await segunda;
        vieja.SetResult(MesDe(2026, 9, 0m, 99m, Mov(9, 99m, 0m, 99m)));
        await primera;

        Assert.Equal(10, vm.LibroMes!.Mes);
        Assert.Equal(new DateOnly(2026, 10, 2), Assert.Single(vm.Movimientos).Fecha);
    }

    [Fact]
    public async Task RespuestaMensualVieja_TrasPasarAAnio_NoPisaElAnual()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var mensual = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).Returns(mensual.Task);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026))
            .ReturnsAsync(AnioDe(2026, DoceMeses(new TotalMensualDto(3, 100m, 0m, 100m))));
        vm.Anio = 2026;
        vm.Mes = 10;

        var primera = vm.CargarAsync();
        vm.VerAnioCompleto = true;
        await vm.CargarAsync();
        mensual.SetResult(MesDe(2026, 10, 0m, 10m, Mov(2, 10m, 0m, 10m)));
        await primera;

        Assert.NotNull(vm.LibroAnual);
        Assert.Null(vm.LibroMes);
        Assert.Empty(vm.Movimientos);
    }

    [Fact]
    public async Task RespuestaAnualVieja_TrasVolverAMes_NoPisaElMes()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var anual = new TaskCompletionSource<LibroCajaAnualDto>();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).Returns(anual.Task);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).ReturnsAsync(MesDe(2026, 10, 0m, 10m, Mov(2, 10m, 0m, 10m)));
        vm.Anio = 2026;
        vm.Mes = 10;
        vm.VerAnioCompleto = true;

        var primera = vm.CargarAsync();
        vm.VerAnioCompleto = false;
        await vm.CargarAsync();
        anual.SetResult(AnioDe(2026, DoceMeses(new TotalMensualDto(3, 100m, 0m, 100m))));
        await primera;

        Assert.NotNull(vm.LibroMes);
        Assert.Null(vm.LibroAnual);
        Assert.Single(vm.Movimientos);
    }

    [Fact]
    public async Task CargarAnual_TrasUnMes_QuitaLoDelMes()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new LibroCajaMesDto(
            2026, 10, 0m, 10m, new List<MovimientoCajaDto> { Mov(2, 10m, 0m, 10m) },
            new List<TotalPorClaveDto> { new("Combustibles", 5m) }, new List<TotalPorClaveDto> { new("Rentas", 10m) }));
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(AnioDe(2026, DoceMeses(new TotalMensualDto(3, 100m, 0m, 100m))));
        await vm.CargarAsync();

        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        Assert.Null(vm.LibroMes);
        Assert.NotNull(vm.LibroAnual);
        Assert.Empty(vm.Movimientos);
        Assert.Empty(vm.TotalesPorRubro);
        Assert.Empty(vm.TotalesPorFuente);
    }

    [Fact]
    public void SinNadaCargado_NoEsSinMovimientos()
    {
        var (vm, _, _, _, _, _, _) = Crear();

        Assert.False(vm.SinMovimientosEnElPeriodo);
        Assert.Null(vm.LibroMes);
        Assert.Null(vm.LibroAnual);
    }

    [Fact]
    public async Task Mes_SinMovimientos_EsSinMovimientosEnElPeriodo()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 300m, 300m));

        await vm.CargarAsync();

        Assert.True(vm.SinMovimientosEnElPeriodo);
        Assert.Equal(300m, vm.SaldoFinal);
    }

    [Fact]
    public async Task Anual_DoceMesesEnCero_EsSinMovimientos()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>())).ReturnsAsync(AnioDe(2026, DoceMeses()));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.Equal(12, vm.LibroAnual!.TotalesPorMes.Count);
        Assert.True(vm.SinMovimientosEnElPeriodo);
        Assert.Equal(0, vm.MesesConMovimientos);
    }

    [Fact]
    public async Task Anual_MesSoloConEgresos_NoEsSinMovimientos_YCuentaComoMesConMovimientos()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>())).ReturnsAsync(AnioDe(2026, DoceMeses(
            new TotalMensualDto(3, 0m, 250m, -250m), new TotalMensualDto(7, 1000m, 0m, 1000m))));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.False(vm.SinMovimientosEnElPeriodo);
        Assert.Equal(2, vm.MesesConMovimientos);
    }
```

El heredado `CargarAsync_SiObtenerLibroCajaMesLanzaUnauthorized_NoPropagaLaExcepcion` queda igual (sigue custodiando que no propaga).

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCajaViewModelTests"` → FAIL de compilación (`MensajeSinPermisoLibroCaja`, `LibroMes`, `SinMovimientosEnElPeriodo`, `MesesConMovimientos`). Pegar. Con esas referencias comentadas, pegar los rojos de comportamiento (`Cargando` siempre false; `SinPermiso` false; la caída escapa como excepción; la vieja pisa a la nueva).

- [ ] **Step 3: Implementación**

En `LibroCajaViewModel`, reemplazar el `<summary>` de la clase (L17-20) por:

```csharp
/// <summary>
/// Pantalla "Libro caja": un mes (movimientos con saldo corrido, totales por rubro y por fuente) o el año completo
/// (totales por mes y por rubro). Lo que la pantalla muestra y lo que se exporta es el período CARGADO
/// (<see cref="LibroMes"/> o <see cref="LibroAnual"/>), no los filtros: mientras una recarga espera, los filtros pueden
/// ir por delante.
/// </summary>
```

Después de `[ObservableProperty] private LibroCajaAnualDto? _libroAnual;` (L36) agregar:

```csharp
    /// <summary>El mes cargado (null si lo cargado es el año completo o si no hay nada cargado).</summary>
    [ObservableProperty] private LibroCajaMesDto? _libroMes;

    private bool _sinMovimientosEnElPeriodo;
    private int _mesesConMovimientos;

    /// <summary>
    /// True si el período cargado no tiene ningún movimiento: el mes sin filas o el año con los doce meses en cero (el
    /// servidor devuelve siempre los doce, así que un "Count == 0" no sirve). False sin nada cargado: eso es Cargando o
    /// Sin conexión, no un vacío.
    /// </summary>
    public bool SinMovimientosEnElPeriodo
    {
        get => _sinMovimientosEnElPeriodo;
        private set => SetProperty(ref _sinMovimientosEnElPeriodo, value);
    }

    /// <summary>Meses del año cargado con algún ingreso o egreso (pie de la grilla de totales por mes).</summary>
    public int MesesConMovimientos
    {
        get => _mesesConMovimientos;
        private set => SetProperty(ref _mesesConMovimientos, value);
    }
```

Reemplazar `CargarAsync` (L59-96, con su `catch` y el comentario) por:

```csharp
    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403).</summary>
    public const string MensajeSinPermisoLibroCaja = "No tenés permiso para ver el libro caja.";

    private readonly ConsultaVigente _consultas = new();

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar (ver <see cref="ViewModelBase.SinConexion"/>).</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor no queda nada cargado: el reintento no puede mostrar el período de otra consulta.</summary>
    protected override void AlPerderConexion() => LimpiarPeriodo();

    /// <summary>Reintentar consulta el año, el mes y el modo que están en pantalla, no los de la consulta que falló.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    /// <summary>
    /// La dispara la View (DataContextChanged), Actualizar y el cambio de año, mes o modo. Protegida: un 403 deja
    /// SinPermiso (el aviso global ya lo dio AuthTokenHandler), un servidor caído deja SinConexion en vez del modal
    /// global, y mientras espera Cargando está en true. Pide turno: si mientras espera empezó otra carga (del mismo modo
    /// o del otro), su resultado se descarta.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        var anio = Anio;
        var mes = Mes;
        var anual = VerAnioCompleto;
        await EjecutarCargaProtegidaAsync(async () =>
        {
            if (anual)
            {
                var libroAnual = await _service.ObtenerLibroCajaAnualAsync(anio);
                if (!turno.EsLaUltima)
                    return;
                AplicarAnual(libroAnual);
                return;
            }

            var libroMes = await _service.ObtenerLibroCajaMesAsync(anio, mes);
            if (!turno.EsLaUltima)
                return;
            AplicarMes(libroMes);
        }, MensajeSinPermisoLibroCaja, turno);
    }

    private void AplicarMes(LibroCajaMesDto libro)
    {
        LimpiarPeriodo();
        SaldoInicial = libro.SaldoInicial;
        SaldoFinal = libro.SaldoFinal;
        foreach (var mov in libro.Movimientos)
            Movimientos.Add(mov);
        foreach (var t in libro.TotalesPorRubro)
            TotalesPorRubro.Add(t);
        foreach (var t in libro.TotalesPorFuente)
            TotalesPorFuente.Add(t);
        LibroMes = libro;
        SinMovimientosEnElPeriodo = libro.Movimientos.Count == 0;
    }

    private void AplicarAnual(LibroCajaAnualDto libro)
    {
        LimpiarPeriodo();
        LibroAnual = libro;
        MesesConMovimientos = libro.TotalesPorMes.Count(t => t.Ingresos != 0m || t.Egresos != 0m);
        SinMovimientosEnElPeriodo = libro.TotalesPorMes.All(t => t.Ingresos == 0m && t.Egresos == 0m);
    }

    /// <summary>Saca de pantalla el período cargado (el de un modo o el del otro).</summary>
    private void LimpiarPeriodo()
    {
        Movimientos.Clear();
        TotalesPorRubro.Clear();
        TotalesPorFuente.Clear();
        LibroMes = null;
        LibroAnual = null;
        MesesConMovimientos = 0;
        SinMovimientosEnElPeriodo = false;
    }
```

- [ ] **Step 4: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~ViewModelBaseCargaProtegida|FullyQualifiedName~ColumnasPdfContraDtosReales"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~SignoNegativoBadge|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~GrillasSinRecorte"
```

Expected: verde (la vista v1 sigue llamando `CargarAsync`; los heredados de PDF exportan `Movimientos`, que se llena igual). El heredado `ExportarPdfCommand_ConAnioCompleto_NoExporta` sigue verde: `ExportarPdfAsync` todavía mira `VerAnioCompleto` (cambia en la Task 4).

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (a) En la rama mensual de `CargarAsync`, quitar `if (!turno.EsLaUltima) return;` (las dos líneas) → rojo `ConsultaVieja_QueTerminaDespues_NoPisaLaNueva` y `RespuestaMensualVieja_TrasPasarAAnio_NoPisaElAnual`. Restaurar. (Spec: "el descarte de la consulta vieja".)
- (b) Quitar la línea `protected override bool AvisaSinConexionEnLinea => true;` → rojo `ServidorCaido_…` y `Reintentar_…`. Restaurar.
- (c) Quitar la línea `protected override void AlPerderConexion() => LimpiarPeriodo();` → rojo `ServidorCaido_TrasUnaCargaBuena_…`. Restaurar.
- (d) En `AplicarAnual`, quitar `LimpiarPeriodo();` → rojo `CargarAnual_TrasUnMes_QuitaLoDelMes`. Restaurar.
- (e) En `AplicarAnual`, quitar ` && t.Egresos == 0m` → rojo `Anual_MesSoloConEgresos_…`. Restaurar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs
git commit -m "feat(finanzas): el libro caja muestra carga, sin conexión con reintento y sin permiso, y solo la última consulta"
```

### Task 2: Recarga con debounce al cambiar Año, Mes o modo; selector Mes | Año completo; año y mes iniciales en hora local (E2, E3)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs` (campos L30-32; constructor L44-57; usings L1-13)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs` (`Crear` L18-40)
- Modify (local): el ledger

**Interfaces:**
- Consumes: `CargarAsync` (Task 1), `VerAnioCompleto`, `Anio`, `Mes`.
- Produces: constructor `LibroCajaViewModel(IFinanzasVistasService, IServicioGuardadoArchivo, IConfirmacionService, IPdfExporter, IServicioAperturaArchivo, ICurrentSession, TimeProvider? reloj = null)`; `internal TimeSpan EsperaDebounce` (300 ms por defecto); `internal Task _tareaDebounce`; `public const string ModoMes = "Mes"`; `public const string ModoAnioCompleto = "Año completo"`; `public static IReadOnlyList<string> OpcionesModo`; `public string ModoSeleccionado { get; set; }`.

- [ ] **Step 1: Tests (rojo)**

En `LibroCajaViewModelTests.cs`: `Crear()` pasa a `Crear(TimeProvider? reloj = null)`, construye el VM con `session.Object, reloj` y, antes del `return`, agrega `vm.EsperaDebounce = Timeout.InfiniteTimeSpan;` (los tests que no prueban el debounce cargan con `CargarAsync` explícito; cambiar Año, Mes o modo no consulta por su cuenta). Agregar:

```csharp
    private sealed class RelojFijo(DateTimeOffset ahora, TimeZoneInfo? zona = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
        public override TimeZoneInfo LocalTimeZone => zona ?? TimeZoneInfo.Utc;
    }

    private static readonly TimeProvider Octubre2026 = new RelojFijo(new DateTimeOffset(2026, 10, 6, 13, 0, 0, TimeSpan.Zero));

    [Fact]
    public void AnioYMesIniciales_SalenDelReloj_YNoConsulta()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);

        Assert.Equal(2026, vm.Anio);
        Assert.Equal(10, vm.Mes);
        svc.Verify(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void MesInicial_SaleDeLaHoraLocal_NoDeLaUtc()
    {
        // 31/10 a las 22:00 en Uruguay (UTC-3) ya es 01/11 en UTC: el libro tiene que abrir octubre.
        var uruguay = TimeZoneInfo.CreateCustomTimeZone("UY-3", TimeSpan.FromHours(-3), "UY-3", "UY-3");
        var reloj = new RelojFijo(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero), uruguay);

        var (vm, _, _, _, _, _, _) = Crear(reloj);

        Assert.Equal(2026, vm.Anio);
        Assert.Equal(10, vm.Mes);
    }

    [Fact]
    public async Task CambiarMes_RecargaSoloConElMesNuevo()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.EsperaDebounce = TimeSpan.Zero;
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 9)).ReturnsAsync(MesDe(2026, 9, 0m, 0m));

        vm.Mes = 9;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 9), Times.Once);
        Assert.Equal(9, vm.LibroMes!.Mes);
    }

    [Fact]
    public async Task CambiarAnio_EnModoMes_RecargaEseMesDelAnioNuevo()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.EsperaDebounce = TimeSpan.Zero;
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2025, 10)).ReturnsAsync(MesDe(2025, 10, 0m, 0m));

        vm.Anio = 2025;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2025, 10), Times.Once);
    }

    [Fact]
    public async Task PasarAAnioCompleto_RecargaElAnual()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.EsperaDebounce = TimeSpan.Zero;
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 1, 0m, 0m));
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(AnioDe(2026, DoceMeses()));

        vm.ModoSeleccionado = LibroCajaViewModel.ModoAnioCompleto;
        await vm._tareaDebounce;

        Assert.True(vm.VerAnioCompleto);
        svc.Verify(s => s.ObtenerLibroCajaAnualAsync(2026), Times.Once);
        Assert.NotNull(vm.LibroAnual);
    }

    [Fact]
    public async Task CambiarAnio_EnAnioCompleto_RecargaElAnualDeEseAnio()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2025, 1, 0m, 0m));
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>())).ReturnsAsync(AnioDe(2025, DoceMeses()));
        vm.VerAnioCompleto = true;
        vm.EsperaDebounce = TimeSpan.Zero;

        vm.Anio = 2025;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaAnualAsync(2025), Times.Once);
    }

    [Fact]
    public async Task CambiarMes_EnAnioCompleto_NoRecarga()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.VerAnioCompleto = true;
        vm.EsperaDebounce = TimeSpan.Zero;

        vm.Mes = 3;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()), Times.Never);
        svc.Verify(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CambiosRapidos_SoloConsultaElUltimo()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.EsperaDebounce = TimeSpan.FromMilliseconds(50);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 8, 0m, 0m));

        vm.Mes = 9;
        vm.Mes = 8;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 9), Times.Never);
        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 8), Times.Once);
    }

    [Fact]
    public void ModoSeleccionado_TraduceAVerAnioCompleto_YLoNotifica()
    {
        var (vm, _, _, _, _, _, _) = Crear(Octubre2026);
        var notificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => notificadas.Add(e.PropertyName);

        Assert.Equal(new[] { "Mes", "Año completo" }, LibroCajaViewModel.OpcionesModo);
        Assert.Equal(LibroCajaViewModel.ModoMes, vm.ModoSeleccionado);

        vm.VerAnioCompleto = true;

        Assert.Equal(LibroCajaViewModel.ModoAnioCompleto, vm.ModoSeleccionado);
        Assert.Contains(nameof(LibroCajaViewModel.ModoSeleccionado), notificadas);
    }

    [Fact]
    public async Task Reintentar_ConsultaElPeriodoQueEstaEnPantalla()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).ThrowsAsync(new ServidorNoDisponibleException());
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 9)).ReturnsAsync(MesDe(2026, 9, 0m, 0m));
        await vm.CargarAsync();
        vm.Mes = 9;   // el debounce infinito deja el cambio en pantalla sin consultarlo

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 9), Times.Once);
        Assert.False(vm.SinConexion);
        Assert.Equal(9, vm.LibroMes!.Mes);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCajaViewModelTests"` → FAIL de compilación (constructor con reloj, `EsperaDebounce`, `_tareaDebounce`, `ModoSeleccionado`, `OpcionesModo`). Pegar.

- [ ] **Step 2: Implementación**

En `LibroCajaViewModel`: agregar `using System.Threading;`. Reemplazar los campos L30-32 por:

```csharp
    [ObservableProperty] private int _anio;
    [ObservableProperty] private int _mes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModoSeleccionado))]
    private bool _verAnioCompleto;
```

El constructor gana `TimeProvider? reloj = null` como último parámetro y, como primeras líneas:

```csharp
        // Año y mes arrancan de la fecha LOCAL: en Uruguay, de 21 a 24 del último día del mes la hora UTC ya es del mes
        // siguiente. Se escriben los campos: no disparan la recarga, la primera carga la pide la vista.
        var hoy = (reloj ?? TimeProvider.System).GetLocalNow();
        _anio = hoy.Year;
        _mes = hoy.Month;
```

Después del constructor:

```csharp
    public const string ModoMes = "Mes";
    public const string ModoAnioCompleto = "Año completo";

    /// <summary>Opciones del selector de período.</summary>
    public static IReadOnlyList<string> OpcionesModo { get; } = [ModoMes, ModoAnioCompleto];

    /// <summary>La opción del selector, traducida a <see cref="VerAnioCompleto"/>.</summary>
    public string ModoSeleccionado
    {
        get => VerAnioCompleto ? ModoAnioCompleto : ModoMes;
        set => VerAnioCompleto = value == ModoAnioCompleto;
    }

    /// <summary>Espera antes de recargar al cambiar año, mes o modo: un año de 4 cifras son 4 cambios y solo el último
    /// tiene que ir al servidor.</summary>
    private static readonly TimeSpan DebouncePorDefecto = TimeSpan.FromMilliseconds(300);

    /// <summary>Espera real del debounce; los tests que no miden el tiempo la bajan a cero.</summary>
    internal TimeSpan EsperaDebounce { get; set; } = DebouncePorDefecto;

    /// <summary>Tarea del último debounce, para que los tests la esperen de forma determinista.</summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    private CancellationTokenSource? _debounceCts;

    partial void OnAnioChanged(int value) => ProgramarRecarga();

    /// <summary>En año completo el mes no cambia la consulta: no recarga.</summary>
    partial void OnMesChanged(int value)
    {
        if (!VerAnioCompleto)
            ProgramarRecarga();
    }

    partial void OnVerAnioCompletoChanged(bool value) => ProgramarRecarga();

    /// <summary>Un cambio nuevo cancela la espera anterior. Un texto inválido en un NumericUpDown no llega acá (el VM
    /// conserva el valor), así que no recarga con el valor viejo.</summary>
    private void ProgramarRecarga()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        _tareaDebounce = DebounceCargarAsync(_debounceCts.Token);
    }

    private async Task DebounceCargarAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(EsperaDebounce, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        await CargarAsync();
    }
```

Antes de cerrar, confirmar con `codegraph callers LibroCajaViewModel` (CLI del worktree) que ningún otro constructor lo instancia con argumentos posicionales que choquen (los tests y la DI usan los 6 primeros).

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~ControlPoa"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~SignoNegativoBadge|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~NumericUpDownVistasReales"
```

Expected: verde. `LibroCajaViewTests.Mes_Invalido_…` sigue verde (el texto inválido no llega al VM, no recarga). Los heredados que ponen `vm.Anio = 2026; vm.Mes = 7;` no consultan solos (debounce infinito en `Crear`).

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (f) Quitar la línea `partial void OnVerAnioCompletoChanged(bool value) => ProgramarRecarga();` → rojo `PasarAAnioCompleto_RecargaElAnual`. Pegar. Restaurar. (Spec: "la recarga al cambiar el modo".)
- (g) En `OnMesChanged`, quitar la línea `if (!VerAnioCompleto)` → rojo `CambiarMes_EnAnioCompleto_NoRecarga`. Restaurar.
- (h) En `ProgramarRecarga`, quitar `_debounceCts?.Cancel();` → rojo `CambiosRapidos_SoloConsultaElUltimo`. Restaurar.
- (i) Quitar la línea `protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();` → rojo `Reintentar_ConsultaElPeriodoQueEstaEnPantalla`. Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs
git commit -m "feat(finanzas): el libro caja recarga solo al cambiar año, mes o modo y abre en el mes local"
```

### Task 3: Saldos del año (E4, P1-A)

**Si el usuario eligió P1-B, esta task no se hace:** anotarlo en el ledger y pasar a la Task 4 (en la Task 4, `ArmarResumenAnual` pasa `Totales: []`).

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs` (rama anual de `CargarAsync` y `AplicarAnual`, de la Task 1)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs` (heredados L97-128 y L149-162; tests anuales de las Tasks 1 y 2)
- Modify (local): el ledger

**Interfaces:**
- Consumes: `AplicarAnual`, `CargarAsync`, `ObtenerLibroCajaMesAsync(anio, 1)`.
- Produces: en modo año, `SaldoInicial` = `SaldoInicial` de enero del año y `SaldoFinal` = `SaldoInicial` + Σ `Neto`; firma `private void AplicarAnual(LibroCajaAnualDto libro, decimal saldoInicialDelAnio)`; en el test, `private static void ConEnero(Mock<IFinanzasVistasService> svc, decimal saldoInicial = 0m)`.

- [ ] **Step 1: Tests (rojo)**

En `LibroCajaViewModelTests.cs`:

```csharp
    /// <summary>El modo año también consulta enero: su saldo inicial es el saldo inicial del año.</summary>
    private static void ConEnero(Mock<IFinanzasVistasService> svc, decimal saldoInicial = 0m) =>
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), 1))
            .ReturnsAsync((int anio, int _) => MesDe(anio, 1, saldoInicial, saldoInicial));

    [Fact]
    public async Task Anual_SaldoInicialEsElDeEnero_YElFinalSumaLosNetos()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        ConEnero(svc, saldoInicial: 812_430.55m);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(AnioDe(2026, DoceMeses(
            new TotalMensualDto(1, 1_250_000m, 980_200m, 269_800m),
            new TotalMensualDto(3, 1_250_000m, 1_322_750m, -72_750m))));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 1), Times.Once);
        Assert.Equal(812_430.55m, vm.SaldoInicial);
        Assert.Equal(1_009_480.55m, vm.SaldoFinal);
    }

    [Fact]
    public async Task Anual_SaldoFinalNegativo_SiLosNetosSuperanElInicial()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        ConEnero(svc, saldoInicial: 50_000m);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026))
            .ReturnsAsync(AnioDe(2026, DoceMeses(new TotalMensualDto(2, 0m, 80_000m, -80_000m))));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.Equal(-30_000m, vm.SaldoFinal);
    }

    [Fact]
    public async Task Anual_403EnLaConsultaDeEnero_MarcaSinPermiso()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(AnioDe(2026, DoceMeses()));
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 1)).ThrowsAsync(new UnauthorizedAccessException("403"));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
    }
```

Adaptar (convención 13; anotar antes y después en el ledger): agregar `ConEnero(svc);` como primera línea después de `Crear(...)` en los heredados `VerAnioCompleto_True_PideLibroCajaAnual` (L97-109), `VerAnioCompleto_ExponeTotalesPorRubroDelAnio` (L111-128) y `ExportarPdfCommand_ConAnioCompleto_NoExporta` (L149-162), y en los tests de las Tasks 1 y 2 que cargan el año (`RespuestaMensualVieja_…`, `RespuestaAnualVieja_…`, `CargarAnual_TrasUnMes_…`, `Anual_DoceMesesEnCero_…`, `Anual_MesSoloConEgresos_…`, `CambiarAnio_EnAnioCompleto_…`; en los que ya tienen un `Setup` de `ObtenerLibroCajaMesAsync` con `It.IsAny` para los dos argumentos, `ConEnero` va DESPUÉS de ese `Setup`, porque en Moq gana el último que coincide). Sin `ConEnero` esos tests caen por un `NullReferenceException` (Moq devuelve una tarea con `null`): pegar uno de esos rojos.

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCajaViewModelTests"` → FAIL (`SaldoInicial` 0, `ObtenerLibroCajaMesAsync(2026, 1)` nunca llamada, `SinPermiso` false). Pegar.

- [ ] **Step 2: Implementación**

En la rama anual de `CargarAsync`:

```csharp
            if (anual)
            {
                var libroAnual = await _service.ObtenerLibroCajaAnualAsync(anio);
                // El año no trae saldos: el inicial del año es el inicial de enero (sin cambiar la API).
                var enero = await _service.ObtenerLibroCajaMesAsync(anio, 1);
                if (!turno.EsLaUltima)
                    return;
                AplicarAnual(libroAnual, enero.SaldoInicial);
                return;
            }
```

Y `AplicarAnual` pasa a:

```csharp
    /// <summary>El saldo final del año es el inicial más los netos de los doce meses: la misma cuenta del saldo corrido
    /// que hace el servidor mes a mes.</summary>
    private void AplicarAnual(LibroCajaAnualDto libro, decimal saldoInicialDelAnio)
    {
        LimpiarPeriodo();
        SaldoInicial = saldoInicialDelAnio;
        SaldoFinal = saldoInicialDelAnio + libro.TotalesPorMes.Sum(t => t.Neto);
        LibroAnual = libro;
        MesesConMovimientos = libro.TotalesPorMes.Count(t => t.Ingresos != 0m || t.Egresos != 0m);
        SinMovimientosEnElPeriodo = libro.TotalesPorMes.All(t => t.Ingresos == 0m && t.Egresos == 0m);
    }
```

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCaja"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~SignoNegativoBadge|FullyQualifiedName~ExportarPdfBotones"
```

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (j) En `AplicarAnual`, quitar ` + libro.TotalesPorMes.Sum(t => t.Neto)` → rojo `Anual_SaldoInicialEsElDeEnero_YElFinalSumaLosNetos` y `Anual_SaldoFinalNegativo_…`. Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs
git commit -m "feat(finanzas): el libro caja anual muestra el saldo inicial y final del año"
```

### Task 4: PDF anual y habilitación del exporte (E6)

**Files:**
- Create: `src/StockApp.Presentation/Converters/NombreMesConverter.cs`, `tests/StockApp.Presentation.Tests/Converters/NombreMesConverterTests.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs` (`ColumnasPdf` L102-118 se conserva; `ExportarPdfAsync` L120-168 se reemplaza)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs` (heredado L149-162), `tests/StockApp.Presentation.Tests/Exportacion/ColumnasPdfContraDtosRealesTests.cs` (después de L145)
- Modify (local): el ledger

**Interfaces:**
- Consumes: `LibroMes`, `LibroAnual`, `SinMovimientosEnElPeriodo`, `SaldoInicial`, `SaldoFinal` (Tasks 1 a 3); `ExportacionPdf.EjecutarAsync(Func<Task>, IConfirmacionService)`, `ExportacionPdf.OfrecerAbrirAsync(…)`, `AvisoVolumenExportacion.ConfirmarAsync(int, IConfirmacionService)`, `IPdfExporter.Exportar<T>(IEnumerable<T>, IReadOnlyList<ColumnaPdf>, MetadatosDocumento, ResumenPdf?)`.
- Produces: `NombreMesConverter.Instance`, `public static string NombreMesConverter.Nombre(int mes)`; `public static readonly IReadOnlyList<ColumnaPdf> LibroCajaViewModel.ColumnasPdfAnual`; `public bool PuedeExportar`; `ExportarPdfCommand` con `CanExecute = PuedeExportar`; `internal static ResumenPdf ArmarResumenAnual(LibroCajaAnualDto anual, decimal saldoInicial, decimal saldoFinal)`.

- [ ] **Step 1: Tests (rojo)**

`tests/StockApp.Presentation.Tests/Converters/NombreMesConverterTests.cs`:

```csharp
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class NombreMesConverterTests
{
    [Theory]
    [InlineData(1, "Enero")]
    [InlineData(3, "Marzo")]
    [InlineData(9, "Setiembre")]
    [InlineData(12, "Diciembre")]
    [InlineData(13, "13")]
    [InlineData(0, "0")]
    public void Nombre_DelMes(int mes, string esperado) => Assert.Equal(esperado, NombreMesConverter.Nombre(mes));

    [Fact]
    public void Convert_DeUnInt_DaElNombre_YDeOtraCosa_Vacio()
    {
        Assert.Equal("Junio", NombreMesConverter.Instance.Convert(6, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, NombreMesConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
```

En `LibroCajaViewModelTests.cs`:

```csharp
    private static LibroCajaAnualDto Anio2026ConDeficit() => new(2026,
        DoceMeses(
            new TotalMensualDto(1, 1_250_000m, 980_200m, 269_800m),
            new TotalMensualDto(3, 1_250_000m, 1_322_750m, -72_750m)),
        new List<TotalPorClaveDto> { new("Obras viales", 2_302_950m) });

    private static async Task<(LibroCajaViewModel vm, Mock<IFinanzasVistasService> svc, Mock<IServicioGuardadoArchivo> guardado,
        Mock<IPdfExporter> pdf)> AnualCargadoAsync(LibroCajaAnualDto anual)
    {
        var (vm, svc, guardado, _, pdf, _, _) = Crear(Octubre2026);
        ConEnero(svc, saldoInicial: 812_430.55m);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(anual);
        vm.VerAnioCompleto = true;
        await vm.CargarAsync();
        pdf.Setup(e => e.Exportar(It.IsAny<IEnumerable<TotalMensualDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>())).Returns(new byte[] { 1 });
        guardado.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
        return (vm, svc, guardado, pdf);
    }

    [Fact]
    public async Task ExportarPdf_Anual_CuatroColumnasDelDtoReal_TituloYNombreDelAnio()
    {
        var esperado = new[]
        {
            new ColumnaPdf("Mes", "Mes"), new ColumnaPdf("Ingresos", "Ingresos"),
            new ColumnaPdf("Egresos", "Egresos"), new ColumnaPdf("Neto", "Neto"),
        };
        var anual = Anio2026ConDeficit();
        var (vm, _, guardado, pdf) = await AnualCargadoAsync(anual);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdf.Verify(e => e.Exportar(
            anual.TotalesPorMes,
            It.Is<IReadOnlyList<ColumnaPdf>>(c => c.SequenceEqual(esperado)),
            It.Is<MetadatosDocumento>(m => m.Titulo == "Libro caja" && m.DescripcionFiltros == "Año completo: 2026."),
            It.IsAny<ResumenPdf>()), Times.Once);
        guardado.Verify(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "libro-caja-2026.pdf", It.IsAny<CancellationToken>(),
            "pdf", "application/pdf"), Times.Once);
        pdf.Verify(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdf_Anual_ResumenConSaldosDelAnio_MesesConDeficit_YRubros_SinFuentes()
    {
        var (vm, _, _, pdf) = await AnualCargadoAsync(Anio2026ConDeficit());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdf.Verify(e => e.Exportar(It.IsAny<IEnumerable<TotalMensualDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(),
            It.Is<ResumenPdf>(r =>
                r.Totales.Count == 2
                && r.Totales[0].Etiqueta == "Saldo inicial" && Equals(r.Totales[0].Valor, 812_430.55m)
                && r.Totales[1].Etiqueta == "Saldo final" && Equals(r.Totales[1].Valor, 1_009_480.55m)
                && r.Secciones.Count == 2
                && r.Secciones[0].Titulo == "Meses con déficit"
                && r.Secciones[0].Filas.Count == 1
                && r.Secciones[0].Filas[0].Etiqueta == "Marzo" && Equals(r.Secciones[0].Filas[0].Valor, -72_750m)
                && r.Secciones[1].Titulo == "Totales por rubro"
                && r.Secciones[1].Filas[0].Etiqueta == "Obras viales"
                && r.Secciones.All(s => s.Titulo != "Totales por fuente"))), Times.Once);
    }

    [Fact]
    public void ResumenAnual_SinDeficit_NoAgregaLaSeccion()
    {
        var anual = new LibroCajaAnualDto(2026, DoceMeses(new TotalMensualDto(1, 10m, 5m, 5m)), new List<TotalPorClaveDto>());

        var resumen = LibroCajaViewModel.ArmarResumenAnual(anual, 0m, 5m);

        Assert.Equal(new[] { "Totales por rubro" }, resumen.Secciones.Select(s => s.Titulo));
    }

    [Fact]
    public void Exportar_SinNadaCargado_NoSePuede()
    {
        var (vm, _, _, _, _, _, _) = Crear(Octubre2026);

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_MesSinMovimientos_NoSePuede()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 10m, 10m));

        await vm.CargarAsync();

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_AnioSinMovimientos_NoSePuede()
    {
        var (vm, _, _, _) = await AnualCargadoAsync(new LibroCajaAnualDto(2026, DoceMeses(), new List<TotalPorClaveDto>()));

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_AnioConMovimientos_SePuede()
    {
        var (vm, _, _, _) = await AnualCargadoAsync(Anio2026ConDeficit());

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        Assert.True(vm.PuedeExportar);
    }

    [Fact]
    public async Task Exportar_ConSinPermiso_NoSePuede()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await vm.CargarAsync();
        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.NotNull(vm.LibroMes);   // un 403 no limpia lo cargado: lo que bloquea es SinPermiso
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_MientrasCarga_NoSePuede_YAlTerminarNotificaYVuelve()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await vm.CargarAsync();
        var pendiente = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(pendiente.Task);
        var avisos = 0;
        vm.ExportarPdfCommand.CanExecuteChanged += (_, _) => avisos++;

        var carga = vm.CargarAsync();
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
        var avisosAlEmpezar = avisos;

        pendiente.SetResult(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await carga;

        Assert.True(avisosAlEmpezar > 0);
        Assert.True(avisos > avisosAlEmpezar);
        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportarPdf_ConMesCambiadoSinRecargar_ExportaElMesCargado()
    {
        var (vm, svc, guardado, _, pdf, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 9)).ReturnsAsync(MesDe(2026, 9, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        vm.Mes = 9;
        await vm.CargarAsync();
        vm.Mes = 10;   // el debounce infinito deja octubre en el filtro y setiembre en pantalla
        pdf.Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>())).Returns(new byte[] { 1 });
        guardado.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdf.Verify(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m => m.DescripcionFiltros == "Mes: 09/2026."), It.IsAny<ResumenPdf>()), Times.Once);
        guardado.Verify(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "libro-caja-2026-09.pdf", It.IsAny<CancellationToken>(),
            "pdf", "application/pdf"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdf_RecienPasadoAAnioSinRecargar_ExportaElMesCargado()
    {
        var (vm, svc, guardado, _, pdf, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).ReturnsAsync(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await vm.CargarAsync();
        vm.VerAnioCompleto = true;   // sin recargar: en pantalla sigue octubre
        pdf.Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>())).Returns(new byte[] { 1 });
        guardado.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        guardado.Verify(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "libro-caja-2026-10.pdf", It.IsAny<CancellationToken>(),
            "pdf", "application/pdf"), Times.Once);
        pdf.Verify(e => e.Exportar(It.IsAny<IEnumerable<TotalMensualDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
    }
```

Adaptar el heredado `ExportarPdfCommand_ConAnioCompleto_NoExporta` (L149-162; convención 13): con E6 el año completo SÍ exporta, así que pasa a llamarse `ExportarPdfCommand_ConAnioCompletoSinMovimientos_NoExporta`, carga un año con `DoceMeses()` (hoy carga una lista vacía, que el servidor nunca devuelve) y además del `Verify(... MovimientoCajaDto ..., Times.Never)` que ya tiene agrega el mismo `Verify` con `IEnumerable<TotalMensualDto>`. Custodia lo mismo que antes para el caso que sigue sin exportar; el caso que ahora exporta lo custodian `ExportarPdf_Anual_…`. Anotar el cambio de nombre en el ledger.

En `ColumnasPdfContraDtosRealesTests.cs`, después de `LibroCaja_ColumnasPdf_…` (L139-145):

```csharp
    [Fact]
    public void LibroCajaAnual_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            LibroCajaViewModel.ColumnasPdfAnual,
            new TotalMensualDto(3, 1250000m, 1322750m, -72750m));

    /// <summary>El PDF anual con su resumen REAL (no un mock): vertical (4 columnas), con los saldos, los meses en
    /// déficit y los totales por rubro, y sin totales por fuente (spec, sección 2).</summary>
    [Fact]
    public void LibroCajaAnual_ConSuResumen_SaleVerticalConSaldosDeficitYRubros_SinFuentes()
    {
        var anual = new LibroCajaAnualDto(2026,
            [new TotalMensualDto(1, 1250000m, 980200m, 269800m), new TotalMensualDto(3, 1250000m, 1322750m, -72750m)],
            [new TotalPorClaveDto("Obras viales", 2302950m)]);
        IPdfExporter exportador = new PdfExporterMigraDoc();

        var pdf = exportador.Exportar(anual.TotalesPorMes, LibroCajaViewModel.ColumnasPdfAnual, Metadatos,
            LibroCajaViewModel.ArmarResumenAnual(anual, 812430.55m, 1009480.55m));

        using var documento = PdfDocument.Open(pdf);
        var primera = documento.GetPage(1);
        Assert.True(primera.Height > primera.Width, "el PDF anual tiene que salir vertical");
        var palabras = string.Join(" ", documento.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
        Assert.Contains("Saldo inicial", palabras);
        Assert.Contains("Meses con déficit", palabras);
        Assert.Contains("Marzo", palabras);
        Assert.Contains("Totales por rubro", palabras);
        Assert.DoesNotContain("fuente", palabras);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~NombreMes|FullyQualifiedName~ColumnasPdfContraDtosReales"` → FAIL de compilación (`NombreMesConverter`, `ColumnasPdfAnual`, `ArmarResumenAnual`, `PuedeExportar`). Pegar. Con esas referencias comentadas, pegar los rojos de comportamiento (el anual no exporta; `CanExecute` true sin datos; el mensual rotula "Mes: 10/2026.").

- [ ] **Step 2: Implementación**

`src/StockApp.Presentation/Converters/NombreMesConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Número de mes (1 a 12) → nombre con mayúscula inicial ("Marzo"; "Setiembre", como en Uruguay). Lo usan la columna
/// MES del libro caja anual (y su ColumnaAuto) y la sección "Meses con déficit" del PDF anual. Fuera de rango devuelve
/// el número tal cual. Solo de lectura.
/// </summary>
public sealed class NombreMesConverter : IValueConverter
{
    public static readonly NombreMesConverter Instance = new();

    private static readonly string[] Nombres =
    [
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Setiembre", "Octubre", "Noviembre", "Diciembre",
    ];

    public static string Nombre(int mes) =>
        mes is >= 1 and <= 12 ? Nombres[mes - 1] : mes.ToString(CultureInfo.InvariantCulture);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int mes ? Nombre(mes) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

En `LibroCajaViewModel` (agregar `using System.ComponentModel;` y `using StockApp.Presentation.Converters;`), después de `ColumnasPdf` (L118):

```csharp
    /// <summary>Columnas del PDF anual: las de la grilla de totales por mes, sobre el DTO real (4 columnas: sale vertical).
    /// El mes va como número; el déficit se marca con el signo del neto y con la sección "Meses con déficit".</summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdfAnual = new[]
    {
        new ColumnaPdf(nameof(TotalMensualDto.Mes), "Mes"),
        new ColumnaPdf(nameof(TotalMensualDto.Ingresos), "Ingresos"),
        new ColumnaPdf(nameof(TotalMensualDto.Egresos), "Egresos"),
        new ColumnaPdf(nameof(TotalMensualDto.Neto), "Neto"),
    };

    /// <summary>
    /// El exporte serializa en el cliente el período CARGADO: sin permiso, mientras carga o sin movimientos no hay nada
    /// confiable que exportar.
    /// </summary>
    public bool PuedeExportar =>
        !SinPermiso && !Cargando && (LibroMes is not null || LibroAnual is not null) && !SinMovimientosEnElPeriodo;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(SinPermiso) or nameof(Cargando) or nameof(LibroMes) or nameof(LibroAnual)
                or nameof(SinMovimientosEnElPeriodo))
        {
            OnPropertyChanged(nameof(PuedeExportar));
            ExportarPdfCommand.NotifyCanExecuteChanged();
        }
    }

    private string Emisor() =>
        _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema";
```

Reemplazar `ExportarPdfAsync` (L120-168, con su `<summary>`) por:

```csharp
    /// <summary>
    /// Exporta el período CARGADO (no el de los filtros, que pueden ir por delante mientras espera la recarga): con el
    /// año completo, el PDF anual; con un mes, el PDF mensual de siempre (A4 apaisado, 10 columnas, aviso de volumen
    /// por encima de 500 filas). Ofrece abrir el PDF con el visor del sistema tras guardarlo.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeExportar))]
    private async Task ExportarPdfAsync()
    {
        if (SinMovimientosEnElPeriodo)
            return;
        if (LibroAnual is { } anual)
        {
            await ExportarAnioAsync(anual);
            return;
        }
        if (LibroMes is { } mes)
            await ExportarMesAsync(mes);
    }

    private async Task ExportarMesAsync(LibroCajaMesDto mes)
    {
        if (!await AvisoVolumenExportacion.ConfirmarAsync(Movimientos.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Libro caja",
                DescripcionFiltros: $"Mes: {mes.Mes:00}/{mes.Anio:0000}.",
                UsuarioEmisor: Emisor());

            var resumen = new ResumenPdf(
                Totales: new[]
                {
                    new TotalPdf("Saldo inicial", SaldoInicial),
                    new TotalPdf("Saldo final", SaldoFinal),
                },
                Secciones: new[]
                {
                    new SeccionResumenPdf(
                        "Totales por rubro",
                        [.. TotalesPorRubro.Select(t => new TotalPdf(t.Clave, t.Total))]),
                    new SeccionResumenPdf(
                        "Totales por fuente",
                        [.. TotalesPorFuente.Select(t => new TotalPdf(t.Clave, t.Total))]),
                });

            var pdf = _pdfExporter.Exportar(Movimientos, ColumnasPdf, metadatos, resumen);
            using var stream = new MemoryStream(pdf);
            var nombreArchivo = $"libro-caja-{mes.Anio:0000}-{mes.Mes:00}.pdf";
            var guardado = await _guardado.GuardarBytesAsync(
                stream, nombreArchivo, extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, nombreArchivo, _confirmacion, _apertura);
        }, _confirmacion);
    }

    /// <summary>PDF anual: la tabla de totales por mes y, después (la plantilla imprime el resumen después de la tabla),
    /// los saldos del año, los meses en déficit y los totales por rubro. Sin totales por fuente.</summary>
    private async Task ExportarAnioAsync(LibroCajaAnualDto anual)
    {
        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Libro caja",
                DescripcionFiltros: $"Año completo: {anual.Anio:0000}.",
                UsuarioEmisor: Emisor());

            var pdf = _pdfExporter.Exportar(anual.TotalesPorMes, ColumnasPdfAnual, metadatos,
                ArmarResumenAnual(anual, SaldoInicial, SaldoFinal));
            using var stream = new MemoryStream(pdf);
            var nombreArchivo = $"libro-caja-{anual.Anio:0000}.pdf";
            var guardado = await _guardado.GuardarBytesAsync(
                stream, nombreArchivo, extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, nombreArchivo, _confirmacion, _apertura);
        }, _confirmacion);
    }

    /// <summary>Resumen del PDF anual. "Meses con déficit" solo si hay alguno (la plantilla no marca filas de la tabla).</summary>
    internal static ResumenPdf ArmarResumenAnual(LibroCajaAnualDto anual, decimal saldoInicial, decimal saldoFinal)
    {
        var secciones = new List<SeccionResumenPdf>();
        var deficit = anual.TotalesPorMes
            .Where(t => t.Neto < 0m)
            .Select(t => new TotalPdf(NombreMesConverter.Nombre(t.Mes), t.Neto))
            .ToList();
        if (deficit.Count > 0)
            secciones.Add(new SeccionResumenPdf("Meses con déficit", deficit));
        secciones.Add(new SeccionResumenPdf(
            "Totales por rubro", [.. anual.TotalesPorRubro.Select(t => new TotalPdf(t.Clave, t.Total))]));

        return new ResumenPdf(
            Totales: [new TotalPdf("Saldo inicial", saldoInicial), new TotalPdf("Saldo final", saldoFinal)],
            Secciones: secciones);
    }
```

(Con P1-B, `Totales:` pasa a `[]` y el test `ExportarPdf_Anual_ResumenConSaldos…` afirma `r.Totales.Count == 0`; con P3-B, ver la tabla de decisiones previas.)

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~NombreMes|FullyQualifiedName~ColumnasPdfContraDtosReales|FullyQualifiedName~ControlPoa"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~SignoNegativoBadge"
```

Expected: verde. Los heredados de PDF mensual (`ExportarPdfCommand_ConMes_…`, `…_PasaMetadatosConTituloFiltrosYUsuarioEmisor`, `…_PasaElResumenConSaldos…`, `…_GuardadoExitoso_…`, `…_GuardadoCancelado_…`, `…_SiFallaGuardarBytesAsync_…`, `…_ConMuchasFilas_…`) siguen: el DTO que cargan es el mes que rotulan. `ExportarPdfBotonesGuardianTests.Montar_LibroCajaView_…` sigue verde (afirma `IsVisible` y `Command`; el botón v1 queda deshabilitado con el mes vacío).

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (k) En `PuedeExportar`, quitar ` && !SinMovimientosEnElPeriodo` → rojo `Exportar_MesSinMovimientos_NoSePuede` y `Exportar_AnioSinMovimientos_NoSePuede`. Restaurar.
- (l) En `PuedeExportar`, quitar ` || LibroAnual is not null` → rojo `Exportar_AnioConMovimientos_SePuede`. Pegar. Restaurar. (Spec: "la habilitación del PDF anual".)
- (m) En `ExportarPdfAsync`, quitar el bloque `if (LibroAnual is { } anual) { await ExportarAnioAsync(anual); return; }` (sus cinco líneas) → rojo `ExportarPdf_Anual_CuatroColumnas…`. Restaurar.
- (n) En `ArmarResumenAnual`, quitar `.Where(t => t.Neto < 0m)` → rojo `ExportarPdf_Anual_ResumenConSaldos…` (doce meses en la sección). Restaurar.
- (o) En `OnPropertyChanged`, quitar `ExportarPdfCommand.NotifyCanExecuteChanged();` → rojo `Exportar_MientrasCarga_NoSePuede_YAlTerminarNotificaYVuelve`. Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Converters/NombreMesConverter.cs src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs tests/StockApp.Presentation.Tests/Converters/NombreMesConverterTests.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs tests/StockApp.Presentation.Tests/Exportacion/ColumnasPdfContraDtosRealesTests.cs
git commit -m "feat(finanzas): el libro caja exporta el PDF del año completo y solo con datos cargados"
```

---

## Bloque B — Datos fijos, medición y mockups (GATE 1 antes de todo XAML de la vista)

### Task 5: Datos fijos de Libro caja

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosLibroCaja.cs`, `DatosLibroCajaTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: el VM de las Tasks 1 a 4; `DatosShell.Crear(UsuarioSesion, string[], string, ViewModelBase?)`; `DatosProductos.UsuarioAdmin`.
- Produces: `DatosLibroCaja.Reloj`, `Octubre`, `OctubreSaldoNegativo`, `OctubreVacio`, `Enero2026`, `Anio2026` (DTOs); `Vm(Func<Task<LibroCajaMesDto>> octubre)`; pantallas (→ `ShellMainViewModel`): `ConMovimientos()`, `SaldoNegativo()`, `AnioCompleto()`, `SinMovimientos()`, `Cargando()`, `SinConexion()`, `SinPermiso()`.

- [ ] **Step 1: Test (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosLibroCajaTests.cs`:

```csharp
using StockApp.Application.Finanzas;
using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Los mockups del libro caja escriben estos números a mano: si un dato no cuadra (un saldo corrido mal
/// sumado, un total que no es la suma), el mockup copia el error y la comparación no lo ve.</summary>
public class DatosLibroCajaTests
{
    private static void CuadraElMes(LibroCajaMesDto mes)
    {
        var corrido = mes.SaldoInicial;
        foreach (var m in mes.Movimientos)
        {
            corrido += m.Ingreso - m.Egreso;
            Assert.Equal(corrido, m.SaldoCorrido);
        }
        Assert.Equal(corrido, mes.SaldoFinal);
        Assert.Equal(mes.Movimientos.Sum(m => m.Egreso), mes.TotalesPorRubro.Sum(t => t.Total));
        Assert.Equal(mes.Movimientos.Sum(m => m.Ingreso - m.Egreso), mes.TotalesPorFuente.Sum(t => t.Total));
    }

    [Fact]
    public void LosMeses_CuadranSaldosYTotales()
    {
        CuadraElMes(DatosLibroCaja.Octubre);
        CuadraElMes(DatosLibroCaja.OctubreSaldoNegativo);
        Assert.Empty(DatosLibroCaja.OctubreVacio.Movimientos);
    }

    [Fact]
    public void Octubre_TextosDelMockup()
    {
        var o = DatosLibroCaja.Octubre;
        Assert.Equal(6, o.Movimientos.Count);
        Assert.Equal("02/10/2026", FechaCortaConverter.Formatear(o.Movimientos[0].Fecha));
        Assert.Equal("$ 2.350.000,00", FormatoEsUy.Moneda(o.SaldoInicial));
        Assert.Equal("$ 2.639.460,00", FormatoEsUy.Moneda(o.SaldoFinal));
        Assert.Equal("-$ 44.000,00", FormatoEsUy.Moneda(o.TotalesPorFuente[1].Total));
        Assert.Equal("—", MonedaOGuionConverter.Instance.Convert(o.Movimientos[0].Egreso, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SaldoNegativo_TextosDelMockup()
    {
        Assert.Equal("-$ 51.540,00", FormatoEsUy.Moneda(DatosLibroCaja.OctubreSaldoNegativo.SaldoFinal));
        Assert.Equal("-$ 101.540,00", FormatoEsUy.Moneda(DatosLibroCaja.OctubreSaldoNegativo.Movimientos[0].SaldoCorrido));
    }

    [Fact]
    public void Anio_NetosRubrosYDeficit()
    {
        var a = DatosLibroCaja.Anio2026;
        Assert.Equal(12, a.TotalesPorMes.Count);
        Assert.All(a.TotalesPorMes, t => Assert.Equal(t.Ingresos - t.Egresos, t.Neto));
        Assert.Equal(a.TotalesPorMes.Sum(t => t.Egresos), a.TotalesPorRubro.Sum(t => t.Total));
        Assert.Equal(new[] { "Marzo", "Junio" },
            a.TotalesPorMes.Where(t => t.Neto < 0m).Select(t => NombreMesConverter.Nombre(t.Mes)));
    }

    [Fact]
    public async Task Vm_ConMovimientos_CargaOctubreConSusSaldos()
    {
        var vm = DatosLibroCaja.Vm(() => Task.FromResult(DatosLibroCaja.Octubre));

        await vm.CargarAsync();

        Assert.Equal(2026, vm.Anio);
        Assert.Equal(10, vm.Mes);
        Assert.Equal(6, vm.Movimientos.Count);
        Assert.Equal(2_639_460m, vm.SaldoFinal);
    }

    [Fact]
    public async Task Vm_AnioCompleto_SaldosDelAnioYMesesConMovimientos()
    {
        var vm = DatosLibroCaja.Vm(() => Task.FromResult(DatosLibroCaja.Octubre));
        vm.ModoSeleccionado = LibroCajaViewModel.ModoAnioCompleto;

        await vm.CargarAsync();

        Assert.Equal("$ 812.430,55", FormatoEsUy.Moneda(vm.SaldoInicial));
        Assert.Equal("$ 2.502.540,55", FormatoEsUy.Moneda(vm.SaldoFinal));
        Assert.Equal(10, vm.MesesConMovimientos);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosLibroCajaTests"` → FAIL de compilación. Pegar.

- [ ] **Step 2: Implementación**

`DatosLibroCaja.cs`:

```csharp
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/libro-caja*.html (cuadres y textos fijados en DatosLibroCajaTests).
/// Octubre de 2026 con el reloj en el 06/10/2026: el saldo corrido es el del servidor (inicial + ingresos − egresos en
/// orden de fecha) y los totales por rubro y por fuente vienen ordenados de mayor a menor, como los arma el servidor.</summary>
public static class DatosLibroCaja
{
    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public static readonly TimeProvider Reloj = new RelojFijo();

    private static MovimientoCajaDto Ingreso(int dia, string concepto, string fuente, decimal monto, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), "Ingreso", concepto, null, null, fuente, null, monto, 0m, corrido);

    private static MovimientoCajaDto Egreso(int dia, string concepto, string proveedor, string factura, string fuente,
        string rubro, decimal monto, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), "Egreso", concepto, proveedor, factura, fuente, rubro, 0m, monto, corrido);

    public static readonly LibroCajaMesDto Octubre = new(2026, 10, 2_350_000m, 2_639_460m,
    [
        Ingreso(2, "Multas de tránsito setiembre", "Rentas generales", 48_300m, 2_398_300m),
        Egreso(3, "Materiales para bacheo de calle Zorrilla", "Barraca Carmelo S.A.", "A-0004521", "FIGM Literal B",
            "Materiales de construcción", 186_540m, 2_211_760m),
        Ingreso(6, "Partida mensual FIGM octubre", "FIGM Literal B", 1_250_000m, 3_461_760m),
        Egreso(8, "Combustible de la flota municipal", "Ancap Distribuidora", "B-0012877", "Rentas generales",
            "Combustibles", 92_300m, 3_369_460m),
        Egreso(12, "Luminarias LED plaza Artigas", "Electro Sur S.R.L.", "A-0001190", "FIGM Literal B",
            "Alumbrado público", 310_000m, 3_059_460m),
        Egreso(15, "Reparación de cordón cuneta", "Constructora Oriental S.A.", "A-0002210", "Préstamo BROU",
            "Obras viales", 420_000m, 2_639_460m),
    ],
    [
        new("Obras viales", 420_000m), new("Alumbrado público", 310_000m),
        new("Materiales de construcción", 186_540m), new("Combustibles", 92_300m),
    ],
    [new("FIGM Literal B", 753_460m), new("Rentas generales", -44_000m), new("Préstamo BROU", -420_000m)]);

    public static readonly LibroCajaMesDto OctubreSaldoNegativo = new(2026, 10, 85_000m, -51_540m,
    [
        Egreso(3, "Materiales para bacheo de calle Zorrilla", "Barraca Carmelo S.A.", "A-0004521", "FIGM Literal B",
            "Materiales de construcción", 186_540m, -101_540m),
        Ingreso(6, "Adelanto de la partida FIGM octubre", "FIGM Literal B", 50_000m, -51_540m),
    ],
    [new("Materiales de construcción", 186_540m)],
    [new("FIGM Literal B", -136_540m)]);

    public static readonly LibroCajaMesDto OctubreVacio = new(2026, 10, 2_350_000m, 2_350_000m, [], [], []);

    /// <summary>Enero solo aporta el saldo inicial del año (el anual no trae saldos).</summary>
    public static readonly LibroCajaMesDto Enero2026 = new(2026, 1, 812_430.55m, 812_430.55m, [], [], []);

    public static readonly LibroCajaAnualDto Anio2026 = new(2026,
    [
        new(1, 1_250_000m, 980_200m, 269_800m),
        new(2, 1_250_000m, 1_104_500m, 145_500m),
        new(3, 1_250_000m, 1_322_750m, -72_750m),
        new(4, 1_298_300m, 1_010_000m, 288_300m),
        new(5, 1_250_000m, 890_400m, 359_600m),
        new(6, 1_250_000m, 1_415_900m, -165_900m),
        new(7, 1_265_000m, 1_120_000m, 145_000m),
        new(8, 1_250_000m, 1_198_600m, 51_400m),
        new(9, 3_250_000m, 2_870_300m, 379_700m),
        new(10, 1_298_300m, 1_008_840m, 289_460m),
        new(11, 0m, 0m, 0m),
        new(12, 0m, 0m, 0m),
    ],
    [
        new("Obras viales", 4_210_000m), new("Materiales de construcción", 2_986_540m),
        new("Servicios contratados", 2_772_650m), new("Alumbrado público", 1_840_000m), new("Combustibles", 1_112_300m),
    ]);

    public static LibroCajaViewModel Vm(Func<Task<LibroCajaMesDto>> octubre)
    {
        var servicio = new Mock<IFinanzasVistasService>();
        servicio.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).Returns(octubre);
        servicio.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 1)).ReturnsAsync(Enero2026);
        servicio.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(Anio2026);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(DatosProductos.UsuarioAdmin.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new LibroCajaViewModel(servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(),
            Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>(), sesion.Object, Reloj);
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "LibroCaja", vm);

    public static ShellMainViewModel ConMovimientos() => EnShell(Vm(() => Task.FromResult(Octubre)));
    public static ShellMainViewModel SaldoNegativo() => EnShell(Vm(() => Task.FromResult(OctubreSaldoNegativo)));
    public static ShellMainViewModel SinMovimientos() => EnShell(Vm(() => Task.FromResult(OctubreVacio)));
    public static ShellMainViewModel Cargando() => EnShell(Vm(() => new TaskCompletionSource<LibroCajaMesDto>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Vm(() => Task.FromException<LibroCajaMesDto>(new ServidorNoDisponibleException())));
    public static ShellMainViewModel SinPermiso() =>
        EnShell(Vm(() => Task.FromException<LibroCajaMesDto>(new UnauthorizedAccessException("403"))));

    /// <summary>El selector cambia antes de que la vista pida la primera carga; el debounce que eso dispara recarga lo
    /// mismo (el Moq responde igual), así que no cambia el render.</summary>
    public static ShellMainViewModel AnioCompleto()
    {
        var vm = Vm(() => Task.FromResult(Octubre));
        vm.ModoSeleccionado = LibroCajaViewModel.ModoAnioCompleto;
        return EnShell(vm);
    }
}
```

Confirmar con la CLI `codegraph node FechaCortaConverter` que `Formatear` es `public static` y recibe `DateOnly?` (lo usa `DatosIngresosTests`), y con Read de `DatosShell.cs` L30 que la sección activa del Libro caja es `"LibroCaja"` (`ShellMainViewModel.cs` L266 y L363).

- [ ] **Step 3: Verde**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosLibroCajaTests"` → PASS.

Mutación (j) de la Task 3 repetida sobre los datos: `cp` de `LibroCajaViewModel.cs` al `$SCRATCH`, quitar ` + libro.TotalesPorMes.Sum(t => t.Neto)`, correr el mismo filtro → rojo en `Vm_AnioCompleto_SaldosDelAnioYMesesConMovimientos` ("$ 812.430,55" en vez de "$ 2.502.540,55"), pegar, restaurar con `cp`, `git diff --no-index` vacío, verde.

- [ ] **Step 4: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosLibroCaja.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosLibroCajaTests.cs
git commit -m "test(ui-v2): datos fijos del libro caja"
```

### Task 6: Medición y mockups del Libro caja — GATE 1 con el usuario

**Decisiones aplicadas:** D1 a D17 (y P1 a P3 como los eligió el usuario).

**Files:**
- Create: `docs/ui-v2/mockups/finanzas/libro-caja.html`, `libro-caja-saldo-negativo.html`, `libro-caja-anio.html`, `libro-caja-vacio.html`, `libro-caja-cargando.html`, `libro-caja-sin-conexion.html`, `libro-caja-sin-permiso.html`
- Modify: `docs/ui-v2/mockups/_base/base.css` (la regla general `.v2-texto-peligro` de la Task 7 se adelanta acá si el mockup la necesita para los saldos; se declara en el GATE)
- Modify (local): el ledger (anchos y altos medidos)

**Interfaces:**
- Consumes: `DatosLibroCaja` (Task 5); `ColumnaAuto` y `DataGridColumnasAutoBehavior.AnchoMedido` (3c-1).
- Produces: las pantallas `finanzas-libro-caja[-saldo-negativo|-anio|-vacio|-cargando|-sin-conexion|-sin-permiso]`; en el ledger los anchos medidos que usan las Tasks 8 y 9.

- [ ] **Step 1: Medir en headless (archivo temporal, no se commitea)**

Crear `tests/StockApp.Presentation.VisualTests/MedicionTemporal4c.cs` siguiendo `MedicionTemporal4a` (plan de la 4a, Task 5 Step 1). Con el tema v2 (`Classes="ui2"`), a 1280×760 y 1366×768 (vista de 1040 y 1126), con `--logger "console;verbosity=detailed"`, escribe:
1. **Grilla del mes:** un `DataGrid` de prueba con las columnas de D7 sobre `DatosLibroCaja.Octubre.Movimientos`: `AnchoMedido` de cada columna `ColumnaAuto` (FECHA con `FechaCortaConverter` y `NoCede`; INGRESO y EGRESO con `MonedaOGuionConverter`; SALDO CORRIDO con `MonedaConverter`), el ancho de CONCEPTO, y si con CONCEPTO en su piso de 200 la grilla entra en el ancho de la card o hace scroll horizontal (y desde qué columna se corta la vista). El alto con header + 12 filas (esperado 516).
2. **Grilla del año:** MES con `NombreMesConverter` ("Setiembre" es el más ancho), INGRESOS, EGRESOS y el ancho fijo de NETO con el peor caso (`Insignia` "Déficit" + "-$ 165.900,00" con 8 de separación), como SALDO de Control POA.
3. **Totales:** RUBRO / FUENTE `*` y TOTAL medida en cada card de media pantalla (`GrillaUniformeV2` de 2 con 16 de espaciado).
4. **Filtros y saldos:** el ancho de la fila de filtros (Año 120, Período 160 con "Año completo" sin cortar, Mes 100) y del panel de saldos con la insignia "Saldo negativo" + "-$ 51.540,00"; que entren juntos en la card a 1040 sin superponerse.
5. **Encabezado:** que el subtítulo de D1 entra en una línea a 1040.

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~MedicionTemporal4c" --logger "console;verbosity=detailed"`. Anotar las líneas en el ledger. Si los filtros y los saldos no entran juntos a 1040, proponer en el GATE los saldos en una fila propia debajo de los filtros (no resolverlo en silencio).

- [ ] **Step 2: Mockups**

Partir de `finanzas/control-poa.html` y `control-poa-sobreejecutado.html` (barra de filtros con `CampoFormulario`, insignia peligro, `v2-texto-peligro` en celda), `finanzas/calendario-pagos.html` (página con scroll y varias cards con `v2-titulo-seccion`), `inventario/historial.html` (grilla de 10 columnas con scroll horizontal y combo con `.v2-glifo-combo`) y `inventario/historial-sin-permiso.html`, `control-poa-cargando.html`, `control-poa-sin-conexion.html`, `control-poa-vacio.html` para los estados. Cada mockup:
- `<title>Finanzas: Libro caja[ estado] (UI v2)</title>` y `<!-- Datos: DatosLibroCaja.<Pantalla>: … -->`.
- Sidebar con "Libro caja" activo (se oculta en la comparación, pero el usuario aprueba la pantalla entera).
- `libro-caja.html`: encabezado de D1; card de filtros de D2 con Año "2026", Período "Mes", Mes "10" y los saldos de D5 ("$ 2.350.000,00" / "$ 2.639.460,00", sin insignias); card "Movimientos" con las 6 filas de `Octubre` (anchos MEDIDOS como `flex: 0 0 Npx`; INGRESO/EGRESO "—" donde es 0; scroll horizontal si la medición lo dio) y el pie "6 movimientos"; debajo, "Totales por rubro" (4 filas) y "Totales por fuente" (3 filas, "-$ 44.000,00" y "-$ 420.000,00" con `v2-texto-peligro`).
- `-saldo-negativo`: `OctubreSaldoNegativo`; saldo final "-$ 51.540,00" en peligro con la insignia "Saldo negativo"; SALDO CORRIDO de las dos filas en peligro.
- `-anio`: Período "Año completo", sin el campo Mes; saldos "$ 812.430,55" / "$ 2.502.540,55"; card "Totales por mes" con los 12 meses por nombre, NETO de Marzo y Junio con la insignia "Déficit" y el número en peligro, pie "10 meses con movimientos"; card "Totales por rubro" con 5 filas.
- `-vacio`: `OctubreVacio`: filtros y saldos ("$ 2.350.000,00" los dos) y una card con `.v2-vacio` "No hay movimientos en el período" / "Probá con otro mes o con el año completo.".
- `-cargando`: filtros, sin saldos, card con `.v2-cargando` "Cargando el libro caja…".
- `-sin-conexion`: filtros, sin saldos, card con `.v2-vacio` de error ("No se pudo conectar con el servidor", mensaje de la convención 18) y "Reintentar" `secundario`.
- `-sin-permiso`: encabezado (Actualizar y Exportar PDF deshabilitado) y el `.v2-vacio` de error "Sin permiso" / "No tenés permiso para ver el libro caja." en lugar de todo lo demás.

- [ ] **Step 3: Capturas y verificación de la herramienta**

```bash
for p in libro-caja libro-caja-saldo-negativo libro-caja-anio libro-caja-vacio libro-caja-cargando libro-caja-sin-conexion libro-caja-sin-permiso; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p@1366x768.png --tamano 1366x768
done
ls -l --time-style=full-iso artifacts/ui-v2/mockups/finanzas-libro-caja*.png
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 14 PNG con fecha de ahora dentro del worktree; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0. MIRAR cada PNG.

- [ ] **Step 4: Borrar la medición temporal**

`rm tests/StockApp.Presentation.VisualTests/MedicionTemporal4c.cs` y `git status --short tests/` (no debe listarlo).

- [ ] **Step 5: GATE 1 — aprobación del usuario (PARADA)**

PARAR. Presentar los 14 PNG (LOS DOS TAMAÑOS), la lista D1 a D17, la elección de P1 a P3 y los anchos medidos del ledger. Señalar explícitamente:
- D3 y D4: selector de modo como `ComboBox` y Mes como número (y sus alternativas).
- D5: dónde quedaron los saldos y que siguen visibles en el vacío.
- D6: grilla del mes con alto máximo y scroll propio dentro de una página que también scrollea.
- D7: "—" en INGRESO/EGRESO cuando es 0 (la v1 mostraba "$ 0,00") y el scroll horizontal de la grilla.
- D9: los 12 meses siempre, mes por nombre en la grilla y por número en el PDF (P3).
- D10: Exportar PDF visible y deshabilitado sin permiso (Control POA lo oculta).
- D11: durante el debounce se ve el período anterior.
- D13 y D14: lo que cambia respecto de la v1 sin que la spec lo diga.
- D17: lo que NO se hace con una revocación en caliente.

No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en los mockups, en el VM y sus tests de las Tasks 1 a 4 que la fijan (con su rojo y su verde) y en los tests de las Tasks 8 y 9, y volver a presentar. Registrar en el ledger: `Mockups 4c: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 6: Commit**

```bash
git add docs/ui-v2/mockups/finanzas/libro-caja*.html
git commit -m "docs(ui-v2): mockups aprobados del libro caja"
```

(Sumar `docs/ui-v2/mockups/_base/base.css` si se tocó.)

---

## Bloque C — Vista v2 (arranca solo con los mockups aprobados)

### Task 7: Texto negativo por token fuera de la grilla

**Files:**
- Modify: `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml` (después de `.ui2 TextBlock.pie-conteo`, L65-69)
- Modify: `docs/ui-v2/mockups/_base/base.css` (después de L314; si la Task 6 ya la agregó, solo se verifica)
- Create: `tests/StockApp.Presentation.UiTests/V2/TextoNegativoV2Tests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: `MontajeV2.Montar(string, bool, double, double)`, `MontajeV2.Buscar<T>`, `MontajeV2.ColorDe`, `MontajeV2.Hex`.
- Produces: estilo `.ui2 TextBlock.negativo` (`V2PeligroTextoBrush`); regla `.v2-texto-peligro` general en `base.css`.

- [ ] **Step 1: Test (rojo)**

`tests/StockApp.Presentation.UiTests/V2/TextoNegativoV2Tests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Un número negativo FUERA de una grilla (los saldos del libro caja) se pinta con el peligro texto por token,
/// también sobre un título de sección. Dentro de una celda ya lo pinta DataGridV2.axaml.</summary>
public class TextoNegativoV2Tests
{
    private const string Contenido = """
        <StackPanel>
            <TextBlock x:Name="Suelto" Classes="negativo" Text="-$ 51.540,00" />
            <TextBlock x:Name="ConTitulo" Classes="titulo-seccion negativo" Text="-$ 51.540,00" />
            <TextBlock x:Name="Normal" Classes="titulo-seccion" Text="$ 2.639.460,00" />
        </StackPanel>
        """;

    [AvaloniaFact]
    public void FueraDeLaGrilla_ElNegativoEsPeligro_AunConTituloDeSeccion()
    {
        var ventana = MontajeV2.Montar(Contenido);

        Assert.Equal(MontajeV2.Hex("#B91C1C"), MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "Suelto").Foreground));
        Assert.Equal(MontajeV2.Hex("#B91C1C"), MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "ConTitulo").Foreground));
        Assert.Equal(MontajeV2.Hex("#1C1917"), MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "Normal").Foreground));
    }

    [AvaloniaFact]
    public void SinScopeUi2_NoSePinta()
    {
        var ventana = MontajeV2.Montar(Contenido, conScopeUi2: false);

        Assert.NotEqual(MontajeV2.Hex("#B91C1C"),
            MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "Suelto").Foreground ?? Avalonia.Media.Brushes.Black));
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TextoNegativoV2Tests"` → FAIL (`Suelto` sale con el primario heredado y `ConTitulo` con `#1C1917`). Pegar. (Este test no alcanza: la memoria "estilo con scope no alcanza vistas reales" pide además la View real; la custodia `SaldoFinalNegativo_InsigniaPeligroYTextoPorToken` en la Task 8.)

- [ ] **Step 2: Implementación**

En `ControlesV2.axaml`, después del estilo `.ui2 TextBlock.pie-conteo` (L65-69):

```xml
    <!-- Número negativo fuera de una grilla (saldos del libro caja): peligro texto por token, nunca opacidad. Va después
         de los estilos de texto (titulo-seccion, cuerpo, texto-secundario, pie-conteo): a igual especificidad gana el
         último. Dentro de una celda lo pinta DataGridV2.axaml con el mismo color. Espejo: .v2-texto-peligro en base.css. -->
    <Style Selector=".ui2 TextBlock.negativo">
        <Setter Property="Foreground" Value="{DynamicResource V2PeligroTextoBrush}" />
    </Style>
```

En `base.css`, después de L314:

```css
/* Número negativo fuera de una celda (ControlesV2.axaml, .ui2 TextBlock.negativo): saldos del libro caja. Va después de
   .v2-titulo-seccion para ganarle a igual especificidad. */
.v2-texto-peligro { color: var(--v2-color-peligro-texto); }
```

- [ ] **Step 3: Verde, build y herramientas**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TextoNegativo|FullyQualifiedName~ControlesV2|FullyQualifiedName~ControlPoa|FullyQualifiedName~MovimientoHistorialViewV2|FullyQualifiedName~ProductoListViewV2|FullyQualifiedName~SignoNegativoBadge"
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
scripts/ui-v2/comparar.sh ControlPoa
scripts/ui-v2/comparar.sh Historial
```

Expected: verde y las comparaciones de Control POA e Historial sin cambios (ya pintaban `.negativo` en la celda con el mismo color).

- [ ] **Step 4: Mutación (QUITANDO)**

- (p) Quitar la línea `<Setter Property="Foreground" Value="{DynamicResource V2PeligroTextoBrush}" />` del estilo nuevo → rojo `FueraDeLaGrilla_ElNegativoEsPeligro_…`. Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Themes/V2/ControlesV2.axaml docs/ui-v2/mockups/_base/base.css tests/StockApp.Presentation.UiTests/V2/TextoNegativoV2Tests.cs
git commit -m "feat(ui-v2): número negativo por token también fuera de la grilla"
```

### Task 8: Libro caja v2, modo mes y estados

**Depende de:** Tasks 1 a 7.

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml` (completo; el `.axaml.cs` no cambia en esta task)
- Modify: `tests/StockApp.Presentation.UiTests/V2/FinanzasVistasFakes.cs` (L37-38)
- Create: `tests/StockApp.Presentation.UiTests/V2/LibroCajaViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/LibroCajaComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (L66, L114, `VistasMigradasAV2` L197-218), `LibroCajaViewTests.cs` (L89-111), `SignoNegativoBadgeTests.cs` (L205-223), `GrillasSinRecorteBarridoTests.cs` (L118-121, solo si hace falta)

**Interfaces:**
- Consumes: `LibroCajaViewModel` (`Anio`, `Mes`, `ModoSeleccionado`, `OpcionesModo`, `VerAnioCompleto`, `SaldoInicial`, `SaldoFinal`, `LibroMes`, `LibroAnual`, `Movimientos`, `MovimientosView`, `TotalesPorRubro`, `TotalesPorFuente`, `SinMovimientosEnElPeriodo`, `Cargando`, `SinPermiso`, `MensajeSinPermiso`, `SinConexion`, `ReintentarCargaCommand`, `RecargarCommand`, `ExportarPdfCommand`, `EsperaDebounce`, `_tareaDebounce`, `MensajeSinPermisoLibroCaja`); `FechaCortaConverter`, `MonedaConverter`, `MonedaOGuionConverter`, `EsNegativoConverter`, `ConteoConverter`, `ColumnaAuto`, `TooltipSiSeCorta`, `GrillaUniformeV2`; `SesionFake`, `ConfirmacionServiceFake`, `ServicioAperturaArchivoFake`, `GuardadoSinUsoFake`, `PdfExporterSinUsoFake`, `ArbolVisual`, `MontajeV2`.
- Produces: en `FinanzasVistasFake`, `Meses` (`Dictionary<(int Anio, int Mes), LibroCajaMesDto>`), `Anios` (`Dictionary<int, LibroCajaAnualDto>`), `PendienteMes`, `FallaLibroCaja`, `ConsultasLibroCaja` (`List<string>`: "2026-10" para un mes, "2026" para un año); en la vista, `x:Name` `Encabezado`, `BotonActualizar`, `BotonExportarPdf`, `EstadoSinPermiso`, `Contenido`, `TarjetaFiltros`, `Filtros`, `CampoAnio`, `SelectorAnio`, `CampoPeriodo`, `ComboModo`, `CampoMes`, `SelectorMes`, `PanelSaldos`, `ValorSaldoInicial`, `InsigniaSaldoInicialNegativo`, `ValorSaldoFinal`, `InsigniaSaldoFinalNegativo`, `TarjetaCargando`, `IndicadorCarga`, `TarjetaSinConexion`, `EstadoSinConexion`, `BotonReintentar`, `TarjetaVacio`, `EstadoSinMovimientos`, `BloquesMes`, `TarjetaMovimientos`, `GrillaMovimientos`, `PieConteoMovimientos`, `TotalesMes`, `TarjetaRubrosMes`, `GrillaRubrosMes`, `TarjetaFuentesMes`, `GrillaFuentesMes`.

- [ ] **Step 1: Fakes**

En `FinanzasVistasFakes.cs`, agregar `using System.Linq;` y reemplazar las dos líneas de libro caja de `FinanzasVistasFake` (L37-38) por:

```csharp
    public Dictionary<(int Anio, int Mes), LibroCajaMesDto> Meses { get; } = new();
    public Dictionary<int, LibroCajaAnualDto> Anios { get; } = new();
    public TaskCompletionSource<LibroCajaMesDto>? PendienteMes { get; set; }
    public Exception? FallaLibroCaja { get; set; }

    /// <summary>Consultas de libro caja en orden: "2026-10" para un mes, "2026" para un año.</summary>
    public List<string> ConsultasLibroCaja { get; } = new();

    /// <summary>Sin datos cargados para ese mes responde un mes vacío, como el servidor.</summary>
    public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)
    {
        ConsultasLibroCaja.Add($"{anio}-{mes:00}");
        return FallaLibroCaja is not null ? Task.FromException<LibroCajaMesDto>(FallaLibroCaja)
            : PendienteMes is not null ? PendienteMes.Task
            : Task.FromResult(Meses.TryGetValue((anio, mes), out var libro) ? libro : new LibroCajaMesDto(anio, mes, 0m, 0m, [], [], []));
    }

    /// <summary>Sin datos cargados para ese año responde los 12 meses en cero, como el servidor.</summary>
    public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio)
    {
        ConsultasLibroCaja.Add($"{anio}");
        return FallaLibroCaja is not null ? Task.FromException<LibroCajaAnualDto>(FallaLibroCaja)
            : Task.FromResult(Anios.TryGetValue(anio, out var libro) ? libro
                : new LibroCajaAnualDto(anio, Enumerable.Range(1, 12).Select(m => new TotalMensualDto(m, 0m, 0m, 0m)).ToList(), []));
    }
```

- [ ] **Step 2: Tests de la vista (rojo)**

`tests/StockApp.Presentation.UiTests/V2/LibroCajaViewV2Tests.cs`:

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
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Libro caja v2 con la View REAL: estructura, filtros, grilla del mes, saldos y totales por token, estados,
/// exporte y, en la segunda parte, el modo año, el selector y los gates.</summary>
public class LibroCajaViewV2Tests
{
    private static readonly Color Peligro = Color.Parse("#B91C1C");

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static MovimientoCajaDto Ingreso(int dia, string concepto, decimal monto, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), "Ingreso", concepto, null, null, "Rentas generales", null, monto, 0m, corrido);

    private static MovimientoCajaDto Egreso(int dia, string concepto, decimal monto, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), "Egreso", concepto, "Barraca Carmelo S.A.", "A-0004521", "FIGM Literal B",
            "Materiales de construcción", 0m, monto, corrido);

    private static readonly LibroCajaMesDto Octubre = new(2026, 10, 100_000m, 61_460m,
        [Ingreso(2, "Multas de tránsito", 48_300m, 148_300m), Egreso(3, "Materiales para bacheo", 186_840m, -38_540m),
         Ingreso(6, "Partida mensual FIGM", 100_000m, 61_460m)],
        [new TotalPorClaveDto("Materiales de construcción", 186_840m)],
        [new TotalPorClaveDto("Rentas generales", 148_300m), new TotalPorClaveDto("FIGM Literal B", -186_840m)]);

    private static SesionFake Mixto() => new(RolUsuario.Operador, Permisos.VerFinanzas);

    private sealed record Montaje(Window Ventana, LibroCajaView Vista, LibroCajaViewModel Vm, FinanzasVistasFake Servicio);

    private static FinanzasVistasFake ConOctubre(LibroCajaMesDto? mes = null)
    {
        var servicio = new FinanzasVistasFake();
        servicio.Meses[(2026, 10)] = mes ?? Octubre;
        return servicio;
    }

    private static Montaje Montar(FinanzasVistasFake servicio, SesionFake? sesion = null, double ancho = 1040, double alto = 760)
    {
        var vm = new LibroCajaViewModel(servicio, new GuardadoSinUsoFake(), new ConfirmacionServiceFake(),
            new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(), sesion ?? Mixto(), new RelojFijo())
        { EsperaDebounce = TimeSpan.Zero };
        var vista = new LibroCajaView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio);
    }

    private static T Control<T>(Montaje m, string nombre) where T : Control => m.Vista.FindControl<T>(nombre)!;
    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);

    private static TextBlock CeldaConTexto(DataGrid grilla, string texto) =>
        grilla.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == texto && ArbolVisual.EsVisibleEnArbol(t));

    private static Color ColorDe(TextBlock t) => Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color;

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTitulo_ActualizarYExportarSecundarios_SinPrimario()
    {
        var m = Montar(ConOctubre());

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Libro caja", Control<EncabezadoPagina>(m, "Encabezado").Titulo);
        Assert.Contains("secundario", Control<Button>(m, "BotonActualizar").Classes);
        Assert.Same(m.Vm.RecargarCommand, Control<Button>(m, "BotonActualizar").Command);
        Assert.Contains("secundario", Control<Button>(m, "BotonExportarPdf").Classes);
        Assert.Same(m.Vm.ExportarPdfCommand, Control<Button>(m, "BotonExportarPdf").Command);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void Filtros_AnioPeriodoYMes_ConSusEtiquetas()
    {
        var m = Montar(ConOctubre());

        Assert.Equal("Año", Control<CampoFormulario>(m, "CampoAnio").Etiqueta);
        Assert.Equal("Período", Control<CampoFormulario>(m, "CampoPeriodo").Etiqueta);
        Assert.Equal("Mes", Control<CampoFormulario>(m, "CampoMes").Etiqueta);
        Assert.Equal("Mes", Control<ComboBox>(m, "ComboModo").SelectedItem);
        Assert.True(Visible(Control<CampoFormulario>(m, "CampoMes")));
        Assert.Equal(2026m, Control<NumericUpDown>(m, "SelectorAnio").Value);
        Assert.Equal(10m, Control<NumericUpDown>(m, "SelectorMes").Value);
    }

    [AvaloniaFact]
    public void GrillaDelMes_DiezColumnasEnOrdenYEnMayusculas_Ordenable()
    {
        var m = Montar(ConOctubre());
        var grilla = Control<DataGrid>(m, "GrillaMovimientos");

        Assert.Equal(new object?[] { "FECHA", "TIPO", "CONCEPTO", "PROVEEDOR", "FACTURA", "FUENTE", "RUBRO", "INGRESO", "EGRESO", "SALDO CORRIDO" },
            grilla.Columns.Select(c => c.Header).ToArray());
        Assert.True(grilla.CanUserSortColumns);
    }

    [AvaloniaFact]
    public void Movimientos_EsUy_GuionSinMonto_YSaldoCorridoNegativoPorToken()
    {
        var m = Montar(ConOctubre());
        var grilla = Control<DataGrid>(m, "GrillaMovimientos");
        var filaEgreso = grilla.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, Octubre.Movimientos[1]));
        var textos = filaEgreso.GetVisualDescendants().OfType<TextBlock>().Where(Visible).ToList();

        Assert.Contains(textos, t => t.Text == "03/10/2026");
        Assert.Contains(textos, t => t.Text == "—");                  // INGRESO en 0
        Assert.Contains(textos, t => t.Text == "$ 186.840,00");
        Assert.Equal(Peligro, ColorDe(textos.Single(t => t.Text == "-$ 38.540,00")));
        Assert.NotEqual(Peligro, ColorDe(CeldaConTexto(grilla, "$ 148.300,00")));
    }

    [AvaloniaFact]
    public void PieConteo_CuentaLosMovimientos()
    {
        var m = Montar(ConOctubre());

        Assert.Equal("3 movimientos", Control<TextBlock>(m, "PieConteoMovimientos").Text);
    }

    [AvaloniaFact]
    public void Saldos_PositivosEnEsUy_SinInsignia()
    {
        var m = Montar(ConOctubre(Octubre with { SaldoFinal = 61_460m }));

        Assert.Equal("$ 100.000,00", Control<TextBlock>(m, "ValorSaldoInicial").Text);
        Assert.Equal("$ 61.460,00", Control<TextBlock>(m, "ValorSaldoFinal").Text);
        Assert.False(Visible(Control<Insignia>(m, "InsigniaSaldoInicialNegativo")));
        Assert.False(Visible(Control<Insignia>(m, "InsigniaSaldoFinalNegativo")));
    }

    [AvaloniaFact]
    public void SaldoFinalNegativo_InsigniaPeligroYTextoPorToken()
    {
        var m = Montar(ConOctubre(Octubre with { SaldoFinal = -38_540m }));

        var insignia = Control<Insignia>(m, "InsigniaSaldoFinalNegativo");
        Assert.True(Visible(insignia));
        Assert.Equal("Saldo negativo", insignia.Texto);
        Assert.Equal(VarianteInsignia.Peligro, insignia.Variante);
        Assert.Equal(Peligro, ColorDe(Control<TextBlock>(m, "ValorSaldoFinal")));
        Assert.False(Visible(Control<Insignia>(m, "InsigniaSaldoInicialNegativo")));
    }

    [AvaloniaFact]
    public void SaldoInicialNegativo_TambienLlevaInsigniaYToken()
    {
        var m = Montar(ConOctubre(Octubre with { SaldoInicial = -5_000m }));

        Assert.True(Visible(Control<Insignia>(m, "InsigniaSaldoInicialNegativo")));
        Assert.Equal(Peligro, ColorDe(Control<TextBlock>(m, "ValorSaldoInicial")));
    }

    [AvaloniaFact]
    public void TotalesPorRubroYPorFuente_EnSusCards_LaFuenteNegativaPorToken()
    {
        var m = Montar(ConOctubre());

        Assert.True(Visible(Control<TarjetaContenido>(m, "TarjetaRubrosMes")));
        Assert.True(Visible(Control<TarjetaContenido>(m, "TarjetaFuentesMes")));
        Assert.Equal(new object?[] { "RUBRO", "TOTAL" }, Control<DataGrid>(m, "GrillaRubrosMes").Columns.Select(c => c.Header).ToArray());
        Assert.Equal(new object?[] { "FUENTE", "TOTAL" }, Control<DataGrid>(m, "GrillaFuentesMes").Columns.Select(c => c.Header).ToArray());
        Assert.Equal(Peligro, ColorDe(CeldaConTexto(Control<DataGrid>(m, "GrillaFuentesMes"), "-$ 186.840,00")));
    }

    [AvaloniaFact]
    public void Cargando_IndicadorSinElVacioNiLosSaldos()
    {
        var m = Montar(new FinanzasVistasFake { PendienteMes = new TaskCompletionSource<LibroCajaMesDto>() });

        Assert.True(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.True(Visible(Control<TarjetaContenido>(m, "TarjetaFiltros")));
        Assert.False(Visible(Control<TarjetaContenido>(m, "TarjetaVacio")));
        Assert.False(Visible(Control<StackPanel>(m, "PanelSaldos")));
        Assert.False(Visible(Control<StackPanel>(m, "BloquesMes")));
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = ConOctubre();
        servicio.FallaLibroCaja = new ServidorNoDisponibleException();
        var m = Montar(servicio);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.False(Visible(Control<StackPanel>(m, "PanelSaldos")));
        Assert.False(Visible(Control<TarjetaContenido>(m, "TarjetaVacio")));

        servicio.FallaLibroCaja = null;
        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<StackPanel>(m, "BloquesMes")));
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDelContenido_ActualizarVisible_YExportarDeshabilitado()
    {
        var m = Montar(new FinanzasVistasFake { FallaLibroCaja = new UnauthorizedAccessException("403") });

        var sinPermiso = Control<EstadoVacio>(m, "EstadoSinPermiso");
        Assert.True(Visible(sinPermiso));
        Assert.Equal(LibroCajaViewModel.MensajeSinPermisoLibroCaja, sinPermiso.Mensaje);
        Assert.False(Visible(Control<StackPanel>(m, "Contenido")));
        Assert.True(Visible(Control<Button>(m, "BotonActualizar")));
        Assert.True(Visible(Control<Button>(m, "BotonExportarPdf")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void SinMovimientos_VacioConSuTexto_SaldosVisibles_YExportarDeshabilitado()
    {
        var m = Montar(ConOctubre(new LibroCajaMesDto(2026, 10, 2_350_000m, 2_350_000m, [], [], [])));

        var vacio = Control<EstadoVacio>(m, "EstadoSinMovimientos");
        Assert.True(Visible(vacio));
        Assert.Equal("No hay movimientos en el período", vacio.Titulo);
        Assert.False(Visible(Control<StackPanel>(m, "BloquesMes")));
        Assert.True(Visible(Control<StackPanel>(m, "PanelSaldos")));
        Assert.Equal("$ 2.350.000,00", Control<TextBlock>(m, "ValorSaldoFinal").Text);
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void ConMovimientos_ExportarHabilitado_YMientrasRecargaDeshabilitado()
    {
        var servicio = ConOctubre();
        var m = Montar(servicio);
        Assert.True(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);

        servicio.PendienteMes = new TaskCompletionSource<LibroCajaMesDto>();
        _ = m.Vm.CargarAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.True(m.Vm.Cargando);
        Assert.True(Visible(Control<StackPanel>(m, "BloquesMes")));   // la recarga deja los datos a la vista
        Assert.False(Visible(Control<TarjetaContenido>(m, "TarjetaCargando")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task CambiarMesEnElCampo_RecargaConEseMes()
    {
        var m = Montar(ConOctubre());

        Control<NumericUpDown>(m, "SelectorMes").Value = 9;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "2026-10", "2026-09" }, m.Servicio.ConsultasLibroCaja);
    }

    [AvaloniaFact]
    public void Grilla_ConSeiscientosMovimientos_NoRealizaTodasLasFilas()
    {
        var movimientos = Enumerable.Range(1, 600).Select(i => Ingreso(1 + i % 28, $"Ingreso {i}", 10m, 10m * i)).ToList();
        var m = Montar(ConOctubre(new LibroCajaMesDto(2026, 10, 0m, 6_000m, movimientos, [], [])));

        var filas = Control<DataGrid>(m, "GrillaMovimientos").GetVisualDescendants().OfType<DataGridRow>().Count();

        Assert.InRange(filas, 1, 40);
    }
}
```

Antes de escribirlo, confirmar con la CLI `codegraph` y Read: (1) que `FindControl<T>` encuentra partes de una card oculta (NameScope de la vista, como en `ControlPoaViewV2Tests` L55) y que `ArbolVisual.EsVisibleEnArbol` da false para algo dentro de un ancestro oculto; (2) que `MontajeV2.Clic` existe con esa firma (`MontajeV2.cs` L52-58); (3) que `GuardadoSinUsoFake` y `PdfExporterSinUsoFake` lanzan si se los llama (`FinanzasVistasFakes.cs` L55-65): ningún test de esta task ejecuta el exporte.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LibroCajaViewV2Tests"` → FAIL (las partes no existen en la vista v1). Pegar.

- [ ] **Step 3: Vista v2**

`LibroCajaView.axaml` completo (los anchos son los MEDIDOS en la Task 6; donde dice `<medido>`, el valor del ledger):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:dto="using:StockApp.Application.Finanzas"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Finanzas.LibroCajaView"
             x:DataType="vm:LibroCajaViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2. Espejo: docs/ui-v2/mockups/finanzas/libro-caja*.html. Pantalla de consulta, sin primario. Cambiar año, mes
         o período recarga solo; lo que se ve (bloques, saldos y PDF) es el período CARGADO, no los filtros. Toda la
         página scrollea; la grilla de movimientos tiene alto máximo y scroll propio para virtualizar las filas. Saldos y
         netos negativos: número por token más insignia, nunca opacidad. Tras un 403, Sin permiso en lugar de todo el
         contenido. -->
    <ScrollViewer x:Name="Desplazamiento" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="28,24">

            <v2:EncabezadoPagina x:Name="Encabezado" Titulo="Libro caja"
                                 Subtitulo="Ingresos y egresos de la caja municipal, con el saldo día por día.">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button x:Name="BotonActualizar" Classes="secundario" Content="Actualizar" Command="{Binding RecargarCommand}" />
                    <Button x:Name="BotonExportarPdf" Classes="secundario" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" />
                </StackPanel>
            </v2:EncabezadoPagina>

            <v2:EstadoVacio x:Name="EstadoSinPermiso" UseLayoutRounding="False"
                            Titulo="Sin permiso" Mensaje="{Binding MensajeSinPermiso}" EsError="True"
                            IsVisible="{Binding SinPermiso}" />

            <StackPanel x:Name="Contenido" Spacing="16" IsVisible="{Binding !SinPermiso}">

                <v2:TarjetaContenido x:Name="TarjetaFiltros">
                    <DockPanel>
                        <!-- Saldos del período cargado; ocultos mientras no hay ninguno (cargando o sin servidor). -->
                        <StackPanel x:Name="PanelSaldos" DockPanel.Dock="Right" Orientation="Horizontal" Spacing="24"
                                    VerticalAlignment="Bottom">
                            <StackPanel.IsVisible>
                                <MultiBinding Converter="{x:Static BoolConverters.Or}">
                                    <Binding Path="LibroMes" Converter="{x:Static ObjectConverters.IsNotNull}" />
                                    <Binding Path="LibroAnual" Converter="{x:Static ObjectConverters.IsNotNull}" />
                                </MultiBinding>
                            </StackPanel.IsVisible>
                            <StackPanel Spacing="4">
                                <TextBlock Classes="etiqueta-campo" Text="Saldo inicial" />
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <v2:Insignia x:Name="InsigniaSaldoInicialNegativo" Texto="Saldo negativo" Variante="Peligro"
                                                 VerticalAlignment="Center"
                                                 IsVisible="{Binding SaldoInicial, Converter={x:Static conv:EsNegativoConverter.Instance}}" />
                                    <TextBlock x:Name="ValorSaldoInicial" Classes="titulo-seccion"
                                               Classes.negativo="{Binding SaldoInicial, Converter={x:Static conv:EsNegativoConverter.Instance}}"
                                               Text="{Binding SaldoInicial, Converter={x:Static conv:MonedaConverter.Instance}}" />
                                </StackPanel>
                            </StackPanel>
                            <StackPanel Spacing="4">
                                <TextBlock Classes="etiqueta-campo" Text="Saldo final" />
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <v2:Insignia x:Name="InsigniaSaldoFinalNegativo" Texto="Saldo negativo" Variante="Peligro"
                                                 VerticalAlignment="Center"
                                                 IsVisible="{Binding SaldoFinal, Converter={x:Static conv:EsNegativoConverter.Instance}}" />
                                    <TextBlock x:Name="ValorSaldoFinal" Classes="titulo-seccion"
                                               Classes.negativo="{Binding SaldoFinal, Converter={x:Static conv:EsNegativoConverter.Instance}}"
                                               Text="{Binding SaldoFinal, Converter={x:Static conv:MonedaConverter.Instance}}" />
                                </StackPanel>
                            </StackPanel>
                        </StackPanel>

                        <v2:BarraFiltros x:Name="Filtros" MostrarBusqueda="False">
                            <StackPanel Orientation="Horizontal" Spacing="12">
                                <v2:CampoFormulario x:Name="CampoAnio" Etiqueta="Año">
                                    <NumericUpDown x:Name="SelectorAnio" Value="{Binding Anio}" Minimum="2020" Maximum="2100"
                                                   FormatString="0" Width="120" beh:NumericUpDownValidacionBehavior.Que="año" />
                                </v2:CampoFormulario>
                                <v2:CampoFormulario x:Name="CampoPeriodo" Etiqueta="Período">
                                    <ComboBox x:Name="ComboModo" Width="160"
                                              ItemsSource="{x:Static vm:LibroCajaViewModel.OpcionesModo}"
                                              SelectedItem="{Binding ModoSeleccionado}" />
                                </v2:CampoFormulario>
                                <!-- En Año completo el mes no aplica: se oculta (no se deshabilita). -->
                                <v2:CampoFormulario x:Name="CampoMes" Etiqueta="Mes" IsVisible="{Binding !VerAnioCompleto}">
                                    <NumericUpDown x:Name="SelectorMes" Value="{Binding Mes}" Minimum="1" Maximum="12"
                                                   FormatString="0" Width="100" beh:NumericUpDownValidacionBehavior.Que="mes" />
                                </v2:CampoFormulario>
                            </StackPanel>
                        </v2:BarraFiltros>
                    </DockPanel>
                </v2:TarjetaContenido>

                <!-- Solo con nada cargado: una recarga con datos los deja a la vista. -->
                <v2:TarjetaContenido x:Name="TarjetaCargando">
                    <v2:TarjetaContenido.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" />
                            <Binding Path="LibroMes" Converter="{x:Static ObjectConverters.IsNull}" />
                            <Binding Path="LibroAnual" Converter="{x:Static ObjectConverters.IsNull}" />
                        </MultiBinding>
                    </v2:TarjetaContenido.IsVisible>
                    <v2:EstadoCargando x:Name="IndicadorCarga" Texto="Cargando el libro caja…" />
                </v2:TarjetaContenido>

                <v2:TarjetaContenido x:Name="TarjetaSinConexion" IsVisible="{Binding SinConexion}">
                    <v2:EstadoVacio x:Name="EstadoSinConexion" UseLayoutRounding="False"
                                    Titulo="No se pudo conectar con el servidor"
                                    Mensaje="Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar."
                                    EsError="True">
                        <Button x:Name="BotonReintentar" Classes="secundario" Content="Reintentar"
                                Command="{Binding ReintentarCargaCommand}" />
                    </v2:EstadoVacio>
                </v2:TarjetaContenido>

                <!-- Un período cargado sin movimientos (el VM lo calcula: el año trae doce meses aunque estén en cero). -->
                <v2:TarjetaContenido x:Name="TarjetaVacio" IsVisible="{Binding SinMovimientosEnElPeriodo}">
                    <v2:EstadoVacio x:Name="EstadoSinMovimientos" UseLayoutRounding="False"
                                    Titulo="No hay movimientos en el período"
                                    Mensaje="Probá con otro mes o con el año completo." />
                </v2:TarjetaContenido>

                <StackPanel x:Name="BloquesMes" Spacing="16">
                    <StackPanel.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="LibroMes" Converter="{x:Static ObjectConverters.IsNotNull}" />
                            <Binding Path="!SinMovimientosEnElPeriodo" />
                        </MultiBinding>
                    </StackPanel.IsVisible>

                    <v2:TarjetaContenido x:Name="TarjetaMovimientos">
                        <DockPanel>
                            <TextBlock DockPanel.Dock="Top" Classes="titulo-seccion" Margin="0,0,0,12" Text="Movimientos" />
                            <TextBlock x:Name="PieConteoMovimientos" DockPanel.Dock="Bottom" Classes="pie-conteo" Margin="0,12,0,0"
                                       Text="{Binding Movimientos.Count, Converter={x:Static conv:ConteoConverter.Instance}, ConverterParameter='movimiento|movimientos'}" />
                            <!-- Alto máximo: dentro de una página que scrollea, sin tope la grilla realizaría todas las filas. -->
                            <DataGrid x:Name="GrillaMovimientos" ItemsSource="{Binding MovimientosView}" MaxHeight="516"
                                      IsReadOnly="True" CanUserSortColumns="True" CanUserResizeColumns="True"
                                      ScrollViewer.HorizontalScrollBarVisibility="Auto">
                                <DataGrid.Columns>
                                    <DataGridTemplateColumn Header="FECHA" Width="Auto" SortMemberPath="Fecha"
                                                            beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.NoCede="True"
                                                            beh:ColumnaAuto.Conversor="{x:Static conv:FechaCortaConverter.Instance}">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding Fecha, Converter={x:Static conv:FechaCortaConverter.Instance}}" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="TIPO" Width="Auto" SortMemberPath="Tipo" beh:ColumnaAuto.Medir="True">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding Tipo}" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="CONCEPTO" Width="*" MinWidth="200" SortMemberPath="Concepto">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding Concepto}" TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Concepto}"
                                                           beh:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="PROVEEDOR" Width="Auto" SortMemberPath="ProveedorNombre" beh:ColumnaAuto.Medir="True">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding ProveedorNombre, TargetNullValue='—'}" TextTrimming="CharacterEllipsis"
                                                           ToolTip.Tip="{Binding ProveedorNombre, TargetNullValue='—'}"
                                                           beh:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="FACTURA" Width="Auto" SortMemberPath="NumeroFactura" beh:ColumnaAuto.Medir="True">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding NumeroFactura, TargetNullValue='—'}" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="FUENTE" Width="Auto" SortMemberPath="FuenteNombre" beh:ColumnaAuto.Medir="True">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding FuenteNombre, TargetNullValue='—'}" TextTrimming="CharacterEllipsis"
                                                           ToolTip.Tip="{Binding FuenteNombre, TargetNullValue='—'}"
                                                           beh:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="RUBRO" Width="Auto" SortMemberPath="RubroNombre" beh:ColumnaAuto.Medir="True">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding RubroNombre, TargetNullValue='—'}" TextTrimming="CharacterEllipsis"
                                                           ToolTip.Tip="{Binding RubroNombre, TargetNullValue='—'}"
                                                           beh:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="INGRESO" Width="Auto" CellStyleClasses="num" SortMemberPath="Ingreso"
                                                            beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaOGuionConverter.Instance}">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding Ingreso, Converter={x:Static conv:MonedaOGuionConverter.Instance}}" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="EGRESO" Width="Auto" CellStyleClasses="num" SortMemberPath="Egreso"
                                                            beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaOGuionConverter.Instance}">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding Egreso, Converter={x:Static conv:MonedaOGuionConverter.Instance}}" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="SALDO CORRIDO" Width="Auto" CellStyleClasses="num" SortMemberPath="SaldoCorrido"
                                                            beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:MovimientoCajaDto">
                                                <TextBlock Text="{Binding SaldoCorrido, Converter={x:Static conv:MonedaConverter.Instance}}"
                                                           Classes.negativo="{Binding SaldoCorrido, Converter={x:Static conv:EsNegativoConverter.Instance}}"
                                                           VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                </DataGrid.Columns>
                            </DataGrid>
                        </DockPanel>
                    </v2:TarjetaContenido>

                    <v2:GrillaUniformeV2 x:Name="TotalesMes" Columnas="2" Espaciado="16">
                        <v2:TarjetaContenido x:Name="TarjetaRubrosMes">
                            <StackPanel>
                                <TextBlock Classes="titulo-seccion" Margin="0,0,0,12" Text="Totales por rubro" />
                                <DataGrid x:Name="GrillaRubrosMes" ItemsSource="{Binding TotalesPorRubro}" IsReadOnly="True" CanUserSortColumns="True">
                                    <DataGrid.Columns>
                                        <DataGridTemplateColumn Header="RUBRO" Width="*" SortMemberPath="Clave">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:TotalPorClaveDto">
                                                    <TextBlock Text="{Binding Clave}" TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Clave}"
                                                               beh:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="TOTAL" Width="Auto" CellStyleClasses="num" SortMemberPath="Total"
                                                                beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:TotalPorClaveDto">
                                                    <TextBlock Text="{Binding Total, Converter={x:Static conv:MonedaConverter.Instance}}" VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                    </DataGrid.Columns>
                                </DataGrid>
                            </StackPanel>
                        </v2:TarjetaContenido>
                        <v2:TarjetaContenido x:Name="TarjetaFuentesMes">
                            <StackPanel>
                                <TextBlock Classes="titulo-seccion" Margin="0,0,0,12" Text="Totales por fuente" />
                                <DataGrid x:Name="GrillaFuentesMes" ItemsSource="{Binding TotalesPorFuente}" IsReadOnly="True" CanUserSortColumns="True">
                                    <DataGrid.Columns>
                                        <DataGridTemplateColumn Header="FUENTE" Width="*" SortMemberPath="Clave">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:TotalPorClaveDto">
                                                    <TextBlock Text="{Binding Clave}" TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Clave}"
                                                               beh:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="TOTAL" Width="Auto" CellStyleClasses="num" SortMemberPath="Total"
                                                                beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:TotalPorClaveDto">
                                                    <TextBlock Text="{Binding Total, Converter={x:Static conv:MonedaConverter.Instance}}"
                                                               Classes.negativo="{Binding Total, Converter={x:Static conv:EsNegativoConverter.Instance}}"
                                                               VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                    </DataGrid.Columns>
                                </DataGrid>
                            </StackPanel>
                        </v2:TarjetaContenido>
                    </v2:GrillaUniformeV2>
                </StackPanel>

            </StackPanel>
        </StackPanel>
    </ScrollViewer>

</UserControl>
```

Confirmar con Read antes de compilar: (1) que `GrillaUniformeV2` acepta hijos directos con `Columnas` y `Espaciado` (`GrillaUniformeV2.cs` L21-25; precedente en `InicioView.axaml`); (2) que `ObjectConverters.IsNull`/`IsNotNull` y `BoolConverters.Or` existen en Avalonia 12 (si `{Binding Path=!SinMovimientosEnElPeriodo}` dentro de un `MultiBinding` no compila, usar `Converter={x:Static BoolConverters.Not}` como en `IngresosView.axaml`); (3) que la grilla de movimientos sigue siendo la PRIMERA `DataGrid` del árbol lógico (`GrillasSinRecorteBarridoTests.Montar`, L131).

`LibroCajaView.axaml.cs` no cambia en esta task (sigue llamando `CargarAsync` en `DataContextChanged`).

Tests heredados (convención 13):
- `GuardianDePatronTests`: quitar `[InlineData(typeof(LibroCajaView), "Libro caja", "FINANZAS")]` (L66) y `typeof(LibroCajaView),` de `VistasDeLaTanda` (L114); agregar `typeof(LibroCajaView),` a `VistasMigradasAV2`. Reemplazo: `Raiz_TieneUi2_EncabezadoConTitulo_ActualizarYExportarSecundarios_SinPrimario`.
- `LibroCajaViewTests`: `BuscarEtiquetaCaptionPorTexto` (L89-91, busca `TextBlock` con `Classes="caption"`) pasa a buscar `window.GetVisualDescendants().OfType<CampoFormulario>().First(c => c.Etiqueta == texto)` y los dos tests de etiqueta afirman `ArbolVisual.EsVisibleEnArbol(campo)`; conservan su nombre y lo que custodian (cada `NumericUpDown` tiene su etiqueta). `Mes_Invalido_…` no cambia (busca por `Maximum` y por `RecargarCommand`): correrlo; si `NumericUpDownPrueba.AssertErrorVisible` no encuentra el mensaje en la plantilla v2, adaptar SOLO el helper como lo hizo `ControlPoaViewV2Tests.EjercicioInvalido_MarcaElError_YNoRecarga` y anotarlo.
- `SignoNegativoBadgeTests`: en `Montar_LibroCajaViewConSaldoFinalNegativo_…` y `…Positivo_…` (L205-223), `BadgesConTexto(window, "Saldo negativo")` pasa a `window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.Insignia>().Where(i => i.Texto == "Saldo negativo")`, con `ArbolVisual.EsVisibleEnArbol` en vez de `IsVisible` y, en el negativo, `Assert.Equal(VarianteInsignia.Peligro, visible.Variante)` (patrón del sitio de Control POA, L276-281). Con el mes vacío del fake los saldos siguen a la vista (D5), así que el sitio sigue ejercitado. Actualizar el comentario de la sección (L142) a "Finanzas: LibroCajaView, saldo final".
- `GrillasSinRecorteBarridoTests`, escenario "Libro de caja (movimientos)": correrlo. Si una columna medida aparece recortada, la medición de la Task 6 estaba mal: re-medir, no relajar el test.
- `ExportarPdfBotonesGuardianTests.Montar_LibroCajaView_…`, `NumericUpDownVistasRealesTests`, `ShellMainViewGatesTests`, `AccesoRevocadoPantallasFinanzasTests`: sin cambio, deben seguir verdes.

- [ ] **Step 4: Build, verde y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~SignoNegativoBadge|FullyQualifiedName~NumericUpDownVistasReales|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~GrillasEspacioDerecho|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~DataGridColumnas|FullyQualifiedName~ControlPoa"
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCaja"
```

- [ ] **Step 5: Comparaciones del modo mes y de los estados**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/LibroCajaComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Libro caja dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class LibroCajaComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, string[]? recortesPermitidos = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar,
            recortesPermitidos: recortesPermitidos);

    [AvaloniaFact] public void LibroCaja_IgualAlMockup() => Comparar(DatosLibroCaja.ConMovimientos(), "finanzas/libro-caja.html");
    [AvaloniaFact] public void LibroCaja_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.ConMovimientos(), "finanzas/libro-caja.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaSaldoNegativo_IgualAlMockup() => Comparar(DatosLibroCaja.SaldoNegativo(), "finanzas/libro-caja-saldo-negativo.html");
    [AvaloniaFact] public void LibroCajaSaldoNegativo_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SaldoNegativo(), "finanzas/libro-caja-saldo-negativo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaVacio_IgualAlMockup() => Comparar(DatosLibroCaja.SinMovimientos(), "finanzas/libro-caja-vacio.html");
    [AvaloniaFact] public void LibroCajaVacio_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SinMovimientos(), "finanzas/libro-caja-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaCargando_IgualAlMockup() => Comparar(DatosLibroCaja.Cargando(), "finanzas/libro-caja-cargando.html");
    [AvaloniaFact] public void LibroCajaCargando_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.Cargando(), "finanzas/libro-caja-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaSinConexion_IgualAlMockup() => Comparar(DatosLibroCaja.SinConexion(), "finanzas/libro-caja-sin-conexion.html");
    [AvaloniaFact] public void LibroCajaSinConexion_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SinConexion(), "finanzas/libro-caja-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaSinPermiso_IgualAlMockup() => Comparar(DatosLibroCaja.SinPermiso(), "finanzas/libro-caja-sin-permiso.html");
    [AvaloniaFact] public void LibroCajaSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SinPermiso(), "finanzas/libro-caja-sin-permiso.html", TamanoLienzo.Municipio);
}
```

Si la grilla hace scroll horizontal, las columnas fuera de vista no se reportan (la comparación excluye el eje scrolleable del `DataGrid`); un texto cortado a la vista se declara en `recortesPermitidos` SOLO si el mockup aprobado también lo corta (precedente: `ControlPoaComparacionTests.CorteDelMockupA1280`).

Run: `scripts/ui-v2/comparar.sh LibroCaja`. Expected: 12 filas APROBADAS con el umbral por defecto. Si una no entra: diagnosticar con `teselas.py`/`tinta.py` (copiados a `$SCRATCH/diag/`, nunca se commitean), corregir la vista o el mockup (≤ 4 px no visible se anota; mayor vuelve al usuario); un `UmbralAprobado` mayor solo en un GATE con el usuario.

- [ ] **Step 6: Mutaciones (QUITANDO)**

- (q) Quitar `IsVisible="{Binding !SinPermiso}"` de `Contenido` → rojo `Un403_SinPermisoEnLugarDelContenido_…`. Pegar. Restaurar. (Spec: "el estado `SinPermiso`".)
- (r) Quitar `Classes.negativo="{Binding SaldoFinal, …}"` de `ValorSaldoFinal` → rojo `SaldoFinalNegativo_InsigniaPeligroYTextoPorToken`. Restaurar.
- (s) Quitar `IsVisible="{Binding SaldoInicial, …}"` de `InsigniaSaldoInicialNegativo` → rojo `Saldos_PositivosEnEsUy_SinInsignia`. Restaurar.
- (t) Quitar `MaxHeight="516"` de `GrillaMovimientos` → rojo `Grilla_ConSeiscientosMovimientos_NoRealizaTodasLasFilas`. Restaurar.
- (u) Quitar la línea `<Binding Path="LibroMes" Converter="{x:Static ObjectConverters.IsNull}" />` del `MultiBinding` de `TarjetaCargando` → rojo `ConMovimientos_ExportarHabilitado_YMientrasRecargaDeshabilitado` (el indicador aparece encima de los datos durante la recarga). Restaurar.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml tests/StockApp.Presentation.UiTests/V2/FinanzasVistasFakes.cs tests/StockApp.Presentation.UiTests/V2/LibroCajaViewV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.UiTests/LibroCajaViewTests.cs tests/StockApp.Presentation.UiTests/SignoNegativoBadgeTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/LibroCajaComparacionTests.cs
git commit -m "feat(ui-v2): libro caja v2 con grilla del mes, saldos por token y estados de pantalla"
```

(Sumar `GrillasSinRecorteBarridoTests.cs` si cambió.)

### Task 9: Libro caja v2, modo año, selector y gates

**Depende de:** Task 8.

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml` (bloques del año, después de `BloquesMes`), `LibroCajaView.axaml.cs`
- Modify: `tests/StockApp.Presentation.UiTests/V2/LibroCajaViewV2Tests.cs`, `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/LibroCajaComparacionTests.cs`

**Interfaces:**
- Consumes: lo de la Task 8; `LibroAnual.TotalesPorMes`, `LibroAnual.TotalesPorRubro`, `MesesConMovimientos`, `NombreMesConverter`, `NumericUpDownValidacionBehavior.SetValidar(NumericUpDown, bool)`; `ShellMainViewModel`, `NavigationServiceFake`, `InfoAppFake`, `AuthServiceFake(ICurrentSession, IReadOnlySet<string>)`, `PreferenciasSidebarFake`, `ManejadorAccesoRevocado.ManejarAsync(…)`, `ViewLocator`, `TestApp`.
- Produces: en la vista, `x:Name` `BloquesAnio`, `TarjetaMeses`, `GrillaMeses`, `PieConteoMeses`, `TarjetaRubrosAnio`, `GrillaRubrosAnio`; code-behind que descarta el texto inválido de Mes al ocultarse.

- [ ] **Step 1: Tests (rojo)**

En `LibroCajaViewV2Tests.cs` agregar `using Avalonia.Markup.Xaml;`, `using StockApp.Presentation.Services;`, `using StockApp.Presentation.ViewModels;` y:

```csharp
    private static readonly LibroCajaAnualDto Anio2026 = new(2026,
        [.. Enumerable.Range(1, 12).Select(m => m switch
        {
            1 => new TotalMensualDto(1, 1_250_000m, 980_200m, 269_800m),
            3 => new TotalMensualDto(3, 1_250_000m, 1_322_750m, -72_750m),
            _ => new TotalMensualDto(m, 0m, 0m, 0m),
        })],
        [new TotalPorClaveDto("Obras viales", 2_302_950m)]);

    private static FinanzasVistasFake ConOctubreYAnio()
    {
        var servicio = ConOctubre();
        servicio.Anios[2026] = Anio2026;
        servicio.Meses[(2026, 1)] = new LibroCajaMesDto(2026, 1, 812_430.55m, 812_430.55m, [], [], []);
        return servicio;
    }

    private static async Task PasarAAsync(Montaje m, string modo)
    {
        Control<ComboBox>(m, "ComboModo").SelectedItem = modo;
        Dispatcher.UIThread.RunJobs();
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task PasarAAnioCompleto_OcultaElCampoMes_YMuestraLosBloquesDelAnio()
    {
        var m = Montar(ConOctubreYAnio());

        Control<ComboBox>(m, "ComboModo").SelectedItem = "Año completo";
        Dispatcher.UIThread.RunJobs();
        Assert.False(Visible(Control<CampoFormulario>(m, "CampoMes")));   // al instante, antes de la recarga

        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Visible(Control<StackPanel>(m, "BloquesAnio")));
        Assert.False(Visible(Control<StackPanel>(m, "BloquesMes")));
        Assert.Equal(new[] { "2026-10", "2026", "2026-01" }, m.Servicio.ConsultasLibroCaja);   // con P1-B, sin "2026-01"
    }

    [AvaloniaFact]
    public async Task VolverAMes_MuestraElCampoYLosBloquesDelMes()
    {
        var m = Montar(ConOctubreYAnio());
        await PasarAAsync(m, "Año completo");

        await PasarAAsync(m, "Mes");

        Assert.True(Visible(Control<CampoFormulario>(m, "CampoMes")));
        Assert.True(Visible(Control<StackPanel>(m, "BloquesMes")));
        Assert.False(Visible(Control<StackPanel>(m, "BloquesAnio")));
    }

    [AvaloniaFact]
    public async Task AnioCompleto_GrillaPorMesConNombre_YDeficitPorInsigniaYToken()
    {
        var m = Montar(ConOctubreYAnio());
        await PasarAAsync(m, "Año completo");
        var grilla = Control<DataGrid>(m, "GrillaMeses");

        Assert.Equal(new object?[] { "MES", "INGRESOS", "EGRESOS", "NETO" }, grilla.Columns.Select(c => c.Header).ToArray());
        var marzo = grilla.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext is TotalMensualDto { Mes: 3 });
        Assert.Contains(marzo.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Marzo");
        var deficit = Assert.Single(grilla.GetVisualDescendants().OfType<Insignia>(), i => i.Texto == "Déficit" && Visible(i));
        Assert.Equal(VarianteInsignia.Peligro, deficit.Variante);
        Assert.Same(marzo, deficit.FindAncestorOfType<DataGridRow>());
        Assert.Equal(Peligro, ColorDe(marzo.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "-$ 72.750,00")));
    }

    [AvaloniaFact]
    public async Task AnioCompleto_TotalesPorRubro_SinTotalesPorFuente_YPie()
    {
        var m = Montar(ConOctubreYAnio());
        await PasarAAsync(m, "Año completo");

        Assert.True(Visible(Control<TarjetaContenido>(m, "TarjetaRubrosAnio")));
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Totales por fuente" && Visible(t));
        Assert.Equal("2 meses con movimientos", Control<TextBlock>(m, "PieConteoMeses").Text);
    }

    [AvaloniaFact]
    public async Task AnioCompleto_SaldosDelAnio_YExportarHabilitado()
    {
        var m = Montar(ConOctubreYAnio());
        await PasarAAsync(m, "Año completo");

        Assert.Equal("$ 812.430,55", Control<TextBlock>(m, "ValorSaldoInicial").Text);
        Assert.Equal("$ 1.009.480,55", Control<TextBlock>(m, "ValorSaldoFinal").Text);
        Assert.True(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task AnioCompletoSinMovimientos_VacioYExportarDeshabilitado()
    {
        var m = Montar(ConOctubre());   // el fake responde el año con los doce meses en cero
        await PasarAAsync(m, "Año completo");

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinMovimientos")));
        Assert.False(Visible(Control<StackPanel>(m, "BloquesAnio")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task MesInvalido_AlPasarAAnioCompleto_NoBloqueaActualizar()
    {
        var m = Montar(ConOctubreYAnio());
        var mes = Control<NumericUpDown>(m, "SelectorMes");
        var texto = mes.GetVisualDescendants().OfType<TextBox>().First(t => t.TemplatedParent == mes);
        texto.Focus();
        Dispatcher.UIThread.RunJobs();
        texto.Text = "13";
        Dispatcher.UIThread.RunJobs();
        Control<NumericUpDown>(m, "SelectorAnio").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(DataValidationErrors.GetHasErrors(mes));
        Assert.False(Control<Button>(m, "BotonActualizar").IsEffectivelyEnabled);   // precondición

        await PasarAAsync(m, "Año completo");

        Assert.False(DataValidationErrors.GetHasErrors(mes));
        Assert.True(Control<Button>(m, "BotonActualizar").IsEffectivelyEnabled);

        await PasarAAsync(m, "Mes");

        Assert.Equal("10", texto.Text);
        Assert.False(DataValidationErrors.GetHasErrors(mes));
    }

    [AvaloniaFact]
    public void OperadorMixto_SoloConVerFinanzas_VeTodoYPuedeExportar()
    {
        var m = Montar(ConOctubre(), Mixto());

        Assert.True(Visible(Control<Button>(m, "BotonActualizar")));
        Assert.True(Visible(Control<Button>(m, "BotonExportarPdf")));
        Assert.True(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
        Assert.True(Visible(Control<StackPanel>(m, "BloquesMes")));
    }

    [AvaloniaFact]
    public async Task Un403AlActualizar_DentroDelShell_MuestraSinPermisoSinNavegar()
    {
        var sesion = Mixto();
        var tras = new HashSet<string>();
        var servicio = ConOctubre();
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = new LibroCajaViewModel(servicio, new GuardadoSinUsoFake(), new ConfirmacionServiceFake(), new PdfExporterSinUsoFake(),
            new ServicioAperturaArchivoFake(), sesion, new RelojFijo()) { EsperaDebounce = TimeSpan.Zero };
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
        var vista = ventana.GetVisualDescendants().OfType<LibroCajaView>().Single();
        Assert.True(ArbolVisual.EsVisibleEnArbol(vista.FindControl<StackPanel>("BloquesMes")!), "Precondición: el mes cargado se ve.");

        servicio.FallaLibroCaja = new UnauthorizedAccessException("403");
        await vm.RecargarCommand.ExecuteAsync(null);
        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.True(ArbolVisual.EsVisibleEnArbol(vista.FindControl<EstadoVacio>("EstadoSinPermiso")!));
        Assert.False(ArbolVisual.EsVisibleEnArbol(vista.FindControl<StackPanel>("Contenido")!));
        Assert.False(vista.FindControl<Button>("BotonExportarPdf")!.IsEffectivelyEnabled);
        Assert.Same(vm, shell.CurrentContent);
    }
```

Antes de escribirlo, copiar de `CalendarioPagosViewV2Tests` (L172-200) e `IngresosViewV2Tests` (`Un403_QueQuitaRegistrarIngresos_…`) las firmas exactas de `ShellMainViewModel`, `AuthServiceFake` y `ManejadorAccesoRevocado.ManejarAsync`; si difieren de lo de arriba, ajustar el test (no la producción) y anotarlo.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LibroCajaViewV2Tests"` → FAIL (no existen `BloquesAnio`, `GrillaMeses`, `PieConteoMeses`, `TarjetaRubrosAnio`; `MesInvalido_…` deja Actualizar bloqueado). Pegar.

- [ ] **Step 2: Vista v2, bloques del año y code-behind**

En `LibroCajaView.axaml`, después del `</StackPanel>` de `BloquesMes`:

```xml
                <StackPanel x:Name="BloquesAnio" Spacing="16">
                    <StackPanel.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="LibroAnual" Converter="{x:Static ObjectConverters.IsNotNull}" />
                            <Binding Path="!SinMovimientosEnElPeriodo" />
                        </MultiBinding>
                    </StackPanel.IsVisible>

                    <v2:TarjetaContenido x:Name="TarjetaMeses">
                        <StackPanel>
                            <TextBlock Classes="titulo-seccion" Margin="0,0,0,12" Text="Totales por mes" />
                            <!-- Los doce meses, también los que están en cero. NETO: ancho fijo medido con el peor caso
                                 (insignia Déficit + neto negativo de 7 cifras), insignia a la izquierda y número a la derecha. -->
                            <DataGrid x:Name="GrillaMeses" ItemsSource="{Binding LibroAnual.TotalesPorMes}" IsReadOnly="True" CanUserSortColumns="True">
                                <DataGrid.Columns>
                                    <DataGridTemplateColumn Header="MES" Width="Auto" SortMemberPath="Mes"
                                                            beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:NombreMesConverter.Instance}">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:TotalMensualDto">
                                                <TextBlock Text="{Binding Mes, Converter={x:Static conv:NombreMesConverter.Instance}}" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="INGRESOS" Width="*" CellStyleClasses="num" SortMemberPath="Ingresos">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:TotalMensualDto">
                                                <TextBlock Text="{Binding Ingresos, Converter={x:Static conv:MonedaConverter.Instance}}" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="EGRESOS" Width="*" CellStyleClasses="num" SortMemberPath="Egresos">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:TotalMensualDto">
                                                <TextBlock Text="{Binding Egresos, Converter={x:Static conv:MonedaConverter.Instance}}" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="NETO" Width="<medido>" CellStyleClasses="num" SortMemberPath="Neto">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:TotalMensualDto">
                                                <Grid VerticalAlignment="Center">
                                                    <v2:Insignia Texto="Déficit" Variante="Peligro" HorizontalAlignment="Left"
                                                                 IsVisible="{Binding Neto, Converter={x:Static conv:EsNegativoConverter.Instance}}" />
                                                    <TextBlock Text="{Binding Neto, Converter={x:Static conv:MonedaConverter.Instance}}"
                                                               Classes.negativo="{Binding Neto, Converter={x:Static conv:EsNegativoConverter.Instance}}"
                                                               HorizontalAlignment="Right" VerticalAlignment="Center" />
                                                </Grid>
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                </DataGrid.Columns>
                            </DataGrid>
                            <TextBlock x:Name="PieConteoMeses" Classes="pie-conteo" Margin="0,12,0,0"
                                       Text="{Binding MesesConMovimientos, Converter={x:Static conv:ConteoConverter.Instance}, ConverterParameter='mes con movimientos|meses con movimientos'}" />
                        </StackPanel>
                    </v2:TarjetaContenido>

                    <v2:TarjetaContenido x:Name="TarjetaRubrosAnio">
                        <StackPanel>
                            <TextBlock Classes="titulo-seccion" Margin="0,0,0,12" Text="Totales por rubro" />
                            <DataGrid x:Name="GrillaRubrosAnio" ItemsSource="{Binding LibroAnual.TotalesPorRubro}" IsReadOnly="True" CanUserSortColumns="True">
                                <DataGrid.Columns>
                                    <DataGridTemplateColumn Header="RUBRO" Width="*" SortMemberPath="Clave">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:TotalPorClaveDto">
                                                <TextBlock Text="{Binding Clave}" TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Clave}"
                                                           beh:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                    <DataGridTemplateColumn Header="TOTAL" Width="Auto" CellStyleClasses="num" SortMemberPath="Total"
                                                            beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate x:DataType="dto:TotalPorClaveDto">
                                                <TextBlock Text="{Binding Total, Converter={x:Static conv:MonedaConverter.Instance}}" VerticalAlignment="Center" />
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                </DataGrid.Columns>
                            </DataGrid>
                        </StackPanel>
                    </v2:TarjetaContenido>
                </StackPanel>
```

(INGRESOS y EGRESOS proporcionales y NETO fijo es la propuesta; si la medición de la Task 6 y el mockup aprobado dicen otra cosa, mandan ellos y se anota.)

`LibroCajaView.axaml.cs`:

```csharp
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class LibroCajaView : UserControl
{
    private LibroCajaViewModel? _vm;

    public LibroCajaView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= AlCambiarVm;
            _vm = DataContext as LibroCajaViewModel;
            if (_vm is null)
                return;
            _vm.PropertyChanged += AlCambiarVm;
            await _vm.CargarAsync();
        };
    }

    /// <summary>
    /// En Año completo el campo Mes se oculta, pero un control oculto sigue en el árbol: si quedó con un texto inválido,
    /// su error seguiría bloqueando Actualizar sin que se vea por qué. Al ocultarlo se apaga su validación (que retira el
    /// error) y vuelve a mostrar el mes del ViewModel; al volver a Mes se prende de nuevo.
    /// </summary>
    private void AlCambiarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibroCajaViewModel.VerAnioCompleto) || _vm is null)
            return;

        if (_vm.VerAnioCompleto)
        {
            NumericUpDownValidacionBehavior.SetValidar(SelectorMes, false);
            SelectorMes.Text = _vm.Mes.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            NumericUpDownValidacionBehavior.SetValidar(SelectorMes, true);
        }
    }
}
```

Si `MesInvalido_AlPasarAAnioCompleto_NoBloqueaActualizar` sigue en rojo con esto (por ejemplo, el `TextBox` interno conserva "13" porque `NumericUpDown.Text` no lo reescribe), PARAR y reportar al controlador con la evidencia (texto interno, `HasErrors`, `HayErroresDeEntrada`): la alternativa toca `NumericUpDownValidacionBehavior` (global, alcanza a todas las vistas) y no se parchea en silencio.

- [ ] **Step 3: Build, verde y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~SignoNegativoBadge|FullyQualifiedName~NumericUpDownVistasReales|FullyQualifiedName~NumericUpDownTextoInvalido|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~DataGridColumnas"
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCaja|FullyQualifiedName~NombreMes"
```

- [ ] **Step 4: Comparaciones del modo año**

En `LibroCajaComparacionTests.cs`:

```csharp
    [AvaloniaFact] public void LibroCajaAnio_IgualAlMockup() => Comparar(DatosLibroCaja.AnioCompleto(), "finanzas/libro-caja-anio.html");
    [AvaloniaFact] public void LibroCajaAnio_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.AnioCompleto(), "finanzas/libro-caja-anio.html", TamanoLienzo.Municipio);
```

Run: `scripts/ui-v2/comparar.sh LibroCaja`. Expected: 14 filas APROBADAS (mismo criterio que la Task 8 si no).

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (v) Quitar `IsVisible="{Binding !VerAnioCompleto}"` de `CampoMes` → rojo `PasarAAnioCompleto_OcultaElCampoMes_…`. Pegar. Restaurar. (Spec: "el `IsVisible` del campo Mes".)
- (w) Quitar la línea `NumericUpDownValidacionBehavior.SetValidar(SelectorMes, false);` → rojo `MesInvalido_AlPasarAAnioCompleto_NoBloqueaActualizar`. Restaurar.
- (x) Quitar `IsVisible="{Binding Neto, …}"` de la insignia "Déficit" → rojo `AnioCompleto_GrillaPorMesConNombre_YDeficit…` (doce insignias visibles). Restaurar.
- (y) Quitar `Classes.negativo="{Binding Neto, …}"` del `TextBlock` de NETO → rojo el mismo test. Restaurar.
- (z) Quitar la línea `<Binding Path="!SinMovimientosEnElPeriodo" />` del `MultiBinding` de `BloquesAnio` → rojo `AnioCompletoSinMovimientos_VacioYExportarDeshabilitado`. Restaurar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml.cs tests/StockApp.Presentation.UiTests/V2/LibroCajaViewV2Tests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/LibroCajaComparacionTests.cs
git commit -m "feat(ui-v2): libro caja v2 con el año completo, déficit por insignia y el mes que se oculta sin bloquear"
```

---

## Bloque D — Guardianes, verificación orgánica y cierre

### Task 10: Repaso de guardianes y de mutaciones; suite y comparación completas

**Files:**
- Modify (local): el ledger
- Modify: los tests que haga falta ajustar (cada ajuste con su rojo y su motivo)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la evidencia completa (suite, comparación, mutaciones) para el GATE 2 de la Task 11.

- [ ] **Step 1: Guardianes de la fase**

Confirmar con `grep` y la CLI `codegraph`, y anotar en el ledger:
1. `LibroCajaView` está en `VistasMigradasAV2` y en ninguna otra lista de `GuardianDePatronTests`; `Guardian_CubreTodasLasVistasDelEnsamblado` verde.
2. `VistaV2_NoUsaPiezasDelTemaViejo` cubre la vista (`SignoNegativoBrushConverter`, `ActivoOpacidadConverter`, `BadgeEstado`, `HeaderVista`, `c:CampoFormulario`, `c:EstadoVacio` ausentes).
3. `VistaV2_NoTieneOpacidadesLiterales` verde; `grep -n Opacity src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml` vacío.
4. `grep -n 'Classes="primary"\|Classes="secondary"\|Classes="caption"\|Classes="seccion"\|<c:\|SignoNegativoBrushConverter\|BadgeEstado' src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml` vacío.
5. `NingunaVista_TieneOverlaysDeModal` verde.
6. `ComandosDeGuardadoGuardianTests` verde (Actualizar sigue siendo `ComandoDeGuardado`).
7. `grep -rn "Csv\|CSV" src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml` vacío.

Si un punto falla, se corrige en un commit propio con su rojo pegado.

- [ ] **Step 2: Repaso de mutaciones**

Confirmar en el ledger que quedaron PEGADOS los rojos de las cinco reglas que pide la spec (sección 3): el `IsVisible` del campo Mes (v), la recarga al cambiar el modo (f), el estado `SinPermiso` (q), el descarte de la consulta vieja (a) y la habilitación del PDF anual (l); y las demás de la fase (b a e, g a k, m a p, r a u, w a z). Una sin rojo pegado se corre ahora.

- [ ] **Step 3: Build, suite completa, herramientas y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos `.ods` de `Infrastructure.Tests` / `Api.Tests` y el intermitente de `ApiStartupMigrationTests` (confirmar por nombre contra la línea base); total ≥ 5789 + nuevos − reemplazados (anotar número y reemplazos: ninguno se borra en esta fase, `ExportarPdfCommand_ConAnioCompleto_NoExporta` se RENOMBRA); los dos `--verificar` exit 0; TODAS las filas de `index.html` APROBADAS a los dos tamaños (las de fases anteriores sin cambios; si una se mueve, diagnosticarla: el estilo `.negativo` nuevo y `base.css` las pueden alcanzar).

- [ ] **Step 4: Commit (solo si hubo ajustes)**

```bash
git add <archivos ajustados>
git commit -m "test(ui-v2): guardianes de la Fase 4c al día"
```

### Task 11: Verificación orgánica (GATE 2 con el usuario), docs de cierre y review final

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (convenciones 32 y 33)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, deuda, §6.5)
- Modify (local): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 4c cerrada en `feat/ui-v2` (sin push ni merge).

- [ ] **Step 1: Verificación orgánica (spec §6.4) — app real contra la base local, en WSLg a 1280×760 y 1366×768**

Preparación:
- `docker start stockapp-pg` (queda andando al terminar). **Backup ANTES de crear datos:** `mkdir -p ~/stockapp-backups-local && docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-4c-$(date +%Y%m%d-%H%M%S).sql` (usuario y base del `.env` local o de `~/.config/GestionMunicipal/conexion.json`; verificar con `ls -l` que no está vacío). Anotar la ruta en el ledger.
- API del worktree: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http` (confirmar el puerto contra `conexion.json`). App: `dotnet run --project src/StockApp.Presentation`. `admin` / `Admin1234`.
- Operador de permisos MIXTOS: crear `f4cmixto` con `finanzas.ver` y nada más de finanzas; anotar su id.
- Datos de prueba por la app (camino del dominio) con prefijo `PRUEBA-4C`, en un mes elegido y anotado (ej. noviembre 2026): dos ingresos, y un gasto con un pago por un monto mayor al saldo del mes para tener saldo final y saldo corrido negativos; anotar los ids.
- Toolkit `scripts/gui-verificacion/`. MIRAR cada captura y comparar `md5sum` con la anterior (dos iguales cuando la pantalla debía cambiar = WSLg congelado: PARAR y pedir al usuario que reinicie WSL). Tras un diálogo modal la ventana principal puede quedar sin foco (`xdotool windowminimize` + `windowmap`). El Aviso global puede quedar DETRÁS de la ventana (`xdotool search --name`).

Pasos (capturar cada pantalla; 1, 3, 4 y 6 se repiten a 1366×768):
1. **Mes con datos (Admin):** Libro caja abre en el mes actual, con "Cargando…" y después la grilla, los saldos, los totales por rubro y por fuente y el pie; ordenar por FECHA, PROVEEDOR y SALDO CORRIDO; tooltip de un concepto cortado; scroll de la grilla y de la página. Comparar a ojo con `libro-caja.html`.
2. **Recarga y validación:** cambiar Mes con las flechas → recarga sola; tipear "13" → error debajo del campo y Actualizar bloqueado con su tooltip; pasar a "Año completo" → el campo Mes desaparece y Actualizar se habilita; volver a "Mes" → el campo muestra el mes válido sin error. Tipear un año "20a5" → error, no recarga.
3. **Año completo:** los 12 meses por nombre, NETO negativo con "Déficit" si lo hay, totales por rubro, pie, saldos del año. Comparar con `libro-caja-anio.html`.
4. **PDF anual:** Exportar PDF en Año completo → guardar `libro-caja-AAAA.pdf` → abrirlo: vertical, membrete, tabla Mes · Ingresos · Egresos · Neto, saldos, "Meses con déficit" (si hay), "Totales por rubro", sin totales por fuente.
5. **PDF mensual:** en Mes, Exportar PDF → `libro-caja-AAAA-MM.pdf` → abrirlo: apaisado, 10 columnas, saldos, totales por rubro y por fuente. Cambiar el mes y exportar ANTES de que recargue no se puede reproducir a mano de forma confiable (300 ms): queda custodiado por `ExportarPdf_ConMesCambiadoSinRecargar_…`.
6. **Saldo negativo:** el mes `PRUEBA-4C` → saldo final y saldo corrido en rojo con la insignia "Saldo negativo". Comparar con `libro-caja-saldo-negativo.html`.
7. **Vacío:** un mes sin movimientos (ej. 2020, enero) → card "No hay movimientos en el período", saldos visibles, Exportar PDF deshabilitado.
8. **Exportar PDF desde otras pantallas** (verificación pendiente del borrado de CSV, `a3ab4d4`): Control POA, Gastos y un reporte (Valorización) → solo "Exportar PDF", se guarda y se abre; ningún botón ni menú de CSV.
9. **Operador mixto `f4cmixto`:** ve todo y exporta. Desde la sesión de Admin quitarle `finanzas.ver`; en la de `f4cmixto`, "Actualizar" → Aviso global de permisos cambiados (uno solo) y "Sin permiso" en lugar del contenido, Exportar deshabilitado, sin navegar.
10. **Sin servidor:** `kill` SOLO del pid de `StockApp.Api` → "Actualizar" → card "No se pudo conectar con el servidor" con Reintentar, sin modal global; levantar la API y "Reintentar" → carga. Anotar si salió un Aviso global y dónde.
11. **Deuda de la 4b (1 minuto):** en el formulario de Ingresos, la guarda del popup del calendario (`IngresoFormView`): abrir el calendario, elegir una fecha con el mouse, verificar que no queda el foco atrapado en el popup. Anotar el resultado.
12. **Limpieza:** dar de baja por la app los ingresos `PRUEBA-4C`, anular el pago y el gasto `PRUEBA-4C` (camino del dominio) y dar de baja a `f4cmixto` por Administración de usuarios. Listar los ids y PREGUNTAR al usuario si quiere además un borrado físico con SQL acotado por esos ids, SOLO contra el contenedor local `stockapp-pg` (nunca el VPS). Anotar en el ledger. El contenedor queda andando.

Si un paso no se puede hacer por el entorno (WSLg caído o congelado, contenedor o API que no arrancan), PARAR y avisar con la evidencia (stderr, `docker ps -a`, md5 de las capturas), sin declarar la fase cerrada. Si el usuario necesita la PC, se pausa.

- [ ] **Step 2: GATE 2 — aprobación final del usuario (PARADA)**

PARAR. Presentar: `index.html` (las 14 filas del Libro caja), las capturas de la verificación orgánica junto a los mockups, los dos PDF abiertos (mensual y anual), los rojos de las mutaciones (a) a (z), los tests heredados adaptados (y el renombrado), los `UmbralAprobado` si los hubo, la elección de P1 a P3 y los cambios respecto de los mockups aprobados. No seguir sin "aprobado" explícito. Registrar en el ledger: `Fase 4c: aprobada por <quién> <fecha>`. Lo que el usuario observe se arregla en commits propios con TDD (rojo pegado) o va a la deuda con su OK.

- [ ] **Step 3: `herramientas.md` y spec**

`herramientas.md`, después de la 31: "Convenciones que agrega la Fase 4c (Libro caja)" con la 32 y la 33 de este plan (como quedaron aprobadas).

Spec:
1. `**Estado:**`: "4c" en la lista de fases implementadas y "; Fase 4c (Libro caja) APROBADA por el usuario el <fecha>"; la fila de Finanzas de la tabla de §7.
2. §7, después de "Estado de la Fase 4b": "**Estado de la Fase 4c (aprobada el <fecha>).** Libro caja v2 (filtros Año · Período · Mes con recarga sola por debounce, selector Mes | Año completo con el campo Mes que se oculta y no bloquea, lo que se ve es lo cargado según la convención 32, grilla del mes de 10 columnas con scroll propio, saldos y netos negativos por token con insignia según la convención 33, totales por rubro y por fuente, año completo con los 12 meses y déficit, estados completos) y el PDF anual nuevo, con E1 a E6. Decisiones previas: P1 <A/B>, P2 <A/B>, P3 <A/B>. Comparado en 14 filas a los dos tamaños (<máximo de teselas densas>). Suite <total> en `<hash>`. Verificación orgánica: <resumen>. Decisiones del usuario: <D1 a D17 aprobadas o cambiadas>."
3. §6.5: cada compensación nueva medida en las Tasks 8 y 9 (si hubo), con su antes y después.
4. "**Deuda abierta tras la Fase 4c:**" con, como mínimo: D17 (revocación en caliente sin consulta: datos y PDF hasta la próxima consulta), lo que P2 y P3 dejaron (orden del PDF anual y déficit por sección, si se eligió A), el resultado del paso 11 (deuda de la 4b) y del paso 10 (Aviso global), cualquier `UmbralAprobado`, los minors diferidos del ledger y los datos de prueba que hayan quedado.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 4c (libro caja)"
```

- [ ] **Step 4: Review final de la rama**

Review de todo el rango `<commit de este plan>..HEAD` (skill de ejecución: review de rama completa con el modelo más capaz), con la suite y las comparaciones ya verdes. Foco: los cinco puntos de "Review Focus", que la excepción a "VMs intactos" no exceda E1 a E6 (y el reloj de E2), que ninguna pieza del tema viejo haya quedado en la vista, que el estilo `.negativo` nuevo no movió ninguna comparación de fases anteriores, que el PDF mensual sigue igual que antes (mismas columnas, resumen y nombre de archivo) y que el modo año no puede mostrar bloques del mes ni exportar con datos de otro período. Los hallazgos se resuelven en commits propios (TDD, con rojo pegado) o se anotan como deuda con el usuario. No se pushea ni se mergea.

---

## Fuera de alcance

- Cambios de API o de DTOs (`GET /finanzas/libro-caja` y los cinco DTOs intactos) y de `FinanzasVistasService`.
- Filtros de texto, edición de movimientos, totales por fuente en el modo año (spec, sección 3).
- Exportación CSV (eliminada de toda la app en `a3ab4d4`).
- Cambiar `PlantillaTabular` o `IPdfExporter` (salvo que el usuario elija P2-B).
- Replicar en Libro caja el gate de Gastos ante una revocación en caliente sin consulta (D17).
- Migrar Gastos y su formulario (4d) y Pagos del gasto (4e).
- Modo oscuro, back-stack (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| Una respuesta vieja de otro modo deja la pantalla incoherente (selector en un modo, bloques del otro). | Un solo turno para los dos modos; `RespuestaMensualVieja_…` y `RespuestaAnualVieja_…` (Task 1), mutación (a). |
| El PDF rotula o serializa un período distinto del que se ve. | Exporta desde `LibroMes`/`LibroAnual`; `ExportarPdf_ConMesCambiadoSinRecargar_…` y `ExportarPdf_RecienPasadoAAnio…` (Task 4). |
| El campo Mes oculto con un texto inválido bloquea Actualizar. | Code-behind que apaga su validación al ocultarlo; `MesInvalido_AlPasarAAnioCompleto_…` (Task 9), mutación (w); si no alcanza, PARAR (tocaría un behavior global). |
| El estilo `.negativo` global cambia otras vistas. | Va bajo `.ui2` y con el mismo color que la regla de la celda; comparación completa de la Task 10; `TextoNegativoV2Tests` (sin scope no pinta). |
| La grilla del mes dentro de una página que scrollea realiza todas las filas (un mes con cientos de movimientos). | `MaxHeight` medido; `Grilla_ConSeiscientosMovimientos_NoRealizaTodasLasFilas` (Task 8), mutación (t). |
| 10 columnas no entran a 1280/1366 y una medida se corta. | Medición en la Task 6, `ColumnaAuto` (convención 19), scroll horizontal (precedente Historial), `GrillasSinRecorteBarridoTests`. |
| El modo año hace una consulta extra (enero) que falla distinto del anual. | Las dos van en la misma carga protegida y el mismo turno; `Anual_403EnLaConsultaDeEnero_MarcaSinPermiso` (Task 3). |
| La verificación orgánica ensucia la base local. | Backup antes; prefijo `PRUEBA-4C`; ids anotados; limpieza por el dominio; borrado físico solo con OK del usuario y SQL acotado; nunca el VPS. |
| WSLg se congela o `capturar.sh` devuelve el escritorio de Windows con exit 0. | md5 de capturas, mirar cada una, recupero de foco; si se congela, PARAR y pedir el reinicio al usuario. |
| Un test heredado se borra sin reemplazo. | Convención 13: `GuardianDePatronTests` L66/L114 con su reemplazo nombrado; `LibroCajaViewTests`, `SignoNegativoBadgeTests` y el anual de `LibroCajaViewModelTests` se adaptan sin borrar; el renombrado se anota. |
