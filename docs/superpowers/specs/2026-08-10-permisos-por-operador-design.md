# Permisos por operador configurables por el Admin

Fecha: 2026-08-10
Estado: aprobado, pendiente de plan de implementación

## Problema

Hoy la autorización de StockApp tiene exactamente dos roles y ninguna variación entre operadores: `Admin` puede todo, y todo `Operador` puede exactamente el mismo conjunto fijo de acciones (`AuthorizationService.AccionesOperador`, un `HashSet<string>` hardcodeado en el código). No existe forma de que el Admin le dé a un operador acceso a Finanzas pero no a Tareas, o a Productos pero no a Movimientos: es binario por rol, no por persona.

El cliente pidió poder configurar, operador por operador, a qué pantallas entra cada uno. Eso no es un ajuste de UI — es reemplazar la fuente de verdad de autorización de "tabla rol→permiso en código" a "tabla usuario→permiso en base de datos", sin romper ninguna de las dos barreras de autorización que ya existen ni los ~2447 tests que hoy pasan sobre ellas.

## Contexto: cómo funciona la autorización hoy

- `src/StockApp.Application/Authorization/Permisos.cs`: 15 constantes string (`GestionarUsuarios`, `VerReportes`, `GestionarProductos`, `GestionarTablasMaestras`, `RegistrarMovimientos`, `RecalcularStock`, `VerFinanzas`, `GestionarMaestrosFinanzas`, `RegistrarGastos`, `RegistrarPagos`, `RegistrarIngresos`, `ImportarPlanillas`, `GestionarDiagnostico`, `GestionarTareas`, `AdministrarTareas`) más `Permisos.Todos` (lista explícita, sin reflection).
- `src/StockApp.Application/Authorization/AuthorizationService.cs`: `HashSet<string> AccionesOperador` (líneas 19-30) con 9 de esos 15 permisos. `Verificar(RolUsuario? rolActual, string accion)` (línea 32) lanza `UnauthorizedAccessException` si el rol no está autorizado; Admin siempre pasa (línea 37-38). `TienePermiso(RolUsuario rol, string accion)` (línea 45) es la versión booleana, sin excepción.
- `src/StockApp.Api/Program.cs:412-430`: al arrancar, itera `Permisos.Todos` y arma una policy ASP.NET por permiso con `policy.RequireClaim(StockAppClaimTypes.Rol, rolesPermitidos)`, donde `rolesPermitidos` sale de recorrer `RolUsuario` y filtrar con `AuthorizationService.TienePermiso`. La tabla rol→permiso queda congelada en el claim de rol del JWT al momento del arranque del proceso: cambiar `AccionesOperador` requiere redeploy, no hay forma de tocarla en caliente.
- Los 32 endpoints del proyecto usan `.RequireAuthorization(Permisos.X)` a nivel de grupo o de ruta individual (`src/StockApp.Api/Endpoints/*.cs`).
- **Doble barrera**: además de la policy HTTP, los servicios de `Application` llaman `IAuthorizationService.Verificar(_session.RolActual, Permisos.X)` como primera línea de sus métodos — defensa en profundidad documentada explícitamente en varios comentarios del código (ej. `BackupsEndpoints.cs:38-41`). Este es un **grep real, no una estimación**: hay **96 call sites** de `IAuthorizationService.Verificar(...)` (95 vía el campo `_auth` en 21 archivos de `Application`, más uno vía la variable `auth` inyectada directamente en `BackupsEndpoints.cs:51`, que es Singleton y no puede depender de `ICurrentSession`).
- JWT (`src/StockApp.Api/Auth/JwtTokenService.cs`): claims `usuarioId`, `rol` e `iat` (milisegundos, para que `IRevocadorTokens` pueda comparar contra revocaciones en el mismo segundo de reloj). Duración default 12 horas (`JwtOptionsFactory.ExpiracionHorasPorDefecto`). Sin refresh token.
- `IRevocadorTokens` / `RevocadorTokensEnMemoria` (`src/StockApp.Application/Auth/`): invalida tokens de un usuario comparando `iat` contra un mínimo aceptado. Singleton en memoria de proceso, con limitación documentada: el estado se pierde al reiniciar la API.
- `ICurrentSession` (`src/StockApp.Application/Interfaces/ICurrentSession.cs:11-27`) expone hoy `EstaAutenticado`, `UsuarioActual`, `RolActual`, `IniciarSesion`, `CerrarSesion` — todo sincrónico. Tiene dos implementaciones con scopes distintos: `HttpCurrentSession` (`src/StockApp.Api/Auth/`), registrada `AddScoped` en `Program.cs:139` — una instancia por request HTTP, construida leyendo los claims del `HttpContext.User` ya autenticado; y `ApiSession` (`src/StockApp.ApiClient/`), registrada `AddSingleton` en `App.axaml.cs:141-142` del desktop — una única instancia para toda la vida del proceso, poblada por login. `HttpCurrentSession.IniciarSesion`/`CerrarSesion` lanzan `NotSupportedException`: no tiene sentido mutarla, se reconstruye por request desde el JWT.
- Desktop: `ShellMainViewModel.cs:38` expone solo `public bool EsAdmin => _session.RolActual == RolUsuario.Admin;`. Se usa como `IsVisible="{Binding EsAdmin}"` **16 veces**: 14 en `Views/ShellMainView.axaml` (líneas 219, 226, 240, 247, 259, 271, 285, 292, 304, 316, 328, 340, 354, 361) y 2 en `Views/InicioView.axaml` (líneas 237, 257).
- Sesión cliente: `src/StockApp.ApiClient/ApiSession.cs` guarda `UsuarioSesion` (Id, NombreUsuario, Rol, NombreCompleto) + token JWT, poblado una sola vez en `EstablecerSesion` al loguearse. No existe endpoint `/me` ni `/auth/yo`, y `ApiSession` no tiene forma de guardar permisos.
- 403: `src/StockApp.ApiClient/ApiErrores.cs:92-98` mapea tanto `Forbidden` como `Unauthorized` a `UnauthorizedAccessException`. No hay manejo central de UI para 403 — `AuthTokenHandler.cs` (`src/StockApp.ApiClient/`) solo reacciona a 401 (dispara `SesionVencida`, cierra la sesión) y a 423 (`LicenciaDesactivada`); un 403 hoy solo llega como excepción al ViewModel que hizo el request, sin ningún efecto centralizado.
- `src/StockApp.Api/Endpoints/UsuariosEndpoints.cs`: ABM completo bajo `MapGroup("/usuarios").RequireAuthorization(Permisos.GestionarUsuarios)` — `GET /`, `POST /`, `DELETE /{id}`, `PUT /{id}/rol`, `PUT /{id}/contrasena`. `UsuarioApiClient` implementa `IUsuarioService` y está registrado como `AddTransient` en `App.axaml.cs:200`, pero ningún ViewModel del desktop lo inyecta hoy: **no existe pantalla de usuarios en el desktop**, pese a que el backend ya soporta el ABM completo.
- Auditoría: `src/StockApp.Domain/Enums/AccionAuditada.cs` es un enum append-only (comentarios explícitos de "NO reordenar, están persistidos en BD"), con el último bloque en 46-50 (`AltaTarea` … `AltaNotaTarea`).

### Restricción de contexto

Post-instalación no hay acceso al servidor. Toda configuración vive en la base de datos o en el cliente, nunca en `appsettings.json` ni en variables de entorno del servidor — el mismo criterio ya aplicado en el diseño del canal de alertas de backup.

## Decisiones tomadas

1. **Alcance**: el Admin configura, por usuario Operador individual, a qué pantallas entra. Admin no se toca: siempre tiene acceso total, y su panel de permisos se muestra deshabilitado con la leyenda "Acceso total".
2. **Unidad en la UI**: el Admin tilda **pantallas**, agrupadas por sección (Catálogo, Stock, Finanzas, Tareas, Reportes, Administración). Por debajo se traduce y persiste a los 15 permisos existentes. Ver la tabla de mapeo más abajo — construida a partir de los ítems reales de `ShellMainViewModel` y de los `.RequireAuthorization` reales de los endpoints, no de una lista supuesta.
3. **Modelo de datos**: tabla `PermisoUsuario(Id, UsuarioId FK→Usuario, Permiso string)` con índice único `(UsuarioId, Permiso)` es la VERDAD ÚNICA para los permisos configurables. Resolver = `SELECT`, sin merge ni overrides. Sin filas = sin permisos (fail-closed). `AccionesOperador` queda solo como plantilla de arranque para operadores nuevos.
4. **Migración**: backfill determinista, por usuario Operador existente, con orden explícito (no depende del orden de iteración del `HashSet`) — mismo criterio que llevó a corregir el no-determinismo del backfill de `LotesImportacion` (commit `af4321b`). Compatibilidad hacia atrás total hasta que alguien destilde algo.
5. **Propagación**: instantánea, sin desloguear. Permisos en DB, resueltos por request, con cache en memoria por `usuarioId` invalidada al guardar. No viajan en el JWT.
6. **Enforcement**: se mantiene la doble barrera. HTTP: se reemplaza el bloque de policies de `Program.cs` por un `AuthorizationHandler` que resuelve contra los permisos reales del usuario — los `.RequireAuthorization(Permisos.X)` de cada endpoint no cambian de sintaxis. Application: `IAuthorizationService.Verificar` cambia de firma para recibir la sesión completa (con los permisos ya resueltos), no solo el rol; los 96 call sites reales se actualizan mecánicamente. **`Verificar` sigue siendo sincrónico** — el I/O de resolver permisos se aísla en un middleware nuevo que corre una sola vez por request, antes de que la Application lo toque (ver "Enforcement — barrera Application" más abajo).
7. **Nuevo endpoint** `GET /auth/permisos`: permisos efectivos del usuario autenticado. El desktop lo consulta al loguearse y al navegar entre secciones.
8. **Desktop**: nueva pantalla de administración de usuarios en `Views/Administracion/` + `ViewModels/Administracion/`, bajo el permiso existente `GestionarUsuarios`. Reusa `UsuarioApiClient`/`IUsuarioService` — incluye el ABM que hoy no existe en el desktop (listar, alta, cambiar rol, cambiar contraseña, baja lógica) más el panel de permisos. Layout: lista de usuarios a la izquierda, panel de permisos del seleccionado a la derecha, botón Guardar.
9. **Menú lateral**: los 16 `IsVisible="{Binding EsAdmin}"` se reemplazan por propiedades por permiso en `ShellMainViewModel`. Ocultar ítems del menú es cosmética, no seguridad — la seguridad vive en la API.
10. **403**: manejo central en el desktop, con mensaje claro y refresco de permisos/menú — un 403 inesperado es la señal de que el Admin cambió algo mientras la sesión seguía abierta.
11. **Testing**: TDD estricto por capas, con un test guardián que recorra los endpoints existentes y verifique que cada uno sigue exigiendo el mismo permiso que exigía antes del cambio, y verificación orgánica con dos sesiones simultáneas.

## Diseño

### Qué permisos son configurables y cuáles quedan Admin-only estructural

No los 15 permisos son candidatos a vivir en `PermisoUsuario`. Cuatro de ellos protegen superficie que el propio código ya documenta como "Admin-only desde el vamos, no espera el futuro sistema de permisos por usuario" (`Permisos.cs:24-31`, comentario textual sobre `ImportarPlanillas` y `GestionarDiagnostico`) o que estructuralmente no puede quedar en manos de un Operador sin abrir una vía de escalación:

| Permiso | Por qué NO es configurable |
|---|---|
| `GestionarUsuarios` | Es el permiso que protege la propia pantalla de administración de usuarios y permisos. Si un Operador pudiera obtenerlo, podría auto-otorgarse cualquier otro permiso. |
| `ImportarPlanillas` | Ya documentado en el código como Admin-only "desde el vamos" (F5b): reemplaza datos históricos de todo el ejercicio. |
| `GestionarDiagnostico` | Ya documentado en el código como Admin-only "desde el vamos", mismo criterio que `ImportarPlanillas`: protege backups y logs del servidor. |
| `AdministrarTareas` | Regla ya establecida por el spec del módulo de Tareas (2026-08-01): cancelar y repriorizar decide sobre trabajo que otro cargó, reservado a `Admin` sin excepción. |

Estos cuatro **nunca** se resuelven contra `PermisoUsuario`: el `Admin` los tiene siempre, el `Operador` nunca, sin consultar la tabla ni la cache. No existe fila para ellos en `PermisoUsuario` — no hace falta que exista.

Los **11 restantes** (`VerReportes`, `GestionarProductos`, `GestionarTablasMaestras`, `RegistrarMovimientos`, `RecalcularStock`, `VerFinanzas`, `GestionarMaestrosFinanzas`, `RegistrarGastos`, `RegistrarPagos`, `RegistrarIngresos`, `GestionarTareas`) son los configurables: para Operador, se resuelven contra la tabla; para Admin, siempre `true`.

### Modelo de datos

```
PermisoUsuario
  Id          int         PK
  UsuarioId   int         FK → Usuarios.Id, Restrict
  Permiso     string      uno de los 11 permisos configurables
  UNIQUE (UsuarioId, Permiso)
```

`Restrict`, igual que toda otra FK hacia `Usuarios` en el proyecto (`AppDbContext.cs:94`, comentario explícito: "Restrict porque Producto/Usuario usan baja lógica"). `Usuario` nunca se borra físicamente, solo `Activo = false` — así que esta FK sigue la misma convención que el resto, sin excepción.

**Qué pasa con las filas al dar de baja a un usuario**: la baja es lógica (`Activo = false`), así que sus filas en `PermisoUsuario` no se tocan y quedan en la tabla. Eso no genera basura funcional: un usuario inactivo no puede autenticarse (el login lo filtra, mismo criterio que ya aplica al resto del sistema), así que nunca llega a generar un JWT ni a pasar por el `AuthorizationHandler` — sus filas de permisos simplemente quedan sin efecto, huérfanas en el sentido de "no se consultan nunca", pero no en el sentido de integridad referencial (la FK sigue apuntando a un `Usuario` que existe, solo que inactivo). Si más adelante se reactiva el usuario, sus permisos previos siguen ahí tal como estaban.

DbSet: `PermisosUsuario` (mismo criterio de pluralización que `NotasTarea`, `CorridasBackup`).

### Resolución y cache

Nuevo componente en `Application`, `IProveedorPermisos` — misma forma que `IRevocadorTokens` (una sola interfaz que junta lectura cacheada y escritura con invalidación, en vez de separar en dos servicios):

```csharp
public interface IProveedorPermisos
{
    /// <summary>Permisos configurables del usuario. Cache-first; SELECT contra
    /// PermisoUsuario solo en cache-miss.</summary>
    Task<IReadOnlySet<string>> ObtenerAsync(int usuarioId);

    /// <summary>Reemplaza el set completo del usuario e invalida su entrada de cache
    /// en la misma operación.</summary>
    Task GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos);
}
```

Implementación singleton en memoria de proceso (`ConcurrentDictionary<int, IReadOnlySet<string>>` como cache, más `IPermisoUsuarioRepository` para el `SELECT`/reemplazo contra Postgres en cache-miss o en `GuardarAsync`) — mismo patrón que `RevocadorTokensEnMemoria`. Nada viaja en el JWT: cada resolución consulta el estado actual (de cache o de DB).

`ObtenerAsync` es un componente de **infraestructura de resolución** (cache + repo), no de política: devuelve el `SELECT` crudo, sin Admin bypass y sin conocer los 4 permisos estructurales. Esa lógica de negocio vive en `AuthorizationService.Verificar` (ver más abajo), que es sincrónico y nunca llama a `IProveedorPermisos` directamente — lee un resultado ya resuelto.

### El middleware nuevo: resolver una vez por request, no una vez por permiso

Este es el punto que hace que `Verificar` pueda seguir siendo sincrónico pese a que la fuente de verdad ahora vive en la base de datos. Tres piezas:

1. **`ICurrentSession` gana un miembro nuevo**, sincrónico: `IReadOnlySet<string> PermisosActuales { get; }` (vacío si no hay sesión), más un método para poblarlo: `void EstablecerPermisos(IReadOnlySet<string> permisos)`.
   - `HttpCurrentSession` (Scoped, API): antes no tenía ningún campo mutable — todo se computaba en el momento leyendo `HttpContext.User`. Gana un campo privado (`_permisos`) que el middleware nuevo puebla una vez por request; `PermisosActuales` lo expone.
   - `ApiSession` (Singleton, desktop): lo puebla el login (tras `POST /auth/login`, con un `GET /auth/permisos` inmediato) y el refresh al navegar entre secciones (decisión 7).
2. **Middleware nuevo** (`PoblarPermisosMiddleware` o similar), insertado en `Program.cs` **entre `app.UseAuthorization()` (línea 574) y el primer `Map*Endpoints()` (`MapAuthEndpoints`, línea 578)** — después de que el usuario está autenticado y autorizado a nivel de policy, antes de que cualquier endpoint (y por lo tanto cualquier servicio de `Application`) se ejecute:
   ```csharp
   app.UseAuthentication();
   app.UseAuthorization();

   app.Use(async (context, next) =>
   {
       if (context.User.Identity?.IsAuthenticated == true)
       {
           var usuarioId = /* claim StockAppClaimTypes.UsuarioId */;
           var session = context.RequestServices.GetRequiredService<ICurrentSession>();
           var proveedor = context.RequestServices.GetRequiredService<IProveedorPermisos>();
           session.EstablecerPermisos(await proveedor.ObtenerAsync(usuarioId));
       }
       await next(context);
   });

   app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));
   app.MapAuthEndpoints();
   // ...
   ```
   Es el único punto de I/O asíncrono de todo este diseño del lado Application. Para requests sin sesión (rutas anónimas: login, licencia) no hace nada.
3. **Orden con el `AuthorizationHandler` de HTTP (sutil, documentado a propósito)**: el handler de la barrera HTTP (siguiente sección) corre **dentro de** `app.UseAuthorization()` — es decir, **antes** que este middleware, porque `UseAuthorization()` está en la línea 574 y el middleware nuevo se inserta después. El handler ya resuelve contra `IProveedorPermisos.ObtenerAsync(usuarioId)` para decidir si autoriza la policy del endpoint. Cuando el middleware nuevo corre milisegundos después, pega **al mismo cache**, ya tibio: es un **cache-hit**, no un segundo `SELECT`. El resultado neto es **un solo hit de I/O por request** (en el peor caso, cache-miss), repartido entre dos consumidores que llegan a la misma cache — no dos. Si algún día se reordena este pipeline, hay que preservar que el handler corra primero: si el middleware corriera antes, seguiría funcionando (el handler encontraría la cache ya tibia, mismo resultado), pero si el middleware corriera SIN que el handler haya corrido antes en algún endpoint sin policy (ej. `GET /auth/permisos`, que usa `.RequireAuthorization()` desnudo), el middleware sigue siendo el que hace el `SELECT` inicial — cualquiera de los dos que llegue primero paga el cache-miss, el otro pega la cache tibia.

### Enforcement — barrera HTTP

Se reemplaza el bloque de `Program.cs:417-430` (`new AuthorizationService()` + `RequireClaim` por policy) por un requirement/handler custom:

```csharp
public record PermisoRequirement(string Permiso) : IAuthorizationRequirement;

public class PermisoAuthorizationHandler : AuthorizationHandler<PermisoRequirement>
{
    // Scoped: inyecta IProveedorPermisos (Singleton) y lee los claims del usuario actual.
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermisoRequirement requirement)
    {
        var rol = /* claim StockAppClaimTypes.Rol */;
        if (rol == RolUsuario.Admin) { context.Succeed(requirement); return; }
        if (PermisosEstructuralesAdmin.Contains(requirement.Permiso)) return; // Operador nunca

        var usuarioId = /* claim StockAppClaimTypes.UsuarioId */;
        var permisos = await _proveedor.ObtenerAsync(usuarioId);
        if (permisos.Contains(requirement.Permiso))
            context.Succeed(requirement);
    }
}
```

`Program.cs` sigue iterando `Permisos.Todos` para registrar una policy por permiso, pero cada policy pasa a tener `policy.Requirements.Add(new PermisoRequirement(permiso))` en vez de `RequireClaim`. Los 32 endpoints existentes no cambian una sola línea: siguen escribiendo `.RequireAuthorization(Permisos.X)`, porque el nombre de policy sigue siendo el string del permiso. La lista `PermisosEstructuralesAdmin` (los 4 de la tabla de arriba) vive en `AuthorizationService`, compartida por el handler y por `Verificar` — una sola fuente de verdad para "estos 4 nunca se resuelven contra la tabla".

### Enforcement — barrera Application

`IAuthorizationService.Verificar` cambia de firma, pero **sigue siendo sincrónico**:

```csharp
// Antes:
void Verificar(RolUsuario? rolActual, string accion);
// Después:
void Verificar(ICurrentSession sesion, string accion);
```

No hace ningún `SELECT`: lee `sesion.PermisosActuales`, ya resuelto por el middleware nuevo (o, en el desktop, por `ApiSession` tras el login/refresh) antes de que cualquier servicio de `Application` se ejecute. Lógica:

1. Si `!sesion.EstaAutenticado` → `UnauthorizedAccessException` (igual que hoy con `rolActual is null`).
2. Si `sesion.RolActual == Admin` → pasa siempre.
3. Si `accion` es uno de los 4 permisos estructurales → rechaza siempre para Operador, sin mirar `PermisosActuales`.
4. Si no, `sesion.PermisosActuales.Contains(accion)` decide.

Los 96 call sites cambian de `_auth.Verificar(_session.RolActual, Permisos.X)` a `_auth.Verificar(_session, Permisos.X)` — un cambio mecánico de argumento, **sin agregar `await` ni tocar la firma `async`/`Task` de ningún método contenedor**. El único caso fuera del patrón `_auth.Verificar(_session.RolActual, ...)` (`BackupsEndpoints.cs:49-58`, lambda sincrónica que inyecta `IAuthorizationService` y `ICurrentSession` directamente) tampoco necesita cambiar de forma: sigue siendo una llamada sincrónica, solo cambia qué le pasa.

`TienePermiso(RolUsuario, string)` se elimina de la interfaz: su único consumidor era `Program.cs` para derivar `rolesPermitidos` al arrancar, y ese código desaparece con el cambio de arriba. La plantilla de arranque para operadores nuevos (hoy `AccionesOperador`) se extrae a una lista pública nueva y ordenada explícitamente, `AuthorizationService.PermisosInicialesOperador`, consumida por dos lugares:

- **Backfill de la migración** (usuarios Operador existentes).
- **`UsuarioService.AltaUsuarioAsync`**, cuando `rol == RolUsuario.Operador`: siembra las mismas filas al crear el usuario. Sin este paso, todo Operador nuevo arrancaría con cero permisos (fail-closed correcto pero inútil en la práctica) — el HashSet documentado en la decisión 3 como "plantilla de arranque" existe justamente para este caso.

### Migración y backfill

Migración EF nueva que crea `PermisosUsuario` con el índice único, y en la misma migración un `migrationBuilder.Sql(...)` que inserta, para cada usuario con `Rol = Operador` (valor de enum, no string — mismo criterio que las migraciones existentes que tocan `RolUsuario`), una fila por cada uno de los 9 permisos que hoy tiene `AccionesOperador`, en el orden textual en que están declarados en el archivo (no el orden de iteración del `HashSet`, que no está garantizado):

```sql
INSERT INTO "PermisosUsuario" ("UsuarioId", "Permiso")
SELECT u."Id", p.permiso
FROM "Usuarios" u
CROSS JOIN (VALUES
    ('catalogo.productos'), ('movimientos.registrar'), ('stock.recalcular'),
    ('finanzas.ver'), ('finanzas.maestros'), ('finanzas.gastos'),
    ('finanzas.pagos'), ('finanzas.ingresos'), ('tareas.gestionar')
) AS p(permiso)
WHERE u."Rol" = 1  -- RolUsuario.Operador
ON CONFLICT ("UsuarioId", "Permiso") DO NOTHING;
```

Los strings van literales (no `Permisos.GestionarProductos` desde SQL) pero en el mismo orden en que aparecen en `AccionesOperador` hoy, para que el diff de la migración sea revisable a simple vista contra el archivo actual. Esto sigue el mismo espíritu que la corrección aplicada en `af4321b` al backfill de `LotesImportacion`: una lista explícita y ordenada, nunca un orden implícito de una estructura de datos que no lo garantiza.

`VerReportes` y `GestionarTablasMaestras` **no** entran en el backfill: hoy ningún Operador los tiene (no están en `AccionesOperador`), así que el backfill los deja fuera — compatibilidad hacia atrás total, ni un permiso de más ni de menos respecto al comportamiento actual.

### Nuevo endpoint: permisos propios

`GET /auth/permisos` en `src/StockApp.Api/Endpoints/AuthEndpoints.cs`, `.RequireAuthorization()` sin permiso específico (cualquier usuario autenticado puede consultar sus propios permisos efectivos). Devuelve la lista de los 11 permisos configurables que el usuario autenticado tiene actualmente (para Admin, los 11 completos siempre; para Operador, el resultado de `IProveedorPermisos.ObtenerAsync`). No incluye los 4 estructurales — el desktop no necesita preguntarlos porque nunca son configurables y su gating de menú ya depende solo de `EsAdmin` para esas pantallas puntuales (Mantenimiento, Administración de usuarios, Importación) y de `AdministrarTareas` dentro de la pantalla de Tareas.

### Endpoint de administración: guardar permisos de un usuario

`GET /usuarios/{id:int}/permisos` y `PUT /usuarios/{id:int}/permisos`, agregados al grupo existente de `UsuariosEndpoints.cs` (ya protegido por `Permisos.GestionarUsuarios`, sin cambios ahí). `PUT` recibe `{ Permisos: string[] }` y:

1. Rechaza con 400 si el usuario objetivo es `Admin` — el servidor no confía en que el cliente deshabilite el panel; lo valida también del lado seguro.
2. Rechaza con 400 si algún string no está en la whitelist de los 11 permisos configurables (defensa contra un cliente viejo o manipulado intentando colar `GestionarUsuarios`).
3. Llama a `IProveedorPermisos.GuardarAsync(usuarioId, permisos)`, que reemplaza el set completo (delete + insert dentro de una transacción) e invalida la entrada de cache de ese usuario en la misma operación.

## Tabla de mapeo pantalla → permiso

Construida recorriendo los comandos `Nav*` reales de `ShellMainViewModel.cs` y los `.RequireAuthorization` reales de cada grupo de endpoints. "Configurable" = aparece como checkbox en el panel del Admin; "Admin-only estructural" = la pantalla directamente no es alcanzable por un Operador, sin checkbox.

| Sección (menú) | Pantalla | ViewModel | Endpoint(s) HTTP | Permiso(s) | Tipo |
|---|---|---|---|---|---|
| — | Inicio | `InicioViewModel` | (sin gating propio) | — | Siempre visible |
| — | Productos | `ProductoListViewModel` | `GET/POST/PUT /productos` | `GestionarProductos` | Configurable |
| — | Productos → Recalcular stock (acción) | — | `POST /productos/.../recalcular` | `RecalcularStock` | Configurable (acción dentro de Productos) |
| — | Registrar Entrada | `EntradaRegistroViewModel` | `POST /movimientos` | `RegistrarMovimientos` | Configurable |
| — | Registrar Salida | `SalidaRegistroViewModel` | `POST /movimientos` | `RegistrarMovimientos` | Configurable |
| — | Ingreso por factura | `IngresoPorFacturaViewModel` | `POST /movimientos/ingreso-factura` | `RegistrarMovimientos` | Configurable |
| — | Historial de movimientos | `MovimientoHistorialViewModel` | `GET /movimientos/historial` | `RegistrarMovimientos` | Configurable |
| Tareas | Tareas | `TareaListViewModel` | `GET/POST /tareas`, `/tomar`, `/soltar`, `/terminar`, `/notas` | `GestionarTareas` | Configurable |
| Tareas | Tareas → Cancelar / Cambiar prioridad (acción) | — | `POST /tareas/{id}/cancelar`, `/prioridad` | `AdministrarTareas` | Admin-only estructural |
| Finanzas | Gastos y facturas | `GastosViewModel` | `GET /finanzas/gastos` (ver) + `POST/PUT/DELETE` (`RegistrarGastos`), pagos (`RegistrarPagos`) | `VerFinanzas` + `RegistrarGastos` + `RegistrarPagos` | Configurable (checkbox compuesto) |
| Finanzas | Ingresos de caja | `IngresosViewModel` | `GET /finanzas/ingresos` (ver) + `POST/PUT/DELETE` | `VerFinanzas` + `RegistrarIngresos` | Configurable (checkbox compuesto) |
| Finanzas | Libro caja | `LibroCajaViewModel` | `GET /finanzas/libro-caja` | `VerFinanzas` | Configurable |
| Finanzas | Control POA | `ControlPoaViewModel` | `GET /finanzas/control-poa` | `VerFinanzas` | Configurable |
| Finanzas | Calendario de pagos | `CalendarioPagosViewModel` | `GET /finanzas/calendario-pagos` | `VerFinanzas` | Configurable |
| Finanzas | Maestros de finanzas | `MaestrosFinanzasViewModel` | `/finanzas/fuentes`, `/finanzas/rubros`, `/finanzas/lineas-poa` (grupo) | `GestionarMaestrosFinanzas` | Configurable |
| Importación | Importar planillas | `ImportacionViewModel` | `/finanzas/importacion` | `ImportarPlanillas` | Admin-only estructural |
| Tablas maestras | Categorías | `CategoriaListViewModel` | `GET/POST/PUT/DELETE /categorias` | `GestionarTablasMaestras` | Configurable |
| Tablas maestras | Proveedores | `ProveedorListViewModel` | `/proveedores` (grupo) | `GestionarTablasMaestras` | Configurable |
| Tablas maestras | Unidades de medida | `UnidadMedidaListViewModel` | `/unidades-medida` (grupo) | `GestionarTablasMaestras` | Configurable |
| Reportes | Valorización de inventario | `ValorizacionViewModel` | `/reportes` (grupo) | `VerReportes` | Configurable |
| Reportes | Stock por categoría | `StockCategoriaViewModel` | `/reportes` (grupo) | `VerReportes` | Configurable |
| Reportes | Historial por producto | `HistorialPorProductoViewModel` | `/reportes` (grupo) | `VerReportes` | Configurable |
| Reportes | Productos más movidos | `MasMovidosViewModel` | `/reportes` (grupo) | `VerReportes` | Configurable |
| Reportes | Log de auditoría | `AuditoriaLogViewModel` | `GET /auditoria` | `VerReportes` | Configurable |
| Administración | Mantenimiento | `MantenimientoViewModel` | `/backups`, `/logs`, `/configuracion/alertas` | `GestionarDiagnostico` | Admin-only estructural |
| Administración | Administración de usuarios (nueva) | `UsuariosAdminViewModel` | `/usuarios`, `/usuarios/{id}/permisos` | `GestionarUsuarios` | Admin-only estructural |

**Sobre los checkboxes compuestos de Finanzas** (Gastos, Ingresos): `VerFinanzas` es el mismo permiso que gatea Libro caja, Control POA y Calendario de pagos. Tildar "Gastos y facturas" enciende `VerFinanzas` (que también habilita, visiblemente, esas otras tres pantallas de solo lectura) más `RegistrarGastos` y `RegistrarPagos` (exclusivos de esa pantalla). El panel de UI muestra esto explícitamente: todas las pantallas que comparten `VerFinanzas` están visualmente agrupadas bajo un mismo indicador, y tildar cualquiera de las cinco pantallas de Finanzas enciende ese indicador compartido — nunca es un efecto invisible.

**Sobre `RecalcularStock` y las acciones de Tareas**: no son pantallas propias, son botones dentro de una pantalla ya cubierta por otro permiso (Productos, Tareas). El checkbox de Productos otorga `GestionarProductos` + `RecalcularStock` juntos (mismo criterio de checkbox compuesto que Gastos/Ingresos). El botón cancelar/repriorizar de Tareas no tiene checkbox: es Admin-only estructural, así que nunca aparece para un Operador sin importar la configuración.

## UI

### Pantalla de administración de usuarios

`Views/Administracion/UsuariosAdminView.axaml` + `ViewModels/Administracion/UsuariosAdminViewModel.cs`, al lado de `MantenimientoView`/`MantenimientoViewModel`. Layout de dos columnas:

- **Izquierda**: lista de usuarios (`IUsuarioService.ListarAsync`), con alta, baja lógica, cambio de rol y cambio de contraseña — el ABM que el backend ya soporta (`UsuarioApiClient`) pero que hoy no tiene pantalla.
- **Derecha**: panel de permisos del usuario seleccionado.
  - Si el seleccionado es `Admin`: panel completo deshabilitado, leyenda "Acceso total".
  - Si es `Operador`: 11 checkboxes agrupados por sección (mismo agrupamiento visual que el sidebar), cada uno atado a la propiedad compartida de su permiso — cuando dos pantallas comparten permiso, sus checkboxes están *bindeados a la misma propiedad*, así que tildar uno tilda el otro en el acto, sin lógica extra que lo sincronice.
  - Botón "Guardar" → `PUT /usuarios/{id}/permisos`.

Nuevo ítem de menú "Administración de usuarios" en `ShellMainView.axaml`, dentro de la sección "Administración" ya existente, junto a "Mantenimiento".

### Gating del menú lateral

`ShellMainViewModel` gana propiedades nuevas por permiso configurable (`PuedeVerFinanzas`, `PuedeGestionarProductos`, `PuedeGestionarTareas`, etc.), calculadas contra el cache local de permisos que trae `GET /auth/permisos`. Los 16 `IsVisible="{Binding EsAdmin}"` de `ShellMainView.axaml` e `InicioView.axaml` se reemplazan uno a uno por la propiedad de permiso correspondiente según la tabla de arriba (los 4 estructurales — Mantenimiento, Administración de usuarios, Importación — siguen atados a `EsAdmin`, sin cambios, porque estructuralmente nunca dependen de la tabla).

**Esto es cosmética, no seguridad.** Ocultar un ítem del menú evita que un Operador *encuentre* una pantalla a la que no tiene acceso, pero la autorización real vive en la API (las dos barreras descritas arriba). Si el binding tuviera un bug y mostrara un ítem de más, el peor caso es un clic que rebota con 403 — nunca un acceso real no autorizado.

`ApiSession` implementa los miembros nuevos de `ICurrentSession` descritos en "El middleware nuevo" (`PermisosActuales` + `EstablecerPermisos`), poblados tras el login y refrescados al navegar entre secciones (decisión 7) — no un helper aparte: las propiedades `Puede*` de `ShellMainViewModel` leen directamente `_session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.X)`, la misma condición que ya evalúa `AuthorizationService.Verificar` del lado servidor.

### Manejo del 403

`AuthTokenHandler.cs` gana una rama nueva junto a la de 401/423: ante `HttpStatusCode.Forbidden`, dispara un evento nuevo (`AccesoRevocado` o similar) que la composition root cablea a: mostrar un mensaje claro ("Ya no tenés acceso a esta sección") y forzar un refresco de `GET /auth/permisos` para que el menú se actualice sin desloguear — a diferencia del 401, la sesión sigue siendo válida, solo cambió lo que el usuario puede ver.

## Auditoría

`AccionAuditada` gana **un valor nuevo**, append-only al final de la lista actual (que termina en `AltaNotaTarea = 50`):

```
ModificacionPermisosUsuario = 51,
```

Se registra desde `PUT /usuarios/{id}/permisos` (o desde el servicio que lo respalda), con el mismo patrón que ya usan `AltaUsuario`/`CambioRol`/`CambioContrasena` en `UsuarioService`. No hace falta un segundo valor: alta y baja de permisos individuales son la misma acción semántica ("el Admin cambió el set de permisos de este usuario"), igual que `CambioRol` no distingue promoción de degradación.

## Testing

TDD estricto por capas, mismo patrón del repositorio (fakes manuales en Application/Api, Testcontainers en Infrastructure):

- **Application**:
  - `AuthorizationServiceTests`: `Verificar(sesion, accion)` — sin sesión lanza `UnauthorizedAccessException`; Admin pasa siempre sin tocar `PermisosActuales`; los 4 permisos estructurales rechazan siempre para Operador aunque `PermisosActuales` los contuviera (defensa en profundidad del propio test: simular una sesión "envenenada" con un estructural adentro y verificar que igual rechaza); el resto decide por `PermisosActuales.Contains`. Ningún test de esta clase necesita mockear I/O — es exactamente el punto del diseño.
  - `ProveedorPermisosTests` (nuevo): cache-miss dispara un solo `SELECT`; cache-hit no vuelve a tocar el repositorio; `GuardarAsync` persiste y deja la cache invalidada (el siguiente `ObtenerAsync` recarga desde DB); fail-closed sin filas.
  - `UsuarioServiceTests`: `AltaUsuarioAsync` siembra `PermisosInicialesOperador` para un Operador nuevo, no siembra nada para un Admin nuevo.
- **Infrastructure** (`PermisoUsuarioRepositoryTests`, Postgres real vía Testcontainers): el índice único `(UsuarioId, Permiso)` rechaza duplicados; el backfill de la migración deja exactamente los 9 permisos esperados por cada Operador preexistente, ninguno de más.
- **Api**: matriz 401/403/200 sobre `GET/PUT /usuarios/{id}/permisos` y `GET /auth/permisos`, incluyendo el 400 de intentar tocar los permisos de un Admin y el 400 de un permiso fuera de whitelist. El middleware nuevo se prueba con `WebApplicationFactory`: un usuario autenticado deja `PermisosActuales` poblado en `ICurrentSession` para cuando el endpoint se ejecuta; una ruta anónima no dispara ningún `SELECT`. **Test guardián**: recorre `Permisos.Todos` y, para cada uno, verifica contra un fixture congelado (lista de endpoint → permiso tal como está hoy en el código) que la policy sigue exigiendo lo mismo que exigía antes de este cambio — protege contra que alguien borre un `.RequireAuthorization` durante el reemplazo del bloque de `Program.cs`.
- **ApiClient**: deserialización de `GET /auth/permisos`, mapeo de 403 a la excepción correcta.
- **Presentation**: las propiedades `Puede*` de `ShellMainViewModel` reflejan el cache de `ApiSession`; los checkboxes compuestos del panel de permisos comparten estado; el panel se deshabilita para Admin; el manejo de 403 refresca menú sin desloguear.
- **Verificación orgánica**: dos sesiones abiertas en paralelo (una Admin, una Operador). El Admin destilda un permiso del Operador y guarda. El Operador, sin cerrar sesión ni recargar, hace clic en la pantalla correspondiente y rebota con el mensaje de 403 — sin haber vuelto a loguearse.

## Riesgos

Este cambio reemplaza el guard que protege **toda la aplicación** (32 endpoints, ~2447 tests hoy verdes sobre las dos barreras actuales). Un error acá no rompe una pantalla — abre un agujero silencioso de seguridad, o peor, cierra el acceso de todo el mundo a todo. Puntos concretos de atención:

- **Los 4 permisos estructurales son el punto de falla más peligroso.** Tanto el `PermisoAuthorizationHandler` como `AuthorizationService.Verificar` tienen que cortar por `PermisosEstructuralesAdmin` ANTES de mirar `PermisosActuales`/`IProveedorPermisos`, no después. Si ese orden se invirtiera, una fila colada por error (o un bug futuro en `PUT /usuarios/{id}/permisos`) le daría a un Operador la capacidad de auto-otorgarse `GestionarUsuarios` y desde ahí cualquier otro permiso.
- **El orden del pipeline HTTP es sutil y silencioso si se rompe.** El diseño depende de que el `AuthorizationHandler` (dentro de `UseAuthorization()`) corra antes que el middleware nuevo que puebla `ICurrentSession.PermisosActuales`, para que ambos compartan el mismo cache-hit en vez de duplicar el `SELECT`. Si alguien reordena el pipeline de `Program.cs` sin saber esto, el sistema sigue funcionando igual (no hay bug de seguridad), pero deja de cumplirse "un solo hit de I/O por request" — una regresión de performance silenciosa, sin ningún test que la agarre salvo uno que cuente queries reales contra Postgres en un request autenticado.
- **96 call sites, no ~30.** El cambio de firma de `Verificar` toca 21 archivos de `Application` (95 llamadas vía `_auth`) más `BackupsEndpoints.cs` (una llamada vía variable inyectada), un volumen bastante mayor al estimado inicialmente. Es mecánico (cambiar el primer argumento de `_session.RolActual` a `_session`) y **no propaga `async`/`await`** — pero su tamaño exige revisión cuidadosa call site por call site, no un sed ciego: hay que confirmar que cada uno sigue compilando con la sesión completa en vez del rol solo.
- El comportamiento por defecto (fail-closed, sin filas = sin permisos) es correcto para Operadores, pero si el backfill de la migración fallara parcialmente (algún Operador sin sus 9 filas), ese usuario pierde acceso a todo silenciosamente hasta que alguien lo note — de ahí la importancia del test de Infrastructure que verifica el conteo exacto por usuario tras la migración.

## Limitación conocida

La cache de permisos (dentro de `IProveedorPermisos`) es por proceso. Con más de una instancia de API corriendo en paralelo, un cambio de permisos guardado en una instancia no invalida la cache de las otras: un Operador podría seguir teniendo acceso "viejo" en una instancia hasta que esa cache expire o el proceso reinicie. Hoy corre una sola instancia de la API bajo `systemd`, así que esto no aplica en la práctica — y es exactamente la misma limitación que ya tiene `RevocadorTokensEnMemoria` (documentada en su propio archivo), así que este diseño es consistente con el precedente ya aceptado, no una excepción nueva.

## Fuera de alcance

- Roles personalizables por el Admin (crear roles propios tipo "Contable" o "Depósito"). El modelo sigue siendo dos roles fijos (`Admin`/`Operador`) con permisos configurables solo para el segundo.
- Permisos a nivel de campo o de registro individual (ej. "puede ver Gastos pero no los montos", o "puede editar estos productos pero no esos").
- Permisos temporales con vencimiento.
- Delegación de permisos entre usuarios.
- Auditoría de cambios de permisos más allá del valor nuevo de `AccionAuditada` propuesto arriba (`ModificacionPermisosUsuario`) — no hay un historial de "quién tenía qué permiso en qué momento" más allá de lo que ya registra `LogAuditoria` por evento.
