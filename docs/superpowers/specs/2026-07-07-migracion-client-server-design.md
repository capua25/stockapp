# Migración a arquitectura client-server — Diseño

**Fecha:** 2026-07-07
**Estado:** Aprobado (brainstorming). Pendiente: plan de implementación.
**Proyecto:** StockApp — control de stock para gobierno municipal.

## 1. Contexto y motivación

StockApp es hoy una app de escritorio Avalonia/.NET 10 con Clean Architecture (Domain / Application / Infrastructure / Presentation) y persistencia en **SQLite local por usuario**. Cada instalación tiene su propia base aislada.

El municipio necesita pasar a un modelo **client-server** por estos motivos (confirmados con el usuario):

- **Multiusuario:** varias personas operando sobre la misma data al mismo tiempo.
- **Multidispositivo:** acceso desde varias PCs de la red municipal.
- **Centralización:** una sola fuente de verdad para backups y auditoría serios.
- **Escala.**
- **Explícitamente NO** se requiere API pública ni integraciones externas.

Implicaciones de dominio (gobierno): auditoría confiable (quién/qué/cuándo), control de acceso por roles, e infraestructura probablemente on-premise.

## 2. Decisiones de diseño (tomadas en brainstorming)

| Decisión | Elección | Razón |
|---|---|---|
| Tipo de cliente | Misma app Avalonia en varias PCs → servidor central | Reutiliza toda la UI, ViewModels y lógica ya testeada. Menor riesgo. |
| Conectividad | **Online puro** (sin offline, sin sincronización) | Evita el infierno de sistemas distribuidos (conflictos, IDs, merges). La resiliencia va en el servidor (UPS, red, backups). |
| Motor de base de datos | **PostgreSQL** (por defecto), portátil vía proveedor EF Core | Costo de licencia cero (presupuesto público), corre en Windows y Linux, gran soporte EF Core. SQLite queda descartado para servidor multiusuario. |
| Infraestructura de hosting | Portátil / a confirmar con el municipio | No bloquea el diseño; PostgreSQL corre igual en Windows y Linux. |
| Data existente | Arranque limpio / piloto (sin migración) | No hay data crítica que preservar. Sin fase de migración de datos. |
| Arquitectura | **Camino A — Servidor de aplicación con API REST** (tres capas) | Única opción que da seguridad real, autoridad de negocio centralizada y auditoría confiable, requisitos de un sistema de gobierno. |

**Caminos descartados:** conexión directa cliente→PostgreSQL (expone credenciales de DB en cada PC, lógica y auditoría no centralizadas, riesgo de seguridad grave) y API delgada a nivel repositorio (lógica de negocio repartida en clientes, mismos problemas de autoridad).

## 3. Estructura de la solución (tres capas físicas)

Los proyectos actuales se reparten en dos ejecutables que hablan por HTTP, más un contrato compartido.

**Lado servidor (corre en el servidor central):**
- `StockApp.Api` *(nuevo)* — Host ASP.NET Core. Expone endpoints REST, valida JWT, chequea permisos, delega en la lógica de negocio. Orquesta, no tiene lógica propia.
- `StockApp.Application` *(existente, se muda al servidor)* — Services con toda la lógica de negocio.
- `StockApp.Infrastructure` *(existente, se muda al servidor)* — EF Core contra PostgreSQL (proveedor Npgsql). Backups y auditoría server-side.
- `StockApp.Domain` *(existente)* — Entidades. Sin cambios de fondo.

**Contrato compartido (lo referencian ambos lados):**
- `StockApp.Contracts` *(nuevo)* — Interfaces de servicio `IXxxService`, DTOs que viajan por el cable, y enums. Único lugar donde vive la forma de los datos.
- Las interfaces de **repositorio**, `IPasswordHasher` y `IAuditLogger` **NO** cruzan al cliente: son server-side.

**Lado cliente (corre en cada PC):**
- `StockApp.Presentation` *(existente, adelgaza)* — Views y ViewModels **intactas**. Donde hoy inyecta un `ProductoService` que toca la base, ahora inyecta un `ProductoApiClient` (misma interfaz, implementación HTTP). Pierde la referencia a Infrastructure: sin EF ni SQLite en el cliente.

**Idea central:** la costura que hoy separa `Presentation` de `Application` deja de ser una llamada en memoria y pasa a ser una llamada HTTP. La Clean Architecture existente ya dibujaba esa junta.

### Fricciones de contrato a resolver
1. **Catálogo filtra entidades de Domain.** `IProductoService` (y catálogo en general) devuelve entidades `Producto/Categoria/Proveedor/UnidadMedida` con navegación (riesgo de ciclos / sobre-serialización). Introducir **DTOs de catálogo** antes de exponer por HTTP.
2. **Valorización devuelve tupla nombrada** `(Items, Totales)` → no serializa estándar. Envolver en `ValorizacionReporteDto { Items, Totales }`.
3. Movimientos, Reportes, Auditoría y Usuarios ya usan `record` DTOs / primitivos sin ciclos → listos para HTTP sin cambios.

## 4. Autenticación y autorización (JWT)

Hoy: login verifica BCrypt y guarda la sesión en un **singleton en memoria** (`InMemorySession`). No sirve para un servidor multiusuario.

**Flujo nuevo:**
1. El cliente hace `POST /auth/login` con usuario+contraseña. El servidor verifica BCrypt (server-side) y firma un **JWT** con un secreto propio. El token lleva `usuarioId`, `rol` y vencimiento.
2. Cada request del cliente incluye `Authorization: Bearer <token>` mediante un `DelegatingHandler` del HttpClient (automático).
3. El servidor valida el JWT en cada request (middleware). Del token arma una sesión **por request** (`ICurrentSession` scoped, ya no singleton).
4. **Autorización server-side:** el `AuthorizationService.Verificar(rol, acción)` actual (Admin pasa todo, Operador subconjunto) se traduce a **políticas** de ASP.NET Core. Cada endpoint declara `[Authorize(Policy = Permisos.Xxx)]`. Se reutiliza exactamente la misma regla. Fail-closed intacto.
5. **El cliente solo apaga botones:** lee el rol del token para gating cosmético de UI. La decisión real la toma el servidor (403 si un cliente se salta la UI).

**No negociables (gobierno):**
- **TLS obligatorio, aunque sea LAN.** La contraseña viaja en el login; sin HTTPS se sniffea en la red. Certificado interno del municipio.
- **Sin refresh tokens en v1** (YAGNI). Token válido lo que dura un turno (8–12h); al vencer, re-login. Logout = el cliente descarta el token.

El `PrimerArranqueService` (crea el primer admin) se muda al servidor como bootstrap del primer arranque.

## 5. Integridad de datos y concurrencia

`Producto.StockActual` es un **contador denormalizado**. Con múltiples PCs concurrentes aparece el *lost update*:
- PC-A registra salida de 10: lee stock=15, valida, escribe 5.
- PC-B (simultáneo) registra salida de 8: lee stock=15, valida, escribe 7.
- El último pisa al primero → salieron 18 de 15. Estado imposible.

**Solución — registrar un movimiento es UNA transacción atómica en el servidor:**
- La baja de stock se hace con un UPDATE **condicional y atómico**:
  `UPDATE Producto SET StockActual = StockActual - @cant WHERE Id = @id AND StockActual >= @cant`
  La base serializa el acceso a la fila. El `WHERE ... >= @cant` hace cumplir la regla "no stock negativo" a nivel base: 0 filas afectadas → stock insuficiente. Imposible *lost update*, imposible negativo.
- Insertar `MovimientoStock`, actualizar `StockActual` y escribir `LogAuditoria` van en **una sola transacción**: o pasan las tres, o ninguna.

**Cambios de infraestructura:**
- `AppDbContext` pasa a **Scoped por request** en el servidor (hoy Transient, parche de desktop). Modelo natural de ASP.NET Core.
- **Auditoría confiable:** el servidor estampa el usuario desde el token verificado, no desde lo que el cliente dice ser. Cumple uno de los "por qué" del proyecto.
- **Red de seguridad:** token de concurrencia optimista en las entidades editables del catálogo, para que dos ediciones simultáneas no se pisen calladas.

## 6. Manejo de errores y detección de "sin conexión"

En online puro aparece una categoría de error nueva que **no es un bug**: el servidor no responde. La app debe tratarla con dignidad.

Pieza central: **un solo lugar** en el cliente (un `DelegatingHandler` o `ApiClient` base) por donde pasa toda llamada. Hace tres cosas:
1. Pega el token.
2. Traduce fallas de transporte (timeout, conexión rechazada) en `SinConexionException` → banner "Sin conexión con el servidor" y deshabilita acciones.
3. Mapea códigos HTTP de vuelta al lenguaje de dominio, para **no tocar las ViewModels**. El servidor traduce excepciones de dominio a HTTP (`ProblemDetails`); el cliente las reconstruye antes de que lleguen a la VM.

| Situación en el servidor | HTTP | Cliente |
|---|---|---|
| Servidor caído / sin red | (sin respuesta) | Banner "sin conexión", bloquea acciones |
| Token vencido/inválido | 401 | Manda a login (sesión expirada) |
| Sin permiso | 403 | "No tenés permiso" (fail-closed) |
| Stock insuficiente / conflicto | 409 | Reconstruye excepción de dominio → la VM la maneja |
| Datos inválidos | 400 | Mensaje de validación |
| Error inesperado | 500 | "Ocurrió un error" + log server-side |

**No negociable:** los **writes NO se reintentan automáticamente**. Reintentar un "registrar salida" que quizás sí llegó duplica el movimiento. En v1, el usuario reintenta a mano.

## 7. Testing y despliegue por fases

**Testing:** ~90 tests actuales en 4 capas. La mayoría sobrevive sin cambios (la lógica de negocio y las ViewModels no cambian, solo se mudan de máquina). Superficie de test nueva:
- `Infrastructure.Tests` contra **PostgreSQL real** (Testcontainers). Obligatorio: **tests de concurrencia** que prueben que dos salidas simultáneas sobre el mismo producto nunca dejan stock negativo ni pierden un update.
- **Tests de integración de API** con `WebApplicationFactory`: login → token → request autorizado; Operador → 403; stock insuficiente → 409.
- **Tests de los `HttpXxxService`** del cliente: mapeo de HTTP de vuelta a excepciones de dominio.
- TDD por capas, como en el resto del repo.

**Regla de oro de despliegue:** el servidor se construye DETRÁS de la app que ya anda; el cliente se enchufa al HTTP al final, de un saque; nunca se deja el repo roto.

| Fase | Qué se hace | Estado al terminar |
|---|---|---|
| 0 — DTOs de contrato | Introducir `ProductoDto` (única entidad de catálogo con navegación) en las lecturas de catálogo + envolver la valorización en `ValorizacionReporteDto`. In-process, tests verdes. | La app de hoy sigue andando. Refactor puro, tests verdes. |
| 1 — Datos a PostgreSQL | Proveedor Npgsql, migraciones Postgres, `Infrastructure.Tests` contra Postgres. Transacción atómica de stock + tests de concurrencia. | Capa de datos probada sobre Postgres. |
| 2 — Servidor API | Crear `StockApp.Api`: endpoints sobre los services, JWT, políticas de autorización, mapeo `ProblemDetails`, TLS. Tests de integración. | Servidor 100% funcional y testeado. Desktop vieja intacta. |
| 3 — Flip del cliente | `HttpXxxService` en Presentation. Cambiar DI en `App.axaml.cs`: HTTP clients + token handler en lugar de services/repos/DbContext. Sacar referencia a Infrastructure. Login contra la API. | Ya es cliente-servidor. ViewModels sin tocar. |
| 4 — Usuarios + hardening | Completar ABM de gestión de usuarios. Empaquetado del servidor, setup de Postgres, cert TLS, bootstrap del primer admin. | Listo para piloto. |
| 5 — Finanzas (spec aparte) | Su propio ciclo, encima de esta fundación. | Fuera del alcance de este spec. |

**Refinamiento YAGNI de la Fase 0 (decidido en planificación):** la extracción del proyecto `StockApp.Contracts` se DIFIERE a la Fase 2/3. Un proyecto de contratos compartidos solo se justifica cuando hay dos consumidores reales — servidor + cliente HTTP —; hoy sigue habiendo un solo proceso. Por ahora los DTOs nacen en `Application`, siguiendo la convención existente, y se mudarán a `Contracts` cuando ese proyecto se extraiga. Además, solo `Producto` necesita DTO en esta fase: `Categoria`, `Proveedor` y `UnidadMedida` son entidades planas (sin navegación) y serializan bien tal cual; sus DTOs se harán si y cuando hagan falta.

**Detalles de despliegue:**
- **Backups:** hoy `BackupService` copia el archivo SQLite; con Postgres pasa a `pg_dump` programado server-side. El concepto sigue, el mecanismo cambia.
- **Versionado de API:** al actualizar el servidor puede haber clientes viejos. La API se versiona y el servidor rechaza con gracia a un cliente demasiado viejo ("actualizá la app"). El updater Velopack sigue sirviendo para empujar actualizaciones al cliente.

## 8. Alcance

**Incluido en este spec:**
- Migración client-server completa (fases 0–4).
- Completar la gestión de usuarios (ABM + roles), que cae naturalmente dentro del rework de auth.

**Excluido — subsistema aparte (futuro):**
- **Módulo de finanzas.** Es un subsistema independiente con su propio dominio (a relevar). Se trata con su propio ciclo brainstorm → spec → plan, montado encima de esta fundación. La estructura de `Api` y `Contracts` se deja modular para que sumarlo sea seguir el patrón.

## 9. Riesgos y no-negociables (resumen)

- **TLS obligatorio** en la API, aunque sea LAN.
- **Tests de concurrencia** sobre el contador de stock: sin ellos, la Sección 5 no existe.
- **Sin auto-retry en writes** (riesgo de duplicar movimientos).
- **Autoridad de negocio y autorización SIEMPRE server-side**; el cliente solo hace gating cosmético.
- **Versionado de API** para compatibilidad cliente/servidor.
