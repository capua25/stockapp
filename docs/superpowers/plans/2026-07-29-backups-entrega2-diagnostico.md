# Backups Entrega 2 — Diagnóstico / Logs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capturar los logs de la API en archivo con credenciales enmascaradas, y permitir que un Admin descargue todos los logs como un único ZIP desde el desktop, sin acceso al servidor.

**Architecture:** Serilog con sink de archivo (rolling diario, nivel Warning+, retención 30 días) escribiendo en un `GetLogsDirectory()` nuevo de `IUserDataPathProvider`. Un `ITextFormatter` propio sanea el texto renderizado (mensaje + stack trace) antes de que llegue al disco. Un grupo de endpoints `/logs` protegido por el permiso `GestionarDiagnostico` ya existente expone un resumen y una descarga ZIP armada por streaming. El desktop consume eso con un `LogsApiClient` y una zona "Diagnóstico" agregada a la `MantenimientoView`/`MantenimientoViewModel` que ya creó la Entrega 1.

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core Minimal API, Serilog.AspNetCore + Serilog.Sinks.File, `System.IO.Compression.ZipArchive`, Avalonia + CommunityToolkit.Mvvm, xUnit + Moq, Testcontainers (PostgreSQL).

## Global Constraints

- Target framework: `net10.0`. EF Core `10.*`.
- Serilog: NO fijar versión en este plan — tomar la estable de `Serilog.AspNetCore` y `Serilog.Sinks.File` compatible con .NET 10 vigente al implementar.
- Nivel mínimo de log a archivo: `Warning`. Retención: 30 días por antigüedad de archivo (NO por cantidad). Rolling: diario.
- Credenciales a enmascarar, exactamente estas tres: `Password=`, `Secret=`, `Bearer ` (con el espacio).
- Permiso: reusar `Permisos.GestionarDiagnostico` (`"diagnostico.gestionar"`). YA EXISTE y ya está en `Permisos.Todos`. NO crear un permiso nuevo, NO tocar `AuthorizationService.AccionesOperador` (su ausencia ahí es deliberada: fail-closed → solo Admin).
- Assertions: xUnit nativo (`Assert.Equal`, `Assert.True`, ...). El repo NO tiene FluentAssertions ni Shouldly. No los agregues.
- Mocks: Moq en `StockApp.Presentation.Tests`. Fakes escritos a mano en `StockApp.Presentation.UiTests` (usa `[AvaloniaFact]`, no `[Fact]`). `StockApp.Api.Tests` no usa mocks: `WebApplicationFactory` + Testcontainers real (requiere Docker corriendo).
- Idioma de identificadores: español para dominio/negocio (`ServicioConsultaLogs`, `ListarAsync`, `ResumenLogsDto`), inglés donde ya es estándar .NET (`HttpClient`, `ILogger`, `ITextFormatter`). Comentarios y nombres de test en español. Nombres de test: `Metodo_Escenario_ResultadoEsperado`.
- Capas: interfaces de contrato en `StockApp.Application`, implementaciones de infraestructura en `StockApp.Infrastructure`, ApiClients en `StockApp.ApiClient` (clase sellada + `ApiErrores.EnviarAsync` + `ApiErrores.AsegurarExitoAsync`).
- Correr la suite: `dotnet test StockApp.sln`, o por proyecto. NUNCA hagas build suelto como paso de verificación: verificá corriendo los tests.
- Commits: Conventional Commits, español, minúscula tras los dos puntos, sin punto final, scope `(diagnostico)`. Ej: `feat(diagnostico): agregar GetLogsDirectory al proveedor de rutas`.
- Un problema de logging NUNCA puede impedir que la API arranque. Si el directorio de logs no existe o no es escribible, la API arranca igual y avisa por consola.

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Infrastructure/Platform/IUserDataPathProvider.cs` (modificar) | Suma `GetLogsDirectory()` |
| `src/StockApp.Infrastructure/Platform/UserDataPathProvider.cs` (modificar) | Implementa `GetLogsDirectory()` |
| `src/StockApp.Api/Logging/SaneadorCredenciales.cs` (crear) | Función pura: texto → texto enmascarado |
| `src/StockApp.Api/Logging/FormateadorSaneado.cs` (crear) | `ITextFormatter` que envuelve al formateador real y sanea su salida |
| `src/StockApp.Api/Program.cs` (modificar) | Configura Serilog, registra `ServicioConsultaLogs`, mapea `/logs` |
| `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs` (modificar) | Empieza a loguear (Warning 4xx / Error 500) |
| `src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs` (modificar) | Exime el prefijo `/logs` |
| `src/StockApp.Api/Endpoints/LogsEndpoints.cs` (crear) | Grupo `/logs`: resumen + descarga ZIP |
| `src/StockApp.Application/Logs/LogsDtos.cs` (crear) | `ResumenLogsDto`, `LogsDescargaDto` |
| `src/StockApp.Application/Logs/ILogsService.cs` (crear) | Contrato que consume el desktop |
| `src/StockApp.Application/Logs/ServicioConsultaLogs.cs` (crear) | Lógica server-side sobre el directorio de logs |
| `src/StockApp.ApiClient/LogsApiClient.cs` (crear) | Implementación HTTP de `ILogsService` |
| `src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs` (modificar) | Suma la zona Diagnóstico |
| `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml` (modificar) | Suma la sección visual Diagnóstico |
| `src/StockApp.Presentation/App.axaml.cs` (modificar) | Registra `ILogsService` |

---

### Task 1: `GetLogsDirectory()` en el proveedor de rutas

**Files:**
- Modify: `src/StockApp.Infrastructure/Platform/IUserDataPathProvider.cs`
- Modify: `src/StockApp.Infrastructure/Platform/UserDataPathProvider.cs`
- Modify: `tests/StockApp.Api.Tests/Fixtures/UserDataPathProviderFake.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `string IUserDataPathProvider.GetLogsDirectory()` — devuelve `<GetDataDirectory()>/logs`. Lo consumen Task 4, Task 7.

- [ ] **Step 1: Agregar el método a la interfaz**

En `src/StockApp.Infrastructure/Platform/IUserDataPathProvider.cs`, agregá una línea al final de la interfaz, después de `GetLicenciaPath()`:

```csharp
public interface IUserDataPathProvider
{
    string GetDataDirectory();
    string GetDatabasePath();
    string GetBackupsDirectory();
    string GetLicenciaPath();
    string GetLogsDirectory();
}
```

- [ ] **Step 2: Implementarlo en `UserDataPathProvider`**

En `src/StockApp.Infrastructure/Platform/UserDataPathProvider.cs`, agregá la constante junto a las otras y el método junto a los otros. Seguí el patrón exacto de `GetBackupsDirectory()`:

```csharp
private const string LogsSubdir = "logs";

public string GetLogsDirectory() => Path.Combine(GetDataDirectory(), LogsSubdir);
```

- [ ] **Step 3: Implementarlo en el fake de tests**

En `tests/StockApp.Api.Tests/Fixtures/UserDataPathProviderFake.cs`:

```csharp
public string GetLogsDirectory() => Path.Combine(_directorioDatos, "logs");
```

- [ ] **Step 4: Correr la suite completa para verificar que nada se rompió**

Run: `dotnet test StockApp.sln`
Expected: PASS (misma cantidad de tests que antes; si alguna otra clase implementaba `IUserDataPathProvider` y ahora no compila, implementale el método siguiendo el mismo patrón).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Infrastructure/Platform/ tests/StockApp.Api.Tests/Fixtures/UserDataPathProviderFake.cs
git commit -m "feat(diagnostico): agregar GetLogsDirectory al proveedor de rutas"
```

---

### Task 2: `SaneadorCredenciales` — enmascarado puro de texto

**Files:**
- Create: `src/StockApp.Api/Logging/SaneadorCredenciales.cs`
- Test: `tests/StockApp.Api.Tests/Logging/SaneadorCredencialesTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `internal static string SaneadorCredenciales.Sanear(string texto)` — devuelve el texto con `Password=<valor>`, `Secret=<valor>` y `Bearer <token>` enmascarados como `Password=***`, `Secret=***`, `Bearer ***`. Lo consume Task 3.

**Por qué así:** un `IDestructuringPolicy` o un enricher de Serilog NO puede modificar `logEvent.Exception`, y ahí es exactamente donde viaja la connection string de Npgsql con la contraseña dentro del stack trace. Saneamos sobre el texto ya renderizado, que es lo único que garantiza cubrir mensaje y excepción.

- [ ] **Step 1: Escribir el test que falla**

Creá `tests/StockApp.Api.Tests/Logging/SaneadorCredencialesTests.cs`:

```csharp
using StockApp.Api.Logging;

namespace StockApp.Api.Tests.Logging;

public class SaneadorCredencialesTests
{
    [Fact]
    public void Sanear_ConPasswordEnConnectionString_LaEnmascara()
    {
        const string texto = "Host=localhost;Database=stockapp;Username=postgres;Password=supersecreta123;";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("supersecreta123", resultado);
        Assert.Contains("Password=***", resultado);
        Assert.Contains("Host=localhost", resultado);
    }

    [Fact]
    public void Sanear_ConSecret_LoEnmascara()
    {
        const string texto = "Jwt:Secret=clave-de-firma-de-32-caracteres-abcdef y sigue el mensaje";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("clave-de-firma-de-32-caracteres-abcdef", resultado);
        Assert.Contains("Secret=***", resultado);
        Assert.Contains("y sigue el mensaje", resultado);
    }

    [Fact]
    public void Sanear_ConTokenBearer_LoEnmascara()
    {
        const string texto = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.abc.def";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.abc.def", resultado);
        Assert.Contains("Bearer ***", resultado);
    }

    [Fact]
    public void Sanear_EsInsensibleAMayusculas()
    {
        const string texto = "PASSWORD=otra-secreta;secret=tambien-secreta;BEARER token-secreto";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("otra-secreta", resultado);
        Assert.DoesNotContain("tambien-secreta", resultado);
        Assert.DoesNotContain("token-secreto", resultado);
    }

    [Fact]
    public void Sanear_ConVariasCredencialesEnUnaLinea_LasEnmascaraTodas()
    {
        const string texto = "Password=uno;Secret=dos;Bearer tres";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("uno", resultado);
        Assert.DoesNotContain("dos", resultado);
        Assert.DoesNotContain("tres", resultado);
    }

    [Fact]
    public void Sanear_SinCredenciales_DevuelveElTextoIntacto()
    {
        const string texto = "Fallo la corrida de backup: el binario pg_dump no existe en la ruta configurada.";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.Equal(texto, resultado);
    }

    [Fact]
    public void Sanear_TextoVacio_NoRompe()
    {
        Assert.Equal(string.Empty, SaneadorCredenciales.Sanear(string.Empty));
    }

    [Fact]
    public void Sanear_ConPasswordEntreComillasDobles_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Password=\"p@ss;word\"";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("p@ss;word", resultado);
        Assert.Contains("Password=***", resultado);
    }

    [Fact]
    public void Sanear_ConPasswordEntreComillasSimples_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Password='p@ss;word'";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("p@ss;word", resultado);
        Assert.Contains("Password=***", resultado);
    }

    [Fact]
    public void Sanear_ConSecretEntreComillasDobles_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Secret=\"cl;ave-secreta\"";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("cl;ave-secreta", resultado);
        Assert.Contains("Secret=***", resultado);
    }

    [Fact]
    public void Sanear_ConSecretEntreComillasSimples_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Secret='cl;ave-secreta'";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("cl;ave-secreta", resultado);
        Assert.Contains("Secret=***", resultado);
    }

    [Fact]
    public void Sanear_ConBearerEntreComillasDobles_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Bearer \"tok;en-secreto\"";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("tok;en-secreto", resultado);
        Assert.Contains("Bearer ***", resultado);
    }

    [Fact]
    public void Sanear_ConBearerEntreComillasSimples_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Bearer 'tok;en-secreto'";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("tok;en-secreto", resultado);
        Assert.Contains("Bearer ***", resultado);
    }
}
```

**Nota (post fix round 1):** los seis casos de "entre comillas" existen porque Npgsql cita automáticamente el valor de una connection string cuando contiene `;` o `'`. Con la regex original (`[^;\s"']+`), un valor citado no matcheaba nunca — la comilla de apertura queda excluida de la clase de caracteres y el `+` no puede arrancar. Resultado: cuanto más fuerte la contraseña (más probable que tenga `;`), menos la protegía el saneador. Ver Step 3 para el fix.

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~SaneadorCredencialesTests"`
Expected: FAIL — error de compilación, `SaneadorCredenciales` no existe.

- [ ] **Step 3: Escribir la implementación mínima**

Creá `src/StockApp.Api/Logging/SaneadorCredenciales.cs`:

```csharp
using System.Text.RegularExpressions;

namespace StockApp.Api.Logging;

/// <summary>
/// Enmascara credenciales en el texto ya renderizado de un evento de log, antes de que
/// toque el disco. Se sanea el texto final —y no las propiedades del evento— porque la
/// connection string con la contraseña suele viajar dentro del stack trace de una
/// excepción de Npgsql, y <c>LogEvent.Exception</c> no es modificable por un enricher.
/// El ZIP de logs termina en la máquina de un administrativo y probablemente adjunto en
/// un mail: acá no se filtra nada.
/// </summary>
internal static partial class SaneadorCredenciales
{
    private const string Mascara = "***";

    [GeneratedRegex(@"(?i)\bPassword\s*=\s*(?:""[^""]*""|'[^']*'|[^;\s""']+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexPassword();

    [GeneratedRegex(@"(?i)\bSecret\s*=\s*(?:""[^""]*""|'[^']*'|[^;\s""']+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexSecret();

    [GeneratedRegex(@"(?i)\bBearer\s+(?:""[^""]*""|'[^']*'|[^;\s""']+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexBearer();

    internal static string Sanear(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto;

        var resultado = RegexPassword().Replace(texto, $"Password={Mascara}");
        resultado = RegexSecret().Replace(resultado, $"Secret={Mascara}");
        resultado = RegexBearer().Replace(resultado, $"Bearer {Mascara}");
        return resultado;
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~SaneadorCredencialesTests"`
Expected: PASS, 13 tests (7 originales + 6 de los casos "entre comillas" agregados en fix round 1).

Si `Sanear_EsInsensibleAMayusculas` falla porque la máscara sale como `Password=***` cuando el original decía `PASSWORD=`, está bien: el test solo exige que el valor secreto desaparezca. No agregues lógica para preservar el casing del nombre de la clave.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Logging/SaneadorCredenciales.cs tests/StockApp.Api.Tests/Logging/SaneadorCredencialesTests.cs
git commit -m "feat(diagnostico): agregar saneador de credenciales para logs"
```

- [ ] **Step 6 (fix round 1): Endurecer las regex para valores citados**

La regex original (`[^;\s"']+`) no matcheaba un valor entre comillas — Npgsql cita automáticamente la password de una connection string cuando contiene `;` o `'`, así que cuanto más fuerte la contraseña, menos la protegía el saneador. Fix: reemplazar el grupo de captura por `(?:"[^"]*"|'[^']*'|[^;\s"']+)` en las tres regex (`RegexPassword`, `RegexSecret`, `RegexBearer`), sin tocar nada más de la clase.

```bash
git add src/StockApp.Api/Logging/SaneadorCredenciales.cs tests/StockApp.Api.Tests/Logging/SaneadorCredencialesTests.cs docs/superpowers/plans/2026-07-29-backups-entrega2-diagnostico.md
git commit -m "fix(diagnostico): enmascarar credenciales entre comillas en los logs"
```

---

### Task 3: `FormateadorSaneado` — el `ITextFormatter` que aplica el saneador

**Files:**
- Create: `src/StockApp.Api/Logging/FormateadorSaneado.cs`
- Test: `tests/StockApp.Api.Tests/Logging/FormateadorSaneadoTests.cs`
- Modify: `src/StockApp.Api/StockApp.Api.csproj`

**Interfaces:**
- Consumes: `SaneadorCredenciales.Sanear(string)` de Task 2.
- Produces: `internal sealed class FormateadorSaneado : ITextFormatter` con constructor `FormateadorSaneado(ITextFormatter interno)` y método `void Format(LogEvent logEvent, TextWriter output)`. Lo consume Task 4.

- [ ] **Step 1: Agregar los paquetes de Serilog**

En `src/StockApp.Api/StockApp.Api.csproj`, dentro del `<ItemGroup>` de `PackageReference`, agregá `Serilog.AspNetCore` y `Serilog.Sinks.File`. Resolvé la versión estable vigente compatible con .NET 10:

```bash
cd /home/capua25/workspace/stockapp
dotnet add src/StockApp.Api/StockApp.Api.csproj package Serilog.AspNetCore
dotnet add src/StockApp.Api/StockApp.Api.csproj package Serilog.Sinks.File
```

- [ ] **Step 2: Escribir el test que falla**

Creá `tests/StockApp.Api.Tests/Logging/FormateadorSaneadoTests.cs`:

```csharp
using System.Globalization;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;
using StockApp.Api.Logging;

namespace StockApp.Api.Tests.Logging;

public class FormateadorSaneadoTests
{
    private static readonly MessageTemplateParser Parser = new();

    private static FormateadorSaneado Crear() =>
        new(new MessageTemplateTextFormatter("{Message:lj}{NewLine}{Exception}", CultureInfo.InvariantCulture));

    private static LogEvent CrearEvento(string plantilla, Exception? excepcion = null) =>
        new(DateTimeOffset.UnixEpoch, LogEventLevel.Warning, excepcion,
            Parser.Parse(plantilla), []);

    [Fact]
    public void Format_ConCredencialEnElMensaje_LaEnmascara()
    {
        var formateador = Crear();
        var evento = CrearEvento("No se pudo conectar: Password=secreta-del-municipio;");
        var salida = new StringWriter();

        formateador.Format(evento, salida);

        var texto = salida.ToString();
        Assert.DoesNotContain("secreta-del-municipio", texto);
        Assert.Contains("Password=***", texto);
    }

    [Fact]
    public void Format_ConCredencialEnElStackTrace_LaEnmascara()
    {
        var formateador = Crear();
        var excepcion = new InvalidOperationException(
            "Npgsql fallo con Host=localhost;Password=secreta-en-excepcion;");
        var evento = CrearEvento("Error al abrir la conexion", excepcion);
        var salida = new StringWriter();

        formateador.Format(evento, salida);

        var texto = salida.ToString();
        Assert.DoesNotContain("secreta-en-excepcion", texto);
        Assert.Contains("Password=***", texto);
    }

    [Fact]
    public void Format_SinCredenciales_DejaElMensajeIntacto()
    {
        var formateador = Crear();
        var evento = CrearEvento("La corrida de backup fallo por timeout");
        var salida = new StringWriter();

        formateador.Format(evento, salida);

        Assert.Contains("La corrida de backup fallo por timeout", salida.ToString());
    }
}
```

- [ ] **Step 3: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~FormateadorSaneadoTests"`
Expected: FAIL — error de compilación, `FormateadorSaneado` no existe.

- [ ] **Step 4: Escribir la implementación**

Creá `src/StockApp.Api/Logging/FormateadorSaneado.cs`:

```csharp
using Serilog.Events;
using Serilog.Formatting;

namespace StockApp.Api.Logging;

/// <summary>
/// Envuelve a otro <see cref="ITextFormatter"/>: lo deja renderizar el evento completo
/// (mensaje + excepcion) a un buffer en memoria, sanea ese texto y recien ahi lo escribe
/// a la salida real. Es el unico punto por el que pasa TODO lo que va a terminar en el
/// archivo de log, incluido el stack trace, que un enricher no puede tocar.
/// </summary>
internal sealed class FormateadorSaneado : ITextFormatter
{
    private readonly ITextFormatter _interno;

    internal FormateadorSaneado(ITextFormatter interno) => _interno = interno;

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var buffer = new StringWriter();
        _interno.Format(logEvent, buffer);
        output.Write(SaneadorCredenciales.Sanear(buffer.ToString()));
    }
}
```

- [ ] **Step 5: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~FormateadorSaneadoTests"`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/StockApp.Api.csproj src/StockApp.Api/Logging/FormateadorSaneado.cs tests/StockApp.Api.Tests/Logging/FormateadorSaneadoTests.cs
git commit -m "feat(diagnostico): agregar formateador de log que aplica el saneador"
```

---

### Task 4: Serilog en `Program.cs`

**Files:**
- Modify: `src/StockApp.Api/Program.cs`
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs`

**Interfaces:**
- Consumes: `IUserDataPathProvider.GetLogsDirectory()` (Task 1), `FormateadorSaneado` (Task 3).
- Produces: archivos `stockapp-<yyyyMMdd>.log` en el directorio de logs. Clave de configuración `Logs:Directorio` que, si está presente, gana sobre `GetLogsDirectory()`. La consume `ApiFactory` en los tests.

**Por qué la clave de configuración:** Serilog se configura durante el build del host, antes de que `ConfigureTestServices` pueda reemplazar `IUserDataPathProvider` por el fake. Sin este override, la suite de tests escribiría logs en el `LocalApplicationData` real de quien corra los tests.

- [ ] **Step 1: Configurar Serilog en `Program.cs`**

Agregá los `using` necesarios arriba de todo:

```csharp
using System.Globalization;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using StockApp.Api.Logging;
```

Inmediatamente después de `var builder = WebApplication.CreateBuilder(args);` y ANTES del `builder.Services.Configure<HostOptions>(...)` existente, insertá:

```csharp
// ── Logging a archivo (Entrega 2) ──────────────────────────────────────
// Un problema de logging no puede dejar al municipio sin sistema: si el directorio
// no se puede crear, la API arranca igual y solo pierde el sink de archivo.
const string PlantillaLog = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

var directorioLogs = builder.Configuration["Logs:Directorio"];
if (string.IsNullOrWhiteSpace(directorioLogs))
    directorioLogs = new UserDataPathProvider().GetLogsDirectory();

// La consola también se sanea, no solo el archivo: si el proceso corre como servicio
// (systemd, journald, Docker) stdout queda capturado y persistido igual que el archivo
// — sin esto, la consola es una segunda vía de filtración de credenciales que las tasks
// 2-4 justamente vienen a cerrar. Se pierde el coloreado por nivel de Serilog al usar un
// formatter propio; es aceptable.
var configuracionLog = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console(new FormateadorSaneado(new MessageTemplateTextFormatter(
        PlantillaLog, CultureInfo.InvariantCulture)));

try
{
    Directory.CreateDirectory(directorioLogs);
    configuracionLog = configuracionLog.WriteTo.File(
        formatter: new FormateadorSaneado(new MessageTemplateTextFormatter(
            PlantillaLog, CultureInfo.InvariantCulture)),
        path: Path.Combine(directorioLogs, "stockapp-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileTimeLimit: TimeSpan.FromDays(30),
        restrictedToMinimumLevel: LogEventLevel.Warning,
        shared: true);
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"[StockApp] No se pudo inicializar el log de archivo en '{directorioLogs}': {ex.Message}. "
        + "La API arranca igual, pero no va a haber logs descargables.");
}

Log.Logger = configuracionLog.CreateLogger();
builder.Logging.ClearProviders();
builder.Host.UseSerilog();
```

Verificá que `using StockApp.Infrastructure.Platform;` ya esté presente (lo necesita `UserDataPathProvider`); si no está, agregalo.

- [ ] **Step 2: Aislar el directorio de logs en los tests**

En `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs`, dentro del `AddInMemoryCollection`, agregá una entrada más al diccionario:

```csharp
["Logs:Directorio"] = Path.Combine(Path.GetTempPath(), "StockAppApiTestsLogs_" + Guid.NewGuid()),
```

- [ ] **Step 3: Correr la suite de la API para verificar que arranca con Serilog**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS. Requiere Docker corriendo (Testcontainers levanta `postgres:16-alpine`).

- [ ] **Step 4: Verificar a mano que el archivo de log se escribe y sale saneado**

Arrancá la API apuntando el log a un directorio temporal y forzá un error:

```bash
cd /home/capua25/workspace/stockapp
Logs__Directorio=/tmp/stockapp-logs-manual dotnet run --project src/StockApp.Api
```

En otra terminal, pegale a un endpoint protegido sin token y después cortá la API:

```bash
curl -i http://localhost:5000/logs
ls -la /tmp/stockapp-logs-manual
```

Expected: existe un archivo `stockapp-<fecha>.log`. Revisalo y confirmá que no aparece ninguna contraseña en claro.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs
git commit -m "feat(diagnostico): configurar serilog con rolling diario y retencion de 30 dias"
```

---

### Task 5: `DomainExceptionHandler` empieza a loguear

**Files:**
- Modify: `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`
- Modify: `tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs`

**Interfaces:**
- Consumes: nada de tasks anteriores (Serilog ya está enganchado por Task 4, pero este handler solo depende de `ILogger<T>`).
- Produces: nada que consuman otras tasks.

**Por qué:** hoy el handler mapea la excepción a `ProblemDetails` y no loguea nada. Sin este cambio, montamos toda la infraestructura de logging y justo el caso `_ => 500` — el error no anticipado, el que más falta hace diagnosticar — no queda registrado en ningún lado.

**Corrección post-brief (2026-07-29):** este plan asumía, sin verificarlo, que `DomainExceptionHandlerTests.cs` no existía. En realidad ya existe con 12 tests que cubren el switch completo de mapeo (404/409/400/403/500, extensions de `StockInsuficienteException`/`ValidacionImportacionException`, no-exposición de `detail` en 500), instanciando `new DomainExceptionHandler()` sin argumentos. Regla de fondo: nunca se borran tests que pasan para hacer entrar código nuevo. El Step 1 de abajo queda corregido: el archivo se MODIFICA, no se crea.

- [ ] **Step 1: Escribir el test que falla**

El archivo `tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs` YA EXISTE con 12 tests de mapeo (ver corrección arriba). No se reemplaza: se AGREGAN los 2 tests nuevos de este brief al final de la clase, junto con el `LoggerEspia`. Los 12 tests preexistentes se actualizan para construir el handler vía un helper `CrearHandler() => new(NullLogger<DomainExceptionHandler>.Instance)` (requiere `using Microsoft.Extensions.Logging.Abstractions;`) en vez de `new DomainExceptionHandler()` — un logger no-op, porque esos tests verifican el mapeo a `ProblemDetails`, no el logueo. El cuerpo/las aserciones de esos 12 tests no cambian.

Contenido a agregar (no reemplaza el archivo entero):

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StockApp.Api.ErrorHandling;
using StockApp.Domain.Exceptions;

namespace StockApp.Api.Tests.ErrorHandling;

public class DomainExceptionHandlerTests
{
    private sealed class LoggerEspia : ILogger<DomainExceptionHandler>
    {
        public List<LogLevel> Niveles { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Niveles.Add(logLevel);
    }

    private static DefaultHttpContext CrearContexto()
    {
        var servicios = new ServiceCollection();
        servicios.AddProblemDetails();
        return new DefaultHttpContext { RequestServices = servicios.BuildServiceProvider() };
    }

    [Fact]
    public async Task TryHandleAsync_ExcepcionDeNegocio_LogueaWarning()
    {
        var espia = new LoggerEspia();
        var handler = new DomainExceptionHandler(espia);

        await handler.TryHandleAsync(CrearContexto(),
            new EntidadNoEncontradaException("no existe"), CancellationToken.None);

        Assert.Contains(LogLevel.Warning, espia.Niveles);
        Assert.DoesNotContain(LogLevel.Error, espia.Niveles);
    }

    [Fact]
    public async Task TryHandleAsync_ExcepcionNoAnticipada_LogueaError()
    {
        var espia = new LoggerEspia();
        var handler = new DomainExceptionHandler(espia);

        await handler.TryHandleAsync(CrearContexto(),
            new InvalidOperationException("algo se rompio feo"), CancellationToken.None);

        Assert.Contains(LogLevel.Error, espia.Niveles);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~DomainExceptionHandlerTests"`
Expected: FAIL — error de compilación, `DomainExceptionHandler` no tiene constructor con `ILogger` (los 12 tests preexistentes siguen intactos hasta este punto; el error de compilación viene de los 2 tests nuevos + del helper `CrearHandler`).

- [ ] **Step 3: Agregar el logger al handler**

En `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`, agregá `using Microsoft.Extensions.Logging;`, el constructor, y el logueo. La clase pasa de no tener constructor a:

```csharp
public class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            // ... el switch existente queda TAL CUAL, no lo toques ...
        };
```

Inmediatamente después del `switch` y ANTES de `httpContext.Response.StatusCode = status;`, insertá:

```csharp
        // Los 4xx son fallas de negocio esperables (Warning). El 500 es el caso que no
        // anticipamos: va como Error porque es el que alguien va a tener que diagnosticar
        // despues, sin acceso al servidor, leyendo el ZIP de logs.
        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Error no controlado en {Ruta}", httpContext.Request.Path);
        else
            _logger.LogWarning(exception, "Falla de negocio {Status} en {Ruta}",
                status, httpContext.Request.Path);
```

- [ ] **Step 4: Verificar que el handler sigue registrado correctamente en DI**

En `src/StockApp.Api/Program.cs`, buscá el registro del handler (`AddExceptionHandler<DomainExceptionHandler>()`). No hay que cambiarlo: el contenedor resuelve `ILogger<DomainExceptionHandler>` solo. Solo confirmá que el registro existe.

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS, 14/14 en `DomainExceptionHandlerTests` (12 preexistentes + 2 nuevos) y toda la matriz de endpoints existente sin regresiones.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs
git commit -m "feat(diagnostico): loguear las excepciones que atrapa DomainExceptionHandler"
```

---

### Task 6: `ServicioConsultaLogs` + DTOs + contrato `ILogsService`

**Files:**
- Create: `src/StockApp.Application/Logs/LogsDtos.cs`
- Create: `src/StockApp.Application/Logs/ILogsService.cs`
- Create: `src/StockApp.Application/Logs/ServicioConsultaLogs.cs`
- Test: `tests/StockApp.Application.Tests/Logs/ServicioConsultaLogsTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `public sealed record ResumenLogsDto(int CantidadArchivos, DateTime? DesdeFecha, DateTime? HastaFecha, long TamanioTotalBytes)`
  - `public sealed class LogsDescargaDto(string NombreArchivo, Stream Contenido) : IAsyncDisposable`
  - `public interface ILogsService { Task<ResumenLogsDto> ObtenerResumenAsync(CancellationToken ct = default); Task<LogsDescargaDto> DescargarZipAsync(CancellationToken ct = default); }`
  - `public sealed class ServicioConsultaLogs { public ResumenLogsDto ObtenerResumen(string directorioLogs); public IReadOnlyList<string> ResolverArchivosParaZip(string directorioLogs); }`

  Los consumen Task 7 (`ServicioConsultaLogs`, DTOs) y Task 8 (`ILogsService`, DTOs).

**Nota de diseño:** `ILogsService` es el contrato que consume el DESKTOP (lo implementa `LogsApiClient` en Task 8). `ServicioConsultaLogs` es la lógica SERVER-SIDE que usa el endpoint. Son dos cosas distintas, igual que `IBackupsService` vs `ServicioConsultaBackups` en la Entrega 1. `ServicioConsultaLogs` recibe el directorio por parámetro — no inyecta `IUserDataPathProvider` — siguiendo el patrón exacto de `ServicioConsultaBackups.ResolverArchivoParaDescargaAsync(id, paths.GetBackupsDirectory())`, lo que además lo hace testeable con un directorio temporal sin abstraer el filesystem.

- [ ] **Step 1: Escribir el test que falla**

Creá `tests/StockApp.Application.Tests/Logs/ServicioConsultaLogsTests.cs`:

```csharp
using StockApp.Application.Logs;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Tests.Logs;

public class ServicioConsultaLogsTests : IDisposable
{
    private readonly string _directorio =
        Path.Combine(Path.GetTempPath(), "StockAppLogsTests_" + Guid.NewGuid());

    private void CrearArchivo(string nombre, string contenido, DateTime escritura)
    {
        Directory.CreateDirectory(_directorio);
        var ruta = Path.Combine(_directorio, nombre);
        File.WriteAllText(ruta, contenido);
        File.SetLastWriteTime(ruta, escritura);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directorio)) Directory.Delete(_directorio, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ObtenerResumen_DirectorioInexistente_DevuelveResumenVacio()
    {
        var servicio = new ServicioConsultaLogs();

        var resumen = servicio.ObtenerResumen(Path.Combine(_directorio, "no-existe"));

        Assert.Equal(0, resumen.CantidadArchivos);
        Assert.Null(resumen.DesdeFecha);
        Assert.Null(resumen.HastaFecha);
        Assert.Equal(0, resumen.TamanioTotalBytes);
    }

    [Fact]
    public void ObtenerResumen_ConTresArchivos_AgregaCantidadTamanioYRango()
    {
        CrearArchivo("stockapp-20260701.log", "aaaa", new DateTime(2026, 7, 1, 10, 0, 0));
        CrearArchivo("stockapp-20260715.log", "bb", new DateTime(2026, 7, 15, 10, 0, 0));
        CrearArchivo("stockapp-20260729.log", "cccccc", new DateTime(2026, 7, 29, 10, 0, 0));
        var servicio = new ServicioConsultaLogs();

        var resumen = servicio.ObtenerResumen(_directorio);

        Assert.Equal(3, resumen.CantidadArchivos);
        Assert.Equal(12, resumen.TamanioTotalBytes);
        Assert.Equal(new DateTime(2026, 7, 1, 10, 0, 0), resumen.DesdeFecha);
        Assert.Equal(new DateTime(2026, 7, 29, 10, 0, 0), resumen.HastaFecha);
    }

    [Fact]
    public void ObtenerResumen_IgnoraArchivosQueNoSonLog()
    {
        CrearArchivo("stockapp-20260701.log", "aaaa", new DateTime(2026, 7, 1, 10, 0, 0));
        CrearArchivo("notas.txt", "esto no es un log", new DateTime(2026, 7, 2, 10, 0, 0));
        var servicio = new ServicioConsultaLogs();

        var resumen = servicio.ObtenerResumen(_directorio);

        Assert.Equal(1, resumen.CantidadArchivos);
        Assert.Equal(4, resumen.TamanioTotalBytes);
    }

    [Fact]
    public void ResolverArchivosParaZip_ConArchivos_LosDevuelveOrdenadosPorNombre()
    {
        CrearArchivo("stockapp-20260729.log", "c", new DateTime(2026, 7, 29, 10, 0, 0));
        CrearArchivo("stockapp-20260701.log", "a", new DateTime(2026, 7, 1, 10, 0, 0));
        var servicio = new ServicioConsultaLogs();

        var archivos = servicio.ResolverArchivosParaZip(_directorio);

        Assert.Equal(2, archivos.Count);
        Assert.EndsWith("stockapp-20260701.log", archivos[0]);
        Assert.EndsWith("stockapp-20260729.log", archivos[1]);
    }

    [Fact]
    public void ResolverArchivosParaZip_SinArchivos_LanzaEntidadNoEncontrada()
    {
        Directory.CreateDirectory(_directorio);
        var servicio = new ServicioConsultaLogs();

        Assert.Throws<EntidadNoEncontradaException>(() => servicio.ResolverArchivosParaZip(_directorio));
    }

    [Fact]
    public void ResolverArchivosParaZip_DirectorioInexistente_LanzaEntidadNoEncontrada()
    {
        var servicio = new ServicioConsultaLogs();

        Assert.Throws<EntidadNoEncontradaException>(
            () => servicio.ResolverArchivosParaZip(Path.Combine(_directorio, "no-existe")));
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~ServicioConsultaLogsTests"`
Expected: FAIL — error de compilación, no existe `StockApp.Application.Logs`.

- [ ] **Step 3: Escribir los DTOs**

Creá `src/StockApp.Application/Logs/LogsDtos.cs`:

```csharp
namespace StockApp.Application.Logs;

/// <summary>
/// Metadatos agregados del directorio de logs. No expone nombres de archivo individuales:
/// la descarga es siempre el ZIP completo, asi no hay ningun parametro de nombre de
/// archivo que pueda convertirse en superficie de path traversal.
/// </summary>
public sealed record ResumenLogsDto(
    int CantidadArchivos, DateTime? DesdeFecha, DateTime? HastaFecha, long TamanioTotalBytes);

public sealed class LogsDescargaDto : IAsyncDisposable
{
    public string NombreArchivo { get; }
    public Stream Contenido { get; }

    public LogsDescargaDto(string nombreArchivo, Stream contenido)
    {
        NombreArchivo = nombreArchivo;
        Contenido = contenido;
    }

    public ValueTask DisposeAsync() => Contenido.DisposeAsync();
}
```

- [ ] **Step 4: Escribir el contrato del cliente**

Creá `src/StockApp.Application/Logs/ILogsService.cs`:

```csharp
namespace StockApp.Application.Logs;

/// <summary>
/// Contrato que consume el desktop. Lo implementa <c>LogsApiClient</c> contra <c>/logs</c>.
/// </summary>
public interface ILogsService
{
    Task<ResumenLogsDto> ObtenerResumenAsync(CancellationToken ct = default);
    Task<LogsDescargaDto> DescargarZipAsync(CancellationToken ct = default);
}
```

- [ ] **Step 5: Escribir el servicio server-side**

Creá `src/StockApp.Application/Logs/ServicioConsultaLogs.cs`:

```csharp
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Logs;

/// <summary>
/// Lee el directorio de logs del filesystem. Recibe la ruta por parametro (en vez de
/// inyectar el proveedor de rutas) siguiendo el mismo patron que
/// <c>ServicioConsultaBackups.ResolverArchivoParaDescargaAsync</c>: el endpoint resuelve
/// la ruta y este servicio solo opera sobre ella.
/// </summary>
public sealed class ServicioConsultaLogs
{
    private const string PatronArchivos = "*.log";

    public ResumenLogsDto ObtenerResumen(string directorioLogs)
    {
        var archivos = ListarArchivos(directorioLogs);
        if (archivos.Count == 0) return new ResumenLogsDto(0, null, null, 0);

        var infos = archivos.Select(r => new FileInfo(r)).ToList();
        return new ResumenLogsDto(
            infos.Count,
            infos.Min(i => i.LastWriteTime),
            infos.Max(i => i.LastWriteTime),
            infos.Sum(i => i.Length));
    }

    /// <summary>
    /// Devuelve las rutas completas a comprimir, ordenadas por nombre. Si no hay ningun
    /// archivo lanza <see cref="EntidadNoEncontradaException"/> — que el handler traduce a
    /// 404 — en vez de devolver un ZIP vacio que parezca un archivo corrupto.
    /// </summary>
    public IReadOnlyList<string> ResolverArchivosParaZip(string directorioLogs)
    {
        var archivos = ListarArchivos(directorioLogs);
        if (archivos.Count == 0)
            throw new EntidadNoEncontradaException(
                "No hay archivos de log para descargar todavía.");

        return archivos;
    }

    private static List<string> ListarArchivos(string directorioLogs)
    {
        if (string.IsNullOrWhiteSpace(directorioLogs) || !Directory.Exists(directorioLogs))
            return [];

        return Directory.GetFiles(directorioLogs, PatronArchivos)
            .OrderBy(r => Path.GetFileName(r), StringComparer.Ordinal)
            .ToList();
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~ServicioConsultaLogsTests"`
Expected: PASS, 6 tests.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Logs/ tests/StockApp.Application.Tests/Logs/
git commit -m "feat(diagnostico): agregar servicio de consulta de logs y sus contratos"
```

---

### Task 7: Endpoints `/logs` + exención de licencia

**Files:**
- Create: `src/StockApp.Api/Endpoints/LogsEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Modify: `src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs`
- Test: `tests/StockApp.Api.Tests/LogsEndpointTests.cs`

**Interfaces:**
- Consumes: `ServicioConsultaLogs`, `ResumenLogsDto` (Task 6), `IUserDataPathProvider.GetLogsDirectory()` (Task 1).
- Produces: `GET /logs` → `200 ResumenLogsDto`; `GET /logs/contenido` → `200 application/zip` o `404`. Los consume Task 8.

- [ ] **Step 1: Escribir el test que falla**

Creá `tests/StockApp.Api.Tests/LogsEndpointTests.cs`. Replicá la matriz exacta de `BackupsEndpointTests` (401 sin token / 403 Operador / 200 Admin / 404 sin archivos / 200 con licencia vencida):

```csharp
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Logs;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Tests;

public class LogsEndpointTests : ApiTestBase
{
    public LogsEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteCon(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string DirectorioLogs() =>
        Factory.Services.GetRequiredService<IUserDataPathProvider>().GetLogsDirectory();

    private void SembrarLog(string nombre, string contenido)
    {
        var dir = DirectorioLogs();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, nombre), contenido);
    }

    private void LimpiarLogs()
    {
        var dir = DirectorioLogs();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task GetLogs_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLogs_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador()).GetAsync("/logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetLogs_ConTokenAdminYSinArchivos_Devuelve200ConResumenVacio()
    {
        LimpiarLogs();

        var response = await ClienteCon(TokenAdmin()).GetAsync("/logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resumen = await response.Content.ReadFromJsonAsync<ResumenLogsDto>();
        Assert.NotNull(resumen);
        Assert.Equal(0, resumen!.CantidadArchivos);
    }

    [Fact]
    public async Task GetLogs_ConArchivos_DevuelveCantidadYTamanio()
    {
        LimpiarLogs();
        SembrarLog("stockapp-20260728.log", "warn uno");
        SembrarLog("stockapp-20260729.log", "warn dos");

        var response = await ClienteCon(TokenAdmin()).GetAsync("/logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resumen = await response.Content.ReadFromJsonAsync<ResumenLogsDto>();
        Assert.Equal(2, resumen!.CantidadArchivos);
        Assert.Equal(16, resumen.TamanioTotalBytes);
    }

    [Fact]
    public async Task GetContenido_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/logs/contenido");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador()).GetAsync("/logs/contenido");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_SinArchivos_Devuelve404()
    {
        LimpiarLogs();

        var response = await ClienteCon(TokenAdmin()).GetAsync("/logs/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_ConArchivos_DevuelveZipConTodosLosArchivos()
    {
        LimpiarLogs();
        SembrarLog("stockapp-20260728.log", "contenido uno");
        SembrarLog("stockapp-20260729.log", "contenido dos");

        var response = await ClienteCon(TokenAdmin()).GetAsync("/logs/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(2, zip.Entries.Count);
        Assert.Contains(zip.Entries, e => e.Name == "stockapp-20260728.log");
        Assert.Contains(zip.Entries, e => e.Name == "stockapp-20260729.log");
    }

    [Fact]
    public async Task GetLogs_ConLicenciaVencida_Devuelve200()
    {
        LimpiarLogs();
        var estado = Factory.Services.GetRequiredService<EstadoLicencia>();
        estado.Activada = false;
        try
        {
            var response = await ClienteCon(TokenAdmin()).GetAsync("/logs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            estado.Activada = true;
        }
    }
}
```

Si el `using` de `IJwtTokenService` o de `EstadoLicencia` no resuelve, copiá los `using` exactos que ya tiene `tests/StockApp.Api.Tests/BackupsEndpointTests.cs`.

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~LogsEndpointTests"`
Expected: FAIL — 404 en todo (los endpoints no existen) y error de compilación por `StockApp.Application.Logs` si Task 6 no se completó.

- [ ] **Step 3: Escribir los endpoints**

Creá `src/StockApp.Api/Endpoints/LogsEndpoints.cs`:

```csharp
using System.IO.Compression;
using StockApp.Application.Authorization;
using StockApp.Application.Logs;
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Endpoints;

public static class LogsEndpoints
{
    public static IEndpointRouteBuilder MapLogsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/logs", (ServicioConsultaLogs servicio, IUserDataPathProvider paths) =>
            Results.Ok(servicio.ObtenerResumen(paths.GetLogsDirectory())))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        // Un unico ZIP con todos los archivos, sin parametro de nombre: sin parametro no
        // hay superficie de path traversal. Se arma por streaming sobre el Response.Body,
        // asi no materializamos el zip completo ni en memoria ni en disco temporal.
        app.MapGet("/logs/contenido", (ServicioConsultaLogs servicio, IUserDataPathProvider paths) =>
        {
            var archivos = servicio.ResolverArchivosParaZip(paths.GetLogsDirectory());
            var nombreZip = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

            return Results.Stream(async salida =>
            {
                using var zip = new ZipArchive(salida, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var ruta in archivos)
                {
                    var entrada = zip.CreateEntry(Path.GetFileName(ruta), CompressionLevel.Optimal);
                    // FileShare.ReadWrite es obligatorio: Serilog tiene abierto el archivo
                    // del dia en curso y sin esto la descarga falla justo cuando mas importa.
                    await using var origen = new FileStream(
                        ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await using var destino = entrada.Open();
                    await origen.CopyToAsync(destino);
                }
            }, "application/zip", nombreZip);
        })
        .RequireAuthorization(Permisos.GestionarDiagnostico);

        return app;
    }
}
```

- [ ] **Step 4: Registrar el servicio y mapear el grupo en `Program.cs`**

Junto al bloque de DI de backups, agregá:

```csharp
builder.Services.AddScoped<ServicioConsultaLogs>();
```

Y junto a los `app.Map<X>Endpoints()`, inmediatamente después de `app.MapBackupsEndpoints();`:

```csharp
app.MapLogsEndpoints();
```

Verificá que `using StockApp.Application.Logs;` esté entre los `using` del archivo.

- [ ] **Step 5: Eximir `/logs` del bloqueo por licencia**

En `src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs`, `EsRutaPermitida` pasa a:

```csharp
    private static bool EsRutaPermitida(PathString path)
        => path.StartsWithSegments("/licencia")
        || path.StartsWithSegments("/auth/reset-admin")
        || path.StartsWithSegments("/auth/login")
        || path.StartsWithSegments("/backups")
        || path.StartsWithSegments("/logs");
```

Cuando la licencia vence es JUSTO cuando más se necesita poder mirar los logs.

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS, incluyendo los 9 tests nuevos de `LogsEndpointTests` y toda la suite previa sin regresiones.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/Endpoints/LogsEndpoints.cs src/StockApp.Api/Program.cs src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs tests/StockApp.Api.Tests/LogsEndpointTests.cs
git commit -m "feat(diagnostico): exponer endpoints de logs con descarga zip por streaming"
```

---

### Task 8: `LogsApiClient`

**Files:**
- Create: `src/StockApp.ApiClient/LogsApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/LogsApiClientTests.cs`

**Interfaces:**
- Consumes: `ILogsService`, `ResumenLogsDto`, `LogsDescargaDto` (Task 6); endpoints de Task 7.
- Produces: `public sealed class LogsApiClient : ILogsService` con constructor `LogsApiClient(HttpClient http)`. Lo consume Task 10 (registro en DI).

- [ ] **Step 1: Escribir el test que falla**

Creá `tests/StockApp.ApiClient.Tests/LogsApiClientTests.cs`. Mirá primero `tests/StockApp.ApiClient.Tests/ApiErroresTests.cs` y copiá de ahí el handler falso que ya usa la suite; si no hay uno reutilizable, usá este:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using StockApp.ApiClient;
using StockApp.Application.Logs;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class LogsApiClientTests
{
    private sealed class HandlerFalso : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? UltimaSolicitud { get; private set; }

        public HandlerFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaSolicitud = request;
            return Task.FromResult(_responder(request));
        }
    }

    private static LogsApiClient Crear(HandlerFalso handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") });

    [Fact]
    public async Task ObtenerResumenAsync_DevuelveElResumenDeserializado()
    {
        var handler = new HandlerFalso(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ResumenLogsDto(
                3, new DateTime(2026, 7, 1), new DateTime(2026, 7, 29), 4096)),
        });

        var resumen = await Crear(handler).ObtenerResumenAsync();

        Assert.Equal(3, resumen.CantidadArchivos);
        Assert.Equal(4096, resumen.TamanioTotalBytes);
        Assert.Equal("/logs", handler.UltimaSolicitud!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DescargarZipAsync_DevuelveElNombreDelContentDisposition()
    {
        var contenido = new ByteArrayContent(Encoding.UTF8.GetBytes("zip falso"));
        contenido.Headers.ContentDisposition =
            new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
            {
                FileName = "\"logs_20260729_101500.zip\"",
            };
        var handler = new HandlerFalso(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = contenido });

        await using var descarga = await Crear(handler).DescargarZipAsync();

        Assert.Equal("logs_20260729_101500.zip", descarga.NombreArchivo);
        Assert.Equal("/logs/contenido", handler.UltimaSolicitud!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DescargarZipAsync_SinContentDisposition_UsaUnNombrePorDefecto()
    {
        var handler = new HandlerFalso(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("zip falso")),
        });

        await using var descarga = await Crear(handler).DescargarZipAsync();

        Assert.Equal("logs.zip", descarga.NombreArchivo);
    }

    [Fact]
    public async Task DescargarZipAsync_ConRespuesta404_LanzaEntidadNoEncontrada()
    {
        var handler = new HandlerFalso(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                """{"title":"Recurso no encontrado.","detail":"No hay archivos de log para descargar todavía.","status":404}""",
                Encoding.UTF8, "application/problem+json"),
        });

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => Crear(handler).DescargarZipAsync());
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter "FullyQualifiedName~LogsApiClientTests"`
Expected: FAIL — error de compilación, `LogsApiClient` no existe.

- [ ] **Step 3: Escribir el cliente**

Creá `src/StockApp.ApiClient/LogsApiClient.cs`, copiando el patrón exacto de `BackupsApiClient`:

```csharp
using System.Net.Http.Json;
using System.Threading;
using StockApp.Application.Logs;

namespace StockApp.ApiClient;

public sealed class LogsApiClient : ILogsService
{
    private readonly HttpClient _http;

    public LogsApiClient(HttpClient http) => _http = http;

    public async Task<ResumenLogsDto> ObtenerResumenAsync(CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("logs", ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<ResumenLogsDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException(
                "Respuesta vacía del servidor al obtener el resumen de logs.");
    }

    public async Task<LogsDescargaDto> DescargarZipAsync(CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(
            () => _http.GetAsync("logs/contenido", HttpCompletionOption.ResponseHeadersRead, ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);

        var contentDisposition = response.Content.Headers.ContentDisposition;
        var nombreArchivo = contentDisposition?.FileNameStar?.Trim('"')
            ?? contentDisposition?.FileName?.Trim('"')
            ?? "logs.zip";

        var contenido = await response.Content.ReadAsStreamAsync(ct);
        return new LogsDescargaDto(nombreArchivo, contenido);
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj`
Expected: PASS, incluyendo los 4 tests nuevos.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/LogsApiClient.cs tests/StockApp.ApiClient.Tests/LogsApiClientTests.cs
git commit -m "feat(diagnostico): agregar LogsApiClient contra el grupo /logs"
```

---

### Task 9: Zona Diagnóstico en `MantenimientoViewModel`

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs`

**Interfaces:**
- Consumes: `ILogsService`, `ResumenLogsDto`, `LogsDescargaDto` (Task 6); `IServicioGuardadoArchivo.GuardarBytesAsync(Stream, string, CancellationToken)` (ya existe, sin cambios).
- Produces: constructor `MantenimientoViewModel(IBackupsService, IServicioGuardadoArchivo, IConfirmacionService, ILogsService)` — la firma cambia, con `ILogsService` como CUARTO parámetro. Propiedades `TextoResumenLogs`, `HayLogs`, `DescargandoLogs`; comando `DescargarLogsCommand`. Los consume Task 10.

**Ojo:** cambiar la firma del constructor rompe todos los tests existentes que lo instancian. Es esperado: hay que actualizarlos en el mismo commit.

- [ ] **Step 1: Escribir los tests que fallan**

Agregá al final de la clase en `tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs`:

```csharp
    [Fact]
    public async Task CargarAsync_ConLogs_ArmaElTextoDelResumen()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(
                3, new DateTime(2026, 7, 1), new DateTime(2026, 7, 29), 2048));
        var (vm, _, _, _) = Crear(logs: logsMock);

        await vm.CargarAsync();

        Assert.True(vm.HayLogs);
        Assert.Contains("3", vm.TextoResumenLogs);
    }

    [Fact]
    public async Task CargarAsync_SinLogs_NoHabilitaLaDescarga()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(0, null, null, 0));
        var (vm, _, _, _) = Crear(logs: logsMock);

        await vm.CargarAsync();

        Assert.False(vm.HayLogs);
    }

    [Fact]
    public async Task CargarAsync_ElServicioDeLogsFalla_NoRompeLaCargaDeBackups()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("la api de logs esta caida"));
        var (vm, _, _, _) = Crear(
            corridas: new List<CorridaBackupDto>
            {
                new(1, new DateTime(2026, 7, 29), "Exitosa", "backup_1.dump", 1024, null),
            },
            logs: logsMock);

        await vm.CargarAsync();

        Assert.Single(vm.Corridas);
        Assert.False(vm.HayLogs);
    }

    [Fact]
    public async Task DescargarLogsCommand_GuardaElZip()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(1, new DateTime(2026, 7, 29), new DateTime(2026, 7, 29), 10));
        logsMock.Setup(l => l.DescargarZipAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LogsDescargaDto("logs_20260729.zip", new MemoryStream([1, 2, 3])));
        var (vm, _, guardadoMock, _) = Crear(logs: logsMock);
        await vm.CargarAsync();

        await vm.DescargarLogsCommand.ExecuteAsync(null);

        guardadoMock.Verify(g => g.GuardarBytesAsync(
            It.IsAny<Stream>(), "logs_20260729.zip", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.DescargandoLogs);
    }

    [Fact]
    public async Task DescargarLogsCommand_ElServicioFalla_InformaElErrorYNoRompe()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(1, new DateTime(2026, 7, 29), new DateTime(2026, 7, 29), 10));
        logsMock.Setup(l => l.DescargarZipAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("se cayo la api"));
        var (vm, _, _, confirmacionMock) = Crear(logs: logsMock);
        await vm.CargarAsync();

        await vm.DescargarLogsCommand.ExecuteAsync(null);

        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Once);
        Assert.False(vm.DescargandoLogs);
    }
```

Y actualizá el helper `Crear` existente para que acepte el mock de logs y siga sirviendo a todos los tests que ya lo usan:

```csharp
private static (MantenimientoViewModel vm,
                Mock<IBackupsService> backupsMock,
                Mock<IServicioGuardadoArchivo> guardadoMock,
                Mock<IConfirmacionService> confirmacionMock)
    Crear(IReadOnlyList<CorridaBackupDto>? corridas = null, Mock<ILogsService>? logs = null)
{
    var backupsMock = new Mock<IBackupsService>();
    backupsMock.Setup(b => b.ListarAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(corridas ?? new List<CorridaBackupDto>());
    var guardadoMock = new Mock<IServicioGuardadoArchivo>();
    var confirmacionMock = new Mock<IConfirmacionService>();

    var logsMock = logs ?? new Mock<ILogsService>();
    if (logs is null)
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(0, null, null, 0));

    var vm = new MantenimientoViewModel(
        backupsMock.Object, guardadoMock.Object, confirmacionMock.Object, logsMock.Object);
    return (vm, backupsMock, guardadoMock, confirmacionMock);
}
```

Agregá `using StockApp.Application.Logs;` a los `using` del archivo.

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~MantenimientoViewModelTests"`
Expected: FAIL — error de compilación, el constructor no acepta un cuarto parámetro.

- [ ] **Step 3: Agregar la zona Diagnóstico al ViewModel**

En `src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs`:

Agregá `using StockApp.Application.Logs;` arriba. Después, el campo, las propiedades y el constructor ampliado:

```csharp
    private readonly ILogsService _logs;

    [ObservableProperty]
    private string _textoResumenLogs = "Sin datos de logs todavía.";

    [ObservableProperty]
    private bool _hayLogs;

    [ObservableProperty]
    private bool _descargandoLogs;

    public MantenimientoViewModel(
        IBackupsService backups,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        ILogsService logs)
    {
        _backups = backups;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _logs = logs;
    }
```

Al final de `CargarAsync()`, DESPUÉS del bloque `finally` existente que apaga `Cargando`, agregá la llamada a la carga del resumen de logs:

```csharp
        await CargarResumenLogsAsync();
```

Y agregá el método y el comando:

```csharp
    /// <summary>
    /// El resumen de logs se carga aparte y se traga sus propios errores: que el
    /// diagnostico no esté disponible no puede dejar la lista de backups en blanco.
    /// </summary>
    private async Task CargarResumenLogsAsync()
    {
        try
        {
            var resumen = await _logs.ObtenerResumenAsync();
            HayLogs = resumen.CantidadArchivos > 0;
            TextoResumenLogs = HayLogs
                ? $"{resumen.CantidadArchivos} archivo(s), {FormatearTamanio(resumen.TamanioTotalBytes)}, "
                  + $"del {resumen.DesdeFecha:dd/MM/yyyy} al {resumen.HastaFecha:dd/MM/yyyy}."
                : "No hay archivos de log todavía.";
        }
        catch (Exception)
        {
            HayLogs = false;
            TextoResumenLogs = "No se pudo consultar el estado de los logs.";
        }
    }

    private static string FormatearTamanio(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    [RelayCommand]
    private async Task DescargarLogsAsync()
    {
        if (DescargandoLogs) return;

        DescargandoLogs = true;
        try
        {
            await using var descarga = await _logs.DescargarZipAsync();
            await _guardado.GuardarBytesAsync(descarga.Contenido, descarga.NombreArchivo);
        }
        catch (OperationCanceledException) { /* cancelación deliberada, no se informa */ }
        catch (Exception ex) { await _confirmacion.InformarAsync(ex.Message); }
        finally { DescargandoLogs = false; }
    }
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
Expected: PASS, incluyendo los 5 tests nuevos y TODOS los tests preexistentes de `MantenimientoViewModelTests` (que ahora usan el `Crear` actualizado).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs
git commit -m "feat(diagnostico): agregar zona de diagnostico al MantenimientoViewModel"
```

---

### Task 10: Zona Diagnóstico en la vista + registro en DI

**Files:**
- Modify: `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs`

**Interfaces:**
- Consumes: `MantenimientoViewModel` con la zona Diagnóstico (Task 9), `LogsApiClient` (Task 8).
- Produces: nada.

- [ ] **Step 1: Escribir el test headless que falla**

En `tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs`:

Primero, agregá un fake de `ILogsService` junto a los fakes que ya existen en el archivo:

```csharp
    private sealed class LogsServiceFake : ILogsService
    {
        private readonly ResumenLogsDto _resumen;

        public LogsServiceFake(ResumenLogsDto resumen) => _resumen = resumen;

        public Task<ResumenLogsDto> ObtenerResumenAsync(CancellationToken ct = default) =>
            Task.FromResult(_resumen);

        public Task<LogsDescargaDto> DescargarZipAsync(CancellationToken ct = default) =>
            Task.FromResult(new LogsDescargaDto("logs.zip", new MemoryStream([1, 2, 3])));
    }
```

Actualizá el helper `Montar` para inyectarlo:

```csharp
    private static (Window Window, MantenimientoViewModel Vm) Montar(
        IReadOnlyList<CorridaBackupDto> corridas, ResumenLogsDto? resumenLogs = null)
    {
        var vm = new MantenimientoViewModel(
            new BackupsServiceFake(corridas),
            new ServicioGuardadoArchivoFake(),
            new ConfirmacionServiceFake(),
            new LogsServiceFake(resumenLogs ?? new ResumenLogsDto(0, null, null, 0)));
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }
```

Y agregá los tests de la zona nueva:

```csharp
    [AvaloniaFact]
    public void Montar_ConLogs_MuestraElResumenYHabilitaLaDescarga()
    {
        var (window, vm) = Montar(
            [],
            new ResumenLogsDto(2, new DateTime(2026, 7, 28), new DateTime(2026, 7, 29), 4096));

        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.HayLogs);
        var textos = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains(textos, t => t.Contains("Diagnóstico", StringComparison.Ordinal));
        Assert.Contains(textos, t => t.Contains("2 archivo(s)", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Montar_SinLogs_MuestraLaZonaConElMensajeVacio()
    {
        var (window, vm) = Montar([], new ResumenLogsDto(0, null, null, 0));

        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.HayLogs);
        var textos = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains(textos, t => t.Contains("No hay archivos de log todavía", StringComparison.Ordinal));
    }
```

Agregá `using StockApp.Application.Logs;` a los `using` del archivo.

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~MantenimientoViewTests"`
Expected: FAIL — error de compilación por el cuarto parámetro del constructor, y después por la sección "Diagnóstico" que la vista todavía no dibuja.

- [ ] **Step 3: Agregar la sección a la vista**

En `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml`, agregá la sección DESPUÉS del bloque de Backups, dentro del mismo contenedor vertical, reusando las clases de estilo que ya usa la sección de Backups (`Classes="card"` para el recuadro y el mismo estilo de subtítulo — copiá el `Classes`/estilo exacto del subtítulo "Backups" que ya está en el archivo, no inventes uno nuevo):

```xml
<StackPanel Margin="0,24,0,0" Spacing="8">
  <TextBlock Text="Diagnóstico" Classes="subtitulo" />

  <Border Classes="card">
    <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
      <TextBlock Grid.Column="0"
                 Text="{Binding TextoResumenLogs}"
                 TextWrapping="Wrap"
                 VerticalAlignment="Center" />

      <Button Grid.Column="1"
              Content="Descargar logs"
              Margin="12,0,0,0"
              IsEnabled="{Binding HayLogs}"
              Command="{Binding DescargarLogsCommand}" />
    </Grid>
  </Border>
</StackPanel>
```

Si el subtítulo "Backups" existente NO usa `Classes="subtitulo"`, copiá textualmente el markup del subtítulo que sí esté en el archivo y cambiale solo el `Text`.

- [ ] **Step 4: Registrar `ILogsService` en el contenedor del desktop**

En `src/StockApp.Presentation/App.axaml.cs`, junto al registro de `IBackupsService`, agregá:

```csharp
services.AddTransient<ILogsService>(sp =>
    new LogsApiClient(sp.GetRequiredKeyedService<HttpClient>("Descargas")));
```

Usa el MISMO `HttpClient` keyed `"Descargas"` (timeout de 30 minutos) que ya usa `BackupsApiClient` — el ZIP de logs es una descarga, no una llamada de API común. Agregá `using StockApp.Application.Logs;` si falta.

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS, incluyendo los 2 tests headless nuevos.

- [ ] **Step 6: Correr la suite COMPLETA**

Run: `dotnet test StockApp.sln`
Expected: PASS, todo verde. Requiere Docker corriendo para `StockApp.Api.Tests` y `StockApp.Infrastructure.Tests`.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml src/StockApp.Presentation/App.axaml.cs tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs
git commit -m "feat(diagnostico): agregar zona de diagnostico a la vista de mantenimiento"
```

---

## Fix wave — hallazgos MINOR del review final de la rama (2026-07-29)

El review final de `feat/backups-entrega2-diagnostico` (post Task 10, con las 10 tasks ya mergeadas) dejó un hallazgo IMPORTANT (saneador de credenciales entre comillas — ya cerrado, ver Task 2 Step 6) y 6 hallazgos MINOR, cerrados en esta pasada con un commit por hallazgo. Quedan documentados acá porque cambian código y tests que las tasks de arriba describen con su versión ORIGINAL (pre-fix) — si releés Task 4 o Task 7 de punta a punta, el snippet de código de esos steps ya no es el que corre en `main`.

- [x] **Hallazgo 1 — dos fuentes de verdad para el directorio de logs.** `Program.cs` (Task 4) resolvía `Logs:Directorio` con fallback a `IUserDataPathProvider`, pero `LogsEndpoints` (Task 7) leía siempre `IUserDataPathProvider` sin mirar la config: si alguien seteaba `Logs:Directorio` en producción, Serilog escribía en un directorio y el endpoint leía otro. Fix: `src/StockApp.Api/Logging/DirectorioLogsResolver.cs` (nuevo) centraliza la precedencia (`IConfiguration` primero, `IUserDataPathProvider` como fallback); lo usan tanto `Program.cs` como los dos endpoints de `LogsEndpoints.cs`. `LogsEndpointTests.DirectorioLogs()` pasa a leer `IConfiguration["Logs:Directorio"]` (lo que `ApiFactory` ya seteaba) en vez de `IUserDataPathProvider.GetLogsDirectory()`, porque ahora es la config la que gana. Commit: `fix(diagnostico): unificar la resolucion del directorio de logs`.

- [x] **Hallazgo 2 — TOCTOU entre listar y comprimir el ZIP.** `ResolverArchivosParaZip` (Task 6) lista los archivos y el `Results.Stream` (Task 7) los abre uno por uno más tarde; si la retención de Serilog purga el más viejo en el medio, el `FileStream.Open` tira con el 200 y los headers ya enviados — ZIP truncado sin ningún error visible. Fix: en `LogsEndpoints.cs`, el `FileStream.Open` de cada archivo va en su propio `try/catch` (`FileNotFoundException`, `DirectoryNotFoundException`, `IOException`) DENTRO del loop; si el archivo ya no está, se lo salta y sigue con el resto — el `ZipArchive.CreateEntry` solo se llama después de abrir el `FileStream` con éxito, para no dejar entradas vacías en el ZIP.

- [x] **Hallazgo 3 — el archivo del día puede dejar de recibir eventos en silencio.** El `WriteTo.File` de `Program.cs` (Task 4) no seteaba `fileSizeLimitBytes`: el default de Serilog es 1 GB con `rollOnFileSizeLimit: false`, así que al llegar ahí el archivo del día deja de recibir eventos EN SILENCIO por el resto del día — justo cuando una tormenta de errores es cuando más se necesitan los logs. Fix: se agregó `fileSizeLimitBytes: 50 * 1024 * 1024` y `rollOnFileSizeLimit: true`, compatible con `rollingInterval: RollingInterval.Day` (Serilog agrega un sufijo de secuencia dentro del mismo día si hace falta rotar por tamaño).

- [x] **Hallazgo 4 — comentario de seguridad desactualizado.** El comentario de `BloqueoLicenciaMiddleware.cs` decía que `/backups` Y `/logs` "exigen el permiso `GestionarDiagnostico` en la capa de Application". Cierto para `/backups` (`ServicioConsultaBackups` llama `_auth.Verificar`), falso para `/logs` (`ServicioConsultaLogs` no valida nada; el único control es el `RequireAuthorization` HTTP del endpoint). No es un agujero — está cubierto por tests que esperan 403 para Operador — pero el comentario mentía. Fix: solo se corrigió el texto del comentario, sin tocar `ServicioConsultaLogs`.

- [x] **Hallazgo 5 — `DescargandoLogs` no bindeada en el XAML.** `MantenimientoViewModel.DescargandoLogs` (Task 9) existía pero ningún XAML de Task 10 lo usaba: con el timeout de 30 minutos del `HttpClient` "Descargas", si el ZIP pesaba la UI quedaba muda sin ninguna señal de progreso. Fix: en `MantenimientoView.axaml`, el botón "Descargar logs" de la zona Diagnóstico ahora usa el mismo patrón de swap que ya usa la lista de corridas de backup (Descargar/Cancelar, Task 9 de Entrega 1) — un segundo botón "Descargando…" deshabilitado (`IsEnabled="False"`) lo reemplaza mientras `DescargandoLogs` es `true`. Test headless nuevo en `MantenimientoViewTests.cs`: `Montar_DescargaDeLogsEnCurso_MuestraLaSenialDeProgresoYOcultaElBotonDeAccion`.

- [x] **Hallazgo 6 — directorio temporal huérfano en los tests.** `ApiFactory` (Task 4 Step 2) generaba `Logs:Directorio` con un GUID bajo `Path.GetTempPath()` en un literal inline dentro de `ConfigureWebHost`, que `Program.cs` crea y llena al arrancar cada test — nadie lo borraba, así que cada corrida de `dotnet test` dejaba basura. Fix: el valor pasa a un campo `_directorioLogsTemporal` de `ApiFactory`, borrado (con `try/catch` defensivo) en `DisposeAsync`.

---

## Verificación orgánica (obligatoria antes de cerrar)

Convención del proyecto: además de los tests, hay que probar la app REAL. No alcanza con la suite verde.

- [ ] **1. Levantar Postgres y la API**

```bash
docker start stockapp-pg
cd /home/capua25/workspace/stockapp && dotnet run --project src/StockApp.Api
```

- [ ] **2. Verificar la matriz HTTP a mano**

```bash
# 401 sin token
curl -i http://localhost:5000/logs
# 200 con token de admin (sacá el token del login)
curl -i -H "Authorization: Bearer <TOKEN_ADMIN>" http://localhost:5000/logs
# ZIP real
curl -i -H "Authorization: Bearer <TOKEN_ADMIN>" http://localhost:5000/logs/contenido -o /tmp/logs.zip
unzip -l /tmp/logs.zip
```

- [ ] **3. Verificar el saneado sobre un log REAL**

Forzá un error de conexión a la base (parar el contenedor y pegarle a un endpoint que consulte DB), después revisá el archivo de log del día y confirmá que la contraseña de la connection string NO aparece en claro.

- [ ] **4. Verificar la descarga desde el desktop**

Levantá el desktop, logueate como Admin, entrá a Mantenimiento, confirmá que la zona Diagnóstico muestra el resumen y que "Descargar logs" abre el selector de archivos y guarda un ZIP que se puede abrir.

- [ ] **5. Verificar con licencia vencida**

Con la licencia vencida, confirmá que `/logs` sigue respondiendo 200 (no 423) y que la zona Diagnóstico sigue accesible desde el modo de acceso acotado.

- [ ] **6. Restaurar el entorno**

Borrá los archivos de log de prueba, restaurá la licencia, y dejá el contenedor `stockapp-pg` corriendo.
