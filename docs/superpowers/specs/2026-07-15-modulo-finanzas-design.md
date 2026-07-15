# Diseño: Módulo de Finanzas

**Fecha**: 2026-07-15
**Estado**: Aprobado por secciones en brainstorming (pendiente review final del spec)

## 1. Contexto y problema

El Municipio de Carmelo lleva hoy sus finanzas en dos planillas LibreOffice mantenidas a mano:

- **"Planilla Gastos 2026 v3.ods"** — libro caja cronológico: 12 hojas mensuales (`FECHA | FACTURA | ORDEN | PROVEEDOR | DESTINO | GASTO | INGRESO | EGRESO | SALDO | LITERAL | Código | RUBRO`) con saldo corrido encadenado mes a mes, resúmenes por rubro (17 rubros) y por literal (fuentes de financiamiento FIGM: A, B, C, Multas, Excedentes/Préstamos), y consolidado anual.
- **"PLANILLA POA 2026 detallada por lineas.ods"** — control presupuestal por línea de proyecto del POA: 15 líneas (Rambla, Carpeta Asfáltica, Mejoras en Pluviales, Eventos, Prensa, etc.), cada una con presupuesto asignado por literal (B o C, una mixta B+C), detalle de facturas imputadas y saldo restante.

**Problema central**: la misma factura se carga a mano en ambas planillas (doble carga verificada con facturas reales), sin conciliación ni alertas — la línea PRENSA ya está sobregirada (-7.915) sin ningún aviso. Además hay datos sucios: literales vacíos, rubros incoherentes, texto libre en campos que deberían ser maestros.

Por otro lado, la app de stock registra ingresos de mercadería (`MovimientoStock` Entrada/Compra) **sin documento**: no hay número de factura, ni proveedor en el movimiento, ni condición de pago, ni comprobante adjunto.

## 2. Objetivo

Un único módulo de Finanzas que:

1. Reemplaza a las dos planillas — la app pasa a ser la **fuente de verdad** (con exportación CSV para reportar).
2. Registra cada gasto **una sola vez** con sus dimensiones (fuente de financiamiento, rubro, línea POA opcional); el libro caja, el control presupuestal POA y el calendario de pagos son **vistas calculadas** sobre los mismos datos.
3. Integra el ingreso de stock: la factura del proveedor es un gasto más de la misma caja municipal.
4. Soporta compra a crédito (con vencimiento) o contado, pagos parciales y comprobantes adjuntos.
5. Incluye un importador de las planillas .ods existentes con grilla de corrección, para la puesta en producción.

**Fuera de alcance (no-goals)**:
- Sistema de gestión de permisos por usuario (el admin asigna permisos a cada usuario): es una feature separada posterior. Este módulo define permisos granulares otorgados a todos los roles por ahora.
- Gráficos (la hoja GRAFICAS): la vista anual es numérica en esta fase.
- Contabilidad formal (partida doble, plan de cuentas): descartada por sobredimensionada.

## 3. Enfoques considerados

- **A. Gasto unificado con vistas derivadas (elegido)**: un registro por gasto con dimensiones; saldos calculados, nunca almacenados. Elimina la doble carga y habilita alertas.
- **B. Imitar las planillas**: grilla mensual + hojas POA independientes. Descartado: replica el defecto de la doble carga dentro de la app.
- **C. Mini contabilidad formal**: partida doble, cuentas corrientes. Descartado: sobredimensionado para caja simple municipal.

## 4. Modelo de datos (Domain)

Convenciones del proyecto: nombres en español, baja lógica `Activo` (nunca DELETE físico), `decimal` precisión 18,4, fechas UTC.

### Maestros

| Entidad | Campos | Notas |
|---|---|---|
| `FuenteFinanciamiento` | `Id`, `Nombre` (único), `Activo` | Los "literales": A, B, C, Multas, Excedentes/Préstamos. Maestro cerrado (hoy texto libre en la planilla). |
| `RubroGasto` | `Id`, `Codigo` (int único), `Nombre`, `Activo` | Los 17 rubros de la hoja Variables. |
| `LineaPoa` | `Id`, `Nombre`, `Programa`, `Ejercicio` (año), `Activo` | Las líneas del POA. |
| `AsignacionPresupuestal` | `Id`, `LineaPoaId`, `FuenteFinanciamientoId`, `Monto` | Presupuesto de cada línea POR fuente — resuelve el financiamiento mixto B+C (caso real COMPOSTERAS). |

### Documentos

- **`Gasto`** (cabecera única): `Id`, `ProveedorId`, `NumeroFactura?`, `NumeroOrden?` (orden de compra), `Detalle`, `Destino?`, `Fecha`, `MontoTotal`, `FuenteFinanciamientoId`, `RubroGastoId`, `LineaPoaId?`, `CondicionPago` (enum `Contado`/`Credito`), `FechaVencimiento?` (obligatoria si crédito), `Activo`. El número de factura es opcional (compromisos sin factura: solicitudes de suministro, expedientes).
- **`PagoGasto`**: `Id`, `GastoId`, `Fecha`, `Monto`, `Nota?`, `Activo`. Contado ⇒ se crea un pago automático por el total en la fecha del gasto.
- **`IngresoCaja`**: `Id`, `Fecha`, `Concepto`, `FuenteFinanciamientoId`, `Monto`, `Activo`. Partidas mensuales, multas, préstamos. El saldo inicial entra como ingreso "Saldo inicial 2026".
- **`Adjunto`**: `Id`, `NombreArchivo`, `ContentType`, `TamanoBytes`, `GastoId?` XOR `PagoGastoId?`, `Activo`. El contenido binario vive en tabla/propiedad separada de los metadatos para que los listados no arrastren bytes.

### Enums nuevos

`CondicionPago` (`Contado`, `Credito`). El **estado** del gasto (`Pendiente` / `Parcial` / `Pagada` / `Vencida`) **se calcula siempre** de `sum(pagos activos)` vs `MontoTotal` + `FechaVencimiento` — nunca se persiste, así jamás queda inconsistente.

### Vínculo con stock

`MovimientoStock` gana `GastoId?` (FK opcional). Una factura agrupa N movimientos de entrada. Nada del comportamiento actual de stock cambia.

### Reglas de oro

- El saldo de caja impacta en la fecha de cada **pago** (no de la factura).
- Los saldos (caja corrida mensual, restante por línea POA) son **consultas**, no columnas.
- Todas las entidades nuevas tienen ABM completo (`AltaAsync`, `ModificarAsync`, `BajaLogicaAsync`, listados) siguiendo el patrón de `CategoriaService`.

## 5. Vínculo con el ingreso de stock y flujo de carga

1. **Ingreso de stock con factura**: en la pantalla de Entrada, al confirmar aparece un paso **opcional** "Asociar factura": proveedor, número de factura, orden, fuente/rubro/línea POA, contado o crédito (con vencimiento), adjuntos. Crea el `Gasto` y vincula los movimientos. Si la factura ya existe (cargada antes desde Finanzas), se busca y asocia en vez de crearla.
2. **Gasto directo desde Finanzas**: para todo lo que no es stock (eventos, obras, servicios) — mismo formulario, sin movimientos asociados. Es el caso masivo de las planillas.
3. **Monto**: el total de la factura es editable, precargado con la suma de los movimientos (cantidad × precio unitario) cuando viene del ingreso de stock (la factura real puede incluir fletes, ítems no-stock, redondeos).
4. **Registrar pago**: desde la factura, acción "Registrar pago" (fecha, monto, recibo adjunto). El paso de factura en el ingreso de stock es opcional — ajustes y devoluciones no tienen factura y la operativa de stock no se bloquea por un dato financiero.

## 6. Adjuntos (infraestructura nueva)

- **Almacenamiento en PostgreSQL (`bytea`)**, tabla propia con contenido separado de metadatos. Razón: el backup de la base se lleva todo consigo, sin rutas rotas ni carpetas que sincronizar. Volumen esperado bajo (decenas de comprobantes por mes). Si el volumen crece, se migra a filesystem detrás de la misma interfaz.
- **Límites**: PDF, JPG, PNG; máximo 10 MB por archivo; validación de content-type real (magic bytes, no solo extensión) en API y en UI.
- **API**: `multipart/form-data` — `POST /finanzas/gastos/{id}/adjuntos`, `POST /finanzas/pagos/{id}/adjuntos`, `GET .../adjuntos/{id}` (descarga), baja lógica. JWT + rate limiting como el resto.
- **Desktop**: lista de adjuntos en el formulario de gasto/pago con Agregar (file picker), Ver (descarga a temp y abre con la app del SO) y Quitar.

## 7. Pantallas del desktop (sección "Finanzas" en el sidebar)

1. **Gastos y facturas** — pantalla de trabajo diaria: grilla con filtros (rango de fechas, proveedor, fuente, rubro, línea POA, estado), acciones Nuevo / Editar / Registrar pago / Adjuntos / Anular. Export CSV.
2. **Ingresos de caja** — ABM simple de partidas.
3. **Libro caja** — reemplaza la planilla de gastos: selector de mes, tabla cronológica de ingresos y egresos con saldo corrido, panel de totales por rubro y por fuente, saldo inicial/final del mes. Egresos en la fecha del pago. Selector "Año completo" para la vista anual (totales por mes y por rubro, sin gráficos).
4. **Control POA** — reemplaza la planilla POA: una fila por línea con presupuesto, gastado, saldo y % de ejecución, con alerta visual de sobregiro. Doble click abre las facturas de la línea.
5. **Calendario de pagos** — facturas vencidas (rojo), a vencer en 7 y 30 días, pagos efectuados; acceso directo a "Registrar pago". Al abrir la app, aviso en Inicio si hay facturas vencidas o por vencer en la semana.
6. **Maestros de finanzas** — gestión de fuentes, rubros y líneas POA con sus asignaciones presupuestales.

El **importador NO va en el menú de Finanzas**: vive dentro de la pantalla de Administración (solo Admin), como opción discreta "Importar planillas históricas" — es una herramienta de puesta en producción, no de uso diario.

## 8. Importador de planillas (.ods) con grilla de corrección

- **Parseo server-side**: el desktop sube el .ods (multipart); la API lo parsea (zip + content.xml, sin dependencias externas). Una sola implementación, testeable con las planillas reales como fixtures.
- **Dos pasos: análisis → confirmación.**
  1. El servidor devuelve **todas las filas tal cual van a ir a la base**, cada una con estado: OK, advertencia (literal vacío, rubro desconocido, proveedor nuevo a crear) o error (fecha/monto ilegible).
  2. El desktop las muestra en **grillas editables** (pestañas por tipo: gastos, ingresos, líneas POA, maestros): celdas problemáticas resaltadas, corrección inline (fuente y rubro con combos, proveedor con autocompletar contra existentes, montos y fechas editables), filas excluibles. **Confirmar importación** se habilita cuando no quedan errores, y envía los datos corregidos al servidor, que importa todo en una transacción.
- **Qué importa**:
  - *Planilla Gastos*: saldo inicial de enero → `IngresoCaja` "Saldo inicial"; filas INGRESO → `IngresoCaja`; filas EGRESO → `Gasto` contado con pago automático en la fecha de la planilla. Rubros por código (hoja Variables), fuentes por nombre, proveedores por nombre (se crean los faltantes).
  - *Planilla POA*: hojas → `LineaPoa` + `AsignacionPresupuestal`; sus facturas se **concilian** contra las del libro caja por número de factura + orden — si matchean no se duplica el gasto, solo se le asigna la línea POA; las dudosas se deciden en la grilla ("es la misma" / "son distintas"). Las que están solo en el POA (compromisos) se crean como gastos pendientes.
- **Idempotente**: reimportar detecta lo existente (factura + orden + fecha + monto) y lo saltea.

## 9. API y permisos

**Endpoints** (patrón `XxxEndpoints` + JWT + policies): `/finanzas/gastos`, `/finanzas/gastos/{id}/pagos`, `/finanzas/gastos/{id}/adjuntos`, `/finanzas/ingresos`, `/finanzas/fuentes`, `/finanzas/rubros`, `/finanzas/lineas-poa`; consultas: `/finanzas/libro-caja?anio&mes`, `/finanzas/control-poa`, `/finanzas/calendario-pagos`; importación: `/finanzas/importar/analizar`, `/finanzas/importar/confirmar`. El desktop consume vía `ApiClient` implementando las mismas interfaces `IXxxService`.

**Permisos granulares** (en `Permisos.cs` + `AuthorizationService`): `VerFinanzas`, `RegistrarGastos`, `RegistrarPagos`, `RegistrarIngresos`, `GestionarMaestrosFinanzas`, `ImportarPlanillas`. Por ahora todos otorgados a Admin y Operador, **excepto `ImportarPlanillas` (solo Admin)**. El futuro sistema de permisos por usuario solo cambia el mapeo.

## 10. Reglas de negocio y manejo de errores

Vía `ReglaDeNegocioException` / `EntidadNoEncontradaException`, como el resto de la app:

- No se puede pagar más que el saldo pendiente de la factura; monto > 0 y fecha obligatorios.
- Crédito exige `FechaVencimiento`; contado no la lleva.
- No se anula un gasto con pagos activos (primero anular los pagos).
- Gasto con línea POA debe tener fuente compatible con las asignaciones presupuestales de esa línea.
- Sobregiro de línea POA: **advierte pero no bloquea** (la app avisa, el humano decide).
- Maestros dados de baja no se ofrecen para gastos nuevos; los históricos los siguen mostrando.
- Adjuntos: content-type real validado, máximo 10 MB, error claro si se excede.

**Auditoría**: toda escritura registrada vía `IAuditLogger` con valores nuevos de `AccionAuditada` agregados al final (append-only): alta/modificación/anulación de gastos, pagos, ingresos, maestros, adjuntos e importaciones.

## 11. Testing

TDD por capas, un proyecto de test por capa (patrón existente):

- **Application**: reglas de negocio, cálculo de estado de factura, saldos de caja y de línea POA, validaciones del importador.
- **Infrastructure**: repositorios contra Postgres (incluida la tabla de adjuntos).
- **Api**: endpoints con la matriz 401/403/409 existente; multipart de adjuntos e importación.
- **ApiClient**: mapeos wire ↔ dominio.
- **Presentation**: ViewModels (formularios, grilla de corrección del importador, calendario).
- **Importador con fixtures reales**: las dos planillas .ods reales como fixtures; los saldos importados deben cerrar exacto contra la planilla (caja a junio 2026 = 43.705; saldo POA Literal B = 6.643.349; saldo POA Literal C = 4.654.206).
- **Verificación orgánica**: app real + Postgres (contenedor stockapp-pg) antes de dar por terminado.

## 12. Decisiones registradas

1. La app reemplaza a las planillas (fuente de verdad) — con export CSV.
2. Importar ya los datos 2026 + herramienta de importación permanente (pero escondida en Administración).
3. Stock y caja municipal son la misma caja: la factura de stock lleva fuente/rubro/línea POA.
4. Pagos parciales con estados calculados.
5. Adjuntos múltiples por factura y por pago, en Postgres.
6. Permisos: todos los roles todo por ahora (importador solo Admin); sistema de permisos por usuario = feature separada posterior.
7. Enfoque A (gasto unificado con vistas derivadas) sobre imitar planillas o partida doble.
