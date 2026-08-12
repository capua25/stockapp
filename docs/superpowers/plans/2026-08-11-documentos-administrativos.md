# Módulo de Documentos Administrativos — Plan de Implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar el módulo de documentos administrativos (expedientes, oficios, suministros) con alta, edición, transiciones de estado auditadas, historial append-only, adjuntos y permisos configurables, copiando capa por capa el patrón ya validado del módulo de Tareas.

**Architecture:** Entidad `DocumentoAdministrativo` con máquina de estados propia (`Pendiente/EnProceso/Finalizado/Anulado`, con reapertura) en el dominio, análoga a `Tarea` pero con la particularidad de que `Finalizado` y `Anulado` no son terminales. Historial append-only vía `EventoDocumento` (molde de `NotaTarea`) que registra tanto transiciones automáticas como notas manuales. Adjuntos con entidad propia `AdjuntoDocumento`/`AdjuntoDocumentoContenido` (metadatos y bytes separados, igual que Finanzas, pero sin reusar la entidad `Adjunto` de Finanzas para no acoplar módulos). El flujo atraviesa 8 capas: Domain → Persistencia (EF Core/Postgres) → Application (servicios + repositorios) → Api (minimal API) → ApiClient (HTTP) → Presentation (ViewModels/Views Avalonia) → Auditoría (`AccionAuditada`) → Permisos (`documentos.gestionar`/`documentos.administrar`).

**Tech Stack:** .NET 10, C#, EF Core 10 + Npgsql/PostgreSQL, ASP.NET Core Minimal API, Avalonia 12.0.5 + CommunityToolkit.Mvvm, xUnit (Testcontainers para Infrastructure/Api contra Postgres real, Moq solo en Presentation.Tests).

## Global Constraints

- Rama de trabajo: crear `feat/documentos-administrativos` desde `main`. No pushear.
- Commits: conventional commits en español, uno por tarea como mínimo. **NUNCA agregar "Co-Authored-By" ni atribución a IA.**
- TDD estricto y sin atajos: escribir el test, **correrlo y verlo fallar**, implementar lo mínimo, correrlo y verlo pasar, commitear.
- **NUNCA correr `StockApp.Application.Tests` y `StockApp.Api.Tests` en paralelo**: colisionan por Testcontainers y producen ~303 falsos rojos. Siempre secuencial.
- `StockApp.Infrastructure.Tests` corre contra PostgreSQL real; el contenedor `stockapp-pg` tiene que estar levantado.
- `AccionAuditada` es **append-only**: nunca reordenar ni reutilizar valores. Este módulo usa 52 a 59.
- Permisos, nombres exactos: `documentos.gestionar` (configurable, va a `PermisosInicialesOperador`) y `documentos.administrar` (estructural, va a `PermisosEstructuralesAdmin`).
- Las Views de Avalonia **no se auto-inicializan**: toda vista nueva engancha `DataContextChanged` para disparar su carga. Es un bug recurrente del proyecto.
- `UnauthorizedAccessException` en los ViewModels se captura **en silencio**: el manejador central del 403 ya muestra el diálogo. Duplicarlo reintroduce el doble aviso arreglado en el commit `093fc7c`.
- Spec de referencia: `docs/superpowers/specs/2026-08-11-documentos-administrativos-design.md`

---

## Task 1: Enums `TipoDocumento`/`EstadoDocumento` + entidad `DocumentoAdministrativo` con máquina de estados

**Files:**
- Create: `src/StockApp.Domain/Enums/TipoDocumento.cs`
- Create: `src/StockApp.Domain/Enums/EstadoDocumento.cs`
- Create: `src/StockApp.Domain/Entities/DocumentoAdministrativo.cs`
- Test: `tests/StockApp.Domain.Tests/Entities/DocumentoAdministrativoTests.cs`

**Interfaces:**
- Consumes: `ReglaDeNegocioException` (`src/StockApp.Domain/Exceptions/ReglaDeNegocioException.cs`, constructor `(string mensaje)`), `Usuario` (`src/StockApp.Domain/Entities/Usuario.cs`, para la nav `RegistradoPor`).
- Produces (consumido por Tasks 2-5 y por el bloque B):
  - `enum TipoDocumento { Expediente = 0, Oficio = 1, Suministro = 2 }`
  - `enum EstadoDocumento { Pendiente = 0, EnProceso = 1, Finalizado = 2, Anulado = 3 }`
  - `class DocumentoAdministrativo` con props `int Id`, `string Numero`, `int Anio`, `TipoDocumento Tipo`, `DateTime FechaEmision`, `string Descripcion`, `EstadoDocumento Estado` (default `Pendiente`), `int RegistradoPorUsuarioId`, `Usuario? RegistradoPor`, `DateTime FechaRegistro`, `DateTime? FechaCierre`, `List<EventoDocumento> Eventos` (colección tipada — se completa en Task 2, acá queda como `List<EventoDocumento> Eventos { get; set; } = new();` ya con el using aunque la clase `EventoDocumento` recién se cree en Task 2: ambas entidades se crean en el mismo Task 1 solo con el enum/estado; `EventoDocumento` real llega en Task 2, así que Task 1 declara la propiedad y Task 2 la llena de contenido real — ver nota de compilación en Step 3).
  - `bool EsActivo => Estado is EstadoDocumento.Pendiente or EstadoDocumento.EnProceso`
  - `bool EsCerrado => Estado is EstadoDocumento.Finalizado or EstadoDocumento.Anulado`
  - `bool PuedeTransicionarA(EstadoDocumento destino)`
  - `void CambiarEstado(EstadoDocumento destino)` — lanza `ReglaDeNegocioException` si la transición no está en `TransicionesValidas`; no toca `FechaCierre` ni ningún otro campo.

**Nota de secuencia**: para que Task 1 compile de forma aislada sin esperar a Task 2, `DocumentoAdministrativo.cs` se escribe ya con `using`s y la propiedad `List<EventoDocumento> Eventos` apuntando a una clase `EventoDocumento` que este mismo Task 1 crea como **stub mínimo** (`Id`, nada más) en un archivo separado; Task 2 la reemplaza por la versión completa. Esto evita que Task 1 quede en un estado que no compila mientras se corre su test.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Domain.Tests/Entities/DocumentoAdministrativoTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class DocumentoAdministrativoTests
{
    private static DocumentoAdministrativo NuevoDocumento(EstadoDocumento estado = EstadoDocumento.Pendiente) => new()
    {
        Numero = "0087",
        Anio = 2026,
        Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow,
        Descripcion = "Solicitud de poda de árbol en vereda",
        RegistradoPorUsuarioId = 1,
        FechaRegistro = DateTime.UtcNow,
        Estado = estado,
    };

    // ── Transiciones válidas (D4 del spec) ────────────────────────────────────

    [Fact]
    public void CambiarEstado_PendienteAEnProceso_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.Pendiente);
        doc.CambiarEstado(EstadoDocumento.EnProceso);
        Assert.Equal(EstadoDocumento.EnProceso, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_PendienteAAnulado_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.Pendiente);
        doc.CambiarEstado(EstadoDocumento.Anulado);
        Assert.Equal(EstadoDocumento.Anulado, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_EnProcesoAPendiente_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.EnProceso);
        doc.CambiarEstado(EstadoDocumento.Pendiente);
        Assert.Equal(EstadoDocumento.Pendiente, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_EnProcesoAFinalizado_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.EnProceso);
        doc.CambiarEstado(EstadoDocumento.Finalizado);
        Assert.Equal(EstadoDocumento.Finalizado, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_EnProcesoAAnulado_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.EnProceso);
        doc.CambiarEstado(EstadoDocumento.Anulado);
        Assert.Equal(EstadoDocumento.Anulado, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_FinalizadoAEnProceso_Permitido_PorReapertura()
    {
        var doc = NuevoDocumento(EstadoDocumento.Finalizado);
        doc.CambiarEstado(EstadoDocumento.EnProceso);
        Assert.Equal(EstadoDocumento.EnProceso, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_AnuladoAEnProceso_Permitido_PorReapertura()
    {
        var doc = NuevoDocumento(EstadoDocumento.Anulado);
        doc.CambiarEstado(EstadoDocumento.EnProceso);
        Assert.Equal(EstadoDocumento.EnProceso, doc.Estado);
    }

    // ── Transiciones inválidas, incluida la identidad ─────────────────────────

    [Theory]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Pendiente)]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Finalizado)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.EnProceso)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Finalizado)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Pendiente)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Anulado)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Anulado)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Pendiente)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Finalizado)]
    public void CambiarEstado_TransicionNoListada_LanzaReglaDeNegocioYNoMuta(
        EstadoDocumento origen, EstadoDocumento destino)
    {
        var doc = NuevoDocumento(origen);

        var ex = Assert.Throws<ReglaDeNegocioException>(() => doc.CambiarEstado(destino));

        Assert.Contains(origen.ToString(), ex.Message);
        Assert.Contains(destino.ToString(), ex.Message);
        Assert.Equal(origen, doc.Estado);
    }

    // ── EsActivo / EsCerrado para los 4 estados ───────────────────────────────

    [Theory]
    [InlineData(EstadoDocumento.Pendiente, true, false)]
    [InlineData(EstadoDocumento.EnProceso, true, false)]
    [InlineData(EstadoDocumento.Finalizado, false, true)]
    [InlineData(EstadoDocumento.Anulado, false, true)]
    public void EsActivo_EsCerrado_ReflejanElEstado(EstadoDocumento estado, bool esperadoActivo, bool esperadoCerrado)
    {
        var doc = NuevoDocumento(estado);

        Assert.Equal(esperadoActivo, doc.EsActivo);
        Assert.Equal(esperadoCerrado, doc.EsCerrado);
    }

    // ── CambiarEstado no toca FechaCierre (D8: la sella el servicio, no la entidad) ──

    [Fact]
    public void CambiarEstado_NoTocaFechaCierre()
    {
        var doc = NuevoDocumento(EstadoDocumento.EnProceso);
        doc.FechaCierre = null;

        doc.CambiarEstado(EstadoDocumento.Finalizado);

        Assert.Null(doc.FechaCierre);
    }

    // ── PuedeTransicionarA: misma tabla que CambiarEstado, de solo lectura ────

    [Theory]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.EnProceso, true)]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Anulado, true)]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Pendiente, false)]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Finalizado, false)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.Pendiente, true)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.Finalizado, true)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.Anulado, true)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.EnProceso, false)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.EnProceso, true)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Pendiente, false)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Anulado, false)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Finalizado, false)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.EnProceso, true)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Pendiente, false)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Finalizado, false)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Anulado, false)]
    public void PuedeTransicionarA_ReflejaExactamenteLaMismaTablaQueCambiarEstado(
        EstadoDocumento origen, EstadoDocumento destino, bool esperado)
    {
        var doc = NuevoDocumento(origen);

        Assert.Equal(esperado, doc.PuedeTransicionarA(destino));
        // De solo lectura: consultar no debe mutar el estado.
        Assert.Equal(origen, doc.Estado);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Domain.Tests --filter FullyQualifiedName~DocumentoAdministrativoTests`
Expected: FALLA de compilación — `StockApp.Domain.Entities.DocumentoAdministrativo`, `StockApp.Domain.Enums.TipoDocumento` y `StockApp.Domain.Enums.EstadoDocumento` no existen.

- [ ] **Step 3: Crear los enums**

`src/StockApp.Domain/Enums/TipoDocumento.cs`:

```csharp
namespace StockApp.Domain.Enums;

/// <summary>
/// Tipo de documento administrativo (spec 2026-08-11, decisión 6). Enum fijo, no tabla
/// maestra configurable: append-only, se persiste como int. Agregar un tipo nuevo el día
/// de mañana es una línea de código, no una migración de datos.
/// </summary>
public enum TipoDocumento
{
    Expediente = 0,
    Oficio = 1,
    Suministro = 2,
}
```

`src/StockApp.Domain/Enums/EstadoDocumento.cs`:

```csharp
namespace StockApp.Domain.Enums;

/// <summary>
/// Estado de trámite de un documento administrativo (spec 2026-08-11, decisión 3). Cuatro
/// estados, no tres como Tarea: Anulado es la salida honesta para el trámite que muere sin
/// completarse, para no falsear la estadística de trámites Finalizados.
/// </summary>
public enum EstadoDocumento
{
    Pendiente = 0,
    EnProceso = 1,
    Finalizado = 2,
    Anulado = 3,
}
```

- [ ] **Step 4: Crear el stub mínimo de `EventoDocumento`**

`src/StockApp.Domain/Entities/EventoDocumento.cs` (stub — Task 2 lo completa):

```csharp
namespace StockApp.Domain.Entities;

/// <summary>Stub mínimo para que DocumentoAdministrativo compile — Task 2 lo completa.</summary>
public class EventoDocumento
{
    public int Id { get; set; }
}
```

- [ ] **Step 5: Crear la entidad `DocumentoAdministrativo`**

`src/StockApp.Domain/Entities/DocumentoAdministrativo.cs`:

```csharp
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Domain.Entities;

/// <summary>
/// Documento administrativo (expediente, oficio o suministro), spec 2026-08-11. Copia el
/// patrón de Tarea capa por capa: máquina de estados con diccionario privado
/// TransicionesValidas, CambiarEstado que valida y muta solo Estado, PuedeTransicionarA de
/// solo lectura para que la UI no recodifique las transiciones a mano. A diferencia de
/// Tarea, Finalizado y Anulado NO son terminales: admiten reapertura hacia EnProceso
/// (decisión 4), así que "cerrado" se parte en dos propiedades explícitas (EsActivo/
/// EsCerrado) en vez de derivarse de que TransicionesValidas[Estado] esté vacío.
/// FechaCierre no la toca CambiarEstado: la sella/limpia DocumentoAdministrativoService
/// (decisión 8), mismo criterio que Tarea.FechaFin no la toca Tarea.CambiarEstado.
/// </summary>
public class DocumentoAdministrativo
{
    public int Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public int Anio { get; set; }
    public TipoDocumento Tipo { get; set; }
    public DateTime FechaEmision { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Pendiente;

    public int RegistradoPorUsuarioId { get; set; }
    public Usuario? RegistradoPor { get; set; }
    public DateTime FechaRegistro { get; set; }
    public DateTime? FechaCierre { get; set; }

    public List<EventoDocumento> Eventos { get; set; } = new();

    private static readonly Dictionary<EstadoDocumento, EstadoDocumento[]> TransicionesValidas = new()
    {
        [EstadoDocumento.Pendiente]  = new[] { EstadoDocumento.EnProceso, EstadoDocumento.Anulado },
        [EstadoDocumento.EnProceso]  = new[] { EstadoDocumento.Pendiente, EstadoDocumento.Finalizado, EstadoDocumento.Anulado },
        [EstadoDocumento.Finalizado] = new[] { EstadoDocumento.EnProceso },
        [EstadoDocumento.Anulado]    = new[] { EstadoDocumento.EnProceso },
    };

    /// <summary>True si el trámite sigue en curso (Pendiente o EnProceso). Va a la solapa Activos.</summary>
    public bool EsActivo => Estado is EstadoDocumento.Pendiente or EstadoDocumento.EnProceso;

    /// <summary>True si el trámite está cerrado (Finalizado o Anulado). Va a la solapa Historial.</summary>
    public bool EsCerrado => Estado is EstadoDocumento.Finalizado or EstadoDocumento.Anulado;

    /// <summary>
    /// Valida y aplica la transición de estado (decisión 4 del spec). Rechaza cualquier
    /// combinación no listada en TransicionesValidas, incluida la identidad. No toca
    /// FechaCierre ni ningún otro campo: eso es responsabilidad del servicio (decisión 8).
    /// </summary>
    public void CambiarEstado(EstadoDocumento destino)
    {
        if (!PuedeTransicionarA(destino))
            throw new ReglaDeNegocioException(
                $"No se puede pasar el documento de '{Estado}' a '{destino}'.");
        Estado = destino;
    }

    /// <summary>
    /// Consulta de solo lectura sobre la misma tabla que usa CambiarEstado: única fuente de
    /// verdad de la máquina de estados. DocumentoFila (Presentation) debe consultar este
    /// método en vez de recodificar las transiciones a mano.
    /// </summary>
    public bool PuedeTransicionarA(EstadoDocumento destino) => TransicionesValidas[Estado].Contains(destino);
}
```

- [ ] **Step 6: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Domain.Tests --filter FullyQualifiedName~DocumentoAdministrativoTests`
Expected: todos los tests en verde (25 casos entre `[Fact]` y `[Theory]` `[InlineData]`).

- [ ] **Step 7: Commit**

```
git add src/StockApp.Domain/Enums/TipoDocumento.cs src/StockApp.Domain/Enums/EstadoDocumento.cs src/StockApp.Domain/Entities/DocumentoAdministrativo.cs src/StockApp.Domain/Entities/EventoDocumento.cs tests/StockApp.Domain.Tests/Entities/DocumentoAdministrativoTests.cs
git commit -m "feat(documentos): agrega DocumentoAdministrativo con máquina de estados"
```

---

## Task 2: Entidad `EventoDocumento` completa + `AgregarEvento` en `DocumentoAdministrativo`

**Files:**
- Modify: `src/StockApp.Domain/Entities/EventoDocumento.cs` (reemplaza el stub del Task 1 por la entidad completa)
- Modify: `src/StockApp.Domain/Entities/DocumentoAdministrativo.cs` (agrega el método `AgregarEvento`)
- Test: `tests/StockApp.Domain.Tests/Entities/EventoDocumentoTests.cs`

**Interfaces:**
- Consumes: `DocumentoAdministrativo`, `EstadoDocumento` (Task 1); `Usuario` (`src/StockApp.Domain/Entities/Usuario.cs`).
- Produces (consumido por Tasks 4-5 y por el bloque B):
  - `class EventoDocumento` con props `int Id`, `int DocumentoAdministrativoId`, `DateTime Fecha`, `int UsuarioId`, `Usuario? Usuario` (nav, ver nota abajo), `EstadoDocumento? EstadoAnterior`, `EstadoDocumento? EstadoNuevo`, `string Texto`, `bool EsAutomatico`.
  - `DocumentoAdministrativo.AgregarEvento(int usuarioId, string texto, bool esAutomatico, EstadoDocumento? anterior = null, EstadoDocumento? nuevo = null)` — agrega un `EventoDocumento` a `Eventos` con `Fecha = DateTime.UtcNow`. Sin método de borrado ni de edición: append-only en espíritu.

**Nota sobre la nav `Usuario`**: el contrato fijo del prompt no declara una nav `Usuario` en `EventoDocumento`, pero D5/D8 del spec necesitan mostrar quién generó cada evento en el hilo (igual que `NotaTarea.Usuario`), y el bloque B (Application/Presentation) va a necesitar el nombre del usuario sin una consulta aparte. Se agrega la nav `public Usuario? Usuario { get; set; }` como propiedad adicional sobre `UsuarioId`, mismo criterio que `NotaTarea.Usuario` — no rompe el contrato de tipos fijo (que no prohíbe navs adicionales de solo lectura), y evita que el bloque B tenga que hacer un JOIN manual para pintar el hilo de eventos.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Domain.Tests/Entities/EventoDocumentoTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class EventoDocumentoTests
{
    private static DocumentoAdministrativo NuevoDocumento() => new()
    {
        Numero = "0087",
        Anio = 2026,
        Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow,
        Descripcion = "Solicitud de poda de árbol en vereda",
        RegistradoPorUsuarioId = 1,
        FechaRegistro = DateTime.UtcNow,
    };

    [Fact]
    public void AgregarEvento_NotaManual_QuedaEnLaColeccionConLosDatosCorrectos()
    {
        var doc = NuevoDocumento();

        doc.AgregarEvento(usuarioId: 2, texto: "El vecino trajo la documentación faltante", esAutomatico: false);

        var evento = Assert.Single(doc.Eventos);
        Assert.Equal(2, evento.UsuarioId);
        Assert.Equal("El vecino trajo la documentación faltante", evento.Texto);
        Assert.False(evento.EsAutomatico);
        Assert.Null(evento.EstadoAnterior);
        Assert.Null(evento.EstadoNuevo);
        Assert.True((DateTime.UtcNow - evento.Fecha).TotalSeconds < 5);
    }

    [Fact]
    public void AgregarEvento_CambioDeEstadoAutomatico_QuedaConLosEstadosCompletos()
    {
        var doc = NuevoDocumento();

        doc.AgregarEvento(
            usuarioId: 1, texto: "Cambio de estado: Pendiente → EnProceso", esAutomatico: true,
            anterior: EstadoDocumento.Pendiente, nuevo: EstadoDocumento.EnProceso);

        var evento = Assert.Single(doc.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Equal(EstadoDocumento.Pendiente, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.EnProceso, evento.EstadoNuevo);
    }

    [Fact]
    public void AgregarEvento_VariasVeces_EsAppendOnly_SumaSinReemplazar()
    {
        var doc = NuevoDocumento();

        doc.AgregarEvento(usuarioId: 1, texto: "primero", esAutomatico: false);
        doc.AgregarEvento(usuarioId: 1, texto: "segundo", esAutomatico: false);
        doc.AgregarEvento(usuarioId: 1, texto: "tercero", esAutomatico: false);

        Assert.Equal(3, doc.Eventos.Count);
        Assert.Equal(new[] { "primero", "segundo", "tercero" }, doc.Eventos.Select(e => e.Texto));
    }

    [Fact]
    public void EventoDocumento_NoTieneMetodoDeBorradoNiDeEdicion()
    {
        // Append-only en espíritu: la única forma de sumar eventos es
        // DocumentoAdministrativo.AgregarEvento. La clase EventoDocumento no expone ningún
        // método propio (solo propiedades) — este test documenta la intención revisando el
        // tipo en runtime, no reemplaza una revisión de código.
        var metodosPropios = typeof(EventoDocumento).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName); // descarta get_/set_ de las propiedades

        Assert.Empty(metodosPropios);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Domain.Tests --filter FullyQualifiedName~EventoDocumentoTests`
Expected: FALLA de compilación — `DocumentoAdministrativo.AgregarEvento` no existe, y `EventoDocumento` (stub) no tiene `DocumentoAdministrativoId`/`Fecha`/`UsuarioId`/`Usuario`/`EstadoAnterior`/`EstadoNuevo`/`Texto`/`EsAutomatico`.

- [ ] **Step 3: Completar la entidad `EventoDocumento`**

Reemplazar el contenido completo de `src/StockApp.Domain/Entities/EventoDocumento.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

/// <summary>
/// Evento del hilo de historial de un DocumentoAdministrativo, append-only (decisión 5 del
/// spec, molde de NotaTarea): se agregan vía DocumentoAdministrativo.AgregarEvento, nunca
/// se editan ni se borran. EstadoAnterior/EstadoNuevo van completos cuando EsAutomatico
/// viene de un cambio de estado (CambioEstadoDocumento) y quedan nulos para notas manuales
/// y para altas/bajas de adjunto automáticas (decisión 11d) — un evento automático sin
/// cambio de estado es tan válido como uno con cambio de estado.
/// </summary>
public class EventoDocumento
{
    public int Id { get; set; }
    public int DocumentoAdministrativoId { get; set; }
    public DateTime Fecha { get; set; }
    public int UsuarioId { get; set; }

    /// <summary>Nav de solo lectura sobre UsuarioId, mismo criterio que NotaTarea.Usuario: el
    /// hilo de eventos necesita mostrar quién generó cada entrada sin una consulta aparte.</summary>
    public Usuario? Usuario { get; set; }

    public EstadoDocumento? EstadoAnterior { get; set; }
    public EstadoDocumento? EstadoNuevo { get; set; }
    public string Texto { get; set; } = string.Empty;

    /// <summary>true si lo generó el sistema (cambio de estado, adjunto agregado/quitado);
    /// false para una nota manual del funcionario.</summary>
    public bool EsAutomatico { get; set; }
}
```

- [ ] **Step 4: Agregar `AgregarEvento` a `DocumentoAdministrativo`**

En `src/StockApp.Domain/Entities/DocumentoAdministrativo.cs`, agregar el método después de `PuedeTransicionarA`:

```csharp
    /// <summary>
    /// Suma un evento al hilo append-only del documento (decisión 5 del spec). Única vía
    /// para agregar a Eventos — no hay método de borrado ni de edición en EventoDocumento.
    /// Fecha se sella acá (DateTime.UtcNow), igual que TareaService sella FechaFin: el
    /// llamador nunca pasa la fecha a mano.
    /// </summary>
    public void AgregarEvento(
        int usuarioId, string texto, bool esAutomatico,
        EstadoDocumento? anterior = null, EstadoDocumento? nuevo = null)
    {
        Eventos.Add(new EventoDocumento
        {
            DocumentoAdministrativoId = Id,
            Fecha = DateTime.UtcNow,
            UsuarioId = usuarioId,
            Texto = texto,
            EsAutomatico = esAutomatico,
            EstadoAnterior = anterior,
            EstadoNuevo = nuevo,
        });
    }
```

- [ ] **Step 5: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Domain.Tests --filter FullyQualifiedName~EventoDocumentoTests`
Expected: 4 tests en verde.

- [ ] **Step 6: Correr toda la suite de Domain.Tests y verificar que sigue verde**

Run: `dotnet test tests/StockApp.Domain.Tests`
Expected: todos los tests en verde, incluidos los de `DocumentoAdministrativoTests` (Task 1).

- [ ] **Step 7: Commit**

```
git add src/StockApp.Domain/Entities/EventoDocumento.cs src/StockApp.Domain/Entities/DocumentoAdministrativo.cs tests/StockApp.Domain.Tests/Entities/EventoDocumentoTests.cs
git commit -m "feat(documentos): agrega EventoDocumento y el hilo append-only del documento"
```

---

## Task 3: Entidades `AdjuntoDocumento` + `AdjuntoDocumentoContenido`

**Files:**
- Create: `src/StockApp.Domain/Entities/AdjuntoDocumento.cs`
- Create: `src/StockApp.Domain/Entities/AdjuntoDocumentoContenido.cs`
- Test: `tests/StockApp.Domain.Tests/Entities/AdjuntoDocumentoTests.cs`

**Interfaces:**
- Consumes: `DocumentoAdministrativo` (Task 1, para la nav `Documento`).
- Produces (consumido por Task 4-5 y por el bloque B):
  - `class AdjuntoDocumento` con props `int Id`, `int DocumentoAdministrativoId`, `DocumentoAdministrativo? Documento`, `string NombreArchivo`, `string ContentType`, `long TamanoBytes`, `bool Activo` (default `true`), `DateTime FechaAltaUtc`.
  - `class AdjuntoDocumentoContenido` con props `int Id`, `byte[] Contenido` (default `[]`).

- [ ] **Step 1: Escribir el test que falla**

Calibrado contra `tests/StockApp.Domain.Tests/Entities/AdjuntoTests.cs` (el molde de Finanzas: solo cubre el default de `Activo`, sin lógica de negocio propia — `AdjuntoDocumento` tampoco tiene ninguna, así que el test es igual de mínimo). Crear `tests/StockApp.Domain.Tests/Entities/AdjuntoDocumentoTests.cs`:

```csharp
using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class AdjuntoDocumentoTests
{
    [Fact]
    public void Activo_PorDefecto_EsTrue()
    {
        var adjunto = new AdjuntoDocumento();

        Assert.True(adjunto.Activo);
    }

    [Fact]
    public void AdjuntoDocumento_SeAsociaAUnDocumentoPorId()
    {
        var adjunto = new AdjuntoDocumento
        {
            DocumentoAdministrativoId = 7,
            NombreArchivo = "factura.pdf",
            ContentType = "application/pdf",
            TamanoBytes = 1024,
            FechaAltaUtc = DateTime.UtcNow,
        };

        Assert.Equal(7, adjunto.DocumentoAdministrativoId);
        Assert.Equal("factura.pdf", adjunto.NombreArchivo);
        Assert.Equal("application/pdf", adjunto.ContentType);
    }

    [Fact]
    public void AdjuntoDocumentoContenido_ContenidoPorDefecto_EsArrayVacio()
    {
        var contenido = new AdjuntoDocumentoContenido();

        Assert.Empty(contenido.Contenido);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Domain.Tests --filter FullyQualifiedName~AdjuntoDocumentoTests`
Expected: FALLA de compilación — `AdjuntoDocumento` y `AdjuntoDocumentoContenido` no existen.

- [ ] **Step 3: Crear `AdjuntoDocumento`**

`src/StockApp.Domain/Entities/AdjuntoDocumento.cs`:

```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Metadatos de un archivo adjunto a un DocumentoAdministrativo (decisión 10 del spec).
/// Entidad propia, NO reusa Adjunto de Finanzas: Documentos es un módulo independiente y
/// no debe meterse dentro del CHECK XOR de Finanzas. El contenido (bytes) vive SEPARADO en
/// AdjuntoDocumentoContenido (relación 1:1, Id = AdjuntoDocumentoId) para que listar
/// adjuntos nunca arrastre bytes de la BD — mismo patrón que Adjunto/AdjuntoContenido.
/// Baja lógica con Activo, nunca borrado físico (decisión 11c).
/// </summary>
public class AdjuntoDocumento
{
    public int Id { get; set; }
    public int DocumentoAdministrativoId { get; set; }
    public DocumentoAdministrativo? Documento { get; set; }

    public string NombreArchivo { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime FechaAltaUtc { get; set; }
}
```

- [ ] **Step 4: Crear `AdjuntoDocumentoContenido`**

`src/StockApp.Domain/Entities/AdjuntoDocumentoContenido.cs`:

```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Bytes del adjunto de un documento administrativo, en tabla propia (mapea a bytea en
/// Postgres). Id comparte valor con el AdjuntoDocumento dueño (relación 1:1 configurada en
/// AppDbContext) — mismo patrón exacto que AdjuntoContenido en Finanzas.
/// </summary>
public class AdjuntoDocumentoContenido
{
    public int Id { get; set; }
    public byte[] Contenido { get; set; } = Array.Empty<byte>();
}
```

- [ ] **Step 5: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Domain.Tests --filter FullyQualifiedName~AdjuntoDocumentoTests`
Expected: 3 tests en verde.

- [ ] **Step 6: Correr toda la suite de Domain.Tests**

Run: `dotnet test tests/StockApp.Domain.Tests`
Expected: todos los tests en verde (Tasks 1-3 acumulados).

- [ ] **Step 7: Commit**

```
git add src/StockApp.Domain/Entities/AdjuntoDocumento.cs src/StockApp.Domain/Entities/AdjuntoDocumentoContenido.cs tests/StockApp.Domain.Tests/Entities/AdjuntoDocumentoTests.cs
git commit -m "feat(documentos): agrega AdjuntoDocumento y AdjuntoDocumentoContenido"
```

---

## Task 4: Configuración en `AppDbContext` + migración `AgregaDocumentosAdministrativos`

**Files:**
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` (4 `DbSet` nuevos cerca de la línea 31, bloque `OnModelCreating` nuevo después del bloque de `PermisoUsuario`, líneas ~350-361)
- Modify: `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs` (agrega las 4 tablas nuevas al `TRUNCATE` de `LimpiarTablas`)
- Create: migración EF `AgregaDocumentosAdministrativos` (`src/StockApp.Infrastructure/Migrations/`)
- Test: `tests/StockApp.Infrastructure.Tests/Persistence/DocumentoAdministrativoIndiceUnicoTests.cs`

**Interfaces:**
- Consumes: `DocumentoAdministrativo`, `EventoDocumento`, `AdjuntoDocumento`, `AdjuntoDocumentoContenido` (Tasks 1-3); `Usuario` (FK); patrón de índice único compuesto con nombre explícito (`IX_Gastos_ProveedorId_NumeroFactura_NumeroOrden` en `GastoRepository.cs` como referencia de convención de nombre — acá el nombre lo genera EF por default a partir de las columnas del `HasIndex`, que coincide con el nombre pedido `IX_DocumentosAdministrativos_Tipo_Anio_Numero` porque el `DbSet` se llama `DocumentosAdministrativos`).
- Produces (consumido por Task 5 y por el bloque B):
  - `DbSet<DocumentoAdministrativo> AppDbContext.DocumentosAdministrativos`
  - `DbSet<EventoDocumento> AppDbContext.EventosDocumento`
  - `DbSet<AdjuntoDocumento> AppDbContext.AdjuntosDocumento`
  - `DbSet<AdjuntoDocumentoContenido> AppDbContext.AdjuntosDocumentoContenido`
  - Índice único `IX_DocumentosAdministrativos_Tipo_Anio_Numero` sobre `(Tipo, Anio, Numero)` — este es el nombre exacto que Task 5 (y el bloque B, en `DocumentoAdministrativoRepository`) usa en el `catch (DbUpdateException ex) when (...)` para detectar la violación.
  - Migración `AgregaDocumentosAdministrativos` aplicable con `dotnet ef database update`.

- [ ] **Step 1: Escribir el test que falla**

Este test asume que Task 4 ya agregó los `DbSet` y la config a `AppDbContext` (si no, no compila) — por eso el Step 1 real es escribir el test ANTES de tocar `AppDbContext`, y el Step 2 confirma que falla por falta de esos `DbSet`, no por falta de la migración (la migración se corre recién en el Step 5, contra un Postgres que Testcontainers levanta desde cero en cada corrida de la suite, así que sin migración el `SELECT`/`INSERT` fallaría igual con "relation does not exist" — un fallo distinto y más confuso que el de compilación que se busca acá).

Crear `tests/StockApp.Infrastructure.Tests/Persistence/DocumentoAdministrativoIndiceUnicoTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

/// <summary>
/// El índice único compuesto (Tipo, Anio, Numero) es la última defensa contra dos
/// funcionarios cargando el mismo expediente a la vez (decisión 1 del spec). Estos tests
/// verifican el índice a nivel de AppDbContext/Postgres directamente, sin pasar por el
/// repositorio (que recién existe en Task 5) — confirman que la BASE rechaza el duplicado,
/// no que el repositorio lo traduzca a un mensaje lindo.
/// </summary>
public class DocumentoAdministrativoIndiceUnicoTests : PostgresRepositoryTestBase
{
    public DocumentoAdministrativoIndiceUnicoTests(PostgresFixture fixture) : base(fixture) { }

    private static Usuario NuevoUsuario(string nombreUsuario = "operario1") => new()
    {
        NombreUsuario = nombreUsuario,
        HashContrasena = "x",
        Rol = RolUsuario.Operador,
        FechaAlta = DateTime.UtcNow,
    };

    private static DocumentoAdministrativo NuevoDocumento(
        int registradoPorUsuarioId, TipoDocumento tipo = TipoDocumento.Expediente,
        int anio = 2026, string numero = "0087") => new()
    {
        Numero = numero,
        Anio = anio,
        Tipo = tipo,
        FechaEmision = DateTime.UtcNow,
        Descripcion = "Solicitud de poda de árbol en vereda",
        RegistradoPorUsuarioId = registradoPorUsuarioId,
        FechaRegistro = DateTime.UtcNow,
    };

    [Fact]
    public async Task DosDocumentos_MismoTipoAnioNumero_ViolaElIndiceUnico()
    {
        var usuario = NuevoUsuario();
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        Assert.Equal("IX_DocumentosAdministrativos_Tipo_Anio_Numero", pg.ConstraintName);
    }

    [Theory]
    [InlineData(TipoDocumento.Oficio, 2026, "0087")]   // distinto Tipo
    [InlineData(TipoDocumento.Expediente, 2027, "0087")] // distinto Anio
    [InlineData(TipoDocumento.Expediente, 2026, "0088")] // distinto Numero
    public async Task DosDocumentos_DifierenEnUnCampoDeLaClave_NoViolanElIndice(
        TipoDocumento tipo, int anio, string numero)
    {
        var usuario = NuevoUsuario();
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id, tipo, anio, numero));

        await Context.SaveChangesAsync(); // no debe lanzar

        Assert.Equal(2, await Context.DocumentosAdministrativos.CountAsync());
    }

    [Fact]
    public async Task Estado_TieneIndicePropio_SePuedeFiltrarSinEscanearTabla()
    {
        // No hay forma directa de aserter "existe un índice" desde EF sin consultar el
        // catálogo de Postgres; en cambio, se prueba el efecto observable: filtrar por
        // Estado funciona sin error, y el modelo ya lo declaró en OnModelCreating (Step 3).
        var usuario = NuevoUsuario();
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var pendientes = await Context.DocumentosAdministrativos
            .Where(d => d.Estado == EstadoDocumento.Pendiente)
            .ToListAsync();

        Assert.Single(pendientes);
    }

    [Fact]
    public async Task AltaConEventoYAdjunto_PersisteElGrafoCompleto()
    {
        var usuario = NuevoUsuario();
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var doc = NuevoDocumento(usuario.Id);
        doc.AgregarEvento(usuario.Id, "Alta del documento", esAutomatico: true);
        Context.DocumentosAdministrativos.Add(doc);
        await Context.SaveChangesAsync();

        Context.AdjuntosDocumento.Add(new AdjuntoDocumento
        {
            DocumentoAdministrativoId = doc.Id,
            NombreArchivo = "factura.pdf",
            ContentType = "application/pdf",
            TamanoBytes = 1024,
            FechaAltaUtc = DateTime.UtcNow,
        });
        await Context.SaveChangesAsync();
        var adjuntoId = await Context.AdjuntosDocumento
            .Where(a => a.DocumentoAdministrativoId == doc.Id)
            .Select(a => a.Id)
            .SingleAsync();

        Context.AdjuntosDocumentoContenido.Add(
            new AdjuntoDocumentoContenido { Id = adjuntoId, Contenido = new byte[] { 1, 2, 3 } });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Assert.Equal(1, await Context.EventosDocumento.CountAsync(e => e.DocumentoAdministrativoId == doc.Id));
        Assert.Equal(1, await Context.AdjuntosDocumento.CountAsync(a => a.DocumentoAdministrativoId == doc.Id));
        Assert.Equal(1, await Context.AdjuntosDocumentoContenido.CountAsync(c => c.Id == adjuntoId));
    }

    [Fact]
    public async Task AltaConRegistradoPorUsuarioIdInexistente_ViolaLaFk()
    {
        var doc = NuevoDocumento(registradoPorUsuarioId: 999);
        Context.DocumentosAdministrativos.Add(doc);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pg.SqlState);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~DocumentoAdministrativoIndiceUnicoTests`
Expected: FALLA de compilación — `Context.DocumentosAdministrativos`, `Context.EventosDocumento`, `Context.AdjuntosDocumento` y `Context.AdjuntosDocumentoContenido` no existen todavía en `AppDbContext`.

- [ ] **Step 3: Agregar los `DbSet` a `AppDbContext`**

En `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`, agregar después de la línea `public DbSet<PermisoUsuario> PermisosUsuario => Set<PermisoUsuario>();`:

```csharp
    public DbSet<DocumentoAdministrativo> DocumentosAdministrativos => Set<DocumentoAdministrativo>();
    public DbSet<EventoDocumento> EventosDocumento => Set<EventoDocumento>();
    public DbSet<AdjuntoDocumento> AdjuntosDocumento => Set<AdjuntoDocumento>();
    public DbSet<AdjuntoDocumentoContenido> AdjuntosDocumentoContenido => Set<AdjuntoDocumentoContenido>();
```

- [ ] **Step 4: Agregar el bloque de configuración a `OnModelCreating`**

En `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`, agregar al final de `OnModelCreating` (después del bloque `modelBuilder.Entity<PermisoUsuario>(e => { ... });`, antes del cierre `}` del método):

```csharp

        // ── Documentos administrativos (módulo independiente, spec 2026-08-11) ────
        // RegistradoPorUsuarioId/EventoDocumento.UsuarioId: Restrict, mismo criterio que todo
        // el resto del modelo (Usuarios usa baja lógica, nunca DELETE físico). Índice único
        // compuesto (Tipo, Anio, Numero): última defensa contra dos funcionarios cargando el
        // mismo expediente a la vez (decisión 1 del spec) — DocumentoAdministrativoRepository
        // (Task 5) atrapa la violación por nombre de constraint y la traduce a 409.
        modelBuilder.Entity<DocumentoAdministrativo>(e =>
        {
            e.Property(d => d.Numero).IsRequired();
            e.Property(d => d.Descripcion).IsRequired();
            e.HasIndex(d => new { d.Tipo, d.Anio, d.Numero }).IsUnique();
            e.HasIndex(d => d.Estado);
            e.HasIndex(d => d.Numero);
            e.HasOne(d => d.RegistradoPor).WithMany()
                .HasForeignKey(d => d.RegistradoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventoDocumento>(e =>
        {
            e.Property(ev => ev.Texto).IsRequired();
            e.HasIndex(ev => ev.DocumentoAdministrativoId);
            // Sin nav DocumentoAdministrativo en EventoDocumento (mismo criterio que NotaTarea →
            // Tarea): la relación se configura solo del lado padre.
            e.HasOne<DocumentoAdministrativo>().WithMany(d => d.Eventos)
                .HasForeignKey(ev => ev.DocumentoAdministrativoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(ev => ev.Usuario).WithMany()
                .HasForeignKey(ev => ev.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AdjuntoDocumento>(e =>
        {
            e.Property(a => a.NombreArchivo).IsRequired();
            e.Property(a => a.ContentType).IsRequired();
            e.Property(a => a.Activo).HasDefaultValue(true);
            e.HasIndex(a => a.DocumentoAdministrativoId);
            e.HasOne(a => a.Documento).WithMany()
                .HasForeignKey(a => a.DocumentoAdministrativoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AdjuntoDocumentoContenido>().WithOne()
                .HasForeignKey<AdjuntoDocumentoContenido>(c => c.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AdjuntoDocumentoContenido>(e =>
        {
            e.Property(c => c.Contenido).IsRequired();
        });
```

- [ ] **Step 5: Generar la migración**

Run: `dotnet ef migrations add AgregaDocumentosAdministrativos --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`

Inspeccionar el archivo generado en `src/StockApp.Infrastructure/Migrations/<timestamp>_AgregaDocumentosAdministrativos.cs`: confirmar que crea las 4 tablas (`DocumentosAdministrativos`, `EventosDocumento`, `AdjuntosDocumento`, `AdjuntosDocumentoContenido`), el índice único con el nombre exacto `IX_DocumentosAdministrativos_Tipo_Anio_Numero` sobre `(Tipo, Anio, Numero)`, los índices simples sobre `Estado` y `Numero`, y las FKs con `onDelete: ReferentialAction.Restrict` (excepto `AdjuntosDocumentoContenido` → `AdjuntosDocumento`, que es `Cascade`). No transcribir el contenido a mano — se generó con el comando.

- [ ] **Step 6: Agregar las tablas nuevas al `TRUNCATE` del fixture**

En `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs`, el método `LimpiarTablas` corre ANTES de cada test y trunca una lista fija de tablas — si las tablas nuevas no se agregan acá, los tests de Task 4/5 en adelante van a arrastrar filas de una corrida anterior dentro de la misma clase de test. Reemplazar:

```csharp
    private void LimpiarTablas()
    {
        using var ctx = Fixture.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\", " +
            "\"CorridasBackup\", \"NotasTarea\", \"Tareas\", \"LotesImportacion\", " +
            "\"PermisosUsuario\" RESTART IDENTITY CASCADE;");
    }
```

por:

```csharp
    private void LimpiarTablas()
    {
        using var ctx = Fixture.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\", " +
            "\"CorridasBackup\", \"NotasTarea\", \"Tareas\", \"LotesImportacion\", " +
            "\"PermisosUsuario\", \"AdjuntosDocumentoContenido\", \"AdjuntosDocumento\", " +
            "\"EventosDocumento\", \"DocumentosAdministrativos\" RESTART IDENTITY CASCADE;");
    }
```

- [ ] **Step 7: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~DocumentoAdministrativoIndiceUnicoTests`
Expected: los 6 tests en verde (Testcontainers levanta Postgres desde cero y aplica todas las migraciones, incluida la nueva).

- [ ] **Step 8: Correr toda la suite de Infrastructure.Tests**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: todos los tests en verde — confirma que el `TRUNCATE` ampliado del Step 6 no rompió ningún test existente.

- [ ] **Step 9: Commit**

```
git add src/StockApp.Infrastructure/Persistence/AppDbContext.cs src/StockApp.Infrastructure/Migrations/ tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs tests/StockApp.Infrastructure.Tests/Persistence/DocumentoAdministrativoIndiceUnicoTests.cs
git commit -m "feat(documentos): agrega persistencia EF de DocumentoAdministrativo y migración"
```

---

## Task 5: `IDocumentoAdministrativoRepository` + `IAdjuntoDocumentoRepository` con implementaciones EF

**Files:**
- Create: `src/StockApp.Application/Interfaces/IDocumentoAdministrativoRepository.cs`
- Create: `src/StockApp.Application/Interfaces/IAdjuntoDocumentoRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/DocumentoAdministrativoRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/AdjuntoDocumentoRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/DocumentoAdministrativoRepositoryTests.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/AdjuntoDocumentoRepositoryTests.cs`

**Interfaces:**
- Consumes: `DocumentoAdministrativo`, `EventoDocumento`, `AdjuntoDocumento`, `AdjuntoDocumentoContenido` (Tasks 1-3); `AppDbContext` con los 4 `DbSet` y el índice único `IX_DocumentosAdministrativos_Tipo_Anio_Numero` (Task 4); patrón de catch de `DbUpdateException`/`PostgresException` de `GastoRepository.EsViolacionFacturaUnica` (`src/StockApp.Infrastructure/Repositories/GastoRepository.cs`, líneas 119-154) como molde exacto para traducir la violación del índice a `ReglaDeNegocioException`; `ReglaDeNegocioException` (`src/StockApp.Domain/Exceptions/ReglaDeNegocioException.cs`); patrón split metadatos/contenido de `AdjuntoRepository.AgregarAsync`/`ObtenerContenidoAsync` (`src/StockApp.Infrastructure/Repositories/AdjuntoRepository.cs`) como molde exacto para `AdjuntoDocumentoRepository`.
- Produces (consumido por el bloque B, `DocumentoAdministrativoService`/`AdjuntoDocumentoService`):

```csharp
public interface IDocumentoAdministrativoRepository
{
    Task<int> AgregarAsync(DocumentoAdministrativo documento);
    Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro);   // WHERE Estado IN (Pendiente, EnProceso)
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarCerradosAsync(FiltroDocumentos filtro);  // WHERE Estado IN (Finalizado, Anulado)
    Task ActualizarAsync(DocumentoAdministrativo documento);
    Task<bool> ExisteNumeroAsync(TipoDocumento tipo, int anio, string numero, int? excluyendoId = null);
}

public interface IAdjuntoDocumentoRepository
{
    Task<int> AgregarAsync(AdjuntoDocumento adjunto, byte[] contenido);
    Task<IReadOnlyList<AdjuntoDocumento>> ListarPorDocumentoAsync(int documentoId);
    Task<AdjuntoDocumento?> ObtenerPorIdAsync(int id);
    Task<byte[]?> ObtenerContenidoAsync(int adjuntoId);
    Task ActualizarAsync(AdjuntoDocumento adjunto);
}
```

`AgregarAsync`/`ActualizarAsync` de `DocumentoAdministrativoRepository` traducen la violación del índice único a `ReglaDeNegocioException` (mismo mensaje que va a usar el bloque B: `$"Ya existe un {documento.Tipo} {documento.Numero}/{documento.Anio}."`), replicando el catch de `GastoRepository` pero contra el constraint `IX_DocumentosAdministrativos_Tipo_Anio_Numero` (Task 4).

**Nota sobre `FiltroDocumentos`**: el bloque B (`StockApp.Application`) todavía no existe en este punto del plan — es un `record` que la Task de Application va a crear como `record FiltroDocumentos(TipoDocumento? Tipo, int? Anio, string? Texto, EstadoDocumento? Estado)`. Como `IDocumentoAdministrativoRepository` vive en `StockApp.Application/Interfaces/` y depende de ese tipo, **esta Task 5 crea también el `record FiltroDocumentos`** en `src/StockApp.Application/Documentos/DocumentosDtos.cs` (mismo criterio de ubicación que `GastoFiltro` en `src/StockApp.Application/Finanzas/GastosDtos.cs`), para que la interfaz compile de forma aislada sin esperar al bloque B. **La Task 7 (bloque B) consume este mismo tipo sin redefinirlo.**

**El filtrado Activos/Historial va en el SQL, no en memoria.** En vez de un único `ListarAsync` genérico, el repositorio expone `ListarActivosAsync` (`WHERE Estado IN (Pendiente, EnProceso)`) y `ListarCerradosAsync` (`WHERE Estado IN (Finalizado, Anulado)`), cada uno combinado con los filtros opcionales de `FiltroDocumentos` (`Tipo`, `Anio`, `Texto`, y `Estado` para acotar más — ej. Historial filtrando solo `Anulado` dentro de los cerrados). Si el historial de un año tiene miles de filas, traer todo y descartar la mitad en el servicio rompería el argumento central de D9 (no se pagina porque el filtro por año ya resuelve el volumen con una condición `WHERE`) — traer de más y filtrar en memoria reintroduce exactamente el problema que D9 evita.

- [ ] **Step 1: Escribir el test que falla — `DocumentoAdministrativoRepositoryTests`**

Crear `tests/StockApp.Infrastructure.Tests/Repositories/DocumentoAdministrativoRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class DocumentoAdministrativoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly DocumentoAdministrativoRepository _repo;

    public DocumentoAdministrativoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new DocumentoAdministrativoRepository(Context);
    }

    private async Task<int> SeedUsuarioAsync(string nombreUsuario = "operario1")
    {
        var usuario = new Usuario
        {
            NombreUsuario = nombreUsuario,
            HashContrasena = "x",
            Rol = RolUsuario.Operador,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();
        return usuario.Id;
    }

    private static DocumentoAdministrativo NuevoDocumento(
        int registradoPorUsuarioId, TipoDocumento tipo = TipoDocumento.Expediente,
        int anio = 2026, string numero = "0087", EstadoDocumento estado = EstadoDocumento.Pendiente,
        string descripcion = "Solicitud de poda de árbol en vereda") => new()
    {
        Numero = numero,
        Anio = anio,
        Tipo = tipo,
        FechaEmision = DateTime.UtcNow,
        Descripcion = descripcion,
        Estado = estado,
        RegistradoPorUsuarioId = registradoPorUsuarioId,
        FechaRegistro = DateTime.UtcNow,
    };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_TraeElDocumentoConLosEventosOrdenados()
    {
        var usuarioId = await SeedUsuarioAsync();

        var doc = NuevoDocumento(usuarioId);
        doc.AgregarEvento(usuarioId, "nueva", esAutomatico: false);
        var id = await _repo.AgregarAsync(doc);
        Context.ChangeTracker.Clear();

        // Segundo evento insertado después, con Fecha más vieja: fuerza a que el orden real
        // dependa de Fecha y no del orden de inserción/Id (mismo criterio que
        // TareaRepositoryTests.ObtenerPorId_NotasOrdenadasPorFecha).
        var releido = await _repo.ObtenerPorIdAsync(id);
        releido!.AgregarEvento(usuarioId, "vieja", esAutomatico: false);
        releido.Eventos.Last().Fecha = DateTime.UtcNow.AddMinutes(-10);
        await _repo.ActualizarAsync(releido);
        Context.ChangeTracker.Clear();

        var encontrado = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(encontrado);
        Assert.Equal("0087", encontrado!.Numero);
        Assert.Equal(2026, encontrado.Anio);
        Assert.Equal(TipoDocumento.Expediente, encontrado.Tipo);
        Assert.Equal(EstadoDocumento.Pendiente, encontrado.Estado);
        Assert.Equal(2, encontrado.Eventos.Count);
        Assert.Equal("vieja", encontrado.Eventos[0].Texto);
        Assert.Equal("nueva", encontrado.Eventos[1].Texto);
    }

    [Fact]
    public async Task ObtenerPorId_Inexistente_DevuelveNull()
    {
        var encontrado = await _repo.ObtenerPorIdAsync(999);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task ListarActivosAsync_SoloDevuelvePendienteYEnProceso()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001", estado: EstadoDocumento.Pendiente));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002", estado: EstadoDocumento.EnProceso));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0003", estado: EstadoDocumento.Finalizado));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0004", estado: EstadoDocumento.Anulado));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(null, null, null, null));

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, d => Assert.True(d.Estado is EstadoDocumento.Pendiente or EstadoDocumento.EnProceso));
    }

    [Fact]
    public async Task ListarCerradosAsync_SoloDevuelveFinalizadoYAnulado()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001", estado: EstadoDocumento.Pendiente));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002", estado: EstadoDocumento.EnProceso));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0003", estado: EstadoDocumento.Finalizado));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0004", estado: EstadoDocumento.Anulado));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarCerradosAsync(new FiltroDocumentos(null, null, null, null));

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, d => Assert.True(d.Estado is EstadoDocumento.Finalizado or EstadoDocumento.Anulado));
    }

    [Fact]
    public async Task ListarActivosAsync_FiltraPorTipo()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, tipo: TipoDocumento.Expediente, numero: "0001"));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, tipo: TipoDocumento.Oficio, numero: "0002"));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(TipoDocumento.Oficio, null, null, null));

        var unico = Assert.Single(resultado);
        Assert.Equal(TipoDocumento.Oficio, unico.Tipo);
    }

    [Fact]
    public async Task ListarActivosAsync_FiltraPorAnio()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, anio: 2025, numero: "0001"));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, anio: 2026, numero: "0002"));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(null, 2025, null, null));

        var unico = Assert.Single(resultado);
        Assert.Equal(2025, unico.Anio);
    }

    [Fact]
    public async Task ListarActivosAsync_FiltraPorTexto_BuscaEnDescripcion_CaseInsensitive()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001", descripcion: "Poda de árbol en vereda"));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002", descripcion: "Bacheo de calle Rivera"));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(null, null, "PODA", null));

        var unico = Assert.Single(resultado);
        Assert.Equal("0001", unico.Numero);
    }

    [Fact]
    public async Task ListarCerradosAsync_FiltraPorEstado_AcotaEntreFinalizadoYAnulado()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001", estado: EstadoDocumento.Finalizado));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002", estado: EstadoDocumento.Anulado));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarCerradosAsync(new FiltroDocumentos(null, null, null, EstadoDocumento.Finalizado));

        var unico = Assert.Single(resultado);
        Assert.Equal(EstadoDocumento.Finalizado, unico.Estado);
    }

    [Fact]
    public async Task ListarActivosAsync_CombinaVariosFiltrosALaVez()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(
            usuarioId, tipo: TipoDocumento.Expediente, anio: 2026, numero: "0001", estado: EstadoDocumento.Pendiente));
        await _repo.AgregarAsync(NuevoDocumento(
            usuarioId, tipo: TipoDocumento.Expediente, anio: 2026, numero: "0002", estado: EstadoDocumento.EnProceso));
        await _repo.AgregarAsync(NuevoDocumento(
            usuarioId, tipo: TipoDocumento.Oficio, anio: 2026, numero: "0003", estado: EstadoDocumento.Pendiente));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(
            new FiltroDocumentos(TipoDocumento.Expediente, 2026, null, EstadoDocumento.Pendiente));

        var unico = Assert.Single(resultado);
        Assert.Equal("0001", unico.Numero);
    }

    [Fact]
    public async Task ListarActivosAsync_SinFiltros_DevuelveTodosLosActivos()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001"));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002"));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(null, null, null, null));

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task ExisteNumeroAsync_ConDuplicadoExacto_DevuelveTrue()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId));
        Context.ChangeTracker.Clear();

        var existe = await _repo.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0087");

        Assert.True(existe);
    }

    [Fact]
    public async Task ExisteNumeroAsync_ConExcluyendoIdIgualAlPropioDocumento_DevuelveFalse()
    {
        var usuarioId = await SeedUsuarioAsync();
        var id = await _repo.AgregarAsync(NuevoDocumento(usuarioId));
        Context.ChangeTracker.Clear();

        // Es exactamente lo que permite editar un documento sin chocar consigo mismo.
        var existe = await _repo.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0087", excluyendoId: id);

        Assert.False(existe);
    }

    [Fact]
    public async Task ExisteNumeroAsync_ConExcluyendoIdDeOtroDocumento_SigueDevolviendoTrue()
    {
        var usuarioId = await SeedUsuarioAsync();
        var id = await _repo.AgregarAsync(NuevoDocumento(usuarioId));
        var otroId = await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0088"));
        Context.ChangeTracker.Clear();

        var existe = await _repo.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0087", excluyendoId: otroId);

        Assert.True(existe);
    }

    [Fact]
    public async Task AgregarAsync_NumeroDuplicado_MapeaAReglaDeNegocio()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId));
        Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _repo.AgregarAsync(NuevoDocumento(usuarioId)));

        Assert.Contains("0087", ex.Message);
        Assert.Contains("2026", ex.Message);
    }

    [Fact]
    public async Task ActualizarAsync_ChocaConOtroNumeroExistente_MapeaAReglaDeNegocio()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0087"));
        var otroId = await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0088"));
        Context.ChangeTracker.Clear();

        var otro = await _repo.ObtenerPorIdAsync(otroId);
        otro!.Numero = "0087"; // choca con el primero

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => _repo.ActualizarAsync(otro));
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~DocumentoAdministrativoRepositoryTests`
Expected: FALLA de compilación — `StockApp.Application.Documentos.FiltroDocumentos` y `StockApp.Infrastructure.Repositories.DocumentoAdministrativoRepository` no existen.

- [ ] **Step 3: Crear `FiltroDocumentos` y las interfaces en `Application`**

`src/StockApp.Application/Documentos/DocumentosDtos.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Application.Documentos;

/// <summary>
/// Filtro de listado de documentos administrativos, compartido por
/// DocumentoAdministrativoService y DocumentoApiClient (spec 2026-08-11) — mismo criterio
/// que GastoFiltro en Finanzas. Todos los campos son opcionales salvo en ListarHistorialAsync
/// (bloque de Application), que rechaza Anio nulo con ArgumentException (decisión 9).
/// </summary>
public record FiltroDocumentos(TipoDocumento? Tipo, int? Anio, string? Texto, EstadoDocumento? Estado);
```

`src/StockApp.Application/Interfaces/IDocumentoAdministrativoRepository.cs`:

```csharp
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Interfaces;

public interface IDocumentoAdministrativoRepository
{
    Task<int> AgregarAsync(DocumentoAdministrativo documento);
    Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro);   // WHERE Estado IN (Pendiente, EnProceso)
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarCerradosAsync(FiltroDocumentos filtro);  // WHERE Estado IN (Finalizado, Anulado)
    Task ActualizarAsync(DocumentoAdministrativo documento);
    Task<bool> ExisteNumeroAsync(TipoDocumento tipo, int anio, string numero, int? excluyendoId = null);
}
```

`src/StockApp.Application/Interfaces/IAdjuntoDocumentoRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IAdjuntoDocumentoRepository
{
    Task<int> AgregarAsync(AdjuntoDocumento adjunto, byte[] contenido);
    Task<IReadOnlyList<AdjuntoDocumento>> ListarPorDocumentoAsync(int documentoId);
    Task<AdjuntoDocumento?> ObtenerPorIdAsync(int id);
    Task<byte[]?> ObtenerContenidoAsync(int adjuntoId);
    Task ActualizarAsync(AdjuntoDocumento adjunto);
}
```

- [ ] **Step 4: Crear `DocumentoAdministrativoRepository`**

`src/StockApp.Infrastructure/Repositories/DocumentoAdministrativoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class DocumentoAdministrativoRepository : IDocumentoAdministrativoRepository
{
    private readonly AppDbContext _ctx;

    public DocumentoAdministrativoRepository(AppDbContext ctx) => _ctx = ctx;

    private IQueryable<DocumentoAdministrativo> ConIncludes() =>
        _ctx.DocumentosAdministrativos
            .Include(d => d.Eventos.OrderBy(e => e.Fecha).ThenBy(e => e.Id));

    public async Task<int> AgregarAsync(DocumentoAdministrativo documento)
    {
        try
        {
            _ctx.DocumentosAdministrativos.Add(documento);
            await _ctx.SaveChangesAsync();
            return documento.Id;
        }
        catch (DbUpdateException ex) when (EsViolacionNumeroUnico(ex))
        {
            throw new ReglaDeNegocioException(
                $"Ya existe un {documento.Tipo} {documento.Numero}/{documento.Anio}.");
        }
    }

    public Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id)
        => ConIncludes().FirstOrDefaultAsync(d => d.Id == id);

    private static readonly EstadoDocumento[] EstadosActivos = { EstadoDocumento.Pendiente, EstadoDocumento.EnProceso };
    private static readonly EstadoDocumento[] EstadosCerrados = { EstadoDocumento.Finalizado, EstadoDocumento.Anulado };

    // El filtrado Activos/Historial va en el SQL (WHERE Estado IN (...)), nunca en memoria: si
    // se trajera todo con un ListarAsync genérico y se filtrara en el servicio, un historial con
    // miles de filas arrastraría el archivo completo de la base para descartar la mitad en el
    // cliente — exactamente el problema que D9 evita no paginando.
    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro)
        => ListarConFiltroAsync(filtro, EstadosActivos);

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarCerradosAsync(FiltroDocumentos filtro)
        => ListarConFiltroAsync(filtro, EstadosCerrados);

    private async Task<IReadOnlyList<DocumentoAdministrativo>> ListarConFiltroAsync(
        FiltroDocumentos filtro, EstadoDocumento[] estadosPermitidos)
    {
        var query = ConIncludes().Where(d => estadosPermitidos.Contains(d.Estado));

        if (filtro.Tipo is not null)
            query = query.Where(d => d.Tipo == filtro.Tipo);
        if (filtro.Anio is not null)
            query = query.Where(d => d.Anio == filtro.Anio);
        if (filtro.Estado is not null)
            query = query.Where(d => d.Estado == filtro.Estado);
        if (!string.IsNullOrWhiteSpace(filtro.Texto))
            query = query.Where(d => EF.Functions.ILike(d.Descripcion, $"%{filtro.Texto}%"));

        return await query
            .OrderByDescending(d => d.FechaRegistro)
            .ThenByDescending(d => d.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Eventos nuevos (Id == 0, agregados por el servicio a la colección de un documento ya
    /// tracked): se agregan EXPLÍCITAMENTE al DbSet en vez de confiar en el fixup automático
    /// del change tracker — mismo criterio explícito que TareaRepository.ActualizarAsync con
    /// NotaTarea.
    /// </summary>
    public async Task ActualizarAsync(DocumentoAdministrativo documento)
    {
        try
        {
            foreach (var evento in documento.Eventos.Where(e => e.Id == 0))
            {
                evento.DocumentoAdministrativoId = documento.Id;
                _ctx.EventosDocumento.Add(evento);
            }

            _ctx.DocumentosAdministrativos.Update(documento);
            await _ctx.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (EsViolacionNumeroUnico(ex))
        {
            throw new ReglaDeNegocioException(
                $"Ya existe un {documento.Tipo} {documento.Numero}/{documento.Anio}.");
        }
    }

    public Task<bool> ExisteNumeroAsync(TipoDocumento tipo, int anio, string numero, int? excluyendoId = null)
    {
        var query = _ctx.DocumentosAdministrativos
            .Where(d => d.Tipo == tipo && d.Anio == anio && d.Numero == numero);

        if (excluyendoId is not null)
            query = query.Where(d => d.Id != excluyendoId);

        return query.AnyAsync();
    }

    /// <summary>
    /// Mismo patrón que GastoRepository.EsViolacionFacturaUnica: el índice único
    /// IX_DocumentosAdministrativos_Tipo_Anio_Numero (AppDbContext, Task 4) es la última
    /// defensa contra dos funcionarios cargando el mismo expediente a la vez. Sin este catch
    /// acá, la violación llegaría como DbUpdateException cruda y el endpoint respondería 500
    /// en vez de 409.
    /// </summary>
    private static bool EsViolacionNumeroUnico(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
           && pg.ConstraintName == "IX_DocumentosAdministrativos_Tipo_Anio_Numero";
}
```

- [ ] **Step 5: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~DocumentoAdministrativoRepositoryTests`
Expected: los 15 tests en verde.

- [ ] **Step 6: Escribir el test que falla — `AdjuntoDocumentoRepositoryTests`**

Crear `tests/StockApp.Infrastructure.Tests/Repositories/AdjuntoDocumentoRepositoryTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class AdjuntoDocumentoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly AdjuntoDocumentoRepository _repo;
    private readonly DocumentoAdministrativoRepository _repoDocumento;

    public AdjuntoDocumentoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new AdjuntoDocumentoRepository(Context);
        _repoDocumento = new DocumentoAdministrativoRepository(Context);
    }

    private async Task<int> SeedDocumentoAsync()
    {
        var usuario = new Usuario
        {
            NombreUsuario = "operario1",
            HashContrasena = "x",
            Rol = RolUsuario.Operador,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var doc = new DocumentoAdministrativo
        {
            Numero = "0087",
            Anio = 2026,
            Tipo = TipoDocumento.Expediente,
            FechaEmision = DateTime.UtcNow,
            Descripcion = "Solicitud de poda de árbol en vereda",
            RegistradoPorUsuarioId = usuario.Id,
            FechaRegistro = DateTime.UtcNow,
        };
        return await _repoDocumento.AgregarAsync(doc);
    }

    private static AdjuntoDocumento NuevoAdjunto(int documentoId, string nombreArchivo = "factura.pdf") => new()
    {
        DocumentoAdministrativoId = documentoId,
        NombreArchivo = nombreArchivo,
        ContentType = "application/pdf",
        TamanoBytes = 3,
        FechaAltaUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task AgregarAsync_PersisteMetadatosYContenidoPorSeparado()
    {
        var documentoId = await SeedDocumentoAsync();
        Context.ChangeTracker.Clear();

        var id = await _repo.AgregarAsync(NuevoAdjunto(documentoId), new byte[] { 1, 2, 3 });
        Context.ChangeTracker.Clear();

        var metadatos = await _repo.ObtenerPorIdAsync(id);
        Assert.NotNull(metadatos);
        Assert.Equal("factura.pdf", metadatos!.NombreArchivo);
        Assert.True(metadatos.Activo);

        var contenido = await _repo.ObtenerContenidoAsync(id);
        Assert.Equal(new byte[] { 1, 2, 3 }, contenido);
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_NoArrastraLosBytes()
    {
        var documentoId = await SeedDocumentoAsync();
        Context.ChangeTracker.Clear();

        await _repo.AgregarAsync(NuevoAdjunto(documentoId, "factura.pdf"), new byte[] { 1, 2, 3 });
        await _repo.AgregarAsync(NuevoAdjunto(documentoId, "nota.jpg"), new byte[] { 4, 5, 6 });
        Context.ChangeTracker.Clear();

        var listado = await _repo.ListarPorDocumentoAsync(documentoId);

        Assert.Equal(2, listado.Count);
        Assert.All(listado, a => Assert.Equal(documentoId, a.DocumentoAdministrativoId));
    }

    [Fact]
    public async Task ObtenerPorIdAsync_Inexistente_DevuelveNull()
    {
        var encontrado = await _repo.ObtenerPorIdAsync(999);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_Inexistente_DevuelveNull()
    {
        var contenido = await _repo.ObtenerContenidoAsync(999);

        Assert.Null(contenido);
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_PersisteActivoEnFalse()
    {
        var documentoId = await SeedDocumentoAsync();
        Context.ChangeTracker.Clear();

        var id = await _repo.AgregarAsync(NuevoAdjunto(documentoId), new byte[] { 1, 2, 3 });
        Context.ChangeTracker.Clear();

        var adjunto = await _repo.ObtenerPorIdAsync(id);
        adjunto!.Activo = false;
        await _repo.ActualizarAsync(adjunto);
        Context.ChangeTracker.Clear();

        var releido = await _repo.ObtenerPorIdAsync(id);
        Assert.False(releido!.Activo);
    }
}
```

- [ ] **Step 7: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~AdjuntoDocumentoRepositoryTests`
Expected: FALLA de compilación — `StockApp.Infrastructure.Repositories.AdjuntoDocumentoRepository` no existe.

- [ ] **Step 8: Crear `AdjuntoDocumentoRepository`**

`src/StockApp.Infrastructure/Repositories/AdjuntoDocumentoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class AdjuntoDocumentoRepository : IAdjuntoDocumentoRepository
{
    private readonly AppDbContext _ctx;

    public AdjuntoDocumentoRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<int> AgregarAsync(AdjuntoDocumento adjunto, byte[] contenido)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        _ctx.AdjuntosDocumento.Add(adjunto);
        await _ctx.SaveChangesAsync();

        _ctx.AdjuntosDocumentoContenido.Add(
            new AdjuntoDocumentoContenido { Id = adjunto.Id, Contenido = contenido });
        await _ctx.SaveChangesAsync();

        await tx.CommitAsync();

        return adjunto.Id;
    }

    public async Task<IReadOnlyList<AdjuntoDocumento>> ListarPorDocumentoAsync(int documentoId)
        => await _ctx.AdjuntosDocumento
            .Where(a => a.DocumentoAdministrativoId == documentoId)
            .OrderByDescending(a => a.FechaAltaUtc)
            .ThenByDescending(a => a.Id)
            .ToListAsync();

    public Task<AdjuntoDocumento?> ObtenerPorIdAsync(int id)
        => _ctx.AdjuntosDocumento.FirstOrDefaultAsync(a => a.Id == id);

    public async Task<byte[]?> ObtenerContenidoAsync(int adjuntoId)
    {
        var fila = await _ctx.AdjuntosDocumentoContenido
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == adjuntoId);
        return fila?.Contenido;
    }

    public Task ActualizarAsync(AdjuntoDocumento adjunto)
    {
        _ctx.AdjuntosDocumento.Update(adjunto);
        return _ctx.SaveChangesAsync();
    }
}
```

- [ ] **Step 9: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~AdjuntoDocumentoRepositoryTests`
Expected: los 5 tests en verde.

- [ ] **Step 10: Correr toda la suite de Infrastructure.Tests**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: todos los tests en verde — cierra el bloque de dominio y persistencia (Tasks 1-5) sin romper nada de lo existente.

- [ ] **Step 11: Commit**

```
git add src/StockApp.Application/Documentos/DocumentosDtos.cs src/StockApp.Application/Interfaces/IDocumentoAdministrativoRepository.cs src/StockApp.Application/Interfaces/IAdjuntoDocumentoRepository.cs src/StockApp.Infrastructure/Repositories/DocumentoAdministrativoRepository.cs src/StockApp.Infrastructure/Repositories/AdjuntoDocumentoRepository.cs tests/StockApp.Infrastructure.Tests/Repositories/DocumentoAdministrativoRepositoryTests.cs tests/StockApp.Infrastructure.Tests/Repositories/AdjuntoDocumentoRepositoryTests.cs
git commit -m "feat(documentos): agrega repositorios EF de DocumentoAdministrativo y AdjuntoDocumento"
```
## Task 6: Permisos del módulo y valores de auditoría

**Files:**
- Modify: `src/StockApp.Application/Authorization/Permisos.cs`
- Modify: `src/StockApp.Application/Authorization/AuthorizationService.cs`
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs`
- Modify: `tests/StockApp.Application.Tests/Authorization/PermisosTests.cs`
- Modify: `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs`

**Interfaces:**
- Consumes: nada nuevo — solo extiende `Permisos`, `AuthorizationService` y `AccionAuditada`, que ya existen.
- Produces: `Permisos.GestionarDocumentos = "documentos.gestionar"` (configurable), `Permisos.AdministrarDocumentos = "documentos.administrar"` (estructural), `AccionAuditada.{AltaDocumentoAdministrativo=52, CambioEstadoDocumento=53, ReaperturaDocumento=54, AnulacionDocumento=55, AltaNotaDocumento=56, AltaAdjuntoDocumento=57, BajaAdjuntoDocumento=58, EdicionDocumento=59}` — consumidos por Tasks 7-12 de este bloque y por los endpoints/UI de los bloques C y D.

- [ ] **Step 1: Escribir el test que falla**

Los tests existentes de `PermisosTests.cs` y `AuthorizationServiceTests.cs` tienen conteos hardcodeados (15 permisos totales, 4 estructurales, 11 configurables, 9 iniciales de Operador) que van a romper apenas se agreguen las dos constantes nuevas. Se actualizan ANTES de tocar el código de producción, así el "test que falla" es real: compila contra símbolos que todavía no existen.

Reemplazar el contenido completo de `tests/StockApp.Application.Tests/Authorization/PermisosTests.cs`:

```csharp
using StockApp.Application.Authorization;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class PermisosTests
{
    [Fact]
    public void Todos_ContieneLasDiecisieteConstantesExactas()
    {
        var esperados = new[]
        {
            Permisos.GestionarUsuarios,
            Permisos.VerReportes,
            Permisos.GestionarProductos,
            Permisos.GestionarTablasMaestras,
            Permisos.RegistrarMovimientos,
            Permisos.RecalcularStock,
            Permisos.VerFinanzas,
            Permisos.GestionarMaestrosFinanzas,
            Permisos.RegistrarGastos,
            Permisos.RegistrarPagos,
            Permisos.RegistrarIngresos,
            Permisos.ImportarPlanillas,
            Permisos.GestionarDiagnostico,
            Permisos.GestionarTareas,
            Permisos.AdministrarTareas,
            Permisos.GestionarDocumentos,
            Permisos.AdministrarDocumentos,
        };

        Assert.Equal(esperados.Length, Permisos.Todos.Count);
        foreach (var permiso in esperados)
            Assert.Contains(permiso, Permisos.Todos);
    }

    [Fact]
    public void Todos_NoTieneDuplicados()
    {
        Assert.Equal(Permisos.Todos.Count, Permisos.Todos.Distinct().Count());
    }
}
```

Reemplazar el contenido completo de `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs`:

```csharp
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class AuthorizationServiceTests
{
    private readonly AuthorizationService _svc = new();

    // ── Verificar(ICurrentSession, string) — nuevo overload (spec 2026-08-10) ──────────────

    private sealed class SesionFake : ICurrentSession
    {
        public bool EstaAutenticado { get; set; } = true;
        public UsuarioSesion? UsuarioActual { get; set; }
        public RolUsuario? RolActual { get; set; }
        public IReadOnlySet<string> PermisosActuales { get; set; } = new HashSet<string>();
        public void IniciarSesion(Usuario usuario) => throw new NotSupportedException();
        public void CerrarSesion() => throw new NotSupportedException();
        public void EstablecerPermisos(IReadOnlySet<string> permisos) => PermisosActuales = permisos;
    }

    [Fact]
    public void VerificarConSesion_SinAutenticar_LanzaUnauthorized()
    {
        var sesion = new SesionFake { EstaAutenticado = false };

        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(sesion, Permisos.VerFinanzas));
    }

    [Fact]
    public void VerificarConSesion_Admin_PasaSiempre_SinConsultarPermisosActuales()
    {
        // PermisosActuales queda deliberadamente null-like (vacío): si Verificar lo consultara
        // para Admin, este test lo detectaría por Assert.True en vez de simplemente no lanzar.
        var sesion = new SesionFake { RolActual = RolUsuario.Admin, PermisosActuales = new HashSet<string>() };

        var ex = Record.Exception(() => _svc.Verificar(sesion, Permisos.GestionarUsuarios));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.ImportarPlanillas)]
    [InlineData(Permisos.GestionarDiagnostico)]
    [InlineData(Permisos.AdministrarTareas)]
    [InlineData(Permisos.AdministrarDocumentos)]
    public void VerificarConSesion_Operador_LosCincoEstructurales_RechazanSiempre(string permisoEstructural)
    {
        var sesion = new SesionFake { RolActual = RolUsuario.Operador, PermisosActuales = new HashSet<string>() };

        Assert.Throws<UnauthorizedAccessException>(() => _svc.Verificar(sesion, permisoEstructural));
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.ImportarPlanillas)]
    [InlineData(Permisos.GestionarDiagnostico)]
    [InlineData(Permisos.AdministrarTareas)]
    [InlineData(Permisos.AdministrarDocumentos)]
    public void VerificarConSesion_SesionEnvenenada_LosCincoEstructuralesRechazanIgual(string permisoEstructural)
    {
        // El test más importante de esta clase: aunque PermisosActuales CONTENGA el permiso
        // estructural (una fila colada por error, o un bug futuro en PUT /usuarios/{id}/permisos),
        // Verificar tiene que rechazar igual. Este es el corte que el spec marca como "el punto
        // de falla más peligroso" — si se invirtiera el orden, un Operador con una fila colada
        // podría auto-otorgarse GestionarUsuarios y desde ahí cualquier otro permiso.
        var sesionEnvenenada = new SesionFake
        {
            RolActual = RolUsuario.Operador,
            PermisosActuales = new HashSet<string> { permisoEstructural },
        };

        Assert.Throws<UnauthorizedAccessException>(() => _svc.Verificar(sesionEnvenenada, permisoEstructural));
    }

    [Fact]
    public void VerificarConSesion_Operador_ConElPermisoEnPermisosActuales_Pasa()
    {
        var sesion = new SesionFake
        {
            RolActual = RolUsuario.Operador,
            PermisosActuales = new HashSet<string> { Permisos.VerFinanzas },
        };

        var ex = Record.Exception(() => _svc.Verificar(sesion, Permisos.VerFinanzas));

        Assert.Null(ex);
    }

    [Fact]
    public void VerificarConSesion_Operador_SinElPermisoEnPermisosActuales_Lanza()
    {
        var sesion = new SesionFake
        {
            RolActual = RolUsuario.Operador,
            PermisosActuales = new HashSet<string> { Permisos.GestionarProductos },
        };

        Assert.Throws<UnauthorizedAccessException>(() => _svc.Verificar(sesion, Permisos.VerFinanzas));
    }

    [Fact]
    public void PermisosEstructuralesAdmin_ContieneExactamenteLosCincoDocumentados()
    {
        Assert.Equal(5, AuthorizationService.PermisosEstructuralesAdmin.Count);
        Assert.Contains(Permisos.GestionarUsuarios, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.ImportarPlanillas, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.GestionarDiagnostico, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.AdministrarTareas, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.AdministrarDocumentos, AuthorizationService.PermisosEstructuralesAdmin);
    }

    [Fact]
    public void PermisosConfigurables_TieneLos12RestantesYNoIntersecaConLosEstructurales()
    {
        Assert.Equal(12, AuthorizationService.PermisosConfigurables.Count);
        Assert.Contains(Permisos.GestionarDocumentos, AuthorizationService.PermisosConfigurables);
        foreach (var permiso in AuthorizationService.PermisosConfigurables)
            Assert.DoesNotContain(permiso, AuthorizationService.PermisosEstructuralesAdmin);
    }

    [Fact]
    public void PermisosInicialesOperador_TieneExactamenteLos10DeAccionesOperadorEnOrden()
    {
        Assert.Equal(new[]
        {
            Permisos.GestionarProductos,
            Permisos.RegistrarMovimientos,
            Permisos.RecalcularStock,
            Permisos.VerFinanzas,
            Permisos.GestionarMaestrosFinanzas,
            Permisos.RegistrarGastos,
            Permisos.RegistrarPagos,
            Permisos.RegistrarIngresos,
            Permisos.GestionarTareas,
            Permisos.GestionarDocumentos,
        }, AuthorizationService.PermisosInicialesOperador);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~PermisosTests|FullyQualifiedName~AuthorizationServiceTests"`
Expected: FAIL — no compila (`Permisos.GestionarDocumentos` y `Permisos.AdministrarDocumentos` no existen todavía).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Authorization/Permisos.cs
// Agregar DEBAJO de "AdministrarTareas" (antes del comentario de "Lista explícita..."):

    // Documentos administrativos (spec 2026-08-11) — módulo independiente. GestionarDocumentos:
    // registrar, editar, listar, transicionar (iniciar/volver a pendiente/finalizar), notas y
    // adjuntos (agregar/listar/descargar) — Admin Y Operador. AdministrarDocumentos: anular,
    // reabrir y quitar adjuntos — solo Admin, mismo criterio que AdministrarTareas.
    public const string GestionarDocumentos = "documentos.gestionar";

    /// <summary>Anular, reabrir y quitar adjuntos: decide sobre el cierre/apertura de un trámite y sobre evidencia documental ya cargada — solo Admin.</summary>
    public const string AdministrarDocumentos = "documentos.administrar";
```

```csharp
// src/StockApp.Application/Authorization/Permisos.cs
// Agregar las dos constantes nuevas al final de la lista "Todos":

    public static readonly IReadOnlyList<string> Todos =
    [
        GestionarUsuarios,
        VerReportes,
        GestionarProductos,
        GestionarTablasMaestras,
        RegistrarMovimientos,
        RecalcularStock,
        VerFinanzas,
        GestionarMaestrosFinanzas,
        RegistrarGastos,
        RegistrarPagos,
        RegistrarIngresos,
        ImportarPlanillas,
        GestionarDiagnostico,
        GestionarTareas,
        AdministrarTareas,
        GestionarDocumentos,
        AdministrarDocumentos,
    ];
```

```csharp
// src/StockApp.Application/Authorization/AuthorizationService.cs
// Agregar "Permisos.AdministrarDocumentos," al HashSet PermisosEstructuralesAdmin:

    public static readonly IReadOnlySet<string> PermisosEstructuralesAdmin = new HashSet<string>
    {
        Permisos.GestionarUsuarios,
        Permisos.ImportarPlanillas,
        Permisos.GestionarDiagnostico,
        Permisos.AdministrarTareas,
        Permisos.AdministrarDocumentos,
    };
```

```csharp
// src/StockApp.Application/Authorization/AuthorizationService.cs
// Agregar "Permisos.GestionarDocumentos," al final de PermisosInicialesOperador:

    public static readonly IReadOnlyList<string> PermisosInicialesOperador =
    [
        Permisos.GestionarProductos,
        Permisos.RegistrarMovimientos,
        Permisos.RecalcularStock,
        Permisos.VerFinanzas,
        Permisos.GestionarMaestrosFinanzas,
        Permisos.RegistrarGastos,
        Permisos.RegistrarPagos,
        Permisos.RegistrarIngresos,
        Permisos.GestionarTareas,
        Permisos.GestionarDocumentos,
    ];
```

```csharp
// src/StockApp.Domain/Enums/AccionAuditada.cs
// Agregar al final, después de "ModificacionPermisosUsuario = 51,":

    // ── Documentos administrativos (append-only a partir de 52) ──────────────
    AltaDocumentoAdministrativo = 52,
    CambioEstadoDocumento       = 53,
    ReaperturaDocumento         = 54,
    AnulacionDocumento          = 55,
    AltaNotaDocumento           = 56,
    AltaAdjuntoDocumento        = 57,
    BajaAdjuntoDocumento        = 58,
    EdicionDocumento            = 59,
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~PermisosTests|FullyQualifiedName~AuthorizationServiceTests"`
Expected: PASS — 19 tests verdes (2 de `PermisosTests` + 17 de `AuthorizationServiceTests`: 7 `[Fact]` + 5+5 de los dos `[Theory]`).

- [ ] **Step 5: Correr la suite completa de Application para confirmar que no rompió nada más**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: PASS — todos los tests verdes (ningún otro archivo depende de los conteos que se acaban de mover).

- [ ] **Step 6: Commit**
```bash
git add src/StockApp.Application/Authorization/Permisos.cs \
        src/StockApp.Application/Authorization/AuthorizationService.cs \
        src/StockApp.Domain/Enums/AccionAuditada.cs \
        tests/StockApp.Application.Tests/Authorization/PermisosTests.cs \
        tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs
git commit -m "feat(documentos): agrega permisos y valores de auditoria del modulo"
```

---

## Task 7: Servicio — alta y listados, con permisos

**Files:**
- Modify: `src/StockApp.Application/Documentos/DocumentosDtos.cs` (creado por la Task 5 con `FiltroDocumentos`; esta tarea le agrega `DatosEdicionDocumento`)
- Create: `src/StockApp.Application/Documentos/IDocumentoAdministrativoService.cs`
- Create: `src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs`
- Test: `tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs`

**Interfaces:**
- Consumes (del bloque A, ya existentes en el repo cuando arranca esta tarea): entidad `StockApp.Domain.Entities.DocumentoAdministrativo` (`Id`, `Numero`, `Anio`, `Tipo`, `FechaEmision`, `Descripcion`, `Estado`, `RegistradoPorUsuarioId`, `FechaRegistro`, `FechaCierre`, `Eventos`, `EsActivo`, `EsCerrado`, `AgregarEvento(int, string, bool, EstadoDocumento?, EstadoDocumento?)`); enums `TipoDocumento`, `EstadoDocumento`; `StockApp.Application.Interfaces.IDocumentoAdministrativoRepository` con `AgregarAsync(DocumentoAdministrativo)`, `ObtenerPorIdAsync(int)`, `ListarActivosAsync(FiltroDocumentos)`, `ListarCerradosAsync(FiltroDocumentos)`, `ActualizarAsync(DocumentoAdministrativo)`, `ExisteNumeroAsync(TipoDocumento, int, string, int?)`; `record FiltroDocumentos(TipoDocumento? Tipo, int? Anio, string? Texto, EstadoDocumento? Estado)` (`StockApp.Application.Documentos`, ya creado por la Task 5 — esta tarea lo reutiliza, no lo redefine). También `Permisos.GestionarDocumentos` (Task 6) y `AccionAuditada.AltaDocumentoAdministrativo` (Task 6).
- Produces: `record DatosEdicionDocumento(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion)`, `IDocumentoAdministrativoService` (11 métodos completos en la interfaz; solo 4 implementados en esta tarea) y `DocumentoAdministrativoService` — consumidos por Tasks 8-12 de este bloque y por los endpoints del bloque C.

**El filtrado Activos/Historial va en el SQL (Task 5), no en memoria.** El servicio no trae todo y descarta la mitad: `ListarActivosAsync` delega directamente en `_repo.ListarActivosAsync(filtro)` (`WHERE Estado IN (Pendiente, EnProceso)`) y `ListarHistorialAsync` en `_repo.ListarCerradosAsync(filtro)` (`WHERE Estado IN (Finalizado, Anulado)`) — sin ningún `.Where(d => d.EsActivo)`/`.Where(d => d.EsCerrado)` del lado del servicio. Traer de más y filtrar en memoria rompería el argumento central de D9 (no se pagina porque el filtro por año ya resuelve el volumen con una condición `WHERE`): un historial con miles de filas arrastraría el archivo completo de la base para descartar la mitad en el cliente.

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Auth;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Application.Tests.Documentos;

public class DocumentoAdministrativoServiceTests
{
    private static (DocumentoAdministrativoService Svc, Mock<IDocumentoAdministrativoRepository> Repo,
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth, Mock<IAuditLogger> Audit)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo    = new Mock<IDocumentoAdministrativoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthorizationService>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));

        var svc = new DocumentoAdministrativoService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    private static DocumentoAdministrativo NuevoDocumento(EstadoDocumento estado = EstadoDocumento.Pendiente) => new()
    {
        Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = new DateTime(2026, 1, 15), Descripcion = "Solicitud de poda de árbol", Estado = estado,
    };

    // ── RegistrarAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.RegistrarAsync(NuevoDocumento()));

        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_NumeroVacio_LanzaArgumentException()
    {
        var ctx = Crear();
        var documento = NuevoDocumento();
        documento.Numero = "   ";

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.RegistrarAsync(documento));
    }

    [Fact]
    public async Task RegistrarAsync_DescripcionVacia_LanzaArgumentException()
    {
        var ctx = Crear();
        var documento = NuevoDocumento();
        documento.Descripcion = "   ";

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.RegistrarAsync(documento));
    }

    [Fact]
    public async Task RegistrarAsync_NumeroDuplicado_LanzaReglaDeNegocioSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0087", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.RegistrarAsync(NuevoDocumento()));

        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_DatosValidos_DelegaAlRepoYDevuelveId()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(It.IsAny<TipoDocumento>(), It.IsAny<int>(), It.IsAny<string>(), null))
            .ReturnsAsync(false);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>())).ReturnsAsync(42);

        var id = await ctx.Svc.RegistrarAsync(NuevoDocumento());

        Assert.Equal(42, id);
        ctx.Repo.Verify(r => r.AgregarAsync(It.Is<DocumentoAdministrativo>(d =>
            d.Numero == "0087" && d.Estado == EstadoDocumento.Pendiente)), Times.Once);
    }

    [Fact]
    public async Task RegistrarAsync_SeteaRegistradoPorYFechaRegistroDesdeLaSesionNoDelInput()
    {
        var ctx = Crear(idSesion: 7);
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(It.IsAny<TipoDocumento>(), It.IsAny<int>(), It.IsAny<string>(), null))
            .ReturnsAsync(false);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>())).ReturnsAsync(1);
        var documento = NuevoDocumento();
        documento.RegistradoPorUsuarioId = 999; // valor espurio: el servicio debe ignorarlo

        await ctx.Svc.RegistrarAsync(documento);

        Assert.Equal(7, documento.RegistradoPorUsuarioId);
        Assert.True((DateTime.UtcNow - documento.FechaRegistro) < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RegistrarAsync_SiembraEventoInicialAutomatico()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(It.IsAny<TipoDocumento>(), It.IsAny<int>(), It.IsAny<string>(), null))
            .ReturnsAsync(false);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>())).ReturnsAsync(1);
        var documento = NuevoDocumento();

        await ctx.Svc.RegistrarAsync(documento);

        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
    }

    [Fact]
    public async Task RegistrarAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 3);
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(It.IsAny<TipoDocumento>(), It.IsAny<int>(), It.IsAny<string>(), null))
            .ReturnsAsync(false);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>())).ReturnsAsync(9);

        await ctx.Svc.RegistrarAsync(NuevoDocumento());

        ctx.Audit.Verify(a => a.RegistrarAsync(
            3, AccionAuditada.AltaDocumentoAdministrativo, "DocumentoAdministrativo", 9, It.IsAny<string>()),
            Times.Once);
    }

    // ── ListarActivosAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ListarActivosAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.ListarActivosAsync(new FiltroDocumentos(null, null, null, null)));
    }

    [Fact]
    public async Task ListarActivosAsync_DelegaEnRepoListarActivosAsync()
    {
        // El filtrado Pendiente/EnProceso va en el SQL (Task 5, IDocumentoAdministrativoRepository.
        // ListarActivosAsync), no en memoria: el servicio delega el filtro tal cual, sin volver a
        // filtrar por EsActivo sobre lo que devuelve el repo.
        var ctx = Crear();
        var pendiente = NuevoDocumento(EstadoDocumento.Pendiente);
        var enProceso = NuevoDocumento(EstadoDocumento.EnProceso);
        var filtro = new FiltroDocumentos(null, null, null, null);
        ctx.Repo.Setup(r => r.ListarActivosAsync(filtro))
            .ReturnsAsync(new List<DocumentoAdministrativo> { pendiente, enProceso });

        var resultado = await ctx.Svc.ListarActivosAsync(filtro);

        Assert.Equal(2, resultado.Count);
        ctx.Repo.Verify(r => r.ListarActivosAsync(filtro), Times.Once);
        ctx.Repo.Verify(r => r.ListarCerradosAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    // ── ListarHistorialAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ListarHistorialAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.ListarHistorialAsync(new FiltroDocumentos(null, 2026, null, null)));
    }

    [Fact]
    public async Task ListarHistorialAsync_AnioNulo_LanzaArgumentException()
    {
        // D9: es un request mal formado (400), no un ReglaDeNegocioException (409).
        var ctx = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => ctx.Svc.ListarHistorialAsync(new FiltroDocumentos(null, null, null, null)));

        ctx.Repo.Verify(r => r.ListarCerradosAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    [Fact]
    public async Task ListarHistorialAsync_DelegaEnRepoListarCerradosAsync()
    {
        // Mismo criterio que ListarActivosAsync: el filtrado Finalizado/Anulado va en el SQL
        // (Task 5, IDocumentoAdministrativoRepository.ListarCerradosAsync), no en memoria.
        var ctx = Crear();
        var finalizado = NuevoDocumento(EstadoDocumento.Finalizado);
        var anulado = NuevoDocumento(EstadoDocumento.Anulado);
        var filtro = new FiltroDocumentos(null, 2026, null, null);
        ctx.Repo.Setup(r => r.ListarCerradosAsync(filtro))
            .ReturnsAsync(new List<DocumentoAdministrativo> { finalizado, anulado });

        var resultado = await ctx.Svc.ListarHistorialAsync(filtro);

        Assert.Equal(2, resultado.Count);
        ctx.Repo.Verify(r => r.ListarCerradosAsync(filtro), Times.Once);
        ctx.Repo.Verify(r => r.ListarActivosAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    // ── ObtenerPorIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerPorIdAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ObtenerPorIdAsync(1));
    }

    [Fact]
    public async Task ObtenerPorIdAsync_DelegaAlRepo()
    {
        var ctx = Crear();
        var documento = NuevoDocumento();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        var resultado = await ctx.Svc.ObtenerPorIdAsync(1);

        Assert.Same(documento, resultado);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: FAIL — no compila (`DocumentoAdministrativoService`, `IDocumentoAdministrativoService`, `FiltroDocumentos` no existen todavía).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Documentos/DocumentosDtos.cs
// Agregar AL FINAL del archivo que ya creó la Task 5 con `FiltroDocumentos` — NO redefinir
// FiltroDocumentos acá, esta tarea solo agrega DatosEdicionDocumento:

/// <summary>Datos editables de un documento activo (D1): Numero, Anio, Tipo, FechaEmision,
/// Descripcion. RegistradoPorUsuarioId/FechaRegistro/Estado/FechaCierre NO son editables acá.</summary>
public record DatosEdicionDocumento(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion);
```

```csharp
// src/StockApp.Application/Documentos/IDocumentoAdministrativoService.cs
using StockApp.Domain.Entities;

namespace StockApp.Application.Documentos;

/// <summary>
/// Documentos administrativos (expedientes, oficios, suministros) — spec 2026-08-11. Molde
/// exacto de ITareaService: métodos por acción, cada uno con su propio permiso, su propia
/// validación y su propia línea de auditoría — nunca un CambiarEstado genérico.
/// </summary>
public interface IDocumentoAdministrativoService
{
    /// <summary>Alta. RegistradoPorUsuarioId y FechaRegistro se completan SIEMPRE desde la
    /// sesión, nunca desde <paramref name="documento"/>. Valida número único (Tipo, Anio,
    /// Numero) y siembra el evento inicial automático.</summary>
    Task<int> RegistrarAsync(DocumentoAdministrativo documento);

    /// <summary>Corrige Numero/Anio/Tipo/FechaEmision/Descripcion sobre un documento activo
    /// (D1). Revalida el número único si cambia la clave. Implementado en Task 11.</summary>
    Task EditarAsync(int id, DatosEdicionDocumento datos);

    /// <summary>Documentos con EsActivo (Pendiente/EnProceso). Solapa Activos.</summary>
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro);

    /// <summary>Documentos con EsCerrado (Finalizado/Anulado). Exige Anio no nulo en el
    /// filtro (ArgumentException, 400) — D9. Solapa Historial, carga perezosa.</summary>
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarHistorialAsync(FiltroDocumentos filtro);

    Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id);

    /// <summary>Pendiente → EnProceso. Rechaza con ReglaDeNegocioException si el documento no
    /// está Pendiente — guarda de estado origen simétrica a la de ReabrirAsync (D4/D8): en el
    /// dominio, Finalizado/Anulado → EnProceso también son válidas (son la reapertura), así
    /// que sin esta guarda esta acción podría reabrir un documento cerrado sin pasar por
    /// documentos.administrar. Implementado en Task 8.</summary>
    Task IniciarProcesoAsync(int id);

    /// <summary>EnProceso → Pendiente. Análogo exacto de TareaService.SoltarAsync. Implementado en Task 8.</summary>
    Task VolverAPendienteAsync(int id);

    /// <summary>EnProceso → Finalizado. Sella FechaCierre. Implementado en Task 8.</summary>
    Task FinalizarAsync(int id);

    /// <summary>Nota manual, append-only. Implementado en Task 9.</summary>
    Task AgregarNotaAsync(int id, string texto);

    /// <summary>Cualquier estado activo → Anulado. Solo Admin, motivo obligatorio, sella
    /// FechaCierre. Implementado en Task 10.</summary>
    Task AnularAsync(int id, string motivo);

    /// <summary>Finalizado/Anulado → EnProceso. Solo Admin, motivo obligatorio, exige que el
    /// documento esté EsCerrado, limpia FechaCierre. Implementado en Task 10.</summary>
    Task ReabrirAsync(int id, string motivo);
}
```

```csharp
// src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Documentos;

/// <summary>
/// Servicio de documentos administrativos. Patrón: auth → validación → mutación vía la
/// entidad (la máquina de estados vive en DocumentoAdministrativo.CambiarEstado) →
/// FechaCierre sellada/limpiada a mano cuando corresponde (D8, el dominio no toca fechas) →
/// persistencia → auditoría. Mismo orden que TareaService. IniciarProceso/VolverAPendiente/
/// Finalizar se implementan en Task 8; AgregarNota en Task 9; Anular/Reabrir en Task 10
/// (agregan el chequeo de Permisos.AdministrarDocumentos); Editar en Task 11.
/// </summary>
public class DocumentoAdministrativoService : IDocumentoAdministrativoService
{
    private readonly IDocumentoAdministrativoRepository _repo;
    private readonly ICurrentSession                    _session;
    private readonly IAuthorizationService               _auth;
    private readonly IAuditLogger                        _audit;

    public DocumentoAdministrativoService(
        IDocumentoAdministrativoRepository repo, ICurrentSession session,
        IAuthorizationService auth, IAuditLogger audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> RegistrarAsync(DocumentoAdministrativo documento)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        if (string.IsNullOrWhiteSpace(documento.Numero))
            throw new ArgumentException("El número del documento es obligatorio.", nameof(documento.Numero));
        if (string.IsNullOrWhiteSpace(documento.Descripcion))
            throw new ArgumentException("La descripción del documento es obligatoria.", nameof(documento.Descripcion));

        if (await _repo.ExisteNumeroAsync(documento.Tipo, documento.Anio, documento.Numero, null))
            throw new ReglaDeNegocioException(
                $"Ya existe un {documento.Tipo} {documento.Numero}/{documento.Anio}.");

        documento.Estado                 = EstadoDocumento.Pendiente;
        documento.RegistradoPorUsuarioId = _session.UsuarioActual!.Id;
        documento.FechaRegistro          = DateTime.UtcNow;
        documento.FechaCierre            = null;

        documento.AgregarEvento(
            _session.UsuarioActual!.Id,
            $"Alta del documento — {documento.Tipo} {documento.Numero}/{documento.Anio}.",
            esAutomatico: true);

        var id = await _repo.AgregarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaDocumentoAdministrativo, "DocumentoAdministrativo", id,
            $"{documento.Tipo} {documento.Numero}/{documento.Anio}");

        return id;
    }

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        // El filtrado Pendiente/EnProceso ya viene resuelto por el repositorio (WHERE Estado IN
        // (...), Task 5) — el servicio no vuelve a filtrar por EsActivo en memoria.
        return _repo.ListarActivosAsync(filtro);
    }

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarHistorialAsync(FiltroDocumentos filtro)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        // D9: el año es obligatorio en el historial — es lo que sostiene la decisión de no
        // paginar. Ausente es un request mal formado (400), no un conflicto de negocio (409).
        if (filtro.Anio is null)
            throw new ArgumentException("El año es obligatorio para consultar el historial.", nameof(filtro));

        // Mismo criterio que ListarActivosAsync: el filtrado Finalizado/Anulado va en el SQL
        // (WHERE Estado IN (...), Task 5), no en memoria.
        return _repo.ListarCerradosAsync(filtro);
    }

    public async Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);
        return await _repo.ObtenerPorIdAsync(id);
    }

    public Task IniciarProcesoAsync(int id) => throw new NotImplementedException();     // Task 8
    public Task VolverAPendienteAsync(int id) => throw new NotImplementedException();   // Task 8
    public Task FinalizarAsync(int id) => throw new NotImplementedException();          // Task 8
    public Task AgregarNotaAsync(int id, string texto) => throw new NotImplementedException(); // Task 9
    public Task AnularAsync(int id, string motivo) => throw new NotImplementedException();     // Task 10
    public Task ReabrirAsync(int id, string motivo) => throw new NotImplementedException();    // Task 10
    public Task EditarAsync(int id, DatosEdicionDocumento datos) => throw new NotImplementedException(); // Task 11
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: PASS — 15 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Documentos/DocumentosDtos.cs \
        src/StockApp.Application/Documentos/IDocumentoAdministrativoService.cs \
        src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs \
        tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs
git commit -m "feat(documentos): agrega servicio con alta y listados"
```

---

## Task 8: Transiciones de gestión — iniciar proceso, volver a pendiente, finalizar

**Files:**
- Modify: `src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs`
- Modify: `tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs`

**Interfaces:**
- Consumes: `DocumentoAdministrativo.CambiarEstado(EstadoDocumento)` (lanza `ReglaDeNegocioException` si la transición no está en la tabla del dominio — bloque A), `DocumentoAdministrativo.AgregarEvento(...)`, `AccionAuditada.CambioEstadoDocumento` (Task 6).
- Produces: `IniciarProcesoAsync`, `VolverAPendienteAsync`, `FinalizarAsync` implementados — consumidos por el endpoint `POST /documentos/{id}/iniciar` etc. (bloque C) y por el gating de botones `PuedeIniciar`/`PuedeFinalizar` (bloque D).

**Hallazgo (no viene del spec, encontrado al implementar):** la tabla de transiciones (D4) hace que `Finalizado -> EnProceso` y `Anulado -> EnProceso` sean válidas en el dominio (son la reapertura) — el mismo estado destino que usa `IniciarProcesoAsync` (`Pendiente -> EnProceso`). El spec solo documenta la guarda de estado origen para `ReabrirAsync` (contra `Pendiente`); acá hace falta la guarda simétrica en `IniciarProcesoAsync` (exigir `Estado == Pendiente` antes de llamar `CambiarEstado`), o cualquier Operador con `documentos.gestionar` podría reabrir un documento `Finalizado`/`Anulado` sin motivo y sin `documentos.administrar`, simplemente llamando al endpoint de "iniciar proceso". Bloque D: si `DocumentoFila.PuedeIniciar` solo consulta `Documento.PuedeTransicionarA(EnProceso)` (como sugiere el spec para `PuedeIniciar`/`PuedeFinalizar`), ese botón también va a aparecer habilitado sobre un documento cerrado — conviene que `PuedeIniciar` agregue `Documento.Estado == EstadoDocumento.Pendiente`, no solo `PuedeTransicionarA`.

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs`, dentro de la clase (después de los tests de `ObtenerPorIdAsync`):

```csharp
    // ── IniciarProcesoAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task IniciarProcesoAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.IniciarProcesoAsync(1));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task IniciarProcesoAsync_DocumentoInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((DocumentoAdministrativo?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => ctx.Svc.IniciarProcesoAsync(1));
    }

    [Fact]
    public async Task IniciarProcesoAsync_DesdePendiente_CambiaAEnProcesoYGeneraEventoAutomatico()
    {
        var ctx = Crear(idSesion: 3);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await ctx.Svc.IniciarProcesoAsync(1);

        Assert.Equal(EstadoDocumento.EnProceso, documento.Estado);
        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Equal(EstadoDocumento.Pendiente, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.EnProceso, evento.EstadoNuevo);
        ctx.Repo.Verify(r => r.ActualizarAsync(documento), Times.Once);
    }

    [Fact]
    public async Task IniciarProcesoAsync_DesdeFinalizado_LanzaReglaDeNegocio()
    {
        // Guarda de estado origen (ver nota de Interfaces más abajo): Finalizado -> EnProceso
        // es una transición válida en la tabla del dominio (es la reapertura), así que sin
        // esta guarda explícita CambiarEstado no rechazaría esto y un Operador con solo
        // documentos.gestionar terminaría reabriendo un documento cerrado.
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Finalizado);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.IniciarProcesoAsync(1));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task IniciarProcesoAsync_DesdeAnulado_LanzaReglaDeNegocio()
    {
        // Mismo caso que el de arriba: Anulado -> EnProceso también es la reapertura.
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Anulado);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.IniciarProcesoAsync(1));
    }

    [Fact]
    public async Task IniciarProcesoAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.IniciarProcesoAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", 5, It.IsAny<string>()), Times.Once);
    }

    // ── VolverAPendienteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task VolverAPendienteAsync_DesdeEnProceso_CambiaAPendienteYGeneraEventoAutomatico()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.VolverAPendienteAsync(5);

        Assert.Equal(EstadoDocumento.Pendiente, documento.Estado);
        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Equal(EstadoDocumento.EnProceso, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.Pendiente, evento.EstadoNuevo);
    }

    [Fact]
    public async Task VolverAPendienteAsync_DesdePendiente_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.VolverAPendienteAsync(5));
    }

    [Fact]
    public async Task VolverAPendienteAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.VolverAPendienteAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", 5, It.IsAny<string>()), Times.Once);
    }

    // ── FinalizarAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task FinalizarAsync_DesdeEnProceso_CambiaAFinalizadoYSellaFechaCierre()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.FinalizarAsync(5);

        Assert.Equal(EstadoDocumento.Finalizado, documento.Estado);
        Assert.NotNull(documento.FechaCierre);
        Assert.True((DateTime.UtcNow - documento.FechaCierre!.Value) < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FinalizarAsync_GeneraEventoAutomaticoConEstados()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.FinalizarAsync(5);

        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Equal(EstadoDocumento.EnProceso, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.Finalizado, evento.EstadoNuevo);
    }

    [Fact]
    public async Task FinalizarAsync_DesdePendiente_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.FinalizarAsync(5));
    }

    [Fact]
    public async Task FinalizarAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.FinalizarAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", 5, It.IsAny<string>()), Times.Once);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: FAIL — `IniciarProcesoAsync`/`VolverAPendienteAsync`/`FinalizarAsync` lanzan `NotImplementedException`.

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs
// Reemplazar las tres líneas "=> throw new NotImplementedException();  // Task 8" por:

    public async Task IniciarProcesoAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        // Guarda de estado ORIGEN, simétrica a la de ReabrirAsync (D4/D8 del spec): en la
        // tabla del dominio, Finalizado -> EnProceso y Anulado -> EnProceso también son
        // transiciones válidas (son la reapertura). Si esta acción se limitara a llamar
        // CambiarEstado(EnProceso) sin verificar primero que el documento esté Pendiente,
        // CambiarEstado NO rechazaría iniciar el proceso de un documento cerrado — y
        // cualquier Operador con documentos.gestionar (sin documentos.administrar) podría
        // reabrir un documento cerrado sin motivo y sin pasar por ReabrirAsync. El spec solo
        // documenta la guarda simétrica del lado de ReabrirAsync; esta es la misma guarda
        // aplicada del lado de IniciarProcesoAsync.
        if (documento.Estado != EstadoDocumento.Pendiente)
            throw new ReglaDeNegocioException(
                $"No se puede iniciar el proceso de un documento en estado '{documento.Estado}': no está pendiente.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.EnProceso);

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"{estadoAnterior} → {documento.Estado}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}");
    }

    public async Task VolverAPendienteAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.Pendiente);

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"{estadoAnterior} → {documento.Estado}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}");
    }

    public async Task FinalizarAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.Finalizado);
        documento.FechaCierre = DateTime.UtcNow;   // D8: lo sella el servicio, no la entidad.

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"{estadoAnterior} → {documento.Estado}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}");
    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: PASS — 27 tests verdes (15 de Task 7 + 12 nuevos).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs \
        tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs
git commit -m "feat(documentos): implementa iniciar proceso, volver a pendiente y finalizar"
```

---

## Task 9: Notas manuales

**Files:**
- Modify: `src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs`
- Modify: `tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs`

**Interfaces:**
- Consumes: `DocumentoAdministrativo.AgregarEvento(...)`, `AccionAuditada.AltaNotaDocumento` (Task 6).
- Produces: `AgregarNotaAsync` implementado — consumido por `POST /documentos/{id}/notas` (bloque C) y el hilo de eventos de `DocumentoFormViewModel` (bloque D).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs`, dentro de la clase (después de los tests de `FinalizarAsync`):

```csharp
    // ── AgregarNotaAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarNotaAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.AgregarNotaAsync(1, "avance"));
    }

    [Fact]
    public async Task AgregarNotaAsync_TextoVacio_LanzaArgumentException()
    {
        var ctx = Crear();
        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.AgregarNotaAsync(1, "   "));
    }

    [Fact]
    public async Task AgregarNotaAsync_DocumentoInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((DocumentoAdministrativo?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => ctx.Svc.AgregarNotaAsync(1, "avance"));
    }

    [Fact]
    public async Task AgregarNotaAsync_GuardaEventoManualYRegistraAuditoria()
    {
        var ctx = Crear(idSesion: 3);
        var documento = NuevoDocumento();
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await ctx.Svc.AgregarNotaAsync(1, "avance del trámite");

        var evento = Assert.Single(documento.Eventos);
        Assert.False(evento.EsAutomatico);
        Assert.Null(evento.EstadoAnterior);
        Assert.Null(evento.EstadoNuevo);
        ctx.Repo.Verify(r => r.ActualizarAsync(documento), Times.Once);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            3, AccionAuditada.AltaNotaDocumento, "DocumentoAdministrativo", 1, It.IsAny<string>()), Times.Once);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: FAIL — `AgregarNotaAsync` lanza `NotImplementedException`.

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs
// Reemplazar "public Task AgregarNotaAsync(int id, string texto) => throw new NotImplementedException(); // Task 9" por:

    public async Task AgregarNotaAsync(int id, string texto)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        if (string.IsNullOrWhiteSpace(texto))
            throw new ArgumentException("El texto de la nota no puede estar vacío.", nameof(texto));

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        documento.AgregarEvento(_session.UsuarioActual!.Id, texto.Trim(), esAutomatico: false);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaNotaDocumento, "DocumentoAdministrativo", id,
            $"Nota: {texto.Trim()}");
    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: PASS — 31 tests verdes (27 de Task 8 + 4 nuevos).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs \
        tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs
git commit -m "feat(documentos): agrega notas manuales al historial del documento"
```

---

## Task 10: Anular y reabrir (solo Admin, con motivo)

**Files:**
- Modify: `src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs`
- Modify: `tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs`

**Interfaces:**
- Consumes: `Permisos.AdministrarDocumentos`, `AccionAuditada.AnulacionDocumento`, `AccionAuditada.ReaperturaDocumento` (Task 6); `DocumentoAdministrativo.EsCerrado`, `CambiarEstado(EstadoDocumento)`.
- Produces: `AnularAsync`, `ReabrirAsync` implementados — consumidos por `POST /documentos/{id}/anular` y `/reabrir` (bloque C, body con `Motivo`) y por `PuedeAnular`/`PuedeReabrir` en `DocumentoFila` (bloque D).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs`, dentro de la clase (después de los tests de `AgregarNotaAsync`):

```csharp
    // ── AnularAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AnularAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Auth.Setup(a => a.Verificar(It.Is<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), Permisos.AdministrarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.AnularAsync(1, "motivo válido"));

        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AnularAsync_MotivoVacio_LanzaReglaDeNegocio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.AnularAsync(1, ""));
    }

    [Fact]
    public async Task AnularAsync_MotivoEnBlanco_LanzaReglaDeNegocio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.AnularAsync(1, "   "));
    }

    [Fact]
    public async Task AnularAsync_DocumentoInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((DocumentoAdministrativo?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => ctx.Svc.AnularAsync(1, "motivo válido"));
    }

    [Fact]
    public async Task AnularAsync_ComoAdmin_CambiaAAnuladoYSellaFechaCierre()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.AnularAsync(5, "el interesado no volvió a presentarse");

        Assert.Equal(EstadoDocumento.Anulado, documento.Estado);
        Assert.NotNull(documento.FechaCierre);
    }

    [Fact]
    public async Task AnularAsync_GeneraEventoAutomaticoConMotivo()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.AnularAsync(5, "el interesado no volvió a presentarse");

        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Contains("el interesado no volvió a presentarse", evento.Texto);
        Assert.Equal(EstadoDocumento.Pendiente, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.Anulado, evento.EstadoNuevo);
    }

    [Fact]
    public async Task AnularAsync_RegistraAuditoria()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.AnularAsync(5, "motivo válido");

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.AnulacionDocumento, "DocumentoAdministrativo", 5, It.IsAny<string>()), Times.Once);
    }

    // ── ReabrirAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReabrirAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Auth.Setup(a => a.Verificar(It.Is<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), Permisos.AdministrarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ReabrirAsync(1, "motivo válido"));

        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ReabrirAsync_MotivoVacio_LanzaReglaDeNegocio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.ReabrirAsync(1, ""));
    }

    [Fact]
    public async Task ReabrirAsync_SobreDocumentoPendiente_LanzaReglaDeNegocio()
    {
        // Caso puntual del spec (D4/D8): Pendiente -> EnProceso ya es válida por otra vía
        // (IniciarProcesoAsync), así que sin esta guarda explícita el dominio no rechazaría
        // "reabrir" algo que nunca estuvo cerrado.
        var ctx = Crear(rol: RolUsuario.Admin);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.ReabrirAsync(5, "motivo válido"));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task ReabrirAsync_SobreDocumentoEnProceso_LanzaReglaDeNegocio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.ReabrirAsync(5, "motivo válido"));
    }

    [Fact]
    public async Task ReabrirAsync_SobreDocumentoFinalizado_CambiaAEnProcesoYLimpiaFechaCierre()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Finalizado);
        documento.Id = 5;
        documento.FechaCierre = DateTime.UtcNow.AddDays(-1);
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.ReabrirAsync(5, "faltaba documentación, se retoma");

        Assert.Equal(EstadoDocumento.EnProceso, documento.Estado);
        Assert.Null(documento.FechaCierre);
    }

    [Fact]
    public async Task ReabrirAsync_SobreDocumentoAnulado_CambiaAEnProcesoYLimpiaFechaCierre()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Anulado);
        documento.Id = 5;
        documento.FechaCierre = DateTime.UtcNow.AddDays(-1);
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.ReabrirAsync(5, "el interesado volvió a presentarse");

        Assert.Equal(EstadoDocumento.EnProceso, documento.Estado);
        Assert.Null(documento.FechaCierre);
    }

    [Fact]
    public async Task ReabrirAsync_GeneraEventoAutomaticoConMotivo()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Finalizado);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.ReabrirAsync(5, "faltaba documentación, se retoma");

        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Contains("faltaba documentación, se retoma", evento.Texto);
        Assert.Equal(EstadoDocumento.Finalizado, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.EnProceso, evento.EstadoNuevo);
    }

    [Fact]
    public async Task ReabrirAsync_RegistraAuditoria()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Finalizado);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.ReabrirAsync(5, "motivo válido");

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.ReaperturaDocumento, "DocumentoAdministrativo", 5, It.IsAny<string>()), Times.Once);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: FAIL — `AnularAsync`/`ReabrirAsync` lanzan `NotImplementedException`.

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs
// Reemplazar las dos líneas "=> throw new NotImplementedException();  // Task 10" por:

    public async Task AnularAsync(int id, string motivo)
    {
        _auth.Verificar(_session, Permisos.AdministrarDocumentos);

        if (string.IsNullOrWhiteSpace(motivo))
            throw new ReglaDeNegocioException("El motivo es obligatorio para anular un documento.");

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.Anulado);
        documento.FechaCierre = DateTime.UtcNow;   // D8: lo sella el servicio, no la entidad.

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"Anulado: {motivo.Trim()}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AnulacionDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}: {motivo.Trim()}");
    }

    public async Task ReabrirAsync(int id, string motivo)
    {
        _auth.Verificar(_session, Permisos.AdministrarDocumentos);

        if (string.IsNullOrWhiteSpace(motivo))
            throw new ReglaDeNegocioException("El motivo es obligatorio para reabrir un documento.");

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        // D4/D8: la guarda es necesaria porque Pendiente -> EnProceso ya es una transición
        // válida por otra vía (IniciarProcesoAsync); sin este chequeo, "reabrir" un
        // documento que nunca estuvo cerrado no lanzaría ninguna excepción.
        if (!documento.EsCerrado)
            throw new ReglaDeNegocioException(
                $"No se puede reabrir un documento en estado '{documento.Estado}': no está cerrado.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.EnProceso);
        documento.FechaCierre = null;

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"Reabierto: {motivo.Trim()}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ReaperturaDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}: {motivo.Trim()}");
    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: PASS — 46 tests verdes (31 de Task 9 + 15 nuevos).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs \
        tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs
git commit -m "feat(documentos): implementa anulacion y reapertura con motivo obligatorio"
```

---

## Task 11: Edición de documentos activos

**Files:**
- Modify: `src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs`
- Modify: `tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs`

**Interfaces:**
- Consumes: `DatosEdicionDocumento` (Task 7), `IDocumentoAdministrativoRepository.ExisteNumeroAsync(TipoDocumento, int, string, int? excluyendoId)` (bloque A), `AccionAuditada.EdicionDocumento` (Task 6).
- Produces: `EditarAsync` implementado — con esto `IDocumentoAdministrativoService` queda completo (11 métodos). Consumido por `PUT /documentos/{id}` (bloque C).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs`, dentro de la clase (después de los tests de `ReabrirAsync`):

```csharp
    // ── EditarAsync ───────────────────────────────────────────────────────────

    private static DatosEdicionDocumento NuevosDatos(
        string numero = "0087", int anio = 2026, TipoDocumento tipo = TipoDocumento.Expediente,
        string descripcion = "Solicitud de poda de árbol")
        => new(numero, anio, tipo, new DateTime(2026, 1, 15), descripcion);

    [Fact]
    public async Task EditarAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.EditarAsync(1, NuevosDatos()));

        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task EditarAsync_DocumentoInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((DocumentoAdministrativo?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => ctx.Svc.EditarAsync(1, NuevosDatos()));
    }

    [Fact]
    public async Task EditarAsync_DocumentoCerrado_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Finalizado);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.EditarAsync(1, NuevosDatos()));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task EditarAsync_NumeroVacio_LanzaArgumentException()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.EditarAsync(1, NuevosDatos(numero: "  ")));
    }

    [Fact]
    public async Task EditarAsync_CambiaNumero_RevalidaIndiceUnicoExcluyendoId()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0088", 1)).ReturnsAsync(false);

        await ctx.Svc.EditarAsync(1, NuevosDatos(numero: "0088"));

        ctx.Repo.Verify(r => r.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0088", 1), Times.Once);
        Assert.Equal("0088", documento.Numero);
    }

    [Fact]
    public async Task EditarAsync_NumeroChocaConOtroDocumento_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0088", 1)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.EditarAsync(1, NuevosDatos(numero: "0088")));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task EditarAsync_SoloCambiaDescripcion_NoRevalidaIndiceUnico()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await ctx.Svc.EditarAsync(1, NuevosDatos(descripcion: "Descripción corregida"));

        ctx.Repo.Verify(r => r.ExisteNumeroAsync(
            It.IsAny<TipoDocumento>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
        Assert.Equal("Descripción corregida", documento.Descripcion);
    }

    [Fact]
    public async Task EditarAsync_GeneraEventoAutomaticoDetallandoCambios()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0088", 1)).ReturnsAsync(false);

        await ctx.Svc.EditarAsync(1, NuevosDatos(numero: "0088"));

        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Contains("0087", evento.Texto);
        Assert.Contains("0088", evento.Texto);
    }

    [Fact]
    public async Task EditarAsync_SinCambios_NoGeneraEvento()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await ctx.Svc.EditarAsync(1, NuevosDatos());

        Assert.Empty(documento.Eventos);
    }

    [Fact]
    public async Task EditarAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await ctx.Svc.EditarAsync(1, NuevosDatos(descripcion: "Otra descripción"));

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.EdicionDocumento, "DocumentoAdministrativo", 1, It.IsAny<string>()), Times.Once);
    }

    // ── Superficie de la interfaz (append-only, whitelist explícita) ─────────

    [Fact]
    public void IDocumentoAdministrativoService_ExponeExactamenteLosMetodosEsperados()
    {
        var esperados = new HashSet<string>
        {
            nameof(IDocumentoAdministrativoService.RegistrarAsync),
            nameof(IDocumentoAdministrativoService.EditarAsync),
            nameof(IDocumentoAdministrativoService.ListarActivosAsync),
            nameof(IDocumentoAdministrativoService.ListarHistorialAsync),
            nameof(IDocumentoAdministrativoService.ObtenerPorIdAsync),
            nameof(IDocumentoAdministrativoService.IniciarProcesoAsync),
            nameof(IDocumentoAdministrativoService.VolverAPendienteAsync),
            nameof(IDocumentoAdministrativoService.FinalizarAsync),
            nameof(IDocumentoAdministrativoService.AgregarNotaAsync),
            nameof(IDocumentoAdministrativoService.AnularAsync),
            nameof(IDocumentoAdministrativoService.ReabrirAsync),
        };

        var metodos = typeof(IDocumentoAdministrativoService).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.Equal(esperados, metodos);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: FAIL — `EditarAsync` lanza `NotImplementedException`.

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs
// Reemplazar "public Task EditarAsync(int id, DatosEdicionDocumento datos) => throw new NotImplementedException(); // Task 11" por:

    public async Task EditarAsync(int id, DatosEdicionDocumento datos)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        if (!documento.EsActivo)
            throw new ReglaDeNegocioException(
                $"No se puede editar un documento en estado '{documento.Estado}': no está activo.");

        if (string.IsNullOrWhiteSpace(datos.Numero))
            throw new ArgumentException("El número del documento es obligatorio.", nameof(datos));
        if (string.IsNullOrWhiteSpace(datos.Descripcion))
            throw new ArgumentException("La descripción del documento es obligatoria.", nameof(datos));

        var cambiaClave = documento.Numero != datos.Numero || documento.Anio != datos.Anio || documento.Tipo != datos.Tipo;
        if (cambiaClave && await _repo.ExisteNumeroAsync(datos.Tipo, datos.Anio, datos.Numero, id))
            throw new ReglaDeNegocioException(
                $"Ya existe un {datos.Tipo} {datos.Numero}/{datos.Anio}.");

        var cambios = new List<string>();
        if (documento.Numero != datos.Numero)
            cambios.Add($"Número: {documento.Numero} → {datos.Numero}");
        if (documento.Anio != datos.Anio)
            cambios.Add($"Año: {documento.Anio} → {datos.Anio}");
        if (documento.Tipo != datos.Tipo)
            cambios.Add($"Tipo: {documento.Tipo} → {datos.Tipo}");
        if (documento.FechaEmision != datos.FechaEmision)
            cambios.Add($"Fecha de emisión: {documento.FechaEmision:yyyy-MM-dd} → {datos.FechaEmision:yyyy-MM-dd}");
        if (documento.Descripcion != datos.Descripcion)
            cambios.Add($"Descripción: '{documento.Descripcion}' → '{datos.Descripcion}'");

        documento.Numero       = datos.Numero;
        documento.Anio         = datos.Anio;
        documento.Tipo         = datos.Tipo;
        documento.FechaEmision = datos.FechaEmision;
        documento.Descripcion  = datos.Descripcion;

        // D1: solo se deja rastro si algo cambió de verdad — evita un evento vacío cuando
        // el usuario reenvía los mismos datos.
        if (cambios.Count > 0)
            documento.AgregarEvento(
                _session.UsuarioActual!.Id, $"Se corrigieron datos: {string.Join("; ", cambios)}.", esAutomatico: true);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.EdicionDocumento, "DocumentoAdministrativo", id,
            cambios.Count > 0 ? string.Join("; ", cambios) : "Sin cambios");
    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~DocumentoAdministrativoServiceTests"`
Expected: PASS — 57 tests verdes (46 de Task 10 + 11 nuevos).

- [ ] **Step 5: Correr la suite completa de Application para confirmar que no rompió nada más**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: PASS — todos los tests verdes.

- [ ] **Step 6: Commit**
```bash
git add src/StockApp.Application/Documentos/DocumentoAdministrativoService.cs \
        tests/StockApp.Application.Tests/Documentos/DocumentoAdministrativoServiceTests.cs
git commit -m "feat(documentos): implementa edicion de documentos activos"
```

---

## Task 12: Servicio de adjuntos

**Files:**
- Create: `src/StockApp.Application/Documentos/AdjuntoDocumentoDto.cs`
- Create: `src/StockApp.Application/Documentos/IAdjuntoDocumentoService.cs`
- Create: `src/StockApp.Application/Documentos/AdjuntoDocumentoService.cs`
- Test: `tests/StockApp.Application.Tests/Documentos/AdjuntoDocumentoServiceTests.cs`

**Interfaces:**
- Consumes (del bloque A): entidad `StockApp.Domain.Entities.AdjuntoDocumento` (`Id`, `DocumentoAdministrativoId`, `NombreArchivo`, `ContentType`, `TamanoBytes`, `Activo`, `FechaAltaUtc`); `StockApp.Application.Interfaces.IAdjuntoDocumentoRepository` con `AgregarAsync(AdjuntoDocumento, byte[])`, `ListarPorDocumentoAsync(int)`, `ObtenerPorIdAsync(int)`, `ObtenerContenidoAsync(int)`, `ActualizarAsync(AdjuntoDocumento)`. También `IDocumentoAdministrativoRepository.ObtenerPorIdAsync`/`ActualizarAsync` (para chequear `EsActivo` y sumar el evento en el documento dueño — Task 7), `AdjuntoValidador` (`StockApp.Application.Finanzas`, se reusa TAL CUAL, sin duplicar — D10), `Permisos.GestionarDocumentos`/`AdministrarDocumentos` (Task 6), `AccionAuditada.AltaAdjuntoDocumento`/`BajaAdjuntoDocumento` (Task 6).
- Produces: `record AdjuntoDocumentoDto(int Id, int DocumentoAdministrativoId, string NombreArchivo, string ContentType, long TamanoBytes, DateTime FechaAltaUtc)`, `record AdjuntoDocumentoContenidoDto(string NombreArchivo, string ContentType, byte[] Contenido)`, `IAdjuntoDocumentoService`, `AdjuntoDocumentoService` — consumidos por los endpoints de adjuntos (bloque C) y `AdjuntosDocumentoPanelViewModel` (bloque D).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Application.Tests/Documentos/AdjuntoDocumentoServiceTests.cs
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Auth;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Application.Tests.Documentos;

public class AdjuntoDocumentoServiceTests
{
    private readonly Mock<IAdjuntoDocumentoRepository> _adjuntos = new();
    private readonly Mock<IDocumentoAdministrativoRepository> _documentos = new();
    private readonly Mock<ICurrentSession> _session = new();
    private readonly Mock<IAuthorizationService> _auth = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AdjuntoDocumentoService _service;

    private static readonly byte[] BytesPdf = { 0x25, 0x50, 0x44, 0x46, 0x01 };

    private static DocumentoAdministrativo NuevoDocumento(EstadoDocumento estado = EstadoDocumento.Pendiente) => new()
    {
        Id = 1, Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = new DateTime(2026, 1, 15), Descripcion = "Solicitud de poda de árbol", Estado = estado,
    };

    public AdjuntoDocumentoServiceTests()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        _session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));
        _auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(NuevoDocumento(EstadoDocumento.Pendiente));

        _service = new AdjuntoDocumentoService(
            _adjuntos.Object, _documentos.Object, _session.Object, _auth.Object, _audit.Object);
    }

    // ── AgregarAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        _auth.Setup(a => a.Verificar(_session.Object, Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.AgregarAsync(1, "factura.pdf", BytesPdf));

        _adjuntos.Verify(r => r.AgregarAsync(It.IsAny<AdjuntoDocumento>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task AgregarAsync_DocumentoInexistente_LanzaEntidadNoEncontrada()
    {
        _documentos.Setup(d => d.ObtenerPorIdAsync(99)).ReturnsAsync((DocumentoAdministrativo?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _service.AgregarAsync(99, "factura.pdf", BytesPdf));
    }

    [Fact]
    public async Task AgregarAsync_DocumentoCerrado_LanzaReglaDeNegocio()
    {
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(NuevoDocumento(EstadoDocumento.Finalizado));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _service.AgregarAsync(1, "factura.pdf", BytesPdf));

        _adjuntos.Verify(r => r.AgregarAsync(It.IsAny<AdjuntoDocumento>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task AgregarAsync_MimeNoPermitido_LanzaReglaDeNegocio()
    {
        var bytesInvalidos = new byte[] { 0x00, 0x01, 0x02 };

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _service.AgregarAsync(1, "archivo.exe", bytesInvalidos));
    }

    [Fact]
    public async Task AgregarAsync_Exitoso_GeneraEventoAutomaticoEnElDocumento()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<AdjuntoDocumento>(), It.IsAny<byte[]>())).ReturnsAsync(10);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await _service.AgregarAsync(1, "factura.pdf", BytesPdf);

        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Contains("factura.pdf", evento.Texto);
        _documentos.Verify(d => d.ActualizarAsync(documento), Times.Once);
    }

    [Fact]
    public async Task AgregarAsync_Exitoso_RegistraAuditoria()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<AdjuntoDocumento>(), It.IsAny<byte[]>())).ReturnsAsync(10);

        await _service.AgregarAsync(1, "factura.pdf", BytesPdf);

        _audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.AltaAdjuntoDocumento, "AdjuntoDocumento", 10, It.IsAny<string>()), Times.Once);
    }

    // ── ListarPorDocumentoAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ListarPorDocumentoAsync_SinPermiso_LanzaExcepcion()
    {
        _auth.Setup(a => a.Verificar(_session.Object, Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ListarPorDocumentoAsync(1));
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_DelegaAlRepo()
    {
        _adjuntos.Setup(r => r.ListarPorDocumentoAsync(1)).ReturnsAsync(new List<AdjuntoDocumento>
        {
            new() { Id = 1, DocumentoAdministrativoId = 1, NombreArchivo = "a.pdf", ContentType = "application/pdf", Activo = true },
        });

        var resultado = await _service.ListarPorDocumentoAsync(1);

        Assert.Single(resultado);
    }

    // ── ObtenerContenidoAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoInexistente_LanzaEntidadNoEncontrada()
    {
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((AdjuntoDocumento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => _service.ObtenerContenidoAsync(99));
    }

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoDadoDeBaja_LanzaEntidadNoEncontrada()
    {
        var adjunto = new AdjuntoDocumento { Id = 7, DocumentoAdministrativoId = 1, Activo = false, NombreArchivo = "a.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => _service.ObtenerContenidoAsync(7));

        _adjuntos.Verify(r => r.ObtenerContenidoAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoActivo_DevuelveContenido()
    {
        var adjunto = new AdjuntoDocumento
        {
            Id = 7, DocumentoAdministrativoId = 1, Activo = true,
            NombreArchivo = "a.pdf", ContentType = "application/pdf",
        };
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);
        _adjuntos.Setup(r => r.ObtenerContenidoAsync(7)).ReturnsAsync(bytes);

        var resultado = await _service.ObtenerContenidoAsync(7);

        Assert.Equal(bytes, resultado.Contenido);
        Assert.Equal("a.pdf", resultado.NombreArchivo);
    }

    // ── QuitarAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task QuitarAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        _auth.Setup(a => a.Verificar(It.Is<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), Permisos.AdministrarDocumentos))
            .Throws<UnauthorizedAccessException>();
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.QuitarAsync(7));

        _adjuntos.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task QuitarAsync_AdjuntoInexistente_LanzaEntidadNoEncontrada()
    {
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((AdjuntoDocumento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => _service.QuitarAsync(99));
    }

    [Fact]
    public async Task QuitarAsync_DocumentoCerrado_LanzaReglaDeNegocio()
    {
        // D11(a): la regla corta en ambos sentidos, agregar Y quitar.
        var adjunto = new AdjuntoDocumento { Id = 7, DocumentoAdministrativoId = 1, Activo = true, NombreArchivo = "a.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(NuevoDocumento(EstadoDocumento.Anulado));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => _service.QuitarAsync(7));

        _adjuntos.Verify(r => r.ActualizarAsync(It.IsAny<AdjuntoDocumento>()), Times.Never);
    }

    [Fact]
    public async Task QuitarAsync_ComoAdmin_HaceBajaLogicaYGeneraEventoEnElDocumento()
    {
        var adjunto = new AdjuntoDocumento { Id = 7, DocumentoAdministrativoId = 1, Activo = true, NombreArchivo = "a.pdf" };
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await _service.QuitarAsync(7);

        Assert.False(adjunto.Activo);
        _adjuntos.Verify(r => r.ActualizarAsync(adjunto), Times.Once);
        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Contains("a.pdf", evento.Texto);
    }

    [Fact]
    public async Task QuitarAsync_RegistraAuditoria()
    {
        var adjunto = new AdjuntoDocumento { Id = 7, DocumentoAdministrativoId = 1, Activo = true, NombreArchivo = "a.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);

        await _service.QuitarAsync(7);

        _audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.BajaAdjuntoDocumento, "AdjuntoDocumento", 7, It.IsAny<string>()), Times.Once);
    }

    // ── Superficie de la interfaz (append-only, whitelist explícita) ─────────

    [Fact]
    public void IAdjuntoDocumentoService_ExponeExactamenteLosMetodosEsperados()
    {
        var esperados = new HashSet<string>
        {
            nameof(IAdjuntoDocumentoService.AgregarAsync),
            nameof(IAdjuntoDocumentoService.ListarPorDocumentoAsync),
            nameof(IAdjuntoDocumentoService.ObtenerContenidoAsync),
            nameof(IAdjuntoDocumentoService.QuitarAsync),
        };

        var metodos = typeof(IAdjuntoDocumentoService).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.Equal(esperados, metodos);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~AdjuntoDocumentoServiceTests"`
Expected: FAIL — no compila (`AdjuntoDocumentoService`, `IAdjuntoDocumentoService`, `AdjuntoDocumentoDto`, `AdjuntoDocumentoContenidoDto` no existen todavía).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Documentos/AdjuntoDocumentoDto.cs
namespace StockApp.Application.Documentos;

/// <summary>Metadatos de un adjunto de documento (sin bytes) — lo que devuelven los listados.</summary>
public record AdjuntoDocumentoDto(
    int Id, int DocumentoAdministrativoId, string NombreArchivo, string ContentType,
    long TamanoBytes, DateTime FechaAltaUtc);

/// <summary>Contenido completo para descarga (Results.File en el endpoint).</summary>
public record AdjuntoDocumentoContenidoDto(string NombreArchivo, string ContentType, byte[] Contenido);
```

```csharp
// src/StockApp.Application/Documentos/IAdjuntoDocumentoService.cs
namespace StockApp.Application.Documentos;

/// <summary>
/// Adjuntos de documentos administrativos (D10/D11). Entidad y tabla propias — no reusa
/// Adjunto de Finanzas — pero SÍ reusa AdjuntoValidador tal cual (D10). Sin método
/// Modificar: un adjunto no se edita, se quita y se sube otro, mismo criterio que
/// IAdjuntoService de Finanzas (YAGNI, spec F3 decisión 8).
/// </summary>
public interface IAdjuntoDocumentoService
{
    /// <summary>Rechaza con ReglaDeNegocioException si el documento no está EsActivo (D11a).</summary>
    Task<AdjuntoDocumentoDto> AgregarAsync(int documentoId, string nombreArchivo, byte[] contenido);

    Task<IReadOnlyList<AdjuntoDocumentoDto>> ListarPorDocumentoAsync(int documentoId);

    Task<AdjuntoDocumentoContenidoDto> ObtenerContenidoAsync(int adjuntoId);

    /// <summary>Baja lógica (D11c). Exige Permisos.AdministrarDocumentos (D11b) y también
    /// rechaza si el documento dueño no está EsActivo (D11a — corta en ambos sentidos).</summary>
    Task QuitarAsync(int adjuntoId);
}
```

```csharp
// src/StockApp.Application/Documentos/AdjuntoDocumentoService.cs
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Documentos;

public class AdjuntoDocumentoService : IAdjuntoDocumentoService
{
    private readonly IAdjuntoDocumentoRepository        _adjuntos;
    private readonly IDocumentoAdministrativoRepository _documentos;
    private readonly ICurrentSession                    _session;
    private readonly IAuthorizationService               _auth;
    private readonly IAuditLogger                        _audit;

    public AdjuntoDocumentoService(
        IAdjuntoDocumentoRepository adjuntos, IDocumentoAdministrativoRepository documentos,
        ICurrentSession session, IAuthorizationService auth, IAuditLogger audit)
    {
        _adjuntos   = adjuntos;
        _documentos = documentos;
        _session    = session;
        _auth       = auth;
        _audit      = audit;
    }

    public async Task<AdjuntoDocumentoDto> AgregarAsync(int documentoId, string nombreArchivo, byte[] contenido)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var documento = await _documentos.ObtenerPorIdAsync(documentoId)
            ?? throw new EntidadNoEncontradaException($"Documento {documentoId} no encontrado.");

        // D11a: la regla corta en ambos sentidos, agregar Y quitar (ver QuitarAsync).
        if (!documento.EsActivo)
            throw new ReglaDeNegocioException(
                $"No se pueden agregar adjuntos a un documento en estado '{documento.Estado}': no está activo.");

        AdjuntoValidador.Validar(contenido, nombreArchivo);

        var adjunto = new AdjuntoDocumento
        {
            DocumentoAdministrativoId = documentoId,
            NombreArchivo = nombreArchivo,
            ContentType   = AdjuntoValidador.DetectarContentType(contenido)!,
            TamanoBytes   = contenido.LongLength,
            Activo        = true,
            FechaAltaUtc  = DateTime.UtcNow,
        };

        var id = await _adjuntos.AgregarAsync(adjunto, contenido);

        // D11d: adjuntar genera evento automático en el historial del documento dueño.
        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"Se agregó el adjunto '{nombreArchivo}'.", esAutomatico: true);
        await _documentos.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaAdjuntoDocumento, "AdjuntoDocumento", id,
            $"Documento {documentoId} — {nombreArchivo}");

        return ADto(adjunto);
    }

    public async Task<IReadOnlyList<AdjuntoDocumentoDto>> ListarPorDocumentoAsync(int documentoId)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);
        return (await _adjuntos.ListarPorDocumentoAsync(documentoId)).Select(ADto).ToList();
    }

    public async Task<AdjuntoDocumentoContenidoDto> ObtenerContenidoAsync(int adjuntoId)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var adjunto = await _adjuntos.ObtenerPorIdAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        // Baja lógica: un adjunto inactivo no debe seguir siendo descargable por id, mismo
        // criterio que AdjuntoService.ObtenerContenidoAsync (Finanzas).
        if (!adjunto.Activo)
            throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        var contenido = await _adjuntos.ObtenerContenidoAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el contenido del adjunto {adjuntoId}.");

        return new AdjuntoDocumentoContenidoDto(adjunto.NombreArchivo, adjunto.ContentType, contenido);
    }

    public async Task QuitarAsync(int adjuntoId)
    {
        _auth.Verificar(_session, Permisos.AdministrarDocumentos);

        var adjunto = await _adjuntos.ObtenerPorIdAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        var documento = await _documentos.ObtenerPorIdAsync(adjunto.DocumentoAdministrativoId)
            ?? throw new EntidadNoEncontradaException($"Documento {adjunto.DocumentoAdministrativoId} no encontrado.");

        // D11a corregido: la regla de "solo sobre documento activo" corta igual al agregar
        // y al quitar, no únicamente al agregar.
        if (!documento.EsActivo)
            throw new ReglaDeNegocioException(
                $"No se pueden quitar adjuntos de un documento en estado '{documento.Estado}': no está activo.");

        adjunto.Activo = false;
        await _adjuntos.ActualizarAsync(adjunto);

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"Se quitó el adjunto '{adjunto.NombreArchivo}'.", esAutomatico: true);
        await _documentos.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.BajaAdjuntoDocumento, "AdjuntoDocumento", adjuntoId,
            $"{adjunto.NombreArchivo}");
    }

    private static AdjuntoDocumentoDto ADto(AdjuntoDocumento a) => new(
        a.Id, a.DocumentoAdministrativoId, a.NombreArchivo, a.ContentType, a.TamanoBytes, a.FechaAltaUtc);
}
```

Nota: `AccionAuditada` vive en `StockApp.Domain.Enums`; agregar `using StockApp.Domain.Enums;` al archivo si el editor no lo resuelve por `ImplicitUsings` (el resto de los `using` de la lista de arriba ya cubren `Entities`/`Exceptions`/`Interfaces`/`Finanzas`/`Authorization`).

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~AdjuntoDocumentoServiceTests"`
Expected: PASS — 17 tests verdes.

- [ ] **Step 5: Correr la suite completa de Application (secuencial, NUNCA en paralelo con StockApp.Api.Tests — colisión de Testcontainers)**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: PASS — todos los tests verdes, incluidos los 57 de `DocumentoAdministrativoServiceTests` y los 17 nuevos de `AdjuntoDocumentoServiceTests`.

- [ ] **Step 6: Commit**
```bash
git add src/StockApp.Application/Documentos/AdjuntoDocumentoDto.cs \
        src/StockApp.Application/Documentos/IAdjuntoDocumentoService.cs \
        src/StockApp.Application/Documentos/AdjuntoDocumentoService.cs \
        tests/StockApp.Application.Tests/Documentos/AdjuntoDocumentoServiceTests.cs
git commit -m "feat(documentos): agrega servicio de adjuntos del modulo"
```
## Task 13: Api — `DocumentosEndpoints.cs` (lectura + alta/edición)

**Files:**
- Create: `src/StockApp.Api/Endpoints/DocumentosEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (DI cerca de la línea 116, registro del grupo cerca de la línea 627)
- Test: `tests/StockApp.Api.Tests/DocumentosEndpointTests.cs`

**Interfaces:**
- Consumes: `IDocumentoAdministrativoService` (`RegistrarAsync(DocumentoAdministrativo)`, `EditarAsync(int, DatosEdicionDocumento)`, `ListarActivosAsync(FiltroDocumentos)`, `ListarHistorialAsync(FiltroDocumentos)`, `ObtenerPorIdAsync(int)`), `IDocumentoAdministrativoRepository`/`DocumentoAdministrativoRepository` (para el DI), `Permisos.GestionarDocumentos`.
- Produces:
  - `GET /documentos/activos` (query `tipo`, `anio`, `texto`) → 200 `List<DocumentoDto>`.
  - `GET /documentos/historial` (query `tipo`, `anio`, `texto`, `estado`; `anio` ausente → 400) → 200 `List<DocumentoDto>`.
  - `GET /documentos/{id:int}` → 200 `DocumentoDto` | 404.
  - `POST /documentos` → 201 `DocumentoCreadoResponse` | 409 (número duplicado).
  - `PUT /documentos/{id:int}` → 200 | 409 (revalidación de número único) | 404.
  - Tipos públicos nuevos en `StockApp.Api.Endpoints`: `EventoDocumentoDto`, `DocumentoDto`, `CrearDocumentoRequest`, `EditarDocumentoRequest`, `DocumentoCreadoResponse`.

**Steps:**

- [ ] 13.1 Escribir `tests/StockApp.Api.Tests/DocumentosEndpointTests.cs` (patrón exacto de `TareasEndpointTests`: hereda `ApiTestBase`, JWT por rol vía `IJwtTokenService`, seed con `DatosDePrueba`):
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class DocumentosEndpointTests : ApiTestBase
{
    public DocumentosEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedUsuariosAsync()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
    }

    private async Task<int> CrearDocumentoAsync(
        HttpClient client, string numero = "0087", int anio = 2026, TipoDocumento tipo = TipoDocumento.Expediente)
    {
        var response = await client.PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest(numero, anio, tipo, new DateTime(2026, 1, 15), "Descripción de prueba"));
        var creado = await response.Content.ReadFromJsonAsync<DocumentoCreadoResponse>();
        return creado!.Id;
    }

    /// <summary>Siembra un documento directo por EF (sin pasar por los endpoints de transición,
    /// que todavía no existen en esta tarea) — mismo criterio que
    /// AdjuntosEndpointTests.SembrarGastoAsync. El registrante es un Admin propio con nombre
    /// único (Guid) para no colisionar con "admin.test"/"operador.test" de SeedUsuariosAsync.</summary>
    private async Task<int> SembrarDocumentoAsync(
        EstadoDocumento estado = EstadoDocumento.Pendiente,
        string numero = "0001", int anio = 2026, TipoDocumento tipo = TipoDocumento.Expediente)
    {
        await using var ctx = Factory.CrearContexto();
        var registrante = await DatosDePrueba.SeedUsuarioAsync(
            ctx, $"registrante.{Guid.NewGuid():N}", "Secreta123!", RolUsuario.Admin);

        var documento = new DocumentoAdministrativo
        {
            Numero = numero,
            Anio = anio,
            Tipo = tipo,
            FechaEmision = new DateTime(2026, 1, 15),
            Descripcion = "Documento de prueba",
            Estado = estado,
            RegistradoPorUsuarioId = registrante.Id,
            FechaRegistro = DateTime.UtcNow,
            FechaCierre = estado is EstadoDocumento.Finalizado or EstadoDocumento.Anulado
                ? DateTime.UtcNow : null,
        };
        ctx.DocumentosAdministrativos.Add(documento);
        await ctx.SaveChangesAsync();
        return documento.Id;
    }

    [Fact]
    public async Task PostDocumentos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostDocumentos_ConTokenOperadorSinPermiso_Devuelve403()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "x"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostDocumentos_ConTokenOperador_Devuelve201()
    {
        // D7: documentos.gestionar es configurable y se agrega a PermisosInicialesOperador
        // (Bloques A/B) — un Operador recién sembrado con SeedUsuarioAsync ya lo trae.
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "Expediente de prueba"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = await response.Content.ReadFromJsonAsync<DocumentoCreadoResponse>();
        Assert.True(creado!.Id > 0);
    }

    [Fact]
    public async Task PostDocumentos_NumeroDuplicado_Devuelve409()
    {
        // D1: índice único (Tipo, Anio, Numero) — condición de carrera real, contra Postgres real.
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await CrearDocumentoAsync(client, numero: "0087", anio: 2026, tipo: TipoDocumento.Expediente);

        var response = await client.PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 2, 1), "Otro expediente"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetActivos_ConTokenOperador_Devuelve200SoloConPendientesYEnProceso()
    {
        await SembrarDocumentoAsync(EstadoDocumento.Pendiente, numero: "0001");
        await SembrarDocumentoAsync(EstadoDocumento.EnProceso, numero: "0002");
        await SembrarDocumentoAsync(EstadoDocumento.Finalizado, numero: "0003");
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/documentos/activos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documentos = await response.Content.ReadFromJsonAsync<List<DocumentoDto>>();
        Assert.Equal(2, documentos!.Count);
    }

    [Fact]
    public async Task GetActivos_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: un test de 403 por endpoint, no representativo — GET /activos tiene su propia
        // policy y puede quedar mal cableada aunque POST /documentos esté bien.
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.GetAsync("/documentos/activos");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_SinAnio_Devuelve400()
    {
        // D9: Anio es obligatorio en el historial — ArgumentException, no ReglaDeNegocioException,
        // porque es un request mal formado, no un conflicto de negocio (contraste con el 409 de arriba).
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/documentos/historial");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_ConAnio_Devuelve200SoloConCerrados()
    {
        await SembrarDocumentoAsync(EstadoDocumento.Finalizado, numero: "0010", anio: 2026);
        await SembrarDocumentoAsync(EstadoDocumento.Pendiente, numero: "0011", anio: 2026);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/documentos/historial?anio=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documentos = await response.Content.ReadFromJsonAsync<List<DocumentoDto>>();
        var documento = Assert.Single(documentos!);
        Assert.Equal("0010", documento.Numero);
    }

    [Fact]
    public async Task GetHistorial_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio, no representativo del de POST /documentos.
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.GetAsync("/documentos/historial?anio=2026");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPorId_Inexistente_Devuelve404()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/documentos/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPorId_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio, no representativo del de POST /documentos.
        await using var ctx = Factory.CrearContexto();
        var admin = await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var clienteAdmin = ClienteAutenticado(jwt.GenerarToken(admin.Id, RolUsuario.Admin));
        var id = await CrearDocumentoAsync(clienteAdmin);
        var clienteSinPermiso = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await clienteSinPermiso.GetAsync($"/documentos/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPorId_Existente_Devuelve200ConLosDatos()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var id = await CrearDocumentoAsync(client);

        var response = await client.GetAsync($"/documentos/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documento = await response.Content.ReadFromJsonAsync<DocumentoDto>();
        Assert.Equal("0087", documento!.Numero);
        Assert.Equal(EstadoDocumento.Pendiente, documento.Estado);
    }

    [Fact]
    public async Task PutDocumentos_CorrigeNumero_Devuelve200()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var id = await CrearDocumentoAsync(client, numero: "0087");

        var response = await client.PutAsJsonAsync($"/documentos/{id}",
            new EditarDocumentoRequest("0088", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "Descripción de prueba"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutDocumentos_NumeroChocaConOtroDocumento_Devuelve409()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await CrearDocumentoAsync(client, numero: "0087");
        var idSegundo = await CrearDocumentoAsync(client, numero: "0088");

        var response = await client.PutAsJsonAsync($"/documentos/{idSegundo}",
            new EditarDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "Descripción de prueba"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutDocumentos_ConTokenOperadorSinPermiso_Devuelve403()
    {
        await using var ctx = Factory.CrearContexto();
        var admin = await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var clienteAdmin = ClienteAutenticado(jwt.GenerarToken(admin.Id, RolUsuario.Admin));
        var id = await CrearDocumentoAsync(clienteAdmin);
        var clienteSinPermiso = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await clienteSinPermiso.PutAsJsonAsync($"/documentos/{id}",
            new EditarDocumentoRequest("0099", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "x"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```
- [ ] 13.2 Correr y ver que falla (no existe `DocumentosEndpoints`, `DocumentoDto`, etc.):
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DocumentosEndpointTests`
  Salida esperada: error de compilación `CS0246: The type or namespace name 'CrearDocumentoRequest' could not be found` (o similar por cada tipo aún inexistente).
- [ ] 13.3 Implementación mínima. Crear `src/StockApp.Api/Endpoints/DocumentosEndpoints.cs`:
```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record EventoDocumentoDto(
    int Id, int UsuarioId, DateTime Fecha,
    EstadoDocumento? EstadoAnterior, EstadoDocumento? EstadoNuevo,
    string Texto, bool EsAutomatico);

public record DocumentoDto(
    int Id, string Numero, int Anio, TipoDocumento Tipo,
    DateTime FechaEmision, string Descripcion, EstadoDocumento Estado,
    int RegistradoPorUsuarioId, string? RegistradoPorNombre,
    DateTime FechaRegistro, DateTime? FechaCierre,
    bool EsActivo, bool EsCerrado,
    List<EventoDocumentoDto> Eventos);

public record CrearDocumentoRequest(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion);
public record EditarDocumentoRequest(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion);
public record DocumentoCreadoResponse(int Id);

public static class DocumentosEndpoints
{
    public static IEndpointRouteBuilder MapDocumentosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documentos");

        group.MapGet("/activos", async (
            TipoDocumento? tipo, int? anio, string? texto, IDocumentoAdministrativoService documentos) =>
        {
            var filtro = new FiltroDocumentos(tipo, anio, texto, null);
            return Results.Ok((await documentos.ListarActivosAsync(filtro)).Select(ADto));
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        // D9: anio es OBLIGATORIO acá — el binding lo deja pasar como null (int? de Minimal API),
        // ListarHistorialAsync es quien lo rechaza con ArgumentException -> 400 (D9/Application).
        group.MapGet("/historial", async (
            TipoDocumento? tipo, int? anio, string? texto, EstadoDocumento? estado,
            IDocumentoAdministrativoService documentos) =>
        {
            var filtro = new FiltroDocumentos(tipo, anio, texto, estado);
            return Results.Ok((await documentos.ListarHistorialAsync(filtro)).Select(ADto));
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapGet("/{id:int}", async (int id, IDocumentoAdministrativoService documentos) =>
        {
            var documento = await documentos.ObtenerPorIdAsync(id);
            return documento is null ? Results.NotFound() : Results.Ok(ADto(documento));
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/", async (CrearDocumentoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            var documento = new DocumentoAdministrativo
            {
                Numero = request.Numero,
                Anio = request.Anio,
                Tipo = request.Tipo,
                FechaEmision = request.FechaEmision,
                Descripcion = request.Descripcion,
            };
            var id = await documentos.RegistrarAsync(documento);
            return Results.Created((string?)null, new DocumentoCreadoResponse(id));
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPut("/{id:int}", async (int id, EditarDocumentoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.EditarAsync(id, new DatosEdicionDocumento(
                request.Numero, request.Anio, request.Tipo, request.FechaEmision, request.Descripcion));
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        return app;
    }

    private static DocumentoDto ADto(DocumentoAdministrativo d) => new(
        d.Id, d.Numero, d.Anio, d.Tipo,
        d.FechaEmision, d.Descripcion, d.Estado,
        d.RegistradoPorUsuarioId, d.RegistradoPor?.NombreUsuario,
        d.FechaRegistro, d.FechaCierre,
        d.EsActivo, d.EsCerrado,
        d.Eventos.OrderBy(e => e.Fecha).ThenBy(e => e.Id)
            .Select(e => new EventoDocumentoDto(
                e.Id, e.UsuarioId, e.Fecha, e.EstadoAnterior, e.EstadoNuevo, e.Texto, e.EsAutomatico))
            .ToList());
}
```
- [ ] 13.4 Modificar `src/StockApp.Api/Program.cs`. Tras el bloque de DI de Tareas (buscar `builder.Services.AddScoped<ITareaService, TareaService>();`, ~línea 116), agregar:
```csharp
builder.Services.AddScoped<IDocumentoAdministrativoRepository, DocumentoAdministrativoRepository>();
builder.Services.AddScoped<IDocumentoAdministrativoService, DocumentoAdministrativoService>();
```
  Tras `app.MapTareasEndpoints();` (~línea 627), agregar:
```csharp
app.MapDocumentosEndpoints();
```
  Nota: esta es la ÚNICA tarea del plan que registra `IDocumentoAdministrativoRepository`/`IDocumentoAdministrativoService` en `Program.cs` — las Tasks 1-12 (dominio, persistencia, aplicación) no tocan `Program.cs`. Una sola línea por tipo, sin duplicar.
- [ ] 13.5 Correr y ver que pasa:
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DocumentosEndpointTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 15`.
- [ ] 13.6 Commit:
  `git add src/StockApp.Api/Endpoints/DocumentosEndpoints.cs src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/DocumentosEndpointTests.cs`
  `git commit -m "feat(documentos): endpoints de lectura y alta/edición de documentos administrativos"`

---

## Task 14: Api — `DocumentosEndpoints.cs` (transiciones de estado)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/DocumentosEndpoints.cs` (agrega `iniciar`, `volver-a-pendiente`, `finalizar`, `notas`, `anular`, `reabrir` dentro de `MapDocumentosEndpoints`)
- Modify: `tests/StockApp.Api.Tests/DocumentosEndpointTests.cs` (agrega los tests de transición)

**Interfaces:**
- Consumes: `IDocumentoAdministrativoService.IniciarProcesoAsync(int)`, `.VolverAPendienteAsync(int)`, `.FinalizarAsync(int)`, `.AgregarNotaAsync(int, string)`, `.AnularAsync(int, string)`, `.ReabrirAsync(int, string)`, `Permisos.AdministrarDocumentos`.
- Produces:
  - `POST /documentos/{id:int}/iniciar` → 200 | 404 | 409 (transición inválida).
  - `POST /documentos/{id:int}/volver-a-pendiente` → 200 | 404 | 409.
  - `POST /documentos/{id:int}/finalizar` → 200 | 404 | 409.
  - `POST /documentos/{id:int}/notas` → 200.
  - `POST /documentos/{id:int}/anular` → 200 | 403 | 409 (motivo vacío).
  - `POST /documentos/{id:int}/reabrir` → 200 | 403 | 409 (motivo vacío o documento no `EsCerrado`).
  - Tipos públicos nuevos: `AgregarNotaDocumentoRequest`, `MotivoRequest`.

**Steps:**

- [ ] 14.1 Agregar a `tests/StockApp.Api.Tests/DocumentosEndpointTests.cs`, dentro de la clase, antes de la llave de cierre:
```csharp
    [Fact]
    public async Task PostIniciar_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().PostAsync("/documentos/1/iniciar", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostIniciar_DocumentoInexistente_Devuelve404()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync("/documentos/9999/iniciar", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostIniciar_ConTokenOperador_Devuelve200()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/iniciar", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostIniciar_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de /iniciar, no representativo del de otro endpoint.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsync($"/documentos/{id}/iniciar", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostVolverAPendiente_DesdeEnProceso_Devuelve200()
    {
        // Análogo de TareaService.SoltarAsync (D4).
        var id = await SembrarDocumentoAsync(EstadoDocumento.EnProceso);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/volver-a-pendiente", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostVolverAPendiente_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de /volver-a-pendiente.
        var id = await SembrarDocumentoAsync(EstadoDocumento.EnProceso);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsync($"/documentos/{id}/volver-a-pendiente", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostFinalizar_DesdePendiente_Devuelve409()
    {
        // Máquina de estados (D4): Pendiente no puede pasar directo a Finalizado.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/finalizar", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostFinalizar_DesdeEnProceso_Devuelve200()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.EnProceso);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/finalizar", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostFinalizar_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de /finalizar.
        var id = await SembrarDocumentoAsync(EstadoDocumento.EnProceso);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsync($"/documentos/{id}/finalizar", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostNotas_ConTokenOperador_Devuelve200()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/notas", new AgregarNotaDocumentoRequest("avance del trámite"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostNotas_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de /notas.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/notas", new AgregarNotaDocumentoRequest("avance del trámite"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostAnular_ConTokenOperador_Devuelve403()
    {
        // D7: documentos.administrar es estructural, Operador nunca lo tiene.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/anular", new MotivoRequest("el interesado desistió"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostAnular_ConTokenAdmin_MotivoVacio_Devuelve409()
    {
        // D8: motivo obligatorio -> ReglaDeNegocioException, no una validación de request (400).
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync($"/documentos/{id}/anular", new MotivoRequest(""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostAnular_ConTokenAdmin_Devuelve200()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/anular", new MotivoRequest("el interesado desistió"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostReabrir_ConTokenOperador_Devuelve403()
    {
        // D7: documentos.administrar es estructural, Operador nunca lo tiene — 403 propio de
        // /reabrir, no representativo del de /anular (C7).
        var id = await SembrarDocumentoAsync(EstadoDocumento.Finalizado);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/reabrir", new MotivoRequest("se encontró documentación nueva"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostReabrir_SobreDocumentoNoCerrado_Devuelve409()
    {
        // D8: Pendiente -> EnProceso ya es válida por otra vía (iniciar); ReabrirAsync exige
        // EsCerrado explícitamente, así que sobre Pendiente debe rechazar, no dejarlo pasar
        // como si fuera un IniciarProcesoAsync más.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/reabrir", new MotivoRequest("se encontró documentación nueva"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostReabrir_ConTokenAdmin_SobreDocumentoCerrado_Devuelve200()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Finalizado);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/reabrir", new MotivoRequest("se encontró documentación nueva"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
```
- [ ] 14.2 Correr y ver que falla (no existen las rutas `/iniciar`, `/anular`, etc. ni los tipos `AgregarNotaDocumentoRequest`/`MotivoRequest`):
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DocumentosEndpointTests`
  Salida esperada: `CS0246: The type or namespace name 'MotivoRequest' could not be found` y, tras resolver eso, 404 en los `Assert.Equal(HttpStatusCode.OK, ...)` de las rutas de transición.
- [ ] 14.3 Implementación. En `src/StockApp.Api/Endpoints/DocumentosEndpoints.cs`, agregar los dos records nuevos junto a los existentes:
```csharp
public record AgregarNotaDocumentoRequest(string Texto);
public record MotivoRequest(string Motivo);
```
  Y, dentro de `MapDocumentosEndpoints`, antes del `return app;`:
```csharp
        group.MapPost("/{id:int}/iniciar", async (int id, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.IniciarProcesoAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/{id:int}/volver-a-pendiente", async (int id, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.VolverAPendienteAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/{id:int}/finalizar", async (int id, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.FinalizarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/{id:int}/notas", async (int id, AgregarNotaDocumentoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.AgregarNotaAsync(id, request.Texto);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/{id:int}/anular", async (int id, MotivoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.AnularAsync(id, request.Motivo);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarDocumentos);

        group.MapPost("/{id:int}/reabrir", async (int id, MotivoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.ReabrirAsync(id, request.Motivo);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarDocumentos);
```
- [ ] 14.4 Correr y ver que pasa:
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DocumentosEndpointTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 32`.
- [ ] 14.5 Commit:
  `git add src/StockApp.Api/Endpoints/DocumentosEndpoints.cs tests/StockApp.Api.Tests/DocumentosEndpointTests.cs`
  `git commit -m "feat(documentos): endpoints de transición de estado (iniciar/finalizar/anular/reabrir)"`

---

## Task 15: Api — `DocumentosEndpoints.cs` (adjuntos, multipart)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/DocumentosEndpoints.cs` (agrega los 4 endpoints de adjuntos)
- Modify: `src/StockApp.Api/Program.cs` (DI de `IAdjuntoDocumentoRepository`/`IAdjuntoDocumentoService`)
- Modify: `tests/StockApp.Api.Tests/DocumentosEndpointTests.cs` (agrega los tests de adjuntos)

**Interfaces:**
- Consumes: `IAdjuntoDocumentoService` (`AgregarAsync(int, string, byte[])`, `ListarPorDocumentoAsync(int)`, `ObtenerContenidoAsync(int)`, `QuitarAsync(int)`), `IAdjuntoDocumentoRepository`/`AdjuntoDocumentoRepository`, `AdjuntoDocumentoDto`, `AdjuntoDocumentoContenidoDto` (ya definidos en `StockApp.Application.Documentos` por los Bloques A/B — no se redefinen acá).
- Produces:
  - `POST /documentos/{id:int}/adjuntos` (multipart, campo `archivo`) → 201 `AdjuntoDocumentoDto` | 409 (documento no `EsActivo`, o MIME inválido).
  - `GET /documentos/{id:int}/adjuntos` → 200 `List<AdjuntoDocumentoDto>`.
  - `GET /documentos/adjuntos/{adjuntoId:int}/contenido` → `Results.File(...)` | 404.
  - `DELETE /documentos/adjuntos/{adjuntoId:int}` → 200 | 403.

**Steps:**

- [ ] 15.1 Agregar a `tests/StockApp.Api.Tests/DocumentosEndpointTests.cs`: el using `System.Net.Http.Headers` ya está declarado (Task 13); agregar `using StockApp.Application.Documentos;` al bloque de usings, y estos miembros dentro de la clase:
```csharp
    private static readonly byte[] BytesPdf = { 0x25, 0x50, 0x44, 0x46, 0x01, 0x02 };

    private static MultipartFormDataContent ArmarMultipart(byte[] bytes, string nombre)
    {
        var contenido = new MultipartFormDataContent();
        var archivo = new ByteArrayContent(bytes);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        contenido.Add(archivo, "archivo", nombre);
        return contenido;
    }

    [Fact]
    public async Task PostAdjunto_ComoOperador_Devuelve201()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/adjuntos", ArmarMultipart(BytesPdf, "expediente.pdf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AdjuntoDocumentoDto>();
        Assert.Equal("expediente.pdf", dto!.NombreArchivo);
        Assert.Equal(id, dto.DocumentoAdministrativoId);
    }

    [Fact]
    public async Task PostAdjunto_SinToken_Devuelve401()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);

        var response = await Factory.CreateClient().PostAsync(
            $"/documentos/{id}/adjuntos", ArmarMultipart(BytesPdf, "expediente.pdf"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjunto_SobreDocumentoCerrado_Devuelve409()
    {
        // D11a: ni agregar ni quitar adjuntos está permitido salvo que el documento esté EsActivo.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Finalizado);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/adjuntos", ArmarMultipart(BytesPdf, "expediente.pdf"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjunto_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de POST /adjuntos.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsync($"/documentos/{id}/adjuntos", ArmarMultipart(BytesPdf, "expediente.pdf"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAdjuntos_ListaLosActivos()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await client.PostAsync($"/documentos/{id}/adjuntos", ArmarMultipart(BytesPdf, "expediente.pdf"));

        var response = await client.GetAsync($"/documentos/{id}/adjuntos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<AdjuntoDocumentoDto>>();
        Assert.Single(lista!);
    }

    [Fact]
    public async Task GetAdjuntos_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de GET /{id}/adjuntos.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.GetAsync($"/documentos/{id}/adjuntos");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_DevuelveLosBytesOriginales()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var creado = await client.PostAsync($"/documentos/{id}/adjuntos", ArmarMultipart(BytesPdf, "expediente.pdf"));
        var dto = await creado.Content.ReadFromJsonAsync<AdjuntoDocumentoDto>();

        var response = await client.GetAsync($"/documentos/adjuntos/{dto!.Id}/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(BytesPdf, bytes);
    }

    [Fact]
    public async Task GetContenido_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de GET /adjuntos/{id}/contenido.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var clienteOperador = ClienteAutenticado(TokenOperador());
        var creado = await clienteOperador.PostAsync($"/documentos/{id}/adjuntos", ArmarMultipart(BytesPdf, "expediente.pdf"));
        var dto = await creado.Content.ReadFromJsonAsync<AdjuntoDocumentoDto>();

        await using var ctx = Factory.CrearContexto();
        var operadorSinPermiso = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var clienteSinPermiso = ClienteAutenticado(jwt.GenerarToken(operadorSinPermiso.Id, RolUsuario.Operador));

        var response = await clienteSinPermiso.GetAsync($"/documentos/adjuntos/{dto!.Id}/contenido");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_Inexistente_Devuelve404()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/documentos/adjuntos/999999/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAdjunto_ConTokenOperador_Devuelve403()
    {
        // D11b: quitar exige documentos.administrar, no gestionar (a diferencia de agregar).
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var creado = await client.PostAsync($"/documentos/{id}/adjuntos", ArmarMultipart(BytesPdf, "expediente.pdf"));
        var dto = await creado.Content.ReadFromJsonAsync<AdjuntoDocumentoDto>();

        var response = await client.DeleteAsync($"/documentos/adjuntos/{dto!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAdjunto_ConTokenAdmin_HaceBajaLogica()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var clienteOperador = ClienteAutenticado(TokenOperador());
        var creado = await clienteOperador.PostAsync($"/documentos/{id}/adjuntos", ArmarMultipart(BytesPdf, "expediente.pdf"));
        var dto = await creado.Content.ReadFromJsonAsync<AdjuntoDocumentoDto>();
        var clienteAdmin = ClienteAutenticado(TokenAdmin());

        var response = await clienteAdmin.DeleteAsync($"/documentos/adjuntos/{dto!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listado = await clienteOperador.GetAsync($"/documentos/{id}/adjuntos");
        var lista = await listado.Content.ReadFromJsonAsync<List<AdjuntoDocumentoDto>>();
        Assert.Empty(lista!);
    }
```
- [ ] 15.2 Correr y ver que falla (no existen las rutas de adjuntos):
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DocumentosEndpointTests`
  Salida esperada: 404 en los `Assert.Equal(HttpStatusCode.Created, ...)`/`Assert.Equal(HttpStatusCode.OK, ...)` de los 11 tests nuevos.
- [ ] 15.3 Implementación. En `src/StockApp.Api/Endpoints/DocumentosEndpoints.cs`, agregar `using StockApp.Application.Documentos;` ya está (Task 13); dentro de `MapDocumentosEndpoints`, antes del `return app;`:
```csharp
        group.MapPost("/{id:int}/adjuntos", async (int id, IFormFile archivo, IAdjuntoDocumentoService adjuntos) =>
        {
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            var dto = await adjuntos.AgregarAsync(id, archivo.FileName, ms.ToArray());
            return Results.Created((string?)null, dto);
        })
        .DisableAntiforgery()
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapGet("/{id:int}/adjuntos", async (int id, IAdjuntoDocumentoService adjuntos) =>
            Results.Ok(await adjuntos.ListarPorDocumentoAsync(id)))
            .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapGet("/adjuntos/{adjuntoId:int}/contenido", async (int adjuntoId, IAdjuntoDocumentoService adjuntos) =>
        {
            var contenido = await adjuntos.ObtenerContenidoAsync(adjuntoId);
            return Results.File(contenido.Contenido, contenido.ContentType, contenido.NombreArchivo);
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapDelete("/adjuntos/{adjuntoId:int}", async (int adjuntoId, IAdjuntoDocumentoService adjuntos) =>
        {
            await adjuntos.QuitarAsync(adjuntoId);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarDocumentos);
```
  Nota de ruteo: `/documentos/adjuntos/{adjuntoId:int}/contenido` y `/documentos/{id:int}/adjuntos` no colisionan — "adjuntos" es un segmento literal que nunca matchea la restricción `{id:int}`, mismo razonamiento que ya usa el proyecto para `/finanzas/gastos/por-factura` vs `/finanzas/gastos/{id:int}` en `GastosEndpoints.cs`.
- [ ] 15.4 Modificar `src/StockApp.Api/Program.cs`. Junto al DI agregado en la Task 13, agregar:
```csharp
builder.Services.AddScoped<IAdjuntoDocumentoRepository, AdjuntoDocumentoRepository>();
builder.Services.AddScoped<IAdjuntoDocumentoService, AdjuntoDocumentoService>();
```
  Misma nota que 13.4: esta es la ÚNICA tarea que registra `IAdjuntoDocumentoRepository`/`IAdjuntoDocumentoService` — una sola línea por tipo.
- [ ] 15.5 Correr y ver que pasa:
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DocumentosEndpointTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 43`.
- [ ] 15.6 Correr la suite completa de `StockApp.Api.Tests` (no solo el filtro) para descartar rotura de otros endpoints por el registro nuevo del grupo `/documentos`:
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
  Salida esperada: todos los tests preexistentes siguen en verde (ningún fallo fuera de `DocumentosEndpointTests`).
- [ ] 15.7 Commit:
  `git add src/StockApp.Api/Endpoints/DocumentosEndpoints.cs src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/DocumentosEndpointTests.cs`
  `git commit -m "feat(documentos): endpoints de adjuntos (subida multipart, descarga, baja lógica)"`

---

## Task 16: ApiClient — `DocumentoApiClient`

**Files:**
- Create: `src/StockApp.ApiClient/DocumentoApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/DocumentoApiClientTests.cs`

**Interfaces:**
- Consumes: `IDocumentoAdministrativoService` (contrato de dominio, del lado cliente), `FiltroDocumentos`, `DatosEdicionDocumento` (`StockApp.Application.Documentos`), `DocumentoAdministrativo`/`EventoDocumento`/`Usuario` (`StockApp.Domain.Entities`), `ApiErrores`, `ApiQuery`, `IdCreado` (internos de `StockApp.ApiClient`, ya existentes).
- Produces: `public sealed class DocumentoApiClient : IDocumentoAdministrativoService` contra `/documentos`.

**Steps:**

- [ ] 16.1 Escribir `tests/StockApp.ApiClient.Tests/DocumentoApiClientTests.cs` (patrón exacto de `TareaApiClientTests`, con `FakeHttpHandler`/`TestHttp`):
```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class DocumentoApiClientTests
{
    [Fact]
    public async Task RegistrarAsync_POSTDocumentos_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 9 }, HttpStatusCode.Created));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.RegistrarAsync(new DocumentoAdministrativo
        {
            Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
            FechaEmision = new DateTime(2026, 1, 15), Descripcion = "Expediente de prueba",
        });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/documentos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"numero\":\"0087\"", fake.UltimoBody);
        Assert.Equal(9, id);
    }

    [Fact]
    public async Task RegistrarAsync_409_LanzaReglaDeNegocioException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe un Expediente 0087/2026."));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.RegistrarAsync(new DocumentoAdministrativo
        {
            Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
            FechaEmision = new DateTime(2026, 1, 15), Descripcion = "x",
        }));
        Assert.Equal("Ya existe un Expediente 0087/2026.", ex.Message);
    }

    [Fact]
    public async Task EditarAsync_PUTDocumentosId_ConLaRutaCorrecta()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        await client.EditarAsync(5, new DatosEdicionDocumento(
            "0088", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "corregido"));

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/documentos/5", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"numero\":\"0088\"", fake.UltimoBody);
    }

    [Fact]
    public async Task ListarActivosAsync_GETDocumentosActivos_ConQueryYDeserializa()
    {
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal("documentos/activos", request.RequestUri!.AbsolutePath.TrimStart('/'));
            Assert.Contains("tipo=0", request.RequestUri.Query);
            return TestHttp.Json(new[]
            {
                new
                {
                    id = 1, numero = "0087", anio = 2026, tipo = TipoDocumento.Expediente,
                    fechaEmision = new DateTime(2026, 1, 15), descripcion = "x", estado = EstadoDocumento.Pendiente,
                    registradoPorUsuarioId = 1, registradoPorNombre = "admin",
                    fechaRegistro = new DateTime(2026, 1, 15), fechaCierre = (DateTime?)null,
                    esActivo = true, esCerrado = false,
                    eventos = Array.Empty<object>(),
                },
            });
        });
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var documentos = await client.ListarActivosAsync(new FiltroDocumentos(TipoDocumento.Expediente, null, null, null));

        var documento = Assert.Single(documentos);
        Assert.Equal("0087", documento.Numero);
        Assert.Equal("admin", documento.RegistradoPor!.NombreUsuario);
    }

    [Fact]
    public async Task ListarHistorialAsync_GETDocumentosHistorial_ConAnioEnQuery()
    {
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal("documentos/historial", request.RequestUri!.AbsolutePath.TrimStart('/'));
            Assert.Contains("anio=2026", request.RequestUri.Query);
            return TestHttp.Json(Array.Empty<object>());
        });
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var documentos = await client.ListarHistorialAsync(new FiltroDocumentos(null, 2026, null, null));

        Assert.Empty(documentos);
    }

    [Fact]
    public async Task ListarHistorialAsync_400_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "El filtro 'anio' es obligatorio para consultar el historial."));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => client.ListarHistorialAsync(new FiltroDocumentos(null, null, null, null)));
        Assert.Equal("El filtro 'anio' es obligatorio para consultar el historial.", ex.Message);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_GETDocumentosId_Deserializa()
    {
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal("documentos/5", request.RequestUri!.AbsolutePath.TrimStart('/'));
            return TestHttp.Json(new
            {
                id = 5, numero = "0087", anio = 2026, tipo = TipoDocumento.Expediente,
                fechaEmision = new DateTime(2026, 1, 15), descripcion = "x", estado = EstadoDocumento.Pendiente,
                registradoPorUsuarioId = 1, registradoPorNombre = (string?)null,
                fechaRegistro = new DateTime(2026, 1, 15), fechaCierre = (DateTime?)null,
                esActivo = true, esCerrado = false,
                eventos = Array.Empty<object>(),
            });
        });
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var documento = await client.ObtenerPorIdAsync(5);

        Assert.Equal("0087", documento!.Numero);
        Assert.Null(documento.RegistradoPor);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_404_DevuelveNull()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.NotFound, "Documento 999 no encontrado."));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        var documento = await client.ObtenerPorIdAsync(999);

        Assert.Null(documento);
    }

    [Fact]
    public async Task AnularAsync_POSTAnular_ConLaRutaYElMotivo()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        await client.AnularAsync(5, "el interesado desistió");

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/documentos/5/anular", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"motivo\":\"el interesado desistió\"", fake.UltimoBody);
    }

    [Fact]
    public async Task ReabrirAsync_403_LanzaUnauthorizedAccessException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol Operador no tiene permiso para ejecutar la acción 'documentos.administrar'."));
        var client = new DocumentoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.ReabrirAsync(5, "motivo"));
    }
}
```
- [ ] 16.2 Correr y ver que falla (no existe `DocumentoApiClient`):
  `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter DocumentoApiClientTests`
  Salida esperada: `CS0246: The type or namespace name 'DocumentoApiClient' could not be found`.
- [ ] 16.3 Implementación. Crear `src/StockApp.ApiClient/DocumentoApiClient.cs`:
```csharp
using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient;

internal sealed record EventoDocumentoWire(
    int Id, int UsuarioId, DateTime Fecha,
    EstadoDocumento? EstadoAnterior, EstadoDocumento? EstadoNuevo,
    string Texto, bool EsAutomatico);

internal sealed record DocumentoWire(
    int Id, string Numero, int Anio, TipoDocumento Tipo,
    DateTime FechaEmision, string Descripcion, EstadoDocumento Estado,
    int RegistradoPorUsuarioId, string? RegistradoPorNombre,
    DateTime FechaRegistro, DateTime? FechaCierre,
    bool EsActivo, bool EsCerrado,
    List<EventoDocumentoWire> Eventos);

internal sealed record CrearDocumentoBody(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion);
internal sealed record EditarDocumentoBody(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion);
internal sealed record AgregarNotaDocumentoBody(string Texto);
internal sealed record MotivoBody(string Motivo);

/// <summary>IDocumentoAdministrativoService contra /documentos.</summary>
public sealed class DocumentoApiClient : IDocumentoAdministrativoService
{
    private readonly HttpClient _http;

    public DocumentoApiClient(HttpClient http) => _http = http;

    public async Task<int> RegistrarAsync(DocumentoAdministrativo documento)
    {
        var body = new CrearDocumentoBody(
            documento.Numero, documento.Anio, documento.Tipo, documento.FechaEmision, documento.Descripcion);
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync("documentos", body));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al registrar el documento.");
        return creado.Id;
    }

    public async Task EditarAsync(int id, DatosEdicionDocumento datos)
    {
        var body = new EditarDocumentoBody(datos.Numero, datos.Anio, datos.Tipo, datos.FechaEmision, datos.Descripcion);
        var response = await ApiErrores.EnviarAsync(() => _http.PutAsJsonAsync($"documentos/{id}", body));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro)
        => ListarAsync("documentos/activos", filtro, incluirEstado: false);

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarHistorialAsync(FiltroDocumentos filtro)
        => ListarAsync("documentos/historial", filtro, incluirEstado: true);

    private async Task<IReadOnlyList<DocumentoAdministrativo>> ListarAsync(
        string ruta, FiltroDocumentos filtro, bool incluirEstado)
    {
        var query = ApiQuery.Construir(
            ("tipo", filtro.Tipo is null ? null : ((int)filtro.Tipo.Value).ToString(CultureInfo.InvariantCulture)),
            ("anio", filtro.Anio?.ToString(CultureInfo.InvariantCulture)),
            ("texto", filtro.Texto),
            ("estado", !incluirEstado || filtro.Estado is null
                ? null : ((int)filtro.Estado.Value).ToString(CultureInfo.InvariantCulture)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta + query));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<DocumentoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    public async Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id)
    {
        try
        {
            var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"documentos/{id}"));
            await ApiErrores.AsegurarExitoAsync(response);

            var dto = await response.Content.ReadFromJsonAsync<DocumentoWire>();
            return dto is null ? null : AEntidad(dto);
        }
        catch (EntidadNoEncontradaException)
        {
            return null;  // 404 = documento inexistente: contrato de la interfaz (null)
        }
    }

    public Task IniciarProcesoAsync(int id) => PostSinBodyAsync($"documentos/{id}/iniciar");
    public Task VolverAPendienteAsync(int id) => PostSinBodyAsync($"documentos/{id}/volver-a-pendiente");
    public Task FinalizarAsync(int id) => PostSinBodyAsync($"documentos/{id}/finalizar");

    public async Task AgregarNotaAsync(int id, string texto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"documentos/{id}/notas", new AgregarNotaDocumentoBody(texto)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task AnularAsync(int id, string motivo)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"documentos/{id}/anular", new MotivoBody(motivo)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task ReabrirAsync(int id, string motivo)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"documentos/{id}/reabrir", new MotivoBody(motivo)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private async Task PostSinBodyAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsync(ruta, content: null));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private static DocumentoAdministrativo AEntidad(DocumentoWire dto) => new()
    {
        Id = dto.Id,
        Numero = dto.Numero,
        Anio = dto.Anio,
        Tipo = dto.Tipo,
        FechaEmision = dto.FechaEmision,
        Descripcion = dto.Descripcion,
        Estado = dto.Estado,
        RegistradoPorUsuarioId = dto.RegistradoPorUsuarioId,
        RegistradoPor = dto.RegistradoPorNombre is null
            ? null : new Usuario { Id = dto.RegistradoPorUsuarioId, NombreUsuario = dto.RegistradoPorNombre },
        FechaRegistro = dto.FechaRegistro,
        FechaCierre = dto.FechaCierre,
        Eventos = dto.Eventos.Select(e => new EventoDocumento
        {
            Id = e.Id, DocumentoAdministrativoId = dto.Id, UsuarioId = e.UsuarioId, Fecha = e.Fecha,
            EstadoAnterior = e.EstadoAnterior, EstadoNuevo = e.EstadoNuevo,
            Texto = e.Texto, EsAutomatico = e.EsAutomatico,
        }).ToList(),
    };
}
```
  Nota: `EsActivo`/`EsCerrado` NO se asignan en `AEntidad` — son propiedades derivadas de `Estado` en el dominio (D4), no campos propios; asignarlas sería, en el mejor caso, ignorado por el compilador (si son solo getter) o una duplicación de la fuente de verdad (si el Bloque A/B las modeló como settable). Si `DocumentoAdministrativo.EsActivo`/`EsCerrado` resultan ser propiedades de solo lectura (`=> Estado is ...`), este código compila tal cual; si el Bloque A/B las definió como propiedades con setter, no hace falta setearlas igual porque se derivan solas al setear `Estado`.
- [ ] 16.4 Correr y ver que pasa:
  `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter DocumentoApiClientTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 10`.
- [ ] 16.5 Commit:
  `git add src/StockApp.ApiClient/DocumentoApiClient.cs tests/StockApp.ApiClient.Tests/DocumentoApiClientTests.cs`
  `git commit -m "feat(documentos): DocumentoApiClient contra /documentos"`

---

## Task 17: ApiClient — `AdjuntoDocumentoApiClient`

**Files:**
- Create: `src/StockApp.ApiClient/AdjuntoDocumentoApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/AdjuntoDocumentoApiClientTests.cs`

**Interfaces:**
- Consumes: `IAdjuntoDocumentoService`, `AdjuntoDocumentoDto`, `AdjuntoDocumentoContenidoDto` (`StockApp.Application.Documentos`), `ApiErrores`.
- Produces: `public sealed class AdjuntoDocumentoApiClient : IAdjuntoDocumentoService` contra `/documentos/.../adjuntos`.

**Steps:**

- [ ] 17.1 Escribir `tests/StockApp.ApiClient.Tests/AdjuntoDocumentoApiClientTests.cs` (patrón exacto de `AdjuntoApiClientTests`):
```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Documentos;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class AdjuntoDocumentoApiClientTests
{
    [Fact]
    public async Task AgregarAsync_EnviaMultipartYParseaRespuesta()
    {
        var dto = new AdjuntoDocumentoDto(1, 5, "expediente.pdf", "application/pdf", 100, DateTime.UtcNow);
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("documentos/5/adjuntos", request.RequestUri!.PathAndQuery.TrimStart('/'));
            Assert.IsType<MultipartFormDataContent>(request.Content);
            return TestHttp.Json(dto, HttpStatusCode.Created);
        });
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.AgregarAsync(5, "expediente.pdf", new byte[] { 1, 2, 3 });

        Assert.Equal(1, resultado.Id);
        Assert.Equal("expediente.pdf", resultado.NombreArchivo);
    }

    [Fact]
    public async Task AgregarAsync_ErrorDelServidor_LanzaExcepcionDeDominio()
    {
        var fake = new FakeHttpHandler(_ =>
            TestHttp.Problema(HttpStatusCode.NotFound, "El documento no existe."));
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => client.AgregarAsync(999, "expediente.pdf", new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_GETParseaListaJson()
    {
        var dtos = new[]
        {
            new AdjuntoDocumentoDto(1, 5, "a.pdf", "application/pdf", 10, DateTime.UtcNow),
            new AdjuntoDocumentoDto(2, 5, "b.pdf", "application/pdf", 20, DateTime.UtcNow),
        };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("documentos/5/adjuntos", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dtos);
        });
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ListarPorDocumentoAsync(5);

        Assert.Equal(2, resultado.Count);
        Assert.Equal("a.pdf", resultado[0].NombreArchivo);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_DevuelveBytesYNombreDesdeHeaders()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("documentos/adjuntos/1/contenido", request.RequestUri!.PathAndQuery.TrimStart('/'));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition =
                new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = "expediente.pdf" };
            return response;
        });
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ObtenerContenidoAsync(1);

        Assert.Equal(bytes, resultado.Contenido);
        Assert.Equal("expediente.pdf", resultado.NombreArchivo);
        Assert.Equal("application/pdf", resultado.ContentType);
    }

    [Fact]
    public async Task QuitarAsync_EnviaDelete()
    {
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("documentos/adjuntos/7", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        await client.QuitarAsync(7);
    }

    [Fact]
    public async Task QuitarAsync_403_LanzaUnauthorizedAccessException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol Operador no tiene permiso para ejecutar la acción 'documentos.administrar'."));
        var client = new AdjuntoDocumentoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.QuitarAsync(7));
    }
}
```
- [ ] 17.2 Correr y ver que falla (no existe `AdjuntoDocumentoApiClient`):
  `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter AdjuntoDocumentoApiClientTests`
  Salida esperada: `CS0246: The type or namespace name 'AdjuntoDocumentoApiClient' could not be found`.
- [ ] 17.3 Implementación. Crear `src/StockApp.ApiClient/AdjuntoDocumentoApiClient.cs`:
```csharp
using System.Net.Http.Json;
using StockApp.Application.Documentos;

namespace StockApp.ApiClient;

/// <summary>
/// IAdjuntoDocumentoService contra /documentos/.../adjuntos. Mismo patrón multipart (upload) y
/// descarga de bytes crudos que AdjuntoApiClient (Finanzas) — ver ese archivo para el porqué del
/// FileNameStar preferido sobre FileName al leer Content-Disposition.
/// </summary>
public sealed class AdjuntoDocumentoApiClient : IAdjuntoDocumentoService
{
    private readonly HttpClient _http;

    public AdjuntoDocumentoApiClient(HttpClient http) => _http = http;

    public async Task<AdjuntoDocumentoDto> AgregarAsync(int documentoId, string nombreArchivo, byte[] contenido)
    {
        using var multipart = new MultipartFormDataContent();
        using var archivo = new ByteArrayContent(contenido);
        multipart.Add(archivo, "archivo", nombreArchivo);

        var response = await ApiErrores.EnviarAsync(() => _http.PostAsync($"documentos/{documentoId}/adjuntos", multipart));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<AdjuntoDocumentoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al subir el adjunto.");
    }

    public async Task<IReadOnlyList<AdjuntoDocumentoDto>> ListarPorDocumentoAsync(int documentoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"documentos/{documentoId}/adjuntos"));
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<List<AdjuntoDocumentoDto>>() ?? new List<AdjuntoDocumentoDto>();
    }

    public async Task<AdjuntoDocumentoContenidoDto> ObtenerContenidoAsync(int adjuntoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"documentos/adjuntos/{adjuntoId}/contenido"));
        await ApiErrores.AsegurarExitoAsync(response);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentDisposition = response.Content.Headers.ContentDisposition;
        var nombreArchivo = contentDisposition?.FileNameStar?.Trim('"')
            ?? contentDisposition?.FileName?.Trim('"')
            ?? "adjunto";
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        return new AdjuntoDocumentoContenidoDto(nombreArchivo, contentType, bytes);
    }

    public async Task QuitarAsync(int adjuntoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"documentos/adjuntos/{adjuntoId}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }
}
```
- [ ] 17.4 Correr y ver que pasa:
  `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter AdjuntoDocumentoApiClientTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 5`.
- [ ] 17.5 Correr la suite completa de `StockApp.ApiClient.Tests` para descartar roturas cruzadas:
  `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj`
  Salida esperada: todos los tests preexistentes en verde.
- [ ] 17.6 Correr la suite completa de `StockApp.Api.Tests` una vez más, ahora con el módulo completo (Tasks 13-15) — **secuencial**, nunca en paralelo con `StockApp.Application.Tests` (colisión de Testcontainers, ~303 falsos rojos si se corren juntos):
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
  Salida esperada: todos los tests en verde.
- [ ] 17.7 Commit:
  `git add src/StockApp.ApiClient/AdjuntoDocumentoApiClient.cs tests/StockApp.ApiClient.Tests/AdjuntoDocumentoApiClientTests.cs`
  `git commit -m "feat(documentos): AdjuntoDocumentoApiClient contra /documentos/.../adjuntos"`
## Task 18: `PedirTextoAsync` en `IConfirmacionService`

**Files:**
- Modify: `src/StockApp.Presentation/Services/IConfirmacionService.cs`
- Modify: `src/StockApp.Presentation/Services/ConfirmacionService.cs`
- Create: `src/StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml`
- Create: `src/StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml.cs`
- Modify: `tests/StockApp.Presentation.UiTests/MovimientoRegistroFakes.cs` (ripple: `ConfirmacionServiceFake` implementa `IConfirmacionService` y se usa en ~10 archivos de `StockApp.Presentation.UiTests`; sin este método el proyecto entero de UiTests no compila)

**Interfaces:**
- Produces: `IConfirmacionService.PedirTextoAsync(string titulo, string mensaje)` → `Task<string?>` (`null` si el usuario cancela); `ConfirmacionService.PedirTextoAsync` (implementación real, diálogo modal); `PedirTextoDialog` (Window, `ShowDialog<string?>`); `ConfirmacionServiceFake.PedirTextoAsync` con `TextoAPedir` configurable (default no-null) y `PedidosDeTexto` como espía.

No existe en la app ningún diálogo que pida texto libre — ni siquiera `GastoService.AnularAsync` lo pide hoy (confirmado: su firma es `AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false)`, sin motivo). Esta pieza es infraestructura nueva, sin precedente, que además le sirve a Finanzas el día que se decida agregarle motivo a `GastoService.AnularAsync`.

- [ ] **Step 1: Agregar el método a la interfaz**

```csharp
// src/StockApp.Presentation/Services/IConfirmacionService.cs
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Servicio de confirmación: muestra un diálogo y devuelve la respuesta del usuario.
/// Se inyecta como Singleton y se mockea en tests de ViewModel.
/// </summary>
public interface IConfirmacionService
{
    /// <summary>
    /// Muestra un mensaje de confirmación al usuario y espera su respuesta.
    /// </summary>
    /// <param name="mensaje">Texto del mensaje a mostrar.</param>
    /// <returns>true si el usuario confirmó, false si canceló.</returns>
    Task<bool> PreguntarAsync(string mensaje);

    /// <summary>
    /// Muestra un mensaje informativo de una sola acción (sin opción de cancelar/confirmar)
    /// y espera a que el usuario lo cierre. Es el mecanismo único para informar errores
    /// amigables tanto desde los comandos de los ViewModels (ej. baja lógica de una entidad
    /// de catálogo ya inactiva) como desde la red de seguridad global de excepciones no
    /// manejadas del hilo de UI (ver App.axaml.cs).
    /// </summary>
    /// <param name="mensaje">Texto del mensaje a mostrar.</param>
    Task InformarAsync(string mensaje);

    /// <summary>
    /// Pide al usuario un texto libre obligatorio (módulo Documentos, spec 2026-08-11:
    /// anular y reabrir un documento administrativo exigen motivo). No valida "no vacío" —
    /// esa validación vive en el servicio de Application (documentos.gestionar/administrar
    /// pasa el texto crudo); este método solo recolecta lo que el usuario tipeó.
    /// </summary>
    /// <param name="titulo">Título de la ventana del diálogo.</param>
    /// <param name="mensaje">Texto explicativo mostrado sobre el campo de texto.</param>
    /// <returns>El texto tipeado, o null si el usuario canceló.</returns>
    Task<string?> PedirTextoAsync(string titulo, string mensaje);
}
```

- [ ] **Step 2: Diálogo real**

```xml
<!-- src/StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="400" d:DesignHeight="220"
        x:Class="StockApp.Presentation.Views.Dialogs.PedirTextoDialog"
        Title="Ingresar motivo"
        Width="400"
        SizeToContent="Height"
        WindowStartupLocation="CenterOwner"
        CanResize="False"
        ShowInTaskbar="False">

    <Border Padding="24">
        <StackPanel Spacing="12">

            <TextBlock x:Name="MensajeText"
                       TextWrapping="Wrap"
                       FontSize="14" />

            <TextBox x:Name="TextoTextBox"
                     AcceptsReturn="True"
                     Height="80"
                     Watermark="Escriba el motivo" />

            <StackPanel Orientation="Horizontal"
                        HorizontalAlignment="Right"
                        Spacing="8"
                        Margin="0,8,0,0">

                <Button x:Name="CancelarButton"
                        Content="Cancelar"
                        Classes="secondary"
                        Width="100"
                        Click="OnCancelarClick" />

                <Button x:Name="AceptarButton"
                        Content="Aceptar"
                        Classes="primary"
                        Width="100"
                        Click="OnAceptarClick" />

            </StackPanel>

        </StackPanel>
    </Border>

</Window>
```

```csharp
// src/StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Diálogo modal que pide un texto libre obligatorio (módulo Documentos, spec 2026-08-11:
/// motivo de anulación/reapertura). "Aceptar" devuelve el texto tipeado (puede venir vacío
/// o en blanco: la validación de "no vacío" vive en el servicio, D8); "Cancelar" devuelve
/// null. Usá <see cref="Window.ShowDialog{TResult}"/> con TResult=string? para obtener el resultado.
/// </summary>
public partial class PedirTextoDialog : Window
{
    public PedirTextoDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Crea el diálogo con el título de ventana y el mensaje explicativo indicados.
    /// </summary>
    public PedirTextoDialog(string titulo, string mensaje) : this()
    {
        Title = titulo;
        MensajeText.Text = mensaje;
    }

    private void OnAceptarClick(object? sender, RoutedEventArgs e)
        => Close(TextoTextBox.Text);

    private void OnCancelarClick(object? sender, RoutedEventArgs e)
        => Close(null);
}
```

- [ ] **Step 3: Implementación real en `ConfirmacionService`**

```csharp
// src/StockApp.Presentation/Services/ConfirmacionService.cs
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Presentation.Views.Dialogs;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de IConfirmacionService.
/// Abre un diálogo modal de Avalonia sobre la ventana principal y devuelve la respuesta del usuario.
/// </summary>
public class ConfirmacionService : IConfirmacionService
{
    /// <inheritdoc />
    public Task<bool> PreguntarAsync(string mensaje)
    {
        // Si no hay aplicación Avalonia inicializada (ej: tests headless), rechazar de forma segura.
        if (AvaloniaApp.Current is null)
            return Task.FromResult(false);

        // Garantizamos ejecución en el hilo de UI (Dispatcher.UIThread).
        return Dispatcher.UIThread.InvokeAsync(() => MostrarDialogoAsync(mensaje));
    }

    private static async Task<bool> MostrarDialogoAsync(string mensaje)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        var owner = lifetime?.MainWindow;

        if (owner is null)
        {
            // No hay ventana principal disponible: rechazar de forma segura.
            return false;
        }

        var dialog = new ConfirmacionDialog(mensaje);
        var resultado = await dialog.ShowDialog<bool>(owner);
        return resultado;
    }

    /// <inheritdoc />
    public Task InformarAsync(string mensaje)
    {
        // Mismo criterio defensivo que PreguntarAsync: sin aplicación Avalonia inicializada
        // (ej: tests headless), no hay dónde mostrar el diálogo — no hacemos nada.
        if (AvaloniaApp.Current is null)
            return Task.CompletedTask;

        return Dispatcher.UIThread.InvokeAsync(() => MostrarMensajeAsync(mensaje));
    }

    private static async Task MostrarMensajeAsync(string mensaje)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        var owner = lifetime?.MainWindow;

        if (owner is null)
        {
            // No hay ventana principal disponible: no hay nada más que hacer.
            return;
        }

        var dialog = new MensajeDialog(mensaje);
        await dialog.ShowDialog(owner);
    }

    /// <inheritdoc />
    public Task<string?> PedirTextoAsync(string titulo, string mensaje)
    {
        // Mismo criterio defensivo que PreguntarAsync/InformarAsync.
        if (AvaloniaApp.Current is null)
            return Task.FromResult<string?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => MostrarPedirTextoAsync(titulo, mensaje));
    }

    private static async Task<string?> MostrarPedirTextoAsync(string titulo, string mensaje)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        var owner = lifetime?.MainWindow;

        if (owner is null)
        {
            // No hay ventana principal disponible: rechazar de forma segura (equivale a cancelar).
            return null;
        }

        var dialog = new PedirTextoDialog(titulo, mensaje);
        var resultado = await dialog.ShowDialog<string?>(owner);
        return resultado;
    }
}
```

- [ ] **Step 4: Extender `ConfirmacionServiceFake` (ripple obligatorio, sin esto no compila `StockApp.Presentation.UiTests`)**

```csharp
// tests/StockApp.Presentation.UiTests/MovimientoRegistroFakes.cs
// Reemplazar la clase ConfirmacionServiceFake completa por:

internal sealed class ConfirmacionServiceFake : IConfirmacionService
{
    public Task<bool> PreguntarAsync(string mensaje) => Task.FromResult(true);

    /// <summary>
    /// Espía (fix/integridad-referencial, Minor 10 del review adversarial): antes InformarAsync
    /// era un no-op puro -- ningún test que dijera "informa el error" podía comprobarlo de verdad,
    /// porque el fake no dejaba rastro de qué se llamó. Ver
    /// MantenimientoViewTests.Montar_HacerBackupAhoraFalla_InformaElErrorYRestauraElBoton.
    /// </summary>
    public List<string> MensajesInformados { get; } = new();

    public Task InformarAsync(string mensaje)
    {
        MensajesInformados.Add(mensaje);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Módulo Documentos (spec 2026-08-11): valor que devuelve PedirTextoAsync. Default no
    /// vacío para que los recorridos "felices" de Anular/Reabrir no necesiten configurarlo;
    /// un test que quiera simular "el usuario canceló" lo pone en null explícitamente.
    /// </summary>
    public string? TextoAPedir { get; set; } = "Motivo de prueba";

    /// <summary>Espía de qué título/mensaje se pidió, en el orden en que se pidieron.</summary>
    public List<(string Titulo, string Mensaje)> PedidosDeTexto { get; } = new();

    public Task<string?> PedirTextoAsync(string titulo, string mensaje)
    {
        PedidosDeTexto.Add((titulo, mensaje));
        return Task.FromResult(TextoAPedir);
    }
}
```

- [ ] **Step 5: Correr toda la suite de `StockApp.Presentation.UiTests` para confirmar que el ripple no rompió nada**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.UiTests`
Expected: PASS — todos los tests existentes siguen verdes (nadie llamaba a `PedirTextoAsync` todavía, así que el comportamiento nuevo es aditivo).

- [ ] **Step 6: Commit**
```bash
git add src/StockApp.Presentation/Services/IConfirmacionService.cs \
        src/StockApp.Presentation/Services/ConfirmacionService.cs \
        src/StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml \
        src/StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml.cs \
        tests/StockApp.Presentation.UiTests/MovimientoRegistroFakes.cs
git commit -m "feat(documentos): agrega PedirTextoAsync a IConfirmacionService"
```

---

## Task 19: `DocumentoFila` + `DocumentoListViewModel`

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Documentos/DocumentoListViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoListViewModelTests.cs`

**Interfaces:**
- Consumes: `IDocumentoAdministrativoService` (`ListarActivosAsync(FiltroDocumentos)`, `ListarHistorialAsync(FiltroDocumentos)`) — contrato de los bloques A-C; `ICurrentSession`, `INavigationService`, `IConfirmacionService` (existentes); `DocumentoAdministrativo.PuedeTransicionarA(EstadoDocumento)`/`.EsCerrado` (dominio, bloque A).
- Produces: `DocumentoFila` (`Id`, `Numero`, `Anio`, `TipoTexto`, `FechaEmision`, `Descripcion`, `EstadoTexto`, `RegistradoPorNombre`, `PuedeIniciar`, `PuedeVolverAPendiente`, `PuedeFinalizar`, `PuedeAnular`, `PuedeReabrir`); `DocumentoListViewModel` (`Activos`, `Historial`, filtros por solapa, `CargarAsync()`, `AbrirHistorialCommand`, `CargarHistorialAsync()`, `NuevoCommand`, `VerDetalleCommand`, `IniciarCommand`, `VolverAPendienteCommand`, `FinalizarCommand`) — consumido por Task 22 (`DocumentoListView`) y Task 23 (`ShellMainViewModel.NavDocumentos`).

Nota sobre `DocumentoFila`: además de `PuedeAnular`/`PuedeReabrir` (los dos exigidos explícitamente por el contrato, gateados por rol porque son `documentos.administrar`), se agrega `PuedeVolverAPendiente` — análogo de `TareaFila.PuedeSoltar`, sin gate de rol porque `VolverAPendienteAsync` es `documentos.gestionar` (Operador y Admin). No está en el contrato fijo pero lo exige el alcance del spec ("iniciar proceso, volver a pendiente, finalizar, anular, reabrir") y el molde de `TareaFila` que la Task pide copiar.

- [ ] **Step 1: Tests de `DocumentoListViewModel` y del gating por rol de `DocumentoFila`**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoListViewModelTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Documentos;

public class DocumentoListViewModelTests
{
    private static DocumentoAdministrativo DocumentoDe(int id, EstadoDocumento estado) => new()
    {
        Id = id, Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow.Date, Descripcion = "Bacheo calle Rivera",
        Estado = estado, RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
    };

    private static (DocumentoListViewModel Vm, Mock<IDocumentoAdministrativoService> Svc, Mock<IConfirmacionService> Confirm)
        Crear(IReadOnlyList<DocumentoAdministrativo>? activos = null,
              IReadOnlyList<DocumentoAdministrativo>? historial = null,
              RolUsuario rol = RolUsuario.Operador)
    {
        var svc = new Mock<IDocumentoAdministrativoService>();
        svc.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(activos ?? new List<DocumentoAdministrativo>());
        svc.Setup(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(historial ?? new List<DocumentoAdministrativo>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new DocumentoListViewModel(svc.Object, session.Object, nav.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    [Fact]
    public async Task CargarAsync_ListaDocumentosActivos_LosAgregaALaColeccion()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
            DocumentoDe(2, EstadoDocumento.EnProceso),
        });

        await ctx.Vm.CargarAsync();

        Assert.Equal(2, ctx.Vm.Activos.Count);
        Assert.Empty(ctx.Vm.Historial);
    }

    [Fact]
    public async Task CargarAsync_NoDisparaLaCargaDelHistorial()
    {
        // D9: la carga del historial es perezosa -- CargarAsync() inicial NO debe pedirlo.
        var ctx = Crear();

        await ctx.Vm.CargarAsync();

        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    [Fact]
    public async Task AbrirHistorialCommand_PrimeraVez_CargaElHistorialConElAnioActual()
    {
        var ctx = Crear(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(3, EstadoDocumento.Finalizado),
        });
        Assert.Equal(DateTime.UtcNow.Year, ctx.Vm.FiltroHistorialAnio);

        await ctx.Vm.AbrirHistorialCommand.ExecuteAsync(null);

        Assert.Single(ctx.Vm.Historial);
        ctx.Svc.Verify(s => s.ListarHistorialAsync(
            It.Is<FiltroDocumentos>(f => f.Anio == DateTime.UtcNow.Year)), Times.Once);
    }

    [Fact]
    public async Task AbrirHistorialCommand_SegundaVez_NoVuelveALlamarAlServicio()
    {
        var ctx = Crear();

        await ctx.Vm.AbrirHistorialCommand.ExecuteAsync(null);
        await ctx.Vm.AbrirHistorialCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Once);
    }

    [Fact]
    public async Task CargarHistorialAsync_LlamadaDirecta_SiempreVuelveAConsultar()
    {
        // A diferencia de AbrirHistorialCommand (una sola vez), CargarHistorialAsync() es el
        // método que dispara el botón "Buscar" del filtro del historial: debe poder recargar
        // cuantas veces el usuario cambie el filtro.
        var ctx = Crear();

        await ctx.Vm.CargarHistorialAsync();
        await ctx.Vm.CargarHistorialAsync();

        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CargarAsync_ConRolOperador_FilaNoPuedeAnularNiReabrir_AunqueLaTransicionSeaValida()
    {
        // Importante 5 del spec: el dominio permite Pendiente->Anulado (PuedeTransicionarA da
        // true), pero AnularAsync es documentos.administrar -- un Operador no debe ver el botón.
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        }, rol: RolUsuario.Operador);

        await ctx.Vm.CargarAsync();

        var fila = ctx.Vm.Activos[0];
        Assert.True(fila.Documento.PuedeTransicionarA(EstadoDocumento.Anulado));
        Assert.False(fila.PuedeAnular);
        Assert.False(fila.PuedeReabrir);
        Assert.True(fila.PuedeIniciar);
    }

    [Fact]
    public async Task CargarAsync_DocumentoFinalizado_FilaNoPuedeIniciar_AunqueLaTransicionSeaValida()
    {
        // C6/Hallazgo Task 8: Finalizado -> EnProceso es una transición válida en el dominio (es
        // la reapertura), así que PuedeTransicionarA(EnProceso) solo no alcanza para gatear el
        // botón "Iniciar" -- si alcanzara, un Operador con documentos.gestionar vería "Iniciar"
        // habilitado sobre un documento cerrado y comería el 409 que el servicio ya rechaza.
        var ctx = Crear(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Finalizado),
        }, rol: RolUsuario.Admin);

        await ctx.Vm.CargarAsync();
        await ctx.Vm.CargarHistorialAsync();

        var fila = ctx.Vm.Historial[0];
        Assert.True(fila.Documento.PuedeTransicionarA(EstadoDocumento.EnProceso));
        Assert.False(fila.PuedeIniciar);
    }

    [Fact]
    public async Task CargarAsync_ConRolAdmin_FilaPuedeAnular()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        }, rol: RolUsuario.Admin);

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.Activos[0].PuedeAnular);
    }

    [Fact]
    public async Task IniciarAsync_LlamaAlServicioYRecarga()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo> { DocumentoDe(1, EstadoDocumento.Pendiente) });
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Activos[0];

        await ctx.Vm.IniciarCommand.ExecuteAsync(fila);

        ctx.Svc.Verify(s => s.IniciarProcesoAsync(1), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_SesionSinPermiso_NoInformaAlUsuario()
    {
        // El manejador central del 403 (AuthTokenHandler + AccesoRevocado) ya avisa; si el
        // módulo también informara, vuelve el doble aviso corregido en el commit 093fc7c.
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.CargarAsync();

        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CargarAsync_ServidorNoDisponible_InformaAlUsuario()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ThrowsAsync(new ServidorNoDisponibleException());

        await ctx.Vm.CargarAsync();

        ctx.Confirm.Verify(c => c.InformarAsync(ServidorNoDisponibleException.MensajePorDefecto), Times.Once);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~DocumentoListViewModelTests"`
Expected: FAIL — no compila (`DocumentoListViewModel`/`DocumentoFila` no existen).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Presentation/ViewModels/Documentos/DocumentoListViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Documentos;

/// <summary>
/// Fila de solo lectura de la lista de documentos: aplana la entidad y agrega el gating de
/// acciones por transición de estado (dominio, D4) combinado con rol (spec: las acciones
/// documentos.administrar -- Anular/Reabrir -- las oculta un Operador aunque el dominio
/// permita la transición). Molde: TareaFila.PuedeCancelar.
/// </summary>
public sealed class DocumentoFila
{
    public DocumentoAdministrativo Documento { get; }
    private readonly RolUsuario _rol;

    public DocumentoFila(DocumentoAdministrativo documento, RolUsuario rol)
    {
        Documento = documento;
        _rol = rol;
    }

    public int Id => Documento.Id;
    public string Numero => Documento.Numero;
    public int Anio => Documento.Anio;
    public string TipoTexto => Documento.Tipo.ToString();
    public DateTime FechaEmision => Documento.FechaEmision;
    public string Descripcion => Documento.Descripcion;
    public string EstadoTexto => Documento.Estado.ToString();
    public string? RegistradoPorNombre => Documento.RegistradoPor?.NombreUsuario;

    // PuedeIniciar exige ADEMÁS Estado == Pendiente, no solo PuedeTransicionarA(EnProceso): en
    // la tabla del dominio (D4), Finalizado/Anulado -> EnProceso también son transiciones
    // válidas (son la reapertura), así que PuedeTransicionarA(EnProceso) da true también sobre
    // un documento cerrado. Sin este chequeo extra, el botón "Iniciar" aparecería habilitado
    // sobre un documento Finalizado/Anulado y el usuario comería el 409 que el servicio ya
    // rechaza (Task 8, guarda simétrica a la de ReabrirAsync).
    public bool PuedeIniciar          => Documento.Estado == EstadoDocumento.Pendiente && Documento.PuedeTransicionarA(EstadoDocumento.EnProceso);
    public bool PuedeVolverAPendiente => Documento.PuedeTransicionarA(EstadoDocumento.Pendiente);
    public bool PuedeFinalizar        => Documento.PuedeTransicionarA(EstadoDocumento.Finalizado);

    public bool PuedeAnular =>
        _rol == RolUsuario.Admin && Documento.PuedeTransicionarA(EstadoDocumento.Anulado);

    public bool PuedeReabrir =>
        _rol == RolUsuario.Admin && Documento.EsCerrado;
}

/// <summary>
/// Pantalla "Documentos administrativos": dos solapas, Activos (Pendiente/EnProceso) e
/// Historial (Finalizado/Anulado, D9). El historial se carga perezoso -- recién al abrir la
/// solapa, no en CargarAsync() inicial -- y exige año (el servidor lo rechaza si viene nulo,
/// D9); la UI precarga el año actual como valor inicial del filtro.
/// La vista dispara CargarAsync() vía DataContextChanged (convención del proyecto).
/// </summary>
public partial class DocumentoListViewModel : ViewModelBase
{
    private readonly IDocumentoAdministrativoService _service;
    private readonly ICurrentSession                 _session;
    private readonly INavigationService               _navigation;
    private readonly IConfirmacionService              _confirmacion;

    private bool _historialCargado;

    [ObservableProperty] private TipoDocumento? _filtroActivosTipo;
    [ObservableProperty] private string? _filtroActivosTexto;

    [ObservableProperty] private TipoDocumento? _filtroHistorialTipo;
    [ObservableProperty] private int? _filtroHistorialAnio = DateTime.UtcNow.Year;
    [ObservableProperty] private string? _filtroHistorialTexto;
    [ObservableProperty] private EstadoDocumento? _filtroHistorialEstado;

    public ObservableCollection<DocumentoFila> Activos { get; } = new();
    public ObservableCollection<DocumentoFila> Historial { get; } = new();

    public DocumentoListViewModel(
        IDocumentoAdministrativoService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    private RolUsuario RolActualODefault => _session.RolActual ?? RolUsuario.Operador;

    public async Task CargarAsync()
    {
        try
        {
            var filtro = new FiltroDocumentos(FiltroActivosTipo, null, FiltroActivosTexto, null);
            var documentos = await _service.ListarActivosAsync(filtro);
            var rol = RolActualODefault;

            Activos.Clear();
            foreach (var doc in documentos)
                Activos.Add(new DocumentoFila(doc, rol));
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    /// <summary>
    /// D9: recarga el historial SIEMPRE que se invoca (a diferencia de AbrirHistorialCommand,
    /// que solo carga la primera vez) -- es lo que dispara el botón "Buscar" del filtro propio
    /// del historial cuando el usuario cambia año/tipo/texto/estado.
    /// </summary>
    public async Task CargarHistorialAsync()
    {
        try
        {
            var filtro = new FiltroDocumentos(FiltroHistorialTipo, FiltroHistorialAnio, FiltroHistorialTexto, FiltroHistorialEstado);
            var documentos = await _service.ListarHistorialAsync(filtro);
            var rol = RolActualODefault;

            Historial.Clear();
            foreach (var doc in documentos)
                Historial.Add(new DocumentoFila(doc, rol));

            _historialCargado = true;
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    /// <summary>
    /// D9 (carga perezosa): la vista invoca este comando al seleccionar la solapa Historial.
    /// Solo consulta al servicio la primera vez -- volver a seleccionar la solapa no repite
    /// la consulta (usar CargarHistorialAsync() directamente para forzar un refresco).
    /// </summary>
    [RelayCommand]
    private async Task AbrirHistorial()
    {
        if (_historialCargado) return;
        await CargarHistorialAsync();
    }

    [RelayCommand]
    private void Nuevo() => _navigation.Navegar<DocumentoFormViewModel>(vm => vm.CargarParaCrear());

    [RelayCommand]
    private void VerDetalle(DocumentoFila fila)
        => _navigation.Navegar<DocumentoFormViewModel>(vm => _ = vm.CargarParaVerAsync(fila.Documento));

    [RelayCommand]
    private async Task Iniciar(DocumentoFila fila)
    {
        try { await _service.IniciarProcesoAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task VolverAPendiente(DocumentoFila fila)
    {
        try { await _service.VolverAPendienteAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task Finalizar(DocumentoFila fila)
    {
        try { await _service.FinalizarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    /// <summary>
    /// Único punto de traducción excepción → mensaje para todos los comandos de esta pantalla
    /// (molde: TareaListViewModel.ManejarErrorAsync). A diferencia de Tareas,
    /// UnauthorizedAccessException se atrapa en SILENCIO (spec "Manejo de errores"): el
    /// manejador central del 403 (AuthTokenHandler + AccesoRevocado, App.axaml.cs) ya muestra
    /// el aviso y refresca permisos -- informarlo también acá vuelve el doble aviso que se
    /// corrigió en el commit 093fc7c.
    /// </summary>
    private async Task ManejarErrorAsync(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return;

        var mensaje = ex switch
        {
            ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
                or ServidorNoDisponibleException => ex.Message,
            _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
        };
        await _confirmacion.InformarAsync(mensaje);
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~DocumentoListViewModelTests"`
Expected: FAIL en compilación por `DocumentoFormViewModel` (referenciado por `Nuevo`/`VerDetalle`, se crea recién en la Task 20) — correr Task 19 y Task 20 en el mismo ciclo antes de testear, o dejar un stub vacío de `DocumentoFormViewModel` con `CargarParaCrear()`/`CargarParaVerAsync(DocumentoAdministrativo)` si se ejecutan por separado. Con el stub (o con la Task 20 ya aplicada): PASS — 10 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Documentos/DocumentoListViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoListViewModelTests.cs
git commit -m "feat(documentos): agrega DocumentoListViewModel con solapas Activos/Historial"
```

---

## Task 20: `DocumentoFormViewModel`

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Documentos/DocumentoFormViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoFormViewModelTests.cs`

**Interfaces:**
- Consumes: `IDocumentoAdministrativoService` (`RegistrarAsync`, `EditarAsync`, `ObtenerPorIdAsync`, `IniciarProcesoAsync`, `VolverAPendienteAsync`, `FinalizarAsync`, `AgregarNotaAsync`, `AnularAsync(id, motivo)`, `ReabrirAsync(id, motivo)`); `IConfirmacionService.PedirTextoAsync` (Task 18); `AdjuntosDocumentoPanelViewModel.InicializarAsync(int, bool)` (Task 21 — se inyecta por constructor, mismo molde que `PagosGastoViewModel`/`GastoFormViewModel` con `AdjuntosPanelViewModel`).
- Produces: `DocumentoFormViewModel` — doble uso alta (`CargarParaCrear`, `GuardarCommand`) / detalle-edición (`CargarParaVerAsync`, `GuardarEdicionCommand` si `PuedeEditar`, hilo de `Eventos`, `AgregarNotaCommand`, `IniciarCommand`/`VolverAPendienteCommand`/`FinalizarCommand`, `AnularCommand`/`ReabrirCommand` solo Admin) — consumido por Task 19 (`Nuevo`/`VerDetalle`) y Task 22 (`DocumentoFormView`).

`Anular`/`Reabrir` piden el motivo con `PedirTextoAsync` (Task 18) y **no llaman al servicio** si el usuario cancela (`null`) o deja el texto en blanco -- la validación de "no vacío" también vive en el servicio (D8), pero no tiene sentido viajar al servidor con un motivo vacío si ya se sabe acá que va a rebotar.

- [ ] **Step 1: Tests de alta, gating y motivo obligatorio en Anular/Reabrir**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoFormViewModelTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Documentos;

public class DocumentoFormViewModelTests
{
    private static DocumentoAdministrativo DocumentoDe(int id, EstadoDocumento estado) => new()
    {
        Id = id, Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow.Date, Descripcion = "Bacheo calle Rivera",
        Estado = estado, RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
    };

    private static (DocumentoFormViewModel Vm, Mock<IDocumentoAdministrativoService> Svc, Mock<IConfirmacionService> Confirm)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var svc = new Mock<IDocumentoAdministrativoService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Motivo de prueba");
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var adjuntosPanel = new AdjuntosDocumentoPanelViewModel(
            Mock.Of<IAdjuntoDocumentoService>(), Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), confirm.Object, session.Object);

        var vm = new DocumentoFormViewModel(svc.Object, session.Object, nav.Object, confirm.Object, adjuntosPanel);
        return (vm, svc, confirm);
    }

    [Fact]
    public async Task GuardarAsync_ModoAlta_LlamaARegistrarAsyncConLosDatosCargados()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Numero = "0099";
        ctx.Vm.AnioSeleccionado = 2026;
        ctx.Vm.TipoSeleccionado = TipoDocumento.Oficio;
        ctx.Vm.FechaEmisionSeleccionada = new DateTime(2026, 8, 11);
        ctx.Vm.Descripcion = "Pedido de materiales";

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.RegistrarAsync(It.Is<DocumentoAdministrativo>(d =>
            d.Numero == "0099" && d.Anio == 2026 && d.Tipo == TipoDocumento.Oficio
            && d.Descripcion == "Pedido de materiales")), Times.Once);
    }

    [Fact]
    public async Task CargarParaVerAsync_DocumentoActivo_PuedeIniciarSegunElDominio()
    {
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        Assert.True(ctx.Vm.PuedeIniciar);
        Assert.True(ctx.Vm.PuedeEditar);
        Assert.False(ctx.Vm.PuedeVolverAPendiente);
    }

    [Fact]
    public async Task CargarParaVerAsync_DocumentoFinalizado_PuedeIniciarEsFalso_AunqueLaTransicionSeaValida()
    {
        // C6, mismo fix que DocumentoFila: Finalizado -> EnProceso es la reapertura, válida en
        // el dominio, así que PuedeTransicionarA(EnProceso) solo no alcanza para gatear el botón.
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Finalizado));

        Assert.False(ctx.Vm.PuedeIniciar);
    }

    [Fact]
    public async Task CargarParaVerAsync_RolOperador_PuedeAnularYPuedeReabrirSonFalse()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        Assert.False(ctx.Vm.PuedeAnular);
        Assert.False(ctx.Vm.PuedeReabrir);
    }

    [Fact]
    public async Task CargarParaVerAsync_DocumentoCerrado_PuedeEditarEsFalse()
    {
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Finalizado));

        Assert.False(ctx.Vm.PuedeEditar);
    }

    [Fact]
    public async Task AnularAsync_UsuarioCancelaElMotivo_NoLlamaAlServicio()
    {
        var ctx = Crear();
        ctx.Confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        await ctx.Vm.AnularCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AnularAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnularAsync_MotivoEnBlanco_NoLlamaAlServicio()
    {
        var ctx = Crear();
        ctx.Confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("   ");
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        await ctx.Vm.AnularCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AnularAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnularAsync_ConMotivo_LlamaAAnularAsyncConElMotivoTipeado()
    {
        var ctx = Crear();
        ctx.Confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("El interesado no volvió a presentarse.");
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        await ctx.Vm.AnularCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AnularAsync(1, "El interesado no volvió a presentarse."), Times.Once);
    }

    [Fact]
    public async Task ReabrirAsync_ConMotivo_LlamaAReabrirAsyncConElMotivoTipeado()
    {
        var ctx = Crear();
        ctx.Confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Se encontró documentación adicional.");
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Anulado));

        await ctx.Vm.ReabrirCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ReabrirAsync(1, "Se encontró documentación adicional."), Times.Once);
    }

    [Fact]
    public async Task AgregarNotaAsync_LlamaAlServicioYLimpiaElTexto()
    {
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));
        ctx.Vm.NuevaNotaTexto = "Falta la firma del interesado.";

        await ctx.Vm.AgregarNotaCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AgregarNotaAsync(1, "Falta la firma del interesado."), Times.Once);
        Assert.Equal(string.Empty, ctx.Vm.NuevaNotaTexto);
    }

    [Fact]
    public async Task IniciarAsync_SesionSinPermiso_NoMuestraMensajeError()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.IniciarProcesoAsync(It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException());
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        await ctx.Vm.IniciarCommand.ExecuteAsync(null);

        Assert.Null(ctx.Vm.MensajeError);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~DocumentoFormViewModelTests"`
Expected: FAIL — no compila (`DocumentoFormViewModel` no existe; requiere que Task 21 (`AdjuntosDocumentoPanelViewModel`) ya esté aplicada, o un stub con `InicializarAsync(int, bool)` que no haga nada).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Presentation/ViewModels/Documentos/DocumentoFormViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Documentos;

/// <summary>
/// Doble uso (molde: TareaFormViewModel): modo alta (EsNuevoDocumento = true) para registrar
/// un documento nuevo, y modo detalle/edición (EsNuevoDocumento = false) para ver un
/// documento existente, su hilo de eventos, editarlo mientras esté activo (D1) y ejecutar
/// las transiciones de estado. El panel de adjuntos (Task 21) se inyecta ya construido
/// (Transient, mismo ciclo de vida que este VM) y se inicializa recién en CargarParaVerAsync,
/// porque agregar adjuntos exige que el documento ya exista (D11a).
/// </summary>
public partial class DocumentoFormViewModel : ViewModelBase
{
    /// <summary>
    /// Mensaje para UnauthorizedAccessException. No se usa hoy (el catch de 403 es silencioso,
    /// spec "Manejo de errores") pero se deja documentado el motivo por si el criterio cambia.
    /// </summary>
    public const string MensajeSinPermiso =
        "La sesión expiró o no tiene permiso para realizar esta acción. Vuelva a iniciar sesión e intente de nuevo.";

    private readonly IDocumentoAdministrativoService _service;
    private readonly ICurrentSession                 _session;
    private readonly INavigationService               _navigation;
    private readonly IConfirmacionService              _confirmacion;

    private DocumentoAdministrativo? _documento;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private string _numero = string.Empty;

    [ObservableProperty] private int _anioSeleccionado = DateTime.UtcNow.Year;
    [ObservableProperty] private TipoDocumento _tipoSeleccionado = TipoDocumento.Expediente;
    [ObservableProperty] private DateTime? _fechaEmisionSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private string? _descripcion;

    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private string? _registradoPorNombre;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeEditar))]
    [NotifyPropertyChangedFor(nameof(PuedeEditarCampos))]
    [NotifyPropertyChangedFor(nameof(PuedeIniciar))]
    [NotifyPropertyChangedFor(nameof(PuedeVolverAPendiente))]
    [NotifyPropertyChangedFor(nameof(PuedeFinalizar))]
    [NotifyPropertyChangedFor(nameof(PuedeAnular))]
    [NotifyPropertyChangedFor(nameof(PuedeReabrir))]
    private bool _esNuevoDocumento = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarNotaCommand))]
    private string _nuevaNotaTexto = string.Empty;

    public ObservableCollection<EventoDocumento> Eventos { get; } = new();

    public IReadOnlyList<TipoDocumento> TiposDisponibles { get; } =
        new[] { TipoDocumento.Expediente, TipoDocumento.Oficio, TipoDocumento.Suministro };

    public AdjuntosDocumentoPanelViewModel AdjuntosPanel { get; }

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    public bool PuedeEditar          => !EsNuevoDocumento && _documento is { EsActivo: true };

    /// <summary>
    /// Controla si los 5 campos del documento (Numero/AnioSeleccionado/TipoSeleccionado/
    /// FechaEmisionSeleccionada/Descripcion) están habilitados en el XAML: en alta siempre
    /// (EsNuevoDocumento), en detalle solo si PuedeEditar (D1: documento activo). Evita
    /// duplicar el bloque de campos en el XAML para alta vs. detalle -- un único bloque con
    /// IsEnabled="{Binding PuedeEditarCampos}" cubre ambos modos.
    /// </summary>
    public bool PuedeEditarCampos => EsNuevoDocumento || PuedeEditar;

    // Mismo fix que DocumentoFila.PuedeIniciar (C6): Finalizado/Anulado -> EnProceso también es
    // válido en el dominio (es la reapertura), así que PuedeTransicionarA(EnProceso) solo no
    // alcanza -- hace falta exigir además que el documento esté Pendiente.
    public bool PuedeIniciar         => !EsNuevoDocumento && (_documento?.Estado == EstadoDocumento.Pendiente) && (_documento?.PuedeTransicionarA(EstadoDocumento.EnProceso) ?? false);
    public bool PuedeVolverAPendiente => !EsNuevoDocumento && (_documento?.PuedeTransicionarA(EstadoDocumento.Pendiente) ?? false);
    public bool PuedeFinalizar       => !EsNuevoDocumento && (_documento?.PuedeTransicionarA(EstadoDocumento.Finalizado) ?? false);
    public bool PuedeAnular          => EsAdmin && !EsNuevoDocumento && (_documento?.PuedeTransicionarA(EstadoDocumento.Anulado) ?? false);
    public bool PuedeReabrir         => EsAdmin && !EsNuevoDocumento && (_documento?.EsCerrado ?? false);

    public DocumentoFormViewModel(
        IDocumentoAdministrativoService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion,
        AdjuntosDocumentoPanelViewModel adjuntosPanel)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
        AdjuntosPanel = adjuntosPanel;
    }

    public void CargarParaCrear()
    {
        _documento = null;
        EsNuevoDocumento = true;
        Numero = string.Empty;
        AnioSeleccionado = DateTime.UtcNow.Year;
        TipoSeleccionado = TipoDocumento.Expediente;
        FechaEmisionSeleccionada = DateTime.UtcNow.Date;
        Descripcion = null;
        EstadoTexto = string.Empty;
        RegistradoPorNombre = null;
        MensajeError = null;
        Eventos.Clear();
    }

    public async Task CargarParaVerAsync(DocumentoAdministrativo documento)
    {
        _documento = documento;
        EsNuevoDocumento = false;
        CargarCamposDesdeDocumento(documento);
        MensajeError = null;

        await AdjuntosPanel.InicializarAsync(documento.Id, documento.EsActivo);
    }

    private void CargarCamposDesdeDocumento(DocumentoAdministrativo documento)
    {
        Numero = documento.Numero;
        AnioSeleccionado = documento.Anio;
        TipoSeleccionado = documento.Tipo;
        FechaEmisionSeleccionada = documento.FechaEmision;
        Descripcion = documento.Descripcion;
        EstadoTexto = documento.Estado.ToString();
        RegistradoPorNombre = documento.RegistradoPor?.NombreUsuario;

        Eventos.Clear();
        foreach (var evento in documento.Eventos.OrderBy(e => e.Fecha))
            Eventos.Add(evento);
    }

    /// <summary>
    /// Refresca el documento desde el servidor y vuelve a poblar los campos + los booleanos
    /// Puede* -- se usa después de CUALQUIER acción que mute el documento (iniciar, volver a
    /// pendiente, finalizar, anular, reabrir, editar, agregar nota) para que el hilo de
    /// eventos y el estado mostrado sean siempre los reales, no una copia local optimista.
    /// </summary>
    private async Task RecargarAsync()
    {
        if (_documento is null) return;
        _documento = await _service.ObtenerPorIdAsync(_documento.Id);
        CargarCamposDesdeDocumento(_documento);
        OnPropertyChanged(nameof(PuedeEditar));
        OnPropertyChanged(nameof(PuedeEditarCampos));
        OnPropertyChanged(nameof(PuedeIniciar));
        OnPropertyChanged(nameof(PuedeVolverAPendiente));
        OnPropertyChanged(nameof(PuedeFinalizar));
        OnPropertyChanged(nameof(PuedeAnular));
        OnPropertyChanged(nameof(PuedeReabrir));
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Numero) && !string.IsNullOrWhiteSpace(Descripcion)
        && FechaEmisionSeleccionada.HasValue;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            await _service.RegistrarAsync(new DocumentoAdministrativo
            {
                Numero = Numero,
                Anio = AnioSeleccionado,
                Tipo = TipoSeleccionado,
                FechaEmision = DateTime.SpecifyKind(FechaEmisionSeleccionada!.Value.Date, DateTimeKind.Utc),
                Descripcion = Descripcion!,
            });
            _navigation.Navegar<DocumentoListViewModel>();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarEdicionAsync()
    {
        if (_documento is null) return;
        MensajeError = null;
        try
        {
            await _service.EditarAsync(_documento.Id, new DatosEdicionDocumento(
                Numero, AnioSeleccionado, TipoSeleccionado,
                DateTime.SpecifyKind(FechaEmisionSeleccionada!.Value.Date, DateTimeKind.Utc), Descripcion!));
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task IniciarAsync()
    {
        if (_documento is null) return;
        try { await _service.IniciarProcesoAsync(_documento.Id); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task VolverAPendienteAsync()
    {
        if (_documento is null) return;
        try { await _service.VolverAPendienteAsync(_documento.Id); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task FinalizarAsync()
    {
        if (_documento is null) return;
        try { await _service.FinalizarAsync(_documento.Id); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task AnularAsync()
    {
        if (_documento is null) return;

        var motivo = await _confirmacion.PedirTextoAsync(
            "Anular documento", "Ingresá el motivo de la anulación:");
        if (string.IsNullOrWhiteSpace(motivo)) return;

        try { await _service.AnularAsync(_documento.Id, motivo); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task ReabrirAsync()
    {
        if (_documento is null) return;

        var motivo = await _confirmacion.PedirTextoAsync(
            "Reabrir documento", "Ingresá el motivo de la reapertura:");
        if (string.IsNullOrWhiteSpace(motivo)) return;

        try { await _service.ReabrirAsync(_documento.Id, motivo); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    private bool PuedeAgregarNota() => !string.IsNullOrWhiteSpace(NuevaNotaTexto);

    [RelayCommand(CanExecute = nameof(PuedeAgregarNota))]
    private async Task AgregarNotaAsync()
    {
        if (_documento is null) return;
        var texto = NuevaNotaTexto;
        try
        {
            await _service.AgregarNotaAsync(_documento.Id, texto);
            NuevaNotaTexto = string.Empty;
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private void Volver() => _navigation.Navegar<DocumentoListViewModel>();

    /// <summary>
    /// Único punto de traducción excepción → mensaje para los comandos de esta pantalla
    /// (molde: TareaFormViewModel.ResolverMensajeError). UnauthorizedAccessException se
    /// atrapa en SILENCIO -- mismo motivo que DocumentoListViewModel.ManejarErrorAsync.
    /// </summary>
    private Task ManejarErrorAsync(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return Task.CompletedTask;

        MensajeError = ex switch
        {
            ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
                or ServidorNoDisponibleException => ex.Message,
            _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
        };
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~DocumentoFormViewModelTests"`
Expected: PASS — 11 tests verdes.

- [ ] **Step 5: Correr toda la carpeta Documentos de Presentation.Tests**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~Documentos"`
Expected: PASS — 22 tests verdes (Task 19 + Task 20 juntas).

- [ ] **Step 6: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Documentos/DocumentoFormViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoFormViewModelTests.cs
git commit -m "feat(documentos): agrega DocumentoFormViewModel con alta, edicion y transiciones"
```

---

## Task 21: `AdjuntosDocumentoPanelViewModel`

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Documentos/AdjuntosDocumentoPanelViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Documentos/AdjuntosDocumentoPanelViewModelTests.cs`

**Interfaces:**
- Consumes: `IAdjuntoDocumentoService` (`AgregarAsync`, `ListarPorDocumentoAsync`, `ObtenerContenidoAsync`, `QuitarAsync` — contrato de los bloques A-C); `IServicioSeleccionArchivo.SeleccionarArchivoAsync()` (existente, reusado tal cual — D10); `IServicioAperturaArchivo.AbrirAsync(string, byte[])` (existente, reusado tal cual); `IConfirmacionService.InformarAsync`; `ICurrentSession.RolActual`.
- Produces: `AdjuntosDocumentoPanelViewModel` (`Items`, `PuedeAgregar`, `PuedeQuitar`, `InicializarAsync(int documentoId, bool documentoActivo)`, `AgregarCommand`, `VerCommand`, `QuitarCommand`) — consumido por Task 20 (`DocumentoFormViewModel.AdjuntosPanel`) y Task 22 (`AdjuntosDocumentoPanelView`).

D11(a): agregar y quitar solo si el documento está activo — por eso `InicializarAsync` recibe `documentoActivo` en vez de derivarlo, así el ViewModel no necesita reconsultar el documento para saber su propio estado. D11(b): quitar exige `documentos.administrar` (Admin), agregar no.

El DTO de contenido, `AdjuntoDocumentoContenidoDto` (`record AdjuntoDocumentoContenidoDto(string NombreArchivo, string ContentType, byte[] Contenido)`, análogo de `AdjuntoContenidoDto` de Finanzas), ya lo define la Task 12 (`IAdjuntoDocumentoService.ObtenerContenidoAsync`) — esta tarea solo lo consume, no lo redefine.

- [ ] **Step 1: Tests del panel**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Documentos/AdjuntosDocumentoPanelViewModelTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Documentos;

public class AdjuntosDocumentoPanelViewModelTests
{
    private static AdjuntoDocumentoDto AdjuntoDe(int id) =>
        new(id, 1, "factura.pdf", "application/pdf", 1024, DateTime.UtcNow);

    private static (AdjuntosDocumentoPanelViewModel Vm, Mock<IAdjuntoDocumentoService> Svc,
        Mock<IServicioSeleccionArchivo> Seleccion, Mock<IConfirmacionService> Confirm)
        Crear(RolUsuario rol = RolUsuario.Operador)
    {
        var svc = new Mock<IAdjuntoDocumentoService>();
        svc.Setup(s => s.ListarPorDocumentoAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<AdjuntoDocumentoDto>());

        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var apertura = new Mock<IServicioAperturaArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);

        var vm = new AdjuntosDocumentoPanelViewModel(svc.Object, seleccion.Object, apertura.Object, confirm.Object, session.Object);
        return (vm, svc, seleccion, confirm);
    }

    [Fact]
    public async Task InicializarAsync_CargaLosAdjuntosDelDocumento()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarPorDocumentoAsync(5))
            .ReturnsAsync(new List<AdjuntoDocumentoDto> { AdjuntoDe(1), AdjuntoDe(2) });

        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        Assert.Equal(2, ctx.Vm.Items.Count);
    }

    [Fact]
    public async Task InicializarAsync_RolOperadorDocumentoActivo_PuedeAgregarTruePuedeQuitarFalse()
    {
        var ctx = Crear(rol: RolUsuario.Operador);

        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        Assert.True(ctx.Vm.PuedeAgregar);
        Assert.False(ctx.Vm.PuedeQuitar);
    }

    [Fact]
    public async Task InicializarAsync_RolAdminDocumentoActivo_PuedeAgregarYPuedeQuitarTrue()
    {
        var ctx = Crear(rol: RolUsuario.Admin);

        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        Assert.True(ctx.Vm.PuedeAgregar);
        Assert.True(ctx.Vm.PuedeQuitar);
    }

    [Fact]
    public async Task InicializarAsync_DocumentoCerrado_PuedeAgregarYPuedeQuitarFalse_AunSiendoAdmin()
    {
        // D11(a): la regla corta en ambos sentidos sobre un documento cerrado, sin importar el rol.
        var ctx = Crear(rol: RolUsuario.Admin);

        await ctx.Vm.InicializarAsync(5, documentoActivo: false);

        Assert.False(ctx.Vm.PuedeAgregar);
        Assert.False(ctx.Vm.PuedeQuitar);
    }

    [Fact]
    public async Task AgregarAsync_UsuarioCancelaLaSeleccion_NoLlamaAlServicio()
    {
        var ctx = Crear();
        ctx.Seleccion.Setup(s => s.SeleccionarArchivoAsync())
            .ReturnsAsync(((string, byte[])?)null);
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.AgregarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AgregarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task AgregarAsync_ArchivoSeleccionado_LlamaAlServicioYRecarga()
    {
        var ctx = Crear();
        var contenido = new byte[] { 1, 2, 3 };
        ctx.Seleccion.Setup(s => s.SeleccionarArchivoAsync())
            .ReturnsAsync(("factura.pdf", contenido));
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.AgregarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AgregarAsync(5, "factura.pdf", contenido), Times.Once);
    }

    [Fact]
    public async Task QuitarAsync_LlamaAlServicioYRecarga()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.QuitarCommand.ExecuteAsync(AdjuntoDe(9));

        ctx.Svc.Verify(s => s.QuitarAsync(9), Times.Once);
    }

    [Fact]
    public async Task AgregarAsync_ErrorDeNegocio_InformaAlUsuario()
    {
        var ctx = Crear();
        ctx.Seleccion.Setup(s => s.SeleccionarArchivoAsync())
            .ReturnsAsync(("factura.pdf", new byte[] { 1 }));
        ctx.Svc.Setup(s => s.AgregarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>()))
            .ThrowsAsync(new StockApp.Domain.Exceptions.ReglaDeNegocioException("El documento está cerrado."));
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.AgregarCommand.ExecuteAsync(null);

        ctx.Confirm.Verify(c => c.InformarAsync("El documento está cerrado."), Times.Once);
    }

    [Fact]
    public async Task QuitarAsync_SesionSinPermiso_NoInformaAlUsuario()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Svc.Setup(s => s.QuitarAsync(It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException());
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.QuitarCommand.ExecuteAsync(AdjuntoDe(9));

        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~AdjuntosDocumentoPanelViewModelTests"`
Expected: FAIL — no compila (`AdjuntosDocumentoPanelViewModel` no existe).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Presentation/ViewModels/Documentos/AdjuntosDocumentoPanelViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Documentos;

/// <summary>
/// Panel de adjuntos del documento administrativo (molde: AdjuntosPanelViewModel de
/// Finanzas). D10: reusa tal cual IServicioSeleccionArchivo/IServicioAperturaArchivo. D11(a):
/// agregar y quitar solo si el documento está activo, en ambos sentidos -- por eso
/// InicializarAsync recibe el estado del documento en vez de reconsultarlo. D11(b): quitar
/// exige Admin (documentos.administrar), agregar no (documentos.gestionar).
/// </summary>
public partial class AdjuntosDocumentoPanelViewModel : ViewModelBase
{
    private readonly IAdjuntoDocumentoService  _adjuntos;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IServicioAperturaArchivo  _apertura;
    private readonly IConfirmacionService      _confirmacion;
    private readonly ICurrentSession           _session;

    private int _documentoId;

    public ObservableCollection<AdjuntoDocumentoDto> Items { get; } = new();

    [ObservableProperty] private bool _puedeAgregar;
    [ObservableProperty] private bool _puedeQuitar;

    public AdjuntosDocumentoPanelViewModel(
        IAdjuntoDocumentoService adjuntos,
        IServicioSeleccionArchivo seleccion,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion,
        ICurrentSession session)
    {
        _adjuntos     = adjuntos;
        _seleccion    = seleccion;
        _apertura     = apertura;
        _confirmacion = confirmacion;
        _session      = session;
    }

    public async Task InicializarAsync(int documentoId, bool documentoActivo)
    {
        _documentoId = documentoId;
        var esAdmin = _session.RolActual == RolUsuario.Admin;

        PuedeAgregar = documentoActivo;
        PuedeQuitar = documentoActivo && esAdmin;

        await RecargarAsync();
    }

    private async Task RecargarAsync()
    {
        try
        {
            Items.Clear();
            var lista = await _adjuntos.ListarPorDocumentoAsync(_documentoId);
            foreach (var item in lista ?? Array.Empty<AdjuntoDocumentoDto>())
                Items.Add(item);
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task AgregarAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoAsync();
        if (seleccionado is null)
            return;

        var (nombreArchivo, contenido) = seleccionado.Value;

        try
        {
            await _adjuntos.AgregarAsync(_documentoId, nombreArchivo, contenido);
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task VerAsync(AdjuntoDocumentoDto item)
    {
        try
        {
            var contenido = await _adjuntos.ObtenerContenidoAsync(item.Id);
            await _apertura.AbrirAsync(contenido.NombreArchivo, contenido.Contenido);
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task QuitarAsync(AdjuntoDocumentoDto item)
    {
        try
        {
            await _adjuntos.QuitarAsync(item.Id);
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    /// <summary>
    /// UnauthorizedAccessException se atrapa en SILENCIO -- mismo motivo que
    /// DocumentoListViewModel/DocumentoFormViewModel.ManejarErrorAsync (spec "Manejo de errores").
    /// </summary>
    private async Task ManejarErrorAsync(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return;

        var mensaje = ex switch
        {
            ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
                or ServidorNoDisponibleException => ex.Message,
            _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
        };
        await _confirmacion.InformarAsync(mensaje);
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~AdjuntosDocumentoPanelViewModelTests"`
Expected: PASS — 9 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Documentos/AdjuntosDocumentoPanelViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Documentos/AdjuntosDocumentoPanelViewModelTests.cs
git commit -m "feat(documentos): agrega AdjuntosDocumentoPanelViewModel"
```

---

## Task 22: Vistas axaml + DI

**Files:**
- Create: `src/StockApp.Presentation/Views/Documentos/DocumentoListView.axaml`
- Create: `src/StockApp.Presentation/Views/Documentos/DocumentoListView.axaml.cs`
- Create: `src/StockApp.Presentation/Views/Documentos/DocumentoFormView.axaml`
- Create: `src/StockApp.Presentation/Views/Documentos/DocumentoFormView.axaml.cs`
- Create: `src/StockApp.Presentation/Views/Documentos/AdjuntosDocumentoPanelView.axaml`
- Create: `src/StockApp.Presentation/Views/Documentos/AdjuntosDocumentoPanelView.axaml.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Create: `tests/StockApp.Presentation.UiTests/DocumentoFakes.cs`
- Test: `tests/StockApp.Presentation.UiTests/DocumentoListViewTests.cs`

**Interfaces:**
- Consumes: `DocumentoListViewModel`/`DocumentoFormViewModel`/`AdjuntosDocumentoPanelViewModel` (Tasks 19-21); `DocumentoApiClient : IDocumentoAdministrativoService`, `AdjuntoDocumentoApiClient : IAdjuntoDocumentoService` (bloque de ApiClient, contrato fijo de la orquestación, namespace `StockApp.ApiClient`).
- Produces: `DocumentoListView`, `DocumentoFormView`, `AdjuntosDocumentoPanelView` (registradas por convención del `ViewLocator`: `ViewModels.Documentos.XxxViewModel` → `Views.Documentos.XxxView`, mismo mecanismo por reflection que el resto del proyecto — no hace falta registrarlas a mano en ningún lado más que el DI de los ViewModels).

Las vistas deben enganchar `DataContextChanged` para disparar la carga inicial — bug recurrente ya documentado del proyecto (`TareaListView.axaml.cs` es la referencia exacta).

- [ ] **Step 1: `DocumentoListView`**

```xml
<!-- src/StockApp.Presentation/Views/Documentos/DocumentoListView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Documentos"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1100" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Documentos.DocumentoListView"
             x:DataType="vm:DocumentoListViewModel">

    <DockPanel Margin="24">

        <Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,16">
            <TextBlock Grid.Column="0" Text="Documentos administrativos" Classes="titulo-vista" />
            <Button Grid.Column="1" Classes="primary" Content="Nuevo documento" Command="{Binding NuevoCommand}" />
        </Grid>

        <TabControl x:Name="Solapas" SelectionChanged="OnSolapaSeleccionada">

            <TabItem Header="Activos">
                <DockPanel Margin="0,12,0,0">

                    <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*" Margin="0,0,0,12">
                        <TextBlock Grid.Column="0" Text="Buscar:" VerticalAlignment="Center" Margin="0,0,8,0" />
                        <TextBox Grid.Column="1" Text="{Binding FiltroActivosTexto}"
                                 Watermark="Número, descripción..." Width="300" HorizontalAlignment="Left" />
                    </Grid>

                    <ScrollViewer>
                        <ItemsControl ItemsSource="{Binding Activos}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="vm:DocumentoFila">
                                    <Border Classes="card" Margin="0,0,0,8">
                                        <Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto,Auto">
                                            <StackPanel Grid.Column="0" Spacing="2">
                                                <TextBlock FontWeight="SemiBold">
                                                    <TextBlock.Text>
                                                        <MultiBinding StringFormat="{}{0} {1}/{2}">
                                                            <Binding Path="TipoTexto" />
                                                            <Binding Path="Numero" />
                                                            <Binding Path="Anio" />
                                                        </MultiBinding>
                                                    </TextBlock.Text>
                                                </TextBlock>
                                                <TextBlock Text="{Binding Descripcion}" Classes="caption" Opacity="0.8" />
                                                <TextBlock Text="{Binding EstadoTexto}" Classes="caption" Opacity="0.7" />
                                            </StackPanel>
                                            <Button Grid.Column="1" Classes="ghost" Content="Ver"
                                                    Command="{Binding $parent[UserControl].((vm:DocumentoListViewModel)DataContext).VerDetalleCommand}"
                                                    CommandParameter="{Binding}" />
                                            <Button Grid.Column="2" Classes="secondary" Content="Iniciar"
                                                    IsVisible="{Binding PuedeIniciar}"
                                                    Command="{Binding $parent[UserControl].((vm:DocumentoListViewModel)DataContext).IniciarCommand}"
                                                    CommandParameter="{Binding}" />
                                            <Button Grid.Column="3" Classes="secondary" Content="Volver a pendiente"
                                                    IsVisible="{Binding PuedeVolverAPendiente}"
                                                    Command="{Binding $parent[UserControl].((vm:DocumentoListViewModel)DataContext).VolverAPendienteCommand}"
                                                    CommandParameter="{Binding}" />
                                            <Button Grid.Column="4" Classes="primary" Content="Finalizar"
                                                    IsVisible="{Binding PuedeFinalizar}"
                                                    Command="{Binding $parent[UserControl].((vm:DocumentoListViewModel)DataContext).FinalizarCommand}"
                                                    CommandParameter="{Binding}" />
                                            <Button Grid.Column="5" Classes="danger" Content="Anular"
                                                    IsVisible="{Binding PuedeAnular}"
                                                    Command="{Binding $parent[UserControl].((vm:DocumentoListViewModel)DataContext).AnularCommand}"
                                                    CommandParameter="{Binding}" />
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </DockPanel>
            </TabItem>

            <TabItem Header="Historial">
                <DockPanel Margin="0,12,0,0">

                    <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,Auto,Auto,*,Auto" Margin="0,0,0,12">
                        <TextBlock Grid.Column="0" Text="Año:" VerticalAlignment="Center" Margin="0,0,8,0" />
                        <NumericUpDown Grid.Column="1" Value="{Binding FiltroHistorialAnio}"
                                       Minimum="2000" Maximum="2100" FormatString="0" Width="120" Margin="0,0,16,0" />
                        <TextBox Grid.Column="2" Text="{Binding FiltroHistorialTexto}"
                                 Watermark="Número, descripción..." Width="260" Margin="0,0,16,0" />
                        <Button Grid.Column="4" Classes="secondary" Content="Buscar"
                                Command="{Binding CargarHistorialCommand}" />
                    </Grid>

                    <ScrollViewer>
                        <ItemsControl ItemsSource="{Binding Historial}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="vm:DocumentoFila">
                                    <Border Classes="card" Margin="0,0,0,8">
                                        <Grid ColumnDefinitions="*,Auto,Auto">
                                            <StackPanel Grid.Column="0" Spacing="2">
                                                <TextBlock FontWeight="SemiBold">
                                                    <TextBlock.Text>
                                                        <MultiBinding StringFormat="{}{0} {1}/{2}">
                                                            <Binding Path="TipoTexto" />
                                                            <Binding Path="Numero" />
                                                            <Binding Path="Anio" />
                                                        </MultiBinding>
                                                    </TextBlock.Text>
                                                </TextBlock>
                                                <TextBlock Text="{Binding Descripcion}" Classes="caption" Opacity="0.8" />
                                                <TextBlock Text="{Binding EstadoTexto}" Classes="caption" Opacity="0.7" />
                                            </StackPanel>
                                            <Button Grid.Column="1" Classes="ghost" Content="Ver"
                                                    Command="{Binding $parent[UserControl].((vm:DocumentoListViewModel)DataContext).VerDetalleCommand}"
                                                    CommandParameter="{Binding}" />
                                            <Button Grid.Column="2" Classes="secondary" Content="Reabrir"
                                                    IsVisible="{Binding PuedeReabrir}"
                                                    Command="{Binding $parent[UserControl].((vm:DocumentoListViewModel)DataContext).VerDetalleCommand}"
                                                    CommandParameter="{Binding}" />
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </DockPanel>
            </TabItem>

        </TabControl>

    </DockPanel>

</UserControl>
```

Nota: el botón "Reabrir" del Historial navega a `VerDetalleCommand` (el detalle) en vez de reabrir directo desde la fila -- reabrir exige motivo (`PedirTextoAsync`, Task 20), y ese flujo vive en `DocumentoFormViewModel.ReabrirCommand`, no en la lista.

```csharp
// src/StockApp.Presentation/Views/Documentos/DocumentoListView.axaml.cs
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Documentos;

namespace StockApp.Presentation.Views.Documentos;

public partial class DocumentoListView : UserControl
{
    public DocumentoListView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan (gotcha del repo): la carga se dispara
        // cuando la navegación asigna el DataContext.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is DocumentoListViewModel vm)
                await vm.CargarAsync();
        };
    }

    /// <summary>
    /// D9: carga perezosa del historial -- recién al seleccionar la solapa "Historial"
    /// (índice 1), nunca al cargar la lista de Activos.
    /// </summary>
    private async void OnSolapaSeleccionada(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl { SelectedIndex: 1 } && DataContext is DocumentoListViewModel vm)
            await vm.AbrirHistorialCommand.ExecuteAsync(null);
    }
}
```

- [ ] **Step 2: `DocumentoFormView`**

```xml
<!-- src/StockApp.Presentation/Views/Documentos/DocumentoFormView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Documentos"
             xmlns:dom="using:StockApp.Domain.Entities"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:adj="using:StockApp.Presentation.Views.Documentos"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="760" d:DesignHeight="800"
             x:Class="StockApp.Presentation.Views.Documentos.DocumentoFormView"
             x:DataType="vm:DocumentoFormViewModel">

    <ScrollViewer>
        <DockPanel Margin="24">

            <Border Classes="card" VerticalAlignment="Top">
                <StackPanel Spacing="12" MaxWidth="680" HorizontalAlignment="Left">

                    <TextBlock Text="Nuevo documento" Classes="titulo-vista" IsVisible="{Binding EsNuevoDocumento}" />
                    <TextBlock Text="Detalle del documento" Classes="titulo-vista" IsVisible="{Binding !EsNuevoDocumento}" />

                    <TextBlock Text="{Binding EstadoTexto, StringFormat='Estado: {0}'}" Classes="caption"
                               IsVisible="{Binding !EsNuevoDocumento}" />
                    <TextBlock Text="{Binding RegistradoPorNombre, StringFormat='Registrado por: {0}'}" Classes="caption"
                               IsVisible="{Binding RegistradoPorNombre, Converter={x:Static ObjectConverters.IsNotNull}}" />

                    <!-- Campos: editables en alta (EsNuevoDocumento) o en detalle solo si PuedeEditar
                         (D1: documento activo) -- PuedeEditarCampos unifica las dos condiciones para
                         no duplicar este bloque completo. -->
                    <StackPanel Spacing="12">
                        <TextBlock Text="Número" />
                        <TextBox Text="{Binding Numero}" IsEnabled="{Binding PuedeEditarCampos}" />

                        <TextBlock Text="Año" />
                        <NumericUpDown Value="{Binding AnioSeleccionado}" Minimum="2000" Maximum="2100"
                                       FormatString="0" IsEnabled="{Binding PuedeEditarCampos}" HorizontalAlignment="Left" Width="140" />

                        <TextBlock Text="Tipo" />
                        <ComboBox ItemsSource="{Binding TiposDisponibles}" SelectedItem="{Binding TipoSeleccionado}"
                                  IsEnabled="{Binding PuedeEditarCampos}" Width="200" HorizontalAlignment="Left" />

                        <TextBlock Text="Fecha de emisión" />
                        <CalendarDatePicker SelectedDate="{Binding FechaEmisionSeleccionada}"
                                            PlaceholderText="dd/mm/aaaa" SelectedDateFormat="Custom"
                                            CustomDateFormatString="dd/MM/yyyy" IsEnabled="{Binding PuedeEditarCampos}"
                                            beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />

                        <TextBlock Text="Descripción" />
                        <TextBox Text="{Binding Descripcion}" AcceptsReturn="True" Height="80"
                                  IsEnabled="{Binding PuedeEditarCampos}" />

                        <Button Classes="primary" Content="Guardar cambios" Command="{Binding GuardarEdicionCommand}"
                                IsVisible="{Binding PuedeEditar}" HorizontalAlignment="Left" />
                    </StackPanel>

                    <StackPanel Orientation="Horizontal" Spacing="8" IsVisible="{Binding EsNuevoDocumento}">
                        <Button Classes="primary" Content="Guardar" Command="{Binding GuardarCommand}" />
                        <Button Classes="secondary" Content="Volver" Command="{Binding VolverCommand}" />
                    </StackPanel>

                    <!-- Transiciones de estado: documento existente -->
                    <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,8,0,0" IsVisible="{Binding !EsNuevoDocumento}">
                        <Button Classes="secondary" Content="Iniciar" Command="{Binding IniciarCommand}" IsVisible="{Binding PuedeIniciar}" />
                        <Button Classes="secondary" Content="Volver a pendiente" Command="{Binding VolverAPendienteCommand}" IsVisible="{Binding PuedeVolverAPendiente}" />
                        <Button Classes="primary" Content="Finalizar" Command="{Binding FinalizarCommand}" IsVisible="{Binding PuedeFinalizar}" />
                        <Button Classes="danger" Content="Anular" Command="{Binding AnularCommand}" IsVisible="{Binding PuedeAnular}" />
                        <Button Classes="secondary" Content="Reabrir" Command="{Binding ReabrirCommand}" IsVisible="{Binding PuedeReabrir}" />
                        <Button Classes="secondary" Content="Volver" Command="{Binding VolverCommand}" />
                    </StackPanel>

                    <!-- Hilo de eventos: documento existente -->
                    <StackPanel Spacing="6" IsVisible="{Binding !EsNuevoDocumento}" Margin="0,8,0,0">
                        <TextBlock Text="Historial del trámite" Classes="seccion" />
                        <ItemsControl ItemsSource="{Binding Eventos}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="dom:EventoDocumento">
                                    <Border Classes="card" Margin="0,0,0,6" Padding="8">
                                        <StackPanel Spacing="2">
                                            <TextBlock Text="{Binding Texto}" TextWrapping="Wrap" />
                                            <TextBlock Text="{Binding Fecha, StringFormat='{}{0:dd/MM/yyyy HH:mm}'}"
                                                       Classes="caption" Opacity="0.7" />
                                        </StackPanel>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>

                        <TextBlock Text="Nueva nota" />
                        <TextBox Text="{Binding NuevaNotaTexto}" AcceptsReturn="True" Height="60"
                                  Watermark="Agregar una nota al hilo" />
                        <Button Classes="secondary" Content="Agregar nota" Command="{Binding AgregarNotaCommand}" />
                    </StackPanel>

                    <!-- Adjuntos: solo documento existente (D11a: agregar exige que ya exista) -->
                    <adj:AdjuntosDocumentoPanelView DataContext="{Binding AdjuntosPanel}"
                                                     IsVisible="{Binding !EsNuevoDocumento}" Margin="0,8,0,0" />

                    <TextBlock Text="{Binding MensajeError}"
                               Foreground="Red"
                               TextWrapping="Wrap"
                               IsVisible="{Binding MensajeError, Converter={x:Static ObjectConverters.IsNotNull}}" />

                </StackPanel>
            </Border>

        </DockPanel>
    </ScrollViewer>

</UserControl>
```

El bloque de campos (Número/Año/Tipo/Fecha/Descripción) es uno solo para los dos modos: `PuedeEditarCampos` (Task 20) resuelve `EsNuevoDocumento || PuedeEditar`, así que están habilitados en alta y en detalle-activo, y deshabilitados (solo lectura) en detalle-cerrado. El botón "Guardar cambios" (`GuardarEdicionCommand`) queda condicionado solo a `PuedeEditar` (no a `PuedeEditarCampos`): en modo alta el guardado lo hace el botón "Guardar" (`GuardarCommand`) de más abajo, no este.

```csharp
// src/StockApp.Presentation/Views/Documentos/DocumentoFormView.axaml.cs
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Documentos;

public partial class DocumentoFormView : UserControl
{
    public DocumentoFormView()
    {
        InitializeComponent();
    }
}
```

(Sin wiring de `DataContextChanged`: igual que `TareaFormView.axaml.cs`, `CargarParaCrear()`/`CargarParaVerAsync(...)` los invoca la navegación desde `DocumentoListViewModel.Nuevo()`/`VerDetalle(...)`, no la vista.)

- [ ] **Step 3: `AdjuntosDocumentoPanelView`**

```xml
<!-- src/StockApp.Presentation/Views/Documentos/AdjuntosDocumentoPanelView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Documentos"
             xmlns:dto="using:StockApp.Application.Documentos"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Documentos.AdjuntosDocumentoPanelView"
             x:DataType="vm:AdjuntosDocumentoPanelViewModel">

    <DockPanel>

        <Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,8">
            <TextBlock Grid.Column="0"
                       Text="Adjuntos"
                       Classes="titulo-vista" />
            <Button Grid.Column="1"
                    Classes="secondary"
                    Content="Agregar"
                    Command="{Binding AgregarCommand}"
                    IsEnabled="{Binding PuedeAgregar}" />
        </Grid>

        <ItemsControl ItemsSource="{Binding Items}">
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="dto:AdjuntoDocumentoDto">
                    <Border Classes="card" Margin="0,0,0,8">
                        <Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto">
                            <TextBlock Grid.Column="0"
                                       Text="{Binding NombreArchivo}"
                                       VerticalAlignment="Center" />
                            <TextBlock Grid.Column="1"
                                       Text="{Binding TamanoBytes, StringFormat={}{0:N0} B}"
                                       Margin="8,0"
                                       VerticalAlignment="Center" />
                            <TextBlock Grid.Column="2"
                                       Text="{Binding FechaAltaUtc, Converter={x:Static conv:FechaUtcALocalConverter.Instance}, StringFormat='dd/MM/yyyy'}"
                                       Margin="8,0"
                                       VerticalAlignment="Center" />
                            <Button Grid.Column="3"
                                    Classes="secondary"
                                    Content="Ver"
                                    Margin="8,0,0,0"
                                    Command="{Binding $parent[UserControl].((vm:AdjuntosDocumentoPanelViewModel)DataContext).VerCommand}"
                                    CommandParameter="{Binding}" />
                            <Button Grid.Column="4"
                                    Classes="secondary"
                                    Content="Quitar"
                                    Margin="8,0,0,0"
                                    Command="{Binding $parent[UserControl].((vm:AdjuntosDocumentoPanelViewModel)DataContext).QuitarCommand}"
                                    CommandParameter="{Binding}"
                                    IsEnabled="{Binding $parent[UserControl].((vm:AdjuntosDocumentoPanelViewModel)DataContext).PuedeQuitar}" />
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

    </DockPanel>

</UserControl>
```

```csharp
// src/StockApp.Presentation/Views/Documentos/AdjuntosDocumentoPanelView.axaml.cs
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Documentos;

public partial class AdjuntosDocumentoPanelView : UserControl
{
    public AdjuntosDocumentoPanelView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 4: DI en `App.axaml.cs`**

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar los usings junto al resto:

using StockApp.Application.Documentos;
using StockApp.Presentation.ViewModels.Documentos;
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO de "── Módulo Tareas (independiente de Finanzas, spec 2026-08-01) ──"
// (mismo bloque donde vive "services.AddTransient<ITareaService, TareaApiClient>();"):

        // ── Módulo Documentos administrativos (spec 2026-08-11) ────────────────
        services.AddTransient<IDocumentoAdministrativoService, DocumentoApiClient>();
        services.AddTransient<IAdjuntoDocumentoService, AdjuntoDocumentoApiClient>();
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO de "── Módulo Tareas (spec 2026-08-01) ──"
// (mismo bloque donde viven TareaListViewModel/TareaFormViewModel):

        // ── Módulo Documentos administrativos (spec 2026-08-11) ────────────────
        services.AddTransient<AdjuntosDocumentoPanelViewModel>();
        services.AddTransient<DocumentoListViewModel>();
        services.AddTransient<DocumentoFormViewModel>();
```

- [ ] **Step 5: Fakes de UiTests**

```csharp
// tests/StockApp.Presentation.UiTests/DocumentoFakes.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fake mínimo de IDocumentoAdministrativoService (mismo criterio que TareaServiceFake: este
/// proyecto no referencia Moq). Cuenta las llamadas a ListarHistorialAsync para verificar la
/// carga perezosa del historial (D9) contra la vista real.
/// </summary>
internal sealed class DocumentoServiceFake : IDocumentoAdministrativoService
{
    private readonly List<DocumentoAdministrativo> _activos;
    private readonly List<DocumentoAdministrativo> _historial;

    public DocumentoServiceFake(
        List<DocumentoAdministrativo>? activos = null, List<DocumentoAdministrativo>? historial = null)
    {
        _activos = activos ?? new List<DocumentoAdministrativo>();
        _historial = historial ?? new List<DocumentoAdministrativo>();
    }

    public int LlamadasListarHistorial { get; private set; }

    public Task<int> RegistrarAsync(DocumentoAdministrativo documento)
    {
        documento.Id = _activos.Count + _historial.Count + 1;
        _activos.Add(documento);
        return Task.FromResult(documento.Id);
    }

    public Task EditarAsync(int id, DatosEdicionDocumento datos) => Task.CompletedTask;

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro) =>
        Task.FromResult<IReadOnlyList<DocumentoAdministrativo>>(_activos.ToList());

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarHistorialAsync(FiltroDocumentos filtro)
    {
        LlamadasListarHistorial++;
        return Task.FromResult<IReadOnlyList<DocumentoAdministrativo>>(_historial.ToList());
    }

    public Task<DocumentoAdministrativo> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_activos.Concat(_historial).First(d => d.Id == id));

    public Task IniciarProcesoAsync(int id)
    {
        _activos.First(d => d.Id == id).CambiarEstado(EstadoDocumento.EnProceso);
        return Task.CompletedTask;
    }

    public Task VolverAPendienteAsync(int id)
    {
        _activos.First(d => d.Id == id).CambiarEstado(EstadoDocumento.Pendiente);
        return Task.CompletedTask;
    }

    public Task FinalizarAsync(int id)
    {
        _activos.First(d => d.Id == id).CambiarEstado(EstadoDocumento.Finalizado);
        return Task.CompletedTask;
    }

    public Task AgregarNotaAsync(int id, string texto) => Task.CompletedTask;

    public Task AnularAsync(int id, string motivo)
    {
        _activos.First(d => d.Id == id).CambiarEstado(EstadoDocumento.Anulado);
        return Task.CompletedTask;
    }

    public Task ReabrirAsync(int id, string motivo)
    {
        _historial.First(d => d.Id == id).CambiarEstado(EstadoDocumento.EnProceso);
        return Task.CompletedTask;
    }
}

/// <summary>Fake mínimo de IAdjuntoDocumentoService: sin adjuntos por defecto, no se ejercita
/// en los recorridos de DocumentoListViewTests.</summary>
internal sealed class AdjuntoDocumentoServiceFake : IAdjuntoDocumentoService
{
    public Task<AdjuntoDocumentoDto> AgregarAsync(int documentoId, string nombreArchivo, byte[] contenido) =>
        throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<IReadOnlyList<AdjuntoDocumentoDto>> ListarPorDocumentoAsync(int documentoId) =>
        Task.FromResult<IReadOnlyList<AdjuntoDocumentoDto>>(Array.Empty<AdjuntoDocumentoDto>());

    public Task<AdjuntoDocumentoContenidoDto> ObtenerContenidoAsync(int adjuntoId) =>
        throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task QuitarAsync(int adjuntoId) =>
        throw new NotSupportedException("No usado en este banco de pruebas.");
}
```

- [ ] **Step 6: Test de carga por `DataContextChanged` y de la solapa disparando la carga perezosa**

```csharp
// tests/StockApp.Presentation.UiTests/DocumentoListViewTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Verificación de DocumentoListView contra el árbol visual real (molde: TareaListViewTests).
/// Cubre lo que el spec asigna explícitamente a UiTests: carga por DataContextChanged y que
/// el cambio de solapa dispare la carga perezosa del historial (D9).
/// </summary>
public class DocumentoListViewTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:docs="clr-namespace:StockApp.Presentation.Views.Documentos;assembly=StockApp.Presentation"
                Width="1100" Height="800">
            <docs:DocumentoListView />
        </Window>
        """;

    private static DocumentoAdministrativo DocumentoDe(int id, string numero, EstadoDocumento estado) => new()
    {
        Id = id, Numero = numero, Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow.Date, Descripcion = $"Descripción {numero}",
        Estado = estado, RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
    };

    private static (Window Window, DocumentoListViewModel Vm, DocumentoServiceFake Servicio) Montar(
        List<DocumentoAdministrativo>? activos = null, List<DocumentoAdministrativo>? historial = null,
        RolUsuario rol = RolUsuario.Admin)
    {
        var servicio = new DocumentoServiceFake(activos, historial);
        var vm = new DocumentoListViewModel(
            servicio, new TareaSessionFake(rol), new NavigationRecorderDocumentosFake(), new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm, servicio);
    }

    [AvaloniaFact]
    public void Montar_ConDocumentosActivos_LosCargaPorDataContextChanged()
    {
        var (window, vm, _) = Montar(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, "0087", EstadoDocumento.Pendiente),
        });

        Assert.Single(vm.Activos);
        var textos = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Descripción 0087", textos);
    }

    [AvaloniaFact]
    public void Montar_SolapaActivosSeleccionadaPorDefecto_NoCargaElHistorial()
    {
        var (_, vm, servicio) = Montar(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(9, "0001", EstadoDocumento.Finalizado),
        });

        Assert.Empty(vm.Historial);
        Assert.Equal(0, servicio.LlamadasListarHistorial);
    }

    [AvaloniaFact]
    public void ClickReal_EnLaSolapaHistorial_DisparaLaCargaPerezosa()
    {
        var (window, vm, servicio) = Montar(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(9, "0001", EstadoDocumento.Finalizado),
        });

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Historial);
        Assert.Equal(1, servicio.LlamadasListarHistorial);

        // Volver a Activos y de nuevo a Historial no debe repetir la consulta (carga perezosa
        // = una sola vez, no "cada vez que se selecciona").
        tabControl.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        tabControl.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, servicio.LlamadasListarHistorial);
    }
}

/// <summary>Análogo de NavigationRecorderFake (TareaFakes.cs) para el módulo Documentos --
/// separado porque graba navegación hacia DocumentoFormViewModel, no TareaFormViewModel.</summary>
internal sealed class NavigationRecorderDocumentosFake : INavigationService
{
    public StockApp.Presentation.ViewModels.ViewModelBase? Actual => null;
    public event Action? Cambiado { add { } remove { } }

    public Type? UltimoTipoNavegado { get; private set; }

    public void Navegar<TVm>() where TVm : StockApp.Presentation.ViewModels.ViewModelBase
        => UltimoTipoNavegado = typeof(TVm);

    public void Navegar<TVm>(Action<TVm> inicializar) where TVm : StockApp.Presentation.ViewModels.ViewModelBase
        => UltimoTipoNavegado = typeof(TVm);
}
```

- [ ] **Step 7: Correr el test y verificar que pasa**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~DocumentoListViewTests"`
Expected: PASS — 3 tests verdes.

- [ ] **Step 8: Commit**
```bash
git add src/StockApp.Presentation/Views/Documentos/ \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.UiTests/DocumentoFakes.cs \
        tests/StockApp.Presentation.UiTests/DocumentoListViewTests.cs
git commit -m "feat(documentos): agrega vistas, DI y carga perezosa del historial"
```

---

## Task 23: Menú lateral + cierre

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml`

**Interfaces:**
- Consumes: `DocumentoListViewModel` (Task 19); `Permisos.GestionarDocumentos` (contrato fijo, bloques A-C).
- Produces: `ShellMainViewModel.PuedeGestionarDocumentos` (bool), `NavDocumentosCommand`.

- [ ] **Step 1: `PuedeGestionarDocumentos` + `NavDocumentos`**

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Agregar el using junto al resto de ViewModels.*:

using StockApp.Presentation.ViewModels.Documentos;
```

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Agregar DEBAJO de la propiedad PuedeGestionarTareas:

    public bool PuedeGestionarDocumentos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarDocumentos);
```

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// En RefrescarPermisosAsync, agregar DEBAJO de OnPropertyChanged(nameof(PuedeGestionarTareas)):

        OnPropertyChanged(nameof(PuedeGestionarDocumentos));
```

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Agregar DEBAJO del comando NavTareas:

    // ── Documentos administrativos (spec 2026-08-11): Admin y Operador con permiso ───────────

    [RelayCommand]
    private void NavDocumentos()
    {
        SeccionActiva = "Documentos";
        _navigation.Navegar<DocumentoListViewModel>();
    }
```

- [ ] **Step 2: Ítem en el menú lateral**

```xml
<!-- src/StockApp.Presentation/Views/ShellMainView.axaml -->
<!-- Agregar DEBAJO del bloque completo de "Tareas" (después del </Button> que cierra
     NavTareasCommand, antes del comentario "Finanzas: gateado por VerFinanzas ..."): -->

                <!-- Documentos administrativos: gateado por GestionarDocumentos (spec 2026-08-11) -->
                <TextBlock Text="Documentos"
                           Classes="caption"
                           Foreground="{DynamicResource SidebarTextoBrush}"
                           FontWeight="SemiBold"
                           Margin="8,8,0,4"
                           IsVisible="{Binding PuedeGestionarDocumentos}"
                           Opacity="0.6" />

                <Button Command="{Binding NavDocumentosCommand}"
                        Classes="ghost"
                        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Documentos}"
                        HorizontalAlignment="Stretch"
                        IsVisible="{Binding PuedeGestionarDocumentos}">
                    <Grid ColumnDefinitions="Auto,*">
                        <i:Icon Grid.Column="0" Value="mdi-file-cabinet" Foreground="{DynamicResource SidebarTextoBrush}" />
                        <TextBlock Grid.Column="1" Text="Documentos administrativos" VerticalAlignment="Center"
                                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
                    </Grid>
                </Button>
```

- [ ] **Step 3: Correr toda la suite de `StockApp.Presentation.Tests` (el shell tiene tests propios que pueden verificar el nuevo binding por reflexión — `ReflexionVistaViewModelTests.cs`)**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests`
Expected: PASS — sin regresiones.

Run: `timeout 180 dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~ReflexionVistaViewModelTests"`
Expected: PASS — si este test recorre `ShellMainViewModel` por reflexión buscando que cada `Nav*Command` tenga su View resoluble, confirma que `DocumentoListView`/`DocumentoFormView` están bien resueltas por el `ViewLocator`.

- [ ] **Step 4: Commit del menú**
```bash
git add src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs \
        src/StockApp.Presentation/Views/ShellMainView.axaml
git commit -m "feat(documentos): agrega Documentos administrativos al menu lateral"
```

- [ ] **Step 5: Suite completa de los 8 proyectos, SECUENCIAL (no en paralelo — Postgres compartido vía Testcontainers/fixture entre `Api.Tests`/`Infrastructure.Tests`, y Presentation/UiTests son lentos y van siempre en foreground)**

```bash
timeout 120 dotnet test tests/StockApp.Domain.Tests
timeout 120 dotnet test tests/StockApp.Application.Tests
timeout 300 dotnet test tests/StockApp.Infrastructure.Tests
timeout 300 dotnet test tests/StockApp.Api.Tests
timeout 120 dotnet test tests/StockApp.ApiClient.Tests
timeout 120 dotnet test tests/StockApp.Licencias.Cli.Tests
timeout 180 dotnet test tests/StockApp.Presentation.Tests
timeout 180 dotnet test tests/StockApp.Presentation.UiTests
```
Expected: PASS en los 8 proyectos, sin regresiones sobre la línea base previa al módulo Documentos.

- [ ] **Step 6: Checklist de verificación orgánica con la app real**

No se da por cerrado el módulo solo con tests verdes (convención del proyecto: "usuario reporta = ya probó" corre en ambos sentidos — antes de decir "listo" hay que haberlo tocado). Con la API y el desktop reales corriendo (Postgres del proyecto levantado, contenedor `stockapp-pg`):

- [ ] Login como Admin. El ítem "Documentos administrativos" aparece en el menú lateral, sección "Documentos".
- [ ] Registrar un expediente nuevo: número, año, tipo "Expediente", fecha de emisión, descripción. Guardar. Aparece en la solapa Activos con estado "Pendiente".
- [ ] Abrir el detalle, click en "Iniciar". El estado pasa a "EnProceso" y aparece un evento automático en el hilo.
- [ ] Adjuntar un PDF (< 10 MB) desde el panel de adjuntos del detalle. Aparece en la lista con nombre y tamaño; "Ver" lo abre con el visor del sistema operativo.
- [ ] Click en "Anular". El diálogo pide el motivo; cancelar sin escribir nada no debe anular (queda "EnProceso"). Repetir con un motivo real: pasa a "Anulado", `FechaCierre` queda seteada (verificable indirectamente porque el documento se va de Activos a Historial), y el motivo aparece como texto del evento automático en el hilo.
- [ ] Abrir la solapa Historial: filtrar por el año actual (precargado) y confirmar que el expediente anulado aparece.
- [ ] Con el documento anulado abierto, click en "Reabrir": pide motivo, reabre a "EnProceso".
- [ ] Intentar quitar un adjunto logueado como Operador (crear un usuario Operador con `documentos.gestionar` tildado desde el panel de permisos): el botón "Quitar" no debe estar visible; "Anular"/"Reabrir" tampoco.
- [ ] Desde el panel de permisos (Admin), destildar `documentos.gestionar` para ese Operador y volver a loguearlo: el ítem "Documentos administrativos" desaparece del menú lateral.
- [ ] Con ese mismo Operador sin el permiso, intentar navegar directo (si quedó una referencia en otra pantalla) y confirmar que un 403 dispara el aviso central único (sin doble diálogo) y refresca el menú.

---

## Índice de tareas

1. [Task 1: Enums `TipoDocumento`/`EstadoDocumento` + entidad `DocumentoAdministrativo` con máquina de estados](#task-1-enums-tipodocumentoestadodocumento-entidad-documentoadministrativo-con-máquina-de-estados)
2. [Task 2: Entidad `EventoDocumento` completa + `AgregarEvento` en `DocumentoAdministrativo`](#task-2-entidad-eventodocumento-completa-agregarevento-en-documentoadministrativo)
3. [Task 3: Entidades `AdjuntoDocumento` + `AdjuntoDocumentoContenido`](#task-3-entidades-adjuntodocumento-adjuntodocumentocontenido)
4. [Task 4: Configuración en `AppDbContext` + migración `AgregaDocumentosAdministrativos`](#task-4-configuración-en-appdbcontext-migración-agregadocumentosadministrativos)
5. [Task 5: `IDocumentoAdministrativoRepository` + `IAdjuntoDocumentoRepository` con implementaciones EF](#task-5-idocumentoadministrativorepository-iadjuntodocumentorepository-con-implementaciones-ef)
6. [Task 6: Permisos del módulo y valores de auditoría](#task-6-permisos-del-módulo-y-valores-de-auditoría)
7. [Task 7: Servicio — alta y listados, con permisos](#task-7-servicio-alta-y-listados-con-permisos)
8. [Task 8: Transiciones de gestión — iniciar proceso, volver a pendiente, finalizar](#task-8-transiciones-de-gestión-iniciar-proceso-volver-a-pendiente-finalizar)
9. [Task 9: Notas manuales](#task-9-notas-manuales)
10. [Task 10: Anular y reabrir (solo Admin, con motivo)](#task-10-anular-y-reabrir-solo-admin-con-motivo)
11. [Task 11: Edición de documentos activos](#task-11-edición-de-documentos-activos)
12. [Task 12: Servicio de adjuntos](#task-12-servicio-de-adjuntos)
13. [Task 13: Api — `DocumentosEndpoints.cs` (lectura + alta/edición)](#task-13-api-documentosendpointscs-lectura-altaedición)
14. [Task 14: Api — `DocumentosEndpoints.cs` (transiciones de estado)](#task-14-api-documentosendpointscs-transiciones-de-estado)
15. [Task 15: Api — `DocumentosEndpoints.cs` (adjuntos, multipart)](#task-15-api-documentosendpointscs-adjuntos-multipart)
16. [Task 16: ApiClient — `DocumentoApiClient`](#task-16-apiclient-documentoapiclient)
17. [Task 17: ApiClient — `AdjuntoDocumentoApiClient`](#task-17-apiclient-adjuntodocumentoapiclient)
18. [Task 18: `PedirTextoAsync` en `IConfirmacionService`](#task-18-pedirtextoasync-en-iconfirmacionservice)
19. [Task 19: `DocumentoFila` + `DocumentoListViewModel`](#task-19-documentofila-documentolistviewmodel)
20. [Task 20: `DocumentoFormViewModel`](#task-20-documentoformviewmodel)
21. [Task 21: `AdjuntosDocumentoPanelViewModel`](#task-21-adjuntosdocumentopanelviewmodel)
22. [Task 22: Vistas axaml + DI](#task-22-vistas-axaml-di)
23. [Task 23: Menú lateral + cierre](#task-23-menú-lateral-cierre)
