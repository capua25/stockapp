# StockApp.Api

API de StockApp: JWT + superficie HTTP completa (Fase 2a: login + slice vertical;
Fase 2b: movimientos, reportes, auditoría, usuarios, catálogo completo;
Fase 3a: bootstrap de primer arranque, migración automática, DTOs de lectura).

## Requisitos

- .NET 10 SDK
- PostgreSQL accesible (local o contenedor Docker).
  En desarrollo se usa el contenedor `stockapp-pg` (`postgres:16-alpine`), expuesto
  en `localhost:5432`, con la connection string por defecto de `appsettings.json`.
- Si la tabla `Usuarios` está vacía, el propio cliente puede crear el primer Admin vía
  `GET /auth/primer-arranque` → `POST /auth/primer-admin` (Fase 3a, D7) — ya no depende
  de `StockApp.Seeder` ni de la app desktop.
- La API migra la base de datos automáticamente al arrancar (Fase 3a, D9) — no hace falta
  correr migraciones a mano ni depender de `DatabaseInitializer` del desktop.

## Configurar el secreto JWT (desarrollo)

El secreto de firma NUNCA se hardcodea ni se committea.

```bash
cd src/StockApp.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "una-clave-de-desarrollo-de-al-menos-32-caracteres"
```

El tiempo de vida del token es configurable vía `Jwt:ExpiracionHoras` (default `12` horas si
no se especifica):

```bash
dotnet user-secrets set "Jwt:ExpiracionHoras" "8"
```

## Correr la API

```bash
dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http
```

Kestrel expone la API en `http://localhost:5043` con el profile `http`.

## Superficie de endpoints

Todos requieren `Authorization: Bearer <token>` salvo `GET /auth/primer-arranque` y
`POST /auth/primer-admin`. Las políticas están derivadas de `AuthorizationService`
(`Permisos.Todos` en `Program.cs`) — Admin siempre tiene acceso; Operador solo a lo
marcado "Admin+Op".

| Recurso | Endpoint | Rol |
|---|---|---|
| Auth | `GET /auth/primer-arranque` | público |
| | `POST /auth/primer-admin` | público (solo funciona una vez, con la BD sin usuarios) |
| | `POST /auth/login` | público |
| Movimientos | `POST /movimientos` | Admin+Op |
| | `GET /movimientos/historial` | Admin+Op |
| | `POST /productos/{id}/recalcular-stock` | Admin+Op |
| Reportes | `GET /reportes/valorizacion` | Admin |
| | `GET /reportes/stock-por-categoria` | Admin |
| | `GET /reportes/mas-movidos` | Admin |
| | `GET /reportes/historial-producto/{productoId}` | Admin |
| Auditoría | `GET /auditoria` | Admin |
| Usuarios | `GET /usuarios` · `POST /usuarios` (201 con `{ id }`) · `DELETE /usuarios/{id}` · `PUT /usuarios/{id}/rol` · `PUT /usuarios/{id}/contrasena` | Admin |
| Productos | `GET /productos?texto=` (o `sku=`/`codigoBarras=`/`nombre=`; todos ausentes = listar todo) · `POST /productos` · `PUT /productos/{id}` · `DELETE /productos/{id}` · `PUT /productos/{id}/precio` | Admin+Op |
| Categorías | `GET /categorias` · `POST` · `PUT /{id}` (sin `Id` en el body — el id de ruta es la única fuente) · `DELETE /{id}` | Admin |
| | `GET /categorias/activas` | Admin+Op |
| Proveedores | `GET /proveedores` · `POST` · `PUT /{id}` (sin `Id` en el body) · `DELETE /{id}` | Admin |
| Unidades | `GET /unidades-medida` · `POST` · `PUT /{id}` (sin `Id` en el body) · `DELETE /{id}` | Admin |
| | `GET /unidades-medida/activas` · `POST /unidades-medida/garantizar-por-defecto` (idempotente) | Admin+Op |

Las responses de `GET /categorias`, `GET /proveedores` y `GET /unidades-medida` (y sus
variantes `/activas`) devuelven DTOs (`CategoriaDto`/`ProveedorDto`/`UnidadMedidaDto`), no
las entidades de dominio crudas — una nav property futura en el dominio no puede cambiar el
contrato HTTP silenciosamente (Fase 3a, D3).

`POST /auth/login` devuelve `{ token, usuario: { id, nombreUsuario, nombreCompleto, rol } }`
— el cliente puebla su sesión local sin decodificar los claims del JWT (Fase 3a, D8).

## Manejo de errores

Excepciones de dominio y aplicación son mapeadas centralmente a status HTTP via `DomainExceptionHandler`
(Fase 3a, D4):

- `EntidadNoEncontradaException` → `404 Not Found`
- `ReglaDeNegocioException` (incluye `StockInsuficienteException`) → `409 Conflict`
- `ArgumentException` → `400 Bad Request`
- `UnauthorizedAccessException` → `403 Forbidden`
- Excepciones no anticipadas → `500 Internal Server Error`

Todas las respuestas de error usan el formato `application/problem+json` definido en
`Program.cs`. Los errores 500 nunca exponen el mensaje de excepción (fail-closed).

## Verificación manual (curl)

Con la API corriendo, en otra terminal (puerto `5043`):

**Nota de idempotencia:** Si ejecutás esta secuencia más de una vez, los POSTs de categorías/proveedores/unidades devolverán `409 Conflict` cuando ya existan con el mismo nombre — es el comportamiento esperado (restricción de duplicados). Para ejecutar nuevamente, cambiá los nombres (ej. `"Bebidas-2"`, `"SKU-CURL-2"`) o limpiá la BD.

```bash
# 0a) Verificar si se requiere crear el admin inicial (Fase 3a, D7)
curl -i http://localhost:5043/auth/primer-arranque

# 0b) Si requiereCrearAdmin es true, crear el primer Admin
curl -i -X POST http://localhost:5043/auth/primer-admin \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"admin","contrasena":"admin123"}'

# 1) Login Admin (respuesta enriquecida con datos del usuario — Fase 3a, D8)
curl -X POST http://localhost:5043/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"admin","contrasena":"admin123"}'
# Respuesta: {"token":"...","usuario":{"id":1,"nombreUsuario":"admin","nombreCompleto":null,"rol":0}}
# Copiar "token" -> <TOKEN_ADMIN>

# 2) Crear un Operador vía POST /usuarios (Admin)
curl -i -X POST http://localhost:5043/usuarios \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_ADMIN>" \
  -d '{"nombreUsuario":"operador","nombreCompleto":"Op Erador","contrasenaPlan":"operador123","rol":1}'
# Respuesta: 201, body {"id":2}

# 3) Login Operador (ya existe)
curl -X POST http://localhost:5043/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"operador","contrasena":"operador123"}'
# Copiar "token" -> <TOKEN_OPERADOR>

# 4) Alta de categoría (Admin) -> 201
curl -i -X POST http://localhost:5043/categorias \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_ADMIN>" \
  -d '{"nombre":"Bebidas"}'

# 5) La misma acción con Operador -> 403 (sin GestionarTablasMaestras)
curl -i -X POST http://localhost:5043/categorias \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_OPERADOR>" \
  -d '{"nombre":"Otra"}'

# 6) Garantizar unidad de medida por defecto (Operador, idempotente — Fase 3a, D2)
curl -i -X POST http://localhost:5043/unidades-medida/garantizar-por-defecto \
  -H "Authorization: Bearer <TOKEN_OPERADOR>"

# 7) Alta de producto (Operador, tiene GestionarProductos) -> 201
curl -i -X POST http://localhost:5043/productos \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_OPERADOR>" \
  -d '{"codigo":"SKU-CURL-1","codigoBarras":null,"nombre":"Producto Curl","descripcion":null,"categoriaId":null,"proveedorId":null,"unidadMedidaId":1,"precioCosto":10,"precioVenta":20,"stockMinimo":0}'

# 8) Registrar un movimiento de entrada (Operador) -> 201
#    Nota: los enums usan valores numéricos en JSON (TipoMovimiento: 0=Entrada/1=Salida;
#    MotivoMovimiento: 0=Compra/1=Venta/2=Ajuste/3=Merma)
#    Fase 3a, D6: sin Location — no existe GET /movimientos/{id}
curl -i -X POST http://localhost:5043/movimientos \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_OPERADOR>" \
  -d '{"productoId":1,"tipo":0,"motivo":0,"cantidad":5,"precioUnitario":10,"comentario":"Carga inicial"}'

# 9) Reporte de valorización (Admin) -> 200; con Operador -> 403
curl -i http://localhost:5043/reportes/valorizacion -H "Authorization: Bearer <TOKEN_ADMIN>"
curl -i http://localhost:5043/reportes/valorizacion -H "Authorization: Bearer <TOKEN_OPERADOR>"

# 10) Auditoría (Admin) -> 200 con las entradas generadas arriba
curl http://localhost:5043/auditoria -H "Authorization: Bearer <TOKEN_ADMIN>"

# 11) Listado de usuarios (Admin, DTOs sin HashContrasena) -> 200
curl http://localhost:5043/usuarios -H "Authorization: Bearer <TOKEN_ADMIN>"

# 12) Búsqueda de productos con filtros (Fase 3a, D5)
curl "http://localhost:5043/productos?nombre=Producto%20Curl" -H "Authorization: Bearer <TOKEN_OPERADOR>"
```

Confirmar:
- `GET /auth/primer-arranque` devuelve `200` con `{"requiereCrearAdmin": true/false}`
- `POST /auth/primer-admin` devuelve `201` (una sola vez si la BD está vacía, luego 409)
- `POST /auth/login` devuelve `200` con `{"token":"...","usuario":{...}}` (Fase 3a, D8)
- `POST /usuarios` devuelve `201` con `{"id": <id>}` (Fase 3a, D1)
- Cada operación según rol devuelve el status esperado (201 para creación, 200 para lectura, etc.)
- Operador sin permiso para una acción (ej. POST /categorias) devuelve `403` en `application/problem+json`
- GET `/categorias`, `/proveedores`, `/unidades-medida` devuelven DTOs con estructura fija (Fase 3a, D3)
- Los efectos (categoría creada, producto creado, stock incrementado) son visibles en
  consultas posteriores (`GET /categorias`, `GET /productos`, historial de movimientos)
