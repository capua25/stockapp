# Fase 0 — DTOs de catálogo + wrapper de valorización — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introducir `ProductoDto` para las lecturas del catálogo de producto y envolver la valorización en `ValorizacionReporteDto`, dejando el contrato de `IProductoService`/`IReporteStockService` listo para cruzar HTTP sin arrastrar navegación EF ni tuplas nombradas.

**Architecture:** Dos cambios de contrato in-process, sin proyectos nuevos. `ProductoService` mapea a mano `Producto` (con `.Include(UnidadMedida).Include(Categoria)`) a `ProductoDto` en los métodos de lectura; los métodos de escritura (`AltaAsync`/`ModificarAsync`) siguen recibiendo la entidad `Producto` sin cambios. `ReporteStockService.ObtenerValorizacionAsync` deja de devolver `(Items, Totales)` y devuelve `ValorizacionReporteDto`. Todo el árbol de consumidores (ViewModels de Presentation) se adapta al nuevo tipo.

**Tech Stack:** .NET 10, C#, xUnit 2.5.3, Moq 4.20.72, Avalonia 12, CommunityToolkit.Mvvm 8.4.1

## Global Constraints
- net10.0; `ImplicitUsings=enable` y `Nullable=enable` en todos los proyectos tocados.
- Conventional commits, SIN `Co-Authored-By`.
- TDD: test que falla → implementación mínima → test verde → commit.
- Mapeo a mano en `ProductoService` (no hay AutoMapper en el repo).
- No crear proyectos nuevos en esta fase; no tocar auth/infraestructura de sesión.
- Los métodos de ESCRITURA del catálogo (`AltaAsync`, `ModificarAsync`, `BajaLogicaAsync`, `CambiarPrecioAsync`) NO cambian de firma: siguen recibiendo/operando sobre la entidad `Producto`.
- `IProductoRepository` NO cambia: sigue devolviendo `Producto` (con navegación). El mapeo a DTO ocurre en la capa `ProductoService`, no en el repo.

## Decisión de alcance (desviación documentada respecto al pedido original)

El pedido original acota Tarea 2 a `ProductoListViewModel`/`ProductoListView.axaml` y Tarea 3 a `ProductoFormViewModel`. Al investigar el código se confirmó que `IProductoService.BuscarAsync`/`BuscarPorTextoAsync` **también son consumidos** por:
- `MovimientoRegistroViewModelBase` (base de `EntradaRegistroViewModel`/`SalidaRegistroViewModel`) — carga el combo de productos del formulario de movimiento.
- `MovimientoHistorialViewModel` — carga el combo de filtro por producto del historial (`OpcionProducto(string Nombre, Producto? Valor)`).
- `ProductoServiceFake` en `tests/StockApp.Presentation.UiTests/MovimientoRegistroFakes.cs` — implementa `IProductoService` a mano (sin Moq) para los tests de UI headless.

Cambiar el tipo de retorno de esos dos métodos de la interfaz rompe la compilación de los tres si no se actualizan. Como el pedido es explícito en que **la interfaz** debe devolver `ProductoDto`, esto es una consecuencia mecánica e inevitable, no scope creep. Se resuelve dentro de la **Tarea 1** (que es la que toca la interfaz), como sub-pasos de "ripple fix" claramente marcados, antes de pasar a la Tarea 2. Ninguna vista XAML de Movimientos necesita cambios: los `ComboBox.ItemTemplate` bindean `{Binding Nombre}` sin `x:DataType` estricto.

---

### Tarea 1: `ProductoDto` + `IProductoService`/`ProductoService` + ripple fix en consumidores

**Files:**
- Create: `src/StockApp.Application/Catalogo/Dtos.cs`
- Modify: `src/StockApp.Application/Catalogo/IProductoService.cs` (13 líneas, firmas en líneas 11-12)
- Modify: `src/StockApp.Application/Catalogo/ProductoService.cs` (209 líneas, `BuscarAsync`/`BuscarPorTextoAsync` en líneas 197-207)
- Test: `tests/StockApp.Application.Tests/Catalogo/ProductoServiceTests.cs` (380 líneas)
- Modify (ripple): `src/StockApp.Presentation/ViewModels/Movimientos/MovimientoRegistroViewModelBase.cs` (127 líneas)
- Test (ripple): `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/MovimientoRegistroViewModelTestsBase.cs` (232 líneas)
- Modify (ripple): `src/StockApp.Presentation/ViewModels/Movimientos/MovimientoHistorialViewModel.cs` (167 líneas)
- Test (ripple): `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/MovimientoHistorialViewModelTests.cs` (334 líneas)
- Modify (ripple): `tests/StockApp.Presentation.UiTests/MovimientoRegistroFakes.cs` (77 líneas)

**Interfaces:**
- Consumes: `StockApp.Domain.Entities.Producto` (repo sigue devolviéndolo), `StockApp.Domain.Entities.Categoria.Nombre`, `StockApp.Domain.Entities.UnidadMedida.Nombre`.
- Produces: `StockApp.Application.Catalogo.ProductoDto` (record posicional). Nueva firma pública:
  ```csharp
  Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre);
  Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto);
  ```

**Steps:**

- [ ] 1.1 Escribir el test que falla: mapeo completo de `Producto` (con navegación cargada) a `ProductoDto`. Agregar al final de `ProductoServiceTests.cs` (antes del cierre de la clase, línea 378), dentro de una nueva región:
  ```csharp
      // ─── Mapeo a ProductoDto (Fase 0 — migración client-server) ──────────────

      [Fact]
      public async Task BuscarAsync_MapeaANombresPlanosDeCategoriaYUnidadMedida()
      {
          var categoria = new Categoria { Id = 3, Nombre = "Almacén", Activo = true };
          var unidad = new UnidadMedida { Id = 1, Nombre = "Kilogramo", Abreviatura = "kg", Activo = true };
          var producto = new Producto
          {
              Id = 1, Codigo = "SKU-001", CodigoBarras = "111", Nombre = "Fideos",
              Descripcion = "Fideos secos", CategoriaId = 3, Categoria = categoria,
              ProveedorId = 9, UnidadMedidaId = 1, UnidadMedida = unidad,
              PrecioCosto = 10m, PrecioVenta = 20m, StockActual = 5m, StockMinimo = 2m,
              Activo = true, FechaAlta = new DateTime(2026, 1, 1)
          };
          var (svc, repo, _, _, _, _) = Crear();
          repo.Setup(r => r.BuscarAsync(null, null, null)).ReturnsAsync(new List<Producto> { producto });

          var resultado = await svc.BuscarAsync(null, null, null);

          var dto = Assert.Single(resultado);
          Assert.Equal(1, dto.Id);
          Assert.Equal("SKU-001", dto.Codigo);
          Assert.Equal("111", dto.CodigoBarras);
          Assert.Equal("Fideos", dto.Nombre);
          Assert.Equal("Fideos secos", dto.Descripcion);
          Assert.Equal(3, dto.CategoriaId);
          Assert.Equal("Almacén", dto.CategoriaNombre);
          Assert.Equal(9, dto.ProveedorId);
          Assert.Equal(1, dto.UnidadMedidaId);
          Assert.Equal("Kilogramo", dto.UnidadMedidaNombre);
          Assert.Equal(10m, dto.PrecioCosto);
          Assert.Equal(20m, dto.PrecioVenta);
          Assert.Equal(5m, dto.StockActual);
          Assert.Equal(2m, dto.StockMinimo);
          Assert.True(dto.Activo);
          Assert.Equal(new DateTime(2026, 1, 1), dto.FechaAlta);
      }

      [Fact]
      public async Task BuscarAsync_ProductoSinCategoriaCargada_CategoriaNombreEsNull()
      {
          var unidad = new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };
          var producto = new Producto
          {
              Id = 2, Codigo = "SKU-002", Nombre = "Arroz", CategoriaId = null, Categoria = null,
              UnidadMedidaId = 1, UnidadMedida = unidad, Activo = true
          };
          var (svc, repo, _, _, _, _) = Crear();
          repo.Setup(r => r.BuscarAsync(null, null, null)).ReturnsAsync(new List<Producto> { producto });

          var resultado = await svc.BuscarAsync(null, null, null);

          Assert.Null(Assert.Single(resultado).CategoriaNombre);
      }

      [Fact]
      public async Task BuscarPorTextoAsync_TambienMapeaAProductoDto()
      {
          var producto = new Producto { Id = 4, Codigo = "SKU-004", Nombre = "Fideos", UnidadMedidaId = 1, Activo = true };
          var (svc, repo, _, _, _, _) = Crear();
          repo.Setup(r => r.BuscarPorTextoAsync("fideos")).ReturnsAsync(new List<Producto> { producto });

          var resultado = await svc.BuscarPorTextoAsync("fideos");

          Assert.Equal("SKU-004", Assert.Single(resultado).Codigo);
      }
  ```

- [ ] 1.2 Correr y ver que falla (no compila: `ProductoDto` no existe todavía).
  ```
  dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ProductoServiceTests"
  ```
  Esperado: FAIL — error de compilación `CS0246: El tipo o el nombre del espacio de nombres 'ProductoDto' no se pudo encontrar`.

- [ ] 1.3 Crear `src/StockApp.Application/Catalogo/Dtos.cs`:
  ```csharp
  namespace StockApp.Application.Catalogo;

  /// <summary>
  /// DTO de lectura de Producto: aplana FK ids + nombres de Categoria/UnidadMedida para
  /// servir tanto al listado como al formulario de edición, sin arrastrar navegación de EF
  /// (Fase 0 de la migración client-server — ver
  /// docs/superpowers/specs/2026-07-07-migracion-client-server-design.md, fricción #1).
  /// ProveedorNombre no se incluye: ProductoRepository.BuscarAsync/BuscarPorTextoAsync no
  /// hacen .Include(Proveedor), así que solo se expone el id.
  /// </summary>
  public record ProductoDto(
      int Id,
      string Codigo,
      string? CodigoBarras,
      string Nombre,
      string? Descripcion,
      int? CategoriaId,
      string? CategoriaNombre,
      int? ProveedorId,
      int UnidadMedidaId,
      string UnidadMedidaNombre,
      decimal PrecioCosto,
      decimal PrecioVenta,
      decimal StockActual,
      decimal StockMinimo,
      bool Activo,
      DateTime FechaAlta);
  ```

- [ ] 1.4 Cambiar `IProductoService.cs` — reemplazar las dos firmas de lectura (líneas 11-12):
  ```csharp
  // antes
      Task<IReadOnlyList<Producto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre);
      Task<IReadOnlyList<Producto>> BuscarPorTextoAsync(string? texto);

  // después
      Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre);
      Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto);
  ```

- [ ] 1.5 Implementar el mapeo en `ProductoService.cs`. Agregar `using System.Linq;` al bloque de usings (línea 1) y reemplazar los métodos `BuscarAsync`/`BuscarPorTextoAsync` (líneas 197-207):
  ```csharp
  // antes
      public async Task<IReadOnlyList<Producto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
      {
          _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);
          return await _repo.BuscarAsync(sku, codigoBarras, nombre);
      }

      public async Task<IReadOnlyList<Producto>> BuscarPorTextoAsync(string? texto)
      {
          _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);
          return await _repo.BuscarPorTextoAsync(texto);
      }

  // después
      public async Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
      {
          _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);
          var productos = await _repo.BuscarAsync(sku, codigoBarras, nombre);
          return productos.Select(AProductoDto).ToList();
      }

      public async Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
      {
          _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);
          var productos = await _repo.BuscarPorTextoAsync(texto);
          return productos.Select(AProductoDto).ToList();
      }

      /// <summary>
      /// Mapeo a mano (no hay AutoMapper en el repo) de la entidad de Domain a ProductoDto.
      /// UnidadMedidaNombre usa "??" porque en tests unitarios la navegación puede no estar
      /// poblada (Producto.UnidadMedida queda null si no se setea explícitamente); en producción
      /// el repo siempre hace .Include(UnidadMedida).
      /// </summary>
      private static ProductoDto AProductoDto(Producto p) => new ProductoDto(
          Id:                 p.Id,
          Codigo:             p.Codigo,
          CodigoBarras:       p.CodigoBarras,
          Nombre:             p.Nombre,
          Descripcion:        p.Descripcion,
          CategoriaId:        p.CategoriaId,
          CategoriaNombre:    p.Categoria?.Nombre,
          ProveedorId:        p.ProveedorId,
          UnidadMedidaId:     p.UnidadMedidaId,
          UnidadMedidaNombre: p.UnidadMedida?.Nombre ?? string.Empty,
          PrecioCosto:        p.PrecioCosto,
          PrecioVenta:        p.PrecioVenta,
          StockActual:        p.StockActual,
          StockMinimo:        p.StockMinimo,
          Activo:             p.Activo,
          FechaAlta:          p.FechaAlta);
  ```

- [ ] 1.6 Correr y ver que pasa (incluye los tests preexistentes de `BuscarAsync`/`BuscarPorTextoAsync`, que siguen en verde sin tocarse porque el mock del repo sigue devolviendo `Producto` y las aserciones leen `.Codigo`/`.Count`/`Assert.Single`, todos válidos sobre `ProductoDto`):
  ```
  dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ProductoServiceTests"
  ```
  Esperado: PASS (todas las tests de la clase, incluidas las 3 nuevas).

- [ ] 1.7 Commit:
  ```
  refactor(catalogo): ProductoService.BuscarAsync/BuscarPorTextoAsync devuelven ProductoDto
  ```

- [ ] 1.8 **Ripple fix 1/3** — `MovimientoRegistroViewModelBase.cs` consume `IProductoService.BuscarAsync` para poblar el combo de productos del formulario de movimiento. Cambiar el tipo de la colección y de la selección (líneas 33 y 51), y quitar el using que queda sin uso:
  ```csharp
  // antes (línea 10)
  using StockApp.Domain.Entities;

  // después: eliminar esa línea (Producto ya no se referencia en este archivo)
  ```
  ```csharp
  // antes (línea 33)
      protected Producto? _productoSeleccionado;

  // después
      protected ProductoDto? _productoSeleccionado;
  ```
  ```csharp
  // antes (línea 51)
      public ObservableCollection<Producto> Productos { get; } = new();

  // después
      public ObservableCollection<ProductoDto> Productos { get; } = new();
  ```
  El resto del archivo (`InicializarAsync`, `RegistrarAsync`) no cambia: `p.Activo`, `ProductoSeleccionado!.Id` existen igual en `ProductoDto`.

- [ ] 1.9 Actualizar `MovimientoRegistroViewModelTestsBase.cs` (clase base heredada por `EntradaRegistroViewModelTests`/`SalidaRegistroViewModelTests`, que corren estos mismos `[Fact]` heredados). Agregar `using StockApp.Application.Catalogo;` (ya está en línea 5) y un factory helper, y reemplazar cada `new Producto {...}`:
  ```csharp
  // agregar como primer método privado de la clase, antes de "Crear" (línea 32)
      private static ProductoDto CrearProductoDto(int id, string nombre, decimal stockActual = 0m)
          => new ProductoDto(
              Id: id, Codigo: $"SKU{id}", CodigoBarras: null, Nombre: nombre, Descripcion: null,
              CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: 1,
              UnidadMedidaNombre: "Unidad", PrecioCosto: 0m, PrecioVenta: 0m, StockActual: stockActual,
              StockMinimo: 0m, Activo: true, FechaAlta: default);
  ```
  ```csharp
  // antes (línea 38, firma del helper Crear)
      Crear(IReadOnlyList<Producto>? productos = null)
  {
      ...
      productoMock
          .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
          .ReturnsAsync(productos ?? new List<Producto>());

  // después
      Crear(IReadOnlyList<ProductoDto>? productos = null)
  {
      ...
      productoMock
          .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
          .ReturnsAsync(productos ?? new List<ProductoDto>());
  ```
  ```csharp
  // línea 78, 87, 97, 198 — antes
      vm.ProductoSeleccionado = new Producto { Id = 1, Nombre = "Azúcar" };
  // después
      vm.ProductoSeleccionado = CrearProductoDto(1, "Azúcar");
  ```
  ```csharp
  // líneas 109, 137, 175 — antes (con StockActual)
      vm.ProductoSeleccionado = new Producto { Id = 1, Nombre = "Azúcar", StockActual = 3m };
  // después
      vm.ProductoSeleccionado = CrearProductoDto(1, "Azúcar", stockActual: 3m);
  ```
  (Línea 109 no setea StockActual: usar `CrearProductoDto(1, "Azúcar")`, sin el parámetro.)
  ```csharp
  // líneas 217-221 (InicializarAsync_CargaProductos) — antes
      var productos = new List<Producto>
      {
          new Producto { Id = 1, Nombre = "Activo",   Activo = true },
          new Producto { Id = 2, Nombre = "Inactivo", Activo = false },
      };
  // después
      var productos = new List<ProductoDto>
      {
          CrearProductoDto(1, "Activo"),
          CrearProductoDto(2, "Inactivo") with { Activo = false },
      };
  ```
  Quitar `using StockApp.Domain.Entities;` (línea 7): ya no queda ningún `Producto` referenciado en el archivo.

- [ ] 1.10 Correr y ver que pasa (ejercita `EntradaRegistroViewModelTests` y `SalidaRegistroViewModelTests`, que heredan de la base):
  ```
  dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~EntradaRegistroViewModelTests|FullyQualifiedName~SalidaRegistroViewModelTests"
  ```
  Esperado: PASS.

- [ ] 1.11 **Ripple fix 2/3** — `MovimientoHistorialViewModel.cs` usa `Producto` en el record `OpcionProducto` para el combo de filtro. Cambiar (línea 10 y línea 26), quitando el using que queda sin uso:
  ```csharp
  // antes (línea 10)
  using StockApp.Domain.Entities;

  // después: eliminar esa línea
  ```
  ```csharp
  // antes (línea 26)
  public sealed record OpcionProducto(string Nombre, Producto? Valor);

  // después
  public sealed record OpcionProducto(string Nombre, ProductoDto? Valor);
  ```
  El resto (`InicializarAsync`, líneas 107-118) no cambia: `productos.Where(p => p.Activo)` y `new OpcionProducto(p.Nombre, p)` compilan igual sobre `ProductoDto`.

- [ ] 1.12 Actualizar `MovimientoHistorialViewModelTests.cs`. Cambiar el helper `CrearProducto` (línea 38-39) y la firma de `Crear` (líneas 41-48):
  ```csharp
  // antes (línea 38-39)
      private static Producto CrearProducto(int id, string nombre = "Producto", bool activo = true)
          => new() { Id = id, Nombre = nombre, Codigo = $"SKU{id}", Activo = activo };

  // después
      private static ProductoDto CrearProducto(int id, string nombre = "Producto", bool activo = true)
          => new ProductoDto(
              Id: id, Codigo: $"SKU{id}", CodigoBarras: null, Nombre: nombre, Descripcion: null,
              CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: 1,
              UnidadMedidaNombre: "Unidad", PrecioCosto: 0m, PrecioVenta: 0m, StockActual: 0m,
              StockMinimo: 0m, Activo: activo, FechaAlta: default);
  ```
  ```csharp
  // antes (líneas 46-60, firma y setup de Crear)
          IReadOnlyList<MovimientoHistorialDto>? items = null,
          IReadOnlyList<Producto>? productos = null)
      {
          ...
          productoSvcMock
              .Setup(s => s.BuscarAsync(null, null, null))
              .ReturnsAsync(productos ?? new List<Producto>());

  // después
          IReadOnlyList<MovimientoHistorialDto>? items = null,
          IReadOnlyList<ProductoDto>? productos = null)
      {
          ...
          productoSvcMock
              .Setup(s => s.BuscarAsync(null, null, null))
              .ReturnsAsync(productos ?? new List<ProductoDto>());
  ```
  Agregar `using StockApp.Application.Catalogo;` (ya está en línea 8) y quitar `using StockApp.Domain.Entities;` (línea 10): sin más referencias a `Producto` en el archivo, todo pasa por `ProductoDto`/`OpcionProducto`.

- [ ] 1.13 Correr y ver que pasa:
  ```
  dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~MovimientoHistorialViewModelTests"
  ```
  Esperado: PASS.

- [ ] 1.14 **Ripple fix 3/3** — `tests/StockApp.Presentation.UiTests/MovimientoRegistroFakes.cs` implementa `IProductoService` a mano (sin Moq, proyecto sin esa referencia). Cambiar las dos firmas de lectura (líneas 46-50):
  ```csharp
  // antes
      public Task<IReadOnlyList<Producto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
          => Task.FromResult<IReadOnlyList<Producto>>(Array.Empty<Producto>());

      public Task<IReadOnlyList<Producto>> BuscarPorTextoAsync(string? texto)
          => Task.FromResult<IReadOnlyList<Producto>>(Array.Empty<Producto>());

  // después
      public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
          => Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());

      public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
          => Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());
  ```
  No tocar `AltaAsync`/`ModificarAsync` (siguen recibiendo `Producto`, así que `using StockApp.Domain.Entities;` de línea 6 se mantiene). `using StockApp.Application.Catalogo;` ya está en línea 4.

- [ ] 1.15 Correr el proyecto completo (chico, sin filtro) y ver que compila y pasa:
  ```
  dotnet test tests/StockApp.Presentation.UiTests
  ```
  Esperado: PASS (0 errores de compilación, los tests de `MovimientoFormControlValidacionTests` en verde).

- [ ] 1.16 Commit del ripple fix:
  ```
  refactor(movimientos): adaptar consumidores de IProductoService a ProductoDto
  ```

---

### Tarea 2: `ProductoListViewModel` + `ProductoListView.axaml`

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs` (175 líneas)
- Modify: `src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml` (147 líneas)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs` (714 líneas — contiene DOS clases: `ProductoListViewModelTests` líneas 17-371 y `ProductoFormViewModelTests` líneas 373-713; esta tarea solo toca la primera)

**Interfaces:**
- Consumes: `IProductoService.BuscarAsync`/`BuscarPorTextoAsync` (ya devuelven `ProductoDto` desde Tarea 1).
- Produces: `ProductoListViewModel.Items : ObservableCollection<ProductoDto>`, `ItemSeleccionado : ProductoDto?`.

**Steps:**

- [ ] 2.1 Escribir el test que falla: helper `CrearProductoDto` + reemplazo de `new Producto {...}` por `ProductoDto`. Agregar el helper como primer método privado de `ProductoListViewModelTests`, antes de `Crear` (línea 22 actual):
  ```csharp
      private static ProductoDto CrearProductoDto(
          int id, string codigo, string nombre, bool activo = true,
          int unidadMedidaId = 1, decimal precioCosto = 0m, decimal precioVenta = 0m)
          => new ProductoDto(
              Id: id, Codigo: codigo, CodigoBarras: null, Nombre: nombre, Descripcion: null,
              CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: unidadMedidaId,
              UnidadMedidaNombre: "Unidad", PrecioCosto: precioCosto, PrecioVenta: precioVenta,
              StockActual: 0m, StockMinimo: 0m, Activo: activo, FechaAlta: default);
  ```
  Cambiar la firma y el body de `Crear` (líneas 21-43):
  ```csharp
  // antes
      private static (ProductoListViewModel vm, Mock<IProductoService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
          Crear(IReadOnlyList<Producto>? productos = null)
      {
          var svcMock = new Mock<IProductoService>();
          svcMock
              .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
              .ReturnsAsync(productos ?? new List<Producto>());
          svcMock
              .Setup(s => s.BuscarPorTextoAsync(It.IsAny<string?>()))
              .ReturnsAsync(productos ?? new List<Producto>());

  // después
      private static (ProductoListViewModel vm, Mock<IProductoService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
          Crear(IReadOnlyList<ProductoDto>? productos = null)
      {
          var svcMock = new Mock<IProductoService>();
          svcMock
              .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
              .ReturnsAsync(productos ?? new List<ProductoDto>());
          svcMock
              .Setup(s => s.BuscarPorTextoAsync(It.IsAny<string?>()))
              .ReturnsAsync(productos ?? new List<ProductoDto>());
  ```
  Reemplazar cada literal `Producto` por su equivalente `CrearProductoDto` en `ProductoListViewModelTests` (todos dentro de líneas 17-371):
  ```csharp
  // línea 50-54 (CargarAsync_LlamaServicioYPopulaItems) y línea 127-131 (ItemsView_EsOrdenable) — antes
      var productos = new List<Producto>
      {
          new() { Id = 1, Codigo = "P001", Nombre = "Producto Uno" },
          new() { Id = 2, Codigo = "P002", Nombre = "Producto Dos" }
      };
  // después
      var productos = new List<ProductoDto>
      {
          CrearProductoDto(1, "P001", "Producto Uno"),
          CrearProductoDto(2, "P002", "Producto Dos")
      };
  ```
  ```csharp
  // línea 144-149 (ItemsView_AlAplicarSortDescription_OrdenaLosItems) — antes
      var desordenados = new List<Producto>
      {
          new() { Id = 1, Codigo = "P003", Nombre = "Zapallo" },
          new() { Id = 2, Codigo = "P001", Nombre = "Aceite" },
          new() { Id = 3, Codigo = "P002", Nombre = "Manteca" }
      };
  // después
      var desordenados = new List<ProductoDto>
      {
          CrearProductoDto(1, "P003", "Zapallo"),
          CrearProductoDto(2, "P001", "Aceite"),
          CrearProductoDto(3, "P002", "Manteca")
      };
  ```
  ```csharp
  // línea 166 y 170-174 (Items_TrasRecarga_SeReflejanEnItemsView) — antes
      var (vm, svcMock, _, _) = Crear(new List<Producto> { new() { Id = 1, Codigo = "P001", Nombre = "Uno" } });
      ...
      var nuevaLista = new List<Producto>
      {
          new() { Id = 10, Codigo = "P010", Nombre = "Diez" },
          new() { Id = 11, Codigo = "P011", Nombre = "Once" },
          new() { Id = 12, Codigo = "P012", Nombre = "Doce" }
      };
  // después
      var (vm, svcMock, _, _) = Crear(new List<ProductoDto> { CrearProductoDto(1, "P001", "Uno") });
      ...
      var nuevaLista = new List<ProductoDto>
      {
          CrearProductoDto(10, "P010", "Diez"),
          CrearProductoDto(11, "P011", "Once"),
          CrearProductoDto(12, "P012", "Doce")
      };
  ```
  ```csharp
  // líneas 191, 205, 219, 236, 271 (BajaCommand_*, Activo=true implícito) — antes
      var producto = new Producto { Id = 5, Codigo = "P005", Nombre = "Prueba", Activo = true };
  // después
      var producto = CrearProductoDto(5, "P005", "Prueba");
  ```
  ```csharp
  // línea 260 (BajaCommand_ItemInactivo_EstaDeshabilitado) — antes
      var producto = new Producto { Id = 5, Codigo = "P005", Nombre = "Prueba", Activo = false };
  // después
      var producto = CrearProductoDto(5, "P005", "Prueba", activo: false);
  ```
  ```csharp
  // líneas 292, 316, 327 (EditarCommand_*, sin Activo) — antes
      var producto = new Producto { Id = 5, Codigo = "P005", Nombre = "Prueba" };
  // después
      var producto = CrearProductoDto(5, "P005", "Prueba");
  ```
  ```csharp
  // línea 305 (EditarCommand_ItemInactivo_EstaDeshabilitado) — antes
      var producto = new Producto { Id = 5, Codigo = "P005", Nombre = "Prueba", Activo = false };
  // después
      var producto = CrearProductoDto(5, "P005", "Prueba", activo: false);
  ```
  ```csharp
  // líneas 342-344 (EditarCommand_ElInicializadorPrecargaElProductoSeleccionado) — antes
      var producto = new Producto
      {
          Id = 5, Codigo = "P005", Nombre = "Prueba", UnidadMedidaId = 1, PrecioCosto = 10, PrecioVenta = 20
      };
  // después
      var producto = CrearProductoDto(5, "P005", "Prueba", unidadMedidaId: 1, precioCosto: 10, precioVenta: 20);
  ```
  Y en el mismo test, `formVm` se sigue construyendo igual (línea 359), pero ahora `inicializadorCapturado!(formVm)` invoca `CargarParaEditar(ProductoDto)` — esto queda resuelto recién al terminar la Tarea 3; por eso el paso 2.2 (build) fallará hasta entonces salvo que la Tarea 3 se ejecute en el mismo ciclo (ver nota en 2.3).

- [ ] 2.2 Correr y ver que falla (no compila: `ProductoListViewModel.Items` sigue siendo `ObservableCollection<Producto>`, no acepta `ProductoDto`):
  ```
  dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ProductoListViewModelTests"
  ```
  Esperado: FAIL — error de compilación (tipos incompatibles en `Crear`/asignaciones a `ItemSeleccionado`).

- [ ] 2.3 Implementar: cambiar `ProductoListViewModel.cs`. Quitar el using que queda sin uso (línea 10) y cambiar los dos tipos (líneas 43 y 45):
  ```csharp
  // antes (línea 10)
  using StockApp.Domain.Entities;

  // después: eliminar esa línea (Producto ya no se referencia en este archivo)
  ```
  ```csharp
  // antes (líneas 40-45)
      [ObservableProperty]
      [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
      [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
      private Producto? _itemSeleccionado;

      public ObservableCollection<Producto> Items { get; } = new();

  // después
      [ObservableProperty]
      [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
      [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
      private ProductoDto? _itemSeleccionado;

      public ObservableCollection<ProductoDto> Items { get; } = new();
  ```
  El resto del archivo no cambia: `CargarAsync`, `EjecutarBusquedaAsync`, `EditarAsync`, `BajaAsync` ya son genéricos sobre el tipo de `Items`/`ItemSeleccionado` (usan `.Id`, `.Nombre`, `.Activo`, todos presentes en `ProductoDto`). NOTA: esta nota importante — `Producto? producto = ItemSeleccionado;` en `EditarAsync` (línea 144) queda como `var producto = ItemSeleccionado;`, sin cambio de texto porque ya usa `var`.

- [ ] 2.4 Actualizar los bindings de `ProductoListView.axaml`. Cambiar el `xmlns` de la línea 4 (ya no se necesita el namespace de Domain.Entities, se agrega el de Catalogo):
  ```xml
  <!-- antes (línea 4) -->
               xmlns:ent="using:StockApp.Domain.Entities"

  <!-- después -->
               xmlns:dto="using:StockApp.Application.Catalogo"
  ```
  Reemplazar las 8 ocurrencias de `x:DataType="ent:Producto"` (líneas 65, 74, 85, 94, 103, 112, 121, 132) por `x:DataType="dto:ProductoDto"`.
  Cambiar el binding y el `SortMemberPath` de la columna Unidad (líneas 110 y 113):
  ```xml
  <!-- antes -->
                          <DataGridTemplateColumn Header="Unidad" Width="Auto" SortMemberPath="UnidadMedida.Nombre">
                              <DataGridTemplateColumn.CellTemplate>
                                  <DataTemplate x:DataType="ent:Producto">
                                      <TextBlock Text="{Binding UnidadMedida.Nombre}"

  <!-- después -->
                          <DataGridTemplateColumn Header="Unidad" Width="Auto" SortMemberPath="UnidadMedidaNombre">
                              <DataGridTemplateColumn.CellTemplate>
                                  <DataTemplate x:DataType="dto:ProductoDto">
                                      <TextBlock Text="{Binding UnidadMedidaNombre}"
  ```
  Cambiar el binding y el `SortMemberPath` de la columna Categoría (líneas 119, 122, 125):
  ```xml
  <!-- antes -->
                          <DataGridTemplateColumn Header="Categoría" Width="*" SortMemberPath="Categoria.Nombre">
                              <DataGridTemplateColumn.CellTemplate>
                                  <DataTemplate x:DataType="ent:Producto">
                                      <TextBlock Text="{Binding Categoria.Nombre, TargetNullValue='—'}"
                                                 Opacity="{Binding Activo, Converter={x:Static conv:ActivoOpacidadConverter.Instance}}"
                                                 TextTrimming="CharacterEllipsis"
                                                 ToolTip.Tip="{Binding Categoria.Nombre, TargetNullValue='—'}"

  <!-- después -->
                          <DataGridTemplateColumn Header="Categoría" Width="*" SortMemberPath="CategoriaNombre">
                              <DataGridTemplateColumn.CellTemplate>
                                  <DataTemplate x:DataType="dto:ProductoDto">
                                      <TextBlock Text="{Binding CategoriaNombre, TargetNullValue='—'}"
                                                 Opacity="{Binding Activo, Converter={x:Static conv:ActivoOpacidadConverter.Instance}}"
                                                 TextTrimming="CharacterEllipsis"
                                                 ToolTip.Tip="{Binding CategoriaNombre, TargetNullValue='—'}"
  ```
  Las demás columnas (Código, Nombre, Costo, Venta, Stock, Estado) solo necesitan el `x:DataType="dto:ProductoDto"` (ya cubierto arriba); sus bindings (`Codigo`, `Nombre`, `PrecioCosto`, `PrecioVenta`, `StockActual`, `Activo`) son iguales en `ProductoDto`.

- [ ] 2.5 Correr y ver que pasa:
  ```
  dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ProductoListViewModelTests"
  ```
  Esperado: PASS.

- [ ] 2.6 Commit:
  ```
  refactor(catalogo): ProductoListViewModel y ProductoListView usan ProductoDto
  ```

---

### Tarea 3: `ProductoFormViewModel.CargarParaEditar` acepta `ProductoDto`

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Catalogo/ProductoFormViewModel.cs` (225 líneas, `CargarParaEditar` en líneas 113-128)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs`, clase `ProductoFormViewModelTests` (líneas 373-713)

**Interfaces:**
- Consumes: nada nuevo (los servicios inyectados no cambian).
- Produces:
  ```csharp
  public void CargarParaEditar(ProductoDto producto)
  ```

**Steps:**

- [ ] 3.1 Escribir el test que falla: agregar un helper `CrearProductoDto` a `ProductoFormViewModelTests` (como primer método privado de la clase, antes de `Crear`, línea ~375 actual) y reemplazar cada `new Producto {...}`/`new Producto{...}` pasado a `CargarParaEditar`:
  ```csharp
      private static ProductoDto CrearProductoDto(
          int id = 9, string codigo = "P009", string nombre = "Aceite",
          string? codigoBarras = null, string? descripcion = null,
          decimal precioCosto = 0m, decimal precioVenta = 0m,
          int unidadMedidaId = 0, int? categoriaId = null, int? proveedorId = null,
          decimal stockMinimo = 0m)
          => new ProductoDto(
              Id: id, Codigo: codigo, CodigoBarras: codigoBarras, Nombre: nombre, Descripcion: descripcion,
              CategoriaId: categoriaId, CategoriaNombre: null, ProveedorId: proveedorId,
              UnidadMedidaId: unidadMedidaId, UnidadMedidaNombre: "", PrecioCosto: precioCosto,
              PrecioVenta: precioVenta, StockActual: 0m, StockMinimo: stockMinimo, Activo: true,
              FechaAlta: default);
  ```
  ```csharp
  // línea 559-568 (CargarParaEditar_SeteaEsEdicionYCamposDelProducto) — antes
      var producto = new Producto
      {
          Id = 9,
          Codigo = "P009",
          Nombre = "Aceite",
          CodigoBarras = "7791234567890",
          Descripcion = "Aceite de girasol",
          PrecioCosto = 100m,
          PrecioVenta = 150m,
      };

      vm.CargarParaEditar(producto);

  // después
      var producto = CrearProductoDto(
          codigoBarras: "7791234567890", descripcion: "Aceite de girasol",
          precioCosto: 100m, precioVenta: 150m);

      vm.CargarParaEditar(producto);
  ```
  ```csharp
  // línea 586 (CargarParaEditar_CambiaTituloAEditarProducto) — antes
      vm.CargarParaEditar(new Producto { Id = 9, Codigo = "P009", Nombre = "Aceite" });
  // después
      vm.CargarParaEditar(CrearProductoDto());
  ```
  ```csharp
  // líneas 600-603 (InicializarAsync_ModoEdicion_PreseleccionaUnidadYCategoriaOriginales) — antes
      vm.CargarParaEditar(new Producto
      {
          Id = 9, Codigo = "P009", Nombre = "Aceite", UnidadMedidaId = kilogramo.Id, CategoriaId = bebidas.Id
      });
  // después
      vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: kilogramo.Id, categoriaId: bebidas.Id));
  ```
  ```csharp
  // líneas 616-619 (InicializarAsync_ModoEdicion_SinCategoriaOriginal_CategoriaSeleccionadaEsNull) — antes
      vm.CargarParaEditar(new Producto
      {
          Id = 9, Codigo = "P009", Nombre = "Aceite", UnidadMedidaId = UnidadPorDefecto.Id, CategoriaId = null
      });
  // después
      vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: UnidadPorDefecto.Id, categoriaId: null));
  ```
  ```csharp
  // líneas 630-633, 667-670, 682-685 (GuardarCommand_ModoEdicion_LlamaModificarAsyncNoAltaAsync,
  // GuardarCommand_ModoEdicion_Exitoso_NavegaAListado, GuardarCommand_ModoEdicion_ServicioLanzaExcepcionDeDominio_MuestraMensajeErrorYNoCrashea) — antes (las 3 idénticas)
      vm.CargarParaEditar(new Producto
      {
          Id = 9, Codigo = "P009", Nombre = "Aceite", UnidadMedidaId = UnidadPorDefecto.Id
      });
  // después (las 3)
      vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: UnidadPorDefecto.Id));
  ```
  ```csharp
  // líneas 646-654 (GuardarCommand_ModoEdicion_PreservaProveedorIdYStockMinimoOriginales) — antes
      vm.CargarParaEditar(new Producto
      {
          Id = 9,
          Codigo = "P009",
          Nombre = "Aceite",
          UnidadMedidaId = UnidadPorDefecto.Id,
          ProveedorId = 3,
          StockMinimo = 12m,
      });
  // después
      vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: UnidadPorDefecto.Id, proveedorId: 3, stockMinimo: 12m));
  ```

- [ ] 3.2 Correr y ver que falla (no compila: `CargarParaEditar(Producto)` no acepta `ProductoDto`):
  ```
  dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ProductoFormViewModelTests"
  ```
  Esperado: FAIL — error de compilación `CS1503` (no se puede convertir de `ProductoDto` a `Producto`).

- [ ] 3.3 Implementar: cambiar la firma de `CargarParaEditar` en `ProductoFormViewModel.cs` (línea 113). El cuerpo (líneas 114-128) NO cambia: todos los campos leídos (`Id`, `Codigo`, `Nombre`, `CodigoBarras`, `Descripcion`, `PrecioCosto`, `PrecioVenta`, `UnidadMedidaId`, `CategoriaId`, `ProveedorId`, `StockMinimo`) existen igual en `ProductoDto`.
  ```csharp
  // antes
      public void CargarParaEditar(Producto producto)
      {

  // después
      public void CargarParaEditar(ProductoDto producto)
      {
  ```
  `using StockApp.Application.Catalogo;` ya está importado (línea 6); `using StockApp.Domain.Entities;` (línea 7) se mantiene porque `Producto`, `UnidadMedida` y `Categoria` se siguen usando en `GuardarAsync` (construcción de la entidad para `AltaAsync`/`ModificarAsync`) y en `UnidadesMedida`/`Categorias`.

- [ ] 3.4 Correr y ver que pasa:
  ```
  dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ProductoFormViewModelTests"
  ```
  Esperado: PASS.

- [ ] 3.5 Correr también `ProductoListViewModelTests` (comparten archivo con la Tarea 2, y el test `EditarCommand_ElInicializadorPrecargaElProductoSeleccionado` invoca `CargarParaEditar` a través del inicializador capturado — queda validado recién ahora):
  ```
  dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ProductoListViewModelTests|FullyQualifiedName~ProductoFormViewModelTests"
  ```
  Esperado: PASS (ambas clases).

- [ ] 3.6 Commit:
  ```
  refactor(catalogo): ProductoFormViewModel.CargarParaEditar acepta ProductoDto
  ```

---

### Tarea 4: `ValorizacionReporteDto` + `ReporteStockService` + `ValorizacionViewModel`

**Files:**
- Modify: `src/StockApp.Application/Reportes/Dtos.cs` (35 líneas)
- Modify: `src/StockApp.Application/Reportes/IReporteStockService.cs` (38 líneas, firma en línea 15)
- Modify: `src/StockApp.Application/Reportes/ReporteStockService.cs` (87 líneas, `ObtenerValorizacionAsync` en líneas 31-44)
- Modify: `src/StockApp.Presentation/ViewModels/Reportes/ValorizacionViewModel.cs` (78 líneas, `BuscarAsync` en líneas 56-62)
- Test: `tests/StockApp.Application.Tests/Reportes/ReporteStockServiceValorizacionTests.cs` (117 líneas)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Reportes/ValorizacionViewModelTests.cs` (118 líneas)

**Interfaces:**
- Consumes: `ValorizacionItemDto`, `ValorizacionTotalesDto` (sin cambios).
- Produces:
  ```csharp
  public record ValorizacionReporteDto(IReadOnlyList<ValorizacionItemDto> Items, ValorizacionTotalesDto Totales);
  Task<ValorizacionReporteDto> ObtenerValorizacionAsync();
  ```

**Steps:**

- [ ] 4.1 Escribir el test que falla: reescribir los 2 tests de `ReporteStockServiceValorizacionTests.cs` que destructuran la tupla, para que usen `.Items`/`.Totales`:
  ```csharp
  // línea 56-68 (ObtenerValorizacionAsync_CalculaValorCostoYValorVenta_Correcto) — antes
      var (resultItems, _) = await svc.ObtenerValorizacionAsync();

      var item = Assert.Single(resultItems);
  // después
      var resultado = await svc.ObtenerValorizacionAsync();

      var item = Assert.Single(resultado.Items);
  ```
  ```csharp
  // línea 70-87 (ObtenerValorizacionAsync_CalculaTotalesCorrectamente) — antes
      var (_, totales) = await svc.ObtenerValorizacionAsync();

      Assert.Equal(350m, totales.TotalValorCosto); // 100 + 200 + 50
      Assert.Equal(575m, totales.TotalValorVenta); // 150 + 350 + 75
  // después
      var resultado = await svc.ObtenerValorizacionAsync();

      Assert.Equal(350m, resultado.Totales.TotalValorCosto); // 100 + 200 + 50
      Assert.Equal(575m, resultado.Totales.TotalValorVenta); // 150 + 350 + 75
  ```
  ```csharp
  // línea 89-101 (ObtenerValorizacionAsync_ProductoSinCategoria_Retorna_SinCategoria) — antes
      var (resultItems, _) = await svc.ObtenerValorizacionAsync();

      var item = Assert.Single(resultItems);
      Assert.Equal("Sin categoría", item.Categoria);
  // después
      var resultado = await svc.ObtenerValorizacionAsync();

      var item = Assert.Single(resultado.Items);
      Assert.Equal("Sin categoría", item.Categoria);
  ```
  El test `ObtenerValorizacionAsync_Operador_LanzaUnauthorizedAccessException` (línea 103-115) no destructura la tupla — no requiere cambios.

- [ ] 4.2 Correr y ver que falla (no compila: `svc.ObtenerValorizacionAsync()` sigue devolviendo la tupla `(Items, Totales)`, no tiene propiedades `.Items`/`.Totales`):
  ```
  dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ReporteStockServiceValorizacionTests"
  ```
  Esperado: FAIL — error de compilación `CS1061` (la tupla no contiene una definición para "Items"/"Totales").

- [ ] 4.3 Crear el DTO envoltorio en `Dtos.cs`. Agregar al final del archivo (después de `MasMovidoDto`, línea 35):
  ```csharp

  /// <summary>
  /// Envoltorio serializable de la valorización: reemplaza la tupla nombrada
  /// (Items, Totales) que no serializa de forma estándar por HTTP (Fase 0 de la
  /// migración client-server — fricción #2 del spec).
  /// </summary>
  public record ValorizacionReporteDto(
      IReadOnlyList<ValorizacionItemDto> Items,
      ValorizacionTotalesDto Totales);
  ```

- [ ] 4.4 Cambiar la firma en `IReporteStockService.cs` (línea 15):
  ```csharp
  // antes
      Task<(IReadOnlyList<ValorizacionItemDto> Items, ValorizacionTotalesDto Totales)> ObtenerValorizacionAsync();

  // después
      Task<ValorizacionReporteDto> ObtenerValorizacionAsync();
  ```

- [ ] 4.5 Implementar en `ReporteStockService.cs` (líneas 31-44):
  ```csharp
  // antes
      public async Task<(IReadOnlyList<ValorizacionItemDto> Items, ValorizacionTotalesDto Totales)>
          ObtenerValorizacionAsync()
      {
          // Autorización fail-closed: PRIMERO, antes de tocar el repo.
          _auth.Verificar(_session.RolActual, Permisos.VerReportes);

          var items = await _repo.ObtenerValorizacionAsync();

          var totales = new ValorizacionTotalesDto(
              TotalValorCosto: items.Sum(i => i.ValorCosto),
              TotalValorVenta: items.Sum(i => i.ValorVenta));

          return (items, totales);
      }

  // después
      public async Task<ValorizacionReporteDto> ObtenerValorizacionAsync()
      {
          // Autorización fail-closed: PRIMERO, antes de tocar el repo.
          _auth.Verificar(_session.RolActual, Permisos.VerReportes);

          var items = await _repo.ObtenerValorizacionAsync();

          var totales = new ValorizacionTotalesDto(
              TotalValorCosto: items.Sum(i => i.ValorCosto),
              TotalValorVenta: items.Sum(i => i.ValorVenta));

          return new ValorizacionReporteDto(items, totales);
      }
  ```

- [ ] 4.6 Correr y ver que pasa:
  ```
  dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ReporteStockServiceValorizacionTests"
  ```
  Esperado: PASS.

- [ ] 4.7 Commit:
  ```
  refactor(reportes): ObtenerValorizacionAsync devuelve ValorizacionReporteDto en vez de tupla
  ```

- [ ] 4.8 Escribir el test que falla en Presentation: actualizar el mock de `ValorizacionViewModelTests.cs` (líneas 42-46) para que devuelva el nuevo DTO en vez de la tupla:
  ```csharp
  // antes
          servicioMock
              .Setup(s => s.ObtenerValorizacionAsync())
              .ReturnsAsync((
                  items ?? new List<ValorizacionItemDto>(),
                  totales ?? new ValorizacionTotalesDto(0m, 0m)));

  // después
          servicioMock
              .Setup(s => s.ObtenerValorizacionAsync())
              .ReturnsAsync(new ValorizacionReporteDto(
                  items ?? new List<ValorizacionItemDto>(),
                  totales ?? new ValorizacionTotalesDto(0m, 0m)));
  ```

- [ ] 4.9 Correr y ver que falla (no compila: `ReturnsAsync` espera `ValorizacionReporteDto`, no una tupla, hasta que se actualice el VM en 4.10 — pero el mock ya compila solo; lo que falla es `ValorizacionViewModel.BuscarAsync` que sigue haciendo `var (items, totales) = await _servicio.ObtenerValorizacionAsync();`, incompatible con el nuevo tipo de retorno):
  ```
  dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ValorizacionViewModelTests"
  ```
  Esperado: FAIL — error de compilación en `ValorizacionViewModel.cs` (no se puede deconstruir `ValorizacionReporteDto` en una tupla de 2 elementos sin un método `Deconstruct` explícito).

- [ ] 4.10 Implementar en `ValorizacionViewModel.cs` (líneas 56-62):
  ```csharp
  // antes
      [RelayCommand]
      private async Task BuscarAsync()
      {
          var (items, totales) = await _servicio.ObtenerValorizacionAsync();
          Items = items;
          Totales = totales;
      }

  // después
      [RelayCommand]
      private async Task BuscarAsync()
      {
          var resultado = await _servicio.ObtenerValorizacionAsync();
          Items = resultado.Items;
          Totales = resultado.Totales;
      }
  ```

- [ ] 4.11 Correr y ver que pasa:
  ```
  dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ValorizacionViewModelTests"
  ```
  Esperado: PASS.

- [ ] 4.12 Commit:
  ```
  refactor(reportes): ValorizacionViewModel consume ValorizacionReporteDto
  ```

---

## Verificación final

- [ ] 5.1 Correr la batería completa de los 3 proyectos de test tocados:
  ```
  dotnet test tests/StockApp.Application.Tests
  dotnet test tests/StockApp.Presentation.Tests
  dotnet test tests/StockApp.Presentation.UiTests
  ```
  Esperado: PASS en los tres, 0 errores, sin regresiones en el resto de la suite (Categoria/Proveedor/UnidadMedida/Movimientos/Auditoría/Actualizaciones no se tocan).

- [ ] 5.2 Build completo de la solución:
  ```
  dotnet build StockApp.sln
  ```
  Esperado: 0 errores, 0 warnings nuevos (además de los preexistentes, si los hubiera).

## Self-Review

**Cobertura vs. alcance:**
- Cambio 1 (ProductoDto en lecturas de catálogo): cubierto en Tareas 1-3, incluido el ripple obligatorio en Movimientos (documentado como desviación explícita, no como scope creep — es consecuencia directa de cambiar la interfaz).
- Cambio 2 (ValorizacionReporteDto): cubierto en Tarea 4.
- Categoria/Proveedor/UnidadMedida: NO tocados, tal como pide el alcance (son planos, sin navegación).
- Métodos de escritura (`AltaAsync`, `ModificarAsync`, `BajaLogicaAsync`, `CambiarPrecioAsync`): NO tocados, siguen recibiendo `Producto`.
- `IProductoRepository`/`ProductoRepository`: NO tocados, siguen devolviendo `Producto` con `.Include(UnidadMedida).Include(Categoria)` (confirmado leyendo el código, sin `.Include(Proveedor)` — de ahí que `ProductoDto` no tenga `ProveedorNombre`).

**Scan de placeholders:** ninguno. Todos los bloques de código en este plan son el código real a escribir, extraído de los archivos leídos con sus rutas y números de línea exactos (pueden desplazarse levemente entre pasos consecutivos dentro de la misma tarea; se referencian las líneas del estado ANTES de cada edición).

**Consistencia de tipos — `ProductoDto` en las 4 tareas:**
- Definido una sola vez en Tarea 1 (`Dtos.cs`), con 16 campos: `Id, Codigo, CodigoBarras, Nombre, Descripcion, CategoriaId, CategoriaNombre, ProveedorId, UnidadMedidaId, UnidadMedidaNombre, PrecioCosto, PrecioVenta, StockActual, StockMinimo, Activo, FechaAlta`.
- Tarea 2 lo consume vía `Items`/`ItemSeleccionado` y bindea `Codigo, Nombre, PrecioCosto, PrecioVenta, StockActual, UnidadMedidaNombre, CategoriaNombre, Activo` — todos existentes.
- Tarea 3 lo consume vía `CargarParaEditar` leyendo `Id, Codigo, Nombre, CodigoBarras, Descripcion, PrecioCosto, PrecioVenta, UnidadMedidaId, CategoriaId, ProveedorId, StockMinimo` — todos existentes.
- Ripple fix (Tarea 1) lo consume vía `Productos`/`ProductoSeleccionado`/`OpcionProducto.Valor` leyendo `Id, Nombre, Activo` — todos existentes.
- Ningún consumidor requiere un campo no declarado en el record.

**Tests de Presentation existentes para los VMs afectados:** SÍ, en un único archivo compartido `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs` — contiene DOS clases: `ProductoListViewModelTests` (líneas 17-371, Tarea 2) y `ProductoFormViewModelTests` (líneas 373-713, Tarea 3). No existen archivos separados `ProductoListViewModelTests.cs`/`ProductoFormViewModelTests.cs`.

**Desviación registrada:** el ripple fix de Tarea 1 (pasos 1.8-1.16) toca 5 archivos fuera de los mencionados explícitamente en el pedido original (`MovimientoRegistroViewModelBase.cs`, `MovimientoRegistroViewModelTestsBase.cs`, `MovimientoHistorialViewModel.cs`, `MovimientoHistorialViewModelTests.cs`, `MovimientoRegistroFakes.cs`). Es inevitable: `IProductoService.BuscarAsync`/`BuscarPorTextoAsync` son un contrato compartido y el pedido es explícito en que deben devolver `ProductoDto`. Sin este fix el build queda roto tras la Tarea 1.
