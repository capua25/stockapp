# Fase 3a — Servidor listo para clientes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cerrar toda la deuda de contrato de la API (D1-D10 del spec) identificada en el review final de Fase 2b, para que el desktop pueda migrar a consumirla en Fase 3b sin romper nada — incluyendo el barrido completo de excepciones de dominio propias en Application y sus consumidores en el desktop.

**Architecture:** Igual que Fase 2b (Minimal APIs + servicios de Application existentes + `DomainExceptionHandler` centralizado). Esta fase agrega dos capas nuevas: (1) excepciones de dominio propias (`EntidadNoEncontradaException`, `ReglaDeNegocioException`) que reemplazan `KeyNotFoundException`/`InvalidOperationException` del BCL en los servicios de Application, cerrando el catch-all genérico del handler; (2) endpoints de bootstrap anónimos (`/auth/primer-arranque`, `/auth/primer-admin`) para que un cliente sin usuarios pueda arrancar. A diferencia de Fase 2b, esta fase SÍ toca `StockApp.Presentation` — es dueña del cambio de excepciones y debe actualizar los `catch` específicos de tipo que dependen de `InvalidOperationException`/`KeyNotFoundException` para no crashear la app de escritorio.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core (Npgsql), JWT Bearer, xUnit + WebApplicationFactory + Testcontainers.PostgreSql (Api.Tests), xUnit + Moq (Application.Tests, Presentation.Tests), xUnit puro (Domain.Tests).

## Global Constraints

- Target framework `net10.0` en todos los proyectos — sin proyectos `.csproj` nuevos.
- Minimal APIs con `MapGroup` + métodos de extensión `MapXxxEndpoints`; **no Controllers**.
- **Esta fase SÍ toca `StockApp.Presentation`** (a diferencia de Fase 2b): es dueña del cambio de excepciones de dominio (D4) y del cambio de firma de `AltaUsuarioAsync` (D2). Cada servicio de Application que cambia sus excepciones actualiza EN EL MISMO TASK los `catch` de sus ViewModels y los tests de Presentation que dependen del tipo exacto.
- Reusar `IXxxService`/repositorios/DTOs existentes tal cual — las únicas excepciones de firma son las explícitamente listadas en el spec (D2: `AltaUsuarioAsync` → `Task<int>`).
- El mensaje de cada excepción de dominio nueva preserva EXACTAMENTE el texto de la excepción del BCL que reemplaza — los ViewModels que muestran `ex.Message` al usuario no cambian de comportamiento visible.
- Manejo de errores centralizado en `DomainExceptionHandler` (`IExceptionHandler`) — los endpoints no hacen try/catch propio.
- Tests de integración contra **Postgres real vía Testcontainers** (`ApiFactory` + `ApiTestBase` + `TRUNCATE ... RESTART IDENTITY CASCADE` entre tests, patrón ya establecido en Fase 2a/2b).
- Tests de Application con **Moq**, mismo patrón que `CategoriaServiceTests.cs`/`UsuarioServiceTests.cs` existentes (helper estático `Crear(...)` devolviendo una tupla `(svc, mocks...)`).
- Seeds con `DatosDePrueba.SeedUsuarioAsync` en todo test de API que ejerza una escritura auditada (FK real a `Usuarios`), igual que en Fase 2b.
- Enums numéricos en bodies JSON (convención Minimal API por defecto, sin `JsonStringEnumConverter`).
- Conventional Commits, **sin** `Co-Authored-By`.
- TDD estricto: test rojo → implementación mínima → test verde → commit. Cada task deja la suite completa en verde antes de terminar (no se “rompe hacia adelante”).
- Commits frecuentes (uno por task, al cierre de cada task).

---

## Bloque A — Dominio y excepciones (D4)

**Orden crítico:** el handler (`DomainExceptionHandler`) se actualiza con los casos NUEVOS antes de migrar ningún servicio (Task 2), y solo se le quitan los casos VIEJOS al final del bloque (Task 10), cuando ya no queda ningún `throw new InvalidOperationException`/`KeyNotFoundException` en `StockApp.Application`. Así la suite de `StockApp.Api.Tests` nunca queda roja entre tasks — el handler entiende ambos tipos (viejo y nuevo) durante todo el barrido.

## Task 1: Excepciones de dominio — `EntidadNoEncontradaException`, `ReglaDeNegocioException`, `StockInsuficienteException` hereda de `ReglaDeNegocioException`

**Files:**
- Create: `src/StockApp.Domain/Exceptions/EntidadNoEncontradaException.cs`
- Create: `src/StockApp.Domain/Exceptions/ReglaDeNegocioException.cs`
- Modify: `src/StockApp.Domain/Exceptions/StockInsuficienteException.cs`
- Test: `tests/StockApp.Domain.Tests/Exceptions/EntidadNoEncontradaExceptionTests.cs`
- Test: `tests/StockApp.Domain.Tests/Exceptions/ReglaDeNegocioExceptionTests.cs`
- Modify: `tests/StockApp.Domain.Tests/Exceptions/StockInsuficienteExceptionTests.cs`

**Interfaces:**
- Produces: `EntidadNoEncontradaException(string mensaje)` — consumida por los servicios de Application (Tasks 3-9) y por `DomainExceptionHandler` (Task 2).
- Produces: `ReglaDeNegocioException(string mensaje)` — misma consumición. `StockInsuficienteException` pasa a heredar de esta clase (sin cambiar su constructor público `(int productoId, decimal stockActual, decimal cantidadSolicitada)`).

- [ ] **Step 1: Escribir los tests que fallan — `EntidadNoEncontradaExceptionTests.cs` y `ReglaDeNegocioExceptionTests.cs`**

```csharp
// tests/StockApp.Domain.Tests/Exceptions/EntidadNoEncontradaExceptionTests.cs
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Exceptions;

public class EntidadNoEncontradaExceptionTests
{
    [Fact]
    public void Constructor_ConMensaje_ExponeMessage()
    {
        var ex = new EntidadNoEncontradaException("Producto 5 no encontrado.");

        Assert.Equal("Producto 5 no encontrado.", ex.Message);
    }

    [Fact]
    public void EsException_PeroNoEsReglaDeNegocioException()
    {
        var ex = new EntidadNoEncontradaException("x");

        Assert.IsAssignableFrom<Exception>(ex);
        Assert.IsNotType<ReglaDeNegocioException>(ex);
    }
}
```

```csharp
// tests/StockApp.Domain.Tests/Exceptions/ReglaDeNegocioExceptionTests.cs
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Exceptions;

public class ReglaDeNegocioExceptionTests
{
    [Fact]
    public void Constructor_ConMensaje_ExponeMessage()
    {
        var ex = new ReglaDeNegocioException("Ya existe una categoría con el nombre 'Bebidas'.");

        Assert.Equal("Ya existe una categoría con el nombre 'Bebidas'.", ex.Message);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj --filter "EntidadNoEncontradaExceptionTests|ReglaDeNegocioExceptionTests"`
Expected: FAIL — error de compilación (las clases no existen todavía).

- [ ] **Step 3: Implementar `EntidadNoEncontradaException.cs` y `ReglaDeNegocioException.cs`**

```csharp
// src/StockApp.Domain/Exceptions/EntidadNoEncontradaException.cs
namespace StockApp.Domain.Exceptions;

/// <summary>
/// Se lanza cuando una entidad solicitada por Id (u otra clave) no existe en el sistema.
/// Reemplaza KeyNotFoundException del BCL en los servicios de Application (Fase 3a, D4):
/// permite que DomainExceptionHandler distinga errores de dominio esperables (404) de
/// errores genéricos/no anticipados (500, fail-closed).
/// </summary>
public class EntidadNoEncontradaException : Exception
{
    public EntidadNoEncontradaException(string mensaje) : base(mensaje)
    {
    }
}
```

```csharp
// src/StockApp.Domain/Exceptions/ReglaDeNegocioException.cs
namespace StockApp.Domain.Exceptions;

/// <summary>
/// Se lanza cuando una operación viola una regla de negocio (duplicado, entidad ya
/// inactiva, último Admin, auto-baja, etc). Reemplaza InvalidOperationException del BCL
/// en los servicios de Application (Fase 3a, D4). StockInsuficienteException es un caso
/// particular (falta de stock al registrar una salida) y hereda de esta clase.
/// </summary>
public class ReglaDeNegocioException : Exception
{
    public ReglaDeNegocioException(string mensaje) : base(mensaje)
    {
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj --filter "EntidadNoEncontradaExceptionTests|ReglaDeNegocioExceptionTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Escribir el test que falla — `StockInsuficienteException` hereda de `ReglaDeNegocioException`**

Agregar al final de la clase `StockInsuficienteExceptionTests` (en `tests/StockApp.Domain.Tests/Exceptions/StockInsuficienteExceptionTests.cs`, antes del `}` de cierre):

```csharp

    [Fact]
    public void EsReglaDeNegocioException()
    {
        var ex = new StockInsuficienteException(productoId: 5, stockActual: 3, cantidadSolicitada: 10);

        Assert.IsAssignableFrom<ReglaDeNegocioException>(ex);
    }
```

Agregar `using StockApp.Domain.Exceptions;` ya está presente en el archivo (es el mismo namespace de `StockInsuficienteException`); no hace falta agregar nada nuevo.

- [ ] **Step 6: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj --filter StockInsuficienteExceptionTests`
Expected: FAIL — `StockInsuficienteException` todavía hereda de `Exception`, no de `ReglaDeNegocioException`.

- [ ] **Step 7: Cambiar la clase base de `StockInsuficienteException`**

En `src/StockApp.Domain/Exceptions/StockInsuficienteException.cs`, reemplazar:

```csharp
public class StockInsuficienteException : Exception
```

por:

```csharp
public class StockInsuficienteException : ReglaDeNegocioException
```

El constructor no cambia — sigue llamando a `base($"...")`, ahora resuelto contra el constructor de `ReglaDeNegocioException(string mensaje)` en vez del de `Exception(string message)` (firma compatible, mismo comportamiento).

- [ ] **Step 8: Correr todos los tests de `StockApp.Domain.Tests` y verificar que pasan**

Run: `dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj`
Expected: PASS (todas — las originales + las 4 nuevas)

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Domain/Exceptions tests/StockApp.Domain.Tests/Exceptions
git commit -m "feat(domain): agrega EntidadNoEncontradaException y ReglaDeNegocioException"
```

---

## Task 2: `DomainExceptionHandler` — agrega los casos nuevos (mantiene los viejos)

**Files:**
- Modify: `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`
- Modify: `tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs`

**Interfaces:**
- Consumes: `EntidadNoEncontradaException`, `ReglaDeNegocioException` (Task 1).
- Produces: el handler ahora mapea AMBAS familias — la vieja (`InvalidOperationException`/`KeyNotFoundException` genéricas, temporalmente todavía a 409/404) y la nueva (`EntidadNoEncontradaException`→404, `ReglaDeNegocioException`→409) — para que la suite de `StockApp.Api.Tests` no se rompa mientras el barrido de Tasks 3-9 migra servicio por servicio. El caso viejo se elimina recién en Task 10.

- [ ] **Step 1: Agregar los tests que fallan a `DomainExceptionHandlerTests.cs`**

Agregar al final de la clase `DomainExceptionHandlerTests` (antes del `}` de cierre):

```csharp

    [Fact]
    public async Task EntidadNoEncontradaException_Mapea404()
    {
        var (status, _, _) = await EjecutarAsync(new EntidadNoEncontradaException("Producto 5 no encontrado."));
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task ReglaDeNegocioException_Mapea409()
    {
        var (status, _, _) = await EjecutarAsync(new ReglaDeNegocioException("Ya existe una categoría con ese nombre."));
        Assert.Equal(StatusCodes.Status409Conflict, status);
    }
```

Agregar `using StockApp.Domain.Exceptions;` ya está presente en el archivo (mismo namespace que `StockInsuficienteException`).

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "EntidadNoEncontradaException_Mapea404|ReglaDeNegocioException_Mapea409"`
Expected: FAIL — ambos tipos caen hoy al caso `_` genérico (500), no a 404/409.

- [ ] **Step 3: Agregar los casos nuevos al switch de `DomainExceptionHandler.cs`**

En `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`, reemplazar el bloque del switch:

```csharp
        var (status, title) = exception switch
        {
            StockInsuficienteException  => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            InvalidOperationException   => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            KeyNotFoundException        => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ArgumentException           => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Prohibido."),
            // Binding fallido de Minimal API (ej. valor de query param que no matchea un enum):
            // input inválido del cliente, nunca un 500. Se respeta el StatusCode propio de la
            // excepción (normalmente 400, pero Kestrel puede usar variantes como 413/431).
            BadHttpRequestException ex  => (ex.StatusCode, "Solicitud inválida."),
            _                           => (StatusCodes.Status500InternalServerError, "Error interno."),
        };
```

por:

```csharp
        var (status, title) = exception switch
        {
            // Fase 3a, D4: excepciones de dominio propias — sustituyen gradualmente a las
            // genéricas del BCL de abajo. StockInsuficienteException hereda de
            // ReglaDeNegocioException (Task 1) así que ya matchea acá sin caso propio.
            EntidadNoEncontradaException => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ReglaDeNegocioException      => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            // TODO(Fase 3a, Task 10): eliminar estos dos casos cuando el barrido de
            // servicios de Application (Tasks 3-9) termine de reemplazarlos por los de arriba.
            // Hasta entonces conviven para no romper la suite mientras se migra servicio a servicio.
            InvalidOperationException    => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            KeyNotFoundException         => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ArgumentException            => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            UnauthorizedAccessException  => (StatusCodes.Status403Forbidden, "Prohibido."),
            // Binding fallido de Minimal API (ej. valor de query param que no matchea un enum):
            // input inválido del cliente, nunca un 500. Se respeta el StatusCode propio de la
            // excepción (normalmente 400, pero Kestrel puede usar variantes como 413/431).
            BadHttpRequestException ex   => (ex.StatusCode, "Solicitud inválida."),
            _                            => (StatusCodes.Status500InternalServerError, "Error interno."),
        };
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DomainExceptionHandlerTests`
Expected: PASS (todas — las 7 originales + las 2 nuevas)

- [ ] **Step 5: Correr toda la suite de `StockApp.Api.Tests` para verificar que nada se rompió**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs
git commit -m "feat(api): DomainExceptionHandler reconoce EntidadNoEncontradaException y ReglaDeNegocioException"
```

---

## Task 3: `CategoriaService` migra a excepciones de dominio + `CategoriaListViewModel` + tests

**Files:**
- Modify: `src/StockApp.Application/Catalogo/CategoriaService.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Catalogo/CategoriaListViewModel.cs`
- Modify: `tests/StockApp.Application.Tests/Catalogo/CategoriaServiceTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/CategoriaViewModelTests.cs`

**Interfaces:**
- Consumes: `EntidadNoEncontradaException`, `ReglaDeNegocioException` (Task 1).
- Produces: `CategoriaService` lanza `ReglaDeNegocioException` (antes `InvalidOperationException`) y `EntidadNoEncontradaException` (antes `KeyNotFoundException`) — mismos mensajes exactos.

- [ ] **Step 1: Actualizar los tests existentes de excepción en `CategoriaServiceTests.cs`**

Reemplazar (en `AltaAsync_NombreDuplicado_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AltaAsync(new Categoria { Nombre = "Lácteos" }));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new Categoria { Nombre = "Lácteos" }));
```

Reemplazar (en `BajaLogicaAsync_YaInactiva_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BajaLogicaAsync(2));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
```

Agregar, al final de la clase (antes del `}` de cierre), dos tests nuevos que cubren el camino `EntidadNoEncontradaException` (hoy sin cobertura — `ModificarAsync`/`BajaLogicaAsync` ya lanzaban `KeyNotFoundException` en producción pero ningún test lo ejercitaba):

```csharp

    // ─── EntidadNoEncontradaException (Fase 3a, D4) ─────────────────────────

    [Fact]
    public async Task ModificarAsync_CategoriaInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Categoria?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new Categoria { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_CategoriaInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Categoria?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo:

```csharp
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter CategoriaServiceTests`
Expected: FAIL — `CategoriaService` todavía lanza `InvalidOperationException`/`KeyNotFoundException`.

- [ ] **Step 3: Migrar las excepciones en `CategoriaService.cs`**

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
```

Reemplazar los 4 `throw`:

```csharp
        if (await _repo.ExisteNombreAsync(categoria.Nombre, null))
            throw new InvalidOperationException($"Ya existe una categoría con el nombre '{categoria.Nombre}'.");
```
→
```csharp
        if (await _repo.ExisteNombreAsync(categoria.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe una categoría con el nombre '{categoria.Nombre}'.");
```

```csharp
        var original = await _repo.ObtenerPorIdAsync(categoria.Id)
            ?? throw new KeyNotFoundException($"Categoría {categoria.Id} no encontrada.");
```
→
```csharp
        var original = await _repo.ObtenerPorIdAsync(categoria.Id)
            ?? throw new EntidadNoEncontradaException($"Categoría {categoria.Id} no encontrada.");
```

```csharp
        if (original.Nombre != categoria.Nombre
            && await _repo.ExisteNombreAsync(categoria.Nombre, categoria.Id))
            throw new InvalidOperationException($"Ya existe una categoría con el nombre '{categoria.Nombre}'.");
```
→
```csharp
        if (original.Nombre != categoria.Nombre
            && await _repo.ExisteNombreAsync(categoria.Nombre, categoria.Id))
            throw new ReglaDeNegocioException($"Ya existe una categoría con el nombre '{categoria.Nombre}'.");
```

```csharp
        var categoria = await _repo.ObtenerPorIdAsync(id)
            ?? throw new KeyNotFoundException($"Categoría {id} no encontrada.");

        if (!categoria.Activo)
            throw new InvalidOperationException($"La categoría {id} ya está inactiva.");
```
→
```csharp
        var categoria = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Categoría {id} no encontrada.");

        if (!categoria.Activo)
            throw new ReglaDeNegocioException($"La categoría {id} ya está inactiva.");
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter CategoriaServiceTests`
Expected: PASS (todas — las originales actualizadas + las 2 nuevas)

- [ ] **Step 5: Actualizar los tests de `CategoriaListViewModel` — Mina 2**

En `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/CategoriaViewModelTests.cs`, reemplazar:

```csharp
        var mensaje = "La categoría 5 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new InvalidOperationException(mensaje));
```

por:

```csharp
        var mensaje = "La categoría 5 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));
```

Reemplazar:

```csharp
        var mensaje = "Categoría 5 no encontrada.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new KeyNotFoundException(mensaje));
```

por:

```csharp
        var mensaje = "Categoría 5 no encontrada.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;
```

- [ ] **Step 6: Correr los tests de Presentation y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter CategoriaListViewModelTests`
Expected: FAIL — el `catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)` de `CategoriaListViewModel.BajaAsync` no atrapa `ReglaDeNegocioException`/`EntidadNoEncontradaException`; la excepción se propaga y el test falla por excepción no manejada en vez de verificar `InformarAsync`.

- [ ] **Step 7: Actualizar el filtro `when` de `CategoriaListViewModel.BajaAsync`**

En `src/StockApp.Presentation/ViewModels/Catalogo/CategoriaListViewModel.cs`, reemplazar:

```csharp
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
```

por:

```csharp
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
```

- [ ] **Step 8: Correr los tests de Presentation y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter CategoriaListViewModelTests`
Expected: PASS (todas)

- [ ] **Step 9: Correr toda la suite de `StockApp.Api.Tests` para verificar que el flujo end-to-end sigue verde**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter CategoriasEndpointTests`
Expected: PASS (`PostCategorias_NombreDuplicado_Devuelve409` sigue en 409 porque `DomainExceptionHandler` ya mapea `ReglaDeNegocioException`→409 desde Task 2).

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Application/Catalogo/CategoriaService.cs src/StockApp.Presentation/ViewModels/Catalogo/CategoriaListViewModel.cs tests/StockApp.Application.Tests/Catalogo/CategoriaServiceTests.cs tests/StockApp.Presentation.Tests/ViewModels/Catalogo/CategoriaViewModelTests.cs
git commit -m "feat(catalogo): CategoriaService lanza excepciones de dominio propias"
```

---

## Task 4: `ProveedorService` migra a excepciones de dominio + `ProveedorListViewModel` + tests

**Files:**
- Modify: `src/StockApp.Application/Catalogo/ProveedorService.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Catalogo/ProveedorListViewModel.cs`
- Modify: `tests/StockApp.Application.Tests/Catalogo/ProveedorServiceTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProveedorViewModelTests.cs`

**Interfaces:**
- Consumes: `EntidadNoEncontradaException`, `ReglaDeNegocioException` (Task 1).
- Produces: `ProveedorService` lanza `ReglaDeNegocioException`/`EntidadNoEncontradaException` — mismos mensajes.

- [ ] **Step 1: Actualizar los tests existentes en `ProveedorServiceTests.cs`**

Reemplazar (en `AltaAsync_NombreDuplicado_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AltaAsync(new Proveedor { Nombre = "DistribuidoraX" }));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new Proveedor { Nombre = "DistribuidoraX" }));
```

Reemplazar (en `BajaLogicaAsync_YaInactivo_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BajaLogicaAsync(3));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(3));
```

Agregar, al final de la clase (antes del `}` de cierre):

```csharp

    // ─── EntidadNoEncontradaException (Fase 3a, D4) ─────────────────────────

    [Fact]
    public async Task ModificarAsync_ProveedorInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Proveedor?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new Proveedor { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_ProveedorInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Proveedor?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo (junto a los demás `using StockApp.Domain.*`).

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter ProveedorServiceTests`
Expected: FAIL

- [ ] **Step 3: Migrar las excepciones en `ProveedorService.cs`**

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo. Reemplazar los 4 `throw`:

```csharp
        if (await _repo.ExisteNombreAsync(proveedor.Nombre, null))
            throw new InvalidOperationException($"Ya existe un proveedor con el nombre '{proveedor.Nombre}'.");
```
→
```csharp
        if (await _repo.ExisteNombreAsync(proveedor.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe un proveedor con el nombre '{proveedor.Nombre}'.");
```

```csharp
        var original = await _repo.ObtenerPorIdAsync(proveedor.Id)
            ?? throw new KeyNotFoundException($"Proveedor {proveedor.Id} no encontrado.");
```
→
```csharp
        var original = await _repo.ObtenerPorIdAsync(proveedor.Id)
            ?? throw new EntidadNoEncontradaException($"Proveedor {proveedor.Id} no encontrado.");
```

```csharp
        if (original.Nombre != proveedor.Nombre
            && await _repo.ExisteNombreAsync(proveedor.Nombre, proveedor.Id))
            throw new InvalidOperationException($"Ya existe un proveedor con el nombre '{proveedor.Nombre}'.");
```
→
```csharp
        if (original.Nombre != proveedor.Nombre
            && await _repo.ExisteNombreAsync(proveedor.Nombre, proveedor.Id))
            throw new ReglaDeNegocioException($"Ya existe un proveedor con el nombre '{proveedor.Nombre}'.");
```

```csharp
        var proveedor = await _repo.ObtenerPorIdAsync(id)
            ?? throw new KeyNotFoundException($"Proveedor {id} no encontrado.");

        if (!proveedor.Activo)
            throw new InvalidOperationException($"El proveedor {id} ya está inactivo.");
```
→
```csharp
        var proveedor = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Proveedor {id} no encontrado.");

        if (!proveedor.Activo)
            throw new ReglaDeNegocioException($"El proveedor {id} ya está inactivo.");
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter ProveedorServiceTests`
Expected: PASS

- [ ] **Step 5: Actualizar los tests de `ProveedorListViewModel` — Mina 2**

En `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProveedorViewModelTests.cs`, reemplazar:

```csharp
        var mensaje = "El proveedor 7 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(7)).ThrowsAsync(new InvalidOperationException(mensaje));
```

por:

```csharp
        var mensaje = "El proveedor 7 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(7)).ThrowsAsync(new ReglaDeNegocioException(mensaje));
```

Reemplazar:

```csharp
        var mensaje = "Proveedor 7 no encontrado.";
        svcMock.Setup(s => s.BajaLogicaAsync(7)).ThrowsAsync(new KeyNotFoundException(mensaje));
```

por:

```csharp
        var mensaje = "Proveedor 7 no encontrado.";
        svcMock.Setup(s => s.BajaLogicaAsync(7)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo.

- [ ] **Step 6: Correr los tests de Presentation y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter ProveedorListViewModelTests`
Expected: FAIL

- [ ] **Step 7: Actualizar el filtro `when` de `ProveedorListViewModel.BajaAsync`**

En `src/StockApp.Presentation/ViewModels/Catalogo/ProveedorListViewModel.cs`, reemplazar:

```csharp
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
```

por:

```csharp
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo.

- [ ] **Step 8: Correr los tests de Presentation y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter ProveedorListViewModelTests`
Expected: PASS

- [ ] **Step 9: Correr `StockApp.Api.Tests` de Proveedores para verificar que el flujo end-to-end sigue verde**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProveedoresEndpointTests`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Application/Catalogo/ProveedorService.cs src/StockApp.Presentation/ViewModels/Catalogo/ProveedorListViewModel.cs tests/StockApp.Application.Tests/Catalogo/ProveedorServiceTests.cs tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProveedorViewModelTests.cs
git commit -m "feat(catalogo): ProveedorService lanza excepciones de dominio propias"
```

---

## Task 5: `UnidadMedidaService` migra a excepciones de dominio + `UnidadMedidaListViewModel` + tests

**Files:**
- Modify: `src/StockApp.Application/Catalogo/UnidadMedidaService.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Catalogo/UnidadMedidaListViewModel.cs`
- Modify: `tests/StockApp.Application.Tests/Catalogo/UnidadMedidaServiceTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/UnidadMedidaViewModelTests.cs`

**Interfaces:**
- Consumes: `EntidadNoEncontradaException`, `ReglaDeNegocioException` (Task 1).
- Produces: `UnidadMedidaService` lanza `ReglaDeNegocioException`/`EntidadNoEncontradaException` — mismos mensajes.

- [ ] **Step 1: Actualizar los tests existentes en `UnidadMedidaServiceTests.cs`**

Reemplazar (en `AltaAsync_NombreDuplicado_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AltaAsync(new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg" }));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg" }));
```

Reemplazar (en `AltaAsync_AbrebiaturaDuplicada_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AltaAsync(new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg" }));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg" }));
```

(Nota: los dos tests de duplicado — nombre y abreviatura — quedan con el mismo cuerpo de assert; solo difieren en el `Setup` previo del mock, ya presente sin cambios.)

Reemplazar (en `BajaLogicaAsync_YaInactiva_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BajaLogicaAsync(4));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(4));
```

Agregar, al final de la clase (antes del `}` de cierre):

```csharp

    // ─── EntidadNoEncontradaException (Fase 3a, D4) ─────────────────────────

    [Fact]
    public async Task ModificarAsync_UnidadInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((UnidadMedida?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new UnidadMedida { Id = 99, Nombre = "X", Abreviatura = "x" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_UnidadInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((UnidadMedida?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo.

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter UnidadMedidaServiceTests`
Expected: FAIL

- [ ] **Step 3: Migrar las excepciones en `UnidadMedidaService.cs`**

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo. Reemplazar los 6 `throw`:

```csharp
        if (await _repo.ExisteNombreAsync(unidadMedida.Nombre, null))
            throw new InvalidOperationException($"Ya existe una unidad de medida con el nombre '{unidadMedida.Nombre}'.");

        if (await _repo.ExisteAbreviaturaAsync(unidadMedida.Abreviatura, null))
            throw new InvalidOperationException($"Ya existe una unidad de medida con la abreviatura '{unidadMedida.Abreviatura}'.");
```
→
```csharp
        if (await _repo.ExisteNombreAsync(unidadMedida.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe una unidad de medida con el nombre '{unidadMedida.Nombre}'.");

        if (await _repo.ExisteAbreviaturaAsync(unidadMedida.Abreviatura, null))
            throw new ReglaDeNegocioException($"Ya existe una unidad de medida con la abreviatura '{unidadMedida.Abreviatura}'.");
```

```csharp
        var original = await _repo.ObtenerPorIdAsync(unidadMedida.Id)
            ?? throw new KeyNotFoundException($"UnidadMedida {unidadMedida.Id} no encontrada.");

        if (original.Nombre != unidadMedida.Nombre
            && await _repo.ExisteNombreAsync(unidadMedida.Nombre, unidadMedida.Id))
            throw new InvalidOperationException($"Ya existe una unidad de medida con el nombre '{unidadMedida.Nombre}'.");

        if (original.Abreviatura != unidadMedida.Abreviatura
            && await _repo.ExisteAbreviaturaAsync(unidadMedida.Abreviatura, unidadMedida.Id))
            throw new InvalidOperationException($"Ya existe una unidad de medida con la abreviatura '{unidadMedida.Abreviatura}'.");
```
→
```csharp
        var original = await _repo.ObtenerPorIdAsync(unidadMedida.Id)
            ?? throw new EntidadNoEncontradaException($"UnidadMedida {unidadMedida.Id} no encontrada.");

        if (original.Nombre != unidadMedida.Nombre
            && await _repo.ExisteNombreAsync(unidadMedida.Nombre, unidadMedida.Id))
            throw new ReglaDeNegocioException($"Ya existe una unidad de medida con el nombre '{unidadMedida.Nombre}'.");

        if (original.Abreviatura != unidadMedida.Abreviatura
            && await _repo.ExisteAbreviaturaAsync(unidadMedida.Abreviatura, unidadMedida.Id))
            throw new ReglaDeNegocioException($"Ya existe una unidad de medida con la abreviatura '{unidadMedida.Abreviatura}'.");
```

```csharp
        var unidadMedida = await _repo.ObtenerPorIdAsync(id)
            ?? throw new KeyNotFoundException($"UnidadMedida {id} no encontrada.");

        if (!unidadMedida.Activo)
            throw new InvalidOperationException($"La unidad de medida {id} ya está inactiva.");
```
→
```csharp
        var unidadMedida = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"UnidadMedida {id} no encontrada.");

        if (!unidadMedida.Activo)
            throw new ReglaDeNegocioException($"La unidad de medida {id} ya está inactiva.");
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter UnidadMedidaServiceTests`
Expected: PASS

- [ ] **Step 5: Actualizar los tests de `UnidadMedidaListViewModel` — Mina 2**

En `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/UnidadMedidaViewModelTests.cs`, reemplazar:

```csharp
        var mensaje = "La unidad de medida 3 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(3)).ThrowsAsync(new InvalidOperationException(mensaje));
```

por:

```csharp
        var mensaje = "La unidad de medida 3 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(3)).ThrowsAsync(new ReglaDeNegocioException(mensaje));
```

Reemplazar:

```csharp
        var mensaje = "UnidadMedida 3 no encontrada.";
        svcMock.Setup(s => s.BajaLogicaAsync(3)).ThrowsAsync(new KeyNotFoundException(mensaje));
```

por:

```csharp
        var mensaje = "UnidadMedida 3 no encontrada.";
        svcMock.Setup(s => s.BajaLogicaAsync(3)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo.

- [ ] **Step 6: Correr los tests de Presentation y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter UnidadMedidaListViewModelTests`
Expected: FAIL

- [ ] **Step 7: Actualizar el filtro `when` de `UnidadMedidaListViewModel.BajaAsync`**

En `src/StockApp.Presentation/ViewModels/Catalogo/UnidadMedidaListViewModel.cs`, reemplazar:

```csharp
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
```

por:

```csharp
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo.

- [ ] **Step 8: Correr los tests de Presentation y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter UnidadMedidaListViewModelTests`
Expected: PASS

- [ ] **Step 9: Correr `StockApp.Api.Tests` de Unidades de Medida para verificar que el flujo end-to-end sigue verde**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UnidadesMedidaEndpointTests`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Application/Catalogo/UnidadMedidaService.cs src/StockApp.Presentation/ViewModels/Catalogo/UnidadMedidaListViewModel.cs tests/StockApp.Application.Tests/Catalogo/UnidadMedidaServiceTests.cs tests/StockApp.Presentation.Tests/ViewModels/Catalogo/UnidadMedidaViewModelTests.cs
git commit -m "feat(catalogo): UnidadMedidaService lanza excepciones de dominio propias"
```

---

## Task 6: `ProductoService` migra a excepciones de dominio + `ProductoListViewModel` + `ProductoFormViewModel` test + tests

**Files:**
- Modify: `src/StockApp.Application/Catalogo/ProductoService.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs`
- Modify: `tests/StockApp.Application.Tests/Catalogo/ProductoServiceTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs`

**Interfaces:**
- Consumes: `EntidadNoEncontradaException`, `ReglaDeNegocioException` (Task 1).
- Produces: `ProductoService` lanza `ReglaDeNegocioException`/`EntidadNoEncontradaException` — mismos mensajes. `ArgumentException` (validaciones de precio/unidad) NO cambia — sigue siendo `ArgumentException`.

- [ ] **Step 1: Actualizar los tests existentes en `ProductoServiceTests.cs`**

Reemplazar (en `AltaAsync_CodigoDuplicado_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AltaAsync(p));
```

(la primera ocurrencia, dentro de `AltaAsync_CodigoDuplicado_LanzaInvalidOperation`) por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(p));
```

Reemplazar (en `AltaAsync_CodigoBarrasDuplicado_LanzaInvalidOperation`, la segunda ocurrencia del mismo patrón):

```csharp
        var p = new Producto { Codigo = "SKU-002", Nombre = "Fideos", UnidadMedidaId = 1, CodigoBarras = "7891234567890" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AltaAsync(p));
```

por:

```csharp
        var p = new Producto { Codigo = "SKU-002", Nombre = "Fideos", UnidadMedidaId = 1, CodigoBarras = "7891234567890" };
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(p));
```

Reemplazar (en `ModificarAsync_CodigoBarrasDuplicado_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ModificarAsync(mod));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.ModificarAsync(mod));
```

Reemplazar (en `BajaLogicaAsync_YaInactivo_LanzaInvalidOperation`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BajaLogicaAsync(5));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(5));
```

Agregar, al final de la clase (antes del `}` de cierre):

```csharp

    // ─── EntidadNoEncontradaException (Fase 3a, D4) ─────────────────────────

    [Fact]
    public async Task ModificarAsync_ProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new Producto { Id = 99, Codigo = "X", Nombre = "X", UnidadMedidaId = 1 }));
    }

    [Fact]
    public async Task BajaLogicaAsync_ProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo.

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter ProductoServiceTests`
Expected: FAIL

- [ ] **Step 3: Migrar las excepciones en `ProductoService.cs`**

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo. Reemplazar los 6 `throw`:

```csharp
        if (await _repo.ExisteCodigoAsync(producto.Codigo, null))
            throw new InvalidOperationException($"Ya existe un producto con el código '{producto.Codigo}'.");

        if (!string.IsNullOrWhiteSpace(producto.CodigoBarras)
            && await _repo.ExisteCodigoBarrasAsync(producto.CodigoBarras, null))
            throw new InvalidOperationException($"Ya existe un producto con el código de barras '{producto.CodigoBarras}'.");
```
→
```csharp
        if (await _repo.ExisteCodigoAsync(producto.Codigo, null))
            throw new ReglaDeNegocioException($"Ya existe un producto con el código '{producto.Codigo}'.");

        if (!string.IsNullOrWhiteSpace(producto.CodigoBarras)
            && await _repo.ExisteCodigoBarrasAsync(producto.CodigoBarras, null))
            throw new ReglaDeNegocioException($"Ya existe un producto con el código de barras '{producto.CodigoBarras}'.");
```

```csharp
        var original = await _repo.ObtenerPorIdAsync(producto.Id)
            ?? throw new KeyNotFoundException($"Producto {producto.Id} no encontrado.");

        // Validar unicidad de código de barras si cambió
        if (!string.IsNullOrWhiteSpace(producto.CodigoBarras)
            && producto.CodigoBarras != original.CodigoBarras
            && await _repo.ExisteCodigoBarrasAsync(producto.CodigoBarras, producto.Id))
            throw new InvalidOperationException($"Ya existe un producto con el código de barras '{producto.CodigoBarras}'.");
```
→
```csharp
        var original = await _repo.ObtenerPorIdAsync(producto.Id)
            ?? throw new EntidadNoEncontradaException($"Producto {producto.Id} no encontrado.");

        // Validar unicidad de código de barras si cambió
        if (!string.IsNullOrWhiteSpace(producto.CodigoBarras)
            && producto.CodigoBarras != original.CodigoBarras
            && await _repo.ExisteCodigoBarrasAsync(producto.CodigoBarras, producto.Id))
            throw new ReglaDeNegocioException($"Ya existe un producto con el código de barras '{producto.CodigoBarras}'.");
```

```csharp
        var producto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new KeyNotFoundException($"Producto {id} no encontrado.");

        if (!producto.Activo)
            throw new InvalidOperationException($"El producto {id} ya está inactivo.");
```
→
```csharp
        var producto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Producto {id} no encontrado.");

        if (!producto.Activo)
            throw new ReglaDeNegocioException($"El producto {id} ya está inactivo.");
```

Este último bloque (`?? throw new KeyNotFoundException($"Producto {id} no encontrado.");` sin el chequeo de `Activo` a continuación) también aparece en `CambiarPrecioAsync` — reemplazar igual:

```csharp
        var producto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new KeyNotFoundException($"Producto {id} no encontrado.");

        var detalle = $"PrecioCosto: {producto.PrecioCosto} → {precioCosto}; PrecioVenta: {producto.PrecioVenta} → {precioVenta}";
```
→
```csharp
        var producto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Producto {id} no encontrado.");

        var detalle = $"PrecioCosto: {producto.PrecioCosto} → {precioCosto}; PrecioVenta: {producto.PrecioVenta} → {precioVenta}";
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter ProductoServiceTests`
Expected: PASS

- [ ] **Step 5: Actualizar los tests de `ProductoListViewModel` y `ProductoFormViewModel` — Mina 2**

En `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs`, reemplazar (test `BajaCommand_ServicioLanzaInvalidOperationException_NoPropagaYInforma`):

```csharp
        var mensaje = "El producto 5 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new InvalidOperationException(mensaje));
```

por:

```csharp
        var mensaje = "El producto 5 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));
```

Reemplazar (test `BajaCommand_ServicioLanzaKeyNotFoundException_NoPropagaYInforma`):

```csharp
        var mensaje = "Producto 5 no encontrado.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new KeyNotFoundException(mensaje));
```

por:

```csharp
        var mensaje = "Producto 5 no encontrado.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));
```

Reemplazar (test `GuardarCommand_ModoEdicion_ServicioLanzaExcepcionDeDominio_MuestraMensajeErrorYNoCrashea` — el catch de `ProductoFormViewModel.GuardarAsync` es `catch (System.Exception ex)` genérico, así que este test NO necesita cambiar de comportamiento, pero se actualiza para reflejar el tipo real que `ProductoService.ModificarAsync` lanza ahora, evitando que el test quede mockeando un tipo que ya no existe en producción):

```csharp
        var mensaje = "Ya existe un producto con el código de barras '7791234567890'.";
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<Producto>()))
            .ThrowsAsync(new InvalidOperationException(mensaje));
```

por:

```csharp
        var mensaje = "Ya existe un producto con el código de barras '7791234567890'.";
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<Producto>()))
            .ThrowsAsync(new ReglaDeNegocioException(mensaje));
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo (junto a `using StockApp.Domain.Entities;` ya presente).

- [ ] **Step 6: Correr los tests de Presentation y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter ProductoListViewModelTests`
Expected: FAIL — el filtro `when` de `ProductoListViewModel.BajaAsync` no atrapa los tipos nuevos. (El test de `ProductoFormViewModelTests` sigue en verde porque su catch es genérico — se actualizó solo por prolijidad, no por necesidad de compilación.)

- [ ] **Step 7: Actualizar el filtro `when` de `ProductoListViewModel.BajaAsync`**

En `src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs`, reemplazar:

```csharp
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
```

por:

```csharp
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
```

- [ ] **Step 8: Correr los tests de Presentation y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "ProductoListViewModelTests|ProductoFormViewModelTests"`
Expected: PASS (todas)

- [ ] **Step 9: Correr `StockApp.Api.Tests` de Productos para verificar que el flujo end-to-end sigue verde**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProductosEndpointTests`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Application/Catalogo/ProductoService.cs src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs tests/StockApp.Application.Tests/Catalogo/ProductoServiceTests.cs tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs
git commit -m "feat(catalogo): ProductoService lanza excepciones de dominio propias"
```

---

## Task 7: `UsuarioService` migra a excepciones de dominio + tests (sin impacto en Presentation)

**Files:**
- Modify: `src/StockApp.Application/Auth/UsuarioService.cs`
- Modify: `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs`

**Interfaces:**
- Consumes: `EntidadNoEncontradaException`, `ReglaDeNegocioException` (Task 1).
- Produces: `UsuarioService` lanza `ReglaDeNegocioException` (auto-baja, último Admin) y `EntidadNoEncontradaException` (usuario no encontrado). `UnauthorizedAccessException` (contraseña incorrecta / falta confirmación) NO cambia.

**Nota de alcance:** No existe hoy ningún `UsuarioListViewModel`/`UsuarioViewModel` en `StockApp.Presentation` que llame a `BajaLogicaAsync`/`CambiarRolAsync`/`CambiarContrasenaAsync` de `IUsuarioService` (confirmado: `rg` sobre `tests/StockApp.Presentation.Tests/` no encontró ningún mock de esos métodos). Este task no toca Presentation.

- [ ] **Step 1: Actualizar los tests existentes en `UsuarioServiceTests.cs`**

Reemplazar (en `BajaLogica_NoSePuedeAutoEliminar`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BajaLogicaAsync(1));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(1));
```

Reemplazar (en `BajaLogica_NoSePuedeDeshabilitarUltimoAdmin`):

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BajaLogicaAsync(2));
```

por:

```csharp
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
```

Agregar, al final de la clase (antes del `}` de cierre):

```csharp

    // ─── EntidadNoEncontradaException (Fase 3a, D4) ─────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_UsuarioInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }

    [Fact]
    public async Task CambiarRolAsync_UsuarioInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.CambiarRolAsync(99, RolUsuario.Admin));
    }

    [Fact]
    public async Task CambiarContrasenaAsync_UsuarioInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.CambiarContrasenaAsync(99, "nuevaContrasena123"));
    }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter UsuarioServiceTests`
Expected: FAIL

- [ ] **Step 3: Migrar las excepciones en `UsuarioService.cs`**

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Auth;
```

Reemplazar los 4 `throw` en `BajaLogicaAsync`, `CambiarRolAsync` y `CambiarContrasenaAsync`:

```csharp
        // Fix 2: no auto-baja
        if (usuarioId == _session.UsuarioActual!.Id)
            throw new InvalidOperationException("Un usuario no puede darse de baja a sí mismo.");

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new KeyNotFoundException($"Usuario {usuarioId} no encontrado.");

        // Fix 2: proteger último Admin activo
        if (usuario.Rol == RolUsuario.Admin && usuario.Activo)
        {
            var adminsActivos = await _repo.ContarAdminsActivosAsync();
            if (adminsActivos <= 1)
                throw new InvalidOperationException(
                    "No se puede deshabilitar al último Admin activo del sistema.");
        }
```
→
```csharp
        // Fix 2: no auto-baja
        if (usuarioId == _session.UsuarioActual!.Id)
            throw new ReglaDeNegocioException("Un usuario no puede darse de baja a sí mismo.");

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        // Fix 2: proteger último Admin activo
        if (usuario.Rol == RolUsuario.Admin && usuario.Activo)
        {
            var adminsActivos = await _repo.ContarAdminsActivosAsync();
            if (adminsActivos <= 1)
                throw new ReglaDeNegocioException(
                    "No se puede deshabilitar al último Admin activo del sistema.");
        }
```

```csharp
        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new KeyNotFoundException($"Usuario {usuarioId} no encontrado.");

        var rolAnterior = usuario.Rol;
```
→
```csharp
        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        var rolAnterior = usuario.Rol;
```

```csharp
        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new KeyNotFoundException($"Usuario {usuarioId} no encontrado.");

        // Fix 7: auto-cambio requiere contraseña actual
```
→
```csharp
        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        // Fix 7: auto-cambio requiere contraseña actual
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter UsuarioServiceTests`
Expected: PASS (todas — las originales actualizadas + las 3 nuevas)

- [ ] **Step 5: Correr `StockApp.Api.Tests` de Usuarios para verificar que el flujo end-to-end sigue verde**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UsuariosEndpointTests`
Expected: PASS (`DeleteUsuario_AutoBaja_Devuelve409` sigue en 409 porque `DomainExceptionHandler` ya mapea `ReglaDeNegocioException`→409 desde Task 2).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Auth/UsuarioService.cs tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs
git commit -m "feat(auth): UsuarioService lanza excepciones de dominio propias"
```

---

## Task 8: `PrimerArranqueService` migra a excepciones de dominio + `PrimerArranqueViewModel` + tests

**Files:**
- Modify: `src/StockApp.Application/Auth/PrimerArranqueService.cs`
- Modify: `src/StockApp.Presentation/ViewModels/PrimerArranqueViewModel.cs`
- Modify: `tests/StockApp.Application.Tests/Auth/PrimerArranqueServiceTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/PrimerArranqueViewModelTests.cs`

**Interfaces:**
- Consumes: `ReglaDeNegocioException` (Task 1).
- Produces: `PrimerArranqueService.CrearAdminInicialAsync` lanza `ReglaDeNegocioException` (antes `InvalidOperationException`) cuando ya existe al menos un usuario. `ArgumentException` (validación de contraseña) NO cambia. Este resultado lo consume directamente `POST /auth/primer-admin` (Task 21, Bloque C) — ya mapeará a 409 sin cambios adicionales gracias a Task 2.

- [ ] **Step 1: Actualizar el test existente en `PrimerArranqueServiceTests.cs`**

Reemplazar (en `CrearAdminInicial_SiYaHayUsuarios_LanzaExcepcion`):

```csharp
        // Fix 6: la contraseña debe cumplir el mínimo (≥6 chars) para llegar al check de usuarios
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CrearAdminInicialAsync("admin", "password123"));
```

por:

```csharp
        // Fix 6: la contraseña debe cumplir el mínimo (≥6 chars) para llegar al check de usuarios
        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.CrearAdminInicialAsync("admin", "password123"));
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter CrearAdminInicial_SiYaHayUsuarios_LanzaExcepcion`
Expected: FAIL

- [ ] **Step 3: Migrar la excepción en `PrimerArranqueService.cs`**

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo. Reemplazar:

```csharp
            if (!await RequiereCrearAdminAsync())
                throw new InvalidOperationException(
                    "No se puede crear el Admin inicial: ya existen usuarios en la base de datos.");
```

por:

```csharp
            if (!await RequiereCrearAdminAsync())
                throw new ReglaDeNegocioException(
                    "No se puede crear el Admin inicial: ya existen usuarios en la base de datos.");
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter PrimerArranqueServiceTests`
Expected: PASS (todas)

- [ ] **Step 5: Actualizar el test de `PrimerArranqueViewModel` — Mina 2**

En `tests/StockApp.Presentation.Tests/ViewModels/PrimerArranqueViewModelTests.cs`, reemplazar (test `CrearAdmin_InvalidOperationException_MuestraMensajeError`):

```csharp
    [Fact]
    public async Task CrearAdmin_InvalidOperationException_MuestraMensajeError()
    {
        var ctx = Crear(excepcionCreacion: new InvalidOperationException("Ya existe un usuario."));
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto123";

        await ctx.Vm.CrearAdminCommand.ExecuteAsync(null);

        Assert.NotNull(ctx.Vm.MensajeError);
        Assert.False(ctx.Vm.MostrarRecomendacion2doAdmin);
    }
```

por:

```csharp
    [Fact]
    public async Task CrearAdmin_ReglaDeNegocioException_MuestraMensajeError()
    {
        var ctx = Crear(excepcionCreacion: new ReglaDeNegocioException("Ya existe un usuario."));
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto123";

        await ctx.Vm.CrearAdminCommand.ExecuteAsync(null);

        Assert.NotNull(ctx.Vm.MensajeError);
        Assert.False(ctx.Vm.MostrarRecomendacion2doAdmin);
    }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo:

```csharp
using Moq;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;
```

- [ ] **Step 6: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter CrearAdmin_ReglaDeNegocioException_MuestraMensajeError`
Expected: FAIL — `PrimerArranqueViewModel.CrearAdminAsync` tiene `catch (InvalidOperationException ex)` específico, que no atrapa `ReglaDeNegocioException`; la excepción se propaga sin manejar.

- [ ] **Step 7: Actualizar el catch de `PrimerArranqueViewModel.CrearAdminAsync`**

En `src/StockApp.Presentation/ViewModels/PrimerArranqueViewModel.cs`, reemplazar:

```csharp
        catch (InvalidOperationException ex)
        {
            MensajeError = ex.Message;
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
```

por:

```csharp
        catch (ReglaDeNegocioException ex)
        {
            MensajeError = ex.Message;
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo (junto al resto de los `using`).

- [ ] **Step 8: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter PrimerArranqueViewModelTests`
Expected: PASS (todas)

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Application/Auth/PrimerArranqueService.cs src/StockApp.Presentation/ViewModels/PrimerArranqueViewModel.cs tests/StockApp.Application.Tests/Auth/PrimerArranqueServiceTests.cs tests/StockApp.Presentation.Tests/ViewModels/PrimerArranqueViewModelTests.cs
git commit -m "feat(auth): PrimerArranqueService lanza ReglaDeNegocioException"
```

---

## Task 9: `MovimientoStockService` migra a excepciones de dominio + test de Presentation + tests

**Files:**
- Modify: `src/StockApp.Application/Movimientos/MovimientoStockService.cs`
- Modify: `tests/StockApp.Application.Tests/Movimientos/MovimientoStockServiceTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/MovimientoRegistroViewModelTestsBase.cs`

**Interfaces:**
- Consumes: `EntidadNoEncontradaException`, `ReglaDeNegocioException` (Task 1).
- Produces: `MovimientoStockService` lanza `EntidadNoEncontradaException` (producto no encontrado, en `RegistrarAsync` y `RecalcularStockAsync`) y `ReglaDeNegocioException` (producto inactivo). `StockInsuficienteException` (ya hereda de `ReglaDeNegocioException` desde Task 1) no cambia su sitio de `throw`.

**Nota de alcance:** `MovimientoRegistroViewModelBase.RegistrarAsync` (`src/StockApp.Presentation/ViewModels/Movimientos/MovimientoRegistroViewModelBase.cs`) captura `StockInsuficienteException` primero (tipo exacto, sin cambios) y después `catch (Exception ex)` genérico — **no tiene ningún filtro `when` acoplado a `InvalidOperationException`/`KeyNotFoundException`**, así que no requiere cambio de código. Solo se actualiza el test que simula el camino "otra excepción" para que siga reflejando el tipo real que el servicio lanza.

- [ ] **Step 1: Actualizar los tests existentes en `MovimientoStockServiceTests.cs`**

Reemplazar (en `RegistrarAsync_AjusteMermaSinPrecio_NoPasaValidacionPrecioYSigueAlProducto`):

```csharp
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.RegistrarAsync(dto));
```

por:

```csharp
        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.RegistrarAsync(dto));
```

Renombrar y actualizar `RegistrarAsync_ProductoNoExiste_LanzaKeyNotFoundException`:

```csharp
    [Fact]
    public async Task RegistrarAsync_ProductoNoExiste_LanzaKeyNotFoundException()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerProductoAsync(99)).ReturnsAsync((Producto?)null);

        var dto = DtoEntrada(productoId: 99);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.RegistrarAsync(dto));
    }
```

por:

```csharp
    [Fact]
    public async Task RegistrarAsync_ProductoNoExiste_LanzaEntidadNoEncontradaException()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerProductoAsync(99)).ReturnsAsync((Producto?)null);

        var dto = DtoEntrada(productoId: 99);
        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.RegistrarAsync(dto));
    }
```

Renombrar y actualizar `RegistrarAsync_ProductoInactivo_LanzaInvalidOperationException`:

```csharp
    [Fact]
    public async Task RegistrarAsync_ProductoInactivo_LanzaInvalidOperationException()
    {
        var (svc, repo, _, _) = Crear();
        var producto = new Producto { Id = 1, Activo = false, Nombre = "X",
                                      Codigo = "X", StockActual = 10m, UnidadMedidaId = 1 };
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);

        var dto = DtoEntrada();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RegistrarAsync(dto));
    }
```

por:

```csharp
    [Fact]
    public async Task RegistrarAsync_ProductoInactivo_LanzaReglaDeNegocioException()
    {
        var (svc, repo, _, _) = Crear();
        var producto = new Producto { Id = 1, Activo = false, Nombre = "X",
                                      Codigo = "X", StockActual = 10m, UnidadMedidaId = 1 };
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);

        var dto = DtoEntrada();
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.RegistrarAsync(dto));
    }
```

Renombrar y actualizar `RecalcularStockAsync_ProductoNoExiste_LanzaKeyNotFoundException`:

```csharp
    [Fact]
    public async Task RecalcularStockAsync_ProductoNoExiste_LanzaKeyNotFoundException()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerProductoAsync(99)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.RecalcularStockAsync(99));
    }
```

por:

```csharp
    [Fact]
    public async Task RecalcularStockAsync_ProductoNoExiste_LanzaEntidadNoEncontradaException()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerProductoAsync(99)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.RecalcularStockAsync(99));
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter MovimientoStockServiceTests`
Expected: FAIL

- [ ] **Step 3: Migrar las excepciones en `MovimientoStockService.cs`**

`using StockApp.Domain.Exceptions;` ya está presente en el archivo (lo usa `StockInsuficienteException`). Reemplazar:

```csharp
        // B5: existencia y estado del producto
        var producto = await _repo.ObtenerProductoAsync(dto.ProductoId)
            ?? throw new KeyNotFoundException($"Producto {dto.ProductoId} no encontrado.");

        if (!producto.Activo)
            throw new InvalidOperationException(
                $"No se permiten movimientos sobre productos inactivos (ProductoId={dto.ProductoId}).");
```

por:

```csharp
        // B5: existencia y estado del producto
        var producto = await _repo.ObtenerProductoAsync(dto.ProductoId)
            ?? throw new EntidadNoEncontradaException($"Producto {dto.ProductoId} no encontrado.");

        if (!producto.Activo)
            throw new ReglaDeNegocioException(
                $"No se permiten movimientos sobre productos inactivos (ProductoId={dto.ProductoId}).");
```

Reemplazar:

```csharp
        var producto = await _repo.ObtenerProductoAsync(productoId)
            ?? throw new KeyNotFoundException($"Producto {productoId} no encontrado.");

        var (neto, total) = await _repo.SumarMovimientosAsync(productoId);
```

por:

```csharp
        var producto = await _repo.ObtenerProductoAsync(productoId)
            ?? throw new EntidadNoEncontradaException($"Producto {productoId} no encontrado.");

        var (neto, total) = await _repo.SumarMovimientosAsync(productoId);
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter MovimientoStockServiceTests`
Expected: PASS (todas)

- [ ] **Step 5: Actualizar el test de Presentation — Mina 2**

En `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/MovimientoRegistroViewModelTestsBase.cs`, reemplazar (test `RegistrarAsync_OtraExcepcion_SetMensajeError`):

```csharp
        svcMock
            .Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), false))
            .ThrowsAsync(new InvalidOperationException("Producto inactivo, no se permiten movimientos."));
```

por:

```csharp
        svcMock
            .Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), false))
            .ThrowsAsync(new ReglaDeNegocioException("Producto inactivo, no se permiten movimientos."));
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo (ya está presente — el archivo ya importa `StockApp.Domain.Exceptions` para `StockInsuficienteException`, confirmar y no duplicar el `using`).

- [ ] **Step 6: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "EntradaRegistroViewModelTests|SalidaRegistroViewModelTests"`
Expected: PASS (todas — el catch genérico de `MovimientoRegistroViewModelBase.RegistrarAsync` no distingue el tipo, así que el test pasa sin cambios de producción; solo se actualizó el mock para reflejar el comportamiento real post-migración).

- [ ] **Step 7: Correr `StockApp.Api.Tests` de Movimientos para verificar que el flujo end-to-end sigue verde**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter MovimientosEndpointTests`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Application/Movimientos/MovimientoStockService.cs tests/StockApp.Application.Tests/Movimientos/MovimientoStockServiceTests.cs tests/StockApp.Presentation.Tests/ViewModels/Movimientos/MovimientoRegistroViewModelTestsBase.cs
git commit -m "feat(movimientos): MovimientoStockService lanza excepciones de dominio propias"
```

---

## Task 10: `DomainExceptionHandler` — cierre (elimina los casos genéricos viejos)

**Files:**
- Modify: `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`
- Modify: `tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs`

**Interfaces:**
- Consumes: nada nuevo — cierra el barrido de Tasks 3-9.
- Produces: `InvalidOperationException`/`KeyNotFoundException` genéricas (no lanzadas por ningún servicio de `StockApp.Application` desde este punto) caen al caso `_` → 500 sin `Detail`, tal como especifica el spec ("InvalidOperationException y KeyNotFoundException genéricas pasan al caso 500 saneado").

- [ ] **Step 1: Verificar que no queda ningún `throw new InvalidOperationException`/`KeyNotFoundException` en Application**

Run: `rg -n "throw new (InvalidOperationException|KeyNotFoundException)" src/StockApp.Application/`
Expected: sin resultados (0 matches) — si aparece alguno, ese servicio quedó sin migrar en Tasks 3-9; hay que volver atrás y migrarlo antes de continuar con este task.

- [ ] **Step 2: Actualizar los tests de `DomainExceptionHandlerTests.cs` para reflejar el comportamiento final**

Reemplazar:

```csharp
    [Fact]
    public async Task InvalidOperationException_Mapea409()
    {
        var (status, _, _) = await EjecutarAsync(new InvalidOperationException("ya existe"));
        Assert.Equal(StatusCodes.Status409Conflict, status);
    }

    [Fact]
    public async Task KeyNotFoundException_Mapea404()
    {
        var (status, _, _) = await EjecutarAsync(new KeyNotFoundException("no existe"));
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }
```

por:

```csharp
    [Fact]
    public async Task InvalidOperationException_Generica_Mapea500SinExponerElMensajeInterno()
    {
        // Fase 3a, D4: ningún servicio de Application lanza esta excepción genérica del BCL —
        // solo las de dominio propias (ReglaDeNegocioException/EntidadNoEncontradaException).
        // Si algo la lanza igual (código nuevo que no siguió la convención), es un error no
        // anticipado: cae al 500 fail-closed, no a un 409 que sugeriría una regla de negocio real.
        var (status, _, body) = await EjecutarAsync(new InvalidOperationException("detalle interno"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        var tieneDetail = body.RootElement.TryGetProperty("detail", out var detalle);
        if (tieneDetail)
            Assert.DoesNotContain("detalle interno", detalle.GetString());
    }

    [Fact]
    public async Task KeyNotFoundException_Generica_Mapea500SinExponerElMensajeInterno()
    {
        var (status, _, body) = await EjecutarAsync(new KeyNotFoundException("detalle interno"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        var tieneDetail = body.RootElement.TryGetProperty("detail", out var detalle);
        if (tieneDetail)
            Assert.DoesNotContain("detalle interno", detalle.GetString());
    }
```

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "InvalidOperationException_Generica_Mapea500SinExponerElMensajeInterno|KeyNotFoundException_Generica_Mapea500SinExponerElMensajeInterno"`
Expected: FAIL — el handler todavía mapea estos tipos a 409/404 (casos viejos presentes desde Task 2).

- [ ] **Step 4: Eliminar los casos genéricos viejos del switch**

En `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`, reemplazar:

```csharp
        var (status, title) = exception switch
        {
            // Fase 3a, D4: excepciones de dominio propias — sustituyen gradualmente a las
            // genéricas del BCL de abajo. StockInsuficienteException hereda de
            // ReglaDeNegocioException (Task 1) así que ya matchea acá sin caso propio.
            EntidadNoEncontradaException => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ReglaDeNegocioException      => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            // TODO(Fase 3a, Task 10): eliminar estos dos casos cuando el barrido de
            // servicios de Application (Tasks 3-9) termine de reemplazarlos por los de arriba.
            // Hasta entonces conviven para no romper la suite mientras se migra servicio a servicio.
            InvalidOperationException    => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            KeyNotFoundException         => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ArgumentException            => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            UnauthorizedAccessException  => (StatusCodes.Status403Forbidden, "Prohibido."),
            // Binding fallido de Minimal API (ej. valor de query param que no matchea un enum):
            // input inválido del cliente, nunca un 500. Se respeta el StatusCode propio de la
            // excepción (normalmente 400, pero Kestrel puede usar variantes como 413/431).
            BadHttpRequestException ex   => (ex.StatusCode, "Solicitud inválida."),
            _                            => (StatusCodes.Status500InternalServerError, "Error interno."),
        };
```

por:

```csharp
        var (status, title) = exception switch
        {
            // Fase 3a, D4: única fuente de 404/409 de negocio. StockInsuficienteException
            // hereda de ReglaDeNegocioException (Task 1) así que ya matchea acá sin caso propio.
            // InvalidOperationException/KeyNotFoundException genéricas del BCL YA NO las lanza
            // ningún servicio de StockApp.Application — si aparecen, es un error no anticipado
            // y caen al 500 fail-closed del caso '_' de abajo, no a un 409/404 que sugeriría
            // una regla de negocio real.
            EntidadNoEncontradaException => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ReglaDeNegocioException      => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            ArgumentException            => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            UnauthorizedAccessException  => (StatusCodes.Status403Forbidden, "Prohibido."),
            // Binding fallido de Minimal API (ej. valor de query param que no matchea un enum):
            // input inválido del cliente, nunca un 500. Se respeta el StatusCode propio de la
            // excepción (normalmente 400, pero Kestrel puede usar variantes como 413/431).
            BadHttpRequestException ex   => (ex.StatusCode, "Solicitud inválida."),
            _                            => (StatusCodes.Status500InternalServerError, "Error interno."),
        };
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DomainExceptionHandlerTests`
Expected: PASS (todas)

- [ ] **Step 6: Correr toda la suite de `StockApp.Api.Tests` para verificar que Bloque A cierra en verde**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas — todos los `409`/`404` de negocio de los recursos de catálogo/usuarios siguen funcionando porque ahora vienen de `ReglaDeNegocioException`/`EntidadNoEncontradaException`, no de los tipos genéricos que se acaban de retirar del handler).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs
git commit -m "feat(api): DomainExceptionHandler ya no mapea InvalidOperationException/KeyNotFoundException genericas a 409/404"
```

---

## Bloque B — Contratos (D1, D2, D3)

## Task 11: `IUsuarioService.AltaUsuarioAsync` → `Task<int>` + `POST /usuarios` devuelve `201 { id }`

**Files:**
- Modify: `src/StockApp.Application/Auth/IUsuarioService.cs`
- Modify: `src/StockApp.Application/Auth/UsuarioService.cs`
- Modify: `src/StockApp.Api/Endpoints/UsuariosEndpoints.cs`
- Modify: `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs`
- Modify: `tests/StockApp.Api.Tests/UsuariosEndpointTests.cs`

**Interfaces:**
- Produces: `IUsuarioService.AltaUsuarioAsync(...): Task<int>` (antes `Task`) — consumido por `UsuariosEndpoints.cs` (este task) y por `PrimerArranqueViewModel.CrearSegundoAdminAsync` (sin cambios de código necesarios — ver Step 6).

**Mina 1 — call-sites afectados (verificados con `rg` antes de escribir este task):**
- `src/StockApp.Application/Auth/IUsuarioService.cs:8` — declaración de interfaz (este task).
- `src/StockApp.Application/Auth/UsuarioService.cs:35-37` — implementación (este task). Ya calcula `var id = await _repo.AgregarAsync(nuevo);` internamente — solo falta el `return id;` y el cambio de firma.
- `src/StockApp.Api/Endpoints/UsuariosEndpoints.cs:16-18` — `POST /usuarios` (este task).
- `src/StockApp.Presentation/ViewModels/PrimerArranqueViewModel.cs:168-172` — `CrearSegundoAdminAsync` llama `await _usuarioService.AltaUsuarioAsync(...)` **sin capturar el resultado** — compila sin cambios con `Task<int>` (un `await` de una `Task<int>` sin asignar el valor de retorno es válido en C#). No requiere edición.
- `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs` (líneas 51, 138, 242, 251, 259) — todas las llamadas son `await svc.AltaUsuarioAsync(...)` sin capturar el resultado; compilan sin cambios salvo el test que se actualiza en el Step 1 para verificar explícitamente el id devuelto.
- **Ningún mock** de `AltaUsuarioAsync` existe en `tests/StockApp.Presentation.Tests/` (confirmado con `rg`) — no hay `.Returns(Task.CompletedTask)` que migrar a `.ReturnsAsync(id)`.

- [ ] **Step 1: Actualizar el test existente en `UsuarioServiceTests.cs` para verificar el id devuelto**

Reemplazar (en `AltaUsuario_Admin_CreaConHashYEventoAuditoria`):

```csharp
    public async Task AltaUsuario_Admin_CreaConHashYEventoAuditoria()
    {
        var (svc, repo, hasher, session, _, audit) = Crear();

        await svc.AltaUsuarioAsync("operador2", "Nombre Completo", "pwd123", RolUsuario.Operador);

        hasher.Verify(h => h.Hash("pwd123"), Times.Once);
        repo.Verify(r => r.AgregarAsync(It.Is<Usuario>(u =>
            u.NombreUsuario == "operador2" &&
            u.HashContrasena == "$2a$12$hashed" &&
            u.Rol == RolUsuario.Operador &&
            u.Activo == true
        )), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaUsuario,
            "Usuario", It.IsAny<int>(), It.IsAny<string>()), Times.Once);
    }
```

por:

```csharp
    public async Task AltaUsuario_Admin_CreaConHashYEventoAuditoria_DevuelveId()
    {
        var (svc, repo, hasher, session, _, audit) = Crear();
        repo.Setup(r => r.AgregarAsync(It.IsAny<Usuario>())).ReturnsAsync(42);

        var id = await svc.AltaUsuarioAsync("operador2", "Nombre Completo", "pwd123", RolUsuario.Operador);

        Assert.Equal(42, id);
        hasher.Verify(h => h.Hash("pwd123"), Times.Once);
        repo.Verify(r => r.AgregarAsync(It.Is<Usuario>(u =>
            u.NombreUsuario == "operador2" &&
            u.HashContrasena == "$2a$12$hashed" &&
            u.Rol == RolUsuario.Operador &&
            u.Activo == true
        )), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaUsuario,
            "Usuario", 42, It.IsAny<string>()), Times.Once);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter AltaUsuario_Admin_CreaConHashYEventoAuditoria_DevuelveId`
Expected: FAIL — error de compilación (`AltaUsuarioAsync` devuelve `Task`, no se puede asignar a `id`).

- [ ] **Step 3: Cambiar la firma en `IUsuarioService.cs` y `UsuarioService.cs`**

En `src/StockApp.Application/Auth/IUsuarioService.cs`, reemplazar:

```csharp
    Task AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol);
```

por:

```csharp
    /// <summary>Crea un usuario nuevo y devuelve su Id (Fase 3a, D2).</summary>
    Task<int> AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol);
```

En `src/StockApp.Application/Auth/UsuarioService.cs`, reemplazar:

```csharp
    public async Task AltaUsuarioAsync(
        string nombreUsuario, string? nombreCompleto,
        string contrasenaPlan, RolUsuario rol)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        // Fix 6: validación mínima de contraseña
        ContrasenaValidator.Validar(contrasenaPlan);

        var nuevo = new Usuario
        {
            NombreUsuario  = nombreUsuario,
            NombreCompleto = nombreCompleto,
            HashContrasena = _hasher.Hash(contrasenaPlan),
            Rol            = rol,
            Activo         = true,
            FechaAlta      = DateTime.UtcNow
        };

        var id = await _repo.AgregarAsync(nuevo);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaUsuario,
            "Usuario", id,
            $"Alta de '{nombreUsuario}' con rol {rol}");
    }
```

por:

```csharp
    public async Task<int> AltaUsuarioAsync(
        string nombreUsuario, string? nombreCompleto,
        string contrasenaPlan, RolUsuario rol)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        // Fix 6: validación mínima de contraseña
        ContrasenaValidator.Validar(contrasenaPlan);

        var nuevo = new Usuario
        {
            NombreUsuario  = nombreUsuario,
            NombreCompleto = nombreCompleto,
            HashContrasena = _hasher.Hash(contrasenaPlan),
            Rol            = rol,
            Activo         = true,
            FechaAlta      = DateTime.UtcNow
        };

        var id = await _repo.AgregarAsync(nuevo);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaUsuario,
            "Usuario", id,
            $"Alta de '{nombreUsuario}' con rol {rol}");

        return id;
    }
```

- [ ] **Step 4: Correr los tests de Application y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter UsuarioServiceTests`
Expected: PASS (todas)

- [ ] **Step 5: Actualizar el test de API existente para verificar el body `{ id }`**

En `tests/StockApp.Api.Tests/UsuariosEndpointTests.cs`, reemplazar (test `PostUsuarios_ConTokenAdmin_CreaUsuarioYDevuelve201`):

```csharp
    [Fact]
    public async Task PostUsuarios_ConTokenAdmin_CreaUsuarioYDevuelve201()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("nuevo.usuario", "Nuevo Usuario", "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var ctx = Factory.CrearContexto();
        Assert.True(await ctx.Usuarios.AnyAsync(u => u.NombreUsuario == "nuevo.usuario"));
    }
```

por:

```csharp
    [Fact]
    public async Task PostUsuarios_ConTokenAdmin_CreaUsuarioYDevuelve201ConId()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("nuevo.usuario", "Nuevo Usuario", "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UsuarioCreadoResponse>();
        Assert.True(body!.Id > 0);

        await using var ctx = Factory.CrearContexto();
        var creado = await ctx.Usuarios.SingleAsync(u => u.NombreUsuario == "nuevo.usuario");
        Assert.Equal(body.Id, creado.Id);
    }
```

- [ ] **Step 6: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PostUsuarios_ConTokenAdmin_CreaUsuarioYDevuelve201ConId`
Expected: FAIL — error de compilación (`UsuarioCreadoResponse` no existe todavía) y/o el endpoint sigue devolviendo body `null`.

- [ ] **Step 7: Actualizar `UsuariosEndpoints.cs` — devolver `{ id }`**

En `src/StockApp.Api/Endpoints/UsuariosEndpoints.cs`, agregar el record de response (junto a los demás records del archivo):

```csharp
public record UsuarioCreadoResponse(int Id);
```

Reemplazar:

```csharp
        group.MapPost("/", async (CrearUsuarioRequest request, IUsuarioService usuarios) =>
        {
            await usuarios.AltaUsuarioAsync(
                request.NombreUsuario, request.NombreCompleto, request.ContrasenaPlan, request.Rol);
            return Results.Created("/usuarios", (object?)null);
        });
```

por:

```csharp
        group.MapPost("/", async (CrearUsuarioRequest request, IUsuarioService usuarios) =>
        {
            var id = await usuarios.AltaUsuarioAsync(
                request.NombreUsuario, request.NombreCompleto, request.ContrasenaPlan, request.Rol);
            return Results.Created($"/usuarios/{id}", new UsuarioCreadoResponse(id));
        });
```

- [ ] **Step 8: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UsuariosEndpointTests`
Expected: PASS (todas)

- [ ] **Step 9: Compilar `StockApp.Presentation` para confirmar que `PrimerArranqueViewModel` sigue compilando sin cambios**

Run: `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`
Expected: éxito — `await _usuarioService.AltaUsuarioAsync(...)` en `CrearSegundoAdminAsync` (línea 168) compila igual con `Task<int>` porque el resultado no se captura.

- [ ] **Step 10: Correr toda la suite del repo para verificar que nada más se rompió**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj && dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj && dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 11: Commit**

```bash
git add src/StockApp.Application/Auth/IUsuarioService.cs src/StockApp.Application/Auth/UsuarioService.cs src/StockApp.Api/Endpoints/UsuariosEndpoints.cs tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs tests/StockApp.Api.Tests/UsuariosEndpointTests.cs
git commit -m "feat(usuarios): AltaUsuarioAsync devuelve el id, POST /usuarios responde 201 con body"
```

---

## Task 12: `ModificarCategoriaRequest` sin `Id` (D1)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/CategoriasEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/CategoriasEndpointTests.cs`

**Interfaces:**
- Produces: `record ModificarCategoriaRequest(string Nombre)` (antes `(int Id, string Nombre)`). El `id` de ruta (`{id:int}`) pasa a ser la ÚNICA fuente del id — elimina el mismatch silencioso del review final (PUT /categorias/7 con body id=999 modificaba la 7 ignorando el 999).

- [ ] **Step 1: Actualizar el test existente en `CategoriasEndpointTests.cs`**

Reemplazar (en `PutCategorias_ConTokenAdmin_ModificaYDevuelve200`):

```csharp
        var response = await client.PutAsJsonAsync($"/categorias/{categoria.Id}", new ModificarCategoriaRequest(categoria.Id, "Modificada"));
```

por:

```csharp
        var response = await client.PutAsJsonAsync($"/categorias/{categoria.Id}", new ModificarCategoriaRequest("Modificada"));
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PutCategorias_ConTokenAdmin_ModificaYDevuelve200`
Expected: FAIL — error de compilación (`ModificarCategoriaRequest` todavía exige 2 argumentos).

- [ ] **Step 3: Quitar el campo `Id` de `ModificarCategoriaRequest`**

En `src/StockApp.Api/Endpoints/CategoriasEndpoints.cs`, reemplazar:

```csharp
public record ModificarCategoriaRequest(int Id, string Nombre);
```

por:

```csharp
public record ModificarCategoriaRequest(string Nombre);
```

El handler ya usaba el `id` de ruta (no `request.Id`) para construir la entidad — no requiere cambios:

```csharp
        group.MapPut("/{id:int}", async (int id, ModificarCategoriaRequest request, ICategoriaService categorias) =>
        {
            await categorias.ModificarAsync(new Categoria { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter CategoriasEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/CategoriasEndpoints.cs tests/StockApp.Api.Tests/CategoriasEndpointTests.cs
git commit -m "feat(api): quita Id del body de ModificarCategoriaRequest, el id de ruta es la unica fuente"
```

---

## Task 13: `ModificarProveedorRequest` sin `Id` (D1)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/ProveedoresEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/ProveedoresEndpointTests.cs`

**Interfaces:**
- Produces: `record ModificarProveedorRequest(string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas)` (antes con `int Id` primero).

- [ ] **Step 1: Actualizar el test existente en `ProveedoresEndpointTests.cs`**

Reemplazar (en `PutProveedores_ConTokenAdmin_ModificaYDevuelve200`):

```csharp
        var response = await client.PutAsJsonAsync($"/proveedores/{proveedor.Id}",
            new ModificarProveedorRequest(proveedor.Id, "Modificado", "011-9999", null, null, null));
```

por:

```csharp
        var response = await client.PutAsJsonAsync($"/proveedores/{proveedor.Id}",
            new ModificarProveedorRequest("Modificado", "011-9999", null, null, null));
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PutProveedores_ConTokenAdmin_ModificaYDevuelve200`
Expected: FAIL — error de compilación.

- [ ] **Step 3: Quitar el campo `Id` de `ModificarProveedorRequest`**

En `src/StockApp.Api/Endpoints/ProveedoresEndpoints.cs`, reemplazar:

```csharp
public record ModificarProveedorRequest(int Id, string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);
```

por:

```csharp
public record ModificarProveedorRequest(string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);
```

El handler ya usaba el `id` de ruta — no requiere cambios (sigue construyendo `new Proveedor { Id = id, Nombre = request.Nombre, ... }`).

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProveedoresEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/ProveedoresEndpoints.cs tests/StockApp.Api.Tests/ProveedoresEndpointTests.cs
git commit -m "feat(api): quita Id del body de ModificarProveedorRequest, el id de ruta es la unica fuente"
```

---

## Task 14: `ModificarUnidadMedidaRequest` sin `Id` (D1)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/UnidadesMedidaEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/UnidadesMedidaEndpointTests.cs`

**Interfaces:**
- Produces: `record ModificarUnidadMedidaRequest(string Nombre, string Abreviatura)` (antes con `int Id` primero).

- [ ] **Step 1: Actualizar el test existente en `UnidadesMedidaEndpointTests.cs`**

Reemplazar (en `PutUnidadesMedida_ConTokenAdmin_ModificaYDevuelve200`):

```csharp
        var response = await client.PutAsJsonAsync($"/unidades-medida/{unidad.Id}",
            new ModificarUnidadMedidaRequest(unidad.Id, "Modificada", "mo"));
```

por:

```csharp
        var response = await client.PutAsJsonAsync($"/unidades-medida/{unidad.Id}",
            new ModificarUnidadMedidaRequest("Modificada", "mo"));
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PutUnidadesMedida_ConTokenAdmin_ModificaYDevuelve200`
Expected: FAIL — error de compilación.

- [ ] **Step 3: Quitar el campo `Id` de `ModificarUnidadMedidaRequest`**

En `src/StockApp.Api/Endpoints/UnidadesMedidaEndpoints.cs`, reemplazar:

```csharp
public record ModificarUnidadMedidaRequest(int Id, string Nombre, string Abreviatura);
```

por:

```csharp
public record ModificarUnidadMedidaRequest(string Nombre, string Abreviatura);
```

El handler ya usaba el `id` de ruta — no requiere cambios.

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UnidadesMedidaEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/UnidadesMedidaEndpoints.cs tests/StockApp.Api.Tests/UnidadesMedidaEndpointTests.cs
git commit -m "feat(api): quita Id del body de ModificarUnidadMedidaRequest, el id de ruta es la unica fuente"
```

---

## Task 15: `ModificarProductoRequest` sin `Id` (D1)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/ProductosEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/ProductosEndpointTests.cs`

**Interfaces:**
- Produces: `record ModificarProductoRequest(string Codigo, string? CodigoBarras, string Nombre, string? Descripcion, int? CategoriaId, int? ProveedorId, int UnidadMedidaId, decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo)` (antes con `int Id` primero).

- [ ] **Step 1: Actualizar el test existente en `ProductosEndpointTests.cs`**

Reemplazar (en `PutProductos_ConTokenAdmin_ModificaYDevuelve200`):

```csharp
        var response = await client.PutAsJsonAsync($"/productos/{producto.Id}", new ModificarProductoRequest(
            producto.Id, producto.Codigo, null, "Nombre Modificado", null, null, null, producto.UnidadMedidaId, 10m, 20m, 0m));
```

por:

```csharp
        var response = await client.PutAsJsonAsync($"/productos/{producto.Id}", new ModificarProductoRequest(
            producto.Codigo, null, "Nombre Modificado", null, null, null, producto.UnidadMedidaId, 10m, 20m, 0m));
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PutProductos_ConTokenAdmin_ModificaYDevuelve200`
Expected: FAIL — error de compilación.

- [ ] **Step 3: Quitar el campo `Id` de `ModificarProductoRequest`**

En `src/StockApp.Api/Endpoints/ProductosEndpoints.cs`, reemplazar:

```csharp
public record ModificarProductoRequest(
    int Id, string Codigo, string? CodigoBarras, string Nombre, string? Descripcion,
    int? CategoriaId, int? ProveedorId, int UnidadMedidaId,
    decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo);
```

por:

```csharp
public record ModificarProductoRequest(
    string Codigo, string? CodigoBarras, string Nombre, string? Descripcion,
    int? CategoriaId, int? ProveedorId, int UnidadMedidaId,
    decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo);
```

El handler ya usaba el `id` de ruta — no requiere cambios.

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProductosEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/ProductosEndpoints.cs tests/StockApp.Api.Tests/ProductosEndpointTests.cs
git commit -m "feat(api): quita Id del body de ModificarProductoRequest, el id de ruta es la unica fuente"
```

---

## Task 16: `CategoriaDto` en las responses de `GET /categorias` y `GET /categorias/activas` (D3)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/CategoriasEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/CategoriasEndpointTests.cs`

**Interfaces:**
- Produces: `record CategoriaDto(int Id, string Nombre, bool Activo)` — público en `StockApp.Api.Endpoints`. `GET /categorias` y `GET /categorias/activas` devuelven `IReadOnlyList<CategoriaDto>` en vez de la entidad `Categoria` cruda de `StockApp.Domain.Entities`. El mapeo vive en el endpoint (la API es un adaptador HTTP; `ICategoriaService` no se toca — sigue devolviendo `Categoria` para el desktop).

- [ ] **Step 1: Actualizar los tests existentes en `CategoriasEndpointTests.cs`**

Reemplazar (en `GetCategorias_ConTokenAdmin_Devuelve200`):

```csharp
        var response = await client.GetAsync("/categorias");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categorias = await response.Content.ReadFromJsonAsync<List<Categoria>>();
        Assert.Contains(categorias!, c => c.Nombre == "Bebidas");
```

por:

```csharp
        var response = await client.GetAsync("/categorias");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categorias = await response.Content.ReadFromJsonAsync<List<CategoriaDto>>();
        Assert.Contains(categorias!, c => c.Nombre == "Bebidas");
```

Reemplazar (en `GetCategoriasActivas_ConTokenOperador_Devuelve200`):

```csharp
        var response = await client.GetAsync("/categorias/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categorias = await response.Content.ReadFromJsonAsync<List<Categoria>>();
        Assert.Contains(categorias!, c => c.Nombre == "Activa");
        Assert.DoesNotContain(categorias!, c => c.Nombre == "Inactiva");
```

por:

```csharp
        var response = await client.GetAsync("/categorias/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categorias = await response.Content.ReadFromJsonAsync<List<CategoriaDto>>();
        Assert.Contains(categorias!, c => c.Nombre == "Activa");
        Assert.DoesNotContain(categorias!, c => c.Nombre == "Inactiva");
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "GetCategorias_ConTokenAdmin_Devuelve200|GetCategoriasActivas_ConTokenOperador_Devuelve200"`
Expected: FAIL — error de compilación (`CategoriaDto` no existe todavía).

- [ ] **Step 3: Agregar `CategoriaDto` y mapear los dos `GET` en `CategoriasEndpoints.cs`**

Agregar el record (junto a los demás records del archivo):

```csharp
/// <summary>
/// DTO de lectura de Categoria (Fase 3a, D3). Reemplaza la entidad de dominio cruda en las
/// responses de GET: una nav property futura en Categoria ya no puede cambiar el contrato
/// HTTP silenciosamente.
/// </summary>
public record CategoriaDto(int Id, string Nombre, bool Activo);
```

Agregar el helper de mapeo privado dentro de `MapCategoriasEndpoints` (antes del `return app;`):

Reemplazar:

```csharp
        group.MapGet("/", async (ICategoriaService categorias) =>
            Results.Ok(await categorias.ListarTodasAsync()))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);
```

por:

```csharp
        group.MapGet("/", async (ICategoriaService categorias) =>
            Results.Ok((await categorias.ListarTodasAsync()).Select(ACategoriaDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);
```

Reemplazar:

```csharp
        group.MapGet("/activas", async (ICategoriaService categorias) =>
            Results.Ok(await categorias.ListarActivasAsync()))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }
}
```

por:

```csharp
        group.MapGet("/activas", async (ICategoriaService categorias) =>
            Results.Ok((await categorias.ListarActivasAsync()).Select(ACategoriaDto)))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }

    private static CategoriaDto ACategoriaDto(Categoria c) => new(c.Id, c.Nombre, c.Activo);
}
```

Agregar `using System.Linq;` al principio del archivo:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter CategoriasEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/CategoriasEndpoints.cs tests/StockApp.Api.Tests/CategoriasEndpointTests.cs
git commit -m "feat(api): GET /categorias y /categorias/activas devuelven CategoriaDto"
```

---

## Task 17: `ProveedorDto` en la response de `GET /proveedores` (D3)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/ProveedoresEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/ProveedoresEndpointTests.cs`

**Interfaces:**
- Produces: `record ProveedorDto(int Id, string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas, bool Activo)` — público en `StockApp.Api.Endpoints`. `GET /proveedores` devuelve `IReadOnlyList<ProveedorDto>` en vez de `Proveedor` crudo.

- [ ] **Step 1: Actualizar el test existente en `ProveedoresEndpointTests.cs`**

Reemplazar (en `GetProveedores_ConTokenAdmin_Devuelve200`):

```csharp
        var response = await client.GetAsync("/proveedores");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proveedores = await response.Content.ReadFromJsonAsync<List<Proveedor>>();
        Assert.Contains(proveedores!, p => p.Nombre == "Proveedor Uno");
```

por:

```csharp
        var response = await client.GetAsync("/proveedores");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proveedores = await response.Content.ReadFromJsonAsync<List<ProveedorDto>>();
        Assert.Contains(proveedores!, p => p.Nombre == "Proveedor Uno");
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter GetProveedores_ConTokenAdmin_Devuelve200`
Expected: FAIL — error de compilación (`ProveedorDto` no existe todavía).

- [ ] **Step 3: Agregar `ProveedorDto` y mapear el `GET` en `ProveedoresEndpoints.cs`**

Agregar el record:

```csharp
/// <summary>DTO de lectura de Proveedor (Fase 3a, D3). Reemplaza la entidad de dominio cruda.</summary>
public record ProveedorDto(
    int Id, string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas, bool Activo);
```

Reemplazar:

```csharp
        group.MapGet("/", async (IProveedorService proveedores) =>
            Results.Ok(await proveedores.ListarTodosAsync()));
```

por:

```csharp
        group.MapGet("/", async (IProveedorService proveedores) =>
            Results.Ok((await proveedores.ListarTodosAsync()).Select(AProveedorDto)));
```

Agregar el helper de mapeo antes del `return app;` de cierre y ajustar el cierre de la clase:

```csharp
        return app;
    }

    private static ProveedorDto AProveedorDto(Proveedor p) =>
        new(p.Id, p.Nombre, p.Telefono, p.Email, p.Direccion, p.Notas, p.Activo);
}
```

Agregar `using System.Linq;` al principio del archivo.

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProveedoresEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/ProveedoresEndpoints.cs tests/StockApp.Api.Tests/ProveedoresEndpointTests.cs
git commit -m "feat(api): GET /proveedores devuelve ProveedorDto"
```

---

## Task 18: `UnidadMedidaDto` en las responses de `GET /unidades-medida` y `GET /unidades-medida/activas` (D3)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/UnidadesMedidaEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/UnidadesMedidaEndpointTests.cs`

**Interfaces:**
- Produces: `record UnidadMedidaDto(int Id, string Nombre, string Abreviatura, bool Activo)` — público en `StockApp.Api.Endpoints`, consumido también por `POST /unidades-medida/garantizar-por-defecto` (Task 20).

- [ ] **Step 1: Actualizar los tests existentes en `UnidadesMedidaEndpointTests.cs`**

Reemplazar (en `GetUnidadesMedida_ConTokenAdmin_Devuelve200`):

```csharp
        var response = await client.GetAsync("/unidades-medida");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unidades = await response.Content.ReadFromJsonAsync<List<UnidadMedida>>();
        Assert.Contains(unidades!, u => u.Nombre == "Kilo");
```

por:

```csharp
        var response = await client.GetAsync("/unidades-medida");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unidades = await response.Content.ReadFromJsonAsync<List<UnidadMedidaDto>>();
        Assert.Contains(unidades!, u => u.Nombre == "Kilo");
```

Reemplazar (en `GetUnidadesMedidaActivas_ConTokenOperador_Devuelve200`):

```csharp
        var response = await client.GetAsync("/unidades-medida/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unidades = await response.Content.ReadFromJsonAsync<List<UnidadMedida>>();
        Assert.Contains(unidades!, u => u.Nombre == "Activa");
        Assert.DoesNotContain(unidades!, u => u.Nombre == "Inactiva");
```

por:

```csharp
        var response = await client.GetAsync("/unidades-medida/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unidades = await response.Content.ReadFromJsonAsync<List<UnidadMedidaDto>>();
        Assert.Contains(unidades!, u => u.Nombre == "Activa");
        Assert.DoesNotContain(unidades!, u => u.Nombre == "Inactiva");
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "GetUnidadesMedida_ConTokenAdmin_Devuelve200|GetUnidadesMedidaActivas_ConTokenOperador_Devuelve200"`
Expected: FAIL — error de compilación (`UnidadMedidaDto` no existe todavía).

- [ ] **Step 3: Agregar `UnidadMedidaDto` y mapear los dos `GET` en `UnidadesMedidaEndpoints.cs`**

Agregar el record:

```csharp
/// <summary>DTO de lectura de UnidadMedida (Fase 3a, D3). Reemplaza la entidad de dominio cruda.</summary>
public record UnidadMedidaDto(int Id, string Nombre, string Abreviatura, bool Activo);
```

Reemplazar:

```csharp
        group.MapGet("/", async (IUnidadMedidaService unidades) =>
            Results.Ok(await unidades.ListarTodasAsync()))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);
```

por:

```csharp
        group.MapGet("/", async (IUnidadMedidaService unidades) =>
            Results.Ok((await unidades.ListarTodasAsync()).Select(AUnidadMedidaDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);
```

Reemplazar:

```csharp
        group.MapGet("/activas", async (IUnidadMedidaService unidades) =>
            Results.Ok(await unidades.ListarActivasAsync()))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }
}
```

por:

```csharp
        group.MapGet("/activas", async (IUnidadMedidaService unidades) =>
            Results.Ok((await unidades.ListarActivasAsync()).Select(AUnidadMedidaDto)))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }

    private static UnidadMedidaDto AUnidadMedidaDto(UnidadMedida u) =>
        new(u.Id, u.Nombre, u.Abreviatura, u.Activo);
}
```

Agregar `using System.Linq;` al principio del archivo.

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UnidadesMedidaEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/UnidadesMedidaEndpoints.cs tests/StockApp.Api.Tests/UnidadesMedidaEndpointTests.cs
git commit -m "feat(api): GET /unidades-medida y /unidades-medida/activas devuelven UnidadMedidaDto"
```

---

## Bloque C — Superficie nueva (D5-D8)

## Task 19: `GET /productos` con `sku`/`codigoBarras`/`nombre`/`texto` (D5)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/ProductosEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/ProductosEndpointTests.cs`

**Interfaces:**
- Consumes: `IProductoService.BuscarAsync(string? sku, string? codigoBarras, string? nombre): Task<IReadOnlyList<ProductoDto>>` y `.BuscarPorTextoAsync(string? texto): Task<IReadOnlyList<ProductoDto>>` (ya existentes, `StockApp.Application.Catalogo`).
- Produces: `GET /productos?texto=&sku=&codigoBarras=&nombre=` — si `texto` viene provisto (no-null) → `BuscarPorTextoAsync(texto)`; si no → `BuscarAsync(sku, codigoBarras, nombre)` (los tres nullable; todos null = listar todo).

- [ ] **Step 1: Agregar los tests que fallan a `ProductosEndpointTests.cs`**

Agregar, al final de la clase (antes del `}` de cierre):

```csharp

    // ── GET /productos con sku/codigoBarras/nombre (Fase 3a, D5) ────────────

    [Fact]
    public async Task GetProductos_ConSku_FiltraPorSku()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-F1", "Producto Sku Uno");
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-F2", "Producto Sku Dos");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos?sku=SKU-F1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var productos = await response.Content.ReadFromJsonAsync<List<ProductoDto>>();
        Assert.Single(productos!);
        Assert.Equal("SKU-F1", productos![0].Codigo);
    }

    [Fact]
    public async Task GetProductos_ConCodigoBarras_FiltraPorCodigoBarras()
    {
        await using var ctx = Factory.CrearContexto();
        var unidad = new UnidadMedida { Nombre = "Unidad CB", Abreviatura = "ucb", Activo = true };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        ctx.Productos.Add(new Producto
        {
            Codigo = "SKU-CB1", CodigoBarras = "7791234500001", Nombre = "Producto Con Barras",
            UnidadMedidaId = unidad.Id, PrecioCosto = 1m, PrecioVenta = 2m,
            StockActual = 0m, StockMinimo = 0m, Activo = true, FechaAlta = DateTime.UtcNow,
        });
        ctx.Productos.Add(new Producto
        {
            Codigo = "SKU-CB2", CodigoBarras = "7791234500002", Nombre = "Otro Producto",
            UnidadMedidaId = unidad.Id, PrecioCosto = 1m, PrecioVenta = 2m,
            StockActual = 0m, StockMinimo = 0m, Activo = true, FechaAlta = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos?codigoBarras=7791234500001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var productos = await response.Content.ReadFromJsonAsync<List<ProductoDto>>();
        Assert.Single(productos!);
        Assert.Equal("SKU-CB1", productos![0].Codigo);
    }

    [Fact]
    public async Task GetProductos_ConNombre_FiltraPorNombre()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-N1", "Manzana Roja");
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-N2", "Pera Verde");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos?nombre=Manzana");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var productos = await response.Content.ReadFromJsonAsync<List<ProductoDto>>();
        Assert.Single(productos!);
        Assert.Equal("SKU-N1", productos![0].Codigo);
    }

    [Fact]
    public async Task GetProductos_SinFiltros_ListaTodosLosProductos()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-T3", "Producto Sin Filtro Uno");
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-T4", "Producto Sin Filtro Dos");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var productos = await response.Content.ReadFromJsonAsync<List<ProductoDto>>();
        Assert.Contains(productos!, p => p.Codigo == "SKU-T3");
        Assert.Contains(productos!, p => p.Codigo == "SKU-T4");
    }
```

Agregar `using System;` al principio del archivo si no está (necesario para `DateTime.UtcNow` en el nuevo test — confirmar, ya debería estar implícito por `global using` del proyecto; si el build falla por esto, agregarlo).

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "GetProductos_ConSku_FiltraPorSku|GetProductos_ConCodigoBarras_FiltraPorCodigoBarras|GetProductos_ConNombre_FiltraPorNombre"`
Expected: FAIL — error de compilación (`sku`/`codigoBarras`/`nombre` no son query params reconocidos todavía) o 404/comportamiento incorrecto.

- [ ] **Step 3: Extender `GET /productos` en `ProductosEndpoints.cs`**

Reemplazar:

```csharp
        group.MapGet("/", async (string? texto, IProductoService productos) =>
            Results.Ok(await productos.BuscarPorTextoAsync(texto)))
            .RequireAuthorization(Permisos.GestionarProductos);
```

por:

```csharp
        group.MapGet("/", async (
            string? texto, string? sku, string? codigoBarras, string? nombre, IProductoService productos) =>
        {
            var resultado = texto is not null
                ? await productos.BuscarPorTextoAsync(texto)
                : await productos.BuscarAsync(sku, codigoBarras, nombre);
            return Results.Ok(resultado);
        })
        .RequireAuthorization(Permisos.GestionarProductos);
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProductosEndpointTests`
Expected: PASS (todas — incluye `GetProductos_ConTexto_FiltraPorTexto` y `GetProductos_ConTokenAdmin_Devuelve200ConProductosSeedeados`, que siguen verdes porque el branch de `texto` no cambió y "todos null" sigue listando todo vía `BuscarAsync(null, null, null)`).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/ProductosEndpoints.cs tests/StockApp.Api.Tests/ProductosEndpointTests.cs
git commit -m "feat(api): GET /productos soporta filtros sku/codigoBarras/nombre ademas de texto"
```

---

## Task 20: `POST /unidades-medida/garantizar-por-defecto` (D6)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/UnidadesMedidaEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/UnidadesMedidaEndpointTests.cs`

**Interfaces:**
- Consumes: `IUnidadMedidaService.GarantizarUnidadPorDefectoAsync(): Task<UnidadMedida>` (existente), `UnidadMedidaDto` + `AUnidadMedidaDto` (Task 18).
- Produces: `POST /unidades-medida/garantizar-por-defecto` → `200 OK` con `UnidadMedidaDto` de la unidad garantizada (idempotente). Política `GestionarProductos` (la que verifica el servicio internamente).

- [ ] **Step 1: Agregar los tests que fallan a `UnidadesMedidaEndpointTests.cs`**

Agregar, al final de la clase (antes del `}` de cierre):

```csharp

    // ── POST /unidades-medida/garantizar-por-defecto (Fase 3a, D6) ──────────

    [Fact]
    public async Task PostGarantizarPorDefecto_SinUnidadPrevia_CreaLaUnidadPorDefecto()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsync("/unidades-medida/garantizar-por-defecto", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<UnidadMedidaDto>();
        Assert.Equal("Unidad", dto!.Nombre);

        await using var verificacion = Factory.CrearContexto();
        Assert.Equal(1, await verificacion.UnidadesMedida.CountAsync(u => u.Nombre == "Unidad"));
    }

    [Fact]
    public async Task PostGarantizarPorDefecto_LlamadoDosVeces_NoDuplica()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var r1 = await client.PostAsync("/unidades-medida/garantizar-por-defecto", content: null);
        var r2 = await client.PostAsync("/unidades-medida/garantizar-por-defecto", content: null);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var dto1 = await r1.Content.ReadFromJsonAsync<UnidadMedidaDto>();
        var dto2 = await r2.Content.ReadFromJsonAsync<UnidadMedidaDto>();
        Assert.Equal(dto1!.Id, dto2!.Id);

        await using var verificacion = Factory.CrearContexto();
        Assert.Equal(1, await verificacion.UnidadesMedida.CountAsync(u => u.Nombre == "Unidad"));
    }

    [Fact]
    public async Task PostGarantizarPorDefecto_ConTokenOperador_Devuelve200()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsync("/unidades-medida/garantizar-por-defecto", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
```

Agregar `using Microsoft.EntityFrameworkCore;` al principio del archivo si no está (necesario para `.CountAsync` — ya debería estar presente, confirmar).

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "PostGarantizarPorDefecto_SinUnidadPrevia_CreaLaUnidadPorDefecto|PostGarantizarPorDefecto_LlamadoDosVeces_NoDuplica|PostGarantizarPorDefecto_ConTokenOperador_Devuelve200"`
Expected: FAIL — 404 (la ruta no existe todavía).

- [ ] **Step 3: Agregar el endpoint en `UnidadesMedidaEndpoints.cs`**

Agregar, dentro de `MapUnidadesMedidaEndpoints`, después del `group.MapGet("/activas", ...)`:

```csharp
        group.MapPost("/garantizar-por-defecto", async (IUnidadMedidaService unidades) =>
            Results.Ok(AUnidadMedidaDto(await unidades.GarantizarUnidadPorDefectoAsync())))
            .RequireAuthorization(Permisos.GestionarProductos);
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UnidadesMedidaEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/UnidadesMedidaEndpoints.cs tests/StockApp.Api.Tests/UnidadesMedidaEndpointTests.cs
git commit -m "feat(api): agrega POST /unidades-medida/garantizar-por-defecto"
```

---

## Task 21: Bootstrap de primer arranque — `GET /auth/primer-arranque` + `POST /auth/primer-admin` (D7)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Create: `tests/StockApp.Api.Tests/PrimerArranqueEndpointTests.cs`

**Interfaces:**
- Consumes: `IPrimerArranqueService.RequiereCrearAdminAsync(): Task<bool>`, `.CrearAdminInicialAsync(string, string): Task` (existente, `StockApp.Application.Auth`, ya migrado a `ReglaDeNegocioException` en Task 8).
- Produces: `GET /auth/primer-arranque` (anónimo) → `{ requiereCrearAdmin: bool }`. `POST /auth/primer-admin` (anónimo) → `201` si crea; `409` si ya hay usuarios (vía `DomainExceptionHandler`, sin código nuevo).

- [ ] **Step 1: Escribir los tests que fallan — `PrimerArranqueEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class PrimerArranqueEndpointTests : ApiTestBase
{
    public PrimerArranqueEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetPrimerArranque_ServidorVirgen_RequiereCrearAdminTrue()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/auth/primer-arranque");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PrimerArranqueEstadoResponse>();
        Assert.True(body!.RequiereCrearAdmin);
    }

    [Fact]
    public async Task PostPrimerAdmin_ServidorVirgen_CreaAdminYDevuelve201()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("admin.inicial", "secreto123"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Usuarios.AnyAsync(
            u => u.NombreUsuario == "admin.inicial" && u.Rol == RolUsuario.Admin));
    }

    [Fact]
    public async Task FlujoCompleto_RequiereAntes_CreaAdmin_YaNoRequiereDespues()
    {
        var client = Factory.CreateClient();

        var antes = await client.GetAsync("/auth/primer-arranque");
        var antesBody = await antes.Content.ReadFromJsonAsync<PrimerArranqueEstadoResponse>();
        Assert.True(antesBody!.RequiereCrearAdmin);

        await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("admin.flujo", "secreto123"));

        var despues = await client.GetAsync("/auth/primer-arranque");
        var despuesBody = await despues.Content.ReadFromJsonAsync<PrimerArranqueEstadoResponse>();
        Assert.False(despuesBody!.RequiereCrearAdmin);
    }

    [Fact]
    public async Task PostPrimerAdmin_LlamadoDeNuevo_Devuelve409()
    {
        var client = Factory.CreateClient();

        await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("admin.uno", "secreto123"));

        var segundo = await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("admin.dos", "secreto123"));

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
    }

    [Fact]
    public async Task PostPrimerAdmin_ConUsuariosExistentes_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.previo", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("otro.admin", "secreto123"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
```

(`ApiTestBase.LimpiarTablas()` hace `TRUNCATE` de `Usuarios` antes de cada test — el "servidor virgen" de estos tests se logra sin setup adicional.)

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PrimerArranqueEndpointTests`
Expected: FAIL — error de compilación (`PrimerArranqueEstadoResponse`/`CrearAdminInicialRequest` no existen) y/o 404 (las rutas no existen).

- [ ] **Step 3: Agregar los records y endpoints en `AuthEndpoints.cs`**

Agregar `using StockApp.Application.Auth;` al principio del archivo:

```csharp
using StockApp.Api.Auth;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
```

Agregar los records (junto a `LoginRequest`/`LoginResponse`):

```csharp
public record PrimerArranqueEstadoResponse(bool RequiereCrearAdmin);
public record CrearAdminInicialRequest(string NombreUsuario, string Contrasena);
```

Agregar, dentro de `MapAuthEndpoints`, después del `group.MapPost("/login", ...)`:

```csharp
        group.MapGet("/primer-arranque", async (IPrimerArranqueService primerArranque) =>
        {
            var requiere = await primerArranque.RequiereCrearAdminAsync();
            return Results.Ok(new PrimerArranqueEstadoResponse(requiere));
        });

        group.MapPost("/primer-admin", async (
            CrearAdminInicialRequest request, IPrimerArranqueService primerArranque) =>
        {
            await primerArranque.CrearAdminInicialAsync(request.NombreUsuario, request.Contrasena);
            return Results.Created("/auth/primer-arranque", (object?)null);
        });
```

(Ninguno de los dos lleva `.RequireAuthorization(...)` — igual que `POST /auth/login`, quedan anónimos por ausencia de política, no por una llamada explícita a `AllowAnonymous()`, siguiendo la convención ya establecida en este archivo.)

- [ ] **Step 4: Registrar `IPrimerArranqueService` en el DI de `Program.cs`**

En `src/StockApp.Api/Program.cs`, agregar, junto al bloque de DI de usuarios:

```csharp
// Usuarios — ABM completo vía API (Fase 2b). IUsuarioRepository y IPasswordHasher
// ya están registrados desde Fase 2a (usados por AuthEndpoints).
builder.Services.AddScoped<IUsuarioService, UsuarioService>();

// Bootstrap de primer arranque (Fase 3a, D7) — reusa IUsuarioRepository/IPasswordHasher.
builder.Services.AddScoped<IPrimerArranqueService, PrimerArranqueService>();
```

`using StockApp.Application.Auth;` ya está presente en `Program.cs` (lo usa `UsuarioService`).

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PrimerArranqueEndpointTests`
Expected: PASS (todas — el 409 de `PostPrimerAdmin_LlamadoDeNuevo_Devuelve409`/`PostPrimerAdmin_ConUsuariosExistentes_Devuelve409` funciona porque `PrimerArranqueService` ya lanza `ReglaDeNegocioException` desde Task 8, y `DomainExceptionHandler` ya la mapea a 409 desde Task 2 — sin código nuevo en el handler).

- [ ] **Step 6: Correr toda la suite de `StockApp.Api.Tests` para verificar que nada se rompió**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/Endpoints/AuthEndpoints.cs src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/PrimerArranqueEndpointTests.cs
git commit -m "feat(api): agrega bootstrap de primer arranque GET/POST /auth/primer-arranque y /auth/primer-admin"
```

---

## Task 22: `LoginResponse` enriquecido con datos del usuario (D8)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/AuthEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/LoginEndpointTests.cs`

**Interfaces:**
- Produces: `record UsuarioLoginResponse(int Id, string NombreUsuario, string? NombreCompleto, RolUsuario Rol)`, `record LoginResponse(string Token, UsuarioLoginResponse Usuario)` (antes `LoginResponse(string Token)`).

- [ ] **Step 1: Actualizar el test existente en `LoginEndpointTests.cs`**

Reemplazar:

```csharp
    [Fact]
    public async Task Login_ConCredencialesValidas_Devuelve200ConToken()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("admin.test", "Secreta123!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
    }
```

por:

```csharp
    [Fact]
    public async Task Login_ConCredencialesValidas_Devuelve200ConTokenYUsuario()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("admin.test", "Secreta123!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal("admin.test", body.Usuario.NombreUsuario);
        Assert.Equal(RolUsuario.Admin, body.Usuario.Rol);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter Login_ConCredencialesValidas_Devuelve200ConTokenYUsuario`
Expected: FAIL — error de compilación (`LoginResponse` no tiene `Usuario` todavía).

- [ ] **Step 3: Enriquecer `LoginResponse` en `AuthEndpoints.cs`**

Agregar `using StockApp.Domain.Enums;` al principio del archivo:

```csharp
using StockApp.Api.Auth;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
```

Reemplazar:

```csharp
public record LoginRequest(string? NombreUsuario, string? Contrasena);
public record LoginResponse(string Token);
```

por:

```csharp
public record LoginRequest(string? NombreUsuario, string? Contrasena);
public record UsuarioLoginResponse(int Id, string NombreUsuario, string? NombreCompleto, RolUsuario Rol);
public record LoginResponse(string Token, UsuarioLoginResponse Usuario);
```

Reemplazar:

```csharp
            var token = jwtTokenService.GenerarToken(usuario.Id, usuario.Rol);
            return Results.Ok(new LoginResponse(token));
```

por:

```csharp
            var token = jwtTokenService.GenerarToken(usuario.Id, usuario.Rol);
            var usuarioResponse = new UsuarioLoginResponse(
                usuario.Id, usuario.NombreUsuario, usuario.NombreCompleto, usuario.Rol);
            return Results.Ok(new LoginResponse(token, usuarioResponse));
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter LoginEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Correr toda la suite de `StockApp.Api.Tests` para verificar que nada más dependía del shape viejo**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/Endpoints/AuthEndpoints.cs tests/StockApp.Api.Tests/LoginEndpointTests.cs
git commit -m "feat(api): LoginResponse incluye datos del usuario autenticado"
```

---

## Bloque D — Infra y cierre (D9, D10, limpieza)

## Task 23: La API migra la base al arrancar (D9)

**Files:**
- Modify: `src/StockApp.Api/Program.cs`
- Create: `tests/StockApp.Api.Tests/ApiStartupMigrationTests.cs`

**Interfaces:**
- Produces: al arrancar, la API ejecuta `AppDbContext.Database.MigrateAsync()` (equivalente del `DatabaseInitializer` del desktop). Ningún endpoint ni servicio cambia de firma.

**Nota de diseño — por qué el test NO usa `ApiFactory`:** `ApiFactory.InitializeAsync()` (`tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs:12-16`) ya migra el contenedor de Testcontainers ANTES de levantar el host, así que cualquier test que use `ApiFactory` no puede demostrar que `Program.cs` migra por sí mismo (el `MigrateAsync` de `Program.cs` sería un no-op silencioso sobre una base ya migrada, y la ausencia del código bajo prueba pasaría desapercibida). Este task usa un `WebApplicationFactory<Program>` crudo, propio, contra un Testcontainer que NO se migra externamente — así la única forma de que la tabla `Usuarios` exista es que `Program.cs` la migre.

- [ ] **Step 1: Escribir el test que falla — `ApiStartupMigrationTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using StockApp.Api.Endpoints;
using Testcontainers.PostgreSql;
using Xunit;

namespace StockApp.Api.Tests;

/// <summary>
/// Prueba D9 (Fase 3a): la API migra la base por su cuenta al arrancar, sin depender de
/// que algo externo (DatabaseInitializer del desktop, StockApp.Seeder, o el propio
/// ApiFactory de los demás tests) haya migrado antes. Arma su propio WebApplicationFactory
/// contra un Postgres de Testcontainers SIN migrar — la única forma de que /auth/login
/// funcione es que Program.cs migre por sí mismo.
/// </summary>
public class ApiStartupMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _container.GetConnectionString(),
                    ["Jwt:Secret"] = "clave-de-prueba-de-al-menos-32-caracteres-arranque",
                });
            });
        });

        var client = _factory.CreateClient();

        // Login contra una BD sin usuarios: si la tabla Usuarios no existe (Program.cs no
        // migró), esto tira 500 por la excepción de Npgsql ("relation Usuarios does not
        // exist"). Con la migración automática, la tabla existe y el resultado es 401
        // (credenciales inválidas) — comportamiento correcto de negocio, no un error de infra.
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("nadie", "nada"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests`
Expected: FAIL — `500 Internal Server Error` (la tabla `Usuarios` no existe; `AppDbContext` nunca migra hoy en `Program.cs`).

- [ ] **Step 3: Agregar `MigrateAsync` al arranque de `Program.cs`**

Reemplazar:

```csharp
// Fail-fast de configuración al arrancar el host (post-Build, ya con la configuración
// final —incluidos los overrides de test de ApiFactory—, no con la snapshot pre-Build).
app.Services.GetRequiredService<JwtOptions>();
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>();
}
```

por:

```csharp
// Fail-fast de configuración al arrancar el host (post-Build, ya con la configuración
// final —incluidos los overrides de test de ApiFactory—, no con la snapshot pre-Build).
app.Services.GetRequiredService<JwtOptions>();

// Migración automática al arranque (Fase 3a, D9): reemplaza al DatabaseInitializer del
// desktop, que se elimina en Fase 3b. MigrateAsync es idempotente — no-op si no hay
// migraciones pendientes, así que no colisiona con ApiFactory (que ya migra su contenedor
// de Testcontainers en InitializeAsync, antes de que el host arranque).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests`
Expected: PASS

- [ ] **Step 5: Correr toda la suite de `StockApp.Api.Tests` para confirmar que no colisiona con `ApiFactory`**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas — la migración de `Program.cs` es un no-op sobre las bases que `ApiFactory`/`ApiStartupMigrationTests` ya migraron).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/ApiStartupMigrationTests.cs
git commit -m "feat(api): la API migra la base de datos al arrancar"
```

---

## Task 24: `Jwt:ExpiracionHoras` configurable, default 12h (D10)

**Files:**
- Create: `src/StockApp.Api/Auth/JwtOptionsFactory.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Create: `tests/StockApp.Api.Tests/Auth/JwtOptionsFactoryTests.cs`

**Interfaces:**
- Produces: `JwtOptionsFactory.Crear(IConfiguration configuration): JwtOptions` — lee `Jwt:Secret` (obligatorio, mismo fail-fast de siempre) y `Jwt:ExpiracionHoras` (opcional, default `12`). Extraído de `Program.cs` para poder testearlo sin levantar el host completo (mismo espíritu que `DomainExceptionHandler`/`JwtTokenService`, ya testeados de forma aislada).

**Contexto verificado:** hoy la expiración está hardcodeada en `Program.cs`: `return new JwtOptions(secret, TimeSpan.FromHours(10));` — 10 horas fijas, sin ninguna clave `Jwt:ExpiracionHoras` en `appsettings.json`. Este task la sube a 12h por defecto (jornada laboral típica) y la hace configurable.

- [ ] **Step 1: Escribir los tests que fallan — `JwtOptionsFactoryTests.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using StockApp.Api.Auth;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class JwtOptionsFactoryTests
{
    private static IConfiguration Config(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    [Fact]
    public void Crear_SinExpiracionHoras_UsaDefault12Horas()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "clave-de-prueba-de-al-menos-32-caracteres-x",
        });

        var options = JwtOptionsFactory.Crear(config);

        Assert.Equal(TimeSpan.FromHours(12), options.Expiracion);
    }

    [Fact]
    public void Crear_ConExpiracionHorasConfigurada_UsaElValorProvisto()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "clave-de-prueba-de-al-menos-32-caracteres-x",
            ["Jwt:ExpiracionHoras"] = "24",
        });

        var options = JwtOptionsFactory.Crear(config);

        Assert.Equal(TimeSpan.FromHours(24), options.Expiracion);
    }

    [Fact]
    public void Crear_SinSecret_LanzaInvalidOperationException()
    {
        var config = Config(new Dictionary<string, string?>());

        Assert.Throws<InvalidOperationException>(() => JwtOptionsFactory.Crear(config));
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter JwtOptionsFactoryTests`
Expected: FAIL — error de compilación (`JwtOptionsFactory` no existe todavía).

- [ ] **Step 3: Implementar `JwtOptionsFactory.cs`**

```csharp
using Microsoft.Extensions.Configuration;

namespace StockApp.Api.Auth;

/// <summary>
/// Arma JwtOptions leyendo Jwt:Secret (obligatorio) y Jwt:ExpiracionHoras (opcional,
/// default 12 — Fase 3a, D10: jornada de trabajo típica). Extraído de Program.cs para
/// poder testearlo sin levantar el host completo (mismo espíritu que DomainExceptionHandler).
/// </summary>
public static class JwtOptionsFactory
{
    public const double ExpiracionHorasPorDefecto = 12;

    public static JwtOptions Crear(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException(
                "Falta 'Jwt:Secret' en la configuración. En desarrollo: " +
                "dotnet user-secrets set \"Jwt:Secret\" \"<clave-de-al-menos-32-caracteres>\".");

        var horas = configuration.GetValue<double?>("Jwt:ExpiracionHoras") ?? ExpiracionHorasPorDefecto;

        return new JwtOptions(secret, TimeSpan.FromHours(horas));
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter JwtOptionsFactoryTests`
Expected: PASS (3 tests)

- [ ] **Step 5: Usar `JwtOptionsFactory.Crear` en `Program.cs`**

Reemplazar:

```csharp
// JwtOptions: misma razón que AppDbContext arriba — el secreto se lee de forma diferida
// en el factory (resuelto post-Build), no en una `var` top-level. JwtOptions es un
// record posicional sin constructor sin parámetros, así que no es compatible con el
// patrón AddOptions<T>().Bind(...).ValidateOnStart() estándar (ese patrón requiere poder
// instanciar T con Activator.CreateInstance<T>() y mutar propiedades por reflexión).
// Se preserva el fail-fast con mensaje amigable forzando la resolución del singleton
// apenas arranca el host (justo después de builder.Build(), abajo).
builder.Services.AddSingleton(sp =>
{
    var secret = sp.GetRequiredService<IConfiguration>()["Jwt:Secret"]
        ?? throw new InvalidOperationException(
            "Falta 'Jwt:Secret' en la configuración. En desarrollo: " +
            "dotnet user-secrets set \"Jwt:Secret\" \"<clave-de-al-menos-32-caracteres>\".");
    return new JwtOptions(secret, TimeSpan.FromHours(10));
});
```

por:

```csharp
// JwtOptions: misma razón que AppDbContext arriba — el secreto (y ahora la expiración,
// Fase 3a D10) se leen de forma diferida en el factory (resuelto post-Build), no en una
// `var` top-level. JwtOptions es un record posicional sin constructor sin parámetros, así
// que no es compatible con el patrón AddOptions<T>().Bind(...).ValidateOnStart() estándar
// (ese patrón requiere poder instanciar T con Activator.CreateInstance<T>() y mutar
// propiedades por reflexión). Se preserva el fail-fast con mensaje amigable forzando la
// resolución del singleton apenas arranca el host (justo después de builder.Build(), abajo).
// La construcción en sí vive en JwtOptionsFactory.Crear (testeable sin host completo).
builder.Services.AddSingleton(sp => JwtOptionsFactory.Crear(sp.GetRequiredService<IConfiguration>()));
```

`using StockApp.Api.Auth;` ya está presente en `Program.cs`.

- [ ] **Step 6: Correr toda la suite de `StockApp.Api.Tests` para confirmar que nada se rompió**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas — `ApiFactory` solo overridea `Jwt:Secret`, no `Jwt:ExpiracionHoras`, así que los tests siguen corriendo con el default de 12h, que es más que suficiente para la duración de un test).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/Auth/JwtOptionsFactory.cs src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/Auth/JwtOptionsFactoryTests.cs
git commit -m "feat(api): Jwt:ExpiracionHoras configurable, default 12 horas"
```

---

## Task 25: `POST /movimientos` deja de emitir un `Location` roto (limpieza del review de 2b)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/MovimientosEndpoints.cs`
- Modify: `tests/StockApp.Api.Tests/MovimientosEndpointTests.cs`

**Interfaces:**
- Produces: `POST /movimientos` sigue devolviendo `201 Created` con el `MovimientoRegistradoDto` en el body, pero SIN header `Location` (antes apuntaba a `/movimientos/{id}`, una ruta que no existe — el único `GET` del recurso es `/movimientos/historial`).

- [ ] **Step 1: Agregar el test que falla a `MovimientosEndpointTests.cs`**

Agregar, al final de la clase (antes del `}` de cierre):

```csharp

    // ── Limpieza Location (review final de Fase 2b) ──────────────────────────

    [Fact]
    public async Task PostMovimientos_Devuelve201SinLocationARutaInexistente()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-LOC1", "Producto Location", 10m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 10m, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PostMovimientos_Devuelve201SinLocationARutaInexistente`
Expected: FAIL — `response.Headers.Location` hoy apunta a `/movimientos/{id}` (ruta inexistente), no es `null`.

- [ ] **Step 3: Quitar el `Location` roto de `MovimientosEndpoints.cs`**

Reemplazar:

```csharp
            var registrado = await movimientos.RegistrarAsync(dto, request.Forzar);
            return Results.Created($"/movimientos/{registrado.MovimientoId}", registrado);
```

por:

```csharp
            var registrado = await movimientos.RegistrarAsync(dto, request.Forzar);
            // Sin Location: no existe GET /movimientos/{id} (el único GET del recurso es
            // /movimientos/historial) — emitir una ruta que no resuelve es peor que omitirla
            // (review final de Fase 2b).
            return Results.Created((string?)null, registrado);
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter MovimientosEndpointTests`
Expected: PASS (todas)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/MovimientosEndpoints.cs tests/StockApp.Api.Tests/MovimientosEndpointTests.cs
git commit -m "fix(api): POST /movimientos ya no emite Location a una ruta inexistente"
```

---

## Task 26: `README.md` actualizado

**Files:**
- Modify: `src/StockApp.Api/README.md`

**Interfaces:** ninguna — task de documentación, sin tests.

- [ ] **Step 1: Actualizar la tabla de "Superficie de endpoints"**

En `src/StockApp.Api/README.md`, reemplazar la sección completa de la tabla (desde `| Recurso | Endpoint | Rol |` hasta la última fila de Unidades):

```markdown
| Recurso | Endpoint | Rol |
|---|---|---|
| Auth | `GET /auth/primer-arranque` | público |
| | `POST /auth/primer-admin` | público (solo funciona una vez, con la BD sin usuarios) |
| | `POST /auth/login` | público |
| Movimientos | `POST /movimientos` | Admin+Op |
| | `GET /movimientos/historial` | Admin+Op |
| | `POST /productos/{id}/recalcular-stock` | Admin+Op |
| Reportes | `GET /reportes/valorizacion` | Admin |
| | `GET /reportes/stock-por-categoria` | Admin |
| | `GET /reportes/mas-movidos` | Admin |
| | `GET /reportes/historial-producto/{productoId}` | Admin |
| Auditoría | `GET /auditoria` | Admin |
| Usuarios | `GET /usuarios` · `POST /usuarios` (201 con `{ id }`) · `DELETE /usuarios/{id}` · `PUT /usuarios/{id}/rol` · `PUT /usuarios/{id}/contrasena` | Admin |
| Productos | `GET /productos?texto=` (o `sku=`/`codigoBarras=`/`nombre=`; todos ausentes = listar todo) · `POST /productos` · `PUT /productos/{id}` · `DELETE /productos/{id}` · `PUT /productos/{id}/precio` | Admin+Op |
| Categorías | `GET /categorias` · `POST` · `PUT /{id}` (sin `Id` en el body — el id de ruta es la única fuente) · `DELETE /{id}` | Admin |
| | `GET /categorias/activas` | Admin+Op |
| Proveedores | `GET /proveedores` · `POST` · `PUT /{id}` (sin `Id` en el body) · `DELETE /{id}` | Admin |
| Unidades | `GET /unidades-medida` · `POST` · `PUT /{id}` (sin `Id` en el body) · `DELETE /{id}` | Admin |
| | `GET /unidades-medida/activas` · `POST /unidades-medida/garantizar-por-defecto` (idempotente) | Admin+Op |

Las responses de `GET /categorias`, `GET /proveedores` y `GET /unidades-medida` (y sus
variantes `/activas`) devuelven DTOs (`CategoriaDto`/`ProveedorDto`/`UnidadMedidaDto`), no
las entidades de dominio crudas — una nav property futura en el dominio no puede cambiar el
contrato HTTP silenciosamente (Fase 3a, D3).

`POST /auth/login` devuelve `{ token, usuario: { id, nombreUsuario, nombreCompleto, rol } }`
— el cliente puebla su sesión local sin decodificar los claims del JWT (Fase 3a, D8).
```

- [ ] **Step 2: Documentar la expiración configurable del JWT**

Reemplazar la sección "Configurar el secreto JWT (desarrollo)" agregando, al final del bloque (antes del siguiente `##`):

```markdown

El tiempo de vida del token es configurable vía `Jwt:ExpiracionHoras` (default `12` horas si
no se especifica):

\`\`\`bash
dotnet user-secrets set "Jwt:ExpiracionHoras" "8"
\`\`\`
```

- [ ] **Step 3: Documentar el bootstrap de primer arranque y quitar la nota vieja**

Reemplazar (en la sección "Requisitos"):

```markdown
- Al menos un usuario Admin y un usuario Operador existentes en la tabla `Usuarios`
  (sembrados por `StockApp.Seeder` o por `PrimerArranqueService` de la app desktop —
  el bootstrap de primer arranque vía API queda para Fase 4).
```

por:

```markdown
- Si la tabla `Usuarios` está vacía, el propio cliente puede crear el primer Admin vía
  `GET /auth/primer-arranque` → `POST /auth/primer-admin` (Fase 3a, D7) — ya no depende
  de `StockApp.Seeder` ni de la app desktop.
```

- [ ] **Step 4: Documentar la migración automática**

Agregar, al final de la sección "Requisitos" (después del bullet anterior):

```markdown
- La API migra la base de datos automáticamente al arrancar (Fase 3a, D9) — no hace falta
  correr migraciones a mano ni depender de `DatabaseInitializer` del desktop.
```

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/README.md
git commit -m "docs(api): actualiza README con la superficie completa de Fase 3a"
```

---

## Task 27: Suite completa del repo + verificación manual

**Files:** ninguno modificado — task de verificación final.

**Interfaces:** ninguna.

- [ ] **Step 1: Correr la suite completa de cada proyecto de test**

Run:
```bash
dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj
dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj
dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj
```
Expected: PASS en los 5 proyectos, 0 failures.

- [ ] **Step 2: Grep final — confirmar que no queda ningún rastro de las excepciones genéricas en Application**

Run: `rg -n "throw new (InvalidOperationException|KeyNotFoundException)" src/StockApp.Application/`
Expected: sin resultados (0 matches) — confirmación final de que Bloque A cerró completo.

- [ ] **Step 3: Grep final — confirmar que ningún `ViewModel` de catálogo sigue filtrando por los tipos viejos**

Run: `rg -n "when \(ex is InvalidOperationException or KeyNotFoundException\)" src/StockApp.Presentation/`
Expected: sin resultados (0 matches) — los 4 filtros `when` (Categoria/Proveedor/UnidadMedida/Producto) migraron en Tasks 3-6.

- [ ] **Step 4: Levantar la API y hacer una verificación manual con curl**

Run: `dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http`

En otra terminal, con la BD vacía (o tras un `TRUNCATE` manual de `Usuarios`):

```bash
# 1) Bootstrap: requiere crear admin
curl http://localhost:5043/auth/primer-arranque
# {"requiereCrearAdmin":true}

# 2) Crear el primer admin
curl -i -X POST http://localhost:5043/auth/primer-admin \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"admin","contrasena":"admin123"}'
# 201 Created

# 3) Ya no requiere
curl http://localhost:5043/auth/primer-arranque
# {"requiereCrearAdmin":false}

# 4) Repetir el bootstrap -> 409
curl -i -X POST http://localhost:5043/auth/primer-admin \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"otro","contrasena":"admin123"}'
# 409 Conflict

# 5) Login -> token + usuario
curl -X POST http://localhost:5043/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"admin","contrasena":"admin123"}'
# {"token":"...","usuario":{"id":1,"nombreUsuario":"admin","nombreCompleto":null,"rol":0}}
# Copiar "token" -> <TOKEN_ADMIN>

# 6) Alta de categoría -> body sin Id en Modificar
curl -i -X POST http://localhost:5043/categorias \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_ADMIN>" \
  -d '{"nombre":"Bebidas"}'
curl -i -X PUT http://localhost:5043/categorias/1 \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_ADMIN>" \
  -d '{"nombre":"Bebidas y Licores"}'
# 200 -- confirmar que modifica la 1 (no hay campo Id para desalinear)

# 7) GET /categorias -> shape DTO, no entidad
curl http://localhost:5043/categorias -H "Authorization: Bearer <TOKEN_ADMIN>"
# [{"id":1,"nombre":"Bebidas y Licores","activo":true}]

# 8) Garantizar unidad por defecto (idempotente)
curl -X POST http://localhost:5043/unidades-medida/garantizar-por-defecto \
  -H "Authorization: Bearer <TOKEN_ADMIN>"
curl -X POST http://localhost:5043/unidades-medida/garantizar-por-defecto \
  -H "Authorization: Bearer <TOKEN_ADMIN>"
# ambas 200, mismo id, sin duplicar

# 9) GET /productos con filtros nuevos
curl "http://localhost:5043/productos?sku=NOEXISTE" -H "Authorization: Bearer <TOKEN_ADMIN>"
# []

# 10) POST /movimientos -> 201 sin header Location
curl -i -X POST http://localhost:5043/movimientos \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_ADMIN>" \
  -d '{"productoId":1,"tipo":0,"motivo":0,"cantidad":1,"precioUnitario":1,"comentario":null}'
# 404 esperado si no hay producto id=1 -- ajustar según datos sembrados; confirmar
# ausencia de header Location en la respuesta 201 si el producto existe.
```

Confirmar visualmente: cada paso devuelve el status esperado, el body de `/categorias` no
trae ningún campo que no esté en `CategoriaDto`, y el flujo de bootstrap se comporta como
un semáforo de un solo uso.

- [ ] **Step 5: Detener la API**

Run: `Ctrl+C` en la terminal donde corre `dotnet run`.

No hay commit en este task — es puramente de verificación. Si algún paso falla, volver al
task correspondiente del plan, corregir, y repetir Steps 1-4.

---

## Self-Review

**1. Cobertura del spec (D1-D10 + limpieza):**

| Decisión | Tasks |
|---|---|
| D1 — Id de ruta única fuente | 12, 13, 14, 15 |
| D2 — POST /usuarios devuelve id | 11 |
| D3 — DTOs en tablas maestras | 16, 17, 18 |
| D4 — Excepciones de dominio propias | 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 |
| D5 — GET /productos búsqueda completa | 19 |
| D6 — garantizar-por-defecto | 20 |
| D7 — Bootstrap primer arranque | 21 |
| D8 — LoginResponse enriquecido | 22 |
| D9 — Migraciones al servidor | 23 |
| D10 — Token de jornada configurable | 24 |
| Limpieza Location POST /movimientos | 25 |

Las dos "minas" del brief están explícitamente desactivadas: Mina 1 (D2 rompe consumidores)
en Task 11, con la lista completa de call-sites verificados por `rg` y la razón por la que
`PrimerArranqueViewModel` no requiere edición. Mina 2 (D4 rompe los catch del desktop) en
cada uno de los Tasks 3-9, actualizando el filtro `when`/`catch` de cada ViewModel EN EL
MISMO task que migra su servicio correspondiente, con los tests de Presentation
sincronizados al tipo real.

**2. Placeholders:** no hay "TBD", "similar a Task N" sin código, ni pasos sin bloque de
código ejecutable. Cada Modify muestra el snippet exacto de antes/después tomado del
contenido real del repo (verificado con `rg`/lectura directa antes de escribir el plan).

**3. Consistencia de tipos entre tasks:**
- `EntidadNoEncontradaException(string mensaje)` y `ReglaDeNegocioException(string mensaje)`
  (Task 1) se usan con el mismo constructor posicional en Tasks 2-10, 21 — sin variaciones.
- `IUsuarioService.AltaUsuarioAsync(...)`: `Task<int>` desde Task 11 en adelante; ningún
  task posterior lo vuelve a declarar como `Task`.
- `CategoriaDto`/`ProveedorDto`/`UnidadMedidaDto` (Tasks 16-18) se reusan sin cambios en
  Task 20 (`AUnidadMedidaDto` para el endpoint de garantizar-por-defecto) y en la tabla del
  README (Task 26) — mismos nombres de campo en los tres.
- `ModificarCategoriaRequest`/`ModificarProveedorRequest`/`ModificarUnidadMedidaRequest`/
  `ModificarProductoRequest` sin `Id` (Tasks 12-15) — ningún task posterior vuelve a pasar
  un `Id` en el body de un PUT.
- `PrimerArranqueEstadoResponse`/`CrearAdminInicialRequest` (Task 21) consumidos una sola
  vez, sin conflicto de nombres con `LoginRequest`/`LoginResponse`/`UsuarioLoginResponse`
  (Task 22) — todos en el mismo archivo `AuthEndpoints.cs`, revisado que no colisionan.

**Correcciones aplicadas durante el self-review:** ninguna — el plan se escribió leyendo el
código real de cada archivo antes de redactar cada task (vía sub-agentes de exploración),
así que las firmas y los call-sites ya estaban verificados al momento de escribir cada Step.
