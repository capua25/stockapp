# StockApp — UI v2: rediseño total de la interfaz

**Fecha:** 2026-09-28
**Estado:** aprobado por el usuario, pendiente de plan de implementación (Fase 0)
**Rama:** `feat/ui-v2` (worktree propio)
**Alcance:** todas las vistas de `src/StockApp.Presentation/` (shell, ~55 de contenido, pantallas fuera del shell, 4 diálogos, 3 overlays de actualización)
**Mockups de referencia:** `docs/ui-v2/mockups/referencia/` — `direccion-visual-aprobada.html` (variante `.di-azul`), `navegacion-aprobada.html` (opción A), `patrones-1.html`

## 1. Contexto y problema

El refactor visual de agosto de 2026 (`feat/ui-refactor-dashboard`, spec `2026-08-18-ui-refactor-dashboard-design.md`, 55 vistas, patrones P0-P9) ordenó la estructura pero no dio una dirección estética: la app terminada **no quedó igual a los mockups**. El usuario marca cuatro problemas:

1. Navegación y estructura.
2. Inconsistencia entre pantallas.
3. Estética general.
4. Densidad y espaciado.

**Objetivo:** una interfaz nueva desde cero que conserve **todas** las características actuales, con **fidelidad visual 1:1 al mockup aprobado** como requisito central. Regla de cierre: **si no queda igual al mockup, no está terminado.**

## 2. Dirección visual

Intermedio entre "institucional sobrio" y "cálido aireado", con acento azul. Reemplaza el verde `#16A34A` y el sidebar oscuro `#1E293B` actuales. **Tema claro fijo**, sin modo oscuro.

### 2.1 Color

| Token | Valor | Uso |
|---|---|---|
| Fondo app | `#F7F7F5` | fondo de ventana y de pantallas fuera del shell |
| Superficie | `#FFFFFF` | cards, sidebar, inputs |
| Borde | `#E7E5E4` | cards, sidebar, separadores estructurales |
| Borde input | `#D6D3D1` | inputs, combos, botón secundario |
| Separador de grilla | `#F0EFED` | líneas entre filas y bajo el header |
| Header de grilla | `#FAFAF9` | fondo del header de columnas |
| Acento | `#1D4ED8` | botón primario, links, ícono del ítem activo |
| Acento hover | `#1E40AF` | hover del primario |
| Acento suave | `#DBEAFE` | pastilla del ítem activo del sidebar |
| Acento oscuro | `#1E3A8A` | texto del ítem activo |
| Texto primario | `#1C1917` | títulos, valores |
| Texto secundario | `#57534E` | subtítulos, fecha, textos de apoyo |
| Texto terciario | `#78716C` | breadcrumb, pie de conteo, header de columna, descripciones |
| Texto etiqueta | `#44403C` | etiquetas de campo, texto de celda, texto de input |
| Texto grupo | `#A8A29E` | headers de sección del sidebar, versión, KPI sin dato |
| Peligro texto | `#B91C1C` | número en rojo (stock negativo), texto destructivo |
| Borde error | `#DC2626` | borde de input inválido y mensaje de error del campo |

Insignias (pill, fondo/texto):

| Variante | Fondo | Texto |
|---|---|---|
| Neutro | `#F5F5F4` | `#44403C` |
| Advertencia | `#FEF3C7` | `#92400E` |
| Peligro | `#FEE2E2` | `#991B1B` |
| Éxito | `#DCFCE7` | `#14532D` |

El valor de texto de Éxito se toma de los mockups (`#14532D`), no del `#166534` del brief: en `direccion-intermedia.html` `#166534` es el hover del primario de la variante verde descartada.

Alertas (fondo / borde / texto):

| Variante | Fondo | Borde | Texto |
|---|---|---|---|
| Advertencia | `#FEF3C7` | `#FDE68A` | `#92400E` |
| Error | `#FEE2E2` | `#FCA5A5` | `#991B1B` |
| Info | `#DBEAFE` | `#DBEAFE` | `#1E3A8A` |

Advertencia y Error salen de `patrones-1.html`. Info no aparece en los mockups de referencia: se arma con la familia de acento ya aprobada (suave como fondo y borde, oscuro como texto) y se valida en la pantalla catálogo-de-componentes de la Fase 0.

### 2.2 Forma, sombra y medidas

- **Radios:** 8 (inputs, botones, pastilla activa, alertas), 10 (cards), 999 (insignias).
- **Sombra de card:** `0 1px 2px rgba(0,0,0,0.05)` — la única sombra del sistema.
- **Alturas:** input y botón 36; fila de grilla 40; header de grilla 36; ítem de sidebar 32.
- **Espaciado:** base 4; todo margen, padding y gap es múltiplo de 4. Única excepción: el padding vertical de 2 de la `Insignia` (valor del mockup aprobado).
- **Anchos fijos:** sidebar 240; card de formulario máx. 760; card de login 400.

### 2.3 Tipografía

Inter, ya embebida vía `Avalonia.Fonts.Inter`. Cifras tabulares y alineadas a la derecha en toda columna numérica.

| Rol | Tamaño / peso | Color |
|---|---|---|
| Título de página | 22 / 600, alto de línea 26 | primario |
| Título de sección (card, panel, detalle de catálogo) | 15 / 600 | primario |
| Marca en sidebar | 15 / 700 | primario |
| Valor de TarjetaMetrica | 20 / 600 | primario |
| Título de login | 20 / 600 | primario |
| Cuerpo (texto corrido) | 14 / 400 | primario o secundario |
| Texto de controles (input, botón, celda, subtítulo, breadcrumb, alerta) | 13 | según el control |
| Subtítulo de sección dentro de formulario | 13 / 600 | primario |
| Etiqueta de campo | 12 / 500 | etiqueta |
| Ítem de sidebar | 12 / 500 | etiqueta; activo en acento oscuro |
| Insignia | 12 / 500, padding 2×8 | según variante |
| Mensaje de error de campo | 12 | borde error |
| Header de columna | 11 / 600, MAYÚSCULAS, letter-spacing 0.6 | terciario, sobre `#FAFAF9` |
| Header de sección del sidebar | 11 / 600, MAYÚSCULAS, letter-spacing 0.6 | grupo |

**Grillas sin zebra**: filas blancas separadas por `#F0EFED`.

**Precedencia:** los valores de esta sección son los tokens canónicos. Los mockups de referencia se aprobaron como dirección y pueden diferir en detalles (letter-spacing en `em`, tamaños puntuales); los mockups por pantalla de cada fase se construyen con el CSS generado de estos tokens, así que ahí no hay conflicto posible.

## 3. Enfoque: reescritura en el lugar por módulos, mockups como contrato

- Rama `feat/ui-v2` en worktree propio.
- **Se reescribe el XAML de cada vista en su mismo archivo.** Los ViewModels quedan intactos salvo lo mínimo: hub de Reportes, Catálogos, KPIs de Inicio y datos del usuario en el pie del sidebar.
- **No cambian:** `ViewLocator`, `INavigationService`, el wiring de `DataContextChanged` en code-behind (44 vistas), el seam de validación `ErrorConverter`, la lógica de permisos.
- Los compiled bindings (`x:DataType`) son la red del compilador: un binding roto en la reescritura es error de build, no un null silencioso.

### 3.1 Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| Solo tema (restilar sin tocar vistas) | Ya se intentó en agosto y falló; no arregla navegación ni estructura. |
| Vistas paralelas con feature flag | Choca con `ViewLocator`, que resuelve por convención de nombre; duplica 69 vistas. |

### 3.2 Tokens como fuente única

Un archivo de tokens, `docs/ui-v2/tokens.json`, del que se **generan** dos salidas:

- `Themes/Tokens.axaml` (recursos de Avalonia).
- El CSS que usan los mockups por pantalla.

Nunca se mantienen dos copias a mano. Un cambio de token se hace en el JSON y se regeneran ambas salidas.

### 3.3 Componentes

Componentes nuevos en `Controls/`, con sus templates en un `.axaml` de componentes:

| Componente | Responsabilidad |
|---|---|
| `EncabezadoPagina` | título, subtítulo, breadcrumb opcional, slot de acciones a la derecha |
| `TarjetaContenido` | card blanca, borde, radio 10, sombra única |
| `BarraFiltros` | buscador y filtros de un listado |
| Tema de `DataGrid` | reescrito completo: header, filas de 40, separadores, sin zebra, numéricos a la derecha |
| `CampoFormulario` | etiqueta arriba, asterisco si es requerido, error debajo. **No intercepta `ErrorConverter`**: muestra lo que el seam ya produce. |
| `Insignia` | pill con variantes neutro/advertencia/peligro/éxito; reemplaza `BadgeEstado` |
| `Alerta` | info / advertencia / error |
| `TarjetaMetrica` | KPI de Inicio: valor, etiqueta, estado sin dato |
| `TarjetaAcceso` | acceso rápido con ícono (Inicio) y tarjeta del hub de Reportes |
| `EstadoVacio` | rediseñado |

Estilos de controles base: `Button` con variantes **primario** (uno solo por vista), **secundario**, **ghost** y **peligro**; inputs (TextBox, ComboBox, DatePicker, NumericUpDown, CheckBox) a 36 de alto con borde input y radio 8.

## 4. Shell y navegación

Opción A aprobada: sidebar plano reorganizado más consolidación de pantallas.

### 4.1 Sidebar

240 de ancho, fondo blanco con borde derecho. Secciones **siempre visibles, sin acordeones**. Ítem activo en pastilla (acento suave, texto acento oscuro).

- **Inicio**
- **INVENTARIO:** Productos · Registrar entrada · Registrar salida · Ingreso por factura · Historial de movimientos
- **FINANZAS:** Gastos y facturas · Ingresos de caja · Libro caja · Control POA · Calendario de pagos
- **GESTIÓN:** Tareas · Documentos
- **ANÁLISIS:** Reportes
- **ADMINISTRACIÓN:** Catálogos · Usuarios · Mantenimiento

Reglas:

- Cada ítem conserva su gate actual (`Puede*` / `EsAdmin`), incluidos los AND compuestos y los ítems estructuralmente Admin-only.
- El header de una sección se oculta si no tiene ningún ítem visible.
- La revocación de permisos en caliente se mantiene: al perder un permiso el ítem desaparece sin reiniciar.
- **Importación:** sigue apagada con `ImportacionHabilitada=false`; el ítem existe pero queda oculto.

**Pie del sidebar:** avatar con iniciales, nombre y rol del usuario (nuevo: hoy no se muestra), cerrar sesión (con la confirmación actual) y versión.

### 4.2 Hub de Reportes (pantalla nueva)

Seis tarjetas: Valorización de inventario, Stock por categoría, Historial por producto, Productos más movidos, Log de auditoría, Estadística de tareas.

- Cada tarjeta conserva el gate actual de su reporte; **Historial por producto exige además `RegistrarMovimientos`**.
- El ítem "Reportes" del sidebar es visible si hay al menos una tarjeta visible.

### 4.3 Catálogos (pantalla nueva, master-detail)

Lista izquierda agrupada, detalle a la derecha:

| Grupo | Catálogos | Gate | Origen actual |
|---|---|---|---|
| INVENTARIO | Categorías, Proveedores, Unidades de medida | `GestionarTablasMaestras` | ítems propios del sidebar |
| FINANZAS | Fuentes de financiamiento, Rubros de gasto, Líneas POA | `GestionarMaestrosFinanzas` | "Maestros de finanzas" |
| TAREAS | Zonas, Dimensiones, Organismos responsables, Orígenes de financiamiento | `GestionarTablasMaestras` | "Catálogos de tareas" |

- Cada grupo se muestra solo con su gate, que es el de la pantalla de la que sale (verificado en `ShellMainViewModel`: "Catálogos de tareas" usa `PuedeGestionarTablasMaestras`, "Maestros de finanzas" usa `PuedeGestionarMaestrosFinanzas`).
- Se eliminan las pestañas anidadas actuales.
- Los listados de catálogo ganan buscador y columna de estado con `Insignia`.
- El ítem "Catálogos" del sidebar es visible si hay al menos un grupo visible.

### 4.4 Breadcrumb y navegación

- Altas y ediciones muestran breadcrumb en `EncabezadoPagina`.
- El primer tramo del breadcrumb dispara el evento `Volver` que el VM ya expone. **No se agrega back-stack.**

### 4.5 Sin cambios

Overlay de actualizaciones (se rediseña visualmente, no cambia su lógica), flujo de licencia / reset / acceso limitado, persistencia de geometría de ventana.

## 5. Patrones de pantalla

Ver `docs/ui-v2/mockups/referencia/`. El patrón de listado está en `direccion-visual-aprobada.html`; formulario, Catálogos, Inicio y Login en `patrones-1.html`.

### 5.1 Listado

`EncabezadoPagina` + `TarjetaContenido` con `BarraFiltros`, grilla y pie de conteo.

**Productos** conserva sus columnas actuales (Código, Nombre, Costo, Stock, Unidad, Categoría, Estado) y su tratamiento de stock negativo: `Insignia` peligro "Stock negativo" y el número en `#B91C1C`.

**No se implementa "stock bajo".** Decisión explícita del usuario (2026-09-28): `StockMinimo` existe en el dominio pero no se usa ni se expone.

### 5.2 Formulario

- Breadcrumb.
- Card de ancho máximo 760 con secciones tituladas.
- Grilla de 2 columnas donde los campos lo permitan (campos cortos y emparentados); campos largos a ancho completo.
- Cada campo es un `CampoFormulario`.
- Un solo botón primario; botones al pie, alineados a la derecha.
- Los textos reales se conservan (el botón dice "Guardar", no otra cosa).

### 5.3 Inicio (dashboard)

- Saludo y fecha.
- Fila de 3 `TarjetaMetrica`, cada una visible solo con su gate actual:

| Métrica | Fuente | Gate |
|---|---|---|
| Facturas a vencer en 7 días | `CalendarioPagosDto.AVencer7Dias` vía `IFinanzasVistasService` | `VerFinanzas` o Admin |
| Tareas vencidas | `ITareaService` + `PanelVencimientosTareas` | `GestionarTareas` o Admin |
| Último backup | `IBackupsService.ObtenerSaludAsync` | solo Admin |

- Panel "Tareas que requieren atención" con `Insignia` Vencida / Próxima (reemplaza las viñetas ●/○).
- Accesos rápidos: los 6 actuales, con sus gates, como `TarjetaAcceso` con ícono.
- Avisos de backup como `Alerta`, **manteniendo la distinción actual** entre salud desconocida y backup vencido.

### 5.4 Login y pantallas fuera del shell

Login, Reset, BloqueoLicencia y AccesoLimitado: card centrada de 400 sobre fondo app, marca, textos reales ("Entrar", "No puedo entrar"), `Alerta` de error y **todos** los estados actuales, incluido el aviso de "Modo de acceso limitado".

### 5.5 Diálogos y overlays

Los 4 diálogos `Window` y los 3 overlays de actualización se rediseñan con el mismo sistema (tokens, botones, `Alerta`). Los diálogos siguen sin bindings (acceso por `x:Name`).

## 6. Fidelidad al mockup

Requisito central de este rediseño.

### 6.1 Un mockup por pantalla

- Uno por pantalla: ~55 de contenido, más shell, pantallas de acceso, diálogos y overlays.
- Contenido real y datos de ejemplo fijos, a **1280×760**.
- Ubicación: `docs/ui-v2/mockups/<modulo>/<pantalla>.html`, usando el CSS generado de tokens.
- **Solo técnicas que Avalonia replica 1:1:** colores sólidos, bordes de 1px, radios, una única `BoxShadow`, Inter, px enteros, múltiplos de 4.
- **Prohibido:** gradientes, blur, transparencias apiladas, transiciones, pseudo-elementos decorativos, `text-shadow`.
- Se aprueban por módulo.

### 6.2 Herramienta de comparación (se construye en Fase 0)

1. Render headless de la vista Avalonia real a 1280×760 (Avalonia.Headless con Skia, captura de frame), con un ViewModel cargado con **los mismos datos fijos del mockup** → PNG.
2. Captura del mockup con Playwright al mismo tamaño → PNG.
3. Página de comparación por pantalla: ambas imágenes lado a lado más una imagen de diferencias resaltadas.
4. Reporte de porcentaje de píxeles distintos.

### 6.3 Aceptación

- El usuario aprueba cada pantalla mirando la comparación.
- Umbral bajo de diferencia: absorbe el antialiasing de texto; cualquier diferencia de layout, color o tamaño lo supera.
- Ese umbral queda como **test de regresión por pantalla**.
- El valor inicial se calibra en la Fase 0 con la pantalla catálogo-de-componentes y se documenta en la spec de esa fase.

### 6.4 Verificación orgánica

Obligatoria por módulo, con la app real contra la base: login, navegar, crear y editar. Salvedad: el render headless y Windows pueden diferir en antialiasing; por eso el umbral no es 0 y **la aprobación final es del usuario mirando la app**.

## 7. Fases

Ciclo de cada fase: mockups de sus pantallas → aprobación del usuario → XAML → comparación de capturas → verificación orgánica → commit.

| Fase | Contenido |
|---|---|
| 0 Fundaciones | `tokens.json` + generador, herramienta de comparación, componentes, pantalla catálogo-de-componentes comparada (calibra el umbral) |
| 1 Shell y acceso | sidebar, Login, Reset, BloqueoLicencia, AccesoLimitado, 4 diálogos, 3 overlays |
| 2 Inicio | KPIs, panel de tareas, accesos rápidos |
| 3 Inventario | Productos (listado y formulario), Entrada / Salida + `MovimientoFormControl`, Ingreso por factura, Historial de movimientos |
| 4 Finanzas | Gastos + formulario, Pagos, Adjuntos, Ingresos + formulario, Libro caja, Control POA, Calendario de pagos |
| 5 Gestión | Tareas (listado, formulario, clasificación, diálogo de reclasificar), Documentos (listado, formulario, adjuntos) |
| 6 Reportes | hub + los 6 reportes |
| 7 Administración | Catálogos master-detail con los 11 catálogos, Usuarios, Mantenimiento |
| 8 Importación | wizard `NuevaImportacionView` + historial; apagada, verificable solo por comparación; unidad de riesgo propia |
| 9 Limpieza | borrar estilos y componentes viejos, reemplazar `GuardianDePatronTests` por el guardián del sistema nuevo, auditoría de residuos |

**Integración:** merge periódico de `main` hacia `feat/ui-v2`. El merge a `main` se hace **una sola vez, al final**, con `--ff-only` + push (convención del repo). No se integra por fase: dejaría la app mitad vieja y mitad nueva.

## 8. Tests

### 8.1 Se mantienen tal cual (red de seguridad)

- Gates de permisos: `ShellMainViewGatesTests` (localiza por identidad de `Command`, sobrevive al cambio de XAML), `DocumentoListViewGatesTests`, `DocumentoFormViewGatesTests`, `MovimientoHistorialGatesTests`, `FinanzasRevocacionPermisosTests`.
- Validación: `MovimientoFormControlValidacionTests` y el seam `ErrorConverter`.
- `CargaProtegidaSinPermisoGuardianTests`.
- `ReflexionVistaViewModelTests`: las rutas de los `.axaml` se conservan (no se renombran ni se mueven vistas).
- `ViewLocatorTests`.
- Guardianes de exportación PDF / CSV.

### 8.2 Se reemplazan

- `GuardianDePatronTests` (patrones P0-P9 viejos) → guardián del sistema nuevo.
- Los 4 asserts `TranslatePoint` de `MantenimientoViewTests`.
- Tests de tokens y contraste viejos (los valores cambian) → equivalentes sobre los tokens nuevos.
- Tests `DataGrid*` de estilo.

### 8.3 Nuevos

- Gates de las tarjetas del hub de Reportes.
- Gates de los grupos de Catálogos.
- Headers de sección del sidebar ocultos cuando no tienen ítems visibles.
- Gates de las tarjetas KPI de Inicio.
- Test de comparación visual por pantalla (umbral de 6.3).

## 9. Riesgos y partes frágiles

| Riesgo | Cómo se respeta |
|---|---|
| Wiring de `DataContextChanged` en code-behind (44 vistas) | se reproduce en cada vista reescrita; las vistas de Avalonia no se autoinicializan |
| `ViewLocator` por convención de nombre | no se renombran ni se mueven vistas; las nuevas (hub de Reportes, Catálogos) siguen la convención |
| `INavigationService` sin back-stack | el breadcrumb usa el evento `Volver` existente |
| Desuscripción de `Cambiado` | se conserva en cada vista que la tenga |
| `AdjuntosPanelView` vs `AdjuntosDocumentoPanelView` | **no se unifican**: tienen ViewModels distintos |
| `NuevaImportacionView` (509 líneas, 4 grillas, `CompileBindings=False` en 2 estilos) | fase propia (8); sin red del compilador en esos estilos |
| Diálogos `Window` sin bindings | se mantiene el acceso por `x:Name` |
| `MinWidth` por columna + scroll horizontal en grillas (fix `3548397`) | el tema nuevo de `DataGrid` lo preserva |
| Decimales y formato es-UY | converters actuales sin cambios |
| Rama larga | merges de `main` frecuentes |
| Worktrees sin fixtures `.ods` | los rojos de Infrastructure.Tests / Api.Tests por `.ods` en el worktree son falsos conocidos |

## 10. Fuera de alcance

- Modo oscuro.
- Stock bajo / `StockMinimo`.
- Unificar ViewModels.
- Cambiar la navegación a back-stack.
- Reactivar Importación.
- Cambios de API, salvo lo necesario para los KPIs de Inicio (los datos ya existen).
