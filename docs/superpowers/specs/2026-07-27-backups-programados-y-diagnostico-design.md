# Backups programados + Diagnóstico de logs (descarga desde el desktop)

**Fecha:** 2026-07-27
**Estado:** Diseño aprobado
**Depende de:** Fase 3 (arquitectura cliente-servidor: API en LAN + N terminales desktop), Fase 2b/D1 (autorización por permisos derivados de `Permisos.Todos`)

## 1. Contexto y motivación

Restricción dura del proyecto (post-instalación): **no hay acceso al servidor**. Todo control debe vivir en el cliente desktop o en la base de datos, nunca en configuración del servidor tocada a mano.

Hoy no existe ningún backup automático de PostgreSQL ni ningún log persistido: la API arranca con el logger default de ASP.NET Core (confirmado — `Program.cs` no registra Serilog ni ningún sink de archivo) → todo va a stdout y nadie lo captura. Si el proceso corre como servicio, esa salida se pierde apenas rota la consola.

Objetivo de este diseño: (1) backups automáticos de la base, con retención y sin intervención manual, y (2) captura de logs de la API con descarga desde el desktop por una cuenta admin, para poder diagnosticar sin acceso físico ni remoto al servidor.

## 2. Decisiones tomadas

1. **Topología sin definir → diseño agnóstico de SO y de host.** `pg_dump` se invoca por TCP contra la misma connection string que ya usa `AppDbContext` (`ConnectionStrings:Default`); la ruta del binario se resuelve por descubrimiento (PATH del proceso) con override por configuración (`Backups:PgDumpPath`) para el caso en que no esté en el PATH del servicio.

2. **Scheduler dentro de la API** (`IHostedService`/`BackgroundService`), NO worker separado ni disparo desde el cliente. Sería el **primer** `BackgroundService` del repo — hoy no hay ninguno (verificado: cero registros de `AddHostedService`/`IHostedService`/`BackgroundService` en `src/`). Razón: cero superficie de instalación nueva, que es justamente lo que exige "sin acceso al servidor". Trade-off aceptado: si la API está caída no hay backup, pero en ese escenario el sistema no se usa igual, y el aviso al login (decisión 6) lo delata al volver.

3. **Rescate de backups**: los dumps quedan en el servidor; el desktop admin los lista y descarga. Sin backup manual bajo demanda (YAGNI explícito — ver §8).

4. **Logs nivel Warning+ solamente.** La auditoría de "quién hizo qué" ya está cubierta por `LogAuditoria`/`LogsAuditoria` en la DB (tabla y `DbSet` confirmados en `src/StockApp.Infrastructure/Persistence/AppDbContext.cs:16`); estos logs de archivo son para diagnóstico técnico, no para trazabilidad de negocio.

5. **Retención escalonada** (grandfather-father-son), NO los 6 planos del requisito original: los 6 más recientes + el último de cada uno de los últimos 7 días + el último de cada una de las últimas 4 semanas. Razón: 6 backups cada 12h cubren solo 3 días; un daño detectado el lunes sobre algo ocurrido el viernes ya no tendría backup útil con el esquema plano.

6. **Aviso al iniciar sesión** si el último backup falló o si pasaron más de 26h sin uno exitoso. Umbral de 26h = dos ventanas de 12h + 2h de margen, para no disparar falsas alarmas por reinicios del servidor (una falsa alarma repetida vuelve el banner invisible con el tiempo). Visible solo para Admin.

## 3. Backup — componentes

### 3.1 Persistencia

Los servicios de `StockApp.Application` **no usan `AppDbContext` directo**: siguen el patrón repositorio (verificado en `GastoService`, `RubroGastoService`, etc.) — la interfaz vive en `StockApp.Application/Interfaces/` (ej. `IGastoRepository.cs`), la implementación en `StockApp.Infrastructure/Repositories/` sobre `AppDbContext`. `ServicioBackup` sigue el mismo patrón:

- `ICorridaBackupRepository` en `src/StockApp.Application/Interfaces/ICorridaBackupRepository.cs`.
- `CorridaBackupRepository` en `src/StockApp.Infrastructure/Repositories/CorridaBackupRepository.cs`, sobre `AppDbContext`.
- Entidad `CorridaBackup` en `src/StockApp.Domain/Entities/CorridaBackup.cs` (mismo directorio que `Gasto`, `LogAuditoria`), campos: `Id`, `IniciadaEn`/`FinalizadaEn` (UTC), `Resultado` (enum `Exitosa`|`Fallida`), `NombreArchivo`, `TamanioBytes`, `MotivoFallo`.
- `DbSet<CorridaBackup> CorridasBackup => Set<CorridaBackup>();` en `AppDbContext` (convención confirmada: `Gasto`→`Gastos`, `PagoGasto`→`PagosGasto`, `RubroGasto`→`RubrosGasto` — el calificador va después del sustantivo pluralizado).
- Migración EF nueva siguiendo el patrón real observado (`yyyyMMddHHmmss_PascalCase`, ej. `20260722132814_AmpliaIndiceFacturaConNumeroOrden.cs`).

Los bytes del dump **nunca** entran a la base; solo el metadato. (Guardar el respaldo de la base dentro de la base es circular.)

### 3.2 Piezas nuevas

- **`PoliticaRetencion`** (`StockApp.Application/Backups/PoliticaRetencion.cs`): función pura. Firma: recibe la lista de corridas exitosas y un `DateTime actual` como **parámetro directo** (no una interfaz `IClock`/`IReloj` — el proyecto no tiene esa abstracción hoy; la convención confirmada en todo `StockApp.Application` es `DateTime.UtcNow` inline, ej. `ProductoService.cs:65`, `AdjuntoService.cs:46`). "Reloj inyectado" en este diseño significa exactamente eso: la fecha actual entra como argumento del método, así el test la fija sin mockear nada. Devuelve qué corridas eliminar. Es la lógica con más casos borde del feature — testeable sin DB, sin filesystem, sin reloj real.
- **`IEjecutorPgDump`** (`StockApp.Application/Backups/IEjecutorPgDump.cs`): abstracción del proceso hijo, mismo espíritu que `IFingerprintMaquina`/`IAlmacenLicencia` en `Licenciamiento/` (interfaz en Application, adaptador real en Infrastructure). Implementación real (`EjecutorPgDumpProceso` en `StockApp.Infrastructure/Backups/`) usa `Process.Start`, formato `-Fc` (custom, comprimido nativo — un dump plano se infla por los `bytea` de adjuntos ya presentes en el esquema de Finanzas), timeout configurable, captura de `stderr`. Fake en tests.
- **`ServicioBackup`** (`StockApp.Application/Backups/ServicioBackup.cs`): orquesta dump → registrar corrida (`ICorridaBackupRepository`) → aplicar `PoliticaRetencion` → borrar huérfanos en disco. No conoce `Process` ni timers — eso queda en `IEjecutorPgDump` y en el `BackgroundService`.
- **`BackupProgramadoService : BackgroundService`** (`StockApp.Api/Backups/BackupProgramadoService.cs`) con `PeriodicTimer`. Al arrancar, si la última corrida exitosa tiene más de 12h (o no hay ninguna), dispara enseguida (cubre el caso "servidor apagado durante la ventana"). **Debe crear su propio `IServiceScope` por corrida** — `AppDbContext` (vía `ICorridaBackupRepository`) es Scoped y el hosted service es Singleton por diseño de ASP.NET Core; dejarlo escrito explícitamente en el código porque es el error clásico de la primera vez que se agrega un `BackgroundService` a un proyecto (y este es el primero del repo).
- Directorio: `IUserDataPathProvider.GetBackupsDirectory()` — **ya existe** y ya resuelve a `{GetDataDirectory()}/backups` (`UserDataPathProvider.cs:23-24`). `IUserDataPathProvider` ya está registrado `Singleton` en `StockApp.Api/Program.cs:155` (comentario propio del código: "IUserDataPathProvider lo usa" junto a `ServicioLicencia`, que es Scoped), así que la API ya puede resolverlo sin cambios de DI. `GetDataDirectory()` devuelve `%LOCALAPPDATA%\StockApp\` en Windows / `~/.local/share/StockApp/` en Linux, relativo a la cuenta que corre el proceso de la API — no al usuario del desktop.

### 3.3 Manejo de errores

Ninguna falla de backup tumba la API. Todo fallo (binario ausente, credenciales rechazadas, disco lleno, timeout) se captura en `ServicioBackup`, se persiste como `CorridaBackup` con `Resultado = Fallida` y el `stderr` de `pg_dump` en `MotivoFallo`, y se loguea Warning+. El `PeriodicTimer` sigue vivo y reintenta en la ventana siguiente. Escritura a `.tmp` con rename atómico al cerrar con éxito; barrido de `.tmp` huérfanos al arrancar `BackupProgramadoService`.

## 4. Logs — componentes

- Serilog con sink de archivo, rolling diario, en un `GetLogsDirectory()` **nuevo** en `IUserDataPathProvider`/`UserDataPathProvider` (`src/StockApp.Infrastructure/Platform/`), siguiendo el patrón ya existente de `GetBackupsDirectory()`/`GetLicenciaPath()` — no se inventa una convención de rutas nueva, se agrega un método más a la interfaz y su única implementación.
- Paquete nuevo: `Serilog.AspNetCore` (+ `Serilog.Sinks.File`) en `src/StockApp.Api/StockApp.Api.csproj`. El proyecto está en `net10.0` con EF Core `10.*` (confirmado en los `.csproj`); se toma la versión estable de Serilog.AspNetCore compatible con .NET 10 vigente al momento de implementar — no se fija un número acá para no dejar una versión potencialmente vencida en el diseño.
- Nivel mínimo Warning. Retención 30 días (rolling + retención por antigüedad de archivo, no por cantidad).
- `ClearProviders()` + Serilog en `Program.cs`, conservando salida a consola (`WriteTo.Console()` además de `WriteTo.File(...)`) — útil cuando se arranca la API a mano para debug.
- **`DomainExceptionHandler` (`src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`) debe empezar a loguear.** Confirmado leyendo el archivo: hoy no inyecta ningún `ILogger` ni llama a nada de logging — mapea la excepción a `ProblemDetails` y listo. Sin este cambio, agregamos infraestructura de logging que no registra justamente los errores no anticipados (el caso `_ => 500` del switch). Se agrega `ILogger<DomainExceptionHandler>` al constructor y se loguea la excepción (Warning para los casos de negocio mapeados 4xx, Error para el 500 fail-closed).
- **Saneador obligatorio**: enmascarar `Password=`, `Secret=`, `Bearer ` antes de que el evento llegue al archivo (un `Serilog.Core.IDestructuringPolicy` o un `Enrich.With`/filtro sobre el mensaje renderizado). Razón: los stack traces de Npgsql pueden arrastrar la connection string con la contraseña, y ese zip termina en la máquina de un administrativo y probablemente adjunto en un mail.

## 5. Endpoints — backups y logs

Ambos grupos comparten permiso y exención de licencia, pero van en archivos y rutas separadas (mismo criterio que `Permisos.GestionarTablasMaestras`, que ya hoy protege tres grupos de endpoints distintos — `CategoriasEndpoints`, `ProveedoresEndpoints`, `UnidadesMedidaEndpoints` — bajo un único permiso). Se descarta a propósito un único grupo `/diagnostico` que mezcle ambos recursos: `/backups` y `/logs` son recursos con forma distinta (uno tiene `Id`, el otro no) y el nombre de ruta debe decirlo.

- **Permiso nuevo**: `Permisos.GestionarDiagnostico = "diagnostico.gestionar"` en `src/StockApp.Application/Authorization/Permisos.cs`, agregado a `Permisos.Todos`. Deliberadamente **ausente** de `AccionesOperador` en `AuthorizationService.cs` → fail-closed, resuelve a `[Admin]` solo por la lógica ya existente de `Verificar`/`TienePermiso` (mismo patrón exacto que `GestionarUsuarios`, que también está ausente de esa lista). Un solo permiso cubre ambos grupos de endpoints — no hay hoy ninguna señal en el pedido del usuario de que backups y logs deban tener control de acceso independiente, y ambos son, por diseño, superficie exclusiva de Admin.
- **`src/StockApp.Api/Endpoints/BackupsEndpoints.cs`**, grupo `/backups`, `.RequireAuthorization(Permisos.GestionarDiagnostico)`:
  - Listado: metadatos por corrida (resultado, fecha, tamaño) — respaldado por `ICorridaBackupRepository`.
  - Descarga de un backup puntual: por `Id` de `CorridaBackup` (no por nombre de archivo crudo — el nombre se resuelve server-side contra el registro en DB, mismo argumento anti-path-traversal que el ZIP de logs, más abajo).
- **`src/StockApp.Api/Endpoints/LogsEndpoints.cs`**, grupo `/logs`, `.RequireAuthorization(Permisos.GestionarDiagnostico)`:
  - Listado: metadatos agregados (cantidad de archivos, rango de fechas, tamaño total) — leído directo del filesystem en `GetLogsDirectory()`.
  - Descarga: **un único ZIP con todos los archivos**, armado por streaming (`System.IO.Compression.ZipArchive` sobre el `Response.Body`, sin materializar el zip completo en memoria ni en disco temporal). Justificación explícita: un endpoint que recibe un nombre de archivo como parámetro es superficie de path traversal (el proyecto ya tuvo que escribir `SanitizarYValidarExtension` para adjuntos por esta misma razón — `AdjuntoValidador.cs`); sin parámetro de nombre esa superficie no existe. Con Warning+ el volumen de logs es de kilobytes, así que la descarga selectiva por archivo es complejidad y riesgo a cambio de nada.
  - Si `GetLogsDirectory()` no existe o no es escribible, la API arranca igual y avisa por consola (mismo espíritu que el resto del logging: un problema de logging no puede dejar al municipio sin sistema). Sin archivos de log → el listado devuelve vacío y la descarga responde `404` con mensaje claro — nunca un ZIP vacío que parezca un backup corrupto.
- **Ambos grupos exentos de `BloqueoLicenciaMiddleware`.** Confirmado el mecanismo real: `BloqueoLicenciaMiddleware.EsRutaPermitida` (`src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs:41-43`) hace `path.StartsWithSegments(...)` contra una lista de prefijos (`/licencia`, `/auth/reset-admin`). Se agregan `/backups` y `/logs` a esa misma lista. Cuando la licencia vence es justo cuando más se necesitan estos endpoints.

## 6. Desktop

- Nueva `Views/Administracion/MantenimientoView.axaml` + `MantenimientoViewModel` (hoy no existe ninguna sección de Administración en el sidebar). Nav command nuevo en `ShellMainViewModel`, con `IsVisible="{Binding EsAdmin}"`, siguiendo el patrón ya usado por el resto de las entradas del sidebar condicionadas por rol.
- Una pantalla, dos zonas: Backups (lista de corridas con resultado/fecha/tamaño + botón de descarga por fila) y Diagnóstico (resumen de logs + botón de descarga del ZIP).
- **La vista debe enganchar `DataContextChanged` para disparar la carga inicial.** Confirmado como patrón recurrente y obligatorio del proyecto: las Views de Avalonia acá no se auto-inicializan; sin este enganche la pantalla queda en blanco hasta la primera interacción.
- **Extender `IServicioGuardadoArchivo`** (`src/StockApp.Presentation/Services/IServicioGuardadoArchivo.cs`). Firma actual confirmada: únicamente `Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido)`, usada hoy solo por flujos de exportación CSV (texto). Un dump `-Fc` y un ZIP son binarios de tamaño no acotado → se agrega:

  ```csharp
  Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido);
  ```

  sobre `Stream`, para que la implementación (`ServicioGuardadoArchivo`) copie del `Stream` de red al `Stream` de escritura del `IStorageFile` sin bufferear el archivo completo en memoria (mismo patrón que ya usa `GuardarTextoAsync` con `StorageProvider.SaveFilePickerAsync`, cambiando `StreamWriter` de texto por `CopyToAsync` de streams).

- **`HttpClient` de descargas aparte.** Confirmado en `App.axaml.cs:141-163`: el `HttpClient` singleton actual tiene `Timeout = TimeSpan.FromSeconds(10)` (comentario propio del código: "LAN local — cubre el reporte más pesado... el default de 100s colgaría la UI"). Bajar un dump de varios MB/GB por ese cliente puede exceder los 10s y abortar la descarga. Se registra un segundo `HttpClient` con nombre (`AddHttpClient("Descargas", ...)` o instancia separada vía factory) apuntando a la misma `BaseAddress` y el mismo `AuthTokenHandler`, pero con timeout mayor (o `Timeout.InfiniteTimeSpan` + `CancellationToken` propio de la operación de descarga). Si no se hace este cambio, la funcionalidad falla en producción el día que el dump crezca, no en la máquina de desarrollo.
- ApiClients nuevos (`BackupsApiClient` contra `/backups`, `LogsApiClient` contra `/logs`) en `src/StockApp.ApiClient/`, siguiendo el patrón ya establecido ahí: clase sellada, records "wire" internos para las respuestas, `ApiErrores.EnviarAsync` + `AsegurarExitoAsync` para el manejo de errores HTTP.
- Endpoint de salud del backup (última corrida, último éxito, si está vencido según el umbral de 26h) consumido por `InicioViewModel` (que ya expone `EsAdmin`) → banner visible solo para admin en la pantalla de Inicio.

## 7. Testing

Por capas, siguiendo el patrón TDD ya usado en el resto del repo (unit puro donde se pueda, integración con `WebApplicationFactory`/Testcontainers para lo que toca DB/HTTP real):

- **`PoliticaRetencion`**: tests puros pasando el `DateTime actual` como parámetro — huecos por corridas fallidas, cruce de mes, semanas parciales, menos de 6 backups, exactamente 6.
- **`ServicioBackup`** con `IEjecutorPgDump` fake: éxito, binario ausente, credenciales rechazadas, timeout, disco lleno, limpieza de `.tmp` huérfano.
- **`BackupProgramadoService`**: que cree su propio `IServiceScope` por corrida (test de composición, en la línea de lo que ya hace el repo para DI — ver convención de tests de composición cruzando `new ServiceCollection()`); que al arrancar con la última corrida vieja dispare enseguida.
- **Endpoints** (`StockApp.Api.Tests`, `WebApplicationFactory`): matriz 401 sin token / 403 rol Operador / 200 Admin / 404 sin archivos / **200 con licencia vencida** (que no se cuele el `423` de `BloqueoLicenciaMiddleware`).
- **Saneador de logs**: que `Password=`, `Secret=` y `Bearer ` salgan enmascarados del evento antes de escribirse.
- **Desktop**: ViewModels con fakes de los ApiClients nuevos; test headless para `MantenimientoView` (con el guard de `IconProvider` en `TestAppBuilder` que ya existe en la suite).
- **Test de integración de restaurabilidad** (categoría aparte, requiere PostgreSQL real vía Testcontainers): dump real → restore en una base temporal → verificar que tablas y conteos coinciden. Es el **único** test que prueba que el feature cumple su propósito real. Sin él, esto es un generador de archivos `.dump` sin garantía de que sirvan para algo.

## 8. Fuera de alcance (YAGNI explícito)

- Backup manual bajo demanda desde el desktop.
- Nivel de log ajustable en runtime desde el cliente.
- Descarga selectiva de archivos de log individuales (solo el ZIP completo).
- Envío de backups fuera del servidor (S3, FTP, mail).
- Restauración desde el desktop (el test de restaurabilidad de §7 valida el dump, no expone un flujo de restore al usuario).
