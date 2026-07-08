# StockApp.Api

API de StockApp (Fase 2a): JWT + slice vertical de catálogo/reportes.

## Requisitos

- .NET 10 SDK
- PostgreSQL accesible (local o contenedor Docker) con la base `stockapp` migrada
  (mismas migraciones que la app desktop, en `StockApp.Infrastructure/Migrations`).
  En desarrollo se usa el contenedor `stockapp-pg` (`postgres:16-alpine`), expuesto
  en `localhost:5432`, con la connection string por defecto de `appsettings.json`
  (`Host=localhost;Port=5432;Database=stockapp;Username=stockapp;Password=stockapp`).
- Al menos un usuario existente en la tabla `Usuarios` (sembrado por `StockApp.Seeder`
  o por `PrimerArranqueService` de la app desktop — el bootstrap de primer arranque
  vía API queda para Fase 4, spec §7). En el entorno de desarrollo local ya están
  sembrados `admin`/`admin123` (rol Admin) y `operador`/`operador123` (rol Operador).

## Configurar el secreto JWT (desarrollo)

El secreto de firma NUNCA se hardcodea ni se committea. No está en `appsettings.json`.
Dos formas de proveerlo en desarrollo:

**Opción A — `dotnet user-secrets` (recomendada, persiste entre corridas):**

```bash
cd src/StockApp.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "una-clave-de-desarrollo-de-al-menos-32-caracteres"
```

**Opción B — variable de entorno (rápida, solo dura la sesión de shell):**

```bash
Jwt__Secret="una-clave-de-desarrollo-de-al-menos-32-caracteres" \
  dotnet run --project src/StockApp.Api/StockApp.Api.csproj
```

(La doble raya baja `__` es el separador de jerarquía de configuración de
ASP.NET Core para variables de entorno — equivale a `Jwt:Secret`.)

Si falta el secreto, el host falla rápido al arrancar con un mensaje explícito
("Falta 'Jwt:Secret' en la configuración...") en vez de arrancar en un estado
inconsistente.

## Correr la API

```bash
dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http
```

Con el profile `http` de `launchSettings.json`, Kestrel expone la API en
`http://localhost:5043` (sin fricción de certificado de desarrollo HTTPS). El
profile `https` también existe (`https://localhost:7216`) — TLS con certificado
real del municipio se resuelve en Fase 2c.

## Verificación manual (curl)

Con la API corriendo, en otra terminal (puerto `5043` del profile `http`):

```bash
# 1) Login con un usuario existente en la base
curl -X POST http://localhost:5043/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"admin","contrasena":"admin123"}'

# Copiar el valor de "token" de la respuesta y reemplazar <TOKEN> abajo.

# 2) GET /productos sin token → 401 (ProblemDetails)
curl -i http://localhost:5043/productos

# 3) GET /productos con el token obtenido → 200 con los productos reales
curl http://localhost:5043/productos \
  -H "Authorization: Bearer <TOKEN>"

# 4) GET /productos/reporte-valorizacion con token de Admin → 200 con la valorización
curl http://localhost:5043/productos/reporte-valorizacion \
  -H "Authorization: Bearer <TOKEN>"

# 5) Login como operador y golpear el mismo endpoint de reportes → 403 (ProblemDetails)
curl -X POST http://localhost:5043/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"operador","contrasena":"operador123"}'

curl -i http://localhost:5043/productos/reporte-valorizacion \
  -H "Authorization: Bearer <TOKEN-DE-OPERADOR>"
```

Esta secuencia se corrió realmente contra Postgres de desarrollo (`stockapp-pg`,
20 productos sembrados) el 2026-07-08: login de `admin`/`admin123` devolvió `200`
+ JWT; credenciales incorrectas devolvieron `401` ProblemDetails; `GET /productos`
sin token devolvió `401`; con token de Admin devolvió `200` con los 20 productos
reales (Coca-Cola 1.5L, Cerveza Quilmes 1L, etc.); `GET /productos/reporte-valorizacion`
devolvió `200` con la valorización para Admin y `403` ProblemDetails para el token
de `operador`/`operador123` — confirmando el modelo de autorización server-side
de punta a punta (spec §6, "no se da 2a por terminada solo con tests en verde,
sino viendo el flujo real correr").
