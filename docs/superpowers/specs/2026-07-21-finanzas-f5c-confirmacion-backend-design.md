# Diseño: F5c — Confirmación transaccional del importador

**Fecha**: 2026-07-21
**Estado**: Validado con el usuario, pendiente de implementación

## 1. Contexto

F5a (parser `.ods`, `IPlanillaParser`/`PlanillaOdsParser`) y F5b (análisis backend read-only, `POST /finanzas/importar/analizar`, `AnalisisImportacionService`) están mergeadas a main. F5c es **SOLO BACKEND**: el endpoint de confirmación y la escritura transaccional. La UI (`ApiClient`, grilla editable, pantalla) es F5d — explícitamente fuera de alcance de esta fase.

### Restricción operativa (condiciona todo el diseño)

Una vez instalado el sistema, el usuario **no va a tener acceso al servidor por mucho tiempo**. De acá se deriva una regla dura para F5c: **todo control operativo tiene que viajar desde el cliente o vivir en la base de datos — nada que dependa de editar configuración o entrar al servidor.** Cualquier mecanismo de control (decidir si se puede re-importar, revertir una corrida mala) tiene que ser accionable con una request HTTP desde el desktop del admin, nunca con un cambio de `appsettings`/variables de entorno/redeploy.

## 2. Decisiones tomadas

### 2.1 Corte de fases

F5c = backend (confirmar + escritura + idempotencia + auditoría + reversa). F5d = `ApiClient` + grilla editable + pantalla + ítem admin-only en el sidebar. Mismo corte que F5a/F5b: primero el contrato y la lógica de servidor, testeados end-to-end sin UI; después el consumo desde el desktop.

### 2.2 Idempotencia = re-ejecutable

`/confirmar` se puede correr N veces; lo ya existente por clave natural se saltea y se reporta como omitido. Motivación real: `docs/finanzas-discrepancias-planilla-poa-2026.md` obliga a reconciliar la planilla antes de migrar, y el usuario va a querer corregir datos y volver a correr la importación sobre lo mismo sin duplicar nada.

### 2.3 Compromisos POA se escriben como gasto a crédito sin pago

Los movimientos clasificados `ClasificacionReconciliacion.CompromisoSoloPoa` (F5b) se escriben como `Gasto` con `CondicionPago.Credito`, `LineaPoaId` asignado y CERO `PagoGasto`. Así:

- `SaldoPendiente == MontoTotal` refleja que es un compromiso pendiente, no una factura pagada.
- El saldo de la línea POA cuadra con la planilla.
- El Control POA (F4) lo muestra sin cambios, porque ya calcula saldo a partir de gastos activos con su `LineaPoaId`.

Descartado:
- **No escribirlos**: el saldo POA no cuadraría contra la planilla (el compromiso resta presupuesto disponible aunque no tenga pago).
- **Escribirlos de contado**: mentiría a la caja (un pago que nunca ocurrió) y desbalancearía el saldo de junio ya validado en F5b (§11).

### 2.4 Contrato estricto para campos obligatorios del dominio

El payload de `/confirmar` es un DTO propio (no reutiliza los DTOs de análisis de F5b) con los campos obligatorios del dominio marcados NO nullable. Si al validar falta uno → `400` con la fila y el campo señalados.

Se descartó el sentinel "SIN CLASIFICAR" porque nadie limpia esos registros después — quedarían gastos y líneas POA con datos basura persistidos permanentemente.

Gaps concretos que este contrato cubre (campos que el análisis de F5b no puede completar y el dominio exige):
- `LineaPoa.Programa` — obligatorio en el dominio (`AppDbContext.cs:130`, `IsRequired()`), pero el análisis lo deja vacío con advertencia (F5b, resolución pre-flight 8: "no se inventa dato").
- `Gasto.RubroGastoId` — obligatorio en el dominio, pero `GastoAnalizadoDto.CodigoRubro` es `int?` en el análisis.
- `Gasto.Detalle` — obligatorio en el dominio (`AppDbContext.cs:154`, `IsRequired()`), pero `GastoAnalizadoDto.Detalle` es `string?` en el análisis.
- `IngresoCaja.FuenteFinanciamientoId` — obligatorio en el dominio, pero el saldo inicial sintético de enero viene con `Fuente = null` en `IngresoAnalizadoDto` (F5b, mapeo "Saldo inicial de enero").
- `Gasto.RubroGastoId` y `Gasto.Fecha` para los compromisos POA (`ClasificacionReconciliacion.CompromisoSoloPoa`, §2.3) — acá el gap es más profundo que en los casos anteriores: `MovimientoPoaAnalizadoDto` (F5b) directamente NO TIENE ninguno de los dos campos, porque la planilla POA no los registra (el rubro es una dimensión de la planilla de Gastos, no de la POA; una hoja de línea POA no tiene columna de fecha por movimiento). No es un `null` opcional que el análisis podría haber completado — es información que no existe en ningún lado de los datos de origen.
- `Gasto.FechaVencimiento` para los mismos compromisos POA, cuando `Condicion == CondicionPago.Credito` — ver el desarrollo completo más abajo, después del contrato.

El saldo inicial viaja como un ingreso más dentro de `Ingresos`, con su `Fuente` obligatoria igual que cualquier otro — no tiene tratamiento especial en el contrato de confirmación.

**Resolución para `RubroGastoId` y `Fecha` de los compromisos POA: el humano los completa en la grilla de F5d**, igual que cualquier otro campo faltante de esta lista. Se descarta explícitamente crear un rubro sentinel tipo "COMPROMISO POA" — sería exactamente el patrón que esta misma decisión rechaza para el resto de los gaps (el sentinel "SIN CLASIFICAR" de más arriba). Consecuencia operativa, anotada también en §9: en F5d el admin va a tener que elegir rubro y fecha a mano para cada movimiento POA clasificado `CompromisoSoloPoa` — el trabajo es proporcional a cuántos haya.

### 2.5 Contrato stateless

F5d manda el payload completo ya corregido en un solo JSON. Sin tablas de staging, sin id de lote pre-generado por el cliente, sin re-parseo de los `.ods` en el confirm.

Descartado:
- **Stateful** (2-3 tablas nuevas para persistir el resultado del análisis entre pasos): sobredimensionado para una herramienta que corre un puñado de veces en la vida del sistema.
- **Re-parseo** (el confirm vuelve a abrir los `.ods` y aplica las correcciones sobre las filas parseadas): ata las correcciones del usuario a coordenadas posicionales del parseo — frágil ante cualquier reordenamiento de filas entre el análisis y la confirmación.

### 2.6 Control de re-importación: guard derivado de la auditoría, NO un flag de configuración

**Se descarta por completo la idea de un flag de configuración `Importacion:Habilitada`** (`appsettings.json` + condicional en `Program.cs`) que una versión anterior de este diseño proponía. Razón, derivada directamente de la restricción operativa de §1: un flag de configuración vive en el servidor; sin acceso al servidor queda congelado en el estado del día de la instalación. Si queda apagado, nunca más se puede re-importar. Si queda prendido, nunca se apaga. Es exactamente lo contrario de un interruptor: es una decisión de un solo uso disfrazada de toggle.

**Reemplazo — guard derivado de la auditoría**:
- `/confirmar` consulta si el ejercicio ya tiene un `LogAuditoria` de `AccionAuditada.ImportacionPlanillas` que no esté revertido (ver §2.7 — "no revertido" se resuelve contra las corridas dadas de baja). Si lo tiene → `409`, salvo que el payload traiga `Forzar = true`.
- `ConfirmarImportacionDto` suma un campo `bool Forzar = false`.
- El control de "se puede volver a importar o no" pasa a ser **estado derivado de datos en la base** (hay o no hay una corrida previa no revertida), no configuración del servidor. El permiso para re-correr viaja en el payload — es decir, se decide desde el desktop del admin. Cero necesidad de acceso al servidor, en cualquier escenario.
- La superficie de zip bomb de `/analizar` (descomprime `.ods` con `ZipArchive`, F5a) se mitiga con un **límite de tamaño de archivo** en el upload multipart, no apagando el endpoint. Es una mitigación real, no depende de que nadie toque nada, y no choca con la restricción de §1. `/confirmar` recibe JSON plano (sin `ZipArchive`, sin descompresión), así que no tiene la misma superficie de zip bomb; igual conviene un límite de tamaño de body como defensa en profundidad contra un payload de abuso, pero es una mitigación distinta y con un motivo distinto (recursos, no zip bomb).

### 2.7 Reversa por lote

Motivación: re-importar SUMA, no CORRIGE. Ejemplo concreto: si un gasto entró con el monto equivocado, el monto es parte de la clave natural (§4), así que el dedupe no lo reconoce como el mismo gasto y crea uno nuevo — quedan los dos, uno bueno y uno con el monto malo. Sumado a que la planilla POA ya se sabe inconsistente (`docs/finanzas-discrepancias-planilla-poa-2026.md`) y a que, por la restricción de §1, no hay acceso al servidor para corregir a mano, un error de migración sin mecanismo de reversa es **permanente**.

**Mecanismo**:
- Cada corrida de `/confirmar` genera un `IdImportacion` (`Guid`) que se estampa en el `LogAuditoria` de la corrida y en cada registro que esa corrida crea.
- Columna nueva `IdImportacion` (`Guid?`, nullable, índice no-único) en `Gasto`, `IngresoCaja` y `LineaPoa`. Nullable porque todo lo cargado a mano (la inmensa mayoría de los datos, hoy y a futuro) la tiene en `null`.
- `PagoGasto` **no lleva columna propia**: se revierte por cascada siguiendo `GastoId` (los pagos automáticos que `/confirmar` crea para los gastos `CondicionPago.Contado` son hijos de un `Gasto` con `IdImportacion` seteado).
- `AsignacionPresupuestal` **no lleva columna propia**: es hija del agregado `LineaPoa` y no tiene un `Activo` propio en el dominio (`AsignacionPresupuestal.cs` — solo `Id`, `LineaPoaId`, `FuenteFinanciamientoId`, `Monto`); al revertir queda colgando de una `LineaPoa` inactiva, que es el estado correcto (no se muestra en ningún lado que filtre por `LineaPoa.Activo`).
- **Los maestros (`Proveedor`, `FuenteFinanciamiento`, `RubroGasto`) NO se revierten**, aunque la corrida los haya creado. Razón: para cuando se ejecuta la reversa, esos maestros pueden estar ya referenciados por gastos cargados a mano (el sistema sigue operando en paralelo a la migración). Darlos de baja rompería datos que nunca fueron parte del lote. Un maestro de más es inocuo (aparece en un combo, nadie lo usa); un gasto huérfano por una FK a un maestro inactivo es un bug.
- `POST /finanzas/importar/revertir/{id}` — baja lógica (`Activo = false`) de todo el lote (`Gasto`, `IngresoCaja`, `LineaPoa` con ese `IdImportacion`, más sus `PagoGasto`/`AsignacionPresupuestal` hijos) en UNA transacción, mismo patrón que `/confirmar`. Permiso: reusa `Permisos.ImportarPlanillas` — no hace falta un permiso nuevo, es la misma operación de migración.
- La reversa se audita: `AccionAuditada.ReversionImportacion = 43` (append-only, siguiente valor después de `ImportacionPlanillas = 42`, ver §7).
- El guard del `409` de §2.6 mira específicamente importaciones **no revertidas**: si se revirtió una corrida, se puede volver a importar limpio sobre el mismo ejercicio sin necesidad de `Forzar`.
- Respuesta: `ResultadoReversionDto` con contadores de registros dados de baja por tipo (gastos, pagos, ingresos, líneas POA, asignaciones).

## 3. Contrato de los endpoints

### `POST /finanzas/importar/confirmar`

- Body: JSON (no multipart — a diferencia de `/analizar`, que recibe los `.ods`).
- `.RequireAuthorization(Permisos.ImportarPlanillas)` — Admin-only, permiso ya existe (`Permisos.cs:27`, `"finanzas.importar"`).
- Guard de re-importación (§2.6): si el `Ejercicio` ya tiene una importación previa no revertida y `Forzar == false` → `409`. Con `Forzar == true`, se ignora el guard y se corre igual (la idempotencia por clave natural de §4 sigue aplicando dentro de esa corrida).

### DTOs nuevos (`StockApp.Application/Finanzas`)

Los maestros se referencian por **NOMBRE**, no por `Id`, porque la mayoría no existe todavía en la base — el servidor resuelve nombre→Id con get-or-create dentro de la transacción.

```csharp
ConfirmarImportacionDto(
    int Ejercicio,
    bool Forzar,   // default false — permite re-confirmar sobre una corrida previa no revertida
    MaestrosNuevosConfirmarDto MaestrosNuevos,
    IReadOnlyList<IngresoConfirmarDto> Ingresos,
    IReadOnlyList<GastoConfirmarDto> Gastos,
    IReadOnlyList<LineaPoaConfirmarDto> LineasPoa)

MaestrosNuevosConfirmarDto(
    IReadOnlyList<string> Proveedores,
    IReadOnlyList<string> Fuentes,
    IReadOnlyList<RubroNuevoConfirmarDto> Rubros)

RubroNuevoConfirmarDto(int Codigo, string Nombre)

IngresoConfirmarDto(DateOnly Fecha, string Concepto, decimal Monto, string Fuente)

GastoConfirmarDto(string Proveedor, string? NumeroFactura, string? NumeroOrden,
                  string Detalle, string? Destino, DateOnly Fecha, decimal MontoTotal,
                  string Fuente, int CodigoRubro, string? LineaPoa, CondicionPago Condicion,
                  DateOnly? FechaVencimiento)

LineaPoaConfirmarDto(string Nombre, string Programa,
                     IReadOnlyList<AsignacionConfirmarDto> Asignaciones)

AsignacionConfirmarDto(string Fuente, decimal Monto)
```

**`FechaVencimiento` — nullable en el tipo, obligatoria por regla de negocio para `Credito`**: `GastoService.AltaAsync` ya exige `FechaVencimiento` para todo gasto con `CondicionPago.Credito` y prohíbe que la lleve un gasto `Contado` (`GastoService.cs:272-275`, `ValidarAsync`, lanza `ReglaDeNegocioException` en ambos sentidos). Los compromisos POA que F5c crea son exactamente gastos `Credito` (§2.3). `ConfirmacionImportacionService` aplica la misma regla sobre el payload: si `Condicion == CondicionPago.Credito` y `FechaVencimiento` viene `null` → `400` con la clave `Gastos[i].FechaVencimiento`. Razón: sin vencimiento, un gasto a crédito nunca aparece en el Calendario de Pagos (F4) — quedaría un compromiso invisible, exactamente el tipo de dato "perdido en la migración" que §2.7 (reversa) existe para poder corregir, pero es mejor no dejarlo entrar en primer lugar.

Nota técnica: `ImportacionRepository` (§5) escribe las entidades directo contra el `DbContext`, sin pasar por `GastoService` — mismo patrón que el resto del repositorio de importación. No hay ningún bloqueo técnico que fuerce esta regla en ese camino de escritura. Se aplica igual, en `ConfirmacionImportacionService` antes de delegar al repositorio, por coherencia del dominio (un gasto a crédito sin vencimiento sería un estado que el alta manual nunca permite crear), no porque algo la fuerce automáticamente.

**Regla de cierre**: toda referencia nominal (proveedor, fuente, rubro por código, línea POA) tiene que resolver contra un maestro ya existente en la base o contra uno declarado en `MaestrosNuevos`. Si no resuelve → `400`. Nada se crea por accidente fuera de lo que el usuario declaró explícitamente.

Respuesta feliz: `ResultadoConfirmacionDto` con `IdImportacion` (el `Guid` del lote, necesario para poder revertirlo después) y contadores de creados y omitidos por tipo (proveedores, fuentes, rubros, líneas POA, asignaciones, ingresos, gastos, pagos).

### `POST /finanzas/importar/revertir/{id}`

- `{id}` es el `IdImportacion` (`Guid`) devuelto por `/confirmar`.
- `.RequireAuthorization(Permisos.ImportarPlanillas)` — mismo permiso, sin uno nuevo.
- `404` si no existe ningún `LogAuditoria` de `ImportacionPlanillas` con ese `IdImportacion`. `409` si ya fue revertido antes (la reversa no es idempotente por diseño: revertir dos veces la misma corrida no tiene un significado adicional).
- Respuesta feliz: `ResultadoReversionDto` con contadores de dados de baja por tipo.

### Límite de tamaño de upload

`/analizar` (multipart, recibe los `.ods`) lleva un límite de tamaño de archivo — la mitigación real contra zip bomb, porque acota el input comprimido antes de que `ZipArchive` lo descomprima. `/confirmar` y `/revertir/{id}` (JSON) llevan un límite de tamaño de body más generoso, como defensa en profundidad contra un payload de abuso — no mitigan zip bomb porque no descomprimen nada, así que ahí el límite es solo un techo razonable, no una mitigación de la misma clase de riesgo.

## 4. Idempotencia — claves naturales y migración de trazabilidad

| Entidad | Clave natural | Índice único en la DB |
|---|---|---|
| `FuenteFinanciamiento` | `Nombre` | Ya existe (`AppDbContext.cs:116`) |
| `RubroGasto` | `Codigo` | Ya existe (`AppDbContext.cs:122`) |
| `Proveedor` | `Nombre` | Ya existe (`AppDbContext.cs:74`) |
| `LineaPoa` | `(Nombre, Ejercicio)` | Ya existe (`AppDbContext.cs:131`) |
| `AsignacionPresupuestal` | `(LineaPoaId, FuenteFinanciamientoId)` | Ya existe (`AppDbContext.cs:142`) |
| `IngresoCaja` | `(Fecha, Concepto, Monto, FuenteFinanciamientoId)` | NUEVA, sin índice |
| `Gasto` | `(ProveedorId, NumeroFactura, NumeroOrden, Fecha, MontoTotal)` | NUEVA, sin índice |

Maestros y líneas POA: get-or-create (si la clave existe, se reutiliza el `Id`; si no, se crea). Ingresos y gastos: si la clave natural ya está presente en la base → se saltea y se cuenta como omitido.

**Matiz importante — corrección respecto de una versión anterior de este documento**: F5c **sí trae una migración EF** (las columnas `IdImportacion` en `Gasto`, `IngresoCaja` y `LineaPoa` de §2.7, más sus índices no-únicos, necesarias para poder ubicar y revertir un lote). Lo que se sostiene sin cambios es que **NO se agregan índices ÚNICOS nuevos** — ese seguía siendo el argumento original: un índice único sobre `Gasto (ProveedorId, Factura, Orden, Fecha, Monto)` rompería la carga manual legítima (mismo proveedor, sin factura, misma fecha, mismo monto, `Detalle` distinto son dos gastos válidos que hoy se pueden cargar sin problema). El índice parcial `(ProveedorId, NumeroFactura)` sobre activos que ya existe (`AppDbContext.cs:165-167`, migración `20260716181915_UniqueFacturaProveedorGastosActivos`) cubre el caso que sí importa proteger (factura duplicada del mismo proveedor). Una columna de trazabilidad con índice no-único no le impone ninguna regla nueva a nadie que no pase por el importador; un índice único sí. Son decisiones distintas y no se contradicen entre sí.

El dedupe por clave natural vive dentro del importador: al abrir la transacción se cargan en memoria las claves naturales del ejercicio ya presentes en la base y se filtra el payload contra ese set.

### Riesgo asumido y su mitigación

Check-then-act (cargar claves en memoria, filtrar, insertar) tiene una carrera si dos confirmaciones corren concurrentes sobre el mismo ejercicio. Se mitiga con `pg_advisory_xact_lock` sobre el ejercicio al abrir la transacción — se libera solo, automáticamente, en commit o rollback. El mismo lock protege también el guard de `409` de §2.6 (la lectura de "¿hay una corrida previa no revertida?" y la escritura del nuevo `LogAuditoria` quedan serializadas entre confirmaciones concurrentes del mismo ejercicio).

Hay precedente de SQL crudo dentro de un repositorio en `GastoRepository.RegistrarPagoAtomicoAsync` (`GastoRepository.cs:138-175`), que usa `FromSqlInterpolated` con `FOR UPDATE` para serializar pagos concurrentes sobre el mismo gasto — mismo principio: guard atómico dentro de la transacción, no check-then-insert desnudo en memoria.

## 5. Arquitectura y transacción

- **`IConfirmacionImportacionService` / `ConfirmacionImportacionService`** (`StockApp.Application/Finanzas`): verifica `Permisos.ImportarPlanillas` vía `ICurrentSession` + `IAuthorizationService` — mismo arranque que `AnalisisImportacionService.AnalizarAsync` (`_auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas)`) — valida el payload COMPLETO, resuelve el guard de `409`/`Forzar`, y recién ahí delega la escritura. El mismo servicio (o uno hermano, a definir en tasks) expone la reversa.
- **`IImportacionRepository`** (`StockApp.Application/Interfaces`) / **`ImportacionRepository`** (`StockApp.Infrastructure/Repositories`): abre la transacción tanto para confirmar como para revertir. Es el único lugar donde puede vivir sin que Application referencie EF/Npgsql — mismo criterio documentado en el comentario de `GastoRepository.cs:113-121` ("en el repo, que es quien referencia Npgsql — Application NO referencia EF/Npgsql para mantener la capa desacoplada").
- **Patrón a imitar**: `MovimientoStockRepository.RegistrarMovimientoAtomicoAsync` (`MovimientoStockRepository.cs:40-101`) — `await using var tx = await _ctx.Database.BeginTransactionAsync()`, todo el trabajo dentro, UN solo `SaveChangesAsync()`, `tx.CommitAsync()` al final, rollback explícito en cada rama de fallo.
- **Migración EF nueva** (§4): columnas `IdImportacion` (`Guid?`) + índices no-únicos en `Gasto`, `IngresoCaja`, `LineaPoa`.

### Orden dentro de la transacción de `/confirmar`

```
BeginTransaction → pg_advisory_xact_lock(ejercicio)
  0. Guard: ¿hay LogAuditoria de ImportacionPlanillas no revertido para este ejercicio?
     → sí y Forzar == false: Rollback + 409
  1. Fuentes, Rubros, Proveedores nuevos (get-or-create)
  2. LineasPoa + AsignacionPresupuestal (get-or-create), IdImportacion en las LineasPoa nuevas
  3. IngresosCaja (dedupe por clave natural, insertar los nuevos con IdImportacion)
  4. Gastos (dedupe por clave natural) + PagoGasto por el total para los de CondicionPago.Contado,
     IdImportacion en los Gastos nuevos
  5. LogAuditoria (IdImportacion del lote, resumen de la corrida)
→ un solo SaveChangesAsync → Commit
```

### Orden dentro de la transacción de `/revertir/{id}`

```
BeginTransaction
  1. Ubicar LogAuditoria de ImportacionPlanillas con ese IdImportacion → 404 si no existe, 409 si ya revertido
  2. Baja lógica (Activo = false) de Gasto, IngresoCaja, LineaPoa con ese IdImportacion
  3. Baja lógica en cascada de PagoGasto (por GastoId) y AsignacionPresupuestal (por LineaPoaId)
     de los registros dados de baja en el paso 2
  4. Maestros (Proveedor, FuenteFinanciamiento, RubroGasto): SIN TOCAR
  5. LogAuditoria (ReversionImportacion, referenciando el IdImportacion revertido)
→ un solo SaveChangesAsync → Commit
```

## 6. Errores de validación con estructura

Excepción nueva `ValidacionImportacionException` que lleva `IReadOnlyDictionary<string, string[]>`, más un caso nuevo en `DomainExceptionHandler` (`StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`) que la mapea a `Results.ValidationProblem`.

Ejemplo de respuesta:

```json
{
  "Gastos[12].CodigoRubro": ["El rubro 340 no existe ni fue declarado nuevo"],
  "LineasPoa[3].Programa":  ["Requerido"]
}
```

Todo sigue saliendo del handler global, como el resto del repo — sin un camino de error paralelo para este endpoint. F5d ancla cada error a su celda en la grilla usando la clave `Tipo[índice].Campo`.

El guard de `409` de §2.6 y el `409`/`404` de `/revertir/{id}` (§3) NO son errores de validación estructurada: son `ReglaDeNegocioException`/`EntidadNoEncontradaException` comunes, mapeadas por el mismo `DomainExceptionHandler` que ya traduce `ReglaDeNegocioException → 409` y `EntidadNoEncontradaException → 404` (`DomainExceptionHandler.cs:25-26`) para el resto de la app — sin caso especial nuevo en el handler para esto.

## 7. Auditoría

`AccionAuditada` es append-only y hoy llega a `BajaAdjunto = 41` (`AccionAuditada.cs:59`) → se agregan dos valores nuevos:
- `ImportacionPlanillas = 42` — una corrida de `/confirmar`.
- `ReversionImportacion = 43` — una corrida de `/revertir/{id}`.

Un `LogAuditoria` por corrida de `/confirmar`, con el `IdImportacion` del lote y el resumen de creados/omitidos en el detalle, insertado DENTRO de la transacción: si la transacción rollbackea (incluido el rollback del guard de `409`), no queda rastro de una importación que no llegó a pasar. Un `LogAuditoria` por corrida de `/revertir/{id}`, referenciando el `IdImportacion` revertido.

## 8. Testing

Tres capas, mismo patrón que F5a/F5b:

- **`Application.Tests`**: validación pura con fakes.
  **Gotcha**: los fakes de maestros en `tests/StockApp.Application.Tests/Finanzas/Fakes/RepositorioMaestrosFake.cs` tiran `NotSupportedException` en los métodos de escritura porque F5b es read-only — hay que extenderlos para que F5c pueda ejercitar el get-or-create.
- **`Infrastructure.Tests`**: transacción contra Postgres real (`Fixtures/PostgresFixture.cs` + `PostgresRepositoryTestBase`). Cubre atomicidad (un fallo en el paso 4 deja la base intacta), idempotencia (segunda corrida = 0 creados, todo omitido) y reversa.
- **`Api.Tests`**: matriz 401/403, `400` estructurado, `409` (guard y `Forzar`), `404` (revertir un id inexistente), `200` feliz. Infra ya lista (`Fixtures/ApiFactory.cs` con Testcontainers `postgres:16-alpine` + `ApiTestBase`).

### Criterio de aceptación duro de la fase

End-to-end con las planillas reales (gitignored, en `tests/StockApp.Api.Tests/Fixtures/Finanzas/`): `/analizar` → completar los obligatorios → `/confirmar` → consultar la base vía `Factory.CrearContexto()` y aseverar:

- Caja de junio 2026 = **43.705**
- Saldo POA Literal B = **6.341.849**
- Saldo POA Literal C = **4.174.206**

**Por qué el oráculo POA cambió respecto de F5b (§11 decía 6.643.349 / 4.654.206) — no es un error de este documento, es una consecuencia de que cambió qué se está probando**: F5b probaba el PARSER, así que el oráculo correcto era el valor cacheado en la hoja "SALDO TOTALES" del `.ods` (`ResultadoAnalisisDto.SaldosPoa`) — un número único y estable, perfecto para verificar que F5a/F5b leen la planilla sin corromper nada. F5c prueba lo que se PERSISTIÓ en la base, y F5c (§10) escribe las `AsignacionPresupuestal` derivadas de las hojas de LÍNEA, no del resumen. `docs/finanzas-discrepancias-planilla-poa-2026.md` documenta que esas dos fuentes están desincronizadas en la planilla real: la suma de las hojas de línea da **6.341.849 / 4.174.206** (Literal B / C), **301.500 y 480.000 menos** que "SALDO TOTALES" respectivamente. El número correcto para un test que consulta la base después de `/confirmar` es la suma de las hojas de línea — es literalmente lo que quedó escrito. Usar 6.643.349/4.654.206 acá produciría un test que falla siempre, incluso contra una implementación correcta.

Si el análisis de F5b reporta movimientos POA clasificados `Dudoso` (`Resumen.PoaDudosos > 0`), el saldo persistido puede quedar por encima del esperado en la magnitud exacta del `Importe` de esos movimientos — un movimiento `Dudoso` nunca se convierte en `Gasto` automáticamente, es por diseño una decisión manual de F5d. Hay que revisar `PoaDudosos` antes de fijar el assert.

La caja de junio 2026 (**43.705**) NO cambia y sigue siendo el mismo número de §11 que F5b validó en memoria: sale enteramente de las hojas de la planilla de Gastos (Ingresos/Egresos), un dato independiente de la reconciliación POA y ajeno a la discrepancia de "SALDO TOTALES".

### Tests nuevos específicos de esta fase

- **Trazabilidad**: los registros creados por `/confirmar` traen `IdImportacion` seteado; los cargados por las vías normales (ABM manual) lo traen `null`.
- **Guard de re-importación**: segunda confirmación del mismo ejercicio sin `Forzar` → `409`; con `Forzar = true` → `200`.
- **Reversa**: `/confirmar` → `/revertir/{id}` → todo el lote (`Gasto`, `IngresoCaja`, `LineaPoa`) queda `Activo = false`; los maestros creados por la corrida SIGUEN `Activo = true`; los `PagoGasto` del lote quedan inactivos por cascada vía `GastoId`.
- **Ciclo completo**: `/confirmar` → `/revertir/{id}` → `/confirmar` de nuevo SIN `Forzar` → `200` (el guard de §2.6 no cuenta las corridas ya revertidas).
- **Revertir dos veces**: `/revertir/{id}` sobre un lote ya revertido → `409`. `/revertir/{id}` con un `id` que no existe → `404`.
- **Límite de tamaño de upload**: archivo `.ods` que excede el límite en `/analizar` → rechazado (no llega a `ZipArchive`).

### Gotcha de test a arreglar

`tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`, método `LimpiarTablas()` (líneas 41-50), NO trunca `"IngresosCaja"`. Hoy no molesta porque ningún test escribe ingresos vía la API; en cuanto F5c lo haga, los tests de la collection se filtran estado entre sí. Hay que agregar `"IngresosCaja"` al `TRUNCATE`.

No hay cambios pendientes en `ApiFactory.cs`: al no existir un flag de configuración (§2.6), no hace falta tocar la `AddInMemoryCollection` de esa fixture para este diseño.

## 9. Fuera de alcance (es F5d)

- `ImportacionApiClient` — molde: `AdjuntoApiClient.cs:22-33` (`SubirAsync`, `PostAsync` + `ApiErrores.EnviarAsync`/`AsegurarExitoAsync`). Cubre tanto `/confirmar` como `/revertir/{id}`.
- La primera grilla editable del repo: las 15 grillas `DataGrid` actuales del desktop son todas `IsReadOnly="True"`.
- La pantalla con pestañas por tipo — molde: `MaestrosFinanzasViewModel` (`src/StockApp.Presentation/ViewModels/Finanzas/MaestrosFinanzasViewModel.cs`).
- El ítem admin-only en el sidebar, incluida cualquier UI para listar corridas previas y disparar su reversa con un botón.
- **Completar rubro y fecha de los compromisos POA (§2.4)**: para cada movimiento clasificado `CompromisoSoloPoa` que el análisis de F5b devuelva, F5d tiene que ofrecerle al admin un combo de rubro y un selector de fecha en la grilla, sin default automático. Es trabajo manual proporcional a la cantidad de compromisos que haya en la planilla real — no se puede estimar sin correr `/analizar` contra los datos reales primero.

## 10. Advertencia operativa

Antes de correr la importación REAL hay que reconciliar la planilla POA: `docs/finanzas-discrepancias-planilla-poa-2026.md` documenta que la hoja "SALDO TOTALES" está desincronizada de las hojas de línea (Literal B −301.500, Literal C −480.000 sin explicar). F5c escribe lo que dicen las hojas de LÍNEA, no el resumen de "SALDO TOTALES".

Dado que no hay flag de servidor que apagar (§2.6), el control real de la ventana de migración es operativo, no técnico: los endpoints están siempre disponibles para quien tenga rol Admin. Si algo sale mal, la respuesta correcta es `/revertir/{id}` (§2.7) sobre el `IdImportacion` de la corrida, no depender de que el importador "esté apagado".
