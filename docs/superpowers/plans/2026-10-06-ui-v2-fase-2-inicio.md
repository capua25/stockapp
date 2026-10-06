# UI v2 — Fase 2 Inicio y deuda menor de la Fase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar `InicioView` al sistema v2 (saludo y fecha, 3 `TarjetaMetrica` con sus gates y estado sin dato, panel de tareas con `Insignia`, 6 accesos rápidos con ícono, avisos de backup como `Alerta`, logo del municipio de fondo), comparada contra su mockup aprobado a 1280×760 y 1366×768, y cerrar los 8 ítems de la deuda menor de la Fase 1 que no dependen de Importación.

**Architecture:** Primero se cierra la deuda de herramienta y de guardianes que no necesita mockups (guardián de recorte, capturador fail-closed, alto de línea del título, opacidades, custodia del sidebar, cosméticos) y se agregan los dos componentes que Inicio necesita (`GrillaUniformeV2`, `V2TemaFilaPanel`). Después se escriben los mockups de Inicio (4 estados) y los de la deuda que cambia pantallas ya aprobadas (Reset con alertas, pulgar del sidebar), y el usuario los aprueba. Recién ahí se toca el ViewModel de Inicio (cambios mínimos: KPIs, fecha con reloj inyectable, notificación de gates), se reescribe `InicioView.axaml` en su mismo archivo y se comparan las pantallas. Las pantallas de contenido se comparan dentro del shell real con el sidebar oculto en ambos renders (el sidebar ya tiene su comparación).

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, Optris.Icons.Avalonia 12.0.7 (prefijo `mdi`), Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Domain.Tests`, `StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`). Python 3 con Pillow (solo para medir PNG en pasos de diagnóstico, nunca commiteado).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§2, §3.3, §5.3 Inicio, §6 fidelidad y §6.5 calibración, §7 fila "2 Inicio" y "Deuda menor de la Fase 1", §8.3, §9, §10). Herramienta: `docs/ui-v2/herramientas.md`. Plan de la Fase 1 (formato y convenciones que este plan hereda): `docs/superpowers/plans/2026-10-01-ui-v2-fase-1-shell-y-acceso.md`.

Todos los comandos se corren desde la raíz del worktree: `/home/capua25/workspace/stockapp-worktrees/ui-v2` (rama `feat/ui-v2`). Ese worktree tiene su propio índice `.codegraph/`: usarlo (CodeGraph MCP `codegraph_explore`) antes de grep/Read para todo lo que sea código; nunca el índice del repo principal.

## Decisiones del usuario (2026-10-06)

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | **Alcance de la Fase 2:** `InicioView` completa (§5.3) + la deuda menor de la Fase 1. | Bloques A a D. | todas |
| U2 | **Inicio MANTIENE el logo del municipio de fondo**, con el mismo tratamiento que se aprobó en el Login v2 (P5 de la Fase 1): `Image` `carmelo-original.png`, `Stretch="Uniform"`, centrada, `Opacity` 0.28 por `Style Selector="Image.marca-agua"` (Priority=Style), `RenderTransform="scale(0.85)"`, detrás del contenido. | Igual que el Login: se excluye de la comparación de píxeles en ambos renders y la custodia un test aparte (`InicioMarcaAguaTests`, Task 11). Los mockups la incluyen. | 8, 11, 14 |
| U3 | **Toda la deuda menor de la Fase 1 entra en esta fase, salvo Importación.** | Un task (o un grupo que un revisor no rechazaría por separado) por ítem, con guardián y mutación donde aplica. Tabla "Dónde quedó cada ítem de la deuda menor". | 1–6, 9, 12, 13 |
| U4 | **Importación NO se toca (corrección del usuario, 2026-10-06):** queda apagada. El ítem "Sin guardián propio de `EsAdmin` en Importación" sale del plan: ninguna task, ningún test que fuerce `ImportacionHabilitada`, ningún cambio en código de Importación. | Listado en "Fuera de alcance" como deuda que sigue ABIERTA mientras Importación esté apagada. En la spec queda marcado así (Task 14). | 14 |
| U5 | **Al terminar:** se actualiza la sección Estado de la spec y la deuda menor queda cerrada/marcada. | Task 14, Step 6. | 14 |

## Decisiones que se confirman al aprobar los mockups (Tasks 8 y 9)

No bloquean el arranque: son decisiones del plan que la spec no fija y que el usuario resuelve mirando el mockup. Si cambia alguna, se ajusta el mockup Y lo que corresponda (componente, `base.css`, ViewModel) en la misma task del mockup, antes del XAML.

- **D1 — Disposición apilada, no en dos columnas.** `patrones-1.html` pone el panel de tareas y los accesos rápidos lado a lado, con los accesos en 2 columnas. Medido sobre el layout v2: a 1280 la columna derecha deja 219 px por tarjeta y 161 px para el título, y "Valorización de inventario" (13/600, ~175 px) no entra (`TarjetaAcceso.PART_Titulo` no envuelve; el guardián de recorte lo rechazaría). El plan apila: encabezado, fila de métricas, alertas, panel de tareas a ancho completo y accesos rápidos en 3 columnas a ancho completo.
- **D2 — Textos reales.** El título es el saludo real (`¡Bienvenido, María Pérez!`), no el "Buenos días, María" del mockup de referencia. El subtítulo junta fecha y rol: `martes 6 de octubre de 2026 · Sesión iniciada como Administrador` (hoy "Sesión iniciada como {0}" va solo). Etiquetas de métricas: "Facturas a vencer en 7 días" (texto de la spec §5.3), "Tareas vencidas", "Último backup".
- **D3 — "Ver calendario de pagos"** pasa al slot de acciones del `EncabezadoPagina`, como botón `secundario`, visible con `VerFinanzas` o Admin (hoy está dentro de la card de vencimientos y solo aparece si hay vencimientos).
- **D4 — Facturas vencidas.** La métrica muestra las a vencer en 7 días; las vencidas pasan a una `Alerta` de advertencia con el texto real del VM (`1 factura vencida` / `N facturas vencidas`). El texto "N facturas por vencer esta semana" deja de mostrarse: lo reemplaza la métrica.
- **D5 — Panel de tareas.** Sin los subtítulos "VENCIDAS (n)" / "PRÓXIMAS A VENCER (n)": la `Insignia` de cada fila (Vencida = peligro, Próxima = advertencia) los reemplaza. Se conserva el texto real de días (`-2 días`, `-1 día`, `hoy`, `3 días`); se quita la flecha "→" de cada fila. Filas separadas por una línea `#F0EFED` arriba de cada una (componente `V2TemaFilaPanel`). El enlace "Ver todas las tareas →" queda abajo a la derecha.
- **D6 — Métrica sin dato.** Si el servicio falla, la métrica muestra "—" en texto grupo (estado sin dato de `TarjetaMetrica`), nunca un "0" que afirme algo falso. "Último backup" muestra fecha y hora (`04/10/2026 14:32`); sin ningún backup registrado, "—".
- **D7 — Márgenes y separaciones.** Padding de página 24 arriba/abajo y 28 a los costados (`patrones-1.html`), 16 entre bloques, 12 entre métricas y entre accesos.
- **D8 — Íconos de los accesos rápidos:** los mismos del sidebar (`mdi-package-variant`, `mdi-tray-arrow-down`, `mdi-tray-arrow-up`, `mdi-history`, `mdi-currency-usd`, `mdi-shield-search`).
- **D9 — Pulgar de scroll del sidebar del Administrador** (Task 9): el mockup lo replica con el color y la geometría medidos en el render (`#7A7A7A`, 2 px), como pedía P4 de la Fase 1.

## Global Constraints

- Tema claro fijo, sin modo oscuro (spec §2, §10).
- Fondo app `#F7F7F5` · Superficie `#FFFFFF` · Borde `#E7E5E4` · Borde input `#D6D3D1` · Separador de grilla `#F0EFED` · Header de grilla `#FAFAF9`.
- Acento `#1D4ED8` · Acento hover `#1E40AF` · Acento suave `#DBEAFE` · Acento oscuro `#1E3A8A`.
- Texto primario `#1C1917` · secundario `#57534E` · terciario `#78716C` · etiqueta `#44403C` · grupo `#A8A29E`.
- Peligro texto `#B91C1C` · Borde error `#DC2626`.
- Insignias (fondo/texto): Neutro `#F5F5F4`/`#44403C` · Advertencia `#FEF3C7`/`#92400E` · Peligro `#FEE2E2`/`#991B1B` · Éxito `#DCFCE7`/`#14532D`.
- Alertas (fondo/borde/texto): Advertencia `#FEF3C7`/`#FDE68A`/`#92400E` · Error `#FEE2E2`/`#FCA5A5`/`#991B1B` · Info `#DBEAFE`/`#DBEAFE`/`#1E3A8A`.
- Radios: 8 (inputs, botones, pastilla activa, alertas, `TarjetaAcceso`, borde de la grilla), 10 (`TarjetaContenido` y `TarjetaMetrica`), 999 (insignias), 4 (casilla del `CheckBox`).
- Sombra de card: `0 1px 2px rgba(0,0,0,0.05)` — la única sombra del sistema.
- Alturas: input y botón 36; fila de grilla 40; header de grilla 36; ítem de sidebar 32.
- Espaciado base 4: todo margen, padding y gap de diseño es múltiplo de 4 (excepción: padding vertical 2 de la `Insignia`; compensaciones medidas listadas en spec §6.5).
- Anchos fijos: sidebar 240 (a 1366 deja 1126 a la vista de contenido; a 1280, 1040); card de formulario máx. 760; card de login 400.
- Tipografía: título de página 22/600 alto de línea 26 · título de sección 15/600 · valor de `TarjetaMetrica` 20/600 · cuerpo 14/400 · texto de controles 13 · etiqueta de campo 12/500 · insignia 12/500 padding 2×8 · header de sección del sidebar 11/600 MAYÚSCULAS letter-spacing 0.6.
- Inicio (§5.3): saludo y fecha; fila de 3 `TarjetaMetrica` cada una SOLO con su gate (Facturas a vencer en 7 días: `VerFinanzas` o Admin · Tareas vencidas: `GestionarTareas` o Admin · Último backup: solo Admin); panel "Tareas que requieren atención" con `Insignia` Vencida/Próxima; los 6 accesos rápidos con sus gates como `TarjetaAcceso` con ícono; avisos de backup como `Alerta` manteniendo la distinción entre salud desconocida (advertencia) y backup vencido (error).
- ViewModels intactos salvo lo mínimo que la spec permite para los KPIs de Inicio (§3). No cambian: `ViewLocator`, `INavigationService`, el wiring de `DataContextChanged` en code-behind (las vistas de Avalonia no se autoinicializan: `InicioView.axaml.cs` NO se toca), el seam `ErrorConverter`, la lógica de permisos. No se renombran ni se mueven vistas.
- Sin cambios de API (§10): los datos de los KPIs ya existen (`CalendarioPagosDto.AVencer7Dias`, `ITareaService.ListarAsync` + `PanelVencimientosTareas`, `IBackupsService.ObtenerSaludAsync`). Verificado: no hace falta ningún endpoint nuevo.
- `Classes="ui2"` en la RAÍZ de cada vista migrada y NUNCA en un contenedor que hospede vistas no migradas. `InicioView` no hospeda nada: lleva `ui2` en su raíz. Antes de tocar un estilo v2, verificar que la vista que lo usa esté dentro de `.ui2` (el fix `961dc7e` de la Fase 1 no tuvo efecto porque el contenido no estaba dentro de `.ui2`).
- Dentro de una vista v2, todo `TextBlock` lleva una clase tipográfica v2 o sus propiedades tipográficas como valor local desde recursos `V2*`.
- Componentes v2: `xmlns:v2="using:StockApp.Presentation.Controls.V2"`; tokens `V2*`; variables CSS `--v2-*`. Cada clase `v2-*` de `base.css` replica la geometría de su `ControlTheme`/estilo: si cambia uno, cambia el otro en el mismo commit.
- Mockups (§6.1): `docs/ui-v2/mockups/<modulo>/<pantalla>.html`, el mismo HTML a 1280×760 y a 1366×768, `../_base/tokens.css` + `../_base/base.css`, todo dentro de UN `<div class="v2-ventana">`, datos fijos idénticos a los del ViewModel del test. Solo colores sólidos, bordes de 1px, radios, una `BoxShadow`, Inter, px enteros. Prohibido: gradientes, blur, transparencias apiladas, transiciones, pseudo-elementos decorativos, `text-shadow`. Se aprueban por módulo, ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los valores calibrados (11 teselas densas, 1.68%) a AMBOS tamaños. Uno mayor solo con `UmbralAprobado` registrado tras revisar CADA tesela densa con el usuario; 16 o más exige además `MutacionPalidaEnRojo`. Nunca se sube para tapar un defecto: si no pasa, se diagnostica y se corrige geometría (o se reporta al usuario).
- Diferencia de tono aceptada (2026-09-30): el texto de la app sale 7-18% más claro que en Chromium. Lo que se exige idéntico es la geometría.
- **Un guardián visual mide la View REAL con el tema real.** Un test que arma su propio contexto (una grilla sintética envuelta en `ui2`) prueba la intención, no el efecto: los guardianes de esta fase sobre Inicio montan `InicioView` real (UiTests) o el shell real con Inicio adentro (VisualTests).
- **Gates de permisos: SIEMPRE con un usuario de permisos mixtos.** Admin cortocircuita el OR y no prueba nada (un test con Admin solo sirve de control). Un test de VM no custodia un gate de UI: el gate se prueba con la View real en `StockApp.Presentation.UiTests`.
- **Verificación por mutación QUITANDO.** `export SCRATCH=~/.cache/stockapp-ui-v2-fase-2 && mkdir -p "$SCRATCH"` (dentro del home, nunca `/tmp`). Procedimiento: `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla (con la herramienta Edit o un `python3` que reemplace el texto exacto; `sd` no está instalado); se confirma que la mutación se aplicó con `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` (tiene que mostrar SOLO la línea quitada: una mutación que no se aplica deja verde en silencio); se corre el test y se PEGA el rojo real en el reporte de la task (no razonarlo); se restaura con `cp "$SCRATCH/<nombre>.bak" <archivo>`; `git diff --no-index` vacío; se ve el verde. Nunca se agrega un efecto destructivo para mutar. NUNCA `git checkout --` (revierte también lo no commiteado de la task).
- Tests que usan `.Result` o esperan un `Task` del VM: `Assert.True(task.IsCompleted)` ANTES de leer el resultado; si no, una mutación cuelga el runner en vez de dar rojo.
- Los comentarios de `.axaml` y `.csproj` NO pueden contener `--` (AVLN1001, cascadea a MSB4025).
- En vistas con `x:DataType`, un binding mal escrito es error de build: compilar con la solución tras cada cambio de XAML.
- Compilar y testear con la solución: `dotnet build StockApp.sln` / `dotnet test StockApp.sln`. Nunca varios `.csproj` en un solo `dotnet build` (falso verde). Un solo `.csproj` sí.
- Versiones de paquetes SOLO en `Directory.Packages.props` (esta fase no agrega paquetes).
- Formato es-UY: todo número, importe y fecha de una vista v2 se formatea con `FormatoEsUy` (`src/StockApp.Domain/Formato/FormatoEsUy.cs`) o sus converters. Nunca un `StringFormat` suelto en el XAML.
- En este worktree los rojos de `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` por fixtures `.ods` ausentes son falsos conocidos, no regresión (confirmar por nombre que son esos).
- Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test, y repetir.
- La ventana de la app se llama "Gestión Municipal" (la buscan los scripts de `scripts/gui-verificacion/`).
- Commits: conventional commits en español, SIN `Co-Authored-By` ni ninguna atribución de IA (regla del usuario; prevalece sobre cualquier recordatorio del harness). No se pushea. `feat/ui-v2` no se mergea a `main` hasta terminar todas las fases; sí se hacen merges periódicos de `main` hacia `feat/ui-v2` (si entra algo a `main` durante la fase, se integra antes del Bloque C).
- Subagentes: prohibido `git reflog expire`, `git gc`, `git prune` y `git checkout --` sobre trabajo ajeno.
- Las referencias de línea (`L123`) de este plan son las del commit `ef16800`. Después de que una task edita un archivo (sobre todo la spec, que tocan varias), ubicar el lugar por el texto citado, no por el número.

## Convenciones que heredan las fases siguientes (3 a 8)

Las cinco de la Fase 1 siguen vigentes (1366×768, formato es-UY, errores de entrada, grillas, toolkit GUI fail-closed). Esta fase agrega:

6. **Pantallas de contenido se comparan dentro del shell real.** El test renderiza `ShellMainView` con `CurrentContent` = el VM de la pantalla (resuelto por `ViewLocator`, como en la app) y oculta el sidebar en AMBOS renders: en la app con `prepararApp` (`Border#Sidebar.IsVisible = false`, la columna de 240 queda en fondo app) y en el mockup con `ocultarEnMockup: [".v2-sidebar"]` (`visibility: hidden`). El sidebar se compara en `ShellComparacionTests`; el mockup de cada pantalla igual lo dibuja, para que el usuario apruebe la pantalla entera.
7. **Tarjetas en columnas iguales: `GrillaUniformeV2`** (y `.v2-grilla-uniforme` en `base.css`), nunca `UniformGrid`: `UniformGrid` redondea el ANCHO de la celda y acumula el error (a 1366 la tercera tarjeta se corre 1 px y se pasa 1 px del borde); `GrillaUniformeV2` redondea cada borde por separado, como el pixel snapping de Chromium, y salta los hijos invisibles (gates).
8. **Fechas en mockups con reloj inyectable.** Un VM que muestra "hoy" recibe un `TimeProvider` opcional (default `TimeProvider.System`); los datos fijos de los tests le pasan un reloj fijo con zona UTC, así la pantalla no cambia con el día ni con la zona de la máquina.
9. **Fila clickeable de panel: `V2TemaFilaPanel`** (`Theme` de `Button`), con su espejo `.v2-fila-panel`.

## Review Focus

1. **Gate de una métrica probado solo con Admin, o solo en el VM.** Un Operador con `VerFinanzas` y sin `GestionarTareas` ve "Tareas vencidas" (o al revés), o un Operador sin ninguno ve la fila vacía. Esperado: cada `TarjetaMetrica` aparece si y solo si su gate se cumple, con la View real, y la fila entera se oculta si no queda ninguna. Tests: `InicioViewV2Tests.OperadorMixto_VeFacturasYTareasPeroNoElBackup`, `OperadorSoloConVerFinanzas_VeSoloFacturas`, `OperadorSoloConGestionarTareas_VeSoloTareas`, `OperadorSinFinanzasNiTareas_NoMuestraLaFilaDeMetricas` (Task 11) y la comparación `OperadorMixto_IgualAlMockup`.
2. **Métrica que dice "0" cuando no se pudo consultar.** La API cae y "Facturas a vencer" muestra 0: el usuario cree que no hay nada que pagar. Esperado: "—" (sin dato) hasta que la consulta termina bien, y "—" si falla. Tests: `InicioViewModelMetricasTests.AntesDeCargar_LasTresMetricasEstanSinDato`, `CalendarioFalla_FacturasSinDato`, `TareasFallan_TareasSinDato`, `BackupFalla_UltimoBackupSinDato` (Task 10) y la comparación `AdminSinDatos_IgualAlMockup` (Task 11).
3. **Fecha y hora que dependen del reloj o de la zona de la máquina.** El subtítulo, el último backup y los días de cada tarea salen distintos según el día o la PC. Esperado: los tres salen del mismo `TimeProvider` inyectado. Test: `InicioViewModelMetricasTests.ConRelojFijo_FechaDiasYBackupSalenDelReloj` (Task 10), con un reloj en 2030 para que el reloj real no pueda coincidir.
4. **Backup vencido y backup desconocido se ven igual al pasar a `Alerta`.** Esperado: vencido = `Alerta` Error; desconocido (la consulta falló) = `Alerta` Advertencia; al día = ninguna. Tests: `InicioViewTests.Montar_AdminConBackupVencido_MuestraLaAlertaDeError`, `Montar_ServicioDeBackupFalla_MuestraLaAlertaDeAdvertencia` (Task 11, adaptados) y las comparaciones `Admin_IgualAlMockup` / `AdminSinDatos_IgualAlMockup`.
5. **Revocación en caliente que deja una métrica a la vista.** Al refrescar permisos, `RefrescarPermisosAsync` solo notifica los gates de los accesos rápidos: `PuedeVerCalendarioPagos` y `PuedeVerTareas` no se re-evalúan y la métrica revocada sigue visible. Esperado: desaparece y la que queda ocupa la fila. Test: `InicioViewV2Tests.RevocacionEnCaliente_OcultaLaMetricaYLaOtraOcupaLaFila` (Task 11) más `InicioViewModelMetricasTests.RefrescarPermisos_NotificaLosGatesDeLasMetricas` (Task 10).

---

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia |
|---|---|---|
| Datos de los KPIs | Sin API nueva. Facturas: `CantidadAVencer7Dias` (ya existe, de `CalendarioPagosDto.AVencer7Dias`). Tareas: `CantidadTareasVencidas` (ya existe, de `PanelVencimientosTareas.Agrupar`). Backup: `SaludBackupDto.UltimoExitoEn` (ya se lee para `TextoAvisoBackup`). | `InicioViewModel.cs` L225-320; `FinanzasVistasDtos.cs` L68; `BackupDtos.cs` L15. Spec §10. |
| Cambios al VM | Nuevos: `Subtitulo`, `MostrarFilaMetricas`, `ValorFacturasAVencer` + `FacturasAVencerSinDato`, `HayFacturasVencidas`, `ValorTareasVencidas` + `TareasVencidasSinDato`, `ValorUltimoBackup` + `UltimoBackupSinDato`; parámetro opcional `TimeProvider? reloj = null`; `RefrescarPermisosAsync` notifica además `PuedeVerCalendarioPagos`, `PuedeVerTareas` y `MostrarFilaMetricas`. Nada se borra (los miembros que la vista v2 deja de usar se excluyen en `ReflexionVistaViewModelTests` y se borran en la Fase 9). | Spec §3: "KPIs de Inicio" es uno de los cambios mínimos permitidos. `InicioViewModel.RefrescarPermisosAsync` L329-339 solo notifica 5 gates. |
| Reloj | `TimeProvider` opcional al final del constructor: los ~20 `new InicioViewModel(6 args)` de los tests existentes siguen compilando y la DI (`services.AddTransient<InicioViewModel>()`, `App.axaml.cs` L358) usa el valor por defecto (MS.DI respeta los parámetros con default). Días de tareas con el overload testeable `PanelVencimientosTareas.Agrupar(..., ahoraUtc, zonaLocal)`; hora del backup con `_reloj.LocalTimeZone` (con `TimeProvider.System` es `TimeZoneInfo.Local`, idéntico al `FechaUtcALocalConverter` actual). | `PanelVencimientosTareas.cs` L25-36 (overload de 5 argumentos ya existe). `FechaUtcALocalConverter.cs` L30-33. |
| Fechas en texto | `FormatoEsUy.FechaLarga(DateOnly)` ("martes 6 de octubre de 2026", nombres en castellano escritos a mano, sin depender de ICU) y `FormatoEsUy.FechaHora(DateTime)` ("04/10/2026 14:32", invariante). | `FormatoEsUy` ya es "único punto de verdad" del formato y no depende de ICU (la Api corre con `InvariantGlobalization`). Convención 2 de la Fase 1. |
| Comparación de Inicio | Shell real + `CurrentContent` = `InicioViewModel` (lo resuelve `ViewLocator`, registrado en `VisualTestApp`), sidebar y marca de agua ocultos en ambos renders con un hook nuevo `prepararApp` (corre después del primer layout, antes de capturar). | `VisualTestApp.cs` L53 (`DataTemplates.Add(new ViewLocator())`); `ShellMainView.axaml` L17 (`Border x:Name="Sidebar"`), L105 (`ContentControl` sin `ui2`); `RenderizadorVista.RenderizarAPng` L100-143 (hoy no hay hook previo a la captura). |
| Columnas de tarjetas | `GrillaUniformeV2` propio en vez de `UniformGrid`. | Decompilado de `Avalonia.Controls.Primitives.UniformGrid` 12.0.5 (`ilspycmd`): `ArrangeOverride` redondea el ancho de celda (`RoundLayoutValue(num3)`) y ubica la celda i en `i * (num3 + columnSpacing)`. A 1366 la fila de métricas tiene 1070 de ancho (1126 menos 2×28): celda 348,67 redondeada a 349, celdas en 0–349, 361–710 y 722–1071 (se pasa 1 px del borde). Chromium redondea cada borde: 0–349, 361–709, 721–1070. |
| Fila de tareas | `ControlTheme x:Key="V2TemaFilaPanel" TargetType="Button"` con plantilla propia; el fondo se pinta en una parte (`Border#PART_Fila`) con estilos anidados y el presentador NO se llama `PART_ContentPresenter`. | `Themes/Controls.axaml` L20-39: el estilo global `Button` setea `Background`, `Padding` y `:pointerover`; `ComponentesV2.axaml` L115-130 (`V2TemaEnlace`) ya documenta el mismo problema. |
| Marca de agua de Inicio | Misma estrategia que el Login: excluida de la comparación de píxeles (en la app `Image#MarcaAgua.IsVisible = false`; en el mockup `.marca-agua`) y custodiada por `InicioMarcaAguaTests` (fuente 2804×1448, opacidad 0.28, escala 0.85, `Uniform`, centrada en el ÁREA DE CONTENIDO, detrás del `ScrollViewer`, diferencia solo dentro de su región, delta ≤ 72). | `LoginMarcaAguaTests.cs`; plan Fase 1 "Estrategia de comparación de la marca de agua (P5)". |
| `GuardianDePatronTests` | `InicioView` sale de `VistasDeLaTanda` y de la fila `[InlineData(typeof(InicioView), null, "INICIO")]` y entra a `VistasMigradasAV2`. | `GuardianDePatronTests.cs` L49, L109, L216-224. |
| Tests existentes de Inicio | `InicioViewTests` e `InicioPanelTareasTests` se adaptan (no se borran): los `Border` con nombre pasan a `Alerta`/`TarjetaContenido` con nombres nuevos; el test de wrap del título de "Valorización de inventario" se reemplaza por uno de textos/íconos, porque el recorte lo custodia el guardián de cada comparación a los dos tamaños. | `InicioViewTests.cs` L104-105, L164-165, L255-285; `InicioPanelTareasTests.cs` L127-153. |

## Mapa de archivos

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/Controls/V2/GrillaUniformeV2.cs` | Panel de N columnas iguales con bordes redondeados como Chromium; salta hijos invisibles. |
| `tests/StockApp.Presentation.UiTests/V2/GrillaUniformeV2Tests.cs` | Geometría exacta de la grilla. |
| `tests/StockApp.Presentation.UiTests/V2/FilaPanelV2Tests.cs` | `V2TemaFilaPanel`: separador, padding, hover, sin fuga del estilo global de `Button`. |
| `tests/StockApp.Presentation.UiTests/V2/AltoDeLineaV2Tests.cs` | Alto de línea del título de página en el control real + helper tolerante al escalado. |
| `docs/ui-v2/mockups/inicio/inicio-admin.html`, `inicio-operador.html`, `inicio-operador-sin-finanzas.html`, `inicio-admin-sin-datos.html` | Mockups de Inicio (4 estados). |
| `docs/ui-v2/mockups/acceso/reset-completado.html`, `reset-error.html` | Reset con sus dos alertas (deuda: paridad del margen). |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inicio/DatosInicio.cs` | VM fijos de los mockups de Inicio (reloj fijo, servicios con Moq). |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inicio/InicioComparacionTests.cs` | 4 estados × 2 tamaños. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inicio/InicioMarcaAguaTests.cs` | Custodia del logo de fondo de Inicio. |
| `tests/StockApp.Presentation.VisualTests/Comparacion/DiferenciaDePixeles.cs` | Medición compartida por las dos custodias de marca de agua. |
| `tests/StockApp.Presentation.UiTests/V2/InicioViewV2Tests.cs` | `ui2`, gates de métricas con usuarios mixtos, revocación en caliente, botón de calendario, accesos con AND. |
| `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelMetricasTests.cs` | KPIs, sin dato, reloj, notificación de gates. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs` + `GuardianTextoRecortadoTests.cs` | TextBox multilínea relaja el eje vertical; cobertura de "la ventana" desde el presentador de filas. |
| `tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs` + `tests/StockApp.UiV2.Herramientas.Tests/CapturadorMockupTests.cs` | `.v2-ventana` fail-closed. |
| `tests/StockApp.Presentation.UiTests/V2/MontajeV2.cs`, `InsigniaAlertaTests.cs` | `EsMultiploDeLinea` reemplaza el `% 16`. |
| `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` | Opacidad custodiada en vistas v2 y diálogos; Inicio pasa a `VistasMigradasAV2`. |
| `src/StockApp.Presentation/ViewModels/GrupoNavegacion.cs`, `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml`, `docs/ui-v2/mockups/acceso/reset.html` | Cosméticos. |
| `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` | `V2TemaFilaPanel`. |
| `docs/ui-v2/mockups/_base/base.css` | `.v2-grilla-uniforme`, `.v2-fila-panel`. |
| `docs/ui-v2/mockups/shell/sidebar-admin.html` | Pulgar de scroll replicado. |
| `src/StockApp.Domain/Formato/FormatoEsUy.cs` + `tests/StockApp.Domain.Tests/Formato/FormatoEsUyTests.cs` | `FechaLarga`, `FechaHora`. |
| `src/StockApp.Presentation/ViewModels/InicioViewModel.cs` | KPIs, reloj, notificaciones. |
| `src/StockApp.Presentation/Views/InicioView.axaml` | Reescritura v2 (code-behind sin cambios). |
| `tests/StockApp.Presentation.VisualTests/Comparacion/RenderizadorVista.cs`, `ComparacionVisual.cs`, `RenderizadorVistaTests.cs` | Hook `preparar`/`prepararApp`. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Shell/DatosShell.cs` | `Crear` público con contenido. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/DatosAcceso.cs`, `AccesoComparacionTests.cs`, `LoginMarcaAguaTests.cs` | Reset con alertas; Login usa `DiferenciaDePixeles`. |
| `src/StockApp.Presentation/Views/ResetAdminView.axaml` | Solo si la medición de la Task 12 lo pide (compensación por estado). |
| `tests/StockApp.Presentation.UiTests/InicioViewTests.cs`, `InicioPanelTareasTests.cs`, `ReflexionVistaViewModelTests.cs` | Adaptación a la vista v2. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` | §6.5 (compensaciones y guardián), deuda cerrada, Estado. |
| `docs/ui-v2/herramientas.md` | Guardián, capturador, `prepararApp`, convenciones 6-9. |

**Comandos de verificación:**

- Suite completa: `dotnet test StockApp.sln`
- Dominio: `dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Un test: agregar `--filter "FullyQualifiedName~NombreDelTest"`
- Comparación con página: `scripts/ui-v2/comparar.sh <filtro>` y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger de la fase: `.superpowers/sdd/2026-10-06-ui-v2-fase-2-inicio/progress.md` (crearlo en la primera task que lo necesite; `.superpowers/sdd/` está en `.gitignore`: es local, no se commitea).

---

## Bloque A — Deuda de herramienta y guardianes, y componentes de Inicio (antes de cualquier mockup)

### Task 1: Guardián de recorte: TextBox multilínea y cobertura de "la ventana" desde el presentador de filas

Deuda (spec L368): "cobertura 'la ventana' del presentador de filas de DataGrid sin cubrir; TextBox multilínea de altura fija con valor largo se reporta aunque scrollee".

**Files:**
- Modify: `tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs` (`EjesDesplazables`, L178-199; `<summary>` L14-35)
- Modify: `tests/StockApp.Presentation.VisualTests/GuardianTextoRecortadoTests.cs` (al final de la clase)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§6.5, párrafo "Cubre también el valor de `TextBox`…", L315)
- Modify: `docs/ui-v2/herramientas.md` (sección "Test por pantalla", párrafo del guardián)

**Interfaces:**
- Consumes: `GuardianTextoRecortado.Buscar(Visual raiz, IReadOnlyCollection<string>? recortesPermitidos = null)`, `RenderizadorVista.RenderizarAPng(...)`, helpers `Renderizar`, `Columna`, `Fila`, `Largo` de `GuardianTextoRecortadoTests`.
- Produces: la misma firma de `Buscar`; ahora el `ScrollViewer` interno de un `TextBox` multilínea (`AcceptsReturn` o `TextWrapping != NoWrap`) relaja el eje vertical (no el horizontal).

- [ ] **Step 1: Tests (rojo)**

Agregar al final de `GuardianTextoRecortadoTests` (antes de la `}` de la clase):

```csharp
    [AvaloniaFact]
    public void TextBoxMultilineaDeAltoFijoConValorLargo_NoSeReporta()
    {
        // Deuda de la Fase 1: un TextBox multilínea SÍ se lee scrolleando en vertical (motivo de anulación,
        // token del Reset). Su ScrollViewer relaja el eje vertical; el valor que no entra no es un recorte.
        var valor = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"Línea {i} del motivo"));

        Assert.Empty(Renderizar(new TextBox
        {
            Width = 240, Height = 88, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = valor,
        }));
    }

    [AvaloniaFact]
    public void TextBoxMultilineaSinWrapConUnaLineaMasAnchaQueElControl_SeReporta()
    {
        // El eje horizontal NO se relaja: sin wrap, la línea larga queda cortada a la derecha y nadie la lee.
        var recortes = Renderizar(new TextBox
        {
            Width = 120, Height = 88, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Text = Largo,
        });

        Assert.Contains(recortes, r => r.Contains(Largo));
    }

    [AvaloniaFact]
    public void TextBoxMultilineaQueSeSaleDeLaVentana_SeReporta()
    {
        // Relajar el eje vertical no exime a la caja del TextBox: si ella misma cae fuera de la ventana, se reporta.
        var valor = string.Join("\n", Enumerable.Range(1, 80).Select(i => $"Línea {i}"));
        var recortes = Renderizar(new TextBox
        {
            Width = 240, Height = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = valor,
        });

        Assert.Contains(recortes, r => r.Contains("la ventana"));
    }

    [AvaloniaFact]
    public void GrillaQueSeSaleDeLaVentanaSinAncestrosQueRecorten_LaReportaLaVentana()
    {
        // Deuda de la Fase 1: GrillaMasAltaQueLaVentanaSinScrollViewer_SeReporta la reporta el UserControl
        // raíz (ClipToBounds), así que el último chequeo, contra la ventana, nunca corría desde el
        // presentador de filas. Con una raíz que no recorta (un Panel) lo decide ese chequeo.
        var grilla = new DataGrid
        {
            Height = 1000,
            Width = 300,
            AutoGenerateColumns = false,
            Columns = { Columna(150) },
            ItemsSource = Enumerable.Range(0, 40).Select(_ => new Fila("A-001")).ToList(),
        };
        var vista = new StackPanel { Classes = { "ui2" }, HorizontalAlignment = HorizontalAlignment.Left, Children = { grilla } };
        IReadOnlyList<string> recortes = [];
        RenderizadorVista.RenderizarAPng(vista, null,
            Path.Combine(Directory.CreateTempSubdirectory("guardian-ventana-").FullName, "vista.png"),
            v => recortes = GuardianTextoRecortado.Buscar(v));

        Assert.NotEmpty(recortes);
        Assert.All(recortes, r => Assert.EndsWith("queda fuera de la ventana", r));
    }
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~GuardianTextoRecortadoTests"`
Expected: FAIL en `TextBoxMultilineaDeAltoFijoConValorLargo_NoSeReporta` (hoy el `ScrollViewer` del `TextBox` no relaja nada y reporta `valor "Línea 1…" queda fuera de ScrollContentPresenter`) y en `TextBoxMultilineaQueSeSaleDeLaVentana_SeReporta` (hoy lo reporta el `ScrollContentPresenter`, no la ventana). `TextBoxMultilineaSinWrap…` y `GrillaQueSeSaleDeLaVentana…` PASAN ya: son la red de lo que el cambio NO puede romper. Si `GrillaQueSeSaleDeLaVentana…` sale rojo porque lo reporta otro ancestro (por ejemplo una parte de la plantilla de `Window`), ese ancestro recorta de verdad: anotar cuál en el reporte, cambiar la aserción a `Assert.Contains("queda fuera de <ese ancestro>", …)` y avisar en el reporte que la rama "la ventana" queda probada solo por `ScrollViewerQueSeSaleDeLaVentana_SeReporta` y `TextBoxMultilineaQueSeSaleDeLaVentana_SeReporta`.

- [ ] **Step 2: Implementar**

En `GuardianTextoRecortado.cs`, reemplazar el primer `if` de `EjesDesplazables` (L181-187) por:

```csharp
        if (ancestro is ScrollContentPresenter { TemplatedParent: ScrollViewer scroll })
        {
            if (scroll.TemplatedParent is TextBox caja)
            {
                // Un TextBox de una línea sigue al cursor: nadie lo scrollea para leer el valor (Fase 1,
                // Task 1). Uno multilínea sí se lee scrolleando en vertical (deuda de la Fase 1, Fase 2
                // Task 1); en horizontal sigue sin relajarse.
                var multilinea = caja.AcceptsReturn || caja.TextWrapping != TextWrapping.NoWrap;
                return (false, multilinea && scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled);
            }

            return (scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled,
                    scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled);
        }
```

En el `<summary>` de la clase, reemplazar el tercer `<item>` por:

```csharp
/// <item>Valor de un TextBox: el TextPresenter mide su TextLayout; el ScrollViewer interno de un TextBox
/// de una línea no relaja ejes; el de uno multilínea (AcceptsReturn o con wrap) relaja solo el vertical.</item>
```

- [ ] **Step 3: Verde**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
Expected: PASS de toda la suite visual (incluidas las comparaciones de la Fase 1: el Reset tiene un `TextBox` multilínea de 88 con placeholder, que entra).

- [ ] **Step 4: Mutación quitando (dos reglas)**

```bash
cp tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs "$SCRATCH/GuardianTextoRecortado.cs.bak"
```

(a) Quitar el bloque `if (scroll.TemplatedParent is TextBox caja) { … }` entero: el `ScrollViewer` de un `TextBox` vuelve a relajar ejes como cualquier otro, sin distinguir una línea de varias. `git diff --no-index "$SCRATCH/GuardianTextoRecortado.cs.bak" tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs` muestra solo ese bloque. Correr el filtro `GuardianTextoRecortadoTests`: rojo en `TextBoxMultilineaSinWrapConUnaLineaMasAnchaQueElControl_SeReporta` y en `TextBoxConValorQueNoEntra_SeReporta` (el eje horizontal se relaja y el valor cortado deja de reportarse). Restaurar con `cp`.
(b) Quitar el chequeo final contra la ventana en `BuscarRecortador`: la línea `return Contiene(new Rect(raizVisual.ClientSize), rect, true, true) ? null : "la ventana";` pasa a `return null;`. `git diff --no-index`. Rojo en `GrillaQueSeSaleDeLaVentanaSinAncestrosQueRecorten_LaReportaLaVentana`, `TextBoxMultilineaQueSeSaleDeLaVentana_SeReporta` y `ScrollViewerQueSeSaleDeLaVentana_SeReporta`. Restaurar con `cp`.

El otro lado de la regla (que el vertical SÍ se relaje para un multilínea) lo custodia `TextBoxMultilineaDeAltoFijoConValorLargo_NoSeReporta`, que estaba rojo en el Step 1 antes de implementar. Pegar los rojos de (a) y (b). `git diff --no-index` vacío tras cada restauración; filtro en verde al final.

- [ ] **Step 5: Documentación**

Spec §6.5, al final del párrafo "Cubre también el valor de `TextBox`…" (L315), agregar: "Un `TextBox` multilínea (`AcceptsReturn` o con wrap) relaja solo el eje vertical: se lee scrolleando; el horizontal no (Fase 2, Task 1). Desde el presentador de filas del `DataGrid` el último chequeo es la ventana: probado con una raíz que no recorta (Fase 2, Task 1)."

`docs/ui-v2/herramientas.md`, sección "Test por pantalla", al final del párrafo del guardián: "Un `TextBox` multilínea relaja el eje vertical (se lee scrolleando); ya no hace falta `recortesPermitidos` para un motivo largo en un campo de alto fijo."

- [ ] **Step 6: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs tests/StockApp.Presentation.VisualTests/GuardianTextoRecortadoTests.cs docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md docs/ui-v2/herramientas.md
git commit -m "test(ui-v2): el guardián de recorte deja leer un TextBox multilínea y cubre la ventana desde la grilla"
```

### Task 2: Capturador: `.v2-ventana` fail-closed

Deuda (spec L368): "`.v2-ventana` sin chequeo fail-closed de que mida el lienzo". Hoy `ScriptDesbordes` hace `if (ventana && …)`: un mockup sin `.v2-ventana`, con dos, o con una que no mide el lienzo (un `width` pisado, un margen) se captura igual.

**Files:**
- Modify: `tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs` (`ScriptDesbordes` L52-65, la llamada L123, `<summary>` L13-18)
- Modify: `tests/StockApp.UiV2.Herramientas.Tests/CapturadorMockupTests.cs`
- Modify: `docs/ui-v2/herramientas.md` (sección "Mockup de una pantalla")

**Interfaces:**
- Consumes: `CapturadorMockup.CapturarAsync(string rutaHtml, string rutaPng, TamanoLienzo? tamano = null, IReadOnlyCollection<string>? ocultarSelectores = null)`.
- Produces: misma firma; ahora lanza `InvalidOperationException` si no hay exactamente un `.v2-ventana` o si no mide el lienzo pedido en el origen.

- [ ] **Step 1: Tests (rojo)**

En `CapturadorMockupTests`, agregar:

```csharp
    [Fact]
    public async Task SinVentana_Lanza()
    {
        var ruta = Path.Combine(_carpeta, "sin-ventana.html");
        File.WriteAllText(ruta,
            "<!doctype html><html><head><meta charset=\"utf-8\">"
            + $"<link rel=\"stylesheet\" href=\"{Uri(RutasRepo.TokensCss)}\">"
            + $"<link rel=\"stylesheet\" href=\"{Uri(Path.Combine(RutasRepo.CarpetaBaseMockups, "base.css"))}\">"
            + "</head><body><p>Sin ventana</p></body></html>");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(ruta, Path.Combine(_carpeta, "x.png")));

        Assert.Contains("hay 0 .v2-ventana", ex.Message);
    }

    [Fact]
    public async Task DosVentanas_Lanza()
    {
        var html = Html("<div class=\"v2-ventana\"></div>");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains("hay 2 .v2-ventana", ex.Message);
    }

    [Fact]
    public async Task VentanaMasAngostaQueElLienzo_Lanza()
    {
        var html = Html("<p>Texto</p>", ".v2-ventana{width:1000px}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains(".v2-ventana mide 1000x760 en (0,0), se esperaba 1280x760 en (0,0)", ex.Message);
    }

    [Fact]
    public async Task VentanaCorridaDelOrigen_Lanza()
    {
        var html = Html("<p>Texto</p>", ".v2-ventana{margin-left:8px;width:1272px}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains(".v2-ventana mide 1272x760 en (8,0)", ex.Message);
    }

    [Fact]
    public async Task VentanaAlTamanoMunicipio_NoLanza()
    {
        var html = Html("<p>Texto</p>");
        var png = Path.Combine(_carpeta, "municipio.png");

        await CapturadorMockup.CapturarAsync(html, png, TamanoLienzo.Municipio);

        Assert.True(File.Exists(png));
    }
```

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~CapturadorMockupTests"`
Expected: FAIL en los cuatro `…_Lanza` nuevos (capturan sin error); `VentanaAlTamanoMunicipio_NoLanza` pasa.

- [ ] **Step 2: Implementar**

En `CapturadorMockup.cs`, reemplazar `ScriptDesbordes` por:

```csharp
    private const string ScriptDesbordes = """
        (lienzo) => {
          const problemas = [];
          const raiz = document.documentElement;
          if (raiz.scrollWidth > raiz.clientWidth || raiz.scrollHeight > raiz.clientHeight) {
            problemas.push(`documento ${raiz.scrollWidth}x${raiz.scrollHeight} > ${raiz.clientWidth}x${raiz.clientHeight}`);
          }
          const ventanas = document.querySelectorAll(".v2-ventana");
          if (ventanas.length !== 1) {
            problemas.push(`hay ${ventanas.length} .v2-ventana (tiene que haber exactamente una)`);
            return problemas;
          }
          const ventana = ventanas[0];
          const caja = ventana.getBoundingClientRect();
          if (caja.x !== 0 || caja.y !== 0 || caja.width !== lienzo.ancho || caja.height !== lienzo.alto) {
            problemas.push(`.v2-ventana mide ${caja.width}x${caja.height} en (${caja.x},${caja.y}), se esperaba ${lienzo.ancho}x${lienzo.alto} en (0,0)`);
          }
          if (ventana.scrollWidth > ventana.clientWidth || ventana.scrollHeight > ventana.clientHeight) {
            problemas.push(`.v2-ventana ${ventana.scrollWidth}x${ventana.scrollHeight} > ${ventana.clientWidth}x${ventana.clientHeight}`);
          }
          return problemas;
        }
        """;
```

y la llamada (L123) por:

```csharp
            var problemas = await pagina.EvaluateAsync<string[]>(ScriptDesbordes, new { ancho = lienzo.Ancho, alto = lienzo.Alto });
```

En el `<summary>` de la clase, después de "…propaga el desborde al documento).", agregar: "Además exige exactamente un \".v2-ventana\" que mida el lienzo pedido en el origen: un mockup sin ella, con dos, o con un ancho o un margen pisado se rechaza en vez de capturarse corrido (deuda de la Fase 1, Fase 2 Task 2)."

- [ ] **Step 3: Verde y regresión de todos los mockups del repo**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "Categoria=ComparacionVisual"`
Expected: PASS. Todas las comparaciones de la Fase 0 y la Fase 1 capturan su mockup: cada uno tiene una sola `.v2-ventana` del tamaño del lienzo. Si alguno falla, es un mockup corrido de verdad: arreglar el mockup (no el chequeo) y anotarlo.

- [ ] **Step 4: Mutación quitando**

```bash
cp tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs "$SCRATCH/CapturadorMockup.cs.bak"
```

(a) Quitar el bloque `if (ventanas.length !== 1) { … return problemas; }`. Confirmar con `git diff --no-index`. Rojo en `SinVentana_Lanza` y `DosVentanas_Lanza` (sin ventana, `ventanas[0]` es `undefined` y el script lanza un TypeError de JS: el mensaje no contiene "hay 0"). Restaurar con `cp`.
(b) Quitar el bloque `if (caja.x !== 0 || … ) { … }`. Rojo en `VentanaMasAngostaQueElLienzo_Lanza` y `VentanaCorridaDelOrigen_Lanza`. Restaurar con `cp`.

Pegar los rojos. Verde al final.

- [ ] **Step 5: Documentación**

`docs/ui-v2/herramientas.md`, sección "Mockup de una pantalla", agregar: "La captura exige exactamente un `<div class=\"v2-ventana\">` que mida el lienzo pedido (1280×760 o 1366×768) en (0,0); si falta, si hay dos o si un estilo de la página le pisa el ancho o le pone margen, falla."

- [ ] **Step 6: Commit**

```bash
git add tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs tests/StockApp.UiV2.Herramientas.Tests/CapturadorMockupTests.cs docs/ui-v2/herramientas.md
git commit -m "feat(ui-v2): el capturador exige una sola .v2-ventana del tamaño del lienzo"
```

### Task 3: Alto de línea del título de página en el control real y múltiplos tolerantes al escalado

Deuda (spec L367): "`V2FuenteTituloPaginaAltoLinea` sin test de control real equivalente al de la Alerta; `% 16` sobre double puede dar falso rojo con escalado fraccional".

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/V2/MontajeV2.cs` (helper nuevo)
- Create: `tests/StockApp.Presentation.UiTests/V2/AltoDeLineaV2Tests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/V2/InsigniaAlertaTests.cs` (L92)

**Interfaces:**
- Consumes: `MontajeV2.Montar`, `Buscar<T>`, `Parte<T>`; `EncabezadoPagina.PART_Titulo`; estilo `.ui2 TextBlock.titulo-pagina` (`ControlesV2.axaml` L18-23).
- Produces: `MontajeV2.EsMultiploDeLinea(double alto, double altoLinea, double escala = 1.0)` — `true` si `alto` es un número entero ≥ 1 de líneas, con tolerancia de medio píxel de dispositivo (`0.5 / escala`).

- [ ] **Step 1: Tests (rojo por compilación)**

En `MontajeV2.cs`, agregar (dentro de la clase; `using Avalonia.Layout;` no hace falta):

```csharp
    /// <summary>
    /// ¿El alto es un número entero (al menos 1) de líneas? Tolerancia de medio píxel de dispositivo: el
    /// layout rounding redondea el alto total a píxeles de dispositivo, así que a 133 % dos líneas de 16
    /// miden 32,33 DIP. Un "alto % 16 == 0" sobre double daba falso rojo con escalado fraccional.
    /// </summary>
    public static bool EsMultiploDeLinea(double alto, double altoLinea, double escala = 1.0)
    {
        var lineas = Math.Round(alto / altoLinea);
        return lineas >= 1 && Math.Abs(alto - lineas * altoLinea) <= 0.5 / escala + 1e-6;
    }
```

Crear `tests/StockApp.Presentation.UiTests/V2/AltoDeLineaV2Tests.cs`:

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Deuda de la Fase 1 (spec §7): el alto de línea del título de página (token 26, el natural de Inter a 22
/// es 27) no tenía un test sobre un control real, como el que tiene la Alerta con su alto de 16.
/// </summary>
public class AltoDeLineaV2Tests
{
    [Theory]
    [InlineData(32.0, 16.0, 1.0, true)]
    [InlineData(15.79, 16.0, 1.33, true)]   // 133 %: 16 DIP son 21,28 px, redondeados a 21
    [InlineData(32.33, 16.0, 1.33, true)]   // 133 %: dos líneas, 42,56 px redondeados a 43
    [InlineData(27.0, 26.0, 1.0, false)]    // el alto natural de Inter a 22: un píxel de más es un defecto
    [InlineData(24.0, 16.0, 1.0, false)]    // línea y media
    [InlineData(0.0, 16.0, 1.0, false)]
    public void EsMultiploDeLinea_ToleraSoloElRedondeoDelEscalado(double alto, double altoLinea, double escala, bool esperado) =>
        Assert.Equal(esperado, EsMultiploDeLinea(alto, altoLinea, escala));

    [AvaloniaFact]
    public void EncabezadoPagina_TituloMideUnaLineaDelToken()
    {
        var encabezado = Buscar<EncabezadoPagina>(Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Productos" />"""), "E");
        var titulo = Parte<TextBlock>(encabezado, "PART_Titulo");

        Assert.Equal(26, titulo.LineHeight);
        Assert.True(EsMultiploDeLinea(titulo.Bounds.Height, 26, LayoutHelper.GetLayoutScale(titulo)),
            $"PART_Titulo mide {titulo.Bounds.Height}, no un múltiplo de 26.");
        Assert.Equal(1, Math.Round(titulo.Bounds.Height / 26));
    }

    [AvaloniaFact]
    public void TituloPaginaEnvuelto_CadaLineaMideElAltoDelToken()
    {
        var texto = Buscar<TextBlock>(Montar(
            """<TextBlock x:Name="T" Classes="titulo-pagina" Width="160" TextWrapping="Wrap" Text="¡Bienvenido, María Fernanda Rodríguez!" />"""),
            "T");

        Assert.Equal(26, texto.LineHeight);
        Assert.True(texto.Bounds.Height >= 52, $"El título no envolvió: alto {texto.Bounds.Height}.");
        Assert.True(EsMultiploDeLinea(texto.Bounds.Height, 26, LayoutHelper.GetLayoutScale(texto)),
            $"El título envuelto mide {texto.Bounds.Height}, no un múltiplo de 26.");
    }
}
```

En `InsigniaAlertaTests.cs` reemplazar L92 (`Assert.Equal(0, texto.Bounds.Height % 16);`) por:

```csharp
        Assert.True(EsMultiploDeLinea(texto.Bounds.Height, 16, LayoutHelper.GetLayoutScale(texto)),
            $"La alerta envuelta mide {texto.Bounds.Height}, no un múltiplo de 16.");
```

(`using Avalonia.Layout;` ya está en ese archivo.)

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~AltoDeLineaV2Tests|FullyQualifiedName~InsigniaAlertaTests"`
Expected: PASS (el token ya está aplicado; esta task agrega custodia, no comportamiento). El "rojo" de esta task es el de las mutaciones.

- [ ] **Step 2: Mutación quitando (tres reglas)**

```bash
cp src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml "$SCRATCH/ComponentesV2.axaml.bak"
cp src/StockApp.Presentation/Themes/V2/ControlesV2.axaml "$SCRATCH/ControlesV2.axaml.bak"
cp src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml "$SCRATCH/ComponentesV2-alerta.axaml.bak"
```

(a) En `ComponentesV2.axaml`, quitar `LineHeight="{DynamicResource V2FuenteTituloPaginaAltoLinea}"` de `PART_Titulo` (L170). `git diff --no-index`. Rojo en `EncabezadoPagina_TituloMideUnaLineaDelToken` (mide 27). Restaurar con `cp`.
(b) En `ControlesV2.axaml`, quitar `<Setter Property="LineHeight" Value="{DynamicResource V2FuenteTituloPaginaAltoLinea}" />` del estilo `.ui2 TextBlock.titulo-pagina` (L21). Rojo en `TituloPaginaEnvuelto_CadaLineaMideElAltoDelToken` (54 no es múltiplo de 26 con tolerancia 0,5). Restaurar con `cp`.
(c) En `ComponentesV2.axaml`, quitar `LineHeight="{DynamicResource V2FuenteControlAltoLinea}"` de `PART_Texto` de la `Alerta` (L67). Rojo en `Alerta_TextoEnvuelto_LasLineasVanACadaAltoDeLineaDeControl` (dos líneas naturales de 15 = 30). Restaurar con `cp`.

Si algún paso deja verde por `.axaml` stale: `dotnet build-server shutdown`, borrar `obj/` y `bin/` de `src/StockApp.Presentation` y de `tests/StockApp.Presentation.UiTests`, repetir. Pegar los tres rojos.

- [ ] **Step 3: Commit**

```bash
git add tests/StockApp.Presentation.UiTests/V2/MontajeV2.cs tests/StockApp.Presentation.UiTests/V2/AltoDeLineaV2Tests.cs tests/StockApp.Presentation.UiTests/V2/InsigniaAlertaTests.cs
git commit -m "test(ui-v2): el alto de línea del título de página se custodia en el control real"
```

### Task 4: Opacidad literal custodiada en las vistas v2

Deuda (spec L369): "Sin chequeo de `Opacity` literal en vistas migradas a v2". `Vista_NoTieneOpacidadesLiterales` corre sobre `VistasDeLaTanda` y `VistasEmbebidas`; cada vista que se migra sale de ahí a `VistasMigradasAV2` y pierde el chequeo. `ShellMainView` está en `VistasFueraDelGuardian` y los diálogos son `Window` (no se pueden montar con `PatronHelpers.Montar`).

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`

**Interfaces:**
- Consumes: `PatronHelpers.Montar(Type)`, `PatronHelpers.OpacidadesLiteralesDe(Control)`, `GuardianDePatronTests.VistasMigradasAV2`.
- Produces: `GuardianDePatronTests.VistasV2Montables` (`TheoryData<Type>`: `VistasMigradasAV2` + `ShellMainView`) y `GuardianDePatronTests.DialogosV2` (`TheoryData<string>`, rutas de `.axaml` desde `src/`). Toda vista que la Fase 3 en adelante agregue a `VistasMigradasAV2` queda cubierta sola.

- [ ] **Step 1: Tests**

En `GuardianDePatronTests.cs`, agregar `using System.IO;`, `using System.Runtime.CompilerServices;` y `using System.Text.RegularExpressions;` si faltan, y debajo de `Vista_NoTieneOpacidadesLiterales`:

```csharp
    /// <summary>
    /// Deuda de la Fase 1: una vista que se migra a v2 sale de VistasDeLaTanda y perdía este chequeo. El
    /// cromo del shell (sidebar v2) también: ShellMainView está fuera del guardián P0-P9, pero se puede montar.
    /// </summary>
    public static TheoryData<Type> VistasV2Montables =>
        new(VistasMigradasAV2.Append(typeof(ShellMainView)).OrderBy(t => t.Name));

    [AvaloniaTheory]
    [MemberData(nameof(VistasV2Montables))]
    public void VistaV2_NoTieneOpacidadesLiterales(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var literales = PatronHelpers.OpacidadesLiteralesDe(vista);

        Assert.True(literales.Count == 0,
            $"{tipoVista.Name} (v2) tiene {literales.Count} control(es) con Opacity literal: "
            + string.Join(", ", literales.Select(c => c.GetType().Name)));
    }

    /// <summary>
    /// Los diálogos son Window: PatronHelpers.Montar no puede montarlos (ver VistasFueraDelGuardian). Se lee
    /// su .axaml: un atributo Opacity="…" en un elemento es un literal; un Setter (Property="Opacity") no.
    /// </summary>
    private static readonly Regex OpacidadLiteralEnXaml = new(@"<[A-Za-z][\w:.]*\b[^>]*\sOpacity\s*=\s*""", RegexOptions.Compiled);

    public static TheoryData<string> DialogosV2 => new()
    {
        "StockApp.Presentation/Views/Dialogs/ConfirmacionDialog.axaml",
        "StockApp.Presentation/Views/Dialogs/MensajeDialog.axaml",
        "StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml",
    };

    [Theory]
    [MemberData(nameof(DialogosV2))]
    public void DialogoV2_NoTieneOpacidadLiteralEnSuXaml(string rutaDesdeSrc)
    {
        var xaml = LeerDesdeSrc(rutaDesdeSrc);

        Assert.DoesNotMatch(OpacidadLiteralEnXaml, xaml);
    }

    [Fact]
    public void DetectorDeOpacidadLiteral_VeUnAtributoYNoUnSetter()
    {
        // Control positivo del detector: sin esto, un regex roto deja verde DialogoV2_NoTieneOpacidadLiteralEnSuXaml.
        Assert.Matches(OpacidadLiteralEnXaml, """<Border x:Name="Fondo" Opacity="0.5" />""");
        Assert.Matches(OpacidadLiteralEnXaml, "<TextBlock\n    Text=\"x\"\n    Opacity=\"0.6\" />");
        Assert.DoesNotMatch(OpacidadLiteralEnXaml, """<Setter Property="Opacity" Value="0.28" />""");
    }

    private static string LeerDesdeSrc(string rutaDesdeSrc, [CallerFilePath] string archivoDeEsteTest = "")
    {
        var ruta = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivoDeEsteTest)!, "..", "..", "src", rutaDesdeSrc));
        Assert.True(File.Exists(ruta), $"No existe {ruta}");
        return File.ReadAllText(ruta);
    }
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~GuardianDePatronTests"`
Expected: PASS (ninguna vista v2 tiene literales hoy: el Login usa `Style` para su marca de agua). Si alguna sale roja, es un literal real: reemplazarlo por un `Style` (como `Image.marca-agua`) o quitarlo, y anotarlo.

- [ ] **Step 2: Mutación quitando**

```bash
cp tests/StockApp.Presentation.UiTests/PatronHelpers.cs "$SCRATCH/PatronHelpers.cs.bak"
```

Quitar la línea `.Where(c => c.GetDiagnostic(Visual.OpacityProperty).Priority == BindingPriority.LocalValue)` de `OpacidadesLiteralesDe` (el filtro deja de distinguir un `Style` de un literal). `git diff --no-index`. Correr el filtro: rojo en `VistaV2_NoTieneOpacidadesLiterales(LoginView)` (su marca de agua es 0.28 por estilo): prueba que la teoría nueva recorre las vistas migradas de verdad. (También se ponen rojas filas viejas; es esperado.) Restaurar con `cp`, verde.

Para el detector de diálogos no se muta un diálogo agregándole un literal (regla "mutá quitando"): su prueba de que detecta es el control positivo `DetectorDeOpacidadLiteral_VeUnAtributoYNoUnSetter`. Mutación sobre el detector: quitar `\s` delante de `Opacity` en `OpacidadLiteralEnXaml` (`…[^>]*Opacity\s*=…`). `git diff --no-index`. Con el detector así, `Opacity="0.5"` sigue matcheando y el `Setter` sigue sin matchear, así que lo esperable es VERDE: anotarlo en el reporte como "el `\s` no agrega custodia medible" y restaurar. Pegar el rojo de la mutación de `PatronHelpers`.

- [ ] **Step 3: Commit**

```bash
git add tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs
git commit -m "test(ui-v2): las vistas migradas y los diálogos v2 no pueden tener Opacity literal"
```

### Task 5: Gate de secciones del sidebar y ubicación de `ui2`: custodia probada por mutación

Deuda (spec L363): "Las comparaciones de píxeles no custodian el gate `IsVisible=EsVisible` del sidebar ni la ubicación de `ui2` (lo custodian los UiTests)". El cierre correcto no es agregar una métrica de píxeles (un header de 11 px en gris claro pesa poco, y la ubicación de `ui2` no cambia ningún píxel del sidebar): es PROBAR que los UiTests, que montan la View real, lo custodian, y dejarlo escrito.

**Files:**
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (deuda, L363; §8.1)
- Modify: `docs/ui-v2/herramientas.md` (sección "Convenciones de la convivencia")

**Interfaces:**
- Consumes: `ShellMainViewV2Tests` (`Ui2_VaEnElSidebarYNoEnLaRaiz`, `ContenidoHospedado_NoTieneAncestroUi2`, `OperadorMixto_SeccionesSinItemsVisibles_OcultanSuHeader`, `RevocacionEnCaliente_OcultaElHeaderDeLaSeccionQueSeVacia`), `ShellComparacionTests`.
- Produces: evidencia en el ledger y en la spec; ningún código.

- [ ] **Step 1: Mutación (a): quitar el gate del header**

```bash
cp src/StockApp.Presentation/Views/ShellMainView.axaml "$SCRATCH/ShellMainView.axaml.bak"
```

Quitar `IsVisible="{Binding EsVisible}"` del `StackPanel` del `DataTemplate` de `GrupoNavegacion` (L83). `git diff --no-index "$SCRATCH/ShellMainView.axaml.bak" src/StockApp.Presentation/Views/ShellMainView.axaml` muestra solo esa línea.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ShellMainViewV2Tests"` y `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~ShellComparacionTests"`
Expected: rojo en `OperadorMixto_SeccionesSinItemsVisibles_OcultanSuHeader` y `RevocacionEnCaliente_OcultaElHeaderDeLaSeccionQueSeVacia`. Anotar en el ledger qué hacen las cuatro comparaciones del shell (si `SidebarOperadorMixto_IgualAlMockup` sale rojo, la comparación también lo ve en ese estado; si sale verde, lo ve solo el UiTest). Pegar los rojos. Restaurar con `cp`; `git diff --no-index` vacío.

- [ ] **Step 2: Mutación (b): quitar `ui2` del sidebar**

Quitar `Classes="ui2"` del `Border x:Name="Sidebar"` (L19). `git diff --no-index`.
Run: los mismos dos filtros.
Expected: rojo en `Ui2_VaEnElSidebarYNoEnLaRaiz` y en las cuatro comparaciones del shell (sin `.ui2` el sidebar pierde la tipografía y los botones v2). Pegar. Restaurar con `cp`.

- [ ] **Step 3: Mutación (c): `ui2` en la raíz (el defecto que el UiTest vigila)**

Este invariante se viola agregando, no quitando: `ui2` en el `Grid` raíz fuga estilos v2 a las vistas hospedadas. Se hace como MOVIMIENTO (no destructivo: nada se borra del repo ni del entorno) y se restaura con `cp` igual: quitar `Classes="ui2"` del `Border#Sidebar` y ponerlo en el `Grid` raíz (L15). `git diff --no-index` muestra las dos líneas.
Run: los dos filtros.
Expected: rojo en `ContenidoHospedado_NoTieneAncestroUi2` y en `Ui2_VaEnElSidebarYNoEnLaRaiz`; las comparaciones del shell probablemente VERDES (el sidebar sigue dentro de `.ui2` y el contenido está vacío): eso es justo lo que dice la deuda, y queda medido. Pegar. Restaurar con `cp`, `git diff --no-index` vacío, verde.

- [ ] **Step 4: Escribirlo**

En la spec, reemplazar la viñeta de la deuda (L363) por:

```markdown
- ~~Las comparaciones de píxeles no custodian el gate `IsVisible=EsVisible` del sidebar ni la ubicación de `ui2`.~~ Cerrada (Fase 2, Task 5): la custodia es de los UiTests, que montan la `ShellMainView` real, y se probó por mutación: sin el gate del header, rojo en `ShellMainViewV2Tests.OperadorMixto_SeccionesSinItemsVisibles_OcultanSuHeader` y `RevocacionEnCaliente_OcultaElHeaderDeLaSeccionQueSeVacia`; con `ui2` movido a la raíz, rojo en `ContenidoHospedado_NoTieneAncestroUi2` (las comparaciones del shell quedan verdes: no lo ven, por diseño). <resultado medido de SidebarOperadorMixto_IgualAlMockup en la mutación (a)>.
```

(La frase entre `<…>` se completa con lo medido en el Step 1: "la comparación del operador mixto también da rojo" o "la comparación del operador mixto queda verde".)

En `docs/ui-v2/herramientas.md`, sección "Convenciones de la convivencia", agregar: "Gates y ubicación de `ui2` se custodian en `StockApp.Presentation.UiTests` con la View real, no con la comparación de píxeles: un header de sección o un `ui2` mal ubicado pueden no cambiar ningún píxel del estado comparado."

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md docs/ui-v2/herramientas.md
git commit -m "docs(ui-v2): la custodia del gate de secciones y de ui2 queda probada por mutación"
```

### Task 6: Cosméticos de la Fase 1

Deuda (spec L370): comentario de `GrupoNavegacion.cs` L11-14 que menciona Finanzas/Maestros; comentario de `ControlesV2.axaml` que habla de `.v2-boton` (el real es `.v2-boton--icono`); `reset.html` declara `.mt-12` y `.enlace-centrado` sin uso.

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/GrupoNavegacion.cs` (L11-14)
- Modify: `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml` (L147)
- Modify: `docs/ui-v2/mockups/acceso/reset.html` (L12, L15)

**Interfaces:**
- Consumes: nada. Produces: nada (solo texto).

- [ ] **Step 1: `GrupoNavegacion.cs`**

Reemplazar L11-14 por:

```csharp
/// EsVisible es "algun hijo visible", NO un permiso propio. Eso arreglo un bug del sidebar viejo: el
/// header "Finanzas" estaba gateado por PuedeVerFinanzas mientras su item "Maestros de finanzas" iba por
/// PuedeGestionarMaestrosFinanzas, y un operador con ese permiso y sin VerFinanzas veia el boton sin titulo.
/// En el sidebar v2 "Maestros de finanzas" esta en ADMINISTRACION, pero la regla sigue: un header nunca
/// tiene permiso propio, se muestra si y solo si su seccion tiene algun item visible.
```

- [ ] **Step 2: `ControlesV2.axaml`**

El comentario no puede nombrar la clase real: lleva doble guion, prohibido en un comentario XAML (AVLN1001). Reemplazar L147 por:

```xml
    <!-- Ghost de solo ícono (cerrar sesión del pie del sidebar). Espejo CSS: el modificador de ícono de v2-boton
         en base.css (su nombre lleva doble guion y un comentario XAML no puede contenerlo). -->
```

- [ ] **Step 3: `reset.html`**

Borrar las líneas `  .mt-12 { margin-top: 12px; }` y `  .enlace-centrado { align-self: center; }` del `<style>`. Confirmar que nada las usa: `grep -n 'mt-12\|enlace-centrado' docs/ui-v2/mockups/acceso/reset.html` no imprime nada.

- [ ] **Step 4: Verde**

Run: `dotnet build StockApp.sln` y `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~Reset_IgualAlMockup"`
Expected: 0 errores; `Reset_IgualAlMockup` y `Reset_IgualAlMockup_1366x768` en verde con las mismas teselas que antes (el CSS borrado no se usaba).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/GrupoNavegacion.cs src/StockApp.Presentation/Themes/V2/ControlesV2.axaml docs/ui-v2/mockups/acceso/reset.html
git commit -m "chore(ui-v2): comentarios y CSS sin uso que dejó la Fase 1"
```

### Task 7: Componentes de Inicio: `GrillaUniformeV2` y `V2TemaFilaPanel`

Van antes de los mockups: los mockups de la Task 8 usan sus espejos CSS.

**Files:**
- Create: `src/StockApp.Presentation/Controls/V2/GrillaUniformeV2.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/GrillaUniformeV2Tests.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/FilaPanelV2Tests.cs`
- Modify: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` (antes de `</ResourceDictionary>`)
- Modify: `docs/ui-v2/mockups/_base/base.css` (al final)
- Modify: `docs/ui-v2/herramientas.md` (convenciones 7 y 9)

**Interfaces:**
- Consumes: `MontajeV2.Montar`, `Buscar<T>`, `Parte<T>`, `ColorDe`, `Hex`.
- Produces: `StockApp.Presentation.Controls.V2.GrillaUniformeV2 : Panel` con `int Columnas` (0 = tantas como hijos visibles) y `double Espaciado` (entre columnas y entre filas); `ControlTheme x:Key="V2TemaFilaPanel" TargetType="Button"` con parte `Border#PART_Fila`; CSS `.v2-grilla-uniforme` y `.v2-fila-panel`.

- [ ] **Step 1: Tests de la grilla (rojo)**

`tests/StockApp.Presentation.UiTests/V2/GrillaUniformeV2Tests.cs`:

```csharp
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Columnas iguales con los bordes donde los pone Chromium (cada borde redondeado por separado). UniformGrid
/// redondea el ANCHO y acumula: a 1366 la tercera tarjeta queda corrida 1 px respecto del mockup.
/// </summary>
public class GrillaUniformeV2Tests
{
    private static Rect[] Celdas(Window ventana) =>
        Buscar<GrillaUniformeV2>(ventana, "G").Children.Where(c => c.IsVisible).Select(c => c.Bounds).ToArray();

    [AvaloniaFact]
    public void TresColumnasFraccionarias_CadaBordeRedondeadoPorSeparado()
    {
        // (992 - 2*12) / 3 = 322,67: bordes en 0|322,67|334,67|657,33|669,33|992 → 0-323, 335-657, 669-992.
        var celdas = Celdas(Montar("""
            <v2:GrillaUniformeV2 x:Name="G" Width="992" Espaciado="12">
                <Border Height="40" /><Border Height="40" /><Border Height="40" />
            </v2:GrillaUniformeV2>
            """, ancho: 1100));

        Assert.Equal(new Rect(0, 0, 323, 40), celdas[0]);
        Assert.Equal(new Rect(335, 0, 322, 40), celdas[1]);
        Assert.Equal(new Rect(669, 0, 323, 40), celdas[2]);
    }

    [AvaloniaFact]
    public void HijoInvisible_NoOcupaColumna()
    {
        // Gate: la tarjeta oculta no deja hueco; las visibles se reparten la fila.
        var celdas = Celdas(Montar("""
            <v2:GrillaUniformeV2 x:Name="G" Width="992" Espaciado="12">
                <Border Height="40" /><Border Height="40" IsVisible="False" /><Border Height="40" />
            </v2:GrillaUniformeV2>
            """, ancho: 1100));

        Assert.Equal(new Rect(0, 0, 490, 40), celdas[0]);
        Assert.Equal(new Rect(502, 0, 490, 40), celdas[1]);
    }

    [AvaloniaFact]
    public void ColumnasFijas_CadaFilaMideSuItemMasAlto()
    {
        // Como grid-auto-rows: auto. Fila 1: 60 de alto; fila 2 arranca en 60 + 12.
        var celdas = Celdas(Montar("""
            <v2:GrillaUniformeV2 x:Name="G" Width="960" Columnas="3" Espaciado="12">
                <Border Height="40" /><Border Height="60" /><Border Height="50" /><Border Height="30" />
            </v2:GrillaUniformeV2>
            """, ancho: 1100));

        Assert.Equal(new Rect(0, 0, 312, 60), celdas[0]);
        Assert.Equal(new Rect(324, 0, 312, 60), celdas[1]);
        Assert.Equal(new Rect(0, 72, 312, 30), celdas[3]);
    }

    [AvaloniaFact]
    public void SinHijosVisibles_MideCeroYNoLanza()
    {
        var grilla = Buscar<GrillaUniformeV2>(Montar("""
            <v2:GrillaUniformeV2 x:Name="G" Width="992" Espaciado="12">
                <Border Height="40" IsVisible="False" />
            </v2:GrillaUniformeV2>
            """, ancho: 1100), "G");

        Assert.Equal(0, grilla.Bounds.Height);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~GrillaUniformeV2Tests"`
Expected: FAIL (el XAML runtime no encuentra `v2:GrillaUniformeV2`).

- [ ] **Step 2: Implementar la grilla**

`src/StockApp.Presentation/Controls/V2/GrillaUniformeV2.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// N columnas iguales separadas por <see cref="Espaciado"/> (fila de métricas de Inicio, accesos rápidos,
/// tarjetas del hub de Reportes). Espejo CSS: .v2-grilla-uniforme con grid-template-columns:
/// repeat(N, minmax(0, 1fr)). Cada borde de celda se redondea por separado, como el pixel snapping de
/// Chromium; UniformGrid redondea el ancho y acumula el error (Fase 2, decisiones de diseño). Los hijos
/// invisibles no ocupan columna. Cada fila mide lo que su ítem más alto, como grid-auto-rows: auto.
/// </summary>
public class GrillaUniformeV2 : Panel
{
    public static readonly StyledProperty<int> ColumnasProperty =
        AvaloniaProperty.Register<GrillaUniformeV2, int>(nameof(Columnas));

    public static readonly StyledProperty<double> EspaciadoProperty =
        AvaloniaProperty.Register<GrillaUniformeV2, double>(nameof(Espaciado));

    static GrillaUniformeV2()
    {
        AffectsMeasure<GrillaUniformeV2>(ColumnasProperty, EspaciadoProperty);
    }

    /// <summary>Cantidad de columnas; 0 = tantas como hijos visibles (una sola fila).</summary>
    public int Columnas
    {
        get => GetValue(ColumnasProperty);
        set => SetValue(ColumnasProperty, value);
    }

    public double Espaciado
    {
        get => GetValue(EspaciadoProperty);
        set => SetValue(EspaciadoProperty, value);
    }

    private List<Control> Visibles() => Children.Where(c => c.IsVisible).ToList();

    private int ColumnasEfectivas(int visibles) => Columnas > 0 ? Columnas : Math.Max(visibles, 1);

    protected override Size MeasureOverride(Size disponible)
    {
        var hijos = Visibles();
        if (hijos.Count == 0)
        {
            return default;
        }

        var n = ColumnasEfectivas(hijos.Count);
        var anchoCelda = double.IsInfinity(disponible.Width)
            ? double.PositiveInfinity
            : Math.Max(0, (disponible.Width - (n - 1) * Espaciado) / n);
        double alto = 0, anchoMaximo = 0;
        foreach (var fila in hijos.Chunk(n))
        {
            double altoFila = 0;
            foreach (var hijo in fila)
            {
                hijo.Measure(new Size(anchoCelda, double.PositiveInfinity));
                altoFila = Math.Max(altoFila, hijo.DesiredSize.Height);
                anchoMaximo = Math.Max(anchoMaximo, hijo.DesiredSize.Width);
            }

            alto += altoFila;
        }

        var filas = (hijos.Count + n - 1) / n;
        alto += (filas - 1) * Espaciado;
        var ancho = double.IsInfinity(disponible.Width) ? anchoMaximo * n + (n - 1) * Espaciado : disponible.Width;
        return new Size(ancho, alto);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var hijos = Visibles();
        if (hijos.Count == 0)
        {
            return final;
        }

        var n = ColumnasEfectivas(hijos.Count);
        var celda = Math.Max(0, (final.Width - (n - 1) * Espaciado) / n);
        double y = 0;
        foreach (var fila in hijos.Chunk(n))
        {
            var altoFila = fila.Max(h => h.DesiredSize.Height);
            for (var i = 0; i < fila.Length; i++)
            {
                // Bordes redondeados por separado (pixel snapping de Chromium): izquierda y derecha de cada
                // celda caen donde las pone el mockup, sin acumular el redondeo del ancho.
                var izquierda = Math.Round(i * (celda + Espaciado), MidpointRounding.AwayFromZero);
                var derecha = Math.Round(i * (celda + Espaciado) + celda, MidpointRounding.AwayFromZero);
                fila[i].Arrange(new Rect(izquierda, y, derecha - izquierda, altoFila));
            }

            y += altoFila + Espaciado;
        }

        return final;
    }
}
```

Run: el mismo filtro. Expected: PASS.

- [ ] **Step 3: Tests de la fila de panel (rojo)**

`tests/StockApp.Presentation.UiTests/V2/FilaPanelV2Tests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>V2TemaFilaPanel: fila clickeable de un panel (Inicio, "Tareas que requieren atención").</summary>
public class FilaPanelV2Tests
{
    private const string Fila = """
        <Button x:Name="F" Theme="{StaticResource V2TemaFilaPanel}">
            <TextBlock Text="Reparar bache en Av. 18 de Julio" Height="19" />
        </Button>
        """;

    [AvaloniaFact]
    public void Fila_SeparadorArribaYPaddingVerticalDe8()
    {
        var fila = Buscar<Button>(Montar(Fila), "F");
        var marco = Parte<Border>(fila, "PART_Fila");

        Assert.Equal(new Thickness(0, 1, 0, 0), marco.BorderThickness);
        Assert.Equal(Hex("#F0EFED"), ColorDe(marco.BorderBrush));
        Assert.Equal(new Thickness(0, 8), marco.Padding);
        Assert.Equal(1 + 8 + 19 + 8, fila.Bounds.Height);
    }

    [AvaloniaFact]
    public void Fila_FondoTransparenteAunqueElEstiloGlobalDeButtonPongaSuperficie()
    {
        // Themes/Controls.axaml: Style Selector="Button" setea Background = SuperficieBrush. La plantilla no lo usa.
        var marco = Parte<Border>(Buscar<Button>(Montar(Fila), "F"), "PART_Fila");

        Assert.Equal(Colors.Transparent, ColorDe(marco.Background));
    }

    [AvaloniaFact]
    public void Fila_ConElMouseEncima_FondoHoverNeutro()
    {
        var ventana = Montar(Fila);
        var fila = Buscar<Button>(ventana, "F");
        var centro = fila.TranslatePoint(new Point(fila.Bounds.Width / 2, fila.Bounds.Height / 2), ventana)!.Value;

        ventana.MouseMove(centro);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<Border>(fila, "PART_Fila").Background));
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~FilaPanelV2Tests"`
Expected: FAIL (`V2TemaFilaPanel` no existe: el parser no resuelve el `StaticResource`).

- [ ] **Step 4: Implementar la fila de panel**

En `ComponentesV2.axaml`, antes de `</ResourceDictionary>`:

```xml
    <!-- Fila clickeable de un panel (Inicio, "Tareas que requieren atención"). Espejo CSS: .v2-fila-panel.
         Separador de grilla arriba, padding vertical 8. Plantilla propia y fondo en una parte: el estilo global
         de Button (Themes/Controls.axaml) setea Background, Padding y :pointerover sobre el control, que esta
         plantilla no usa. El presentador NO se llama PART_ContentPresenter para que ningún estilo de Fluent ni
         global lo alcance. -->
    <ControlTheme x:Key="V2TemaFilaPanel" TargetType="Button">
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="HorizontalContentAlignment" Value="Stretch" />
        <Setter Property="Template">
            <ControlTemplate>
                <Border x:Name="PART_Fila"
                        BorderBrush="{DynamicResource V2SeparadorGrillaBrush}"
                        BorderThickness="0,1,0,0"
                        Padding="0,8">
                    <ContentPresenter x:Name="PART_Contenido"
                                      Content="{TemplateBinding Content}"
                                      ContentTemplate="{TemplateBinding ContentTemplate}"
                                      HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}" />
                </Border>
            </ControlTemplate>
        </Setter>

        <Style Selector="^ /template/ Border#PART_Fila">
            <Setter Property="Background" Value="Transparent" />
        </Style>
        <Style Selector="^:pointerover /template/ Border#PART_Fila">
            <Setter Property="Background" Value="{DynamicResource V2HoverNeutroBrush}" />
        </Style>
    </ControlTheme>
```

En `base.css`, al final:

```css

/* GrillaUniformeV2 (Controls/V2/GrillaUniformeV2): cada página fija grid-template-columns:
   repeat(N, minmax(0, 1fr)) y el gap. Chromium redondea cada borde de celda por separado; la grilla
   de Avalonia hace lo mismo. Cada fila mide lo que su ítem más alto. */
.v2-grilla-uniforme { display: grid; grid-auto-rows: auto; align-items: stretch; }

/* Fila clickeable de panel (ComponentesV2.axaml, V2TemaFilaPanel): separador arriba, padding 8 vertical. */
.v2-fila-panel { display: flex; align-items: center; padding: 8px 0; border-top: 1px solid var(--v2-color-separador-grilla); }
```

Run: `dotnet build StockApp.sln` y el filtro `FilaPanelV2Tests`. Expected: PASS.

- [ ] **Step 5: Mutación quitando**

```bash
cp src/StockApp.Presentation/Controls/V2/GrillaUniformeV2.cs "$SCRATCH/GrillaUniformeV2.cs.bak"
cp src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml "$SCRATCH/ComponentesV2.axaml.bak"
```

(a) Quitar el redondeo de los bordes: reemplazar las dos líneas `var izquierda = Math.Round(…);` / `var derecha = Math.Round(…);` por `var izquierda = i * (celda + Espaciado);` / `var derecha = i * (celda + Espaciado) + celda;` (se quita `Math.Round`). `git diff --no-index`. Rojo en `TresColumnasFraccionarias_CadaBordeRedondeadoPorSeparado` (el layout rounding de Avalonia redondea origen y ancho: la celda 1 queda en 335 con ancho 323). Restaurar con `cp`.
(b) Quitar `.Where(c => c.IsVisible)` de `Visibles()` (queda `Children.ToList()`). Rojo en `HijoInvisible_NoOcupaColumna`. Restaurar con `cp`.
(c) Quitar el `Style` `^ /template/ Border#PART_Fila` (Background Transparent) de `V2TemaFilaPanel`. Rojo en `Fila_FondoTransparenteAunque…` (el `Background` queda `null`: `ColorDe` falla al castear). Restaurar con `cp`.

Pegar los tres rojos. Verde al final.

- [ ] **Step 6: Documentación**

`docs/ui-v2/herramientas.md`, sección "Convenciones de la convivencia", agregar las convenciones 7 y 9 de este plan (texto de "Convenciones que heredan las fases siguientes", ítems 7 y 9).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2/GrillaUniformeV2.cs src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml docs/ui-v2/mockups/_base/base.css tests/StockApp.Presentation.UiTests/V2/GrillaUniformeV2Tests.cs tests/StockApp.Presentation.UiTests/V2/FilaPanelV2Tests.cs docs/ui-v2/herramientas.md
git commit -m "feat(ui-v2): grilla uniforme con bordes como Chromium y fila clickeable de panel"
```

## Bloque B — Mockups y aprobación del usuario (GATE antes del XAML)

Spec §6.1 y §7: "mockups de sus pantallas → aprobación del usuario → XAML". Cada task termina en un GATE: el ejecutor PARA, presenta los mockups y no sigue hasta tener la aprobación explícita. Si el usuario pide cambios, se aplican en el mockup (y en su componente/estilo/VM con sus tests, si corresponde) dentro de la misma task y se vuelve a presentar.

Datos fijos que comparten mockups y tests (`DatosInicio`, Task 11, usa EXACTAMENTE estos valores):

| Dato | Valor |
|---|---|
| Reloj | `2026-10-06T13:00:00Z`, zona UTC → "martes 6 de octubre de 2026" |
| Administrador | `UsuarioSesion(1, "mperez", Admin, "María Pérez")` |
| Operador mixto | `UsuarioSesion(2, "jrodriguez", Operador, "Juan Rodríguez")`; permisos `GestionarProductos`, `RegistrarMovimientos`, `VerFinanzas`, `GestionarTareas` |
| Operador sin finanzas ni tareas | `UsuarioSesion(3, "lsilva", Operador, "Laura Silva")`; permisos `GestionarProductos`, `VerReportes` |
| Admin | facturas: 0 vencidas, 3 a vencer; tareas (Pendiente, sin tomar): "Reparar bache en Av. 18 de Julio" (límite 04/10), "Retirar poda zona Rambla" (05/10), "Inspección de alumbrado público" (08/10), "Mantenimiento plaza Independencia" (09/10); backup: último éxito `2026-10-04 14:32 UTC`, vencido, umbral 26 |
| Operador mixto | facturas: 1 vencida, 2 a vencer; tareas: "Controlar stock del depósito" (03/10), "Recibir pedido de luminarias" (06/10) |
| Admin sin datos | los tres servicios fallan (`HttpRequestException("API caída")`) |

Íconos: los HTML se escriben con marcadores `{{mdi-<clave>}}` y se reemplazan con la salida de `icono-svg` (paso "Insertar íconos").

### Task 8: Mockups de Inicio (Administrador, Operador mixto, Operador sin finanzas ni tareas, Administrador sin datos)

**Decisiones aplicadas:** U2 (logo de fondo), D1 a D8.

**Files:**
- Create: `docs/ui-v2/mockups/inicio/inicio-admin.html`
- Create: `docs/ui-v2/mockups/inicio/inicio-operador.html`
- Create: `docs/ui-v2/mockups/inicio/inicio-operador-sin-finanzas.html`
- Create: `docs/ui-v2/mockups/inicio/inicio-admin-sin-datos.html`

**Interfaces:**
- Consumes: `base.css` (`.v2-encabezado*`, `.v2-titulo-pagina`, `.v2-boton--secundario`, `.v2-metrica*`, `.v2-alerta--*`, `.v2-tarjeta`, `.v2-titulo-seccion`, `.v2-fila-panel`, `.v2-insignia--*`, `.v2-pie-conteo`, `.v2-enlace`, `.v2-grilla-uniforme`, `.v2-acceso*`, `.v2-sidebar*`), comandos `icono-svg` y `capturar-mockup`.
- Produces: pantallas `inicio-inicio-admin`, `inicio-inicio-operador`, `inicio-inicio-operador-sin-finanzas`, `inicio-inicio-admin-sin-datos` para `InicioComparacionTests` (Task 11). Selectores ocultables `.v2-sidebar` y `.marca-agua` presentes en los cuatro.

El `<head>` es el mismo en los cuatro (cambia solo el `<title>`):

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Inicio: Administrador (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  /* Área de contenido del shell: a la derecha del sidebar de 240 (ShellMainView, Grid 240,*). */
  .contenido { position: absolute; left: var(--v2-ancho-sidebar); top: 0; right: 0; bottom: 0; overflow: hidden; }
  /* Marca de agua del logo (U2): igual que el Login (P5) e InicioView.axaml (Image.marca-agua: Stretch
     Uniform, centrada en el área de contenido, Opacity 0.28, scale(0.85)), detrás del contenido. La
     comparación la oculta (ocultarEnMockup); InicioMarcaAguaTests exige estos 0.28 y 0.85. */
  .marca-agua { position: absolute; left: 0; top: 0; width: 100%; height: 100%; object-fit: contain; opacity: 0.28; transform: scale(0.85); }
  /* StackPanel Margin 28,24 Spacing 16 de InicioView.axaml. */
  .pagina { position: relative; z-index: 1; display: flex; flex-direction: column; gap: 16px; padding: 24px 28px; }
  .pagina > .v2-encabezado { margin-bottom: 0; }
  .metricas-3 { grid-template-columns: repeat(3, minmax(0, 1fr)); column-gap: 12px; }
  .metricas-2 { grid-template-columns: repeat(2, minmax(0, 1fr)); column-gap: 12px; }
  .accesos { grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
  .panel { display: flex; flex-direction: column; }
  .panel-titulo { margin-bottom: 12px; }
  .fila-titulo { flex: 1 1 0; min-width: 0; margin-left: 8px; font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-primario); }
  .fila-dias { margin-left: 8px; }
  .ver-todas { align-self: flex-end; margin-top: 12px; }
</style>
</head>
```

- [ ] **Step 1: `inicio-admin.html`**

`<title>Inicio: Administrador (UI v2)</title>`. Cuerpo:

```html
<body>
<!-- Datos: DatosInicio.Admin() (Task 11). Reloj fijo: martes 6 de octubre de 2026, zona UTC.
     El sidebar está para que la pantalla se apruebe entera; la comparación lo oculta (tiene la suya). -->
<div class="v2-ventana">
  SIDEBAR_ADMIN
  <div class="contenido">
    <img class="marca-agua" src="../../../../src/StockApp.Presentation/Assets/carmelo-original.png" alt="">
    <div class="pagina">
      <div class="v2-encabezado">
        <div class="v2-encabezado-fila">
          <div>
            <div class="v2-titulo-pagina">¡Bienvenido, María Pérez!</div>
            <div class="v2-encabezado-subtitulo">martes 6 de octubre de 2026 · Sesión iniciada como Administrador</div>
          </div>
          <div class="v2-acciones"><span class="v2-boton v2-boton--secundario">Ver calendario de pagos</span></div>
        </div>
      </div>
      <div class="v2-grilla-uniforme metricas-3">
        <div class="v2-metrica"><div class="v2-metrica-valor">3</div><div class="v2-metrica-etiqueta">Facturas a vencer en 7 días</div></div>
        <div class="v2-metrica"><div class="v2-metrica-valor">2</div><div class="v2-metrica-etiqueta">Tareas vencidas</div></div>
        <div class="v2-metrica"><div class="v2-metrica-valor">04/10/2026 14:32</div><div class="v2-metrica-etiqueta">Último backup</div></div>
      </div>
      <div class="v2-alerta v2-alerta--error">El último backup exitoso fue el 04/10/2026 14:32 (hace más de 26 horas).</div>
      <div class="v2-tarjeta panel">
        <div class="v2-titulo-seccion panel-titulo">Tareas que requieren atención</div>
        <div class="v2-fila-panel"><span class="v2-insignia v2-insignia--peligro">Vencida</span><span class="fila-titulo">Reparar bache en Av. 18 de Julio</span><span class="v2-pie-conteo fila-dias">-2 días</span></div>
        <div class="v2-fila-panel"><span class="v2-insignia v2-insignia--peligro">Vencida</span><span class="fila-titulo">Retirar poda zona Rambla</span><span class="v2-pie-conteo fila-dias">-1 día</span></div>
        <div class="v2-fila-panel"><span class="v2-insignia v2-insignia--advertencia">Próxima</span><span class="fila-titulo">Inspección de alumbrado público</span><span class="v2-pie-conteo fila-dias">2 días</span></div>
        <div class="v2-fila-panel"><span class="v2-insignia v2-insignia--advertencia">Próxima</span><span class="fila-titulo">Mantenimiento plaza Independencia</span><span class="v2-pie-conteo fila-dias">3 días</span></div>
        <span class="v2-enlace ver-todas">Ver todas las tareas →</span>
      </div>
      <div class="v2-tarjeta">
        <div class="v2-titulo-seccion panel-titulo">Accesos rápidos</div>
        <div class="v2-grilla-uniforme accesos">
          <div class="v2-acceso">{{mdi-package-variant}}<div><div class="v2-acceso-titulo">Productos</div><div class="v2-acceso-descripcion">Ver y administrar el catálogo</div></div></div>
          <div class="v2-acceso">{{mdi-tray-arrow-down}}<div><div class="v2-acceso-titulo">Registrar Entrada</div><div class="v2-acceso-descripcion">Cargar entradas de stock</div></div></div>
          <div class="v2-acceso">{{mdi-tray-arrow-up}}<div><div class="v2-acceso-titulo">Registrar Salida</div><div class="v2-acceso-descripcion">Cargar salidas de stock</div></div></div>
          <div class="v2-acceso">{{mdi-history}}<div><div class="v2-acceso-titulo">Historial de movimientos</div><div class="v2-acceso-descripcion">Consultar movimientos registrados</div></div></div>
          <div class="v2-acceso">{{mdi-currency-usd}}<div><div class="v2-acceso-titulo">Valorización de inventario</div><div class="v2-acceso-descripcion">Ver el valor total del stock</div></div></div>
          <div class="v2-acceso">{{mdi-shield-search}}<div><div class="v2-acceso-titulo">Log de auditoría</div><div class="v2-acceso-descripcion">Revisar la actividad del sistema</div></div></div>
        </div>
      </div>
    </div>
  </div>
</div>
</body>
</html>
```

`SIDEBAR_ADMIN` NO es texto del archivo: en su lugar va el bloque `<aside class="v2-sidebar">…</aside>` copiado tal cual de `docs/ui-v2/mockups/shell/sidebar-admin.html` (ya trae los SVG insertados; si la Task 9 ya agregó el pulgar, copiarlo con él), con un solo cambio: quitar `v2-item-sidebar--activo` de "Productos" y ponerlo en "Inicio". Comprobar: `grep -c 'v2-item-sidebar--activo' docs/ui-v2/mockups/inicio/inicio-admin.html` imprime `1` y `grep -c 'SIDEBAR_' docs/ui-v2/mockups/inicio/*.html` imprime `0` en todos.

- [ ] **Step 2: `inicio-operador.html`**

`<title>Inicio: Operador con permisos mixtos (UI v2)</title>`. Cuerpo:

```html
<body>
<!-- Datos: DatosInicio.OperadorMixto() (Task 11): GestionarProductos, RegistrarMovimientos, VerFinanzas y
     GestionarTareas. Sin métrica de backup (solo Admin), sin Valorización ni Log de auditoría (VerReportes). -->
<div class="v2-ventana">
  SIDEBAR_OPERADOR
  <div class="contenido">
    <img class="marca-agua" src="../../../../src/StockApp.Presentation/Assets/carmelo-original.png" alt="">
    <div class="pagina">
      <div class="v2-encabezado">
        <div class="v2-encabezado-fila">
          <div>
            <div class="v2-titulo-pagina">¡Bienvenido, Juan Rodríguez!</div>
            <div class="v2-encabezado-subtitulo">martes 6 de octubre de 2026 · Sesión iniciada como Operador</div>
          </div>
          <div class="v2-acciones"><span class="v2-boton v2-boton--secundario">Ver calendario de pagos</span></div>
        </div>
      </div>
      <div class="v2-grilla-uniforme metricas-2">
        <div class="v2-metrica"><div class="v2-metrica-valor">2</div><div class="v2-metrica-etiqueta">Facturas a vencer en 7 días</div></div>
        <div class="v2-metrica"><div class="v2-metrica-valor">1</div><div class="v2-metrica-etiqueta">Tareas vencidas</div></div>
      </div>
      <div class="v2-alerta v2-alerta--advertencia">1 factura vencida</div>
      <div class="v2-tarjeta panel">
        <div class="v2-titulo-seccion panel-titulo">Tareas que requieren atención</div>
        <div class="v2-fila-panel"><span class="v2-insignia v2-insignia--peligro">Vencida</span><span class="fila-titulo">Controlar stock del depósito</span><span class="v2-pie-conteo fila-dias">-3 días</span></div>
        <div class="v2-fila-panel"><span class="v2-insignia v2-insignia--advertencia">Próxima</span><span class="fila-titulo">Recibir pedido de luminarias</span><span class="v2-pie-conteo fila-dias">hoy</span></div>
        <span class="v2-enlace ver-todas">Ver todas las tareas →</span>
      </div>
      <div class="v2-tarjeta">
        <div class="v2-titulo-seccion panel-titulo">Accesos rápidos</div>
        <div class="v2-grilla-uniforme accesos">
          <div class="v2-acceso">{{mdi-package-variant}}<div><div class="v2-acceso-titulo">Productos</div><div class="v2-acceso-descripcion">Ver y administrar el catálogo</div></div></div>
          <div class="v2-acceso">{{mdi-tray-arrow-down}}<div><div class="v2-acceso-titulo">Registrar Entrada</div><div class="v2-acceso-descripcion">Cargar entradas de stock</div></div></div>
          <div class="v2-acceso">{{mdi-tray-arrow-up}}<div><div class="v2-acceso-titulo">Registrar Salida</div><div class="v2-acceso-descripcion">Cargar salidas de stock</div></div></div>
          <div class="v2-acceso">{{mdi-history}}<div><div class="v2-acceso-titulo">Historial de movimientos</div><div class="v2-acceso-descripcion">Consultar movimientos registrados</div></div></div>
        </div>
      </div>
    </div>
  </div>
</div>
</body>
</html>
```

`SIDEBAR_OPERADOR` = el `<aside class="v2-sidebar">…</aside>` de `docs/ui-v2/mockups/shell/sidebar-operador.html` tal cual, moviendo `v2-item-sidebar--activo` de "Gastos y facturas" a "Inicio".

- [ ] **Step 3: `inicio-operador-sin-finanzas.html`**

`<title>Inicio: Operador sin finanzas ni tareas (UI v2)</title>`. Cuerpo (el sidebar se escribe acá: es otro usuario):

```html
<body>
<!-- Datos: DatosInicio.OperadorSinFinanzasNiTareas() (Task 11): GestionarProductos y VerReportes. Sin fila
     de métricas (ningún gate se cumple), sin alertas, sin panel de tareas y sin "Ver calendario de pagos".
     Sidebar: Productos (INVENTARIO); ANÁLISIS sin "Historial por producto" (exige además RegistrarMovimientos). -->
<div class="v2-ventana">
  <aside class="v2-sidebar">
    <div class="v2-marca">Gestión Municipal</div>
    <div class="v2-marca-subtitulo">Municipio de Carmelo</div>
    <nav class="v2-sidebar-nav">
      <div class="v2-item-sidebar v2-item-sidebar--activo">{{mdi-home}}<span>Inicio</span></div>
      <div class="v2-header-seccion-sidebar">INVENTARIO</div>
      <div class="v2-item-sidebar">{{mdi-package-variant}}<span>Productos</span></div>
      <div class="v2-header-seccion-sidebar">ANÁLISIS</div>
      <div class="v2-item-sidebar">{{mdi-currency-usd}}<span>Valorización de inventario</span></div>
      <div class="v2-item-sidebar">{{mdi-chart-pie}}<span>Stock por categoría</span></div>
      <div class="v2-item-sidebar">{{mdi-trending-up}}<span>Productos más movidos</span></div>
      <div class="v2-item-sidebar">{{mdi-shield-search}}<span>Log de auditoría</span></div>
      <div class="v2-item-sidebar">{{mdi-chart-bar}}<span>Estadística de tareas</span></div>
    </nav>
    <div class="v2-sidebar-pie">
      <div class="v2-avatar">LS</div>
      <div class="v2-usuario">
        <div class="v2-usuario-nombre">Laura Silva</div>
        <div class="v2-usuario-rol">Operador</div>
      </div>
      <span class="v2-boton v2-boton--ghost v2-boton--icono">{{mdi-logout}}</span>
    </div>
    <div class="v2-version">v0.4.0</div>
  </aside>
  <div class="contenido">
    <img class="marca-agua" src="../../../../src/StockApp.Presentation/Assets/carmelo-original.png" alt="">
    <div class="pagina">
      <div class="v2-encabezado">
        <div class="v2-encabezado-fila">
          <div>
            <div class="v2-titulo-pagina">¡Bienvenido, Laura Silva!</div>
            <div class="v2-encabezado-subtitulo">martes 6 de octubre de 2026 · Sesión iniciada como Operador</div>
          </div>
        </div>
      </div>
      <div class="v2-tarjeta">
        <div class="v2-titulo-seccion panel-titulo">Accesos rápidos</div>
        <div class="v2-grilla-uniforme accesos">
          <div class="v2-acceso">{{mdi-package-variant}}<div><div class="v2-acceso-titulo">Productos</div><div class="v2-acceso-descripcion">Ver y administrar el catálogo</div></div></div>
          <div class="v2-acceso">{{mdi-currency-usd}}<div><div class="v2-acceso-titulo">Valorización de inventario</div><div class="v2-acceso-descripcion">Ver el valor total del stock</div></div></div>
          <div class="v2-acceso">{{mdi-shield-search}}<div><div class="v2-acceso-titulo">Log de auditoría</div><div class="v2-acceso-descripcion">Revisar la actividad del sistema</div></div></div>
        </div>
      </div>
    </div>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 4: `inicio-admin-sin-datos.html`**

`<title>Inicio: Administrador sin datos (UI v2)</title>`. Cuerpo: igual que `inicio-admin.html` del Step 1 (con `SIDEBAR_ADMIN` resuelto igual) salvo, entre el encabezado y los accesos:

```html
      <div class="v2-grilla-uniforme metricas-3">
        <div class="v2-metrica"><div class="v2-metrica-valor v2-metrica-valor--sin-dato">—</div><div class="v2-metrica-etiqueta">Facturas a vencer en 7 días</div></div>
        <div class="v2-metrica"><div class="v2-metrica-valor v2-metrica-valor--sin-dato">—</div><div class="v2-metrica-etiqueta">Tareas vencidas</div></div>
        <div class="v2-metrica"><div class="v2-metrica-valor v2-metrica-valor--sin-dato">—</div><div class="v2-metrica-etiqueta">Último backup</div></div>
      </div>
      <div class="v2-alerta v2-alerta--advertencia">No se pudo verificar el estado del backup.</div>
```

sin la alerta de error ni la `v2-tarjeta panel` de tareas (la consulta de tareas falló: el panel no se muestra), y con el comentario `<!-- Datos: DatosInicio.AdminSinDatos() (Task 11): los tres servicios fallan. Las métricas quedan sin dato ("—"), nunca en 0; la salud del backup es desconocida (advertencia), no vencida (error). -->`. Escribirlo completo (no por diferencia) para que el archivo se lea solo.

- [ ] **Step 5: Insertar íconos**

```bash
dotnet build tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj
python3 - docs/ui-v2/mockups/inicio/*.html <<'PY'
import pathlib, re, subprocess, sys
for ruta in sys.argv[1:]:
    p = pathlib.Path(ruta)
    html = p.read_text(encoding="utf-8")
    for clave in sorted(set(re.findall(r"\{\{(mdi-[a-z0-9-]+)\}\}", html))):
        svg = subprocess.run(
            ["dotnet", "run", "--no-build", "--project", "tools/StockApp.UiV2.Herramientas", "--", "icono-svg", clave],
            capture_output=True, text=True, check=True).stdout.strip()
        html = html.replace("{{" + clave + "}}", svg)
    p.write_text(html, encoding="utf-8")
PY
grep -c '{{mdi-' docs/ui-v2/mockups/inicio/*.html
```

Expected: `0` para cada archivo.

- [ ] **Step 6: Capturar y revisar**

```bash
for p in inicio-admin inicio-operador inicio-operador-sin-finanzas inicio-admin-sin-datos; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inicio/$p.html artifacts/ui-v2/mockups/inicio-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inicio/$p.html artifacts/ui-v2/mockups/inicio-$p@1366x768.png --tamano 1366x768
done
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~MockupsDelRepo_RespetanLaConvencion"
```

Expected: 8 PNG sin error de fuentes, de desborde ni de `.v2-ventana` (Task 2); el test de convención en verde. Revisar a ojo: a 1280×760 el contenido del Administrador entra sin scroll (estimado ~731 de alto; si no entra, la vista mostraría un pulgar de scroll que el mockup no tiene: avisar en el GATE); "Valorización de inventario" entra en su tarjeta de accesos a los dos tamaños; la fila de 2 métricas del operador ocupa todo el ancho.

- [ ] **Step 7: GATE — aprobación del usuario**

PARAR. Presentar al usuario los cuatro HTML (abrirlos en Chromium/Edge: `calc-size()` solo funciona ahí) o los 8 PNG de `artifacts/ui-v2/mockups/` (LOS DOS TAMAÑOS), junto con la lista D1 a D8 de "Decisiones que se confirman al aprobar los mockups". Señalarle explícitamente: el logo de fondo (U2), la disposición apilada (D1), "Ver calendario de pagos" arriba a la derecha (D3), la alerta de facturas vencidas (D4) y las métricas sin dato (D6). No seguir hasta tener "aprobado" explícito. Registrar en `.superpowers/sdd/2026-10-06-ui-v2-fase-2-inicio/progress.md` (crearlo si no existe): `Mockups Inicio: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 8: Commit**

```bash
git add docs/ui-v2/mockups/inicio/
git commit -m "docs(ui-v2): mockups aprobados de Inicio (Administrador, operador mixto, sin finanzas y sin datos)"
```

Si la aprobación cambió `base.css`, un componente o el texto que el VM produce, esos archivos y sus tests van en este mismo commit y el mensaje lo dice.

### Task 9: Mockups de la deuda: Reset con sus alertas y pulgar de scroll del sidebar del Administrador

Deudas (spec L364, L366): "Pulgar de scroll de Fluent (2 px) en el sidebar del Administrador que el mockup no tiene (P4)" y "Margin `0,1,0,0` de la card de Reset es estático: con `AlertaCompletado`/`AlertaError` visibles cambia la paridad del alto (sin mockup de esos estados)". Las dos cambian pantallas ya aprobadas: van al usuario.

**Medición del pulgar (hecha al escribir este plan, sobre `artifacts/ui-v2/comparacion/shell-sidebar-admin*/app.png` de la Fase 1):** columnas x = 223 y 224, color sólido `#7A7A7A` (122,122,122) en toda su altura y dibujado ENCIMA de la pastilla activa; filas y = 84 a 435 (352 px) a 1280×760 y y = 84 a 445 (362 px) a 1366×768. El Step 1 vuelve a medirlo antes de escribirlo.

**Files:**
- Modify: `docs/ui-v2/mockups/shell/sidebar-admin.html`
- Create: `docs/ui-v2/mockups/acceso/reset-completado.html`
- Create: `docs/ui-v2/mockups/acceso/reset-error.html`
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§6.5, lista de excepciones)

**Interfaces:**
- Consumes: `reset.html` (ya limpio por la Task 6), `ShellComparacionTests`.
- Produces: pantallas `acceso-reset-completado`, `acceso-reset-error` para la Task 12. Textos: "Listo. Volvé al login e ingresá con la nueva contraseña." (`ResetAdminView.axaml` L98) y "No se pudo resetear el Admin." (`ResetAdminViewModel` L98, texto de respaldo real).

- [ ] **Step 1: Volver a medir el pulgar**

```bash
dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~SidebarAdmin_IgualAlMockup"
mkdir -p "$SCRATCH/diag"
cat > "$SCRATCH/diag/pulgar.py" <<'PY'
import sys
from collections import Counter
from PIL import Image
base = sys.argv[1]
for d in ("shell-sidebar-admin", "shell-sidebar-admin@1366x768"):
    im = Image.open(f"{base}/{d}/app.png").convert("RGB")
    for x in (222, 223, 224, 225):
        filas = [y for y in range(60, im.size[1] - 90) if im.getpixel((x, y)) == (122, 122, 122)]
        print(d, x, (filas[0], filas[-1], len(filas)) if filas else None,
              Counter(im.getpixel((x, y)) for y in range(60, im.size[1] - 90)).most_common(2))
PY
(cd "$SCRATCH/diag" && python3 pulgar.py "$OLDPWD/artifacts/ui-v2/comparacion")
```

Expected: x = 223 y 224 con `(84, 435, 352)` en el estándar y `(84, 445, 362)` a 1366×768; x = 222 y 225 sin `(122,122,122)`. Si los números cambiaron, usar los medidos en el Step 2 y anotarlos en el ledger.

- [ ] **Step 2: Pulgar en `sidebar-admin.html`**

En el `<head>`, después de los `<link>`, agregar:

```html
<style>
  /* Pulgar de scroll de Fluent (P4, barra Auto): el ScrollViewer de la navegación del Administrador
     desborda y Fluent dibuja un indicador sólido de 2 px. Medido en el render (Fase 2, Task 9): x 223-224
     del sidebar, desde y 84, 352 px de alto a 760 y 362 px a 768 (crece con el viewport). Color de Fluent,
     no un token: es una compensación medida (spec §6.5), no un valor de diseño. */
  .v2-sidebar-pulgar { position: absolute; left: 223px; top: 84px; width: 2px; height: 352px; background: #7A7A7A; }
  @media (min-height: 768px) { .v2-sidebar-pulgar { height: 362px; } }
</style>
```

y, como ÚLTIMO hijo del `<aside class="v2-sidebar">` (después de `<div class="v2-version">v0.4.0</div>`, para que quede encima de la pastilla activa):

```html
    <div class="v2-sidebar-pulgar"></div>
```

- [ ] **Step 3: `reset-completado.html` y `reset-error.html`**

`docs/ui-v2/mockups/acceso/reset-completado.html` (el `reset.html` de la Task 6 con la alerta info después del botón primario, como en el `StackPanel` de `ResetAdminView.axaml`):

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Acceso: Recuperar acceso de Admin, completado (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  .v2-ventana { display: flex; align-items: center; justify-content: center; }
  .tarjeta-acceso { width: var(--v2-ancho-card-login); padding: 32px; display: flex; flex-direction: column; }
  .centro { text-align: center; }
  .mt-16 { margin-top: 16px; }
  .boton-completo { display: flex; width: 100%; }
  /* Código + "Copiar": Grid *,Auto con el botón a 8. El código en Inter con cifras tabulares. */
  .fila-codigo { display: flex; align-items: center; gap: 8px; }
  .codigo {
    flex: 1 1 0; min-width: 0; overflow-wrap: anywhere; font-variant-numeric: tabular-nums;
    font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-etiqueta);
  }
  /* TextBox AcceptsReturn de 88: padding 8x12, texto arriba (ControlesV2.axaml). */
  .multilinea { height: 88px; align-items: flex-start; padding: 8px 12px; }
</style>
</head>
<body>
<!-- Datos: DatosAcceso.Reset(completado: true) (Task 12). Deuda de la Fase 1: la card cambia de alto con la
     alerta; esta comparación mide si el Margin 0,1,0,0 de la card sigue siendo correcto en este estado. -->
<div class="v2-ventana">
  <div class="v2-tarjeta tarjeta-acceso">
    <div class="v2-titulo-login centro">Recuperar acceso de Admin</div>
    <span class="v2-boton v2-boton--secundario boton-completo mt-16">1) Generar código de recuperación</span>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Código de máquina</span></div>
      <div class="fila-codigo"><span class="codigo">7F3A-91C2-4D8E-B605</span><span class="v2-boton v2-boton--secundario">Copiar</span></div>
    </div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Desafío</span></div>
      <div class="fila-codigo"><span class="codigo">K4PZ-8QWN-2M7R</span><span class="v2-boton v2-boton--secundario">Copiar</span></div>
    </div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Token de reset (del proveedor)</span></div>
      <div class="v2-input multilinea"><span class="v2-input-placeholder">Pegá el token firmado</span></div>
    </div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Nueva contraseña de Admin</span></div>
      <div class="v2-input"><span class="v2-input-placeholder">Nueva contraseña</span></div>
    </div>
    <span class="v2-boton v2-boton--primario v2-boton--deshabilitado boton-completo mt-16">2) Resetear Admin</span>
    <div class="v2-alerta v2-alerta--info mt-16">Listo. Volvé al login e ingresá con la nueva contraseña.</div>
    <span class="v2-boton v2-boton--ghost boton-completo mt-16">Volver al login</span>
  </div>
</div>
</body>
</html>
```

`docs/ui-v2/mockups/acceso/reset-error.html`: el mismo archivo completo, con `<title>Acceso: Recuperar acceso de Admin, error (UI v2)</title>`, el comentario `<!-- Datos: DatosAcceso.Reset(error: "No se pudo resetear el Admin.") (Task 12). … -->` y, en lugar de la alerta info:

```html
    <div class="v2-alerta v2-alerta--error mt-16">No se pudo resetear el Admin.</div>
```

Antes de escribirlos, confirmar contra `ResetAdminView.axaml` que el orden real del `StackPanel` es: título, botón 1), código, desafío, token, nueva contraseña, botón 2), `AlertaCompletado`, `AlertaError`, "Volver al login". Si difiere, el mockup sigue al XAML.

- [ ] **Step 4: Capturar**

```bash
for p in reset-completado reset-error; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/acceso/$p.html artifacts/ui-v2/mockups/acceso-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/acceso/$p.html artifacts/ui-v2/mockups/acceso-$p@1366x768.png --tamano 1366x768
done
scripts/ui-v2/comparar.sh SidebarAdmin
```

Expected: 4 PNG; las dos comparaciones del sidebar del Administrador APROBADAS con menos píxeles distintos que antes (la Fase 1 dejó 3240 a 1280; anotar el antes y el después en el ledger). El pulgar es una columna de 2 px: no hace teselas densas ni antes ni después (2 de 16 columnas < 25%), así que ninguna métrica lo custodia; es fidelidad para el ojo del usuario, y así se anota.

- [ ] **Step 5: GATE — aprobación del usuario**

PARAR. Presentar: `sidebar-admin.html` con el pulgar (los dos tamaños) y los dos estados del Reset (los dos tamaños). No seguir hasta tener "aprobado" explícito. Registrar en el ledger: `Mockups deuda (pulgar, Reset con alertas): aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 6: Spec**

En §6.5, lista "Excepciones a 'px enteros, múltiplos de 4'", agregar: "- Pulgar de scroll del sidebar del Administrador en `sidebar-admin.html` (`.v2-sidebar-pulgar`): `#7A7A7A`, 2 px en x 223, desde y 84, 352 px de alto a 760 y 362 a 768 (media query). Es el indicador de Fluent medido en el render, no un token (Fase 2, Task 9)." Y en la deuda (L364), tachar el ítem del pulgar con "Cerrada (Fase 2, Task 9): el mockup lo replica."

- [ ] **Step 7: Commit**

```bash
git add docs/ui-v2/mockups/shell/sidebar-admin.html docs/ui-v2/mockups/acceso/reset-completado.html docs/ui-v2/mockups/acceso/reset-error.html docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): mockups aprobados del pulgar del sidebar y del Reset con sus alertas"
```

## Bloque C — ViewModel, XAML y comparaciones (cada task arranca solo con sus mockups aprobados)

### Task 10: `InicioViewModel`: métricas, fecha con reloj inyectable y notificación de gates

Cambios mínimos de VM permitidos por la spec §3 ("KPIs de Inicio"). No depende del GATE (no toca XAML) y puede adelantarse a la Task 8; los textos que produce son los de los mockups.

**Files:**
- Modify: `src/StockApp.Domain/Formato/FormatoEsUy.cs`
- Modify: `tests/StockApp.Domain.Tests/Formato/FormatoEsUyTests.cs`
- Modify: `src/StockApp.Presentation/ViewModels/InicioViewModel.cs`
- Create: `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelMetricasTests.cs`

**Interfaces:**
- Consumes: `PanelVencimientosTareas.Agrupar(IEnumerable<Tarea>, RolUsuario, int, DateTime ahoraUtc, TimeZoneInfo zonaLocal)`, `SaludBackupDto(DateTime? UltimoExitoEn, bool Vencido, int UmbralHoras)`, `CalendarioPagosDto`.
- Produces:
  - `FormatoEsUy.FechaLarga(DateOnly fecha) : string` ("martes 6 de octubre de 2026") y `FormatoEsUy.FechaHora(DateTime fecha) : string` ("04/10/2026 14:32").
  - `InicioViewModel(ICurrentSession, INavigationService, IFinanzasVistasService, IBackupsService, ITareaService, IAuthService, TimeProvider? reloj = null)`.
  - Propiedades nuevas: `string Subtitulo`, `bool MostrarFilaMetricas`, `string ValorFacturasAVencer`, `bool FacturasAVencerSinDato`, `bool HayFacturasVencidas`, `string ValorTareasVencidas`, `bool TareasVencidasSinDato`, `string? ValorUltimoBackup`, `bool UltimoBackupSinDato`.

- [ ] **Step 1: Tests de formato (rojo)**

En `FormatoEsUyTests.cs`, agregar:

```csharp
    // ── Fechas (Fase 2 de la UI v2: subtítulo de Inicio y último backup) ──────

    [Theory]
    [InlineData(2026, 10, 6, "martes 6 de octubre de 2026")]
    [InlineData(2026, 1, 1, "jueves 1 de enero de 2026")]
    [InlineData(2030, 3, 15, "viernes 15 de marzo de 2030")]
    [InlineData(2026, 10, 4, "domingo 4 de octubre de 2026")]
    public void FechaLarga_NombresEnCastellanoEnMinuscula(int anio, int mes, int dia, string esperado) =>
        Assert.Equal(esperado, FormatoEsUy.FechaLarga(new DateOnly(anio, mes, dia)));

    [Fact]
    public void FechaLargaYFechaHora_NoDependenDeLaCulturaDelHilo()
    {
        // en-US difiere de es-UY en nombres y en orden: si la salida dependiera de la cultura, cambiaría.
        var anterior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            Assert.Equal("martes 6 de octubre de 2026", FormatoEsUy.FechaLarga(new DateOnly(2026, 10, 6)));
            Assert.Equal("04/10/2026 14:32", FormatoEsUy.FechaHora(new DateTime(2026, 10, 4, 14, 32, 0)));
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }
```

Run: `dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj --filter "FullyQualifiedName~FormatoEsUyTests"`
Expected: FAIL (compilación: no existen `FechaLarga` ni `FechaHora`).

- [ ] **Step 2: Implementar el formato**

En `FormatoEsUy.cs`, después de `Decimal(...)`:

```csharp
    private static readonly string[] DiasSemana = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

    private static readonly string[] Meses =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "setiembre", "octubre", "noviembre", "diciembre"];

    /// <summary>
    /// Fecha larga en castellano, sin depender de ICU ni de la cultura del hilo: 2026-10-06 → "martes 6 de
    /// octubre de 2026" (subtítulo de Inicio). "setiembre" es la forma usual en Uruguay.
    /// </summary>
    public static string FechaLarga(DateOnly fecha) =>
        $"{DiasSemana[(int)fecha.DayOfWeek]} {fecha.Day} de {Meses[fecha.Month - 1]} de {fecha.Year}";

    /// <summary>Fecha y hora de 24 h, invariante: 2026-10-04 14:32 → "04/10/2026 14:32".</summary>
    public static string FechaHora(DateTime fecha) => fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
```

Ninguno de los datos fijos de este plan cae en septiembre. Si el usuario prefiere "septiembre", es un cambio de una palabra: preguntar en el GATE de la Task 8 solo si un mockup lo muestra.

Run: el mismo filtro. Expected: PASS.

- [ ] **Step 3: Tests del VM (rojo)**

`tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelMetricasTests.cs`:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// KPIs de Inicio v2 (spec §5.3): valor, estado sin dato y gates; fecha y horas desde un reloj inyectado.
/// </summary>
public class InicioViewModelMetricasTests
{
    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    // 2030: lejos del reloj real, así un VM que lea DateTime.UtcNow no puede coincidir por casualidad.
    private static readonly DateTimeOffset Ahora = new(2030, 3, 15, 13, 0, 0, TimeSpan.Zero);

    private static readonly UsuarioSesion Admin = new(1, "admin", RolUsuario.Admin, "Administrador General");
    private static readonly UsuarioSesion Operador = new(2, "juan", RolUsuario.Operador, "Juan Pérez");

    private static CalendarioPagosDto Calendario(int vencidas, int aVencer)
    {
        FacturaCalendarioDto Factura(int id) => new(id, "Proveedor", $"A-{id}", 100m, new DateOnly(2030, 3, 18), "Pendiente");
        return new CalendarioPagosDto(
            Enumerable.Range(1, vencidas).Select(Factura).ToList(),
            Enumerable.Range(100, aVencer).Select(Factura).ToList(),
            new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>());
    }

    private static Tarea Tarea(int id, DateTime limiteUtc) => new()
    {
        Id = id, Titulo = $"Tarea {id}", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = limiteUtc.AddDays(-30), FechaLimite = limiteUtc,
    };

    private sealed record Servicios(
        Mock<ICurrentSession> Sesion, Mock<IFinanzasVistasService> Finanzas, Mock<ITareaService> Tareas,
        Mock<IBackupsService> Backups, Mock<IAuthService> Auth);

    private static (InicioViewModel Vm, Servicios S) Crear(UsuarioSesion usuario, params string[] permisos)
    {
        var s = new Servicios(new(), new(), new(), new(), new());
        s.Sesion.Setup(x => x.UsuarioActual).Returns(usuario);
        s.Sesion.Setup(x => x.RolActual).Returns(usuario.Rol);
        s.Sesion.Setup(x => x.PermisosActuales).Returns(new HashSet<string>(permisos));
        s.Finanzas.Setup(x => x.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ReturnsAsync(Calendario(0, 0));
        s.Tareas.Setup(x => x.ListarAsync()).ReturnsAsync(new List<Tarea>());
        s.Backups.Setup(x => x.ObtenerSaludAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaludBackupDto(Ahora.UtcDateTime.AddHours(-1), false, 26));
        s.Auth.Setup(x => x.ObtenerPermisosPropiosAsync()).ReturnsAsync((IReadOnlySet<string>)new HashSet<string>(permisos));
        var vm = new InicioViewModel(s.Sesion.Object, Mock.Of<INavigationService>(), s.Finanzas.Object, s.Backups.Object,
            s.Tareas.Object, s.Auth.Object, new RelojFijo(Ahora));
        return (vm, s);
    }

    [Fact]
    public void Subtitulo_FechaLargaDelRelojYRol()
    {
        var (vm, _) = Crear(Operador);

        Assert.Equal("viernes 15 de marzo de 2030 · Sesión iniciada como Operador", vm.Subtitulo);
    }

    [Fact]
    public void AntesDeCargar_LasTresMetricasEstanSinDato()
    {
        // Review Focus 2: un "0" antes de consultar afirmaría que no hay nada vencido.
        var (vm, _) = Crear(Admin);

        Assert.True(vm.FacturasAVencerSinDato);
        Assert.True(vm.TareasVencidasSinDato);
        Assert.True(vm.UltimoBackupSinDato);
    }

    [Fact]
    public async Task CalendarioConDatos_ValorDeFacturasYAlertaDeVencidas()
    {
        var (vm, s) = Crear(Operador, Permisos.VerFinanzas);
        s.Finanzas.Setup(x => x.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ReturnsAsync(Calendario(1, 3));

        await vm.CargarAsync();

        Assert.False(vm.FacturasAVencerSinDato);
        Assert.Equal("3", vm.ValorFacturasAVencer);
        Assert.True(vm.HayFacturasVencidas);
        Assert.Equal("1 factura vencida", vm.TextoVencidas);
    }

    [Fact]
    public async Task CalendarioFalla_FacturasSinDato()
    {
        var (vm, s) = Crear(Operador, Permisos.VerFinanzas);
        s.Finanzas.Setup(x => x.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ThrowsAsync(new HttpRequestException("API caída"));

        await vm.CargarAsync();

        Assert.True(vm.FacturasAVencerSinDato);
        Assert.False(vm.HayFacturasVencidas);
    }

    [Fact]
    public async Task TareasFallan_TareasSinDato()
    {
        var (vm, s) = Crear(Operador, Permisos.GestionarTareas);
        s.Tareas.Setup(x => x.ListarAsync()).ThrowsAsync(new HttpRequestException("API caída"));

        await vm.CargarAsync();

        Assert.True(vm.TareasVencidasSinDato);
    }

    [Fact]
    public async Task ConRelojFijo_FechaDiasYBackupSalenDelReloj()
    {
        // Review Focus 3. Límite 14/03/2030 con el reloj en 15/03/2030: vencida hace 1 día. Con el reloj real
        // (2026) esa tarea estaría a años de vencer y no entraría al panel.
        var (vm, s) = Crear(Admin);
        s.Tareas.Setup(x => x.ListarAsync()).ReturnsAsync(new List<Tarea> { Tarea(1, new DateTime(2030, 3, 14, 0, 0, 0, DateTimeKind.Utc)) });
        s.Backups.Setup(x => x.ObtenerSaludAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaludBackupDto(new DateTime(2030, 3, 13, 14, 32, 0, DateTimeKind.Utc), true, 26));

        await vm.CargarAsync();

        Assert.False(vm.TareasVencidasSinDato);
        Assert.Equal("1", vm.ValorTareasVencidas);
        Assert.Equal("-1 día", Assert.Single(vm.TareasVencidas).TextoDiasParaVencer);
        Assert.False(vm.UltimoBackupSinDato);
        Assert.Equal("13/03/2030 14:32", vm.ValorUltimoBackup);
        Assert.Equal("El último backup exitoso fue el 13/03/2030 14:32 (hace más de 26 horas).", vm.TextoAvisoBackup);
    }

    [Fact]
    public async Task SinNingunBackup_UltimoBackupSinDato()
    {
        var (vm, s) = Crear(Admin);
        s.Backups.Setup(x => x.ObtenerSaludAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SaludBackupDto(null, true, 26));

        await vm.CargarAsync();

        Assert.True(vm.UltimoBackupSinDato);
        Assert.Null(vm.ValorUltimoBackup);
    }

    [Fact]
    public async Task BackupFalla_UltimoBackupSinDato()
    {
        var (vm, s) = Crear(Admin);
        s.Backups.Setup(x => x.ObtenerSaludAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("API caída"));

        await vm.CargarAsync();

        Assert.True(vm.UltimoBackupSinDato);
        Assert.True(vm.MostrarAvisoBackupDesconocido);
    }

    [Theory]
    [InlineData(false, new string[0], false)]
    [InlineData(false, new[] { Permisos.GestionarProductos, Permisos.VerReportes }, false)]
    [InlineData(false, new[] { Permisos.VerFinanzas }, true)]
    [InlineData(false, new[] { Permisos.GestionarTareas }, true)]
    [InlineData(true, new string[0], true)]
    public void MostrarFilaMetricas_SiAlgunaMetricaCumpleSuGate(bool esAdmin, string[] permisos, bool esperado)
    {
        var (vm, _) = Crear(esAdmin ? Admin : Operador, permisos);

        Assert.Equal(esperado, vm.MostrarFilaMetricas);
    }

    [Fact]
    public async Task RefrescarPermisos_NotificaLosGatesDeLasMetricas()
    {
        // Review Focus 5: sin estas notificaciones una métrica revocada en caliente sigue a la vista.
        var (vm, _) = Crear(Operador, Permisos.VerFinanzas, Permisos.GestionarTareas);
        var notificadas = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => notificadas.Add(e.PropertyName);

        await vm.CargarAsync();
        Assert.True(vm._tareaRefrescoPermisos.IsCompleted);
        await vm._tareaRefrescoPermisos;

        Assert.Contains(nameof(InicioViewModel.PuedeVerCalendarioPagos), notificadas);
        Assert.Contains(nameof(InicioViewModel.PuedeVerTareas), notificadas);
        Assert.Contains(nameof(InicioViewModel.MostrarFilaMetricas), notificadas);
    }

    [Fact]
    public void Contenedor_ResuelveElVmSinRegistrarUnReloj()
    {
        // App.axaml.cs registra AddTransient<InicioViewModel>() y ningún TimeProvider: el parámetro opcional
        // tiene que caer en su valor por defecto (TimeProvider.System), no romper la resolución.
        var servicios = new ServiceCollection()
            .AddSingleton(Mock.Of<ICurrentSession>())
            .AddSingleton(Mock.Of<INavigationService>())
            .AddSingleton(Mock.Of<IFinanzasVistasService>())
            .AddSingleton(Mock.Of<IBackupsService>())
            .AddSingleton(Mock.Of<ITareaService>())
            .AddSingleton(Mock.Of<IAuthService>())
            .AddTransient<InicioViewModel>()
            .BuildServiceProvider();

        Assert.NotNull(servicios.GetRequiredService<InicioViewModel>());
    }
}
```

`InlineData` con `Permisos.X` funciona porque son `const string` (`Permisos.cs` L18). Si `Microsoft.Extensions.DependencyInjection` no llega transitivamente a `StockApp.Presentation.Tests`, el test `Contenedor_…` no compila: en ese caso agregar `<PackageReference Include="Microsoft.Extensions.DependencyInjection" />` al `.csproj` de tests (sin versión: la versión ya está en `Directory.Packages.props`; verificarlo con `grep -n 'Microsoft.Extensions.DependencyInjection"' Directory.Packages.props`).

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~InicioViewModelMetricasTests"`
Expected: FAIL (compilación: el constructor no recibe `TimeProvider`, no existen las propiedades nuevas).

- [ ] **Step 4: Implementar en el VM**

En `InicioViewModel.cs`:

1. Usings: agregar `using StockApp.Domain.Formato;`; quitar `using StockApp.Presentation.Converters;` si queda sin uso tras el punto 6.
2. Campo, debajo de `_authService` (L38): `private readonly TimeProvider _reloj;`
3. Constructor (L190-201): agregar el parámetro `TimeProvider? reloj = null` al final y `_reloj = reloj ?? TimeProvider.System;` al final del cuerpo. Documentar en el `<summary>` del constructor: "reloj: fecha del subtítulo, días de las tareas y hora del último backup (Fase 2 UI v2, convención 8); null = TimeProvider.System, que es lo que usa la DI."
4. Debajo de `PuedeVerTareas` (L116-117):

```csharp
    /// <summary>Subtítulo de Inicio v2 (spec §5.3, "saludo y fecha"): fecha de hoy según el reloj y rol.</summary>
    public string Subtitulo =>
        $"{FormatoEsUy.FechaLarga(DateOnly.FromDateTime(_reloj.GetLocalNow().DateTime))} · Sesión iniciada como {RolTexto}";

    /// <summary>
    /// Fila de TarjetaMetrica de Inicio v2: visible si al menos una métrica cumple su gate (spec §5.3). Se
    /// deriva de los mismos Puede*/EsAdmin que cada tarjeta, igual que PuedeVerAccesosRapidos.
    /// </summary>
    public bool MostrarFilaMetricas => PuedeVerCalendarioPagos || PuedeVerTareas || EsAdmin;

    // Métricas de Inicio v2. "SinDato" arranca en true: hasta que la consulta termina bien la tarjeta muestra
    // "—", nunca un 0 que afirme algo (Review Focus 2 del plan de la Fase 2).
    [ObservableProperty] private bool _facturasAVencerSinDato = true;
    [ObservableProperty] private bool _tareasVencidasSinDato = true;
    [ObservableProperty] private bool _ultimoBackupSinDato = true;
    [ObservableProperty] private string? _valorUltimoBackup;

    public string ValorFacturasAVencer => FormatoEsUy.Cantidad(CantidadAVencer7Dias);
    public string ValorTareasVencidas => FormatoEsUy.Cantidad(CantidadTareasVencidas);
    public bool HayFacturasVencidas => CantidadVencidas > 0;
```

5. Atributos de notificación: a `_cantidadVencidas` (L121-123) agregar `[NotifyPropertyChangedFor(nameof(HayFacturasVencidas))]`; a `_cantidadAVencer7Dias` (L125-127) agregar `[NotifyPropertyChangedFor(nameof(ValorFacturasAVencer))]`; a `_cantidadTareasVencidas` (L166-170) agregar `[NotifyPropertyChangedFor(nameof(ValorTareasVencidas))]`.

6. En `CargarAsync`:
   - Dentro del `try` del calendario, después de `MostrarAvisoVencimientos = …;` (L232): `FacturasAVencerSinDato = false;`. En su `catch` (L234-237) y en el `else` (L239-242): `FacturasAVencerSinDato = true;`.
   - Reemplazar `PanelVencimientosTareas.Agrupar(tareas, rol, usuarioActualId)` (L257) por `PanelVencimientosTareas.Agrupar(tareas, rol, usuarioActualId, _reloj.GetUtcNow().UtcDateTime, _reloj.LocalTimeZone)`. Después de `CantidadTareasProximasAVencer = …;` (L266): `TareasVencidasSinDato = false;`. En su `catch` (L268-274) y en el `else` (L276-282): `TareasVencidasSinDato = true;`.
   - Reemplazar el bloque `if (salud.UltimoExitoEn is DateTime ultimo) { … } else { … }` (L298-310) por:

```csharp
            if (salud.UltimoExitoEn is DateTime ultimo)
            {
                // Hora LOCAL según el reloj (con TimeProvider.System es TimeZoneInfo.Local, el mismo criterio
                // que FechaUtcALocalConverter de MantenimientoView: un backup no muestra distinta hora según la
                // pantalla). Con un reloj fijo, la hora no depende de la máquina (convención 8, Fase 2).
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ultimo, DateTimeKind.Utc), _reloj.LocalTimeZone);
                ValorUltimoBackup = FormatoEsUy.FechaHora(local);
                UltimoBackupSinDato = false;
                TextoAvisoBackup =
                    $"El último backup exitoso fue el {FormatoEsUy.FechaHora(local)} (hace más de {salud.UmbralHoras} horas).";
            }
            else
            {
                ValorUltimoBackup = null;
                UltimoBackupSinDato = true;
                TextoAvisoBackup = "Todavía no se registró ningún backup exitoso.";
            }
```

   - En el `catch` del backup (L312-320) agregar `ValorUltimoBackup = null;` y `UltimoBackupSinDato = true;`.

7. En `RefrescarPermisosAsync` (L329-339), después de `OnPropertyChanged(nameof(PuedeVerAccesosRapidos));`:

```csharp
        // Gates de las métricas de Inicio v2 (Fase 2): sin esto una métrica revocada en caliente sigue visible.
        OnPropertyChanged(nameof(PuedeVerCalendarioPagos));
        OnPropertyChanged(nameof(PuedeVerTareas));
        OnPropertyChanged(nameof(MostrarFilaMetricas));
```

Run: `dotnet build StockApp.sln`, el filtro `InicioViewModelMetricasTests` y `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~InicioViewModelTests"`.
Expected: PASS; los tests viejos de `InicioViewModelTests` (incluido `CargarAsync_AdminConBackupVencido_UsaHoraLocalSinEtiquetaUtc`, que compara contra `ToLocalTime()` y `"dd/MM/yyyy HH:mm"`) siguen verdes.

- [ ] **Step 5: Mutación quitando**

```bash
cp src/StockApp.Presentation/ViewModels/InicioViewModel.cs "$SCRATCH/InicioViewModel.cs.bak"
```

(a) Quitar `, _reloj.GetUtcNow().UtcDateTime, _reloj.LocalTimeZone` de la llamada a `Agrupar` (vuelve al overload de reloj real). `git diff --no-index`. Rojo en `ConRelojFijo_FechaDiasYBackupSalenDelReloj` (la tarea de 2030 no entra al panel: `Assert.Single` falla). Restaurar.
(b) Quitar las tres líneas `OnPropertyChanged(nameof(PuedeVerCalendarioPagos|PuedeVerTareas|MostrarFilaMetricas))`. Rojo en `RefrescarPermisos_NotificaLosGatesDeLasMetricas`. Restaurar.
(c) Quitar el inicializador `= true` del campo `_facturasAVencerSinDato` (queda `private bool _facturasAVencerSinDato;`) → rojo en `AntesDeCargar_LasTresMetricasEstanSinDato` (`CalendarioFalla_FacturasSinDato` sigue verde: el `catch` lo vuelve a poner en `true`). Restaurar.
(d) Quitar `UltimoBackupSinDato = false;` del `if`: rojo en `ConRelojFijo_…` (`Assert.False(vm.UltimoBackupSinDato)`). Restaurar.

Pegar los rojos (a), (b), (c) y (d). Verde al final.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Domain/Formato/FormatoEsUy.cs tests/StockApp.Domain.Tests/Formato/FormatoEsUyTests.cs src/StockApp.Presentation/ViewModels/InicioViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelMetricasTests.cs
git commit -m "feat(inicio): métricas con estado sin dato, fecha del día y reloj inyectable"
```

(Si hubo que agregar el `PackageReference` de DI, el `.csproj` de tests va en el mismo commit.)

### Task 11: `InicioView` v2 con sus cuatro estados comparados y el logo custodiado aparte

**Depende de:** Tasks 7, 8 (mockups aprobados) y 10.

**Files:**
- Modify: `tests/StockApp.Presentation.VisualTests/Comparacion/RenderizadorVista.cs` (hook `preparar`)
- Modify: `tests/StockApp.Presentation.VisualTests/Comparacion/ComparacionVisual.cs` (`prepararApp`)
- Modify: `tests/StockApp.Presentation.VisualTests/RenderizadorVistaTests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Comparacion/DiferenciaDePixeles.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/LoginMarcaAguaTests.cs` (usa `DiferenciaDePixeles`)
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Shell/DatosShell.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inicio/DatosInicio.cs`, `InicioComparacionTests.cs`, `InicioMarcaAguaTests.cs`
- Modify: `src/StockApp.Presentation/Views/InicioView.axaml` (reescritura; `InicioView.axaml.cs` NO cambia: conserva el `DataContextChanged` → `CargarAsync`)
- Create: `tests/StockApp.Presentation.UiTests/V2/InicioViewV2Tests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/InicioViewTests.cs`, `InicioPanelTareasTests.cs`, `ReflexionVistaViewModelTests.cs`, `GuardianDePatronTests.cs`
- Modify: `docs/ui-v2/herramientas.md` (convenciones 6 y 8; `prepararApp`)

**Interfaces:**
- Consumes: `GrillaUniformeV2`, `V2TemaFilaPanel`, `TarjetaMetrica(Valor, Etiqueta, SinDato)`, `TarjetaAcceso(Icono, Titulo, Descripcion)`, `Insignia(Texto, Variante)`, `Alerta(Texto, Variante)`, `EncabezadoPagina(Titulo, Subtitulo, Acciones)`, `TarjetaContenido`; propiedades del VM de la Task 10; `ShellMainViewModel.CurrentContent`, `SeccionActiva`.
- Produces: `RenderizadorVista.RenderizarAPng(Control vista, object? viewModel, string rutaPng, Action<Control>? inspeccionar = null, TamanoLienzo? tamano = null, Action<Control>? preparar = null)`; `ComparacionVisual.CompararConMockup(..., IReadOnlyCollection<string>? ocultarEnMockup = null, Action<Control>? prepararApp = null)`; `DiferenciaDePixeles.Medir(string rutaA, string rutaB) : (int Cantidad, int DeltaMaximo, Rect Caja)`; `DatosShell.Crear(UsuarioSesion, string[] permisos, string seccionActiva, ViewModelBase? contenido = null)`; `DatosInicio.Admin()`, `OperadorMixto()`, `OperadorSinFinanzasNiTareas()`, `AdminSinDatos()` (todas `ShellMainViewModel`); `InicioComparacionTests.OcultarSidebarYMarcaDeAgua(Control)`; partes nombradas de `InicioView`: `MarcaAgua` (Image), `Encabezado`, `BotonCalendarioPagos`, `FilaMetricas` (GrillaUniformeV2), `MetricaFacturasAVencer`, `MetricaTareasVencidas`, `MetricaUltimoBackup`, `AlertaBackupVencido`, `AlertaBackupDesconocido`, `AlertaFacturasVencidas`, `PanelTareas`, `ListaTareasVencidas`, `ListaTareasProximas`, `EnlaceVerTodasLasTareas`, `PanelAccesosRapidos`, `BotonAccesoProductos`, `BotonAccesoRegistrarEntrada`, `BotonAccesoRegistrarSalida`, `BotonAccesoHistorialMovimientos`, `BotonAccesoValorizacion`, `BotonAccesoAuditoria`.

- [ ] **Step 1: Herramienta: hook `preparar` (rojo → verde)**

En `RenderizadorVistaTests.cs`, agregar:

```csharp
    [AvaloniaFact]
    public void Preparar_CorreAntesDeCapturar()
    {
        // Las pantallas de contenido ocultan el sidebar y la marca de agua DESPUÉS del primer layout (la vista
        // de contenido la crea ViewLocator) y ANTES de la captura (Fase 2, convención 6).
        var ruta = Path.Combine(Directory.CreateTempSubdirectory("preparar-").FullName, "vista.png");
        var vista = new Border { Background = Brushes.White };

        RenderizadorVista.RenderizarAPng(vista, null, ruta, preparar: v => ((Border)v).Background = Brushes.Red);

        using var imagen = SKBitmap.Decode(ruta);
        Assert.Equal(new SKColor(255, 0, 0), imagen.GetPixel(640, 380));
    }
```

(agregar `using Avalonia.Media;` y `using SkiaSharp;` si faltan).

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~Preparar_CorreAntesDeCapturar"` → FAIL (compilación).

En `RenderizadorVista.RenderizarAPng`: agregar el parámetro `Action<Control>? preparar = null` al final, documentarlo (`/// <param name="preparar">Corre con la ventana abierta después del primer layout y antes de capturar: para ocultar partes que la comparación excluye (sidebar, marca de agua).</param>`) y reemplazar las dos primeras vueltas por:

```csharp
        ventana.Show();
        // Dos vueltas: la primera procesa Loaded (donde algunas vistas cargan o setean estado) y
        // la segunda el layout que eso invalida. Sin la segunda, el frame puede salir sin ese cambio.
        Dispatcher.UIThread.RunJobs();
        preparar?.Invoke(vista);
        ventana.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
```

En `ComparacionVisual.CompararConMockup`: agregar `Action<Control>? prepararApp = null` al final de la firma, documentarlo (`/// <param name="prepararApp">Se aplica a la vista de la app antes de capturar (RenderizadorVista, preparar); en pantallas de contenido oculta el sidebar y la marca de agua. Se pasa siempre con argumento nombrado.</param>`) y pasarlo: `RenderizadorVista.RenderizarAPng(vista, viewModel, pngApp, v => recortes = GuardianTextoRecortado.Buscar(v, recortesPermitidos), tamano, prepararApp);`.

Run: el filtro y `--filter "Categoria=ComparacionVisual"`. Expected: PASS (nadie más usa `preparar`).

- [ ] **Step 2: Medición compartida de la marca de agua**

`tests/StockApp.Presentation.VisualTests/Comparacion/DiferenciaDePixeles.cs`:

```csharp
using Avalonia;
using SkiaSharp;

namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Diferencia exacta entre dos capturas del mismo tamaño: cuántos píxeles cambian, el mayor delta de canal y
/// la caja que los contiene. La usan las custodias de marca de agua (Login e Inicio): con y sin la marca, la
/// diferencia ES la marca.
/// </summary>
public static class DiferenciaDePixeles
{
    public static (int Cantidad, int DeltaMaximo, Rect Caja) Medir(string rutaA, string rutaB)
    {
        using var a = SKBitmap.Decode(rutaA);
        using var b = SKBitmap.Decode(rutaB);
        int cantidad = 0, delta = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                var d = Math.Max(Math.Abs(pa.Red - pb.Red), Math.Max(Math.Abs(pa.Green - pb.Green), Math.Abs(pa.Blue - pb.Blue)));
                if (d == 0)
                {
                    continue;
                }

                cantidad++;
                delta = Math.Max(delta, d);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return (cantidad, delta, cantidad == 0 ? default : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
    }
}
```

En `LoginMarcaAguaTests.cs`: borrar el método privado `Diferencias` (L91-118) y reemplazar su llamada (L61) por `var (cantidad, deltaMaximo, caja) = DiferenciaDePixeles.Medir(Png("con.png"), Png("sin.png"));`; quitar `using SkiaSharp;` si queda sin uso.

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~LoginMarcaAguaTests"` → PASS (mismo cálculo, otro lugar).

- [ ] **Step 3: Datos fijos**

En `DatosShell.cs`, reemplazar `private static ShellMainViewModel Crear(UsuarioSesion usuario, string[] permisos, string seccionActiva)` por la versión pública con contenido:

```csharp
    /// <summary>
    /// Shell con sesión fija. <paramref name="contenido"/>: el VM de la pantalla de contenido (lo resuelve
    /// ViewLocator, como en la app); null deja el área de contenido en fondo app (comparación del sidebar).
    /// </summary>
    public static ShellMainViewModel Crear(UsuarioSesion usuario, string[] permisos, string seccionActiva, ViewModelBase? contenido = null)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.UsuarioActual).Returns(usuario);
        sesion.Setup(s => s.RolActual).Returns(usuario.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));
        var vm = new ShellMainViewModel(sesion.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(i => i.Version == "0.4.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());
        vm.SeccionActiva = seccionActiva;
        vm.CurrentContent = contenido;
        return vm;
    }
```

`tests/StockApp.Presentation.VisualTests/Pantallas/Inicio/DatosInicio.cs`:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Inicio;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/inicio/*.html. Reloj fijo (martes 6 de octubre de 2026, 13:00, zona UTC):
/// subtítulo, días de cada tarea y hora del backup no cambian con el día ni con la PC (convención 8).
/// </summary>
public static class DatosInicio
{
    public static readonly DateTimeOffset Ahora = new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);

    public static readonly UsuarioSesion UsuarioAdmin = new(1, "mperez", RolUsuario.Admin, "María Pérez");
    public static readonly UsuarioSesion UsuarioOperadorMixto = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");
    public static readonly UsuarioSesion UsuarioSinFinanzasNiTareas = new(3, "lsilva", RolUsuario.Operador, "Laura Silva");

    public static readonly string[] PermisosMixtos =
        [Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas];

    public static readonly string[] PermisosSinFinanzasNiTareas = [Permisos.GestionarProductos, Permisos.VerReportes];

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static DateTime Dia(int desplazamiento) =>
        new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc).AddDays(desplazamiento);

    private static Tarea Tarea(int id, string titulo, int dias) => new()
    {
        Id = id, Titulo = titulo, Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = Dia(-30), FechaLimite = Dia(dias),
    };

    private static CalendarioPagosDto Calendario(int vencidas, int aVencer)
    {
        FacturaCalendarioDto Factura(int id, int dias) =>
            new(id, "Proveedor de prueba", $"A-{id:0000}", 1000m, DateOnly.FromDateTime(Dia(dias)), "Pendiente");
        return new CalendarioPagosDto(
            Enumerable.Range(1, vencidas).Select(i => Factura(i, -5)).ToList(),
            Enumerable.Range(100, aVencer).Select(i => Factura(i, 3)).ToList(),
            [], []);
    }

    /// <summary>calendario, tareas o salud en null: ese servicio falla ("API caída").</summary>
    public static InicioViewModel Inicio(UsuarioSesion usuario, string[] permisos,
        CalendarioPagosDto? calendario, IReadOnlyList<Tarea>? tareas, SaludBackupDto? salud)
    {
        var caida = new HttpRequestException("API caída");
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.UsuarioActual).Returns(usuario);
        sesion.Setup(s => s.RolActual).Returns(usuario.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));

        var finanzas = new Mock<IFinanzasVistasService>();
        if (calendario is null)
        {
            finanzas.Setup(f => f.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ThrowsAsync(caida);
        }
        else
        {
            finanzas.Setup(f => f.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ReturnsAsync(calendario);
        }

        var servicioTareas = new Mock<ITareaService>();
        if (tareas is null)
        {
            servicioTareas.Setup(t => t.ListarAsync()).ThrowsAsync(caida);
        }
        else
        {
            servicioTareas.Setup(t => t.ListarAsync()).ReturnsAsync(tareas);
        }

        var backups = new Mock<IBackupsService>();
        if (salud is null)
        {
            backups.Setup(b => b.ObtenerSaludAsync(It.IsAny<CancellationToken>())).ThrowsAsync(caida);
        }
        else
        {
            backups.Setup(b => b.ObtenerSaludAsync(It.IsAny<CancellationToken>())).ReturnsAsync(salud);
        }

        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.ObtenerPermisosPropiosAsync()).ReturnsAsync((IReadOnlySet<string>)new HashSet<string>(permisos));

        return new InicioViewModel(sesion.Object, Mock.Of<INavigationService>(), finanzas.Object, backups.Object,
            servicioTareas.Object, auth.Object, new RelojFijo());
    }

    private static readonly SaludBackupDto BackupVencido = new(new DateTime(2026, 10, 4, 14, 32, 0, DateTimeKind.Utc), true, 26);
    private static readonly SaludBackupDto BackupAlDia = new(new DateTime(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc), false, 26);

    public static ShellMainViewModel Admin() =>
        DatosShell.Crear(UsuarioAdmin, [], "Inicio", Inicio(UsuarioAdmin, [], Calendario(0, 3),
            [
                Tarea(1, "Reparar bache en Av. 18 de Julio", -2),
                Tarea(2, "Retirar poda zona Rambla", -1),
                Tarea(3, "Inspección de alumbrado público", 2),
                Tarea(4, "Mantenimiento plaza Independencia", 3),
            ],
            BackupVencido));

    public static ShellMainViewModel OperadorMixto() =>
        DatosShell.Crear(UsuarioOperadorMixto, PermisosMixtos, "Inicio", Inicio(UsuarioOperadorMixto, PermisosMixtos, Calendario(1, 2),
            [Tarea(5, "Controlar stock del depósito", -3), Tarea(6, "Recibir pedido de luminarias", 0)],
            BackupAlDia));

    public static ShellMainViewModel OperadorSinFinanzasNiTareas() =>
        DatosShell.Crear(UsuarioSinFinanzasNiTareas, PermisosSinFinanzasNiTareas, "Inicio",
            Inicio(UsuarioSinFinanzasNiTareas, PermisosSinFinanzasNiTareas, Calendario(0, 0), [], BackupAlDia));

    public static ShellMainViewModel AdminSinDatos() =>
        DatosShell.Crear(UsuarioAdmin, [], "Inicio", Inicio(UsuarioAdmin, [], null, null, null));
}
```

- [ ] **Step 4: Comparaciones y custodia del logo (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Inicio/InicioComparacionTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inicio;

[Trait("Categoria", "ComparacionVisual")]
public class InicioComparacionTests
{
    // Pantalla de contenido (convención 6): shell real, Inicio resuelto por ViewLocator. El sidebar ya tiene su
    // comparación (ShellComparacionTests) y la marca de agua su custodia (InicioMarcaAguaTests): se ocultan en
    // AMBOS renders sin mover el layout. Valores calibrados (11 teselas, 1.68%), sin UmbralAprobado.
    private static readonly string[] OcultarEnMockup = [".v2-sidebar", ".marca-agua"];

    public static void OcultarSidebarYMarcaDeAgua(Control vista)
    {
        var shell = (ShellMainView)vista;
        shell.FindControl<Border>("Sidebar")!.IsVisible = false;
        var inicio = shell.GetVisualDescendants().OfType<InicioView>().Single();
        inicio.FindControl<Image>("MarcaAgua")!.IsVisible = false;
    }

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebarYMarcaDeAgua);

    [AvaloniaFact]
    public void Admin_IgualAlMockup() => Comparar(DatosInicio.Admin(), "inicio/inicio-admin.html");

    [AvaloniaFact]
    public void Admin_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.Admin(), "inicio/inicio-admin.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void OperadorMixto_IgualAlMockup() => Comparar(DatosInicio.OperadorMixto(), "inicio/inicio-operador.html");

    [AvaloniaFact]
    public void OperadorMixto_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.OperadorMixto(), "inicio/inicio-operador.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void OperadorSinFinanzasNiTareas_IgualAlMockup() =>
        Comparar(DatosInicio.OperadorSinFinanzasNiTareas(), "inicio/inicio-operador-sin-finanzas.html");

    [AvaloniaFact]
    public void OperadorSinFinanzasNiTareas_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.OperadorSinFinanzasNiTareas(), "inicio/inicio-operador-sin-finanzas.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void AdminSinDatos_IgualAlMockup() => Comparar(DatosInicio.AdminSinDatos(), "inicio/inicio-admin-sin-datos.html");

    [AvaloniaFact]
    public void AdminSinDatos_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.AdminSinDatos(), "inicio/inicio-admin-sin-datos.html", TamanoLienzo.Municipio);
}
```

`tests/StockApp.Presentation.VisualTests/Pantallas/Inicio/InicioMarcaAguaTests.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inicio;

/// <summary>
/// Logo de Carmelo de fondo en Inicio (decisión del usuario, 2026-10-06): mismo tratamiento que el Login
/// (P5). La comparación de píxeles lo oculta (InicioComparacionTests); este es el único guardián de que sigue
/// estando, con su opacidad, su escala y su lugar: centrado en el ÁREA DE CONTENIDO, no en la ventana.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class InicioMarcaAguaTests
{
    private const double OpacidadAprobada = 0.28;
    private const double EscalaAprobada = 0.85;

    private static string Png(string nombre) => Path.Combine(ComparacionVisual.CarpetaSalida, "inicio-marca-agua", nombre);

    private static Image Marca(InicioView vista) =>
        vista.FindControl<Image>("MarcaAgua") ?? throw new InvalidOperationException("InicioView perdió Image#MarcaAgua.");

    private static void OcultarSidebar(Control vista) => ((ShellMainView)vista).FindControl<Border>("Sidebar")!.IsVisible = false;

    [AvaloniaFact]
    public void Marca_ConservaFuenteOpacidadEscalaYLugar_YSoloDifiereDentroDeSuRegion()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Png("x"))!);
        Size fuente = default;
        Rect area = default;

        RenderizadorVista.RenderizarAPng(new ShellMainView(), DatosInicio.Admin(), Png("con.png"), v =>
        {
            var inicio = v.GetVisualDescendants().OfType<InicioView>().Single();
            var marca = Marca(inicio);
            Assert.NotNull(marca.Source);
            fuente = marca.Source!.Size;
            Assert.Equal(new Size(2804, 1448), fuente);
            Assert.Equal(OpacidadAprobada, marca.Opacity, 3);
            var transformacion = Assert.IsAssignableFrom<ITransform>(marca.RenderTransform).Value;
            Assert.Equal(EscalaAprobada, transformacion.M11, 3);
            Assert.Equal(EscalaAprobada, transformacion.M22, 3);
            Assert.Equal(Stretch.Uniform, marca.Stretch);
            Assert.Equal(HorizontalAlignment.Center, marca.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, marca.VerticalAlignment);
            // Detrás del contenido: el ScrollViewer viene DESPUÉS en el Grid raíz.
            var panel = Assert.IsAssignableFrom<Panel>(marca.Parent);
            Assert.True(panel.Children.IndexOf(marca) < panel.Children.IndexOf(panel.Children.OfType<ScrollViewer>().Single()));
            var origen = inicio.TranslatePoint(default, TopLevel.GetTopLevel(inicio)!)!.Value;
            area = new Rect(origen, inicio.Bounds.Size);
        }, preparar: OcultarSidebar);

        RenderizadorVista.RenderizarAPng(new ShellMainView(), DatosInicio.Admin(), Png("sin.png"),
            preparar: InicioComparacionTests.OcultarSidebarYMarcaDeAgua);

        var (cantidad, deltaMaximo, caja) = DiferenciaDePixeles.Medir(Png("con.png"), Png("sin.png"));

        // Región: Uniform en el área de contenido (1040x760 a 1280), por la escala aprobada, centrada (+-2 px).
        var uniforme = Math.Min(area.Width / fuente.Width, area.Height / fuente.Height);
        var ancho = fuente.Width * uniforme * EscalaAprobada;
        var alto = fuente.Height * uniforme * EscalaAprobada;
        var esperada = new Rect(area.X + (area.Width - ancho) / 2, area.Y + (area.Height - alto) / 2, ancho, alto).Inflate(2);
        Assert.True(cantidad > 1000, $"La marca casi no se ve: {cantidad} píxeles difieren.");
        Assert.True(esperada.Contains(caja), $"La marca se dibuja fuera de su región: {caja} no está en {esperada}.");
        // Con opacidad 0.28 ningún canal puede moverse más de 0.28 * 255 = 71.4 respecto del fondo.
        Assert.InRange(deltaMaximo, 8, 72);
    }

    [Theory]
    [InlineData("inicio-admin")]
    [InlineData("inicio-operador")]
    [InlineData("inicio-operador-sin-finanzas")]
    [InlineData("inicio-admin-sin-datos")]
    public void Mockup_DeclaraLaMismaMarcaDeAguaQueLaApp(string mockup)
    {
        var html = File.ReadAllText(Path.Combine(RutasRepo.CarpetaMockups, "inicio", mockup + ".html"));

        Assert.Contains("carmelo-original.png", html);
        // Dentro de la regla, no en cualquier parte: el comentario también menciona 0.28 y scale(0.85).
        var regla = Regex.Match(html, @"\.marca-agua\s*\{(?<cuerpo>[^}]*)\}");
        Assert.True(regla.Success, "El mockup perdió la regla .marca-agua.");
        Assert.Contains("opacity: " + OpacidadAprobada.ToString(CultureInfo.InvariantCulture), regla.Groups["cuerpo"].Value);
        Assert.Contains("scale(" + EscalaAprobada.ToString(CultureInfo.InvariantCulture) + ")", regla.Groups["cuerpo"].Value);
    }
}
```

Las cotas (`> 1000` píxeles, delta entre 8 y 72) son las del Login; con las tarjetas blancas tapando buena parte del logo, la cantidad puede bajar. En el Step 9 se anotan los valores reales; si una cota no tiene sentido, se ajusta a una que siga fallando con opacidad 1, con la marca fuera de lugar y sin la marca (mutaciones (d) a (f) del Step 10).

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~InicioComparacionTests|FullyQualifiedName~InicioMarcaAguaTests"`
Expected: FAIL (la vista vieja no tiene `MarcaAgua`: el `FindControl` devuelve null y `OcultarSidebarYMarcaDeAgua` lanza; las comparaciones no coinciden).

- [ ] **Step 5: UiTests de la vista v2 (rojo)**

`tests/StockApp.Presentation.UiTests/V2/InicioViewV2Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Inicio v2 (spec §5.3, §8.3 "Gates de las tarjetas KPI de Inicio"). Gates SIEMPRE con Operador de permisos
/// mixtos: Admin cortocircuita el OR y no prueba nada (Admin_VeLasTresMetricas es solo control). View real.
/// </summary>
public class InicioViewV2Tests
{
    private sealed class FinanzasFake : IFinanzasVistasService
    {
        private static readonly FacturaCalendarioDto Factura = new(1, "Proveedor", "A-0001", 1000m, new DateOnly(2026, 10, 9), "Pendiente");

        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes) => throw new NotSupportedException();
        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio) => throw new NotSupportedException();
        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio) => throw new NotSupportedException();

        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null) =>
            Task.FromResult(new CalendarioPagosDto([Factura], [Factura, Factura], [], []));
    }

    private sealed class BackupsFake : IBackupsService
    {
        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) =>
            Task.FromResult(new SaludBackupDto(DateTime.UtcNow.AddHours(-30), true, 26));
        public Task IniciarAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                Width="1040" Height="760">
            <vistas:InicioView />
        </Window>
        """;

    private static readonly UsuarioSesion Operador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");
    private static readonly UsuarioSesion Admin = new(1, "mperez", RolUsuario.Admin, "María Pérez");

    private static (Window Ventana, InicioViewModel Vm) Montar(UsuarioSesion usuario, string[] permisos, IAuthService? auth = null)
    {
        var sesion = new SesionFake(usuario, permisos);
        var vm = new InicioViewModel(sesion, new NavigationServiceFake(), new FinanzasFake(), new BackupsFake(),
            new TareaServiceFake(), auth ?? new AuthServiceFake());
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        ventana.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return (ventana, vm);
    }

    private static T Nombrado<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static bool SeVe(Window ventana, string nombre) => ArbolVisual.EsVisibleEnArbol(Nombrado<Control>(ventana, nombre));

    [AvaloniaFact]
    public void Raiz_TieneUi2() =>
        Assert.Contains("ui2", Montar(Operador, [Permisos.VerFinanzas]).Ventana.GetVisualDescendants().OfType<InicioView>().Single().Classes);

    [AvaloniaFact]
    public void OperadorMixto_VeFacturasYTareasPeroNoElBackup()
    {
        var (ventana, _) = Montar(Operador,
            [Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas]);

        Assert.True(SeVe(ventana, "MetricaFacturasAVencer"));
        Assert.True(SeVe(ventana, "MetricaTareasVencidas"));
        Assert.False(SeVe(ventana, "MetricaUltimoBackup"));
    }

    [AvaloniaFact]
    public void OperadorSoloConVerFinanzas_VeSoloFacturas()
    {
        var (ventana, _) = Montar(Operador, [Permisos.VerFinanzas]);

        Assert.True(SeVe(ventana, "MetricaFacturasAVencer"));
        Assert.False(SeVe(ventana, "MetricaTareasVencidas"));
        Assert.False(SeVe(ventana, "MetricaUltimoBackup"));
        Assert.Equal("2", Nombrado<TarjetaMetrica>(ventana, "MetricaFacturasAVencer").Valor);
    }

    [AvaloniaFact]
    public void OperadorSoloConGestionarTareas_VeSoloTareas()
    {
        var (ventana, _) = Montar(Operador, [Permisos.GestionarTareas]);

        Assert.False(SeVe(ventana, "MetricaFacturasAVencer"));
        Assert.True(SeVe(ventana, "MetricaTareasVencidas"));
        Assert.False(SeVe(ventana, "MetricaUltimoBackup"));
    }

    [AvaloniaFact]
    public void OperadorSinFinanzasNiTareas_NoMuestraLaFilaDeMetricas()
    {
        var (ventana, _) = Montar(Operador, [Permisos.GestionarProductos, Permisos.VerReportes]);

        Assert.False(SeVe(ventana, "FilaMetricas"));
        Assert.False(SeVe(ventana, "BotonCalendarioPagos"));
    }

    [AvaloniaFact]
    public void Admin_VeLasTresMetricas()
    {
        // Control, no prueba de gate: Admin cortocircuita el OR.
        var (ventana, _) = Montar(Admin, []);

        Assert.True(SeVe(ventana, "MetricaFacturasAVencer"));
        Assert.True(SeVe(ventana, "MetricaTareasVencidas"));
        Assert.True(SeVe(ventana, "MetricaUltimoBackup"));
    }

    [AvaloniaFact]
    public void RevocacionEnCaliente_OcultaLaMetricaYLaOtraOcupaLaFila()
    {
        // Review Focus 5: el refresco de permisos (AuthServiceFake con revocación) saca VerFinanzas.
        var sesion = new SesionFake(Operador, Permisos.VerFinanzas, Permisos.GestionarTareas);
        var auth = new AuthServiceFake(sesion, new HashSet<string> { Permisos.GestionarTareas });
        var vm = new InicioViewModel(sesion, new NavigationServiceFake(), new FinanzasFake(), new BackupsFake(),
            new TareaServiceFake(), auth);
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        ventana.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm._tareaRefrescoPermisos.IsCompleted);
        Dispatcher.UIThread.RunJobs();

        Assert.False(SeVe(ventana, "MetricaFacturasAVencer"));
        var tareas = Nombrado<TarjetaMetrica>(ventana, "MetricaTareasVencidas");
        Assert.True(ArbolVisual.EsVisibleEnArbol(tareas));
        Assert.Equal(Nombrado<GrillaUniformeV2>(ventana, "FilaMetricas").Bounds.Width, tareas.Bounds.Width);
    }

    [AvaloniaFact]
    public void BotonCalendarioDePagos_ConVerFinanzas_UsaElComandoDelVm()
    {
        var (ventana, vm) = Montar(Operador, [Permisos.VerFinanzas]);
        var boton = Nombrado<Button>(ventana, "BotonCalendarioPagos");

        Assert.True(ArbolVisual.EsVisibleEnArbol(boton));
        Assert.Equal("Ver calendario de pagos", boton.Content);
        Assert.Contains("secundario", boton.Classes);
        Assert.Same(vm.IrACalendarioPagosCommand, boton.Command);
    }

    [AvaloniaFact]
    public void Accesos_OperadorSoloConRegistrarMovimientos_VeHistorialPeroNoEntradaNiSalida()
    {
        // Gate AND: Registrar Entrada/Salida exigen RegistrarMovimientos Y GestionarProductos.
        var (ventana, _) = Montar(Operador, [Permisos.RegistrarMovimientos]);

        Assert.True(SeVe(ventana, "BotonAccesoHistorialMovimientos"));
        Assert.False(SeVe(ventana, "BotonAccesoRegistrarEntrada"));
        Assert.False(SeVe(ventana, "BotonAccesoRegistrarSalida"));
        Assert.False(SeVe(ventana, "BotonAccesoProductos"));
    }

    [AvaloniaFact]
    public void Accesos_ConservanTitulosDescripcionesEIconosReales()
    {
        // Reemplaza el test de wrap del título de la vista vieja: el recorte lo custodia el guardián de
        // texto recortado en cada comparación, a los dos tamaños.
        var (ventana, _) = Montar(Admin, []);
        var esperado = new (string Nombre, string Titulo, string Descripcion, string Icono)[]
        {
            ("BotonAccesoProductos", "Productos", "Ver y administrar el catálogo", "mdi-package-variant"),
            ("BotonAccesoRegistrarEntrada", "Registrar Entrada", "Cargar entradas de stock", "mdi-tray-arrow-down"),
            ("BotonAccesoRegistrarSalida", "Registrar Salida", "Cargar salidas de stock", "mdi-tray-arrow-up"),
            ("BotonAccesoHistorialMovimientos", "Historial de movimientos", "Consultar movimientos registrados", "mdi-history"),
            ("BotonAccesoValorizacion", "Valorización de inventario", "Ver el valor total del stock", "mdi-currency-usd"),
            ("BotonAccesoAuditoria", "Log de auditoría", "Revisar la actividad del sistema", "mdi-shield-search"),
        };

        foreach (var (nombre, titulo, descripcion, icono) in esperado)
        {
            var acceso = Nombrado<TarjetaAcceso>(ventana, nombre);
            Assert.Equal(titulo, acceso.Titulo);
            Assert.Equal(descripcion, acceso.Descripcion);
            Assert.Equal(icono, acceso.Icono);
        }
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~InicioViewV2Tests"`
Expected: FAIL (no existen las partes nombradas ni `ui2` en la raíz).

- [ ] **Step 6: El XAML**

Reemplazar `src/StockApp.Presentation/Views/InicioView.axaml` entero por:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels"
             xmlns:tareasvm="using:StockApp.Presentation.ViewModels.Tareas"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.InicioView"
             x:DataType="vm:InicioViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <UserControl.Styles>
        <!-- Marca de agua del logo del municipio (decisión del usuario 2026-10-06): mismo tratamiento aprobado
             que el Login v2 (P5 de la Fase 1). Por Style (Priority=Style, no LocalValue) para no chocar con el
             guardián de opacidades literales. Su custodia es InicioMarcaAguaTests. -->
        <Style Selector="Image.marca-agua">
            <Setter Property="Opacity" Value="0.28" />
        </Style>
    </UserControl.Styles>

    <!-- UI v2 (spec §5.3). Espejo: docs/ui-v2/mockups/inicio/*.html. El logo va primero en el Grid: detrás
         del contenido. Cada bloque conserva su gate actual; la fila de métricas se oculta entera si ninguna
         métrica cumple el suyo. -->
    <Grid>
        <Image x:Name="MarcaAgua"
               Source="avares://GestionMunicipal/Assets/carmelo-original.png"
               Classes="marca-agua"
               Stretch="Uniform"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               RenderTransform="scale(0.85)" />

        <ScrollViewer x:Name="Desplazamiento" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
            <StackPanel Margin="28,24" Spacing="16">

                <v2:EncabezadoPagina x:Name="Encabezado" Titulo="{Binding Saludo}" Subtitulo="{Binding Subtitulo}" Margin="0">
                    <Button x:Name="BotonCalendarioPagos"
                            Classes="secundario"
                            Content="Ver calendario de pagos"
                            Command="{Binding IrACalendarioPagosCommand}"
                            IsVisible="{Binding PuedeVerCalendarioPagos}" />
                </v2:EncabezadoPagina>

                <!-- Métricas (spec §5.3): cada una SOLO con su gate. Sin dato = "—", nunca un 0 inventado. -->
                <v2:GrillaUniformeV2 x:Name="FilaMetricas" Espaciado="12" IsVisible="{Binding MostrarFilaMetricas}">
                    <v2:TarjetaMetrica x:Name="MetricaFacturasAVencer"
                                       Valor="{Binding ValorFacturasAVencer}"
                                       Etiqueta="Facturas a vencer en 7 días"
                                       SinDato="{Binding FacturasAVencerSinDato}"
                                       IsVisible="{Binding PuedeVerCalendarioPagos}" />
                    <v2:TarjetaMetrica x:Name="MetricaTareasVencidas"
                                       Valor="{Binding ValorTareasVencidas}"
                                       Etiqueta="Tareas vencidas"
                                       SinDato="{Binding TareasVencidasSinDato}"
                                       IsVisible="{Binding PuedeVerTareas}" />
                    <v2:TarjetaMetrica x:Name="MetricaUltimoBackup"
                                       Valor="{Binding ValorUltimoBackup}"
                                       Etiqueta="Último backup"
                                       SinDato="{Binding UltimoBackupSinDato}"
                                       IsVisible="{Binding EsAdmin}" />
                </v2:GrillaUniformeV2>

                <!-- Backup: vencido (error) y no se pudo verificar (advertencia) se distinguen (review final E1). -->
                <v2:Alerta x:Name="AlertaBackupVencido"
                           Variante="Error"
                           Texto="{Binding TextoAvisoBackup}"
                           IsVisible="{Binding MostrarAvisoBackupProblema}" />
                <v2:Alerta x:Name="AlertaBackupDesconocido"
                           Variante="Advertencia"
                           Texto="{Binding TextoAvisoBackup}"
                           IsVisible="{Binding MostrarAvisoBackupDesconocido}" />

                <v2:Alerta x:Name="AlertaFacturasVencidas" Variante="Advertencia" Texto="{Binding TextoVencidas}">
                    <v2:Alerta.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="PuedeVerCalendarioPagos" />
                            <Binding Path="HayFacturasVencidas" />
                        </MultiBinding>
                    </v2:Alerta.IsVisible>
                </v2:Alerta>

                <!-- Panel de tareas (spec 2026-08-06): oculto si no hay nada que mostrar o sin GestionarTareas. Cada
                     fila es un Button entero que navega al detalle. -->
                <v2:TarjetaContenido x:Name="PanelTareas">
                    <v2:TarjetaContenido.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="PuedeVerTareas" />
                            <Binding Path="MostrarPanelTareas" />
                        </MultiBinding>
                    </v2:TarjetaContenido.IsVisible>
                    <StackPanel>
                        <TextBlock Classes="titulo-seccion" Text="Tareas que requieren atención" Margin="0,0,0,12" />

                        <ItemsControl x:Name="ListaTareasVencidas"
                                      ItemsSource="{Binding TareasVencidas}"
                                      IsVisible="{Binding MostrarSeccionTareasVencidas}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="tareasvm:TareaFila">
                                    <Button Theme="{StaticResource V2TemaFilaPanel}"
                                            Command="{Binding $parent[UserControl].((vm:InicioViewModel)DataContext).VerTareaCommand}"
                                            CommandParameter="{Binding}">
                                        <Grid ColumnDefinitions="Auto,*,Auto">
                                            <v2:Insignia Grid.Column="0" Texto="Vencida" Variante="Peligro" />
                                            <TextBlock Grid.Column="1"
                                                       Margin="8,0,0,0"
                                                       VerticalAlignment="Center"
                                                       Text="{Binding Titulo}"
                                                       FontSize="{DynamicResource V2FuenteControlTamano}"
                                                       Foreground="{DynamicResource V2TextoPrimarioBrush}" />
                                            <TextBlock Grid.Column="2"
                                                       Classes="pie-conteo"
                                                       Margin="8,0,0,0"
                                                       VerticalAlignment="Center"
                                                       Text="{Binding TextoDiasParaVencer}" />
                                        </Grid>
                                    </Button>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>

                        <ItemsControl x:Name="ListaTareasProximas"
                                      ItemsSource="{Binding TareasProximasAVencer}"
                                      IsVisible="{Binding MostrarSeccionTareasProximasAVencer}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="tareasvm:TareaFila">
                                    <Button Theme="{StaticResource V2TemaFilaPanel}"
                                            Command="{Binding $parent[UserControl].((vm:InicioViewModel)DataContext).VerTareaCommand}"
                                            CommandParameter="{Binding}">
                                        <Grid ColumnDefinitions="Auto,*,Auto">
                                            <v2:Insignia Grid.Column="0" Texto="Próxima" Variante="Advertencia" />
                                            <TextBlock Grid.Column="1"
                                                       Margin="8,0,0,0"
                                                       VerticalAlignment="Center"
                                                       Text="{Binding Titulo}"
                                                       FontSize="{DynamicResource V2FuenteControlTamano}"
                                                       Foreground="{DynamicResource V2TextoPrimarioBrush}" />
                                            <TextBlock Grid.Column="2"
                                                       Classes="pie-conteo"
                                                       Margin="8,0,0,0"
                                                       VerticalAlignment="Center"
                                                       Text="{Binding TextoDiasParaVencer}" />
                                        </Grid>
                                    </Button>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>

                        <Button x:Name="EnlaceVerTodasLasTareas"
                                Theme="{StaticResource V2TemaEnlace}"
                                Content="Ver todas las tareas →"
                                Command="{Binding VerTodasLasTareasCommand}"
                                HorizontalAlignment="Right"
                                Margin="0,12,0,0" />
                    </StackPanel>
                </v2:TarjetaContenido>

                <!-- Accesos rápidos: los 6 actuales con sus gates (fix 2026-08-16 y 2026-08-17). -->
                <v2:TarjetaContenido x:Name="PanelAccesosRapidos" IsVisible="{Binding PuedeVerAccesosRapidos}">
                    <StackPanel>
                        <TextBlock Classes="titulo-seccion" Text="Accesos rápidos" Margin="0,0,0,12" />
                        <v2:GrillaUniformeV2 Columnas="3" Espaciado="12">
                            <v2:TarjetaAcceso x:Name="BotonAccesoProductos"
                                              Icono="mdi-package-variant"
                                              Titulo="Productos"
                                              Descripcion="Ver y administrar el catálogo"
                                              Command="{Binding IrAProductosCommand}"
                                              IsVisible="{Binding PuedeGestionarProductos}" />
                            <v2:TarjetaAcceso x:Name="BotonAccesoRegistrarEntrada"
                                              Icono="mdi-tray-arrow-down"
                                              Titulo="Registrar Entrada"
                                              Descripcion="Cargar entradas de stock"
                                              Command="{Binding IrARegistrarEntradaCommand}"
                                              IsVisible="{Binding PuedeRegistrarEntradaSalida}" />
                            <v2:TarjetaAcceso x:Name="BotonAccesoRegistrarSalida"
                                              Icono="mdi-tray-arrow-up"
                                              Titulo="Registrar Salida"
                                              Descripcion="Cargar salidas de stock"
                                              Command="{Binding IrARegistrarSalidaCommand}"
                                              IsVisible="{Binding PuedeRegistrarEntradaSalida}" />
                            <v2:TarjetaAcceso x:Name="BotonAccesoHistorialMovimientos"
                                              Icono="mdi-history"
                                              Titulo="Historial de movimientos"
                                              Descripcion="Consultar movimientos registrados"
                                              Command="{Binding IrAHistorialMovimientosCommand}"
                                              IsVisible="{Binding PuedeRegistrarMovimientos}" />
                            <v2:TarjetaAcceso x:Name="BotonAccesoValorizacion"
                                              Icono="mdi-currency-usd"
                                              Titulo="Valorización de inventario"
                                              Descripcion="Ver el valor total del stock"
                                              Command="{Binding IrAValorizacionCommand}"
                                              IsVisible="{Binding PuedeVerReportes}" />
                            <v2:TarjetaAcceso x:Name="BotonAccesoAuditoria"
                                              Icono="mdi-shield-search"
                                              Titulo="Log de auditoría"
                                              Descripcion="Revisar la actividad del sistema"
                                              Command="{Binding IrAAuditoriaCommand}"
                                              IsVisible="{Binding PuedeVerReportes}" />
                        </v2:GrillaUniformeV2>
                    </StackPanel>
                </v2:TarjetaContenido>

            </StackPanel>
        </ScrollViewer>
    </Grid>

</UserControl>
```

- [ ] **Step 7: Tests existentes, guardián de patrón y reflexión**

`tests/StockApp.Presentation.UiTests/InicioViewTests.cs`:
1. Reemplazar `BuscarBorderPorNombre` (L104-105) por `private static Control BuscarPorNombre(Window window, string nombre) => window.GetVisualDescendants().OfType<Control>().First(c => c.Name == nombre);` y actualizar el comentario de L98-103 ("los avisos son `Alerta` con nombre propio desde la UI v2").
2. Renombrar y adaptar los tres tests de backup:

```csharp
    [AvaloniaFact]
    public void Montar_AdminConBackupVencido_MuestraLaAlertaDeError()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow.AddHours(-30), true, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.True(vm.MostrarAvisoBackupProblema);
        var vencido = Assert.IsType<Alerta>(BuscarPorNombre(window, "AlertaBackupVencido"));
        Assert.True(vencido.IsVisible);
        Assert.Equal(VarianteAlerta.Error, vencido.Variante);
        Assert.False(BuscarPorNombre(window, "AlertaBackupDesconocido").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_AdminConBackupAlDia_NoMuestraNingunaAlerta()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.False(vm.MostrarAvisoBackup);
        Assert.False(BuscarPorNombre(window, "AlertaBackupVencido").IsVisible);
        Assert.False(BuscarPorNombre(window, "AlertaBackupDesconocido").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_ServicioDeBackupFalla_MuestraLaAlertaDeAdvertencia()
    {
        // Tercer estado (review final E1): "no se pudo verificar" NUNCA se ve como "vencido" (error).
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (window, vm) = Montar(usuario, new BackupsServiceFake(new InvalidOperationException("servidor caído")));

        Assert.True(vm.MostrarAvisoBackupDesconocido);
        var desconocido = Assert.IsType<Alerta>(BuscarPorNombre(window, "AlertaBackupDesconocido"));
        Assert.True(desconocido.IsVisible);
        Assert.Equal(VarianteAlerta.Advertencia, desconocido.Variante);
        Assert.False(BuscarPorNombre(window, "AlertaBackupVencido").IsVisible);
    }
```

(agregar `using StockApp.Presentation.Controls.V2;`).
3. En los tests de accesos: `BuscarBorderPorNombre(window, "BorderAccesosRapidos")` → `BuscarPorNombre(window, "PanelAccesosRapidos")` (tres usos). `BuscarBotonPorNombre` sigue igual (`TarjetaAcceso` es `Button`, los nombres `BotonAcceso*` se conservan).
4. Borrar `BuscarTextBlockPorTexto` y `Montar_CardValorizacionDeInventario_TituloEnvuelveEnVezDeCortarse` (L255-285): lo reemplaza `InicioViewV2Tests.Accesos_ConservanTitulosDescripcionesEIconosReales` más el guardián de recorte de cada comparación a los dos tamaños. `using Avalonia.Media;` puede quedar sin uso: borrarlo.

`tests/StockApp.Presentation.UiTests/InicioPanelTareasTests.cs`:
1. Reemplazar `BuscarBorderPorNombre` (L127-128) por `private static Control BuscarPorNombre(Window window, string nombre) => window.GetVisualDescendants().OfType<Control>().First(c => c.Name == nombre);` y `"BorderPanelTareas"` → `"PanelTareas"` en sus dos usos.
2. En `Montar_ConTareasVencidasYProximas_MuestraElPanelConLasFilasCorrectas`, reemplazar `Assert.Contains("VENCIDAS (2)", textos);` y `Assert.Contains("PRÓXIMAS A VENCER (1)", textos);` por:

```csharp
        // D5: la Insignia de cada fila reemplaza los subtítulos "VENCIDAS (n)" / "PRÓXIMAS A VENCER (n)".
        var insignias = window.GetVisualDescendants().OfType<Insignia>().ToList();
        Assert.Equal(2, insignias.Count(i => i.Texto == "Vencida" && i.Variante == VarianteInsignia.Peligro));
        Assert.Equal(1, insignias.Count(i => i.Texto == "Próxima" && i.Variante == VarianteInsignia.Advertencia));
```

(agregar `using StockApp.Presentation.Controls.V2;`). El resto (clicks reales en filas y en "Ver todas las tareas →") no cambia: las filas siguen siendo `Button` con el título adentro y el enlace conserva su `Content`.

`tests/StockApp.Presentation.UiTests/ReflexionVistaViewModelTests.cs`, en `InicioViewModel_TodaPropiedadYComando_TieneUnControlEnLaVista` (L143-172), reemplazar el `HashSet` de exclusiones por:

```csharp
        var exclusiones = new HashSet<string>
        {
            // Helpers consumidos indirectamente por otra propiedad bindeada:
            nameof(InicioViewModel.NombreUsuario),            // → Saludo
            nameof(InicioViewModel.RolTexto),                 // → Subtitulo (UI v2)
            nameof(InicioViewModel.CantidadVencidas),         // → HayFacturasVencidas y TextoVencidas
            nameof(InicioViewModel.CantidadAVencer7Dias),     // → ValorFacturasAVencer
            nameof(InicioViewModel.CantidadTareasVencidas),   // → ValorTareasVencidas, MostrarPanelTareas
            nameof(InicioViewModel.CantidadTareasProximasAVencer), // → MostrarPanelTareas
            nameof(InicioViewModel.MostrarAvisoBackup),       // → MostrarAvisoBackupProblema/Desconocido
            nameof(InicioViewModel.AvisoBackupEsDesconocido), // → idem
            // Sin consumidor en la vista v2 (Fase 2, decisiones D4 y D5); se borran en la Fase 9 (limpieza):
            nameof(InicioViewModel.MostrarAvisoVencimientos),
            nameof(InicioViewModel.TextoAVencer7Dias),
            nameof(InicioViewModel.TituloVencidas),
            nameof(InicioViewModel.TituloProximasAVencer),
        };
```

(`EsAdmin` sale de las exclusiones: ahora está bindeado en `MetricaUltimoBackup`.)

`tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`:
1. Borrar `[InlineData(typeof(InicioView), null, "INICIO")]` (L49).
2. Sacar `typeof(InicioView),` de `VistasDeLaTanda` (L109).
3. Agregar `typeof(InicioView),` a `VistasMigradasAV2` (L216-224).

- [ ] **Step 8: Verde**

Run: `dotnet build StockApp.sln`, `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`, `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj` y `scripts/ui-v2/comparar.sh Inicio`.
Expected: 0 errores de build (los compiled bindings validan cada `{Binding}` contra `InicioViewModel`/`TareaFila`); UiTests y VM en verde (incluidos `InicioViewV2Tests`, `InicioViewTests`, `InicioPanelTareasTests`, `ReflexionVistaViewModelTests`, `GuardianDePatronTests`, `VistaV2_NoTieneOpacidadesLiterales(InicioView)`); las 8 comparaciones de Inicio APROBADAS con los valores calibrados (11 teselas, 1.68%, SIN `UmbralAprobado`) y sin recortes; `InicioMarcaAguaTests` en verde.

Si una comparación a 1366×768 no pasa: abrir su página, mirar las teselas densas (contorno magenta) y diagnosticar con el script del Step 2 de la Task 13. Causas esperables y su arreglo (nunca subir el umbral): (1) la tercera columna corrida 1 px → un borde redondeado distinto en `GrillaUniformeV2` (comparar contra la regla `round(i*(celda+gap))`); (2) un medio píxel de centrado vertical de texto → compensación entera en ambos lados, anotada en §6.5; (3) el contenido no entra en 760 y aparece un pulgar de scroll en la vista → replicarlo en el mockup (como el del sidebar, Task 9) y volver al usuario con el cambio.

- [ ] **Step 9: Medición de respaldo del logo**

Anotar en el ledger los valores reales de `InicioMarcaAguaTests` (píxeles que difieren, delta máximo, caja) y, una sola vez, correr un test temporal idéntico a `Admin_IgualAlMockup` pero sin `.marca-agua` en `OcultarEnMockup` ni el `IsVisible = false` del logo: `Marca de agua visible en Inicio: X% / N teselas densas (no comparable: motivo de la exclusión)`. Borrar el test temporal. Guardar `artifacts/ui-v2/comparacion/inicio-marca-agua/con.png` para mostrárselo al usuario junto al mockup en la Task 14.

- [ ] **Step 10: Mutación quitando**

```bash
cp src/StockApp.Presentation/Views/InicioView.axaml "$SCRATCH/InicioView.axaml.bak"
```

Cada una: quitar, `git diff --no-index "$SCRATCH/InicioView.axaml.bak" src/StockApp.Presentation/Views/InicioView.axaml` (solo esa línea), correr, pegar el rojo, restaurar con `cp`.

(a) Quitar `IsVisible="{Binding PuedeVerTareas}"` de `MetricaTareasVencidas` → rojo en `OperadorSoloConVerFinanzas_VeSoloFacturas`.
(b) Quitar `IsVisible="{Binding EsAdmin}"` de `MetricaUltimoBackup` → rojo en `OperadorMixto_VeFacturasYTareasPeroNoElBackup` y en `OperadorMixto_IgualAlMockup` (aparece la tercera métrica).
(c) Quitar `IsVisible="{Binding MostrarFilaMetricas}"` de `FilaMetricas` → rojo en `OperadorSinFinanzasNiTareas_NoMuestraLaFilaDeMetricas` (la grilla vacía queda "visible" con alto 0).
(d) Quitar el `<Setter Property="Opacity" Value="0.28" />` del estilo de la marca → rojo en `Marca_ConservaFuenteOpacidadEscalaYLugar…`.
(e) Quitar `RenderTransform="scale(0.85)"` → rojo en el mismo test (escala y región). En el mockup: quitar `transform: scale(0.85);` de `.marca-agua` de `inicio-admin.html` (backup con `cp`) → rojo en `Mockup_DeclaraLaMismaMarcaDeAguaQueLaApp("inicio-admin")`. Restaurar.
(f) Quitar el `Image x:Name="MarcaAgua"` entero → rojo en `InicioMarcaAguaTests` y en las comparaciones (`OcultarSidebarYMarcaDeAgua` no encuentra la imagen: es el comportamiento buscado, fail-closed).
(g) Quitar `Variante="Error"` de `AlertaBackupVencido` → rojo en `Montar_AdminConBackupVencido_MuestraLaAlertaDeError` y en `Admin_IgualAlMockup` (la alerta pasa a info).
(h) Quitar `Classes="ui2"` de la raíz → rojo en `Raiz_TieneUi2` y en las 8 comparaciones.
(i) Quitar `Theme="{StaticResource V2TemaFilaPanel}"` de la fila de vencidas → rojo en `Admin_IgualAlMockup` (la fila toma la plantilla de `Button` de Fluent y el estilo global).
(j) En el VM (`cp src/StockApp.Presentation/ViewModels/InicioViewModel.cs "$SCRATCH/InicioViewModel.cs.bak"`), quitar `OnPropertyChanged(nameof(PuedeVerCalendarioPagos));` de `RefrescarPermisosAsync` → rojo en `RevocacionEnCaliente_OcultaLaMetricaYLaOtraOcupaLaFila`. Restaurar.
(k) En `InicioComparacionTests`, quitar `".v2-sidebar", ` de `OcultarEnMockup` → rojo en las 8 comparaciones (el sidebar del mockup aparece contra el fondo de la app): prueba que la exclusión es simétrica y necesaria. Restaurar.

- [ ] **Step 11: Documentación**

`docs/ui-v2/herramientas.md`, sección "Test por pantalla": documentar `prepararApp` y las convenciones 6 y 8 (texto de "Convenciones que heredan las fases siguientes"), con el ejemplo de `InicioComparacionTests.Comparar`.

- [ ] **Step 12: Commit**

```bash
git add src/StockApp.Presentation/Views/InicioView.axaml tests/StockApp.Presentation.VisualTests/Comparacion/ tests/StockApp.Presentation.VisualTests/RenderizadorVistaTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/ tests/StockApp.Presentation.UiTests/V2/InicioViewV2Tests.cs tests/StockApp.Presentation.UiTests/InicioViewTests.cs tests/StockApp.Presentation.UiTests/InicioPanelTareasTests.cs tests/StockApp.Presentation.UiTests/ReflexionVistaViewModelTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs docs/ui-v2/herramientas.md
git commit -m "feat(ui-v2): Inicio v2 con métricas, panel de tareas y accesos comparados, y el logo custodiado aparte"
```

### Task 12: Reset con sus alertas: comparación de los dos estados y compensación según la paridad

Deuda (spec L366). **Depende de:** Task 9 (mockups aprobados).

Predicción (a confirmar midiendo): las dos alertas suman un alto PAR a la card (16 de separación + 42 de una línea, o + 58 de dos líneas con `LineHeight` 16), así que la paridad no cambia y el `Margin="0,1,0,0"` estático sigue siendo correcto. Si la medición la contradice, se aplica el Step 4.

**Files:**
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/DatosAcceso.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/AccesoComparacionTests.cs`
- Modify (solo si el Step 4 aplica): `src/StockApp.Presentation/Views/ResetAdminView.axaml`
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§6.5, viñeta del Reset; deuda L366)

**Interfaces:**
- Consumes: mockups `acceso/reset-completado.html`, `acceso/reset-error.html`; `ResetAdminViewModel.Completado`, `MensajeError`.
- Produces: `DatosAcceso.Reset(bool completado = false, string? error = null)`.

- [ ] **Step 1: Datos y tests**

En `DatosAcceso.cs`, reemplazar `Reset()` por:

```csharp
    public static ResetAdminViewModel Reset(bool completado = false, string? error = null)
    {
        var vm = new ResetAdminViewModel(Mock.Of<IResetAdminService>());
        vm.CodigoMaquina = CodigoMaquina;
        vm.Desafio = Desafio;
        vm.Completado = completado;
        vm.MensajeError = error;
        return vm;
    }
```

En `AccesoComparacionTests.cs`, agregar:

```csharp
    // Deuda de la Fase 1: el Margin 0,1,0,0 de la card del Reset depende de la paridad de su alto; estos dos
    // estados la cambian (o no) al mostrar una alerta.
    [AvaloniaFact]
    public void ResetCompletado_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(completado: true), "acceso/reset-completado.html");

    [AvaloniaFact]
    public void ResetCompletado_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(completado: true), "acceso/reset-completado.html",
            tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ResetConError_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(error: "No se pudo resetear el Admin."),
            "acceso/reset-error.html");

    [AvaloniaFact]
    public void ResetConError_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(error: "No se pudo resetear el Admin."),
            "acceso/reset-error.html", tamano: TamanoLienzo.Municipio);
```

- [ ] **Step 2: Medir**

Run: `scripts/ui-v2/comparar.sh Reset`
Anotar en el ledger, para las 6 filas (`Reset`, `ResetCompletado`, `ResetConError` × 2 tamaños), el porcentaje y las teselas densas, y el alto de la card en cada estado (inspeccionar `TarjetaContenido.Bounds.Height` con un test temporal o leyendo la caja del borde en `app.png` con el script de la Task 13 Step 2). Expected: las 6 APROBADAS con lo calibrado.

- [ ] **Step 3: Mutación quitando (prueba de que el margen sigue haciendo falta)**

```bash
cp src/StockApp.Presentation/Views/ResetAdminView.axaml "$SCRATCH/ResetAdminView.axaml.bak"
```

Quitar `Margin="0,1,0,0"` de la `TarjetaContenido`. `git diff --no-index`. Correr `scripts/ui-v2/comparar.sh Reset`. Expected: rojo en `Reset_IgualAlMockup` (la Fase 1 midió 75 teselas sin el margen) y, si la paridad no cambió, también en los dos estados con alerta. Pegar el rojo y anotar qué estados se pusieron rojos. Restaurar con `cp`.

- [ ] **Step 4: Solo si algún estado con alerta falla en el Step 2 por un corrimiento de 1 px de toda la card**

El margen pasa de valor local a estilo, para poder anularlo en el estado de paridad opuesta. En `ResetAdminView.axaml`: quitar `Margin="0,1,0,0"` de la `TarjetaContenido`, darle `x:Name="CardReset"` y la clase condicional del estado que falló (ejemplo para "completado"; con "error" el binding es `MensajeError` con `StringConverters.IsNotNullOrEmpty`):

```xml
    <UserControl.Styles>
        <!-- Compensación medida (spec 6.5): con alto de card impar, centrarla da medio píxel; Avalonia
             redondea al par y Chromium hacia arriba. El píxel extra solo va cuando la paridad lo pide:
             medido en la Fase 2 (Task 12), el estado con alerta tiene la paridad opuesta. -->
        <Style Selector="v2|TarjetaContenido#CardReset">
            <Setter Property="Margin" Value="0,1,0,0" />
        </Style>
        <Style Selector="v2|TarjetaContenido#CardReset.paridad-opuesta">
            <Setter Property="Margin" Value="0" />
        </Style>
    </UserControl.Styles>
```

y en la card: `Classes.paridad-opuesta="{Binding Completado}"`. Volver a correr el Step 2: las 6 APROBADAS. Agregar a la Task la mutación: quitar `Classes.paridad-opuesta="…"` → rojo en el estado que lo necesitaba.

- [ ] **Step 5: Spec**

§6.5, viñeta "`Margin=\"0,1,0,0\"` en la `TarjetaContenido` de `ResetAdminView`…": agregar "Medido también con `AlertaCompletado` y `AlertaError` visibles (Fase 2, Task 12): <resultado: 'la paridad no cambia, el margen estático vale para los tres estados' o 'el estado X tiene la paridad opuesta y anula el margen por estilo'>". En la deuda (L366), tachar el ítem con "Cerrada (Fase 2, Task 12)".

- [ ] **Step 6: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/DatosAcceso.cs tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/AccesoComparacionTests.cs docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "test(ui-v2): el Reset se compara también con sus alertas de completado y de error"
```

(Si aplicó el Step 4, `src/StockApp.Presentation/Views/ResetAdminView.axaml` va en el mismo commit y el mensaje pasa a "fix(ui-v2): la compensación de la card del Reset sigue la paridad de cada estado".)

### Task 13: Márgenes de `Mensaje`, `Modal` y `LoginAccesoLimitado@1366`: diagnóstico por tesela

Deuda (spec L365): "`Mensaje_IgualAlMockup` (umbral 12 aprobado), `Modal_IgualAlMockup` y `LoginAccesoLimitado_IgualAlMockup_1366x768` pasan sin margen (tono de rasterizador); frágiles ante cambios de render". Última medición en `artifacts/` (Fase 1): `dialogos-mensaje` 12 de 12, `dialogos-mensaje@1366x768` 11 de 11, `actualizaciones-modal` 10 de 11. El objetivo NO es mover el umbral: es encontrar las teselas que sean geometría (y corregirlas con una compensación entera documentada) y dejar escrito, con números, lo que sea solo tono.

**Files:**
- Modify (solo si aparece geometría): el `.axaml` y/o el mockup de la pantalla afectada (`src/StockApp.Presentation/Views/Dialogs/MensajeDialog.axaml`, `src/StockApp.Presentation/Actualizaciones/Views/ActualizacionModalView.axaml`, `src/StockApp.Presentation/Views/LoginView.axaml`, y sus `docs/ui-v2/mockups/...html`)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (deuda L365; §6.5 si hay compensación nueva)

**Interfaces:**
- Consumes: páginas `artifacts/ui-v2/comparacion/<pantalla>/app.png` y `mockup.png`.
- Produces: diagnóstico por tesela en el ledger.

- [ ] **Step 1: Medir las seis corridas**

Run: `scripts/ui-v2/comparar.sh Mensaje`, `scripts/ui-v2/comparar.sh Modal` y `scripts/ui-v2/comparar.sh LoginAccesoLimitado`
Anotar en el ledger, por pantalla y tamaño: porcentaje, teselas densas y el máximo que se le aplica.

- [ ] **Step 2: Diagnóstico por tesela**

```bash
mkdir -p "$SCRATCH/diag"
cat > "$SCRATCH/diag/teselas.py" <<'PY'
"""Para cada tesela densa (misma métrica que spec 6.5: canal > 32 fuerte, > 4 leve; 16x16; densa si
>= 25% fuertes o >= 90% leves) busca el corrimiento (dx, dy) en [-2, 2] que mejor alinea la app con el
mockup en una ventana de 24x24 centrada en la tesela. Corrimiento (0, 0) = tono; otro = geometría candidata."""
import sys
from PIL import Image

carpeta = sys.argv[1]
app = Image.open(f"{carpeta}/app.png").convert("L")
mock = Image.open(f"{carpeta}/mockup.png").convert("L")
appc = Image.open(f"{carpeta}/app.png").convert("RGB")
mockc = Image.open(f"{carpeta}/mockup.png").convert("RGB")
W, H = app.size
T = 16

def densa(x0, y0):
    fuertes = leves = total = 0
    for y in range(y0, min(y0 + T, H)):
        for x in range(x0, min(x0 + T, W)):
            a, b = appc.getpixel((x, y)), mockc.getpixel((x, y))
            d = max(abs(a[i] - b[i]) for i in range(3))
            total += 1
            fuertes += d > 32
            leves += d > 4
    return fuertes >= 0.25 * total or leves >= 0.9 * total

def costo(x0, y0, dx, dy):
    s = 0
    for y in range(max(2, y0 - 4), min(H - 2, y0 + T + 4)):
        for x in range(max(2, x0 - 4), min(W - 2, x0 + T + 4)):
            s += abs(app.getpixel((x, y)) - mock.getpixel((x + dx, y + dy)))
    return s

for y0 in range(0, H, T):
    for x0 in range(0, W, T):
        if not densa(x0, y0):
            continue
        base = costo(x0, y0, 0, 0)
        mejor = min(((costo(x0, y0, dx, dy), dx, dy) for dx in range(-2, 3) for dy in range(-2, 3)))
        tipo = "tono" if (mejor[1], mejor[2]) == (0, 0) or mejor[0] > 0.8 * base else f"GEOMETRIA dx={mejor[1]} dy={mejor[2]}"
        print(f"tesela ({x0},{y0}): costo base {base}, mejor {mejor[0]} -> {tipo}")
PY
for p in dialogos-mensaje "dialogos-mensaje@1366x768" actualizaciones-modal "actualizaciones-modal@1366x768" acceso-login-acceso-limitado "acceso-login-acceso-limitado@1366x768"; do
  echo "== $p"; (cd "$SCRATCH/diag" && python3 teselas.py "$OLDPWD/artifacts/ui-v2/comparacion/$p")
done
```

Expected: una línea por tesela densa con su clasificación. Mirar cada una también a ojo en la página de comparación (contorno magenta): el script señala candidatas, la decisión es visual. Pegar la salida en el ledger.

- [ ] **Step 3: Corregir la geometría, si la hay**

Por cada tesela "GEOMETRIA" confirmada a ojo: identificar el elemento (texto, borde, botón) y su desfase; corregirlo con una compensación ENTERA en el `.axaml` o en el mockup (la que corresponda: el mockup manda cuando es diseño; la app cuando es una plantilla de Avalonia), igual que las de §6.5 (TextAlignment en vez de HorizontalAlignment para texto centrado, margen de 1 px por paridad, `UseLayoutRounding="False"` en un botón centrado de ancho fraccionario). Re-correr su comparación y verificar que bajan las teselas. Agregar cada compensación a la lista de §6.5 con su medición (antes y después). Si cambia algo visible del mockup aprobado, PARAR y mostrárselo al usuario antes del commit.

- [ ] **Step 4: Cerrar en la spec**

Reemplazar la viñeta de la deuda (L365) por el resultado, con números. Dos formas posibles, según lo medido:

- "~~…pasan sin margen…~~ Cerrada (Fase 2, Task 13): <pantalla> tenía <n> teselas de geometría (<qué>), corregidas con <compensación>; queda en <m> de <máx>."
- "~~…pasan sin margen…~~ Revisada (Fase 2, Task 13), tesela por tesela: todas las teselas densas de <pantallas> son tono de rasterizador (corrimiento óptimo 0,0), sin geometría que corregir. Siguen con margen <x>; un cambio de versión de Skia o de Chromium que mueva el tono puede ponerlas en rojo, y ese rojo se diagnostica con `teselas.py` (plan de la Fase 2, Task 13) antes de tocar ningún umbral."

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): diagnóstico por tesela de las comparaciones con margen chico"
```

(Si hubo compensaciones, sus `.axaml` y mockups van en el mismo commit y el mensaje pasa a "fix(ui-v2): …".)

## Bloque D — Cierre de la fase

### Task 14: Suite completa, comparación de toda la fase, verificación orgánica, aprobación final y spec

**Files:**
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (línea "Estado", §7 estado de la Fase 2 y lista de deuda)
- Modify (local, no se commitea): `.superpowers/sdd/2026-10-06-ui-v2-fase-2-inicio/progress.md`

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 2 cerrada en `feat/ui-v2` (sin merge a `main`).

- [ ] **Step 1: Build y suite completa**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
```

Expected: 0 errores de build; todo verde salvo los falsos conocidos de fixtures `.ods` en `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` (confirmar por nombre). Anotar el total.

- [ ] **Step 2: Herramientas**

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
```

Expected: exit 0 (esta fase no cambia `tokens.json`).

- [ ] **Step 3: Comparación de toda la fase**

```bash
scripts/ui-v2/comparar.sh
```

Expected: TODAS las filas de `artifacts/ui-v2/comparacion/index.html` APROBADAS a 1280×760 y 1366×768: catálogo (Fase 0), 2 shell, 7 acceso (5 de la Fase 1 + 2 estados del Reset), 3 diálogos, 4 actualizaciones y 4 de Inicio = 21 pantallas, 42 filas; ninguna con `UmbralAprobado` salvo `Mensaje` a 1280 (aprobado en la Fase 1) o las que el usuario apruebe explícitamente por el procedimiento de §6.5.

- [ ] **Step 4: Verificación orgánica (spec §6.4) — app real contra la base**

Preparación:
- `docker start stockapp-pg` (y dejarlo andando).
- `~/.config/GestionMunicipal/conexion.json` apuntando a `localhost`.
- Credencial de desarrollo: `admin` / `Admin1234`.
- WSLg levantado; herramientas de `scripts/gui-verificacion/` (xdotool sin root; capturas con PowerShell; la ventana se llama "Gestión Municipal"); fail-closed: si un popup se despliega fuera de la ventana, teclado y `Escape` antes de capturar.

Todo el recorrido con la ventana a **1366×768**; los pasos 1 y 2 se repiten al tamaño por defecto de la ventana. Capturar cada pantalla.

1. Administrador: entrar, ver Inicio. Contrastar cada métrica con su fuente: "Facturas a vencer en 7 días" contra la sección de 7 días de Calendario de pagos; "Tareas vencidas" contra las tareas vencidas del listado de Tareas; "Último backup" contra Mantenimiento (misma fecha y hora local). Logo de Carmelo semitransparente detrás del contenido y centrado en el área de contenido (U2: la comparación no lo ve, lo mira el usuario). Subtítulo con la fecha de HOY. Si el backup está vencido, la alerta es roja (error); si la API de backups no responde, ámbar (advertencia).
2. Click en cada acceso rápido (navega a su pantalla), en una fila del panel de tareas (abre el detalle de ESA tarea), en "Ver todas las tareas →" y en "Ver calendario de pagos".
3. Operador con permisos mixtos (Administración → Usuarios, crear o reutilizar uno con `GestionarProductos`, `RegistrarMovimientos`, `VerFinanzas` y `GestionarTareas`): ve "Facturas a vencer" y "Tareas vencidas", NO "Último backup"; ve Productos, Registrar Entrada, Registrar Salida e Historial; no ve Valorización ni Log de auditoría.
4. Revocación en caliente: con el Operador logueado, desde otra sesión de Administrador sacarle `VerFinanzas`; en el Operador navegar a otra pantalla y volver a Inicio: desaparecen la métrica de facturas y "Ver calendario de pagos", y "Tareas vencidas" ocupa la fila.
5. Operador sin finanzas ni tareas (solo `GestionarProductos` y `VerReportes`): sin fila de métricas, sin panel de tareas, sin botón de calendario; accesos Productos, Valorización y Log de auditoría.
6. Reset: desde el Login, "No puedo entrar / resetear Admin", pegar un token inválido y "2) Resetear Admin": aparece la alerta de error y la card no se ve corrida (comparar a ojo contra `reset-error.html`). El estado "completado" exige un token firmado válido: queda cubierto por su comparación (Step 3); anotarlo así.
7. Sidebar del Administrador: el pulgar de scroll se ve como en el mockup aprobado (Task 9).

Si algún paso no se puede hacer por el entorno (WSLg caído, contenedor que no arranca), PARAR y avisar al usuario con la evidencia (stderr, `docker ps -a`), sin declarar la fase cerrada.

- [ ] **Step 5: Aprobación final del usuario**

PARAR. Presentar: el `index.html` de comparación, las capturas de la verificación orgánica, `artifacts/ui-v2/comparacion/inicio-marca-agua/con.png` junto al mockup `inicio-admin.html` (el logo que la comparación no ve) y la lista de cambios respecto de los mockups aprobados (si hubo). La aprobación final es del usuario mirando la app (spec §6.4). Registrar en el ledger: `Fase 2: aprobada por <quién> <fecha>`.

- [ ] **Step 6: Spec**

1. Encabezado, línea `**Estado:**`: `**Estado:** aprobado por el usuario; Fases 0, 1 y 2 implementadas en feat/ui-v2; Fase 2 APROBADA por el usuario el <fecha> (merge a main al final de todas las fases)`.
2. En §7, después del párrafo "Estado de la Fase 1", agregar "**Estado de la Fase 2 (aprobada el <fecha>).** Inicio v2 (métricas con gates y sin dato, panel de tareas con insignias, accesos rápidos con ícono, alertas de backup, logo de fondo), comparado en 4 estados a los dos tamaños; verificación orgánica a 1366×768 con Administrador, operador mixto y operador sin finanzas ni tareas. Las 22 grillas con el tema viejo se resuelven al migrar cada vista (Fases 3 a 8)."
3. Título de la lista de deuda: "**Deuda menor de la Fase 1 (cerrada en la Fase 2 salvo Importación):**". Cada viñeta queda tachada con su "Cerrada (Fase 2, Task N)" (las Tasks 1-6, 9, 12 y 13 ya lo escribieron; completar las que falten: Task 1 guardián, Task 2 `.v2-ventana`, Task 3 alto de línea, Task 4 opacidad, Task 6 cosméticos), salvo Importación, que queda: "- Sin guardián propio de `EsAdmin` en Importación (hoy lo tapa `ImportacionHabilitada=false`). **Sigue ABIERTA mientras Importación esté apagada** (decisión del usuario, 2026-10-06: Importación no se toca); se resuelve si algún día se reactiva."

```bash
git add docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 2 (Inicio y deuda menor de la Fase 1)"
```

No se pushea ni se mergea.

---

## Dónde quedó cada ítem de la deuda menor de la Fase 1

| Ítem (spec L362-370) | Task | Cómo se cierra |
|---|---|---|
| Sin guardián propio de `EsAdmin` en Importación | — | **Fuera de alcance (U4).** Sigue abierta mientras Importación esté apagada. |
| Comparaciones no custodian el gate `EsVisible` del sidebar ni la ubicación de `ui2` | 5 | Custodia de los UiTests probada por mutación y escrita. |
| Pulgar de scroll de Fluent en el sidebar del Administrador | 9 | El mockup lo replica (medido); GATE del usuario. |
| `Mensaje` / `Modal` / `LoginAccesoLimitado@1366` sin margen | 13 | Diagnóstico tesela por tesela; geometría corregida o tono documentado con números. |
| Margin `0,1,0,0` del Reset estático frente a las alertas | 9 (mockups) + 12 | Comparación de los dos estados; compensación por estado si la paridad cambia. |
| `V2FuenteTituloPaginaAltoLinea` sin test de control real; `% 16` sobre double | 3 | Test en `EncabezadoPagina` real y en `.titulo-pagina` envuelto; `EsMultiploDeLinea` con tolerancia de medio píxel de dispositivo. |
| Guardián de recorte: "la ventana" desde el presentador de filas; TextBox multilínea; `.v2-ventana` sin fail-closed | 1 + 2 | Rama de la ventana probada con raíz que no recorta; multilínea relaja el vertical; capturador exige una `.v2-ventana` del tamaño del lienzo. |
| Sin chequeo de `Opacity` literal en vistas v2 | 4 | Teoría sobre `VistasMigradasAV2` + shell; escaneo del XAML de los diálogos con control positivo. |
| Cosméticos (`GrupoNavegacion.cs`, `ControlesV2.axaml`, `reset.html`) | 6 | Comentarios corregidos; CSS sin uso borrado. |

## Fuera de alcance

- **Importación (U4).** No se toca: ni código, ni tests que fuercen `ImportacionHabilitada`. La deuda "Sin guardián propio de `EsAdmin` en Importación" sigue abierta mientras esté apagada (Fase 8 o el día que se reactive).
- **Las 22 grillas con el tema viejo.** Inicio no tiene `DataGrid`; cada grilla se resuelve al migrar su vista (Fases 3 a 8). Sin tasks en esta fase.
- Cambios de API (los datos de los KPIs ya existen), stock bajo / `StockMinimo`, modo oscuro, back-stack (spec §10).
- Borrar los miembros de `InicioViewModel` que la vista v2 deja de usar (`MostrarAvisoVencimientos`, `TextoAVencer7Dias`, `TituloVencidas`, `TituloProximasAVencer`): Fase 9 (limpieza).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| A 1366 el ancho útil de la fila (1070 tras el padding de 28) no se divide en 3 columnas enteras; con `UniformGrid` la tercera tarjeta se corre 1 px. | `GrillaUniformeV2` (Task 7) redondea cada borde como Chromium; la diferencia restante es tono de texto en posición subpíxel (absorbida por la calibración). Si suma teselas, Task 11 Step 8 da el diagnóstico; nunca se sube el umbral. |
| Chromium trunca el track `1fr` a 1/64 de píxel y en un borde que cae justo en ,5 puede redondear distinto que `Math.Round`. | Probabilidad baja con los anchos de esta fase (medido a mano: ningún borde cae en ,5); si aparece, una tesela de borde en la comparación lo muestra y se ajusta `MidpointRounding`. |
| El Administrador no entra en 760 de alto (estimado ~731) y aparece un pulgar de scroll en Inicio que el mockup no tiene. | Task 8 Step 6 lo revisa al capturar; Task 11 Step 8 indica replicarlo en el mockup y volver al usuario. |
| El logo queda casi tapado por las tarjetas y `InicioMarcaAguaTests` mide pocos píxeles. | Task 11 Step 9 anota los valores reales y ajusta las cotas manteniendo los rojos de las mutaciones (d) a (f). |
| Copiar el `<aside>` del sidebar en cada mockup de contenido lo desactualiza si el sidebar cambia. | La comparación lo oculta (convención 6): un sidebar desactualizado en un mockup de contenido no rompe nada; el sidebar se compara solo en `ShellComparacionTests`. Al aprobar, el usuario ve el sidebar real en la verificación orgánica. |
| `TimeProvider` opcional y la DI: si se registrara algún `TimeProvider` en el contenedor, Inicio lo tomaría. | Hoy no hay ninguno registrado (`App.axaml.cs`); `Contenedor_ResuelveElVmSinRegistrarUnReloj` (Task 10) prueba la resolución. Registrar `TimeProvider.System` explícito sería equivalente. |
| Las teselas de tono de `Mensaje`/`Modal` no tienen corrección posible. | Task 13 lo deja escrito con números y con el procedimiento de diagnóstico para cuando un cambio de render las ponga en rojo; no se toca el umbral. |
| `.axaml` stale en `obj/` durante las mutaciones. | Global Constraints: `dotnet build-server shutdown` + `rm -rf` de `obj/bin`. |
| WSLg caído o `conexion.json` apuntando al VPS bloquean la verificación orgánica. | Task 14 Step 4: preparación explícita y PARAR con evidencia. |
| Una mutación que no se aplica (texto no encontrado) deja verde en silencio. | Global Constraints: `git diff --no-index` contra el backup ANTES de correr cada mutación. |
