# Inc 7 Fase B — Licenciamiento offline firmado + Reset de Admin firmado — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Licenciar el SERVIDOR de StockApp con una licencia offline firmada (ECDSA P-256) atada al fingerprint de la máquina, bloqueando la API (423 Locked) sin licencia válida, y agregar un flujo de reset de Admin firmado — con activación y reset operables 100% desde el desktop, sin acceso físico al servidor.

**Architecture:** Una sola pieza criptográfica (ECDSA P-256 nativo, `System.Security.Cryptography`) firma y valida dos tipos de payload JSON: licencias y tokens de reset. El enforcement vive todo en la API: un `EstadoLicencia` singleton, calculado al arranque y actualizado en la activación, hace que un middleware devuelva `423 Locked` a toda request salvo `/licencia/*` y `/auth/reset-admin/*`. El desktop solo muestra pantallas (bloqueo, reset) y pega textos. Una CLU de desarrollador (`tools/StockApp.Licencias.Cli`, nunca empaquetada) genera el par de claves y emite licencias/tokens con la clave privada, reutilizando el MISMO firmador que valida `Application`.

**Tech Stack:** .NET 10, Clean Architecture (Domain / Application / Infrastructure / Api / ApiClient / Presentation), ASP.NET Core Minimal API + JWT, EF Core + Npgsql (PostgreSQL), xUnit + Testcontainers (integración de Api contra Postgres real), Avalonia 12 + CommunityToolkit.Mvvm (desktop), `System.Security.Cryptography.ECDsa`.

## Global Constraints

- Cripto: ECDSA P-256 nativo (`System.Security.Cryptography`), CERO dependencias criptográficas nuevas. Formato de string firmado: `base64url(payload JSON) + "." + base64url(firma)`. La firma se calcula sobre los **bytes UTF-8 del primer segmento** (`base64url(payload)`), no sobre el JSON crudo — así ambos lados coinciden sin re-serializar.
- La clave PÚBLICA se embebe en `Application` como constante base64 reemplazable (`OpcionesLicencia.ClavePublicaBase64Default`); en runtime se lee `Licencia:ClavePublicaBase64` de configuración con esa constante como fallback. Los tests inyectan la clave pública de TEST vía configuración. La clave PRIVADA jamás entra al repo.
- Licencia perpetua, sin expiración (venta única, spec §11.4). Payload licencia: `{ "ver":1, "cliente":"...", "maquina":"<fingerprint>", "emitida":"YYYY-MM-DD" }`. Payload reset: `{ "ver":1, "accion":"reset-admin", "maquina":"<fingerprint>", "desafio":"<nonce>" }`.
- Fingerprint: SHA-256 del id de máquina del OS (Windows: `MachineGuid` del registro; Linux: `/etc/machine-id`), presentado agrupado en bloques de 4 hex con `-` (`A3F2-9B41-...`). Nunca se expone el id crudo.
- Persistencia de la licencia: `licencia.lic` en el directorio de datos (`IUserDataPathProvider.GetLicenciaPath()`), que los updates de Velopack no tocan.
- Reset: nonce cripto-seguro en memoria, TTL 24 h, UNO solo activo (pedir otro invalida el anterior), UN solo uso (muere al consumirse o expirar).
- Resultados por enum, NUNCA excepciones para flujo: `ResultadoVerificacion { Ok, FormatoInvalido, FirmaInvalida }`, `ResultadoValidacionLicencia { Valida, FormatoInvalido, FirmaInvalida, MaquinaDistinta }`, `ResultadoValidacionReset { Valido, FormatoInvalido, FirmaInvalido, MaquinaDistinta, AccionInvalida, DesafioInvalido, DesafioExpirado }`.
- Auditoría: activaciones exitosas E intentos fallidos de licencia + resets se registran en `LogAuditoria`. Como los eventos de licencia son anónimos (pre-login) y `LogAuditoria.UsuarioId` es FK requerida con INNER JOIN en la lectura, se auditan atribuidos al **primer admin** (menor `Id`); si no hay admin, no se audita en DB (fail-soft). El reset audita con el `Id` del admin reseteado.
- Registro DI: `EstadoLicencia`, `IFingerprintMaquina`, `ValidadorFirma`, `IAlmacenLicencia`, `IAlmacenDesafiosReset` son SINGLETON; `ServicioLicencia` y `ServicioResetAdmin` son SCOPED (auditan → dependen de servicios scoped). `ValidateOnBuild=true` detecta captive dependencies: no registrar un singleton que dependa de scoped.
- Commits: conventional commits, en español, SIN `Co-Authored-By` ni atribución de IA. NO commitear salvo el step de commit de cada task. NO buildear la app de escritorio (solo `dotnet test`).
- Suite verde al cerrar cada task en el proyecto de test afectado; verificación de solución completa (`dotnet test`) en la última task.
- Validación manual en Windows/Linux real queda fuera de alcance de la ejecución automática: se hace junto con la validación pendiente de Fase A (ver sección final).

---

### Task 1: Núcleo criptográfico — payloads, codificación, firmador y validador (Application)

**Files:**
- Create: `src/StockApp.Application/Licenciamiento/CodificadorBase64Url.cs`
- Create: `src/StockApp.Application/Licenciamiento/Payloads.cs`
- Create: `src/StockApp.Application/Licenciamiento/FirmadorLicencias.cs`
- Create: `src/StockApp.Application/Licenciamiento/ValidadorFirma.cs`
- Create: `src/StockApp.Application/Licenciamiento/OpcionesLicencia.cs`
- Test: `tests/StockApp.Application.Tests/Licenciamiento/ValidadorFirmaTests.cs`

**Interfaces:**
- Produces:
  - `StockApp.Application.Licenciamiento.CodificadorBase64Url` (static): `string Codificar(byte[] datos)`, `byte[] Decodificar(string texto)`.
  - `record LicenciaPayload(int Ver, string Cliente, string Maquina, string Emitida)` con `[JsonPropertyName]` `ver/cliente/maquina/emitida`.
  - `record TokenResetPayload(int Ver, string Accion, string Maquina, string Desafio)` con `[JsonPropertyName]` `ver/accion/maquina/desafio`.
  - `static class FirmadorLicencias`: `string EmitirLicencia(LicenciaPayload payload, string clavePrivadaPem)`, `string EmitirTokenReset(TokenResetPayload payload, string clavePrivadaPem)`.
  - `enum ResultadoVerificacion { Ok, FormatoInvalido, FirmaInvalida }`.
  - `class ValidadorFirma`: ctor `(string clavePublicaBase64)`; `ResultadoVerificacion Verificar(string tokenFirmado, out byte[] payloadJson)`. NUNCA lanza.
  - `static class OpcionesLicencia`: `const string ClavePublicaBase64Default`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Application.Tests/Licenciamiento/ValidadorFirmaTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Application.Tests.Licenciamiento;

public class ValidadorFirmaTests
{
    // Par de claves EFÍMERO por corrida: determinístico dentro del test, sin claves
    // hardcodeadas. La privada firma, la pública valida — exactamente el flujo real.
    private static (string publicaBase64, string privadaPem) CrearPar()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publica = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        var privada = ecdsa.ExportPkcs8PrivateKeyPem();
        return (publica, privada);
    }

    [Fact]
    public void Verificar_LicenciaBienFirmada_DevuelveOkYPayload()
    {
        var (publica, privada) = CrearPar();
        var payload = new LicenciaPayload(1, "Ferretería X", "A3F2-9B41", "2026-07-15");
        var licencia = FirmadorLicencias.EmitirLicencia(payload, privada);

        var validador = new ValidadorFirma(publica);
        var resultado = validador.Verificar(licencia, out var payloadJson);

        Assert.Equal(ResultadoVerificacion.Ok, resultado);
        var decodificado = JsonSerializer.Deserialize<LicenciaPayload>(payloadJson);
        Assert.Equal("Ferretería X", decodificado!.Cliente);
        Assert.Equal("A3F2-9B41", decodificado.Maquina);
    }

    [Fact]
    public void Verificar_TokenResetBienFirmado_DevuelveOk()
    {
        var (publica, privada) = CrearPar();
        var payload = new TokenResetPayload(1, "reset-admin", "A3F2-9B41", "nonce-123");
        var token = FirmadorLicencias.EmitirTokenReset(payload, privada);

        var resultado = new ValidadorFirma(publica).Verificar(token, out var payloadJson);

        Assert.Equal(ResultadoVerificacion.Ok, resultado);
        var decodificado = JsonSerializer.Deserialize<TokenResetPayload>(payloadJson);
        Assert.Equal("reset-admin", decodificado!.Accion);
        Assert.Equal("nonce-123", decodificado.Desafio);
    }

    [Fact]
    public void Verificar_FirmadaConOtraClave_DevuelveFirmaInvalida()
    {
        var (_, privada) = CrearPar();
        var (otraPublica, _) = CrearPar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "X", "MAQ", "2026-07-15"), privada);

        var resultado = new ValidadorFirma(otraPublica).Verificar(licencia, out _);

        Assert.Equal(ResultadoVerificacion.FirmaInvalida, resultado);
    }

    [Fact]
    public void Verificar_PayloadAdulterado_DevuelveFirmaInvalida()
    {
        var (publica, privada) = CrearPar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "X", "MAQ", "2026-07-15"), privada);

        // Adulterar el payload conservando la firma original.
        var partes = licencia.Split('.');
        var payloadFalso = CodificadorBase64Url.Codificar(
            System.Text.Encoding.UTF8.GetBytes("{\"ver\":1,\"cliente\":\"HACK\",\"maquina\":\"MAQ\",\"emitida\":\"2026-07-15\"}"));
        var adulterada = payloadFalso + "." + partes[1];

        var resultado = new ValidadorFirma(publica).Verificar(adulterada, out _);

        Assert.Equal(ResultadoVerificacion.FirmaInvalida, resultado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-punto")]
    [InlineData("demasiados.puntos.aca")]
    [InlineData(".")]
    [InlineData("no-base64-url-válido!.firma")]
    public void Verificar_FormatoRoto_DevuelveFormatoInvalido(string entrada)
    {
        var (publica, _) = CrearPar();

        var resultado = new ValidadorFirma(publica).Verificar(entrada, out _);

        Assert.Equal(ResultadoVerificacion.FormatoInvalido, resultado);
    }

    [Fact]
    public void Verificar_ClavePublicaBasura_DevuelveFirmaInvalidaSinLanzar()
    {
        var (_, privada) = CrearPar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "X", "MAQ", "2026-07-15"), privada);

        // Clave pública inválida (no es SubjectPublicKeyInfo): fail-closed, sin excepción.
        var resultado = new ValidadorFirma("no-es-una-clave").Verificar(licencia, out _);

        Assert.Equal(ResultadoVerificacion.FirmaInvalida, resultado);
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~ValidadorFirmaTests"`
Expected: FALLA de compilación — `CodificadorBase64Url`, `LicenciaPayload`, `FirmadorLicencias`, `ValidadorFirma` no existen.

- [ ] **Step 3: Escribir `CodificadorBase64Url`**

Crear `src/StockApp.Application/Licenciamiento/CodificadorBase64Url.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Base64url (RFC 4648 §5): base64 sin padding, con '+'→'-' y '/'→'_'. Es el alfabeto
/// seguro para un string pegable a mano que no se rompe al copiar por chat/mail.
/// </summary>
public static class CodificadorBase64Url
{
    public static string Codificar(byte[] datos)
        => Convert.ToBase64String(datos)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static byte[] Decodificar(string texto)
    {
        var normalizado = texto.Replace('-', '+').Replace('_', '/');
        var relleno = normalizado.Length % 4;
        if (relleno == 2) normalizado += "==";
        else if (relleno == 3) normalizado += "=";
        else if (relleno == 1) throw new FormatException("Longitud base64url inválida.");
        return Convert.FromBase64String(normalizado);
    }
}
```

- [ ] **Step 4: Escribir los payloads**

Crear `src/StockApp.Application/Licenciamiento/Payloads.cs`:

```csharp
using System.Text.Json.Serialization;

namespace StockApp.Application.Licenciamiento;

/// <summary>Payload de una licencia perpetua atada a una máquina (spec §11.4).</summary>
public record LicenciaPayload(
    [property: JsonPropertyName("ver")]     int    Ver,
    [property: JsonPropertyName("cliente")] string Cliente,
    [property: JsonPropertyName("maquina")] string Maquina,
    [property: JsonPropertyName("emitida")] string Emitida);

/// <summary>Payload de un token de reset de Admin, atado a máquina + desafío (spec §5.1).</summary>
public record TokenResetPayload(
    [property: JsonPropertyName("ver")]     int    Ver,
    [property: JsonPropertyName("accion")]  string Accion,
    [property: JsonPropertyName("maquina")] string Maquina,
    [property: JsonPropertyName("desafio")] string Desafio);
```

- [ ] **Step 5: Escribir `FirmadorLicencias`**

Crear `src/StockApp.Application/Licenciamiento/FirmadorLicencias.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Emite strings firmados `base64url(payload).base64url(firma)` con ECDSA P-256.
/// Lo reutiliza la CLI del desarrollador (tools/StockApp.Licencias.Cli) — un solo formato
/// que no puede divergir del que valida <see cref="ValidadorFirma"/>.
/// </summary>
public static class FirmadorLicencias
{
    public static string EmitirLicencia(LicenciaPayload payload, string clavePrivadaPem)
        => Firmar(payload, clavePrivadaPem);

    public static string EmitirTokenReset(TokenResetPayload payload, string clavePrivadaPem)
        => Firmar(payload, clavePrivadaPem);

    private static string Firmar<T>(T payload, string clavePrivadaPem)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        var segmentoPayload = CodificadorBase64Url.Codificar(json);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(clavePrivadaPem);

        // La firma se calcula sobre los bytes UTF-8 del segmento base64url del payload,
        // no sobre el JSON crudo: el validador re-verifica sobre ese mismo segmento.
        var firma = ecdsa.SignData(
            Encoding.UTF8.GetBytes(segmentoPayload), HashAlgorithmName.SHA256);

        return segmentoPayload + "." + CodificadorBase64Url.Codificar(firma);
    }
}
```

- [ ] **Step 6: Escribir `ValidadorFirma` y `OpcionesLicencia`**

Crear `src/StockApp.Application/Licenciamiento/ValidadorFirma.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace StockApp.Application.Licenciamiento;

/// <summary>Resultado de bajo nivel de verificar la firma de un string firmado.</summary>
public enum ResultadoVerificacion { Ok, FormatoInvalido, FirmaInvalida }

/// <summary>
/// Verifica la firma ECDSA P-256 de un string `base64url(payload).base64url(firma)` contra
/// una clave pública. NUNCA lanza: cualquier problema de formato o de firma se devuelve como
/// enum (fail-closed). La clave pública viene de configuración (o de la constante embebida).
/// </summary>
public sealed class ValidadorFirma
{
    private readonly string _clavePublicaBase64;

    public ValidadorFirma(string clavePublicaBase64) => _clavePublicaBase64 = clavePublicaBase64;

    public ResultadoVerificacion Verificar(string tokenFirmado, out byte[] payloadJson)
    {
        payloadJson = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(tokenFirmado))
            return ResultadoVerificacion.FormatoInvalido;

        var partes = tokenFirmado.Split('.');
        if (partes.Length != 2 || partes[0].Length == 0 || partes[1].Length == 0)
            return ResultadoVerificacion.FormatoInvalido;

        byte[] payload;
        byte[] firma;
        try
        {
            payload = CodificadorBase64Url.Decodificar(partes[0]);
            firma   = CodificadorBase64Url.Decodificar(partes[1]);
        }
        catch (FormatException)
        {
            return ResultadoVerificacion.FormatoInvalido;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(
                Convert.FromBase64String(_clavePublicaBase64), out _);

            var valida = ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(partes[0]), firma, HashAlgorithmName.SHA256);

            if (!valida)
                return ResultadoVerificacion.FirmaInvalida;

            payloadJson = payload;
            return ResultadoVerificacion.Ok;
        }
        catch (Exception)
        {
            // Clave pública basura / firma con longitud inválida: fail-closed.
            return ResultadoVerificacion.FirmaInvalida;
        }
    }
}
```

Crear `src/StockApp.Application/Licenciamiento/OpcionesLicencia.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Constante embebida con la clave pública del desarrollador para validar licencias/tokens.
/// El valor por defecto es un PLACEHOLDER: se reemplaza por la clave real que imprime
/// `StockApp.Licencias.Cli generar-claves` durante la puesta en producción. En runtime se
/// puede sobrescribir con la config `Licencia:ClavePublicaBase64` (los tests inyectan la
/// clave pública de prueba por ahí). Con el placeholder, toda licencia falla la verificación
/// (fail-closed) y la API queda bloqueada — es el comportamiento correcto hasta pegar la real.
/// </summary>
public static class OpcionesLicencia
{
    public const string ClavePublicaBase64Default = "REEMPLAZAR-CON-CLAVE-PUBLICA-GENERADA-POR-LA-CLI";
}
```

- [ ] **Step 7: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~ValidadorFirmaTests"`
Expected: PASA (9 casos: 3 Fact + 5 InlineData del Theory + 1 Fact de clave basura). Nota: si el runtime rechaza `demasiados.puntos.aca` por Split, se cuenta como 2 partes o 3; el guard `partes.Length != 2` lo cubre → `FormatoInvalido`.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Application/Licenciamiento tests/StockApp.Application.Tests/Licenciamiento/ValidadorFirmaTests.cs
git commit -m "feat(licenciamiento): primitiva cripto ECDSA P-256, payloads, firmador y validador de firma"
```

---

### Task 2: Fingerprint por OS + almacén de licencia en archivo (Infrastructure)

**Files:**
- Create: `src/StockApp.Application/Licenciamiento/IFingerprintMaquina.cs`
- Create: `src/StockApp.Application/Licenciamiento/IAlmacenLicencia.cs`
- Create: `src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaBase.cs`
- Create: `src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaWindows.cs`
- Create: `src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaLinux.cs`
- Create: `src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaFactory.cs`
- Create: `src/StockApp.Infrastructure/Licenciamiento/AlmacenLicenciaArchivo.cs`
- Modify: `src/StockApp.Infrastructure/Platform/IUserDataPathProvider.cs`
- Modify: `src/StockApp.Infrastructure/Platform/UserDataPathProvider.cs`
- Modify: `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj` (paquete `Microsoft.Win32.Registry`)
- Test: `tests/StockApp.Infrastructure.Tests/Licenciamiento/FingerprintMaquinaTests.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Licenciamiento/AlmacenLicenciaArchivoTests.cs`

**Interfaces:**
- Consumes: `CodificadorBase64Url` NO; solo `System.Security.Cryptography.SHA256`.
- Produces:
  - `interface IFingerprintMaquina { string CodigoAgrupado { get; } }` (Application).
  - `interface IAlmacenLicencia { Task<string?> LeerAsync(); Task GuardarAsync(string licencia); }` (Application).
  - `abstract class FingerprintMaquinaBase : IFingerprintMaquina` con `protected abstract string ObtenerIdCrudo();` y `CodigoAgrupado` = SHA-256(id) en hex mayúsculas agrupado de a 4 con `-`.
  - `FingerprintMaquinaWindows`, `FingerprintMaquinaLinux`, `static FingerprintMaquinaFactory.Crear() : IFingerprintMaquina`.
  - `class AlmacenLicenciaArchivo : IAlmacenLicencia` ctor `(IUserDataPathProvider paths)`.
  - `IUserDataPathProvider.GetLicenciaPath()` + impl en `UserDataPathProvider`.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/StockApp.Infrastructure.Tests/Licenciamiento/FingerprintMaquinaTests.cs`:

```csharp
using System.Text.RegularExpressions;
using StockApp.Infrastructure.Licenciamiento;
using Xunit;

namespace StockApp.Infrastructure.Tests.Licenciamiento;

public class FingerprintMaquinaTests
{
    // Subclase de prueba: fija el id crudo para verificar el hasheo/agrupado sin depender
    // del OS real (registro / /etc/machine-id).
    private sealed class FingerprintFijo : FingerprintMaquinaBase
    {
        private readonly string _id;
        public FingerprintFijo(string id) => _id = id;
        protected override string ObtenerIdCrudo() => _id;
    }

    [Fact]
    public void CodigoAgrupado_TieneFormatoDeBloquesDe4HexMayuscula()
    {
        var fp = new FingerprintFijo("id-de-maquina-fijo");

        var codigo = fp.CodigoAgrupado;

        // SHA-256 = 64 hex → 16 bloques de 4 unidos por '-'.
        Assert.Matches(new Regex("^[0-9A-F]{4}(-[0-9A-F]{4}){15}$"), codigo);
    }

    [Fact]
    public void CodigoAgrupado_EsDeterministicoParaElMismoId()
    {
        var a = new FingerprintFijo("misma-maquina").CodigoAgrupado;
        var b = new FingerprintFijo("misma-maquina").CodigoAgrupado;

        Assert.Equal(a, b);
    }

    [Fact]
    public void CodigoAgrupado_DifiereEntreIdsDistintos()
    {
        var a = new FingerprintFijo("maquina-1").CodigoAgrupado;
        var b = new FingerprintFijo("maquina-2").CodigoAgrupado;

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CodigoAgrupado_NoContieneElIdCrudo()
    {
        var fp = new FingerprintFijo("SECRETO-machine-guid");

        Assert.DoesNotContain("SECRETO", fp.CodigoAgrupado);
    }
}
```

Crear `tests/StockApp.Infrastructure.Tests/Licenciamiento/AlmacenLicenciaArchivoTests.cs`:

```csharp
using StockApp.Infrastructure.Licenciamiento;
using StockApp.Infrastructure.Platform;
using Xunit;

namespace StockApp.Infrastructure.Tests.Licenciamiento;

public class AlmacenLicenciaArchivoTests : IDisposable
{
    private readonly string _dirTemp;
    private readonly IUserDataPathProvider _paths;

    public AlmacenLicenciaArchivoTests()
    {
        _dirTemp = Path.Combine(Path.GetTempPath(), "stockapp-lic-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_dirTemp);
        _paths = new PathsFake(_dirTemp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dirTemp)) Directory.Delete(_dirTemp, recursive: true);
    }

    private sealed class PathsFake : IUserDataPathProvider
    {
        private readonly string _dir;
        public PathsFake(string dir) => _dir = dir;
        public string GetDataDirectory() => _dir;
        public string GetDatabasePath() => Path.Combine(_dir, "stockapp.db");
        public string GetBackupsDirectory() => Path.Combine(_dir, "backups");
        public string GetLicenciaPath() => Path.Combine(_dir, "licencia.lic");
    }

    [Fact]
    public async Task LeerAsync_SinArchivo_DevuelveNull()
    {
        var almacen = new AlmacenLicenciaArchivo(_paths);

        Assert.Null(await almacen.LeerAsync());
    }

    [Fact]
    public async Task GuardarAsync_LuegoLeerAsync_DevuelveLoGuardado()
    {
        var almacen = new AlmacenLicenciaArchivo(_paths);

        await almacen.GuardarAsync("payload.firma");

        Assert.Equal("payload.firma", await almacen.LeerAsync());
    }

    [Fact]
    public async Task GuardarAsync_SobrescribeLicenciaAnterior()
    {
        var almacen = new AlmacenLicenciaArchivo(_paths);

        await almacen.GuardarAsync("vieja");
        await almacen.GuardarAsync("nueva");

        Assert.Equal("nueva", await almacen.LeerAsync());
    }

    [Fact]
    public async Task GuardarAsync_CreaElDirectorioSiNoExiste()
    {
        var subdir = Path.Combine(_dirTemp, "no", "existe");
        var almacen = new AlmacenLicenciaArchivo(new PathsFake(subdir));

        await almacen.GuardarAsync("x.y");

        Assert.Equal("x.y", await almacen.LeerAsync());
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Licenciamiento"`
Expected: FALLA de compilación — `FingerprintMaquinaBase`, `AlmacenLicenciaArchivo`, `IUserDataPathProvider.GetLicenciaPath()` no existen.

- [ ] **Step 3: Crear las interfaces en Application**

Crear `src/StockApp.Application/Licenciamiento/IFingerprintMaquina.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Huella estable de la máquina donde corre la API, presentada como código agrupado
/// (ej. A3F2-9B41-...). Nunca expone el id crudo del OS.
/// </summary>
public interface IFingerprintMaquina
{
    string CodigoAgrupado { get; }
}
```

Crear `src/StockApp.Application/Licenciamiento/IAlmacenLicencia.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>Persistencia de la licencia activa (un solo string firmado).</summary>
public interface IAlmacenLicencia
{
    /// <summary>El string de licencia persistido, o null si no hay ninguno.</summary>
    Task<string?> LeerAsync();

    /// <summary>Persiste (sobrescribe) el string de licencia.</summary>
    Task GuardarAsync(string licencia);
}
```

- [ ] **Step 4: Crear los fingerprints y el almacén en Infrastructure**

Crear `src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaBase.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using StockApp.Application.Licenciamiento;

namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>
/// Hashea el id crudo del OS con SHA-256 y lo presenta en hex mayúsculas agrupado de a 4
/// con guiones. Las subclases solo aportan de dónde sale el id crudo.
/// </summary>
public abstract class FingerprintMaquinaBase : IFingerprintMaquina
{
    public string CodigoAgrupado
    {
        get
        {
            var idCrudo = ObtenerIdCrudo();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(idCrudo));
            var hex = Convert.ToHexString(hash); // 64 chars, mayúsculas

            var sb = new StringBuilder(hex.Length + hex.Length / 4);
            for (var i = 0; i < hex.Length; i += 4)
            {
                if (i > 0) sb.Append('-');
                sb.Append(hex, i, 4);
            }
            return sb.ToString();
        }
    }

    /// <summary>Id crudo del OS (registro en Windows, /etc/machine-id en Linux).</summary>
    protected abstract string ObtenerIdCrudo();
}
```

Crear `src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaWindows.cs`:

```csharp
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>Lee HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid (id estable por instalación de Windows).</summary>
[SupportedOSPlatform("windows")]
public sealed class FingerprintMaquinaWindows : FingerprintMaquinaBase
{
    protected override string ObtenerIdCrudo()
    {
        using var key = RegistryKey
            .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");

        var guid = key?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(guid))
            throw new InvalidOperationException(
                "No se pudo leer MachineGuid del registro de Windows.");

        return guid;
    }
}
```

Crear `src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaLinux.cs`:

```csharp
namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>Lee /etc/machine-id (o /var/lib/dbus/machine-id como fallback), id estable de systemd/dbus.</summary>
public sealed class FingerprintMaquinaLinux : FingerprintMaquinaBase
{
    private static readonly string[] Rutas =
    {
        "/etc/machine-id",
        "/var/lib/dbus/machine-id",
    };

    protected override string ObtenerIdCrudo()
    {
        foreach (var ruta in Rutas)
        {
            if (File.Exists(ruta))
            {
                var id = File.ReadAllText(ruta).Trim();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
        }

        throw new InvalidOperationException(
            "No se pudo leer /etc/machine-id (ni el fallback de dbus).");
    }
}
```

Crear `src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaFactory.cs`:

```csharp
using StockApp.Application.Licenciamiento;

namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>Elige la implementación de fingerprint según el OS del servidor.</summary>
public static class FingerprintMaquinaFactory
{
    public static IFingerprintMaquina Crear()
        => OperatingSystem.IsWindows()
            ? new FingerprintMaquinaWindows()
            : new FingerprintMaquinaLinux();
}
```

Crear `src/StockApp.Infrastructure/Licenciamiento/AlmacenLicenciaArchivo.cs`:

```csharp
using StockApp.Application.Licenciamiento;
using StockApp.Infrastructure.Platform;

namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>
/// Persiste la licencia como texto plano en `licencia.lic` dentro del directorio de datos.
/// Los updates de Velopack no tocan ese directorio, así que la licencia sobrevive upgrades.
/// </summary>
public sealed class AlmacenLicenciaArchivo : IAlmacenLicencia
{
    private readonly IUserDataPathProvider _paths;

    public AlmacenLicenciaArchivo(IUserDataPathProvider paths) => _paths = paths;

    public async Task<string?> LeerAsync()
    {
        var ruta = _paths.GetLicenciaPath();
        if (!File.Exists(ruta))
            return null;

        var contenido = (await File.ReadAllTextAsync(ruta)).Trim();
        return string.IsNullOrWhiteSpace(contenido) ? null : contenido;
    }

    public async Task GuardarAsync(string licencia)
    {
        var ruta = _paths.GetLicenciaPath();
        var dir = Path.GetDirectoryName(ruta);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(ruta, licencia.Trim());
    }
}
```

- [ ] **Step 5: Agregar `GetLicenciaPath()` al path provider**

En `src/StockApp.Infrastructure/Platform/IUserDataPathProvider.cs`, agregar el miembro a la interfaz (junto a los existentes `GetDataDirectory`/`GetDatabasePath`/`GetBackupsDirectory`):

```csharp
    /// <summary>Ruta del archivo de licencia (licencia.lic) en el directorio de datos.</summary>
    string GetLicenciaPath();
```

En `src/StockApp.Infrastructure/Platform/UserDataPathProvider.cs`, agregar la constante y el método:

```csharp
    private const string LicenciaFileName = "licencia.lic";
```

```csharp
    public string GetLicenciaPath()
        => Path.Combine(GetDataDirectory(), LicenciaFileName);
```

- [ ] **Step 6: Agregar el paquete del registro de Windows**

En `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj`, dentro del `ItemGroup` de `PackageReference`, agregar:

```xml
    <PackageReference Include="Microsoft.Win32.Registry" Version="5.0.0" />
```

Gotcha conocido del repo: agregar paquetes a Infrastructure puede disparar NU1605 por pins de versión en los csproj de tests. Si `dotnet test` reporta NU1605 para `Microsoft.Win32.Registry`, alinear la versión (usar la exacta que el error indique como resuelta) en el/los csproj de test afectados. NO usar `<NoWarn>`.

- [ ] **Step 7: Correr los tests para verificar que pasan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Licenciamiento"`
Expected: PASA (4 de fingerprint + 4 de almacén = 8). Si otros tests de Infrastructure usan un `IUserDataPathProvider` fake propio, el compilador exigirá implementar `GetLicenciaPath()` en ellos — agregarlo devolviendo `Path.Combine(GetDataDirectory(), "licencia.lic")`.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Application/Licenciamiento/IFingerprintMaquina.cs \
        src/StockApp.Application/Licenciamiento/IAlmacenLicencia.cs \
        src/StockApp.Infrastructure/Licenciamiento \
        src/StockApp.Infrastructure/Platform/IUserDataPathProvider.cs \
        src/StockApp.Infrastructure/Platform/UserDataPathProvider.cs \
        src/StockApp.Infrastructure/StockApp.Infrastructure.csproj \
        tests/StockApp.Infrastructure.Tests/Licenciamiento
git commit -m "feat(licenciamiento): fingerprint por OS (registro/machine-id) y almacén de licencia en archivo"
```

---

### Task 3: `ServicioLicencia` + `EstadoLicencia` cacheado (Application)

**Files:**
- Create: `src/StockApp.Application/Licenciamiento/EstadoLicencia.cs`
- Create: `src/StockApp.Application/Licenciamiento/ResultadoValidacionLicencia.cs`
- Create: `src/StockApp.Application/Licenciamiento/ServicioLicencia.cs`
- Test: `tests/StockApp.Application.Tests/Licenciamiento/ServicioLicenciaTests.cs`

**Interfaces:**
- Consumes: `ValidadorFirma` (Task 1), `LicenciaPayload` (Task 1), `IFingerprintMaquina`, `IAlmacenLicencia` (Task 2).
- Produces:
  - `class EstadoLicencia` (singleton mutable, thread-safe): `bool Activada { get; set; }`, `string CodigoMaquina { get; set; }`.
  - `enum ResultadoValidacionLicencia { Valida, FormatoInvalido, FirmaInvalida, MaquinaDistinta }`.
  - `class ServicioLicencia`: ctor `(ValidadorFirma, IFingerprintMaquina, IAlmacenLicencia, EstadoLicencia)`; `ResultadoValidacionLicencia Validar(string licencia)`; `Task<ResultadoValidacionLicencia> ActivarAsync(string licencia)`; `Task CargarAlArranqueAsync()`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Application.Tests/Licenciamiento/ServicioLicenciaTests.cs`:

```csharp
using System.Security.Cryptography;
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Application.Tests.Licenciamiento;

public class ServicioLicenciaTests
{
    private const string Maquina = "AAAA-BBBB-CCCC";

    private sealed class FingerprintFake : IFingerprintMaquina
    {
        private readonly Func<string> _codigo;
        public FingerprintFake(string codigo) => _codigo = () => codigo;
        public FingerprintFake(Func<string> codigo) => _codigo = codigo;
        public string CodigoAgrupado => _codigo();
    }

    private sealed class AlmacenFake : IAlmacenLicencia
    {
        public string? Guardado { get; private set; }
        public AlmacenFake(string? inicial = null) => Guardado = inicial;
        public Task<string?> LeerAsync() => Task.FromResult(Guardado);
        public Task GuardarAsync(string licencia) { Guardado = licencia; return Task.CompletedTask; }
    }

    private static (ValidadorFirma validador, string privadaPem) CrearCripto()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var validador = new ValidadorFirma(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return (validador, ecdsa.ExportPkcs8PrivateKeyPem());
    }

    private static string EmitirLicencia(string privadaPem, string maquina)
        => FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "Ferretería X", maquina, "2026-07-15"), privadaPem);

    [Fact]
    public void Validar_LicenciaValidaParaEstaMaquina_DevuelveValida()
    {
        var (validador, privada) = CrearCripto();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), new EstadoLicencia());

        Assert.Equal(ResultadoValidacionLicencia.Valida,
            servicio.Validar(EmitirLicencia(privada, Maquina)));
    }

    [Fact]
    public void Validar_LicenciaDeOtraMaquina_DevuelveMaquinaDistinta()
    {
        var (validador, privada) = CrearCripto();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), new EstadoLicencia());

        Assert.Equal(ResultadoValidacionLicencia.MaquinaDistinta,
            servicio.Validar(EmitirLicencia(privada, "OTRA-MAQUINA")));
    }

    [Fact]
    public void Validar_FirmadaConOtraClave_DevuelveFirmaInvalida()
    {
        var (validador, _) = CrearCripto();
        var (_, otraPrivada) = CrearCripto();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), new EstadoLicencia());

        Assert.Equal(ResultadoValidacionLicencia.FirmaInvalida,
            servicio.Validar(EmitirLicencia(otraPrivada, Maquina)));
    }

    [Fact]
    public void Validar_FormatoRoto_DevuelveFormatoInvalido()
    {
        var (validador, _) = CrearCripto();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), new EstadoLicencia());

        Assert.Equal(ResultadoValidacionLicencia.FormatoInvalido, servicio.Validar("basura"));
    }

    [Fact]
    public async Task ActivarAsync_LicenciaValida_PersisteYActivaEstado()
    {
        var (validador, privada) = CrearCripto();
        var almacen = new AlmacenFake();
        var estado = new EstadoLicencia();
        var servicio = new ServicioLicencia(validador, new FingerprintFake(Maquina), almacen, estado);

        var licencia = EmitirLicencia(privada, Maquina);
        var resultado = await servicio.ActivarAsync(licencia);

        Assert.Equal(ResultadoValidacionLicencia.Valida, resultado);
        Assert.True(estado.Activada);
        Assert.Equal(Maquina, estado.CodigoMaquina);
        Assert.Equal(licencia, almacen.Guardado);
    }

    [Fact]
    public async Task ActivarAsync_LicenciaInvalida_NoPersisteNiActiva()
    {
        var (validador, privada) = CrearCripto();
        var almacen = new AlmacenFake();
        var estado = new EstadoLicencia();
        var servicio = new ServicioLicencia(validador, new FingerprintFake(Maquina), almacen, estado);

        var resultado = await servicio.ActivarAsync(EmitirLicencia(privada, "OTRA"));

        Assert.Equal(ResultadoValidacionLicencia.MaquinaDistinta, resultado);
        Assert.False(estado.Activada);
        Assert.Null(almacen.Guardado);
    }

    [Fact]
    public async Task CargarAlArranqueAsync_ConLicenciaValidaGuardada_DejaEstadoActivado()
    {
        var (validador, privada) = CrearCripto();
        var estado = new EstadoLicencia();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(EmitirLicencia(privada, Maquina)), estado);

        await servicio.CargarAlArranqueAsync();

        Assert.True(estado.Activada);
        Assert.Equal(Maquina, estado.CodigoMaquina);
    }

    [Fact]
    public async Task CargarAlArranqueAsync_SinLicencia_DejaEstadoBloqueadoPeroConCodigo()
    {
        var (validador, _) = CrearCripto();
        var estado = new EstadoLicencia();
        var servicio = new ServicioLicencia(
            validador, new FingerprintFake(Maquina), new AlmacenFake(), estado);

        await servicio.CargarAlArranqueAsync();

        Assert.False(estado.Activada);
        Assert.Equal(Maquina, estado.CodigoMaquina);
    }

    [Fact]
    public async Task CargarAlArranqueAsync_FingerprintIlegible_NoCrasheaYQuedaBloqueado()
    {
        var (validador, _) = CrearCripto();
        var estado = new EstadoLicencia();
        var fingerprintRoto = new FingerprintFake(() => throw new InvalidOperationException("registro ilegible"));
        var servicio = new ServicioLicencia(validador, fingerprintRoto, new AlmacenFake(), estado);

        await servicio.CargarAlArranqueAsync(); // no debe lanzar

        Assert.False(estado.Activada);
        Assert.Equal("", estado.CodigoMaquina);
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~ServicioLicenciaTests"`
Expected: FALLA de compilación — `EstadoLicencia`, `ResultadoValidacionLicencia`, `ServicioLicencia` no existen.

- [ ] **Step 3: Escribir `EstadoLicencia` y `ResultadoValidacionLicencia`**

Crear `src/StockApp.Application/Licenciamiento/EstadoLicencia.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Estado de licencia cacheado en memoria (singleton). Se calcula una vez al arranque y se
/// actualiza en la activación; el middleware de bloqueo lo lee con costo cero por request.
/// Thread-safe con lock simple (lo leen requests concurrentes).
/// </summary>
public sealed class EstadoLicencia
{
    private readonly object _lock = new();
    private bool _activada;
    private string _codigoMaquina = "";

    public bool Activada
    {
        get { lock (_lock) return _activada; }
        set { lock (_lock) _activada = value; }
    }

    public string CodigoMaquina
    {
        get { lock (_lock) return _codigoMaquina; }
        set { lock (_lock) _codigoMaquina = value; }
    }
}
```

Crear `src/StockApp.Application/Licenciamiento/ResultadoValidacionLicencia.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>Resultado de validar una licencia contra esta máquina (spec §7). Sin excepciones para flujo.</summary>
public enum ResultadoValidacionLicencia
{
    Valida,
    FormatoInvalido,
    FirmaInvalida,
    MaquinaDistinta,
}
```

- [ ] **Step 4: Escribir `ServicioLicencia`**

Crear `src/StockApp.Application/Licenciamiento/ServicioLicencia.cs`:

```csharp
using System.Text.Json;

namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Orquesta la licencia: verifica firma → deserializa → compara la máquina → decide estado.
/// SCOPED en la API (lo consumen los endpoints de licencia). El estado cacheado (singleton)
/// es lo que el middleware lee; este servicio lo actualiza al arrancar y al activar.
/// </summary>
public sealed class ServicioLicencia
{
    private readonly ValidadorFirma      _validador;
    private readonly IFingerprintMaquina _fingerprint;
    private readonly IAlmacenLicencia    _almacen;
    private readonly EstadoLicencia      _estado;

    public ServicioLicencia(
        ValidadorFirma      validador,
        IFingerprintMaquina fingerprint,
        IAlmacenLicencia    almacen,
        EstadoLicencia      estado)
    {
        _validador   = validador;
        _fingerprint = fingerprint;
        _almacen     = almacen;
        _estado      = estado;
    }

    /// <summary>Valida una licencia contra esta máquina (puro, sin efectos).</summary>
    public ResultadoValidacionLicencia Validar(string licencia)
    {
        var verificacion = _validador.Verificar(licencia, out var payloadJson);
        if (verificacion == ResultadoVerificacion.FormatoInvalido)
            return ResultadoValidacionLicencia.FormatoInvalido;
        if (verificacion == ResultadoVerificacion.FirmaInvalida)
            return ResultadoValidacionLicencia.FirmaInvalida;

        LicenciaPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicenciaPayload>(payloadJson);
        }
        catch (JsonException)
        {
            return ResultadoValidacionLicencia.FormatoInvalido;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Maquina))
            return ResultadoValidacionLicencia.FormatoInvalido;

        return payload.Maquina == _fingerprint.CodigoAgrupado
            ? ResultadoValidacionLicencia.Valida
            : ResultadoValidacionLicencia.MaquinaDistinta;
    }

    /// <summary>Valida y, si es válida, persiste la licencia y activa el estado cacheado.</summary>
    public async Task<ResultadoValidacionLicencia> ActivarAsync(string licencia)
    {
        var resultado = Validar(licencia);
        if (resultado != ResultadoValidacionLicencia.Valida)
            return resultado;

        await _almacen.GuardarAsync(licencia);
        _estado.CodigoMaquina = _fingerprint.CodigoAgrupado;
        _estado.Activada = true;
        return ResultadoValidacionLicencia.Valida;
    }

    /// <summary>
    /// Al arranque: resuelve el código de máquina y valida la licencia guardada. Si el
    /// fingerprint es ilegible (registro/machine-id inaccesible), deja el estado bloqueado
    /// con código vacío — NUNCA lanza (spec §7: la API arranca igual, en modo bloqueado).
    /// </summary>
    public async Task CargarAlArranqueAsync()
    {
        string codigo;
        try
        {
            codigo = _fingerprint.CodigoAgrupado;
        }
        catch (Exception)
        {
            _estado.CodigoMaquina = "";
            _estado.Activada = false;
            return;
        }

        _estado.CodigoMaquina = codigo;

        var licencia = await _almacen.LeerAsync();
        _estado.Activada = licencia is not null
            && Validar(licencia) == ResultadoValidacionLicencia.Valida;
    }
}
```

- [ ] **Step 5: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~ServicioLicenciaTests"`
Expected: PASA (10 casos).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Licenciamiento/EstadoLicencia.cs \
        src/StockApp.Application/Licenciamiento/ResultadoValidacionLicencia.cs \
        src/StockApp.Application/Licenciamiento/ServicioLicencia.cs \
        tests/StockApp.Application.Tests/Licenciamiento/ServicioLicenciaTests.cs
git commit -m "feat(licenciamiento): ServicioLicencia con estado cacheado y validación por máquina"
```

---

### Task 4: Endpoints `/licencia/*` + middleware 423 + wiring + ApiFactory de test

**Files:**
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs` (valores 19, 20)
- Create: `src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs`
- Create: `src/StockApp.Api/Endpoints/LicenciaEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (registros DI + carga al arranque + middleware + map)
- Create: `tests/StockApp.Api.Tests/Fixtures/ClavesDePrueba.cs`
- Create: `tests/StockApp.Api.Tests/Fixtures/FingerprintMaquinaFake.cs`
- Create: `tests/StockApp.Api.Tests/Fixtures/AlmacenLicenciaEnMemoria.cs`
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs` (config clave pública + reemplazo de fingerprint/almacén)
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs` (reset de `EstadoLicencia` por test)
- Test: `tests/StockApp.Api.Tests/Licenciamiento/BloqueoLicenciaTests.cs`
- Test: `tests/StockApp.Api.Tests/Licenciamiento/LicenciaEndpointsTests.cs`

**Interfaces:**
- Consumes: `EstadoLicencia`, `ServicioLicencia`, `ResultadoValidacionLicencia`, `IFingerprintMaquina`, `IAlmacenLicencia`, `ValidadorFirma`, `OpcionesLicencia`, `FingerprintMaquinaFactory`, `AlmacenLicenciaArchivo`, `IUsuarioRepository`, `IAuditLogger`, `RolUsuario`, `AccionAuditada`.
- Produces:
  - `AccionAuditada.ActivacionLicencia = 19`, `AccionAuditada.IntentoActivacionLicenciaFallido = 20`.
  - `class BloqueoLicenciaMiddleware` (ctor `RequestDelegate`, método `Task Invoke(HttpContext, EstadoLicencia, IProblemDetailsService)`).
  - `LicenciaEndpoints.MapLicenciaEndpoints(this IEndpointRouteBuilder)`; records `LicenciaEstadoResponse(bool Activada, string CodigoMaquina)`, `ActivarLicenciaRequest(string? Licencia)`.
  - Test doubles: `ClavesDePrueba` (con `ClavePublicaBase64`, `CodigoMaquina`, `EmitirLicencia(...)`, `ClavePrivadaPem`), `FingerprintMaquinaFake`, `AlmacenLicenciaEnMemoria`.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/StockApp.Api.Tests/Fixtures/ClavesDePrueba.cs`:

```csharp
using System.Security.Cryptography;
using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Par de claves ECDSA P-256 fijo por proceso de test: la MISMA clave configura la API
/// (pública) y firma las licencias/tokens que los tests emiten (privada). El código de
/// máquina es fijo y coincide con el que devuelve FingerprintMaquinaFake.
/// </summary>
public static class ClavesDePrueba
{
    public const string CodigoMaquina = "TEST-MAQUINA-0001";

    private static readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static string ClavePublicaBase64 { get; } =
        Convert.ToBase64String(_ecdsa.ExportSubjectPublicKeyInfo());

    public static string ClavePrivadaPem { get; } = _ecdsa.ExportPkcs8PrivateKeyPem();

    public static string EmitirLicencia(string maquina = CodigoMaquina, string cliente = "Ferretería Test")
        => FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, cliente, maquina, "2026-07-15"), ClavePrivadaPem);

    public static string EmitirTokenReset(string desafio, string maquina = CodigoMaquina)
        => FirmadorLicencias.EmitirTokenReset(
            new TokenResetPayload(1, "reset-admin", maquina, desafio), ClavePrivadaPem);
}
```

Crear `tests/StockApp.Api.Tests/Fixtures/FingerprintMaquinaFake.cs`:

```csharp
using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>Fingerprint fijo para tests: nunca toca el registro / machine-id de la máquina real.</summary>
public sealed class FingerprintMaquinaFake : IFingerprintMaquina
{
    public string CodigoAgrupado => ClavesDePrueba.CodigoMaquina;
}
```

Crear `tests/StockApp.Api.Tests/Fixtures/AlmacenLicenciaEnMemoria.cs`:

```csharp
using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>Almacén de licencia en memoria para tests (sin tocar el filesystem del server).</summary>
public sealed class AlmacenLicenciaEnMemoria : IAlmacenLicencia
{
    private string? _licencia;
    public AlmacenLicenciaEnMemoria(string? inicial = null) => _licencia = inicial;
    public Task<string?> LeerAsync() => Task.FromResult(_licencia);
    public Task GuardarAsync(string licencia) { _licencia = licencia; return Task.CompletedTask; }
}
```

Crear `tests/StockApp.Api.Tests/Licenciamiento/BloqueoLicenciaTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Api.Tests.Licenciamiento;

public class BloqueoLicenciaTests : ApiTestBase
{
    public BloqueoLicenciaTests(ApiFactory factory) : base(factory) { }

    private void Bloquear()
        => Factory.Services.GetRequiredService<EstadoLicencia>().Activada = false;

    [Fact]
    public async Task Bloqueada_EndpointNormal_Devuelve423()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task Bloqueada_Login_Devuelve423()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "x", Contrasena = "y" });

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task Bloqueada_EstadoDeLicencia_Pasa()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/licencia/estado");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Bloqueada_DesafioDeReset_Pasa()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.PostAsync("/auth/reset-admin/desafio", content: null);

        // El endpoint de reset se agrega en Task 5; acá sólo importa que el middleware NO lo bloquee.
        // Antes de Task 5 devolverá 404 (ruta inexistente), NO 423.
        Assert.NotEqual((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task Activada_EndpointNormal_NoDevuelve423()
    {
        // ApiTestBase deja Activada=true por defecto.
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        // Sin token → 401, pero NO 423 (la licencia está activa).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

Crear `tests/StockApp.Api.Tests/Licenciamiento/LicenciaEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Licenciamiento;

public class LicenciaEndpointsTests : ApiTestBase
{
    public LicenciaEndpointsTests(ApiFactory factory) : base(factory) { }

    private void Bloquear()
        => Factory.Services.GetRequiredService<EstadoLicencia>().Activada = false;

    [Fact]
    public async Task Estado_DevuelveCodigoDeMaquina()
    {
        var client = Factory.CreateClient();

        var estado = await client.GetFromJsonAsync<LicenciaEstadoResponse>("/licencia/estado");

        Assert.Equal(ClavesDePrueba.CodigoMaquina, estado!.CodigoMaquina);
    }

    [Fact]
    public async Task Activar_LicenciaValida_ActivaYDevuelveEstado()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/licencia/activar",
            new ActivarLicenciaRequest(ClavesDePrueba.EmitirLicencia()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var estado = await response.Content.ReadFromJsonAsync<LicenciaEstadoResponse>();
        Assert.True(estado!.Activada);

        // Tras activar, un endpoint normal ya no da 423.
        Assert.NotEqual((HttpStatusCode)423,
            (await client.GetAsync("/productos")).StatusCode);
    }

    [Fact]
    public async Task Activar_LicenciaDeOtraMaquina_Devuelve400YSigueBloqueada()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/licencia/activar",
            new ActivarLicenciaRequest(ClavesDePrueba.EmitirLicencia(maquina: "OTRA-MAQUINA")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((HttpStatusCode)423, (await client.GetAsync("/productos")).StatusCode);
    }

    [Fact]
    public async Task Activar_Exitosa_QuedaAuditada()
    {
        // Sembrar un admin para que el evento de licencia se pueda atribuir.
        await SembrarAdminAsync();
        Bloquear();
        var client = Factory.CreateClient();

        await client.PostAsJsonAsync("/licencia/activar",
            new ActivarLicenciaRequest(ClavesDePrueba.EmitirLicencia()));

        using var ctx = Factory.CrearContexto();
        var hayEvento = await ctx.LogsAuditoria
            .AnyAsync(l => l.Accion == AccionAuditada.ActivacionLicencia);
        Assert.True(hayEvento);
    }

    private async Task SembrarAdminAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var primerArranque = scope.ServiceProvider
            .GetRequiredService<StockApp.Application.Auth.IPrimerArranqueService>();
        await primerArranque.CrearAdminInicialAsync("admin-lic", "clave-lic-123");
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~Licenciamiento"`
Expected: FALLA de compilación — faltan `LicenciaEstadoResponse`, `ActivarLicenciaRequest`, el middleware, los registros DI y los cambios de `ApiFactory`/`ApiTestBase`.

- [ ] **Step 3: Agregar los valores de auditoría de licencia**

En `src/StockApp.Domain/Enums/AccionAuditada.cs`, agregar al final del enum (append-only, después de `RecalculoStock = 18`):

```csharp

    // ── Licenciamiento / Reset — Incremento 7 Fase B (append-only a partir de 19) ──
    ActivacionLicencia               = 19,
    IntentoActivacionLicenciaFallido = 20,
```

- [ ] **Step 4: Escribir el middleware de bloqueo**

Crear `src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs`:

```csharp
using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Licenciamiento;

/// <summary>
/// Sin licencia activa, TODO devuelve 423 Locked salvo /licencia/* y /auth/reset-admin/*
/// (los flujos pre-login de activación y recuperación). El login incluido. El estado se lee
/// del singleton EstadoLicencia — costo cero por request cuando la licencia está activa.
/// </summary>
public sealed class BloqueoLicenciaMiddleware
{
    private const int StatusLocked = 423;
    private readonly RequestDelegate _next;

    public BloqueoLicenciaMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(
        HttpContext context, EstadoLicencia estado, IProblemDetailsService problemDetails)
    {
        if (estado.Activada || EsRutaPermitida(context.Request.Path))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusLocked;
        context.Response.ContentType = "application/problem+json";
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusLocked,
                Title  = "Licencia no activada.",
                Detail = "El servidor no tiene una licencia válida activada. "
                       + "Activá la licencia desde la pantalla de bloqueo del cliente.",
            },
        });
    }

    private static bool EsRutaPermitida(PathString path)
        => path.StartsWithSegments("/licencia")
        || path.StartsWithSegments("/auth/reset-admin");
}
```

- [ ] **Step 5: Escribir los endpoints de licencia**

Crear `src/StockApp.Api/Endpoints/LicenciaEndpoints.cs`:

```csharp
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record LicenciaEstadoResponse(bool Activada, string CodigoMaquina);
public record ActivarLicenciaRequest(string? Licencia);

public static class LicenciaEndpoints
{
    public static IEndpointRouteBuilder MapLicenciaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/licencia");

        // Anónimo por diseño: es pre-login. El código de máquina es público.
        group.MapGet("/estado", (EstadoLicencia estado) =>
            Results.Ok(new LicenciaEstadoResponse(estado.Activada, estado.CodigoMaquina)));

        group.MapPost("/activar", async (
            ActivarLicenciaRequest request,
            ServicioLicencia servicio,
            EstadoLicencia estado,
            IUsuarioRepository usuarios,
            IAuditLogger audit) =>
        {
            if (string.IsNullOrWhiteSpace(request.Licencia))
                return Results.Problem(
                    title: "La licencia es obligatoria.",
                    statusCode: StatusCodes.Status400BadRequest);

            var resultado = await servicio.ActivarAsync(request.Licencia);

            if (resultado == ResultadoValidacionLicencia.Valida)
            {
                await AuditarAsync(usuarios, audit,
                    AccionAuditada.ActivacionLicencia, "Activación de licencia exitosa.");
                return Results.Ok(new LicenciaEstadoResponse(estado.Activada, estado.CodigoMaquina));
            }

            await AuditarAsync(usuarios, audit,
                AccionAuditada.IntentoActivacionLicenciaFallido,
                $"Intento de activación fallido: {resultado}.");

            return Results.Problem(
                title: MotivoDe(resultado),
                statusCode: StatusCodes.Status400BadRequest);
        });

        return app;
    }

    private static string MotivoDe(ResultadoValidacionLicencia resultado) => resultado switch
    {
        ResultadoValidacionLicencia.FormatoInvalido => "El texto de la licencia no tiene un formato válido.",
        ResultadoValidacionLicencia.FirmaInvalida   => "La firma de la licencia no es válida.",
        ResultadoValidacionLicencia.MaquinaDistinta => "La licencia fue emitida para otra máquina.",
        _ => "Licencia inválida.",
    };

    // Los eventos de licencia son anónimos (pre-login). LogAuditoria.UsuarioId es FK requerida:
    // se atribuye al primer admin (menor Id). Si no hay admin todavía, no se audita en DB.
    private static async Task AuditarAsync(
        IUsuarioRepository usuarios, IAuditLogger audit, AccionAuditada accion, string detalle)
    {
        var todos = await usuarios.ListarTodosAsync();
        var admin = todos
            .Where(u => u.Rol == RolUsuario.Admin)
            .OrderBy(u => u.Id)
            .FirstOrDefault();

        if (admin is not null)
            await audit.RegistrarAsync(admin.Id, accion, "Licencia", 0, detalle);
    }
}
```

- [ ] **Step 6: Cablear en `Program.cs`**

En `src/StockApp.Api/Program.cs`:

(a) Agregar los `using` que falten al tope del archivo:

```csharp
using StockApp.Api.Licenciamiento;
using StockApp.Application.Licenciamiento;
using StockApp.Infrastructure.Licenciamiento;
using StockApp.Infrastructure.Platform;
```

(b) Registrar los servicios de licencia. Después del registro de `IPrimerArranqueService` (línea 107 actual, `builder.Services.AddScoped<IPrimerArranqueService, PrimerArranqueService>();`), agregar:

```csharp
// Licenciamiento (Inc 7 Fase B). La clave pública se lee de config (Licencia:ClavePublicaBase64)
// con la constante embebida como fallback. EstadoLicencia/fingerprint/almacén/validador son
// SINGLETON (estables por proceso); ServicioLicencia es SCOPED. IUserDataPathProvider lo usa
// AlmacenLicenciaArchivo para persistir licencia.lic en el directorio de datos del server.
builder.Services.AddSingleton<IUserDataPathProvider, UserDataPathProvider>();
builder.Services.AddSingleton<IFingerprintMaquina>(_ => FingerprintMaquinaFactory.Crear());
builder.Services.AddSingleton<IAlmacenLicencia, AlmacenLicenciaArchivo>();
builder.Services.AddSingleton<EstadoLicencia>();
builder.Services.AddSingleton(sp =>
{
    var clavePublica = sp.GetRequiredService<IConfiguration>()["Licencia:ClavePublicaBase64"]
        ?? OpcionesLicencia.ClavePublicaBase64Default;
    return new ValidadorFirma(clavePublica);
});
builder.Services.AddScoped<ServicioLicencia>();
```

(c) En el scope de arranque (bloque `using (var scope = app.Services.CreateScope())`, actual líneas 221-234), después de `await seeder.SembrarAsync();`, agregar la carga de licencia:

```csharp
    // Cargar el estado de licencia al arranque (Inc 7 Fase B): resuelve el código de máquina
    // y valida licencia.lic. Nunca lanza — si no hay licencia válida, la API arranca bloqueada.
    var servicioLicencia = scope.ServiceProvider.GetRequiredService<ServicioLicencia>();
    await servicioLicencia.CargarAlArranqueAsync();
```

(d) Insertar el middleware ANTES de `app.UseAuthentication();` (línea 241 actual). Justo después de `app.UseExceptionHandler();`:

```csharp
// Bloqueo por licencia (Inc 7 Fase B): 423 Locked a todo salvo /licencia/* y /auth/reset-admin/*
// cuando no hay licencia activa. Va antes de autenticación (bloquea incluso el login).
app.UseMiddleware<BloqueoLicenciaMiddleware>();
```

(e) Mapear los endpoints. Junto a los `app.MapAuthEndpoints();` etc. (línea 246 actual), agregar:

```csharp
app.MapLicenciaEndpoints();
```

- [ ] **Step 7: Ajustar `ApiFactory` para inyectar fingerprint/almacén de test y la clave pública**

En `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs`:

(a) Agregar los `using`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockApp.Application.Licenciamiento;
```

(b) En `ConfigureWebHost`, agregar la clave pública de test al diccionario de configuración (junto a `Jwt:Secret`):

```csharp
                ["Licencia:ClavePublicaBase64"] = ClavesDePrueba.ClavePublicaBase64,
```

(c) En `ConfigureWebHost`, después del `ConfigureAppConfiguration(...)`, agregar el reemplazo de servicios: fingerprint fake + almacén en memoria PRECARGADO con una licencia válida (así la licencia arranca ACTIVA por defecto y ningún test existente se rompe con 423):

```csharp
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IFingerprintMaquina, FingerprintMaquinaFake>());
            services.Replace(ServiceDescriptor.Singleton<IAlmacenLicencia>(
                _ => new AlmacenLicenciaEnMemoria(ClavesDePrueba.EmitirLicencia())));
        });
```

- [ ] **Step 8: Resetear `EstadoLicencia` por test en `ApiTestBase`**

En `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`, agregar los `using` y, en el constructor, después de `LimpiarTablas();`, restaurar la licencia a activa (aislamiento entre tests que la bloquean):

```csharp
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Licenciamiento;
```

En el constructor:

```csharp
    protected ApiTestBase(ApiFactory factory)
    {
        Factory = factory;
        LimpiarTablas();

        // Cada test arranca con la licencia ACTIVA (algunos la bloquean explícitamente).
        // El EstadoLicencia es singleton y se comparte en la collection: restaurarlo evita
        // que un test de modo-bloqueado filtre estado a los demás.
        Factory.Services.GetRequiredService<EstadoLicencia>().Activada = true;
    }
```

- [ ] **Step 9: Correr los tests para verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASA toda la suite de Api.Tests, incluidos los nuevos (`BloqueoLicenciaTests`, `LicenciaEndpointsTests`) y TODOS los existentes (la ApiFactory arranca activada, así que no aparecen 423 inesperados). El test `Bloqueada_DesafioDeReset_Pasa` espera 404 (la ruta de reset se agrega en Task 5) — lo importante es que NO sea 423.

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Domain/Enums/AccionAuditada.cs \
        src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs \
        src/StockApp.Api/Endpoints/LicenciaEndpoints.cs \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Api.Tests/Fixtures/ClavesDePrueba.cs \
        tests/StockApp.Api.Tests/Fixtures/FingerprintMaquinaFake.cs \
        tests/StockApp.Api.Tests/Fixtures/AlmacenLicenciaEnMemoria.cs \
        tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs \
        tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs \
        tests/StockApp.Api.Tests/Licenciamiento
git commit -m "feat(api): endpoints /licencia/* y middleware 423 de bloqueo por licencia"
```

---

### Task 5: Desafío de reset + endpoints `/auth/reset-admin/*` + recreación de Admin + auditoría

**Files:**
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs` (valor 21)
- Create: `src/StockApp.Application/Licenciamiento/IAlmacenDesafiosReset.cs`
- Create: `src/StockApp.Application/Licenciamiento/AlmacenDesafiosResetEnMemoria.cs`
- Create: `src/StockApp.Application/Licenciamiento/ResultadoValidacionReset.cs`
- Create: `src/StockApp.Application/Licenciamiento/ServicioResetAdmin.cs`
- Create: `src/StockApp.Api/Endpoints/ResetAdminEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (registros DI + map)
- Test: `tests/StockApp.Application.Tests/Licenciamiento/AlmacenDesafiosResetTests.cs`
- Test: `tests/StockApp.Application.Tests/Licenciamiento/ServicioResetAdminTests.cs`
- Test: `tests/StockApp.Api.Tests/Licenciamiento/ResetAdminEndpointsTests.cs`

**Interfaces:**
- Consumes: `ValidadorFirma`, `TokenResetPayload`, `IFingerprintMaquina`, `EstadoLicencia`, `IUsuarioRepository`, `IPasswordHasher`, `IPrimerArranqueService`, `IAuditLogger`, `RolUsuario`, `AccionAuditada`, `ContrasenaValidator` (internal a Application).
- Produces:
  - `enum ResultadoDesafio { Valido, Inexistente, Expirado }`.
  - `interface IAlmacenDesafiosReset { string GenerarNuevo(); ResultadoDesafio Consumir(string desafio); }`.
  - `class AlmacenDesafiosResetEnMemoria : IAlmacenDesafiosReset` ctor `(TimeSpan? ttl = null, Func<DateTime>? ahora = null)`.
  - `enum ResultadoValidacionReset { Valido, FormatoInvalido, FirmaInvalido, MaquinaDistinta, AccionInvalida, DesafioInvalido, DesafioExpirado }`.
  - `class ServicioResetAdmin`: ctor `(ValidadorFirma, IFingerprintMaquina, IAlmacenDesafiosReset, IUsuarioRepository, IPasswordHasher, IPrimerArranqueService, IAuditLogger)`; `Task<ResultadoValidacionReset> ResetearAsync(string token, string nuevaContrasena)`.
  - `ResetAdminEndpoints.MapResetAdminEndpoints(this IEndpointRouteBuilder)`; records `ResetDesafioResponse(string Desafio, string CodigoMaquina)`, `ResetAdminRequest(string? Token, string? NuevaContrasena)`.
  - `AccionAuditada.ResetAdminFirmado = 21`.

- [ ] **Step 1: Escribir los tests que fallan (Application)**

Crear `tests/StockApp.Application.Tests/Licenciamiento/AlmacenDesafiosResetTests.cs`:

```csharp
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Application.Tests.Licenciamiento;

public class AlmacenDesafiosResetTests
{
    [Fact]
    public void GenerarNuevo_DevuelveNonceNoVacio()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();

        var desafio = almacen.GenerarNuevo();

        Assert.False(string.IsNullOrWhiteSpace(desafio));
    }

    [Fact]
    public void GenerarNuevo_DosVeces_DaNoncesDistintos()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();

        Assert.NotEqual(almacen.GenerarNuevo(), almacen.GenerarNuevo());
    }

    [Fact]
    public void Consumir_DesafioVivo_DevuelveValido()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();
        var desafio = almacen.GenerarNuevo();

        Assert.Equal(ResultadoDesafio.Valido, almacen.Consumir(desafio));
    }

    [Fact]
    public void Consumir_DosVecesElMismo_LaSegundaEsInexistente()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();
        var desafio = almacen.GenerarNuevo();

        almacen.Consumir(desafio);

        Assert.Equal(ResultadoDesafio.Inexistente, almacen.Consumir(desafio));
    }

    [Fact]
    public void GenerarNuevo_InvalidaElAnterior()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();
        var primero = almacen.GenerarNuevo();
        almacen.GenerarNuevo(); // reemplaza al primero

        Assert.Equal(ResultadoDesafio.Inexistente, almacen.Consumir(primero));
    }

    [Fact]
    public void Consumir_Desconocido_DevuelveInexistente()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();
        almacen.GenerarNuevo();

        Assert.Equal(ResultadoDesafio.Inexistente, almacen.Consumir("no-existe"));
    }

    [Fact]
    public void Consumir_DesafioExpirado_DevuelveExpirado()
    {
        var ahora = new DateTime(2026, 07, 15, 10, 0, 0, DateTimeKind.Utc);
        var reloj = ahora;
        var almacen = new AlmacenDesafiosResetEnMemoria(
            ttl: TimeSpan.FromHours(24), ahora: () => reloj);

        var desafio = almacen.GenerarNuevo();
        reloj = ahora.AddHours(25); // pasó el TTL

        Assert.Equal(ResultadoDesafio.Expirado, almacen.Consumir(desafio));
    }
}
```

Crear `tests/StockApp.Application.Tests/Licenciamiento/ServicioResetAdminTests.cs`:

```csharp
using System.Security.Cryptography;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Licenciamiento;

public class ServicioResetAdminTests
{
    private const string Maquina = "MAQ-RESET-1";

    // ── Fakes ────────────────────────────────────────────────────────────────
    private sealed class FingerprintFake : IFingerprintMaquina
    {
        public string CodigoAgrupado => Maquina;
    }

    private sealed class HasherFake : IPasswordHasher
    {
        public string Hash(string plana) => "HASH:" + plana;
        public bool Verify(string plana, string hash) => hash == "HASH:" + plana;
    }

    private sealed class AuditFake : IAuditLogger
    {
        public List<(int usuarioId, AccionAuditada accion)> Eventos { get; } = new();
        public Task RegistrarAsync(int usuarioId, AccionAuditada accion, string entidad, int entidadId, string detalle)
        {
            Eventos.Add((usuarioId, accion));
            return Task.CompletedTask;
        }
    }

    private sealed class UsuarioRepoFake : IUsuarioRepository
    {
        public List<Usuario> Usuarios { get; } = new();
        private int _seq = 1;

        public Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario)
            => Task.FromResult(Usuarios.FirstOrDefault(u => u.NombreUsuario == nombreUsuario));
        public Task<Usuario?> ObtenerPorIdAsync(int id)
            => Task.FromResult(Usuarios.FirstOrDefault(u => u.Id == id));
        public Task<IReadOnlyList<Usuario>> ListarTodosAsync()
            => Task.FromResult((IReadOnlyList<Usuario>)Usuarios.ToList());
        public Task<bool> ExisteAlgunUsuarioAsync() => Task.FromResult(Usuarios.Count > 0);
        public Task<int> ContarAdminsActivosAsync()
            => Task.FromResult(Usuarios.Count(u => u.Rol == RolUsuario.Admin && u.Activo));
        public Task<int> AgregarAsync(Usuario usuario)
        {
            usuario.Id = _seq++;
            Usuarios.Add(usuario);
            return Task.FromResult(usuario.Id);
        }
        public Task ActualizarAsync(Usuario usuario) => Task.CompletedTask;
        public Task ActualizarUltimoAccesoAsync(int usuarioId, DateTime fechaAcceso) => Task.CompletedTask;
    }

    private sealed class Contexto
    {
        public required ServicioResetAdmin Servicio { get; init; }
        public required IAlmacenDesafiosReset Desafios { get; init; }
        public required UsuarioRepoFake Repo { get; init; }
        public required AuditFake Audit { get; init; }
        public required string PrivadaPem { get; init; }
    }

    private static Contexto Armar(bool conAdmin)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var validador = new ValidadorFirma(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        var privada = ecdsa.ExportPkcs8PrivateKeyPem();

        var repo = new UsuarioRepoFake();
        var hasher = new HasherFake();
        if (conAdmin)
            repo.Usuarios.Add(new Usuario
            {
                Id = 7, NombreUsuario = "admin", HashContrasena = "HASH:vieja",
                Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow,
            });

        var desafios = new AlmacenDesafiosResetEnMemoria();
        var audit = new AuditFake();
        var primerArranque = new PrimerArranqueService(repo, hasher);
        var servicio = new ServicioResetAdmin(
            validador, new FingerprintFake(), desafios, repo, hasher, primerArranque, audit);

        return new Contexto
        {
            Servicio = servicio, Desafios = desafios, Repo = repo, Audit = audit, PrivadaPem = privada,
        };
    }

    private static string Token(string privada, string desafio, string maquina = Maquina, string accion = "reset-admin")
        => FirmadorLicencias.EmitirTokenReset(
            new TokenResetPayload(1, accion, maquina, desafio), privada);

    [Fact]
    public async Task Resetear_TokenValido_CambiaLaContrasenaDelAdminYAudita()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();

        var resultado = await c.Servicio.ResetearAsync(Token(c.PrivadaPem, desafio), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.Valido, resultado);
        Assert.Equal("HASH:nueva-clave-123", c.Repo.Usuarios.Single().HashContrasena);
        Assert.Contains(c.Audit.Eventos, e => e.accion == AccionAuditada.ResetAdminFirmado && e.usuarioId == 7);
    }

    [Fact]
    public async Task Resetear_AdminInactivo_LoReactiva()
    {
        var c = Armar(conAdmin: true);
        c.Repo.Usuarios.Single().Activo = false;
        var desafio = c.Desafios.GenerarNuevo();

        await c.Servicio.ResetearAsync(Token(c.PrivadaPem, desafio), "nueva-clave-123");

        Assert.True(c.Repo.Usuarios.Single().Activo);
    }

    [Fact]
    public async Task Resetear_SinAdmin_RecreaAdminViaPrimerArranque()
    {
        var c = Armar(conAdmin: false);
        var desafio = c.Desafios.GenerarNuevo();

        var resultado = await c.Servicio.ResetearAsync(Token(c.PrivadaPem, desafio), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.Valido, resultado);
        var admin = c.Repo.Usuarios.Single(u => u.Rol == RolUsuario.Admin);
        Assert.Equal("HASH:nueva-clave-123", admin.HashContrasena);
    }

    [Fact]
    public async Task Resetear_TokenReusado_LaSegundaVezDaDesafioInvalido()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();
        var token = Token(c.PrivadaPem, desafio);

        await c.Servicio.ResetearAsync(token, "nueva-clave-123");
        var segunda = await c.Servicio.ResetearAsync(token, "otra-clave-456");

        Assert.Equal(ResultadoValidacionReset.DesafioInvalido, segunda);
    }

    [Fact]
    public async Task Resetear_MaquinaDistinta_DevuelveMaquinaDistinta()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();

        var resultado = await c.Servicio.ResetearAsync(
            Token(c.PrivadaPem, desafio, maquina: "OTRA"), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.MaquinaDistinta, resultado);
    }

    [Fact]
    public async Task Resetear_AccionEquivocada_DevuelveAccionInvalida()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();

        var resultado = await c.Servicio.ResetearAsync(
            Token(c.PrivadaPem, desafio, accion: "otra-cosa"), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.AccionInvalida, resultado);
    }

    [Fact]
    public async Task Resetear_DesafioDesconocido_DevuelveDesafioInvalido()
    {
        var c = Armar(conAdmin: true);
        c.Desafios.GenerarNuevo(); // hay uno vivo, pero el token trae otro

        var resultado = await c.Servicio.ResetearAsync(
            Token(c.PrivadaPem, "nonce-que-no-existe"), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.DesafioInvalido, resultado);
    }

    [Fact]
    public async Task Resetear_FirmaInvalida_DevuelveFirmaInvalido()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();
        using var otra = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var tokenMalFirmado = Token(otra.ExportPkcs8PrivateKeyPem(), desafio);

        var resultado = await c.Servicio.ResetearAsync(tokenMalFirmado, "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.FirmaInvalido, resultado);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~Reset"`
Expected: FALLA de compilación — faltan `AlmacenDesafiosResetEnMemoria`, `ResultadoDesafio`, `ResultadoValidacionReset`, `ServicioResetAdmin`.

- [ ] **Step 3: Escribir el almacén de desafíos y sus enums**

Crear `src/StockApp.Application/Licenciamiento/IAlmacenDesafiosReset.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>Resultado de consumir un desafío de reset.</summary>
public enum ResultadoDesafio { Valido, Inexistente, Expirado }

/// <summary>
/// Custodia el nonce de reset: uno solo activo a la vez, con TTL, de un solo uso.
/// En memoria (se pierde al reiniciar la API, que es justo lo que se quiere: no persiste desafíos).
/// </summary>
public interface IAlmacenDesafiosReset
{
    /// <summary>Genera un nonce nuevo e invalida cualquiera anterior. Devuelve el nonce.</summary>
    string GenerarNuevo();

    /// <summary>Consume el nonce si está vivo (lo elimina). Informa si no existe o expiró.</summary>
    ResultadoDesafio Consumir(string desafio);
}
```

Crear `src/StockApp.Application/Licenciamiento/AlmacenDesafiosResetEnMemoria.cs`:

```csharp
using System.Security.Cryptography;

namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Implementación en memoria del custodio de desafíos. Thread-safe con lock. TTL por defecto
/// 24 h. El reloj es inyectable para testear la expiración sin esperas reales.
/// </summary>
public sealed class AlmacenDesafiosResetEnMemoria : IAlmacenDesafiosReset
{
    private readonly object _lock = new();
    private readonly TimeSpan _ttl;
    private readonly Func<DateTime> _ahora;

    private string? _desafio;
    private DateTime _expira;

    public AlmacenDesafiosResetEnMemoria(TimeSpan? ttl = null, Func<DateTime>? ahora = null)
    {
        _ttl   = ttl ?? TimeSpan.FromHours(24);
        _ahora = ahora ?? (() => DateTime.UtcNow);
    }

    public string GenerarNuevo()
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_lock)
        {
            _desafio = nonce;
            _expira  = _ahora() + _ttl;
        }
        return nonce;
    }

    public ResultadoDesafio Consumir(string desafio)
    {
        lock (_lock)
        {
            if (_desafio is null || _desafio != desafio)
                return ResultadoDesafio.Inexistente;

            if (_ahora() > _expira)
            {
                _desafio = null;
                return ResultadoDesafio.Expirado;
            }

            _desafio = null; // un solo uso
            return ResultadoDesafio.Valido;
        }
    }
}
```

Crear `src/StockApp.Application/Licenciamiento/ResultadoValidacionReset.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>Resultado de validar un token de reset de Admin (spec §5.1, §7). Sin excepciones para flujo.</summary>
public enum ResultadoValidacionReset
{
    Valido,
    FormatoInvalido,
    FirmaInvalido,
    MaquinaDistinta,
    AccionInvalida,
    DesafioInvalido,
    DesafioExpirado,
}
```

- [ ] **Step 4: Escribir `ServicioResetAdmin`**

Crear `src/StockApp.Application/Licenciamiento/ServicioResetAdmin.cs`:

```csharp
using System.Linq;
using System.Text.Json;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;

namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Valida un token de reset firmado y, si es correcto, resetea la contraseña del Admin (o lo
/// recrea si no queda ninguno) y audita. SCOPED: audita y toca el repositorio.
/// Propiedades: un solo uso (el desafío muere al usarse), no transferible (atado al fingerprint),
/// no pre-generable (el desafío nace en esta máquina en ese momento).
/// </summary>
public sealed class ServicioResetAdmin
{
    private const string AccionEsperada = "reset-admin";

    private readonly ValidadorFirma        _validador;
    private readonly IFingerprintMaquina   _fingerprint;
    private readonly IAlmacenDesafiosReset _desafios;
    private readonly IUsuarioRepository    _usuarios;
    private readonly IPasswordHasher       _hasher;
    private readonly IPrimerArranqueService _primerArranque;
    private readonly IAuditLogger          _audit;

    public ServicioResetAdmin(
        ValidadorFirma         validador,
        IFingerprintMaquina    fingerprint,
        IAlmacenDesafiosReset  desafios,
        IUsuarioRepository     usuarios,
        IPasswordHasher        hasher,
        IPrimerArranqueService primerArranque,
        IAuditLogger           audit)
    {
        _validador      = validador;
        _fingerprint    = fingerprint;
        _desafios       = desafios;
        _usuarios       = usuarios;
        _hasher         = hasher;
        _primerArranque = primerArranque;
        _audit          = audit;
    }

    public async Task<ResultadoValidacionReset> ResetearAsync(string token, string nuevaContrasena)
    {
        var verificacion = _validador.Verificar(token, out var payloadJson);
        if (verificacion == ResultadoVerificacion.FormatoInvalido)
            return ResultadoValidacionReset.FormatoInvalido;
        if (verificacion == ResultadoVerificacion.FirmaInvalida)
            return ResultadoValidacionReset.FirmaInvalido;

        TokenResetPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TokenResetPayload>(payloadJson);
        }
        catch (JsonException)
        {
            return ResultadoValidacionReset.FormatoInvalido;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Desafio))
            return ResultadoValidacionReset.FormatoInvalido;

        if (payload.Accion != AccionEsperada)
            return ResultadoValidacionReset.AccionInvalida;

        if (payload.Maquina != _fingerprint.CodigoAgrupado)
            return ResultadoValidacionReset.MaquinaDistinta;

        // La contraseña se valida ANTES de consumir el desafío: si es inválida, no quemamos
        // el nonce. ArgumentException burbujea al endpoint (400 vía DomainExceptionHandler).
        ContrasenaValidator.Validar(nuevaContrasena);

        var consumo = _desafios.Consumir(payload.Desafio);
        if (consumo == ResultadoDesafio.Inexistente)
            return ResultadoValidacionReset.DesafioInvalido;
        if (consumo == ResultadoDesafio.Expirado)
            return ResultadoValidacionReset.DesafioExpirado;

        var adminId = await AplicarResetAsync(nuevaContrasena);

        await _audit.RegistrarAsync(
            adminId, AccionAuditada.ResetAdminFirmado, "Usuario", adminId,
            "Reset de contraseña de Admin vía token firmado.");

        return ResultadoValidacionReset.Valido;
    }

    private async Task<int> AplicarResetAsync(string nuevaContrasena)
    {
        var todos = await _usuarios.ListarTodosAsync();
        var admin = todos
            .Where(u => u.Rol == RolUsuario.Admin)
            .OrderBy(u => u.Id)
            .FirstOrDefault();

        if (admin is not null)
        {
            admin.HashContrasena = _hasher.Hash(nuevaContrasena);
            admin.Activo = true; // recuperación: si estaba deshabilitado, se reactiva
            await _usuarios.ActualizarAsync(admin);
            return admin.Id;
        }

        // No queda ningún Admin: recrear vía PrimerArranqueService (username fijo "admin").
        await _primerArranque.CrearAdminInicialAsync("admin", nuevaContrasena);
        var recreado = await _usuarios.BuscarPorNombreAsync("admin");
        return recreado!.Id;
    }
}
```

- [ ] **Step 5: Escribir los endpoints de reset (test de integración primero)**

Crear `tests/StockApp.Api.Tests/Licenciamiento/ResetAdminEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Licenciamiento;

public class ResetAdminEndpointsTests : ApiTestBase
{
    public ResetAdminEndpointsTests(ApiFactory factory) : base(factory) { }

    private async Task SembrarAdminAsync(string usuario, string contrasena)
    {
        using var scope = Factory.Services.CreateScope();
        var primerArranque = scope.ServiceProvider.GetRequiredService<IPrimerArranqueService>();
        await primerArranque.CrearAdminInicialAsync(usuario, contrasena);
    }

    [Fact]
    public async Task Desafio_DevuelveDesafioYCodigoDeMaquina()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsync("/auth/reset-admin/desafio", content: null);
        var body = await response.Content.ReadFromJsonAsync<ResetDesafioResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body!.Desafio));
        Assert.Equal(ClavesDePrueba.CodigoMaquina, body.CodigoMaquina);
    }

    [Fact]
    public async Task Reset_FlujoCompleto_CambiaLaContrasenaYPermiteLogin()
    {
        await SembrarAdminAsync("admin", "clave-vieja-1");
        var client = Factory.CreateClient();

        var desafio = (await (await client.PostAsync("/auth/reset-admin/desafio", null))
            .Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;
        var token = ClavesDePrueba.EmitirTokenReset(desafio);

        var reset = await client.PostAsJsonAsync("/auth/reset-admin",
            new ResetAdminRequest(token, "clave-nueva-9"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // La vieja ya no sirve; la nueva loguea.
        var loginViejo = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "admin", Contrasena = "clave-vieja-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginViejo.StatusCode);

        var loginNuevo = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "admin", Contrasena = "clave-nueva-9" });
        Assert.Equal(HttpStatusCode.OK, loginNuevo.StatusCode);
    }

    [Fact]
    public async Task Reset_TokenReusado_Rechazado()
    {
        await SembrarAdminAsync("admin", "clave-vieja-1");
        var client = Factory.CreateClient();

        var desafio = (await (await client.PostAsync("/auth/reset-admin/desafio", null))
            .Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;
        var token = ClavesDePrueba.EmitirTokenReset(desafio);

        await client.PostAsJsonAsync("/auth/reset-admin", new ResetAdminRequest(token, "clave-nueva-9"));
        var segunda = await client.PostAsJsonAsync("/auth/reset-admin", new ResetAdminRequest(token, "otra-clave-8"));

        Assert.Equal(HttpStatusCode.BadRequest, segunda.StatusCode);
    }

    [Fact]
    public async Task Reset_TokenDeOtraMaquina_Rechazado()
    {
        await SembrarAdminAsync("admin", "clave-vieja-1");
        var client = Factory.CreateClient();

        var desafio = (await (await client.PostAsync("/auth/reset-admin/desafio", null))
            .Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;
        var token = ClavesDePrueba.EmitirTokenReset(desafio, maquina: "OTRA-MAQUINA");

        var reset = await client.PostAsJsonAsync("/auth/reset-admin", new ResetAdminRequest(token, "clave-nueva-9"));

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    [Fact]
    public async Task Reset_Exitoso_QuedaAuditado()
    {
        await SembrarAdminAsync("admin", "clave-vieja-1");
        var client = Factory.CreateClient();

        var desafio = (await (await client.PostAsync("/auth/reset-admin/desafio", null))
            .Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;
        await client.PostAsJsonAsync("/auth/reset-admin",
            new ResetAdminRequest(ClavesDePrueba.EmitirTokenReset(desafio), "clave-nueva-9"));

        using var ctx = Factory.CrearContexto();
        Assert.True(await ctx.LogsAuditoria.AnyAsync(l => l.Accion == AccionAuditada.ResetAdminFirmado));
    }
}
```

- [ ] **Step 6: Escribir los endpoints de reset**

Crear `src/StockApp.Api/Endpoints/ResetAdminEndpoints.cs`:

```csharp
using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Endpoints;

public record ResetDesafioResponse(string Desafio, string CodigoMaquina);
public record ResetAdminRequest(string? Token, string? NuevaContrasena);

public static class ResetAdminEndpoints
{
    public static IEndpointRouteBuilder MapResetAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/reset-admin");

        // Anónimo (pre-login): la protección es criptográfica (token firmado + nonce en memoria).
        group.MapPost("/desafio", (IAlmacenDesafiosReset desafios, EstadoLicencia estado) =>
            Results.Ok(new ResetDesafioResponse(desafios.GenerarNuevo(), estado.CodigoMaquina)));

        group.MapPost("", async (ResetAdminRequest request, ServicioResetAdmin servicio) =>
        {
            if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NuevaContrasena))
                return Results.Problem(
                    title: "El token y la nueva contraseña son obligatorios.",
                    statusCode: StatusCodes.Status400BadRequest);

            // ContrasenaValidator lanza ArgumentException (→ 400 vía DomainExceptionHandler)
            // si la contraseña es corta; el resto es flujo por enum.
            var resultado = await servicio.ResetearAsync(request.Token, request.NuevaContrasena);

            return resultado == ResultadoValidacionReset.Valido
                ? Results.Ok(new { ok = true })
                : Results.Problem(title: MotivoDe(resultado), statusCode: StatusCodes.Status400BadRequest);
        });

        return app;
    }

    private static string MotivoDe(ResultadoValidacionReset resultado) => resultado switch
    {
        ResultadoValidacionReset.FormatoInvalido => "El token no tiene un formato válido.",
        ResultadoValidacionReset.FirmaInvalido   => "La firma del token no es válida.",
        ResultadoValidacionReset.MaquinaDistinta => "El token fue emitido para otra máquina.",
        ResultadoValidacionReset.AccionInvalida  => "El token no es un token de reset de Admin.",
        ResultadoValidacionReset.DesafioInvalido => "El desafío no es válido o ya fue usado. Pedí uno nuevo.",
        ResultadoValidacionReset.DesafioExpirado => "El desafío expiró. Pedí uno nuevo.",
        _ => "Token de reset inválido.",
    };
}
```

- [ ] **Step 7: Cablear en `Program.cs`**

En `src/StockApp.Api/Program.cs`:

(a) En la sección de registros de licenciamiento (Task 4, después de `builder.Services.AddScoped<ServicioLicencia>();`), agregar:

```csharp
builder.Services.AddSingleton<IAlmacenDesafiosReset, AlmacenDesafiosResetEnMemoria>();
builder.Services.AddScoped<ServicioResetAdmin>();
```

(b) Junto a `app.MapLicenciaEndpoints();`, agregar:

```csharp
app.MapResetAdminEndpoints();
```

- [ ] **Step 8: Correr los tests para verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~Reset"` seguido de `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~ResetAdmin"`
Expected: PASA (8 de `AlmacenDesafiosResetTests` + `ServicioResetAdminTests` en Application; 5 de `ResetAdminEndpointsTests` en Api). Además, ahora `Bloqueada_DesafioDeReset_Pasa` (Task 4) devuelve 200 en vez de 404.

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Domain/Enums/AccionAuditada.cs \
        src/StockApp.Application/Licenciamiento/IAlmacenDesafiosReset.cs \
        src/StockApp.Application/Licenciamiento/AlmacenDesafiosResetEnMemoria.cs \
        src/StockApp.Application/Licenciamiento/ResultadoValidacionReset.cs \
        src/StockApp.Application/Licenciamiento/ServicioResetAdmin.cs \
        src/StockApp.Api/Endpoints/ResetAdminEndpoints.cs \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Application.Tests/Licenciamiento/AlmacenDesafiosResetTests.cs \
        tests/StockApp.Application.Tests/Licenciamiento/ServicioResetAdminTests.cs \
        tests/StockApp.Api.Tests/Licenciamiento/ResetAdminEndpointsTests.cs
git commit -m "feat(api): reset de Admin firmado con desafío en memoria y recreación vía primer arranque"
```

---

### Task 6: CLI generadora de claves/licencias/tokens (nunca empaquetada)

**Files:**
- Create: `tools/StockApp.Licencias.Cli/StockApp.Licencias.Cli.csproj`
- Create: `tools/StockApp.Licencias.Cli/Program.cs`
- Create: `tools/StockApp.Licencias.Cli/GeneradorClaves.cs`
- Create: `tests/StockApp.Licencias.Cli.Tests/StockApp.Licencias.Cli.Tests.csproj`
- Create: `tests/StockApp.Licencias.Cli.Tests/RoundTripTests.cs`
- Modify: `StockApp.sln` (agregar los dos proyectos)
- Modify: `.gitignore` (claves privadas)

**Interfaces:**
- Consumes: `FirmadorLicencias`, `ValidadorFirma`, `LicenciaPayload`, `TokenResetPayload` (Application, Task 1).
- Produces: `static class GeneradorClaves` con `(string privadaPem, string publicaBase64) Generar()`; CLI con comandos `generar-claves`, `emitir-licencia`, `emitir-reset`.

- [ ] **Step 1: Escribir el test de round-trip que falla**

Crear el csproj de tests `tests/StockApp.Licencias.Cli.Tests/StockApp.Licencias.Cli.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\tools\StockApp.Licencias.Cli\StockApp.Licencias.Cli.csproj" />
    <ProjectReference Include="..\..\src\StockApp.Application\StockApp.Application.csproj" />
  </ItemGroup>

</Project>
```

Nota: las versiones de `Microsoft.NET.Test.Sdk`/`xunit` deben coincidir con las de los demás csproj de test del repo — si `dotnet test` reporta NU1605, copiar las versiones exactas de `tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj`.

Crear `tests/StockApp.Licencias.Cli.Tests/RoundTripTests.cs`:

```csharp
using StockApp.Application.Licenciamiento;
using StockApp.Licencias.Cli;
using Xunit;

namespace StockApp.Licencias.Cli.Tests;

public class RoundTripTests
{
    [Fact]
    public void GenerarClaves_ProduceUnParUsable()
    {
        var (privadaPem, publicaBase64) = GeneradorClaves.Generar();

        Assert.Contains("PRIVATE KEY", privadaPem);
        Assert.False(string.IsNullOrWhiteSpace(publicaBase64));
    }

    [Fact]
    public void LicenciaEmitidaPorLaCli_ValidaConLaClavePublica()
    {
        var (privada, publica) = GeneradorClaves.Generar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "Ferretería X", "A3F2-9B41", "2026-07-15"), privada);

        var resultado = new ValidadorFirma(publica).Verificar(licencia, out _);

        Assert.Equal(ResultadoVerificacion.Ok, resultado);
    }

    [Fact]
    public void TokenDeResetEmitidoPorLaCli_ValidaConLaClavePublica()
    {
        var (privada, publica) = GeneradorClaves.Generar();
        var token = FirmadorLicencias.EmitirTokenReset(
            new TokenResetPayload(1, "reset-admin", "A3F2-9B41", "nonce-1"), privada);

        var resultado = new ValidadorFirma(publica).Verificar(token, out _);

        Assert.Equal(ResultadoVerificacion.Ok, resultado);
    }

    [Fact]
    public void LicenciaDeUnPar_NoValidaConLaPublicaDeOtroPar()
    {
        var (privadaA, _) = GeneradorClaves.Generar();
        var (_, publicaB) = GeneradorClaves.Generar();
        var licencia = FirmadorLicencias.EmitirLicencia(
            new LicenciaPayload(1, "X", "MAQ", "2026-07-15"), privadaA);

        Assert.Equal(ResultadoVerificacion.FirmaInvalida,
            new ValidadorFirma(publicaB).Verificar(licencia, out _));
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Licencias.Cli.Tests/StockApp.Licencias.Cli.Tests.csproj`
Expected: FALLA — el proyecto `StockApp.Licencias.Cli` y `GeneradorClaves` no existen (o el csproj no resuelve la referencia).

- [ ] **Step 3: Crear el proyecto de la CLI**

Crear `tools/StockApp.Licencias.Cli/StockApp.Licencias.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>StockApp.Licencias.Cli</RootNamespace>
    <!-- Herramienta interna del desarrollador: NUNCA se empaqueta ni distribuye. -->
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\StockApp.Application\StockApp.Application.csproj" />
  </ItemGroup>

</Project>
```

Crear `tools/StockApp.Licencias.Cli/GeneradorClaves.cs`:

```csharp
using System.Security.Cryptography;

namespace StockApp.Licencias.Cli;

/// <summary>Genera un par ECDSA P-256: privada en PEM (PKCS#8), pública en base64 (SubjectPublicKeyInfo).</summary>
public static class GeneradorClaves
{
    public static (string privadaPem, string publicaBase64) Generar()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privadaPem = ecdsa.ExportPkcs8PrivateKeyPem();
        var publicaBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        return (privadaPem, publicaBase64);
    }
}
```

Crear `tools/StockApp.Licencias.Cli/Program.cs`:

```csharp
using StockApp.Application.Licenciamiento;
using StockApp.Licencias.Cli;

// CLI interna del desarrollador. Reutiliza el MISMO firmador que valida Application, así el
// formato no puede divergir. La clave privada vive fuera del repo; nunca se commitea.
if (args.Length == 0)
{
    ImprimirAyuda();
    return 1;
}

try
{
    switch (args[0])
    {
        case "generar-claves":
        {
            var salida = LeerOpcion(args, "--salida") ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(salida);
            var (privadaPem, publicaBase64) = GeneradorClaves.Generar();

            var rutaPrivada = Path.Combine(salida, "clave-privada.pem");
            File.WriteAllText(rutaPrivada, privadaPem);

            Console.WriteLine($"Clave privada escrita en: {rutaPrivada}");
            Console.WriteLine("GUARDALA FUERA DEL REPO. No la compartas ni la commitees.");
            Console.WriteLine();
            Console.WriteLine("Clave pública (pegar en OpcionesLicencia.ClavePublicaBase64Default):");
            Console.WriteLine(publicaBase64);
            return 0;
        }

        case "emitir-licencia":
        {
            var privada = LeerClave(args);
            var cliente = LeerOpcionObligatoria(args, "--cliente");
            var maquina = LeerOpcionObligatoria(args, "--maquina");
            var payload = new LicenciaPayload(1, cliente, maquina, DateTime.UtcNow.ToString("yyyy-MM-dd"));
            Console.WriteLine(FirmadorLicencias.EmitirLicencia(payload, privada));
            return 0;
        }

        case "emitir-reset":
        {
            var privada = LeerClave(args);
            var maquina = LeerOpcionObligatoria(args, "--maquina");
            var desafio = LeerOpcionObligatoria(args, "--desafio");
            var payload = new TokenResetPayload(1, "reset-admin", maquina, desafio);
            Console.WriteLine(FirmadorLicencias.EmitirTokenReset(payload, privada));
            return 0;
        }

        default:
            ImprimirAyuda();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static string LeerClave(string[] args)
{
    var ruta = LeerOpcionObligatoria(args, "--clave");
    if (!File.Exists(ruta))
        throw new FileNotFoundException($"No se encontró la clave privada en: {ruta}");
    return File.ReadAllText(ruta);
}

static string? LeerOpcion(string[] args, string nombre)
{
    var i = Array.IndexOf(args, nombre);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string LeerOpcionObligatoria(string[] args, string nombre)
    => LeerOpcion(args, nombre)
       ?? throw new ArgumentException($"Falta la opción obligatoria {nombre}.");

static void ImprimirAyuda()
{
    Console.WriteLine("StockApp.Licencias.Cli — herramienta interna (no distribuir).");
    Console.WriteLine();
    Console.WriteLine("  generar-claves  --salida <dir>");
    Console.WriteLine("  emitir-licencia --clave <clave-privada.pem> --cliente \"Ferretería X\" --maquina A3F2-9B41-...");
    Console.WriteLine("  emitir-reset    --clave <clave-privada.pem> --maquina A3F2-9B41-... --desafio <nonce>");
}
```

- [ ] **Step 4: Agregar los proyectos a la solución**

```bash
dotnet sln StockApp.sln add tools/StockApp.Licencias.Cli/StockApp.Licencias.Cli.csproj
dotnet sln StockApp.sln add tests/StockApp.Licencias.Cli.Tests/StockApp.Licencias.Cli.Tests.csproj
```

- [ ] **Step 5: Ignorar claves privadas en git**

En `.gitignore`, agregar al final:

```gitignore

# Inc 7 Fase B — claves privadas de licenciamiento (jamás en el repo)
*.pem
clave-privada*
```

- [ ] **Step 6: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Licencias.Cli.Tests/StockApp.Licencias.Cli.Tests.csproj`
Expected: PASA (4 casos de round-trip: la CLI firma, `Application` valida — el mismo código de ambos lados).

- [ ] **Step 7: Commit**

```bash
git add tools/StockApp.Licencias.Cli tests/StockApp.Licencias.Cli.Tests StockApp.sln .gitignore
git commit -m "feat(licenciamiento): CLI generadora de claves/licencias/tokens con tests de round-trip"
```

---

### Task 7: Servicios de cliente (Application) + ApiClients de licencia/reset + manejo de 423

**Files:**
- Create: `src/StockApp.Application/Licenciamiento/ILicenciaService.cs`
- Create: `src/StockApp.Application/Licenciamiento/IResetAdminService.cs`
- Create: `src/StockApp.ApiClient/LicenciaApiClient.cs`
- Create: `src/StockApp.ApiClient/ResetAdminApiClient.cs`
- Modify: `src/StockApp.ApiClient/ApiSession.cs` (evento `LicenciaDesactivada` + dispatch interno)
- Modify: `src/StockApp.ApiClient/AuthTokenHandler.cs` (detectar 423)
- Test: `tests/StockApp.ApiClient.Tests/LicenciaApiClientTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/ResetAdminApiClientTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/AuthTokenHandlerTests.cs` (agregar caso 423)

**Interfaces:**
- Consumes: `ApiSession`, `AuthTokenHandler`, `ApiErrores` (patrón existente), `FakeHttpHandler`/`TestHttp` (test infra existente).
- Produces:
  - `record EstadoLicenciaDto(bool Activada, string CodigoMaquina)`, `record ResultadoActivacionDto(bool Exito, string? Motivo)`, `interface ILicenciaService { Task<EstadoLicenciaDto> ObtenerEstadoAsync(); Task<ResultadoActivacionDto> ActivarAsync(string licencia); }` (Application).
  - `record DesafioResetDto(string Desafio, string CodigoMaquina)`, `record ResultadoResetDto(bool Exito, string? Motivo)`, `interface IResetAdminService { Task<DesafioResetDto> SolicitarDesafioAsync(); Task<ResultadoResetDto> ResetearAsync(string token, string nuevaContrasena); }` (Application).
  - `class LicenciaApiClient : ILicenciaService`, `class ResetAdminApiClient : IResetAdminService` (ApiClient).
  - `ApiSession.LicenciaDesactivada` (event Action) + `internal void DispararLicenciaDesactivada()`.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/StockApp.ApiClient.Tests/LicenciaApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Licenciamiento;

namespace StockApp.ApiClient.Tests;

public class LicenciaApiClientTests
{
    [Fact]
    public async Task ObtenerEstado_GETLicenciaEstado_DevuelveDto()
    {
        var body = new { activada = false, codigoMaquina = "A3F2-9B41" };
        var fake = new FakeHttpHandler(_ => TestHttp.Json(body));
        var client = new LicenciaApiClient(TestHttp.CrearCliente(fake));

        var estado = await client.ObtenerEstadoAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/licencia/estado", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.False(estado.Activada);
        Assert.Equal("A3F2-9B41", estado.CodigoMaquina);
    }

    [Fact]
    public async Task Activar_200_DevuelveExito()
    {
        var body = new { activada = true, codigoMaquina = "A3F2-9B41" };
        var fake = new FakeHttpHandler(_ => TestHttp.Json(body));
        var client = new LicenciaApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ActivarAsync("payload.firma");

        Assert.Equal("/licencia/activar", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"licencia\":\"payload.firma\"", fake.UltimoBody);
        Assert.True(resultado.Exito);
    }

    [Fact]
    public async Task Activar_400_DevuelveFalloConMotivo()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "La licencia fue emitida para otra máquina."));
        var client = new LicenciaApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ActivarAsync("payload.firma");

        Assert.False(resultado.Exito);
        Assert.Equal("La licencia fue emitida para otra máquina.", resultado.Motivo);
    }
}
```

Crear `tests/StockApp.ApiClient.Tests/ResetAdminApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Licenciamiento;

namespace StockApp.ApiClient.Tests;

public class ResetAdminApiClientTests
{
    [Fact]
    public async Task SolicitarDesafio_POSTDesafio_DevuelveDto()
    {
        var body = new { desafio = "nonce-1", codigoMaquina = "A3F2-9B41" };
        var fake = new FakeHttpHandler(_ => TestHttp.Json(body));
        var client = new ResetAdminApiClient(TestHttp.CrearCliente(fake));

        var dto = await client.SolicitarDesafioAsync();

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/auth/reset-admin/desafio", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal("nonce-1", dto.Desafio);
        Assert.Equal("A3F2-9B41", dto.CodigoMaquina);
    }

    [Fact]
    public async Task Resetear_200_DevuelveExito()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { ok = true }));
        var client = new ResetAdminApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ResetearAsync("token-1", "clave-nueva-9");

        Assert.Equal("/auth/reset-admin", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"token\":\"token-1\"", fake.UltimoBody);
        Assert.Contains("\"nuevaContrasena\":\"clave-nueva-9\"", fake.UltimoBody);
        Assert.True(resultado.Exito);
    }

    [Fact]
    public async Task Resetear_400_DevuelveFalloConMotivo()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "El desafío expiró. Pedí uno nuevo."));
        var client = new ResetAdminApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ResetearAsync("token-1", "clave-nueva-9");

        Assert.False(resultado.Exito);
        Assert.Equal("El desafío expiró. Pedí uno nuevo.", resultado.Motivo);
    }
}
```

En `tests/StockApp.ApiClient.Tests/AuthTokenHandlerTests.cs`, agregar el caso de 423 (dispara `LicenciaDesactivada`):

```csharp
    [Fact]
    public async Task Respuesta423_DisparaLicenciaDesactivada()
    {
        var session = new ApiSession();
        var disparado = false;
        session.LicenciaDesactivada += () => disparado = true;

        var fake = new FakeHttpHandler(_ => new HttpResponseMessage((System.Net.HttpStatusCode)423));
        var client = TestHttp.CrearCliente(fake, session);

        await client.GetAsync("productos");

        Assert.True(disparado);
    }
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter "FullyQualifiedName~Licencia|FullyQualifiedName~ResetAdmin|FullyQualifiedName~AuthTokenHandler"`
Expected: FALLA de compilación — faltan `ILicenciaService`, `IResetAdminService`, `LicenciaApiClient`, `ResetAdminApiClient`, `ApiSession.LicenciaDesactivada`.

- [ ] **Step 3: Crear los servicios y DTOs en Application**

Crear `src/StockApp.Application/Licenciamiento/ILicenciaService.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>Estado de licencia visto por el desktop (pantalla de bloqueo).</summary>
public record EstadoLicenciaDto(bool Activada, string CodigoMaquina);

/// <summary>Resultado de intentar activar una licencia desde el desktop.</summary>
public record ResultadoActivacionDto(bool Exito, string? Motivo);

/// <summary>Consulta y activación de licencia contra la API (endpoints /licencia/*).</summary>
public interface ILicenciaService
{
    Task<EstadoLicenciaDto> ObtenerEstadoAsync();
    Task<ResultadoActivacionDto> ActivarAsync(string licencia);
}
```

Crear `src/StockApp.Application/Licenciamiento/IResetAdminService.cs`:

```csharp
namespace StockApp.Application.Licenciamiento;

/// <summary>Desafío de reset devuelto por la API (nonce + código de máquina para copiar).</summary>
public record DesafioResetDto(string Desafio, string CodigoMaquina);

/// <summary>Resultado de aplicar un reset de Admin desde el desktop.</summary>
public record ResultadoResetDto(bool Exito, string? Motivo);

/// <summary>Flujo de recuperación de Admin contra la API (endpoints /auth/reset-admin/*).</summary>
public interface IResetAdminService
{
    Task<DesafioResetDto> SolicitarDesafioAsync();
    Task<ResultadoResetDto> ResetearAsync(string token, string nuevaContrasena);
}
```

- [ ] **Step 4: Crear los ApiClients**

Crear `src/StockApp.ApiClient/LicenciaApiClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using StockApp.Application.Licenciamiento;

namespace StockApp.ApiClient;

internal sealed record EstadoLicenciaWire(bool Activada, string CodigoMaquina);
internal sealed record ActivarLicenciaBody(string Licencia);
internal sealed record ProblemaWire(string? Detail, string? Title);

/// <summary>
/// ILicenciaService contra /licencia/*. La activación NO usa el mapeo de errores de ApiErrores:
/// un 400 acá es flujo esperado (licencia inválida), no una excepción — se traduce a
/// ResultadoActivacionDto con el motivo del problem+json.
/// </summary>
public sealed class LicenciaApiClient : ILicenciaService
{
    private readonly HttpClient _http;

    public LicenciaApiClient(HttpClient http) => _http = http;

    public async Task<EstadoLicenciaDto> ObtenerEstadoAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("licencia/estado"));
        await ApiErrores.AsegurarExitoAsync(response);

        var wire = await response.Content.ReadFromJsonAsync<EstadoLicenciaWire>()
            ?? throw new InvalidOperationException("Respuesta vacía de /licencia/estado.");
        return new EstadoLicenciaDto(wire.Activada, wire.CodigoMaquina);
    }

    public async Task<ResultadoActivacionDto> ActivarAsync(string licencia)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("licencia/activar", new ActivarLicenciaBody(licencia)));

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problema = await LeerProblemaAsync(response);
            return new ResultadoActivacionDto(false, problema?.Detail ?? problema?.Title
                ?? "No se pudo activar la licencia.");
        }

        await ApiErrores.AsegurarExitoAsync(response);
        return new ResultadoActivacionDto(true, null);
    }

    private static async Task<ProblemaWire?> LeerProblemaAsync(HttpResponseMessage response)
    {
        try { return await response.Content.ReadFromJsonAsync<ProblemaWire>(); }
        catch (Exception) { return null; }
    }
}
```

Crear `src/StockApp.ApiClient/ResetAdminApiClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using StockApp.Application.Licenciamiento;

namespace StockApp.ApiClient;

internal sealed record DesafioResetWire(string Desafio, string CodigoMaquina);
internal sealed record ResetAdminBody(string Token, string NuevaContrasena);

/// <summary>
/// IResetAdminService contra /auth/reset-admin/*. Igual que la activación de licencia, un 400
/// es flujo esperado (token/desafío inválido) → ResultadoResetDto con motivo, sin excepción.
/// </summary>
public sealed class ResetAdminApiClient : IResetAdminService
{
    private readonly HttpClient _http;

    public ResetAdminApiClient(HttpClient http) => _http = http;

    public async Task<DesafioResetDto> SolicitarDesafioAsync()
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync("auth/reset-admin/desafio", content: null));
        await ApiErrores.AsegurarExitoAsync(response);

        var wire = await response.Content.ReadFromJsonAsync<DesafioResetWire>()
            ?? throw new InvalidOperationException("Respuesta vacía de /auth/reset-admin/desafio.");
        return new DesafioResetDto(wire.Desafio, wire.CodigoMaquina);
    }

    public async Task<ResultadoResetDto> ResetearAsync(string token, string nuevaContrasena)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("auth/reset-admin", new ResetAdminBody(token, nuevaContrasena)));

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            ProblemaWire? problema;
            try { problema = await response.Content.ReadFromJsonAsync<ProblemaWire>(); }
            catch (Exception) { problema = null; }
            return new ResultadoResetDto(false, problema?.Detail ?? problema?.Title
                ?? "No se pudo resetear el Admin.");
        }

        await ApiErrores.AsegurarExitoAsync(response);
        return new ResultadoResetDto(true, null);
    }
}
```

Nota: `ProblemaWire` se define en `LicenciaApiClient.cs` y se reutiliza en `ResetAdminApiClient.cs` (mismo assembly, mismo namespace) — no re-declararlo.

- [ ] **Step 5: Agregar el evento de licencia desactivada a `ApiSession` y el 423 a `AuthTokenHandler`**

En `src/StockApp.ApiClient/ApiSession.cs`, junto al evento `SesionVencida` (línea 25), agregar:

```csharp
    /// <summary>
    /// El servidor respondió 423 Locked: la licencia se desactivó (ej. borraron licencia.lic
    /// con la app abierta). La composition root lo cablea a la pantalla de bloqueo.
    /// </summary>
    public event Action? LicenciaDesactivada;
```

Y junto a `DispararSesionVencida()` (línea 78), agregar:

```csharp
    /// <summary>Lo invoca AuthTokenHandler ante un 423 (internal + InternalsVisibleTo).</summary>
    internal void DispararLicenciaDesactivada() => LicenciaDesactivada?.Invoke();
```

En `src/StockApp.ApiClient/AuthTokenHandler.cs`, después del bloque que maneja el 401 (líneas 31-35), agregar:

```csharp
        if (response.StatusCode == (HttpStatusCode)423)
        {
            _session.DispararLicenciaDesactivada();
        }
```

- [ ] **Step 6: Correr los tests para verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj`
Expected: PASA toda la suite de ApiClient.Tests (incluidos los 3 nuevos de licencia, 3 de reset y el caso de 423). Los tests existentes de `AuthTokenHandler` no se ven afectados (423 es un status nuevo, no colisiona con 401).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Licenciamiento/ILicenciaService.cs \
        src/StockApp.Application/Licenciamiento/IResetAdminService.cs \
        src/StockApp.ApiClient/LicenciaApiClient.cs \
        src/StockApp.ApiClient/ResetAdminApiClient.cs \
        src/StockApp.ApiClient/ApiSession.cs \
        src/StockApp.ApiClient/AuthTokenHandler.cs \
        tests/StockApp.ApiClient.Tests/LicenciaApiClientTests.cs \
        tests/StockApp.ApiClient.Tests/ResetAdminApiClientTests.cs \
        tests/StockApp.ApiClient.Tests/AuthTokenHandlerTests.cs
git commit -m "feat(apiclient): clientes de licencia y reset de Admin, y manejo de 423 como licencia desactivada"
```

---

### Task 8: Pantalla de bloqueo + flujo de reset en el desktop (integración al Shell)

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/BloqueoLicenciaViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/ResetAdminViewModel.cs`
- Create: `src/StockApp.Presentation/Views/BloqueoLicenciaView.axaml`
- Create: `src/StockApp.Presentation/Views/BloqueoLicenciaView.axaml.cs`
- Create: `src/StockApp.Presentation/Views/ResetAdminView.axaml`
- Create: `src/StockApp.Presentation/Views/ResetAdminView.axaml.cs`
- Modify: `src/StockApp.Presentation/ViewModels/ShellViewModel.cs` (dep `ILicenciaService`, `InicializarAsync`, `MostrarBloqueoLicencia`, `MostrarReset`)
- Modify: `src/StockApp.Presentation/ViewModels/LoginViewModel.cs` (comando "resetear Admin")
- Modify: `src/StockApp.Presentation/App.axaml.cs` (DI + cableado de `LicenciaDesactivada`)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/ShellViewModelTests.cs` (nuevo dep)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs` (nuevo dep del shell)
- Modify: `tests/StockApp.Presentation.Tests/Actualizaciones/ShellViewModelActualizacionTests.cs` (nuevo dep)
- Modify: `tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs` (registros nuevos)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/BloqueoLicenciaViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/ResetAdminViewModelTests.cs`

**Interfaces:**
- Consumes: `ILicenciaService`, `IResetAdminService`, `EstadoLicenciaDto`, `ResultadoActivacionDto`, `DesafioResetDto`, `ResultadoResetDto` (Task 7); `ShellViewModel`, `LoginViewModel`, `ViewModelBase`, `IUiDispatcher`.
- Produces:
  - `BloqueoLicenciaViewModel` (ctor `(ILicenciaService)`) con evento `Action? LicenciaActivada`, `Task CargarEstadoAsync()`, comando `Activar`.
  - `ResetAdminViewModel` (ctor `(IResetAdminService)`) con evento `Action? Volver`, comandos `PedirDesafio` / `Resetear`.
  - `ShellViewModel` con parámetro nuevo `ILicenciaService` y métodos `MostrarBloqueoLicencia()`, `MostrarReset()`; `InicializarAsync` consulta el estado de licencia primero.
  - `LoginViewModel` con comando `ResetearAdmin`.

- [ ] **Step 1: Escribir los tests de los ViewModels (rojo)**

Crear `tests/StockApp.Presentation.Tests/ViewModels/BloqueoLicenciaViewModelTests.cs`:

```csharp
using Moq;
using StockApp.Application.Licenciamiento;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class BloqueoLicenciaViewModelTests
{
    [Fact]
    public async Task CargarEstado_MuestraElCodigoDeMaquina()
    {
        var svc = new Mock<ILicenciaService>();
        svc.Setup(s => s.ObtenerEstadoAsync())
           .ReturnsAsync(new EstadoLicenciaDto(false, "A3F2-9B41"));
        var vm = new BloqueoLicenciaViewModel(svc.Object);

        await vm.CargarEstadoAsync();

        Assert.Equal("A3F2-9B41", vm.CodigoMaquina);
    }

    [Fact]
    public async Task Activar_Exitosa_DisparaLicenciaActivada()
    {
        var svc = new Mock<ILicenciaService>();
        svc.Setup(s => s.ActivarAsync("lic")).ReturnsAsync(new ResultadoActivacionDto(true, null));
        var vm = new BloqueoLicenciaViewModel(svc.Object) { LicenciaPegada = "lic" };
        var activada = false;
        vm.LicenciaActivada += () => activada = true;

        await vm.ActivarCommand.ExecuteAsync(null);

        Assert.True(activada);
        Assert.Null(vm.MensajeError);
    }

    [Fact]
    public async Task Activar_Fallida_MuestraMotivoYNoDispara()
    {
        var svc = new Mock<ILicenciaService>();
        svc.Setup(s => s.ActivarAsync("lic"))
           .ReturnsAsync(new ResultadoActivacionDto(false, "La licencia fue emitida para otra máquina."));
        var vm = new BloqueoLicenciaViewModel(svc.Object) { LicenciaPegada = "lic" };
        var activada = false;
        vm.LicenciaActivada += () => activada = true;

        await vm.ActivarCommand.ExecuteAsync(null);

        Assert.False(activada);
        Assert.Equal("La licencia fue emitida para otra máquina.", vm.MensajeError);
    }

    [Fact]
    public void Activar_DeshabilitadoSinLicenciaPegada()
    {
        var vm = new BloqueoLicenciaViewModel(Mock.Of<ILicenciaService>());

        Assert.False(vm.ActivarCommand.CanExecute(null));
        vm.LicenciaPegada = "algo";
        Assert.True(vm.ActivarCommand.CanExecute(null));
    }
}
```

Crear `tests/StockApp.Presentation.Tests/ViewModels/ResetAdminViewModelTests.cs`:

```csharp
using Moq;
using StockApp.Application.Licenciamiento;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class ResetAdminViewModelTests
{
    [Fact]
    public async Task PedirDesafio_MuestraDesafioYCodigo()
    {
        var svc = new Mock<IResetAdminService>();
        svc.Setup(s => s.SolicitarDesafioAsync())
           .ReturnsAsync(new DesafioResetDto("nonce-1", "A3F2-9B41"));
        var vm = new ResetAdminViewModel(svc.Object);

        await vm.PedirDesafioCommand.ExecuteAsync(null);

        Assert.Equal("nonce-1", vm.Desafio);
        Assert.Equal("A3F2-9B41", vm.CodigoMaquina);
    }

    [Fact]
    public async Task Resetear_Exitoso_MarcaCompletadoYPermiteVolver()
    {
        var svc = new Mock<IResetAdminService>();
        svc.Setup(s => s.ResetearAsync("tok", "clave-nueva-9"))
           .ReturnsAsync(new ResultadoResetDto(true, null));
        var vm = new ResetAdminViewModel(svc.Object)
        {
            TokenPegado = "tok",
            NuevaContrasena = "clave-nueva-9",
        };

        await vm.ResetearCommand.ExecuteAsync(null);

        Assert.True(vm.Completado);
        Assert.Null(vm.MensajeError);
    }

    [Fact]
    public async Task Resetear_Fallido_MuestraMotivo()
    {
        var svc = new Mock<IResetAdminService>();
        svc.Setup(s => s.ResetearAsync("tok", "clave-nueva-9"))
           .ReturnsAsync(new ResultadoResetDto(false, "El desafío expiró. Pedí uno nuevo."));
        var vm = new ResetAdminViewModel(svc.Object)
        {
            TokenPegado = "tok",
            NuevaContrasena = "clave-nueva-9",
        };

        await vm.ResetearCommand.ExecuteAsync(null);

        Assert.False(vm.Completado);
        Assert.Equal("El desafío expiró. Pedí uno nuevo.", vm.MensajeError);
    }

    [Fact]
    public void Volver_DisparaElEvento()
    {
        var vm = new ResetAdminViewModel(Mock.Of<IResetAdminService>());
        var volvio = false;
        vm.Volver += () => volvio = true;

        vm.VolverCommand.Execute(null);

        Assert.True(volvio);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~BloqueoLicenciaViewModel|FullyQualifiedName~ResetAdminViewModel"`
Expected: FALLA de compilación — `BloqueoLicenciaViewModel` y `ResetAdminViewModel` no existen.

- [ ] **Step 3: Escribir `BloqueoLicenciaViewModel`**

Crear `src/StockApp.Presentation/ViewModels/BloqueoLicenciaViewModel.cs`:

```csharp
using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Licenciamiento;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Pantalla de bloqueo pre-login: muestra el código de máquina del servidor (para copiar y
/// pasárselo al desarrollador) y un campo para pegar la licencia y activarla. Al activar OK,
/// dispara <see cref="LicenciaActivada"/> — el Shell pasa al login.
/// </summary>
public partial class BloqueoLicenciaViewModel : ViewModelBase
{
    private readonly ILicenciaService _licencia;

    /// <summary>La activación fue exitosa; el Shell debe navegar al login.</summary>
    public event Action? LicenciaActivada;

    [ObservableProperty]
    private string _codigoMaquina = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivarCommand))]
    private string _licenciaPegada = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivarCommand))]
    private bool _operacionEnCurso;

    public BloqueoLicenciaViewModel(ILicenciaService licencia) => _licencia = licencia;

    /// <summary>Carga el código de máquina desde la API (se llama al mostrar la pantalla).</summary>
    public async Task CargarEstadoAsync()
    {
        try
        {
            var estado = await _licencia.ObtenerEstadoAsync();
            CodigoMaquina = estado.CodigoMaquina;
        }
        catch (ServidorNoDisponibleException ex)
        {
            MensajeError = ex.Message;
        }
    }

    private bool PuedeActivar()
        => !string.IsNullOrWhiteSpace(LicenciaPegada) && !OperacionEnCurso;

    [RelayCommand(CanExecute = nameof(PuedeActivar))]
    private async Task ActivarAsync()
    {
        OperacionEnCurso = true;
        MensajeError = null;
        try
        {
            var resultado = await _licencia.ActivarAsync(LicenciaPegada.Trim());
            if (resultado.Exito)
                LicenciaActivada?.Invoke();
            else
                MensajeError = resultado.Motivo ?? "No se pudo activar la licencia.";
        }
        catch (ServidorNoDisponibleException ex)
        {
            MensajeError = ex.Message;
        }
        finally
        {
            OperacionEnCurso = false;
        }
    }
}
```

- [ ] **Step 4: Escribir `ResetAdminViewModel`**

Crear `src/StockApp.Presentation/ViewModels/ResetAdminViewModel.cs`:

```csharp
using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Licenciamiento;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Flujo "No puedo entrar / resetear Admin" desde el login. Paso 1: pedir un desafío (muestra
/// desafío + código de máquina para copiar). Paso 2: pegar el token firmado + nueva contraseña
/// y aplicar el reset. <see cref="Volver"/> regresa al login.
/// </summary>
public partial class ResetAdminViewModel : ViewModelBase
{
    private readonly IResetAdminService _reset;

    /// <summary>El usuario pidió volver al login.</summary>
    public event Action? Volver;

    [ObservableProperty]
    private string _codigoMaquina = "";

    [ObservableProperty]
    private string _desafio = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetearCommand))]
    private string _tokenPegado = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetearCommand))]
    private string _nuevaContrasena = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    private bool _completado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetearCommand))]
    private bool _operacionEnCurso;

    public ResetAdminViewModel(IResetAdminService reset) => _reset = reset;

    [RelayCommand]
    private async Task PedirDesafioAsync()
    {
        MensajeError = null;
        try
        {
            var dto = await _reset.SolicitarDesafioAsync();
            Desafio = dto.Desafio;
            CodigoMaquina = dto.CodigoMaquina;
        }
        catch (ServidorNoDisponibleException ex)
        {
            MensajeError = ex.Message;
        }
    }

    private bool PuedeResetear()
        => !string.IsNullOrWhiteSpace(TokenPegado)
        && !string.IsNullOrWhiteSpace(NuevaContrasena)
        && !OperacionEnCurso;

    [RelayCommand(CanExecute = nameof(PuedeResetear))]
    private async Task ResetearAsync()
    {
        OperacionEnCurso = true;
        MensajeError = null;
        try
        {
            var resultado = await _reset.ResetearAsync(TokenPegado.Trim(), NuevaContrasena);
            if (resultado.Exito)
                Completado = true;
            else
                MensajeError = resultado.Motivo ?? "No se pudo resetear el Admin.";
        }
        catch (ServidorNoDisponibleException ex)
        {
            MensajeError = ex.Message;
        }
        finally
        {
            OperacionEnCurso = false;
        }
    }

    [RelayCommand]
    private void Volver_() => Volver?.Invoke();
}
```

Nota: el método `Volver_` genera el comando `VolverCommand`; el guion bajo evita colisión con el evento `Volver`. Verificá que `[RelayCommand]` derive `VolverCommand` (el toolkit quita el sufijo `Async` y respeta el nombre base; si generara `Volver_Command`, renombrá el método a `EjecutarVolver` y agregá `[RelayCommand(...)]` — pero el nombre esperado por el test es `VolverCommand`). Alternativa segura: nombrar el método `EjecutarVolver` y el test usar `vm.EjecutarVolverCommand`; para mantener `VolverCommand`, usar el atributo con el método `Volver_` sí produce `Volver_Command`. Para garantizar `VolverCommand`, implementar el comando a mano:

```csharp
    public IRelayCommand VolverCommand { get; }
```

y en el constructor: `VolverCommand = new RelayCommand(() => Volver?.Invoke());` — quitando el método `Volver_` y su `[RelayCommand]`. Usar ESTA variante manual (evita ambigüedad con el generador).

- [ ] **Step 5: Crear las Views (convención del `ViewLocator`)**

Crear `src/StockApp.Presentation/Views/BloqueoLicenciaView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels"
             x:Class="StockApp.Presentation.Views.BloqueoLicenciaView"
             x:DataType="vm:BloqueoLicenciaViewModel">
  <StackPanel Margin="40" Spacing="12" MaxWidth="520" HorizontalAlignment="Center" VerticalAlignment="Center">
    <TextBlock Text="Activación de licencia" FontSize="22" FontWeight="Bold" />
    <TextBlock TextWrapping="Wrap"
               Text="Este servidor no tiene una licencia activada. Pasale el código de máquina al proveedor y pegá la licencia que te devuelva." />
    <TextBlock Text="Código de máquina:" FontWeight="SemiBold" />
    <SelectableTextBlock Text="{Binding CodigoMaquina}" FontFamily="Consolas,monospace" />
    <TextBlock Text="Licencia:" FontWeight="SemiBold" />
    <TextBox Text="{Binding LicenciaPegada}" AcceptsReturn="True" Height="90"
             Watermark="Pegá acá el texto de la licencia" />
    <Button Content="Activar" Command="{Binding ActivarCommand}" />
    <TextBlock Text="{Binding MensajeError}" Foreground="#C0392B" TextWrapping="Wrap"
               IsVisible="{Binding MensajeError, Converter={x:Static ObjectConverters.IsNotNull}}" />
  </StackPanel>
</UserControl>
```

Crear `src/StockApp.Presentation/Views/BloqueoLicenciaView.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Views;

public partial class BloqueoLicenciaView : UserControl
{
    public BloqueoLicenciaView()
    {
        InitializeComponent();
        // Igual que otras Views del proyecto: cargar datos cuando se asigna el DataContext
        // (las Views de Avalonia no se auto-inicializan).
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is BloqueoLicenciaViewModel vm)
                await vm.CargarEstadoAsync();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

Crear `src/StockApp.Presentation/Views/ResetAdminView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels"
             x:Class="StockApp.Presentation.Views.ResetAdminView"
             x:DataType="vm:ResetAdminViewModel">
  <StackPanel Margin="40" Spacing="12" MaxWidth="520" HorizontalAlignment="Center" VerticalAlignment="Center">
    <TextBlock Text="Recuperar acceso de Admin" FontSize="22" FontWeight="Bold" />
    <Button Content="1) Generar código de recuperación" Command="{Binding PedirDesafioCommand}" />
    <TextBlock Text="Código de máquina:" FontWeight="SemiBold" />
    <SelectableTextBlock Text="{Binding CodigoMaquina}" FontFamily="Consolas,monospace" />
    <TextBlock Text="Desafío:" FontWeight="SemiBold" />
    <SelectableTextBlock Text="{Binding Desafio}" FontFamily="Consolas,monospace" TextWrapping="Wrap" />
    <TextBlock Text="Token de reset (del proveedor):" FontWeight="SemiBold" />
    <TextBox Text="{Binding TokenPegado}" AcceptsReturn="True" Height="90" Watermark="Pegá el token firmado" />
    <TextBlock Text="Nueva contraseña de Admin:" FontWeight="SemiBold" />
    <TextBox Text="{Binding NuevaContrasena}" PasswordChar="•" />
    <Button Content="2) Resetear Admin" Command="{Binding ResetearCommand}" />
    <TextBlock Text="Listo. Volvé al login e ingresá con la nueva contraseña."
               Foreground="#1E8449" IsVisible="{Binding Completado}" TextWrapping="Wrap" />
    <TextBlock Text="{Binding MensajeError}" Foreground="#C0392B" TextWrapping="Wrap"
               IsVisible="{Binding MensajeError, Converter={x:Static ObjectConverters.IsNotNull}}" />
    <Button Content="Volver al login" Command="{Binding VolverCommand}" />
  </StackPanel>
</UserControl>
```

Crear `src/StockApp.Presentation/Views/ResetAdminView.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StockApp.Presentation.Views;

public partial class ResetAdminView : UserControl
{
    public ResetAdminView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 6: Integrar en `ShellViewModel`**

En `src/StockApp.Presentation/ViewModels/ShellViewModel.cs`:

(a) Agregar el campo (junto a `_authService`):

```csharp
    private readonly ILicenciaService _licenciaService;
```

Agregar `using StockApp.Application.Licenciamiento;` al tope.

(b) Agregar el parámetro al constructor (después de `IAuthService authService`) y su asignación:

```csharp
    public ShellViewModel(
        IAuthService             authService,
        ILicenciaService         licenciaService,
        INavigationService       navigation,
        CoordinadorActualizacion coordinadorActualizacion,
        IUiDispatcher            uiDispatcher,
        IInfoApp                 infoApp)
    {
        _authService              = authService;
        _licenciaService          = licenciaService;
        _navigation               = navigation;
        _coordinadorActualizacion = coordinadorActualizacion;
        _uiDispatcher             = uiDispatcher;
        _infoApp                  = infoApp;
    }
```

(c) Reemplazar `InicializarAsync()` para consultar el estado de licencia primero:

```csharp
    public async Task InicializarAsync()
    {
        // Inc 7 Fase B: antes del login se consulta la licencia. Sin licencia activa se
        // muestra la pantalla de bloqueo; con licencia (o si la API está caída) va al login.
        try
        {
            var estado = await _licenciaService.ObtenerEstadoAsync();
            if (!estado.Activada)
                MostrarBloqueoLicencia();
            else
                MostrarLogin();
        }
        catch (Exception)
        {
            // API inalcanzable: no bloqueamos el arranque; el login muestra el error de conexión.
            MostrarLogin();
        }

        _tareaActualizacion = EvaluarYAsignarOverlayAsync();
        _ = _tareaActualizacion;
    }
```

(d) Agregar los métodos de navegación (junto a `MostrarLogin()`):

```csharp
    public void MostrarBloqueoLicencia()
    {
        var bloqueo = new BloqueoLicenciaViewModel(_licenciaService);
        bloqueo.LicenciaActivada += () => _uiDispatcher.Post(MostrarLogin);
        CurrentViewModel = bloqueo;
    }

    public void MostrarReset()
    {
        var reset = new ResetAdminViewModel(_resetAdminService);
        reset.Volver += () => _uiDispatcher.Post(MostrarLogin);
        CurrentViewModel = reset;
    }
```

Para `MostrarReset` hace falta `IResetAdminService`. Agregar también ese dependency: campo `private readonly IResetAdminService _resetAdminService;`, parámetro en el constructor (después de `ILicenciaService licenciaService`) y su asignación. Constructor final:

```csharp
    public ShellViewModel(
        IAuthService             authService,
        ILicenciaService         licenciaService,
        IResetAdminService       resetAdminService,
        INavigationService       navigation,
        CoordinadorActualizacion coordinadorActualizacion,
        IUiDispatcher            uiDispatcher,
        IInfoApp                 infoApp)
    {
        _authService              = authService;
        _licenciaService          = licenciaService;
        _resetAdminService        = resetAdminService;
        _navigation               = navigation;
        _coordinadorActualizacion = coordinadorActualizacion;
        _uiDispatcher             = uiDispatcher;
        _infoApp                  = infoApp;
    }
```

- [ ] **Step 7: Agregar el comando de reset al `LoginViewModel`**

En `src/StockApp.Presentation/ViewModels/LoginViewModel.cs`, agregar un comando que delega en el shell (después de `EntrarAsync`):

```csharp
    /// <summary>Abre el flujo de recuperación de Admin ("No puedo entrar / resetear Admin").</summary>
    [RelayCommand]
    private void ResetearAdmin() => _shell.MostrarReset();
```

- [ ] **Step 8: Actualizar los tests que construyen `ShellViewModel`**

En `tests/StockApp.Presentation.Tests/ViewModels/ShellViewModelTests.cs`, en el helper `Crear()`:
- Agregar `using StockApp.Application.Licenciamiento;`.
- Antes del `return`, crear los mocks y pasarlos al constructor (nuevo orden de parámetros):

```csharp
        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync())
                    .ReturnsAsync(new EstadoLicenciaDto(true, "MAQ")); // activada → va al login

        return new ShellViewModel(
            Mock.Of<IAuthService>(),
            licenciaMock.Object,
            Mock.Of<IResetAdminService>(),
            navSvc,
            coordinador,
            new FakeUiDispatcher(),
            InfoAppStub);
```

El test `Inicializar_MuestraLogin` sigue verde porque la licencia está activada. Si algún test necesita el caso bloqueado, exponer un overload del helper con `EstadoLicenciaDto(false, ...)` y assertear que `CurrentViewModel` es `BloqueoLicenciaViewModel`. Agregar este test:

```csharp
    [Fact]
    public async Task Inicializar_LicenciaNoActivada_MuestraBloqueo()
    {
        // helper inline con licencia NO activada
        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync())
                    .ReturnsAsync(new EstadoLicenciaDto(false, "MAQ-1"));
        var navSvc = new NavigationService(_ => throw new InvalidOperationException());
        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());
        var shell = new ShellViewModel(
            Mock.Of<IAuthService>(), licenciaMock.Object, Mock.Of<IResetAdminService>(),
            navSvc, coordinador, new FakeUiDispatcher(), InfoAppStub);

        await shell.InicializarAsync();

        Assert.IsType<BloqueoLicenciaViewModel>(shell.CurrentViewModel);
    }
```

En `tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs` (línea ~49 según el plan de hardening): agregar `using StockApp.Application.Licenciamiento;` y, donde se construya el `ShellViewModel`, insertar los dos argumentos nuevos `Mock.Of<ILicenciaService>()` (2º) y `Mock.Of<IResetAdminService>()` (3º) respetando el orden del constructor.

En `tests/StockApp.Presentation.Tests/Actualizaciones/ShellViewModelActualizacionTests.cs`: en cada construcción de `ShellViewModel`, agregar `Mock.Of<ILicenciaService>()` y `Mock.Of<IResetAdminService>()` en las posiciones 2 y 3. Para que `InicializarAsync` no cambie el comportamiento de esos tests (que evalúan el overlay de actualización), configurar la licencia como activada:

```csharp
        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync()).ReturnsAsync(new EstadoLicenciaDto(true, "MAQ"));
```

y pasar `licenciaMock.Object` como 2º argumento (agregar `using StockApp.Application.Licenciamiento;`).

- [ ] **Step 9: Registrar en la DI y cablear `LicenciaDesactivada` en `App.axaml.cs`**

En `src/StockApp.Presentation/App.axaml.cs`:

(a) Junto a los ApiClients (después de `services.AddTransient<IAuthService, AuthApiClient>();`), registrar:

```csharp
        services.AddTransient<ILicenciaService>(sp => new LicenciaApiClient(sp.GetRequiredService<HttpClient>()));
        services.AddTransient<IResetAdminService>(sp => new ResetAdminApiClient(sp.GetRequiredService<HttpClient>()));
```

Agregar `using StockApp.Application.Licenciamiento;` si falta.

(b) En el bloque de arranque del desktop, junto al cableado de `SesionVencida` (líneas 80-84), agregar el de licencia desactivada:

```csharp
            apiSession.LicenciaDesactivada += () => uiDispatcher.Post(
                () => shell.MostrarBloqueoLicencia());
```

- [ ] **Step 10: Actualizar `ComposicionDIApiTests`**

En `tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs`, junto a los registros de ApiClients, agregar:

```csharp
        services.AddTransient<ILicenciaService>(sp => new LicenciaApiClient(sp.GetRequiredService<HttpClient>()));
        services.AddTransient<IResetAdminService>(sp => new ResetAdminApiClient(sp.GetRequiredService<HttpClient>()));
```

Agregar `using StockApp.Application.Licenciamiento;`. Si el test tiene una lista de resoluciones esperadas (`[InlineData(...)]`), agregar `ILicenciaService` y `IResetAdminService`.

- [ ] **Step 11: Correr toda la suite de Presentation**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
Expected: PASA — los VMs nuevos, la integración del Shell (login por defecto con licencia activada; bloqueo cuando no) y los tests de actualización adaptados. Si el generador de `RelayCommand` produjo un nombre inesperado para el comando "volver", aplicar la variante manual del Step 4.

- [ ] **Step 12: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/BloqueoLicenciaViewModel.cs \
        src/StockApp.Presentation/ViewModels/ResetAdminViewModel.cs \
        src/StockApp.Presentation/Views/BloqueoLicenciaView.axaml \
        src/StockApp.Presentation/Views/BloqueoLicenciaView.axaml.cs \
        src/StockApp.Presentation/Views/ResetAdminView.axaml \
        src/StockApp.Presentation/Views/ResetAdminView.axaml.cs \
        src/StockApp.Presentation/ViewModels/ShellViewModel.cs \
        src/StockApp.Presentation/ViewModels/LoginViewModel.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/BloqueoLicenciaViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/ResetAdminViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/ShellViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs \
        tests/StockApp.Presentation.Tests/Actualizaciones/ShellViewModelActualizacionTests.cs \
        tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs
git commit -m "feat(desktop): pantalla de bloqueo de licencia y flujo de reset de Admin desde el login"
```

- [ ] **Step 13: Verificación final de solución completa**

Run: `dotnet test`
Expected: PASA la solución entera (todas las suites, incluida la nueva `StockApp.Licencias.Cli.Tests`). Anotar el total de tests. Si aparece NU1605 por el paquete `Microsoft.Win32.Registry` (Task 2) o por las versiones del csproj nuevo de CLI tests (Task 6), alinear versiones como se indicó en esas tasks.

---

## Validación manual pendiente (unificada con Fase A)

Estos pasos NO se automatizan: requieren Windows/Linux real y la clave privada del desarrollador. Se hacen JUNTO con la validación manual pendiente de Inc 7 Fase A (empaquetado Velopack + actualizador), en una sola pasada sobre binarios reales.

1. **Generar el par de claves reales** (una única vez, en la máquina del desarrollador, fuera del repo):
   `dotnet run --project tools/StockApp.Licencias.Cli -- generar-claves --salida <dir-seguro-fuera-del-repo>`
   Guardar `clave-privada.pem` en un lugar seguro (gestor de secretos / bóveda). Copiar la clave pública impresa y pegarla en `OpcionesLicencia.ClavePublicaBase64Default` (`src/StockApp.Application/Licenciamiento/OpcionesLicencia.cs`), reemplazando el placeholder. Commitear SOLO ese cambio de la constante pública.
2. **Fingerprint real del servidor**: arrancar la API en la máquina servidor (Windows o Linux), abrir el desktop → debe mostrar la pantalla de bloqueo con el código de máquina. Verificar que el código es estable entre reinicios de la API.
3. **Emisión y activación de licencia**: con el código de máquina real, emitir la licencia:
   `dotnet run --project tools/StockApp.Licencias.Cli -- emitir-licencia --clave <clave-privada.pem> --cliente "Ferretería X" --maquina <CODIGO-REAL>`
   Pegar la licencia en la pantalla de bloqueo → Activar → debe pasar al login. Reiniciar la API → debe quedar activada (persistió `licencia.lic` en el directorio de datos).
4. **Modo bloqueado**: borrar `licencia.lic` con la app abierta y hacer cualquier acción → el 423 debe llevar al desktop de vuelta a la pantalla de bloqueo. Con la API bloqueada, confirmar que el login devuelve 423 y que `/licencia/estado` responde igual.
5. **Reset de Admin firmado**: desde el login, "No puedo entrar / resetear Admin" → generar desafío → emitir el token:
   `dotnet run --project tools/StockApp.Licencias.Cli -- emitir-reset --clave <clave-privada.pem> --maquina <CODIGO-REAL> --desafio <DESAFIO>`
   Pegar token + nueva contraseña → resetear → volver al login e ingresar con la nueva contraseña. Verificar: reusar el mismo token debe fallar (un solo uso); un token para otra máquina debe fallar.
6. **Cambio de PC del servidor**: mover la API a otra máquina → nuevo código de máquina en la pantalla de bloqueo → reemisión de licencia con la CLI → activación OK.
7. **Auditoría**: como Admin, revisar el log de auditoría y confirmar que quedaron registradas la activación de licencia y el reset de Admin.

## Notas de ejecución

- Orden pensado para que cada task cierre verde e independientemente aprobable: primitiva cripto (1) → adaptadores de OS/almacén (2) → servicio de estado (3) → superficie HTTP de licencia + bloqueo (4) → reset (5) → CLI (6) → clientes desktop (7) → UI desktop (8).
- La `ApiFactory` compartida arranca ACTIVADA por defecto (Task 4) para no romper la suite existente; solo los tests de bloqueo/activación fuerzan `Activada=false`. Nunca eliminar el reset por-test de `EstadoLicencia` en `ApiTestBase`: es lo que aísla ese estado singleton entre tests.
- La clave pública embebida queda como PLACEHOLDER durante toda la ejecución automatizada — los tests inyectan la clave de test por configuración. La real se pega recién en la validación manual (paso 1 de arriba). Con el placeholder, la API queda fail-closed (bloqueada), que es el comportamiento correcto.
- `LogAuditoria.UsuarioId` es FK requerida: los eventos de licencia (anónimos) se atribuyen al primer admin; si no hay admin, no se auditan en DB (fail-soft, sin migración de esquema). El reset audita con el admin real reseteado.

## Self-Review

**1. Cobertura del spec (design doc §1-§8):**
- §2/§3 pieza cripto compartida + formato + fingerprint → Task 1 (cripto/payloads) + Task 2 (fingerprint/almacén). ✓
- §4 flujo de activación + modo bloqueado (423, allowlist, arranque, `GET/POST /licencia/*`) → Task 3 (estado/arranque) + Task 4 (endpoints/middleware/wiring). ✓
- §5 reset firmado (desafío TTL/uno-activo/uno-uso, endpoints, recreación de Admin, auditoría) → Task 5. ✓
- §6 CLI (3 comandos, misma primitiva, round-trip, .gitignore) → Task 6. ✓
- §7 enums de resultado (sin excepciones) → `ResultadoVerificacion`/`ResultadoValidacionLicencia` (T1/T3), `ResultadoValidacionReset`/`ResultadoDesafio` (T5); fingerprint ilegible → arranque bloqueado sin crash (T3, test dedicado). ✓
- §8 testing por capa (Application unit, Api integración con `WebApplicationFactory` + fingerprint/clave inyectados, CLI round-trip, VMs de desktop con fakes) → Tasks 1-8. ✓
- Desktop: pantalla de bloqueo + `ApiSession` trata 423 como licencia desactivada + flujo reset desde login → Task 7 (423) + Task 8 (UI). ✓

**2. Placeholder scan:** sin "TBD"/"similar a Task N"/"agregar validación apropiada". Cada step con código lo muestra completo. El único "placeholder" es intencional y documentado: `OpcionesLicencia.ClavePublicaBase64Default` (constante reemplazable), explicado en Global Constraints y en Validación manual.

**3. Consistencia de tipos/firmas entre tasks:**
- `ValidadorFirma.Verificar(string, out byte[]) : ResultadoVerificacion` — definido en T1, consumido idéntico en T3 (`ServicioLicencia`) y T5 (`ServicioResetAdmin`). ✓
- `FirmadorLicencias.EmitirLicencia/EmitirTokenReset(payload, string clavePrivadaPem)` — T1, reutilizado en T4/T5 (tests, vía `ClavesDePrueba`) y T6 (CLI). ✓
- `IFingerprintMaquina.CodigoAgrupado`, `IAlmacenLicencia.LeerAsync/GuardarAsync` — T2, consumidos en T3/T4. ✓
- `EstadoLicencia { Activada; CodigoMaquina }` singleton — T3, leído por middleware (T4) y endpoints de reset (T5). ✓
- `ServicioLicencia` SCOPED / `EstadoLicencia` SINGLETON — coherente con `ValidateOnBuild` (Global Constraints). ✓
- `IAlmacenDesafiosReset.GenerarNuevo()/Consumir()` + `ResultadoDesafio` — T5, consumidos por `ServicioResetAdmin` y `ResetAdminEndpoints`. ✓
- Servicios de cliente `ILicenciaService`/`IResetAdminService` + DTOs — T7, consumidos por los VMs y el Shell en T8. ✓
- `ApiSession.LicenciaDesactivada` + `DispararLicenciaDesactivada()` — T7, cableado en `App.axaml.cs` (T8). ✓
- `AccionAuditada`: 19/20 (T4), 21 (T5) — append-only, sin colisión con 0-18 existentes. ✓
- Constructor de `ShellViewModel`: el orden final de 7 parámetros (T8) se refleja en TODOS los tests que lo construyen (Shell/Login/Actualizacion) y en la DI. ✓

Correcciones aplicadas inline durante el self-review: (a) se explicitó la variante MANUAL de `VolverCommand` en `ResetAdminViewModel` (T8, Step 4) para no depender del nombre que genere `[RelayCommand]`; (b) se unificó `ProblemaWire` en un solo archivo del assembly ApiClient (T7) evitando doble declaración.

## Execution Handoff

**Plan completo y guardado en `docs/superpowers/plans/2026-07-15-inc7-faseB-licenciamiento-reset.md`. Dos opciones de ejecución:**

**1. Subagent-Driven (recomendada)** — un subagente fresco por task, con review entre tasks e iteración rápida.
**REQUIRED SUB-SKILL:** Use superpowers:subagent-driven-development.

**2. Inline Execution** — ejecutar las tasks en esta sesión con checkpoints de review por lotes.
**REQUIRED SUB-SKILL:** Use superpowers:executing-plans.

**¿Cuál preferís?**
