# Incremento 1: Fundaciones — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dejar parada la solución .NET con las 4 capas, una app Avalonia que abre una ventana, SQLite cableado vía EF Core y los proyectos de test con xUnit corriendo.

**Architecture:** Solución por capas (Domain / Application / Infrastructure / Presentation). Domain no depende de nadie; Application depende de Domain; Infrastructure depende de Application; Presentation (Avalonia) depende de Application e Infrastructure. Cada capa de producción tiene su proyecto de test xUnit espejo.

**Tech Stack:** .NET 10 (LTS), C#, Avalonia 11 (MVVM), EF Core 10 + `Microsoft.EntityFrameworkCore.Sqlite`, xUnit.

> **Nota sobre versiones:** los comandos usan la última versión estable de cada plantilla/paquete. Si tu SDK instalado es .NET 8 o 9, los comandos funcionan igual ajustando el `TargetFramework`. Verificá con `dotnet --version` antes de empezar.

---

## File Structure

```
StockApp.sln
src/
  StockApp.Domain/            # entidades + enums (sin dependencias). Vacío en este incremento.
  StockApp.Application/        # servicios + interfaces. Ref: Domain. Vacío en este incremento.
  StockApp.Infrastructure/     # AppDbContext + EF Core/SQLite. Ref: Application.
    Persistence/AppDbContext.cs
  StockApp.Presentation/       # app Avalonia (MVVM). Ref: Application + Infrastructure.
tests/
  StockApp.Domain.Tests/        # xUnit. Ref: Domain.
  StockApp.Application.Tests/    # xUnit. Ref: Application.
  StockApp.Infrastructure.Tests/ # xUnit. Ref: Infrastructure.
    AppDbContextSmokeTests.cs
```

Responsabilidad de cada unidad en este incremento:
- `AppDbContext`: punto único de acceso a SQLite vía EF Core. Sin entidades todavía (llegan en el Incremento 2); acá solo probamos que la conexión y la creación de la BD funcionan.
- Proyectos de test: andamiaje para que el ciclo TDD esté disponible desde el primer commit.

---

## Task 1: Crear la solución y el proyecto Domain

**Files:**
- Create: `StockApp.sln`
- Create: `src/StockApp.Domain/StockApp.Domain.csproj`

- [ ] **Step 1: Crear la solución vacía**

```bash
dotnet new sln -n StockApp
```

- [ ] **Step 2: Crear el proyecto Domain (class library) y agregarlo a la solución**

```bash
dotnet new classlib -o src/StockApp.Domain
rm src/StockApp.Domain/Class1.cs
dotnet sln add src/StockApp.Domain/StockApp.Domain.csproj
```

- [ ] **Step 3: Verificar que la solución compila**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add StockApp.sln src/StockApp.Domain
git commit -m "chore: solución inicial y proyecto Domain"
```

---

## Task 2: Crear el proyecto Application

**Files:**
- Create: `src/StockApp.Application/StockApp.Application.csproj`

- [ ] **Step 1: Crear el proyecto Application y agregarlo a la solución**

```bash
dotnet new classlib -o src/StockApp.Application
rm src/StockApp.Application/Class1.cs
dotnet sln add src/StockApp.Application/StockApp.Application.csproj
```

- [ ] **Step 2: Application referencia a Domain**

```bash
dotnet add src/StockApp.Application/StockApp.Application.csproj reference src/StockApp.Domain/StockApp.Domain.csproj
```

- [ ] **Step 3: Verificar compilación**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/StockApp.Application StockApp.sln
git commit -m "chore: proyecto Application con referencia a Domain"
```

---

## Task 3: Crear el proyecto Infrastructure con EF Core + SQLite

**Files:**
- Create: `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj`
- Create: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Crear el proyecto Infrastructure y agregarlo a la solución**

```bash
dotnet new classlib -o src/StockApp.Infrastructure
rm src/StockApp.Infrastructure/Class1.cs
dotnet sln add src/StockApp.Infrastructure/StockApp.Infrastructure.csproj
```

- [ ] **Step 2: Infrastructure referencia a Application + paquetes EF Core**

```bash
dotnet add src/StockApp.Infrastructure/StockApp.Infrastructure.csproj reference src/StockApp.Application/StockApp.Application.csproj
dotnet add src/StockApp.Infrastructure/StockApp.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/StockApp.Infrastructure/StockApp.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design
```

- [ ] **Step 3: Crear el `AppDbContext` mínimo**

Create `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace StockApp.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Las entidades (DbSet<>) se agregan en el Incremento 2.
}
```

- [ ] **Step 4: Verificar compilación**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Infrastructure StockApp.sln
git commit -m "chore: proyecto Infrastructure con EF Core, SQLite y AppDbContext mínimo"
```

---

## Task 4: Crear la app Avalonia (Presentation)

**Files:**
- Create: `src/StockApp.Presentation/` (estructura generada por la plantilla Avalonia MVVM)

- [ ] **Step 1: Instalar las plantillas de Avalonia (si no están)**

```bash
dotnet new install Avalonia.Templates
```
Expected: confirma que se instalaron las plantillas `avalonia.*`.

- [ ] **Step 2: Crear la app Avalonia MVVM y agregarla a la solución**

```bash
dotnet new avalonia.mvvm -o src/StockApp.Presentation
dotnet sln add src/StockApp.Presentation/StockApp.Presentation.csproj
```

- [ ] **Step 3: Presentation referencia a Application e Infrastructure**

```bash
dotnet add src/StockApp.Presentation/StockApp.Presentation.csproj reference src/StockApp.Application/StockApp.Application.csproj
dotnet add src/StockApp.Presentation/StockApp.Presentation.csproj reference src/StockApp.Infrastructure/StockApp.Infrastructure.csproj
```

- [ ] **Step 4: Verificar que la solución completa compila**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Verificar que la app arranca y muestra la ventana**

Run: `dotnet run --project src/StockApp.Presentation`
Expected: se abre la ventana principal de Avalonia (la de la plantilla). Cerrar la ventana para terminar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation StockApp.sln
git commit -m "chore: app Avalonia MVVM referenciando Application e Infrastructure"
```

---

## Task 5: Crear los proyectos de test xUnit

**Files:**
- Create: `tests/StockApp.Domain.Tests/`
- Create: `tests/StockApp.Application.Tests/`
- Create: `tests/StockApp.Infrastructure.Tests/`

- [ ] **Step 1: Crear los tres proyectos de test y agregarlos a la solución**

```bash
dotnet new xunit -o tests/StockApp.Domain.Tests
dotnet new xunit -o tests/StockApp.Application.Tests
dotnet new xunit -o tests/StockApp.Infrastructure.Tests
dotnet sln add tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj
dotnet sln add tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj
dotnet sln add tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj
```

- [ ] **Step 2: Cada test referencia a su proyecto de producción**

```bash
dotnet add tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj reference src/StockApp.Domain/StockApp.Domain.csproj
dotnet add tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj reference src/StockApp.Application/StockApp.Application.csproj
dotnet add tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj reference src/StockApp.Infrastructure/StockApp.Infrastructure.csproj
```

- [ ] **Step 3: Borrar los archivos de test de ejemplo de la plantilla**

```bash
rm tests/StockApp.Domain.Tests/UnitTest1.cs
rm tests/StockApp.Application.Tests/UnitTest1.cs
rm tests/StockApp.Infrastructure.Tests/UnitTest1.cs
```

- [ ] **Step 4: Verificar que la solución compila con los tests**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add tests StockApp.sln
git commit -m "chore: proyectos de test xUnit espejo de cada capa"
```

---

## Task 6: Smoke test de SQLite vía AppDbContext

Verifica con un test real que EF Core abre/crea una base SQLite. Es el primer test del ciclo TDD del proyecto.

**Files:**
- Test: `tests/StockApp.Infrastructure.Tests/AppDbContextSmokeTests.cs`

- [ ] **Step 1: Escribir el test que falla**

Create `tests/StockApp.Infrastructure.Tests/AppDbContextSmokeTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Infrastructure.Tests;

public class AppDbContextSmokeTests
{
    [Fact]
    public void PuedeCrearYAbrirBaseSqliteEnMemoria()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new AppDbContext(options);
        context.Database.OpenConnection();

        var creada = context.Database.EnsureCreated();

        Assert.True(creada);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: `Passed!  - Failed: 0, Passed: 1`

> Nota: este test usa SQLite en memoria (`:memory:`), por eso requiere mantener la conexión abierta con `OpenConnection()`. Con un `AppDbContext` sin entidades, `EnsureCreated()` igualmente devuelve `true` porque crea la base aunque no tenga tablas.

- [ ] **Step 3: Commit**

```bash
git add tests/StockApp.Infrastructure.Tests/AppDbContextSmokeTests.cs
git commit -m "test: smoke test de creación de base SQLite vía AppDbContext"
```

---

## Task 7: Suite completa verde + .gitignore

**Files:**
- Create: `.gitignore`

- [ ] **Step 1: Crear el `.gitignore` de .NET (si no existe uno)**

```bash
dotnet new gitignore
```
Expected: crea `.gitignore` con los patrones estándar de .NET (`bin/`, `obj/`, etc.).

- [ ] **Step 2: Correr TODA la suite de tests**

Run: `dotnet test`
Expected: `Passed!` en los tres proyectos de test; total `Passed: 1, Failed: 0` (solo el smoke test por ahora).

- [ ] **Step 3: Commit**

```bash
git add .gitignore
git commit -m "chore: .gitignore de .NET"
```

---

## Self-Review (cobertura del incremento)

- ✅ Solución con las 4 capas y dependencias correctas (Task 1–4).
- ✅ Avalonia arranca y muestra ventana (Task 4, Step 5).
- ✅ EF Core + SQLite cableado y verificado con test real (Task 3 + Task 6).
- ✅ xUnit en las tres capas, listo para TDD (Task 5).
- ✅ `.gitignore` para no commitear `bin/`/`obj/` (Task 7).

**Lo que este incremento NO hace (próximos incrementos):** entidades del dominio, migraciones reales, inyección de dependencias en el arranque de Avalonia, cualquier feature funcional. Eso arranca en el Incremento 2 (Dominio + Persistencia).
