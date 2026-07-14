# Caché de Reportes con Invalidación por Versión Global — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cachear en el servidor los 4 reportes de stock con invalidación por versión global, para liberar a PostgreSQL de recomputar reportes idénticos entre navegaciones y terminales.

**Architecture:** Un contador de versión singleton (`IVersionReportes`) en Application; un decorator (`ReporteStockServiceCacheado`) en Infrastructure que cachea las 4 lecturas en `IMemoryCache` con claves versionadas; y los services de mutación (movimientos, productos, categorías) que llaman `Invalidar()` después de cada commit exitoso. TTL de respaldo de 1 hora.

**Tech Stack:** .NET (ASP.NET Core Minimal API), EF Core + Npgsql (PostgreSQL), `Microsoft.Extensions.Caching.Memory`, xUnit + Moq, Testcontainers.

## Global Constraints

- Commits: conventional commits, en español, SIN `Co-Authored-By` ni atribución de IA.
- Rama: trabajar sobre una rama de feature y luego mergear a `main` con `--ff-only` + push. NO abrir PR.
- La auditoría (`GET /auditoria`) NO se cachea.
- Invalidar SIEMPRE después del commit exitoso, nunca antes.
- TTL de respaldo: 1 hora (absoluto).
- `IVersionReportes` vive en Application y NO depende de ningún caché (solo `System.Threading.Interlocked`). El único componente que toca `IMemoryCache` es el decorator en Infrastructure.
- Contexto de seguridad: los 4 endpoints de reportes ya exigen `Permisos.VerReportes` a nivel de endpoint (primera barrera). El caché opera detrás de esa barrera; los reportes son datos globales (no por-usuario), así que compartir el caché entre usuarios autorizados es correcto.
- Suite completa verde al cerrar cada task: `dotnet test` sobre `StockApp.sln`.

---

### Task 1: `IVersionReportes` + `VersionReportes` (Application)

**Files:**
- Create: `src/StockApp.Application/Reportes/IVersionReportes.cs`
- Create: `src/StockApp.Application/Reportes/VersionReportes.cs`
- Test: `tests/StockApp.Application.Tests/Reportes/VersionReportesTests.cs`

**Interfaces:**
- Produces: `IVersionReportes` con `long Actual { get; }` y `void Invalidar()`; impl `VersionReportes` (thread-safe con `Interlocked`).

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Application.Tests/Reportes/VersionReportesTests.cs`:

```csharp
using System.Threading.Tasks;
using StockApp.Application.Reportes;
using Xunit;

namespace StockApp.Application.Tests.Reportes;

public class VersionReportesTests
{
    [Fact]
    public void Actual_AlInicio_EsCero()
    {
        var version = new VersionReportes();
        Assert.Equal(0, version.Actual);
    }

    [Fact]
    public void Invalidar_IncrementaLaVersion()
    {
        var version = new VersionReportes();

        version.Invalidar();

        Assert.Equal(1, version.Actual);
    }

    [Fact]
    public void Invalidar_VariasVeces_IncrementaMonotonicamente()
    {
        var version = new VersionReportes();

        version.Invalidar();
        version.Invalidar();
        version.Invalidar();

        Assert.Equal(3, version.Actual);
    }

    [Fact]
    public async Task Invalidar_Concurrente_NoPierdeIncrementos()
    {
        var version = new VersionReportes();
        var tareas = new Task[100];

        for (var i = 0; i < tareas.Length; i++)
            tareas[i] = Task.Run(() => version.Invalidar());
        await Task.WhenAll(tareas);

        Assert.Equal(100, version.Actual);
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~VersionReportesTests"`
Expected: FALLA de compilación — `IVersionReportes`/`VersionReportes` no existen.

- [ ] **Step 3: Escribir la interfaz**

Crear `src/StockApp.Application/Reportes/IVersionReportes.cs`:

```csharp
namespace StockApp.Application.Reportes;

/// <summary>
/// Versión monotónica del conjunto de datos de reportes de stock. Cada mutación que
/// afecta un reporte (movimiento, ABM de producto, cambio de precio, ABM de categoría)
/// llama a <see cref="Invalidar"/> después de commitear; el caché de reportes incluye
/// <see cref="Actual"/> en sus claves, de modo que al incrementarse la versión las
/// entradas viejas quedan huérfanas.
/// </summary>
public interface IVersionReportes
{
    /// <summary>Versión vigente. Comienza en 0.</summary>
    long Actual { get; }

    /// <summary>Incrementa la versión (invalida todo el caché de reportes).</summary>
    void Invalidar();
}
```

- [ ] **Step 4: Escribir la implementación**

Crear `src/StockApp.Application/Reportes/VersionReportes.cs`:

```csharp
namespace StockApp.Application.Reportes;

/// <summary>
/// Implementación thread-safe de <see cref="IVersionReportes"/> con un contador
/// atómico. Se registra como singleton: una sola versión para todo el proceso.
/// </summary>
public sealed class VersionReportes : IVersionReportes
{
    private long _actual;

    public long Actual => Interlocked.Read(ref _actual);

    public void Invalidar() => Interlocked.Increment(ref _actual);
}
```

- [ ] **Step 5: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~VersionReportesTests"`
Expected: PASA (4 tests).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Reportes/IVersionReportes.cs src/StockApp.Application/Reportes/VersionReportes.cs tests/StockApp.Application.Tests/Reportes/VersionReportesTests.cs
git commit -m "feat(reportes): IVersionReportes para invalidación por versión global del caché"
```

---

### Task 2: Decorator `ReporteStockServiceCacheado` (Infrastructure)

**Files:**
- Modify: `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj` (agregar paquete)
- Create: `src/StockApp.Infrastructure/Reportes/ReporteStockServiceCacheado.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Reportes/ReporteStockServiceCacheadoTests.cs`

**Interfaces:**
- Consumes: `IReporteStockService` (inner), `IMemoryCache`, `IVersionReportes` (Task 1).
- Produces: `ReporteStockServiceCacheado : IReporteStockService` que cachea las 4 lecturas.

Firmas exactas de `IReporteStockService` que el decorator implementa:
- `Task<ValorizacionReporteDto> ObtenerValorizacionAsync()`
- `Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()`
- `Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20)`
- `Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(int productoId, DateTime? fechaDesde, DateTime? fechaHasta)`

- [ ] **Step 1: Agregar el paquete de caché a Infrastructure**

En `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj`, dentro del `<ItemGroup>` de `<PackageReference>`, agregar:

```xml
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.9" />
```

(Usá la MISMA versión mayor que el resto de los `Microsoft.Extensions.*`/EF del repo — 10.x. Si `dotnet` reporta que esa versión exacta no existe, usá la 10.x disponible más cercana.)

- [ ] **Step 2: Escribir el test que falla**

Crear `tests/StockApp.Infrastructure.Tests/Reportes/ReporteStockServiceCacheadoTests.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Moq;
using StockApp.Application.Reportes;
using StockApp.Infrastructure.Reportes;
using Xunit;

namespace StockApp.Infrastructure.Tests.Reportes;

public class ReporteStockServiceCacheadoTests
{
    private static ReporteStockServiceCacheado Crear(
        IReporteStockService inner, IVersionReportes version)
        => new(inner, new MemoryCache(new MemoryCacheOptions()), version);

    [Fact]
    public async Task Valorizacion_PrimeraLlamada_DelegaEnElInner()
    {
        var inner = new Mock<IReporteStockService>();
        var dto = new ValorizacionReporteDto(new List<ValorizacionItemDto>(), new ValorizacionTotalesDto(0, 0));
        inner.Setup(s => s.ObtenerValorizacionAsync()).ReturnsAsync(dto);
        var sut = Crear(inner.Object, new VersionReportes());

        var resultado = await sut.ObtenerValorizacionAsync();

        Assert.Same(dto, resultado);
        inner.Verify(s => s.ObtenerValorizacionAsync(), Times.Once);
    }

    [Fact]
    public async Task Valorizacion_SegundaLlamadaMismaVersion_SirveDeCacheSinTocarElInner()
    {
        var inner = new Mock<IReporteStockService>();
        inner.Setup(s => s.ObtenerValorizacionAsync())
            .ReturnsAsync(new ValorizacionReporteDto(new List<ValorizacionItemDto>(), new ValorizacionTotalesDto(0, 0)));
        var sut = Crear(inner.Object, new VersionReportes());

        await sut.ObtenerValorizacionAsync();
        await sut.ObtenerValorizacionAsync();

        inner.Verify(s => s.ObtenerValorizacionAsync(), Times.Once);
    }

    [Fact]
    public async Task Valorizacion_TrasInvalidar_Recalcula()
    {
        var inner = new Mock<IReporteStockService>();
        inner.Setup(s => s.ObtenerValorizacionAsync())
            .ReturnsAsync(new ValorizacionReporteDto(new List<ValorizacionItemDto>(), new ValorizacionTotalesDto(0, 0)));
        var version = new VersionReportes();
        var sut = Crear(inner.Object, version);

        await sut.ObtenerValorizacionAsync();
        version.Invalidar();
        await sut.ObtenerValorizacionAsync();

        inner.Verify(s => s.ObtenerValorizacionAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task MasMovidos_ParametrosDistintos_SonEntradasDistintas()
    {
        var inner = new Mock<IReporteStockService>();
        inner.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MasMovidoDto>());
        var sut = Crear(inner.Object, new VersionReportes());

        await sut.ObtenerMasMovidosAsync(new DateTime(2026, 1, 1), null, 20);
        await sut.ObtenerMasMovidosAsync(new DateTime(2026, 2, 1), null, 20);

        inner.Verify(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Historial_ProductosDistintos_SonEntradasDistintas()
    {
        var inner = new Mock<IReporteStockService>();
        inner.Setup(s => s.ObtenerHistorialPorProductoAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<MovimientoHistorialDto>());
        var sut = Crear(inner.Object, new VersionReportes());

        await sut.ObtenerHistorialPorProductoAsync(1, null, null);
        await sut.ObtenerHistorialPorProductoAsync(2, null, null);

        inner.Verify(s => s.ObtenerHistorialPorProductoAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Exactly(2));
    }
}
```

- [ ] **Step 3: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ReporteStockServiceCacheadoTests"`
Expected: FALLA de compilación — `ReporteStockServiceCacheado` no existe.

- [ ] **Step 4: Escribir el decorator**

Crear `src/StockApp.Infrastructure/Reportes/ReporteStockServiceCacheado.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;

namespace StockApp.Infrastructure.Reportes;

/// <summary>
/// Decorator de <see cref="IReporteStockService"/> que cachea los 4 reportes de stock
/// en <see cref="IMemoryCache"/> con claves versionadas por <see cref="IVersionReportes"/>.
/// Al incrementarse la versión, las claves viejas quedan huérfanas y expiran por TTL o
/// por presión de tamaño. TTL de respaldo: 1 hora (la invalidación por versión es la
/// defensa primaria e inmediata).
/// </summary>
public sealed class ReporteStockServiceCacheado : IReporteStockService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    private readonly IReporteStockService _inner;
    private readonly IMemoryCache _cache;
    private readonly IVersionReportes _version;

    public ReporteStockServiceCacheado(
        IReporteStockService inner, IMemoryCache cache, IVersionReportes version)
    {
        _inner = inner;
        _cache = cache;
        _version = version;
    }

    public Task<ValorizacionReporteDto> ObtenerValorizacionAsync()
        => GetOrCreate(
            $"valorizacion@v{_version.Actual}",
            () => _inner.ObtenerValorizacionAsync());

    public Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
        => GetOrCreate(
            $"stock-categoria@v{_version.Actual}",
            () => _inner.ObtenerStockPorCategoriaAsync());

    public Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(
        DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20)
        => GetOrCreate(
            $"mas-movidos:{fechaDesde:o}:{fechaHasta:o}:{topN}@v{_version.Actual}",
            () => _inner.ObtenerMasMovidosAsync(fechaDesde, fechaHasta, topN));

    public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(
        int productoId, DateTime? fechaDesde, DateTime? fechaHasta)
        => GetOrCreate(
            $"historial:{productoId}:{fechaDesde:o}:{fechaHasta:o}@v{_version.Actual}",
            () => _inner.ObtenerHistorialPorProductoAsync(productoId, fechaDesde, fechaHasta));

    private Task<T> GetOrCreate<T>(string clave, Func<Task<T>> calcular)
        => _cache.GetOrCreateAsync(clave, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return calcular();
        })!;
}
```

- [ ] **Step 5: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ReporteStockServiceCacheadoTests"`
Expected: PASA (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Infrastructure/StockApp.Infrastructure.csproj src/StockApp.Infrastructure/Reportes/ReporteStockServiceCacheado.cs tests/StockApp.Infrastructure.Tests/Reportes/ReporteStockServiceCacheadoTests.cs
git commit -m "feat(reportes): decorator ReporteStockServiceCacheado con claves versionadas y TTL de 1h"
```

---

### Task 3: Invalidación en los services de mutación

**Files:**
- Modify: `src/StockApp.Application/Movimientos/MovimientoStockService.cs`
- Modify: `src/StockApp.Application/Catalogo/ProductoService.cs`
- Modify: `src/StockApp.Application/Catalogo/CategoriaService.cs`
- Modify: los tests de Application que construyen esos tres services (ver Step 1)
- Test: agregar tests de invalidación (ver Steps)

**Interfaces:**
- Consumes: `IVersionReportes` (Task 1).
- Produces: los tres services ahora reciben `IVersionReportes` en el constructor y llaman `Invalidar()` después de cada commit exitoso.

**IMPORTANTE (orden):** en este punto el decorator NO está registrado todavía (eso es Task 4), así que llamar `Invalidar()` incrementa un contador que nadie lee aún — no cambia el comportamiento observable. Los tests verifican la llamada con un mock. La suite debe quedar verde.

- [ ] **Step 1: Actualizar los constructores en los tests existentes (rojo de compilación)**

Al agregar un parámetro `IVersionReportes` a los constructores de los tres services, TODOS los sitios que hacen `new MovimientoStockService(...)`, `new ProductoService(...)` y `new CategoriaService(...)` en `tests/StockApp.Application.Tests/` dejan de compilar. Buscalos con:

```bash
rg -n "new MovimientoStockService\(|new ProductoService\(|new CategoriaService\(" tests/StockApp.Application.Tests
```

En cada sitio, agregá como ÚLTIMO argumento `Mock.Of<IVersionReportes>()` (para los tests que NO verifican invalidación). Asegurate de tener `using StockApp.Application.Reportes;` y `using Moq;` en esos archivos.

- [ ] **Step 2: Escribir los tests de invalidación que fallan**

Crear `tests/StockApp.Application.Tests/Movimientos/MovimientoStockServiceInvalidacionTests.cs` (ajustá el armado del service y del DTO al patrón que ya usan los tests vecinos de `MovimientoStockService` — mirá un test existente de `RegistrarAsync` para copiar cómo se mockean `IMovimientoStockRepository`, `ICurrentSession`, `IAuthorizationService`, cómo se arma `RegistrarMovimientoDto`, y qué devuelve `RegistrarMovimientoAtomicoAsync` en el caso exitoso):

```csharp
using Moq;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using Xunit;

namespace StockApp.Application.Tests.Movimientos;

public class MovimientoStockServiceInvalidacionTests
{
    [Fact]
    public async Task RegistrarAsync_Exitoso_InvalidaLaVersionDeReportes()
    {
        // Armá el service igual que los tests existentes de RegistrarAsync (auth OK,
        // repo devuelve un resultado exitoso NO StockInsuficiente), pero pasando este mock:
        var version = new Mock<IVersionReportes>();

        // ... construir MovimientoStockService con los mocks vecinos + version.Object,
        //     invocar RegistrarAsync con un DTO válido ...

        version.Verify(v => v.Invalidar(), Times.Once);
    }
}
```

Análogamente, agregá un test de invalidación para `ProductoService.AltaAsync` (en `tests/StockApp.Application.Tests/Catalogo/`) y para `CategoriaService.AltaAsync`, cada uno verificando `version.Verify(v => v.Invalidar(), Times.Once)` tras una mutación exitosa, reutilizando el patrón de armado de los tests vecinos de esos services.

- [ ] **Step 3: Correr los tests para verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~Invalidacion"`
Expected: FALLA de compilación — los constructores todavía no aceptan `IVersionReportes`.

- [ ] **Step 4: Agregar la invalidación en `MovimientoStockService`**

En `src/StockApp.Application/Movimientos/MovimientoStockService.cs`:

(a) Agregar el `using` si falta: `using StockApp.Application.Reportes;`

(b) Agregar el campo y el parámetro al constructor. El constructor pasa de:

```csharp
    private readonly IMovimientoStockRepository _repo;
    private readonly ICurrentSession            _session;
    private readonly IAuthorizationService      _auth;

    public MovimientoStockService(
        IMovimientoStockRepository repo,
        ICurrentSession            session,
        IAuthorizationService      auth)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
    }
```

a:

```csharp
    private readonly IMovimientoStockRepository _repo;
    private readonly ICurrentSession            _session;
    private readonly IAuthorizationService      _auth;
    private readonly IVersionReportes           _version;

    public MovimientoStockService(
        IMovimientoStockRepository repo,
        ICurrentSession            session,
        IAuthorizationService      auth,
        IVersionReportes           version)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _version = version;
    }
```

(c) En `RegistrarAsync`, después del guard de `StockInsuficiente` y antes del `return` (el commit ya está confirmado en ese punto), agregar `_version.Invalidar();`. Queda así:

```csharp
        var resultado = await _repo.RegistrarMovimientoAtomicoAsync(args);
        // ... (código intermedio existente sin cambios) ...
        if (resultado.Estado == ResultadoRegistroEstado.StockInsuficiente)
            throw new StockInsuficienteException(dto.ProductoId, resultado.StockResultante, dto.Cantidad);

        _version.Invalidar();

        return new MovimientoRegistradoDto(
```

(d) En `RecalcularStockAsync`, después de `await _repo.RecalcularAtomicoAsync(args);` y antes del `return`, agregar `_version.Invalidar();`:

```csharp
        await _repo.RecalcularAtomicoAsync(args);

        _version.Invalidar();

        return new RecalculoResultadoDto(
```

- [ ] **Step 5: Agregar la invalidación en `ProductoService`**

En `src/StockApp.Application/Catalogo/ProductoService.cs`:

(a) `using StockApp.Application.Reportes;` si falta.

(b) Agregar el campo `private readonly IVersionReportes _version;` y el parámetro `IVersionReportes version` como ÚLTIMO parámetro del constructor, con su asignación `_version = version;`.

(c) Llamar `_version.Invalidar();` inmediatamente DESPUÉS de la persistencia en cada método que muta, antes del `return` cuando lo haya:
- `AltaAsync`: después de `var id = await _repo.AgregarAsync(producto);` (antes de `return id;`).
- `ModificarAsync`: después de `await _repo.ActualizarAsync(original);` (NO en el early-return de "sin cambios").
- `BajaLogicaAsync`: después de `await _repo.ActualizarAsync(producto);`.
- `CambiarPrecioAsync`: después de `await _repo.ActualizarAsync(producto);`.

- [ ] **Step 6: Agregar la invalidación en `CategoriaService`**

En `src/StockApp.Application/Catalogo/CategoriaService.cs`:

(a) `using StockApp.Application.Reportes;` si falta.

(b) Agregar el campo `private readonly IVersionReportes _version;` y el parámetro `IVersionReportes version` como ÚLTIMO parámetro del constructor, con `_version = version;`.

(c) Llamar `_version.Invalidar();` después de la persistencia en cada método que muta:
- `AltaAsync`: después de `var id = await _repo.AgregarAsync(categoria);` (antes de `return id;`).
- `ModificarAsync`: después de `await _repo.ActualizarAsync(original);` (NO en el early-return de "sin cambios").
- `BajaLogicaAsync`: después de `await _repo.ActualizarAsync(categoria);`.

- [ ] **Step 7: Correr toda la suite de Application**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj`
Expected: PASA — los tests de invalidación nuevos verdes y todos los tests existentes (con el arg `Mock.Of<IVersionReportes>()` agregado) siguen verdes.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(reportes): invalidar el caché de reportes tras cada mutación de stock/producto/categoría"
```

---

### Task 4: Registrar el caché en el arranque + test de integración de oro

**Files:**
- Modify: `src/StockApp.Api/Program.cs` (registros DI)
- Test: `tests/StockApp.Api.Tests/Reportes/CacheReportesIntegracionTests.cs`

**Interfaces:**
- Consumes: `VersionReportes` (Task 1), `ReporteStockServiceCacheado` (Task 2).

- [ ] **Step 1: Escribir el test de integración de oro que falla**

Crear `tests/StockApp.Api.Tests/Reportes/CacheReportesIntegracionTests.cs`. Reutilizá el patrón de los tests de integración existentes (heredan de `ApiTestBase`, usan `Factory.CrearContexto()` + `DatosDePrueba.SeedUsuarioAsync(...)` para sembrar un admin, `Factory.CreateClient()`, y login para obtener el token). El test:
1. Siembra un admin y un producto con stock conocido.
2. Hace login y arma el header `Authorization: Bearer {token}`.
3. `GET /reportes/valorizacion` → guarda el total inicial.
4. `POST /movimientos` con una entrada que cambia el stock de ese producto.
5. `GET /reportes/valorizacion` de nuevo → el total DEBE reflejar el cambio (prueba que la invalidación funciona; si el caché no se invalidara, devolvería el total viejo).

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Reportes;

public class CacheReportesIntegracionTests : ApiTestBase
{
    public CacheReportesIntegracionTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Valorizacion_TrasUnMovimiento_ReflejaElCambio()
    {
        // 1-2. Sembrar admin + producto con stock, y obtener token.
        //      (usá DatosDePrueba.SeedUsuarioAsync y el patrón de login de los tests existentes;
        //       para el producto, insertá una entidad Producto con StockActual y precios conocidos
        //       vía Factory.CrearContexto(), o usá el endpoint POST /productos con el token admin).
        var client = Factory.CreateClient();
        // client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 3. Valorización inicial
        var inicial = await client.GetFromJsonAsync<ValorizacionReporteDto>("/reportes/valorizacion");
        var totalInicial = inicial!.Totales.TotalValorCosto;

        // 4. Movimiento de entrada que cambia el stock del producto sembrado
        //    (POST /movimientos con RegistrarMovimientoDto: Tipo=Entrada, Motivo=Compra,
        //     Cantidad>0, PrecioUnitario válido — copiá el shape del DTO de MovimientosEndpoints).
        // var resp = await client.PostAsJsonAsync("/movimientos", nuevoMovimiento);
        // Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        // 5. Valorización tras el movimiento: refleja el cambio
        var despues = await client.GetFromJsonAsync<ValorizacionReporteDto>("/reportes/valorizacion");
        Assert.NotEqual(totalInicial, despues!.Totales.TotalValorCosto);
    }
}
```

Nota: completá los pasos comentados siguiendo el patrón de `LoginEndpointTests`/`MovimientosEndpoints` existentes (token, header, shape del DTO de movimiento). El aserto clave es que el total DESPUÉS difiere del inicial.

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~CacheReportesIntegracionTests"`
Expected: FALLA — al no estar registrado el decorator todavía, o bien el test aún no compila; una vez que compile, DEBE fallar solo si el caché quedara stale. El objetivo del registro (siguiente step) es que el flujo completo (caché + invalidación) quede coherente.

- [ ] **Step 3: Registrar el caché y el decorator en `Program.cs`**

En `src/StockApp.Api/Program.cs`:

(a) Agregar el registro del caché en memoria y del versionador (cerca de los otros `AddScoped`/`AddSingleton`, por ejemplo junto a la sección de servicios de Application). Agregar los `using` necesarios: `using StockApp.Application.Reportes;` y `using StockApp.Infrastructure.Reportes;` si faltan.

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IVersionReportes, VersionReportes>();
```

(b) Reemplazar el registro actual de `IReporteStockService` (hoy: `builder.Services.AddScoped<IReporteStockService, ReporteStockService>();`) por el registro del concreto + el decorator como implementación de la interfaz:

```csharp
builder.Services.AddScoped<ReporteStockService>();
builder.Services.AddScoped<IReporteStockService>(sp =>
    new ReporteStockServiceCacheado(
        sp.GetRequiredService<ReporteStockService>(),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<IVersionReportes>()));
```

Agregar `using Microsoft.Extensions.Caching.Memory;` si falta.

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASA — el test de oro verde (la valorización refleja el movimiento) y toda la suite de Api.Tests sigue verde.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/Reportes/CacheReportesIntegracionTests.cs
git commit -m "feat(reportes): registrar el caché de reportes (IMemoryCache + decorator + versionador) en el arranque"
```

---

### Task 5: Verificación final + merge

**Files:** ninguno (verificación e integración).

- [ ] **Step 1: Suite completa verde**

Run: `dotnet test`
Expected: PASA la solución completa. Anotar el total de tests.

- [ ] **Step 2: Verificación dura (grep de coherencia)**

Run:
```bash
rg -n "Invalidar\(\)" src/StockApp.Application
```
Expected: aparece en `MovimientoStockService` (2 veces: RegistrarAsync, RecalcularStockAsync), `ProductoService` (4: alta, modificar, baja, cambiar precio) y `CategoriaService` (3: alta, modificar, baja). Total 9 invocaciones. Si falta alguna, agregarla.

- [ ] **Step 3: Verificación orgánica (manual, con el stack real)**

Con el stack real (contenedor `stockapp-pg`, API, desktop): abrir un reporte desde una terminal, registrar un movimiento desde otra, y confirmar que el reporte refleja el cambio en la siguiente carga. (Este paso lo hace el usuario.)

- [ ] **Step 4: Merge a main**

```bash
git checkout main
git merge --ff-only <rama-de-feature>
git push origin main
```

---

## Notas de ejecución

- Orden pensado para que cada task deje la suite verde: primero el contador (1), luego el decorator aislado sin registrar (2), luego la invalidación desde los services —que sin decorator registrado no cambia comportamiento observable— (3), y recién al final se conecta todo con el registro + test de oro (4). Así el "test de oro" de la Task 4 encuentra la invalidación ya en su lugar.
- El decorator saltea la segunda barrera de auth (la del service) en los cache hits, pero la primera barrera (endpoint `RequireAuthorization(VerReportes)`) siempre aplica y los reportes son datos globales, así que es seguro.
- Los reportes devuelven `record` (value equality), útil para los asserts.
