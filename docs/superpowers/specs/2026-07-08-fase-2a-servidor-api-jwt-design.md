# Fase 2a — Servidor API + JWT + slice vertical — Diseño

**Fecha:** 2026-07-08
**Estado:** Aprobado (brainstorming). Pendiente: plan de implementación.
**Proyecto:** StockApp — control de stock para gobierno municipal.
**Contexto macro:** parte de la migración client-server (`docs/superpowers/specs/2026-07-07-migracion-client-server-design.md`, Fase 2 = "Servidor API"). Este spec cubre **solo** la Fase 2a.

## 0. Estado previo y ubicación en el roadmap

- **Fase 0 (DTOs de contrato)** ✅ mergeada a main: `ProductoDto` en `StockApp.Application/Catalogo/Dtos.cs`, `ValorizacionReporteDto` para el reporte de valorización.
- **Fase 1 (PostgreSQL + concurrencia)** ✅ mergeada a main: switch total a Postgres (sin SQLite), UPDATE condicional atómico para `StockActual`, `AppDbContext` contra Npgsql, tests de concurrencia con `PostgresFixture` (Testcontainers) en `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresFixture.cs`. La app desktop Avalonia anda hoy contra Postgres real.
- **Fase 2 (Servidor API)** se PARTE en tres sub-incrementos, decisión tomada en este brainstorming:
  - **2a (este spec):** esqueleto de `StockApp.Api` + autenticación JWT + andamiaje de autorización + un slice vertical de un endpoint real, probado de punta a punta.
  - **2b:** resto de endpoints (movimientos, reportes, auditoría, usuarios) + políticas de autorización completas para todas las acciones.
  - **2c:** TLS, `ProblemDetails` pulido para todos los códigos de error, versionado de API.
- **Regla de oro heredada del diseño macro:** el servidor se construye DETRÁS de la app que ya anda. La Fase 2 (2a/2b/2c completas) **no toca** `StockApp.Presentation` ni su composición root (`App.axaml.cs`). La app desktop sigue intacta, contra su propia composición Transient, hasta la Fase 3 (flip del cliente).

## 1. Arquitectura

Proyecto nuevo `StockApp.Api` (ASP.NET Core, **net10.0**, Minimal APIs — no Controllers).

- **Referencias:** `StockApp.Api` → `StockApp.Application` + `StockApp.Infrastructure`. Reutiliza los `IXxxService` existentes, los repositorios y `AppDbContext` tal cual están hoy. La API **orquesta** (routing, auth, mapeo de errores HTTP); **no reimplementa lógica de negocio**.
- **Composición root:** `Program.cs` de `StockApp.Api` registra los mismos servicios de aplicación que hoy registra `App.axaml.cs` (los `IXxxService`, `IAuthorizationService`, `IPasswordHasher`, repositorios), pero con una diferencia deliberada de lifetime:
  - `AppDbContext` → **Scoped por request** en la API (el patrón natural de ASP.NET Core: un scope de DI por request HTTP).
  - La app desktop sigue con `AppDbContext` **Transient** en su propia composición root — no se toca, no se unifica. Cada host tiene el lifetime que le corresponde a su modelo de ejecución (un proceso de larga vida con UI vs. un request corto de servidor).
- **Organización de endpoints:** un archivo de endpoints por recurso (ej. `Endpoints/AuthEndpoints.cs`, `Endpoints/ProductosEndpoints.cs`), cada uno expone un método de extensión `MapXxxEndpoints(this IEndpointRouteBuilder app)` que arma un `MapGroup("/xxx")` y aplica `.RequireAuthorization(...)` al grupo o a rutas puntuales según corresponda. `Program.cs` solo llama a esos métodos de extensión — no acumula lambdas de endpoints inline.
- **En 2a se crean dos archivos de endpoints:** `AuthEndpoints.cs` (login) y `ProductosEndpoints.cs` (el slice vertical de catálogo). El resto de recursos (movimientos, reportes, auditoría, usuarios) se agregan en 2b siguiendo el mismo patrón.

## 2. Autenticación JWT

- **`POST /auth/login`:** recibe usuario + contraseña (DTO de request simple, sin reusar entidades de dominio). Verifica la contraseña **server-side** reusando `IPasswordHasher` (`BcryptPasswordHasher`, ya existe en `StockApp.Infrastructure/Auth/`). Si es válida, firma un **JWT** con:
  - claim `usuarioId`
  - claim `rol`
  - vencimiento: **10 horas** (punto medio del rango 8–12h acordado; fijo, no configurable en 2a — si hace falta ajustarlo se revisita en 2b/2c).
  - Si la validación falla (usuario inexistente o contraseña incorrecta), responde **401** sin distinguir cuál de las dos falló (no filtrar si el usuario existe).
- **Secreto de firma:** en configuración vía **user-secrets** en desarrollo (`dotnet user-secrets`), nunca hardcodeado en el repo ni en `appsettings.json` committeado. En producción se resuelve vía variable de entorno o secret store del municipio — decisión de infraestructura fuera del alcance de 2a (se retoma en 2c/Fase 4 junto con TLS).
- **Sin refresh tokens** (YAGNI, ya acordado en el diseño macro). Al vencer el token, el cliente futuro re-loguea. En 2a esto se verifica solo a nivel servidor (no hay cliente HTTP todavía).
- **Middleware:** `AddAuthentication().AddJwtBearer(...)` estándar de ASP.NET Core, validando emisor, audiencia (si se configuran), firma y vencimiento. Se agrega en `Program.cs` antes del middleware de autorización.
- **`ICurrentSession` scoped:** nueva implementación en `StockApp.Api` (ej. `Auth/HttpCurrentSession.cs`) que lee `usuarioId` y `rol` desde `HttpContext.User` (los claims del token ya validado) y los expone a través de la interfaz `ICurrentSession` que ya consume `StockApp.Application`. Esta implementación **reemplaza** a `InMemorySession` (`StockApp.Infrastructure/Auth/InMemorySession.cs`) **solo en el grafo de DI de la API** — `InMemorySession` sigue existiendo y en uso en la composición root de la app desktop, sin tocar. La interfaz `ICurrentSession` no cambia; solo cambia qué implementación se registra en cada host.

## 3. Autorización

- Se montan **políticas de ASP.NET Core** (`AddAuthorization(options => options.AddPolicy(...))`) que traducen las reglas ya existentes en `AuthorizationService.Verificar` (`StockApp.Application/Authorization/`): Admin pasa todas las acciones, Operador un subconjunto. **Las políticas de la API se nombran igual que las constantes de `Permisos`** (`StockApp.Application/Authorization/Permisos.cs`), para que no haya dos vocabularios de permisos en el sistema — el nombre de la política HTTP es literalmente el mismo string que ya usa `AuthorizationService.Verificar` puertas adentro.
- **En 2a se define el andamiaje de políticas y se ejercitan exactamente las dos que usa el slice vertical** (ver sección 4): la política **`Permisos.GestionarProductos`** ("catalogo.productos", Admin y Operador — así está definida hoy en `AuthorizationService`) y la política **`Permisos.VerReportes`** ("reportes.ver", exclusiva de Admin — ausente de `AccionesOperador`). El resto de las políticas (`RegistrarMovimientos`, `RecalcularStock`, `GestionarTablasMaestras`, `GestionarUsuarios`) se monta en 2b, cuando tengan un endpoint real que las use — no se declaran de antemano sin uso.
- **Fail-closed intacto:** cualquier acción sin política explícita definida en el código de 2a queda inaccesible por default (no hay fallback permisivo). Esto es una propiedad del diseño de ASP.NET Core (un endpoint con `RequireAuthorization` sin política nombrada exige solo autenticación; los endpoints de 2a que necesitan restricción de rol **siempre** nombran su política explícitamente).
- **La decisión de negocio la toma el servidor.** No hay concepto de "UI que oculta el botón" en 2a porque el cliente HTTP todavía no existe (llega en Fase 3); el andamiaje de autorización se verifica directo contra la API con curl/tests, simulando ambos roles.

## 4. Slice vertical (el endpoint)

Dos endpoints, elegidos para poder demostrar tanto el camino feliz autorizado como el 403 por rol, con datos reales:

- **`GET /productos`** — `[RequireAuthorization(Policy = Permisos.GestionarProductos)]`. Política que cumplen **Admin y Operador**. Reusa `IProductoService.BuscarPorTextoAsync(texto: null)` (existente, `StockApp.Application/Catalogo/IProductoService.cs`) — pasar `null` devuelve el listado completo sin filtrar, sin necesidad de agregar un método nuevo al service. Devuelve `IReadOnlyList<ProductoDto>` (el DTO de Fase 0, ya sin ciclos de navegación). Sin parámetros de query en 2a — filtros (`sku`, `nombre`, texto) y paginación quedan para 2b si se necesitan.
- **`GET /productos/reporte-valorizacion`** — `[RequireAuthorization(Policy = Permisos.VerReportes)]` (la política **solo-Admin**: `VerReportes` está deliberadamente ausente del subconjunto de Operador en `AuthorizationService`). Reusa `IReporteStockService.ObtenerValorizacionAsync()` (existente, `StockApp.Application/Reportes/IReporteStockService.cs` — **no** `IProductoService`, la valorización vive en el servicio de reportes) y devuelve `ValorizacionReporteDto` (Fase 0). Se elige este endpoint como el "solo-Admin" del slice porque ya existe la lógica y el DTO listos desde Fase 0 — no hace falta escribir nada de negocio nuevo, solo exponerlo.
  - Con un usuario Operador logueado, este endpoint debe responder **403**. Ese es el caso de prueba concreto que demuestra la autorización server-side funcionando.

Ambos endpoints van en `Endpoints/ProductosEndpoints.cs`, agrupados bajo `MapGroup("/productos")`.

## 5. Manejo de errores

Andamiaje mínimo de `ProblemDetails` para los códigos que el slice puede producir:

- **401** — token ausente o inválido (vencido, firma incorrecta, malformado). Lo produce el middleware de autenticación; se configura un `ProblemDetails` de respuesta estándar para el challenge.
- **403** — autenticado pero sin la política requerida (el caso Operador → endpoint solo-Admin).
- **400** — datos de entrada inválidos (ej. `POST /auth/login` con body vacío o campos faltantes). Validación simple del DTO de request, no un framework de validación nuevo.
- **El 409 completo de conflicto de stock queda fuera de 2a.** No hay endpoints de escritura de stock en este sub-incremento (el slice es de solo lectura de catálogo); el mapeo de la excepción de dominio de stock insuficiente a 409 se implementa en 2b junto con el endpoint de registrar movimiento, que es donde ese caso puede ocurrir.
- Se usa `app.UseExceptionHandler()` + `AddProblemDetails()` estándar de .NET para el andamiaje base; el detalle fino de mensajes por tipo de excepción de dominio se completa en 2c.

## 6. Testing y verificación

**Automatizado — `WebApplicationFactory<Program>` + `PostgresFixture` (Testcontainers), contra Postgres real, sin mocks** (mismo patrón que Fase 1). Razón: el comportamiento real de autorización y JWT solo es fiel si se ejercita contra la base real con datos seedeados, igual que la concurrencia de Fase 1 solo era fiel contra Postgres real.

Casos cubiertos por los tests de integración de 2a:
1. Login con credenciales válidas → `200` + token JWT no vacío.
2. Login con credenciales inválidas → `401`.
3. `GET /productos` sin header `Authorization` → `401`.
4. `GET /productos` con token válido (Admin u Operador) → `200` con la lista de productos seedeados en la fixture.
5. `GET /productos/reporte-valorizacion` con token de Operador → `403`.
6. `GET /productos/reporte-valorizacion` con token de Admin → `200` con el DTO de valorización.

Estos tests viven en un proyecto nuevo `tests/StockApp.Api.Tests`, siguiendo la misma convención de fixtures que `StockApp.Infrastructure.Tests` (reusa o referencia `PostgresFixture` en vez de duplicarlo — el plan de implementación decide si se comparte el proyecto de fixtures o se referencia directamente).

**Verificación orgánica (manual, además de los tests automatizados):** correr `StockApp.Api` real localmente (`dotnet run`) contra una instancia de Postgres, y pegarle con `curl`:
```
curl -X POST https://localhost:PORT/auth/login -d '{"usuario":"...","contrasena":"..."}'
curl https://localhost:PORT/productos -H "Authorization: Bearer <token>"
```
Confirmando que los productos reales de la base salen por HTTP con el token obtenido en el paso anterior. Esta verificación manual es la que cierra el sub-incremento: no se da 2a por terminada solo con tests en verde, sino viendo el flujo real correr.

## 7. Alcance y límites (diferimientos YAGNI aprobados)

- **`StockApp.Contracts` NO se extrae en 2a.** Los DTOs (`ProductoDto`, `ValorizacionReporteDto`) siguen viviendo en `StockApp.Application/Catalogo/Dtos.cs`, como quedaron en Fase 0. `StockApp.Api` los referencia directamente vía su referencia a `StockApp.Application`. La extracción a un proyecto `Contracts` compartido se hace en Fase 3, cuando exista un segundo consumidor real (el cliente HTTP de Presentation) — hoy sigue habiendo un solo consumidor de esos DTOs además del propio servidor.
- **Bootstrap del primer admin fuera de 2a.** Los tests y la verificación manual usan usuarios **ya existentes** en la base (ej. el admin que ya sembró `StockApp.Seeder` o el `PrimerArranqueService` de la app desktop). La migración de `PrimerArranqueService` al servidor como bootstrap de primer arranque es una tarea de Fase 4, no de 2a.
- **TLS diferido a 2c.** En 2a la API corre sobre HTTP plano en desarrollo local (o HTTPS de desarrollo por defecto de Kestrel, sin certificado del municipio). El no-negociable de TLS obligatorio del diseño macro se cumple recién al cerrar 2c, antes de cualquier despliegue real.
- **Versionado de API diferido a 2c.** No hay prefijo de versión en las rutas de 2a (`/auth/login`, `/productos`, no `/v1/auth/login`). Se agrega en 2c junto con TLS, antes de que exista un cliente real que dependa de la compatibilidad.
- **Resto de endpoints (movimientos, reportes restantes, auditoría, usuarios) y políticas completas para todas las acciones: Fase 2b.** 2a demuestra el patrón (JWT + política + endpoint) con dos endpoints; 2b lo replica para el resto de la superficie de la aplicación.
- **La app desktop (`StockApp.Presentation`) no se toca en 2a.** Ni sus ViewModels, ni su composición root, ni su `AppDbContext` Transient. Sigue funcionando exactamente igual que hoy, contra Postgres, en paralelo a que `StockApp.Api` se desarrolla y prueba de forma independiente.

## 8. Dependencias

- **Fase 0** completa: `ProductoDto` y `ValorizacionReporteDto` existen en `StockApp.Application/Catalogo/Dtos.cs` y son los tipos de retorno reales de `IProductoService`.
- **Fase 1** completa: `AppDbContext` contra Npgsql, transacción atómica de stock, `PostgresFixture` (Testcontainers) disponible en `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresFixture.cs` como referencia de patrón para los tests de 2a.
- No se toca la app desktop (`StockApp.Presentation`) en ningún punto de este spec.
