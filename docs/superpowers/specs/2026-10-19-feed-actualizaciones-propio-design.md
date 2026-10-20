# StockApp — Feed de actualizaciones propio en el VPS y chequeo horario

**Fecha:** 2026-10-19
**Estado:** diseño para revisión de Martin. Las decisiones marcadas **(decidido por Martin)** ya están tomadas; las marcadas **(decisión del asistente)** son propuestas para que Martin las confirme o corrija antes de planificar.
**Alcance:** actualizador del desktop (`src/StockApp.Presentation/Actualizaciones/`, `src/StockApp.Application/Actualizaciones/`), la API (`src/StockApp.Api/`) como servidor del feed, los scripts de deploy (`deploy/`) y de empaquetado (`build/`).
**Fuera de alcance:** Linux/AppImage, el zip de testers, las instalaciones en LAN con API propia, la compatibilidad de versiones cliente/API, hacer privado el repo (ver §3).

## 1. Intención y porqué

Hoy el desktop (Avalonia + Velopack 1.2.0) se actualiza desde GitHub Releases del repo público `capua25/stockapp`, y cada release se sube a mano por la web de GitHub. La API va a correr en producción en un VPS que Martin controla y al que tiene acceso. El municipio usa esa API del VPS **(decidido por Martin, 2026-10-19)**, así que el VPS es el único feed de producción.

Queremos dos cosas:

1. **Dejar de depender de GitHub.** Cada instalación se actualiza desde el servidor de **su propia API**. Publicar una versión pasa a ser correr un script que la sube al VPS, sin pasar por la web de GitHub.
2. **Que una actualización llegue aunque la app no se reinicie.** Hoy hay un único chequeo, al arrancar, y la app queda abierta todo el día. Se suma un chequeo cada 1 hora.

La regla que gobierna el chequeo horario: **nunca se bloquea la app de golpe en medio de la sesión.** Perder una factura cargada a medias destruye la confianza en el sistema. Una actualización crítica llega, a más tardar, en el próximo arranque.

## 2. Objetivos

- O1. El cliente busca, descarga y aplica actualizaciones **solo** desde `<url base de la API>/updates/`. No se agrega configuración nueva por instalación.
- O2. La API sirve el feed: es anónimo, no lista el directorio, responde aunque la licencia no esté activada y usa content-types correctos.
- O3. Un script (`build/publicar-release.sh`) publica una versión en el VPS de forma que ningún cliente vea nunca un feed que apunte a un paquete que todavía no está.
- O4. Un chequeo horario, además del de arranque, se comporta según la tabla de §5.4 y notifica una sola vez por versión.
- O5. El flujo de arranque (severidades, modal, bloqueo, modo degradado) queda **igual que hoy**.

## 3. No-objetivos

- **Instalaciones en LAN con API propia (decidido por Martin, 2026-10-19).** El municipio usa la API del VPS, no una instalación en LAN con su propia API, así que ese escenario queda fuera de alcance. Si existiera una, su feed estaría vacío y no se construye ningún mecanismo para alcanzarla. Tampoco se diseña para HTTP sin TLS.
- **Linux.** `build/pack-linux.sh` y el AppImage no se tocan, aunque `pack-linux.sh:133` todavía diga "subir a un GitHub Release".
- **El zip de testers** (`build/pack-testers-win.sh`). No tiene actualizador.
- **La compatibilidad de versiones cliente/API.** Que un cliente nuevo hable con una API vieja, o al revés, es otro problema y va por separado.
- **Hacer privado el repo.** Ya se puede, pero Martin lo decide aparte.
- **Decidir el contenido de la próxima release.** `build/RELEASE_NOTES.md` dice 0.2.0 y el csproj (`src/StockApp.Presentation/StockApp.Presentation.csproj:18`) dice 0.4.0. Esto se anota como pendiente (§9) y no se resuelve acá.
- **Limpiar versiones viejas del servidor.** Tampoco hay retención automática de paquetes viejos en el VPS (ver §6).
- **Una cuenta regresiva que fuerce el bloqueo a mitad de sesión.** Se evaluó y se descartó **(decidido por Martin)**: suma complejidad y no evita perder trabajo sin guardar.

## 4. Anti-objetivos (lo que NO puede pasar)

- **A1. Perder trabajo sin guardar.** Ningún chequeo horario puede reiniciar la app, cubrirla con un bloqueo ni abrir un modal que interrumpa. El reinicio pasa solo cuando el usuario toca un botón.
- **A2. Un cliente que ve un feed que apunta a un paquete que falta.** Si `releases.win.json` anuncia una versión, cada `.nupkg` que lista ya tiene que estar en el servidor, completo y con el tamaño correcto.
- **A3. Romper la otra app del VPS.** El VPS es compartido con "pinar", cuyo nginx ocupa los puertos 80 y 443. No se toca nginx. Ningún comando tiene alcance global: todo se acota por nombre a StockApp (servicio `stockapp-api`, `/var/lib/stockapp`, `~/stockapp-deploy`).
- **A4. Que una instalación sin licencia activada no pueda actualizarse.** Con la licencia bloqueada, la API devuelve 423 a casi todo. El feed tiene que quedar fuera de ese bloqueo, porque si no el arreglo de un problema de licencia nunca llega.
- **A5. Un feed que lista el directorio o que sirve archivos fuera de la carpeta del feed.**
- **A6. Que la API no arranque porque falta la carpeta del feed.** El feed es accesorio, y la API tiene que levantar igual.

## 5. Diseño

### 5.1 Decisiones de Martin

1. Producción es un VPS que Martin controla y al que puede subir archivos. Las actualizaciones se sirven desde ese servidor.
2. GitHub sale del actualizador por completo, sin quedar ni como fallback.
3. Hay un chequeo cada 1 hora, además del de arranque.
4. El comportamiento a mitad de sesión es el de la tabla de §5.4, que Martin aprobó. Una versión se notifica una sola vez. El chequeo horario falla en silencio si no hay red, y el modo degradado queda solo para el arranque. Nunca hay un bloqueo abrupto a mitad de sesión.
5. **(decidido por Martin, 2026-10-19)** El municipio usa la API del VPS, no una instalación en LAN con API propia. El único feed de producción es el VPS (ver §3).
6. **(decidido por Martin, 2026-10-19)** No hay instalaciones reales hechas con el `Setup.exe` de Velopack: solo existen los zips de testers, que no tienen actualizador. No hace falta una release de transición en GitHub (R9 cerrado).

**Resoluciones del asistente a las preguntas abiertas** (están desarrolladas en §5.4 y §5.5):
- **(decisión del asistente)** Retirar una versión mala se hace publicando la versión buena anterior con un número **mayor** (§5.5).
- **(decisión del asistente)** Cuando el cliente salta varias versiones, la severidad efectiva es el máximo entre todas las releases posteriores a la instalada, hasta el destino (§5.4).
- **(decisión del asistente)** Si aplicar una actualización falla, esa versión no cuenta como ya notificada (§5.4).
- **(decisión del asistente)** El script de publicación releva el servidor por SSH antes de subir (§5.5).

### 5.2 Servidor: la API sirve el feed

**La API sirve el feed ella misma, con archivos estáticos de ASP.NET Core (decisión del asistente).** No se usa nginx porque el nginx del VPS es de pinar y no se toca (A3). Kestrel ya sirve TLS directo: `deploy/stockapp-api.service:55` arma `ASPNETCORE_URLS=__API_SCHEME__://__API_BIND__:__API_PORT__`, y `deploy/install.sh:239-263` valida los certificados cuando `API_SCHEME=https`. Servir desde la API también hace que el feed viva siempre en la misma URL base que el cliente ya conoce, lo que hace posible §5.3.

**Carpeta.** La ruta por defecto es `/var/lib/stockapp/updates/` y se configura con la clave `Actualizaciones:Directorio`. En `api.env` se escribe como `Actualizaciones__Directorio`, y `install.sh:100-103` la pasa tal cual a través del passthrough de variables no conocidas.
- La unit ya tiene `ReadWritePaths=/var/lib/stockapp` (`deploy/stockapp-api.service:89`), que cubre la subcarpeta. Bajo `ProtectSystem=strict`, la API solo necesita **leerla**.
- Dueño y permisos: `stockapp:stockapp`, carpeta 750 y archivos 640, igual que `$STOCKAPP_HOME` (`install.sh:316-318`). El servicio lee como `stockapp`, y el operador escribe con `sudo` (ver §5.5).
- **Gotcha:** `PhysicalFileProvider` tira `DirectoryNotFoundException` si la carpeta no existe. Por A6, en el arranque la API intenta crearla (`Directory.CreateDirectory`). Si no puede, por ejemplo en desarrollo sin permisos, loguea una advertencia y **no monta** `/updates`, sin tumbar el proceso.

**Montaje en `Program.cs`.** Se monta con `UseStaticFiles` y `RequestPath = "/updates"`.
- Va **después** de `app.UseMiddleware<BloqueoLicenciaMiddleware>()` (`src/StockApp.Api/Program.cs:644-647`) y **antes** de `UseAuthentication` (`:649`). Así, la exención por licencia de A4 pasa por la lista explícita de rutas permitidas, que se puede testear por mutación. Si se montara antes del middleware, la exención quedaría implícita en el orden, y un reordenamiento futuro la rompería sin aviso.
- Se configura `ServeUnknownFileTypes = false` y un `FileExtensionContentTypeProvider` **explícito** con `.nupkg` → `application/octet-stream` y `.json` → `application/json`. Si se publica el instalador, también `.exe` → `application/octet-stream`. El provider por defecto no conoce `.nupkg` y lo devolvería 404. Con el provider explícito, cualquier archivo con otra extensión, como el `.tmp` de la publicación atómica (§5.5), da 404.
- No se registra `UseDirectoryBrowser`, así que `GET /updates/` da 404. `PhysicalFileProvider` ya rechaza las rutas que intentan salir de la raíz con `..` (A5).
- Cabeceras de caché: `releases.win.json` sale con `Cache-Control: no-cache`, para que nadie guarde un feed viejo. Los `.nupkg` son inmutables porque llevan la versión en el nombre, así que pueden salir con caché larga.
- **Qué archivos necesita Velopack.** `SimpleWebSource` pide `releases.{channel}.json`, que acá es `releases.win.json` (`$Channel = "win"`, `build/pack-win.ps1:48`). Después descarga cada `.nupkg` por su `FileName`, relativo a la URL base. Velopack puede agregar query string (`?arch=…&localVersion=…`), y los archivos estáticos la ignoran. **El implementador tiene que confirmar contra el fuente de Velopack 1.2.0 que nada más se pide**, por ejemplo el `RELEASES` legado sin extensión. Si se pidiera, habría que mapear un archivo sin extensión, cosa que el provider no hace por extensión. Esto requiere un mapeo puntual por nombre, no `ServeUnknownFileTypes = true`.

**Exención de licencia.** Se agrega `|| path.StartsWithSegments("/updates")` a `BloqueoLicenciaMiddleware.EsRutaPermitida` (`src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs:74-81`). También se actualiza el `<summary>` de la clase con un "POR QUÉ /updates" (A4), siguiendo el formato de los otros. `StartsWithSegments` no matchea `/updatesX`.

**Anonimato.** `AddAuthorization` (`Program.cs:456-463`) no define `FallbackPolicy`, así que todo lo que no pida autorización es anónimo por defecto. `UseStaticFiles` corta el pipeline antes de que intervenga la autorización. El feed **tiene que** seguir siendo anónimo, porque `SimpleWebSource` usa su propio `HttpClient`, sin el `AuthTokenHandler` de la app. Un test lo fija (§7).

**Scripts de deploy.** Siempre se acotan por nombre a StockApp.
- `deploy/install.sh` crea `${STOCKAPP_HOME}/updates` (`install -d -o stockapp -g stockapp -m 750`) junto al bloque de `install.sh:316-318`. Lo hace de forma idempotente y sin tocar el contenido si ya existe.
- `deploy/deploy-vps.sh` copia `install.sh` solo si cambió (paso 4). Como este cambio lo modifica, el próximo deploy lo lleva solo. No hace falta tocar `deploy-vps.sh` más allá de, opcionalmente, mencionar el feed en el relevamiento del paso 0.
- `deploy/.env.example` documenta `Actualizaciones__Directorio` como override opcional.

### 5.3 Cliente: una sola fuente, derivada de la URL de la API

**De dónde sale la URL del feed (decisión del asistente).** Se deriva de la URL base de la API que la app ya resuelve: `ResolucionConexion.ResolverApiBaseUrl` (`src/StockApp.Configuracion/ResolucionConexion.cs:59`), con la precedencia `conexion.json` (lo que escribe el Configurador en `%AppData%\GestionMunicipal\`) → `appsettings.json` → `ConexionDefaults.UrlPorDefecto`. A eso se le agrega `updates/`. No se agrega configuración nueva por instalación.
- La derivación es una función pura y testeable, por ejemplo `UrlFeedActualizaciones.Desde(string apiBaseUrl)`. Normaliza la barra final: tanto `https://h:8080` como `https://h:8080/` dan `https://h:8080/updates/`. Respeta un path base si lo hubiera, y no lo reemplaza.
- La URL se resuelve una sola vez, en `ConfigurarServicios`, igual que el `HttpClient` de la API. Si el Configurador cambia la conexión, el cambio aplica en el próximo arranque, lo mismo que hoy pasa con la API.

**`VelopackGatewayReal.BuildSource`** (`src/StockApp.Presentation/Actualizaciones/VelopackGatewayReal.cs:54-72`) queda en `return new SimpleWebSource(options.FeedUrl);`.

**`UpdaterOptions`** (`src/StockApp.Presentation/Actualizaciones/UpdaterOptions.cs`) queda con una sola propiedad requerida, `FeedUrl`. Lo que se borra está en §5.6.

**Cableado en `App.axaml.cs`.** El bloque está en `src/StockApp.Presentation/App.axaml.cs:421-444`: el comentario arranca en 423, la lectura de `Updater:GitHub*` está en 427-436 y el `new UpdaterOptions { … FeedPropiUrl = null … }` en 438-444, con `FeedPropiUrl = null` en la línea 442. Todo eso se reemplaza por la construcción de `UpdaterOptions { FeedUrl = UrlFeedActualizaciones.Desde(<apiBaseUrl ya resuelta>) }`, y se reusa la misma variable que alimenta el `HttpClient` principal. El resto del bloque, de 446 a 457 (gateway, `VelopackUpdateService`, `PoliticaUxActualizacion`, `CoordinadorActualizacion`), queda igual, salvo lo que agrega §5.4.

**El arranque no cambia.** `ShellViewModel.EvaluarYAsignarOverlayAsync` → `CoordinadorActualizacion.EvaluarEnArranqueAsync` → `PoliticaUxActualizacion.Decidir` → `ResolverOverlayViewModel` queda tal cual.

**Severidad efectiva con varias versiones de salto (decisión del asistente).** Si el cliente tiene la 0.3.0 y el feed ya está en la 0.5.0, la severidad efectiva es el **máximo** entre las severidades de todas las releases posteriores a la instalada, hasta el destino, no solo la de `TargetFullRelease`. Así, una 0.4.0 crítica no se pierde porque la 0.5.0 sea normal. El implementador tiene que determinar cómo obtener las notas de las releases intermedias con Velopack 1.2.0: las notas de `UpdateInfo.DeltasToTarget`, o leer las entradas de `releases.win.json` del feed (que ya incluye `NotesMarkdown` por asset). Esto afecta a `VelopackUpdateService` y a sus tests (§7).

**Corrección a una premisa: la severidad viaja en el feed, no solo en el nupkg.** El `releases.win.json` real generado por vpk 1.2.0 (`releases/win/releases.win.json`, versión 0.2.0) tiene en cada asset un campo `NotesMarkdown` con las notas completas, y la primera línea es `severity: normal`. `VelopackUpdateService.BuscarAsync` lee `TargetFullRelease.NotesMarkdown` y se lo pasa a `SeverityParser`, así que **la severidad se conoce al consultar el feed, sin descargar el paquete**. Con `SimpleWebSource` esto funciona igual que con GitHub, porque las dos fuentes leen el mismo JSON.

**HTTP sin TLS.** `SimpleWebSource` acepta `http://`. Velopack verifica el SHA1/SHA256 que figura en el feed contra el paquete descargado, pero sobre HTTP plano un intermediario puede cambiar los dos a la vez (ver §9). Como el feed de producción es el del VPS, que sirve por `https`, este diseño no atiende el caso de HTTP sin TLS (§3).

### 5.4 Chequeo horario

**Tabla de comportamiento (decidido por Martin):**

| Severidad | Arranque (sin cambios) | Chequeo horario (nuevo) |
|---|---|---|
| Normal | banner discreto | banner discreto |
| Importante | modal posponible | banner discreto (sin modal que interrumpa) |
| Crítica | bloqueo (o modo degradado si no puede descargar) | aviso rojo **fijo, no cerrable**: "Hay una actualización crítica. Guardá lo que estés haciendo y tocá Actualizar ahora", con botón **Actualizar ahora**. Si el usuario no actualiza, el bloqueo llega en el próximo arranque |

Hay tres reglas más **(decidido por Martin)**:
- **Una notificación por versión.** Si una versión ya se mostró, o el usuario la pospuso, el chequeo horario no la vuelve a mostrar en esa sesión.
- **Sin red, silencio.** El chequeo horario falla sin mostrar nada. El modo degradado es solo del arranque.
- **Nunca un bloqueo abrupto a mitad de sesión.**

**Piezas (decisión del asistente).**

1. **Entrada nueva en la política.** Se agrega `PoliticaUxActualizacion.DecidirEnSesion(UpdateCheckResult)` en `src/StockApp.Application/Actualizaciones/PoliticaUxActualizacion.cs`, que es puro y testeable. Mapea así:
   - Normal → `BannerDiscreto`.
   - Important → `BannerDiscreto`.
   - Critical → un `ModoUx` nuevo, `AvisoCriticoEnSesion`. No recibe `descargaPosible`, porque a mitad de sesión no se descarga por adelantado y no hay modo degradado.
   - Sin update → `Ninguno`.

   `Decidir` (la de arranque) no se toca.
2. **Overlay nuevo.** `AvisoCriticoEnSesion` necesita un VM y una View propios, o una variante del banner, a criterio del implementador (§6). Tiene que ser una franja roja que **no tapa el contenido**, que no tiene "Posponer" ni cerrar y que tiene un solo botón, **Actualizar ahora**, cableado a `CoordinadorActualizacion.AplicarActualizacionAsync`, igual que el resto de los overlays (`ShellViewModel.SuscribirEventosOverlay`). Lo resuelve `CoordinadorActualizacion.ResolverOverlayViewModel`, con un caso nuevo en el `switch`. Los tokens visuales son los de UI v2 (spec `2026-09-28-ui-v2-rediseno-total-design.md`, alertas de peligro).
3. **Evaluación en sesión en el coordinador.** `CoordinadorActualizacion.EvaluarEnSesionAsync()` devuelve la `AccionUx` (o `null` si no hay nada para mostrar):
   - Llama a `BuscarAsync`. Cualquier excepción devuelve `null` en silencio.
   - Si `Version` ya está en el conjunto de **versiones notificadas en la sesión**, devuelve `null`.
   - Si no, la agrega al conjunto y devuelve `DecidirEnSesion(resultado)`.
   - **Si aplicar falla (decisión del asistente).** Cuando `AplicarActualizacionAsync` devuelve `false` (descarga parcial, reinicio pendiente), esa versión **no** se considera ya notificada: se saca del conjunto, y el próximo chequeo horario puede volver a avisarla.
   - El chequeo de arranque **también** agrega su versión al conjunto, así que si al arrancar se mostró la 0.5.0, el chequeo horario no la vuelve a mostrar. El conjunto vive en memoria y se pierde al reiniciar. Eso es intencional: el arranque vuelve a evaluar todo, como hoy.
4. **Sin superposición.** `VelopackUpdateService` guarda el `UpdateInfo` en `_updatePendiente`, y cada `BuscarAsync` lo pisa. Por eso el coordinador serializa todo con un único `SemaphoreSlim(1,1)`: el chequeo de arranque, el horario y `AplicarActualizacionAsync`.
   - El chequeo horario usa `WaitAsync(0)`: si el semáforo está tomado, porque el arranque no terminó o hay una aplicación en curso, **se saltea ese tick** sin esperar.
   - Una vez que se tocó "Actualizar", el chequeo horario no vuelve a correr en esa sesión. Si `AplicarActualizacionAsync` devolvió `false`, el horario sigue normalmente.
5. **El temporizador.** Es una abstracción inyectable para tests, con el intervalo en una constante (`IntervaloChequeoHorario = TimeSpan.FromHours(1)`). Se recomienda `TimeProvider` (el repo ya lo inyecta en `InicioViewModel`) más `PeriodicTimer(intervalo, timeProvider)`. Si se elige así, falta sumar `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) en `Directory.Packages.props`, siguiendo la convención de Central Package Management. Una interfaz propia mínima también sirve (§6).
   - El primer tick cae **1 hora después** del chequeo de arranque, no en el momento.
   - El loop arranca en `ShellViewModel` **después** de que termina `EvaluarYAsignarOverlayAsync`, y corre en el thread pool.
   - La asignación del overlay pasa por `IUiDispatcher.Post`, igual que hoy.
   - El loop se cancela con un `CancellationToken` al cerrar la app.
   - Para tests, el loop expone su tarea `internal`, como `_tareaActualizacion`.
6. **Qué pasa con un overlay ya abierto.**
   - Si el overlay actual es `BloqueoCritico` o `ModoDegradado` (del arranque), el chequeo horario no hace nada: ya se está avisando lo máximo.
   - Si el overlay actual es un banner o un modal y el chequeo trae una **versión nueva** (no notificada), el reemplazo se decide así:
     - Si la acción nueva es `AvisoCriticoEnSesion`, siempre reemplaza.
     - Si la acción nueva es un banner y lo abierto es un modal, no lo reemplaza: el modal ya está pidiendo atención, y al aplicar se instala la versión más nueva, porque `_updatePendiente` ya apunta a ella.
     - Si la acción nueva es un banner y lo abierto es otro banner, lo reemplaza por el de la versión nueva.
   - Si no hay overlay, se asigna el nuevo.
7. **Instalaciones sin Velopack.** Si la app corre fuera de una instalación de Velopack (dev, tests), `VelopackUpdateService.BuscarAsync` ya devuelve `SinUpdate`, y el loop horario no hace nada visible.

**Textos que mencionan GitHub.** Ninguna vista ni VM de `Actualizaciones/` menciona GitHub. El único texto con "GitHub caído" está en `build/RELEASE_NOTES.md:39`, en el bloque de instrucciones que `FormateadorNotasActualizacion` limpia y que no llega a la UI. Igual se cambia a "sin red, servidor caído, etc.". El texto del modo degradado (`ActualizacionBloqueoView.axaml:39`, "MODO DEGRADADO — Actualizacion critica no descargada. Reintentara al reiniciar.") no menciona GitHub y queda igual.

### 5.5 Publicación

**`build/pack-win.ps1` sigue igual:** genera el paquete con `vpk pack` en el canal `win`, con deltas y `--releaseNotes build\RELEASE_NOTES.md`. Solo cambia su mensaje final (`build/pack-win.ps1:166`, "Siguiente paso: subir el contenido de $OutputDir a un GitHub Release."), que pasa a apuntar a `build/publicar-release.sh`.

**`releases/win` se conserva localmente entre versiones.** Está ignorado por git (`.gitignore:26`, `[Rr]eleases/`). vpk genera el delta contra la release anterior que encuentra en esa carpeta, así que si se borra, la próxima versión sale solo con el paquete full. No se rompe nada, pero los clientes bajan unos 53 MB en vez de un delta. Esto se documenta en `build/README-empaquetado.md`. Hay una advertencia adicional: `pack-win.ps1` corre en Windows, así que si el checkout de Windows no es el mismo que el de WSL, `publicar-release.sh` necesita recibir la carpeta de origen (ver más abajo).

**`build/publicar-release.sh` (nuevo; decisión del asistente).** Corre en la máquina del operador, como `deploy-vps.sh`.
- **Uso:** `build/publicar-release.sh <version> [--origen <dir>]`. Por defecto, `--origen` es `releases/win` del repo.
- **Configuración:** sigue el mismo criterio que `deploy-vps.sh`:
  - Hace `source` del `.env` **del operador** (`deploy/.env` local, `deploy-vps.sh:135-141`), no del `.env` del VPS. Hay dos `.env` distintos en el deploy: el del VPS configura el servicio y el local es para las herramientas del operador.
  - Toma `VPS_HOST`, `VPS_USER`, `VPS_SSH_PORT` y `VPS_DIR` con los mismos defaults (`deploy-vps.sh:34-41`), y `API_SCHEME`, `API_HOSTNAME` y `API_PORT` con la misma validación (`deploy-vps.sh:159-177`).
  - Usa `ssh`/`scp` con `-o BatchMode=yes`, con helpers idénticos a `ssh_vps`/`scp_vps`. Igual que `deploy-vps.sh`, que corre `sudo ./install.sh` por SSH, asume `sudo` sin contraseña para `VPS_USER`.
- **Bash:** `set -euo pipefail`. Ojo con el gotcha de `grep` en un pipe sin coincidencias, que con `pipefail` mata el script. Donde "no hay match" sea un resultado válido, hay que usar `|| true` o capturar el exit code explícitamente. Para leer el JSON se usa `jq`, que está en la máquina del operador. El script falla al arrancar si `jq` no está. El `releases.win.json` de vpk 1.2.0 no trae BOM, pero `RELEASES` sí.
- **Pasos, en este orden. El ORDEN IMPORTA (A2):**
  1. **Validación local.** `releases.win.json` existe y su asset `Type: Full` de mayor versión coincide con `<version>`. Cada `FileName` que lista existe en `--origen`, con el `Size` declarado.
  2. **Staging.** Se hace `scp` de los archivos necesarios a `${VPS_DIR}/updates-staging/` (por defecto `~/stockapp-deploy/updates-staging/`, del usuario SSH). Los necesarios son **todos los `.nupkg` que lista el JSON y que todavía no están en el servidor** con ese tamaño, y además el `releases.win.json`. No alcanza con los de la versión nueva: el feed lista toda la historia de deltas que vpk ve localmente, y Velopack puede encadenar deltas intermedios.
  3. **Paquetes primero.** Por SSH, con `sudo`: `install -d -o stockapp -g stockapp -m 750 /var/lib/stockapp/updates` (idempotente), y después `install -o stockapp -g stockapp -m 640` de cada `.nupkg` desde el staging.
  4. **Verificación remota previa al feed.** Cada `FileName` del JSON nuevo existe en `/var/lib/stockapp/updates` con el `Size` declarado. Si falta alguno, se aborta **sin** tocar el feed vigente.
  5. **Feed último, con rename atómico.** Se hace `install` del JSON como `/var/lib/stockapp/updates/releases.win.json.tmp` (extensión no servida, así que da 404) y después `mv` a `releases.win.json`. Como `mv` es dentro del mismo directorio, es un `rename(2)` atómico: un cliente ve el feed viejo o el nuevo, nunca uno a medio escribir.
  6. **Limpieza** de `updates-staging/`, acotada a esa carpeta y con un `trap` que la limpia también si el script falla.
  7. **Verificación final con curl.** `GET <api>/updates/releases.win.json` tiene que responder 200 y contener `<version>`. La comprobación remota es obligatoria: corre por `ssh_vps` contra `127.0.0.1`, con `--resolve ${API_HOSTNAME}:${API_PORT}:127.0.0.1` si `API_SCHEME=https`, igual que `deploy-vps.sh:170-177`. Funciona también en modo loopback más túnel. Si `API_HOSTNAME` está definido, además se pega desde la máquina del operador a `https://${API_HOSTNAME}:${API_PORT}/updates/releases.win.json`, que es lo que ve un cliente real. También se verifica que `GET` del `.nupkg` full nuevo responda 200 con `Content-Length` igual al `Size`.
- **Alcance:** solo `/var/lib/stockapp/updates` y `${VPS_DIR}/updates-staging`. Nada de `rm -rf` con variables sin validar, ni comandos globales, ni `systemctl` sobre otro servicio. La API **no** necesita reiniciarse para publicar.
- **Relevamiento previo (decisión del asistente).** Antes de subir nada, el script releva el servidor por SSH (nombres y tamaños de lo que hay en `/var/lib/stockapp/updates`) y sube solo lo que falta. Los detalles quedan a criterio del implementador.
- **Retirar una versión mala (decisión del asistente).** Velopack no soporta downgrade. El procedimiento estándar es publicar la versión buena anterior con un número de versión **mayor** que la mala. No hay un comando especial de retiro.
- **Lo que el script no hace:** no borra paquetes viejos del servidor (§6) y no sube el Setup.exe salvo que se decida servirlo (§6).

**Nota sobre "sin acceso al servidor".** La restricción original se refería al servidor **del municipio**. Ahora el VPS de producción es de Martin, así que publicar ahí está bien. Lo que se mantiene es que una instalación no requiere editar configuración del servidor después de instalar, más allá de lo que hacen `install.sh` y `deploy-vps.sh`.

### 5.6 Qué se borra

| Qué | Dónde |
|---|---|
| `GithubSource` (su uso) | `VelopackGatewayReal.cs:56-59` |
| Rama de fallback y orden | `VelopackGatewayReal.cs:61-71` |
| `GitHubRepoUrlDefault`, `GitHubRepoUrl`, `GitHubAccessToken`, `GitHubPrerelease`, `FeedPropiUrl` (con el typo "Propi"), `Orden` | `UpdaterOptions.cs:14-43` |
| `enum OrdenFuentes` | `UpdaterOptions.cs:46-54` |
| `FallbackUpdateSource` completo | `src/StockApp.Presentation/Actualizaciones/FallbackUpdateSource.cs` |
| Sus tests | `tests/StockApp.Presentation.Tests/Actualizaciones/FallbackUpdateSourceTests.cs` |
| Lectura de `Updater:GitHub*` y `FeedPropiUrl = null` | `App.axaml.cs:423-444` |
| Sección `"Updater"` entera | `src/StockApp.Presentation/appsettings.json` (queda solo `"Api"`) |

**Por qué se borra `FallbackUpdateSource` (decisión del asistente).** Con una sola fuente no aporta nada, y tiene un riesgo propio: como `GetReleaseFeed` y `DownloadReleaseEntry` prueban las fuentes cada uno por su cuenta, podía anunciar la versión desde una fuente y bajar el `.nupkg` de **otra**. No hay ningún otro uso: `UpdaterOptions`, `OrdenFuentes` y `FallbackUpdateSource` solo aparecen en esos cinco archivos.

**Documentación que se actualiza:**
- `build/README-empaquetado.md`: cambia la sección "Publicar en GitHub Releases" (línea 186 en adelante) y las líneas 121, 198-199, 274 y 298, que hablan de `FeedPropioUrl` y del fallback a GitHub. Hay que documentar `publicar-release.sh`, conservar `releases/win` y el orden de la publicación. La línea 170 dice que `RELEASES` es el índice que usa el cliente, y se corrige según lo que confirme el implementador (§5.2).
- `build/VALIDACION-MANUAL-faseA.md`: cambian V5 (líneas 51 y 138-146), la línea 241 (el `FeedPropioUrl = null`, que ya no existe) y el flujo de las líneas 280 en adelante, que dejan de publicar en GitHub. Se agrega el caso del chequeo horario (§7).
- `build/RELEASE_NOTES.md`: cambian la línea 39 ("GitHub caido") y la 56 ("Subir … a un GitHub Release").
- `deploy/.env.example`: se documenta `Actualizaciones__Directorio` (opcional).

## 6. Qué queda a criterio del implementador

- La forma de la abstracción de tiempo: `TimeProvider` + `FakeTimeProvider`, o una interfaz propia mínima. Lo único fijo es que el intervalo es una constante, que los tests avanzan el tiempo sin dormir y que el primer tick cae 1 hora después del arranque.
- Si `AvisoCriticoEnSesion` usa un VM y una View nuevos, o una variante del banner con un flag. Lo fijo es lo de §5.4: franja roja, no cerrable, un solo botón y sin tapar el contenido.
- El nombre exacto de `UrlFeedActualizaciones.Desde` y de `EvaluarEnSesionAsync`, y dónde vive el conjunto de versiones notificadas (en el coordinador es lo natural).
- Si `/updates` sirve también `GestionMunicipal-win-Setup.exe`, lo que serviría para instalar en máquinas nuevas desde el mismo servidor. Si se sirve, va con el mapeo de `.exe` y queda documentado que es público.
- Retención en el VPS: por ahora no se borra nada automáticamente. Cada full pesa unos 53 MB y el disco es compartido. Si se agrega un comando de poda, tiene que ser explícito y manual, conservar todo lo que liste el `releases.win.json` vigente y estar acotado a `/var/lib/stockapp/updates`.
- Si `deploy-vps.sh` informa el estado del feed en el relevamiento del paso 0, por ejemplo la versión publicada.
- El texto exacto del aviso crítico en sesión, siempre que mantenga el mensaje aprobado por Martin: "Hay una actualización crítica. Guardá lo que estés haciendo y tocá Actualizar ahora".

## 7. Verificación

**TDD, y cada test se verifica por mutación:** hay que reintroducir el bug, quitando código y no agregándolo, y pegar el rojo.

**Política y coordinador** (`StockApp.Application.Tests` / `StockApp.Presentation.Tests`):
- `DecidirEnSesion`: Normal → banner, Important → **banner** (mutación: si devuelve modal, queda en rojo), Critical → `AvisoCriticoEnSesion` (mutación: si devuelve `BloqueoCritico`, queda en rojo), sin update → Ninguno.
- `Decidir` (arranque): los tests existentes siguen verdes sin cambios.
- Severidad con saltos de varias versiones: el máximo de las intermedias gana. Mutación: usar solo la de `TargetFullRelease` pone el test en rojo (una intermedia crítica con destino normal).
- `EvaluarEnSesionAsync`:
  - Si `AplicarActualizacionAsync` devolvió `false`, la versión no queda como notificada y el próximo tick puede avisarla.
  - Si `BuscarAsync` tira una excepción, devuelve `null` sin propagarla.
  - La misma versión dos veces se notifica una sola vez. Mutación: sacar el registro de la versión pone el test en rojo.
  - Una versión ya mostrada en el arranque no se repite.
  - Con el semáforo tomado, el tick se saltea.
  - Con una aplicación en curso, no se busca.
- `ResolverOverlayViewModel` con `AvisoCriticoEnSesion` devuelve el VM nuevo, sin "Posponer".
- Las reglas de reemplazo de overlay de §5.4.6, con `IUiDispatcher` falso.
- El loop horario con tiempo falso:
  - Avanzar 59 minutos no busca.
  - Avanzar 60 minutos busca una vez.
  - Cancelar detiene el loop.
- `UrlFeedActualizaciones.Desde`: con y sin barra final, `http`/`https`, con puerto y con path base.

**API** (`tests/StockApp.Api.Tests`, `WebApplicationFactory` con `Actualizaciones:Directorio` apuntando a una carpeta temporal):
- `GET /updates/releases.win.json` sin token → 200, `application/json`.
- `GET /updates/<x>.nupkg` sin token → 200, `application/octet-stream`. Mutación: sacar el mapeo de `.nupkg` lo pone en rojo (404).
- Con la licencia **no** activada: `GET /updates/releases.win.json` → 200, no 423. Mutación: sacar `/updates` de `EsRutaPermitida` lo pone en rojo. Para el estado de licencia se reusa el patrón de `tests/StockApp.Api.Tests/Licenciamiento/BloqueoLicenciaTests.cs`.
- Con la licencia no activada, `/updatesX` y cualquier otra ruta siguen dando 423, lo que prueba que la exención está acotada.
- `GET /updates/` → 404, sin listado.
- `GET /updates/releases.win.json.tmp` → 404.
- Path traversal: `GET /updates/..%2Fappsettings.json` y sus variantes no sirven nada fuera de la carpeta.
- Con la carpeta inexistente y sin poder crearla, la API arranca igual y `/updates/*` da 404 (A6).
- Cabecera `Cache-Control: no-cache` en `releases.win.json`.

**Script** (`publicar-release.sh`):
- Se prueba con el patrón de stub por `PATH` que ya usan los tests de los scripts de deploy (`tests/StockApp.Infrastructure.Tests/Kit/`, por ejemplo `DeployVpsTests.cs`, que corren el bash real con `EjecutorBash`): `ssh`, `scp` y `curl` falsos al frente del `PATH`, que registran el orden de las invocaciones.
- Hay que afirmar que **ningún** `mv … releases.win.json` ocurre antes de que todos los `.nupkg` estén instalados y verificados. Mutación: invertir el orden pone el test en rojo.
- Si falta un paquete local, aborta sin tocar el servidor.
- Si la verificación remota falla, aborta sin hacer rename.
- El relevamiento previo no vuelve a subir lo que ya está en el servidor con el mismo tamaño.

**Build.** Los cambios en `App.axaml.cs` y los `.axaml` nuevos obligan a compilar por regla del proyecto: hay que compilar **cada proyecto por separado o la `.sln`**, nunca varios `.csproj` en un mismo `dotnet build`.

**Contra el VPS de testing** (`stockapp.capuanomartin.dev:8080`, TLS con Let's Encrypt): deploy de la API con el cambio, `publicar-release.sh` de una versión real y después:
- `curl -sS https://stockapp.capuanomartin.dev:8080/updates/releases.win.json` → 200 con la versión.
- `curl -sI …/<full>.nupkg` → 200 con el tamaño correcto.
- `curl -sS …/updates/` → 404.

Con eso queda probada la parte de servidor sin Windows.

**End to end en Windows real (pendiente; requiere Windows).** `VelopackGatewayReal` no se puede probar con tests unitarios. Hay que:
1. Instalar la versión N con `Setup.exe`, apuntando la conexión al VPS de testing.
2. Publicar N+1 con `severity: normal` y ver el banner al arrancar.
3. Repetir con `important` (modal) y `critical` (bloqueo), y con la red cortada en el arranque (modo degradado).
4. Con la app **abierta**, publicar N+2 y esperar el chequeo horario, o bajar la constante en un build de prueba. Ver el banner con `important` y el aviso rojo fijo con `critical`. Confirmar que no se repite en el siguiente tick, y que en el próximo arranque llega el bloqueo.
5. Verificar que una instalación con la licencia sin activar también se actualiza.

Esto se agrega como caso nuevo en `build/VALIDACION-MANUAL-faseA.md`.

## 8. Restricciones

- **El VPS es compartido con pinar.** Todo comando se acota por nombre a StockApp. No se toca nginx, no se abre ningún puerto nuevo y no se reinicia nada ajeno.
- **No se puede pedir configuración por instalación** más allá de lo que hacen `install.sh` y `deploy-vps.sh`. El cliente no recibe configuración nueva: deriva el feed de la URL que ya tiene.
- **TDD con verificación por mutación** (§7).
- **No se compila por rutina**, salvo lo que exige §7 por tocar `App.axaml.cs` y XAML.
- **El ciclo real de Velopack** solo se valida en Windows real. Mientras tanto, el servidor se valida con `curl` contra el VPS de testing y con tests de integración.

## 9. Riesgos

- **R1. Que Velopack 1.2.0 pida algo más que `releases.win.json` y los `.nupkg`.** Mitigación: confirmarlo en el fuente antes de implementar (§5.2). En el e2e de Windows, mirar el log de acceso de la API o el `velopack.log` del cliente.
- **R2. El feed es público.** Cualquiera que conozca la URL descarga el binario de la app. Hoy ya es así, porque el repo y las releases son públicos. El uso lo protege la licencia, no el secreto del binario. Las descargas anónimas de unos 53 MB consumen ancho de banda de un VPS compartido. Por ahora se acepta, y si molesta, se le pone rate limiting a `/updates`.
- **R3. HTTP sin TLS.** Si una instalación apuntara a la API por `http://`, el feed y el paquete viajarían sin protección: un intermediario puede servir un paquete y un hash falsos. En el VPS de producción con `API_SCHEME=https` el riesgo no existe, y las instalaciones en LAN con API propia están fuera de alcance (§3). Se documenta, no se resuelve acá.
- **R4. Que se borre `releases/win` localmente.** No se rompe nada, pero se pierden los deltas y la próxima versión va full. Se documenta (§5.5).
- **R5. Que vpk se corra contra una carpeta `releases/win` distinta de la que generó lo publicado.** El JSON nuevo podría no listar deltas que los clientes esperan, o listar archivos que el servidor no tiene. Para lo segundo, el paso 4 de `publicar-release.sh` aborta antes del rename (A2). Lo primero solo hace que los clientes bajen el paquete full.
- **R6. Que el chequeo horario pise `_updatePendiente` mientras el usuario mira un banner viejo.** Al aplicar se instala la versión más nueva, lo que es aceptable. La serialización de §5.4.4 evita que pase **durante** una descarga o una aplicación.
- **R7. Un aviso crítico en sesión que el usuario ignora todo el día.** Es intencional: el bloqueo llega en el próximo arranque. Martin lo aceptó a cambio de no perder trabajo (A1).
- **R8. La versión de `RELEASE_NOTES.md` (0.2.0) no coincide con la del csproj (0.4.0).** Si se empaqueta sin corregirla, las notas anunciarían la versión equivocada. Queda anotado para la próxima release, y este diseño no decide su contenido.
- **R9. Una instalación vieja que todavía apunta a GitHub. CERRADO (decidido por Martin, 2026-10-19).** No hay instalaciones reales hechas con el `Setup.exe` de Velopack, solo los zips de testers, que no tienen actualizador. Por eso no hace falta publicar una release de transición en GitHub, y la primera versión con el feed propio se publica solo en el VPS.

---

**Nota 2026-10-20.** Martin cambió el intervalo del chequeo en sesión de 1 hora a **15 minutos** (`CoordinadorActualizacion.IntervaloChequeo`). Las menciones a "chequeo horario" de este documento se conservan como historia de la decisión original; el comportamiento de la tabla de §5.4 no cambia, solo la frecuencia.
