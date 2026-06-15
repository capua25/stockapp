# Incremento 7 — Fase A: Empaquetado (Velopack) + Actualizador in-app

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para implementar este plan tarea por tarea. Los pasos usan checkbox (`- [ ]`) para tracking.

> **Estado del plan:** Propuesta + Diseño a aprobar por el usuario. Una vez aprobado, la sección **## Tareas** se ejecuta en TDD estricto, un commit por task.

**Goal:** Convertir StockApp en una app **distribuible y auto-actualizable**. Fase A entrega dos
subsistemas: (1) **empaquetado** self-contained con Velopack que genera `Setup.exe` (Windows) y
`AppImage` (Linux) sin requerir prerequisitos en la PC del cliente, mediante scripts locales
versionados; y (2) un **actualizador in-app** que al arranque chequea updates contra una fuente
**encadenada** (feed propio parametrizable → fallback GitHub), descarga **deltas** y aplica la
actualización con una **UX por severidad** (normal / important / critical, incluyendo modo
degradado bloqueante). La abstracción se diseña respetando Clean Architecture: la lógica de
decisión es testeable con xUnit; Velopack queda confinado a Infrastructure detrás de una interfaz
fina.

**Tech Stack:** .NET 10 (self-contained, runtime incluido), C#, Clean Architecture, Avalonia MVVM
(CommunityToolkit.Mvvm 8.4), Velopack (NuGet `Velopack` + CLI global `vpk`), GitHub Releases como
fuente primaria real, xUnit + Moq. TDD estricto en Application/Infrastructure/Presentation;
packaging validado manualmente por OS (no automatizable con xUnit).

---

## Propuesta

### Contexto y motivación

Los incrementos 1–6 dejaron una app funcional (catálogo, movimientos, reportes, auditoría) que hoy
**solo corre desde el código fuente / `dotnet run`**. No existe forma de entregarla a un cliente
B2B ni de actualizarla sin intervención manual del desarrollador. El Incremento 7 "Distribución"
cubre ese hueco. La **Fase A** ataca lo mínimo viable para entregar valor: empaquetar un instalador
y resolver actualizaciones automáticas. La **Fase B** (licenciamiento offline, machine fingerprint,
token de reset firmado, etc.) se construye **después**, sobre la base de distribución que esta fase
establece — y **no se diseña acá**.

Restricciones del negocio que moldean las decisiones:

- El cliente final **no tiene .NET instalado** ni se le va a pedir que lo instale → self-contained
  con runtime embebido es obligatorio.
- No hay infraestructura de CI/CD ni servidor de updates propio **todavía** → GitHub Releases
  (repo público, solo releases) es la fuente real y funcional desde el día uno; el feed propio
  queda parametrizable para enchufarlo cuando exista.
- No hay certificado de code signing → se acepta la advertencia de SmartScreen (B2B directo,
  instalación asistida).
- El instalador **nunca** toca la BD ni los datos del usuario (viven en el directorio de datos del
  usuario, gestionado por `IUserDataPathProvider` desde el Inc 2).

### Objetivos medibles

1. `vpk pack` produce un `Setup.exe` (Windows) y un `AppImage` (Linux) instalables en una máquina
   limpia **sin** prerequisito de .NET.
2. La app, al arrancar, chequea updates en background contra GitHub Releases; si hay uno, lo
   descarga (delta) y aplica la UX correspondiente a su `severity`.
3. Si la fuente primaria (feed propio, cuando exista) falla o no está configurada, el chequeo cae
   automáticamente al fallback GitHub **sin tocar código** (solo configuración).
4. Toda la lógica de decisión (parseo de severity, mapeo severity→acción de UI, encadenamiento de
   fuentes, adaptación de Velopack a DTOs propios) es **unit-testeable** sin levantar UI ni Velopack
   real.
5. `dotnet build StockApp.sln` y `dotnet test` pasan con 0 errores y sin regresiones de Inc 1–6.

### Alcance

#### Entra (In Scope)

- **Application:** `IUpdateService` (contrato) + DTOs propios como records (`UpdateCheckResult`,
  `UpdateProgress`, `AccionUx`) + enum `UpdateSeverity`; parser de severity (string notas →
  `UpdateSeverity`) y **política de UX** (severity + estado descarga → acción de UI) como piezas
  puras y testeables. Application **no** conoce Velopack.
- **Infrastructure:** `IVelopackGateway` (interfaz fina) + `VelopackGatewayReal` que envuelve
  `UpdateManager`; `VelopackUpdateService : IUpdateService` que adapta el gateway y mapea a DTOs;
  `FallbackUpdateSource : IUpdateSource` (fuente encadenada [primaria, secundaria] con orden por
  config); configuración del actualizador (`UpdaterOptions`) y registro DI.
- **Presentation:** `VelopackApp.Build().Run()` como primera línea de `Program.Main`; 3 modos de UI
  (banner discreto, modal posponible, overlay rojo bloqueante + modo degradado) como
  ViewModels/Views; integración del chequeo al arranque en background, enganchado en `ShellViewModel`.
- **Empaquetado:** carpeta `build/` con scripts versionados por OS (`pack-win.ps1`, `pack-linux.sh`)
  que hacen `dotnet publish` self-contained + `vpk pack`, con `--signParams` comentado; documento
  de flujo manual paso a paso.
- **Versionado:** `InformationalVersion` en el `.csproj` de Presentation como single source of
  truth, propagada a `vpk pack --packVersion` y expuesta en un "Acerca de".

#### Fuera (Out of Scope)

- **Licenciamiento offline, machine fingerprint, token de reset firmado** → Fase B. Solo se
  mencionan como dependencia futura que se apoya en esta base de distribución.
- **Backups / migraciones de BD** → ya implementados (Inc 2: `BackupPeriodicoService`,
  `DatabaseInitializer`) o ajenos a esta fase; el actualizador **no** los toca.
- **CI/CD** (GitHub Actions u otro) → Decisión D6: packaging por scripts locales manuales en esta
  fase.
- **Code signing de Windows** → Decisión D7: fuera de alcance; `Setup.exe` mostrará SmartScreen
  "editor desconocido" pero instala igual. Flags `--signParams` quedan preparados/comentados.
- **Auto-update silencioso forzado sin consentimiento** → la UX siempre respeta la severidad; solo
  `critical` bloquea.
- **Servidor de feed propio** → no se construye acá; su URL queda parametrizable.

### Decisiones firmes (tomadas por el usuario — no se re-discuten)

**D1 — Velopack self-contained con runtime .NET 10 embebido.**
`dotnet publish -c Release -r <rid> --self-contained` + `vpk pack` genera `Setup.exe` (Windows) y
`AppImage` (Linux). El cliente **no** necesita .NET instalado. El instalador **no** toca BD ni datos
del usuario.

**D2 — Fuente encadenada: feed propio (primario) → GitHub (fallback).**
Se implementa un `IUpdateSource` propio **encadenado** `[primario, GithubSource]` que intenta el
primario y cae al fallback ante excepción/timeout. Los **mismos assets** se publican en ambos
orígenes. No existe un ChainedSource de fábrica en Velopack → hay que implementarlo.

**D3 — Updates diferenciales (delta).**
Solo se descarga lo que cambió respecto a la release instalada (`vpk pack` genera deltas
automáticamente si hay release previa en el directorio de salida).

**D4 — Severity por release con UX diferenciada.**
Cada release declara `severity`: `normal | important | critical`. UX:
- **normal:** banner discreto; aplica al próximo reinicio voluntario; reintenta en silencio si falla.
- **important:** cartel modal al arrancar; posponible pero insiste en cada arranque; reintenta en
  cada arranque si falla.
- **critical:** cartel rojo que **BLOQUEA** el uso hasta actualizar; si no se puede bajar → **MODO
  DEGRADADO** (app operable pero con banner rojo permanente no-cerrable; reintenta en cada arranque).
- El texto de los carteles sale de las **release notes** (markdown).

**D5 (era D-A1) — GitHub es la fuente primaria real desde el día uno.**
GitHub Releases (repo público, solo releases) es la fuente **funcional** hoy. La URL del feed propio
queda **parametrizable** en configuración para enchufarla cuando exista el servidor. El
`IUpdateSource` encadenado se diseña **completo igual**, pero en pruebas solo se ejercita la rama
GitHub. Cuando el feed propio exista, **pasa a primario y GitHub a fallback** reordenando por
configuración — **sin tocar código**.

**D6 (era D-A2) — Empaquetado mediante scripts locales versionados, sin CI/CD.**
`dotnet publish` + `vpk` ejecutados a mano por OS. El packaging **no** es testeable con xUnit:
validación manual por OS.

**D7 (era D-A3) — Code signing de Windows fuera de alcance en esta fase.**
Sin certificado, `Setup.exe` muestra advertencia SmartScreen "editor desconocido" pero instala igual
(aceptable B2B directo). Los scripts dejan los flags `--signParams` **preparados/comentados** para
enchufar un certificado después.

---

## Especificación

### Versionado (single source of truth)

- La versión vive en `<InformationalVersion>` del `.csproj` de Presentation (SemVer2 estricto, ej:
  `1.0.0`, `1.1.0-beta.1`).
- El script de packaging **lee esa misma versión** y la pasa a `vpk pack --packVersion`. Una sola
  fuente: el `.csproj`.
- En runtime, la versión actual se obtiene del `InformationalVersion` del assembly (no hay getter
  público oficial `GetCurrentVersion()` en Velopack). Post-chequeo también está disponible
  `UpdateInfo.CurrentVersion`.
- Se expone en un panel "Acerca de" accesible desde el shell.

### Comportamiento del actualizador

**Arranque (background):** ni bien el shell está listo, se dispara un chequeo **asíncrono en
background** (no bloquea el render inicial salvo que la severidad sea `critical`). Secuencia:

1. **Init Velopack** — `VelopackApp.Build().Run()` ya corrió como primera línea de `Main`. Esto
   gestiona hooks de instalación/actualización/desinstalación de Velopack. Si la app **no** está
   instalada vía Velopack (corriendo en dev / `dotnet run`), el chequeo posterior lanzará
   `NotInstalledException` → se **traga** y se saltea todo el flujo de updates (la app sigue normal).
2. **Chequeo** — `IUpdateService.BuscarAsync()` consulta la fuente encadenada. Devuelve un
   `UpdateCheckResult` propio: `HayUpdate`, `Version`, `Severity`, `NotasMarkdown`.
3. **Decisión de UX** — la **política** mapea `(Severity, estado_descarga)` → `AccionUx`. Para
   `critical`, la política puede exigir resolver la severidad **antes** de descargar nada (de ahí el
   riesgo documentado de disponibilidad de notas pre-descarga).
4. **Descarga** — `IUpdateService.DescargarAsync()` baja el delta. Reporta progreso vía
   `IProgress<UpdateProgress>`.
5. **Aplicación** — `IUpdateService.AplicarYReiniciar()` (Windows: `ApplyUpdatesAndRestart`, no
   retorna). En Linux el AppImage toma efecto en la **próxima ejecución** (no reinicia in-place).

**Fuente encadenada (D2 + D5):** `FallbackUpdateSource` recibe una **lista ordenada** de fuentes
desde configuración. En `GetReleaseFeed()` y `DownloadFileAsync()`: intenta la primera; ante
excepción/timeout, cae a la siguiente. Hoy el orden efectivo es `[GitHub]` (el feed propio aún no
existe → su URL está vacía y se omite). Cuando exista: `[FeedPropio, GitHub]`, configurado, sin
tocar código.

### Severidades y su UX (detallada)

| Severity   | Disparador de UI        | Bloquea | Posponible | Si la descarga falla |
|------------|-------------------------|---------|------------|----------------------|
| `normal`   | Banner discreto (no-modal) | No   | Sí (implícito) | Reintenta en silencio en próximo arranque |
| `important`| Modal al arrancar       | No (app usable detrás) | Sí, pero reaparece cada arranque | Reintenta en cada arranque |
| `critical` | Overlay rojo modal      | **Sí** | No         | **Modo degradado**: app operable con banner rojo permanente no-cerrable; reintenta cada arranque |

- El **texto** de banner/modal/overlay proviene de las **release notes markdown** de la release
  candidata (se renderiza como texto plano o markdown simple).
- **Modo degradado:** ocurre solo en `critical` cuando la descarga **no** pudo completarse (ej:
  sin red, GitHub caído, pkexec rechazado en Linux). La app deja usar las funciones, pero con un
  banner rojo permanente no-cerrable y reintento en cada arranque. Es un estado de **resiliencia**:
  no dejamos al usuario sin app, pero le dejamos clarísimo que está en riesgo.

### Convención de `severity` en release notes (decisión de diseño 4, ver abajo)

El metadato `severity` **no** viaja en el `releases.json` estándar de Velopack. Para no inventar
infraestructura, se acuerda un **front-matter mínimo de una línea** al tope de las release notes:

```
severity: critical

## Qué cambió
- Fix urgente de cálculo de stock...
```

El parser lee la primera línea `severity: <valor>`; si está ausente o es inválida → default
`normal`. Es cero-infra: viaja con el feed de Velopack **y** con el fallback GitHub, en un solo
lugar.

### Empaquetado por OS

**Windows (`build/pack-win.ps1`):**
```powershell
dotnet publish src/StockApp.Presentation -c Release -r win-x64 --self-contained `
  -o publish/win
vpk pack `
  --packId StockApp `
  --packVersion <SemVer leída del .csproj> `
  --packDir publish/win `
  --mainExe StockApp.Presentation.exe `
  --channel win `
  --delta BestSpeed `
  --releaseNotes RELEASE_NOTES.md `
  --output releases/win
  # --signParams "/a /f cert.pfx /p $env:CERT_PWD"   # D7: enchufar cuando haya certificado
```
Produce `releases/win/Setup.exe` + `releases.win.json` + paquetes full/delta.

**Linux (`build/pack-linux.sh`):**
```bash
dotnet publish src/StockApp.Presentation -c Release -r linux-x64 --self-contained \
  -o publish/linux
vpk pack \
  --packId StockApp \
  --packVersion <SemVer leída del .csproj> \
  --packDir publish/linux \
  --mainExe StockApp.Presentation \
  --channel linux \
  --delta BestSpeed \
  --releaseNotes RELEASE_NOTES.md \
  --output releases/linux
```
Produce un `AppImage` portable + `releases.linux.json`. **Recomendar instalar en `$HOME`** para
evitar el prompt de `pkexec` en directorios protegidos.

**Publicación:** los assets de `releases/<channel>` se suben como **GitHub Release** (fuente
primaria real). Cuando exista el feed propio, **los mismos assets** se publican también ahí.

---

## Diseño técnico

> Nota de realidad del repo: el `.csproj` de Presentation hoy referencia **Avalonia 12.0.4** y
> **Avalonia.Controls.DataGrid 12.0.0** (no 11). El diseño usa la versión real instalada. El DI vive
> en `App.axaml.cs → ConfigurarServicios()` (no hay `DependencyInjection.cs` separado en
> Presentation). El shell raíz es `ShellViewModel` (singleton); el shell de navegación post-login es
> `ShellMainViewModel` (con `EsAdmin` e `INavigationService`). El guardado de archivos ya existe como
> `IServicioGuardadoArchivo` (Inc 6). Estos nombres reales se respetan abajo.

### Estructura de carpetas / archivos nuevos por capa

```
src/StockApp.Application/
  Actualizaciones/
    IUpdateService.cs            ← contrato (Application no conoce Velopack)
    UpdateSeverity.cs            ← enum
    Dtos.cs                      ← UpdateCheckResult, UpdateProgress, AccionUx, ModoUx
    SeverityParser.cs            ← string notas → UpdateSeverity (puro, testeable)
    PoliticaUxActualizacion.cs   ← (Severity, estado) → AccionUx (puro, testeable)

src/StockApp.Infrastructure/
  Actualizaciones/
    IVelopackGateway.cs          ← interfaz fina sobre UpdateManager (mockeable)
    VelopackGatewayReal.cs       ← adapta UpdateManager real
    VelopackUpdateService.cs     ← IUpdateService; mapea gateway → DTOs propios
    FallbackUpdateSource.cs      ← IUpdateSource encadenado [primaria, secundaria]
    UpdaterOptions.cs            ← config: URLs, orden de fuentes, packId, channel

src/StockApp.Presentation/
  Actualizaciones/
    ActualizacionBannerViewModel.cs    ← modo normal
    ActualizacionModalViewModel.cs     ← modo important (posponible)
    ActualizacionBloqueoViewModel.cs   ← modo critical + modo degradado
    Views/
      ActualizacionBannerView.axaml
      ActualizacionModalView.axaml
      ActualizacionBloqueoView.axaml
    CoordinadorActualizacion.cs        ← orquesta chequeo→política→UI en background
  Program.cs                            ← (mod) VelopackApp.Build().Run() primera línea
  App.axaml.cs                          ← (mod) registro DI del actualizador
  ViewModels/ShellViewModel.cs          ← (mod) dispara CoordinadorActualizacion al arrancar

build/
  pack-win.ps1
  pack-linux.sh
  RELEASE_NOTES.md.template
  README-empaquetado.md
```

### Contratos de interfaz (Application)

```csharp
// Application/Actualizaciones/UpdateSeverity.cs
namespace StockApp.Application.Actualizaciones;

public enum UpdateSeverity
{
    Normal,
    Important,
    Critical
}

// Application/Actualizaciones/IUpdateService.cs
namespace StockApp.Application.Actualizaciones;

/// <summary>
/// Contrato del actualizador. Application NO conoce Velopack: devuelve DTOs propios.
/// La implementación (Infrastructure) adapta UpdateManager detrás de IVelopackGateway.
/// </summary>
public interface IUpdateService
{
    /// <summary>Consulta la fuente encadenada. Null-safe: si no hay update, HayUpdate=false.</summary>
    Task<UpdateCheckResult> BuscarAsync(CancellationToken ct = default);

    /// <summary>Descarga el delta del update encontrado. Reporta progreso 0..100.</summary>
    Task DescargarAsync(IProgress<UpdateProgress>? progreso = null, CancellationToken ct = default);

    /// <summary>Aplica el update descargado y reinicia (Windows). No retorna en éxito.</summary>
    void AplicarYReiniciar();
}
```

```csharp
// Infrastructure/Actualizaciones/IVelopackGateway.cs
namespace StockApp.Infrastructure.Actualizaciones;

using Velopack;

/// <summary>
/// Interfaz fina sobre UpdateManager (clase concreta difícil de mockear). Permite que la
/// LÓGICA de VelopackUpdateService (mapeo a DTOs, parseo severity, decisión UX) sea testeable
/// con un doble de este gateway, sin tocar Velopack real.
/// </summary>
public interface IVelopackGateway
{
    /// <returns>UpdateInfo? — null si ya está al día. Lanza NotInstalledException en dev.</returns>
    Task<UpdateInfo?> CheckForUpdatesAsync();

    Task DownloadUpdatesAsync(UpdateInfo info, Action<int>? progreso = null);

    /// <summary>Windows: ApplyUpdatesAndRestart (no retorna). Linux: aplica para próxima ejecución.</summary>
    void ApplyUpdatesAndRestart(UpdateInfo info);
}
```

### DTOs (records)

```csharp
// Application/Actualizaciones/Dtos.cs
namespace StockApp.Application.Actualizaciones;

/// <summary>Resultado del chequeo de updates, sin dependencia de Velopack.</summary>
public record UpdateCheckResult(
    bool HayUpdate,
    string? Version,
    UpdateSeverity Severity,
    string? NotasMarkdown)
{
    public static UpdateCheckResult SinUpdate { get; } =
        new(false, null, UpdateSeverity.Normal, null);
}

/// <summary>Progreso de descarga, 0..100.</summary>
public record UpdateProgress(int Porcentaje);

/// <summary>Modo de presentación que la política decide según severity + estado.</summary>
public enum ModoUx
{
    Ninguno,        // no hay update o corriendo en dev
    BannerDiscreto, // normal
    ModalPosponible,// important
    BloqueoCritico, // critical, descarga posible
    ModoDegradado   // critical, descarga falló → app usable con banner rojo no-cerrable
}

/// <summary>Acción de UI resultante de la política (severity + estado de descarga).</summary>
public record AccionUx(
    ModoUx Modo,
    string? TextoMarkdown,
    bool Posponible,
    bool ReintentaEnArranque);
```

### Infrastructure: VelopackUpdateService + FallbackUpdateSource + parser severity

```csharp
// Infrastructure/Actualizaciones/VelopackUpdateService.cs
namespace StockApp.Infrastructure.Actualizaciones;

using StockApp.Application.Actualizaciones;
using Velopack;

public class VelopackUpdateService : IUpdateService
{
    private readonly IVelopackGateway _gateway;
    private readonly SeverityParser   _severityParser;
    private UpdateInfo?               _pendiente; // cacheado entre Buscar y Descargar

    public VelopackUpdateService(IVelopackGateway gateway, SeverityParser severityParser)
    {
        _gateway        = gateway;
        _severityParser = severityParser;
    }

    public async Task<UpdateCheckResult> BuscarAsync(CancellationToken ct = default)
    {
        try
        {
            var info = await _gateway.CheckForUpdatesAsync();
            if (info is null)
                return UpdateCheckResult.SinUpdate;

            _pendiente = info;

            // GOTCHA: NotesMarkdown puede NO estar disponible pre-descarga (ver Riesgos).
            // Si está vacío, el parser cae a default Normal — mitigación: manifiesto severities.json.
            var notas    = info.TargetFullRelease?.NotesMarkdown;
            var severity = _severityParser.Parse(notas);

            return new UpdateCheckResult(
                HayUpdate:     true,
                Version:       info.TargetFullRelease?.Version?.ToString(),
                Severity:      severity,
                NotasMarkdown: notas);
        }
        catch (NotInstalledException)
        {
            // Corriendo en dev / no es install Velopack → saltear updates por completo.
            return UpdateCheckResult.SinUpdate;
        }
    }

    public async Task DescargarAsync(IProgress<UpdateProgress>? progreso = null,
                                     CancellationToken ct = default)
    {
        if (_pendiente is null) return;
        await _gateway.DownloadUpdatesAsync(
            _pendiente,
            p => progreso?.Report(new UpdateProgress(p)));
    }

    public void AplicarYReiniciar()
    {
        if (_pendiente is null) return;
        _gateway.ApplyUpdatesAndRestart(_pendiente); // no retorna en Windows
    }
}
```

```csharp
// Application/Actualizaciones/SeverityParser.cs  (vive en Application: lógica pura, sin Velopack)
namespace StockApp.Application.Actualizaciones;

/// <summary>Parsea la primera línea `severity: <valor>` de las release notes markdown.
/// Default Normal si ausente o inválido. Cero dependencia de infraestructura.</summary>
public class SeverityParser
{
    public UpdateSeverity Parse(string? notasMarkdown)
    {
        if (string.IsNullOrWhiteSpace(notasMarkdown))
            return UpdateSeverity.Normal;

        var primera = notasMarkdown
            .Split('\n')[0]
            .Trim();

        if (!primera.StartsWith("severity:", StringComparison.OrdinalIgnoreCase))
            return UpdateSeverity.Normal;

        var valor = primera["severity:".Length..].Trim();
        return valor.ToLowerInvariant() switch
        {
            "critical"  => UpdateSeverity.Critical,
            "important" => UpdateSeverity.Important,
            _           => UpdateSeverity.Normal,
        };
    }
}
```

```csharp
// Application/Actualizaciones/PoliticaUxActualizacion.cs  (pura, testeable)
namespace StockApp.Application.Actualizaciones;

/// <summary>Dada una severity + si la descarga fue posible, decide la acción de UI.
/// Sin dependencias de UI ni Velopack → 100% unit-testeable.</summary>
public class PoliticaUxActualizacion
{
    public AccionUx Decidir(UpdateCheckResult resultado, bool descargaPosible)
    {
        if (!resultado.HayUpdate)
            return new AccionUx(ModoUx.Ninguno, null, false, false);

        return resultado.Severity switch
        {
            UpdateSeverity.Normal => new AccionUx(
                ModoUx.BannerDiscreto, resultado.NotasMarkdown,
                Posponible: true,  ReintentaEnArranque: true),

            UpdateSeverity.Important => new AccionUx(
                ModoUx.ModalPosponible, resultado.NotasMarkdown,
                Posponible: true,  ReintentaEnArranque: true),

            UpdateSeverity.Critical when descargaPosible => new AccionUx(
                ModoUx.BloqueoCritico, resultado.NotasMarkdown,
                Posponible: false, ReintentaEnArranque: true),

            UpdateSeverity.Critical => new AccionUx(   // critical pero no se pudo bajar
                ModoUx.ModoDegradado, resultado.NotasMarkdown,
                Posponible: false, ReintentaEnArranque: true),

            _ => new AccionUx(ModoUx.Ninguno, null, false, false),
        };
    }
}
```

```csharp
// Infrastructure/Actualizaciones/FallbackUpdateSource.cs
namespace StockApp.Infrastructure.Actualizaciones;

using Velopack.Sources;

/// <summary>
/// Fuente encadenada: recibe una lista ORDENADA [primaria, secundaria] desde configuración
/// (D5: hoy efectivamente [GitHub]; mañana [FeedPropio, GitHub] sin tocar código).
/// Intenta cada fuente en orden; ante excepción/timeout cae a la siguiente.
/// NOTA: Velopack no provee ChainedSource de fábrica → se implementa IUpdateSource a mano,
/// envolviendo los métodos (GetReleaseFeed / DownloadFile) con try/catch en cascada.
/// </summary>
public class FallbackUpdateSource : IUpdateSource
{
    private readonly IReadOnlyList<IUpdateSource> _fuentes; // orden = prioridad

    public FallbackUpdateSource(IReadOnlyList<IUpdateSource> fuentesOrdenadas)
    {
        if (fuentesOrdenadas.Count == 0)
            throw new ArgumentException("Se requiere al menos una fuente.");
        _fuentes = fuentesOrdenadas;
    }

    // Firma ilustrativa — alinear con la API real de IUpdateSource al implementar.
    public async Task<VelopackAssetFeed> GetReleaseFeed(/* params reales de Velopack */)
    {
        Exception? ultima = null;
        foreach (var fuente in _fuentes)
        {
            try   { return await fuente.GetReleaseFeed(/* ... */); }
            catch (Exception ex) { ultima = ex; /* log + siguiente */ }
        }
        throw new AggregateException("Todas las fuentes de update fallaron.", ultima!);
    }

    public async Task DownloadReleaseEntry(/* params reales */)
    {
        Exception? ultima = null;
        foreach (var fuente in _fuentes)
        {
            try   { await fuente.DownloadReleaseEntry(/* ... */); return; }
            catch (Exception ex) { ultima = ex; }
        }
        throw new AggregateException("Descarga falló en todas las fuentes.", ultima!);
    }
}
```

```csharp
// Infrastructure/Actualizaciones/UpdaterOptions.cs
namespace StockApp.Infrastructure.Actualizaciones;

/// <summary>Configuración del actualizador. El ORDEN de fuentes sale de acá (D5),
/// no hardcodeado. FeedPropioUrl vacío ⇒ se omite y queda solo GitHub.</summary>
public class UpdaterOptions
{
    public string  PackId        { get; init; } = "StockApp";
    public string  Channel       { get; init; } = OperatingSystem.IsWindows() ? "win" : "linux";
    public string? FeedPropioUrl { get; init; } = null;                 // aún inexistente (D5)
    public string  GithubRepoUrl { get; init; } = "https://github.com/<owner>/<repo>";
    public bool    IncluirPrerelease { get; init; } = false;
}
```

### Presentation: ViewModels/Views por severidad + integración en shell + Program.cs

**`Program.cs` (mod):** `VelopackApp.Build().Run()` debe ser la **primera** línea de `Main`, antes
de construir Avalonia.

```csharp
[STAThread]
public static void Main(string[] args)
{
    // OBLIGATORIO Velopack: primera línea, antes de cualquier API de Avalonia.
    Velopack.VelopackApp.Build().Run();

    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
```

**`CoordinadorActualizacion`:** orquesta el flujo en background. Se invoca desde
`ShellViewModel.InicializarAsync()` (que ya existe y se llama antes de mostrar la ventana). No
bloquea el arranque salvo `critical`.

```csharp
// Presentation/Actualizaciones/CoordinadorActualizacion.cs (ilustrativo)
public class CoordinadorActualizacion
{
    private readonly IUpdateService          _update;
    private readonly PoliticaUxActualizacion _politica;
    // + servicio de diálogos/overlay del shell

    public async Task EvaluarEnArranqueAsync()
    {
        var resultado = await _update.BuscarAsync();
        if (!resultado.HayUpdate) return;

        // Para critical intentamos descargar primero para saber si entramos en modo degradado.
        bool descargaOk = true;
        if (resultado.Severity == UpdateSeverity.Critical)
        {
            try   { await _update.DescargarAsync(); }
            catch { descargaOk = false; }
        }

        var accion = _politica.Decidir(resultado, descargaOk);
        MostrarSegunModo(accion); // banner / modal / overlay bloqueante / degradado
    }
}
```

- **`ActualizacionBannerViewModel` / View:** banner no-modal en `normal`. Botón "Actualizar al
  reiniciar".
- **`ActualizacionModalViewModel` / View:** modal posponible en `important`. Botón "Posponer"
  (reaparece próximo arranque) + "Actualizar ahora".
- **`ActualizacionBloqueoViewModel` / View:** overlay rojo modal en `critical`. Sin "Posponer". En
  **modo degradado**: banner rojo permanente no-cerrable, app usable detrás.
- El texto de los tres sale de `AccionUx.TextoMarkdown` (release notes).

**DI (`App.axaml.cs → ConfigurarServicios`, mod):** registrar bajo un bloque "Inc 7 Fase A:
actualizador":

```csharp
// ── Inc 7 Fase A: actualizador ────────────────────────────────────────
services.AddSingleton(new UpdaterOptions { /* leído de config */ });
services.AddSingleton<SeverityParser>();
services.AddSingleton<PoliticaUxActualizacion>();
services.AddSingleton<IVelopackGateway, VelopackGatewayReal>();
services.AddSingleton<IUpdateService, VelopackUpdateService>();
services.AddSingleton<CoordinadorActualizacion>();
services.AddTransient<ActualizacionBannerViewModel>();
services.AddTransient<ActualizacionModalViewModel>();
services.AddTransient<ActualizacionBloqueoViewModel>();
```

`ShellViewModel.InicializarAsync()` (mod): tras inicializar el shell, dispara
`CoordinadorActualizacion.EvaluarEnArranqueAsync()` en background (fire-and-forget controlado, con
manejo de excepciones para no tumbar el arranque).

### Empaquetado: scripts por OS

Carpeta `build/` versionada (D6). Cada script: lee la versión del `.csproj`, hace `dotnet publish`
self-contained y `vpk pack`. Snippets completos en la sección **Especificación → Empaquetado por
OS**. `RELEASE_NOTES.md.template` documenta el front-matter `severity:`. `README-empaquetado.md`
documenta el flujo manual paso a paso (instalar `vpk` con `dotnet tool install -g vpk`, ejecutar el
script del OS, subir los assets a GitHub Release).

### Tus decisiones de diseño (1–8) con justificación

> Estas son **decisiones de arquitectura del diseñador**, distintas de las **decisiones firmes del
> usuario** (D1–D7 arriba). Si alguna entra en conflicto con la realidad de la API de Velopack al
> implementar, se ajusta y se documenta — sin cambiar las decisiones firmes.

1. **Abstracción `IUpdateService` en Application con DTOs propios.** Application define el contrato y
   los records (`UpdateCheckResult`, `UpdateProgress`, `AccionUx`, `UpdateSeverity`). **No** referencia
   Velopack. *Justificación:* respeta la regla de dependencias de Clean Architecture (Application no
   depende de Infrastructure ni de librerías de UI/distribución); permite cambiar Velopack por otra
   solución sin tocar Application/Presentation.

2. **`IVelopackGateway` fino entre el servicio y `UpdateManager`.** `UpdateManager` es una clase
   concreta difícil de mockear. La envolvemos en una interfaz fina que Infrastructure adapta
   (`VelopackGatewayReal`). *Justificación:* la **lógica** de `VelopackUpdateService` (mapeo a DTOs,
   parseo de severity, manejo de `NotInstalledException`) queda **unit-testeable** con un doble del
   gateway, sin levantar Velopack real ni red.

3. **`FallbackUpdateSource : IUpdateSource` con orden por configuración.** Lista ordenada
   `[primaria, secundaria]`; try/catch en cascada en `GetReleaseFeed` y descarga. El orden viene de
   `UpdaterOptions` (D5), no hardcodeado. *Justificación:* soporta el reordenamiento feed-propio↔GitHub
   por config sin tocar código; testeable (primaria falla → se usa secundaria).

4. **Severity vía front-matter mínimo en release notes, con fallback a `severities.json`.**
   Convención `severity: <valor>` en la primera línea de las notes; default `normal` si ausente.
   *Justificación:* cero infraestructura extra, viaja con el feed de Velopack **y** con GitHub, un
   solo lugar. *Riesgo:* depende de que `NotesMarkdown` esté disponible **pre-descarga** (necesario
   para decidir la UX `critical` antes de bajar nada). *Mitigación:* si no lo está, fallback a un
   manifiesto propio `severities.json` hosteado junto a los assets. El parser (`SeverityParser`) es un
   componente testeable aislado.

5. **Política de UX severity→acción como pieza pura testeable (`PoliticaUxActualizacion`).** Vive en
   Application; dado `(UpdateSeverity, descargaPosible)` devuelve `AccionUx`. *Justificación:* el mapeo
   severity→acción (incluido el caso modo degradado) se testea sin UI; Presentation solo **renderiza**
   la `AccionUx`.

6. **Presentation: 3 ViewModels/Views por modo + chequeo en background al arranque enganchado en
   `ShellViewModel`.** `VelopackApp.Build().Run()` primera línea de `Program.Main`. *Justificación:*
   coherente con el patrón MVVM del repo; el shell ya tiene `InicializarAsync()` como punto de enganche
   natural; el background evita bloquear el render salvo `critical`.

7. **`InformationalVersion` del `.csproj` de Presentation como single source of truth.** Pasada a
   `vpk pack --packVersion`; expuesta en "Acerca de"; leída en runtime del assembly. *Justificación:*
   no hay getter público oficial de versión en Velopack; una sola fuente evita divergencia
   versión-empaquetada vs versión-runtime.

8. **Empaquetado por scripts locales versionados en `build/`.** `dotnet publish` self-contained +
   `vpk pack` por OS, `--signParams` comentado (D7). *Justificación:* sin CI/CD en esta fase (D6); los
   scripts versionados documentan y reproducen el proceso manual de forma consistente.

---

## Testing

### Unit tests (xUnit + Moq)

| Módulo | Tests |
|--------|-------|
| `SeverityParser` | `severity: critical` → Critical; `severity: important` → Important; `severity: normal` → Normal; ausente → Normal (default); valor inválido → Normal; mayúsculas/espacios tolerados (`Severity:  CRITICAL `); notas null/vacías → Normal; severity NO en primera línea → Normal. |
| `PoliticaUxActualizacion` | sin update → `ModoUx.Ninguno`; normal → BannerDiscreto + posponible; important → ModalPosponible + posponible; critical con descarga OK → BloqueoCritico + no-posponible; critical con descarga fallida → ModoDegradado + no-posponible; todos los modos con update marcan `ReintentaEnArranque`. |
| `FallbackUpdateSource` | primaria OK → no consulta secundaria; primaria lanza → usa secundaria; ambas fallan → `AggregateException`; respeta el orden recibido; una sola fuente y falla → propaga. |
| `VelopackUpdateService` (con `IVelopackGateway` mockeado) | `CheckForUpdatesAsync` null → `UpdateCheckResult.SinUpdate`; gateway lanza `NotInstalledException` → SinUpdate (se traga); update presente → mapea Version/Severity/Notas a DTO; severity se deriva del parser sobre `NotesMarkdown`; `DescargarAsync` sin pendiente → no-op; `AplicarYReiniciar` sin pendiente → no-op; `DescargarAsync` reporta progreso vía `IProgress`. |
| Mapeo a DTOs | `UpdateInfo` real → `UpdateCheckResult` con campos correctos (Version string, Severity parseada, NotasMarkdown pasante). |

### Validación manual (no automatizable)

| Caso | Validación |
|------|------------|
| `pack-win.ps1` en Windows limpio | Genera `Setup.exe`; instala en máquina sin .NET; app arranca. SmartScreen "editor desconocido" aparece pero instala (D7). |
| `pack-linux.sh` en Linux limpio | Genera `AppImage`; ejecutable en `$HOME`; app arranca sin .NET instalado. |
| Update end-to-end Windows | Publicar v1 → instalar → publicar v2 (delta) en GitHub Release → reabrir app → chequeo detecta v2 → `ApplyUpdatesAndRestart` aplica y reinicia en v2. |
| Update end-to-end Linux | Idem, validando que el AppImage toma efecto en la **próxima ejecución** (no in-place). Probar en `$HOME` (sin pkexec) y verificar comportamiento si está en dir protegido. |
| Severity UX por modo | Releases con `severity: normal/important/critical` → banner / modal / overlay rojo respectivamente; critical sin red → modo degradado. |
| Fallback de fuente | Con feed propio configurado y caído → cae a GitHub (cuando exista el feed; hoy solo rama GitHub). |
| Corrida en dev | `dotnet run` → `NotInstalledException` tragada → app funciona sin tocar updates. |

---

## Riesgos / Mitigaciones

| Riesgo | Probabilidad | Mitigación |
|--------|--------------|------------|
| `NotesMarkdown` no disponible **pre-descarga** → no se puede decidir UX `critical` antes de bajar | **Media-Alta** | Validar en implementación si `TargetFullRelease.NotesMarkdown` llega en el feed pre-descarga. Si no → fallback a manifiesto propio `severities.json` hosteado junto a los assets, consultado por el servicio antes de descargar. Parser aislado y testeable. |
| Linux: `pkexec` rechazado en directorio protegido → update falla | Media | Recomendar/documentar instalación en `$HOME`; si falla y severity critical → modo degradado (app usable + banner rojo, reintenta). |
| `NotInstalledException` en dev tumba el arranque | Alta si no se maneja | `try/catch` específico en `VelopackUpdateService.BuscarAsync` que la traga y devuelve `SinUpdate`. Test explícito con gateway mockeado. |
| Feed propio aún inexistente | Cierta (hoy) | D5: GitHub es primaria real; `FeedPropioUrl` vacío se omite. Diseño encadenado completo, solo se ejercita rama GitHub en tests. |
| Sin code signing → SmartScreen "editor desconocido" | Cierta (D7) | Aceptado para B2B directo; instalación asistida. `--signParams` comentado listo para enchufar certificado. Documentar al cliente que la advertencia es esperada. |
| `IUpdateSource` real de Velopack tiene firma distinta a la ilustrada | Media | Las firmas de `FallbackUpdateSource` son ilustrativas; alinear con la API real al implementar (validar nombres `GetReleaseFeed`/`DownloadReleaseEntry` y tipos `VelopackAssetFeed`). |
| `ApplyUpdatesAndRestart` no retorna → estado de UI a medias en Windows | Baja | Llamar solo tras confirmar descarga OK; no ejecutar lógica después de la llamada. |
| Versión `.csproj` y `--packVersion` divergen | Media | Script lee la versión del `.csproj` (no hardcode); decisión de diseño 7 (single source of truth). |
| Avalonia/DataGrid 12.x vs spec que menciona Avalonia 11 | Baja | El repo ya está en 12.0.4; el diseño usa la versión real instalada. Sin acción adicional. |

---

## Criterios de aceptación de alto nivel

- [ ] `dotnet build StockApp.sln` y `dotnet test` pasan con 0 errores y sin regresiones de Inc 1–6.
- [ ] `IUpdateService` vive en Application sin referencia alguna a Velopack (verificable: el `.csproj`
      de Application no referencia el paquete `Velopack`).
- [ ] `SeverityParser` mapea correctamente las 3 severidades + default `normal` ante ausencia/inválido.
- [ ] `PoliticaUxActualizacion` cubre los 5 modos (Ninguno, BannerDiscreto, ModalPosponible,
      BloqueoCritico, ModoDegradado) con tests.
- [ ] `FallbackUpdateSource` cae de primaria a secundaria ante fallo, respetando el orden de config.
- [ ] `VelopackUpdateService` traga `NotInstalledException` y devuelve `SinUpdate` (corrida en dev no
      rompe).
- [ ] `VelopackApp.Build().Run()` es la primera línea de `Program.Main`.
- [ ] El chequeo se dispara en background al arranque desde `ShellViewModel`, sin bloquear el render
      (salvo `critical`).
- [ ] Los 3 ViewModels/Views de actualización renderizan el texto desde las release notes.
- [ ] `build/pack-win.ps1` y `build/pack-linux.sh` existen, versionados, con `--signParams` comentado,
      y producen `Setup.exe` / `AppImage` self-contained (validación manual por OS).
- [ ] La versión sale del `.csproj` de Presentation y se expone en "Acerca de".

---

## Tareas

> Nota metodológica: **TDD estricto** (test rojo → impl mínima → verde → commit), **un commit por
> tarea**, convención `feat(scope)` / `test(scope)` / `fix` / `docs` / `refactor`. Las tareas del
> Bloque D (packaging) son **manuales** (no TDD): se validan por OS.
> Orden respetando dependencia de capas: Application → Infrastructure → Presentation → Empaquetado.

### Bloque A — Application (contratos + DTOs + parser + política)

#### A1 — Application: enum + DTOs + contrato

**Archivos:** `Application/Actualizaciones/UpdateSeverity.cs`, `Application/Actualizaciones/Dtos.cs`,
`Application/Actualizaciones/IUpdateService.cs` — **Dep:** ninguna

- [ ] A1.1 Crear `UpdateSeverity` (enum `Normal | Important | Critical`)
- [ ] A1.2 Crear `Dtos.cs` con records `UpdateCheckResult` (+ `SinUpdate` estático), `UpdateProgress`,
      `AccionUx`, y enum `ModoUx`
- [ ] A1.3 Crear `IUpdateService` (`BuscarAsync`, `DescargarAsync`, `AplicarYReiniciar`)
- [ ] A1.4 `dotnet build StockApp.sln` → commit: `feat(app): contrato IUpdateService + DTOs de actualización como records`

#### A2 — Application: SeverityParser (TDD)

**Archivos:** `Application/Actualizaciones/SeverityParser.cs`,
`tests/StockApp.Application.Tests/Actualizaciones/SeverityParserTests.cs` — **Dep:** A1

- [ ] A2.1 Escribir 8 tests que fallan: `Parse_Critical`, `Parse_Important`, `Parse_Normal`,
      `Parse_Ausente_DefaultNormal`, `Parse_Invalido_DefaultNormal`, `Parse_MayusculasYEspacios_Tolerado`,
      `Parse_NotasNullOVacias_Normal`, `Parse_SeverityNoEnPrimeraLinea_Normal`
- [ ] A2.2 Implementar `SeverityParser.Parse` (primera línea `severity:`, default Normal,
      `OrdinalIgnoreCase`)
- [ ] A2.3 `dotnet test .../Actualizaciones` verde → commit: `feat(app): SeverityParser TDD — front-matter severity con default normal`

#### A3 — Application: PoliticaUxActualizacion (TDD)

**Archivos:** `Application/Actualizaciones/PoliticaUxActualizacion.cs`,
`tests/StockApp.Application.Tests/Actualizaciones/PoliticaUxActualizacionTests.cs` — **Dep:** A1

- [ ] A3.1 Escribir 6 tests: `Decidir_SinUpdate_Ninguno`, `Decidir_Normal_BannerDiscreto`,
      `Decidir_Important_ModalPosponible`, `Decidir_CriticalDescargaOk_BloqueoCritico`,
      `Decidir_CriticalDescargaFalla_ModoDegradado`, `Decidir_ConUpdate_MarcaReintentaEnArranque`
- [ ] A3.2 Implementar `PoliticaUxActualizacion.Decidir` (switch por severity + `descargaPosible`)
- [ ] A3.3 `dotnet test .../Actualizaciones` verde → commit: `feat(app): PoliticaUxActualizacion TDD — mapeo severity→acción UX`

---

### Bloque B — Infrastructure (gateway + servicio + fallback source + config + DI)

#### B1 — Infrastructure: paquete Velopack + IVelopackGateway + VelopackGatewayReal

**Archivos:** `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj` (mod),
`Infrastructure/Actualizaciones/IVelopackGateway.cs`,
`Infrastructure/Actualizaciones/VelopackGatewayReal.cs` — **Dep:** A1

- [ ] B1.1 Agregar `PackageReference Include="Velopack"` al `.csproj` de Infrastructure
- [ ] B1.2 Definir `IVelopackGateway` (`CheckForUpdatesAsync`, `DownloadUpdatesAsync`,
      `ApplyUpdatesAndRestart`)
- [ ] B1.3 Implementar `VelopackGatewayReal` adaptando `UpdateManager` (sin lógica de negocio: solo
      passthrough)
- [ ] B1.4 `dotnet build StockApp.sln` → commit: `feat(infra): IVelopackGateway + VelopackGatewayReal sobre UpdateManager`

#### B2 — Infrastructure: VelopackUpdateService (TDD con gateway mockeado)

**Archivos:** `Infrastructure/Actualizaciones/VelopackUpdateService.cs`,
`tests/StockApp.Infrastructure.Tests/Actualizaciones/VelopackUpdateServiceTests.cs` — **Dep:** B1, A2

- [ ] B2.1 Escribir 7 tests con `Mock<IVelopackGateway>`: `BuscarAsync_Null_SinUpdate`,
      `BuscarAsync_NotInstalledException_SeTraga_SinUpdate`, `BuscarAsync_ConUpdate_MapeaDto`,
      `BuscarAsync_DerivaSeverityDelParser`, `DescargarAsync_SinPendiente_NoOp`,
      `DescargarAsync_ReportaProgreso`, `AplicarYReiniciar_SinPendiente_NoOp`
- [ ] B2.2 Implementar `VelopackUpdateService` (cachea `_pendiente`, traga `NotInstalledException`,
      mapea a `UpdateCheckResult`, usa `SeverityParser`)
- [ ] B2.3 `dotnet test .../Actualizaciones` verde → commit: `feat(infra): VelopackUpdateService TDD — adapta gateway a DTOs + maneja NotInstalled`

#### B3 — Infrastructure: FallbackUpdateSource (TDD)

**Archivos:** `Infrastructure/Actualizaciones/FallbackUpdateSource.cs`,
`tests/StockApp.Infrastructure.Tests/Actualizaciones/FallbackUpdateSourceTests.cs` — **Dep:** B1

- [ ] B3.1 Escribir 5 tests: `PrimariaOk_NoConsultaSecundaria`, `PrimariaFalla_UsaSecundaria`,
      `AmbasFallan_AggregateException`, `RespetaOrdenRecibido`, `UnaSolaFuenteFalla_Propaga`
- [ ] B3.2 Implementar `FallbackUpdateSource` (lista ordenada, try/catch en cascada en feed y
      descarga). **Alinear firmas con la API real de `IUpdateSource` de Velopack** al implementar
- [ ] B3.3 `dotnet test .../Actualizaciones` verde → commit: `feat(infra): FallbackUpdateSource encadenado TDD — primaria→secundaria por config`

#### B4 — Infrastructure: UpdaterOptions + DI

**Archivos:** `Infrastructure/Actualizaciones/UpdaterOptions.cs`,
`src/StockApp.Presentation/App.axaml.cs` (mod, bloque DI) — **Dep:** B2, B3, A3

- [ ] B4.1 Crear `UpdaterOptions` (PackId, Channel, FeedPropioUrl null, GithubRepoUrl, IncluirPrerelease)
- [ ] B4.2 Registrar en `ConfigurarServicios`: `UpdaterOptions`, `SeverityParser`,
      `PoliticaUxActualizacion`, `IVelopackGateway`→`VelopackGatewayReal`,
      `IUpdateService`→`VelopackUpdateService` (bloque "Inc 7 Fase A: actualizador")
- [ ] B4.3 `dotnet build StockApp.sln` → commit: `feat(infra): UpdaterOptions + registro DI del actualizador`

---

### Bloque C — Presentation (Program.cs + coordinador + ViewModels/Views + integración shell)

#### C1 — Presentation: Velopack init en Program.cs + paquete

**Archivos:** `src/StockApp.Presentation/StockApp.Presentation.csproj` (mod),
`src/StockApp.Presentation/Program.cs` (mod) — **Dep:** B4

- [ ] C1.1 Agregar `PackageReference Include="Velopack"` al `.csproj` de Presentation (proyecto con
      `Main`)
- [ ] C1.2 Agregar `VelopackApp.Build().Run()` como **primera línea** de `Program.Main` (antes de
      `BuildAvaloniaApp`)
- [ ] C1.3 `dotnet build StockApp.sln` → commit: `feat(presentation): VelopackApp.Build().Run() como primera línea de Main`

#### C2 — Presentation: CoordinadorActualizacion (TDD)

**Archivos:** `Presentation/Actualizaciones/CoordinadorActualizacion.cs`,
`tests/StockApp.Presentation.Tests/Actualizaciones/CoordinadorActualizacionTests.cs` — **Dep:** C1

- [ ] C2.1 Escribir 4 tests con `Mock<IUpdateService>` + `PoliticaUxActualizacion` real:
      `Evaluar_SinUpdate_NoMuestraNada`, `Evaluar_Normal_PideBanner`,
      `Evaluar_CriticalDescargaOk_PideBloqueo`, `Evaluar_CriticalDescargaFalla_PideModoDegradado`
- [ ] C2.2 Implementar `CoordinadorActualizacion.EvaluarEnArranqueAsync` (buscar → para critical
      intenta descargar → política → expone la `AccionUx` resultante de forma observable/testeable)
- [ ] C2.3 `dotnet test .../Actualizaciones` verde → commit: `feat(presentation): CoordinadorActualizacion TDD — orquesta chequeo→política en arranque`

#### C3 — Presentation: ViewModels/Views por modo

**Archivos:** `Presentation/Actualizaciones/ActualizacionBannerViewModel.cs`,
`ActualizacionModalViewModel.cs`, `ActualizacionBloqueoViewModel.cs`,
`Presentation/Actualizaciones/Views/ActualizacionBannerView.axaml`, `ActualizacionModalView.axaml`,
`ActualizacionBloqueoView.axaml` — **Dep:** C2

- [ ] C3.1 Escribir tests de los VMs (texto desde `AccionUx.TextoMarkdown`; modal expone "Posponer"
      cuando `Posponible`; bloqueo NO expone "Posponer"; modo degradado → banner no-cerrable)
- [ ] C3.2 Implementar los 3 ViewModels con `[ObservableProperty]` (TextoMarkdown, EsPosponible) y
      `[RelayCommand]` (ActualizarAhora / Posponer / al-reiniciar)
- [ ] C3.3 Crear las 3 Views (.axaml): banner discreto, modal, overlay rojo bloqueante
- [ ] C3.4 `dotnet test` verde → commit: `feat(presentation): ViewModels y Views de actualización por severidad`

#### C4 — Presentation: integración en ShellViewModel + DI de VMs

**Archivos:** `src/StockApp.Presentation/ViewModels/ShellViewModel.cs` (mod),
`src/StockApp.Presentation/App.axaml.cs` (mod) — **Dep:** C2, C3

- [ ] C4.1 Escribir test: `ShellViewModel.InicializarAsync` dispara
      `CoordinadorActualizacion.EvaluarEnArranqueAsync` (con mock del coordinador o de `IUpdateService`)
- [ ] C4.2 Enganchar el coordinador en `InicializarAsync` (background, con manejo de excepción para no
      tumbar el arranque); registrar `CoordinadorActualizacion` + los 3 ViewModels en
      `ConfigurarServicios`
- [ ] C4.3 `dotnet test` verde → commit: `feat(presentation): disparo del actualizador en background al arranque del shell`

---

### Bloque D — Empaquetado (scripts por OS + doc) [manual, no TDD]

#### D1 — Versionado: InformationalVersion en el .csproj

**Archivos:** `src/StockApp.Presentation/StockApp.Presentation.csproj` (mod) — **Dep:** C4

- [ ] D1.1 Agregar `<Version>` / `<InformationalVersion>` (SemVer2) al `.csproj` de Presentation como
      single source of truth
- [ ] D1.2 (Opcional) Exponer la versión en un "Acerca de" del shell
- [ ] D1.3 `dotnet build StockApp.sln` → commit: `feat(presentation): InformationalVersion como single source of truth de versión`

#### D2 — Empaquetado: script Windows

**Archivos:** `build/pack-win.ps1`, `build/RELEASE_NOTES.md.template` — **Dep:** D1

- [ ] D2.1 Escribir `pack-win.ps1`: lee versión del `.csproj` → `dotnet publish -r win-x64
      --self-contained` → `vpk pack` (`--channel win`, `--delta BestSpeed`, `--releaseNotes`,
      `--signParams` **comentado**)
- [ ] D2.2 Crear `RELEASE_NOTES.md.template` con front-matter `severity:` documentado
- [ ] D2.3 Commit: `feat(build): script pack-win.ps1 self-contained + vpk, signParams comentado` —
      **validación manual en Windows** (genera `Setup.exe` instalable sin .NET)

#### D3 — Empaquetado: script Linux

**Archivos:** `build/pack-linux.sh` — **Dep:** D1

- [ ] D3.1 Escribir `pack-linux.sh`: lee versión del `.csproj` → `dotnet publish -r linux-x64
      --self-contained` → `vpk pack` (`--channel linux`, `--delta BestSpeed`, `--releaseNotes`)
- [ ] D3.2 Commit: `feat(build): script pack-linux.sh self-contained + vpk (AppImage)` —
      **validación manual en Linux** (genera `AppImage` ejecutable en `$HOME` sin .NET)

#### D4 — Empaquetado: documentación del flujo manual

**Archivos:** `build/README-empaquetado.md` — **Dep:** D2, D3

- [ ] D4.1 Documentar: instalar `vpk` (`dotnet tool install -g vpk`), ejecutar el script del OS, subir
      assets a GitHub Release, convención de versionado y `severity`, nota de SmartScreen (D7) y de
      `$HOME`/pkexec en Linux
- [ ] D4.2 Commit: `docs(build): flujo manual de empaquetado y publicación por OS`

#### D5 — Cierre del Incremento 7 Fase A

**Archivos:** `docs/plans/2026-06-08-00-roadmap.md` (mod) — **Dep:** D4

- [ ] D5.1 `dotnet build StockApp.sln` y `dotnet test` — 0 errores, sin regresiones de Inc 1–6
- [ ] D5.2 Marcar Incremento 7 Fase A como completado en el roadmap
- [ ] D5.3 Commit: `docs(plans): marcar Incremento 7 Fase A (empaquetado + actualizador) como completado`

---

**Total tasks: 18 | Tests nuevos estimados: ~35 | Commits: 18 (uno por task)**
