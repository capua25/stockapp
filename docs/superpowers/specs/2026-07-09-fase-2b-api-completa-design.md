# Fase 2b — Superficie completa de la API REST

**Fecha:** 2026-07-09
**Estado:** Diseño aprobado
**Precedente:** Fase 2a (`docs/superpowers/specs/2026-07-08-fase-2a-servidor-api-jwt-design.md`) — JWT + política + endpoint demostrado con login, GET /productos y reporte de valorización.

## Objetivo

Completar la superficie HTTP de StockApp: exponer por API todos los casos de uso de la capa de aplicación (movimientos, reportes, auditoría, usuarios, catálogo completo) con las políticas de autorización completas. Consumidor previsto: el propio desktop Avalonia, que en una fase futura reemplaza el acceso directo a Postgres por llamadas HTTP (multi-puesto real). Por eso los contratos calcan los DTOs de la capa de aplicación — cero traducción de modelos al migrar.

## Decisiones de diseño

### D1 — Políticas derivadas de la fuente de verdad (enfoque B, el elegido)

Las políticas de autorización NO se declaran a mano en `Program.cs`. Se derivan de la tabla rol→permiso de `AuthorizationService` (`src/StockApp.Application/Authorization/AuthorizationService.cs`):

- `Permisos` expone una lista estática `Todos` con las 6 constantes (explícita, sin reflection).
- `Program.cs` itera `Permisos.Todos` y para cada permiso arma la política `RequireClaim(StockAppClaimTypes.Rol, <roles autorizados según la tabla>)`.
- Resultado derivado: `GestionarProductos`, `RegistrarMovimientos`, `RecalcularStock` → Admin+Operador; `VerReportes`, `GestionarUsuarios`, `GestionarTablasMaestras` → solo Admin.

Alternativas descartadas: (A) declarar políticas a mano — duplica la tabla de permisos en dos lugares y diverge con el tiempo; (C) helper CRUD genérico para tablas maestras — abstracción prematura sobre 3 casos con asimetrías reales (`ListarActivasAsync` no existe en Proveedor; `GarantizarUnidadPorDefectoAsync` solo en Unidad).

### D2 — Defensa en profundidad

La política HTTP es la primera barrera. Los servicios de aplicación conservan su `Verificar(rol, permiso)` interno (fail-closed) — no se toca. Un endpoint mal mapeado es rebotado igual por el servicio. Dos capas, una sola fuente de verdad.

### D3 — La valorización se muda a /reportes (breaking change deliberado)

`GET /productos/reporte-valorizacion` (Fase 2a) se elimina y pasa a `GET /reportes/valorizacion`. No existe ningún cliente todavía: el costo del breaking change es cero hoy y alto después.

### D4 — El ABM de productos entra al alcance

La definición original de 2b no lo incluía, pero la migración del desktop a la API exige paridad completa: alta, modificación, baja, cambio de precio y búsqueda de productos se exponen en esta fase.

### D5 — Auditoría sin permiso propio

`AuditoriaQueryService` verifica `VerReportes` (no existe un permiso de auditoría en el dominio). La API respeta eso: `GET /auditoria` usa la política `VerReportes`. Documentado como decisión, no como omisión.

### D6 — Único cambio en la capa de aplicación

`IUsuarioService.ListarAsync()` no existe y sin él no hay `GET /usuarios`. Se agrega al servicio con verificación `GestionarUsuarios` interna, mismo patrón que sus hermanos. No se expone `IUsuarioRepository` crudo en endpoints: saltearía la autorización fail-closed.

## Arquitectura

Un archivo por recurso en `src/StockApp.Api/Endpoints/`: `MovimientosEndpoints.cs`, `ReportesEndpoints.cs`, `AuditoriaEndpoints.cs`, `UsuariosEndpoints.cs`, `CategoriasEndpoints.cs`, `ProveedoresEndpoints.cs`, `UnidadesMedidaEndpoints.cs`, más la ampliación de `ProductosEndpoints.cs`. Cada endpoint inyecta el servicio de aplicación existente y delega: la API es un adaptador HTTP sin lógica de negocio.

Registros DI nuevos en `Program.cs`: `ICategoriaService`+repo, `IProveedorService`+repo, `IUnidadMedidaService`, `IAuditoriaQueryService`+repo, `IUsuarioService`.

## Superficie de endpoints

| Recurso | Endpoint | Política |
|---|---|---|
| Movimientos | `POST /movimientos` (flag `forzar` en body) | `RegistrarMovimientos` |
| | `GET /movimientos/historial?productoId&tipo&fechaDesde&fechaHasta` | `RegistrarMovimientos` |
| | `POST /productos/{id}/recalcular-stock` | `RecalcularStock` |
| Reportes | `GET /reportes/valorizacion` (mudada desde /productos) | `VerReportes` |
| | `GET /reportes/stock-por-categoria` | `VerReportes` |
| | `GET /reportes/mas-movidos?fechaDesde&fechaHasta&topN` | `VerReportes` |
| | `GET /reportes/historial-producto/{productoId}?fechaDesde&fechaHasta` | `VerReportes` |
| Auditoría | `GET /auditoria?usuarioId&fechaDesde&fechaHasta` | `VerReportes` (D5) |
| Usuarios | `GET /usuarios` · `POST /usuarios` · `DELETE /usuarios/{id}` (baja lógica) · `PUT /usuarios/{id}/rol` · `PUT /usuarios/{id}/contrasena` | `GestionarUsuarios` |
| Productos | `POST /productos` · `PUT /productos/{id}` · `DELETE /productos/{id}` · `PUT /productos/{id}/precio` · `GET /productos?texto=` | `GestionarProductos` |
| Categorías | `GET /categorias` · `POST` · `PUT /{id}` · `DELETE /{id}` | `GestionarTablasMaestras` |
| | `GET /categorias/activas` | `GestionarProductos` |
| Proveedores | `GET /proveedores` · `POST` · `PUT /{id}` · `DELETE /{id}` | `GestionarTablasMaestras` |
| Unidades | `GET /unidades-medida` · `POST` · `PUT /{id}` · `DELETE /{id}` | `GestionarTablasMaestras` |
| | `GET /unidades-medida/activas` | `GestionarProductos` |

Contratos: request/response calcan los DTOs de `src/StockApp.Application/` (`RegistrarMovimientoDto`, `MovimientoHistorialDto`, `ValorizacionReporteDto`, `StockCategoriaDto`, `MasMovidoDto`, `AuditoriaItemDto`, etc.).

## Manejo de errores

Mapeo centralizado en el `UseExceptionHandler` existente — tabla excepción→status, los endpoints no hacen try/catch:

| Situación | HTTP |
|---|---|
| Regla de negocio violada (stock insuficiente sin `forzar`, código duplicado) | 409 Conflict + ProblemDetails con mensaje de dominio |
| Entidad inexistente | 404 + ProblemDetails |
| Input inválido | 400 + ProblemDetails (patrón existente) |
| Acceso denegado por el servicio (2ª barrera) | 403 + ProblemDetails (mismo formato que la política HTTP) |
| Cualquier otra excepción | 500 genérico sin detalles internos (fail-closed) |

Nota: los tipos exactos de excepción del dominio se verifican al escribir el plan de implementación — este diseño fija el contrato HTTP.

## Testing

Mismo andamiaje que 2a: `ApiTestBase` + Testcontainers (Postgres real), TRUNCATE entre tests, tokens JWT reales por rol. TDD endpoint por endpoint.

Matriz mínima por grupo de endpoints:
1. Sin token → 401
2. Rol sin permiso → 403 (ej. Operador contra `/usuarios`)
3. Rol con permiso → 200/201 + efecto verificado en la DB (no solo el status)
4. Regla de negocio violada → 409 donde aplique

Test de cierre del enfoque B: itera `Permisos.Todos` y verifica que cada política registrada en la API autoriza exactamente los roles que `AuthorizationService` dicta. Suite estimada: de 15 a ~70-80 tests.

## Fuera de alcance

- Migración del desktop a la API (fase futura).
- Paginación, versionado y OpenAPI/Swagger (se evalúan cuando exista el cliente remoto).
- Permiso propio para auditoría (cambio de dominio, no de API).
- Refresh tokens / expiración deslizante (la 2a fijó el esquema de token actual).
