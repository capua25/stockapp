# F5d — Frontend del importador de planillas de Finanzas (diseño)

- **Fecha:** 2026-07-24
- **Fase:** F5d (continúa F5c, backend de confirmación ya mergeado a main)
- **Alcance:** UI de escritorio (Avalonia, proyecto `StockApp.Presentation`) para operar el importador one-shot de planillas `.ods`, más un endpoint de lectura nuevo para el historial.

## 1. Objetivo y contexto

F5c dejó el backend completo del importador: analizar, confirmar y revertir. F5d construye la cara visible: la pantalla de escritorio con la que el admin sube las planillas del municipio, revisa y corrige el análisis, confirma la carga, ve el resultado y puede revertir un lote.

Restricción operativa dura que condiciona el diseño: **una vez instalado el sistema, el usuario NO tendrá acceso al servidor por mucho tiempo.** Todo control operativo debe vivir en el cliente. Esto justifica que la reversa (incluida la de importaciones pasadas) sea operable desde la app.

Hecho central de arquitectura: **hoy NO existe ninguna grilla editable en todo el repo** — todas son read-only y la edición vive en formularios full-screen. F5d introduce la **primera grilla editable**, por lo que sienta el patrón de edición para lo que venga después. Por eso el riesgo se aísla en su propia entrega (ver §8).

## 2. Contrato backend existente (F5c) que la UI debe respetar

Los tres endpoints comparten prefijo `/finanzas/importar/`, son **POST** y **admin-only** (permiso `finanzas.importar`, sólo `RolUsuario.Admin`). Definidos en `src/StockApp.Api/Endpoints/ImportacionEndpoints.cs`.

- **`POST /finanzas/importar/analizar`** — multipart/form-data: `IFormFile gastos`, `IFormFile poa`, `[FromForm] int ejercicio`. Límite 10 MB por archivo. Read-only (no escribe). Devuelve `ResultadoAnalisisDto`.
- **`POST /finanzas/importar/confirmar`** — JSON body `ConfirmarImportacionDto` (límite 5 MB). Devuelve `ResultadoConfirmacionDto`.
- **`POST /finanzas/importar/revertir/{id:guid}`** — `Guid` en la ruta, sin body. Devuelve `ResultadoReversionDto`.

### Análisis (`ResultadoAnalisisDto`, `src/StockApp.Application/Finanzas/AnalisisImportacionDtos.cs`)
- Colecciones: `Ingresos`, `Gastos`, `LineasPoa`, más `MaestrosNuevos`, `Resumen`, `SaldosPoa`.
- `EstadoFila { Ok, Advertencia, Error }` por fila. `Ok` importa directo; `Advertencia` importa pero pide atención; `Error` NO se puede importar hasta corregir.
- `TipoMotivo` (LiteralVacio, FuenteDesconocida, RubroDesconocido, ProveedorNuevo, FechaIlegible, MontoIlegible, ReconciliacionDudosa) + `MotivoEstado(Tipo, Mensaje)` por celda.
- Celdas en blanco = valor **nullable** (`Fecha DateOnly?`, `Monto decimal?`, `Fuente/Proveedor/Rubro/... string?/int?`) + flags (`FuenteDesconocida`, `ProveedorNuevo`, `RubroDesconocido`) + `Motivos`.
- `MaestrosNuevosDto(Proveedores, Fuentes, Rubros)` con `CodigoRubroNuevoDto(int Codigo, string? NombreSugerido)` — el nombre del rubro nuevo llega vacío y lo completa el humano.
- `ResumenAnalisisDto(TotalFilas, Ok, Advertencias, Errores, PoaConciliados, PoaDudosos, PoaCompromisos)`.

### Confirmación (`ConfirmarImportacionDto`, `src/StockApp.Application/Finanzas/ConfirmacionImportacionDtos.cs`)
- Maestros se referencian por **nombre** (Proveedor/Fuente/LineaPoa) o **código** (Rubro), no por Id; el server hace get-or-create. Campos obligatorios del dominio van **no-nullable** aunque el análisis los dejara vacíos → el humano DEBE completarlos.
- `ConfirmarImportacionDto(int Ejercicio, bool Forzar, MaestrosNuevosConfirmarDto, IReadOnlyList<IngresoConfirmarDto>, IReadOnlyList<GastoConfirmarDto>, IReadOnlyList<LineaPoaConfirmarDto>)`.
- Regla de cierre: nada se crea fuera de lo declarado en `MaestrosNuevosConfirmarDto`. `RubroNuevoConfirmarDto(int Codigo, string Nombre)` — Nombre obligatorio.
- `GastoConfirmarDto`: `FechaVencimiento` obligatoria si `Condicion == Credito`, prohibida si `Contado`.
- Respuesta `ResultadoConfirmacionDto`: `IdImportacion` (GUID del lote, necesario para revertir), contadores creados/omitidos/reactivados, y `Conflictos: IReadOnlyList<ConflictoGastoDto>`.
- `ConflictoGastoDto(string Proveedor, string NumeroFactura, IReadOnlyList<CampoDivergenteDto> CamposDivergentes, int Indice)`; `CampoDivergenteDto(string Campo, string ValorAnterior, string ValorNuevo)` — strings ya formateados. Los conflictos NO se escriben; quedan para decisión humana.

### Errores estructurados
`ValidacionImportacionException` → HTTP **400** `application/problem+json` con extensión `errors`: `{ "Tipo[i].Campo": ["mensaje", ...] }` (ej. `"Gastos[3].Fuente"`, `"Gastos[3].FechaVencimiento"`). Manejado por `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`.

### Reversión (`ResultadoReversionDto`)
`(Guid IdImportacion, int GastosRevertidos, PagosRevertidos, IngresosRevertidos, LineasPoaRevertidas, AsignacionesRevertidas)`. Reversa = baja lógica (`Activo=false`) de las filas del lote. Doble reversión bloqueada por la existencia de un log `ReversionImportacion` con el mismo `IdLote`.

## 3. Backend nuevo — endpoint de historial

Objetivo: listar importaciones pasadas para poder revertirlas desde el cliente (coherente con la restricción operativa).

Verificado: **NO hace falta entidad cabecera nueva ni migración.** Toda la metadata ya está en `LogsAuditoria` (`src/StockApp.Domain/Entities/LogAuditoria.cs`), con índice sobre `IdLote`:
- **IdLote** ← `LogAuditoria.IdLote`
- **Fecha** ← `LogAuditoria.Fecha`
- **Ejercicio** ← `LogAuditoria.EntidadId` (filas con `Accion == ImportacionPlanillas`)
- **Usuario** ← `LogAuditoria.UsuarioId` (+ join a `Usuario`)
- **¿Revertida?** ← existe un `LogAuditoria` con `Accion == ReversionImportacion` y el mismo `IdLote` (mismo patrón que ya usan `BuscarImportacionNoRevertidaAsync` y el guard de doble reversión en `ImportacionRepository.cs`).

Diseño:
- **`GET /finanzas/importar/historial`** (admin-only, permiso `finanzas.importar`). Devuelve `IReadOnlyList<ImportacionHistorialDto>`.
- `ImportacionHistorialDto(Guid IdImportacion, DateTime Fecha, int Ejercicio, string Usuario, bool Revertida)`.
- Query de lectura sobre `LogsAuditoria` (método nuevo en `ImportacionRepository` + servicio de aplicación). Sin escritura, sin migración.
- **Decisión: sin contadores.** Los contadores sólo viven como texto legible en `LogAuditoria.Detalle` (no parseable) y recomputarlos desde las hijas reflejaría el estado actual, no el del momento de importar. El historial sólo necesita ubicar el lote y revertirlo; los contadores exactos ya se ven en el Paso 3 al confirmar.

## 4. ApiClient

Nuevo cliente tipado en `src/StockApp.ApiClient/`, siguiendo el patrón de los existentes (records `*Wire`, `ApiErrores.EnviarAsync` + `AsegurarExitoAsync`, `ApiQuery`):
- Interfaz `IImportacionService` en `src/StockApp.Application/Finanzas/`.
- `ImportacionApiClient : IImportacionService` con 4 métodos: `AnalizarAsync` (multipart), `ConfirmarAsync` (JSON), `RevertirAsync(Guid)`, `ListarHistorialAsync`.
- Registro en DI en `src/StockApp.Presentation/App.axaml.cs`.
- Auth JWT y manejo 401/403/423 vienen gratis por `AuthTokenHandler`/`ApiSession`; el 400 estructurado de confirmación se mapea a una excepción que la UI pueda descomponer por clave `"Tipo[i].Campo"`.

## 5. Estructura de UI

- **1 ítem admin-only en el sidebar**: "Importar planillas" (`IsVisible="{Binding EsAdmin}"` en `ShellMainView.axaml`, comando de navegación en `ShellMainViewModel`). Abre la pantalla contenedora.
- **Pantalla contenedora con 2 tabs de nivel superior** (patrón `TabControl` como `MaestrosFinanzasView`): **[Nueva importación]** y **[Historial]**. VM contenedor expone sub-VMs.

### Tab "Nueva importación" — wizard de 3 pasos
- **Paso 1 · Cargar**: selector de `gastos.ods`, selector de `poa.ods`, campo ejercicio, checkbox `Forzar` (re-importar sobre un ejercicio ya importado; el backend rebota 409 salvo `Forzar`). Botón **Analizar** → `POST /analizar`.
- **Paso 2 · Revisar**: barra de resumen arriba (`Ok / Advertencias / Errores` de `ResumenAnalisisDto`). **TabControl interno**: `[Gastos] [Ingresos] [Líneas POA] [Maestros nuevos]`.
  - Grillas envueltas en `DataGridCollectionView` para conservar el sort por header (gotcha Avalonia 12, ver `GastosViewModel`).
  - Color de fila por `EstadoFila`: rojo `Error`, amarillo `Advertencia`, neutro `Ok`. Reusar/añadir converters (`SignoNegativoBrushConverter`, `MonedaConverter`, etc. en `Presentation/Converters/`).
  - Botón **Confirmar**: deshabilitado mientras haya ≥1 fila en `Error`; las `Advertencia` NO bloquean. Arma `ConfirmarImportacionDto` a partir del análisis corregido y hace `POST /confirmar`.
  - Red de seguridad: si el server igual rebota `400` estructurado, se parsea `errors`, se resalta la celda de la clave `"Tipo[i].Campo"` y se salta a la pestaña que la contiene.
- **Paso 3 · Resultado**: contadores de `ResultadoConfirmacionDto`; grillita de **conflictos** (`Proveedor · NumeroFactura · Campo: ValorAnterior → ValorNuevo`) con nota de que esos gastos NO se escribieron y hay que resolverlos a mano; botón **Revertir** que usa el `IdImportacion` recién confirmado → `POST /revertir/{id}`.

### Tab "Historial"
- Grilla **read-only clásica** (patrón del repo, disparo por `DataContextChanged → CargarAsync()`): columnas Fecha · Ejercicio · Usuario · Estado (Activa/Revertida).
- Botón **Revertir por fila**, habilitado sólo en filas Activas → `POST /revertir/{id}`. Tras revertir, refrescar la grilla.

## 6. Patrón de edición híbrido (Paso 2, se activa en Entrega 2)

- La grilla se ve como una planilla: **celdas `Ok` bloqueadas** (read-only), **editables sólo las faltantes o erróneas** (las marcadas por `EstadoFila`/`Motivos`).
- Tipos de celda editable: Fuente/Rubro/Proveedor = **combos de texto libre**; Fecha/Vencimiento = **date-pickers**; Monto = decimal validado; Detalle/Concepto = texto.
- **Maestros nuevos automáticos**: si en un combo se escribe un valor que no existe, se auto-declara y aparece en la pestaña **Maestros nuevos** (badge con conteo). Esa pestaña es el tablero de control: ahí se completa lo que falta, sobre todo el **nombre obligatorio de cada rubro nuevo**. Regla de cierre del backend: sólo se crea lo declarado ahí.
- Validación por celda que alimenta el estado del botón Confirmar (Error bloquea).

## 7. Decisiones tomadas (y por qué)

1. **Edición híbrida** (no form-por-fila, no todo-editable puro): el caso real es corregir decenas de celdas de una planilla; el form-por-fila obliga a abrir/cerrar decenas de veces; "todo editable" cuesta lo MISMO que el híbrido pero permite pisar celdas `Ok` y descarta la guía visual que el backend ya provee (`EstadoFila`/`Motivos`). El híbrido aprovecha esa guía y protege lo correcto.
2. **Wizard de 3 pasos** con tabs internas en el paso de revisión: cada paso una responsabilidad; las tabs reflejan las 3 colecciones que devuelve el backend + maestros.
3. **Historial con endpoint nuevo pero barato**: coherente con "no hay acceso al servidor"; sale de una query sobre auditoría, sin migración ni entidad nueva; sin contadores (frágiles) porque el historial sólo ubica y revierte.
4. **Dos tabs de nivel superior, un solo ítem de sidebar**: todo lo de importación en un lugar.
5. **Reversa inmediata (Paso 3) + por fila en Historial**.
6. **Confirmar bloqueado por Errores, no por Advertencias**, con el 400 estructurado como red de seguridad.
7. **Conflictos** informativos en el Paso 3 (vienen en la respuesta 200, no son errores 400).

## 8. Plan de entregas

Dos entregas, cada una mergeada y verificada orgánicamente por separado.

### Entrega 1 — Fundación (bajo riesgo, patrones ya probados del repo)
- Backend: endpoint `GET /finanzas/importar/historial` + `ImportacionHistorialDto` + query de lectura sobre auditoría.
- ApiClient: `IImportacionService` + `ImportacionApiClient` (4 métodos) + registro DI.
- Sidebar: ítem admin-only + navegación.
- Pantalla contenedora con 2 tabs.
- Tab **Historial** completo (grilla read-only + Revertir por fila).
- Wizard: **Paso 1** (analizar) y **Paso 3** (resultado + revertir). Las grillas del **Paso 2 en modo SOLO LECTURA con color** (sin edición). Permite analizar, ver el estado por color, confirmar si el análisis vino limpio, y revertir.

### Entrega 2 — La primera grilla editable del repo (riesgo aislado)
- Grilla **híbrida editable** del Paso 2: celdas `Ok` bloqueadas, combos de texto libre, date-pickers, validación por celda.
- **Maestros nuevos automáticos** desde la celda + pestaña Maestros como tablero de control.
- Mapeo del análisis corregido → `ConfirmarImportacionDto` + manejo del 400 estructurado (resaltar celda + saltar a pestaña).

## 9. Fuera de alcance de F5d

- Backup automático `pg_dump` cada 12 h y descarga de logs desde el desktop (requisitos pre-instalación, tienen su propio frente).
- Resolución automática de conflictos de gasto (se muestran, se resuelven a mano).
- Contadores estructurados del historial (requeriría persistir datos que hoy no existen tipados).
