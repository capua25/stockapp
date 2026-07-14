# Diseño: Seed de admin en arranque + eliminación del bootstrap HTTP (deuda D7)

Fecha: 2026-07-14
Estado: Aprobado
Deuda que salda: D7 (Fase 3a) — ventana de "admin génesis anónimo"

## Problema

La API expone hoy dos endpoints anónimos de bootstrap en `src/StockApp.Api/Endpoints/AuthEndpoints.cs`:

- `GET /auth/primer-arranque` → devuelve `{ requiereCrearAdmin: bool }`.
- `POST /auth/primer-admin` → crea el primer admin (rol Admin) cuando la DB no tiene usuarios.

La única defensa es el estado de la BD: con un usuario presente, el POST devuelve 409 para siempre. Pero en el despliegue real la API escucha en `0.0.0.0:5043` sobre HTTP plano en la LAN (`src/StockApp.Api/README.md`). Eso abre una **race condition de una sola vez**: desde que la DB está migrada y vacía hasta que se crea el primer usuario, cualquier atacante en la red local puede ganarle la carrera al operador legítimo con un `POST /auth/primer-admin` y quedarse como el ÚNICO administrador del sistema, bloqueando al operador legítimo (que recibiría 409).

## Decisión

No se restringe el bootstrap: se **elimina por completo** el bootstrap HTTP y el primer admin nace por **seed en el arranque de la API**. Al no existir ningún endpoint anónimo, la ventana de red no se reduce — desaparece.

El flujo de "primer arranque" tampoco se necesita ya en el desktop: la app arranca siempre yendo directo al login.

## Arquitectura

### 1. Seed de admin en el arranque de la API (lo nuevo)

Nueva clase `BootstrapAdminSeeder` (unidad testeable de forma aislada). Depende de:
- `IPrimerArranqueService` (la interfaz existente en Application; ya expone `RequiereCrearAdminAsync()` y `CrearAdminInicialAsync(...)`).
- La configuración (`IConfiguration` / un `BootstrapOptions` con `AdminUser` y `Password`).
- Un logger.

Método `SembrarAsync()`:
- Si `RequiereCrearAdminAsync()` es **false** (ya existe algún usuario) → no hace nada y **no mira la configuración**. Es idempotente y no molesta en arranques normales.
- Si es **true** (DB virgen) → lee `Bootstrap:AdminUser` y `Bootstrap:Password` de la configuración y llama a `CrearAdminInicialAsync(adminUser, password)`. Esa ruta ya trae la validación de contraseña (`ContrasenaValidator`, mínimo 6 caracteres), el rechazo de nombre en blanco y el semáforo anti-TOCTOU existentes.

**Punto de inserción**: `src/StockApp.Api/Program.cs`, dentro del scope que ya existe para las migraciones (líneas ~207-211), justo después de `await db.Database.MigrateAsync();` y antes de cerrar el `using`. Ese scope ya tiene un `IServiceProvider` activo para resolver el seeder. No se crea ningún `IHostedService`.

**Configuración**:
- `Bootstrap:AdminUser` y `Bootstrap:Password`.
- En desarrollo: user-secrets.
- En el servidor: variables de entorno (`Bootstrap__AdminUser`, `Bootstrap__Password`).
- No van en `appsettings.json` versionado (igual que `Jwt:Secret`).

**Fail-fast**: si la DB está virgen y la config falta o es inválida (usuario en blanco o contraseña que no pasa el validador), el seed lanza una excepción con un mensaje claro (por ejemplo: "DB sin usuarios y falta configurar Bootstrap:AdminUser/Bootstrap:Password") y la **API no arranca**. Es consistente con el fail-fast que ya existe para `Jwt:Secret` (`Program.cs:199-201`). La config solo se exige cuando la DB está virgen; en arranques normales ni se lee.

Tras crear el admin por seed, la rotación de contraseña y el alta de más administradores se hacen desde el desktop, logueado como admin (ABM de usuarios existente).

### 2. Eliminación del bootstrap HTTP

Se elimina:

**Server** (`src/StockApp.Api`):
- Los endpoints `GET /auth/primer-arranque` y `POST /auth/primer-admin` en `Endpoints/AuthEndpoints.cs`.

**ApiClient** (`src/StockApp.ApiClient`):
- `PrimerArranqueApiClient.cs` completo.

**Desktop** (`src/StockApp.Presentation`):
- `Views/PrimerArranqueView.axaml` y `Views/PrimerArranqueView.axaml.cs`.
- `ViewModels/PrimerArranqueViewModel.cs`.
- El registro DI `AddTransient<IPrimerArranqueService, PrimerArranqueApiClient>()` en `App.axaml.cs` (~línea 156).
- En `ViewModels/ShellViewModel.cs`: la dependencia `_primerArranqueService` (campo, parámetro de constructor y asignación), la llamada a `RequiereCrearAdminAsync()` dentro de `InicializarAsync()` y el método `MostrarPrimerArranque()`. `InicializarAsync()` pasa a llamar directamente a `MostrarLogin()` (conservando el disparo de `EvaluarYAsignarOverlayAsync()` para el chequeo de actualizaciones). Con esto el arranque del desktop ya no ejecuta una llamada HTTP que pueda fallar.

### 3. Lo que NO se toca

- La interfaz `IPrimerArranqueService` (`src/StockApp.Application/Auth/IPrimerArranqueService.cs`): se conserva; ahora la consume el seed.
- La implementación server `PrimerArranqueService` (`src/StockApp.Application/Auth/PrimerArranqueService.cs`) y su registro DI en `Program.cs:93`: se conservan y se reutilizan para el seed.
- `ContrasenaValidator` (`src/StockApp.Application/Auth/ContrasenaValidator.cs`): lo comparte `UsuarioService` (alta y cambio de contraseña). Intocable.

## Testing

### Gotcha conocido de la suite de integración

`tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs` migra una DB virgen (Testcontainers) en `InitializeAsync` y luego arranca el host real (`Program`). Con el seed fail-fast activo, si la fixture no provee la configuración de bootstrap, el arranque del host lanzaría la excepción de fail-fast y **se caería toda la suite**.

**Solución**: `ApiFactory` setea `Bootstrap:AdminUser` y `Bootstrap:Password` en su `ConfigureAppConfiguration`, igual que ya hace con `Jwt:Secret`. El `TRUNCATE ... RESTART IDENTITY CASCADE` por test (`ApiTestBase`) sigue limpiando entre casos; el seed corre una sola vez al construir el host.

### Tests a eliminar

- `tests/StockApp.Api.Tests/PrimerArranqueEndpointTests.cs`.
- `tests/StockApp.ApiClient.Tests/PrimerArranqueApiClientTests.cs`.
- `PrimerArranqueViewModelTests` (desktop).
- Referencias a `IPrimerArranqueService` / `RequiereCrearAdminAsync` en `ShellViewModelTests`, `LoginViewModelTests`, `ComposicionDIApiTests`, `ShellViewModelActualizacionTests` (ajustar, no necesariamente eliminar).

### Tests a agregar

Tests del `BootstrapAdminSeeder`:
- DB virgen + config válida → crea el admin (rol Admin) con las credenciales configuradas.
- DB con usuarios ya presentes → no hace nada (no crea, no lee config).
- DB virgen + config faltante → fail-fast (excepción con mensaje claro).
- DB virgen + contraseña inválida (menos de 6 caracteres) → fail-fast.

## Consecuencias

- La ventana de admin génesis anónimo desaparece: no queda ningún endpoint anónimo de creación de admin.
- El bootstrap se hace exclusivamente en el servidor, por quien tiene acceso a su configuración.
- El arranque del desktop se simplifica y se vuelve más robusto (una llamada HTTP menos que puede fallar).
- Se elimina código muerto (View + ViewModel + ApiClient de primer arranque).
- Se pierde la sugerencia de crear un segundo admin de respaldo que ofrecía `PrimerArranqueViewModel`; se compensa con el ABM de usuarios del desktop.
