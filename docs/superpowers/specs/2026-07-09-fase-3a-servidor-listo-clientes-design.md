# Fase 3a — Servidor listo para clientes

**Fecha:** 2026-07-09
**Estado:** Diseño aprobado
**Precedente:** Fase 2b (`2026-07-09-fase-2b-api-completa-design.md`). Deuda de contrato identificada en el review final de esa fase (`.superpowers/sdd/final-review-report.md`, resumen en memoria del proyecto).

## Objetivo

Dejar la API en condiciones de contrato y superficie para que el desktop migre a consumirla (Fase 3b) sin deuda conocida. Todo cambio de contrato se hace AHORA, mientras el único consumidor es la suite de tests — después de 3b cada breaking change cuesta.

## Decisiones

### D1 — El Id de ruta es la única fuente
Se elimina el campo `Id` del body de todos los `Modificar*Request` (categorías, proveedores, unidades, productos). El id viaja SOLO en la ruta. Elimina el mismatch silencioso detectado en el review final (PUT /categorias/7 con body id=999 modificaba la 7 ignorando el 999).

### D2 — POST /usuarios devuelve el id
`IUsuarioService.AltaUsuarioAsync` pasa a devolver `Task<int>` (la restricción de no tocar Application era de la Fase 2b; esta fase es dueña del cambio). Response: `201` con body `{ id }`. Se ajustan sus consumidores existentes (PrimerArranqueViewModel usa el servicio vía interfaz — verificar impacto en compilación y tests).

### D3 — DTOs en tablas maestras
Las responses de categorías, proveedores y unidades de medida dejan de serializar entidades de dominio crudas: `CategoriaDto`, `ProveedorDto`, `UnidadMedidaDto` con los campos actuales visibles (mismos nombres). Una nav property futura en el dominio ya no puede cambiar el contrato HTTP silenciosamente.

### D4 — Excepciones de dominio propias (se cierra el catch-all)
Se crean en el dominio: `EntidadNoEncontradaException` y `ReglaDeNegocioException` (conviven con `StockInsuficienteException : ReglaDeNegocioException` si encaja la jerarquía — lo define el plan leyendo el código). Los servicios de Application reemplazan sus `KeyNotFoundException` → `EntidadNoEncontradaException` y sus `InvalidOperationException` de reglas → `ReglaDeNegocioException`. El `DomainExceptionHandler` mapea SOLO las de dominio a 404/409 con mensaje; `InvalidOperationException` y `KeyNotFoundException` genéricas pasan al caso 500 saneado. `ArgumentException`→400, `UnauthorizedAccessException`→403 y `BadHttpRequestException` (su StatusCode propio) quedan igual. Se actualizan los tests de Application/Api afectados.

### D5 — GET /productos con la búsqueda completa de la interfaz real
`GET /productos?texto=&sku=&codigoBarras=&nombre=`: si viene `texto` → `BuscarPorTextoAsync(texto)`; si no → `BuscarAsync(sku, codigoBarras, nombre)` (los tres nullable; todos null = listar todo). Calca la firma real de `IProductoService` — cubre los tres call-sites del desktop que hoy llaman `BuscarAsync(null, null, null)` y la búsqueda por texto existente. No se inventan filtros que la interfaz no tiene.

### D6 — Endpoint garantizar unidad por defecto
`POST /unidades-medida/garantizar-por-defecto` → `GarantizarUnidadPorDefectoAsync()`, idempotente, política `GestionarProductos` (la que verifica el servicio). Devuelve la unidad garantizada.

### D7 — Bootstrap de primer arranque sin auth
- `GET /auth/primer-arranque` (anónimo): `{ requiereCrearAdmin: bool }` vía `IPrimerArranqueService.RequiereCrearAdminAsync()`.
- `POST /auth/primer-admin` (anónimo): crea el admin inicial vía `IPrimerArranqueService.CrearAdminInicialAsync(...)`. La regla de seguridad la impone el SERVIDOR: solo funciona si no existe ningún usuario (semáforo anti-TOCTOU ya existente en el servicio); con usuarios presentes devuelve 409. Con un usuario creado, el endpoint queda muerto para siempre.
- `IPrimerArranqueService` se registra en el DI de la API.

### D8 — LoginResponse enriquecido
`POST /auth/login` pasa de `{ token }` a `{ token, usuario: { id, nombreUsuario, nombreCompleto, rol } }`. El cliente puebla su sesión local sin decodificar claims (el JWT solo lleva usuarioId y rol).

### D9 — Migraciones al servidor
La API ejecuta `Database.MigrateAsync()` al arranque (equivalente del `DatabaseInitializer` del desktop, que se elimina en 3b). Los tests de ApiFactory ya migran por su cuenta — verificar que no colisione.

### D10 — Token de jornada
Expiración del JWT configurable (`Jwt:ExpiracionHoras`), default 12h. Se documenta en README.

### Limpieza menor (del review final de 2b)
`POST /movimientos` deja de emitir Location a una ruta inexistente (201 sin Location o con ruta válida — lo define el plan).

## Manejo de errores

Tabla del handler tras D4: `EntidadNoEncontradaException`→404 · `ReglaDeNegocioException` (incl. `StockInsuficienteException`)→409 · `ArgumentException`→400 · `UnauthorizedAccessException`→403 · `BadHttpRequestException`→su StatusCode · resto (incl. `InvalidOperationException`/`KeyNotFoundException` genéricas)→500 sin detalle.

## Testing

Patrón existente (ApiTestBase + Testcontainers). Los cambios de contrato actualizan los tests de los recursos afectados. Tests nuevos: GET /productos con `sku`/`codigoBarras`/`nombre` y el caso todos-null (listar todo), garantizar-por-defecto (idempotencia), bootstrap (flujo server virgen: GET requiere→POST crea→GET ya no requiere→POST repetido 409; y con usuarios existentes 409), LoginResponse enriquecido, expiración configurable. La suite de Application se actualiza por D2/D4.

## Fuera de alcance

- Todo lo del cliente (Fase 3b).
- Refresh tokens; permiso propio de auditoría; paginación/OpenAPI.
