# Feed de actualizaciones propio y chequeo horario Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que el desktop se actualice solo desde `<url base de la API>/updates/` (servido por la propia API en el VPS, sin GitHub), con un chequeo cada 1 hora que avisa sin bloquear nunca a mitad de sesión, y un script que publica una versión en el VPS sin que ningún cliente vea un feed que apunta a un paquete que falta.

**Architecture:** Nueve tasks en serie sobre `feat/feed-actualizaciones-propio`. (1) La API monta `/updates` con archivos estáticos y lo exime del bloqueo por licencia. (2) `install.sh` crea la carpeta del feed. (3) El cliente deriva la URL del feed de la URL de la API y se borra todo lo de GitHub y el fallback. (4) La severidad pasa a ser el máximo de las releases entre la instalada y el destino, leída del feed que capturó la fuente. (5) Modo de UI nuevo `AvisoCriticoEnSesion` con su política y su overlay. (6) El coordinador gana la evaluación en sesión (una notificación por versión, semáforo, aplicación fallida). (7) El bucle horario en `ShellViewModel`, con espera inyectable y reglas de reemplazo de overlay. (8) `build/publicar-release.sh` y la documentación de publicación. (9) Verificación contra el VPS de testing, documentación de la validación manual y merge.

**Tech Stack:** .NET 10, ASP.NET Core (archivos estáticos, `WebApplicationFactory` + Testcontainers en `StockApp.Api.Tests`), Velopack 1.2.0 (`SimpleWebSource`, `IUpdateSource`), Avalonia 12.0.5 + CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Application.Tests`, `StockApp.Presentation.Tests`, `StockApp.Api.Tests`, `StockApp.Infrastructure.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`), bash + `jq` (operador), `EjecutorBash` (`tests/_Compartido/EjecutorBash.cs`).

**Spec:** `docs/superpowers/specs/2026-10-19-feed-actualizaciones-propio-design.md` (aprobada por Martin el 2026-10-19, con todas las decisiones del asistente aceptadas).

## Global Constraints

- **Licenciamiento fuera de alcance.** La app va a pasar a SaaS multi-tenant y el modelo de licencias se rediseña aparte. Lo único que este plan toca es agregar `/updates` a `BloqueoLicenciaMiddleware.EsRutaPermitida` (Task 1). Nada más de `Licenciamiento/`.
- **Worktree:** `git worktree add -b feat/feed-actualizaciones-propio /home/capua25/workspace/stockapp-worktrees/feed-actualizaciones-propio main` (nunca bajo `/tmp`). Todos los comandos corren como `cd /home/capua25/workspace/stockapp-worktrees/feed-actualizaciones-propio && <comando>`.
- **CodeGraph:** el worktree tiene su PROPIO índice: `gentle-ai codegraph init --cwd /home/capua25/workspace/stockapp-worktrees/feed-actualizaciones-propio` una vez (nunca copiar ni enlazar el `.codegraph/` de otro checkout). El MCP `codegraph_explore` responde con el índice del checkout principal; en el worktree se usa la CLI (`codegraph query|node|callers|impact`). Antes de editar, contrastar líneas y miembros con `sed -n` (el índice ya devolvió versiones viejas en worktrees). `grep`/Read como fallback, y Read directo para `.axaml`, `.md`, `.html`, `.sh`, `.ps1`, `.json`, `.service`, `.csproj`.
- **TDD con verificación por MUTACIÓN.** Test primero, rojo REAL pegado en el ledger, código mínimo, verde. Cada guardián se verifica quitando la regla y pegando el rojo, nunca razonando. Mutaciones QUITANDO, nunca agregando: `export SCRATCH=~/.cache/stockapp-feed-actualizaciones && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la línea o fragmento; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas; se corre el test y se PEGA el rojo; `cp "$SCRATCH/<nombre>.bak" <archivo>` y `git diff --quiet -- <archivo>` confirma la restauración.
- **Versiones de paquetes** solo en `Directory.Packages.props` (Central Package Management), nunca `Version=` en un `.csproj`. Este plan no agrega paquetes.
- **Build:** `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un mismo `dotnet build` dan falso verde). No se compila por rutina, SALVO en las tasks que tocan `App.axaml.cs` o un `.axaml` (3, 5 y 7), donde es obligatorio.
- **Tests:** de a UN `.csproj` por comando, con `--blame-hang-timeout 5m`; nunca dos `dotnet test` a la vez. Rojos FALSOS conocidos en el worktree: los de fixtures `.ods` en `StockApp.Infrastructure.Tests` y `StockApp.Api.Tests` (confirmar por nombre); `ApiStartupMigrationTests.Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests` es intermitente conocido. Cualquier otro rojo es real.
- **XAML:** las vistas con `x:DataType` usan compiled bindings (un typo es error de build). Ningún comentario de `.axaml` ni `.csproj` puede contener `--` (AVLN1001 / MSB4025).
- **Bash:** `set -euo pipefail`. Un `grep` en un pipe sin coincidencias mata el script: donde "no hay match" es válido, `|| true` o capturar el exit code explícito. Los scripts se testean con stubs por `PATH` (`ssh`/`scp`/`curl` falsos al frente), probando el camino feliz Y los de falla, capturando exit code y stderr, y distinguiendo "no hay nada" de "no pude consultar".
- **VPS compartido con pinar:** todo comando remoto acotado por nombre a StockApp (`stockapp-api`, `/var/lib/stockapp/updates`, `${VPS_DIR}/updates-staging`). No se toca nginx, no se abre ningún puerto, ningún comando global, nada de `rm -rf` sobre variables sin validar.
- **Commits:** conventional commits en español, uno por task, SIN `Co-Authored-By` ni línea de atribución de IA; `git add` con rutas exactas (nunca `-A` ni `.`), verificado con `git diff --cached --name-only`. Sin `git reflog expire`, `git gc` ni `git prune`. El merge a `main` es `--ff-only` + push, solo en la Task 9.
- **Ledger** git-ignored: `.superpowers/sdd/2026-10-19-feed-actualizaciones-propio/progress.md` (línea base, rojos, mutaciones, totales por task).
- Las referencias `archivo:línea` son las del commit de este plan; tras editar, ubicar por el texto citado.
- Sin referencias al plan en el código (nada de "Task 4" en `.cs`/`.axaml`/`.sh`).
- **Verificación orgánica final la hace un AGENTE** (Task 9), no un checklist para Martin: curl contra `https://stockapp.capuanomartin.dev:8080` después de desplegar la API. El ciclo real de Velopack requiere Windows real y queda como pendiente manual documentado.

## Review Focus

1. **Un banner nuevo llega mientras está abierto el aviso crítico en sesión.** La spec (§5.4.6) no lo cubre. Esperado: el aviso crítico NO se baja a un banner; solo otro aviso crítico lo reemplaza. Test: `DebeReemplazarOverlay` con actual = aviso crítico y nuevo = `BannerDiscreto` → `false` (Task 7).
2. **La publicación se corta a mitad (falla el `scp` del segundo `.nupkg`).** Esperado: el feed vigente queda intacto, no hay `mv` y el staging remoto se limpia por el `trap`. Test: `ScpFalla_NoTocaElFeedYLimpiaElStaging` (Task 8).
3. **Se corre `publicar-release.sh` dos veces con la misma versión.** Esperado: la segunda corrida no vuelve a subir ningún `.nupkg`, reescribe el feed de forma atómica y termina en 0. Test: `RepublicarLaMismaVersion_NoSubeNingunPaquete` (Task 8).
4. **App sin versión instalada conocida o feed sin capturar** (`VersionInstalada` null, `UltimoFeed` vacío o null por un mock suelto). Esperado: la severidad es la del destino, sin excepción. Test: `BuscarAsync_SinVersionInstalada_UsaSoloLaSeveridadDelDestino` (Task 4).
5. **Un tick horario falla (sin red) y el siguiente tiene que volver a buscar.** Esperado: el bucle no muere por una falla de un tick. Test: `TickConFalla_ElSiguienteTickVuelveABuscar` (Task 7).

---

### Task 1: La API sirve el feed en `/updates`, anónimo y exento del bloqueo por licencia

**Files:**
- Create: `src/StockApp.Api/Actualizaciones/FeedActualizaciones.cs`
- Modify: `src/StockApp.Api/Program.cs` (entre el `if (licenciamientoHabilitado) { app.UseMiddleware<BloqueoLicenciaMiddleware>(); }` de L644-L647 y `app.UseAuthentication();` de L649)
- Modify: `src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs` (`EsRutaPermitida` L74-L81 y el `<summary>` L5-L42)
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs` (carpeta temporal del feed, como `_directorioLogsTemporal` L40-L41, L102 y su limpieza en `DisposeAsync`)
- Test: `tests/StockApp.Api.Tests/Actualizaciones/FeedActualizacionesTests.cs`

**Interfaces:**
- Produces: `public static class FeedActualizaciones` con `public const string Ruta = "/updates"`, `public const string ClaveDirectorio = "Actualizaciones:Directorio"`, `public const string DirectorioPorDefecto = "/var/lib/stockapp/updates"` y `public static void Montar(WebApplication app)`. La clave en `api.env` es `Actualizaciones__Directorio` (Task 2 la documenta). `ApiFactory` expone `public string DirectorioFeed { get; }`.
- El cliente (Task 3) pide `releases.win.json` y cada `FileName` relativo a `<base>/updates/`. Confirmado contra el fuente de Velopack 1.2.0 (`ilspycmd` de `SimpleWebSource`): `GetReleaseFeed` pide SOLO `releases.{channel}.json` (con query `arch`, `os`, `rid`, `id`, `localVersion`) y `DownloadReleaseEntry` pide `FileName` relativo a la base con barra final. No pide `RELEASES`.

- [ ] **Step 1: Línea base de la suite**

Run, de a uno: `dotnet test tests/<P>/<P>.csproj --blame-hang-timeout 5m` para `StockApp.Application.Tests`, `StockApp.Presentation.Tests`, `StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`, `StockApp.Api.Tests` y `StockApp.Infrastructure.Tests`.
Expected: verde salvo los falsos conocidos. Pegar los totales en el ledger ("Línea base").

- [ ] **Step 2: Carpeta del feed en `ApiFactory` y tests en rojo**

`ApiFactory`: campo `_directorioFeedTemporal = Path.Combine(Path.GetTempPath(), "StockAppApiTestsFeed_" + Guid.NewGuid())`, creado en el constructor, `["Actualizaciones:Directorio"] = _directorioFeedTemporal` en el `AddInMemoryCollection`, `DirectorioFeed` público y borrado defensivo en `DisposeAsync` (mismo patrón que los logs).

`FeedActualizacionesTests : ApiTestBase` (xUnit v2, `[Fact]`/`[Theory]`). Cada test escribe sus archivos en `Factory.DirectorioFeed` (`releases.win.json` con `{"Assets":[]}`, `GestionMunicipal-0.5.0-full.nupkg` con 1234 bytes, `releases.win.json.tmp`) y usa `Factory.CreateClient()` SIN token:
- `FeedJson_SinToken_Devuelve200ConJsonYNoCache`: 200, `Content-Type` empieza con `application/json`, `Cache-Control` contiene `no-cache`.
- `Nupkg_SinToken_Devuelve200ConOctetStream`: 200, `application/octet-stream`, `Content-Length` 1234.
- `LicenciaBloqueada_FeedJson_Devuelve200`: `Factory.Services.GetRequiredService<EstadoLicencia>().Activada = false` (como `BloqueoLicenciaTests.Bloquear`), `GET /updates/releases.win.json` → 200.
- `LicenciaBloqueada_RutasFueraDelFeed_Siguen423` (`[Theory]` con `/updatesX/releases.win.json` y `/productos`) → 423.
- `RaizDelFeed_Devuelve404SinListado`: `GET /updates/` → 404.
- `ArchivoTmp_Devuelve404`: `GET /updates/releases.win.json.tmp` → 404.
- `PathTraversal_NoSirveNadaFueraDeLaCarpeta` (`[Theory]` con `/updates/..%2Ffuera.json`, `/updates/%2e%2e%2Ffuera.json`, `/updates/..%5Cfuera.json`): con un `fuera.json` cuyo contenido es `"SECRETO-FUERA"` escrito en el PADRE de `DirectorioFeed`, la respuesta no es 200 y su cuerpo no contiene `SECRETO-FUERA`.
- `CarpetaImposible_LaApiArrancaYElFeedDa404`: crea un ARCHIVO regular temporal y arma `Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Actualizaciones:Directorio"] = Path.Combine(archivo, "updates") })))` (patrón de `RateLimitingTests`); `GET /` → 200 y `GET /updates/releases.win.json` → 404.

- [ ] **Step 3: Correr y ver el rojo**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~FeedActualizacionesTests`
Expected: FAIL en los de 200 (404 o 423); pegar el rojo.

- [ ] **Step 4: Implementar `FeedActualizaciones.Montar` y la exención**

`Montar`: lee `app.Configuration[ClaveDirectorio]` (vacío → `DirectorioPorDefecto`); `Directory.CreateDirectory` dentro de `try`; ante cualquier excepción, `app.Logger.LogWarning` con la ruta y el motivo, y `return` sin montar (A6). Si no, `app.UseStaticFiles(new StaticFileOptions { ... })` con `FileProvider = new PhysicalFileProvider(dir)`, `RequestPath = Ruta`, `ServeUnknownFileTypes = false`, `ContentTypeProvider = new FileExtensionContentTypeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [".json"] = "application/json", [".nupkg"] = "application/octet-stream" })` (explícito: sin `.exe`, el `Setup.exe` NO se sirve) y `OnPrepareResponse`: `.json` → `Cache-Control: no-cache`; `.nupkg` → `Cache-Control: public, max-age=31536000, immutable`. Sin `UseDirectoryBrowser`.

`Program.cs`: `FeedActualizaciones.Montar(app);` entre el bloque del middleware de licencia y `UseAuthentication`, con un comentario que diga por qué va DESPUÉS del bloqueo (la exención vive en la lista explícita y testeable, no en el orden) y ANTES de la autenticación (el feed es anónimo: `SimpleWebSource` usa su propio `HttpClient`, sin `AuthTokenHandler`).

`BloqueoLicenciaMiddleware.EsRutaPermitida`: `|| path.StartsWithSegments("/updates")`. En el `<summary>`, sumar `/updates` a la lista y un párrafo "POR QUÉ /updates": con la licencia bloqueada, el arreglo de un problema de licencia tiene que poder llegar por actualización (A4); `StartsWithSegments` no matchea `/updatesX`.

- [ ] **Step 5: Verde**

Run: el mismo comando del Step 3. Expected: PASS.

- [ ] **Step 6: Mutaciones (pegar cada rojo)**

1. Quitar `|| path.StartsWithSegments("/updates")` → rojo en `LicenciaBloqueada_FeedJson_Devuelve200` (423).
2. Quitar la entrada `[".nupkg"] = ...` del provider → rojo en `Nupkg_SinToken_Devuelve200ConOctetStream` (404).
3. Quitar la rama `.json` de `OnPrepareResponse` → rojo en `FeedJson_SinToken_Devuelve200ConJsonYNoCache`.
4. Quitar el `try`/`catch` (dejar el `CreateDirectory` suelto) → rojo en `CarpetaImposible_LaApiArrancaYElFeedDa404` (el host no arranca).

- [ ] **Step 7: Suite de la API completa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --blame-hang-timeout 5m`. Expected: verde salvo falsos conocidos; total = línea base + los nuevos.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Api/Actualizaciones/FeedActualizaciones.cs src/StockApp.Api/Program.cs src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs tests/StockApp.Api.Tests/Actualizaciones/FeedActualizacionesTests.cs
git commit -m "feat(api): sirve el feed de actualizaciones en /updates, anónimo y exento del bloqueo por licencia"
```

---

### Task 2: `install.sh` crea la carpeta del feed

**Files:**
- Modify: `deploy/install.sh` (después de `chmod 750 "$STOCKAPP_HOME"`, L318)
- Modify: `deploy/.env.example` (sección API)
- Test: `tests/StockApp.Infrastructure.Tests/Kit/InstallEsquemaTests.cs` (`EjecutarInstall`, L159-L230, y dos tests nuevos)

**Interfaces:**
- Consumes: `Actualizaciones__Directorio` (Task 1). `install.sh:100-103` ya pasa tal cual toda variable no conocida de `.env` a `api.env`; no se agrega a `VARS_CONOCIDAS`.
- Produces: `/var/lib/stockapp/updates`, `stockapp:stockapp`, modo 750. `deploy-vps.sh` copia `install.sh` solo si cambió (paso 4), así que el próximo deploy lo lleva solo; `deploy-vps.sh` no se toca.

- [ ] **Step 1: Extender el harness y escribir los tests**

En el entrypoint de `EjecutarInstall` agregar al final `echo "---UPDATES---"; stat -c '%U:%G %a' /var/lib/stockapp/updates 2>/dev/null || echo AUSENTE; ls -1 /var/lib/stockapp/updates 2>/dev/null` y un parámetro opcional `bool conFeedPrevio = false` que, ANTES de correr `install.sh`, hace `mkdir -p /var/lib/stockapp/updates && echo previo > /var/lib/stockapp/updates/previo.nupkg`. La tupla de retorno suma `string Updates` (actualizar las deconstrucciones existentes, todas posicionales).

- `CaminoFeliz_CreaLaCarpetaDelFeedConDuenoYPermisosDelServicio`: env de `CaminoFeliz_ApiSchemeHttp_...`; exit 0 y `Updates` empieza con `stockapp:stockapp 750`.
- `CarpetaDelFeedExistente_NoSeTocaSuContenido`: con `conFeedPrevio: true`; exit 0, `Updates` empieza con `stockapp:stockapp 750` y contiene `previo.nupkg`.

- [ ] **Step 2: Rojo**

Run: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~InstallEsquemaTests`
Expected: FAIL en el primero (`AUSENTE`); pegar.

- [ ] **Step 3: Implementar**

`install.sh`: `install -d -o stockapp -g stockapp -m 750 "${STOCKAPP_HOME}/updates"` con un comentario de una línea (carpeta del feed que sirve la API en `/updates`; idempotente, no toca el contenido). `.env.example`: bloque comentado que documenta `# Actualizaciones__Directorio=/var/lib/stockapp/updates` como override opcional, sin la palabra `CAMBIAR`.

- [ ] **Step 4: Verde + mutación**

Run: el comando del Step 2. Expected: PASS. Mutación: quitar la línea `install -d ... updates` → rojo en el primero; pegar.

- [ ] **Step 5: Commit**

```bash
git add deploy/install.sh deploy/.env.example tests/StockApp.Infrastructure.Tests/Kit/InstallEsquemaTests.cs
git commit -m "feat(deploy): install.sh crea la carpeta del feed de actualizaciones"
```

---

### Task 3: El cliente usa una sola fuente, derivada de la URL de la API

**Files:**
- Create: `src/StockApp.Presentation/Actualizaciones/UrlFeedActualizaciones.cs`
- Modify: `src/StockApp.Presentation/Actualizaciones/UpdaterOptions.cs` (queda solo `FeedUrl`; se borran L9-L43 y el enum L46-L54)
- Modify: `src/StockApp.Presentation/Actualizaciones/VelopackGatewayReal.cs` (`BuildSource` L54-L72 y el `<summary>` L10-L17)
- Modify: `src/StockApp.Presentation/App.axaml.cs` (L421-L444)
- Modify: `src/StockApp.Presentation/appsettings.json` (se borra `"Updater"`; queda solo `"Api"`)
- Modify: `build/RELEASE_NOTES.md:39` ("GitHub caido" → "servidor caido")
- Delete: `src/StockApp.Presentation/Actualizaciones/FallbackUpdateSource.cs`, `tests/StockApp.Presentation.Tests/Actualizaciones/FallbackUpdateSourceTests.cs`
- Test: `tests/StockApp.Presentation.Tests/Actualizaciones/UrlFeedActualizacionesTests.cs`, `tests/StockApp.Presentation.Tests/Arranque/ConfigurarServiciosTests.cs`, `tests/StockApp.Infrastructure.Tests/Kit/PackTestersWinTests.cs`

**Interfaces:**
- Consumes: `App.ResolverApiBaseUrl(IConfiguration)` y `App.ConstruirConfiguracion()` (`App.axaml.cs:470-512`, `internal`).
- Produces: `public static class UrlFeedActualizaciones { public static string Desde(string apiBaseUrl); }` y `public sealed class UpdaterOptions { public required string FeedUrl { get; init; } }`. `VelopackGatewayReal.BuildSource` devuelve `new SimpleWebSource(options.FeedUrl)` (la Task 4 lo envuelve).

- [ ] **Step 1: Tests en rojo**

`UrlFeedActualizacionesTests` (`[Theory]`):
- `https://h:8080` → `https://h:8080/updates/`
- `https://h:8080/` → `https://h:8080/updates/`
- `http://localhost:5043` → `http://localhost:5043/updates/`
- `https://h/api` → `https://h/api/updates/` y `https://h/api/` → `https://h/api/updates/` (respeta el path base)

`ConfigurarServiciosTests.ConfigurarServicios_FeedUrl_DerivaDeLaUrlDeLaApi`: `using var sp = App.ConfigurarServicios();` y `Assert.Equal(UrlFeedActualizaciones.Desde(App.ResolverApiBaseUrl(App.ConstruirConfiguracion())), sp.GetRequiredService<UpdaterOptions>().FeedUrl)`.

`PackTestersWinTests.AppsettingsDelRepo_NoTieneSeccionUpdater`: con `RutaAppsettingsRepo()`, `Assert.False(doc.RootElement.TryGetProperty("Updater", out _))`.

- [ ] **Step 2: Rojo**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~UrlFeedActualizacionesTests|FullyQualifiedName~ConfigurarServiciosTests"` (error de compilación por los símbolos que faltan es el rojo esperado) y `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~PackTestersWinTests`. Pegar.

- [ ] **Step 3: Implementar**

`Desde`: `new Uri(new Uri(apiBaseUrl.TrimEnd('/') + "/"), "updates/").ToString()`. `App.axaml.cs`: el bloque L421-L444 queda en un comentario corto (el feed es `<url de la API>/updates/`, sin configuración nueva; un cambio del Configurador aplica en el próximo arranque, igual que la API) y `services.AddSingleton(new UpdaterOptions { FeedUrl = UrlFeedActualizaciones.Desde(ResolverApiBaseUrl(configuration)) });` (misma función y misma `configuration` que el `HttpClient` principal y el keyed `"Descargas"`, que ya la llaman cada uno). El resto del bloque (L446-L457) no cambia. Borrar los dos archivos de fallback, la sección `"Updater"` y los usos de `GithubSource`/`OrdenFuentes`.

- [ ] **Step 4: Build + verde**

Run: `dotnet build StockApp.sln` (0 errores) y los dos comandos del Step 2. Expected: PASS. Confirmar sin restos: `grep -rn "GithubSource\|GitHubRepoUrl\|FeedPropiUrl\|OrdenFuentes\|FallbackUpdateSource" src tests --include=*.cs --include=*.json || true` → sin salida.

- [ ] **Step 5: Mutaciones**

1. En `App.axaml.cs`, quitar la envoltura `UrlFeedActualizaciones.Desde(` … `)` (queda `FeedUrl = ResolverApiBaseUrl(configuration)`) → rojo en `ConfigurarServicios_FeedUrl_DerivaDeLaUrlDeLaApi`.
2. En `Desde`, quitar `.TrimEnd('/')` → rojo en el caso con barra final.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Actualizaciones/UrlFeedActualizaciones.cs src/StockApp.Presentation/Actualizaciones/UpdaterOptions.cs src/StockApp.Presentation/Actualizaciones/VelopackGatewayReal.cs src/StockApp.Presentation/App.axaml.cs src/StockApp.Presentation/appsettings.json build/RELEASE_NOTES.md tests/StockApp.Presentation.Tests/Actualizaciones/UrlFeedActualizacionesTests.cs tests/StockApp.Presentation.Tests/Arranque/ConfigurarServiciosTests.cs tests/StockApp.Infrastructure.Tests/Kit/PackTestersWinTests.cs
git rm src/StockApp.Presentation/Actualizaciones/FallbackUpdateSource.cs tests/StockApp.Presentation.Tests/Actualizaciones/FallbackUpdateSourceTests.cs
git commit -m "feat(updater): una sola fuente de actualizaciones derivada de la URL de la API, sin GitHub"
```

---

### Task 4: Severidad efectiva = máximo de las releases entre la instalada y el destino

**Por qué no `UpdateInfo.DeltasToTarget`:** en Velopack 1.2.0 (`UpdateManager.CreateDeltaUpdateStrategy`, verificado con `ilspycmd`) `DeltasToTarget` queda VACÍO si no hay paquete local base o si el feed no tiene un delta de la versión destino. Con eso, una intermedia crítica se perdería justo en el caso de instalación sin base. Se lee el feed que devolvió la fuente.

**Files:**
- Create: `src/StockApp.Presentation/Actualizaciones/FuenteFeedRegistrado.cs`
- Modify: `src/StockApp.Presentation/Actualizaciones/IVelopackGateway.cs`, `VelopackGatewayReal.cs`, `VelopackUpdateService.cs` (`BuscarAsync` L30-L51)
- Test: `tests/StockApp.Presentation.Tests/Actualizaciones/FuenteFeedRegistradoTests.cs`, `tests/StockApp.Presentation.Tests/Actualizaciones/VelopackUpdateServiceTests.cs`

**Interfaces:**
- Consumes: `UpdaterOptions.FeedUrl` (Task 3).
- Produces:
  - `public sealed class FuenteFeedRegistrado : IUpdateSource` con ctor `(IUpdateSource interna)` y `public IReadOnlyList<VelopackAsset> UltimoFeed { get; }` (vacía al nacer; `GetReleaseFeed` delega y guarda `feed.Assets`; `DownloadReleaseEntry` delega sin más).
  - `IVelopackGateway` suma `SemanticVersion? VersionInstalada { get; }` y `IReadOnlyList<VelopackAsset> UltimoFeed { get; }`.
  - `VelopackGatewayReal`: `BuildSource` = `new FuenteFeedRegistrado(new SimpleWebSource(options.FeedUrl))`, guardada en un campo; `VersionInstalada => _manager.CurrentVersion`; `UltimoFeed => _fuente.UltimoFeed`.
  - `UpdateCheckResult` no cambia: `Severity` pasa a ser la efectiva; `Version` y `NotasMarkdown` siguen siendo las del destino.

- [ ] **Step 1: Tests en rojo**

`FuenteFeedRegistradoTests` (patrón `Mock<IUpdateSource>` del viejo `FallbackUpdateSourceTests`): `UltimoFeed_AntesDeConsultar_EstaVacio`; `GetReleaseFeed_DevuelveElFeedDeLaInternaYLoRegistra` (mismo objeto devuelto y `UltimoFeed` con sus assets); `DownloadReleaseEntry_DelegaEnLaInterna` (`Verify` Times.Once).

`VelopackUpdateServiceTests` (helpers `BuildAsset`/`BuildUpdateInfo` existentes; `gateway.Setup(g => g.VersionInstalada)` y `g.UltimoFeed`):
- `BuscarAsync_IntermediaCriticaYDestinoNormal_SeveridadCritica`: instalada 0.3.0; feed = 0.4.0 `severity: critical` + 0.5.0 `severity: normal`; destino 0.5.0 normal → `Severity == Critical`, `Version == "0.5.0"`, `NotasMarkdown` = las de 0.5.0.
- `BuscarAsync_ReleaseNoPosteriorALaInstalada_NoCuenta`: instalada 0.4.0; feed = 0.4.0 critical + 0.5.0 normal → `Normal`.
- `BuscarAsync_SinVersionInstalada_UsaSoloLaSeveridadDelDestino` (Review Focus 4): `VersionInstalada` null y `UltimoFeed` null (setup explícito `Returns((IReadOnlyList<VelopackAsset>)null!)`); destino important → `Important`, sin excepción.

- [ ] **Step 2: Rojo**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~FuenteFeedRegistradoTests|FullyQualifiedName~VelopackUpdateServiceTests"`. Pegar.

- [ ] **Step 3: Implementar**

En `BuscarAsync`: severidad = máximo (por el orden del enum `Normal < Important < Critical`) entre la del destino y la de cada asset de `_gateway.UltimoFeed ?? []` con `Version > VersionInstalada && Version <= destino.Version`, solo si `VersionInstalada` no es null. Actualizar el `<summary>` de `VelopackUpdateService` y el de `VelopackGatewayReal` (ya no hay fuente encadenada).

- [ ] **Step 4: Verde + mutación**

Run: el comando del Step 2 y luego todo `StockApp.Presentation.Tests`. Expected: PASS (los tests viejos siguen verdes con un mock suelto). Mutación: quitar el recorrido del feed (dejar solo la severidad del destino) → rojo en `BuscarAsync_IntermediaCriticaYDestinoNormal_SeveridadCritica`; pegar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Actualizaciones/FuenteFeedRegistrado.cs src/StockApp.Presentation/Actualizaciones/IVelopackGateway.cs src/StockApp.Presentation/Actualizaciones/VelopackGatewayReal.cs src/StockApp.Presentation/Actualizaciones/VelopackUpdateService.cs tests/StockApp.Presentation.Tests/Actualizaciones/FuenteFeedRegistradoTests.cs tests/StockApp.Presentation.Tests/Actualizaciones/VelopackUpdateServiceTests.cs
git commit -m "feat(updater): la severidad efectiva es el máximo de las releases entre la instalada y el destino"
```

---

### Task 5: Aviso crítico en sesión: política y overlay

**Files:**
- Modify: `src/StockApp.Application/Actualizaciones/Dtos.cs` (`ModoUx` L18-L25), `PoliticaUxActualizacion.cs`
- Create: `src/StockApp.Presentation/Actualizaciones/ActualizacionAvisoCriticoViewModel.cs`, `src/StockApp.Presentation/Actualizaciones/Views/ActualizacionAvisoCriticoView.axaml` (+ `.axaml.cs` como el de `ActualizacionBannerView`), `docs/ui-v2/mockups/actualizaciones/aviso-critico.html`
- Modify: `src/StockApp.Presentation/Actualizaciones/CoordinadorActualizacion.cs` (`ResolverOverlayViewModel` L38-L47)
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Actualizaciones/DatosActualizaciones.cs`, `ActualizacionesComparacionTests.cs`; `tests/StockApp.Presentation.UiTests/GuardianSistemaV2Tests.cs` (`VistasV2`, L79-L81)
- Test: `tests/StockApp.Application.Tests/Actualizaciones/PoliticaUxActualizacionTests.cs`, `tests/StockApp.Presentation.Tests/Actualizaciones/OverlayViewModelFactoryTests.cs`, `tests/StockApp.Presentation.Tests/Actualizaciones/ActualizacionViewModelsTests.cs`

**Interfaces:**
- Produces:
  - `ModoUx.AvisoCriticoEnSesion` (al final del enum).
  - `public AccionUx DecidirEnSesion(UpdateCheckResult resultado)` en `PoliticaUxActualizacion`: sin update → `Ninguno`; Normal e Important → `BannerDiscreto` (`Posponible: true`); Critical → `AvisoCriticoEnSesion` (`Posponible: false`, `ReintentaEnArranque: true`). Siempre con `Version` y `NotasMarkdown`. `Decidir` no se toca.
  - `public partial class ActualizacionAvisoCriticoViewModel : ViewModelBase` con ctor `(AccionUx accion)`, `public const string Mensaje = "Hay una actualización crítica. Guardá lo que estés haciendo y tocá Actualizar ahora."`, `[ObservableProperty] string? _version`, `[ObservableProperty] bool _operacionEnCurso`, `public event Action? ActualizarAhoraSolicitado` y `[RelayCommand] void ActualizarAhora()`. Sin `Posponer` ni cerrar.
  - `ResolverOverlayViewModel`: `ModoUx.AvisoCriticoEnSesion => new ActualizacionAvisoCriticoViewModel(accion)`.

- [ ] **Step 1: Tests en rojo**

- `PoliticaUxActualizacionTests.DecidirEnSesion_MapeaSeveridad` (`[Theory]`): Normal → `BannerDiscreto`; Important → `BannerDiscreto`; Critical → `AvisoCriticoEnSesion`; y `DecidirEnSesion_SinUpdate_Ninguno`. Los tests de `Decidir` no se tocan.
- `OverlayViewModelFactoryTests.AvisoCriticoEnSesion_DevuelveAvisoCriticoSinPosponer`: tipo `ActualizacionAvisoCriticoViewModel` y `typeof(ActualizacionAvisoCriticoViewModel).GetProperty("PosponerCommand")` es null.
- `ActualizacionViewModelsTests.AvisoCritico_ActualizarAhoraCommand_DisparaElEvento`.

- [ ] **Step 2: Rojo**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~PoliticaUxActualizacionTests` y `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~OverlayViewModelFactoryTests|FullyQualifiedName~ActualizacionViewModelsTests"`. Pegar.

- [ ] **Step 3: Política, VM y resolución**

Implementar lo del bloque Interfaces.

- [ ] **Step 4: Mockup, vista, comparación y guardián**

`aviso-critico.html`: copia de `banner.html` con los tokens `--v2-color-alerta-error-*` (los del `modo-degradado.html`), el texto de `Mensaje` y un solo `v2-boton v2-boton--primario` "Actualizar ahora". La vista replica `ActualizacionBannerView.axaml` (`Classes="ui2"`, `x:DataType="vm:ActualizacionAvisoCriticoViewModel"`, franja de 56 arriba con `V2AlertaError{Fondo,Borde,Texto}Brush`, un solo botón `primario` con `Command="{Binding ActualizarAhoraCommand}"` e `IsEnabled="{Binding !OperacionEnCurso}"`). La franja ocupa la misma banda superior que el banner y el modo degradado: no es un overlay de pantalla completa (ver "Hallazgos" al final). `DatosActualizaciones.AvisoCritico()` con `new AccionUx(ModoUx.AvisoCriticoEnSesion, "Corrige un problema de seguridad.", false, true, "0.5.0")`. En `ActualizacionesComparacionTests`, `AvisoCritico_IgualAlMockup` y `AvisoCritico_IgualAlMockup_1366x768`. Agregar `typeof(ActualizacionAvisoCriticoView)` a `GuardianSistemaV2Tests.VistasV2`.

- [ ] **Step 5: Build + verde**

Run: `dotnet build StockApp.sln`; los dos comandos del Step 2; `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~ActualizacionesComparacionTests` y `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~GuardianSistemaV2Tests`. Expected: PASS. Mirar el reporte de `scripts/ui-v2/comparar.sh AvisoCritico` y pegar el porcentaje en el ledger.

- [ ] **Step 6: Mutaciones**

1. En `DecidirEnSesion`, quitar la rama `Important` (cae al default) → rojo; y quitar la rama `Critical` → rojo (si cae a `BloqueoCritico` o a `Ninguno`, el test lo ve igual).
2. Quitar el caso `AvisoCriticoEnSesion` del `switch` de `ResolverOverlayViewModel` → rojo en `OverlayViewModelFactoryTests`.
3. Quitar `typeof(ActualizacionAvisoCriticoView)` de `VistasV2` → rojo en `Guardian_CubreTodasLasVistasDelEnsamblado`.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Actualizaciones/Dtos.cs src/StockApp.Application/Actualizaciones/PoliticaUxActualizacion.cs src/StockApp.Presentation/Actualizaciones/ActualizacionAvisoCriticoViewModel.cs src/StockApp.Presentation/Actualizaciones/Views/ActualizacionAvisoCriticoView.axaml src/StockApp.Presentation/Actualizaciones/Views/ActualizacionAvisoCriticoView.axaml.cs src/StockApp.Presentation/Actualizaciones/CoordinadorActualizacion.cs docs/ui-v2/mockups/actualizaciones/aviso-critico.html tests/StockApp.Application.Tests/Actualizaciones/PoliticaUxActualizacionTests.cs tests/StockApp.Presentation.Tests/Actualizaciones/OverlayViewModelFactoryTests.cs tests/StockApp.Presentation.Tests/Actualizaciones/ActualizacionViewModelsTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Actualizaciones/DatosActualizaciones.cs tests/StockApp.Presentation.VisualTests/Pantallas/Actualizaciones/ActualizacionesComparacionTests.cs tests/StockApp.Presentation.UiTests/GuardianSistemaV2Tests.cs
git commit -m "feat(updater): aviso crítico fijo para la actualización detectada a mitad de sesión"
```

---

### Task 6: El coordinador evalúa en sesión: una notificación por versión, sin superposición

**Files:**
- Modify: `src/StockApp.Presentation/Actualizaciones/CoordinadorActualizacion.cs` (`EvaluarEnArranqueAsync` L54-L79, `AplicarActualizacionAsync` L92-L104)
- Test: `tests/StockApp.Presentation.Tests/Actualizaciones/CoordinadorActualizacionTests.cs`

**Interfaces:**
- Consumes: `PoliticaUxActualizacion.DecidirEnSesion` (Task 5).
- Produces:
  - `public static readonly TimeSpan IntervaloChequeoHorario = TimeSpan.FromHours(1);`
  - `public async Task<AccionUx?> EvaluarEnSesionAsync()`: `null` si ya se pidió aplicar en esta sesión, si el semáforo está tomado (`WaitAsync(0)`), si `BuscarAsync` tira (en silencio), si no hay update o si la versión ya está en las notificadas; si no, agrega la versión y devuelve `DecidirEnSesion(resultado)`.
  - Estado interno: `SemaphoreSlim(1,1)` único para arranque (`WaitAsync()`), sesión (`WaitAsync(0)`) y aplicar (`WaitAsync()`); `HashSet<string>` de versiones notificadas; versión de la última búsqueda; flag de aplicación solicitada.
  - `EvaluarEnArranqueAsync` agrega su versión a las notificadas. `AplicarActualizacionAsync`: marca el flag al empezar; si termina en `false`, saca de las notificadas la versión buscada y desmarca el flag.

- [ ] **Step 1: Tests en rojo**

Con `Mock<IUpdateService>` (patrón `Crear(updateMock)` existente) y `TaskCompletionSource` para bloquear:
- `EvaluarEnSesion_MismaVersionDosVeces_NotificaUnaSola`: 0.5.0 Important → primera `AccionUx` `BannerDiscreto`, segunda `null`.
- `EvaluarEnSesion_VersionMostradaEnElArranque_NoSeRepite`.
- `EvaluarEnSesion_BuscarTira_DevuelveNullSinPropagar`.
- `EvaluarEnSesion_ConArranqueEnCurso_SalteaElTickSinBuscar`: `BuscarAsync` del arranque bloqueado en un TCS; mientras tanto `EvaluarEnSesionAsync()` devuelve `null` y `BuscarAsync` se llamó una sola vez.
- `EvaluarEnSesion_ConAplicacionEnCurso_NoBusca`: `DescargarAsync` bloqueado; ídem.
- `EvaluarEnSesion_TrasAplicarExitoso_NoVuelveABuscar`.
- `EvaluarEnSesion_TrasAplicarFallido_LaVersionSePuedeVolverANotificar`: `DescargarAsync` tira → `AplicarActualizacionAsync` da `false` → la misma 0.5.0 vuelve a dar `AccionUx`.
- `EvaluarEnSesion_Critica_DevuelveAvisoCriticoEnSesion`.

- [ ] **Step 2: Rojo**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~CoordinadorActualizacionTests`. Pegar.

- [ ] **Step 3: Implementar**

Lo del bloque Interfaces; los `Release` en `finally`. Los tests existentes del coordinador no cambian.

- [ ] **Step 4: Verde + mutaciones**

Run: el comando del Step 2. Expected: PASS. Mutaciones (pegar cada rojo):
1. Quitar el registro de la versión en `EvaluarEnSesionAsync` → rojo en `MismaVersionDosVeces`.
2. Quitar el registro en `EvaluarEnArranqueAsync` → rojo en `VersionMostradaEnElArranque`.
3. Quitar el `if (!await _cerrojo.WaitAsync(0)) return null;` (y su `Release`) → rojo en `ConArranqueEnCurso`.
4. Quitar la remoción de la versión en la rama de falla de `AplicarActualizacionAsync` → rojo en `TrasAplicarFallido`.
5. Quitar el chequeo del flag de aplicación solicitada → rojo en `TrasAplicarExitoso`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Actualizaciones/CoordinadorActualizacion.cs tests/StockApp.Presentation.Tests/Actualizaciones/CoordinadorActualizacionTests.cs
git commit -m "feat(updater): evaluación en sesión con una notificación por versión y sin superposición"
```

---

### Task 7: Bucle horario en `ShellViewModel`

**Files:**
- Create: `src/StockApp.Presentation/Actualizaciones/IEsperaChequeo.cs` (interfaz + `EsperaChequeoReal`)
- Modify: `src/StockApp.Presentation/ViewModels/ShellViewModel.cs` (ctor L47-L64, `EvaluarYAsignarOverlayAsync` L105-L131, `SuscribirEventosOverlay` L139-L157, `DispararAplicarActualizacion` L177-L199)
- Modify: `src/StockApp.Presentation/App.axaml.cs` (registro junto a `AddSingleton<CoordinadorActualizacion>()`, L457)
- Test: `tests/StockApp.Presentation.Tests/Actualizaciones/ShellViewModelChequeoHorarioTests.cs`

**Interfaces:**
- Consumes: `CoordinadorActualizacion.EvaluarEnSesionAsync`, `IntervaloChequeoHorario` (Task 6); `ResolverOverlayViewModel` y `ActualizacionAvisoCriticoViewModel` (Task 5).
- Produces:
  - `public interface IEsperaChequeo { Task EsperarAsync(TimeSpan intervalo, CancellationToken ct); }` y `public sealed class EsperaChequeoReal : IEsperaChequeo` (`Task.Delay(intervalo, ct)`).
  - `ShellViewModel`: parámetro opcional nuevo al final del ctor, `IEsperaChequeo? esperaChequeo = null` (null → `EsperaChequeoReal`; los 4 archivos de test que hacen `new ShellViewModel(` no cambian); implementa `IDisposable` (`Dispose` cancela el bucle; el contenedor lo dispone en `desktop.Exit`, `App.axaml.cs:151`); `internal Task _tareaChequeoHorario = Task.CompletedTask;`; `internal static bool DebeReemplazarOverlay(ViewModelBase? actual, ModoUx nuevo)`.
  - `DispararAplicarActualizacion` pasa a recibir `Action<bool>? marcarEnCurso` (el banner pasa `v => banner.OperacionEnCurso = v`, el aviso crítico `v => aviso.OperacionEnCurso = v`, modal y bloqueo `null`).
  - `App.axaml.cs`: `services.AddSingleton<IEsperaChequeo, EsperaChequeoReal>();`.

Reglas de `DebeReemplazarOverlay` (spec §5.4.6 + Review Focus 1), en este orden: actual `ActualizacionBloqueoViewModel` (bloqueo o degradado) → `false`; nuevo `AvisoCriticoEnSesion` → `true`; actual `ActualizacionAvisoCriticoViewModel` → `false`; actual `ActualizacionModalViewModel` → `false`; si no (banner o null) → `true`.

- [ ] **Step 1: Fake de espera y tests en rojo**

`EsperaManual : IEsperaChequeo` (privado del test): `EsperarAsync` registra el intervalo pedido y devuelve una tarea que se completa cuando lo acumulado por `Avanzar(TimeSpan)` desde ese pedido llega al intervalo (o se cancela con el token); `Task SiguienteEsperaAsync()` se completa cuando el bucle vuelve a llamar a `EsperarAsync`. Sin `Task.Delay` ni relojes reales. `FakeUiDispatcher` inline como en `ShellViewModelActualizacionTests`.

Tests (arranque sin update; después `await vm._tareaActualizacion` y `await espera.SiguienteEsperaAsync()`):
- `PrimerTick_PideUnaHora`: el intervalo registrado es `CoordinadorActualizacion.IntervaloChequeoHorario` (1 h).
- `Avanzar59Minutos_NoBusca`: `BuscarAsync` sigue en 1 llamada (la del arranque).
- `Avanzar60Minutos_BuscaUnaVez`: tras `Avanzar(1 h)` y `await SiguienteEsperaAsync()`, 2 llamadas.
- `Dispose_DetieneElBucle`: `vm.Dispose()` → `_tareaChequeoHorario` termina (sin excepción observable) y `Avanzar` ya no busca.
- `TickConFalla_ElSiguienteTickVuelveABuscar` (Review Focus 5): el primer tick tira `HttpRequestException`; el segundo tick busca igual.
- `TickCritico_AsignaElAvisoCritico_YActualizarAhoraAplica`: el tick trae 0.6.0 Critical → `OverlayActualizacion` es `ActualizacionAvisoCriticoViewModel`; `ActualizarAhoraCommand.Execute(null)` + `await vm._tareaAplicarActualizacion` → `DescargarAsync` y `AplicarYReiniciar` llamados una vez.
- `DebeReemplazarOverlay_Reglas` (`[Theory]` sobre las 12 combinaciones: actual ∈ {null, banner, modal, bloqueo, degradado, aviso crítico} × nuevo ∈ {`BannerDiscreto`, `AvisoCriticoEnSesion`}, incluida la de Review Focus 1 = `false`).
- `TickConBannerNuevo_ConModalAbierto_NoLoReemplaza`: arranque Important (modal) y tick con 0.6.0 Normal → sigue el modal.

- [ ] **Step 2: Rojo**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~ShellViewModelChequeoHorarioTests`. Pegar.

- [ ] **Step 3: Implementar**

Al final de `EvaluarYAsignarOverlayAsync` (también si el arranque falló): `_tareaChequeoHorario = Task.Run(() => BucleChequeoHorarioAsync(_cts.Token));`. El bucle: `EsperarAsync(IntervaloChequeoHorario, ct)` (cancelación → sale); `EvaluarEnSesionAsync()`; si devuelve acción, `ResolverOverlayViewModel` + `SuscribirEventosOverlay` y `_uiDispatcher.Post(() => { if (DebeReemplazarOverlay(OverlayActualizacion, accion.Modo)) OverlayActualizacion = vm; })`; cualquier otra excepción de un tick se traga y el bucle sigue. `SuscribirEventosOverlay` suma el caso del aviso crítico. Registrar `IEsperaChequeo` en `App.axaml.cs`.

- [ ] **Step 4: Build + verde**

Run: `dotnet build StockApp.sln`; el comando del Step 2; después todo `StockApp.Presentation.Tests` (los `ShellViewModelActualizacionTests` existentes siguen verdes).

- [ ] **Step 5: Mutaciones**

1. Quitar el `try`/`catch` del tick → rojo en `TickConFalla_...`.
2. Quitar la regla del aviso crítico abierto en `DebeReemplazarOverlay` → rojo en el caso (aviso crítico, `BannerDiscreto`).
3. Quitar la regla del modal → rojo en `TickConBannerNuevo_ConModalAbierto_NoLoReemplaza`.
4. Quitar el `Cancel()` de `Dispose` → rojo en `Dispose_DetieneElBucle`.
5. Quitar el caso del aviso crítico de `SuscribirEventosOverlay` → rojo en `TickCritico_...`.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Actualizaciones/IEsperaChequeo.cs src/StockApp.Presentation/ViewModels/ShellViewModel.cs src/StockApp.Presentation/App.axaml.cs tests/StockApp.Presentation.Tests/Actualizaciones/ShellViewModelChequeoHorarioTests.cs
git commit -m "feat(updater): chequeo de actualizaciones cada una hora sin interrumpir la sesión"
```

---

### Task 8: `build/publicar-release.sh` y documentación de la publicación

**Files:**
- Create: `build/publicar-release.sh` (ejecutable)
- Modify: `build/pack-win.ps1:166` (el mensaje final apunta a `build/publicar-release.sh`)
- Modify: `build/README-empaquetado.md` (L3, L121, L170, sección "Publicar en GitHub Releases" L186-L199, L274, L298), `build/RELEASE_NOTES.md:56`
- Test: `tests/StockApp.Infrastructure.Tests/Kit/PublicarReleaseTests.cs`

**Interfaces:**
- Consumes: `/var/lib/stockapp/updates` (Task 2) y `GET /updates/*` (Task 1). Configuración idéntica a `deploy/deploy-vps.sh`: `source` de `deploy/.env` LOCAL (L135-L141); `VPS_HOST`/`VPS_USER`/`VPS_SSH_PORT`/`VPS_DIR` con los defaults de L34-L41; `API_SCHEME`/`API_HOSTNAME`/`API_PORT` con la validación de L157-L177 (`API_URL_BASE` y `CURL_RESOLVE_REMOTO` iguales); `ssh_vps`/`scp_vps` idénticos a L99-L105 (`-o BatchMode=yes`).
- Produces: `build/publicar-release.sh <version> [--origen <dir>]` (default `--origen` = `<repo>/releases/win`). Formas de los comandos remotos, fijas para que los stubs las despachen:
  - Relevamiento: `if sudo test -d /var/lib/stockapp/updates; then sudo find /var/lib/stockapp/updates -maxdepth 1 -type f -printf '%f %s\n'; fi`. Salida vacía = "no hay nada" (primera publicación); exit de `ssh` ≠ 0 = "no pude relevar" → aborta sin subir nada.
  - Paquetes: `sudo install -d -o stockapp -g stockapp -m 750 /var/lib/stockapp/updates` y, por cada `.nupkg` faltante, `sudo install -o stockapp -g stockapp -m 640 ${VPS_DIR}/updates-staging/<f> /var/lib/stockapp/updates/<f>`.
  - Feed: `sudo install -o stockapp -g stockapp -m 640 ${VPS_DIR}/updates-staging/releases.win.json /var/lib/stockapp/updates/releases.win.json.tmp && sudo mv -f /var/lib/stockapp/updates/releases.win.json.tmp /var/lib/stockapp/updates/releases.win.json`.
  - Limpieza (`trap EXIT`, también en falla): `rm -rf -- ${VPS_DIR}/updates-staging` (con `VPS_DIR` validado no vacío).
- Pasos, en este orden (spec §5.5): (1) validación local con `jq` (falla al arrancar si no está): el asset `Type == "Full"` de mayor versión es `<version>`; cada `FileName` cumple `^[A-Za-z0-9._-]+\.nupkg$` (si no, aborta: va a un comando remoto); existe en `--origen` con su `Size` (`stat -c %s`); (2) relevamiento; (3) `mkdir -p` remoto del staging y `scp` de los `.nupkg` cuyo nombre y tamaño no están ya en el servidor, más el JSON; (4) instalar paquetes; (5) re-relevar y verificar que CADA `FileName` del JSON nuevo esté con su `Size`; si no, abortar SIN tocar el feed; (6) feed con rename atómico; (7) verificación final: por `ssh_vps`, `curl -fsS ${CURL_RESOLVE_REMOTO}${API_URL_BASE}/updates/releases.win.json` contiene `<version>` y `curl -fsSI` del full nuevo da 200 con `Content-Length` = `Size`; si `API_HOSTNAME` está definido, además `curl` local a `https://${API_HOSTNAME}:${API_PORT}/updates/releases.win.json`. La API no se reinicia.

- [ ] **Step 1: Tests en rojo**

`PublicarReleaseTests` (mismo armado que `DeployVpsTests`: repo falso con el script real copiado en `build/`, `deploy/.env` dummy y `releases/win/` con un `releases.win.json` generado por el test: 0.4.0 full (100 bytes), 0.5.0 full (200 bytes) y 0.5.0 delta (50 bytes), con sus archivos). Stubs por `PATH` que NO mienten: `ssh` registra cada invocación (`%q`, como `SshFalso`) y EJECUTA el comando remoto contra un servidor falso local (`$SERVIDOR_FALSO`): reescribe `/var/lib/stockapp` y el `VPS_DIR` a rutas bajo `$SERVIDOR_FALSO`, saca `sudo ` y `-o stockapp -g stockapp `, y corre el texto con `bash -c`, igual que el patrón "el comando remoto corre de verdad contra el PATH del test" de `DeployVpsTests` (L543); `scp` copia al staging falso y registra; `curl` sirve desde `$SERVIDOR_FALSO/var/lib/stockapp/updates` (cuerpo del JSON, o cabeceras con `Content-Length` real para `-I`). Flags de simulación: falla del relevamiento (exit 255), `scp` que falla en el N-ésimo archivo, archivo que queda truncado al instalarse, `curl` que devuelve 404.

- `CaminoFeliz_PaquetesInstaladosYVerificadosAntesDelRenameDelFeed`: exit 0; en el rastro de `ssh`, el índice del `mv -f …releases.win.json.tmp…` es mayor que el de TODOS los `install … .nupkg` y que el del re-relevamiento; el servidor falso queda con los 3 `.nupkg` y el JSON, sin `.tmp`.
- `PrimeraPublicacion_CarpetaRemotaInexistente_SubeTodo`.
- `YaPublicadoConMismoTamano_SoloSubeLoQueFalta`: el servidor falso ya tiene 0.4.0 full con 100 bytes → el rastro de `scp` no la incluye.
- `RepublicarLaMismaVersion_NoSubeNingunPaquete` (Review Focus 3): dos corridas seguidas; en la segunda el `scp` lleva solo el JSON y el exit es 0.
- `PaqueteLocalFaltante_AbortaSinTocarElServidor`: exit ≠ 0, stderr nombra el archivo, rastros de `ssh` y `scp` vacíos.
- `TamanoLocalDistinto_AbortaSinTocarElServidor`.
- `VersionNoEsLaMayorDelFeed_Aborta` (pedir 0.4.0).
- `FileNameInseguro_Aborta` (`FileName` = `../x.nupkg`).
- `RelevamientoFalla_AbortaSinSubirNada`: exit ≠ 0 y stderr distingue "no se pudo relevar" de "carpeta vacía"; `scp` sin rastro.
- `VerificacionRemotaFalla_AbortaSinRename_YElFeedVigenteQuedaIntacto`: con un JSON viejo ya publicado y un paquete truncado al instalar → exit ≠ 0, ningún `mv` en el rastro, el `releases.win.json` del servidor falso es el viejo.
- `ScpFalla_NoTocaElFeedYLimpiaElStaging` (Review Focus 2): falla el segundo `scp` → exit ≠ 0, sin `mv`, el staging falso no existe al terminar.
- `VerificacionFinalCurlFalla_ExitDistintoDeCero`.
- `ApiSchemeHttps_LaVerificacionRemotaUsaResolve`: con `API_SCHEME=https` y `API_HOSTNAME`, el curl remoto lleva `--resolve host:puerto:127.0.0.1` y no `-k`.

- [ ] **Step 2: Rojo**

Run: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~PublicarReleaseTests`. Expected: FAIL (el script no existe); pegar.

- [ ] **Step 3: Implementar el script**

Con los pasos y las formas del bloque Interfaces. Encabezado como el de `deploy-vps.sh` (uso, aviso de VPS compartido, los pasos y por qué el feed va último). Ningún `grep` en un pipe sin `|| true` donde "no hay match" es válido.

- [ ] **Step 4: Verde + mutaciones**

Run: el comando del Step 2. Expected: PASS. Mutaciones QUITANDO (no se "invierte" el orden como dice la spec §7: invertir es agregar):
1. Quitar la llamada al paso de verificación remota (5) → rojo en `VerificacionRemotaFalla_...` (hay `mv`).
2. Quitar la llamada al paso de paquetes (4) → rojo en `CaminoFeliz_...`.
3. Quitar el filtro del relevamiento en el armado de la lista de `scp` → rojo en `YaPublicadoConMismoTamano_...`.
4. Quitar el `trap` → rojo en `ScpFalla_...`.
5. Quitar la validación de `FileName` → rojo en `FileNameInseguro_Aborta`.

- [ ] **Step 5: Documentación**

`README-empaquetado.md`: la sección "Publicar en GitHub Releases" pasa a "Publicar en el VPS" con `build/publicar-release.sh`, el orden (paquetes, verificación, feed con rename atómico), `--origen` para cuando `pack-win.ps1` corre en un checkout de Windows distinto, conservar `releases/win` (sin ella la próxima versión sale solo full, ~53 MB por cliente), "retirar una versión mala = publicar la buena con número mayor" (Velopack no hace downgrade), y que el feed es público (spec §9 R2). L170: `RELEASES` NO lo usa el cliente (Velopack 1.2.0 pide solo `releases.win.json` y los `.nupkg`). Corregir L3, L121, L274 y L298 (fallback a GitHub). `pack-win.ps1:166` y `RELEASE_NOTES.md:56` apuntan al script.

- [ ] **Step 6: Commit**

```bash
git add build/publicar-release.sh build/pack-win.ps1 build/README-empaquetado.md build/RELEASE_NOTES.md tests/StockApp.Infrastructure.Tests/Kit/PublicarReleaseTests.cs
git commit -m "feat(build): publicar-release.sh sube una versión al VPS con el feed último y rename atómico"
```

---

### Task 9: Verificación contra el VPS de testing, validación manual pendiente y merge

**Files:**
- Modify: `build/VALIDACION-MANUAL-faseA.md` (V5: L51 y L138-L146; L241; flujo L280 en adelante; caso nuevo del chequeo horario)

**Interfaces:**
- Consumes: todo lo anterior. `deploy/.env` (git-ignored) y `releases/win/` (0.2.0, generados en Windows el 2026-08-23) solo existen en el checkout principal.

- [ ] **Step 1: Suite completa**

Run, de a uno, los seis proyectos del Step 1 de la Task 1 más `dotnet build StockApp.sln`. Expected: verde salvo falsos conocidos; totales = línea base + nuevos − los de `FallbackUpdateSourceTests` borrados (motivo: lo custodiado ya no existe). Pegar en el ledger.

- [ ] **Step 2: Deploy de la API al VPS de testing**

Copiar el `.env` del operador: `cp /home/capua25/workspace/stockapp/deploy/.env deploy/.env` (git-ignored; tiene `API_SCHEME=https`, `API_HOSTNAME=stockapp.capuanomartin.dev`, `API_PORT=8080`). `VPS_USER` tiene que venir del entorno: si no está, el agente se DETIENE y se lo pide a Martin. Run: `deploy/deploy-vps.sh "$(git rev-parse --short HEAD)-feed"`. Expected: exit 0 y el paso 6 en verde. El `install.sh` nuevo viaja solo (cambió).

- [ ] **Step 3: Publicar una versión real**

Run: `build/publicar-release.sh 0.2.0 --origen /home/capua25/workspace/stockapp/releases/win`. Expected: exit 0 y la verificación final en verde.

- [ ] **Step 4: Verificación con curl desde afuera (pegar salidas en el ledger)**

- `curl -sS -o /dev/null -w '%{http_code} %{content_type}\n' https://stockapp.capuanomartin.dev:8080/updates/releases.win.json` → `200 application/json`, y el cuerpo contiene `"0.2.0"`.
- `curl -sI https://stockapp.capuanomartin.dev:8080/updates/GestionMunicipal-0.2.0-full.nupkg` → 200 con `Content-Length: 53503903`.
- `curl -sS -o /dev/null -w '%{http_code}\n' https://stockapp.capuanomartin.dev:8080/updates/` → 404.
- `curl -sS -o /dev/null -w '%{http_code}\n' https://stockapp.capuanomartin.dev:8080/updates/releases.win.json.tmp` → 404.
- Por SSH, `sudo stat -c '%U:%G %a' /var/lib/stockapp/updates` → `stockapp:stockapp 750`, y `systemctl is-active stockapp-api` → `active` (nada fuera de StockApp).

Una salida vacía o un error de conexión NO es un resultado: se reporta como "no se pudo verificar", con el exit code y el stderr.

- [ ] **Step 5: Validación manual pendiente (Windows real)**

En `VALIDACION-MANUAL-faseA.md`, V5 y el flujo pasan a publicar con `build/publicar-release.sh` contra el VPS (sin GitHub); se borra L241 (`FeedPropioUrl = null`). Caso nuevo con los 5 pasos de la spec §7 ("End to end en Windows real"), marcado PENDIENTE (requiere Windows), incluido el chequeo horario (bajar la constante en un build de prueba) y la actualización con la licencia sin activar. Anotar en el ledger que el ciclo real de Velopack sigue sin validar.

- [ ] **Step 6: Commit, merge y push**

```bash
git add build/VALIDACION-MANUAL-faseA.md
git commit -m "docs(updater): validación manual del feed propio y del chequeo horario en Windows real"
git -C /home/capua25/workspace/stockapp merge --ff-only feat/feed-actualizaciones-propio
git -C /home/capua25/workspace/stockapp push origin main
```

Si el `--ff-only` falla porque `main` avanzó, se rebasa la rama sobre `main` en el worktree, se repite el Step 1 y se reintenta; nunca un merge con commit.

---

## Hallazgos contra la spec (decididos en este plan, con evidencia)

1. **Severidad de las intermedias (spec §5.3):** `DeltasToTarget` no sirve (vacío sin paquete base o sin delta del destino, `UpdateManager.CreateDeltaUpdateStrategy` de Velopack 1.2.0). Se lee el feed capturado por `FuenteFeedRegistrado`, así que `BuildSource` no queda en el `return new SimpleWebSource(options.FeedUrl);` literal de la spec, sino envuelto (Task 4).
2. **`RELEASES` (spec §5.2, R1):** confirmado que `SimpleWebSource` no lo pide; no hace falta ningún mapeo sin extensión.
3. **"Franja roja que no tapa el contenido" (spec §5.4.2):** el overlay vive en la capa superior del `Grid` de `MainWindow.axaml` (L23-L26), igual que el banner de 56 y el modo degradado, que ya tapan la banda superior del contenido. El plan la interpreta como "franja superior, no overlay de pantalla completa". Reservarle espacio de verdad exige partir el `Grid` en filas, lo que cambia también el banner y el modo degradado: queda fuera.
4. **Mutación "invertir el orden" (spec §7, script):** la convención del proyecto es mutar QUITANDO; el orden se custodia quitando el paso de verificación y el de paquetes (Task 8 Step 4).
5. **"Se reusa la misma variable que alimenta el `HttpClient` principal" (spec §5.3):** esa variable vive dentro de la lambda del registro (`App.axaml.cs:180`); el plan llama a la misma `ResolverApiBaseUrl(configuration)`, como ya hace el `HttpClient` keyed `"Descargas"` (L207).
