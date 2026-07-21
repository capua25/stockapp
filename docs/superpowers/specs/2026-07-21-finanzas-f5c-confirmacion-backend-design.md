# Diseño: F5c — Confirmación transaccional del importador

**Fecha**: 2026-07-21
**Estado**: Validado con el usuario, pendiente de implementación

## 1. Contexto

F5a (parser `.ods`, `IPlanillaParser`/`PlanillaOdsParser`) y F5b (análisis backend read-only, `POST /finanzas/importar/analizar`, `AnalisisImportacionService`) están mergeadas a main. F5c es **SOLO BACKEND**: el endpoint de confirmación y la escritura transaccional. La UI (`ApiClient`, grilla editable, pantalla) es F5d — explícitamente fuera de alcance de esta fase.

## 2. Decisiones tomadas

### 2.1 Corte de fases

F5c = backend (confirmar + escritura + idempotencia + auditoría). F5d = `ApiClient` + grilla editable + pantalla + ítem admin-only en el sidebar. Mismo corte que F5a/F5b: primero el contrato y la lógica de servidor, testeados end-to-end sin UI; después el consumo desde el desktop.

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

El saldo inicial viaja como un ingreso más dentro de `Ingresos`, con su `Fuente` obligatoria igual que cualquier otro — no tiene tratamiento especial en el contrato de confirmación.

### 2.5 Contrato stateless

F5d manda el payload completo ya corregido en un solo JSON. Sin tablas de staging, sin id de lote, sin re-parseo de los `.ods` en el confirm.

Descartado:
- **Stateful** (2-3 tablas nuevas para persistir el resultado del análisis entre pasos): sobredimensionado para una herramienta que corre un puñado de veces en la vida del sistema.
- **Re-parseo** (el confirm vuelve a abrir los `.ods` y aplica las correcciones sobre las filas parseadas): ata las correcciones del usuario a coordenadas posicionales del parseo — frágil ante cualquier reordenamiento de filas entre el análisis y la confirmación.

## 3. Contrato del endpoint

`POST /finanzas/importar/confirmar`

- Body: JSON (no multipart — a diferencia de `/analizar`, que recibe los `.ods`).
- `.RequireAuthorization(Permisos.ImportarPlanillas)` — Admin-only, permiso ya existe (`Permisos.cs:27`, `"finanzas.importar"`).

### DTOs nuevos (`StockApp.Application/Finanzas`)

Los maestros se referencian por **NOMBRE**, no por `Id`, porque la mayoría no existe todavía en la base — el servidor resuelve nombre→Id con get-or-create dentro de la transacción.

```csharp
ConfirmarImportacionDto(
    int Ejercicio,
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
                  string Fuente, int CodigoRubro, string? LineaPoa, CondicionPago Condicion)

LineaPoaConfirmarDto(string Nombre, string Programa,
                     IReadOnlyList<AsignacionConfirmarDto> Asignaciones)

AsignacionConfirmarDto(string Fuente, decimal Monto)
```

**Regla de cierre**: toda referencia nominal (proveedor, fuente, rubro por código, línea POA) tiene que resolver contra un maestro ya existente en la base o contra uno declarado en `MaestrosNuevos`. Si no resuelve → `400`. Nada se crea por accidente fuera de lo que el usuario declaró explícitamente.

Respuesta feliz: `ResultadoConfirmacionDto` con contadores de creados y omitidos por tipo (proveedores, fuentes, rubros, líneas POA, asignaciones, ingresos, gastos, pagos).

## 4. Idempotencia — claves naturales

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

**Decisión explícita: NO se agregan índices únicos nuevos; F5c no trae migración de esquema.**

Razón: un índice único sobre `Gasto (ProveedorId, Factura, Orden, Fecha, Monto)` rompería la carga manual legítima — mismo proveedor, sin factura, misma fecha, mismo monto, `Detalle` distinto, son dos gastos válidos que hoy se pueden cargar sin problema. El índice parcial `(ProveedorId, NumeroFactura)` sobre activos que ya existe (`AppDbContext.cs:165-167`, migración `20260716181915_UniqueFacturaProveedorGastosActivos`) cubre el caso que sí importa proteger (factura duplicada del mismo proveedor). El importador no le impone sus reglas de idempotencia al resto de la app.

El dedupe vive dentro del importador: al abrir la transacción se cargan en memoria las claves naturales del ejercicio ya presentes en la base y se filtra el payload contra ese set.

### Riesgo asumido y su mitigación

Check-then-act (cargar claves en memoria, filtrar, insertar) tiene una carrera si dos confirmaciones corren concurrentes sobre el mismo ejercicio. Se mitiga con `pg_advisory_xact_lock` sobre el ejercicio al abrir la transacción — se libera solo, automáticamente, en commit o rollback.

Hay precedente de SQL crudo dentro de un repositorio en `GastoRepository.RegistrarPagoAtomicoAsync` (`GastoRepository.cs:138-175`), que usa `FromSqlInterpolated` con `FOR UPDATE` para serializar pagos concurrentes sobre el mismo gasto — mismo principio: guard atómico dentro de la transacción, no check-then-insert desnudo en memoria.

## 5. Arquitectura y transacción

- **`IConfirmacionImportacionService` / `ConfirmacionImportacionService`** (`StockApp.Application/Finanzas`): verifica `Permisos.ImportarPlanillas` vía `ICurrentSession` + `IAuthorizationService` — mismo arranque que `AnalisisImportacionService.AnalizarAsync` (`_auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas)`) — valida el payload COMPLETO, y recién ahí delega la escritura.
- **`IImportacionRepository`** (`StockApp.Application/Interfaces`) / **`ImportacionRepository`** (`StockApp.Infrastructure/Repositories`): abre la transacción. Es el único lugar donde puede vivir sin que Application referencie EF/Npgsql — mismo criterio documentado en el comentario de `GastoRepository.cs:113-121` ("en el repo, que es quien referencia Npgsql — Application NO referencia EF/Npgsql para mantener la capa desacoplada").
- **Patrón a imitar**: `MovimientoStockRepository.RegistrarMovimientoAtomicoAsync` (`MovimientoStockRepository.cs:40-101`) — `await using var tx = await _ctx.Database.BeginTransactionAsync()`, todo el trabajo dentro, UN solo `SaveChangesAsync()`, `tx.CommitAsync()` al final, rollback explícito en cada rama de fallo.

### Orden dentro de la transacción

```
BeginTransaction → pg_advisory_xact_lock(ejercicio)
  1. Fuentes, Rubros, Proveedores nuevos (get-or-create)
  2. LineasPoa + AsignacionPresupuestal (get-or-create)
  3. IngresosCaja (dedupe por clave natural, insertar los nuevos)
  4. Gastos (dedupe por clave natural) + PagoGasto por el total para los de CondicionPago.Contado
  5. LogAuditoria (resumen de la corrida)
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

## 7. Auditoría

`AccionAuditada` es append-only y hoy llega a `BajaAdjunto = 41` (`AccionAuditada.cs:59`) → se agrega `ImportacionPlanillas = 42`.

Un solo `LogAuditoria` por corrida, con el resumen de creados/omitidos en el detalle, insertado DENTRO de la transacción: si la transacción rollbackea, no queda rastro de una importación que no llegó a pasar.

## 8. Testing

Tres capas, mismo patrón que F5a/F5b:

- **`Application.Tests`**: validación pura con fakes.
  **Gotcha**: los fakes de maestros en `tests/StockApp.Application.Tests/Finanzas/Fakes/RepositorioMaestrosFake.cs` tiran `NotSupportedException` en los métodos de escritura porque F5b es read-only — hay que extenderlos para que F5c pueda ejercitar el get-or-create.
- **`Infrastructure.Tests`**: transacción contra Postgres real (`Fixtures/PostgresFixture.cs` + `PostgresRepositoryTestBase`). Cubre atomicidad (un fallo en el paso 4 deja la base intacta) e idempotencia (segunda corrida = 0 creados, todo omitido).
- **`Api.Tests`**: matriz 401/403, `400` estructurado, `200` feliz. Infra ya lista (`Fixtures/ApiFactory.cs` con Testcontainers `postgres:16-alpine` + `ApiTestBase`).

### Criterio de aceptación duro de la fase

End-to-end con las planillas reales (gitignored, en `tests/StockApp.Api.Tests/Fixtures/Finanzas/`): `/analizar` → completar los obligatorios → `/confirmar` → consultar la base vía `Factory.CrearContexto()` y aseverar:

- Caja de junio 2026 = **43.705**
- Saldo POA Literal B = **6.643.349**
- Saldo POA Literal C = **4.654.206**

Son los mismos números de §11 que F5b validó en memoria (sobre el resultado del análisis); acá quedan persistidos en la base real.

### Gotcha de test a arreglar

`tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`, método `LimpiarTablas()` (líneas 41-50), NO trunca `"IngresosCaja"`. Hoy no molesta porque ningún test escribe ingresos vía la API; en cuanto F5c lo haga, los tests de la collection se filtran estado entre sí. Hay que agregar `"IngresosCaja"` al `TRUNCATE`.

## 9. Fuera de alcance (es F5d)

- `ImportacionApiClient` — molde: `AdjuntoApiClient.cs:22-33` (`SubirAsync`, `PostAsync` + `ApiErrores.EnviarAsync`/`AsegurarExitoAsync`).
- La primera grilla editable del repo: las 15 grillas `DataGrid` actuales del desktop son todas `IsReadOnly="True"`.
- La pantalla con pestañas por tipo — molde: `MaestrosFinanzasViewModel` (`src/StockApp.Presentation/ViewModels/Finanzas/MaestrosFinanzasViewModel.cs`).
- El ítem admin-only en el sidebar.

## 10. Advertencia operativa

Antes de correr la importación REAL hay que reconciliar la planilla POA: `docs/finanzas-discrepancias-planilla-poa-2026.md` documenta que la hoja "SALDO TOTALES" está desincronizada de las hojas de línea (Literal B −301.500, Literal C −480.000 sin explicar). F5c escribe lo que dicen las hojas de LÍNEA, no el resumen de "SALDO TOTALES".
