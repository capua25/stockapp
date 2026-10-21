# Caja POA — diseño

Fecha: 2026-10-21. Rama: `feat/caja-poa`.

## Intención y por qué

El cliente (Municipio de Carmelo, Uruguay) pide una segunda caja, la **caja POA** (Plan Operativo Anual), además de la caja local del municipio.

En la realidad, la plata POA la paga un organismo gubernamental contra rendiciones de cuentas. Pero a nivel sistema y operativa lo más práctico es tratarla como otra caja: el presupuesto asignado entra como **ingreso** a la caja POA y cada rendición de cuentas es una **salida** (un gasto con su pago) de esa caja. Para el sistema no existe el concepto "rendición de cuentas": es una salida común.

## Objetivos

- Dos cajas, **Local** y **POA**, con saldos COMPLETAMENTE independientes.
- La caja se mueve en el momento en que se registra la plata: al ingresar el recibo de pago (`PagoGasto`) o el ingreso de caja (`IngresoCaja`). Decisión de Martin: la caja Y el literal se eligen en el **pago**, no en el `Gasto`; en los ingresos de caja van en el `IngresoCaja`. La caja no se deduce del literal: se elige EXPLÍCITAMENTE.
- **Todo lo de la caja POA es siempre contado** (decisión de Martin). Por eso:
  - **Gasto contado** (`GastoService.AltaAsync` e `IngresoPorFacturaService` contado): la caja y el literal se eligen en el formulario de alta y se guardan en el pago automático (`PagoGasto.Automatico`).
  - **Gasto a crédito y sus pagos manuales** (`PagosGastoView` y el pago desde el calendario de pagos): siempre caja **Local**. No hay selector de caja; solo se elige el literal entre los de caja Local.
  - El backend rechaza un gasto a crédito con caja POA y un pago manual con un literal de POA.
- Operaciones alcanzadas: alta y modificación de ingreso de caja; alta y modificación de gasto contado (caja + literal de su pago automático); alta de gasto a crédito (sin caja ni literal); pago manual de gasto (literal Local); ingreso por factura contado (caja + literal) y crédito (sin caja ni literal).
- **Literal** = la `FuenteFinanciamiento` existente (documentada en el código como "literal FIGM"). Es solo una etiqueta para separar el estado de cuenta: la plata de la POA es una sola. Con caja POA solo se permiten los literales B y C; con caja Local no se permiten B ni C (B y C son siempre POA).
- **Libro de caja** (lo que el cliente llama "estado de cuenta"; es la pantalla que ya existe) con selector de caja.

## No-objetivos

- Transferencias entre cajas: NO existen.
- Concepto de "rendición de cuentas" o agrupación de gastos: no.
- Gastos a crédito en caja POA: no existen (POA es siempre contado).
- Permisos por caja: no. Quien puede registrar gastos, ingresos o pagos lo hace en cualquier caja. *(Decisión del agente.)*
- Proyectos/Tareas (otro pedido del cliente): diseño aparte, después.
- Importador de planillas ODS: se elimina en esta misma rama en un commit previo (solo código; su esquema queda inutilizado en la DB a propósito). No recibe selector de caja.
- Control POA / asignaciones presupuestales (`FinanzasVistasService.ObtenerControlPoaAsync`, líneas POA y sus asignaciones): no se tocan. Martin va a revisar cómo se relaciona con la caja POA (dijo que sería la misma plata y que las asignaciones serían salidas, no entradas). Cualquier conexión es un cambio futuro aparte. OJO: mover el literal del gasto al pago SÍ toca el aviso de sobregiro por (línea, fuente) de `GastoService`; ver punto 11 de "Diseño decidido". El cálculo de "gastado" de la vista Control POA no cambia (sigue sumando `MontoTotal` de gastos activos por línea).

## Anti-objetivos

- Que un movimiento POA (ingreso o pago) quede con un literal que no sea B/C, o uno Local con B/C.
- Que un gasto a crédito o un pago manual quede en caja POA.
- Gasto a crédito o de caja Local con línea POA (la línea POA solo existe en gastos contado con caja POA).
- Que el saldo de una caja se contamine con movimientos de la otra.
- Una migración que pierda datos existentes: el literal de cada gasto se copia a sus pagos ANTES de quitarlo del gasto, y todo lo existente queda en caja Local.

## Restricciones

- **Datos existentes**: TODOS los ingresos y pagos van a la caja Local (cada pago existente hereda en la migración el literal de su gasto). Martin confirmó que no hay movimientos existentes con literal B o C, así que no violan la regla.
- **Sin acceso al servidor post-instalación**: todo control vive en el cliente o en la DB.
- **Formato es-UY** y convenciones existentes de grillas y formularios (`FormatoEsUy`, `ComandoDeGuardado`, `DataGridColumnasAutoBehavior`, etc.).
- **Migraciones** EF Core (Npgsql) en `src/StockApp.Infrastructure/Migrations`; la API migra al arrancar.
  GOTCHA: el esquema del importador eliminado (`LotesImportacion`, columnas `IdImportacion`, FKs de lote) sigue mapeado a propósito. La migración nueva NO debe traer drops de nada de eso: revisar el `Up`/`Down` generado antes de commitear.
- **Auditoría**: los movimientos ya se auditan (`IAuditLogger.RegistrarAsync` en `IngresoCajaService` y `GastoService`). La caja y el literal del pago tienen que quedar en lo auditado.

## Estado actual (referencia para el builder)

- Entidades de dominio en `src/StockApp.Domain/Entities/`:
  - `IngresoCaja` — tiene `FuenteFinanciamiento`, `Fecha`, `Monto`, `Concepto`.
  - `Gasto` — HOY tiene `FuenteFinanciamientoId` (pasa al pago con este cambio), `RubroGasto`, `Proveedor`, `CondicionPago`, `LineaPoaId` (opcional), colección `Pagos`.
  - `PagoGasto` — `GastoId`, `Fecha` (el saldo de caja impacta acá), `Monto`, `Activo`, `EsAutomatico`; fábrica `PagoGasto.Automatico(...)` para los pagos que se generan solos (gasto contado, ingreso por factura contado). Hoy no tiene fuente: el libro de caja la lee del gasto vía `p.Gasto.FuenteFinanciamiento` (el comentario de la navegación `Gasto` lo dice; actualizarlo).
  - `FuenteFinanciamiento` — hoy solo `Id`, `Nombre` (único), `Activo`. No hay seed: los nombres reales de los literales están en la DB.
- Enums en `src/StockApp.Domain/Enums/` (ej. `CondicionPago`, `EstadoGasto`).
- Servicios de aplicación:
  - `src/StockApp.Application/Finanzas/IngresoCajaService.cs` (valida fuente en `ValidarAsync`).
  - `src/StockApp.Application/Finanzas/GastoService.cs` — `AltaAsync(Gasto, IReadOnlyList<int>?)`, `ModificarAsync(Gasto)`, `RegistrarPagoAsync(PagoGasto)`.
  - `src/StockApp.Application/Finanzas/FuenteFinanciamientoService.cs` (ABM de literales).
  - `src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs` (+ `IngresoPorFacturaDtos.cs`): crea un `Gasto` siempre y, si es contado, le agrega un `PagoGasto.Automatico`.
  - `src/StockApp.Application/Finanzas/FinanzasVistasService.cs` — `ObtenerLibroCajaMesAsync(int anio, int mes)`, `ObtenerLibroCajaAnualAsync(int anio)`, `ObtenerControlPoaAsync(int ejercicio)`; interfaz `IFinanzasVistasService`; DTOs en `FinanzasVistasDtos.cs` (`LibroCajaMesDto`, `LibroCajaAnualDto`, `MovimientoCajaDto`, `TotalPorClaveDto`, `TotalMensualDto`).
- Repositorios (`src/StockApp.Infrastructure/Repositories/`):
  - `IngresoCajaRepository` — `ListarPorRangoAsync(desdeUtc, hastaUtc)`, `TotalActivosAntesDeAsync(fechaUtc)`.
  - `GastoRepository` — `ListarPagosActivosPorRangoAsync(desdeUtc, hastaUtc)`, `TotalPagosActivosAntesDeAsync(fechaUtc)`, `ListarAsync(GastoFiltro)` (filtra por `g.FuenteFinanciamientoId`), `TotalGastadoLineaFuenteAsync` (sobregiro POA por línea y fuente del gasto), `ConIncludes` (incluye la fuente del gasto).
  - Interfaces en `src/StockApp.Application/Interfaces/` (`IIngresoCajaRepository`, `IGastoRepository`, `IFuenteFinanciamientoRepository`).
- API (`src/StockApp.Api/Endpoints/`): `IngresosCajaEndpoints` (`/finanzas/ingresos`), `GastosEndpoints` (`/finanzas/gastos`, incluye `POST /{id}/pagos`), `FuentesFinanciamientoEndpoints` (`/finanzas/fuentes`, `/activas`), `IngresoPorFacturaEndpoints`, `FinanzasVistasEndpoints` (`GET /finanzas/libro-caja?anio=&mes=`).
- ApiClient (`src/StockApp.ApiClient/`): `IngresoCajaApiClient`, `GastoApiClient`, `FuenteFinanciamientoApiClient`, `IngresoPorFacturaApiClient`, `FinanzasVistasApiClient`.
- Presentación (`src/StockApp.Presentation/`):
  - `ViewModels/Finanzas/`: `IngresoFormViewModel`, `IngresosViewModel`, `GastoFormViewModel`, `GastosViewModel`, `PagosGastoViewModel`, `LibroCajaViewModel`, `FuenteFinanciamientoFormViewModel`, `FuenteFinanciamientoListViewModel`.
  - `ViewModels/Movimientos/IngresoPorFacturaViewModel`.
  - Vistas homónimas en `Views/Finanzas/` (`IngresoFormView`, `IngresosView`, `GastoFormView`, `GastosView`, `PagosGastoView`, `LibroCajaView`, `FuenteFinanciamientoFormView`).
- Lectores actuales de la fuente del gasto (relevados con grep en `src/`; el builder confirma con CodeGraph): `GastoService.ValidarAsync` y `AdvertirSobregiroAsync`, `GastoRepository` (ver arriba), `FinanzasVistasService.ObtenerLibroCajaMesAsync` (columna `FuenteNombre` de movimientos y `TotalesPorFuente`, vía `p.Gasto.FuenteFinanciamiento`), `GastosDtos.cs`, `GastosEndpoints`, `GastoApiClient`, `IngresoPorFacturaDtos`/`Service`/`Endpoints`/`ApiClient`, `GastoFormViewModel`/`View`, `GastosViewModel`/`View`, `IngresoPorFacturaViewModel`/`View`, y el PDF del libro de caja (`LibroCajaViewModel`, columna "Fuente").
- Serialización de enums: la API NO registra `JsonStringEnumConverter` (`src/StockApp.Api/Program.cs` solo agrega `DateTimeUnspecifiedAsUtcConverter`), así que los enums existentes (`CondicionPago`, etc.) viajan como número. La caja debe viajar igual.

## Diseño decidido

### 1. Caja

Enum de dominio con dos valores, Local y POA (nombre exacto a criterio del builder, ej. `Caja { Local, Poa }` en `src/StockApp.Domain/Enums/`). Se persiste en `IngresoCaja` y en `PagoGasto`. NO se persiste en `Gasto`. Columna no nula; la migración usa Local para las filas existentes.

### 2. Caja y literal en el pago (decisión de Martin; mecánica: decisión del agente)

- **Martin**: la caja y el literal (`FuenteFinanciamiento`) se eligen en el pago. Un gasto contado los elige en su formulario de alta y quedan en su `PagoGasto.Automatico`. Un gasto a crédito no tiene caja ni literal; cada pago manual es caja Local y elige un literal de caja Local.
- **El literal sale del `Gasto`** *(decisión del agente)*: `Gasto.FuenteFinanciamientoId` deja de ser parte del gasto y pasa a `PagoGasto` (`Caja` + `FuenteFinanciamientoId`, ambos no nulos).
- **Migración** *(decisión del agente)*:
  1. Agregar `Caja` y `FuenteFinanciamientoId` (FK a `FuentesFinanciamiento`) a `PagoGasto`.
  2. Copiar a cada pago existente la fuente de su gasto y `Caja = Local` (SQL en la migración, antes de hacer la columna no nula).
  3. `IngresoCaja` recibe `Caja = Local`.
  4. La columna vieja de `Gasto`: la decide el builder, prefiriendo **dropearla en la misma migración** después del copiado (la alternativa es dejarla nullable sin uso). Si se dropea, el `Down` la recrea y la repuebla desde los pagos lo mejor posible (un gasto sin pagos queda sin fuente; documentarlo en la migración).
  5. Revisar el `Up`/`Down` generado: NO debe traer drops del esquema muerto del importador ODS (ver Restricciones).
- **Todo lo que hoy lee la fuente del gasto pasa a la fuente del pago** (lista en "Estado actual"): validaciones de `GastoService`, `TotalesPorFuente` y la columna `FuenteNombre` del libro de caja, DTOs, formulario de gasto, listados, filtro `GastoFiltro.FuenteFinanciamientoId` (pasa a "gastos con algún pago de esa fuente" o se quita: decide el builder) y el PDF. El aviso de sobregiro por línea POA también pasa a leer el literal del pago automático (ver punto 11).

### 3. Qué literales van en qué caja *(decisión del agente)*

`FuenteFinanciamiento` es un maestro editable, así que no se puede depender del nombre en tiempo de ejecución. Se agrega a `FuenteFinanciamiento` la caja en la que se puede usar (Local o POA).

La migración marca como POA las fuentes B y C y el resto como Local. Las identifica por nombre, con un match tolerante a mayúsculas y espacios (ej. "B", "Literal B", " literal  c "). El builder verifica los nombres reales en la DB del VPS antes de fijar el criterio (no hay seed en el código).

La caja de la fuente es editable en el ABM de fuentes (`FuenteFinanciamientoFormViewModel`/`View`, endpoints `/finanzas/fuentes`, `FuenteFinanciamientoService`). *(Decisiones del agente.)*

- **Alta**: la caja es obligatoria, sin valor por defecto.
- **Cambio de caja de una fuente existente**: prohibido si algún `IngresoCaja` o `PagoGasto` (activo o no) la usa; se responde con un error de conflicto como los existentes del servicio. Una fuente sin uso sí puede cambiar de caja.
- **Clasificación inicial**: la app todavía no está instalada en el municipio (dato de Martin), así que la única DB con datos reales es la del VPS de testing; el builder verifica ahí los nombres de B y C. La migración clasifica por nombre con match tolerante y NO falla si no encuentra B o C (simplemente quedan todas como Local).

### 4. Validación

- **Backend** (servicios de aplicación; es el control que garantiza los anti-objetivos, la UI no alcanza). Error de validación con el mismo mecanismo que las validaciones existentes de cada servicio:
  - `IngresoCajaService`: el literal pertenece a la caja elegida.
  - `GastoService.AltaAsync`/`ModificarAsync` e `IngresoPorFacturaService`, gasto contado: caja y literal obligatorios; el literal pertenece a la caja.
  - Gasto a crédito: se rechaza si viene con caja POA (o cualquier caja/literal, según cómo el builder modele el request).
  - Gasto con línea POA: se rechaza si no es contado con caja POA (incluye ingreso por factura); ver punto 11.
  - `GastoService.RegistrarPagoAsync` (pago manual, incluido el del calendario): la caja es siempre Local (la fija el servicio, no el cliente) y se rechaza un literal de caja POA.
- **Al modificar** *(decisión del agente)*: la validación literal↔caja sigue el patrón existente de `ValidarAsync` para maestros inactivos: solo se rechaza si cambió el literal o la caja. Un movimiento histórico inconsistente se puede seguir editando en otros campos.
- **UI**: donde hay selector de caja, se elige primero la caja y el combo de literal se filtra a los de esa caja; cambiar la caja con un literal ya elegido que no corresponde lo limpia. Donde no hay selector (pago manual), el combo muestra solo literales de caja Local.

### 5. Formularios

- `IngresoFormViewModel`/`IngresoFormView`: selector de caja + literal filtrado.
- `GastoFormViewModel`/`GastoFormView` e `IngresoPorFacturaViewModel`/`View`: selector de caja + literal filtrado visibles SOLO si la condición es contado (van al pago automático). En crédito no hay caja ni literal. El selector de línea POA solo se habilita/muestra cuando la condición es contado y la caja elegida es POA (punto 11).
- `PagosGastoViewModel`/`PagosGastoView` y el pago desde el calendario: sin selector de caja; combo de literal con los de caja Local. La grilla de pagos muestra el literal (y la caja, si es barato) de cada pago.
- Valor por defecto del selector de caja: decisión del builder (sugerencia: Local).
- Aviso de caja sin literales *(decisión del agente)*: si la caja elegida no tiene literales disponibles (ej. POA sin fuentes asignadas), el formulario muestra un aviso claro ("No hay literales asignados a la caja POA; configuralos en Fuentes de financiamiento") en vez de un combo vacío mudo. Ídem en el pago manual si no hay literales Local.

### 6. Libro de caja *(decisión del agente)*

- Selector de caja (Local / POA) en `LibroCajaViewModel`/`LibroCajaView`.
- Saldo inicial, movimientos, saldo corrido, totales por rubro, totales por fuente y modo anual se calculan solo con la caja elegida. El filtro se aplica a `IngresoCaja.Caja` y a `PagoGasto.Caja`. Concretamente: `ObtenerLibroCajaMesAsync` y `ObtenerLibroCajaAnualAsync` reciben la caja, y la pasan a `IngresoCajaRepository.ListarPorRangoAsync`/`TotalActivosAntesDeAsync` y a `GastoRepository.ListarPagosActivosPorRangoAsync`/`TotalPagosActivosAntesDeAsync`. Filtrar en el repositorio, no en memoria.
- Los totales por fuente y la columna `FuenteNombre` usan la fuente del **pago** (no la del gasto). Son la apertura por literal (en POA: B y C).
- El endpoint `GET /finanzas/libro-caja` recibe la caja; `FinanzasVistasApiClient` la envía.
- El título del PDF exportado indica la caja (y conviene que el nombre de archivo también, hoy `libro-caja-AAAA-MM.pdf`).

### 7. Listados *(decisión del agente)*

- `IngresosViewModel`/`IngresosView`: columnas caja y literal.
- `GastosViewModel`/`GastosView`: caja y literal del pago automático si el gasto es contado; vacío (o "—") si es crédito, porque cada pago puede tener otro literal.
- Un filtro por caja entra si es barato.

### 8. API, DTOs y ApiClient

- Ingresos: los requests de alta/modificación y el DTO de lectura llevan caja (ej. `CrearIngresoCajaRequest`, `ModificarIngresoCajaRequest`).
- Gastos: los requests de alta/modificación (`CrearGastoRequest`, `ModificarGastoRequest`) y el DTO de ingreso por factura llevan caja y literal opcionales (obligatorios solo en contado); `GastoDto` deja de tener la fuente propia y expone la del pago automático cuando es contado.
- Pagos: el request de pago manual (`POST /finanzas/gastos/{id}/pagos`) lleva el literal (no la caja); el DTO de pago expone caja y literal.
- Los DTOs de fuente llevan su caja.
- `IngresoCajaApiClient`, `GastoApiClient`, `FuenteFinanciamientoApiClient`, `IngresoPorFacturaApiClient`, `FinanzasVistasApiClient` y los stubs/fakes de tests respetan el contrato real: la caja serializada igual que los otros enums del proyecto (número, ver "Estado actual").

### 9. Cambio de caja y literal en la edición *(decisiones del agente)*

- **Gasto contado**: editarlo puede cambiar la caja y el literal de su pago automático (es corregir una carga, igual que en un ingreso), sujeto a la validación literal↔caja del punto 4 (solo si cambiaron).
- **Gasto a crédito**: no tiene caja ni literal que editar. El literal de un pago manual ya registrado no se edita (los pagos hoy se anulan, no se modifican; se mantiene).
- **Cambio de condición contado↔crédito**: ya está prohibido hoy en `GastoService.ModificarAsync`; se mantiene.
- **Ingreso de caja**: el cambio de caja está permitido (es corregir un error de carga), sujeto a la validación del punto 4.
- **Fuente de financiamiento**: ver punto 3.

### 10. Calendario de pagos *(decisión del agente)*

Lo pendiente es siempre crédito, y el crédito es siempre caja Local: el calendario NO necesita columna de caja y queda FUERA del alcance, salvo que el pago hecho desde ahí pida el literal (Local) como cualquier pago manual (punto 5).

### 11. Línea POA y aviso de sobregiro (decisión de Martin)

- La línea POA solo tiene sentido en gastos de la caja POA. Como lo POA es siempre contado, un gasto con línea POA es siempre contado con caja POA, y su literal (B o C) se elige en el mismo formulario de alta y queda en el pago automático.
- **Regla**: el backend rechaza un gasto con línea POA si no es contado con caja POA (incluye ingreso por factura). En la UI, el selector de línea POA solo se habilita/muestra cuando la condición es contado y la caja elegida es POA.
- **Aviso de sobregiro**: sigue funcionando por línea + literal, pero toma el literal del formulario / pago automático en vez de `Gasto.FuenteFinanciamientoId` (`GastoService.AdvertirSobregiroAsync`). `GastoRepository.TotalGastadoLineaFuenteAsync` pasa a leer la fuente desde el pago automático del gasto.
- **Datos existentes**: si algún gasto existente tiene línea POA, queda en caja Local (migración "todo a Local"); la migración no lo corrige. Al editarlo aplica la regla de "solo se rechaza si cambió" (literal, caja o línea). El builder verifica en la DB del VPS de testing cuántos gastos tienen línea POA y lo anota en el plan/PR.
- **Control POA** (vista): no cambia su cálculo de "gastado" (sigue sumando `MontoTotal` de gastos activos por línea) y sigue fuera de alcance.

## Lo que queda al builder

Nombres exactos, valor por defecto del selector, si el filtro en listados entra, qué hacer con la columna vieja `Gasto.FuenteFinanciamientoId` (preferencia: dropearla tras el copiado), qué hacer con `GastoFiltro.FuenteFinanciamientoId`, y orden de tasks.

Método: TDD por capas como el resto del proyecto. Gates de UI (filtro de literales por caja, selector presente) con test en `tests/StockApp.Presentation.UiTests` sobre la View real, no solo sobre el ViewModel. Verificación orgánica final con la app real usando el toolkit GUI de `scripts/gui-verificacion/`, incluyendo: alta de ingreso POA y de gasto contado POA, intento de literal cruzado rechazado, gasto a crédito con pago manual (literal Local), y libro de caja Local vs. POA con saldos independientes. Incluir el rechazo de línea POA en gasto a crédito o de caja Local y el aviso de sobregiro por línea+literal del pago automático. Verificar también que la migración copió la fuente de cada gasto existente a sus pagos (conteo antes/después en la DB).

## Preguntas abiertas

- Relación Control POA / asignaciones ↔ caja POA (Martin la revisa; fuera de alcance).

## Decisiones del agente para revisar

- A. Mecánica del traslado del literal: `Caja` + `FuenteFinanciamientoId` en `PagoGasto`; migración que copia la fuente del gasto a cada pago y pone caja Local (también en `IngresoCaja`); preferencia por dropear la columna de `Gasto` en la misma migración, sin drops del esquema del importador ODS (punto 2).
- B. Editar un gasto contado puede cambiar caja y literal de su pago automático (punto 9).
- C. Validación literal↔caja al modificar: solo se rechaza si cambió el literal o la caja (patrón de `ValidarAsync`) (punto 4).
- D. Cambio de condición contado↔crédito: sigue prohibido (punto 9).
- E. Calendario de pagos sin columna de caja: fuera de alcance (punto 10).
- F. Cambio de caja de una fuente en el ABM: prohibido si algún `IngresoCaja` o `PagoGasto` la usa; sin uso, se puede. Alta de fuente: caja obligatoria sin default (punto 3).
- G. Libro de caja: filtro por `IngresoCaja.Caja` y `PagoGasto.Caja`; totales por fuente con la fuente del pago (punto 6).
- H. Listados: caja y literal en ingresos; en gastos, los del pago automático si es contado (punto 7).
- I. Caja en `FuenteFinanciamiento` para determinar qué literales van en cada caja; clasificación inicial por nombre tolerante, sin fallar si faltan B/C, verificada en la DB del VPS de testing; aviso claro si la caja no tiene literales (puntos 3 y 5).
- J. Sin permisos por caja (No-objetivos).

(La regla de línea POA solo en contado con caja POA y el aviso de sobregiro por línea+literal del pago son decisión de Martin, no del agente: punto 11.)
