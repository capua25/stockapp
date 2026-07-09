# StockApp.Api

API de StockApp: JWT + superficie HTTP completa (Fase 2a: login + slice vertical;
Fase 2b: movimientos, reportes, auditoría, usuarios, catálogo completo).

## Requisitos

- .NET 10 SDK
- PostgreSQL accesible (local o contenedor Docker) con la base `stockapp` migrada
  (mismas migraciones que la app desktop, en `StockApp.Infrastructure/Migrations`).
  En desarrollo se usa el contenedor `stockapp-pg` (`postgres:16-alpine`), expuesto
  en `localhost:5432`, con la connection string por defecto de `appsettings.json`.
- Al menos un usuario Admin y un usuario Operador existentes en la tabla `Usuarios`
  (sembrados por `StockApp.Seeder` o por `PrimerArranqueService` de la app desktop —
  el bootstrap de primer arranque vía API queda para Fase 4).

## Configurar el secreto JWT (desarrollo)

El secreto de firma NUNCA se hardcodea ni se committea.

```bash
cd src/StockApp.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "una-clave-de-desarrollo-de-al-menos-32-caracteres"
```

## Correr la API

```bash
dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http
```

Kestrel expone la API en `http://localhost:5043` con el profile `http`.

## Superficie de endpoints

Todos requieren `Authorization: Bearer <token>` salvo `POST /auth/login`. Las
políticas están derivadas de `AuthorizationService` (`Permisos.Todos` en
`Program.cs`) — Admin siempre tiene acceso; Operador solo a lo marcado "Admin+Op".

| Recurso | Endpoint | Rol |
|---|---|---|
| Auth | `POST /auth/login` | público |
| Movimientos | `POST /movimientos` | Admin+Op |
| | `GET /movimientos/historial` | Admin+Op |
| | `POST /productos/{id}/recalcular-stock` | Admin+Op |
| Reportes | `GET /reportes/valorizacion` | Admin |
| | `GET /reportes/stock-por-categoria` | Admin |
| | `GET /reportes/mas-movidos` | Admin |
| | `GET /reportes/historial-producto/{productoId}` | Admin |
| Auditoría | `GET /auditoria` | Admin |
| Usuarios | `GET /usuarios` · `POST /usuarios` · `DELETE /usuarios/{id}` · `PUT /usuarios/{id}/rol` · `PUT /usuarios/{id}/contrasena` | Admin |
| Productos | `GET /productos?texto=` · `POST /productos` · `PUT /productos/{id}` · `DELETE /productos/{id}` · `PUT /productos/{id}/precio` | Admin+Op |
| Categorías | `GET /categorias` · `POST` · `PUT /{id}` · `DELETE /{id}` | Admin |
| | `GET /categorias/activas` | Admin+Op |
| Proveedores | `GET /proveedores` · `POST` · `PUT /{id}` · `DELETE /{id}` | Admin |
| Unidades | `GET /unidades-medida` · `POST` · `PUT /{id}` · `DELETE /{id}` | Admin |
| | `GET /unidades-medida/activas` | Admin+Op |

## Verificación manual (curl)

Con la API corriendo, en otra terminal (puerto `5043`):

**Nota de idempotencia:** Si ejecutás esta secuencia más de una vez, los POSTs de categorías/proveedores/unidades devolverán `409 Conflict` cuando ya existan con el mismo nombre — es el comportamiento esperado (restricción de duplicados). Para ejecutar nuevamente, cambiá los nombres (ej. `"Bebidas-2"`, `"SKU-CURL-2"`) o limpiá la BD.

```bash
# 1) Login Admin
curl -X POST http://localhost:5043/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"admin","contrasena":"admin123"}'
# Copiar "token" -> <TOKEN_ADMIN>

# 2) Login Operador
curl -X POST http://localhost:5043/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"operador","contrasena":"operador123"}'
# Copiar "token" -> <TOKEN_OPERADOR>

# 3) Alta de categoría (Admin) -> 201
curl -i -X POST http://localhost:5043/categorias \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_ADMIN>" \
  -d '{"nombre":"Bebidas"}'

# 4) La misma acción con Operador -> 403 (sin GestionarTablasMaestras)
curl -i -X POST http://localhost:5043/categorias \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_OPERADOR>" \
  -d '{"nombre":"Otra"}'

# 5) Alta de producto (Operador, tiene GestionarProductos) -> 201
curl -i -X POST http://localhost:5043/productos \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_OPERADOR>" \
  -d '{"codigo":"SKU-CURL-1","codigoBarras":null,"nombre":"Producto Curl","descripcion":null,"categoriaId":null,"proveedorId":null,"unidadMedidaId":1,"precioCosto":10,"precioVenta":20,"stockMinimo":0}'

# 6) Registrar un movimiento de entrada (Operador) -> 201
#    Nota: los enums usan valores numéricos en JSON (TipoMovimiento: 0=Entrada/1=Salida;
#    MotivoMovimiento: 0=Compra/1=Venta/2=Ajuste/3=Merma)
curl -i -X POST http://localhost:5043/movimientos \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_OPERADOR>" \
  -d '{"productoId":1,"tipo":0,"motivo":0,"cantidad":5,"precioUnitario":10,"comentario":"Carga inicial"}'

# 7) Reporte de valorización (Admin) -> 200; con Operador -> 403
curl -i http://localhost:5043/reportes/valorizacion -H "Authorization: Bearer <TOKEN_ADMIN>"
curl -i http://localhost:5043/reportes/valorizacion -H "Authorization: Bearer <TOKEN_OPERADOR>"

# 8) Auditoría (Admin, D5: usa VerReportes) -> 200 con las entradas generadas arriba
curl http://localhost:5043/auditoria -H "Authorization: Bearer <TOKEN_ADMIN>"

# 9) Listado de usuarios (Admin) -> 200, sin HashContrasena en el body
curl http://localhost:5043/usuarios -H "Authorization: Bearer <TOKEN_ADMIN>"
```

Confirmar: cada `201`/`200` con Admin/Operador según la tabla de arriba; cada
intento fuera de rol devuelve `403` en formato `application/problem+json`; los
efectos (categoría creada, producto creado, stock incrementado) son visibles en
consultas posteriores (`GET /categorias`, `GET /productos`, historial de
movimientos) — no alcanza con mirar el status code.
