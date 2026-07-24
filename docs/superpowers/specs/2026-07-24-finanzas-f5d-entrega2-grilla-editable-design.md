# F5d Entrega 2 — Grilla híbrida editable (diseño)

- **Fecha:** 2026-07-24
- **Fase:** F5d Entrega 2 (continúa la Entrega 1, ya mergeada a main en 938315a)
- **Alcance:** convertir el Paso 2 del importador (hoy read-only con color) en la PRIMERA grilla editable del repo, más un cambio chico de backend para líneas POA nuevas.

## 1. Objetivo y contexto

La Entrega 1 dejó el importador funcionando para ver, revisar (read-only con color), historial y revertir, pero con Confirmar limitado: sólo funciona si el análisis vino 100% completo, porque no hay forma de completar las celdas faltantes. La Entrega 2 agrega la EDICIÓN: completar las ~34 celdas en blanco (fuente/proveedor/fecha), los compromisos POA (rubro/fecha/vencimiento) y crear las líneas POA nuevas, para que la planilla real del municipio se cargue de punta a punta.

Es la **primera grilla editable del repo** (hoy todas son read-only, edición en forms full-screen). Sienta el patrón de edición inline para el proyecto. El QUÉ se validó en el brainstorming original (edición híbrida); esta Entrega 2 resuelve el CÓMO técnico en Avalonia 12.

## 2. Decisiones tomadas (brainstorming, validadas con el usuario)

1. **Scope COMPLETO**: la Entrega 2 incluye edición de gastos/ingresos + maestros nuevos automáticos + creación de líneas POA nuevas + validación por celda + descomposición visual del error 400. Objetivo: la primera importación real queda funcional de punta a punta.
2. **Condición de pago y Vencimiento editables**: columnas nuevas (combo Contado/Crédito + date-picker de vencimiento habilitado sólo si Crédito). La heurística actual de la Entrega 1 (compromiso POA → Crédito con vencimiento = fecha del gasto; si no, Contado) queda como VALOR INICIAL sugerido, corregible. Validación espejo del backend: vencimiento obligatorio si Crédito, prohibido si Contado.
3. **Celdas Ok**: bloqueo estricto por defecto (guía a lo que falta, protege lo resuelto) + **desbloqueo por fila bajo demanda** (una acción explícita ✎ por fila desbloquea sus celdas Ok para corregir una reconciliación errónea). Lo mejor de ambos: guía/protección por defecto, sin quedar atrapado si el sistema resolvió mal.
4. **Líneas POA nuevas**: se crean desde la grilla (ver §6). Requiere un cambio chico de backend (flag `EsNueva`).

## 3. Modelo: VM por fila editable

Los DTOs de análisis (`GastoAnalizadoDto`, etc.) son records inmutables que hoy se bindean directo — no soportan two-way binding de celda. Se reemplazan por VMs de fila mutables:
- `FilaGastoEditableVm`, `FilaIngresoEditableVm`, `FilaLineaPoaEditableVm` — heredan de `ObservableValidator` (CommunityToolkit.Mvvm) para validación por campo.
- Cada fila expone: los valores editables como propiedades observables TwoWay; el `EstadoFila`/`Motivos` original (para color y para saber qué celda arranca editable); una propiedad calculada `EsEditable(campo)` (o una por celda) según null/motivo; y `Desbloqueada` (bool, lo togglea la acción ✎ de fila).
- `AnalizarAsync` proyecta `ResultadoAnalisisDto` → colecciones de filas VM. Confirmar mapea filas VM → `ConfirmarImportacionDto`.
- Es el patrón estándar de DataGrid editable en MVVM (necesario para two-way, validación, `CancelEdit`, y las props calculadas de edición).

## 4. Grilla híbrida (Paso 2, ahora editable)

Patrón Avalonia 12 (verificado en la doc):
- Cada columna editable pasa de `DataGridTextColumn` a `DataGridTemplateColumn` con `CellTemplate` (modo lectura, muestra el valor + candado si está bloqueada) y `CellEditingTemplate` (modo edición, con el control adecuado).
- `IsReadOnly="False"` explícito en el `DataGrid` Y en cada columna editable (NO confiar en el cascade — gotcha #14127 + `DataGridCollectionView`).
- Edición selectiva (híbrido): dentro del `CellEditingTemplate`, el control de edición bindea `IsEnabled` a la propiedad calculada del VM de fila (ej. `EsEditableFuente => Fuente is null || tieneMotivoFuente || Desbloqueada`). Celda Ok y fila bloqueada → control deshabilitado. (Alternativa si hace falta impedir el ingreso a modo edición, no sólo deshabilitar: interceptar `BeginningEdit` y `e.Cancel`.)
- Controles por tipo de celda:
  - **Fuente / Rubro / Proveedor** → `ComboBox IsEditable="True"` con `Text` bindeado (no sólo `SelectedItem`) e `ItemsSource` de los maestros existentes; permite elegir uno existente O escribir uno nuevo. Los maestros existentes se cargan vía `IFuenteFinanciamientoService.ListarActivasAsync`, `IRubroGastoService.ListarActivosAsync`, `IProveedorService.ListarTodosAsync` (filtrando activos). El repo NO usa `AutoCompleteBox` hoy; se introduce `ComboBox IsEditable`.
  - **Fecha / Vencimiento** (`DateOnly?`) → `CalendarDatePicker`/`DatePicker`. Avalonia usa `DateTimeOffset?`, no `DateOnly?`: se necesita un converter `DateOnly? ↔ DateTimeOffset?` (nuevo en Presentation/Converters/) o una propiedad de conveniencia en el VM de fila. Binding TwoWay explícito; manejo defensivo de null (gotchas #12252/#13037).
  - **Monto** (`decimal?`) → `TextBox` + `DecimalOpcionalConverter` (ya existe, reutilizable).
  - **Condición** → `ComboBox` Contado/Crédito (enum `CondicionPago`).
  - **Detalle / Concepto / NumeroFactura / NumeroOrden / Destino** → `TextBox`, editables donde falten o si la fila está desbloqueada.
- El color por `EstadoFila` (Style compartido de `DataGridRow` con `x:CompileBindings="False"`) se mantiene; se suma el indicador visual de celda bloqueada (candado) vs editable.

## 5. Maestros nuevos automáticos

- Cuando en un combo (`ComboBox IsEditable`) se escribe un valor que no matchea ningún maestro existente, se auto-declara como nuevo: se agrega a la colección correspondiente de maestros nuevos (Proveedores / Fuentes / Rubros). La resolución "es nuevo" se hace al confirmar (comparando el texto contra los existentes), no hay control nativo "create-if-missing".
- La pestaña **Maestros nuevos** es el tablero de control: badge con el conteo, y para cada **rubro nuevo** un campo **Nombre obligatorio** (el análisis deja `NombreSugerido` en null; hoy la Entrega 1 lo manda como `""`, lo que viola `RubroNuevoConfirmarDto.Nombre` no-vacío — la Entrega 2 lo corrige exigiendo el nombre).
- Regla de cierre del backend: sólo se crea lo declarado en `MaestrosNuevosConfirmarDto`.

## 6. Líneas POA nuevas

Investigación del backend confirmada:
- `Nombre` de la línea = la Hoja del .ods (ya en `LineaPoaAnalizadaDto.Hoja`; el backend ya la usa así para vincular gastos).
- `Asignaciones` (Fuente + Monto) = ya vienen en el análisis (`Literal`/`Presupuesto`, una fila por asignación por financiamiento mixto); sólo hay que reagruparlas por Hoja.
- `Programa` = **NO existe en la planilla** ni lo lee el parser. El humano lo completa a mano (igual que en el ABM manual de líneas POA). Campo obligatorio (`LineaPoa.Programa` no-vacío).
- **Distinguir línea nueva vs existente**: hoy el análisis NO lo expone (a diferencia de `ProveedorNuevo`/`FuenteDesconocida`/`RubroDesconocido`).

Diseño:
- **Backend (tarea nueva, cambio chico)**: agregar un flag `bool EsNueva` a `LineaPoaAnalizadaDto`, computado en `AnalisisImportacionService` comparando la Hoja contra las líneas POA existentes del ejercicio (`ILineaPoaRepository`/servicio; el backend ya hace esa comparación en la validación de confirmación).
- **Frontend**: en la grilla de Líneas POA del Paso 2, cada línea con `EsNueva == true` muestra un campo **Programa** editable y obligatorio (texto libre, con autocompletar de los programas ya usados en líneas existentes). `Nombre` = Hoja (read-only). Las asignaciones se agrupan del análisis por Hoja.
- Al confirmar: las líneas nuevas se mandan como `LineaPoaConfirmarDto(Nombre=Hoja, Programa=input, Asignaciones=agrupadas)` en `ConfirmarImportacionDto.LineasPoa` (reemplaza el `[]` fijo de la Entrega 1).

## 7. Validación por celda

- Las filas VM (`ObservableValidator`) usan DataAnnotations (`[Required]`, etc.) + reglas custom (vencimiento condicional a la condición).
- **Avalonia 12 DESHABILITA la validación por DataAnnotations por defecto** → activar `.WithDataAnnotationsValidation()` en `Program.cs` (paso obligatorio, fácil de olvidar).
- Errores inline por celda vía `DataValidationErrors` (borde rojo + tooltip, ya presente en el ControlTemplate de TextBox/ComboBox; converter `ErrorValidacionConverter` ya registrado globalmente).
- El VM contenedor agrega `HasErrors` de todas las filas (suscribiéndose a `ErrorsChanged` de cada una). El **gating de Confirmar se relaja**: de `ContarFilasIncompletas() == 0` (Entrega 1) a "**ninguna fila con errores de validación**" — ahora que los campos se pueden completar editando. `MensajeConfirmarBloqueado` pasa a indicar cuántas filas tienen errores pendientes.

## 8. Error 400 estructurado — descomposición visual

La Entrega 1 muestra el `ValidacionImportacionException` como texto plano en un diálogo. La Entrega 2 lo descompone:
- El ApiClient ya reconstruye el diccionario `Errores` ("Tipo[i].Campo" → mensajes) desde el problem+json (hecho en la Entrega 1).
- Al recibirlo, mapear cada clave: `Tipo` (Gastos/Ingresos/LineasPoa) + índice `[i]` → la fila i de la colección correspondiente; `Campo` → la celda. Resaltar esas celdas (borde de error), marcar la fila, y **saltar automáticamente a la pestaña** que contiene el primer error, dejando el foco cerca.
- Es la red de seguridad: aunque la validación cliente pase, si el server rebota, el usuario ve exactamente dónde.

## 9. Gotchas de Avalonia 12 a tener presentes (de la investigación)

- Validación por DataAnnotations deshabilitada por defecto → `.WithDataAnnotationsValidation()`.
- `DataGrid.IsReadOnly="False"` no cascada confiable a columnas con `DataGridCollectionView` → setear explícito por columna.
- Edición inline con `DataGridCollectionView` tuvo un bug (#15865) fixeado en jul-2024; probablemente ya en 12.0.5 pero **verificar en la app real** (doble-click para editar): si no commitea o la fila salta, revisar bindings con casts en el Path.
- `DatePicker` no soporta `DateOnly?` nativo → converter.
- Ordenar por una columna que se está editando hace que la fila salte al commitear (comportamiento normal de CollectionView con sort): considerar congelar el sort durante la edición.

## 10. Contrato relevante (recordatorio)

- Análisis: `GastoAnalizadoDto`/`IngresoAnalizadoDto`/`LineaPoaAnalizadaDto` con campos nullable (editables) + flags (`FuenteDesconocida`/`ProveedorNuevo`/`RubroDesconocido`) + `EstadoFila`/`Motivos`. `CodigoRubroNuevoDto(Codigo, NombreSugerido?)`.
- Confirmación: `GastoConfirmarDto` (obligatorios Proveedor/Detalle/Fecha/MontoTotal/Fuente/CodigoRubro/Condicion; FechaVencimiento condicional), `IngresoConfirmarDto` (Fecha/Concepto/Monto/Fuente), `RubroNuevoConfirmarDto(Codigo, Nombre)`, `LineaPoaConfirmarDto(Nombre, Programa, Asignaciones)`, `AsignacionConfirmarDto(Fuente, Monto)`.
- Maestros existentes: `IFuenteFinanciamientoService.ListarActivasAsync`, `IRubroGastoService.ListarActivosAsync`, `IProveedorService.ListarTodosAsync`, `ILineaPoaService.ListarActivasAsync`.

## 11. Fuera de alcance de la Entrega 2

- Reconciliación automática de conflictos de gasto (se muestran en el Paso 3, se resuelven a mano — sin cambios respecto a Entrega 1).
- Edición de los movimientos POA a nivel submovimiento (reconciliación dudosa por movimiento) — el análisis los expone pero no hay UI; queda como deuda futura si aparece la necesidad.
- Contadores estructurados del historial (deuda ya anotada).
- Backup pg_dump / descarga de logs (frente aparte).
