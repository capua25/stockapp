# Ingreso de stock por factura — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir cargar una factura de compra con N artículos en una sola pantalla, creando atómicamente el `Gasto`, los `MovimientoStock` de entrada, los deltas de stock, el alta opcional de productos nuevos y la actualización selectiva de precio de costo, con anulación por asiento inverso.

**Architecture:** Se reutilizan `Gasto` y `MovimientoStock.GastoId` (sin entidades ni migraciones nuevas). `IMovimientoStockRepository` gana dos métodos atómicos (`RegistrarIngresoPorFacturaAtomicoAsync` / `AnularIngresoPorFacturaAtomicoAsync`) que envuelven todo en una única transacción EF Core, siguiendo el patrón ya existente de `RegistrarMovimientoAtomicoAsync`. `IngresoPorFacturaService` compone las entidades y valida; el repositorio persiste sin conocer reglas de negocio.

**Tech Stack:** .NET, EF Core + Npgsql, Avalonia, xUnit, Moq, Testcontainers

## Global Constraints

- Tests: xUnit + Moq, aserciones `Assert.*` (nunca FluentAssertions).
- Comando de tests: `dotnet test StockApp.sln`; por proyecto `dotnet test tests/<Proyecto>`; filtrado `dotnet test tests/<Proyecto> --filter "FullyQualifiedName~Nombre"`.
- Nunca correr `dotnet build` como paso separado — los tests compilan.
- `AccionAuditada` es append-only: `IngresoPorFactura = 44` y `AnulacionIngresoPorFactura = 45` se agregan al final, nunca se reordenan valores existentes.
- Fechas del request se normalizan a UTC en el borde JSON (converter ya existente, commit 2239734) — no se reimplementa.
- El endpoint exige `Permisos.RegistrarMovimientos`; el servicio verifica además `Permisos.RegistrarGastos` y, condicionalmente, `Permisos.GestionarProductos`. Ambos roles del sistema (`Admin`, `Operador`) tienen los tres permisos (`AuthorizationService.cs`), así que **no existe un 403 por rol posible en este endpoint** — se documenta, no se inventa un caso que no puede ocurrir.
- Fuente de financiamiento y rubro NUNCA se preseleccionan en la UI (decisión 8 del spec): los combos arrancan sin selección.
- Commits: conventional commits, sin atribución de IA, uno por ciclo test→implementación completado.

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs` | (Modify) Args/resultados/métodos atómicos nuevos del lote de ingreso y de su anulación. |
| `src/StockApp.Application/Movimientos/IngresoPorFacturaDtos.cs` | (Create) DTOs de entrada/salida del servicio. |
| `src/StockApp.Application/Movimientos/IIngresoPorFacturaService.cs` | (Create) Contrato del servicio. |
| `src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs` | (Create) Autorización + validaciones + composición de entidades. |
| `src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs` | (Modify) Implementación atómica de alta y anulación del lote. |
| `src/StockApp.Api/Endpoints/IngresoPorFacturaEndpoints.cs` | (Create) `POST /movimientos/ingreso-factura` y `POST /movimientos/ingreso-factura/{gastoId}/anular`. |
| `src/StockApp.Api/Program.cs` | (Modify) DI del servicio + mapeo del endpoint. |
| `src/StockApp.ApiClient/IngresoPorFacturaApiClient.cs` | (Create) Cliente HTTP del servicio. |
| `src/StockApp.Presentation/ViewModels/Movimientos/FilaRenglonFacturaVm.cs` | (Create) Fila editable de renglón. |
| `src/StockApp.Presentation/ViewModels/Movimientos/ItemConfirmacionPrecioVm.cs` | (Create) Fila de la confirmación de cambios de precio. |
| `src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs` | (Create) Cabecera, renglones, totales, alta en línea, confirmación de precios, guardado. |
| `src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml` (+ `.axaml.cs`) | (Create) Vista + wiring `DataContextChanged`. |
| `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` | (Modify) Comando de navegación `NavIngresoPorFactura`. |
| `src/StockApp.Presentation/Views/ShellMainView.axaml` | (Modify) Botón del sidebar. |
| `src/StockApp.Presentation/App.axaml.cs` | (Modify) DI de `IIngresoPorFacturaApiClient`, `IngresoPorFacturaViewModel`. |
| `tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs` | (Create) Tests de servicio (repos mockeados). |
| `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs` | (Create) Tests atómicos contra Postgres real. |
| `tests/StockApp.Api.Tests/IngresoPorFacturaEndpointTests.cs` | (Create) Matriz HTTP. |
| `tests/StockApp.ApiClient.Tests/IngresoPorFacturaApiClientTests.cs` | (Create) Serialización + mapeo de errores. |
| `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs` | (Create) Totales, alta en línea, confirmación de precios. |
| `src/StockApp.Application/Finanzas/GastoService.cs` | (Modify, Task 11) `AnularAsync` se bifurca a la anulación por asiento inverso cuando el gasto tiene movimientos. |
| `tests/StockApp.Application.Tests/Finanzas/GastoServiceTests.cs` | (Modify, Task 11) Tests de la bifurcación de `AnularAsync`. |
| `tests/StockApp.Api.Tests/GastosEndpointTests.cs` | (Modify, Task 11) 409 del DELETE existente ante stock insuficiente. |

---

## Task 1: Contratos y validaciones del servicio (Application, repo mockeado)

**Files:**
- Create: `src/StockApp.Application/Movimientos/IngresoPorFacturaDtos.cs`
- Create: `src/StockApp.Application/Movimientos/IIngresoPorFacturaService.cs`
- Create: `src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs`
- Modify: `src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs`
- Test: `tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs`

**Interfaces:**
- Consumes: `IMovimientoStockRepository.ObtenerProductoAsync(int)` (ya existe); `IProveedorRepository.ObtenerPorIdAsync(int)`; `IFuenteFinanciamientoRepository.ObtenerPorIdAsync(int)`; `IRubroGastoRepository.ObtenerPorIdAsync(int)`; `ILineaPoaRepository.ObtenerPorIdAsync(int)`; `IProductoRepository.ExisteCodigoAsync(string, int?)`; `IUnidadMedidaRepository.ObtenerPorIdAsync(int)`; `IGastoRepository` (inyectado para Task 5, sin uso en esta tarea).
- Produces: `IngresoPorFacturaDto`, `RenglonFacturaDto`, `ProductoNuevoDto`, `IngresoPorFacturaResultadoDto`, `IIngresoPorFacturaService.RegistrarAsync/AnularLoteAsync`, `RenglonIngresoFacturaArgs`, `IngresoPorFacturaArgs`, `ResultadoIngresoPorFactura`, `IMovimientoStockRepository.RegistrarIngresoPorFacturaAtomicoAsync(IngresoPorFacturaArgs)` — firmas consumidas por Task 2 (repo) y Task 8 (ViewModel).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Movimientos;

public class IngresoPorFacturaServiceTests
{
    private static (IngresoPorFacturaService svc,
                    Mock<IMovimientoStockRepository> movRepo,
                    Mock<IGastoRepository> gastoRepo,
                    Mock<IProveedorRepository> proveedores,
                    Mock<IFuenteFinanciamientoRepository> fuentes,
                    Mock<IRubroGastoRepository> rubros,
                    Mock<ILineaPoaRepository> lineasPoa,
                    Mock<IProductoRepository> productos,
                    Mock<IUnidadMedidaRepository> unidades,
                    Mock<ICurrentSession> session,
                    Mock<IAuthSvc> auth)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var movRepo    = new Mock<IMovimientoStockRepository>();
        var gastoRepo  = new Mock<IGastoRepository>();
        var proveedores = new Mock<IProveedorRepository>();
        var fuentes    = new Mock<IFuenteFinanciamientoRepository>();
        var rubros     = new Mock<IRubroGastoRepository>();
        var lineasPoa  = new Mock<ILineaPoaRepository>();
        var productos  = new Mock<IProductoRepository>();
        var unidades   = new Mock<IUnidadMedidaRepository>();
        var session    = new Mock<ICurrentSession>();
        var auth       = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(
            new StockApp.Application.Auth.UsuarioSesion(idSesion, "test-user", rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        proveedores.Setup(p => p.ObtenerPorIdAsync(1))
            .ReturnsAsync(new Proveedor { Id = 1, Nombre = "Proveedor Test", Activo = true });
        fuentes.Setup(f => f.ObtenerPorIdAsync(1))
            .ReturnsAsync(new FuenteFinanciamiento { Id = 1, Nombre = "Fuente Test", Activo = true });
        rubros.Setup(r => r.ObtenerPorIdAsync(1))
            .ReturnsAsync(new RubroGasto { Id = 1, Codigo = 1, Nombre = "Rubro Test", Activo = true });
        unidades.Setup(u => u.ObtenerPorIdAsync(1))
            .ReturnsAsync(new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true });
        productos.Setup(p => p.ExisteCodigoAsync(It.IsAny<string>(), null)).ReturnsAsync(false);

        var svc = new IngresoPorFacturaService(
            movRepo.Object, gastoRepo.Object, proveedores.Object, fuentes.Object, rubros.Object,
            lineasPoa.Object, productos.Object, unidades.Object, session.Object, auth.Object,
            Mock.Of<StockApp.Application.Reportes.IVersionReportes>());

        return (svc, movRepo, gastoRepo, proveedores, fuentes, rubros, lineasPoa, productos, unidades, session, auth);
    }

    private static RenglonFacturaDto RenglonExistente(
        int productoId = 1, decimal cantidad = 5m, decimal precio = 100m, bool actualizarPrecio = false) =>
        new(productoId, null, cantidad, precio, actualizarPrecio);

    private static IngresoPorFacturaDto DtoValido(params RenglonFacturaDto[] renglones) => new(
        ProveedorId: 1, NumeroFactura: "A-0001", NumeroOrden: null,
        Fecha: DateTime.UtcNow, Detalle: "Compra de insumos", Destino: null,
        MontoTotal: 500m, FuenteFinanciamientoId: 1, RubroGastoId: 1, LineaPoaId: null,
        CondicionPago: CondicionPago.Contado, FechaVencimiento: null,
        Renglones: renglones.Length == 0 ? new[] { RenglonExistente() } : renglones);

    // ── Autorización fail-closed ──────────────────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_SinPermisoMovimientos_LanzaExcepcionSinTocarElRepo()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, auth) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.RegistrarMovimientos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RegistrarAsync(DtoValido()));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_ConProductoNuevo_SinPermisoCatalogo_LanzaExcepcionSinTocarElRepo()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, auth) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarProductos))
            .Throws<UnauthorizedAccessException>();

        var renglon = new RenglonFacturaDto(
            null, new ProductoNuevoDto("SKU-N1", "Producto nuevo", null, 1, 50m), 3m, 20m, false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RegistrarAsync(DtoValido(renglon)));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    // ── Validaciones de renglones y cabecera ─────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_SinRenglones_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido() with { Renglones = Array.Empty<RenglonFacturaDto>() };

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RegistrarAsync_CantidadCeroONegativa_LanzaArgumentException(decimal cantidad)
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido(RenglonExistente(cantidad: cantidad));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_PrecioUnitarioNegativo_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido(RenglonExistente(precio: -1m));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_MontoTotalCeroONegativo_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido() with { MontoTotal = 0m };

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_RenglonSinProductoIdNiProductoNuevo_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var renglon = new RenglonFacturaDto(null, null, 1m, 10m, false);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(DtoValido(renglon)));
    }

    [Fact]
    public async Task RegistrarAsync_RenglonConProductoIdYProductoNuevoALaVez_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var renglon = new RenglonFacturaDto(
            1, new ProductoNuevoDto("X", "Y", null, 1, 1m), 1m, 10m, false);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(DtoValido(renglon)));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.RegistrarAsync(DtoValido()));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoInactivo_LanzaReglaDeNegocio()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1))
            .ReturnsAsync(new Producto { Id = 1, Codigo = "SKU1", Activo = false, StockActual = 10m });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.RegistrarAsync(DtoValido()));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoDuplicadoEnDosRenglones_SeAcepta()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1))
            .ReturnsAsync(new Producto { Id = 1, Codigo = "SKU1", Activo = true, StockActual = 10m, PrecioCosto = 5m });
        movRepo.Setup(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()))
            .ReturnsAsync(new ResultadoIngresoPorFactura(1, new List<int> { 10, 11 }));

        var dto = DtoValido(RenglonExistente(cantidad: 2m), RenglonExistente(cantidad: 3m));

        var resultado = await svc.RegistrarAsync(dto);

        Assert.Equal(1, resultado.GastoId);
        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(
            It.Is<IngresoPorFacturaArgs>(a => a.Renglones.Count == 2)), Times.Once);
    }

    // ── Camino feliz: totales y delegación ────────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_DatosValidos_DelegaAlRepoYCalculaTotales()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1))
            .ReturnsAsync(new Producto { Id = 1, Codigo = "SKU1", Activo = true, StockActual = 10m, PrecioCosto = 5m });
        movRepo.Setup(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()))
            .ReturnsAsync(new ResultadoIngresoPorFactura(7, new List<int> { 100 }));

        var dto = DtoValido(RenglonExistente(cantidad: 5m, precio: 90m)) with { MontoTotal = 500m };

        var resultado = await svc.RegistrarAsync(dto);

        Assert.Equal(7, resultado.GastoId);
        Assert.Equal(450m, resultado.SumaRenglones);       // 5 * 90
        Assert.Equal(50m, resultado.DiferenciaConTotal);    // 500 - 450
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~IngresoPorFacturaServiceTests"`
Expected: FAIL — no compila (`IngresoPorFacturaService`, `IIngresoPorFacturaService`, `IngresoPorFacturaDto`, `RenglonFacturaDto`, `ProductoNuevoDto`, `IngresoPorFacturaResultadoDto`, `RenglonIngresoFacturaArgs`, `IngresoPorFacturaArgs`, `ResultadoIngresoPorFactura` no existen).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs
// (agregar DEBAJO de RecalculoAtomicoArgs, antes de la interfaz)

/// <summary>Renglón del lote de ingreso por factura, ya validado y compuesto por el service.</summary>
public record RenglonIngresoFacturaArgs(
    int? ProductoId,                  // producto existente
    Producto? ProductoNuevo,          // entidad nueva sin Id, o null si ProductoId no es null
    decimal Cantidad,
    decimal PrecioUnitario,
    bool ActualizarPrecioCosto,
    decimal? PrecioCostoAnterior);    // snapshot pre-mutación, solo si ActualizarPrecioCosto

/// <summary>Args del lote atómico de ingreso por factura (Gasto + N MovimientoStock + deltas de stock).</summary>
public record IngresoPorFacturaArgs(
    Gasto Gasto,
    IReadOnlyList<RenglonIngresoFacturaArgs> Renglones,
    int UsuarioId,
    string DetalleAuditoria);

/// <summary>Resultado del alta atómica: id del gasto y los ids de movimiento generados.</summary>
public record ResultadoIngresoPorFactura(int GastoId, IReadOnlyList<int> MovimientoIds);
```

Y agregar el método a la interfaz `IMovimientoStockRepository` (junto a `RegistrarMovimientoAtomicoAsync`):

```csharp
    /// <summary>
    /// ATÓMICO: insert Gasto + insert productos nuevos + insert N MovimientoStock + deltas de
    /// StockActual + cambios selectivos de PrecioCosto + LogAuditoria, todo en UNA transacción.
    /// </summary>
    Task<ResultadoIngresoPorFactura> RegistrarIngresoPorFacturaAtomicoAsync(IngresoPorFacturaArgs args);
```

También agregar `using StockApp.Domain.Entities;` ya está presente en el archivo (usado por `Producto`, `Gasto` en el mismo namespace `StockApp.Domain.Entities` — sin nuevo using).

```csharp
// src/StockApp.Application/Movimientos/IngresoPorFacturaDtos.cs
using StockApp.Domain.Enums;

namespace StockApp.Application.Movimientos;

/// <summary>Cabecera + renglones de una factura de compra a ingresar en un solo lote atómico.</summary>
public record IngresoPorFacturaDto(
    int ProveedorId,
    string? NumeroFactura,
    string? NumeroOrden,
    DateTime Fecha,
    string Detalle,
    string? Destino,
    decimal MontoTotal,
    int FuenteFinanciamientoId,
    int RubroGastoId,
    int? LineaPoaId,
    CondicionPago CondicionPago,
    DateTime? FechaVencimiento,
    IReadOnlyList<RenglonFacturaDto> Renglones);

/// <summary>Renglón de artículo. Exactamente uno de ProductoId/ProductoNuevo debe venir seteado.</summary>
public record RenglonFacturaDto(
    int? ProductoId,
    ProductoNuevoDto? ProductoNuevo,
    decimal Cantidad,
    decimal PrecioUnitario,
    bool ActualizarPrecioCosto);

/// <summary>Datos del alta en línea de un producto nuevo dentro del lote.</summary>
public record ProductoNuevoDto(
    string Codigo,
    string Nombre,
    int? CategoriaId,
    int UnidadMedidaId,
    decimal PrecioVenta);

/// <summary>Resultado del alta: id del gasto, ids de movimiento generados y totales calculados.</summary>
public record IngresoPorFacturaResultadoDto(
    int GastoId,
    IReadOnlyList<int> MovimientoIds,
    decimal SumaRenglones,
    decimal DiferenciaConTotal);
```

```csharp
// src/StockApp.Application/Movimientos/IIngresoPorFacturaService.cs
namespace StockApp.Application.Movimientos;

/// <summary>Ingreso de stock por factura en un solo lote atómico (Gasto + N MovimientoStock).</summary>
public interface IIngresoPorFacturaService
{
    Task<IngresoPorFacturaResultadoDto> RegistrarAsync(IngresoPorFacturaDto dto);

    /// <summary>Anula el lote completo por asiento inverso. Ver Task 5.</summary>
    Task AnularLoteAsync(int gastoId);
}
```

```csharp
// src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Movimientos;

/// <summary>
/// Servicio de ingreso de stock por factura. Patrón: auth → validación → composición de
/// entidades → delegación al repo atómico. AnularLoteAsync se implementa en Task 5.
/// </summary>
public class IngresoPorFacturaService : IIngresoPorFacturaService
{
    private readonly IMovimientoStockRepository        _movRepo;
    private readonly IGastoRepository                  _gastoRepo;
    private readonly IProveedorRepository              _proveedores;
    private readonly IFuenteFinanciamientoRepository    _fuentes;
    private readonly IRubroGastoRepository              _rubros;
    private readonly ILineaPoaRepository                _lineasPoa;
    private readonly IProductoRepository                _productos;
    private readonly IUnidadMedidaRepository            _unidades;
    private readonly ICurrentSession                    _session;
    private readonly IAuthorizationService              _auth;
    private readonly IVersionReportes                   _version;

    public IngresoPorFacturaService(
        IMovimientoStockRepository movRepo,
        IGastoRepository gastoRepo,
        IProveedorRepository proveedores,
        IFuenteFinanciamientoRepository fuentes,
        IRubroGastoRepository rubros,
        ILineaPoaRepository lineasPoa,
        IProductoRepository productos,
        IUnidadMedidaRepository unidades,
        ICurrentSession session,
        IAuthorizationService auth,
        IVersionReportes version)
    {
        _movRepo     = movRepo;
        _gastoRepo   = gastoRepo;
        _proveedores = proveedores;
        _fuentes     = fuentes;
        _rubros      = rubros;
        _lineasPoa   = lineasPoa;
        _productos   = productos;
        _unidades    = unidades;
        _session     = session;
        _auth        = auth;
        _version     = version;
    }

    public async Task<IngresoPorFacturaResultadoDto> RegistrarAsync(IngresoPorFacturaDto dto)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarMovimientos);
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var requierePermisoCatalogo = dto.Renglones.Any(r => r.ProductoNuevo is not null || r.ActualizarPrecioCosto);
        if (requierePermisoCatalogo)
            _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);

        if (dto.Renglones.Count == 0)
            throw new ArgumentException("La factura debe tener al menos un renglón.", nameof(dto.Renglones));

        foreach (var renglon in dto.Renglones)
        {
            if (renglon.Cantidad <= 0)
                throw new ArgumentException("La cantidad de cada renglón debe ser mayor que cero.", nameof(renglon.Cantidad));
            if (renglon.PrecioUnitario < 0)
                throw new ArgumentException("El precio unitario no puede ser negativo.", nameof(renglon.PrecioUnitario));
            if (renglon.ProductoId is null && renglon.ProductoNuevo is null)
                throw new ArgumentException("Cada renglón debe indicar un producto existente o los datos de un producto nuevo.");
            if (renglon.ProductoId is not null && renglon.ProductoNuevo is not null)
                throw new ArgumentException("Un renglón no puede traer productoId y productoNuevo a la vez.");
        }

        if (dto.MontoTotal <= 0)
            throw new ArgumentException("El monto total de la factura debe ser mayor que cero.", nameof(dto.MontoTotal));

        if (dto.CondicionPago == CondicionPago.Credito && dto.FechaVencimiento is null)
            throw new ReglaDeNegocioException("Una factura a crédito exige fecha de vencimiento.");
        if (dto.CondicionPago == CondicionPago.Contado && dto.FechaVencimiento is not null)
            throw new ReglaDeNegocioException("Una factura de contado no lleva fecha de vencimiento.");

        var proveedor = await _proveedores.ObtenerPorIdAsync(dto.ProveedorId)
            ?? throw new EntidadNoEncontradaException($"Proveedor {dto.ProveedorId} no encontrado.");
        if (!proveedor.Activo)
            throw new ReglaDeNegocioException($"El proveedor '{proveedor.Nombre}' está dado de baja.");

        var fuente = await _fuentes.ObtenerPorIdAsync(dto.FuenteFinanciamientoId)
            ?? throw new EntidadNoEncontradaException($"Fuente de financiamiento {dto.FuenteFinanciamientoId} no encontrada.");
        if (!fuente.Activo)
            throw new ReglaDeNegocioException($"La fuente de financiamiento '{fuente.Nombre}' está dada de baja.");

        var rubro = await _rubros.ObtenerPorIdAsync(dto.RubroGastoId)
            ?? throw new EntidadNoEncontradaException($"Rubro de gasto {dto.RubroGastoId} no encontrado.");
        if (!rubro.Activo)
            throw new ReglaDeNegocioException($"El rubro '{rubro.Nombre}' está dado de baja.");

        if (dto.LineaPoaId is not null)
        {
            var linea = await _lineasPoa.ObtenerPorIdAsync(dto.LineaPoaId.Value)
                ?? throw new EntidadNoEncontradaException($"Línea POA {dto.LineaPoaId} no encontrada.");
            if (!linea.Activo)
                throw new ReglaDeNegocioException($"La línea POA '{linea.Nombre}' está dada de baja.");
        }

        var renglonesArgs = new List<RenglonIngresoFacturaArgs>(dto.Renglones.Count);
        foreach (var renglon in dto.Renglones)
        {
            if (renglon.ProductoId is int productoId)
            {
                var producto = await _movRepo.ObtenerProductoAsync(productoId)
                    ?? throw new EntidadNoEncontradaException($"Producto {productoId} no encontrado.");
                if (!producto.Activo)
                    throw new ReglaDeNegocioException($"El producto '{producto.Codigo}' está inactivo.");

                renglonesArgs.Add(new RenglonIngresoFacturaArgs(
                    ProductoId:            productoId,
                    ProductoNuevo:         null,
                    Cantidad:              renglon.Cantidad,
                    PrecioUnitario:        renglon.PrecioUnitario,
                    ActualizarPrecioCosto: renglon.ActualizarPrecioCosto,
                    PrecioCostoAnterior:   renglon.ActualizarPrecioCosto ? producto.PrecioCosto : null));
            }
            else
            {
                var nuevo = renglon.ProductoNuevo!;
                if (string.IsNullOrWhiteSpace(nuevo.Codigo))
                    throw new ArgumentException("El código del producto nuevo es obligatorio.");
                if (string.IsNullOrWhiteSpace(nuevo.Nombre))
                    throw new ArgumentException("El nombre del producto nuevo es obligatorio.");
                if (await _unidades.ObtenerPorIdAsync(nuevo.UnidadMedidaId) is null)
                    throw new ArgumentException($"La unidad de medida {nuevo.UnidadMedidaId} no existe.");
                if (await _productos.ExisteCodigoAsync(nuevo.Codigo, null))
                    throw new ReglaDeNegocioException($"Ya existe un producto con el código '{nuevo.Codigo}'.");

                var productoNuevo = new Producto
                {
                    Codigo         = nuevo.Codigo,
                    Nombre         = nuevo.Nombre,
                    CategoriaId    = nuevo.CategoriaId,
                    UnidadMedidaId = nuevo.UnidadMedidaId,
                    PrecioCosto    = renglon.PrecioUnitario,
                    PrecioVenta    = nuevo.PrecioVenta,
                    StockActual    = renglon.Cantidad,
                    Activo         = true,
                    FechaAlta      = DateTime.UtcNow,
                };

                renglonesArgs.Add(new RenglonIngresoFacturaArgs(
                    ProductoId:            null,
                    ProductoNuevo:         productoNuevo,
                    Cantidad:              renglon.Cantidad,
                    PrecioUnitario:        renglon.PrecioUnitario,
                    ActualizarPrecioCosto: false,
                    PrecioCostoAnterior:   null));
            }
        }

        var gasto = new Gasto
        {
            ProveedorId            = dto.ProveedorId,
            NumeroFactura          = string.IsNullOrWhiteSpace(dto.NumeroFactura) ? null : dto.NumeroFactura.Trim(),
            NumeroOrden            = string.IsNullOrWhiteSpace(dto.NumeroOrden) ? null : dto.NumeroOrden.Trim(),
            Detalle                = dto.Detalle,
            Destino                = dto.Destino,
            Fecha                  = dto.Fecha,
            MontoTotal             = dto.MontoTotal,
            FuenteFinanciamientoId = dto.FuenteFinanciamientoId,
            RubroGastoId           = dto.RubroGastoId,
            LineaPoaId             = dto.LineaPoaId,
            CondicionPago          = dto.CondicionPago,
            FechaVencimiento       = dto.FechaVencimiento,
        };

        var sumaRenglones = dto.Renglones.Sum(r => r.Cantidad * r.PrecioUnitario);
        var detalle = $"Proveedor={dto.ProveedorId}; Factura={gasto.NumeroFactura ?? "(sin factura)"}; " +
                      $"Renglones={dto.Renglones.Count}; SumaRenglones={sumaRenglones}; MontoTotal={dto.MontoTotal}";

        var args = new IngresoPorFacturaArgs(
            Gasto:            gasto,
            Renglones:        renglonesArgs,
            UsuarioId:        _session.UsuarioActual!.Id,
            DetalleAuditoria: detalle);

        var resultado = await _movRepo.RegistrarIngresoPorFacturaAtomicoAsync(args);

        _version.Invalidar();

        return new IngresoPorFacturaResultadoDto(
            GastoId:            resultado.GastoId,
            MovimientoIds:      resultado.MovimientoIds,
            SumaRenglones:      sumaRenglones,
            DiferenciaConTotal: dto.MontoTotal - sumaRenglones);
    }

    /// <summary>Implementado en Task 5 (necesita IMovimientoStockRepository.AnularIngresoPorFacturaAtomicoAsync).</summary>
    public Task AnularLoteAsync(int gastoId) => throw new NotImplementedException();
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~IngresoPorFacturaServiceTests"`
Expected: PASS — 12 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs \
        src/StockApp.Application/Movimientos/IngresoPorFacturaDtos.cs \
        src/StockApp.Application/Movimientos/IIngresoPorFacturaService.cs \
        src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs \
        tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs
git commit -m "feat(movimientos): agrega servicio de ingreso por factura con validaciones"
```

---

## Task 2: Escritura atómica del lote (Infrastructure)

Implementa `RegistrarIngresoPorFacturaAtomicoAsync` solo para renglones con producto EXISTENTE (`ProductoId` seteado). El alta de producto nuevo dentro del lote se agrega en Task 3; la actualización selectiva de precio, en Task 4.

**Files:**
- Modify: `src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs`
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs`

**Interfaces:**
- Consumes: `IngresoPorFacturaArgs`, `RenglonIngresoFacturaArgs`, `ResultadoIngresoPorFactura` (Task 1).
- Produces: `MovimientoStockRepository.RegistrarIngresoPorFacturaAtomicoAsync(IngresoPorFacturaArgs)` implementado — consumido por Task 3/4 (extensión del mismo método) y por Task 6 (endpoint vía el servicio).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de RegistrarIngresoPorFacturaAtomicoAsync / AnularIngresoPorFacturaAtomicoAsync contra
/// PostgreSQL real (Testcontainers). Task 2 cubre el alta con productos EXISTENTES; Task 3
/// (alta de producto nuevo), Task 4 (precio selectivo) y Task 5 (anulación) agregan tests acá.
/// </summary>
public class MovimientoStockRepositoryIngresoTests : PostgresRepositoryTestBase
{
    private readonly MovimientoStockRepository _repo;

    public MovimientoStockRepositoryIngresoTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new MovimientoStockRepository(Context);
    }

    private static UnidadMedida NuevaUm() => new() { Nombre = "Unidad", Abreviatura = "u" };

    private static Usuario NuevoUsuario() => new()
    {
        NombreUsuario = "admin", HashContrasena = "hash", Rol = RolUsuario.Admin,
        Activo = true, FechaAlta = DateTime.UtcNow,
    };

    private static Proveedor NuevoProveedor(string nombre = "Proveedor Test") => new() { Nombre = nombre };
    private static FuenteFinanciamiento NuevaFuente() => new() { Nombre = "Fuente Test" };
    private static RubroGasto NuevoRubro(int codigo) => new() { Codigo = codigo, Nombre = "Rubro Test" };

    private static Producto NuevoProducto(string codigo, UnidadMedida um, decimal stock = 10m, decimal precioCosto = 5m) => new()
    {
        Codigo = codigo, Nombre = $"Producto {codigo}", UnidadMedida = um,
        PrecioCosto = precioCosto, PrecioVenta = precioCosto * 2, StockActual = stock,
        Activo = true, FechaAlta = DateTime.UtcNow,
    };

    private async Task<(UnidadMedida um, Usuario usuario, Proveedor proveedor, FuenteFinanciamiento fuente, RubroGasto rubro)> SeedMaestrosAsync()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario();
        var proveedor = NuevoProveedor();
        var fuente = NuevaFuente();
        var rubro = NuevoRubro(Random.Shared.Next(1, 1_000_000));
        Context.AddRange(um, usuario, proveedor, fuente, rubro);
        await Context.SaveChangesAsync();
        return (um, usuario, proveedor, fuente, rubro);
    }

    private static Gasto NuevoGasto(Proveedor proveedor, FuenteFinanciamiento fuente, RubroGasto rubro, string? factura = "F-0001") => new()
    {
        ProveedorId = proveedor.Id, NumeroFactura = factura, Detalle = "Compra de insumos",
        Fecha = DateTime.UtcNow, MontoTotal = 500m,
        FuenteFinanciamientoId = fuente.Id, RubroGastoId = rubro.Id,
        CondicionPago = CondicionPago.Contado,
    };

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_TresRenglonesExistentes_PersisteGastoMovimientosYStock()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("ING-1", um, stock: 10m);
        var p2 = NuevoProducto("ING-2", um, stock: 20m);
        var p3 = NuevoProducto("ING-3", um, stock: 0m);
        Context.Productos.AddRange(p1, p2, p3);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var gasto = NuevoGasto(proveedor, fuente, rubro);
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[]
            {
                new RenglonIngresoFacturaArgs(p1.Id, null, 5m, 90m, false, null),
                new RenglonIngresoFacturaArgs(p2.Id, null, 3m, 50m, false, null),
                new RenglonIngresoFacturaArgs(p3.Id, null, 8m, 20m, false, null),
            },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "Ingreso por factura de prueba");

        var resultado = await _repo.RegistrarIngresoPorFacturaAtomicoAsync(args);

        Assert.True(resultado.GastoId > 0);
        Assert.Equal(3, resultado.MovimientoIds.Count);

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(1, await ctx2.Gastos.CountAsync());
        var movimientos = await ctx2.MovimientosStock.ToListAsync();
        Assert.Equal(3, movimientos.Count);
        Assert.All(movimientos, m => Assert.Equal(resultado.GastoId, m.GastoId));

        Assert.Equal(15m, (await ctx2.Productos.FindAsync(p1.Id))!.StockActual);
        Assert.Equal(23m, (await ctx2.Productos.FindAsync(p2.Id))!.StockActual);
        Assert.Equal(8m, (await ctx2.Productos.FindAsync(p3.Id))!.StockActual);

        var log = await ctx2.LogsAuditoria.SingleAsync();
        Assert.Equal(44, (int)log.Accion);   // AccionAuditada.IngresoPorFactura
        Assert.Equal(resultado.GastoId, log.EntidadId);
    }

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_FacturaDuplicada_LanzaReglaDeNegocioYNoEscribeNada()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var existente = NuevoGasto(proveedor, fuente, rubro, factura: "DUP-01");
        Context.Gastos.Add(existente);
        var producto = NuevoProducto("ING-DUP", um);
        Context.Productos.Add(producto);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var gastoNuevo = NuevoGasto(proveedor, fuente, rubro, factura: "DUP-01");
        var args = new IngresoPorFacturaArgs(
            Gasto: gastoNuevo,
            Renglones: new[] { new RenglonIngresoFacturaArgs(producto.Id, null, 2m, 10m, false, null) },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "intento duplicado");

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => _repo.RegistrarIngresoPorFacturaAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(1, await ctx2.Gastos.CountAsync());     // solo el existente
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
        Assert.Equal(producto.StockActual, (await ctx2.Productos.FindAsync(producto.Id))!.StockActual);
    }

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_FallaAlEscribirAuditoria_RevierteLosTresRenglones()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("ING-R1", um, stock: 10m);
        var p2 = NuevoProducto("ING-R2", um, stock: 10m);
        var p3 = NuevoProducto("ING-R3", um, stock: 10m);
        Context.Productos.AddRange(p1, p2, p3);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var repoRoto = new MovimientoStockRepositoryIngresoConDetalleNulo(Context);
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "ROLLBACK-01");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[]
            {
                new RenglonIngresoFacturaArgs(p1.Id, null, 1m, 10m, false, null),
                new RenglonIngresoFacturaArgs(p2.Id, null, 2m, 10m, false, null),
                new RenglonIngresoFacturaArgs(p3.Id, null, 3m, 10m, false, null),
            },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "se sobreescribe con null en el repo roto");

        await Assert.ThrowsAsync<DbUpdateException>(() => repoRoto.RegistrarIngresoPorFacturaAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(0, await ctx2.Gastos.CountAsync());
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p1.Id))!.StockActual);
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p2.Id))!.StockActual);
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p3.Id))!.StockActual);
        Assert.Equal(0, await ctx2.LogsAuditoria.CountAsync());
    }
}

/// <summary>
/// Variante que inyecta Detalle=null en el LogAuditoria final para forzar DbUpdateException
/// DENTRO de la transacción explícita, después de que los 3 renglones ya fueron procesados —
/// verifica que el rollback revierte también los ExecuteUpdateAsync de stock ya ejecutados.
/// Mismo patrón que MovimientoStockRepositoryConDetalleNulo (MovimientoStockRepositoryTests.cs).
/// </summary>
internal sealed class MovimientoStockRepositoryIngresoConDetalleNulo : MovimientoStockRepository
{
    private readonly AppDbContext _ctx;
    public MovimientoStockRepositoryIngresoConDetalleNulo(AppDbContext ctx) : base(ctx) => _ctx = ctx;

    public override async Task<ResultadoIngresoPorFactura> RegistrarIngresoPorFacturaAtomicoAsync(IngresoPorFacturaArgs args)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        _ctx.Gastos.Add(args.Gasto);
        var movimientos = new List<MovimientoStock>();
        foreach (var renglon in args.Renglones)
        {
            var productoId = renglon.ProductoId!.Value;
            await _ctx.Productos.Where(p => p.Id == productoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, p => p.StockActual + renglon.Cantidad));
            var movimiento = new MovimientoStock
            {
                ProductoId = productoId, UsuarioId = args.UsuarioId, Tipo = TipoMovimiento.Entrada,
                Motivo = MotivoMovimiento.Compra, Cantidad = renglon.Cantidad,
                PrecioUnitario = renglon.PrecioUnitario, Fecha = DateTime.UtcNow, Gasto = args.Gasto,
            };
            movimientos.Add(movimiento);
            _ctx.MovimientosStock.Add(movimiento);
        }
        await _ctx.SaveChangesAsync();

        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = args.UsuarioId, Fecha = DateTime.UtcNow,
            Accion = AccionAuditada.IngresoPorFactura, Entidad = "Gasto", EntidadId = args.Gasto.Id,
            Detalle = null!,   // viola NOT NULL → DbUpdateException dentro de la transacción
        });
        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoIngresoPorFactura(args.Gasto.Id, movimientos.Select(m => m.Id).ToList());
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: FAIL — `RegistrarIngresoPorFacturaAtomicoAsync` no está implementado en `MovimientoStockRepository` (no compila) y `AccionAuditada.IngresoPorFactura` no existe.

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Domain/Enums/AccionAuditada.cs
// Agregar DEBAJO de "ReversionImportacion = 43,", respetando append-only:

    // ── Movimientos — Ingreso por factura (append-only a partir de 44) ───────
    IngresoPorFactura           = 44,
    AnulacionIngresoPorFactura  = 45,
```

```csharp
// src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs
// Agregar "using Npgsql;" al bloque de usings del archivo (junto a los existentes).
// Agregar el método DEBAJO de RegistrarMovimientoAtomicoAsync:

    /// <inheritdoc/>
    /// ATÓMICO (Task 2: solo productos EXISTENTES; Task 3 agrega producto nuevo; Task 4 agrega
    /// precio selectivo). Dos SaveChangesAsync dentro de la MISMA transacción explícita: el
    /// primero genera los Ids de Gasto/MovimientoStock; el segundo escribe el LogAuditoria que
    /// necesita el Id del Gasto ya generado. Ambos se revierten juntos si algo falla antes del
    /// commit (mismo principio que RegistrarMovimientoAtomicoAsync).
    public virtual async Task<ResultadoIngresoPorFactura> RegistrarIngresoPorFacturaAtomicoAsync(IngresoPorFacturaArgs args)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        var movimientos = new List<MovimientoStock>(args.Renglones.Count);

        try
        {
            _ctx.Gastos.Add(args.Gasto);

            foreach (var renglon in args.Renglones)
            {
                if (renglon.ProductoId is not int productoId)
                    throw new InvalidOperationException(
                        "Alta de producto nuevo dentro del lote todavía no soportada (ver Task 3).");

                await _ctx.Productos
                    .Where(p => p.Id == productoId)
                    .ExecuteUpdateAsync(s => s.SetProperty(
                        p => p.StockActual, p => p.StockActual + renglon.Cantidad));

                var movimiento = new MovimientoStock
                {
                    ProductoId     = productoId,
                    UsuarioId      = args.UsuarioId,
                    Tipo           = TipoMovimiento.Entrada,
                    Motivo         = MotivoMovimiento.Compra,
                    Cantidad       = renglon.Cantidad,
                    PrecioUnitario = renglon.PrecioUnitario,
                    Fecha          = DateTime.UtcNow,
                    Gasto          = args.Gasto,
                };
                movimientos.Add(movimiento);
                _ctx.MovimientosStock.Add(movimiento);
            }

            await _ctx.SaveChangesAsync();

            _ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = args.UsuarioId,
                Fecha     = DateTime.UtcNow,
                Accion    = AccionAuditada.IngresoPorFactura,
                Entidad   = "Gasto",
                EntidadId = args.Gasto.Id,
                Detalle   = args.DetalleAuditoria,
            });
            await _ctx.SaveChangesAsync();

            await tx.CommitAsync();

            return new ResultadoIngresoPorFactura(args.Gasto.Id, movimientos.Select(m => m.Id).ToList());
        }
        catch (DbUpdateException ex) when (EsViolacionFacturaUnica(ex))
        {
            throw new ReglaDeNegocioException(
                $"Ya existe la factura '{args.Gasto.NumeroFactura}' para ese proveedor.");
        }
    }

    /// <summary>
    /// Mismo criterio que GastoRepository.EsViolacionFacturaUnica: traduce la violación del
    /// índice único parcial (Proveedor, Factura, Orden) a 409 en vez de dejarla llegar como
    /// DbUpdateException cruda (500).
    /// </summary>
    private static bool EsViolacionFacturaUnica(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
           && pg.ConstraintName == "IX_Gastos_ProveedorId_NumeroFactura_NumeroOrden";
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: PASS — 3 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs \
        src/StockApp.Domain/Enums/AccionAuditada.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs
git commit -m "feat(movimientos): escritura atomica del lote de ingreso por factura"
```

---

## Task 3: Alta de producto nuevo dentro del lote

**Files:**
- Modify: `src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs`
- Modify: `tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs`

**Interfaces:**
- Consumes: `RenglonIngresoFacturaArgs.ProductoNuevo` (ya declarado en Task 1); `Permisos.GestionarProductos` (gating ya implementado en Task 1's `IngresoPorFacturaService.RegistrarAsync`).
- Produces: rama `else` de `RegistrarIngresoPorFacturaAtomicoAsync` que inserta `Producto` nuevos vía nav fixup de EF, consumida sin cambios por Task 4 y Task 6.

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs` (dentro de la clase, antes del cierre):

```csharp
    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_ProductoNuevo_SeCreaConStockIgualALaCantidad()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var productoNuevo = new Producto
        {
            Codigo = "NUEVO-1", Nombre = "Producto recién creado", UnidadMedidaId = um.Id,
            PrecioCosto = 45m, PrecioVenta = 90m, StockActual = 0m, Activo = true, FechaAlta = DateTime.UtcNow,
        };

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "NUEVO-FAC-1");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[] { new RenglonIngresoFacturaArgs(null, productoNuevo, 12m, 45m, false, null) },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "Alta de producto nuevo en el lote");

        var resultado = await _repo.RegistrarIngresoPorFacturaAtomicoAsync(args);

        await using var ctx2 = Fixture.CrearContexto();
        var creado = await ctx2.Productos.SingleAsync(p => p.Codigo == "NUEVO-1");
        Assert.Equal(12m, creado.StockActual);
        Assert.Equal(45m, creado.PrecioCosto);

        var movimiento = await ctx2.MovimientosStock.SingleAsync();
        Assert.Equal(creado.Id, movimiento.ProductoId);
        Assert.Equal(resultado.GastoId, movimiento.GastoId);
    }

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_ProductoNuevoConCodigoDuplicado_RollbackTotal()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var existente = NuevoProducto("DUP-PROD", um);
        Context.Productos.Add(existente);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var productoNuevo = new Producto
        {
            Codigo = "DUP-PROD", Nombre = "Choca contra el existente", UnidadMedidaId = um.Id,
            PrecioCosto = 10m, PrecioVenta = 20m, StockActual = 0m, Activo = true, FechaAlta = DateTime.UtcNow,
        };
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "DUP-PROD-FAC");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[] { new RenglonIngresoFacturaArgs(null, productoNuevo, 5m, 10m, false, null) },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "código duplicado");

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => _repo.RegistrarIngresoPorFacturaAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(1, await ctx2.Productos.CountAsync());   // solo el existente
        Assert.Equal(0, await ctx2.Gastos.CountAsync());
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
    }
```

Agregar a `tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs` (dentro de la clase):

```csharp
    [Fact]
    public async Task RegistrarAsync_ProductoNuevo_SinPermisoCatalogo_NoLlegaAlRepo()
    {
        // Ya cubierto por RegistrarAsync_ConProductoNuevo_SinPermisoCatalogo_LanzaExcepcionSinTocarElRepo
        // (Task 1). Este test confirma que CON permiso, un renglón de producto nuevo SÍ llega al
        // repo con ProductoNuevo seteado y PrecioCosto == PrecioUnitario del renglón.
        var (svc, movRepo, _, _, _, _, _, _, unidades, _, _) = Crear();
        unidades.Setup(u => u.ObtenerPorIdAsync(2))
            .ReturnsAsync(new UnidadMedida { Id = 2, Nombre = "Kilo", Abreviatura = "kg", Activo = true });
        movRepo.Setup(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()))
            .ReturnsAsync(new ResultadoIngresoPorFactura(1, new List<int> { 1 }));

        var renglon = new RenglonFacturaDto(
            null, new ProductoNuevoDto("SKU-N2", "Producto Nuevo", null, 2, 80m), 4m, 35m, false);

        await svc.RegistrarAsync(DtoValido(renglon));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.Is<IngresoPorFacturaArgs>(a =>
            a.Renglones.Single().ProductoNuevo!.Codigo == "SKU-N2"
            && a.Renglones.Single().ProductoNuevo!.PrecioCosto == 35m
            && a.Renglones.Single().ProductoNuevo!.StockActual == 4m)), Times.Once);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: FAIL — `RegistrarIngresoPorFacturaAtomicoAsync_ProductoNuevo_SeCreaConStockIgualALaCantidad` lanza `InvalidOperationException` ("todavía no soportada").

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs
// Reemplazar el cuerpo del foreach de RegistrarIngresoPorFacturaAtomicoAsync (dentro del try):

            foreach (var renglon in args.Renglones)
            {
                var movimiento = new MovimientoStock
                {
                    UsuarioId      = args.UsuarioId,
                    Tipo           = TipoMovimiento.Entrada,
                    Motivo         = MotivoMovimiento.Compra,
                    Cantidad       = renglon.Cantidad,
                    PrecioUnitario = renglon.PrecioUnitario,
                    Fecha          = DateTime.UtcNow,
                    Gasto          = args.Gasto,
                };

                if (renglon.ProductoId is int productoId)
                {
                    await _ctx.Productos
                        .Where(p => p.Id == productoId)
                        .ExecuteUpdateAsync(s => s.SetProperty(
                            p => p.StockActual, p => p.StockActual + renglon.Cantidad));
                    movimiento.ProductoId = productoId;
                }
                else
                {
                    // Producto nuevo: se inserta en la MISMA transacción. El nav fixup de EF
                    // asigna MovimientoStock.ProductoId al Id generado del producto recién
                    // insertado, sin necesidad de conocerlo de antemano.
                    _ctx.Productos.Add(renglon.ProductoNuevo!);
                    movimiento.Producto = renglon.ProductoNuevo;
                }

                movimientos.Add(movimiento);
                _ctx.MovimientosStock.Add(movimiento);
            }
```

Y agregar el segundo catch, justo debajo del catch de `EsViolacionFacturaUnica`:

```csharp
        catch (DbUpdateException ex) when (EsViolacionCodigoProducto(ex))
        {
            throw new ReglaDeNegocioException("Ya existe un producto con ese código.");
        }
```

Con el helper correspondiente, debajo de `EsViolacionFacturaUnica`:

```csharp
    /// <summary>Índice único IX_Productos_Codigo — defensa en profundidad ante la carrera que el
    /// chequeo previo de IngresoPorFacturaService.ExisteCodigoAsync no cierra (check-then-insert).</summary>
    private static bool EsViolacionCodigoProducto(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
           && pg.ConstraintName == "IX_Productos_Codigo";
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: PASS — 5 tests verdes.

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~IngresoPorFacturaServiceTests"`
Expected: PASS — 13 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs \
        tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs
git commit -m "feat(movimientos): alta de producto nuevo dentro del lote de ingreso por factura"
```

---

## Task 4: Actualización selectiva del precio de costo

**Files:**
- Modify: `src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs`

**Interfaces:**
- Consumes: `RenglonIngresoFacturaArgs.ActualizarPrecioCosto` / `PrecioCostoAnterior` (Task 1).
- Produces: rama de `RegistrarIngresoPorFacturaAtomicoAsync` que aplica `PrecioCosto` selectivo + audita `AccionAuditada.CambioPrecio` — consumida sin cambios por Task 6/9.

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs`:

```csharp
    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_SoloActualizaLosTildados()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("PRC-1", um, stock: 10m, precioCosto: 10m);
        var p2 = NuevoProducto("PRC-2", um, stock: 10m, precioCosto: 10m);
        Context.Productos.AddRange(p1, p2);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "PRC-FAC-1");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[]
            {
                new RenglonIngresoFacturaArgs(p1.Id, null, 2m, 15m, true, 10m),   // se actualiza
                new RenglonIngresoFacturaArgs(p2.Id, null, 2m, 15m, false, null), // NO se actualiza
            },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "precio selectivo");

        await _repo.RegistrarIngresoPorFacturaAtomicoAsync(args);

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(15m, (await ctx2.Productos.FindAsync(p1.Id))!.PrecioCosto);
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p2.Id))!.PrecioCosto);   // intacto

        var logCambioPrecio = await ctx2.LogsAuditoria.SingleAsync(l => l.Accion == AccionAuditada.CambioPrecio);
        Assert.Equal(p1.Id, logCambioPrecio.EntidadId);
    }

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_RollbackRevierteTambienLosPrecios()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("PRC-RB-1", um, stock: 10m, precioCosto: 10m);
        Context.Productos.Add(p1);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var repoRoto = new MovimientoStockRepositoryIngresoConDetalleNulo(Context);
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "PRC-RB-FAC");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[] { new RenglonIngresoFacturaArgs(p1.Id, null, 1m, 99m, true, 10m) },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "se sobreescribe con null en el repo roto");

        await Assert.ThrowsAsync<DbUpdateException>(() => repoRoto.RegistrarIngresoPorFacturaAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p1.Id))!.PrecioCosto);   // sin cambios
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: FAIL — `p1.PrecioCosto` sigue en 10m tras el llamado (la rama `ActualizarPrecioCosto` no existe todavía).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs
// Dentro del "if (renglon.ProductoId is int productoId)" del foreach, DEBAJO del
// ExecuteUpdateAsync de StockActual y de "movimiento.ProductoId = productoId;":

                    if (renglon.ActualizarPrecioCosto)
                    {
                        await _ctx.Productos
                            .Where(p => p.Id == productoId)
                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PrecioCosto, renglon.PrecioUnitario));

                        _ctx.LogsAuditoria.Add(new LogAuditoria
                        {
                            UsuarioId = args.UsuarioId,
                            Fecha     = DateTime.UtcNow,
                            Accion    = AccionAuditada.CambioPrecio,
                            Entidad   = "Producto",
                            EntidadId = productoId,
                            Detalle   = $"PrecioCosto: {renglon.PrecioCostoAnterior} → {renglon.PrecioUnitario} (ingreso por factura)",
                        });
                    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: PASS — 7 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs
git commit -m "feat(movimientos): actualizacion selectiva de precio de costo en el ingreso por factura"
```

---

## Task 5: Anulación del lote por asiento inverso

**Files:**
- Modify: `src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs`
- Modify: `src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs`
- Modify: `src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs`
- Modify: `tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs`

**Interfaces:**
- Consumes: `IGastoRepository.ObtenerPorIdAsync(int)` (ya inyectado en el servicio desde Task 1).
- Produces: `IMovimientoStockRepository.AnularIngresoPorFacturaAtomicoAsync(int, int, string)`, `ItemFaltanteStock`, `ResultadoAnulacionIngreso`, `IngresoPorFacturaService.AnularLoteAsync(int)` — consumidos por Task 6 (endpoint de anulación) y Task 9/10 (deuda de UI, fuera de alcance de este plan — ver cierre).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs`:

```csharp
    [Fact]
    public async Task AnularIngresoPorFacturaAtomicoAsync_ConStockSuficiente_GeneraSalidasEspejoYAnulaElGasto()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("ANU-1", um, stock: 20m);
        var p2 = NuevoProducto("ANU-2", um, stock: 20m);
        Context.Productos.AddRange(p1, p2);
        await Context.SaveChangesAsync();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "ANU-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.AddRange(
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 8m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id },
            new MovimientoStock { ProductoId = p2.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.AnularIngresoPorFacturaAtomicoAsync(gasto.Id, usuario.Id, "Anulación de prueba");

        Assert.Equal(ResultadoAnulacionIngresoEstado.Ok, resultado.Estado);

        await using var ctx2 = Fixture.CrearContexto();
        var salidas = await ctx2.MovimientosStock.Where(m => m.Tipo == TipoMovimiento.Salida).ToListAsync();
        Assert.Equal(2, salidas.Count);
        Assert.All(salidas, s => Assert.Equal(MotivoMovimiento.Ajuste, s.Motivo));

        Assert.Equal(12m, (await ctx2.Productos.FindAsync(p1.Id))!.StockActual);   // 20 - 8
        Assert.Equal(15m, (await ctx2.Productos.FindAsync(p2.Id))!.StockActual);   // 20 - 5

        var gastoFresh = await ctx2.Gastos.FindAsync(gasto.Id);
        Assert.False(gastoFresh!.Activo);

        var log = await ctx2.LogsAuditoria.SingleAsync();
        Assert.Equal(45, (int)log.Accion);   // AccionAuditada.AnulacionIngresoPorFactura
    }

    [Fact]
    public async Task AnularIngresoPorFacturaAtomicoAsync_StockInsuficienteEnUnoDeTres_NoEscribeNadaYNombraElProducto()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("INS-1", um, stock: 10m);
        var p2 = NuevoProducto("INS-2", um, stock: 10m);
        var p3 = NuevoProducto("INS-3", um, stock: 2m);   // insuficiente: se consumió parte
        p3.Nombre = "Producto consumido";
        Context.Productos.AddRange(p1, p2, p3);
        await Context.SaveChangesAsync();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "INS-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.AddRange(
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id },
            new MovimientoStock { ProductoId = p2.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id },
            new MovimientoStock { ProductoId = p3.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.AnularIngresoPorFacturaAtomicoAsync(gasto.Id, usuario.Id, "Anulación con faltante");

        Assert.Equal(ResultadoAnulacionIngresoEstado.StockInsuficiente, resultado.Estado);
        var faltante = Assert.Single(resultado.Faltantes);
        Assert.Equal("Producto consumido", faltante.ProductoNombre);
        Assert.Equal(2m, faltante.StockActual);
        Assert.Equal(5m, faltante.CantidadNecesaria);

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(3, await ctx2.MovimientosStock.CountAsync());   // solo las 3 entradas originales
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p1.Id))!.StockActual);
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p2.Id))!.StockActual);
        Assert.Equal(2m, (await ctx2.Productos.FindAsync(p3.Id))!.StockActual);
        Assert.True((await ctx2.Gastos.FindAsync(gasto.Id))!.Activo);
    }

    [Fact]
    public async Task AnularIngresoPorFacturaAtomicoAsync_GastoConMovimientosAsociadosPorElFlujoViejo_TambienSeAnula()
    {
        // Decisión 9 del spec: la anulación aplica a CUALQUIER gasto con movimientos asociados,
        // no solo a los creados por esta pantalla — cubre el vínculo hecho a mano desde
        // GastoService.AsociarMovimientosAsync (flujo "Asociar factura" existente).
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var producto = NuevoProducto("VIEJO-1", um, stock: 15m);
        Context.Productos.Add(producto);
        await Context.SaveChangesAsync();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "VIEJO-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada,
            Motivo = MotivoMovimiento.Compra, Cantidad = 6m, PrecioUnitario = 10m,
            Fecha = DateTime.UtcNow, GastoId = gasto.Id,   // vínculo hecho por el flujo viejo
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.AnularIngresoPorFacturaAtomicoAsync(gasto.Id, usuario.Id, "Anulación de vínculo viejo");

        Assert.Equal(ResultadoAnulacionIngresoEstado.Ok, resultado.Estado);
        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(9m, (await ctx2.Productos.FindAsync(producto.Id))!.StockActual);   // 15 - 6
    }
```

Agregar a `tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs`:

```csharp
    [Fact]
    public async Task AnularLoteAsync_GastoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, _, gastoRepo, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(99)).ReturnsAsync((Gasto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.AnularLoteAsync(99));
    }

    [Fact]
    public async Task AnularLoteAsync_GastoYaAnulado_LanzaReglaDeNegocio()
    {
        var (svc, _, gastoRepo, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(5)).ReturnsAsync(new Gasto { Id = 5, Activo = false });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AnularLoteAsync(5));
    }

    [Fact]
    public async Task AnularLoteAsync_GastoConPagosActivos_LanzaReglaDeNegocio()
    {
        var (svc, _, gastoRepo, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(6)).ReturnsAsync(new Gasto
        {
            Id = 6, Activo = true,
            Pagos = new List<PagoGasto> { new() { Id = 1, Monto = 100m, Activo = true } },
        });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AnularLoteAsync(6));
    }

    [Fact]
    public async Task AnularLoteAsync_StockInsuficiente_LanzaReglaDeNegocioConElDetalle()
    {
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(7)).ReturnsAsync(new Gasto { Id = 7, Activo = true });
        movRepo.Setup(m => m.AnularIngresoPorFacturaAtomicoAsync(7, It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ResultadoAnulacionIngreso(
                ResultadoAnulacionIngresoEstado.StockInsuficiente,
                new List<ItemFaltanteStock> { new(1, "Producto X", 2m, 5m) }));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AnularLoteAsync(7));
        Assert.Contains("Producto X", ex.Message);
    }

    [Fact]
    public async Task AnularLoteAsync_DatosValidos_DelegaAlRepo()
    {
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(8)).ReturnsAsync(new Gasto { Id = 8, Activo = true });
        movRepo.Setup(m => m.AnularIngresoPorFacturaAtomicoAsync(8, It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ResultadoAnulacionIngreso(ResultadoAnulacionIngresoEstado.Ok, Array.Empty<ItemFaltanteStock>()));

        await svc.AnularLoteAsync(8);

        movRepo.Verify(m => m.AnularIngresoPorFacturaAtomicoAsync(8, It.IsAny<int>(), It.IsAny<string>()), Times.Once);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: FAIL — no compila (`AnularIngresoPorFacturaAtomicoAsync`, `ResultadoAnulacionIngresoEstado`, `ItemFaltanteStock`, `ResultadoAnulacionIngreso` no existen).

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~IngresoPorFacturaServiceTests"`
Expected: FAIL — `AnularLoteAsync_*` fallan con `NotImplementedException` (stub de Task 1).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs
// Agregar DEBAJO de ResultadoIngresoPorFactura:

/// <summary>Estado del intento de anulación atómica del lote.</summary>
public enum ResultadoAnulacionIngresoEstado { Ok, StockInsuficiente }

/// <summary>Detalle de un producto sin stock suficiente para la salida espejo de la anulación.</summary>
public record ItemFaltanteStock(int ProductoId, string ProductoNombre, decimal StockActual, decimal CantidadNecesaria);

/// <summary>Resultado tipado de la anulación atómica.</summary>
public record ResultadoAnulacionIngreso(
    ResultadoAnulacionIngresoEstado Estado,
    IReadOnlyList<ItemFaltanteStock> Faltantes);
```

Y agregar a la interfaz, debajo de `RegistrarIngresoPorFacturaAtomicoAsync`:

```csharp
    /// <summary>
    /// ATÓMICO: verifica stock suficiente para CADA salida espejo antes de escribir nada;
    /// inserta las salidas, descuenta StockActual, marca Gasto.Activo=false y audita.
    /// </summary>
    Task<ResultadoAnulacionIngreso> AnularIngresoPorFacturaAtomicoAsync(
        int gastoId, int usuarioId, string detalleAuditoria);
```

```csharp
// src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs
// Agregar DEBAJO de RegistrarIngresoPorFacturaAtomicoAsync:

    /// <inheritdoc/>
    /// ATÓMICO: lee TODOS los movimientos de entrada del gasto agrupados por producto, verifica
    /// stock suficiente para TODOS antes de escribir una sola fila (ningún saldo negativo
    /// silencioso — spec riesgo/decisión 7), y recién ahí inserta las salidas espejo.
    public virtual async Task<ResultadoAnulacionIngreso> AnularIngresoPorFacturaAtomicoAsync(
        int gastoId, int usuarioId, string detalleAuditoria)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        var movimientos = await _ctx.MovimientosStock
            .Include(m => m.Producto)
            .Where(m => m.GastoId == gastoId)
            .ToListAsync();

        var faltantes = new List<ItemFaltanteStock>();
        foreach (var grupo in movimientos.GroupBy(m => m.ProductoId))
        {
            var necesario = grupo.Sum(m => m.Cantidad);
            var producto  = grupo.First().Producto!;
            if (producto.StockActual < necesario)
                faltantes.Add(new ItemFaltanteStock(producto.Id, producto.Nombre, producto.StockActual, necesario));
        }

        if (faltantes.Count > 0)
        {
            await tx.RollbackAsync();
            return new ResultadoAnulacionIngreso(ResultadoAnulacionIngresoEstado.StockInsuficiente, faltantes);
        }

        foreach (var movimiento in movimientos)
        {
            await _ctx.Productos
                .Where(p => p.Id == movimiento.ProductoId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    p => p.StockActual, p => p.StockActual - movimiento.Cantidad));

            _ctx.MovimientosStock.Add(new MovimientoStock
            {
                ProductoId     = movimiento.ProductoId,
                UsuarioId      = usuarioId,
                Tipo           = TipoMovimiento.Salida,
                Motivo         = MotivoMovimiento.Ajuste,
                Cantidad       = movimiento.Cantidad,
                PrecioUnitario = movimiento.PrecioUnitario,
                Fecha          = DateTime.UtcNow,
                Comentario     = $"Anulación de ingreso por factura (Gasto {gastoId})",
            });
        }

        await _ctx.Gastos
            .Where(g => g.Id == gastoId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Activo, false));

        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = usuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = AccionAuditada.AnulacionIngresoPorFactura,
            Entidad   = "Gasto",
            EntidadId = gastoId,
            Detalle   = detalleAuditoria,
        });

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoAnulacionIngreso(ResultadoAnulacionIngresoEstado.Ok, Array.Empty<ItemFaltanteStock>());
    }
```

```csharp
// src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs
// Reemplazar el stub final:

    public async Task AnularLoteAsync(int gastoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarMovimientos);
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var gasto = await _gastoRepo.ObtenerPorIdAsync(gastoId)
            ?? throw new EntidadNoEncontradaException($"Gasto {gastoId} no encontrado.");

        if (!gasto.Activo)
            throw new ReglaDeNegocioException($"El gasto {gastoId} ya está anulado.");
        if (gasto.Pagos.Any(p => p.Activo))
            throw new ReglaDeNegocioException(
                "No se puede anular un gasto con pagos activos: primero anulá los pagos.");

        var detalle = $"Anulación de ingreso por factura '{gasto.NumeroFactura ?? "s/n"}' (Gasto {gastoId})";

        var resultado = await _movRepo.AnularIngresoPorFacturaAtomicoAsync(
            gastoId, _session.UsuarioActual!.Id, detalle);

        if (resultado.Estado == ResultadoAnulacionIngresoEstado.StockInsuficiente)
        {
            var detalleFaltantes = string.Join("; ", resultado.Faltantes.Select(f =>
                $"{f.ProductoNombre}: stock {f.StockActual}, necesita {f.CantidadNecesaria}"));
            throw new ReglaDeNegocioException(
                $"No se puede anular: stock insuficiente en {resultado.Faltantes.Count} producto(s). {detalleFaltantes}");
        }

        _version.Invalidar();
    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: PASS — 10 tests verdes.

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~IngresoPorFacturaServiceTests"`
Expected: PASS — 18 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs \
        src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs \
        src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs \
        tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs
git commit -m "feat(movimientos): anulacion del lote de ingreso por factura por asiento inverso"
```

---

## Task 6: Endpoints de API + DI

**Files:**
- Create: `src/StockApp.Api/Endpoints/IngresoPorFacturaEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/IngresoPorFacturaEndpointTests.cs`

**Interfaces:**
- Consumes: `IIngresoPorFacturaService.RegistrarAsync/AnularLoteAsync` (Task 1/5).
- Produces: `POST /movimientos/ingreso-factura`, `POST /movimientos/ingreso-factura/{gastoId:int}/anular` — consumidos por Task 7 (ApiClient).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Api.Tests/IngresoPorFacturaEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class IngresoPorFacturaEndpointTests : ApiTestBase
{
    public IngresoPorFacturaEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Seed de maestros + un producto activo con stock, para probar el camino feliz.</summary>
    private async Task<(int proveedorId, int fuenteId, int rubroId, int productoId)> SeedMaestrosAsync()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro ingreso" };
        ctx.AddRange(proveedor, fuente, rubro);
        await ctx.SaveChangesAsync();

        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "IPF-01", "Producto IPF", 5m);

        return (proveedor.Id, fuente.Id, rubro.Id, producto.Id);
    }

    private static IngresoPorFacturaRequest RequestValido(
        int proveedorId, int fuenteId, int rubroId, int productoId, string? factura = null) => new(
        ProveedorId: proveedorId, NumeroFactura: factura, NumeroOrden: null,
        Fecha: DateTime.UtcNow, Detalle: "Compra vía API", Destino: null, MontoTotal: 100m,
        FuenteFinanciamientoId: fuenteId, RubroGastoId: rubroId, LineaPoaId: null,
        CondicionPago: CondicionPago.Contado, FechaVencimiento: null,
        Lineas: new List<RenglonFacturaRequest>
        {
            new(productoId, null, 5m, 20m, false),
        });

    [Fact]
    public async Task PostIngresoFactura_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient()
            .PostAsJsonAsync("/movimientos/ingreso-factura", RequestValido(1, 1, 1, 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostIngresoFactura_ConTokenOperador_Crea201ConElResultado()
    {
        // Spec decisión 5: RegistrarMovimientos + RegistrarGastos + GestionarProductos los tiene
        // Admin Y Operador — no hay 403 por rol posible en este endpoint (AuthorizationService.cs).
        var (proveedorId, fuenteId, rubroId, productoId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-API-01"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<IngresoPorFacturaResultadoDto>();
        Assert.True(resultado!.GastoId > 0);
        Assert.Single(resultado.MovimientoIds);
        Assert.Equal(100m, resultado.SumaRenglones);   // 5 * 20
        Assert.Equal(0m, resultado.DiferenciaConTotal); // 100 - 100

        await using var verificacion = Factory.CrearContexto();
        var producto = await verificacion.Productos.SingleAsync(p => p.Id == productoId);
        Assert.Equal(10m, producto.StockActual);   // 5 + 5
    }

    [Fact]
    public async Task PostIngresoFactura_RenglonesVacios_Devuelve400()
    {
        var (proveedorId, fuenteId, rubroId, _) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var request = RequestValido(proveedorId, fuenteId, rubroId, 1) with { Lineas = new List<RenglonFacturaRequest>() };

        var response = await client.PostAsJsonAsync("/movimientos/ingreso-factura", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostIngresoFactura_FacturaDuplicada_Devuelve409()
    {
        var (proveedorId, fuenteId, rubroId, productoId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var primera = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-DUP-01"));
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        var segunda = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-DUP-01"));

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }

    [Fact]
    public async Task PostAnular_SinStockSuficiente_Devuelve409()
    {
        var (proveedorId, fuenteId, rubroId, productoId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var creado = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-ANU-01"));
        var resultado = await creado.Content.ReadFromJsonAsync<IngresoPorFacturaResultadoDto>();

        // Consumir el stock recién ingresado hasta dejarlo por debajo de lo necesario para revertir.
        await using (var ctx = Factory.CrearContexto())
        {
            await ctx.Productos.Where(p => p.Id == productoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, 1m));
        }

        var response = await client.PostAsync($"/movimientos/ingreso-factura/{resultado!.GastoId}/anular", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~IngresoPorFacturaEndpointTests"`
Expected: FAIL — no compila (`IngresoPorFacturaRequest`, `RenglonFacturaRequest`, la ruta `/movimientos/ingreso-factura` no existe → 404 en vez de los status esperados).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Api/Endpoints/IngresoPorFacturaEndpoints.cs
using StockApp.Application.Authorization;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record ProductoNuevoRequest(
    string Codigo, string Nombre, int? CategoriaId, int UnidadMedidaId, decimal PrecioVenta);

public record RenglonFacturaRequest(
    int? ProductoId,
    ProductoNuevoRequest? ProductoNuevo,
    decimal Cantidad,
    decimal PrecioUnitario,
    bool ActualizarPrecioCosto);

public record IngresoPorFacturaRequest(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    DateTime Fecha, string Detalle, string? Destino, decimal MontoTotal,
    int FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    List<RenglonFacturaRequest> Lineas);

public static class IngresoPorFacturaEndpoints
{
    public static IEndpointRouteBuilder MapIngresoPorFacturaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/movimientos/ingreso-factura");

        group.MapPost("/", async (IngresoPorFacturaRequest request, IIngresoPorFacturaService service) =>
        {
            var dto = new IngresoPorFacturaDto(
                request.ProveedorId, request.NumeroFactura, request.NumeroOrden,
                request.Fecha, request.Detalle, request.Destino, request.MontoTotal,
                request.FuenteFinanciamientoId, request.RubroGastoId, request.LineaPoaId,
                request.CondicionPago, request.FechaVencimiento,
                request.Lineas.Select(l => new RenglonFacturaDto(
                    l.ProductoId,
                    l.ProductoNuevo is null ? null : new ProductoNuevoDto(
                        l.ProductoNuevo.Codigo, l.ProductoNuevo.Nombre, l.ProductoNuevo.CategoriaId,
                        l.ProductoNuevo.UnidadMedidaId, l.ProductoNuevo.PrecioVenta),
                    l.Cantidad, l.PrecioUnitario, l.ActualizarPrecioCosto)).ToList());

            var resultado = await service.RegistrarAsync(dto);
            // Sin Location: mismo criterio que /movimientos y /finanzas/gastos (no hay
            // convención de Location en los POST del proyecto).
            return Results.Created((string?)null, resultado);
        })
        .RequireAuthorization(Permisos.RegistrarMovimientos);

        group.MapPost("/{gastoId:int}/anular", async (int gastoId, IIngresoPorFacturaService service) =>
        {
            await service.AnularLoteAsync(gastoId);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarMovimientos);

        return app;
    }
}
```

```csharp
// src/StockApp.Api/Program.cs
// DI: agregar DEBAJO de "builder.Services.AddScoped<IIngresoCajaService, IngresoCajaService>();"
// (línea ~209, cierre del bloque "Finanzas — Fase 2"), ANTES del comentario
// "// Finanzas — Fase 3: adjuntos de gastos/pagos":

builder.Services.AddScoped<IIngresoPorFacturaService, IngresoPorFacturaService>();
```

```csharp
// src/StockApp.Api/Program.cs
// Mapeo de endpoint: agregar DEBAJO de "app.MapMovimientosEndpoints();" (línea ~555):

app.MapIngresoPorFacturaEndpoints();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~IngresoPorFacturaEndpointTests"`
Expected: PASS — 5 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Api/Endpoints/IngresoPorFacturaEndpoints.cs \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Api.Tests/IngresoPorFacturaEndpointTests.cs
git commit -m "feat(api): expone endpoints de ingreso por factura y su anulacion"
```

---

## Task 7: ApiClient

**Files:**
- Create: `src/StockApp.ApiClient/IngresoPorFacturaApiClient.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.ApiClient.Tests/IngresoPorFacturaApiClientTests.cs`

**Interfaces:**
- Consumes: `IIngresoPorFacturaService` (Task 1), `POST /movimientos/ingreso-factura` y `POST /movimientos/ingreso-factura/{gastoId}/anular` (Task 6), `ApiErrores.EnviarAsync/AsegurarExitoAsync`.
- Produces: `IngresoPorFacturaApiClient` — consumido por Task 8/9/10 (ViewModel, vía DI).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.ApiClient.Tests/IngresoPorFacturaApiClientTests.cs
using System.Net;
using System.Net.Http.Json;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class IngresoPorFacturaApiClientTests
{
    private static IngresoPorFacturaDto DtoValido() => new(
        ProveedorId: 3, NumeroFactura: "A-0099", NumeroOrden: null,
        Fecha: new DateTime(2026, 7, 20), Detalle: "Compra de insumos", Destino: null,
        MontoTotal: 500m, FuenteFinanciamientoId: 4, RubroGastoId: 5, LineaPoaId: null,
        CondicionPago: CondicionPago.Contado, FechaVencimiento: null,
        Renglones: new[]
        {
            new RenglonFacturaDto(10, null, 5m, 90m, false),
            new RenglonFacturaDto(null, new ProductoNuevoDto("SKU-N", "Nuevo", null, 1, 50m), 2m, 25m, false),
        });

    [Fact]
    public async Task Registrar_POSTIngresoFactura_SerializaCabeceraYRenglones()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            gastoId = 42, movimientoIds = new[] { 1, 2 }, sumaRenglones = 500.0, diferenciaConTotal = 0.0,
        }, HttpStatusCode.Created));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.RegistrarAsync(DtoValido());

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/movimientos/ingreso-factura", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"proveedorId\":3", fake.UltimoBody);
        Assert.Contains("\"productoId\":10", fake.UltimoBody);
        Assert.Contains("\"codigo\":\"SKU-N\"", fake.UltimoBody);
        Assert.Equal(42, resultado.GastoId);
        Assert.Equal(2, resultado.MovimientoIds.Count);
    }

    [Fact]
    public async Task Registrar_400_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "La factura debe tener al menos un renglón."));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.RegistrarAsync(DtoValido()));
        Assert.Equal("La factura debe tener al menos un renglón.", ex.Message);
    }

    [Fact]
    public async Task Registrar_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe la factura 'A-0099' para ese proveedor."));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.RegistrarAsync(DtoValido()));
    }

    [Fact]
    public async Task AnularLoteAsync_POSTAnular_ConElGastoIdEnLaRuta()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        await client.AnularLoteAsync(42);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/movimientos/ingreso-factura/42/anular", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task AnularLoteAsync_409SinStock_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "No se puede anular: stock insuficiente en 1 producto(s)."));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.AnularLoteAsync(42));
        Assert.Contains("stock insuficiente", ex.Message);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~IngresoPorFacturaApiClientTests"`
Expected: FAIL — `IngresoPorFacturaApiClient` no existe (no compila).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.ApiClient/IngresoPorFacturaApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient;

internal sealed record ProductoNuevoBody(
    string Codigo, string Nombre, int? CategoriaId, int UnidadMedidaId, decimal PrecioVenta);

internal sealed record RenglonFacturaBody(
    int? ProductoId, ProductoNuevoBody? ProductoNuevo,
    decimal Cantidad, decimal PrecioUnitario, bool ActualizarPrecioCosto);

internal sealed record IngresoPorFacturaBody(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    DateTime Fecha, string Detalle, string? Destino, decimal MontoTotal,
    int FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    List<RenglonFacturaBody> Lineas);

/// <summary>IIngresoPorFacturaService contra /movimientos/ingreso-factura.</summary>
public sealed class IngresoPorFacturaApiClient : IIngresoPorFacturaService
{
    private readonly HttpClient _http;

    public IngresoPorFacturaApiClient(HttpClient http) => _http = http;

    public async Task<IngresoPorFacturaResultadoDto> RegistrarAsync(IngresoPorFacturaDto dto)
    {
        var body = new IngresoPorFacturaBody(
            dto.ProveedorId, dto.NumeroFactura, dto.NumeroOrden,
            dto.Fecha, dto.Detalle, dto.Destino, dto.MontoTotal,
            dto.FuenteFinanciamientoId, dto.RubroGastoId, dto.LineaPoaId,
            dto.CondicionPago, dto.FechaVencimiento,
            dto.Renglones.Select(r => new RenglonFacturaBody(
                r.ProductoId,
                r.ProductoNuevo is null ? null : new ProductoNuevoBody(
                    r.ProductoNuevo.Codigo, r.ProductoNuevo.Nombre, r.ProductoNuevo.CategoriaId,
                    r.ProductoNuevo.UnidadMedidaId, r.ProductoNuevo.PrecioVenta),
                r.Cantidad, r.PrecioUnitario, r.ActualizarPrecioCosto)).ToList());

        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("movimientos/ingreso-factura", body));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<IngresoPorFacturaResultadoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al registrar el ingreso por factura.");
    }

    public async Task AnularLoteAsync(int gastoId)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync($"movimientos/ingreso-factura/{gastoId}/anular", content: null));
        await ApiErrores.AsegurarExitoAsync(response);
    }
}
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO de "services.AddTransient<IAdjuntoService, AdjuntoApiClient>();"
// (bloque "Módulo Finanzas — Fase 2: gastos e ingresos de caja"):

services.AddTransient<IIngresoPorFacturaService, IngresoPorFacturaApiClient>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~IngresoPorFacturaApiClientTests"`
Expected: PASS — 5 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.ApiClient/IngresoPorFacturaApiClient.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.ApiClient.Tests/IngresoPorFacturaApiClientTests.cs
git commit -m "feat(apiclient): agrega cliente HTTP de ingreso por factura"
```

---

## Task 8: ViewModel — cabecera, renglones y totales

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Movimientos/FilaRenglonFacturaVm.cs`
- Create: `src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs`

**Interfaces:**
- Consumes: `IIngresoPorFacturaService.RegistrarAsync/AnularLoteAsync` (Task 1), `IProductoService.BuscarAsync` (existente), `IProveedorService.ListarTodosAsync`, `IFuenteFinanciamientoService.ListarActivasAsync`, `IRubroGastoService.ListarActivosAsync`, `ILineaPoaService.ListarActivasAsync` (existentes, mismo patrón que `GastoFormViewModel`).
- Produces: `IngresoPorFacturaViewModel.Renglones/SumaRenglones/DiferenciaConTotal/GuardarCommand/AgregarRenglonCommand/QuitarRenglonCommand`, `FilaRenglonFacturaVm` — consumidos por Task 9 (alta en línea + confirmación de precios) y Task 10 (vista).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;
using ICategoriaProveedorService = StockApp.Application.Catalogo.IProveedorService;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

public class IngresoPorFacturaViewModelTests
{
    private static (IngresoPorFacturaViewModel vm,
                    Mock<IIngresoPorFacturaService> svcMock,
                    Mock<INavigationService> navMock)
        Crear()
    {
        var svc = new Mock<IIngresoPorFacturaService>();
        var productos = new Mock<IProductoService>();
        productos.Setup(p => p.BuscarAsync(null, null, null)).ReturnsAsync(new List<ProductoDto>
        {
            new(1, "SKU1", null, "Producto Uno", null, null, null, null, 1, "Unidad", 10m, 20m, 5m, 0m, true, DateTime.UtcNow),
        });
        var categorias = new Mock<ICategoriaService>();
        categorias.Setup(c => c.ListarActivasAsync()).ReturnsAsync(new List<Categoria>());
        var unidades = new Mock<IUnidadMedidaService>();
        unidades.Setup(u => u.ListarActivasAsync()).ReturnsAsync(new List<UnidadMedida>
        {
            new() { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true },
        });
        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Proveedor Uno", Activo = true },
        });
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "Fuente Uno", Activo = true },
        });
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>
        {
            new() { Id = 1, Codigo = 1, Nombre = "Rubro Uno", Activo = true },
        });
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>());

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var adjuntosPanel = new AdjuntosPanelViewModel(
            new Mock<IAdjuntoService>().Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            new Mock<IAuthorizationService>().Object,
            new Mock<ICurrentSession>().Object);

        var vm = new IngresoPorFacturaViewModel(
            svc.Object, productos.Object, categorias.Object, unidades.Object,
            proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, adjuntosPanel);

        return (vm, svc, nav);
    }

    private static async Task InicializarYCompletarCabeceraAsync(IngresoPorFacturaViewModel vm)
    {
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Compra de insumos";
        vm.MontoTotalTexto = "1.000,00";
    }

    [Fact]
    public async Task AgregarYQuitarRenglones_RecalculaLaSuma()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 3m;
        vm.Renglones[0].PrecioUnitario = 50m;

        Assert.Equal(150m, vm.SumaRenglones);

        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[1].Cantidad = 2m;
        vm.Renglones[1].PrecioUnitario = 25m;

        Assert.Equal(200m, vm.SumaRenglones);   // 150 + 50

        vm.QuitarRenglonCommand.Execute(vm.Renglones[0]);

        Assert.Equal(50m, vm.SumaRenglones);
    }

    [Fact]
    public async Task CambiarMontoTotalTexto_ActualizaLaDiferencia()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Cantidad = 4m;
        vm.Renglones[0].PrecioUnitario = 100m;   // subtotal 400

        vm.MontoTotalTexto = "450,00";

        Assert.Equal(50m, vm.DiferenciaConTotal);

        vm.MontoTotalTexto = "400,00";

        Assert.Equal(0m, vm.DiferenciaConTotal);
    }

    [Fact]
    public async Task PuedeGuardar_SinRenglones_EsFalse()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        Assert.False(vm.GuardarCommand.CanExecute(null));

        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 10m;

        Assert.True(vm.GuardarCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~IngresoPorFacturaViewModelTests"`
Expected: FAIL — `IngresoPorFacturaViewModel` y `FilaRenglonFacturaVm` no existen (no compila).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Presentation/ViewModels/Movimientos/FilaRenglonFacturaVm.cs
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Catalogo;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>Fila editable de la grilla de renglones de la factura (Task 8). El alta en línea de
/// producto nuevo (Task 9) usa los campos ProductoNuevo*; en ese caso Producto queda null.</summary>
public partial class FilaRenglonFacturaVm : ObservableObject
{
    [ObservableProperty] private ProductoDto? _producto;
    [ObservableProperty] private bool _esProductoNuevo;
    [ObservableProperty] private string? _productoNuevoCodigo;
    [ObservableProperty] private string? _productoNuevoNombre;
    [ObservableProperty] private int? _productoNuevoCategoriaId;
    [ObservableProperty] private int _productoNuevoUnidadMedidaId;
    [ObservableProperty] private decimal _productoNuevoPrecioVenta;
    [ObservableProperty] private decimal _cantidad;
    [ObservableProperty] private decimal _precioUnitario;
    [ObservableProperty] private bool _actualizarPrecioCosto;

    public decimal Subtotal => Cantidad * PrecioUnitario;

    partial void OnCantidadChanged(decimal value) => OnPropertyChanged(nameof(Subtotal));
    partial void OnPrecioUnitarioChanged(decimal value) => OnPropertyChanged(nameof(Subtotal));

    /// <summary>Nombre a mostrar en la grilla, exista o no todavía el producto (alta en línea).</summary>
    public string NombreMostrado => EsProductoNuevo
        ? (ProductoNuevoNombre ?? "(producto nuevo)")
        : (Producto?.Nombre ?? string.Empty);
}
```

```csharp
// src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// Cabecera de factura + grilla editable de renglones en una sola pantalla (spec "Ingreso de
/// stock por factura"). Task 8 cubre cabecera/renglones/totales; Task 9 agrega el alta en línea
/// de producto nuevo y la confirmación de cambios de precio; Task 10 la vista y el adjunto.
/// </summary>
public partial class IngresoPorFacturaViewModel : ViewModelBase
{
    private readonly IIngresoPorFacturaService    _service;
    private readonly IProductoService             _productoService;
    private readonly ICategoriaService            _categoriaService;
    private readonly IUnidadMedidaService         _unidadMedidaService;
    private readonly IProveedorService            _proveedorService;
    private readonly IFuenteFinanciamientoService _fuenteService;
    private readonly IRubroGastoService           _rubroService;
    private readonly ILineaPoaService             _lineaService;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;
    private readonly AdjuntosPanelViewModel       _adjuntosPanel;

    public AdjuntosPanelViewModel AdjuntosPanel => _adjuntosPanel;

    private static readonly IFormatProvider CulturaMonto = CrearCulturaMonto();

    private static IFormatProvider CrearCulturaMonto()
    {
        try { return CultureInfo.GetCultureInfo("es-UY"); }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private Proveedor? _proveedorSeleccionado;

    [ObservableProperty] private string? _numeroFactura;
    [ObservableProperty] private string? _numeroOrden;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _detalle = string.Empty;

    [ObservableProperty] private string? _destino;
    [ObservableProperty] private DateTime? _fechaSeleccionada = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _montoTotalTexto = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private RubroGasto? _rubroSeleccionado;

    [ObservableProperty] private LineaPoa? _lineaPoaSeleccionada;
    [ObservableProperty] private bool _esCredito;
    [ObservableProperty] private DateTime? _fechaVencimientoSeleccionada;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _guardadoExitoso;

    [ObservableProperty] private decimal _sumaRenglones;
    [ObservableProperty] private decimal _diferenciaConTotal;
    [ObservableProperty] private int? _gastoIdCreado;

    public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();
    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
    public ObservableCollection<LineaPoa> LineasPoaDisponibles { get; } = new();
    public ObservableCollection<ProductoDto> ProductosDisponibles { get; } = new();
    public ObservableCollection<Categoria> CategoriasDisponibles { get; } = new();
    public ObservableCollection<UnidadMedida> UnidadesMedidaDisponibles { get; } = new();
    public ObservableCollection<FilaRenglonFacturaVm> Renglones { get; } = new();

    public IngresoPorFacturaViewModel(
        IIngresoPorFacturaService service,
        IProductoService productoService,
        ICategoriaService categoriaService,
        IUnidadMedidaService unidadMedidaService,
        IProveedorService proveedorService,
        IFuenteFinanciamientoService fuenteService,
        IRubroGastoService rubroService,
        ILineaPoaService lineaService,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        AdjuntosPanelViewModel adjuntosPanel)
    {
        _service             = service;
        _productoService     = productoService;
        _categoriaService    = categoriaService;
        _unidadMedidaService = unidadMedidaService;
        _proveedorService    = proveedorService;
        _fuenteService       = fuenteService;
        _rubroService        = rubroService;
        _lineaService        = lineaService;
        _navigation          = navigation;
        _confirmacion        = confirmacion;
        _adjuntosPanel       = adjuntosPanel;
    }

    /// <summary>Carga los combos. La dispara la View (DataContextChanged) — sin preselección
    /// (decisión 8 del spec): fuente y rubro arrancan sin seleccionar.</summary>
    public async Task InicializarAsync()
    {
        var proveedores = await _proveedorService.ListarTodosAsync();
        ProveedoresDisponibles.Clear();
        foreach (var p in proveedores.Where(p => p.Activo)) ProveedoresDisponibles.Add(p);

        var fuentes = await _fuenteService.ListarActivasAsync();
        FuentesDisponibles.Clear();
        foreach (var f in fuentes) FuentesDisponibles.Add(f);

        var rubros = await _rubroService.ListarActivosAsync();
        RubrosDisponibles.Clear();
        foreach (var r in rubros) RubrosDisponibles.Add(r);

        var lineas = await _lineaService.ListarActivasAsync();
        LineasPoaDisponibles.Clear();
        foreach (var l in lineas) LineasPoaDisponibles.Add(l);

        var productos = await _productoService.BuscarAsync(null, null, null);
        ProductosDisponibles.Clear();
        foreach (var p in productos.Where(p => p.Activo)) ProductosDisponibles.Add(p);

        var categorias = await _categoriaService.ListarActivasAsync();
        CategoriasDisponibles.Clear();
        foreach (var c in categorias) CategoriasDisponibles.Add(c);

        var unidades = await _unidadMedidaService.ListarActivasAsync();
        UnidadesMedidaDisponibles.Clear();
        foreach (var u in unidades) UnidadesMedidaDisponibles.Add(u);
    }

    private void RecalcularTotales()
    {
        SumaRenglones = Renglones.Sum(r => r.Cantidad * r.PrecioUnitario);
        decimal.TryParse(MontoTotalTexto, NumberStyles.Number, CulturaMonto, out var monto);
        DiferenciaConTotal = monto - SumaRenglones;
    }

    partial void OnMontoTotalTextoChanged(string value) => RecalcularTotales();

    private void Renglon_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilaRenglonFacturaVm.Cantidad) or nameof(FilaRenglonFacturaVm.PrecioUnitario))
            RecalcularTotales();
    }

    [RelayCommand]
    private void AgregarRenglon()
    {
        var fila = new FilaRenglonFacturaVm { Cantidad = 1m };
        fila.PropertyChanged += Renglon_PropertyChanged;
        Renglones.Add(fila);
        RecalcularTotales();
        GuardarCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void QuitarRenglon(FilaRenglonFacturaVm fila)
    {
        fila.PropertyChanged -= Renglon_PropertyChanged;
        Renglones.Remove(fila);
        RecalcularTotales();
        GuardarCommand.NotifyCanExecuteChanged();
    }

    private bool PuedeGuardar()
        => !GuardadoExitoso
           && Renglones.Count > 0
           && ProveedorSeleccionado is not null
           && FuenteSeleccionada is not null
           && RubroSeleccionado is not null
           && !string.IsNullOrWhiteSpace(Detalle)
           && !string.IsNullOrWhiteSpace(MontoTotalTexto);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;

        if (!decimal.TryParse(MontoTotalTexto, NumberStyles.Number, CulturaMonto, out var monto))
        {
            MensajeError = "El monto total no es un número válido.";
            return;
        }

        await GuardarInternoAsync(monto);
    }

    private async Task GuardarInternoAsync(decimal monto)
    {
        var dto = new IngresoPorFacturaDto(
            ProveedorSeleccionado!.Id, NumeroFactura, NumeroOrden,
            FechaSeleccionada is null ? DateTime.UtcNow : DateTime.SpecifyKind(FechaSeleccionada.Value.Date, DateTimeKind.Utc),
            Detalle, Destino, monto,
            FuenteSeleccionada!.Id, RubroSeleccionado!.Id, LineaPoaSeleccionada?.Id,
            EsCredito ? CondicionPago.Credito : CondicionPago.Contado,
            EsCredito && FechaVencimientoSeleccionada is not null
                ? DateTime.SpecifyKind(FechaVencimientoSeleccionada.Value.Date, DateTimeKind.Utc)
                : null,
            Renglones.Select(ARenglonDto).ToList());

        try
        {
            var resultado = await _service.RegistrarAsync(dto);
            GastoIdCreado = resultado.GastoId;
            GuardadoExitoso = true;
            await _adjuntosPanel.InicializarAsync(resultado.GastoId, null);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    private static RenglonFacturaDto ARenglonDto(FilaRenglonFacturaVm fila) => new(
        fila.EsProductoNuevo ? null : fila.Producto?.Id,
        fila.EsProductoNuevo ? new ProductoNuevoDto(
            fila.ProductoNuevoCodigo!, fila.ProductoNuevoNombre!, fila.ProductoNuevoCategoriaId,
            fila.ProductoNuevoUnidadMedidaId, fila.ProductoNuevoPrecioVenta) : null,
        fila.Cantidad, fila.PrecioUnitario, fila.ActualizarPrecioCosto);

    [RelayCommand]
    private void Finalizar() => _navigation.Navegar<StockApp.Presentation.ViewModels.Finanzas.GastosViewModel>();

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<StockApp.Presentation.ViewModels.Finanzas.GastosViewModel>();
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~IngresoPorFacturaViewModelTests"`
Expected: PASS — 3 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Movimientos/FilaRenglonFacturaVm.cs \
        src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs
git commit -m "feat(presentation): VM de ingreso por factura con cabecera renglones y totales"
```

---

## Task 9: ViewModel — alta de producto en línea y confirmación de precios

El alta en línea de producto NO llama a `IProductoService.AltaAsync`: el producto solo se crea de verdad cuando se guarda TODA la factura (Task 1/3, en la misma transacción). Acá solo se capturan los datos en la fila — así un fallo posterior del guardado no deja un producto huérfano sin movimiento asociado.

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Movimientos/ItemConfirmacionPrecioVm.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs`

**Interfaces:**
- Consumes: `FilaRenglonFacturaVm.EsProductoNuevo/ProductoNuevoCodigo/...` (Task 8), `ProductoDto.PrecioCosto` (existente).
- Produces: `AbrirAltaProductoCommand/ConfirmarAltaProductoCommand/CancelarAltaProductoCommand`, `MostrandoAltaProducto`, `CambiosDePrecio`, `MostrandoConfirmacionPrecios`, `ConfirmarPreciosYGuardarCommand` — consumidos por Task 10 (vista).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs`:

```csharp
    [Fact]
    public async Task AltaEnLinea_NoDescartaLosRenglonesYaCargados()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 2m;
        vm.Renglones[0].PrecioUnitario = 30m;

        vm.AgregarRenglonCommand.Execute(null);
        var filaNueva = vm.Renglones[1];

        vm.AbrirAltaProductoCommand.Execute(filaNueva);
        vm.NuevoProductoCodigo = "SKU-INLINE";
        vm.NuevoProductoNombre = "Producto cargado en línea";
        vm.NuevaUnidadSeleccionada = vm.UnidadesMedidaDisponibles[0];
        vm.NuevoProductoPrecioVenta = 40m;
        vm.ConfirmarAltaProductoCommand.Execute(null);

        Assert.Equal(2, vm.Renglones.Count);
        Assert.False(vm.MostrandoAltaProducto);
        // el renglón cargado antes queda intacto
        Assert.Equal(2m, vm.Renglones[0].Cantidad);
        Assert.Equal(30m, vm.Renglones[0].PrecioUnitario);
        // el renglón editado queda marcado como producto nuevo
        Assert.True(filaNueva.EsProductoNuevo);
        Assert.Equal("SKU-INLINE", filaNueva.ProductoNuevoCodigo);
        Assert.Equal("Producto cargado en línea", filaNueva.ProductoNuevoNombre);
    }

    [Fact]
    public async Task Guardar_ListaSoloLosProductosCuyoPrecioCostoDifiere()
    {
        var (vm, svc, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        // Renglon 1: mismo precio que PrecioCosto del producto (10m) → NO entra en la confirmación.
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 10m;

        // Renglon 2: precio distinto (15m != 10m) → SÍ entra en la confirmación.
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[1].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[1].Cantidad = 1m;
        vm.Renglones[1].PrecioUnitario = 15m;

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.MostrandoConfirmacionPrecios);
        var cambio = Assert.Single(vm.CambiosDePrecio);
        Assert.Equal(10m, cambio.PrecioActual);
        Assert.Equal(15m, cambio.PrecioNuevo);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmarPreciosYGuardar_SoloAplicaLosTildados()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1 }, 15m, 0m));
        await InicializarYCompletarCabeceraAsync(vm);

        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 15m;

        await vm.GuardarCommand.ExecuteAsync(null);
        vm.CambiosDePrecio[0].Confirmado = true;

        await vm.ConfirmarPreciosYGuardarCommand.ExecuteAsync(null);

        Assert.True(vm.Renglones[0].ActualizarPrecioCosto);
        svc.Verify(s => s.RegistrarAsync(It.Is<IngresoPorFacturaDto>(
            d => d.Renglones.Single().ActualizarPrecioCosto)), Times.Once);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~IngresoPorFacturaViewModelTests"`
Expected: FAIL — no compila (`AbrirAltaProductoCommand`, `NuevoProductoCodigo`, `CambiosDePrecio`, `ConfirmarPreciosYGuardarCommand`, `ItemConfirmacionPrecioVm` no existen).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Presentation/ViewModels/Movimientos/ItemConfirmacionPrecioVm.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>Fila de la lista de confirmación de precio de costo (Task 9). Solo aparecen acá los
/// productos existentes cuyo PrecioCosto difiere del PrecioUnitario cargado en el renglón.</summary>
public partial class ItemConfirmacionPrecioVm : ObservableObject
{
    public required FilaRenglonFacturaVm Fila { get; init; }
    public required string ProductoNombre { get; init; }
    public required decimal PrecioActual { get; init; }
    public required decimal PrecioNuevo { get; init; }

    [ObservableProperty] private bool _confirmado;
}
```

```csharp
// src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs
// Agregar campos DEBAJO de "public ObservableCollection<FilaRenglonFacturaVm> Renglones { get; } = new();":

    private FilaRenglonFacturaVm? _filaEnAltaProducto;

    [ObservableProperty] private bool _mostrandoAltaProducto;
    [ObservableProperty] private string? _nuevoProductoCodigo;
    [ObservableProperty] private string? _nuevoProductoNombre;
    [ObservableProperty] private Categoria? _nuevaCategoriaSeleccionada;
    [ObservableProperty] private UnidadMedida? _nuevaUnidadSeleccionada;
    [ObservableProperty] private decimal _nuevoProductoPrecioVenta;

    [ObservableProperty] private bool _mostrandoConfirmacionPrecios;
    private decimal _montoConfirmadoPendiente;

    public ObservableCollection<ItemConfirmacionPrecioVm> CambiosDePrecio { get; } = new();
```

Agregar los comandos del alta en línea, junto a `QuitarRenglon`:

```csharp
    [RelayCommand]
    private void AbrirAltaProducto(FilaRenglonFacturaVm fila)
    {
        _filaEnAltaProducto = fila;
        NuevoProductoCodigo = null;
        NuevoProductoNombre = null;
        NuevaCategoriaSeleccionada = null;
        NuevaUnidadSeleccionada = null;
        NuevoProductoPrecioVenta = 0m;
        MostrandoAltaProducto = true;
    }

    [RelayCommand]
    private void ConfirmarAltaProducto()
    {
        if (_filaEnAltaProducto is null || string.IsNullOrWhiteSpace(NuevoProductoCodigo)
            || string.IsNullOrWhiteSpace(NuevoProductoNombre) || NuevaUnidadSeleccionada is null)
            return;

        _filaEnAltaProducto.EsProductoNuevo = true;
        _filaEnAltaProducto.Producto = null;
        _filaEnAltaProducto.ProductoNuevoCodigo = NuevoProductoCodigo;
        _filaEnAltaProducto.ProductoNuevoNombre = NuevoProductoNombre;
        _filaEnAltaProducto.ProductoNuevoCategoriaId = NuevaCategoriaSeleccionada?.Id;
        _filaEnAltaProducto.ProductoNuevoUnidadMedidaId = NuevaUnidadSeleccionada.Id;
        _filaEnAltaProducto.ProductoNuevoPrecioVenta = NuevoProductoPrecioVenta;

        MostrandoAltaProducto = false;
        _filaEnAltaProducto = null;
    }

    [RelayCommand]
    private void CancelarAltaProducto()
    {
        MostrandoAltaProducto = false;
        _filaEnAltaProducto = null;
    }
```

Reemplazar `GuardarAsync` para intercalar el gate de confirmación de precios ANTES de llamar al servicio:

```csharp
    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;

        if (!decimal.TryParse(MontoTotalTexto, NumberStyles.Number, CulturaMonto, out var monto))
        {
            MensajeError = "El monto total no es un número válido.";
            return;
        }

        CambiosDePrecio.Clear();
        foreach (var fila in Renglones)
        {
            if (!fila.EsProductoNuevo && fila.Producto is not null && fila.Producto.PrecioCosto != fila.PrecioUnitario)
                CambiosDePrecio.Add(new ItemConfirmacionPrecioVm
                {
                    Fila           = fila,
                    ProductoNombre = fila.Producto.Nombre,
                    PrecioActual   = fila.Producto.PrecioCosto,
                    PrecioNuevo    = fila.PrecioUnitario,
                });
        }

        if (CambiosDePrecio.Count > 0)
        {
            _montoConfirmadoPendiente = monto;
            MostrandoConfirmacionPrecios = true;
            return;
        }

        await GuardarInternoAsync(monto);
    }

    [RelayCommand]
    private async Task ConfirmarPreciosYGuardarAsync()
    {
        foreach (var item in CambiosDePrecio)
            item.Fila.ActualizarPrecioCosto = item.Confirmado;

        MostrandoConfirmacionPrecios = false;
        await GuardarInternoAsync(_montoConfirmadoPendiente);
    }

    [RelayCommand]
    private void CancelarConfirmacionPrecios() => MostrandoConfirmacionPrecios = false;
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~IngresoPorFacturaViewModelTests"`
Expected: PASS — 6 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs \
        src/StockApp.Presentation/ViewModels/Movimientos/ItemConfirmacionPrecioVm.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs
git commit -m "feat(presentation): alta de producto en linea y confirmacion de precio de costo"
```

---

## Task 10: Vista AXAML, navegación y adjunto del PDF

El PDF se sube con el endpoint YA EXISTENTE `POST /finanzas/gastos/{id}/adjuntos` (Task 7 de F3 Adjuntos, `AdjuntoApiClient.AgregarAGastoAsync`) — no se crea ningún endpoint combinado. La subida es posible recién DESPUÉS del alta exitosa: `AdjuntosPanel` solo queda usable cuando `GuardadoExitoso = true` (Task 9's `GuardarInternoAsync` ya llama `_adjuntosPanel.InicializarAsync(resultado.GastoId, null)`).

**Files:**
- Create: `src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml`
- Create: `src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml.cs`
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml`
- Modify: `src/StockApp.Presentation/App.axaml.cs`

**Interfaces:**
- Consumes: `IngresoPorFacturaViewModel` (Task 8/9), `AdjuntosPanelViewModel.Items/AgregarCommand/VerCommand/QuitarCommand` (existente), convención `DataContextChanged` (igual que `EntradaRegistroView`).
- Produces: pantalla navegable desde el sidebar, sin consumidores adicionales dentro de este plan.

- [ ] **Step 1: Escribir el test que falla**

No aplica TDD rojo-verde para AXAML puro (sin lógica testeable nueva — el behavior de `DataContextChanged` ya está cubierto conceptualmente por Task 8/9's `InicializarAsync` tests). Se verifica con un smoke test de composición: que `IngresoPorFacturaViewModel` puede resolverse desde el contenedor DI real de `App.axaml.cs` sin excepciones. Agregar a `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs`:

```csharp
    [Fact]
    public void Constructor_ExponeAdjuntosPanel()
    {
        // Smoke test de wiring: confirma que la vista puede bindear vm.AdjuntosPanel.* sin
        // que el VM lo oculte accidentalmente al agregar Task 9's ObservableProperty nuevos.
        var (vm, _, _) = Crear();

        Assert.NotNull(vm.AdjuntosPanel);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~IngresoPorFacturaViewModelTests.Constructor_ExponeAdjuntosPanel"`
Expected: FAIL solo si `AdjuntosPanel` no compila; en este punto del plan ya existe (Task 8) — Expected real: PASS de entrada. Se documenta igual para dejar registrado el smoke test antes de tocar AXAML/DI.

- [ ] **Step 3: Implementación mínima**

```xml
<!-- src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Movimientos"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="900" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Movimientos.IngresoPorFacturaView"
             x:DataType="vm:IngresoPorFacturaViewModel">

    <Grid>
        <ScrollViewer>
        <StackPanel Spacing="16">
            <DockPanel Margin="24" IsEnabled="{Binding !GuardadoExitoso}">

                <TextBlock DockPanel.Dock="Top" Text="Ingreso de stock por factura"
                           Classes="titulo-vista" Margin="0,0,0,16" />

                <Border Classes="card" DockPanel.Dock="Top" Margin="0,0,0,16">
                    <StackPanel Spacing="12">
                        <TextBlock Text="Proveedor" />
                        <ComboBox ItemsSource="{Binding ProveedoresDisponibles}"
                                  SelectedItem="{Binding ProveedorSeleccionado}"
                                  PlaceholderText="Elegí el proveedor" HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>

                        <Grid ColumnDefinitions="*,12,*">
                            <StackPanel Grid.Column="0" Spacing="4">
                                <TextBlock Text="Número de factura (opcional)" />
                                <TextBox Text="{Binding NumeroFactura}" Watermark="Ej.: A-0001234" />
                            </StackPanel>
                            <StackPanel Grid.Column="2" Spacing="4">
                                <TextBlock Text="Orden de compra (opcional)" />
                                <TextBox Text="{Binding NumeroOrden}" Watermark="Ej.: OC-77" />
                            </StackPanel>
                        </Grid>

                        <TextBlock Text="Detalle" />
                        <TextBox Text="{Binding Detalle}" Watermark="Ej.: Materiales para la rambla" />

                        <Grid ColumnDefinitions="*,12,*">
                            <StackPanel Grid.Column="0" Spacing="4">
                                <TextBlock Text="Fecha" />
                                <CalendarDatePicker SelectedDate="{Binding FechaSeleccionada}"
                                                    PlaceholderText="dd/mm/aaaa" SelectedDateFormat="Custom"
                                                    CustomDateFormatString="dd/MM/yyyy"
                                                    beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                            </StackPanel>
                            <StackPanel Grid.Column="2" Spacing="4">
                                <TextBlock Text="Fuente de financiamiento" />
                                <ComboBox ItemsSource="{Binding FuentesDisponibles}"
                                          SelectedItem="{Binding FuenteSeleccionada}"
                                          PlaceholderText="Elegí la fuente" HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </StackPanel>
                        </Grid>

                        <TextBlock Text="Rubro de gasto" />
                        <ComboBox ItemsSource="{Binding RubrosDisponibles}"
                                  SelectedItem="{Binding RubroSeleccionado}"
                                  PlaceholderText="Elegí el rubro" HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </StackPanel>
                </Border>

                <Border Classes="card" DockPanel.Dock="Top" Margin="0,0,0,16">
                    <DockPanel>
                        <Button DockPanel.Dock="Top" Content="+ Agregar renglón"
                                Command="{Binding AgregarRenglonCommand}" Margin="0,0,0,8" />

                        <DataGrid ItemsSource="{Binding Renglones}" AutoGenerateColumns="False"
                                  CanUserSortColumns="False" MinHeight="220">
                            <DataGrid.Columns>
                                <DataGridTemplateColumn Header="Producto" Width="3*">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate x:DataType="vm:FilaRenglonFacturaVm">
                                            <TextBlock Text="{Binding NombreMostrado}" VerticalAlignment="Center" Margin="4" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTextColumn Header="Cantidad"
                                    Binding="{Binding Cantidad, DataType={x:Type vm:FilaRenglonFacturaVm}}" Width="Auto" />
                                <DataGridTextColumn Header="Precio unitario"
                                    Binding="{Binding PrecioUnitario, DataType={x:Type vm:FilaRenglonFacturaVm}}" Width="Auto" />
                                <DataGridTextColumn Header="Subtotal"
                                    Binding="{Binding Subtotal, DataType={x:Type vm:FilaRenglonFacturaVm}}"
                                    IsReadOnly="True" Width="Auto" />
                                <DataGridCheckBoxColumn Header="Actualizar precio costo"
                                    Binding="{Binding ActualizarPrecioCosto, DataType={x:Type vm:FilaRenglonFacturaVm}}" Width="Auto" />
                                <DataGridTemplateColumn Header="" Width="Auto">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate x:DataType="vm:FilaRenglonFacturaVm">
                                            <StackPanel Orientation="Horizontal" Spacing="4">
                                                <Button Content="Producto nuevo"
                                                        Command="{Binding $parent[UserControl].((vm:IngresoPorFacturaViewModel)DataContext).AbrirAltaProductoCommand}"
                                                        CommandParameter="{Binding}" />
                                                <Button Content="Quitar"
                                                        Command="{Binding $parent[UserControl].((vm:IngresoPorFacturaViewModel)DataContext).QuitarRenglonCommand}"
                                                        CommandParameter="{Binding}" />
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>
                    </DockPanel>
                </Border>

                <Border Classes="card" DockPanel.Dock="Top" Margin="0,0,0,16">
                    <Grid ColumnDefinitions="*,*,*">
                        <StackPanel Grid.Column="0" Spacing="4">
                            <TextBlock Text="Suma de renglones" />
                            <TextBlock Text="{Binding SumaRenglones, StringFormat='{}{0:N2}'}" FontWeight="Bold" />
                        </StackPanel>
                        <StackPanel Grid.Column="1" Spacing="4">
                            <TextBlock Text="Total de factura" />
                            <TextBox Text="{Binding MontoTotalTexto}" Watermark="0,00" />
                        </StackPanel>
                        <StackPanel Grid.Column="2" Spacing="4">
                            <TextBlock Text="Diferencia" />
                            <TextBlock Text="{Binding DiferenciaConTotal, StringFormat='{}{0:N2}'}"
                                       FontWeight="Bold" Foreground="OrangeRed" />
                        </StackPanel>
                    </Grid>
                </Border>

                <TextBlock DockPanel.Dock="Top" Text="{Binding MensajeError}" Foreground="Red"
                           IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                           Margin="0,0,0,12" />

                <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="12">
                    <Button Content="Guardar" Command="{Binding GuardarCommand}" Classes="primario" />
                    <Button Content="Cancelar" Command="{Binding CancelarCommand}" />
                </StackPanel>

            </DockPanel>

            <!-- Post-guardado: adjuntar el PDF vía una SEGUNDA llamada (spec, Task 10). El
                 formulario de arriba queda deshabilitado (IsEnabled=!GuardadoExitoso) — la
                 única salida de acá en más es "Finalizar". -->
            <Border Classes="card" Margin="24,0,24,24" IsVisible="{Binding GuardadoExitoso}">
                <StackPanel Spacing="12">
                    <TextBlock Text="Factura guardada. Adjuntá el PDF si corresponde." Classes="titulo-vista" />

                    <ItemsControl ItemsSource="{Binding AdjuntosPanel.Items}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,4">
                                    <TextBlock Grid.Column="0" Text="{Binding NombreArchivo}" VerticalAlignment="Center" />
                                    <Button Grid.Column="1" Content="Ver"
                                            Command="{Binding $parent[UserControl].((vm:IngresoPorFacturaViewModel)DataContext).AdjuntosPanel.VerCommand}"
                                            CommandParameter="{Binding}" />
                                    <Button Grid.Column="2" Content="Quitar"
                                            Command="{Binding $parent[UserControl].((vm:IngresoPorFacturaViewModel)DataContext).AdjuntosPanel.QuitarCommand}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <Button Content="Agregar adjunto (PDF)"
                            Command="{Binding AdjuntosPanel.AgregarCommand}"
                            IsVisible="{Binding AdjuntosPanel.PuedeModificar}" />

                    <Button Content="Finalizar" Command="{Binding FinalizarCommand}" Classes="primario"
                            HorizontalAlignment="Right" />
                </StackPanel>
            </Border>
        </StackPanel>
        </ScrollViewer>

        <!-- Overlay: alta rápida de producto (Task 9) -->
        <Border IsVisible="{Binding MostrandoAltaProducto}" Background="#AA000000">
            <Border Classes="card" MaxWidth="420" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel Spacing="8">
                    <TextBlock Text="Producto nuevo" Classes="titulo-vista" />
                    <TextBlock Text="Código" /><TextBox Text="{Binding NuevoProductoCodigo}" />
                    <TextBlock Text="Nombre" /><TextBox Text="{Binding NuevoProductoNombre}" />
                    <TextBlock Text="Categoría (opcional)" />
                    <ComboBox ItemsSource="{Binding CategoriasDisponibles}" SelectedItem="{Binding NuevaCategoriaSeleccionada}">
                        <ComboBox.ItemTemplate>
                            <DataTemplate><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>
                    <TextBlock Text="Unidad de medida" />
                    <ComboBox ItemsSource="{Binding UnidadesMedidaDisponibles}" SelectedItem="{Binding NuevaUnidadSeleccionada}">
                        <ComboBox.ItemTemplate>
                            <DataTemplate><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>
                    <TextBlock Text="Precio de venta" /><TextBox Text="{Binding NuevoProductoPrecioVenta}" />
                    <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                        <Button Content="Cancelar" Command="{Binding CancelarAltaProductoCommand}" />
                        <Button Content="Confirmar" Command="{Binding ConfirmarAltaProductoCommand}" Classes="primario" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Border>

        <!-- Overlay: confirmación de cambios de precio de costo (Task 9) -->
        <Border IsVisible="{Binding MostrandoConfirmacionPrecios}" Background="#AA000000">
            <Border Classes="card" MaxWidth="520" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel Spacing="8">
                    <TextBlock Text="Cambios de precio de costo detectados" Classes="titulo-vista" />
                    <ItemsControl ItemsSource="{Binding CambiosDePrecio}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:ItemConfirmacionPrecioVm">
                                <Grid ColumnDefinitions="Auto,*,Auto,Auto" Margin="0,4">
                                    <CheckBox Grid.Column="0" IsChecked="{Binding Confirmado}" />
                                    <TextBlock Grid.Column="1" Text="{Binding ProductoNombre}" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="2" Text="{Binding PrecioActual, StringFormat='{}{0:N2}'}" Margin="8,0" />
                                    <TextBlock Grid.Column="3" Text="{Binding PrecioNuevo, StringFormat='{}{0:N2}'}" FontWeight="Bold" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                        <Button Content="Cancelar" Command="{Binding CancelarConfirmacionPreciosCommand}" />
                        <Button Content="Confirmar y guardar" Command="{Binding ConfirmarPreciosYGuardarCommand}" Classes="primario" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Border>

    </Grid>

</UserControl>
```

```csharp
// src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml.cs
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.Views.Movimientos;

public partial class IngresoPorFacturaView : UserControl
{
    public IngresoPorFacturaView()
    {
        InitializeComponent();

        // Mismo patrón que EntradaRegistroView: no hay hook de INavigationService que
        // dispare la carga; se cablea acá.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresoPorFacturaViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
```

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Agregar DEBAJO de NavRegistrarSalida (bloque "Movimientos (Inc 5)"):

    [RelayCommand]
    private void NavIngresoPorFactura()
    {
        SeccionActiva = "IngresoPorFactura";
        _navigation.Navegar<IngresoPorFacturaViewModel>();
    }
```

```xml
<!-- src/StockApp.Presentation/Views/ShellMainView.axaml -->
<!-- Agregar DEBAJO del botón "Registrar Entrada" (línea ~82), ANTES de "Registrar Salida" -->

<Button Command="{Binding NavIngresoPorFacturaCommand}"
        Classes="ghost"
        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=IngresoPorFactura}"
        HorizontalAlignment="Stretch">
    <Grid ColumnDefinitions="Auto,*">
        <i:Icon Grid.Column="0" Value="mdi-receipt-text-plus" Foreground="{DynamicResource SidebarTextoBrush}" />
        <TextBlock Grid.Column="1" Text="Ingreso por factura" VerticalAlignment="Center"
                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
    </Grid>
</Button>
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO de "services.AddTransient<SalidaRegistroViewModel>();"
// (bloque "Inc 5: VMs de movimientos"):

services.AddTransient<IngresoPorFacturaViewModel>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~IngresoPorFacturaViewModelTests"`
Expected: PASS — 7 tests verdes (incluye el smoke test del Step 1).

Run completo de la suite antes de cerrar la entrega (convención del repo — un cambio en un enum compartido o en una lista "Todos" ya rompió tests de otro módulo antes; correr TODO, no solo el filtro de esta pantalla):

Run: `dotnet test StockApp.sln`
Expected: PASS — 0 fallos.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml \
        src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml.cs \
        src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs \
        src/StockApp.Presentation/Views/ShellMainView.axaml \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs
git commit -m "feat(presentation): vista de ingreso por factura, navegacion y adjunto de PDF"
```

---

## Task 11: Unificar la anulación de gastos con movimientos

Cierra el agujero que documenta la decisión 9 del spec: hoy `GastoService.AnularAsync` desvincula
los movimientos del gasto (`DesvincularMovimientosAsync`) pero nunca revierte el stock que esos
movimientos sumaron — el stock queda "fantasma". La decisión 9 exige que la anulación por asiento
inverso aplique a CUALQUIER gasto con movimientos asociados, no solo a los creados por la pantalla
de este plan: también a los vinculados a mano vía `GastoService.AsociarMovimientosAsync` (flujo
"Asociar factura" ya existente en `EntradaRegistroViewModel`). `AnularAsync` se bifurca: sin
movimientos asociados, la baja lógica simple de siempre (sin cambios de comportamiento); con
movimientos asociados, delega en `IMovimientoStockRepository.AnularIngresoPorFacturaAtomicoAsync`
— el mismo método atómico que ya usa `IIngresoPorFacturaService.AnularLoteAsync` (Task 5) — que
revierte el stock con salidas espejo y rechaza con el detalle de faltantes si no hay stock
suficiente, en vez de dejar un saldo fantasma.

**Files:**
- Modify: `src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs`
- Modify: `src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs`
- Modify: `src/StockApp.Application/Finanzas/GastoService.cs`
- Modify: `tests/StockApp.Application.Tests/Finanzas/GastoServiceTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs`
- Modify: `tests/StockApp.Api.Tests/GastosEndpointTests.cs`

**Interfaces:**
- Consumes: `IMovimientoStockRepository.AnularIngresoPorFacturaAtomicoAsync(int, int, string)`, `ResultadoAnulacionIngreso`, `ResultadoAnulacionIngresoEstado`, `ItemFaltanteStock` (ya declarados en Task 5, sin cambios).
- Produces: `IMovimientoStockRepository.ExistenMovimientosDeGastoAsync(int)` — consumido únicamente por `GastoService.AnularAsync`. `GastoService` gana el constructor param `IMovimientoStockRepository movRepo`; no requiere tocar `Program.cs` (`IMovimientoStockRepository` ya está registrado — `Program.cs:179`). Sin consumidores adicionales fuera de este plan.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs` (dentro de la clase `MovimientoStockRepositoryIngresoTests`, antes del cierre — reutiliza `SeedMaestrosAsync`/`NuevoProducto`/`NuevoGasto` ya definidos ahí):

```csharp
    [Fact]
    public async Task ExistenMovimientosDeGastoAsync_SinMovimientos_DevuelveFalse()
    {
        var (_, _, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "EXIST-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var existen = await _repo.ExistenMovimientosDeGastoAsync(gasto.Id);

        Assert.False(existen);
    }

    [Fact]
    public async Task ExistenMovimientosDeGastoAsync_ConUnMovimiento_DevuelveTrue()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var producto = NuevoProducto("EXIST-1", um, stock: 5m);
        Context.Productos.Add(producto);
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "EXIST-FAC-2");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada,
            Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m,
            Fecha = DateTime.UtcNow, GastoId = gasto.Id,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var existen = await _repo.ExistenMovimientosDeGastoAsync(gasto.Id);

        Assert.True(existen);
    }
```

Reemplazar en `tests/StockApp.Application.Tests/Finanzas/GastoServiceTests.cs` el `record Mocks` y el método `Crear` por (agrega el mock de `IMovimientoStockRepository`, con default `false` para no alterar ningún test existente):

```csharp
    private sealed record Mocks(
        GastoService Svc,
        Mock<IGastoRepository> Repo,
        Mock<IProveedorRepository> Proveedores,
        Mock<IFuenteFinanciamientoRepository> Fuentes,
        Mock<IRubroGastoRepository> Rubros,
        Mock<ILineaPoaRepository> LineasPoa,
        Mock<IMovimientoStockRepository> MovRepo,
        Mock<IAuditLogger> Audit);

    private static Mocks Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo       = new Mock<IGastoRepository>();
        var proveedores = new Mock<IProveedorRepository>();
        var fuentes    = new Mock<IFuenteFinanciamientoRepository>();
        var rubros     = new Mock<IRubroGastoRepository>();
        var lineasPoa  = new Mock<ILineaPoaRepository>();
        var movRepo    = new Mock<IMovimientoStockRepository>();
        var session    = new Mock<ICurrentSession>();
        var auth       = new Mock<IAuthSvc>();
        var audit      = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        // Maestros por defecto: existen y están activos (los tests puntuales los pisan)
        proveedores.Setup(p => p.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new Proveedor { Id = id, Nombre = $"Proveedor {id}", Activo = true });
        fuentes.Setup(f => f.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new FuenteFinanciamiento { Id = id, Nombre = $"Fuente {id}", Activo = true });
        rubros.Setup(r => r.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new RubroGasto { Id = id, Codigo = id, Nombre = $"Rubro {id}", Activo = true });

        // Por defecto, sin movimientos asociados: preserva el camino de baja lógica simple para
        // TODOS los tests que no lo pisan explícitamente (Task 11 — decisión 9 del spec).
        movRepo.Setup(r => r.ExistenMovimientosDeGastoAsync(It.IsAny<int>())).ReturnsAsync(false);

        var svc = new GastoService(
            repo.Object, proveedores.Object, fuentes.Object, rubros.Object, lineasPoa.Object,
            movRepo.Object, session.Object, auth.Object, audit.Object);
        return new Mocks(svc, repo, proveedores, fuentes, rubros, lineasPoa, movRepo, audit);
    }
```

Agregar a `tests/StockApp.Application.Tests/Finanzas/GastoServiceTests.cs`, debajo de `AnularAsync_SinPagosActivos_AnulaDesvinculaYAudita` (sección "── Anulación del gasto ──"):

```csharp
    [Fact]
    public async Task AnularAsync_ConMovimientosAsociados_DelegaEnAsientoInversoYNoDesvincula()
    {
        // Decisión 9 del spec: un gasto CON movimientos se anula por el mismo camino atómico que
        // IIngresoPorFacturaService.AnularLoteAsync (Task 5) — nunca por la baja lógica simple,
        // sin importar si el vínculo lo hizo esta pantalla o AsociarMovimientosAsync a mano.
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);
        m.MovRepo.Setup(r => r.ExistenMovimientosDeGastoAsync(1)).ReturnsAsync(true);
        m.MovRepo.Setup(r => r.AnularIngresoPorFacturaAtomicoAsync(1, It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ResultadoAnulacionIngreso(ResultadoAnulacionIngresoEstado.Ok, Array.Empty<ItemFaltanteStock>()));

        await m.Svc.AnularAsync(1);

        m.MovRepo.Verify(r => r.AnularIngresoPorFacturaAtomicoAsync(1, It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        m.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Gasto>()), Times.Never);
        m.Repo.Verify(r => r.DesvincularMovimientosAsync(It.IsAny<int>()), Times.Never);
        // El propio AnularIngresoPorFacturaAtomicoAsync ya audita (AnulacionIngresoPorFactura,
        // Task 5) — auditar acá también sería doble asiento de auditoría para la misma anulación.
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AnulacionGasto, "Gasto", 1, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnularAsync_ConMovimientosYStockInsuficiente_LanzaReglaDeNegocioConElProducto()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);
        m.MovRepo.Setup(r => r.ExistenMovimientosDeGastoAsync(1)).ReturnsAsync(true);
        m.MovRepo.Setup(r => r.AnularIngresoPorFacturaAtomicoAsync(1, It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ResultadoAnulacionIngreso(
                ResultadoAnulacionIngresoEstado.StockInsuficiente,
                new List<ItemFaltanteStock> { new(5, "Cemento", 2m, 10m) }));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AnularAsync(1));

        Assert.Contains("Cemento", ex.Message);
        m.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Gasto>()), Times.Never);
    }
```

Agregar a `tests/StockApp.Api.Tests/GastosEndpointTests.cs`, debajo de `DeleteGasto_ConPagosActivos409_SinPagosAnula`:

```csharp
    [Fact]
    public async Task DeleteGasto_ConMovimientosYStockInsuficiente_Devuelve409()
    {
        // Matriz E2E de la decisión 9: un gasto vinculado a mano (AsociarMovimientosAsync, el
        // flujo "asociar factura" ya existente) también pasa por el asiento inverso al anularse,
        // y si el stock ya se consumió, el DELETE existente ahora devuelve 409 en vez de 200.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        int productoId;
        await using (var ctx = Factory.CrearContexto())
        {
            var producto = new Producto
            {
                Codigo = $"DEL-{Guid.NewGuid():N}", Nombre = "Producto con movimiento",
                UnidadMedida = new UnidadMedida { Nombre = "Unidad", Abreviatura = "u" },
                PrecioCosto = 10m, PrecioVenta = 20m, StockActual = 2m,
                Activo = true, FechaAlta = DateTime.UtcNow,
            };
            ctx.Add(producto);
            await ctx.SaveChangesAsync();
            productoId = producto.Id;

            ctx.MovimientosStock.Add(new MovimientoStock
            {
                ProductoId = productoId, UsuarioId = 1, Tipo = TipoMovimiento.Entrada,
                Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m,
                Fecha = DateTime.UtcNow, GastoId = creado!.Id,
            });
            await ctx.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/finanzas/gastos/{creado!.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True((await verificacion.Gastos.SingleAsync(g => g.Id == creado.Id)).Activo);
        Assert.Equal(2m, (await verificacion.Productos.FindAsync(productoId))!.StockActual);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: FAIL — no compila (`ExistenMovimientosDeGastoAsync` no existe en `IMovimientoStockRepository`).

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~GastoServiceTests"`
Expected: FAIL — no compila (`GastoService` no tiene un constructor que acepte `IMovimientoStockRepository`).

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~GastosEndpointTests"`
Expected: FAIL — `DeleteGasto_ConMovimientosYStockInsuficiente_Devuelve409` recibe 200 (el `AnularAsync` viejo desvincula sin revisar stock).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs
// Agregar a la interfaz, DEBAJO de AnularIngresoPorFacturaAtomicoAsync:

    /// <summary>
    /// True si el gasto tiene al menos un MovimientoStock asociado, sin importar si el vínculo
    /// se hizo por el ingreso por factura (Task 1-5) o a mano vía
    /// GastoService.AsociarMovimientosAsync. GastoService.AnularAsync la usa para decidir entre
    /// la baja lógica simple y la anulación por asiento inverso (decisión 9 del spec).
    /// </summary>
    Task<bool> ExistenMovimientosDeGastoAsync(int gastoId);
```

```csharp
// src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs
// Agregar DEBAJO de AnularIngresoPorFacturaAtomicoAsync:

    /// <inheritdoc/>
    public virtual Task<bool> ExistenMovimientosDeGastoAsync(int gastoId)
        => _ctx.MovimientosStock.AnyAsync(m => m.GastoId == gastoId);
```

```csharp
// src/StockApp.Application/Finanzas/GastoService.cs
// Reemplazar el campo/constructor: agregar el campo DEBAJO de "_audit" y el parámetro
// "IMovimientoStockRepository movRepo" al constructor (entre lineasPoa y session, mismo orden
// que el resto de las inyecciones de repos):

    private readonly IGastoRepository                _repo;
    private readonly IProveedorRepository            _proveedores;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly IRubroGastoRepository           _rubros;
    private readonly ILineaPoaRepository             _lineasPoa;
    private readonly IMovimientoStockRepository      _movRepo;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public GastoService(
        IGastoRepository repo,
        IProveedorRepository proveedores,
        IFuenteFinanciamientoRepository fuentes,
        IRubroGastoRepository rubros,
        ILineaPoaRepository lineasPoa,
        IMovimientoStockRepository movRepo,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo        = repo;
        _proveedores = proveedores;
        _fuentes     = fuentes;
        _rubros      = rubros;
        _lineasPoa   = lineasPoa;
        _movRepo     = movRepo;
        _session     = session;
        _auth        = auth;
        _audit       = audit;
    }
```

```csharp
// src/StockApp.Application/Finanzas/GastoService.cs
// Reemplazar el método AnularAsync completo:

    public async Task AnularAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var gasto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Gasto {id} no encontrado.");

        if (!gasto.Activo)
            throw new ReglaDeNegocioException($"El gasto {id} ya está anulado.");
        if (gasto.Pagos.Any(p => p.Activo))
            throw new ReglaDeNegocioException(
                "No se puede anular un gasto con pagos activos: primero anulá los pagos.");

        if (await _movRepo.ExistenMovimientosDeGastoAsync(id))
        {
            // Decisión 9 del spec: un gasto CON movimientos asociados se anula por asiento
            // inverso — la misma ruta atómica que IIngresoPorFacturaService.AnularLoteAsync
            // (Task 5) — para que el stock se revierta en vez de quedar sumado con el gasto
            // desvinculado (el agujero que tenía DesvincularMovimientosAsync).
            var detalle = $"Anulación de '{gasto.Detalle}' (factura {gasto.NumeroFactura ?? "s/n"}, monto {gasto.MontoTotal})";
            var resultado = await _movRepo.AnularIngresoPorFacturaAtomicoAsync(
                id, _session.UsuarioActual!.Id, detalle);

            if (resultado.Estado == ResultadoAnulacionIngresoEstado.StockInsuficiente)
            {
                var detalleFaltantes = string.Join("; ", resultado.Faltantes.Select(f =>
                    $"{f.ProductoNombre}: stock {f.StockActual}, necesita {f.CantidadNecesaria}"));
                throw new ReglaDeNegocioException(
                    $"No se puede anular: stock insuficiente en {resultado.Faltantes.Count} producto(s). {detalleFaltantes}");
            }

            // AnularIngresoPorFacturaAtomicoAsync ya marcó Gasto.Activo=false y escribió su
            // propio LogAuditoria (AnulacionIngresoPorFactura) dentro de la misma transacción.
            return;
        }

        gasto.Activo = false;
        await _repo.ActualizarAsync(gasto);
        // Los movimientos quedan libres para re-facturar (el gasto anulado no los retiene)
        await _repo.DesvincularMovimientosAsync(id);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AnulacionGasto, "Gasto", id,
            $"Anulación de '{gasto.Detalle}' (factura {gasto.NumeroFactura ?? "s/n"}, monto {gasto.MontoTotal})");
    }
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~MovimientoStockRepositoryIngresoTests"`
Expected: PASS — 12 tests verdes.

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~GastoServiceTests"`
Expected: PASS — 32 tests verdes.

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~GastosEndpointTests"`
Expected: PASS — 19 tests verdes.

Run completo de la suite antes de cerrar la entrega (mismo criterio que Task 10 — un constructor nuevo en un service compartido es exactamente el tipo de cambio que ya rompió tests de otro módulo antes):

Run: `dotnet test StockApp.sln`
Expected: PASS — 0 fallos.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs \
        src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs \
        src/StockApp.Application/Finanzas/GastoService.cs \
        tests/StockApp.Application.Tests/Finanzas/GastoServiceTests.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryIngresoTests.cs \
        tests/StockApp.Api.Tests/GastosEndpointTests.cs
git commit -m "fix(finanzas): unifica la anulacion de gastos con movimientos por asiento inverso"
```

---

## Deuda conocida / fuera de alcance

- El adjunto se sube en una segunda llamada (Task 10): si esa llamada falla, la factura queda creada sin adjunto — mismo comportamiento ya aceptado en el alta de gastos existente (`GastoFormViewModel`), documentado como riesgo en el spec.
