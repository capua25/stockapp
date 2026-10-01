# UI v2 — Fase 1 Shell y acceso Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar al sistema v2 el cromo de la app (sidebar del shell), las cuatro pantallas fuera del shell (Login, Reset, BloqueoLicencia, AccesoLimitado), los diálogos `Window` genéricos y los tres overlays de actualización, cada pantalla comparada contra su mockup aprobado, sin tocar la lógica de navegación, permisos ni actualización.

**Architecture:** Primero se cierran los puntos ciegos de la herramienta de comparación que la Fase 0 dejó anotados (guardián de texto recortado en `TextPresenter`, `Inlines`, `MaxLines` y ancestros externos; convención `.v2-num > span`; breadcrumb sin recorte) y se agregan los componentes que el shell necesita (`ItemSidebar`, tipografías del sidebar y del login, botón ghost de ícono). Después se escriben los mockups por módulo (`shell/`, `acceso/`, `dialogos/`, `actualizaciones/`) y el usuario los aprueba antes de tocar XAML. Recién ahí se reescribe el XAML de cada vista en su mismo archivo, con su test de comparación en `tests/StockApp.Presentation.VisualTests/Pantallas/`. La clase `ui2` va en la raíz de cada vista migrada salvo donde la vista hospeda vistas no migradas (shell, AccesoLimitado), que la llevan solo en el contenedor propio.

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, Optris.Icons.Avalonia 12.0.7 (prefijo `mdi`), Microsoft.Playwright 1.63.0 (Chromium rev 1243), SkiaSharp 3.119.4, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`UiTests`, `VisualTests`).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§2, §4.1, §4.4, §5.4, §5.5, §6, §7 fila "1 Shell y acceso", §8, §9). Herramienta: `docs/ui-v2/herramientas.md`. Plan y ledger de la Fase 0: `docs/superpowers/plans/2026-09-28-ui-v2-fase-0-fundaciones.md`, `.superpowers/sdd/2026-09-28-ui-v2-fase-0-fundaciones/progress.md`.

## Decisiones del usuario (2026-10-01)

El usuario resolvió las seis preguntas abiertas del borrador. P1 a P4 confirman la opción que el plan proponía; P5 y P6 la CAMBIAN: la marca de agua del logo se CONSERVA en el Login y la línea "Municipio de Carmelo" se AGREGA bajo la marca. Ninguna task espera una respuesta: las que decían "Depende de: respuesta a P…" ya están escritas con la decisión.

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| P1 | **Sidebar transitorio (opción A).** ANÁLISIS lista los 6 reportes; ADMINISTRACIÓN lista los catálogos actuales (Categorías, Proveedores, Unidades de medida, Catálogos de tareas, Maestros de finanzas) más Usuarios y Mantenimiento, hasta que las Fases 6 y 7 los consoliden. | Sin cambios respecto del borrador. Los VM transitorios se reescriben en las Fases 6 y 7 (ver Riesgos). | 6, 10, 11 |
| P2 | **`ReclasificarTareaDialog` queda para la Fase 5**, junto con su panel. La Fase 1 migra los 3 diálogos genéricos (Confirmación, Mensaje, PedirTexto). | Sin cambios. | 8, 15 |
| P3 | **AccesoLimitado: en la Fase 1 solo se migra el aviso.** La comparación de la pantalla completa va en la Fase 7, cuando Mantenimiento ya sea v2. | Sin cambios. | 14 |
| P4 | **Scroll del sidebar del Administrador con barra `Auto`.** | Sin cambios: `ScrollViewer` con `VerticalScrollBarVisibility="Auto"`, marca arriba y pie abajo fijos; si Fluent deja una franja visible, el mockup la replica (Task 11, Step 6). La alternativa `Hidden` queda descartada. | 6, 11 |
| P5 | **Se MANTIENE la marca de agua del logo de Carmelo en el Login** (aprobada y mergeada a main el 2026-07-10). Es lo contrario de lo que proponía el borrador. | El mockup del Login (Task 7) la incluye y `LoginView` v2 la conserva (Task 12). Valores reales verificados en `LoginView.axaml`: `Image` de `avares://GestionMunicipal/Assets/carmelo-original.png` (PNG 2804×1448), `Stretch="Uniform"`, centrada, `Opacity` 0.28 por `Style Selector="Image.marca-agua"` (Priority=Style, no LocalValue) y `RenderTransform="scale(0.85)"`, primer hijo del `Grid` (detrás de la card). La comparación de píxeles la EXCLUYE y un test aparte la custodia: ver "Estrategia de comparación de la marca de agua (P5)". | 7, 12, 17 |
| P6 | **Se AGREGA la línea "Municipio de Carmelo" bajo la marca**, como en los mockups de referencia (`navegacion-aprobada.html`, `patrones-1.html`). | Clase tipográfica nueva `marca-subtitulo` (12/400 texto terciario `#78716C`, igual que `usuario-rol`; Task 5). En el sidebar va bajo "Gestión Municipal" (margen superior 2); en el Login va bajo el título (margen superior 2) y antes de "Ingresá tus credenciales…" (que conserva su margen de 16). Solo en shell y Login: `patrones-1.html` no la muestra en Reset ni en BloqueoLicencia. | 5, 6, 7, 11, 12 |

### Estrategia de comparación de la marca de agua (P5)

El problema. Una imagen de 2804×1448 con `Uniform` en 1280×760 mide 1280×661; con `scale(0.85)` queda de 1088×562, centrada (x 96–1184, y 99–661). Skia y Chromium la remuestrean con filtros distintos y mezclan la opacidad 0.28 con distinta gamma: la diferencia es leve (más de 4 de canal) pero cubre unos 1088×562 px, del orden de 2.400 teselas de 16×16. Es una estimación: la Task 12 Step 6 la mide y la registra en el `progress.md` (con la marca visible, solo como evidencia, sin commitear el resultado).

Lo que se descarta, con la regla de la spec §6.5:

- **Subir el umbral global:** prohibido (regla 1 de "Umbral por pantalla").
- **`UmbralAprobado` para el Login:** 16 o más teselas exige `MutacionPalidaEnRojo` (regla 3), y un umbral de miles de teselas dejaría pasar M1 a M4 (28 a 262 teselas, tabla de la calibración): la pantalla quedaría ciega a cualquier defecto de color o layout. Además la regla 2 pide que el usuario revise CADA tesela densa, y miles no se revisan.

Lo que se hace (la más estricta que respeta §6.5): **la marca de agua se excluye de AMBOS renders de la comparación de píxeles y se custodia aparte.**

1. Las tres comparaciones del Login (`login`, `login-error`, `login-acceso-limitado`) corren con los valores calibrados (11 teselas, 1.68%), sin `UmbralAprobado`, sobre la pantalla SIN marca de agua en la app y en el mockup: el resto de la pantalla (card, textos, alertas, botones) conserva la sensibilidad de siempre.
2. Cómo se oculta, sin mover un píxel de layout: en el mockup, `visibility: hidden` sobre `.marca-agua` (parámetro nuevo `ocultarSelectores` de `CapturadorMockup` y `ocultarEnMockup` de `ComparacionVisual`, Task 12 Step 1; falla si el selector no encuentra nada, para que un typo no compare con el logo puesto); en la app, el test pone `IsVisible = false` en `Image#MarcaAgua` antes de renderizar.
3. `LoginMarcaAguaTests` (Task 12 Step 3) custodia la marca: que existe y es la imagen correcta, que su opacidad efectiva es 0.28 y su escala 0.85, `Stretch` `Uniform`, centrada y DETRÁS de la card, que una captura con y sin marca difiere solo dentro de la región esperada (1088×562 centrada, 2 px de tolerancia) y que esa diferencia no supera lo que permite una opacidad de 0.28 (delta máximo de canal 72) ni es nula. Otro test lee `login.html` y exige que el mockup declare los mismos 0.28 y 0.85, para que app y mockup no diverjan sin que nada lo vea.
4. El usuario ve la marca con el logo puesto: al aprobar los mockups (Task 7 Step 7), en la página de comparación (el `app.png` y el `mockup.png` salen sin marca; el procedimiento de la Task 12 Step 6 le pide mirar además la captura de la app SIN ocultar) y en la verificación orgánica (Task 17 Step 4).

Riesgo residual, aceptado: la posición exacta de la marca entre Skia y Chromium no se compara píxel a píxel. Lo acotan el test de la región y de la escala, el valor idéntico en el mockup y la revisión visual del usuario; no se compensa con umbral.

## Decisiones que se confirman al aprobar cada mockup (Tasks 6 a 9)

No bloquean el arranque: son detalles visuales que la spec no fija y que el usuario resuelve mirando el mockup. Si cambia alguno, se ajusta el mockup Y el componente/estilo en la misma task del mockup, antes de la task de XAML.

- Sidebar: ícono de ítem inactivo en texto terciario `#78716C`; pie con avatar de 28 (fondo `#D6D3D1`, iniciales 11/600), nombre 12/500 primario, rol 12/400 terciario, cerrar sesión como botón ghost de solo ícono (32×32, tooltip "Cerrar sesión") y versión 12/400 en texto grupo. Rol en texto: "Administrador" / "Operador".
- Login y pantallas fuera del shell: card de 400 con padding 32; "Entrar" deshabilitado con los campos vacíos (estado real del botón); el link "No puedo entrar / resetear Admin" y "¿Necesitás bajar un backup? Iniciar sesión" con apariencia de enlace (`V2TemaEnlace`) en vez de botón ghost; el código de máquina y el desafío en Inter con cifras tabulares (hoy `Consolas,monospace`, que no está en los mockups); el éxito del Reset como `Alerta` info (la spec no tiene variante éxito).
- Diálogos: padding 24, mensaje en cuerpo 14, botones con ancho por contenido (hoy fijo 100/110).
- Overlays: banner sobre fondo info; modal y bloqueo crítico como `TarjetaContenido` de 480; "Aplicar y reiniciar" como `primario` (hoy `danger`: no es destructivo y es la única acción); modo degradado como franja de error. Textos sin tilde que se conservan tal cual salvo pedido explícito: "Actualizacion critica requerida", "Reintentara".

## Global Constraints

- Tema claro fijo, sin modo oscuro (spec §2, §10).
- Fondo app `#F7F7F5` · Superficie `#FFFFFF` · Borde `#E7E5E4` · Borde input `#D6D3D1` · Separador de grilla `#F0EFED` · Header de grilla `#FAFAF9`.
- Acento `#1D4ED8` · Acento hover `#1E40AF` · Acento suave `#DBEAFE` · Acento oscuro `#1E3A8A`.
- Texto primario `#1C1917` · secundario `#57534E` · terciario `#78716C` · etiqueta `#44403C` · grupo `#A8A29E`.
- Peligro texto `#B91C1C` · Borde error `#DC2626`.
- Alertas (fondo/borde/texto): Advertencia `#FEF3C7`/`#FDE68A`/`#92400E` · Error `#FEE2E2`/`#FCA5A5`/`#991B1B` · Info `#DBEAFE`/`#DBEAFE`/`#1E3A8A`.
- Radios: 8 (inputs, botones, pastilla activa, alertas, `TarjetaAcceso` y el borde de la grilla), 10 (`TarjetaContenido` y `TarjetaMetrica`), 999 (insignias), 4 (casilla del `CheckBox`). El 8 de `TarjetaAcceso` y de la grilla está aprobado (2026-09-30): no se reabre.
- Sombra de card: `0 1px 2px rgba(0,0,0,0.05)` — la única sombra del sistema.
- Alturas: input y botón 36; fila de grilla 40; header de grilla 36; ítem de sidebar 32.
- Espaciado base 4: todo margen, padding y gap de diseño es múltiplo de 4 (excepción: padding vertical 2 de la `Insignia`; compensaciones medidas listadas en spec §6.5).
- Anchos fijos: sidebar 240; card de formulario máx. 760; card de login 400.
- Tipografía: marca 15/700 · título de login 20/600 · título de sección 15/600 · cuerpo 14/400 · texto de controles 13 · etiqueta de campo 12/500 · ítem de sidebar 12/500 (activo en acento oscuro) · header de sección del sidebar 11/600 MAYÚSCULAS letter-spacing 0.6 en texto grupo.
- Sidebar (§4.1): 240, fondo blanco con borde derecho, secciones SIEMPRE visibles (sin acordeones), ítem activo en pastilla acento suave con texto acento oscuro e ícono acento. Cada ítem conserva su gate actual (`Puede*`/`EsAdmin`, AND compuestos incluidos). El header de una sección se oculta si no tiene ítems visibles. La revocación en caliente se mantiene. Importación sigue apagada (`ImportacionHabilitada=false`): el ítem existe pero queda oculto. Pie: avatar con iniciales, nombre y rol, cerrar sesión (con la confirmación actual) y versión.
- Pantallas fuera del shell (§5.4): card centrada de 400 sobre fondo app, marca, textos reales ("Entrar", "No puedo entrar / resetear Admin"), la línea "Municipio de Carmelo" bajo la marca del shell y del Login (P6), la marca de agua del logo en el Login (P5, se conserva), `Alerta` de error y TODOS los estados actuales, incluido el aviso de "Modo de acceso limitado".
- Diálogos y overlays (§5.5): mismo sistema (tokens, botones, `Alerta`). Los diálogos siguen sin bindings: acceso por `x:Name`. Los overlays se rediseñan visualmente sin cambiar su lógica (§4.5).
- Textos reales: ningún texto visible cambia salvo los que la spec renombra en el sidebar (§4.1: "Registrar entrada", "Registrar salida", "Documentos") y los títulos de sección.
- No cambian: `ViewLocator`, `INavigationService`, el wiring de `DataContextChanged`, el seam `ErrorConverter`, la lógica de permisos, los `Window` de diálogo y su acceso por `x:Name`. No se renombran ni se mueven vistas. Los ViewModels quedan intactos salvo lo mínimo: secciones del sidebar y datos del usuario en el pie (§3).
- `Classes="ui2"` en la RAÍZ de cada vista migrada y NUNCA en un contenedor que hospede vistas no migradas: en `ShellMainView` va en el `Border` del sidebar (no en el `Grid` raíz, que contiene el `ContentControl` de contenido); en `AccesoLimitadoView` va en el contenedor del aviso (no en la raíz, que hospeda `MantenimientoView`); en los diálogos va en el `Border` raíz del contenido (para poder extraerlo en la comparación); `MainWindow` no lo lleva.
- Dentro de una vista v2, todo `TextBlock` lleva una clase tipográfica v2 o sus propiedades tipográficas como valor local desde recursos `V2*`: si no, hereda la tipografía vieja global de `Themes/Typography.axaml`.
- Componentes v2: `xmlns:v2="using:StockApp.Presentation.Controls.V2"`; tokens `V2*`; variables CSS `--v2-*`. Cada clase `v2-*` de `base.css` replica la geometría de su `ControlTheme`/estilo: si cambia uno, cambia el otro en el mismo commit.
- Mockups (§6.1): `docs/ui-v2/mockups/<modulo>/<pantalla>.html`, 1280×760, `../_base/tokens.css` + `../_base/base.css`, todo dentro de `<div class="v2-ventana">`, datos fijos idénticos a los del ViewModel del test. Solo colores sólidos, bordes de 1px, radios, una `BoxShadow`, Inter, px enteros. Prohibido: gradientes, blur, transparencias apiladas, transiciones, pseudo-elementos decorativos, `text-shadow`. Se aprueban por módulo, ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los valores calibrados (11 teselas, 1.68%). Uno mayor solo con `UmbralAprobado` registrado tras revisar CADA tesela densa con el usuario; 16 o más exige además `MutacionPalidaEnRojo`. Nunca se sube para tapar un defecto. La marca de agua del Login no es excepción: se excluye de la comparación y se custodia con su propio test (ver "Estrategia de comparación de la marca de agua (P5)"), no con un umbral.
- Diferencia de tono aceptada (2026-09-30): el texto de la app sale 7-18% más claro que en Chromium. Lo que se exige idéntico es la geometría.
- Los comentarios de `.axaml` NO pueden contener `--` (AVLN1001, cascadea a MSB4025).
- En vistas con `x:DataType`, un binding mal escrito es error de build: compilar con la solución tras cada cambio de XAML.
- Compilar y testear con la solución: `dotnet build StockApp.sln` / `dotnet test StockApp.sln`. Nunca varios `.csproj` en un solo `dotnet build` (falso verde). Un solo `.csproj` sí.
- En este worktree los rojos de `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` por fixtures `.ods` ausentes son falsos conocidos, no regresión.
- Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test, y repetir.
- `$SCRATCH` en los comandos es un directorio de trabajo fuera del repo y dentro del home del usuario (por ejemplo `export SCRATCH=~/.cache/stockapp-ui-v2-fase-1 && mkdir -p "$SCRATCH"`), no `/tmp`.
- Todo guardián nuevo se verifica por MUTACIÓN QUITANDO: backup con `cp <archivo> "$SCRATCH"/<archivo>.bak`, se elimina la regla, se ve el rojo, se restaura con `cp` del backup, se ve el verde. NUNCA `git checkout --` (revierte también los cambios sin commitear de la task). Nunca se agrega un efecto destructivo para mutar.
- Versiones de paquetes SOLO en `Directory.Packages.props`.
- La ventana de la app se llama "Gestión Municipal" (la buscan los scripts de `scripts/gui-verificacion/`).
- Commits: conventional commits en español, SIN `Co-Authored-By` ni atribución de IA. No se pushea. Merge a `main` UNA sola vez al final de todas las fases (no en esta).

## Review Focus

1. **Fuga de estilos v2 a vistas no migradas a través del shell o de AccesoLimitado.** Si `ui2` queda en el `Grid` raíz de `ShellMainView` o en la raíz de `AccesoLimitadoView`, cada pantalla de contenido (o `MantenimientoView`) cambia de aspecto sin que nadie la haya migrado: botones `primary` quedan como estaban pero `TextBox` y `DataGrid` toman los estilos `.ui2`. Esperado: el contenido hospedado no tiene ningún ancestro con la clase `ui2`. Tests: `ShellMainViewV2Tests.ContenidoHospedado_NoTieneAncestroUi2` (Task 11) y `AccesoLimitadoViewV2Tests.MantenimientoHospedado_NoTieneAncestroUi2` (Task 14).
2. **Header de sección huérfano o sección vacía visible.** Un Operador con permisos mixtos ve "ANÁLISIS" sin ningún reporte debajo, o deja de ver un ítem al que tiene permiso porque el header se ocultó de más; también después de una revocación en caliente. Esperado: el header aparece si y solo si la sección tiene algún ítem visible, y se actualiza al refrescar permisos. Tests: `ShellMainViewV2Tests.OperadorMixto_SeccionesSinItemsVisibles_OcultanSuHeader` y `RevocacionEnCaliente_OcultaElHeaderDeLaSeccionQueSeVacia` (Task 11), y la comparación `SidebarOperadorMixto_IgualAlMockup` (Task 11).
3. **Texto recortado que la comparación no ve.** Un nombre de usuario largo en el pie, el valor de un `TextBox` (código de máquina, usuario), un breadcrumb largo o la última fila de un sidebar que se sale de la ventana: los píxeles no detectan glifos faltantes. Esperado: el guardián los reporta. Tests: `GuardianTextoRecortadoTests` nuevos de las Tasks 1 y 2, `EncabezadoPaginaBreadcrumbTests` (Task 4) y el guardián corriendo en cada comparación de las Tasks 11 a 16.
4. **Login que deja de responder a Enter, o pantalla de licencia que no carga el código.** Reescribir el XAML puede perder el `KeyDown="OnCampoKeyDown"` de los campos del Login o el `DataContextChanged` de `BloqueoLicenciaView`. Esperado: Enter en Usuario o Contraseña ejecuta `EntrarCommand`; al asignar el VM se ve el código de máquina. Tests: `LoginViewV2Tests.EnterEnContrasena_EjecutaEntrar` (Task 12) y `BloqueoLicenciaComparacionTests` (Task 13, el mockup muestra el código, que solo aparece si `CargarEstadoAsync` corrió).
5. **Diálogo que pierde un `x:Name` o un manejador.** El code-behind de los diálogos lee `MensajeText`, `TextoTextBox` y los `Click` cierran con el resultado; sin bindings, solo el compilador y un test de flujo lo custodian. Esperado: Confirmación devuelve `true`/`false`, PedirTexto devuelve el texto o `null`, Mensaje cierra. Tests: `DialogosV2Tests` (Task 15) más `ConfirmacionServiceDialogosConsecutivosTests` existente.

---

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia |
|---|---|---|
| Ítem del sidebar | Componente nuevo `ItemSidebar : Button` con `StyleKeyOverride` (patrón de `TarjetaAcceso`), propiedades `Icono`, `Titulo` y `EstaActivo` (pseudoclase `:activo`). Los colores por estado van en estilos anidados del `ControlTheme` sobre las partes. | `TarjetaAcceso.cs` y su `ControlTheme`: los selectores `Button` (viejos y v2) no alcanzan un `StyleKey` propio (ledger Fase 0, T10 → T15). `ComponentesV2.axaml` línea 9: "los colores que dependen de la variante van en estilos anidados sobre la parte". `ShellMainViewGatesTests` localiza por `Button` + identidad de `Command`: un `ItemSidebar` sigue siendo `Button`. |
| Sin acordeones | El XAML deja de usar `GrupoNavegacion.EstaExpandido` y `AlternarGrupoCommand`; el VM los conserva hasta la Fase 9 (limpieza). | `ShellMainViewGatesTests.Montar` setea `EstaExpandido` (spec §8.1: se mantiene tal cual); `ShellMainViewModelGruposTests` y `ServicioPreferenciasSidebarTests` siguen cubriendo ese código. Borrarlo ahora suma riesgo sin valor visible. |
| Títulos de sección en mayúsculas literales en el VM | `"INVENTARIO"`, `"FINANZAS"`, `"GESTIÓN"`, `"ANÁLISIS"`, `"ADMINISTRACIÓN"`. | Avalonia no tiene `text-transform`; convención de `herramientas.md` (headers de columna escritos en MAYÚSCULAS a mano). |
| Datos del usuario en el pie | Propiedades nuevas de `ShellMainViewModel`: `NombreUsuarioVisible`, `RolTexto`, `Iniciales`, leídas de `ICurrentSession.UsuarioActual` (`UsuarioSesion(Id, NombreUsuario, Rol, NombreCompleto)`). | `src/StockApp.Application/Auth/UsuarioSesion.cs`. Spec §3: "datos del usuario en el pie del sidebar" es uno de los cambios mínimos permitidos al VM. |
| Comparación del shell | `CurrentContent = null`: el área de contenido queda en fondo app, el mockup también. Dos estados: Administrador (desborda, P4) y Operador con permisos mixtos. | Spec §6.1 (datos fijos) y regla "un gate probado con Admin no prueba nada" (memoria del proyecto). |
| Comparación de diálogos | El contenido del `Window` (su `Border` raíz con `ui2`) se desengancha con `dialogo.Content = null` y se renderiza centrado sobre fondo app, con el ancho del diálogo. El mockup es el área cliente del diálogo centrada. La barra de título es del sistema operativo y no se compara. | `RenderizadorVista.RenderizarAPng(Control, …)` exige un `Control` que no sea `Window` (lo mete como `Content` de su propia ventana); `GuardianDePatronTests` ya documenta que montar un `Window` dentro de otro lanza "already has a visual parent". |
| Comparación de overlays | Cada overlay se renderiza solo, sobre fondo app (en la app real va encima del Login o del shell; su `Background` no cambia). | `MainWindow.axaml`: el overlay es un `ContentControl` encima del contenido, sin fondo propio. §4.5: no cambia su lógica. |
| Fakes en VisualTests | `PackageReference Moq` en `StockApp.Presentation.VisualTests.csproj` (versión central 4.20.72) y clases `Datos*` por módulo con los datos de cada mockup. | `Directory.Packages.props` línea 48 ya declara Moq. `LoginViewModel` exige un `ShellViewModel` real (ver `LoginViewModelTests.CrearShellFake`). |
| `GuardianDePatronTests` | Lista nueva `VistasMigradasAV2` (exclusión explícita, contada por `Guardian_CubreTodasLasVistasDelEnsamblado`): cada vista migrada sale de su lista P0-P9 y entra ahí. | Spec §8.2: el guardián viejo se reemplaza en la Fase 9. `VistaCentrada_TieneCardConPaddingHolgado` exige `Border.card` con padding 48: la card v2 es `TarjetaContenido` con 32. |
| Pantallas fuera del shell con scroll | La card va dentro de un `ScrollViewer` vertical `Auto`: con 760 de alto entra sin barra; en una ventana chica (MinHeight 300) se puede scrollear en vez de recortar. | `MainWindow.axaml`: `MinHeight="300"`. El Reset mide ~580 de alto. |
| Guardián y `TextBox` | El `ScrollViewer` interno de un `TextBox` NO relaja ejes (el usuario no lo scrollea para leer), y el valor del `TextPresenter` se mide con su `TextLayout`. | Spec §6.5 "Puntos ciegos conocidos". Ledger Fase 0, Task 18 minor. |
| Relajamiento de eje | Al cruzar un viewport desplazable (ScrollViewer o presentador de DataGrid), hacia afuera se controla la caja del viewport en ese eje, no la del texto. | Ledger Fase 0, Task 18 minor: "una grilla sin ScrollViewer que se sale de la ventana no se reporta". |
| Breadcrumb | `PART_Breadcrumb` pasa de `StackPanel` horizontal a `Grid` `Auto,Auto,*` con el tramo actual en `TextWrapping="Wrap"`: un texto largo baja de línea en vez de salirse. | Ledger Fase 0, Task 12 minor. |
| Íconos en mockups | Los HTML se escriben con marcadores `{{mdi-<clave>}}` y un paso del plan los reemplaza con la salida de `icono-svg`. | `ExportadorIconos.Svg` (`tools/StockApp.UiV2.Herramientas/Mockups/ExportadorIconos.cs`) devuelve el `<svg class="v2-icono" width="16" height="16" …>` exacto que espera `base.css`. |

## Mapa de archivos

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `tools/StockApp.UiV2.Herramientas/Mockups/VerificadorConvenciones.cs` | Detecta celdas `v2-num` sin su `<span>` único. |
| `tests/StockApp.UiV2.Herramientas.Tests/VerificadorConvencionesTests.cs` | Tests del verificador y escaneo de todos los mockups del repo. |
| `tests/StockApp.Presentation.VisualTests/EncabezadoPaginaBreadcrumbTests.cs` | Breadcrumb largo sin recorte, con métricas Skia. |
| `src/StockApp.Presentation/Controls/V2/ItemSidebar.cs` | Ítem del sidebar (Button con ícono, título y estado activo). |
| `tests/StockApp.Presentation.UiTests/V2/ItemSidebarTests.cs` | Geometría, colores por estado y comando del ítem. |
| `tests/StockApp.Presentation.UiTests/V2/TipografiaShellV2Tests.cs` | Clases tipográficas del shell/login y botón ghost de ícono. |
| `docs/ui-v2/mockups/shell/sidebar-admin.html`, `sidebar-operador.html` | Mockups del shell. |
| `docs/ui-v2/mockups/acceso/login.html`, `login-error.html`, `login-acceso-limitado.html`, `reset.html`, `bloqueo-licencia.html` | Mockups de las pantallas fuera del shell. |
| `docs/ui-v2/mockups/dialogos/confirmacion.html`, `mensaje.html`, `pedir-texto.html` | Mockups de los diálogos. |
| `docs/ui-v2/mockups/actualizaciones/banner.html`, `modal.html`, `bloqueo.html`, `modo-degradado.html` | Mockups de los overlays. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Shell/DatosShell.cs`, `ShellComparacionTests.cs` | VM fijos y comparación del shell. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/DatosAcceso.cs`, `AccesoComparacionTests.cs` | VM fijos y comparación de Login, Reset y BloqueoLicencia. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/DialogosComparacionTests.cs` | Comparación de los diálogos. |
| `tests/StockApp.Presentation.VisualTests/Comparacion/ContenidoDeDialogo.cs` | Extrae el contenido de un `Window` de diálogo para renderizarlo. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Actualizaciones/DatosActualizaciones.cs`, `ActualizacionesComparacionTests.cs` | VM fijos y comparación de los overlays. |
| `tests/StockApp.Presentation.UiTests/V2/ShellMainViewV2Tests.cs` | Estructura del shell v2: scope `ui2`, headers, pie, cerrar sesión. |
| `tests/StockApp.Presentation.UiTests/V2/LoginViewV2Tests.cs` | Enter, estados y alertas del Login. |
| `tests/StockApp.Presentation.UiTests/V2/AccesoLimitadoViewV2Tests.cs` | Aviso v2 y no-fuga a Mantenimiento. |
| `tests/StockApp.Presentation.UiTests/V2/DialogosV2Tests.cs` | Resultados de los diálogos y scope `ui2`. |
| `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelPieTests.cs` | Nombre, rol e iniciales del pie. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs` | `TextPresenter`, `Inlines`, `MaxLines`, `TextBox` sin relajamiento, relajamiento hasta el viewport. |
| `tests/StockApp.Presentation.VisualTests/GuardianTextoRecortadoTests.cs` | Casos nuevos. |
| `tests/StockApp.Presentation.VisualTests/DataGridV2CatalogoTests.cs` | Se borra `Headers_TextoCompletoSinRecorte`. |
| `tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj` | + `Moq`. |
| `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` | Breadcrumb en `Grid`; `ControlTheme` de `ItemSidebar`. |
| `tests/StockApp.Presentation.UiTests/V2/TarjetaContenidoEncabezadoTests.cs` | `PART_Breadcrumb` es `Grid`. |
| `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml` | Clases `marca`, `header-seccion-sidebar`, `titulo-login`, `avatar`, `usuario-nombre`, `usuario-rol`, `version`; `Button.ghost.icono`. |
| `docs/ui-v2/mockups/_base/base.css` | Espejo CSS de todo lo anterior + `.v2-enlace`, `.v2-boton--deshabilitado`. |
| `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` | Secciones v2 (P1) y datos del pie. |
| `tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs`, `tests/StockApp.UiV2.Herramientas.Tests/CapturadorMockupTests.cs` | Parámetro `ocultarSelectores` (marca de agua del Login, P5). |
| `tests/StockApp.Presentation.VisualTests/Comparacion/ComparacionVisual.cs` | Parámetro `ocultarEnMockup` (P5). |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/LoginMarcaAguaTests.cs` | Custodia de la marca de agua del Login: opacidad, escala, región y valores del mockup (P5). |
| `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelGruposTests.cs` | Títulos y composición de secciones nuevas. |
| `src/StockApp.Presentation/Views/ShellMainView.axaml` | Sidebar v2. |
| `src/StockApp.Presentation/Views/LoginView.axaml`, `ResetAdminView.axaml`, `BloqueoLicenciaView.axaml`, `AccesoLimitadoView.axaml` | XAML v2 (code-behind sin cambios). |
| `src/StockApp.Presentation/Views/Dialogs/ConfirmacionDialog.axaml`, `MensajeDialog.axaml`, `PedirTextoDialog.axaml` | XAML v2 (code-behind sin cambios). |
| `src/StockApp.Presentation/Actualizaciones/Views/ActualizacionBannerView.axaml`, `ActualizacionModalView.axaml`, `ActualizacionBloqueoView.axaml` | XAML v2. |
| `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` | Lista `VistasMigradasAV2`. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` | §6.5 (puntos ciegos resueltos, relajamiento); estado de la Fase 1. |
| `docs/ui-v2/herramientas.md` | Guardián ampliado, verificador de convenciones, marcadores de íconos, convención de diálogos. |

**Comandos de verificación:**

- Suite completa: `dotnet test StockApp.sln`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Un test: agregar `--filter "FullyQualifiedName~NombreDelTest"`
- Comparación con página: `scripts/ui-v2/comparar.sh <filtro>` y abrir `artifacts/ui-v2/comparacion/index.html`

---

## Bloque A — Herramienta y componentes (antes de cualquier pantalla)

### Task 1: Guardián de texto recortado: valor de `TextBox`, placeholder, `Inlines` y `MaxLines`

Va ANTES del primer formulario (el Login de la Task 12 tiene dos `TextBox`): spec §6.5 lista estos tres puntos ciegos y dice que ahí la única red es el ojo del usuario.

**Files:**
- Modify: `tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/GuardianTextoRecortadoTests.cs`
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§6.5, párrafo "Puntos ciegos conocidos del guardián")
- Modify: `docs/ui-v2/herramientas.md` (sección "Test por pantalla", último párrafo del guardián)

**Interfaces:**
- Consumes: `RenderizadorVista.RenderizarAPng(Control, object?, string, Action<Control>?)`, `GuardianTextoRecortado.Buscar(Visual, IReadOnlyCollection<string>?)` (Fase 0).
- Produces: la misma firma `GuardianTextoRecortado.Buscar(Visual raiz, IReadOnlyCollection<string>? recortesPermitidos = null)`, que ahora además reporta: el valor de un `TextPresenter` (con el texto `"(contraseña)"` en lugar del valor si hay `PasswordChar`) que no entra en su `TextBox`; el placeholder que no entra; un `TextBlock` con `Inlines` y `Text` nulo; y un `TextBlock` cortado por `MaxLines`. `recortesPermitidos` se compara contra el texto visible (para `Inlines`, la concatenación de sus `Run`).

- [ ] **Step 1: Escribir los tests (rojo)**

Agregar a `GuardianTextoRecortadoTests` (mismo archivo, debajo de `TextoDentroDeUnContenedorOculto_NoSeReporta`):

```csharp
    [AvaloniaFact]
    public void TextBoxConValorQueNoEntra_SeReporta()
    {
        // Spec §6.5, punto ciego: el valor lo dibuja un TextPresenter, no un TextBlock.
        var recortes = Renderizar(new TextBox { Width = 120, Text = Largo });

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextBoxConValorQueEntra_NoSeReporta() =>
        Assert.Empty(Renderizar(new TextBox { Width = 300, Text = "A-001" }));

    [AvaloniaFact]
    public void TextBoxConPlaceholderQueNoEntra_SeReporta()
    {
        var recortes = Renderizar(new TextBox { Width = 120, PlaceholderText = Largo });

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void ContrasenaQueNoEntra_SeReportaSinMostrarElValor()
    {
        const string secreto = "unaContraseniaBastanteLarga123";
        var recortes = Renderizar(new TextBox { Width = 80, PasswordChar = '*', Text = secreto });

        var recorte = Assert.Single(recortes);
        Assert.Contains("(contraseña)", recorte);
        Assert.DoesNotContain(secreto, recorte);
    }

    [AvaloniaFact]
    public void NumericUpDownConValorQueNoEntra_SeReporta()
    {
        // El TextBox interno del NumericUpDown también dibuja con TextPresenter.
        var recortes = Renderizar(new NumericUpDown { Width = 90, Value = 123456789.12m, FormatString = "0.00" });

        Assert.Contains(recortes, r => r.Contains("123456789.12"));
    }

    [AvaloniaFact]
    public void TextoConInlinesYTextNulo_QueNoEntra_SeReporta()
    {
        var texto = new TextBlock { Width = 60 };
        texto.Inlines = new InlineCollection { new Run(Largo) };

        var recortes = Renderizar(texto);

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextoConMaxLinesQueCortaLineas_SeReporta()
    {
        // MaxLines limita también el Measure: sin anularlo, el texto pide lo que tiene.
        var recortes = Renderizar(new TextBlock { Width = 120, TextWrapping = TextWrapping.Wrap, MaxLines = 1, Text = Largo });

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextoConMaxLinesQueAlcanza_NoSeReporta() =>
        Assert.Empty(Renderizar(new TextBlock { Width = 120, TextWrapping = TextWrapping.Wrap, MaxLines = 3, Text = "Corto" }));
```

Agregar `using Avalonia.Controls.Documents;` al principio del archivo (para `InlineCollection` y `Run`).

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~GuardianTextoRecortadoTests"`
Expected: FAIL en `TextBoxConValorQueNoEntra_SeReporta`, `ContrasenaQueNoEntra_SeReportaSinMostrarElValor`, `NumericUpDownConValorQueNoEntra_SeReporta`, `TextoConInlinesYTextNulo_QueNoEntra_SeReporta` y `TextoConMaxLinesQueCortaLineas_SeReporta` (colección vacía). `TextBoxConPlaceholderQueNoEntra_SeReporta` puede fallar o pasar según dónde ubique Fluent el placeholder: si ya pasa, anotarlo en el reporte de la task (lo cubría la regla de recorte propio) y seguir. Los dos `…NoSeReporta` pasan.

- [ ] **Step 2: Implementar**

En `GuardianTextoRecortado.cs`:

1. Agregar `using Avalonia.Controls.Documents;`.
2. Reemplazar el cuerpo de `Buscar` por:

```csharp
    public static IReadOnlyList<string> Buscar(Visual raiz, IReadOnlyCollection<string>? recortesPermitidos = null)
    {
        var recortes = new List<string>();
        foreach (var texto in raiz.GetVisualDescendants().OfType<TextBlock>().ToList())
        {
            var contenido = ContenidoDe(texto);
            if (!texto.IsEffectivelyVisible || string.IsNullOrEmpty(contenido)
                || recortesPermitidos?.Contains(contenido) == true)
            {
                continue;
            }

            var asignado = texto.Bounds.Size;
            var margen = texto.Margin;
            var anchoDisponible = texto.TextWrapping == TextWrapping.NoWrap
                ? double.PositiveInfinity
                : asignado.Width + margen.Left + margen.Right;
            var pedido = MedirSinTamanoFijo(texto, new Size(anchoDisponible, double.PositiveInfinity)).Deflate(margen);

            if (pedido.Width > asignado.Width + Tolerancia || pedido.Height > asignado.Height + Tolerancia)
            {
                recortes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"\"{contenido}\" pide {pedido.Width:0.#}x{pedido.Height:0.#} y tiene {asignado.Width:0.#}x{asignado.Height:0.#}"));
                continue;
            }

            var recortador = BuscarRecortador(texto, RectDelTexto(texto, pedido));
            if (recortador is not null)
            {
                recortes.Add($"\"{contenido}\" queda fuera de {recortador}");
            }
        }

        // Spec §6.5: el valor de un TextBox (y del TextBox interno de NumericUpDown,
        // CalendarDatePicker o un ComboBox editable) lo dibuja un TextPresenter, no un TextBlock.
        foreach (var presentador in raiz.GetVisualDescendants().OfType<TextPresenter>().ToList())
        {
            var valor = presentador.Text;
            if (!presentador.IsEffectivelyVisible || string.IsNullOrEmpty(valor)
                || recortesPermitidos?.Contains(valor) == true)
            {
                continue;
            }

            // Nunca se escribe una contraseña en un mensaje de test.
            var etiqueta = presentador.PasswordChar != default ? "(contraseña)" : valor;
            var layout = presentador.TextLayout;
            var rect = new Rect(0, 0, layout.WidthIncludingTrailingWhitespace, layout.Height);
            var recortador = BuscarRecortador(presentador, rect);
            if (recortador is not null)
            {
                recortes.Add($"valor \"{etiqueta}\" queda fuera de {recortador}");
            }
        }

        return recortes;
    }

    // Un TextBlock con Inlines tiene Text nulo: el contenido visible es la concatenación de sus Run.
    private static string? ContenidoDe(TextBlock texto) =>
        !string.IsNullOrEmpty(texto.Text)
            ? texto.Text
            : texto.Inlines is { Count: > 0 } inlines
                ? string.Concat(inlines.OfType<Run>().Select(r => r.Text))
                : null;
```

3. En `MedirSinTamanoFijo`, agregar la anulación de `MaxLines` junto a las otras cuatro (y actualizar el comentario de arriba para nombrarla):

```csharp
        using var lineas = texto.SetValue(TextBlock.MaxLinesProperty, 0, BindingPriority.Animation);
```

4. En `BuscarRecortador`, el `ScrollViewer` interno de un `TextBox` no relaja ningún eje: el usuario no lo scrollea para leer el valor ni el placeholder. Reemplazar el bloque del `ScrollContentPresenter` por:

```csharp
            if (ancestro is ScrollContentPresenter { TemplatedParent: ScrollViewer scroll }
                && scroll.TemplatedParent is not TextBox)
            {
                // Lo que el viewport recorta en un eje desplazable se alcanza scrolleando; lo de
                // afuera (la ventana incluida) tampoco decide en ese eje. El ScrollViewer de un
                // TextBox no cuenta: sigue al cursor, no se usa para leer el valor.
                horizontalFijo &= scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled;
                verticalFijo &= scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled;
            }
```

5. Cambiar la firma de `BuscarRecortador(TextBlock texto, Rect rectLocal)` a `BuscarRecortador(Visual texto, Rect rectLocal)` (el cuerpo no usa nada propio de `TextBlock`).

6. Actualizar el `<summary>` de la clase: agregar un tercer `<item>` "Valor de un TextBox: el TextPresenter mide su TextLayout; el ScrollViewer interno del TextBox no relaja ejes." y borrar de la documentación cualquier mención a esos puntos ciegos.

- [ ] **Step 3: Verde**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
Expected: PASS de toda la suite visual (los tests nuevos y `Catalogo_IgualAlMockup`: el catálogo tiene `TextBox`, `NumericUpDown` y `CalendarDatePicker` con valores que entran; si alguno se reporta, es un recorte real del catálogo: arreglar el catálogo y su mockup, no el guardián).

- [ ] **Step 4: Mutación quitando (tres reglas)**

```bash
cp tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs "$SCRATCH/GuardianTextoRecortado.cs.bak"
```

(a) Borrar el bucle `foreach (var presentador …)` entero → correr el filtro `GuardianTextoRecortadoTests`: rojo en `TextBoxConValorQueNoEntra_SeReporta`, `ContrasenaQueNoEntra…` y `NumericUpDownConValorQueNoEntra…`. Restaurar con `cp "$SCRATCH/GuardianTextoRecortado.cs.bak" tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs`.
(b) Borrar la línea `using var lineas = …` → rojo en `TextoConMaxLinesQueCortaLineas_SeReporta`. Restaurar con `cp`.
(c) Reemplazar el cuerpo de `ContenidoDe` por `texto.Text;` (es QUITAR la rama de `Inlines`) → rojo en `TextoConInlinesYTextNulo_QueNoEntra_SeReporta`. Restaurar con `cp`.

Correr el filtro una vez más: verde. Pegar los tres rojos en el reporte de la task.

- [ ] **Step 5: Documentación**

En la spec §6.5 reemplazar el párrafo "Puntos ciegos conocidos del guardián: …" por:

```markdown
Cubre también el valor de `TextBox`, `NumericUpDown`, `CalendarDatePicker` y `ComboBox` editable (lo dibuja un `TextPresenter`: se mide su `TextLayout`, y el `ScrollViewer` interno del `TextBox` no relaja ejes porque nadie lo scrollea para leer), el placeholder, un `TextBlock` con `Inlines` y `Text` nulo, y `MaxLines` (se mide sin el límite). Un valor de contraseña se reporta como "(contraseña)", nunca en claro (Fase 1, Task 1).
```

En `docs/ui-v2/herramientas.md`, en el párrafo "Además de los píxeles, `CompararConMockup` falla si algún `TextBlock` visible…", agregar al final: "También mira el valor y el placeholder de los `TextBox` (incluidos los internos de `NumericUpDown`, `CalendarDatePicker` y `ComboBox` editable), los `TextBlock` con `Inlines` y los cortados por `MaxLines`."

- [ ] **Step 6: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs tests/StockApp.Presentation.VisualTests/GuardianTextoRecortadoTests.cs docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md docs/ui-v2/herramientas.md
git commit -m "test(ui-v2): el guardián de texto recortado mira TextBox, Inlines y MaxLines"
```

### Task 2: Guardián: el relajamiento de eje llega hasta el viewport, y se borra el test redundante de headers

**Files:**
- Modify: `tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/GuardianTextoRecortadoTests.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/DataGridV2CatalogoTests.cs`
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§6.5, viñeta "Recorte por un ancestro")

**Interfaces:**
- Consumes: `GuardianTextoRecortado.Buscar` de la Task 1.
- Produces: misma firma. Cambio de comportamiento: un texto dentro de un viewport desplazable (ScrollViewer o presentador de filas/headers de DataGrid) sigue exento de lo que ese viewport recorta en su eje desplazable, pero los ancestros de afuera y la ventana controlan la caja del VIEWPORT en ese eje. Una grilla o un ScrollViewer que se salen de la ventana se reportan.

- [ ] **Step 1: Escribir los tests (rojo)**

Agregar a `GuardianTextoRecortadoTests`:

```csharp
    [AvaloniaFact]
    public void GrillaMasAltaQueLaVentanaSinScrollViewer_SeReporta()
    {
        // Ledger Fase 0, Task 18: el relajamiento del DataGrid seguía vigente en la ventana, así
        // que las filas que caen debajo de los 760 px (inalcanzables: nada scrollea la página)
        // no se reportaban.
        var recortes = RenderizarGrilla(new DataGrid { Height = 1000, Width = 300, Columns = { Columna(150) } }, filas: 40);

        Assert.Contains(recortes, r => r.Contains("la ventana"));
    }

    [AvaloniaFact]
    public void ScrollViewerQueSeSaleDeLaVentana_SeReporta()
    {
        var recortes = Renderizar(new ScrollViewer
        {
            Height = 1000,
            Width = 300,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel { Children = { new Border { Height = 900 }, new TextBlock { Text = "Debajo de la ventana" } } },
        });

        Assert.Contains(recortes, r => r.Contains("Debajo de la ventana") && r.Contains("la ventana"));
    }

    [AvaloniaFact]
    public void GrillaAltaDentroDeUnScrollViewerQueEntra_NoSeReporta()
    {
        // El ScrollViewer de afuera sí se alcanza entero: la grilla se lee scrolleando la página.
        var grilla = new DataGrid
        {
            Height = 1000,
            Width = 300,
            AutoGenerateColumns = false,
            Columns = { Columna(150) },
            ItemsSource = Enumerable.Range(0, 40).Select(_ => new Fila("A-001")).ToList(),
        };
        var vista = new UserControl
        {
            Classes = { "ui2" },
            Content = new ScrollViewer { Height = 400, Width = 320, HorizontalAlignment = HorizontalAlignment.Left, Content = grilla },
        };
        IReadOnlyList<string> recortes = [];
        RenderizadorVista.RenderizarAPng(vista, null,
            Path.Combine(Directory.CreateTempSubdirectory("guardian-anidado-").FullName, "vista.png"),
            v => recortes = GuardianTextoRecortado.Buscar(v));

        Assert.Empty(recortes);
    }
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~GuardianTextoRecortadoTests"`
Expected: FAIL en `GrillaMasAltaQueLaVentanaSinScrollViewer_SeReporta` y `ScrollViewerQueSeSaleDeLaVentana_SeReporta` (colección sin "la ventana"); PASS en `GrillaAltaDentroDeUnScrollViewerQueEntra_NoSeReporta` y en todos los anteriores.

- [ ] **Step 2: Implementar**

Primero guardar la versión de la Task 1 (la usa el Step 4): `cp tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs "$SCRATCH/GuardianTextoRecortado.task1.cs"`.

Reemplazar `BuscarRecortador` entero por:

```csharp
    private static string? BuscarRecortador(Visual texto, Rect rectLocal)
    {
        var raizVisual = TopLevel.GetTopLevel(texto);
        if (raizVisual is null || texto.TransformToVisual(raizVisual) is not { } aRaiz)
        {
            return null;
        }

        // rect: lo que tiene que verse entero. Empieza siendo el texto; al cruzar un viewport
        // desplazable pasa a ser la caja del viewport en ese eje (el texto se alcanza scrolleando,
        // pero solo a través de ese viewport, que sí tiene que verse entero hacia afuera).
        var rect = rectLocal.TransformToAABB(aRaiz);
        foreach (var ancestro in texto.GetVisualAncestors())
        {
            if (ancestro == raizVisual)
            {
                break;
            }

            var (desplazaH, desplazaV) = EjesDesplazables(ancestro);
            var caja = ancestro.TransformToVisual(raizVisual) is { } ancestroARaiz
                ? new Rect(ancestro.Bounds.Size).TransformToAABB(ancestroARaiz)
                : (Rect?)null;

            if (ancestro.ClipToBounds && caja is { } recorte && !Contiene(recorte, rect, !desplazaH, !desplazaV))
            {
                return ancestro.GetType().Name + (string.IsNullOrEmpty((ancestro as Control)?.Name) ? "" : "#" + ((Control)ancestro).Name);
            }

            if ((desplazaH || desplazaV) && caja is { } viewport)
            {
                rect = new Rect(
                    desplazaH ? viewport.Left : rect.Left,
                    desplazaV ? viewport.Top : rect.Top,
                    desplazaH ? viewport.Width : rect.Width,
                    desplazaV ? viewport.Height : rect.Height);
            }
        }

        return Contiene(new Rect(raizVisual.ClientSize), rect, true, true) ? null : "la ventana";
    }

    // Ejes en que este ancestro es un viewport que el usuario scrollea.
    private static (bool Horizontal, bool Vertical) EjesDesplazables(Visual ancestro)
    {
        if (ancestro is ScrollContentPresenter { TemplatedParent: ScrollViewer scroll }
            && scroll.TemplatedParent is not TextBox)
        {
            // El ScrollViewer de un TextBox sigue al cursor, no se usa para leer el valor (Task 1).
            return (scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled,
                    scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled);
        }

        if (ancestro is DataGridRowsPresenter or DataGridColumnHeadersPresenter
            && ancestro.FindAncestorOfType<DataGrid>() is { } grilla)
        {
            // El DataGrid scrollea por su cuenta, sin ScrollViewer: desde su presentador de filas
            // (o de headers, solo horizontal) hacia afuera. Lo de adentro (la celda) se sigue mirando.
            return (grilla.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled,
                    ancestro is DataGridRowsPresenter && grilla.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled);
        }

        return (false, false);
    }
```

Actualizar en el `<summary>` de la clase el ítem 2: "…El viewport de un ScrollViewer no cuenta en el eje en que se puede scrollear: el texto sigue alcanzable, pero desde ahí hacia afuera se controla la caja del viewport, que sí tiene que verse entera. Lo mismo el DataGrid…".

- [ ] **Step 3: Verde**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
Expected: PASS de toda la suite visual, incluidos `GrillaConMasFilasQueSuAlto_LaFilaParcialNoSeReporta`, `GrillaConColumnasMasAnchasQueElla_LaColumnaParcialNoSeReporta`, `TextoQueSigueEnUnScrollViewerDesplazable_NoSeReporta` y `Catalogo_IgualAlMockup`.

- [ ] **Step 4: Verificación por reintroducción del bug**

El cambio no es una regla que se pueda quitar sola (quitar el reemplazo de `rect` rompe también los tests viejos de la grilla, porque el `Border#DataGridBorder` pasaría a medir el texto). La verificación es reintroducir el bug: volver a la versión de la Task 1 de `BuscarRecortador` con los tests nuevos puestos.

Antes del Step 2 (es decir, ANTES de implementar), guardar la versión de la Task 1:

```bash
cp tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs "$SCRATCH/GuardianTextoRecortado.task1.cs"
```

Con el Step 3 en verde:

```bash
cp tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs "$SCRATCH/GuardianTextoRecortado.task2.cs"
cp "$SCRATCH/GuardianTextoRecortado.task1.cs" tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs
```

Correr el filtro `GuardianTextoRecortadoTests`: rojo en `GrillaMasAltaQueLaVentanaSinScrollViewer_SeReporta` y `ScrollViewerQueSeSaleDeLaVentana_SeReporta`, verde en el resto. Restaurar con `cp "$SCRATCH/GuardianTextoRecortado.task2.cs" tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs` y ver el verde. Pegar el rojo en el reporte.

- [ ] **Step 5: Borrar `Headers_TextoCompletoSinRecorte`, probando antes que el guardián lo cubre**

`DataGridV2CatalogoTests.Headers_TextoCompletoSinRecorte` mide después de cerrar la ventana (estado poco confiable, ledger Fase 0 Task 18) y es redundante con el guardián, que corre dentro de `Catalogo_IgualAlMockup`. Antes de borrarlo, ver que el guardián caza el mismo defecto:

```bash
cp src/StockApp.Presentation/Themes/V2/DataGridV2.axaml "$SCRATCH/DataGridV2.axaml.bak"
```

En `DataGridV2.axaml`, dentro del estilo `<Style Selector=".ui2 DataGridColumnHeader:not(#PART_TopLeftCornerHeader)">`, borrar el `<Setter Property="Template">…</Setter>` entero (vuelve la plantilla de Fluent, que reserva 32 px para la flecha). Correr:

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~Catalogo_IgualAlMockup"`
Expected: FAIL con "texto recortado por el layout" y las líneas `"CÓDIGO" pide … y tiene …` y `"STOCK" pide …` (spec §6.5, "Verificado por mutación").

Restaurar con `cp "$SCRATCH/DataGridV2.axaml.bak" src/StockApp.Presentation/Themes/V2/DataGridV2.axaml` (si el rojo no desaparece: `dotnet build-server shutdown` y `rm -rf` de `obj/bin` de Presentation y VisualTests). Recién entonces borrar el método `Headers_TextoCompletoSinRecorte` de `DataGridV2CatalogoTests.cs`, y en el `<summary>` de la clase cambiar "encabezados sin recorte y esquinas inferiores recortadas al radio 8" por "esquinas inferiores recortadas al radio 8 (el recorte de encabezados lo custodia el guardián de texto, spec §6.5)". Si `Renderizar()` queda devolviendo una tupla con un campo sin uso, simplificarla a `string Png`.

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DataGridV2CatalogoTests"`
Expected: PASS (los `EsquinaInferior_…`).

- [ ] **Step 6: Documentación**

Spec §6.5, viñeta "Recorte por un ancestro": reemplazar "Excepción: el viewport de un `ScrollViewer` no cuenta en el eje en que se puede scrollear (el texto sigue alcanzable), ni tampoco los ancestros de afuera y la ventana en ese eje; en un eje con scroll `Disabled` sí cuenta." por "Excepción: el viewport de un `ScrollViewer` no cuenta en el eje en que se puede scrollear (el texto sigue alcanzable); desde ahí hacia afuera se controla la caja del viewport en ese eje, que sí tiene que verse entera (un ScrollViewer o una grilla que se salen de la ventana se reportan, Fase 1 Task 2). En un eje con scroll `Disabled` cuenta el texto."

- [ ] **Step 7: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Comparacion/GuardianTextoRecortado.cs tests/StockApp.Presentation.VisualTests/GuardianTextoRecortadoTests.cs tests/StockApp.Presentation.VisualTests/DataGridV2CatalogoTests.cs docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "test(ui-v2): el guardián controla el viewport hacia afuera y borra el test redundante de headers"
```

### Task 3: Guardián de la convención `.v2-num > span` en los mockups

**Files:**
- Create: `tools/StockApp.UiV2.Herramientas/Mockups/VerificadorConvenciones.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/VerificadorConvencionesTests.cs`
- Modify: `docs/ui-v2/herramientas.md` (punto 4 de "Mockup de una pantalla")

**Interfaces:**
- Consumes: `RutasRepo.CarpetaMockups` (Fase 0).
- Produces: `public static IReadOnlyList<string> VerificadorConvenciones.CeldasNumericasSinSpan(string html)`: un fragmento por cada elemento con la clase exacta `v2-num` cuyo contenido no es un único `<span>` sin hijos.

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.UiV2.Herramientas.Tests/VerificadorConvencionesTests.cs`:

```csharp
using StockApp.UiV2.Herramientas;
using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// Convención de herramientas.md: el valor de una celda numérica va en un único span, que hace de
/// TextBlock (ceil del ancho como Avalonia). Sin el span la cifra queda hasta 1px corrida y la
/// comparación lo absorbe como ruido: nadie lo ve (ledger Fase 0, Task 17).
/// </summary>
public class VerificadorConvencionesTests
{
    [Fact]
    public void CeldaConUnSpan_Cumple() =>
        Assert.Empty(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-grilla-celda v2-num"><span>12.50</span></div>"""));

    [Fact]
    public void CeldaConTextoSuelto_NoCumple() =>
        Assert.Single(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-grilla-celda v2-num">12.50</div>"""));

    [Fact]
    public void CeldaConDosSpans_NoCumple() =>
        Assert.Single(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-num"><span>12</span><span>.50</span></div>"""));

    [Fact]
    public void ClaseParecida_NoCuenta() =>
        Assert.Empty(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-numero">12.50</div>"""));

    [Fact]
    public void ContenedorSinV2Num_NoSeMira() =>
        Assert.Empty(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-grilla-fila"><div class="v2-grilla-celda">A-001</div><div class="v2-grilla-celda v2-num"><span>340</span></div></div>"""));

    [Fact]
    public void MockupsDelRepo_RespetanLaConvencion()
    {
        // referencia/ son los mockups de dirección aprobados en el brainstorming: no usan base.css.
        var referencia = Path.Combine(RutasRepo.CarpetaMockups, "referencia") + Path.DirectorySeparatorChar;
        var errores = Directory.EnumerateFiles(RutasRepo.CarpetaMockups, "*.html", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(referencia, StringComparison.Ordinal))
            .SelectMany(f => VerificadorConvenciones.CeldasNumericasSinSpan(File.ReadAllText(f))
                .Select(e => $"{Path.GetRelativePath(RutasRepo.CarpetaMockups, f)}: {e}"))
            .ToList();

        Assert.True(errores.Count == 0,
            "Celdas v2-num sin su <span> único (herramientas.md, punto 4):" + Environment.NewLine
            + string.Join(Environment.NewLine, errores));
    }
}
```

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~VerificadorConvencionesTests"`
Expected: FAIL de compilación, `The name 'VerificadorConvenciones' does not exist in the current context`.

- [ ] **Step 2: Implementar**

`tools/StockApp.UiV2.Herramientas/Mockups/VerificadorConvenciones.cs`:

```csharp
using System.Text.RegularExpressions;

namespace StockApp.UiV2.Herramientas.Mockups;

/// <summary>
/// Convenciones de los mockups que el navegador no hace cumplir (docs/ui-v2/herramientas.md).
/// </summary>
public static partial class VerificadorConvenciones
{
    // Un elemento cuya lista de clases tiene v2-num EXACTA (no v2-numero) y su contenido hasta el
    // cierre de la misma etiqueta. Las celdas numéricas no anidan elementos del mismo tipo.
    [GeneratedRegex("""<(?<tag>[a-z][a-z0-9]*)\b[^>]*\bclass="(?:[^"]*\s)?v2-num(?:\s[^"]*)?"[^>]*>(?<interior>.*?)</\k<tag>>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CeldaNumerica();

    [GeneratedRegex("""^\s*<span>[^<]*</span>\s*$""")]
    private static partial Regex UnSoloSpan();

    /// <summary>Elementos con clase <c>v2-num</c> cuyo contenido no es un único &lt;span&gt; de texto.</summary>
    public static IReadOnlyList<string> CeldasNumericasSinSpan(string html) =>
        CeldaNumerica().Matches(html)
            .Where(m => !UnSoloSpan().IsMatch(m.Groups["interior"].Value))
            .Select(m => m.Value.Length > 120 ? m.Value[..120] + "…" : m.Value)
            .ToList();
}
```

- [ ] **Step 3: Verde**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
Expected: PASS (los 6 nuevos y el resto de la herramienta; `catalogo.html` cumple).

- [ ] **Step 4: Mutación quitando**

```bash
cp docs/ui-v2/mockups/componentes/catalogo.html "$SCRATCH/catalogo.html.bak"
```

En `catalogo.html` QUITAR el `<span>` y `</span>` de `<div class="v2-grilla-celda col-costo v2-num"><span>12.50</span></div>`. Correr `--filter "FullyQualifiedName~MockupsDelRepo_RespetanLaConvencion"`: rojo con `componentes/catalogo.html: <div class="v2-grilla-celda col-costo v2-num">12.50</div>`. Restaurar con `cp` y ver el verde.

- [ ] **Step 5: Documentación**

`docs/ui-v2/herramientas.md`, al final del punto 4 de "Mockup de una pantalla": "La convención la custodia `VerificadorConvencionesTests.MockupsDelRepo_RespetanLaConvencion` (todos los HTML de `docs/ui-v2/mockups/` salvo `referencia/`)."

- [ ] **Step 6: Commit**

```bash
git add tools/StockApp.UiV2.Herramientas/Mockups/VerificadorConvenciones.cs tests/StockApp.UiV2.Herramientas.Tests/VerificadorConvencionesTests.cs docs/ui-v2/herramientas.md
git commit -m "test(ui-v2): guardián de la convención .v2-num > span en los mockups"
```

### Task 4: Breadcrumb de `EncabezadoPagina` que no recorta texto

El primer breadcrumb real llega en la Fase 3 (formularios), pero el componente es de la Fase 0 y el ajuste no depende de ninguna pantalla: se cierra acá, con su test.

**Files:**
- Modify: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` (template de `EncabezadoPagina`)
- Modify: `docs/ui-v2/mockups/_base/base.css` (`.v2-breadcrumb`)
- Modify: `tests/StockApp.Presentation.UiTests/V2/TarjetaContenidoEncabezadoTests.cs` (línea 70: `Parte<StackPanel>` → `Parte<Grid>`)
- Create: `tests/StockApp.Presentation.VisualTests/EncabezadoPaginaBreadcrumbTests.cs`

**Interfaces:**
- Consumes: `EncabezadoPagina` (`Titulo`, `BreadcrumbOrigen`, `BreadcrumbComando`, `BreadcrumbActual`), `GuardianTextoRecortado.Buscar` (Tasks 1-2).
- Produces: `PART_Breadcrumb` es un `Grid` con columnas `Auto,Auto,*`; `PART_BreadcrumbActual` tiene `TextWrapping="Wrap"`. Mismas partes con los mismos nombres.

- [ ] **Step 1: Escribir el test (rojo)**

`tests/StockApp.Presentation.VisualTests/EncabezadoPaginaBreadcrumbTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Ledger Fase 0, Task 12: el tramo actual del breadcrumb no tenía límite y un nombre largo se
/// salía de la card. Con métricas reales de Inter (Skia), baja de línea en vez de recortarse.
/// </summary>
public class EncabezadoPaginaBreadcrumbTests
{
    private const string Largo = "Pintura látex blanca 4 L para exteriores, color blanco tiza, marca de prueba";

    [AvaloniaFact]
    public void BreadcrumbLargo_EnUnaCardAngosta_BajaDeLineaSinRecortar()
    {
        var encabezado = new EncabezadoPagina { Titulo = "Editar producto", BreadcrumbOrigen = "Productos", BreadcrumbActual = Largo };
        var vista = new UserControl
        {
            Classes = { "ui2" },
            Content = new Border { Width = 400, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = encabezado },
        };
        IReadOnlyList<string> recortes = [];
        double altoActual = 0;
        RenderizadorVista.RenderizarAPng(vista, null,
            Path.Combine(Directory.CreateTempSubdirectory("breadcrumb-").FullName, "vista.png"),
            v =>
            {
                recortes = GuardianTextoRecortado.Buscar(v);
                altoActual = v.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_BreadcrumbActual").Bounds.Height;
            });

        Assert.Empty(recortes);
        Assert.True(altoActual > 16, $"El tramo actual tenía que ocupar más de una línea de 16 px y mide {altoActual}.");
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~EncabezadoPaginaBreadcrumbTests"`
Expected: FAIL en `Assert.Empty(recortes)` con un elemento `"Pintura látex blanca 4 L para exteriores, …" queda fuera de Border`.

- [ ] **Step 2: Implementar**

En `ComponentesV2.axaml`, template de `EncabezadoPagina`, reemplazar el `StackPanel x:Name="PART_Breadcrumb" …>…</StackPanel>` por:

```xml
                    <!-- Grid y no StackPanel horizontal: en un StackPanel el tramo actual recibe todo
                         el ancho que pide y un nombre largo se sale de la card. En la columna * baja
                         de línea (Fase 1, Task 4). base.css .v2-breadcrumb lo replica. -->
                    <Grid x:Name="PART_Breadcrumb"
                          ColumnDefinitions="Auto,Auto,*"
                          Margin="0,0,0,8"
                          IsVisible="{TemplateBinding BreadcrumbOrigen, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
                        <Button x:Name="PART_BreadcrumbOrigen"
                                Grid.Column="0"
                                VerticalAlignment="Top"
                                Theme="{StaticResource V2TemaEnlace}"
                                Content="{TemplateBinding BreadcrumbOrigen}"
                                Command="{TemplateBinding BreadcrumbComando}" />
                        <TextBlock x:Name="PART_BreadcrumbSeparador"
                                   Grid.Column="1"
                                   VerticalAlignment="Top"
                                   Text=" / "
                                   FontSize="{DynamicResource V2FuenteControlTamano}"
                                   Foreground="{DynamicResource V2TextoTerciarioBrush}" />
                        <TextBlock x:Name="PART_BreadcrumbActual"
                                   Grid.Column="2"
                                   VerticalAlignment="Top"
                                   TextWrapping="Wrap"
                                   Text="{TemplateBinding BreadcrumbActual}"
                                   FontSize="{DynamicResource V2FuenteControlTamano}"
                                   Foreground="{DynamicResource V2TextoTerciarioBrush}" />
                    </Grid>
```

En `base.css`, reemplazar la regla `.v2-breadcrumb { … }` y agregar la del tramo actual:

```css
.v2-breadcrumb {
  display: flex; align-items: flex-start; margin-bottom: 8px; white-space: pre;
  font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-terciario);
}
/* Tramo actual: ocupa el resto y baja de línea (TextWrapping=Wrap parte también palabras largas). */
.v2-breadcrumb > span:last-child { flex: 1 1 0; min-width: 0; white-space: normal; overflow-wrap: anywhere; }
```

En `TarjetaContenidoEncabezadoTests.cs` línea 70: `Parte<StackPanel>(encabezado, "PART_Breadcrumb")` → `Parte<Grid>(encabezado, "PART_Breadcrumb")`.

- [ ] **Step 3: Verde**

Run: `dotnet build StockApp.sln` y luego `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj` y `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TarjetaContenidoEncabezadoTests"`
Expected: PASS. En particular `Catalogo_IgualAlMockup` sigue en 11 teselas o menos (el breadcrumb corto del catálogo no se movió).

- [ ] **Step 4: Mutación quitando**

```bash
cp src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml "$SCRATCH/ComponentesV2.axaml.bak"
```

Quitar `TextWrapping="Wrap"` de `PART_BreadcrumbActual`. Correr el filtro `EncabezadoPaginaBreadcrumbTests`: rojo (`pide … y tiene …` o `queda fuera de Border`). Restaurar con `cp` y ver el verde (si sigue rojo: `dotnet build-server shutdown` + `rm -rf` de `obj/bin`).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml docs/ui-v2/mockups/_base/base.css tests/StockApp.Presentation.UiTests/V2/TarjetaContenidoEncabezadoTests.cs tests/StockApp.Presentation.VisualTests/EncabezadoPaginaBreadcrumbTests.cs
git commit -m "fix(ui-v2): el breadcrumb baja de línea en vez de recortar un nombre largo"
```

### Task 5: Componentes y estilos del shell y del acceso: `ItemSidebar`, tipografías y botón de ícono

**Files:**
- Create: `src/StockApp.Presentation/Controls/V2/ItemSidebar.cs`
- Modify: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` (`ControlTheme` de `ItemSidebar`)
- Modify: `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml` (clases tipográficas y `Button.ghost.icono`)
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `tests/StockApp.Presentation.UiTests/V2/ItemSidebarTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/TipografiaShellV2Tests.cs`

**Interfaces:**
- Consumes: tokens `V2AltoItemSidebar`, `V2RadioControl`, `V2FuenteItemSidebar*`, `V2FuenteHeaderSeccionSidebar*`, `V2FuenteMarca*`, `V2FuenteTituloLogin*`, `V2FuentePieConteo*`, brushes `V2*`; helpers `MontajeV2` (`Montar`, `Buscar`, `Parte`, `ColorDe`, `Hex`, `Clic`) y `ComandoContador` (Fase 0).
- Produces:
  - `public class ItemSidebar : Button` en `StockApp.Presentation.Controls.V2`, `StyleKeyOverride => typeof(ItemSidebar)`, con `StyledProperty<string?> Icono`, `StyledProperty<string?> Titulo`, `StyledProperty<bool> EstaActivo` (pseudoclase `:activo`). Partes: `Border#PART_Fondo`, `Icon#PART_Icono`, `TextBlock#PART_Titulo`.
  - Clases de `TextBlock` bajo `.ui2`: `marca` (15/700 primario), `marca-subtitulo` (12/400 texto terciario, P6), `header-seccion-sidebar` (11/600, `LetterSpacing` 0.6, texto grupo), `titulo-login` (20/600 primario), `avatar` (11/600 etiqueta), `usuario-nombre` (12/500 primario), `usuario-rol` (12/400 terciario), `version` (12/400 texto grupo).
  - Estilo `.ui2 Button.ghost.icono`: 32×32, padding 0, contenido centrado.
  - CSS: `.v2-sidebar`, `.v2-sidebar-nav`, `.v2-marca`, `.v2-marca-subtitulo`, `.v2-header-seccion-sidebar`, `.v2-item-sidebar`, `.v2-item-sidebar--activo`, `.v2-sidebar-pie`, `.v2-avatar`, `.v2-usuario`, `.v2-usuario-nombre`, `.v2-usuario-rol`, `.v2-version`, `.v2-titulo-login`, `.v2-enlace`, `.v2-boton--icono`, `.v2-boton--deshabilitado`.

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.Presentation.UiTests/V2/ItemSidebarTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Optris.Icons.Avalonia;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Ítem del sidebar v2 (spec §2.2 alto 32, §2.3 12/500, §4.1 pastilla activa).</summary>
public class ItemSidebarTests
{
    private static ItemSidebar Item(bool activo = false) =>
        Buscar<ItemSidebar>(Montar(
            $"""<v2:ItemSidebar x:Name="I" Icono="mdi-package-variant" Titulo="Productos" EstaActivo="{activo}" />"""), "I");

    [AvaloniaFact]
    public void Inactivo_Alto32SinFondoTextoEtiquetaIconoTerciario()
    {
        var item = Item();

        Assert.Equal(32, item.Bounds.Height);
        Assert.Equal(Colors.Transparent, ColorDe(Parte<Border>(item, "PART_Fondo").Background));
        Assert.Equal(new CornerRadius(8), Parte<Border>(item, "PART_Fondo").CornerRadius);
        var titulo = Parte<TextBlock>(item, "PART_Titulo");
        Assert.Equal("Productos", titulo.Text);
        Assert.Equal(12, titulo.FontSize);
        Assert.Equal(FontWeight.Medium, titulo.FontWeight);
        Assert.Equal(Hex("#44403C"), ColorDe(titulo.Foreground));
        var icono = Parte<Icon>(item, "PART_Icono");
        Assert.Equal("mdi-package-variant", icono.Value);
        Assert.Equal(Hex("#78716C"), ColorDe(icono.Foreground));
    }

    [AvaloniaFact]
    public void Activo_PastillaAcentoSuaveTextoAcentoOscuroIconoAcento()
    {
        var item = Item(activo: true);

        Assert.Equal(Hex("#DBEAFE"), ColorDe(Parte<Border>(item, "PART_Fondo").Background));
        Assert.Equal(Hex("#1E3A8A"), ColorDe(Parte<TextBlock>(item, "PART_Titulo").Foreground));
        Assert.Equal(Hex("#1D4ED8"), ColorDe(Parte<Icon>(item, "PART_Icono").Foreground));
    }

    [AvaloniaFact]
    public void CambiarEstaActivo_ActualizaLaPastilla()
    {
        // Ruling 6 del sidebar: EstaActivo cambia en cada navegación sin reconstruir el ítem.
        var item = Item();

        item.EstaActivo = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#DBEAFE"), ColorDe(Parte<Border>(item, "PART_Fondo").Background));
    }

    [AvaloniaFact]
    public void Hover_FondoNeutroYNoElVerdeViejo()
    {
        // Review Focus 5 de la Fase 0: un Button no puede tomar el "Button:pointerover" viejo.
        var item = Item();

        ((IPseudoClasses)item.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<Border>(item, "PART_Fondo").Background));
    }

    [AvaloniaFact]
    public void ActivoConHover_SigueEnLaPastillaActiva()
    {
        var item = Item(activo: true);

        ((IPseudoClasses)item.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#DBEAFE"), ColorDe(Parte<Border>(item, "PART_Fondo").Background));
    }

    [AvaloniaFact]
    public void Click_EjecutaElComando()
    {
        var ventana = Montar("""<v2:ItemSidebar x:Name="I" Titulo="Inicio" />""");
        var item = Buscar<ItemSidebar>(ventana, "I");
        var comando = new ComandoContador();
        item.Command = comando;

        Clic(ventana, item);

        Assert.Equal(1, comando.Ejecuciones);
    }
}
```

`tests/StockApp.Presentation.UiTests/V2/TipografiaShellV2Tests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Tipografías del shell y del acceso (spec §2.3) y botón ghost de solo ícono.</summary>
public class TipografiaShellV2Tests
{
    [AvaloniaTheory]
    [InlineData("marca", 15, 700, "#1C1917")]
    [InlineData("marca-subtitulo", 12, 400, "#78716C")]
    [InlineData("header-seccion-sidebar", 11, 600, "#A8A29E")]
    [InlineData("titulo-login", 20, 600, "#1C1917")]
    [InlineData("avatar", 11, 600, "#44403C")]
    [InlineData("usuario-nombre", 12, 500, "#1C1917")]
    [InlineData("usuario-rol", 12, 400, "#78716C")]
    [InlineData("version", 12, 400, "#A8A29E")]
    public void ClaseTipografica_TamanoPesoYColorDeLaSpec(string clase, double tamano, int peso, string color)
    {
        var texto = Buscar<TextBlock>(Montar($"""<TextBlock x:Name="T" Classes="{clase}" Text="Texto" />"""), "T");

        Assert.Equal(tamano, texto.FontSize);
        Assert.Equal((FontWeight)peso, texto.FontWeight);
        Assert.Equal(Hex(color), ColorDe(texto.Foreground));
    }

    [AvaloniaFact]
    public void HeaderSeccionSidebar_LetterSpacing06()
    {
        var texto = Buscar<TextBlock>(Montar("""<TextBlock x:Name="T" Classes="header-seccion-sidebar" Text="INVENTARIO" />"""), "T");

        Assert.Equal(0.6, texto.LetterSpacing);
    }

    [AvaloniaFact]
    public void ClaseFueraDeUi2_NoCambiaNada()
    {
        // Convivencia: "marca" no existe en el sistema viejo, fuera de .ui2 no aplica.
        var texto = Buscar<TextBlock>(Montar("""<TextBlock x:Name="T" Classes="marca" Text="Texto" />""", conScopeUi2: false), "T");

        Assert.NotEqual(15, texto.FontSize);
    }

    [AvaloniaFact]
    public void BotonGhostIcono_32x32SinPadding()
    {
        var boton = Buscar<Button>(Montar("""
            <Button x:Name="B" Classes="ghost icono"><i:Icon Value="mdi-logout" FontSize="16" /></Button>
            """), "B");

        Assert.Equal(32, boton.Bounds.Width);
        Assert.Equal(32, boton.Bounds.Height);
        Assert.Equal(new Avalonia.Thickness(0), boton.Padding);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ItemSidebarTests|FullyQualifiedName~TipografiaShellV2Tests"`
Expected: FAIL de compilación, `The type or namespace name 'ItemSidebar' could not be found`.

- [ ] **Step 2: El componente**

`src/StockApp.Presentation/Controls/V2/ItemSidebar.cs`:

```csharp
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Optris.Icons.Avalonia;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Ítem del sidebar v2 (spec §4.1): ícono, título y pastilla cuando está activo. Hereda de Button
/// para tener Command, foco y teclado (y para que ShellMainViewGatesTests lo encuentre por la
/// identidad del Command); su StyleKey es ItemSidebar, así que los estilos "Button" viejos y v2
/// no lo alcanzan (mismo patrón que TarjetaAcceso).
/// </summary>
public class ItemSidebar : Button
{
    private Icon? _icono;

    public static readonly StyledProperty<string?> IconoProperty =
        AvaloniaProperty.Register<ItemSidebar, string?>(nameof(Icono));

    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<ItemSidebar, string?>(nameof(Titulo));

    public static readonly StyledProperty<bool> EstaActivoProperty =
        AvaloniaProperty.Register<ItemSidebar, bool>(nameof(EstaActivo));

    protected override Type StyleKeyOverride => typeof(ItemSidebar);

    /// <summary>Clave de ícono Material Design, p. ej. "mdi-home".</summary>
    public string? Icono
    {
        get => GetValue(IconoProperty);
        set => SetValue(IconoProperty, value);
    }

    public string? Titulo
    {
        get => GetValue(TituloProperty);
        set => SetValue(TituloProperty, value);
    }

    /// <summary>Pastilla activa (pseudoclase :activo). La calcula ShellMainViewModel por navegación.</summary>
    public bool EstaActivo
    {
        get => GetValue(EstaActivoProperty);
        set => SetValue(EstaActivoProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _icono = e.NameScope.Find<Icon>("PART_Icono");
        AplicarIcono();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EstaActivoProperty)
        {
            PseudoClasses.Set(":activo", change.GetNewValue<bool>());
        }
        else if (change.Property == IconoProperty)
        {
            AplicarIcono();
        }
    }

    // Igual que TarjetaAcceso.AplicarIcono: Optris maneja clave nula o vacía sin lanzar.
    private void AplicarIcono()
    {
        if (_icono is not null)
        {
            _icono.Value = Icono ?? string.Empty;
        }
    }
}
```

En `ComponentesV2.axaml`, después del `ControlTheme` de `TarjetaAcceso`:

```xml
    <!-- Ítem del sidebar (spec §4.1). Espejo CSS: .v2-item-sidebar en base.css. -->
    <ControlTheme x:Key="{x:Type v2:ItemSidebar}" TargetType="v2:ItemSidebar">
        <Setter Property="Height" Value="{DynamicResource V2AltoItemSidebar}" />
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <ControlTemplate>
                <Border x:Name="PART_Fondo"
                        CornerRadius="{DynamicResource V2RadioControl}"
                        Padding="12,0">
                    <Grid ColumnDefinitions="Auto,*">
                        <!-- Value lo asigna ItemSidebar.AplicarIcono. -->
                        <i:Icon x:Name="PART_Icono"
                                Grid.Column="0"
                                Margin="0,0,8,0"
                                FontSize="16"
                                VerticalAlignment="Center" />
                        <TextBlock x:Name="PART_Titulo"
                                   Grid.Column="1"
                                   VerticalAlignment="Center"
                                   Text="{TemplateBinding Titulo}"
                                   FontSize="{DynamicResource V2FuenteItemSidebarTamano}"
                                   FontWeight="{DynamicResource V2FuenteItemSidebarPeso}" />
                    </Grid>
                </Border>
            </ControlTemplate>
        </Setter>

        <Style Selector="^ /template/ Border#PART_Fondo">
            <Setter Property="Background" Value="Transparent" />
        </Style>
        <Style Selector="^ /template/ TextBlock#PART_Titulo">
            <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
        </Style>
        <Style Selector="^ /template/ i|Icon#PART_Icono">
            <Setter Property="Foreground" Value="{DynamicResource V2TextoTerciarioBrush}" />
        </Style>
        <Style Selector="^:pointerover /template/ Border#PART_Fondo">
            <Setter Property="Background" Value="{DynamicResource V2HoverNeutroBrush}" />
        </Style>
        <!-- Después de :pointerover: activo con el mouse encima sigue en la pastilla activa. -->
        <Style Selector="^:activo /template/ Border#PART_Fondo">
            <Setter Property="Background" Value="{DynamicResource V2AcentoSuaveBrush}" />
        </Style>
        <Style Selector="^:activo /template/ TextBlock#PART_Titulo">
            <Setter Property="Foreground" Value="{DynamicResource V2AcentoOscuroBrush}" />
        </Style>
        <Style Selector="^:activo /template/ i|Icon#PART_Icono">
            <Setter Property="Foreground" Value="{DynamicResource V2AcentoBrush}" />
        </Style>
    </ControlTheme>
```

- [ ] **Step 3: Estilos de `ControlesV2.axaml`**

Debajo del estilo `.ui2 TextBlock.pie-conteo`:

```xml
    <!-- Shell y pantallas de acceso (Fase 1). Espejo CSS en base.css (.v2-marca, etc.). -->
    <Style Selector=".ui2 TextBlock.marca">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteMarcaTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteMarcaPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoPrimarioBrush}" />
    </Style>
    <!-- Línea "Municipio de Carmelo" bajo la marca (P6): mismo tamaño, peso y color que usuario-rol. -->
    <Style Selector=".ui2 TextBlock.marca-subtitulo">
        <Setter Property="FontSize" Value="{DynamicResource V2FuentePieConteoTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuentePieConteoPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoTerciarioBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.header-seccion-sidebar">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteHeaderSeccionSidebarTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteHeaderSeccionSidebarPeso}" />
        <Setter Property="LetterSpacing" Value="{DynamicResource V2FuenteHeaderSeccionSidebarEspaciadoLetras}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoGrupoBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.titulo-login">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteTituloLoginTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteTituloLoginPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoPrimarioBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.avatar">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteHeaderSeccionSidebarTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteHeaderSeccionSidebarPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.usuario-nombre">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteItemSidebarTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteItemSidebarPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoPrimarioBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.usuario-rol">
        <Setter Property="FontSize" Value="{DynamicResource V2FuentePieConteoTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuentePieConteoPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoTerciarioBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.version">
        <Setter Property="FontSize" Value="{DynamicResource V2FuentePieConteoTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuentePieConteoPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoGrupoBrush}" />
    </Style>
```

Debajo del estilo `.ui2 Button.ghost:pointerover …`:

```xml
    <!-- Ghost de solo ícono (cerrar sesión del pie del sidebar). Espejo CSS: .v2-boton--icono. -->
    <Style Selector=".ui2 Button.ghost.icono">
        <Setter Property="Width" Value="32" />
        <Setter Property="Height" Value="32" />
        <Setter Property="Padding" Value="0" />
    </Style>
```

Si `V2FuenteHeaderSeccionSidebarEspaciadoLetras` no es un `x:Double` en `Tokens.axaml`, el `Setter` de `LetterSpacing` falla al cargar: verificar en el archivo generado que el tipo sea el mismo que usa `DataGridV2.axaml` para `V2FuenteHeaderColumnaEspaciadoLetras` y copiar esa forma.

- [ ] **Step 4: `base.css`**

Agregar al final:

```css
/* Botón deshabilitado (ControlesV2.axaml, Button.primario|secundario|peligro:disabled). */
.v2-boton--deshabilitado { background: var(--v2-color-deshabilitado-fondo); border-color: var(--v2-color-borde); color: var(--v2-color-deshabilitado-texto); }
/* Ghost de solo ícono (Button.ghost.icono): 32x32, ícono centrado. */
.v2-boton--icono { width: 32px; height: 32px; padding: 0; }
/* Botón con apariencia de link (V2TemaEnlace). */
.v2-enlace { color: var(--v2-color-acento); font-size: var(--v2-fuente-enlace-tamano); line-height: var(--v2-fuente-enlace-alto-linea); font-weight: var(--v2-fuente-enlace-peso); }

/* Tipografía del shell y del acceso (ControlesV2.axaml, TextBlock.marca, etc.) */
.v2-marca { font-size: var(--v2-fuente-marca-tamano); line-height: var(--v2-fuente-marca-alto-linea); font-weight: var(--v2-fuente-marca-peso); color: var(--v2-color-texto-primario); }
.v2-marca-subtitulo { font-size: var(--v2-fuente-pie-conteo-tamano); line-height: var(--v2-fuente-pie-conteo-alto-linea); font-weight: var(--v2-fuente-pie-conteo-peso); color: var(--v2-color-texto-terciario); }
.v2-titulo-login { font-size: var(--v2-fuente-titulo-login-tamano); line-height: var(--v2-fuente-titulo-login-alto-linea); font-weight: var(--v2-fuente-titulo-login-peso); color: var(--v2-color-texto-primario); }
.v2-header-seccion-sidebar {
  font-size: var(--v2-fuente-header-seccion-sidebar-tamano); line-height: var(--v2-fuente-header-seccion-sidebar-alto-linea);
  font-weight: var(--v2-fuente-header-seccion-sidebar-peso); letter-spacing: var(--v2-fuente-header-seccion-sidebar-espaciado-letras);
  text-transform: var(--v2-fuente-header-seccion-sidebar-transformacion); color: var(--v2-color-texto-grupo);
}
.v2-usuario-nombre { font-size: var(--v2-fuente-item-sidebar-tamano); line-height: var(--v2-fuente-item-sidebar-alto-linea); font-weight: var(--v2-fuente-item-sidebar-peso); color: var(--v2-color-texto-primario); white-space: nowrap; }
.v2-usuario-rol { font-size: var(--v2-fuente-pie-conteo-tamano); line-height: var(--v2-fuente-pie-conteo-alto-linea); font-weight: var(--v2-fuente-pie-conteo-peso); color: var(--v2-color-texto-terciario); white-space: nowrap; }
.v2-version { font-size: var(--v2-fuente-pie-conteo-tamano); line-height: var(--v2-fuente-pie-conteo-alto-linea); font-weight: var(--v2-fuente-pie-conteo-peso); color: var(--v2-color-texto-grupo); }

/* Sidebar (ShellMainView.axaml). Border 240 con borde derecho 1 y padding 16x12; DockPanel:
   marca arriba, versión y pie abajo, navegación (ScrollViewer) en el resto. */
.v2-sidebar {
  position: absolute; left: 0; top: 0; bottom: 0; width: var(--v2-ancho-sidebar);
  display: flex; flex-direction: column; padding: 16px 12px;
  background: var(--v2-color-superficie); border-right: 1px solid var(--v2-color-borde);
}
.v2-sidebar > .v2-marca { margin: 4px 8px 0; }
.v2-sidebar > .v2-marca-subtitulo { margin: 2px 8px 12px; }
.v2-sidebar-nav { flex: 1 1 0; min-height: 0; overflow: hidden; display: flex; flex-direction: column; }
.v2-sidebar-nav > .v2-header-seccion-sidebar { margin: 12px 8px 4px; }
/* ItemSidebar (Controls/V2/ItemSidebar): alto 32, radio 8, padding 0 12, ícono 16 + 8. */
.v2-item-sidebar {
  display: flex; align-items: center; flex: 0 0 auto; height: var(--v2-alto-item-sidebar); padding: 0 12px;
  border-radius: var(--v2-radio-control); white-space: nowrap;
  font-size: var(--v2-fuente-item-sidebar-tamano); line-height: var(--v2-fuente-item-sidebar-alto-linea); font-weight: var(--v2-fuente-item-sidebar-peso);
  color: var(--v2-color-texto-etiqueta);
}
.v2-item-sidebar .v2-icono { margin-right: 8px; color: var(--v2-color-texto-terciario); }
.v2-item-sidebar--activo { background: var(--v2-color-acento-suave); color: var(--v2-color-acento-oscuro); }
.v2-item-sidebar--activo .v2-icono { color: var(--v2-color-acento); }
/* Pie: borde superior, avatar 28 + 8, nombre y rol, botón de ícono; versión debajo. */
.v2-sidebar-pie { display: flex; align-items: center; margin-top: 8px; padding: 8px 4px 4px; border-top: 1px solid var(--v2-color-borde); }
.v2-avatar {
  display: flex; align-items: center; justify-content: center; flex: 0 0 28px; width: 28px; height: 28px; margin-right: 8px;
  border-radius: 14px; background: var(--v2-color-borde-input);
  font-size: var(--v2-fuente-header-seccion-sidebar-tamano); line-height: var(--v2-fuente-header-seccion-sidebar-alto-linea);
  font-weight: var(--v2-fuente-header-seccion-sidebar-peso); color: var(--v2-color-texto-etiqueta);
}
.v2-usuario { flex: 1 1 0; min-width: 0; }
.v2-sidebar > .v2-version { padding: 4px 4px 0; }
```

Verificar que todas las variables usadas existen en `tokens.css` (`grep -o -- '--v2-[a-z0-9-]*' docs/ui-v2/mockups/_base/base.css | sort -u` contra `tokens.css`). En particular `--v2-color-deshabilitado-fondo`, `--v2-color-deshabilitado-texto`, `--v2-color-texto-sobre-acento` y `--v2-fuente-enlace-*`; si alguna falta, se usa el nombre que sí genera `tokens.css` para el mismo token (nunca un valor literal).

- [ ] **Step 5: Verde**

Run: `dotnet build StockApp.sln` y `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~V2"`
Expected: PASS (los 6 de `ItemSidebarTests`, los 11 de `TipografiaShellV2Tests` y el resto de `V2/`). Luego `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`: PASS (el catálogo no cambió).

- [ ] **Step 6: Mutación quitando**

```bash
cp src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml "$SCRATCH/ComponentesV2.axaml.bak"
cp src/StockApp.Presentation/Themes/V2/ControlesV2.axaml "$SCRATCH/ControlesV2.axaml.bak"
```

(a) Quitar el estilo `^:activo /template/ Border#PART_Fondo` → rojo en `Activo_…`, `CambiarEstaActivo_…` y `ActivoConHover_…`. Restaurar con `cp`.
(b) Quitar `protected override Type StyleKeyOverride => typeof(ItemSidebar);` (backup previo de `ItemSidebar.cs` con `cp`) → rojo (el `ControlTheme` deja de aplicar o el hover toma el estilo viejo). Restaurar con `cp`.
(c) Quitar `.ui2` del selector `.ui2 TextBlock.marca` → rojo en `ClaseFueraDeUi2_NoCambiaNada`. Restaurar con `cp`.

Si un rojo esperado no aparece: `dotnet build-server shutdown` + `rm -rf` de `obj/bin` de Presentation y UiTests, y repetir. Pegar los tres rojos en el reporte.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2/ItemSidebar.cs src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml src/StockApp.Presentation/Themes/V2/ControlesV2.axaml docs/ui-v2/mockups/_base/base.css tests/StockApp.Presentation.UiTests/V2/ItemSidebarTests.cs tests/StockApp.Presentation.UiTests/V2/TipografiaShellV2Tests.cs
git commit -m "feat(ui-v2): ItemSidebar, tipografías del shell y del acceso, botón ghost de ícono"
```

---

## Bloque B — Mockups por módulo y aprobación del usuario (GATE antes del XAML)

Spec §6.1 y §7: "mockups de sus pantallas → aprobación del usuario → XAML". Cada task de este bloque termina en un GATE: el ejecutor PARA, presenta los mockups al usuario y no sigue hasta tener la aprobación explícita del módulo. Si el usuario pide cambios, se aplican en el mockup (y, si tocan un componente o una clase de `base.css`, también en su `ControlTheme`/estilo de la Task 5, con sus tests) dentro de la misma task, y se vuelve a presentar.

Datos fijos que comparten mockups y tests (las clases `Datos*` del Bloque C usan EXACTAMENTE estos textos):

| Dato | Valor |
|---|---|
| Versión | `v0.4.0` (`IInfoApp.Version = "0.4.0"`) |
| Administrador | `UsuarioSesion(1, "mperez", Admin, "María Pérez")` → "MP", "Administrador" |
| Operador mixto | `UsuarioSesion(2, "jrodriguez", Operador, "Juan Rodríguez")` → "JR", "Operador"; permisos `GestionarProductos`, `RegistrarMovimientos`, `VerFinanzas`, `GestionarTareas` |
| Código de máquina | `7F3A-91C2-4D8E-B605` |
| Desafío | `K4PZ-8QWN-2M7R` |
| Banner | `AccionUx(BannerDiscreto, "Mejoras en reportes.", true, false, "0.5.0")` → "Nueva versión v0.5.0 disponible" |
| Modal | `AccionUx(ModalPosponible, "Esta versión corrige el cálculo del Libro caja y mejora la exportación a PDF.", true, false, "0.5.0")` |
| Bloqueo / degradado | `AccionUx(BloqueoCritico|ModoDegradado, "Esta versión corrige un problema de seguridad. La app se va a reiniciar para aplicarla.", false, true, "0.5.0")` |

Íconos: los HTML se escriben con marcadores `{{mdi-<clave>}}`. Después de escribir cada archivo se reemplazan con la salida exacta de `icono-svg` (paso "Insertar íconos" de cada task).

### Task 6: Mockups del shell (sidebar de Administrador y de Operador con permisos mixtos)

**Decisiones aplicadas:** P1 (sidebar transitorio: composición de ANÁLISIS y ADMINISTRACIÓN), P4 (scroll `Auto`) y P6 (línea "Municipio de Carmelo" bajo la marca).

**Files:**
- Create: `docs/ui-v2/mockups/shell/sidebar-admin.html`
- Create: `docs/ui-v2/mockups/shell/sidebar-operador.html`

**Interfaces:**
- Consumes: clases de `base.css` de la Task 5 (`.v2-sidebar`, `.v2-item-sidebar`, etc.), comando `icono-svg` y `capturar-mockup` de la herramienta.
- Produces: pantallas `shell-sidebar-admin` y `shell-sidebar-operador` para `ShellComparacionTests` (Task 11). Ítem activo: "Productos" (admin) y "Gastos y facturas" (operador). Área de contenido vacía (fondo app).

- [ ] **Step 1: `sidebar-admin.html`**

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Shell: sidebar de Administrador (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
</head>
<body>
<!-- Datos: DatosShell.Admin() (Task 11). Sidebar transitorio (P1, decidido): ANÁLISIS y
     ADMINISTRACIÓN listan los ítems actuales hasta las Fases 6 y 7. El Administrador no entra en
     760: la navegación se corta abajo (ScrollViewer, P4). Contenido vacío: CurrentContent = null. -->
<div class="v2-ventana">
  <aside class="v2-sidebar">
    <div class="v2-marca">Gestión Municipal</div>
    <div class="v2-marca-subtitulo">Municipio de Carmelo</div>
    <nav class="v2-sidebar-nav">
      <div class="v2-item-sidebar">{{mdi-home}}<span>Inicio</span></div>
      <div class="v2-header-seccion-sidebar">INVENTARIO</div>
      <div class="v2-item-sidebar v2-item-sidebar--activo">{{mdi-package-variant}}<span>Productos</span></div>
      <div class="v2-item-sidebar">{{mdi-tray-arrow-down}}<span>Registrar entrada</span></div>
      <div class="v2-item-sidebar">{{mdi-tray-arrow-up}}<span>Registrar salida</span></div>
      <div class="v2-item-sidebar">{{mdi-receipt-text-plus}}<span>Ingreso por factura</span></div>
      <div class="v2-item-sidebar">{{mdi-history}}<span>Historial de movimientos</span></div>
      <div class="v2-header-seccion-sidebar">FINANZAS</div>
      <div class="v2-item-sidebar">{{mdi-receipt-text}}<span>Gastos y facturas</span></div>
      <div class="v2-item-sidebar">{{mdi-cash-plus}}<span>Ingresos de caja</span></div>
      <div class="v2-item-sidebar">{{mdi-book-open-variant}}<span>Libro caja</span></div>
      <div class="v2-item-sidebar">{{mdi-chart-donut}}<span>Control POA</span></div>
      <div class="v2-item-sidebar">{{mdi-calendar-clock}}<span>Calendario de pagos</span></div>
      <div class="v2-header-seccion-sidebar">GESTIÓN</div>
      <div class="v2-item-sidebar">{{mdi-checkbox-marked-outline}}<span>Tareas</span></div>
      <div class="v2-item-sidebar">{{mdi-file-cabinet}}<span>Documentos</span></div>
      <div class="v2-header-seccion-sidebar">ANÁLISIS</div>
      <div class="v2-item-sidebar">{{mdi-currency-usd}}<span>Valorización de inventario</span></div>
      <div class="v2-item-sidebar">{{mdi-chart-pie}}<span>Stock por categoría</span></div>
      <div class="v2-item-sidebar">{{mdi-file-document}}<span>Historial por producto</span></div>
      <div class="v2-item-sidebar">{{mdi-trending-up}}<span>Productos más movidos</span></div>
      <div class="v2-item-sidebar">{{mdi-shield-search}}<span>Log de auditoría</span></div>
      <div class="v2-item-sidebar">{{mdi-chart-bar}}<span>Estadística de tareas</span></div>
      <div class="v2-header-seccion-sidebar">ADMINISTRACIÓN</div>
      <div class="v2-item-sidebar">{{mdi-shape}}<span>Categorías</span></div>
      <div class="v2-item-sidebar">{{mdi-truck}}<span>Proveedores</span></div>
      <div class="v2-item-sidebar">{{mdi-ruler}}<span>Unidades de medida</span></div>
      <div class="v2-item-sidebar">{{mdi-tag-multiple}}<span>Catálogos de tareas</span></div>
      <div class="v2-item-sidebar">{{mdi-cash-multiple}}<span>Maestros de finanzas</span></div>
      <div class="v2-item-sidebar">{{mdi-account-cog}}<span>Usuarios</span></div>
      <div class="v2-item-sidebar">{{mdi-database-cog}}<span>Mantenimiento</span></div>
    </nav>
    <div class="v2-sidebar-pie">
      <div class="v2-avatar">MP</div>
      <div class="v2-usuario">
        <div class="v2-usuario-nombre">María Pérez</div>
        <div class="v2-usuario-rol">Administrador</div>
      </div>
      <span class="v2-boton v2-boton--ghost v2-boton--icono">{{mdi-logout}}</span>
    </div>
    <div class="v2-version">v0.4.0</div>
  </aside>
</div>
</body>
</html>
```

- [ ] **Step 2: `sidebar-operador.html`**

Mismo `<head>` y estructura. Solo lo que el Operador mixto ve (Ingreso por factura exige además `RegistrarGastos`; Documentos, ANÁLISIS y ADMINISTRACIÓN quedan sin ítems visibles y sin header):

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Shell: sidebar de Operador con permisos mixtos (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
</head>
<body>
<!-- Datos: DatosShell.OperadorMixto() (Task 11): GestionarProductos, RegistrarMovimientos,
     VerFinanzas y GestionarTareas. Sin header para las secciones sin ítems visibles (§4.1). -->
<div class="v2-ventana">
  <aside class="v2-sidebar">
    <div class="v2-marca">Gestión Municipal</div>
    <div class="v2-marca-subtitulo">Municipio de Carmelo</div>
    <nav class="v2-sidebar-nav">
      <div class="v2-item-sidebar">{{mdi-home}}<span>Inicio</span></div>
      <div class="v2-header-seccion-sidebar">INVENTARIO</div>
      <div class="v2-item-sidebar">{{mdi-package-variant}}<span>Productos</span></div>
      <div class="v2-item-sidebar">{{mdi-tray-arrow-down}}<span>Registrar entrada</span></div>
      <div class="v2-item-sidebar">{{mdi-tray-arrow-up}}<span>Registrar salida</span></div>
      <div class="v2-item-sidebar">{{mdi-history}}<span>Historial de movimientos</span></div>
      <div class="v2-header-seccion-sidebar">FINANZAS</div>
      <div class="v2-item-sidebar v2-item-sidebar--activo">{{mdi-receipt-text}}<span>Gastos y facturas</span></div>
      <div class="v2-item-sidebar">{{mdi-cash-plus}}<span>Ingresos de caja</span></div>
      <div class="v2-item-sidebar">{{mdi-book-open-variant}}<span>Libro caja</span></div>
      <div class="v2-item-sidebar">{{mdi-chart-donut}}<span>Control POA</span></div>
      <div class="v2-item-sidebar">{{mdi-calendar-clock}}<span>Calendario de pagos</span></div>
      <div class="v2-header-seccion-sidebar">GESTIÓN</div>
      <div class="v2-item-sidebar">{{mdi-checkbox-marked-outline}}<span>Tareas</span></div>
    </nav>
    <div class="v2-sidebar-pie">
      <div class="v2-avatar">JR</div>
      <div class="v2-usuario">
        <div class="v2-usuario-nombre">Juan Rodríguez</div>
        <div class="v2-usuario-rol">Operador</div>
      </div>
      <span class="v2-boton v2-boton--ghost v2-boton--icono">{{mdi-logout}}</span>
    </div>
    <div class="v2-version">v0.4.0</div>
  </aside>
</div>
</body>
</html>
```

- [ ] **Step 3: Insertar íconos**

```bash
dotnet build tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj
python3 - docs/ui-v2/mockups/shell/sidebar-admin.html docs/ui-v2/mockups/shell/sidebar-operador.html <<'PY'
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
grep -c '{{mdi-' docs/ui-v2/mockups/shell/*.html
```

Expected: el `grep -c` imprime `0` para cada archivo. Si `icono-svg` sale con exit 1 (clave inexistente), `check=True` corta el script: corregir la clave (son las de `ShellMainViewModel`).

- [ ] **Step 4: Capturar y revisar**

```bash
for p in sidebar-admin sidebar-operador; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/shell/$p.html artifacts/ui-v2/mockups/shell-$p.png
done
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~MockupsDelRepo_RespetanLaConvencion"
```

Expected: dos PNG de 1280×760 sin error de fuentes ni de desborde (el capturador falla si Inter no cargó o si algo se sale de la ventana); el test de convención en verde. Abrir los PNG y chequear a ojo contra spec §4.1: pastilla activa, headers en gris claro, pie con borde superior.

- [ ] **Step 5: GATE — aprobación del usuario**

PARAR. Presentar al usuario los dos HTML (abrirlos en un navegador Chromium/Edge: `calc-size()` solo funciona ahí) o los PNG de `artifacts/ui-v2/mockups/`, junto con la lista de "Decisiones que se confirman al aprobar cada mockup" que corresponden al sidebar. No seguir hasta tener "aprobado" explícito. Registrar en `.superpowers/sdd/2026-10-01-ui-v2-fase-1-shell-y-acceso/progress.md` (crearlo si no existe): `Mockups shell: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 6: Commit**

```bash
git add docs/ui-v2/mockups/shell/
git commit -m "docs(ui-v2): mockups aprobados del shell (sidebar de Administrador y de Operador)"
```

Si la aprobación cambió `base.css` o un componente, sus archivos y tests van en este mismo commit (con un mensaje que lo diga).

### Task 7: Mockups de las pantallas de acceso (Login, Reset, BloqueoLicencia)

**Decisiones aplicadas:** P5 (la marca de agua del logo SE CONSERVA: los tres mockups del Login la llevan, `Reset` y `BloqueoLicencia` no, como en la app) y P6 (línea "Municipio de Carmelo" bajo el título del Login). AccesoLimitado no tiene mockup en esta fase (P3).

**Files:**
- Create: `docs/ui-v2/mockups/acceso/login.html`
- Create: `docs/ui-v2/mockups/acceso/login-error.html`
- Create: `docs/ui-v2/mockups/acceso/login-acceso-limitado.html`
- Create: `docs/ui-v2/mockups/acceso/reset.html`
- Create: `docs/ui-v2/mockups/acceso/bloqueo-licencia.html`

**Interfaces:**
- Consumes: `base.css` (Task 5: `.v2-titulo-login`, `.v2-marca-subtitulo`, `.v2-enlace`, `.v2-version`, `.v2-boton--deshabilitado`; Fase 0: `.v2-tarjeta`, `.v2-campo`, `.v2-input`, `.v2-alerta`, `.v2-boton`).
- Produces: pantallas `acceso-login`, `acceso-login-error`, `acceso-login-acceso-limitado`, `acceso-reset`, `acceso-bloqueo-licencia` para `AccesoComparacionTests` (Tasks 12 y 13). Geometría común: card de 400 (`--v2-ancho-card-login`) con padding 32, centrada; separación de 16 entre bloques, 20 antes del botón principal del Login y 12 antes de un enlace.

- [ ] **Step 1: `login.html`** (estado inicial: campos vacíos, "Entrar" deshabilitado como en la app)

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Acceso: Login (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  /* Layout propio de las pantallas fuera del shell: el mismo que LoginView.axaml (card de 400
     centrada, padding 32, bloques separados por Margin). */
  .v2-ventana { display: flex; align-items: center; justify-content: center; }
  .tarjeta-acceso { position: relative; z-index: 1; width: var(--v2-ancho-card-login); padding: 32px; display: flex; flex-direction: column; }
  /* Marca de agua del logo (P5): el mismo logo, escala y opacidad que LoginView.axaml
     (Image.marca-agua: Stretch Uniform, centrada, Opacity 0.28, scale(0.85)), detrás de la card.
     La comparación de píxeles la oculta con visibility:hidden (ocultarEnMockup, Task 12);
     LoginMarcaAguaTests exige que estos valores sigan siendo 0.28 y 0.85. */
  .marca-agua { position: absolute; left: 0; top: 0; width: 100%; height: 100%; object-fit: contain; opacity: 0.28; transform: scale(0.85); }
  .centro { text-align: center; }
  .mt-2 { margin-top: 2px; }
  .mt-12 { margin-top: 12px; }
  .mt-16 { margin-top: 16px; }
  .mt-20 { margin-top: 20px; }
  .boton-completo { display: flex; width: 100%; }
  .enlace-centrado { align-self: center; }
</style>
</head>
<body>
<!-- Datos: DatosAcceso.Login() (Task 12). -->
<div class="v2-ventana">
  <img class="marca-agua" src="../../../../src/StockApp.Presentation/Assets/carmelo-original.png" alt="">
  <div class="v2-tarjeta tarjeta-acceso">
    <div class="v2-titulo-login centro">Gestión Municipal</div>
    <div class="v2-marca-subtitulo centro mt-2">Municipio de Carmelo</div>
    <div class="v2-texto-secundario centro mt-16">Ingresá tus credenciales para continuar.</div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Usuario</span></div>
      <div class="v2-input"><span class="v2-input-placeholder">Nombre de usuario</span></div>
    </div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Contraseña</span></div>
      <div class="v2-input"><span class="v2-input-placeholder">Contraseña</span></div>
    </div>
    <span class="v2-boton v2-boton--primario v2-boton--deshabilitado boton-completo mt-20">Entrar</span>
    <span class="v2-enlace enlace-centrado mt-12">No puedo entrar / resetear Admin</span>
    <div class="v2-version centro mt-16">v0.4.0</div>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 2: `login-error.html`**

Igual a `login.html` (mismo `<head>` y `<style>`, incluida la marca de agua y la línea "Municipio de Carmelo"; `<title>Acceso: Login con error (UI v2)</title>`), con dos diferencias dentro de la card: después del texto secundario, la alerta; y el usuario tipeado.

```html
<!-- Datos: DatosAcceso.Login(usuario: "mperez", error: "Usuario o contraseña incorrectos.") (Task 12).
     La contraseña queda vacía a propósito: el glifo de PasswordChar no está en los mockups. -->
<div class="v2-ventana">
  <img class="marca-agua" src="../../../../src/StockApp.Presentation/Assets/carmelo-original.png" alt="">
  <div class="v2-tarjeta tarjeta-acceso">
    <div class="v2-titulo-login centro">Gestión Municipal</div>
    <div class="v2-marca-subtitulo centro mt-2">Municipio de Carmelo</div>
    <div class="v2-texto-secundario centro mt-16">Ingresá tus credenciales para continuar.</div>
    <div class="v2-alerta v2-alerta--error mt-16">Usuario o contraseña incorrectos.</div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Usuario</span></div>
      <div class="v2-input"><span>mperez</span></div>
    </div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Contraseña</span></div>
      <div class="v2-input"><span class="v2-input-placeholder">Contraseña</span></div>
    </div>
    <span class="v2-boton v2-boton--primario v2-boton--deshabilitado boton-completo mt-20">Entrar</span>
    <span class="v2-enlace enlace-centrado mt-12">No puedo entrar / resetear Admin</span>
    <div class="v2-version centro mt-16">v0.4.0</div>
  </div>
</div>
```

- [ ] **Step 3: `login-acceso-limitado.html`**

Igual a `login.html` (con la marca de agua y la línea "Municipio de Carmelo"; `<title>Acceso: Login en modo de acceso limitado (UI v2)</title>`), con la alerta de advertencia después del texto secundario:

```html
<!-- Datos: DatosAcceso.Login(soloAccesoLimitado: true) (Task 12). -->
<div class="v2-ventana">
  <img class="marca-agua" src="../../../../src/StockApp.Presentation/Assets/carmelo-original.png" alt="">
  <div class="v2-tarjeta tarjeta-acceso">
    <div class="v2-titulo-login centro">Gestión Municipal</div>
    <div class="v2-marca-subtitulo centro mt-2">Municipio de Carmelo</div>
    <div class="v2-texto-secundario centro mt-16">Ingresá tus credenciales para continuar.</div>
    <div class="v2-alerta v2-alerta--advertencia mt-16">Modo de acceso limitado: la licencia está vencida. Solo vas a poder descargar backups.</div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Usuario</span></div>
      <div class="v2-input"><span class="v2-input-placeholder">Nombre de usuario</span></div>
    </div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Contraseña</span></div>
      <div class="v2-input"><span class="v2-input-placeholder">Contraseña</span></div>
    </div>
    <span class="v2-boton v2-boton--primario v2-boton--deshabilitado boton-completo mt-20">Entrar</span>
    <span class="v2-enlace enlace-centrado mt-12">No puedo entrar / resetear Admin</span>
    <div class="v2-version centro mt-16">v0.4.0</div>
  </div>
</div>
```

- [ ] **Step 4: `reset.html`** (códigos ya generados; token y contraseña vacíos, así que "2) Resetear Admin" está deshabilitado)

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Acceso: Recuperar acceso de Admin (UI v2)</title>
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
<!-- Datos: DatosAcceso.Reset() (Task 13). -->
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
    <span class="v2-boton v2-boton--ghost boton-completo mt-16">Volver al login</span>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 5: `bloqueo-licencia.html`**

Antes de escribirlo, verificar en `BloqueoLicenciaViewModel.cs` el `CanExecute` de `ActivarCommand`: si con `LicenciaPegada` vacía está deshabilitado (lo sugiere `[NotifyCanExecuteChangedFor(nameof(ActivarCommand))]` sobre `_licenciaPegada`), "Activar" lleva `v2-boton--deshabilitado`; si no, se le quita esa clase.

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Acceso: Activación de licencia (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  .v2-ventana { display: flex; align-items: center; justify-content: center; }
  .tarjeta-acceso { width: var(--v2-ancho-card-login); padding: 32px; display: flex; flex-direction: column; }
  .centro { text-align: center; }
  .mt-12 { margin-top: 12px; }
  .mt-16 { margin-top: 16px; }
  .boton-completo { display: flex; width: 100%; }
  .enlace-centrado { align-self: center; }
  .fila-codigo { display: flex; align-items: center; gap: 8px; }
  .codigo {
    flex: 1 1 0; min-width: 0; overflow-wrap: anywhere; font-variant-numeric: tabular-nums;
    font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-etiqueta);
  }
  .multilinea { height: 88px; align-items: flex-start; padding: 8px 12px; }
</style>
</head>
<body>
<!-- Datos: DatosAcceso.BloqueoLicencia() (Task 13): el código aparece solo si el
     DataContextChanged de la vista corrió CargarEstadoAsync. -->
<div class="v2-ventana">
  <div class="v2-tarjeta tarjeta-acceso">
    <div class="v2-titulo-login centro">Activación de licencia</div>
    <div class="v2-texto-secundario centro mt-16">Este servidor no tiene una licencia activada. Pasale el código de máquina al proveedor y pegá la licencia que te devuelva.</div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Código de máquina</span></div>
      <div class="fila-codigo"><span class="codigo">7F3A-91C2-4D8E-B605</span><span class="v2-boton v2-boton--secundario">Copiar</span></div>
    </div>
    <div class="v2-campo mt-16">
      <div class="v2-campo-etiqueta"><span>Licencia</span></div>
      <div class="v2-input multilinea"><span class="v2-input-placeholder">Pegá acá el texto de la licencia</span></div>
    </div>
    <span class="v2-boton v2-boton--primario v2-boton--deshabilitado boton-completo mt-16">Activar</span>
    <span class="v2-enlace enlace-centrado mt-12">¿Necesitás bajar un backup? Iniciar sesión</span>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 6: Capturar y revisar**

```bash
for p in login login-error login-acceso-limitado reset bloqueo-licencia; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/acceso/$p.html artifacts/ui-v2/mockups/acceso-$p.png
done
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~MockupsDelRepo_RespetanLaConvencion"
```

Expected: cinco PNG sin error; convención en verde. Chequear a ojo: card centrada, el logo detrás de la card (se ve alrededor y NO sobre la card), la línea "Municipio de Carmelo" bajo el título, ningún texto real cambiado (comparar contra los `.axaml` actuales de `LoginView`, `ResetAdminView` y `BloqueoLicenciaView`).

- [ ] **Step 7: GATE — aprobación del usuario**

PARAR. Presentar los cinco mockups con las decisiones de "Login y pantallas fuera del shell" de la lista de arriba (enlaces en vez de botones ghost, código en Inter, éxito del Reset como `Alerta` info, "Entrar" deshabilitado) y las decisiones P5/P6 ya tomadas (marca de agua conservada, con el logo visible en los tres mockups del Login, y la línea "Municipio de Carmelo"): pedirle que confirme que el logo se ve como en la app actual. Para la comparación de píxeles el logo se oculta (Task 12), pero ESTOS mockups lo llevan puesto: es lo que el usuario aprueba. No seguir sin "aprobado". Registrar en el `progress.md`: `Mockups acceso: aprobados por <quién> <fecha>; cambios: …`.

- [ ] **Step 8: Commit**

```bash
git add docs/ui-v2/mockups/acceso/
git commit -m "docs(ui-v2): mockups aprobados de Login, Reset y activación de licencia"
```

### Task 8: Mockups de los diálogos (Confirmación, Mensaje, PedirTexto)

**Decisión aplicada:** P2 (`ReclasificarTareaDialog` queda para la Fase 5).

**Files:**
- Create: `docs/ui-v2/mockups/dialogos/confirmacion.html`
- Create: `docs/ui-v2/mockups/dialogos/mensaje.html`
- Create: `docs/ui-v2/mockups/dialogos/pedir-texto.html`

**Interfaces:**
- Consumes: `base.css` (`.v2-cuerpo`, `.v2-boton`, `.v2-input`).
- Produces: pantallas `dialogos-confirmacion`, `dialogos-mensaje`, `dialogos-pedir-texto` para `DialogosComparacionTests` (Task 15). Representan el ÁREA CLIENTE del diálogo (sin barra de título del sistema), con el ancho del `Window` (360 / 360 / 400), centrada sobre fondo app.

- [ ] **Step 1: `confirmacion.html`**

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Diálogo: confirmación (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  /* Área cliente del Window (ConfirmacionDialog Width=360): Border raíz con padding 24 sobre
     superficie, StackPanel con 24 entre el mensaje y la fila de botones (a la derecha, 8). La
     barra de título es del sistema operativo y no se compara. */
  .v2-ventana { display: flex; align-items: center; justify-content: center; }
  .dialogo { width: 360px; padding: 24px; background: var(--v2-color-superficie); display: flex; flex-direction: column; gap: 24px; }
  .botones { display: flex; justify-content: flex-end; gap: 8px; }
</style>
</head>
<body>
<!-- Datos: new ConfirmacionDialog("¿Cerrar la sesión?") (Task 15), el mensaje real de cerrar sesión. -->
<div class="v2-ventana">
  <div class="dialogo">
    <div class="v2-cuerpo">¿Cerrar la sesión?</div>
    <div class="botones">
      <span class="v2-boton v2-boton--secundario">Cancelar</span>
      <span class="v2-boton v2-boton--primario">Sí, continuar</span>
    </div>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 2: `mensaje.html`**

Mismo `<head>` y `<style>` (`<title>Diálogo: mensaje (UI v2)</title>`):

```html
<!-- Datos: new MensajeDialog("No se puede dar de baja la categoría: tiene productos asociados.") (Task 15). -->
<div class="v2-ventana">
  <div class="dialogo">
    <div class="v2-cuerpo">No se puede dar de baja la categoría: tiene productos asociados.</div>
    <div class="botones">
      <span class="v2-boton v2-boton--primario">Aceptar</span>
    </div>
  </div>
</div>
```

- [ ] **Step 3: `pedir-texto.html`**

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Diálogo: pedir texto (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  /* PedirTextoDialog Width=400: mensaje, TextBox multilínea de 80 y botones; 16 entre bloques y
     8 más antes de los botones. */
  .v2-ventana { display: flex; align-items: center; justify-content: center; }
  .dialogo { width: 400px; padding: 24px; background: var(--v2-color-superficie); display: flex; flex-direction: column; gap: 16px; }
  .multilinea { height: 80px; align-items: flex-start; padding: 8px 12px; }
  .botones { display: flex; justify-content: flex-end; gap: 8px; margin-top: 8px; }
</style>
</head>
<body>
<!-- Datos: new PedirTextoDialog("Anular documento", "Ingresá el motivo de la anulación:") (Task 15),
     los textos reales de DocumentoFormViewModel. -->
<div class="v2-ventana">
  <div class="dialogo">
    <div class="v2-cuerpo">Ingresá el motivo de la anulación:</div>
    <div class="v2-input multilinea"><span class="v2-input-placeholder">Escriba el motivo</span></div>
    <div class="botones">
      <span class="v2-boton v2-boton--secundario">Cancelar</span>
      <span class="v2-boton v2-boton--primario">Aceptar</span>
    </div>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 4: Capturar y revisar**

```bash
for p in confirmacion mensaje pedir-texto; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/dialogos/$p.html artifacts/ui-v2/mockups/dialogos-$p.png
done
```

Expected: tres PNG sin error.

- [ ] **Step 5: GATE — aprobación del usuario**

PARAR. Presentar los tres mockups con las decisiones de "Diálogos" y la decisión P2 (Reclasificar queda para la Fase 5). Aclarar que la barra de título del sistema no forma parte de la comparación. Registrar en `progress.md`: `Mockups diálogos: aprobados por <quién> <fecha>; cambios: …`.

- [ ] **Step 6: Commit**

```bash
git add docs/ui-v2/mockups/dialogos/
git commit -m "docs(ui-v2): mockups aprobados de los diálogos de confirmación, mensaje y texto"
```

### Task 9: Mockups de los overlays de actualización

**Files:**
- Create: `docs/ui-v2/mockups/actualizaciones/banner.html`
- Create: `docs/ui-v2/mockups/actualizaciones/modal.html`
- Create: `docs/ui-v2/mockups/actualizaciones/bloqueo.html`
- Create: `docs/ui-v2/mockups/actualizaciones/modo-degradado.html`

**Interfaces:**
- Consumes: `base.css` (`.v2-tarjeta`, `.v2-titulo-seccion`, `.v2-texto-secundario`, `.v2-boton`).
- Produces: pantallas `actualizaciones-banner`, `actualizaciones-modal`, `actualizaciones-bloqueo`, `actualizaciones-modo-degradado` para `ActualizacionesComparacionTests` (Task 16). Cada overlay solo, sobre fondo app.

- [ ] **Step 1: `banner.html`**

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Actualización: banner (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  /* ActualizacionBannerView: franja de 56 arriba, fondo info, borde inferior 1, padding 0 24. */
  .banner {
    position: absolute; left: 0; right: 0; top: 0; height: 56px; padding: 0 24px;
    display: flex; align-items: center; justify-content: space-between;
    background: var(--v2-color-alerta-info-fondo); border-bottom: 1px solid var(--v2-color-alerta-info-borde);
  }
  .banner-titulo {
    font-size: var(--v2-fuente-subtitulo-formulario-tamano); line-height: var(--v2-fuente-subtitulo-formulario-alto-linea);
    font-weight: var(--v2-fuente-subtitulo-formulario-peso); color: var(--v2-color-alerta-info-texto);
  }
  .acciones { display: flex; gap: 8px; }
</style>
</head>
<body>
<!-- Datos: DatosActualizaciones.Banner() (Task 16). -->
<div class="v2-ventana">
  <div class="banner">
    <span class="banner-titulo">Nueva versión v0.5.0 disponible</span>
    <div class="acciones">
      <span class="v2-boton v2-boton--ghost">Más tarde</span>
      <span class="v2-boton v2-boton--primario">Actualizar ahora</span>
    </div>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 2: `modal.html`**

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Actualización: modal posponible (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  /* ActualizacionModalView: TarjetaContenido de 480, padding 24, 16 entre bloques. */
  .v2-ventana { display: flex; align-items: center; justify-content: center; }
  .modal { width: 480px; padding: 24px; display: flex; flex-direction: column; gap: 16px; }
  .acciones { display: flex; justify-content: flex-end; gap: 8px; }
</style>
</head>
<body>
<!-- Datos: DatosActualizaciones.Modal() (Task 16). -->
<div class="v2-ventana">
  <div class="v2-tarjeta modal">
    <div class="v2-titulo-seccion">Actualización disponible</div>
    <div class="v2-texto-secundario">Esta versión corrige el cálculo del Libro caja y mejora la exportación a PDF.</div>
    <div class="acciones">
      <span class="v2-boton v2-boton--secundario">Posponer</span>
      <span class="v2-boton v2-boton--primario">Actualizar ahora</span>
    </div>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 3: `bloqueo.html`**

Mismo `<head>` y `<style>` que `modal.html` (`<title>Actualización: bloqueo crítico (UI v2)</title>`), más `.titulo-critico { color: var(--v2-color-peligro-texto); }`:

```html
<!-- Datos: DatosActualizaciones.Bloqueo() (Task 16). Texto real sin tilde ("Actualizacion"). -->
<div class="v2-ventana">
  <div class="v2-tarjeta modal">
    <div class="v2-titulo-seccion titulo-critico">Actualizacion critica requerida</div>
    <div class="v2-texto-secundario">Esta versión corrige un problema de seguridad. La app se va a reiniciar para aplicarla.</div>
    <div class="acciones">
      <span class="v2-boton v2-boton--primario">Aplicar y reiniciar</span>
    </div>
  </div>
</div>
```

- [ ] **Step 4: `modo-degradado.html`**

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Actualización: modo degradado (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  /* Banner rojo permanente: franja de error arriba, padding 12x24, texto 13/600. */
  .degradado {
    position: absolute; left: 0; right: 0; top: 0; padding: 12px 24px;
    background: var(--v2-color-alerta-error-fondo); border-bottom: 1px solid var(--v2-color-alerta-error-borde);
    font-size: var(--v2-fuente-subtitulo-formulario-tamano); line-height: var(--v2-fuente-subtitulo-formulario-alto-linea);
    font-weight: var(--v2-fuente-subtitulo-formulario-peso); color: var(--v2-color-alerta-error-texto);
  }
</style>
</head>
<body>
<!-- Datos: DatosActualizaciones.ModoDegradado() (Task 16). Texto real. -->
<div class="v2-ventana">
  <div class="degradado">MODO DEGRADADO — Actualizacion critica no descargada. Reintentara al reiniciar.</div>
</div>
</body>
</html>
```

- [ ] **Step 5: Capturar y revisar**

```bash
for p in banner modal bloqueo modo-degradado; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/actualizaciones/$p.html artifacts/ui-v2/mockups/actualizaciones-$p.png
done
```

Expected: cuatro PNG sin error.

- [ ] **Step 6: GATE — aprobación del usuario**

PARAR. Presentar los cuatro mockups con las decisiones de "Overlays" (incluida la de los textos sin tilde). Aclarar que en la app el overlay se dibuja encima del Login o del shell, sin fondo propio (no cambia su lógica, §4.5). Registrar en `progress.md`: `Mockups actualizaciones: aprobados por <quién> <fecha>; cambios: …`.

- [ ] **Step 7: Commit**

```bash
git add docs/ui-v2/mockups/actualizaciones/
git commit -m "docs(ui-v2): mockups aprobados de los overlays de actualización"
```

---

## Bloque C — XAML por módulo (cada task arranca solo con su mockup aprobado)

Ciclo de cada task (spec §7): test de comparación y tests de UI en rojo → XAML → comparación verde (sin subir el umbral) → mutación quitando → commit. Si la comparación no pasa, se corrige el XAML (o, si el mockup tiene un error de geometría respecto de lo aprobado, el mockup con aviso al usuario); nunca se sube el umbral sin el procedimiento de spec §6.5. Revisar SIEMPRE la página de comparación (`artifacts/ui-v2/comparacion/<pantalla>/index.html`) aunque el test pase: el rojo/naranja debe ser solo borde de glifos.

### Task 10: `ShellMainViewModel`: secciones v2 y datos del usuario para el pie

**Decisión aplicada:** P1 (sidebar transitorio).

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` (constructor: lista `Grupos`; propiedades nuevas)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelGruposTests.cs`
- Create: `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelPieTests.cs`

**Interfaces:**
- Consumes: `ICurrentSession.UsuarioActual` (`UsuarioSesion(int Id, string NombreUsuario, RolUsuario Rol, string? NombreCompleto)`), `RolActual`; `CrearItem(...)` y los `Nav*Command` existentes.
- Produces:
  - `Grupos` con títulos `"INVENTARIO"`, `"FINANZAS"`, `"GESTIÓN"`, `"ANÁLISIS"`, `"ADMINISTRACIÓN"` (en ese orden) y los ítems de la tabla del Step 3. Mismos `Seccion`, `Comando` y evaluadores de gate que hoy.
  - `public string NombreUsuarioVisible` (NombreCompleto recortado, o NombreUsuario si no hay; `""` sin sesión).
  - `public string RolTexto` (`"Administrador"`, `"Operador"`, `""`).
  - `public string Iniciales` y `internal static string CalcularIniciales(string nombre)`: dos palabras o más → primera letra de la primera y de la última; una palabra → sus dos primeras letras; vacío → `""`; siempre en mayúsculas.

- [ ] **Step 1: Tests del pie (rojo)**

`tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelPieTests.cs`:

```csharp
using System.Collections.Generic;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>Pie del sidebar v2 (spec §4.1): avatar con iniciales, nombre y rol del usuario.</summary>
public class ShellMainViewModelPieTests
{
    private static ShellMainViewModel Crear(UsuarioSesion? usuario)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.UsuarioActual).Returns(usuario);
        sesion.Setup(s => s.RolActual).Returns(usuario?.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new ShellMainViewModel(sesion.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(i => i.Version == "0.4.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());
    }

    [Fact]
    public void ConNombreCompleto_MuestraElNombreCompletoYSusIniciales()
    {
        var vm = Crear(new UsuarioSesion(1, "mperez", RolUsuario.Admin, "María Pérez"));

        Assert.Equal("María Pérez", vm.NombreUsuarioVisible);
        Assert.Equal("MP", vm.Iniciales);
        Assert.Equal("Administrador", vm.RolTexto);
    }

    [Fact]
    public void SinNombreCompleto_UsaElNombreDeUsuario()
    {
        var vm = Crear(new UsuarioSesion(2, "jrodriguez", RolUsuario.Operador, null));

        Assert.Equal("jrodriguez", vm.NombreUsuarioVisible);
        Assert.Equal("JR", vm.Iniciales);
        Assert.Equal("Operador", vm.RolTexto);
    }

    [Fact]
    public void NombreCompletoEnBlanco_UsaElNombreDeUsuario() =>
        Assert.Equal("admin", Crear(new UsuarioSesion(3, "admin", RolUsuario.Admin, "   ")).NombreUsuarioVisible);

    [Fact]
    public void SinSesion_TodoVacio()
    {
        var vm = Crear(null);

        Assert.Equal("", vm.NombreUsuarioVisible);
        Assert.Equal("", vm.Iniciales);
        Assert.Equal("", vm.RolTexto);
    }

    [Theory]
    [InlineData("María Pérez", "MP")]
    [InlineData("Juan Carlos Rodríguez", "JR")]
    [InlineData("  ana   gómez  ", "AG")]
    [InlineData("admin", "AD")]
    [InlineData("x", "X")]
    [InlineData("", "")]
    public void CalcularIniciales(string nombre, string esperado) =>
        Assert.Equal(esperado, ShellMainViewModel.CalcularIniciales(nombre));
}
```

`StockApp.Presentation.Tests` ve los `internal` de Presentation solo si hay `InternalsVisibleTo`: verificar con `grep -rn InternalsVisibleTo src/StockApp.Presentation/` (ya existe: `ShellMainViewModel` expone `internal const bool ImportacionHabilitada` y `internal Task _tareaRefrescoPermisos`, que los tests usan). Si no estuviera, hacer `CalcularIniciales` `public static`.

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ShellMainViewModelPieTests"`
Expected: FAIL de compilación, `'ShellMainViewModel' does not contain a definition for 'NombreUsuarioVisible'`.

- [ ] **Step 2: Tests de secciones (rojo)**

En `ShellMainViewModelGruposTests.cs`:

1. Reemplazar `Grupos_ParaUnAdmin_TieneLosOchoGrupos` por:

```csharp
    [Fact]
    public void Grupos_SonLasCincoSeccionesDeLaSpec()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        Assert.Equal(
            new[] { "INVENTARIO", "FINANZAS", "GESTIÓN", "ANÁLISIS", "ADMINISTRACIÓN" },
            vm.Grupos.Select(g => g.Titulo));
    }

    // Spec §4.1 (orden y textos) con P1 (sidebar transitorio): ANÁLISIS y ADMINISTRACIÓN transitorias
    // hasta el hub de Reportes (Fase 6) y Catálogos (Fase 7).
    [Theory]
    [InlineData("INVENTARIO", "Productos|Registrar entrada|Registrar salida|Ingreso por factura|Historial de movimientos")]
    [InlineData("FINANZAS", "Gastos y facturas|Ingresos de caja|Libro caja|Control POA|Calendario de pagos")]
    [InlineData("GESTIÓN", "Tareas|Documentos")]
    [InlineData("ANÁLISIS", "Valorización de inventario|Stock por categoría|Historial por producto|Productos más movidos|Log de auditoría|Estadística de tareas")]
    [InlineData("ADMINISTRACIÓN", "Categorías|Proveedores|Unidades de medida|Catálogos de tareas|Maestros de finanzas|Usuarios|Mantenimiento|Importar planillas")]
    public void Seccion_TieneSusItemsEnElOrdenDeLaSpec(string seccion, string titulos)
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        Assert.Equal(titulos.Split('|'), vm.Grupos.Single(g => g.Titulo == seccion).Items.Select(i => i.Titulo));
    }

    [Fact]
    public void Importacion_ExisteEnAdministracionPeroQuedaOcultaAunParaAdmin()
    {
        // ImportacionHabilitada=false (2026-09-04): el ítem existe pero no se muestra (spec §4.1).
        var (vm, _, _) = Crear(RolUsuario.Admin);
        var administracion = vm.Grupos.Single(g => g.Titulo == "ADMINISTRACIÓN");

        Assert.Contains(administracion.Items, i => i.Seccion == "Importacion");
        Assert.DoesNotContain(administracion.ItemsVisibles, i => i.Seccion == "Importacion");
    }

    [Fact]
    public void OperadorMixto_VeInventarioFinanzasYGestion_SinAnalisisNiAdministracion()
    {
        // Gate probado con permisos mixtos, no con Admin (Admin cortocircuita el OR).
        var (vm, _, _) = Crear(RolUsuario.Operador, new[]
        {
            Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas,
        });

        Assert.Equal(new[] { "INVENTARIO", "FINANZAS", "GESTIÓN" }, vm.Grupos.Where(g => g.EsVisible).Select(g => g.Titulo));
        Assert.Equal(
            new[] { "Productos", "RegistrarEntrada", "RegistrarSalida", "HistorialMovimientos" },
            vm.Grupos.Single(g => g.Titulo == "INVENTARIO").ItemsVisibles.Select(i => i.Seccion));
        Assert.Equal(new[] { "Tareas" }, vm.Grupos.Single(g => g.Titulo == "GESTIÓN").ItemsVisibles.Select(i => i.Seccion));
    }
```

2. Reemplazar `Grupo_EsVisible_SiAlgunItemEsVisible` por (Maestros de finanzas pasa a ADMINISTRACIÓN):

```csharp
    [Fact]
    public void Grupo_EsVisible_SiAlgunItemEsVisible()
    {
        // Un Operador con SOLO GestionarMaestrosFinanzas: ADMINISTRACIÓN se ve con ese único ítem
        // y FINANZAS (VerFinanzas) no se ve.
        var (vm, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.GestionarMaestrosFinanzas });

        var administracion = vm.Grupos.Single(g => g.Titulo == "ADMINISTRACIÓN");
        Assert.True(administracion.EsVisible);
        Assert.Equal("MaestrosFinanzas", Assert.Single(administracion.ItemsVisibles).Seccion);
        Assert.False(vm.Grupos.Single(g => g.Titulo == "FINANZAS").EsVisible);
    }
```

3. En el resto del archivo reemplazar los literales de título: `"Finanzas"` → `"FINANZAS"` (incluidos los de `PreferenciasSidebar` y `GruposAbiertos.Contains`), `"Reportes"` → `"ANÁLISIS"`, `"Tablas maestras"` → `"ADMINISTRACIÓN"`. Los tests de acordeón (`AlternarGrupo_*`, `Navegar_AUnaSeccion_AutoabreSuGrupo`, `AbrirUnGrupo_NoCierraLosOtros`, preferencias) se conservan: el VM mantiene ese comportamiento hasta la Fase 9 aunque el XAML v2 no lo use.

Agregar `using StockApp.Application.Authorization;` si no está.

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ShellMainViewModelGruposTests"`
Expected: FAIL (títulos viejos; tras compilar el Step 1 en rojo de compilación, primero se ve el error de compilación del pie).

- [ ] **Step 3: Implementar**

En `ShellMainViewModel`, reemplazar la inicialización de `Grupos` en el constructor por:

```csharp
        // Secciones v2 (spec §4.1, Fase 1). P1 (sidebar transitorio): ANÁLISIS y ADMINISTRACIÓN listan los
        // ítems actuales hasta que el hub de Reportes (Fase 6) y Catálogos (Fase 7) los consoliden.
        // Títulos en mayúsculas literales: Avalonia no tiene text-transform. Cada ítem conserva su
        // gate (Seccion, Comando y evaluador sin cambios).
        Grupos = new List<GrupoNavegacion>
        {
            new GrupoNavegacion("INVENTARIO", new List<ItemNavegacion>
            {
                CrearItem("Productos", "mdi-package-variant", NavProductosCommand, "Productos", () => PuedeGestionarProductos),
                CrearItem("Registrar entrada", "mdi-tray-arrow-down", NavRegistrarEntradaCommand, "RegistrarEntrada", () => PuedeRegistrarEntradaSalida),
                CrearItem("Registrar salida", "mdi-tray-arrow-up", NavRegistrarSalidaCommand, "RegistrarSalida", () => PuedeRegistrarEntradaSalida),
                CrearItem("Ingreso por factura", "mdi-receipt-text-plus", NavIngresoPorFacturaCommand, "IngresoPorFactura", () => PuedeIngresarPorFactura),
                CrearItem("Historial de movimientos", "mdi-history", NavHistorialMovimientosCommand, "HistorialMovimientos", () => PuedeRegistrarMovimientos),
            }),
            new GrupoNavegacion("FINANZAS", new List<ItemNavegacion>
            {
                CrearItem("Gastos y facturas", "mdi-receipt-text", NavGastosCommand, "Gastos", () => PuedeVerFinanzas),
                CrearItem("Ingresos de caja", "mdi-cash-plus", NavIngresosCommand, "Ingresos", () => PuedeVerFinanzas),
                CrearItem("Libro caja", "mdi-book-open-variant", NavLibroCajaCommand, "LibroCaja", () => PuedeVerFinanzas),
                CrearItem("Control POA", "mdi-chart-donut", NavControlPoaCommand, "ControlPoa", () => PuedeVerFinanzas),
                CrearItem("Calendario de pagos", "mdi-calendar-clock", NavCalendarioPagosCommand, "CalendarioPagos", () => PuedeVerFinanzas),
            }),
            new GrupoNavegacion("GESTIÓN", new List<ItemNavegacion>
            {
                CrearItem("Tareas", "mdi-checkbox-marked-outline", NavTareasCommand, "Tareas", () => PuedeGestionarTareas),
                CrearItem("Documentos", "mdi-file-cabinet", NavDocumentosCommand, "Documentos", () => PuedeGestionarDocumentos),
            }),
            new GrupoNavegacion("ANÁLISIS", new List<ItemNavegacion>
            {
                CrearItem("Valorización de inventario", "mdi-currency-usd", NavValorizacionCommand, "Valorizacion", () => PuedeVerReportes),
                CrearItem("Stock por categoría", "mdi-chart-pie", NavStockCategoriaCommand, "StockCategoria", () => PuedeVerReportes),
                CrearItem("Historial por producto", "mdi-file-document", NavHistorialPorProductoCommand, "HistorialPorProducto", () => PuedeVerHistorialPorProducto),
                CrearItem("Productos más movidos", "mdi-trending-up", NavMasMovidosCommand, "MasMovidos", () => PuedeVerReportes),
                CrearItem("Log de auditoría", "mdi-shield-search", NavAuditoriaLogCommand, "AuditoriaLog", () => PuedeVerReportes),
                CrearItem("Estadística de tareas", "mdi-chart-bar", NavReporteTareasCommand, "ReporteTareas", () => PuedeVerReportes),
            }),
            new GrupoNavegacion("ADMINISTRACIÓN", new List<ItemNavegacion>
            {
                CrearItem("Categorías", "mdi-shape", NavCategoriasCommand, "Categorias", () => PuedeGestionarTablasMaestras),
                CrearItem("Proveedores", "mdi-truck", NavProveedoresCommand, "Proveedores", () => PuedeGestionarTablasMaestras),
                CrearItem("Unidades de medida", "mdi-ruler", NavUnidadesMedidaCommand, "UnidadesMedida", () => PuedeGestionarTablasMaestras),
                CrearItem("Catálogos de tareas", "mdi-tag-multiple", NavCatalogosTareaCommand, "CatalogosTarea", () => PuedeGestionarTablasMaestras),
                CrearItem("Maestros de finanzas", "mdi-cash-multiple", NavMaestrosFinanzasCommand, "MaestrosFinanzas", () => PuedeGestionarMaestrosFinanzas),
                CrearItem("Usuarios", "mdi-account-cog", NavUsuariosCommand, "Usuarios", () => EsAdmin),
                CrearItem("Mantenimiento", "mdi-database-cog", NavMantenimientoCommand, "Mantenimiento", () => EsAdmin),
                CrearItem("Importar planillas", "mdi-file-upload", NavImportacionCommand, "Importacion", () => EsAdmin && ImportacionHabilitada),
            }),
        };
```

Actualizar el `<summary>` de `Grupos`: "Las 5 secciones del sidebar v2 (spec §4.1). El XAML v2 las muestra siempre abiertas: `EstaExpandido` y `AlternarGrupo` quedan sin uso en la vista hasta la Fase 9 (limpieza)."

Debajo de `VersionTexto`, agregar:

```csharp
    /// <summary>Nombre del pie del sidebar (spec §4.1): el nombre completo, o el de usuario si no hay.</summary>
    public string NombreUsuarioVisible =>
        _session.UsuarioActual is not { } usuario
            ? string.Empty
            : string.IsNullOrWhiteSpace(usuario.NombreCompleto) ? usuario.NombreUsuario : usuario.NombreCompleto.Trim();

    /// <summary>Rol del pie del sidebar, en texto.</summary>
    public string RolTexto => _session.RolActual switch
    {
        RolUsuario.Admin => "Administrador",
        RolUsuario.Operador => "Operador",
        _ => string.Empty,
    };

    /// <summary>Iniciales del avatar del pie del sidebar.</summary>
    public string Iniciales => CalcularIniciales(NombreUsuarioVisible);

    internal static string CalcularIniciales(string nombre)
    {
        var palabras = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return palabras.Length switch
        {
            0 => string.Empty,
            1 => palabras[0][..Math.Min(2, palabras[0].Length)].ToUpperInvariant(),
            _ => string.Concat(char.ToUpperInvariant(palabras[0][0]), char.ToUpperInvariant(palabras[^1][0])),
        };
    }
```

- [ ] **Step 4: Verde**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
Expected: PASS de todo el proyecto (incluidos `ShellMainViewModelTests`, `ShellMainViewModelReportesTests` y `ShellMainFinanzasTests`, que prueban gates por propiedad y por comando, no por título). Luego `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ShellMainViewGatesTests"`: PASS sin tocar ese archivo (spec §8.1; todavía corre contra el XAML viejo, que bindea `Grupos` igual).

- [ ] **Step 5: Mutación quitando**

```bash
cp src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs "$SCRATCH/ShellMainViewModel.cs.bak"
```

(a) Quitar el `CrearItem("Documentos", …)` de GESTIÓN → rojo en `Seccion_TieneSusItemsEnElOrdenDeLaSpec("GESTIÓN", …)`. Restaurar con `cp`.
(b) Quitar `.Trim()` de `usuario.NombreCompleto.Trim()` y la rama `string.IsNullOrWhiteSpace(...) ? usuario.NombreUsuario :` (dejando `usuario.NombreCompleto ?? usuario.NombreUsuario`) → rojo en `NombreCompletoEnBlanco_UsaElNombreDeUsuario`. Restaurar con `cp`.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelGruposTests.cs tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelPieTests.cs
git commit -m "feat(ui-v2): secciones del sidebar v2 y datos del usuario para el pie"
```

### Task 11: `ShellMainView` v2 (sidebar) con su comparación

**Depende de:** Tasks 5, 6 (mockups aprobados) y 10. **Decisiones aplicadas:** P4 (barra `Auto`) y P6 (línea "Municipio de Carmelo").

**Files:**
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml` (reescritura completa; el code-behind no cambia)
- Modify: `tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj` (+ `Moq`)
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Shell/DatosShell.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Shell/ShellComparacionTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/ShellMainViewV2Tests.cs`

**Interfaces:**
- Consumes: `ItemSidebar` y clases tipográficas (Task 5), `NombreUsuarioVisible`/`RolTexto`/`Iniciales` y secciones (Task 10), mockups `shell/sidebar-admin.html` y `shell/sidebar-operador.html` (Task 6), fakes de UiTests (`SesionFake(UsuarioSesion, params string[])`, `InfoAppFake(string)`, `AuthServiceFake(ICurrentSession, IReadOnlySet<string>)`, `PreferenciasSidebarFake`, `NavigationServiceFake.SimularNavegacion()`, `ConfirmacionServiceFake`), `ArbolVisual.EsVisibleEnArbol(Visual)`.
- Produces: `ShellMainView` con partes nombradas `Sidebar` (Border, `Classes="ui2"`), `Navegacion` (ScrollViewer), `PieUsuario` (Border), `Iniciales`, `NombreUsuario`, `RolUsuario` (TextBlock), `CerrarSesion` (Button) y `Contenido` (ContentControl, sin `ui2` en sus ancestros). `DatosShell.Admin()` y `DatosShell.OperadorMixto()`.

- [ ] **Step 1: Moq en VisualTests y datos fijos**

En `StockApp.Presentation.VisualTests.csproj`, dentro del primer `ItemGroup` de paquetes: `<PackageReference Include="Moq" />` (la versión 4.20.72 ya está en `Directory.Packages.props`).

`tests/StockApp.Presentation.VisualTests/Pantallas/Shell/DatosShell.cs`:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.VisualTests.Pantallas.Shell;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/shell/sidebar-*.html. CurrentContent queda null: el área de
/// contenido es fondo app en la app y en el mockup.
/// </summary>
public static class DatosShell
{
    public static ShellMainViewModel Admin() =>
        Crear(new UsuarioSesion(1, "mperez", RolUsuario.Admin, "María Pérez"), [], "Productos");

    public static ShellMainViewModel OperadorMixto() =>
        Crear(new UsuarioSesion(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez"),
            [Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas],
            "Gastos");

    private static ShellMainViewModel Crear(UsuarioSesion usuario, string[] permisos, string seccionActiva)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.UsuarioActual).Returns(usuario);
        sesion.Setup(s => s.RolActual).Returns(usuario.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));
        var vm = new ShellMainViewModel(sesion.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(i => i.Version == "0.4.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());
        vm.SeccionActiva = seccionActiva;
        return vm;
    }
}
```

- [ ] **Step 2: Tests (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Shell/ShellComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests.Pantallas.Shell;

[Trait("Categoria", "ComparacionVisual")]
public class ShellComparacionTests
{
    [AvaloniaFact]
    public void SidebarAdmin_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosShell.Admin(), "shell/sidebar-admin.html");

    [AvaloniaFact]
    public void SidebarOperadorMixto_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosShell.OperadorMixto(), "shell/sidebar-operador.html");
}
```

`tests/StockApp.Presentation.UiTests/V2/ShellMainViewV2Tests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Sidebar v2 (spec §4.1) montado con la vista real. Los gates por ítem los custodia ShellMainViewGatesTests.</summary>
public class ShellMainViewV2Tests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                Width="1280" Height="760">
            <vistas:ShellMainView />
        </Window>
        """;

    private static readonly UsuarioSesion Admin = new(1, "mperez", RolUsuario.Admin, "María Pérez");
    private static readonly UsuarioSesion Operador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");

    private static (Window Ventana, ShellMainViewModel Vm, NavigationServiceFake Navegacion) Montar(
        UsuarioSesion usuario, string[] permisos, IReadOnlySet<string>? permisosTrasRefresco = null)
    {
        var sesion = new SesionFake(usuario, permisos);
        var navegacion = new NavigationServiceFake();
        var auth = permisosTrasRefresco is null ? new AuthServiceFake() : new AuthServiceFake(sesion, permisosTrasRefresco);
        var vm = new ShellMainViewModel(sesion, navegacion, new InfoAppFake("0.4.0"), new ConfirmacionServiceFake(), auth,
            new PreferenciasSidebarFake());
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        ventana.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (ventana, vm, navegacion);
    }

    private static T Nombrado<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static string[] HeadersVisibles(Window ventana) =>
        ventana.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("header-seccion-sidebar") && ArbolVisual.EsVisibleEnArbol(t))
            .Select(t => t.Text!)
            .ToArray();

    [AvaloniaFact]
    public void Ui2_VaEnElSidebarYNoEnLaRaiz()
    {
        var (ventana, _, _) = Montar(Admin, []);

        Assert.Contains("ui2", Nombrado<Border>(ventana, "Sidebar").Classes);
        var vista = ventana.GetVisualDescendants().OfType<StockApp.Presentation.Views.ShellMainView>().Single();
        Assert.DoesNotContain("ui2", vista.Classes);
    }

    [AvaloniaFact]
    public void ContenidoHospedado_NoTieneAncestroUi2()
    {
        // Review Focus 1: todo lo que hospeda el ContentControl hereda de sus ancestros. Si alguno
        // tiene ui2, las vistas todavía no migradas toman los estilos v2.
        var (ventana, _, _) = Montar(Admin, []);
        var contenido = Nombrado<ContentControl>(ventana, "Contenido");

        Assert.DoesNotContain(contenido.GetSelfAndVisualAncestors().OfType<StyledElement>(), e => e.Classes.Contains("ui2"));
    }

    [AvaloniaFact]
    public void Admin_VeLasCincoSecciones() =>
        Assert.Equal(new[] { "INVENTARIO", "FINANZAS", "GESTIÓN", "ANÁLISIS", "ADMINISTRACIÓN" }, HeadersVisibles(Montar(Admin, []).Ventana));

    [AvaloniaFact]
    public void OperadorMixto_SeccionesSinItemsVisibles_OcultanSuHeader()
    {
        var (ventana, _, _) = Montar(Operador,
            [Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas]);

        Assert.Equal(new[] { "INVENTARIO", "FINANZAS", "GESTIÓN" }, HeadersVisibles(ventana));
    }

    [AvaloniaFact]
    public void RevocacionEnCaliente_OcultaElHeaderDeLaSeccionQueSeVacia()
    {
        var (ventana, _, navegacion) = Montar(Operador, [Permisos.VerReportes], new HashSet<string>());
        Assert.Equal(new[] { "ANÁLISIS" }, HeadersVisibles(ventana));

        navegacion.SimularNavegacion();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(HeadersVisibles(ventana));
    }

    [AvaloniaFact]
    public void Pie_MuestraInicialesNombreRolYVersion()
    {
        var (ventana, _, _) = Montar(Admin, []);

        Assert.Equal("MP", Nombrado<TextBlock>(ventana, "Iniciales").Text);
        Assert.Equal("María Pérez", Nombrado<TextBlock>(ventana, "NombreUsuario").Text);
        Assert.Equal("Administrador", Nombrado<TextBlock>(ventana, "RolUsuario").Text);
        Assert.Contains(ventana.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "v0.4.0" && t.Classes.Contains("version"));
    }

    [AvaloniaFact]
    public void Marca_LlevaLaLineaMunicipioDeCarmelo()
    {
        // P6: la línea va bajo "Gestión Municipal" y con la clase tipográfica del subtítulo.
        var (ventana, _, _) = Montar(Admin, []);

        var textos = ventana.GetVisualDescendants().OfType<TextBlock>().ToList();
        var marca = textos.Single(t => t.Classes.Contains("marca"));
        var subtitulo = Assert.Single(textos, t => t.Classes.Contains("marca-subtitulo"));
        Assert.Equal("Municipio de Carmelo", subtitulo.Text);
        Assert.True(subtitulo.TranslatePoint(default, ventana)!.Value.Y > marca.TranslatePoint(default, ventana)!.Value.Y);
    }

    [AvaloniaFact]
    public void CerrarSesion_UsaElComandoConConfirmacionDelVm()
    {
        var (ventana, vm, _) = Montar(Admin, []);
        var boton = Nombrado<Button>(ventana, "CerrarSesion");

        Assert.Same(vm.CerrarSesionCommand, boton.Command);
        Assert.Equal("Cerrar sesión", ToolTip.GetTip(boton));
    }

    [AvaloniaFact]
    public void Navegar_MarcaActivoSoloElItemDeLaSeccion()
    {
        var (ventana, vm, _) = Montar(Admin, []);

        vm.NavProductosCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var activos = ventana.GetVisualDescendants().OfType<ItemSidebar>().Where(i => i.EstaActivo).ToList();
        Assert.Same(vm.NavProductosCommand, Assert.Single(activos).Command);
    }

    [AvaloniaFact]
    public void SinAcordeones_LosItemsSeVenAunqueElGrupoEsteColapsado()
    {
        // Spec §4.1: secciones siempre visibles. EstaExpandido sigue en el VM (Fase 9) pero la
        // vista no lo usa.
        var (ventana, vm, _) = Montar(Admin, []);
        foreach (var grupo in vm.Grupos)
        {
            grupo.EstaExpandido = false;
        }

        Dispatcher.UIThread.RunJobs();

        var productos = ventana.GetVisualDescendants().OfType<ItemSidebar>().Single(i => ReferenceEquals(i.Command, vm.NavProductosCommand));
        Assert.True(ArbolVisual.EsVisibleEnArbol(productos));
    }
}
```

Si `SesionFake`, `AuthServiceFake`, etc. están en el namespace `StockApp.Presentation.UiTests` (sí), se ven desde `StockApp.Presentation.UiTests.V2` sin `using`.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ShellMainViewV2Tests"` y `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~ShellComparacionTests"`
Expected: FAIL (no existe `Sidebar`, `Contenido`, `ItemSidebar` en el árbol; las comparaciones fallan por el sidebar oscuro viejo).

- [ ] **Step 3: El XAML**

Reemplazar `src/StockApp.Presentation/Views/ShellMainView.axaml` entero por:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1280" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.ShellMainView"
             x:DataType="vm:ShellMainViewModel">

    <!-- UI v2 (spec 2026-09-28 §4.1). Espejo: docs/ui-v2/mockups/shell/sidebar-*.html.
         Classes="ui2" va en el Border del sidebar y NO en este Grid: el ContentControl de la
         derecha hospeda vistas que todavía no se migraron (ShellMainViewV2Tests). -->
    <Grid ColumnDefinitions="240,*" Background="{DynamicResource V2FondoBrush}">

        <Border x:Name="Sidebar"
                Grid.Column="0"
                Classes="ui2"
                Background="{DynamicResource V2SuperficieBrush}"
                BorderBrush="{DynamicResource V2BordeBrush}"
                BorderThickness="0,0,1,0"
                Padding="12,16">
            <DockPanel>

                <!-- Marca + línea "Municipio de Carmelo" (P6). Espejo CSS: .v2-marca (margin 4 8 0) y
                     .v2-marca-subtitulo (margin 2 8 12). -->
                <StackPanel DockPanel.Dock="Top" Margin="8,4,8,12">
                    <TextBlock Classes="marca" Text="Gestión Municipal" />
                    <TextBlock Classes="marca-subtitulo" Text="Municipio de Carmelo" Margin="0,2,0,0" />
                </StackPanel>

                <!-- DockPanel: el primer Bottom queda más abajo. Versión al pie, el usuario arriba de ella. -->
                <TextBlock DockPanel.Dock="Bottom" Classes="version" Text="{Binding VersionTexto}" Margin="4,4,4,0" />

                <Border x:Name="PieUsuario"
                        DockPanel.Dock="Bottom"
                        Margin="0,8,0,0"
                        Padding="4,8,4,4"
                        BorderBrush="{DynamicResource V2BordeBrush}"
                        BorderThickness="0,1,0,0">
                    <Grid ColumnDefinitions="Auto,*,Auto">
                        <Border Grid.Column="0"
                                Width="28" Height="28"
                                CornerRadius="14"
                                Margin="0,0,8,0"
                                Background="{DynamicResource V2BordeInputBrush}">
                            <TextBlock x:Name="Iniciales" Classes="avatar" Text="{Binding Iniciales}"
                                       HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <StackPanel Grid.Column="1" VerticalAlignment="Center">
                            <TextBlock x:Name="NombreUsuario" Classes="usuario-nombre" Text="{Binding NombreUsuarioVisible}"
                                       TextTrimming="CharacterEllipsis" />
                            <TextBlock x:Name="RolUsuario" Classes="usuario-rol" Text="{Binding RolTexto}" />
                        </StackPanel>
                        <!-- Mismo comando que antes: CerrarSesionCommand pide la confirmación actual. -->
                        <Button x:Name="CerrarSesion"
                                Grid.Column="2"
                                Classes="ghost icono"
                                VerticalAlignment="Center"
                                Command="{Binding CerrarSesionCommand}"
                                ToolTip.Tip="Cerrar sesión"
                                AutomationProperties.Name="Cerrar sesión">
                            <i:Icon Value="mdi-logout" FontSize="16" />
                        </Button>
                    </Grid>
                </Border>

                <!-- Secciones siempre visibles, sin acordeones (§4.1): EstaExpandido no se usa. Un
                     Administrador no entra en 760 de alto: la navegación scrollea (P4, barra Auto). -->
                <ScrollViewer x:Name="Navegacion"
                              VerticalScrollBarVisibility="Auto"
                              HorizontalScrollBarVisibility="Disabled">
                    <StackPanel>
                        <v2:ItemSidebar Icono="{Binding ItemInicio.Icono}"
                                        Titulo="{Binding ItemInicio.Titulo}"
                                        Command="{Binding ItemInicio.Comando}"
                                        EstaActivo="{Binding ItemInicio.EstaActivo}" />

                        <ItemsControl ItemsSource="{Binding Grupos}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate DataType="vm:GrupoNavegacion">
                                    <!-- EsVisible = algún ítem visible: el header se oculta con su sección. -->
                                    <StackPanel IsVisible="{Binding EsVisible}">
                                        <TextBlock Classes="header-seccion-sidebar" Text="{Binding Titulo}" Margin="8,12,8,4" />
                                        <ItemsControl ItemsSource="{Binding ItemsVisibles}">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate DataType="vm:ItemNavegacion">
                                                    <v2:ItemSidebar Icono="{Binding Icono}"
                                                                    Titulo="{Binding Titulo}"
                                                                    Command="{Binding Comando}"
                                                                    EstaActivo="{Binding EstaActivo}" />
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </ScrollViewer>
            </DockPanel>
        </Border>

        <!-- Región de contenido: INavigationService + ViewLocator. Sin ui2 (ver arriba). -->
        <ContentControl x:Name="Contenido" Grid.Column="1" Content="{Binding CurrentContent}" />

    </Grid>

</UserControl>
```

En `base.css`, agregar a `.v2-usuario-nombre` `overflow: hidden; text-overflow: ellipsis;` (espejo del `TextTrimming`; con los datos fijos no recorta).

- [ ] **Step 4: Verde de estructura**

Run: `dotnet build StockApp.sln` (un binding con nombre mal escrito es error de compilación por `x:DataType`) y luego `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ShellMainViewV2Tests|FullyQualifiedName~ShellMainViewGatesTests"`
Expected: PASS de los 9 nuevos y de TODOS los de `ShellMainViewGatesTests` sin modificarlos (spec §8.1).

- [ ] **Step 5: Comparación**

Run: `scripts/ui-v2/comparar.sh Sidebar`
Expected: las dos pantallas APROBADAS (11 teselas o menos, 1.68% o menos) y sin texto recortado. Abrir `artifacts/ui-v2/comparacion/index.html` y revisar cada página: el rojo/naranja solo en bordes de glifos.

- [ ] **Step 6: P4 — la barra de scroll del Administrador**

En `artifacts/ui-v2/comparacion/shell-sidebar-admin/app.png`, mirar el borde derecho de la navegación (x ≈ 220-228). Si la barra auto-oculta de Fluent dibuja algo y el mockup no, la comparación lo marca como teselas densas en esa franja:
- P4 = "Auto" (decidido): medir la franja en el PNG (ancho, color, posición, alto del pulgar) y replicarla en `sidebar-admin.html` con un `<div>` absoluto de color sólido dentro de `.v2-sidebar-nav` (colores sólidos y px enteros, §6.1). Volver a presentar ESE cambio al usuario antes de commitear (es un cambio del mockup aprobado).
Si no hay diferencia en esa franja, no hacer nada y anotarlo en el reporte.

- [ ] **Step 7: Mutación quitando**

```bash
cp src/StockApp.Presentation/Views/ShellMainView.axaml "$SCRATCH/ShellMainView.axaml.bak"
```

(a) Quitar `IsVisible="{Binding EsVisible}"` del `StackPanel` de la sección → rojo en `OperadorMixto_SeccionesSinItemsVisibles_OcultanSuHeader`, `RevocacionEnCaliente_…` y en `SidebarOperadorMixto_IgualAlMockup` (aparecen headers vacíos). Restaurar con `cp`.
(b) Quitar `EstaActivo="{Binding EstaActivo}"` del `ItemSidebar` de la plantilla de ítems → rojo en `Navegar_MarcaActivoSoloElItemDeLaSeccion` y en las dos comparaciones (sin pastilla). Restaurar con `cp`.
(c) Mover `Classes="ui2"` del `Border` del sidebar al `Grid` raíz (es QUITARLO del Border y ponerlo donde la convención lo prohíbe: la mutación reproduce el bug de fuga, no agrega un efecto destructivo) → rojo en `ContenidoHospedado_NoTieneAncestroUi2` y `Ui2_VaEnElSidebarYNoEnLaRaiz`. Restaurar con `cp`.

(d) Quitar el `TextBlock Classes="marca-subtitulo"` → rojo en `Marca_LlevaLaLineaMunicipioDeCarmelo` y en las dos comparaciones (falta la línea del mockup). Restaurar con `cp`.

Si una mutación de XAML no da rojo: `dotnet build-server shutdown` + `rm -rf src/StockApp.Presentation/obj src/StockApp.Presentation/bin` y los de los proyectos de test, y repetir.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/Views/ShellMainView.axaml docs/ui-v2/mockups/_base/base.css tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj tests/StockApp.Presentation.VisualTests/Pantallas/Shell/ tests/StockApp.Presentation.UiTests/V2/ShellMainViewV2Tests.cs
git commit -m "feat(ui-v2): sidebar v2 del shell, comparado contra su mockup"
```

Si el Step 6 cambió el mockup, agregar `docs/ui-v2/mockups/shell/sidebar-admin.html` al mismo commit.

### Task 12: `LoginView` v2 con sus tres estados comparados

**Depende de:** Tasks 1, 5, 7 (mockups aprobados). **Decisiones aplicadas:** P5 (se conserva la marca de agua; se excluye de la comparación de píxeles y se custodia aparte: ver "Estrategia de comparación de la marca de agua (P5)") y P6 (línea "Municipio de Carmelo" bajo el título).

**Files:**
- Modify: `src/StockApp.Presentation/Views/LoginView.axaml` (reescritura; `LoginView.axaml.cs` NO cambia: conserva `OnCampoKeyDown`)
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/DatosAcceso.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/AccesoComparacionTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/LoginViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/LoginMarcaAguaTests.cs`
- Modify: `tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs` y `tests/StockApp.UiV2.Herramientas.Tests/CapturadorMockupTests.cs` (`ocultarSelectores`)
- Modify: `tests/StockApp.Presentation.VisualTests/Comparacion/ComparacionVisual.cs` (`ocultarEnMockup`)
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`

**Interfaces:**
- Consumes: `LoginViewModel(IAuthService, ShellViewModel, IInfoApp, bool soloAccesoLimitado = false)` con `NombreUsuario`, `Contrasena`, `MensajeError`, `SoloAccesoLimitado`, `VersionTexto`, `EntrarCommand`, `ResetearAdminCommand`; `ShellViewModel(IAuthService, ILicenciaService, IResetAdminService, INavigationService, CoordinadorActualizacion, IUiDispatcher, IInfoApp, Func<MantenimientoViewModel>? = null)`; `TarjetaContenido`, `Alerta`, `CampoFormulario`, `V2TemaEnlace`; mockups `acceso/login*.html`.
- Produces: `DatosAcceso.Login(bool soloAccesoLimitado = false, string? usuario = null, string? error = null)` (y en la Task 13, `Reset()` y `BloqueoLicencia()`); partes nombradas del Login `MarcaAgua` (Image), `AvisoAccesoLimitado`, `AlertaError` (Alerta), `CampoUsuario`, `CampoContrasena` (TextBox), `BotonEntrar`, `EnlaceReset` (Button). `CapturadorMockup.CapturarAsync(rutaHtml, rutaPng, IReadOnlyCollection<string>? ocultarSelectores = null)` y `ComparacionVisual.CompararConMockup(..., IReadOnlyCollection<string>? ocultarEnMockup = null)`. Lista `GuardianDePatronTests.VistasMigradasAV2`.

- [ ] **Step 1: Herramienta: ocultar elementos del mockup en la comparación (P5)**

La marca de agua se excluye de ambos renders (ver "Estrategia de comparación de la marca de agua (P5)"). El mockup la oculta con `visibility: hidden` (el layout no se mueve) y la herramienta falla si el selector no coincide con nada.

En `tests/StockApp.UiV2.Herramientas.Tests/CapturadorMockupTests.cs`, agregar (rojo primero; reutilizan el helper `Html`):

```csharp
    [Fact]
    public async Task OcultarSelectores_ElElementoNoSeDibujaYElRestoQuedaDondeEstaba()
    {
        var html = Html(
            "<div class=\"oculto\"></div><div class=\"visible\"></div>",
            ".oculto{position:absolute;left:100px;top:100px;width:200px;height:100px;background:var(--v2-color-acento)}"
            + ".visible{position:absolute;left:400px;top:100px;width:200px;height:100px;background:var(--v2-color-acento)}");
        var png = Path.Combine(_carpeta, "oculto.png");

        await CapturadorMockup.CapturarAsync(html, png, [".oculto"]);

        using var imagen = SKBitmap.Decode(png);
        Assert.Equal(SKColor.Parse("#F7F7F5"), imagen.GetPixel(150, 150));
        Assert.Equal(SKColor.Parse("#1D4ED8"), imagen.GetPixel(450, 150));
    }

    [Fact]
    public async Task OcultarSelectores_SinCoincidencias_Lanza()
    {
        var html = Html("<p>Nada que ocultar</p>");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png"), [".marca-agua"]));

        Assert.Contains(".marca-agua", ex.Message);
    }
```

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~CapturadorMockupTests"`
Expected: FAIL (compilación: `CapturarAsync` no recibe el tercer parámetro).

En `tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs`: la firma pasa a `CapturarAsync(string rutaHtml, string rutaPng, IReadOnlyCollection<string>? ocultarSelectores = null)` (el `Cli.cs` la llama con dos argumentos y no cambia) y, entre el chequeo de desbordes y la captura, se agrega:

```csharp
            if (ocultarSelectores is { Count: > 0 })
            {
                foreach (var selector in ocultarSelectores)
                {
                    // Un selector con typo compararía contra el mockup CON el elemento: falla fuerte.
                    if (await pagina.Locator(selector).CountAsync() == 0)
                    {
                        throw new InvalidOperationException(
                            $"{Path.GetFileName(rutaHtml)}: el selector a ocultar '{selector}' no coincide con ningún elemento.");
                    }
                }

                await pagina.AddStyleTagAsync(new PageAddStyleTagOptions
                {
                    Content = string.Join("\n", ocultarSelectores.Select(s => $"{s} {{ visibility: hidden !important; }}")),
                });
            }
```

En `tests/StockApp.Presentation.VisualTests/Comparacion/ComparacionVisual.cs`: agregar el parámetro `IReadOnlyCollection<string>? ocultarEnMockup = null` al final de la firma (después de `recortesPermitidos`), documentarlo ("elementos del mockup que NO se comparan por píxeles y que el test custodia aparte; hoy solo la marca de agua del Login, P5") y pasarlo: `CapturadorMockup.CapturarAsync(rutaHtml, pngMockup, ocultarEnMockup)`.

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
Expected: PASS (los dos nuevos y el resto de la herramienta).

- [ ] **Step 2: Datos fijos**

`tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/DatosAcceso.cs`:

```csharp
using Moq;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Licenciamiento;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.VisualTests.Pantallas.Acceso;

/// <summary>Datos fijos de docs/ui-v2/mockups/acceso/*.html.</summary>
public static class DatosAcceso
{
    public const string CodigoMaquina = "7F3A-91C2-4D8E-B605";
    public const string Desafio = "K4PZ-8QWN-2M7R";

    private static IInfoApp Info() => Mock.Of<IInfoApp>(i => i.Version == "0.4.0");

    public static LoginViewModel Login(bool soloAccesoLimitado = false, string? usuario = null, string? error = null)
    {
        var vm = new LoginViewModel(Mock.Of<IAuthService>(), Shell(), Info(), soloAccesoLimitado);
        if (usuario is not null)
        {
            vm.NombreUsuario = usuario;
        }

        if (error is not null)
        {
            vm.MensajeError = error;
        }

        return vm;
    }

    // LoginViewModel exige un ShellViewModel; ninguno de sus servicios se usa al renderizar.
    private static ShellViewModel Shell() =>
        new(Mock.Of<IAuthService>(), Mock.Of<ILicenciaService>(), Mock.Of<IResetAdminService>(), Mock.Of<INavigationService>(),
            new CoordinadorActualizacion(Mock.Of<IUpdateService>(), new PoliticaUxActualizacion()), Mock.Of<IUiDispatcher>(), Info());
}
```

- [ ] **Step 3: Tests (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/AccesoComparacionTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests.Pantallas.Acceso;

[Trait("Categoria", "ComparacionVisual")]
public class AccesoComparacionTests
{
    // P5: la marca de agua (opacidad 0.28 + escala 0.85 de un PNG) no se replica igual entre Skia y
    // Chromium, y un UmbralAprobado para ella dejaría ciega a toda la pantalla (spec §6.5). Se oculta
    // en AMBOS renders y la custodia LoginMarcaAguaTests. Valores calibrados, sin UmbralAprobado.
    private static readonly string[] OcultarMarcaAgua = [".marca-agua"];

    private static LoginView LoginSinMarcaAgua()
    {
        var vista = new LoginView();
        vista.FindControl<Image>("MarcaAgua")!.IsVisible = false;
        return vista;
    }

    [AvaloniaFact]
    public void Login_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(LoginSinMarcaAgua(), DatosAcceso.Login(), "acceso/login.html", ocultarEnMockup: OcultarMarcaAgua);

    [AvaloniaFact]
    public void LoginConError_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(LoginSinMarcaAgua(),
            DatosAcceso.Login(usuario: "mperez", error: "Usuario o contraseña incorrectos."), "acceso/login-error.html",
            ocultarEnMockup: OcultarMarcaAgua);

    [AvaloniaFact]
    public void LoginAccesoLimitado_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(LoginSinMarcaAgua(), DatosAcceso.Login(soloAccesoLimitado: true), "acceso/login-acceso-limitado.html",
            ocultarEnMockup: OcultarMarcaAgua);
}
```

`tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/LoginMarcaAguaTests.cs` (la custodia de lo que la comparación de píxeles no ve; está en VisualTests porque necesita el render Skia):

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SkiaSharp;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Acceso;

/// <summary>
/// Marca de agua del logo de Carmelo en el Login (P5, aprobada y mergeada a main el 2026-07-10).
/// La comparación de píxeles la oculta (AccesoComparacionTests); este es el único guardián de que
/// sigue estando, con su opacidad, su escala y su lugar.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class LoginMarcaAguaTests
{
    private const double OpacidadAprobada = 0.28;
    private const double EscalaAprobada = 0.85;

    private static string Png(string nombre) => Path.Combine(ComparacionVisual.CarpetaSalida, "acceso-login-marca-agua", nombre);

    private static Image Marca(LoginView vista) =>
        vista.FindControl<Image>("MarcaAgua") ?? throw new InvalidOperationException("LoginView perdió Image#MarcaAgua.");

    [AvaloniaFact]
    public void Marca_ConservaFuenteOpacidadEscalaYLugar_YSoloDifiereDentroDeSuRegion()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Png("x"))!);
        Size fuente = default;

        // Con la marca (la ventana abierta: los Style de la vista solo aplican montada).
        RenderizadorVista.RenderizarAPng(new LoginView(), DatosAcceso.Login(), Png("con.png"), v =>
        {
            var marca = Marca((LoginView)v);
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
            // Detrás de la card: el ScrollViewer con la card viene DESPUÉS en el Grid.
            var panel = Assert.IsAssignableFrom<Panel>(marca.Parent);
            Assert.True(panel.Children.IndexOf(marca) < panel.Children.IndexOf(panel.Children.OfType<ScrollViewer>().Single()));
        });

        // Sin la marca: la diferencia entre las dos capturas ES la marca.
        var vistaSin = new LoginView();
        Marca(vistaSin).IsVisible = false;
        RenderizadorVista.RenderizarAPng(vistaSin, DatosAcceso.Login(), Png("sin.png"));

        var (cantidad, deltaMaximo, caja) = Diferencias(Png("con.png"), Png("sin.png"));

        // Región esperada: Uniform en 1280x760, por la escala aprobada, centrada (+-2 px).
        var uniforme = Math.Min(Lienzo.Ancho / fuente.Width, Lienzo.Alto / fuente.Height);
        var ancho = fuente.Width * uniforme * EscalaAprobada;
        var alto = fuente.Height * uniforme * EscalaAprobada;
        var esperada = new Rect((Lienzo.Ancho - ancho) / 2, (Lienzo.Alto - alto) / 2, ancho, alto).Inflate(2);
        Assert.True(cantidad > 1000, $"La marca casi no se ve: {cantidad} píxeles difieren.");
        Assert.True(esperada.Contains(caja), $"La marca se dibuja fuera de su región: {caja} no está en {esperada}.");
        // Con opacidad 0.28 ningún canal puede moverse más de 0.28 * 255 = 71.4 respecto del fondo.
        Assert.InRange(deltaMaximo, 8, 72);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("login-error")]
    [InlineData("login-acceso-limitado")]
    public void Mockup_DeclaraLaMismaMarcaDeAguaQueLaApp(string mockup)
    {
        var html = File.ReadAllText(Path.Combine(RutasRepo.CarpetaMockups, "acceso", mockup + ".html"));

        Assert.Contains("carmelo-original.png", html);
        Assert.Contains("opacity: " + OpacidadAprobada.ToString(CultureInfo.InvariantCulture), html);
        Assert.Contains("scale(" + EscalaAprobada.ToString(CultureInfo.InvariantCulture) + ")", html);
    }

    private static (int Cantidad, int DeltaMaximo, Rect Caja) Diferencias(string rutaA, string rutaB)
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

Los umbrales del test (`> 1000` píxeles, delta entre 8 y 72) son cotas, no valores medidos: en el Step 6 se corre el test, se anotan los valores reales (cantidad, delta máximo, caja) en el `progress.md` y, si una cota no tiene sentido para el logo real, se ajusta a una que siga fallando con opacidad 0, con opacidad 1 y con la marca fuera de lugar (las mutaciones (d) a (g) del Step 7 lo prueban).

`tests/StockApp.Presentation.UiTests/V2/LoginViewV2Tests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Login v2 (spec §5.4): todos los estados actuales y Enter en los campos.</summary>
public class LoginViewV2Tests
{
    private sealed class AuthQueRechaza : IAuthService
    {
        public int Intentos { get; private set; }

        public Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
        {
            Intentos++;
            return Task.FromResult(new LoginResult(false));
        }

        public Task LogoutAsync() => Task.CompletedTask;

        public Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync() =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                Width="1280" Height="760">
            <vistas:LoginView />
        </Window>
        """;

    private static (Window Ventana, LoginViewModel Vm, AuthQueRechaza Auth) Montar(bool soloAccesoLimitado = false)
    {
        var auth = new AuthQueRechaza();
        // ShellViewModel solo guarda sus dependencias; el camino de login fallido no las usa.
        var shell = new ShellViewModel(auth, null!, null!, null!, null!, null!, new InfoAppFake("0.4.0"));
        var vm = new LoginViewModel(auth, shell, new InfoAppFake("0.4.0"), soloAccesoLimitado);
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        ventana.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return (ventana, vm, auth);
    }

    private static T Nombrado<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static void Enter(TextBox campo)
    {
        campo.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Raiz_TieneUi2() =>
        Assert.Contains("ui2", Montar().Ventana.GetVisualDescendants().OfType<StockApp.Presentation.Views.LoginView>().Single().Classes);

    [AvaloniaFact]
    public void Marca_LlevaLaLineaMunicipioDeCarmeloBajoElTitulo()
    {
        // P6: bajo "Gestión Municipal" y antes de "Ingresá tus credenciales…".
        var ventana = Montar().Ventana;

        var textos = ventana.GetVisualDescendants().OfType<TextBlock>().ToList();
        var titulo = textos.Single(t => t.Classes.Contains("titulo-login"));
        var subtitulo = Assert.Single(textos, t => t.Classes.Contains("marca-subtitulo"));
        Assert.Equal("Municipio de Carmelo", subtitulo.Text);
        var yTitulo = titulo.TranslatePoint(default, ventana)!.Value.Y;
        var yInstruccion = textos.Single(t => t.Text == "Ingresá tus credenciales para continuar.").TranslatePoint(default, ventana)!.Value.Y;
        var ySubtitulo = subtitulo.TranslatePoint(default, ventana)!.Value.Y;
        Assert.InRange(ySubtitulo, yTitulo + 1, yInstruccion - 1);
    }

    [AvaloniaFact]
    public void MarcaDeAgua_ExisteDetrasDeLaCard() =>
        // El detalle (opacidad, escala, región) lo custodia LoginMarcaAguaTests; acá, que la parte exista.
        Assert.NotNull(Nombrado<Image>(Montar().Ventana, "MarcaAgua").Source);

    [AvaloniaTheory]
    [InlineData("CampoUsuario")]
    [InlineData("CampoContrasena")]
    public void Enter_EnUnCampo_EjecutaEntrar(string campo)
    {
        // Review Focus 4: el KeyDown="OnCampoKeyDown" no puede perderse en la reescritura.
        var (ventana, vm, auth) = Montar();
        vm.NombreUsuario = "mperez";
        vm.Contrasena = "secreto1";

        Enter(Nombrado<TextBox>(ventana, campo));

        Assert.Equal(1, auth.Intentos);
        Assert.Equal("Usuario o contraseña incorrectos.", vm.MensajeError);
    }

    [AvaloniaFact]
    public void ErrorDeLogin_SeMuestraEnLaAlertaDeError()
    {
        var (ventana, vm, _) = Montar();

        vm.MensajeError = "Usuario o contraseña incorrectos.";
        Dispatcher.UIThread.RunJobs();

        var alerta = Nombrado<Alerta>(ventana, "AlertaError");
        Assert.True(alerta.IsVisible);
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.Equal("Usuario o contraseña incorrectos.", alerta.Texto);
    }

    [AvaloniaFact]
    public void SinError_LaAlertaDeErrorNoSeVe() =>
        Assert.False(Nombrado<Alerta>(Montar().Ventana, "AlertaError").IsVisible);

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AvisoDeAccesoLimitado_SoloEnEseModo(bool soloAccesoLimitado)
    {
        var alerta = Nombrado<Alerta>(Montar(soloAccesoLimitado).Ventana, "AvisoAccesoLimitado");

        Assert.Equal(soloAccesoLimitado, alerta.IsVisible);
        Assert.Equal(VarianteAlerta.Advertencia, alerta.Variante);
    }

    [AvaloniaFact]
    public void BotonesConSusTextosRealesYComandos()
    {
        var (ventana, vm, _) = Montar();

        var entrar = Nombrado<Button>(ventana, "BotonEntrar");
        Assert.Equal("Entrar", entrar.Content);
        Assert.Contains("primario", entrar.Classes);
        Assert.True(entrar.IsDefault);
        Assert.Same(vm.EntrarCommand, entrar.Command);
        var reset = Nombrado<Button>(ventana, "EnlaceReset");
        Assert.Equal("No puedo entrar / resetear Admin", reset.Content);
        Assert.Same(vm.ResetearAdminCommand, reset.Command);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~LoginViewV2Tests"` y `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~AccesoComparacionTests|FullyQualifiedName~LoginMarcaAguaTests"`
Expected: FAIL (no existen las partes nombradas ni `MarcaAgua`/`marca-subtitulo`; la raíz no tiene `ui2`; las comparaciones fallan contra el Login viejo). `Enter_EnUnCampo_EjecutaEntrar` puede fallar solo por no encontrar `CampoUsuario`.

- [ ] **Step 4: El XAML**

Reemplazar `LoginView.axaml` entero por:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1280" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.LoginView"
             x:DataType="vm:LoginViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <UserControl.Styles>
        <!-- Marca de agua del logo del municipio (P5): efecto de marca fijo aprobado y mergeado a main
             el 2026-07-10 (commits 4bb3222 + 8ee5f32). Se define por Style (Priority=Style, no
             LocalValue), igual que antes y que Image.fondo-difuminado de InicioView, para no chocar con
             el guardián de opacidades literales. Su custodia es LoginMarcaAguaTests. -->
        <Style Selector="Image.marca-agua">
            <Setter Property="Opacity" Value="0.28" />
        </Style>
    </UserControl.Styles>

    <!-- UI v2 (spec §5.4). Espejo: docs/ui-v2/mockups/acceso/login*.html. Card de 400 centrada;
         el ScrollViewer evita recortar en una ventana más baja que la card (MinHeight 300).
         El logo va primero en el Grid: detrás de la card. -->
    <Grid>
    <Image x:Name="MarcaAgua"
           Source="avares://GestionMunicipal/Assets/carmelo-original.png"
           Classes="marca-agua"
           Stretch="Uniform"
           HorizontalAlignment="Center"
           VerticalAlignment="Center"
           RenderTransform="scale(0.85)" />

    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <v2:TarjetaContenido Width="{DynamicResource V2AnchoCardLogin}"
                             Padding="32"
                             HorizontalAlignment="Center"
                             VerticalAlignment="Center">
            <StackPanel>

                <TextBlock Classes="titulo-login" Text="Gestión Municipal" HorizontalAlignment="Center" />

                <!-- P6: la línea del mockup de referencia (patrones-1.html), margen superior 2. -->
                <TextBlock Classes="marca-subtitulo" Text="Municipio de Carmelo" HorizontalAlignment="Center" Margin="0,2,0,0" />

                <TextBlock Classes="texto-secundario"
                           Text="Ingresá tus credenciales para continuar."
                           HorizontalAlignment="Center"
                           TextAlignment="Center"
                           TextWrapping="Wrap"
                           Margin="0,16,0,0" />

                <!-- Modo acceso limitado (FIX 1, re-review final E1): al autenticar va a
                     Mantenimiento (solo backups) y no al sistema completo. -->
                <v2:Alerta x:Name="AvisoAccesoLimitado"
                           Variante="Advertencia"
                           Margin="0,16,0,0"
                           Texto="Modo de acceso limitado: la licencia está vencida. Solo vas a poder descargar backups."
                           IsVisible="{Binding SoloAccesoLimitado}" />

                <v2:Alerta x:Name="AlertaError"
                           Variante="Error"
                           Margin="0,16,0,0"
                           Texto="{Binding MensajeError}"
                           IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />

                <v2:CampoFormulario Etiqueta="Usuario" Margin="0,16,0,0">
                    <TextBox x:Name="CampoUsuario"
                             Text="{Binding NombreUsuario}"
                             PlaceholderText="Nombre de usuario"
                             KeyDown="OnCampoKeyDown" />
                </v2:CampoFormulario>

                <v2:CampoFormulario Etiqueta="Contraseña" Margin="0,16,0,0">
                    <TextBox x:Name="CampoContrasena"
                             Text="{Binding Contrasena}"
                             PasswordChar="●"
                             PlaceholderText="Contraseña"
                             KeyDown="OnCampoKeyDown" />
                </v2:CampoFormulario>

                <Button x:Name="BotonEntrar"
                        Classes="primario"
                        Content="Entrar"
                        Command="{Binding EntrarCommand}"
                        IsDefault="True"
                        HorizontalAlignment="Stretch"
                        Margin="0,20,0,0" />

                <!-- Reset de Admin (Inc 7 Fase B): link discreto al flujo de recuperación. -->
                <Button x:Name="EnlaceReset"
                        Theme="{StaticResource V2TemaEnlace}"
                        Content="No puedo entrar / resetear Admin"
                        Command="{Binding ResetearAdminCommand}"
                        HorizontalAlignment="Center"
                        Margin="0,12,0,0" />

                <TextBlock Classes="version" Text="{Binding VersionTexto}" HorizontalAlignment="Center" Margin="0,16,0,0" />

            </StackPanel>
        </v2:TarjetaContenido>
    </ScrollViewer>
    </Grid>

</UserControl>
```


- [ ] **Step 5: `GuardianDePatronTests`**

En `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`:

1. Sacar `typeof(LoginView),` de `VistasCentradasSinSidebar`.
2. Debajo de `VistasFueraDelGuardian`, agregar:

```csharp
    /// <summary>
    /// Vistas reescritas con el sistema v2 (spec 2026-09-28): los patrones P0-P9 de este guardián ya
    /// no les aplican (por ejemplo, P6 exige Border.card con padding 48 y la card v2 es
    /// TarjetaContenido con 32). Las custodian su comparación contra el mockup (VisualTests) y sus
    /// tests de UiTests/V2. La Fase 9 reemplaza este guardián entero (spec §8.2).
    /// </summary>
    public static readonly HashSet<Type> VistasMigradasAV2 = new()
    {
        typeof(LoginView),
    };
```

3. En `Guardian_CubreTodasLasVistasDelEnsamblado`, agregar `.Concat(VistasMigradasAV2)` después de `.Concat(VistasFueraDelGuardian)`, y en el `<summary>` del test mencionar la quinta lista.

- [ ] **Step 6: Verde**

Run: `dotnet build StockApp.sln`, `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj` (suite UI completa: `GuardianDePatronTests`, `BrandingGestionMunicipalTests` —"Gestión Municipal" sigue visible— y los nuevos), `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~LoginMarcaAguaTests"` y `scripts/ui-v2/comparar.sh Login`.
Expected: PASS; las tres pantallas de Login APROBADAS con los valores calibrados (11 teselas, 1.68%, SIN `UmbralAprobado`), sin recortes (el guardián ahora mira los `TextBox`: el placeholder y "mperez" entran). Revisar las tres páginas de comparación (su `app.png` y su `mockup.png` salen SIN logo, por diseño).

Medición de respaldo, que no se commitea: correr una vez un test temporal idéntico a `Login_IgualAlMockup` pero SIN `ocultarEnMockup` y SIN `IsVisible = false`, anotar en el `progress.md` `Marca de agua visible: X% / N teselas densas (no comparable, motivo de la exclusión)` y borrar el test temporal. Guardar además la captura de la app con la marca visible (`artifacts/ui-v2/comparacion/acceso-login-marca-agua/con.png`, la deja `LoginMarcaAguaTests`) y pedirle al usuario que la mire junto al mockup `login.html` (que lleva el logo puesto): ahí revisa posición y tono de la marca, que la comparación de píxeles no ve. Anotar en el `progress.md` los valores reales del test (píxeles que difieren, delta máximo, caja) y ajustar las cotas si hace falta (ver el Step 3).

Si la card no queda centrada en vertical dentro del `ScrollViewer` (la comparación lo muestra como bloque corrido): envolver la `TarjetaContenido` en un `Grid` con `MinHeight` igual al alto del viewport (`MinHeight="{Binding $parent[ScrollViewer].Viewport.Height}"`) y la card con `VerticalAlignment="Center"` adentro; re-correr.

- [ ] **Step 7: Mutación quitando**

```bash
cp src/StockApp.Presentation/Views/LoginView.axaml "$SCRATCH/LoginView.axaml.bak"
```

(a) Quitar `KeyDown="OnCampoKeyDown"` del `CampoContrasena` → rojo en `Enter_EnUnCampo_EjecutaEntrar("CampoContrasena")`. Restaurar con `cp`.
(b) Quitar `IsVisible="{Binding SoloAccesoLimitado}"` → rojo en `AvisoDeAccesoLimitado_SoloEnEseModo(false)` y en `Login_IgualAlMockup` (aparece la alerta). Restaurar con `cp`.
(c) Quitar `Classes="ui2"` de la raíz → rojo en `Raiz_TieneUi2` y en las tres comparaciones. Restaurar con `cp`.
(d) Quitar el `<Setter Property="Opacity" Value="0.28" />` del `Style` de la marca (la imagen queda a opacidad 1) → rojo en `Marca_ConservaFuenteOpacidadEscalaYLugar_YSoloDifiereDentroDeSuRegion`. Restaurar con `cp`.
(e) Quitar `RenderTransform="scale(0.85)"` → rojo en el mismo test (escala y región). Restaurar con `cp`. En el mockup: quitar `transform: scale(0.85);` de `.marca-agua` de `login.html` (backup con `cp`) → rojo en `Mockup_DeclaraLaMismaMarcaDeAguaQueLaApp("login")`. Restaurar con `cp`.
(f) Quitar el `TextBlock Classes="marca-subtitulo"` → rojo en `Marca_LlevaLaLineaMunicipioDeCarmeloBajoElTitulo` y en las tres comparaciones. Restaurar con `cp`.
(g) Quitar el `Image x:Name="MarcaAgua"` entero → rojo en `MarcaDeAgua_ExisteDetrasDeLaCard` y en `LoginMarcaAguaTests` (y deja de compilar la comparación por el `FindControl` nulo: es el comportamiento buscado). Restaurar con `cp`.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/Views/LoginView.axaml tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs tests/StockApp.UiV2.Herramientas.Tests/CapturadorMockupTests.cs tests/StockApp.Presentation.VisualTests/Comparacion/ComparacionVisual.cs tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/ tests/StockApp.Presentation.UiTests/V2/LoginViewV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs
git commit -m "feat(ui-v2): Login v2 con sus tres estados comparados y la marca de agua custodiada aparte"
```

### Task 13: `ResetAdminView` y `BloqueoLicenciaView` v2

**Depende de:** Tasks 5, 7 y 12 (`DatosAcceso`, `VistasMigradasAV2`).

**Files:**
- Modify: `src/StockApp.Presentation/Views/ResetAdminView.axaml` (code-behind sin cambios: `CopiarCodigoMaquina_Click`, `CopiarDesafio_Click`)
- Modify: `src/StockApp.Presentation/Views/BloqueoLicenciaView.axaml` (code-behind sin cambios: `DataContextChanged` → `CargarEstadoAsync`, `CopiarCodigoMaquina_Click`)
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/DatosAcceso.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/AccesoComparacionTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/ResetYLicenciaV2Tests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`

**Interfaces:**
- Consumes: `ResetAdminViewModel(IResetAdminService)` con `CodigoMaquina`, `Desafio`, `TokenPegado`, `NuevaContrasena`, `MensajeError`, `Completado`, `PedirDesafioCommand`, `ResetearCommand`, `VolverCommand`; `BloqueoLicenciaViewModel(ILicenciaService)` con `CodigoMaquina`, `LicenciaPegada`, `MensajeError`, `ActivarCommand`, `IrALoginAccesoLimitadoCommand`; `ILicenciaService.ObtenerEstadoAsync()` → `EstadoLicenciaDto(bool Activada, string CodigoMaquina)`; mockups `acceso/reset.html` y `acceso/bloqueo-licencia.html`.
- Produces: `DatosAcceso.Reset()` y `DatosAcceso.BloqueoLicencia()`; partes nombradas `AlertaCompletado`, `AlertaError` (Reset), `AlertaError`, `EnlaceAccesoLimitado` (Bloqueo).

- [ ] **Step 1: Datos fijos y tests (rojo)**

Agregar a `DatosAcceso`:

```csharp
    public static ResetAdminViewModel Reset()
    {
        var vm = new ResetAdminViewModel(Mock.Of<IResetAdminService>());
        vm.CodigoMaquina = CodigoMaquina;
        vm.Desafio = Desafio;
        return vm;
    }

    // El código lo carga el DataContextChanged de BloqueoLicenciaView (CargarEstadoAsync): si la
    // vista perdiera ese wiring, el mockup no coincide.
    public static BloqueoLicenciaViewModel BloqueoLicencia()
    {
        var licencia = new Mock<ILicenciaService>();
        licencia.Setup(l => l.ObtenerEstadoAsync()).ReturnsAsync(new EstadoLicenciaDto(false, CodigoMaquina));
        return new BloqueoLicenciaViewModel(licencia.Object);
    }
```

Agregar a `AccesoComparacionTests`:

```csharp
    [AvaloniaFact]
    public void Reset_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(), "acceso/reset.html");

    [AvaloniaFact]
    public void BloqueoLicencia_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new BloqueoLicenciaView(), DatosAcceso.BloqueoLicencia(), "acceso/bloqueo-licencia.html");
```

`tests/StockApp.Presentation.UiTests/V2/ResetYLicenciaV2Tests.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Licenciamiento;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Reset de Admin y activación de licencia v2 (spec §5.4): todos los estados actuales.</summary>
public class ResetYLicenciaV2Tests
{
    private sealed class ResetInerte : IResetAdminService
    {
        public Task<DesafioResetDto> SolicitarDesafioAsync() => Task.FromResult(new DesafioResetDto("D", "C"));
        public Task<ResultadoResetDto> ResetearAsync(string token, string nuevaContrasena) => Task.FromResult(new ResultadoResetDto(true, null));
    }

    private sealed class LicenciaFija : ILicenciaService
    {
        public Task<EstadoLicenciaDto> ObtenerEstadoAsync() => Task.FromResult(new EstadoLicenciaDto(false, "7F3A-91C2-4D8E-B605"));
        public Task<ResultadoActivacionDto> ActivarAsync(string licencia) => Task.FromResult(new ResultadoActivacionDto(false, "x"));
    }

    private static Window Montar(Control vista, object vm)
    {
        var ventana = new Window { Width = 1280, Height = 760, Content = vista };
        vista.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return ventana;
    }

    private static T Nombrado<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    [AvaloniaFact]
    public void Reset_Completado_MuestraElAvisoDeExito()
    {
        var vm = new ResetAdminViewModel(new ResetInerte());
        var ventana = Montar(new ResetAdminView(), vm);
        Assert.False(Nombrado<Alerta>(ventana, "AlertaCompletado").IsVisible);

        vm.Completado = true;
        Dispatcher.UIThread.RunJobs();

        var alerta = Nombrado<Alerta>(ventana, "AlertaCompletado");
        Assert.True(alerta.IsVisible);
        Assert.Equal("Listo. Volvé al login e ingresá con la nueva contraseña.", alerta.Texto);
    }

    [AvaloniaFact]
    public void Reset_Error_MuestraLaAlertaDeError()
    {
        var vm = new ResetAdminViewModel(new ResetInerte());
        var ventana = Montar(new ResetAdminView(), vm);

        vm.MensajeError = "El token no es válido.";
        Dispatcher.UIThread.RunJobs();

        var alerta = Nombrado<Alerta>(ventana, "AlertaError");
        Assert.True(alerta.IsVisible);
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
    }

    [AvaloniaFact]
    public void Bloqueo_AlAsignarElVm_CargaElCodigoDeMaquina()
    {
        // Review Focus 4: el DataContextChanged del code-behind no puede perderse.
        var ventana = Montar(new BloqueoLicenciaView(), new BloqueoLicenciaViewModel(new LicenciaFija()));

        Assert.Contains(ventana.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "7F3A-91C2-4D8E-B605");
    }

    [AvaloniaFact]
    public void Bloqueo_EnlaceDeAccesoLimitado_UsaSuComando()
    {
        var vm = new BloqueoLicenciaViewModel(new LicenciaFija());
        var ventana = Montar(new BloqueoLicenciaView(), vm);

        var enlace = Nombrado<Button>(ventana, "EnlaceAccesoLimitado");
        Assert.Equal("¿Necesitás bajar un backup? Iniciar sesión", enlace.Content);
        Assert.Same(vm.IrALoginAccesoLimitadoCommand, enlace.Command);
    }

    [AvaloniaFact]
    public void AmbasVistas_TienenUi2EnLaRaiz()
    {
        Assert.Contains("ui2", new ResetAdminView().Classes);
        Assert.Contains("ui2", new BloqueoLicenciaView().Classes);
    }
}
```

Verificar en `ResetAdminViewModel.cs` y `BloqueoLicenciaViewModel.cs` que `Completado`, `MensajeError` y `IrALoginAccesoLimitadoCommand` son públicos con esos nombres (los bindea el XAML actual).

Run: los filtros `ResetYLicenciaV2Tests` (UiTests) y `AccesoComparacionTests` (VisualTests).
Expected: FAIL (partes nombradas inexistentes, raíz sin `ui2`, comparaciones contra el aspecto viejo).

- [ ] **Step 2: `ResetAdminView.axaml`**

Reemplazar entero por:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1280" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.ResetAdminView"
             x:DataType="vm:ResetAdminViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (spec §5.4). Espejo: docs/ui-v2/mockups/acceso/reset.html. -->
    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <v2:TarjetaContenido Width="{DynamicResource V2AnchoCardLogin}"
                             Padding="32"
                             HorizontalAlignment="Center"
                             VerticalAlignment="Center">
            <StackPanel Spacing="16">

                <TextBlock Classes="titulo-login" Text="Recuperar acceso de Admin" HorizontalAlignment="Center" />

                <Button Classes="secundario"
                        Content="1) Generar código de recuperación"
                        Command="{Binding PedirDesafioCommand}"
                        HorizontalAlignment="Stretch" />

                <v2:CampoFormulario Etiqueta="Código de máquina">
                    <Grid ColumnDefinitions="*,Auto">
                        <SelectableTextBlock Grid.Column="0"
                                             Text="{Binding CodigoMaquina}"
                                             TextWrapping="Wrap"
                                             VerticalAlignment="Center"
                                             FontFeatures="+tnum"
                                             FontSize="{DynamicResource V2FuenteControlTamano}"
                                             Foreground="{DynamicResource V2TextoEtiquetaBrush}" />
                        <Button Grid.Column="1"
                                Classes="secundario"
                                Content="Copiar"
                                Margin="8,0,0,0"
                                VerticalAlignment="Center"
                                Click="CopiarCodigoMaquina_Click" />
                    </Grid>
                </v2:CampoFormulario>

                <v2:CampoFormulario Etiqueta="Desafío">
                    <Grid ColumnDefinitions="*,Auto">
                        <SelectableTextBlock Grid.Column="0"
                                             Text="{Binding Desafio}"
                                             TextWrapping="Wrap"
                                             VerticalAlignment="Center"
                                             FontFeatures="+tnum"
                                             FontSize="{DynamicResource V2FuenteControlTamano}"
                                             Foreground="{DynamicResource V2TextoEtiquetaBrush}" />
                        <Button Grid.Column="1"
                                Classes="secundario"
                                Content="Copiar"
                                Margin="8,0,0,0"
                                VerticalAlignment="Center"
                                Click="CopiarDesafio_Click" />
                    </Grid>
                </v2:CampoFormulario>

                <v2:CampoFormulario Etiqueta="Token de reset (del proveedor)">
                    <TextBox Text="{Binding TokenPegado}"
                             AcceptsReturn="True"
                             TextWrapping="Wrap"
                             Height="88"
                             PlaceholderText="Pegá el token firmado" />
                </v2:CampoFormulario>

                <v2:CampoFormulario Etiqueta="Nueva contraseña de Admin">
                    <TextBox Text="{Binding NuevaContrasena}"
                             PasswordChar="●"
                             PlaceholderText="Nueva contraseña" />
                </v2:CampoFormulario>

                <Button Classes="primario"
                        Content="2) Resetear Admin"
                        Command="{Binding ResetearCommand}"
                        IsDefault="True"
                        HorizontalAlignment="Stretch" />

                <v2:Alerta x:Name="AlertaCompletado"
                           Variante="Info"
                           Texto="Listo. Volvé al login e ingresá con la nueva contraseña."
                           IsVisible="{Binding Completado}" />

                <v2:Alerta x:Name="AlertaError"
                           Variante="Error"
                           Texto="{Binding MensajeError}"
                           IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />

                <Button Classes="ghost"
                        Content="Volver al login"
                        Command="{Binding VolverCommand}"
                        HorizontalAlignment="Stretch" />

            </StackPanel>
        </v2:TarjetaContenido>
    </ScrollViewer>

</UserControl>
```

`StackPanel Spacing="16"` ignora a los hijos invisibles: con `Completado=false` y sin error, el espacio entre "2) Resetear Admin" y "Volver al login" es 16, igual que el mockup. Si `FontFeatures` no existe como propiedad de `SelectableTextBlock` en Avalonia 12.0.5 (error de build), usar `TextElement.FontFeatures="+tnum"` (la forma que usa `DataGridV2.axaml`).

- [ ] **Step 3: `BloqueoLicenciaView.axaml`**

Reemplazar entero por:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1280" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.BloqueoLicenciaView"
             x:DataType="vm:BloqueoLicenciaViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (spec §5.4). Espejo: docs/ui-v2/mockups/acceso/bloqueo-licencia.html. El código de
         máquina lo carga el DataContextChanged del code-behind (no cambia). -->
    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <v2:TarjetaContenido Width="{DynamicResource V2AnchoCardLogin}"
                             Padding="32"
                             HorizontalAlignment="Center"
                             VerticalAlignment="Center">
            <StackPanel>

                <TextBlock Classes="titulo-login" Text="Activación de licencia" HorizontalAlignment="Center" />

                <TextBlock Classes="texto-secundario"
                           Text="Este servidor no tiene una licencia activada. Pasale el código de máquina al proveedor y pegá la licencia que te devuelva."
                           HorizontalAlignment="Center"
                           TextAlignment="Center"
                           TextWrapping="Wrap"
                           Margin="0,16,0,0" />

                <v2:CampoFormulario Etiqueta="Código de máquina" Margin="0,16,0,0">
                    <Grid ColumnDefinitions="*,Auto">
                        <SelectableTextBlock Grid.Column="0"
                                             Text="{Binding CodigoMaquina}"
                                             TextWrapping="Wrap"
                                             VerticalAlignment="Center"
                                             FontFeatures="+tnum"
                                             FontSize="{DynamicResource V2FuenteControlTamano}"
                                             Foreground="{DynamicResource V2TextoEtiquetaBrush}" />
                        <Button Grid.Column="1"
                                Classes="secundario"
                                Content="Copiar"
                                Margin="8,0,0,0"
                                VerticalAlignment="Center"
                                Click="CopiarCodigoMaquina_Click" />
                    </Grid>
                </v2:CampoFormulario>

                <v2:CampoFormulario Etiqueta="Licencia" Margin="0,16,0,0">
                    <TextBox Text="{Binding LicenciaPegada}"
                             AcceptsReturn="True"
                             TextWrapping="Wrap"
                             Height="88"
                             PlaceholderText="Pegá acá el texto de la licencia" />
                </v2:CampoFormulario>

                <v2:Alerta x:Name="AlertaError"
                           Variante="Error"
                           Margin="0,16,0,0"
                           Texto="{Binding MensajeError}"
                           IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />

                <Button Classes="primario"
                        Content="Activar"
                        Command="{Binding ActivarCommand}"
                        IsDefault="True"
                        HorizontalAlignment="Stretch"
                        Margin="0,16,0,0" />

                <!-- Modo acceso limitado (FIX 1, re-review final E1): único camino a los backups
                     mientras la licencia sigue vencida. No habilita operar el sistema. -->
                <Button x:Name="EnlaceAccesoLimitado"
                        Theme="{StaticResource V2TemaEnlace}"
                        Content="¿Necesitás bajar un backup? Iniciar sesión"
                        Command="{Binding IrALoginAccesoLimitadoCommand}"
                        HorizontalAlignment="Center"
                        Margin="0,12,0,0" />

            </StackPanel>
        </v2:TarjetaContenido>
    </ScrollViewer>

</UserControl>
```

- [ ] **Step 4: `GuardianDePatronTests`**

Sacar `typeof(ResetAdminView)` y `typeof(BloqueoLicenciaView)` de `VistasCentradasSinSidebar` y agregarlos a `VistasMigradasAV2`. `VistasCentradasSinSidebar` queda vacía: xUnit falla un `[MemberData]` sin filas ("No data found"), así que borrar también la lista y el test `VistaCentrada_TieneCardConPaddingHolgado`, y sacar `.Concat(VistasCentradasSinSidebar…)` de `Guardian_CubreTodasLasVistasDelEnsamblado` (actualizar su `<summary>`).

- [ ] **Step 5: Verde**

Run: `dotnet build StockApp.sln`, `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj` (suite UI completa, incluido `CargaProtegidaSinPermisoGuardianTests`, que sigue encontrando `CargarEstadoAsync` en `BloqueoLicenciaView.axaml.cs`) y `scripts/ui-v2/comparar.sh Acceso`.
Expected: PASS; las cinco pantallas de acceso APROBADAS. Revisar las páginas.

- [ ] **Step 6: Mutación quitando**

```bash
cp src/StockApp.Presentation/Views/BloqueoLicenciaView.axaml.cs "$SCRATCH/BloqueoLicenciaView.axaml.cs.bak"
cp src/StockApp.Presentation/Views/ResetAdminView.axaml "$SCRATCH/ResetAdminView.axaml.bak"
```

(a) Quitar el bloque `DataContextChanged += …;` del constructor de `BloqueoLicenciaView.axaml.cs` → rojo en `Bloqueo_AlAsignarElVm_CargaElCodigoDeMaquina` y en `BloqueoLicencia_IgualAlMockup` (sin código). Restaurar con `cp`.
(b) Quitar `IsVisible="{Binding Completado}"` de `AlertaCompletado` → rojo en `Reset_Completado_MuestraElAvisoDeExito` (visible desde el inicio) y en `Reset_IgualAlMockup`. Restaurar con `cp`.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Views/ResetAdminView.axaml src/StockApp.Presentation/Views/BloqueoLicenciaView.axaml tests/StockApp.Presentation.VisualTests/Pantallas/Acceso/ tests/StockApp.Presentation.UiTests/V2/ResetYLicenciaV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs
git commit -m "feat(ui-v2): Reset de Admin y activación de licencia v2, comparados contra el mockup"
```

### Task 14: `AccesoLimitadoView`: aviso v2 sin filtrar estilos a Mantenimiento

**Decisión aplicada:** P3 (en la Fase 1 solo se migra el aviso; la comparación de la pantalla entera va en la Fase 7).

**Files:**
- Modify: `src/StockApp.Presentation/Views/AccesoLimitadoView.axaml`
- Modify: `tests/StockApp.Presentation.UiTests/AccesoLimitadoViewTests.cs` (tests nuevos, mismo `Montar`)

**Interfaces:**
- Consumes: `Alerta` (Variante `Advertencia`), `AccesoLimitadoViewModel.Mantenimiento`, `AccesoLimitadoViewTests.Montar(IReadOnlyList<CorridaBackupDto>)` existente.
- Produces: `Border x:Name="AvisoAccesoLimitado"` con `Classes="ui2"` que contiene la `Alerta`; la raíz y `MantenimientoView` sin `ui2` en su cadena de ancestros.

- [ ] **Step 1: Tests (rojo)**

Agregar a `AccesoLimitadoViewTests` (usa su `Montar` y sus fakes; agregar `using StockApp.Presentation.Controls.V2;` y `using StockApp.Presentation.Views.Administracion;`):

```csharp
    [AvaloniaFact]
    public void Aviso_EsUnaAlertaDeAdvertenciaV2ConElTextoReal()
    {
        var (window, _) = Montar(System.Array.Empty<CorridaBackupDto>());

        var alerta = window.GetVisualDescendants().OfType<Alerta>().Single();
        Assert.Equal(VarianteAlerta.Advertencia, alerta.Variante);
        Assert.Equal(
            "Modo de acceso limitado: la licencia está vencida. Solo podés descargar backups; para operar el sistema, activá la licencia.",
            alerta.Texto);
        Assert.True(alerta.GetVisualAncestors().OfType<Border>().Any(b => b.Name == "AvisoAccesoLimitado" && b.Classes.Contains("ui2")));
    }

    [AvaloniaFact]
    public void MantenimientoHospedado_NoTieneAncestroUi2()
    {
        // Review Focus 1: MantenimientoView se migra en la Fase 7; hasta entonces no puede tomar
        // los estilos .ui2 por herencia del contenedor.
        var (window, _) = Montar(System.Array.Empty<CorridaBackupDto>());

        var mantenimiento = window.GetVisualDescendants().OfType<MantenimientoView>().Single();
        Assert.DoesNotContain(mantenimiento.GetSelfAndVisualAncestors().OfType<StyledElement>(), e => e.Classes.Contains("ui2"));
    }
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~AccesoLimitadoViewTests"`
Expected: FAIL en `Aviso_EsUnaAlertaDeAdvertenciaV2ConElTextoReal` (no hay `Alerta`); PASS en `MantenimientoHospedado_NoTieneAncestroUi2` (hoy no hay `ui2` en ningún lado: queda como guardián del Step 2) y en los dos existentes.

- [ ] **Step 2: El XAML**

Reemplazar el `Border DockPanel.Dock="Top" Classes="card" …>…</Border>` de `AccesoLimitadoView.axaml` por (agregar `xmlns:v2="using:StockApp.Presentation.Controls.V2"` a la raíz; la raíz NO lleva `ui2` y conserva su `Background`):

```xml
        <!-- UI v2 (Fase 1, P3): solo el aviso. Classes="ui2" va en este Border y NO en la raíz:
             MantenimientoView (Fase 7) no puede heredar los estilos v2. -->
        <Border x:Name="AvisoAccesoLimitado"
                DockPanel.Dock="Top"
                Classes="ui2"
                Margin="16,16,16,0">
            <v2:Alerta Variante="Advertencia"
                       Texto="Modo de acceso limitado: la licencia está vencida. Solo podés descargar backups; para operar el sistema, activá la licencia." />
        </Border>
```

- [ ] **Step 3: Verde**

Run: `dotnet build StockApp.sln` y `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS (incluido `GuardianDePatronTests`: `AccesoLimitadoView` sigue en `VistasEmbebidas`; si algún invariante viejo falla por el aviso v2, moverla a `VistasMigradasAV2` y anotarlo en el reporte).

- [ ] **Step 4: Mutación quitando**

```bash
cp src/StockApp.Presentation/Views/AccesoLimitadoView.axaml "$SCRATCH/AccesoLimitadoView.axaml.bak"
```

Mover `Classes="ui2"` del `Border` del aviso a la raíz `UserControl` (reproduce la fuga) → rojo en `MantenimientoHospedado_NoTieneAncestroUi2` y en `Aviso_…` (el Border ya no tiene la clase). Restaurar con `cp`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Views/AccesoLimitadoView.axaml tests/StockApp.Presentation.UiTests/AccesoLimitadoViewTests.cs
git commit -m "feat(ui-v2): aviso de acceso limitado como Alerta v2 sin tocar Mantenimiento"
```

### Task 15: Diálogos v2 (Confirmación, Mensaje, PedirTexto)

**Depende de:** Task 8 (mockups aprobados). **Decisión aplicada:** P2 (Reclasificar queda para la Fase 5).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Dialogs/ConfirmacionDialog.axaml`, `MensajeDialog.axaml`, `PedirTextoDialog.axaml` (code-behind sin cambios)
- Create: `tests/StockApp.Presentation.VisualTests/Comparacion/ContenidoDeDialogo.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/DialogosComparacionTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/DialogosV2Tests.cs`
- Modify: `docs/ui-v2/herramientas.md` (sección "Test por pantalla": diálogos)

**Interfaces:**
- Consumes: constructores `ConfirmacionDialog(string)`, `MensajeDialog(string)`, `PedirTextoDialog(string titulo, string mensaje)`; `x:Name` que lee el code-behind: `MensajeText`, `TextoTextBox`, `ConfirmarButton`, `CancelarButton`, `AceptarButton`.
- Produces: en los tres diálogos, el contenido del `Window` es un `Border x:Name="Contenido"` con `Classes="ui2"`. `public static Control ContenidoDeDialogo.Extraer(Window dialogo)`: desengancha ese contenido y lo devuelve dentro de un `Border` centrado con el ancho del diálogo.

- [ ] **Step 1: Helper y tests (rojo)**

`tests/StockApp.Presentation.VisualTests/Comparacion/ContenidoDeDialogo.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Layout;

namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Los diálogos son Window y RenderizadorVista no puede meter un Window dentro de su ventana
/// ("already has a visual parent"). Se compara el ÁREA CLIENTE: el Border raíz del diálogo (que
/// lleva ui2 y su fondo) desenganchado del Window, centrado y con el ancho del diálogo. La barra
/// de título es del sistema operativo y no se compara.
/// </summary>
public static class ContenidoDeDialogo
{
    public static Control Extraer(Window dialogo)
    {
        var contenido = dialogo.Content as Control
            ?? throw new InvalidOperationException($"{dialogo.GetType().Name} no tiene un Control como contenido.");
        dialogo.Content = null;
        return new Border
        {
            Width = dialogo.Width,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = contenido,
        };
    }
}
```

`tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/DialogosComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views.Dialogs;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests.Pantallas.Dialogos;

[Trait("Categoria", "ComparacionVisual")]
public class DialogosComparacionTests
{
    [AvaloniaFact]
    public void Confirmacion_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(
            ContenidoDeDialogo.Extraer(new ConfirmacionDialog("¿Cerrar la sesión?")), null, "dialogos/confirmacion.html");

    [AvaloniaFact]
    public void Mensaje_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(
            ContenidoDeDialogo.Extraer(new MensajeDialog("No se puede dar de baja la categoría: tiene productos asociados.")),
            null, "dialogos/mensaje.html");

    [AvaloniaFact]
    public void PedirTexto_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(
            ContenidoDeDialogo.Extraer(new PedirTextoDialog("Anular documento", "Ingresá el motivo de la anulación:")),
            null, "dialogos/pedir-texto.html");
}
```

`tests/StockApp.Presentation.UiTests/V2/DialogosV2Tests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StockApp.Presentation.Views.Dialogs;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Diálogos v2 (spec §5.5): siguen sin bindings, con acceso por x:Name. Review Focus 5: un
/// x:Name o un Click perdido cambia el resultado que recibe ConfirmacionService.
/// </summary>
public class DialogosV2Tests
{
    private static Task<T> Abrir<T>(Window dialogo)
    {
        var duenio = new Window();
        duenio.Show();
        var resultado = dialogo.ShowDialog<T>(duenio);
        Dispatcher.UIThread.RunJobs();
        return resultado;
    }

    private static void Clic(Window dialogo, string boton)
    {
        dialogo.FindControl<Button>(boton)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("ConfirmarButton", true)]
    [InlineData("CancelarButton", false)]
    public void Confirmacion_DevuelveSegunElBoton(string boton, bool esperado)
    {
        var dialogo = new ConfirmacionDialog("¿Cerrar la sesión?");
        var resultado = Abrir<bool>(dialogo);

        Clic(dialogo, boton);

        Assert.True(resultado.IsCompleted);
        Assert.Equal(esperado, resultado.Result);
    }

    [AvaloniaFact]
    public void PedirTexto_AceptarDevuelveElTexto_CancelarDevuelveNull()
    {
        var aceptar = new PedirTextoDialog("Anular documento", "Ingresá el motivo de la anulación:");
        var conTexto = Abrir<string?>(aceptar);
        aceptar.FindControl<TextBox>("TextoTextBox")!.Text = "Error de carga";
        Clic(aceptar, "AceptarButton");
        Assert.Equal("Error de carga", conTexto.Result);

        var cancelar = new PedirTextoDialog("Anular documento", "Ingresá el motivo de la anulación:");
        var sinTexto = Abrir<string?>(cancelar);
        Clic(cancelar, "CancelarButton");
        Assert.Null(sinTexto.Result);
    }

    [AvaloniaFact]
    public void Mensaje_AceptarCierra()
    {
        var dialogo = new MensajeDialog("Aviso");
        var duenio = new Window();
        duenio.Show();
        var cierre = dialogo.ShowDialog(duenio);
        Dispatcher.UIThread.RunJobs();

        Clic(dialogo, "AceptarButton");

        Assert.True(cierre.IsCompleted);
    }

    [AvaloniaFact]
    public void LosTres_TienenUi2EnElBorderRaizYUnSoloPrimario()
    {
        foreach (Window dialogo in new Window[] { new ConfirmacionDialog("x"), new MensajeDialog("x"), new PedirTextoDialog("t", "x") })
        {
            var contenido = Assert.IsType<Border>(dialogo.Content);
            Assert.Equal("Contenido", contenido.Name);
            Assert.Contains("ui2", contenido.Classes);
            var primarios = new[] { "ConfirmarButton", "AceptarButton", "CancelarButton" }
                .Select(n => dialogo.FindControl<Button>(n))
                .Count(b => b is not null && b.Classes.Contains("primario"));
            Assert.Equal(1, primarios);
        }
    }
}
```

(Agregar `using System.Linq;` y `using System.Threading.Tasks;` si el proyecto no tiene `ImplicitUsings`.)

Run: filtros `DialogosV2Tests` (UiTests) y `DialogosComparacionTests` (VisualTests).
Expected: FAIL en `LosTres_TienenUi2EnElBorderRaizYUnSoloPrimario` (sin nombre ni `ui2`; clases `primary`) y en las tres comparaciones. Los de resultado pasan ya con el XAML viejo: son el guardián de que el Step 2 no rompa el flujo.

- [ ] **Step 2: El XAML**

`ConfirmacionDialog.axaml`: conservar la raíz `Window` con todos sus atributos actuales (Title, Width, SizeToContent, WindowStartupLocation, CanResize, ShowInTaskbar) y agregar `Background="{DynamicResource V2SuperficieBrush}"`. Reemplazar el contenido por:

```xml
    <!-- UI v2 (spec §5.5). ui2 en el Border raíz y no en el Window: así la comparación visual puede
         extraer el contenido (ContenidoDeDialogo). Sin bindings: el code-behind lee los x:Name. -->
    <Border x:Name="Contenido"
            Classes="ui2"
            Background="{DynamicResource V2SuperficieBrush}"
            Padding="24">
        <StackPanel Spacing="24">

            <TextBlock x:Name="MensajeText" Classes="cuerpo" TextWrapping="Wrap" />

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
                <Button x:Name="CancelarButton" Classes="secundario" Content="Cancelar" Click="OnCancelarClick" />
                <Button x:Name="ConfirmarButton" Classes="primario" Content="Sí, continuar" Click="OnConfirmarClick" />
            </StackPanel>

        </StackPanel>
    </Border>
```

`MensajeDialog.axaml`: igual (raíz `Window` intacta + `Background`), contenido:

```xml
    <!-- UI v2 (spec §5.5). Ver ConfirmacionDialog.axaml. -->
    <Border x:Name="Contenido"
            Classes="ui2"
            Background="{DynamicResource V2SuperficieBrush}"
            Padding="24">
        <StackPanel Spacing="24">

            <TextBlock x:Name="MensajeText" Classes="cuerpo" TextWrapping="Wrap" />

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
                <Button x:Name="AceptarButton" Classes="primario" Content="Aceptar" Click="OnAceptarClick" />
            </StackPanel>

        </StackPanel>
    </Border>
```

`PedirTextoDialog.axaml`: igual (raíz `Window` intacta + `Background`), contenido:

```xml
    <!-- UI v2 (spec §5.5). Ver ConfirmacionDialog.axaml. -->
    <Border x:Name="Contenido"
            Classes="ui2"
            Background="{DynamicResource V2SuperficieBrush}"
            Padding="24">
        <StackPanel Spacing="16">

            <TextBlock x:Name="MensajeText" Classes="cuerpo" TextWrapping="Wrap" />

            <TextBox x:Name="TextoTextBox"
                     AcceptsReturn="True"
                     TextWrapping="Wrap"
                     Height="80"
                     PlaceholderText="Escriba el motivo" />

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8" Margin="0,8,0,0">
                <Button x:Name="CancelarButton" Classes="secundario" Content="Cancelar" Click="OnCancelarClick" />
                <Button x:Name="AceptarButton" Classes="primario" Content="Aceptar" Click="OnAceptarClick" />
            </StackPanel>

        </StackPanel>
    </Border>
```

(`Watermark` pasa a `PlaceholderText`, el nombre vigente en Avalonia 12; mismo texto.)

- [ ] **Step 3: Verde**

Run: `dotnet build StockApp.sln`, `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj` (incluido `ConfirmacionServiceDialogosConsecutivosTests`) y `scripts/ui-v2/comparar.sh Dialogo`.
Expected: PASS; las tres pantallas de diálogos APROBADAS.

- [ ] **Step 4: Mutación quitando**

```bash
cp src/StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml "$SCRATCH/PedirTextoDialog.axaml.bak"
```

Quitar `Click="OnAceptarClick"` del `AceptarButton` de PedirTexto → rojo en `PedirTexto_AceptarDevuelveElTexto_CancelarDevuelveNull` (la tarea no completa). Restaurar con `cp`.

- [ ] **Step 5: Documentación**

`docs/ui-v2/herramientas.md`, sección "Test por pantalla", agregar al final:

```markdown
Diálogos (`Window`): el contenido va en un `Border x:Name="Contenido" Classes="ui2"` (no en el
`Window`), y el test compara su área cliente con `ContenidoDeDialogo.Extraer(dialogo)`:

    ComparacionVisual.CompararConMockup(ContenidoDeDialogo.Extraer(new ConfirmacionDialog("…")), null, "dialogos/confirmacion.html");

El mockup es el área cliente (sin barra de título) con el ancho del `Window`, centrada sobre fondo app.
```

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Views/Dialogs/ConfirmacionDialog.axaml src/StockApp.Presentation/Views/Dialogs/MensajeDialog.axaml src/StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml tests/StockApp.Presentation.VisualTests/Comparacion/ContenidoDeDialogo.cs tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/ tests/StockApp.Presentation.UiTests/V2/DialogosV2Tests.cs docs/ui-v2/herramientas.md
git commit -m "feat(ui-v2): diálogos de confirmación, mensaje y texto v2, comparados contra el mockup"
```

### Task 16: Overlays de actualización v2 (banner, modal, bloqueo, modo degradado)

**Depende de:** Task 9 (mockups aprobados) y Task 12 (`VistasMigradasAV2`).

**Files:**
- Modify: `src/StockApp.Presentation/Actualizaciones/Views/ActualizacionBannerView.axaml`, `ActualizacionModalView.axaml`, `ActualizacionBloqueoView.axaml` (code-behind sin cambios)
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Actualizaciones/DatosActualizaciones.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Actualizaciones/ActualizacionesComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`

**Interfaces:**
- Consumes: `ActualizacionBannerViewModel(AccionUx)` (`Titulo`, `EsPosponible`, `OperacionEnCurso`, `PosponerCommand`, `ActualizarAlReiniciarCommand`), `ActualizacionModalViewModel(AccionUx)` (`TextoMarkdown`, `EsPosponible`, `PosponerCommand`, `ActualizarAhoraCommand`), `ActualizacionBloqueoViewModel(AccionUx)` (`TextoMarkdown`, `EsModoDegradado`, `AplicarYReiniciarCommand`); `AccionUx(ModoUx Modo, string? TextoMarkdown, bool Posponible, bool ReintentaEnArranque, string? Version = null)`.
- Produces: `DatosActualizaciones.Banner()`, `.Modal()`, `.Bloqueo()`, `.ModoDegradado()`; partes nombradas `TarjetaBloqueo` y `BannerDegradado` en `ActualizacionBloqueoView`.

- [ ] **Step 1: Datos y comparación (rojo)**

`DatosActualizaciones.cs`:

```csharp
using StockApp.Application.Actualizaciones;
using StockApp.Presentation.Actualizaciones;

namespace StockApp.Presentation.VisualTests.Pantallas.Actualizaciones;

/// <summary>Datos fijos de docs/ui-v2/mockups/actualizaciones/*.html.</summary>
public static class DatosActualizaciones
{
    private const string TextoCritico = "Esta versión corrige un problema de seguridad. La app se va a reiniciar para aplicarla.";

    public static ActualizacionBannerViewModel Banner() =>
        new(new AccionUx(ModoUx.BannerDiscreto, "Mejoras en reportes.", true, false, "0.5.0"));

    public static ActualizacionModalViewModel Modal() =>
        new(new AccionUx(ModoUx.ModalPosponible,
            "Esta versión corrige el cálculo del Libro caja y mejora la exportación a PDF.", true, false, "0.5.0"));

    public static ActualizacionBloqueoViewModel Bloqueo() =>
        new(new AccionUx(ModoUx.BloqueoCritico, TextoCritico, false, true, "0.5.0"));

    public static ActualizacionBloqueoViewModel ModoDegradado() =>
        new(new AccionUx(ModoUx.ModoDegradado, TextoCritico, false, true, "0.5.0"));
}
```

Los VM de modal y bloqueo pasan el texto por `FormateadorNotasActualizacion.Limpiar`: verificar en `src/StockApp.Application/Actualizaciones/FormateadorNotasActualizacion.cs` que un texto plano de una línea sale igual; si lo altera, ajustar el texto del mockup a lo que devuelve `Limpiar` (avisando al usuario: es dato de ejemplo, no copy).

`ActualizacionesComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Actualizaciones.Views;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests.Pantallas.Actualizaciones;

[Trait("Categoria", "ComparacionVisual")]
public class ActualizacionesComparacionTests
{
    [AvaloniaFact]
    public void Banner_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ActualizacionBannerView(), DatosActualizaciones.Banner(), "actualizaciones/banner.html");

    [AvaloniaFact]
    public void Modal_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ActualizacionModalView(), DatosActualizaciones.Modal(), "actualizaciones/modal.html");

    [AvaloniaFact]
    public void Bloqueo_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ActualizacionBloqueoView(), DatosActualizaciones.Bloqueo(), "actualizaciones/bloqueo.html");

    [AvaloniaFact]
    public void ModoDegradado_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ActualizacionBloqueoView(), DatosActualizaciones.ModoDegradado(), "actualizaciones/modo-degradado.html");
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~ActualizacionesComparacionTests"`
Expected: FAIL en las cuatro (aspecto viejo).

- [ ] **Step 2: El XAML**

`ActualizacionBannerView.axaml` (agregar `Classes="ui2"` a la raíz; mismo `x:DataType`):

```xml
    <!-- UI v2 (spec §5.5). Franja superior discreta (severity normal), no bloquea. Espejo:
         docs/ui-v2/mockups/actualizaciones/banner.html. -->
    <Border Background="{DynamicResource V2AlertaInfoFondoBrush}"
            BorderBrush="{DynamicResource V2AlertaInfoBordeBrush}"
            BorderThickness="0,0,0,1"
            Padding="24,0"
            Height="56"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Top">
        <Grid ColumnDefinitions="*,Auto">
            <TextBlock Grid.Column="0"
                       Text="{Binding Titulo}"
                       VerticalAlignment="Center"
                       FontSize="{DynamicResource V2FuenteSubtituloFormularioTamano}"
                       FontWeight="{DynamicResource V2FuenteSubtituloFormularioPeso}"
                       Foreground="{DynamicResource V2AlertaInfoTextoBrush}" />
            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                <Button Classes="ghost" Content="Más tarde" Command="{Binding PosponerCommand}" IsVisible="{Binding EsPosponible}" />
                <Button Classes="primario" Content="Actualizar ahora" Command="{Binding ActualizarAlReiniciarCommand}"
                        IsEnabled="{Binding !OperacionEnCurso}" />
            </StackPanel>
        </Grid>
    </Border>
```

`ActualizacionModalView.axaml` (raíz con `Classes="ui2"` y `xmlns:v2="using:StockApp.Presentation.Controls.V2"`):

```xml
    <!-- UI v2 (spec §5.5). Modal posponible (severity important), app usable detrás. Espejo:
         docs/ui-v2/mockups/actualizaciones/modal.html. -->
    <v2:TarjetaContenido Width="480" Padding="24" HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel Spacing="16">
            <TextBlock Classes="titulo-seccion" Text="Actualización disponible" />
            <TextBlock Classes="texto-secundario" Text="{Binding TextoMarkdown}" TextWrapping="Wrap" />
            <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                <Button Classes="secundario" Content="Posponer" Command="{Binding PosponerCommand}" IsVisible="{Binding EsPosponible}" />
                <Button Classes="primario" Content="Actualizar ahora" Command="{Binding ActualizarAhoraCommand}" />
            </StackPanel>
        </StackPanel>
    </v2:TarjetaContenido>
```

`ActualizacionBloqueoView.axaml` (raíz con `Classes="ui2"` y `xmlns:v2`):

```xml
    <!-- UI v2 (spec §5.5). EsModoDegradado=false: bloqueo crítico (sin posponer).
         EsModoDegradado=true: franja de error permanente. Espejos: actualizaciones/bloqueo.html y
         actualizaciones/modo-degradado.html. Textos reales sin tilde, sin cambios. -->
    <Grid>

        <v2:TarjetaContenido x:Name="TarjetaBloqueo"
                             IsVisible="{Binding !EsModoDegradado}"
                             Width="480"
                             Padding="24"
                             HorizontalAlignment="Center"
                             VerticalAlignment="Center">
            <StackPanel Spacing="16">
                <TextBlock Classes="titulo-seccion" Text="Actualizacion critica requerida"
                           Foreground="{DynamicResource V2PeligroTextoBrush}" />
                <TextBlock Classes="texto-secundario" Text="{Binding TextoMarkdown}" TextWrapping="Wrap" />
                <Button Classes="primario" Content="Aplicar y reiniciar" Command="{Binding AplicarYReiniciarCommand}"
                        HorizontalAlignment="Right" />
            </StackPanel>
        </v2:TarjetaContenido>

        <Border x:Name="BannerDegradado"
                IsVisible="{Binding EsModoDegradado}"
                Background="{DynamicResource V2AlertaErrorFondoBrush}"
                BorderBrush="{DynamicResource V2AlertaErrorBordeBrush}"
                BorderThickness="0,0,0,1"
                Padding="24,12"
                VerticalAlignment="Top">
            <TextBlock Text="MODO DEGRADADO — Actualizacion critica no descargada. Reintentara al reiniciar."
                       TextWrapping="Wrap"
                       FontSize="{DynamicResource V2FuenteSubtituloFormularioTamano}"
                       FontWeight="{DynamicResource V2FuenteSubtituloFormularioPeso}"
                       Foreground="{DynamicResource V2AlertaErrorTextoBrush}" />
        </Border>

    </Grid>
```

- [ ] **Step 3: `GuardianDePatronTests`**

Sacar `typeof(ActualizacionBannerView)`, `typeof(ActualizacionModalView)` y `typeof(ActualizacionBloqueoView)` de `VistasEmbebidas` y agregarlos a `VistasMigradasAV2`.

- [ ] **Step 4: Verde**

Run: `dotnet build StockApp.sln`, `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`, `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~Actualizacion"` y `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj` (incluidos `ViewLocatorTests` y el test de alfa de `RenderizadorVistaTests`, que montan `ActualizacionBannerView`).
Expected: PASS; luego `scripts/ui-v2/comparar.sh Actualizacion` con las cuatro pantallas APROBADAS.

- [ ] **Step 5: Mutación quitando**

```bash
cp src/StockApp.Presentation/Actualizaciones/Views/ActualizacionBloqueoView.axaml "$SCRATCH/ActualizacionBloqueoView.axaml.bak"
```

Quitar `IsVisible="{Binding !EsModoDegradado}"` de `TarjetaBloqueo` → rojo en `ModoDegradado_IgualAlMockup` (aparece la tarjeta). Restaurar con `cp`.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Actualizaciones/Views/ tests/StockApp.Presentation.VisualTests/Pantallas/Actualizaciones/ tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs
git commit -m "feat(ui-v2): overlays de actualización v2 comparados contra el mockup"
```

---

## Bloque D — Cierre de la fase

### Task 17: Suite completa, comparación de toda la fase, verificación orgánica y aprobación final

**Files:**
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (línea "Estado" del encabezado)
- Modify: `.superpowers/sdd/2026-10-01-ui-v2-fase-1-shell-y-acceso/progress.md` (resultado de la verificación)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 1 cerrada en `feat/ui-v2` (sin merge a `main`: se mergea una sola vez al final de todas las fases).

- [ ] **Step 1: Build y suite completa**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
```

Expected: 0 errores de build; todos los tests verdes salvo los falsos conocidos de fixtures `.ods` en `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` (confirmar que los rojos son exactamente esos, por nombre). Anotar el total en el reporte.

- [ ] **Step 2: Herramientas**

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
```

Expected: ambos con exit 0 (esta fase no cambia `tokens.json`).

- [ ] **Step 3: Comparación de toda la fase**

```bash
scripts/ui-v2/comparar.sh
```

Expected: TODAS las filas de `artifacts/ui-v2/comparacion/index.html` APROBADAS (catálogo de la Fase 0 + 2 shell + 5 acceso + 3 diálogos + 4 actualizaciones = 15 pantallas), ninguna con `UmbralAprobado` salvo las que el usuario haya aprobado explícitamente por el procedimiento de spec §6.5.

- [ ] **Step 4: Verificación orgánica (spec §6.4) — app real contra la base**

Preparación (memoria del proyecto y ledger de la Fase 0):
- `docker start stockapp-pg` (y dejarlo andando).
- `~/.config/GestionMunicipal/conexion.json` apuntando a `localhost` (en la Fase 0 apuntaba al VPS de testing y el login fallaba sin relación con la UI).
- Credencial de desarrollo: `admin` / `Admin1234`.
- WSLg tiene que estar levantado (en la Fase 0 la sesión RDP de WSLg se cayó y bloqueó el cierre). Herramientas: `scripts/gui-verificacion/` (xdotool sin root; capturas con PowerShell, la ventana se llama "Gestión Municipal").

Recorrido, capturando cada pantalla:
1. Login: campos vacíos ("Entrar" deshabilitado), credencial equivocada (alerta de error), Enter en Contraseña con la credencial correcta (entra). Con el logo de Carmelo semitransparente detrás de la card (P5: la comparación de píxeles no lo ve, acá lo mira el usuario) y la línea "Municipio de Carmelo" bajo el título (P6); en el sidebar, la misma línea bajo "Gestión Municipal".
2. Sidebar como Administrador: las 5 secciones; navegar a CADA ítem visible y ver que la pastilla activa acompaña; que la navegación scrollea hasta "Mantenimiento"; que las vistas de contenido (no migradas) se ven IGUAL que antes. Para esto último, comparar Productos e Inicio contra las capturas "antes" de la Fase 0 con `dotnet run --project tools/StockApp.UiV2.Herramientas -- comparar-png <antes.png> <despues.png> <diff.png>` sobre el área de contenido (la columna de la derecha cambia de ancho de 232 a 240: recortar ambas capturas a la misma región de contenido antes de comparar).
3. Crear y editar un producto desde el shell nuevo (pantallas viejas hospedadas: confirma que el `ContentControl` y la navegación no cambiaron).
4. Usuario con permisos mixtos: con Administración → Usuarios, crear (o reutilizar) un Operador con `GestionarProductos`, `RegistrarMovimientos`, `VerFinanzas` y `GestionarTareas`; entrar con él y ver solo INVENTARIO (sin "Ingreso por factura"), FINANZAS y GESTIÓN (sin "Documentos").
5. Revocación en caliente: con el Operador logueado, desde otra sesión de Administrador sacarle `GestionarTareas`; en el Operador navegar a otra pantalla y ver desaparecer "Tareas" y el header "GESTIÓN".
6. Cerrar sesión desde el pie: aparece `ConfirmacionDialog` v2 ("¿Cerrar la sesión?"); Cancelar no cierra; "Sí, continuar" vuelve al Login.
7. `PedirTextoDialog`: en Documentos, anular un documento (o el flujo que lo dispare) y ver el diálogo v2. `MensajeDialog`: provocar un error amigable (por ejemplo, dar de baja una categoría con productos).
8. Reset: desde el Login, "No puedo entrar / resetear Admin" → pantalla v2 → "Volver al login".
9. BloqueoLicencia y overlays de actualización: no se pueden provocar sin desactivar la licencia del servidor ni publicar una versión; quedan cubiertos por su comparación (Step 3). Anotarlo así en el reporte (no es un hueco silencioso).

Si algún paso no se puede hacer por el entorno (WSLg caído, contenedor que no arranca), PARAR y avisar al usuario con la evidencia (stderr, `docker ps -a`), sin declarar la fase cerrada.

- [ ] **Step 5: Aprobación final del usuario**

PARAR. Presentar al usuario: el `index.html` de comparación, las capturas de la verificación orgánica y la lista de cambios respecto de los mockups aprobados (si hubo, por ejemplo la barra de scroll del Step 6 de la Task 11). La aprobación final es del usuario mirando la app (spec §6.4). Registrar en `progress.md`: `Fase 1: aprobada por <quién> <fecha>`.

- [ ] **Step 6: Estado de la spec y commit**

En el encabezado de la spec, cambiar la línea `**Estado:**` a: `**Estado:** aprobado por el usuario; Fase 0 y Fase 1 implementadas en feat/ui-v2 (merge a main al final de todas las fases)`.

```bash
git add docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md .superpowers/sdd/2026-10-01-ui-v2-fase-1-shell-y-acceso/progress.md
git commit -m "docs(ui-v2): cierre de la Fase 1 (shell y acceso)"
```

No se pushea ni se mergea.

---

## Dónde quedó cada punto diferido de la Fase 0

| Punto (ledger Fase 0) | Task |
|---|---|
| Guardián: valor recortado en `TextBox`/`NumericUpDown`/`CalendarDatePicker` (`TextPresenter`), `Inlines` con `Text` nulo, `MaxLines` con wrap — ANTES del primer formulario | Task 1 (el primer formulario es el Login, Task 12) |
| Relajamiento de eje vigente en ancestros externos | Task 2 |
| Borrar `DataGridV2CatalogoTests.Headers_TextoCompletoSinRecorte` | Task 2, Step 5 (con la mutación que prueba que el guardián lo cubre) |
| Guardián de la convención `.v2-num > span` | Task 3 |
| Breadcrumb de `EncabezadoPagina` que no recorta texto | Task 4 |

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| El sidebar de Administrador desborda 760 y la barra de scroll de Fluent puede dibujarse distinta que en el mockup. | P4 (barra `Auto`, decidido) + Task 11 Step 6 (medir la franja y replicarla en el mockup). |
| La card centrada dentro de un `ScrollViewer` puede quedar arriba en vez de centrada. | Task 12 Step 6 trae la corrección (Grid con `MinHeight` del viewport). |
| Texto centrado verticalmente en alturas impares (ítem de 32 con línea de 15) puede caer en medio píxel distinto entre Skia y Chromium. | Es el mismo ruido de centrado subpíxel que la calibración ya absorbe (spec §6.5); si suma teselas densas, se iguala con un margen entero en ambos lados, no con umbral. |
| Mockups aprobados que luego no se pueden igualar en Avalonia (p. ej. la barra de scroll). | Todo cambio de un mockup aprobado vuelve al usuario antes del commit (Task 11 Step 6, Task 17 Step 5). |
| Fuga de estilos `.ui2` a vistas viejas a través del shell o de AccesoLimitado. | Review Focus 1; tests con mutación en Tasks 11 y 14; verificación orgánica Task 17 Step 4.2. |
| `GuardianDePatronTests` queda con listas vacías al migrar (xUnit falla `MemberData` sin filas). | Tasks 13 y 16 borran la lista y su test cuando se vacía. |
| `FontFeatures` en `SelectableTextBlock` puede no existir como propiedad directa. | Task 13 Step 2 da la alternativa `TextElement.FontFeatures`. |
| El guardián más estricto (Tasks 1-2) puede hacer fallar el catálogo de la Fase 0. | Tasks 1-4 corren `Catalogo_IgualAlMockup`; si falla por un recorte real, se arregla el catálogo y su mockup, no el guardián. |
| `.axaml` stale en `obj/` durante mutaciones (pasó en la Fase 0, Task 16). | Global Constraints: `dotnet build-server shutdown` + `rm -rf` de `obj/bin`. |
| WSLg caído o `conexion.json` apuntando al VPS bloquean la verificación orgánica (pasó en la Fase 0). | Task 17 Step 4: preparación explícita y PARAR con evidencia si el entorno falla. |
| Los VM transitorios de P1 se reescriben en las Fases 6 y 7. | Aceptado por el usuario (P1): es el costo de no tener ítems que no navegan. |
| La marca de agua del Login queda fuera de la comparación de píxeles (P5). | `LoginMarcaAguaTests` (Task 12 Step 3) custodia opacidad, escala, región y la paridad de valores con el mockup; el usuario la ve en la aprobación de mockups (Task 7) y en la verificación orgánica (Task 17 Step 4). No se compensa con umbral (spec §6.5). |
