# Proyectos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nueva entidad Proyecto (clasificadores, presupuesto opcional, gastos imputados), y la Tarea vuelve a la forma de agosto con proyecto y expediente opcionales.

**Architecture:** El corte vertical es el mismo de Tareas: Domain → Application (servicio + interfaz compartida) → Infrastructure (EF + migración) → Api (minimal API + DTOs públicos) → ApiClient (implementa la interfaz con records `*Wire`/`*Body` internos) → Presentation (Avalonia UI v2). Hay dos migraciones: una aditiva (`AgregaProyectos`) y una destructiva (`QuitaClasificadoresDeTarea`), cada una en su propio commit.

**Tech Stack:** .NET, EF Core + PostgreSQL (Npgsql), ASP.NET Core minimal API + JWT, Avalonia 12 + CommunityToolkit.Mvvm, xUnit, MigraDoc (PDF).

**Spec:** `docs/superpowers/specs/2026-10-30-proyectos-design.md`. Leelo entero antes de cada task.

## Global Constraints

- Código, identificadores y textos de UI en español, igual que el resto del repo. Los mensajes de commit siguen Conventional Commits.
- Formato es-UY en montos (`FormatoEsUy`). Las grillas usan `DataGridColumnasAutoBehavior`; los guardados, `ComandoDeGuardado`.
- Las Views de Avalonia no se auto-inicializan: la carga se engancha en `DataContextChanged` (nunca `Loaded`/`Attached`; lo custodia `CargaProtegidaSinPermisoGuardianTests`).
- `AccionAuditada` es append-only con valores explícitos. El último valor existente es `ReclasificacionTarea = 72`.
- Las versiones de paquetes van en `Directory.Packages.props`; no hacen falta paquetes nuevos.
- Los comentarios de `.axaml` y `.csproj` no pueden contener `--`.
- Para compilar se usa la `.sln` o un build por proyecto, nunca `dotnet build` con varios csproj.
- Si una task toca algo bajo `src/StockApp.Presentation/Views` o un `.axaml`, se compila la `.sln` antes de commitear.
- Un gate de permisos en la UI lleva un test en `Presentation.UiTests` con la View real y un usuario Operador de permisos mixtos, nunca Admin.
- Los guardianes que enumeran vistas, VMs, endpoints y permisos se actualizan en la misma task que agrega la pieza:
  - `GuardianSistemaV2Tests.VistasV2`;
  - `ReflexionVistaViewModelTests`: un `[Fact]` por VM nuevo;
  - `PermisosEndpointGuardTests`: tabla de rutas;
  - `PermisosTests.Todos_*`;
  - `CatalogoPermisosPanelTests`;
  - `ShellMainSeccionActivaTests`;
  - `ExportarPdfBotonesGuardianTests`;
  - `TextoAccionAuditadaTests`.
- Los textos exactos de los errores son los de este plan; los tests los comparan literalmente.

## Review Focus

1. **Gasto ya imputado a un proyecto que después se terminó.** Al editarlo, el combo tiene que mostrar ese proyecto (fallback como con los maestros inactivos) y guardar sin error si no se cambió. Test en T7 (servicio) y T11 (VM).
2. **Operador con `proyectos.ver` sin `finanzas.ver`.** La API no devuelve la lista de gastos (`Gastos == null`) y la ficha oculta la sección, pero muestra los totales. Test en T4 (servicio), T5 (endpoint) y T10 (UiTest con la View real).
3. **Gasto anulado.** No suma al total gastado del proyecto. Test en T4.
4. **Presupuesto en 0 o vacío.** Con `null` no se muestran presupuesto, disponible ni sobregiro. Con `0` sí se muestran, y todo lo gastado es sobregiro. Test en T4 (DTO) y T10 (VM).
5. **Tarea existente con expediente al aplicar la migración destructiva.** Conserva `DocumentoAdministrativoId`, notas, estado, tomada/cerrada y fechas. Test en T9 (migración).

---

### Task 1: Dominio de Proyecto

**Files:**
- Create: `src/StockApp.Domain/Entities/Proyecto.cs`
- Create: `src/StockApp.Domain/Entities/NotaProyecto.cs`
- Create: `src/StockApp.Domain/Enums/EstadoProyecto.cs`
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs` (append)
- Modify: `src/StockApp.Domain/Entities/Tarea.cs` (agregar `ProyectoId`/`Proyecto`; los clasificadores se quitan recién en T9)
- Modify: `src/StockApp.Domain/Entities/Gasto.cs` (agregar `ProyectoId`/`Proyecto`)
- Modify: `src/StockApp.Presentation/ViewModels/Reportes/TextoAccionAuditada.cs`
- Test: `tests/StockApp.Domain.Tests/Entities/ProyectoTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Reportes/TextoAccionAuditadaTests.cs`

**Interfaces:**
- Produces:
  - `enum EstadoProyecto { Pendiente = 0, EnCurso = 1, Terminado = 2, Cancelado = 3 }`
  - `class Proyecto`:
    - `Id`, `Titulo`, `Descripcion?`, `Estado` (default `Pendiente`);
    - `Prioridad` de tipo `PrioridadTarea` (se reusa el enum; default `Media`), `FechaLimite?`;
    - `Presupuesto` de tipo `decimal?`;
    - `CreadoPorUsuarioId`, `FechaCreacion`, `FechaFin?`;
    - `ZonaId?`/`Zona`, `DimensionTematicaId?`/`DimensionTematica`, `OrganismoResponsableId?`/`OrganismoResponsable`, `OrigenFinanciamientoId?`/`OrigenFinanciamiento`;
    - `List<NotaProyecto> Notas`;
    - `void CambiarEstado(EstadoProyecto destino, DateTime ahoraUtc)`, `bool PuedeTransicionarA(EstadoProyecto)`, `bool EsCerrado`.
  - `class NotaProyecto`: `Id`, `ProyectoId`, `UsuarioId`, `Usuario?`, `Fecha`, `Texto`, `EsAutomatica`. Mismo shape que `NotaTarea`.
  - `Tarea.ProyectoId` (`int?`) + `Tarea.Proyecto` (`Proyecto?`); `Gasto.ProyectoId` (`int?`) + `Gasto.Proyecto` (`Proyecto?`).
  - Valores nuevos de `AccionAuditada`: `AltaProyecto = 73`, `ModificacionProyecto = 74`, `CambioEstadoProyecto = 75`, `AltaNotaProyecto = 76`.

- [ ] **Step 1: Escribir los tests que fallan** en `ProyectoTests`:
  - `CambiarEstado_TransicionesValidas_Aplican`. Theory con exactamente estos pares válidos: Pendiente→EnCurso, Pendiente→Cancelado, EnCurso→Pendiente, EnCurso→Terminado, EnCurso→Cancelado, Terminado→EnCurso, Cancelado→EnCurso.
  - `CambiarEstado_TransicionInvalida_LanzaReglaDeNegocio`. Pendiente→Terminado, Terminado→Cancelado y la identidad Pendiente→Pendiente lanzan `ReglaDeNegocioException` con el mensaje `"No se puede pasar el proyecto de '{origen}' a '{destino}'."`.
  - `CambiarEstado_ATerminadoOCancelado_SellaFechaFin` (`FechaFin == ahoraUtc`).
  - `CambiarEstado_Reabrir_LimpiaFechaFin` (Terminado→EnCurso deja `FechaFin == null`).
  - `EsCerrado_SoloTerminadoYCancelado`.
- [ ] **Step 2: Correr y ver el rojo.** `dotnet test tests/StockApp.Domain.Tests --filter ProyectoTests`. Esperado: no compila o FAIL.
- [ ] **Step 3: Implementar.** `Proyecto` usa una tabla `TransicionesValidas` privada, igual que `Tarea`, con los pares de arriba. `EsCerrado => Estado is Terminado or Cancelado`. `CambiarEstado` sella `FechaFin = ahoraUtc` al entrar a un estado cerrado y lo pone en `null` al salir. Agregar los cuatro valores de `AccionAuditada` y sus textos en `TextoAccionAuditada`: "Alta de proyecto", "Modificación de proyecto", "Cambio de estado de proyecto", "Nota en proyecto". Agregar las FKs nullable en `Tarea` y `Gasto`.
- [ ] **Step 4: Correr Domain.Tests y TextoAccionAuditadaTests.** Esperado: PASS.
- [ ] **Step 5: Commit** `feat(proyectos): entidad Proyecto con su máquina de estados`.

### Task 2: Permisos de Proyectos

**Files:**
- Modify: `src/StockApp.Application/Authorization/Permisos.cs` (constantes + `Todos`)
- Modify: `src/StockApp.Application/Authorization/AuthorizationService.cs` (`PermisosInicialesOperador`)
- Modify: `src/StockApp.Application/Authorization/PermisoDependencias.cs` (`Requisitos`)
- Modify: `src/StockApp.Presentation/ViewModels/Administracion/CatalogoPermisosPanel.cs`
- Test: `tests/StockApp.Application.Tests/Authorization/PermisosTests.cs`, `PermisoDependenciasTests.cs`, `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs`, `tests/StockApp.Presentation.Tests/ViewModels/Administracion/CatalogoPermisosPanelTests.cs`

**Interfaces:**
- Produces: `Permisos.VerProyectos = "proyectos.ver"` y `Permisos.GestionarProyectos = "proyectos.gestionar"`. Los dos son configurables (NO van a `PermisosEstructuralesAdmin`). `Requisitos[GestionarProyectos] = VerProyectos`.

- [ ] **Step 1: Tests que fallan.**
  - Renombrar `Todos_ContieneLasDieciseisConstantesExactas` a `Todos_ContieneLasDieciochoConstantesExactas`, con las dos nuevas.
  - `PermisosInicialesOperador_IncluyeVerYGestionarProyectos`.
  - `Requisitos_GestionarProyectos_RequiereVerProyectos`.
  - El catálogo del panel incluye `new(Permisos.VerProyectos, "Proyectos", "Ver proyectos")` y `new(Permisos.GestionarProyectos, "Proyectos", "Gestionar proyectos")`.
- [ ] **Step 2: Correr y ver el rojo.** `dotnet test tests/StockApp.Application.Tests --filter "PermisosTests|PermisoDependenciasTests|UsuarioServiceTests"` y `dotnet test tests/StockApp.Presentation.Tests --filter CatalogoPermisosPanelTests`.
- [ ] **Step 3: Implementar** las constantes, `Todos`, `PermisosInicialesOperador`, `Requisitos` y el catálogo.
- [ ] **Step 4: Correr los mismos filtros.** Esperado: PASS.
- [ ] **Step 5: Commit** `feat(proyectos): permisos proyectos.ver y proyectos.gestionar`.

### Task 3: Persistencia y migración aditiva

**Files:**
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`:
  - DbSets `Proyectos` y `NotasProyecto`;
  - config de `Proyecto` calcada de `Tarea` (:381-407): FKs a los 4 catálogos con `Restrict`, `Presupuesto` con precisión 18,4;
  - `Tarea.ProyectoId` y `Gasto.ProyectoId`, ambas con `Restrict`.
- Create: `src/StockApp.Application/Interfaces/IProyectoRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/ProyectoRepository.cs`
- Create: `src/StockApp.Infrastructure/Migrations/<timestamp>_AgregaProyectos.cs` (+ Designer + snapshot)
- Create: `src/StockApp.Infrastructure/Migrations/ProyectosPermisosBackfillSql.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/ProyectoRepositoryTests.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Migrations/AgregaProyectosMigrationTests.cs`

**Interfaces:**
- Consumes: entidades de T1, permisos de T2.
- Produces `IProyectoRepository`:
  ```csharp
  Task<int> AgregarAsync(Proyecto proyecto);
  Task<Proyecto?> ObtenerPorIdAsync(int id);              // con catálogos y Notas ordenadas por Fecha
  Task<IReadOnlyList<Proyecto>> ListarAsync();             // con catálogos, por FechaCreacion desc
  Task<IReadOnlyList<Gasto>> ListarGastosActivosAsync(int proyectoId); // Activo == true, con Proveedor, por Fecha desc
  Task ActualizarAsync(Proyecto proyecto);                 // instancia tracked de ObtenerPorIdAsync
  ```
- `ProyectosPermisosBackfillSql.InsertarPermisosProyectos`: el mismo patrón `INSERT ... CROSS JOIN (VALUES ('proyectos.ver'),('proyectos.gestionar')) WHERE u."Rol" = 1 ON CONFLICT DO NOTHING` de `PermisoUsuarioBackfillSql`. NO se modifica `PermisoUsuarioBackfillSql`: es historia, y su test afirma 9.

- [ ] **Step 1: Tests que fallan.**
  - `ProyectoRepositoryTests`:
    - `AgregarYObtener_ConservaPresupuestoYClasificadores`;
    - `ListarGastosActivosAsync_ExcluyeAnuladosYDeOtrosProyectos`;
    - `ActualizarAsync_PersisteNotaNueva`.
  - `AgregaProyectosMigrationTests`: siguiendo el patrón de `QuitaFuenteDelGastoMigrationTests`:
    - `Up_OperadorExistente_RecibeVerYGestionarProyectos`;
    - `Up_AdminNoRecibeFilas`;
    - `Up_TareasYGastosExistentes_QuedanConProyectoNull`.
- [ ] **Step 2: Correr y ver el rojo.** `dotnet test tests/StockApp.Infrastructure.Tests --filter "ProyectoRepositoryTests|AgregaProyectosMigrationTests"`.
- [ ] **Step 3: Implementar.** Configurar el DbContext y el repositorio. Generar la migración con `dotnet ef migrations add AgregaProyectos` y agregar `migrationBuilder.Sql(ProyectosPermisosBackfillSql.InsertarPermisosProyectos)` al final de `Up`. `Down` borra las filas `proyectos.*` de `PermisosUsuario` y deshace el esquema.
- [ ] **Step 4: Correr Infrastructure.Tests completo, salvo Kit** (`--filter "FullyQualifiedName!~Kit"`). Esperado: PASS. La línea base no-Kit es 434 + los nuevos.
- [ ] **Step 5: Commit** `feat(proyectos): persistencia y migración AgregaProyectos`.

### Task 4: ProyectoService

**Files:**
- Create: `src/StockApp.Application/Proyectos/IProyectoService.cs`
- Create: `src/StockApp.Application/Proyectos/ProyectoService.cs`
- Create: `src/StockApp.Application/Proyectos/ProyectosDtos.cs`
- Test: `tests/StockApp.Application.Tests/Proyectos/ProyectoServiceTests.cs`

**Interfaces:**
- Consumes: `IProyectoRepository` (T3), `IUsuarioRepository`, `ICurrentSession`, `IAuthorizationService`, `IAuditLogger`, y los repositorios de Zona, DimensionTematica, OrganismoResponsable y OrigenFinanciamiento.
- Produces:
  ```csharp
  public record DatosProyecto(string Titulo, string? Descripcion, PrioridadTarea Prioridad,
      DateTime? FechaLimite, decimal? Presupuesto,
      int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId, int? OrigenFinanciamientoId);
  public record GastoDeProyectoDto(int Id, DateTime Fecha, string ProveedorNombre, string Detalle, decimal MontoTotal);
  public record FichaProyectoDto(Proyecto Proyecto, decimal TotalGastado,
      decimal? Disponible,                       // Presupuesto - TotalGastado; null sin presupuesto
      IReadOnlyList<GastoDeProyectoDto>? Gastos); // null si la sesión no tiene finanzas.ver
  public record OpcionProyectoDto(int Id, string Titulo, EstadoProyecto Estado);

  public interface IProyectoService
  {
      Task<int> CrearAsync(DatosProyecto datos);                       // GestionarProyectos
      Task ModificarAsync(int id, DatosProyecto datos);                // GestionarProyectos
      Task CambiarEstadoAsync(int id, EstadoProyecto destino);         // GestionarProyectos
      Task AgregarNotaAsync(int id, string texto);                     // GestionarProyectos
      Task<IReadOnlyList<Proyecto>> ListarAsync();                     // VerProyectos
      Task<FichaProyectoDto> ObtenerFichaAsync(int id);                // VerProyectos
      Task<IReadOnlyList<OpcionProyectoDto>> ListarOpcionesAsync();    // solo sesión autenticada
  }
  ```
- `ListarOpcionesAsync` devuelve los proyectos Pendiente y En curso, ordenados por título. Alimenta los selectores de gasto, factura y tarea, cuyos usuarios pueden no tener `proyectos.ver`. Por eso solo exige sesión autenticada y expone apenas id, título y estado.

- [ ] **Step 1: Tests que fallan.**
  - `CrearAsync_TituloVacio_Lanza`: `ArgumentException` con `"El título del proyecto es obligatorio."`.
  - `CrearAsync_PresupuestoNegativo_Lanza`: `ReglaDeNegocioException` con `"El presupuesto no puede ser negativo."`.
  - `CrearAsync_CatalogoInactivo_Lanza`: misma regla que `TareaService.ValidarClasificacionAsync`.
  - `CrearAsync_AuditaAltaProyecto_YFuerzaPendiente`.
  - `ModificarAsync_ProyectoCerrado_Lanza`: `ReglaDeNegocioException` con `"Reabrí el proyecto para editarlo."`.
  - `ModificarAsync_AuditaDiffDeCampos` (`ModificacionProyecto`, con el detalle "campo: antes → después"). `ModificarAsync_SinCambios_NoAudita`.
  - `CambiarEstadoAsync_AuditaEstadoAnteriorYNuevo`: `CambioEstadoProyecto` con el detalle `"Pendiente → EnCurso"`.
  - `AgregarNotaAsync_TextoVacio_Lanza` y `AgregarNotaAsync_EnProyectoCerrado_Permitido`.
  - `ObtenerFichaAsync_SumaSoloGastosActivos`. El repositorio fake devuelve solo activos; el test arma 100 + 50 y espera `TotalGastado == 150`.
  - `ObtenerFichaAsync_SinPresupuesto_DisponibleNull`.
  - `ObtenerFichaAsync_PresupuestoCero_DisponibleNegativo`: presupuesto 0 y gastado 150 dan `Disponible == -150`.
  - `ObtenerFichaAsync_OperadorSinVerFinanzas_GastosNull`.
  - `ObtenerFichaAsync_OperadorConVerFinanzas_ListaGastos`.
  - `ListarOpcionesAsync_SoloPendientesYEnCurso`.
  - Matriz de permisos: cada método lanza `UnauthorizedAccessException` sin su permiso, probado con un Operador de permisos mixtos.
  - `IProyectoService_ExponeExactamenteLosMetodosEsperados` (whitelist de 7).
- [ ] **Step 2: Correr y ver el rojo.** `dotnet test tests/StockApp.Application.Tests --filter ProyectoServiceTests`.
- [ ] **Step 3: Implementar.**
  - El chequeo de `finanzas.ver` en la ficha usa la misma expresión que los VMs: `_session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerFinanzas)`.
  - La validación de catálogos repite la regla de `TareaService.ValidarClasificacionAsync`: cada catálogo existe y está `Activo`. En T9 esa validación sale de `TareaService`; no crear una abstracción compartida.
- [ ] **Step 4: Correr Application.Tests completo.** Esperado: PASS.
- [ ] **Step 5: Commit** `feat(proyectos): servicio de proyectos con ficha y presupuesto`.

### Task 5: API y ApiClient de Proyectos

**Files:**
- Create: `src/StockApp.Api/Endpoints/ProyectosEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (DI de `IProyectoService`/`IProyectoRepository` + `MapProyectosEndpoints`)
- Create: `src/StockApp.ApiClient/ProyectoApiClient.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (DI desktop)
- Test: `tests/StockApp.Api.Tests/ProyectosEndpointTests.cs`, `tests/StockApp.Api.Tests/Auth/PermisosEndpointGuardTests.cs`, `tests/StockApp.ApiClient.Tests/ProyectoApiClientTests.cs`

**Interfaces:**
- Consumes: `IProyectoService` (T4).
- Rutas bajo el grupo `/proyectos`:

| Ruta | Permiso |
|---|---|
| POST `/` | GestionarProyectos |
| PUT `/{id}` | GestionarProyectos |
| POST `/{id}/estado` (body `{ estado }`) | GestionarProyectos |
| POST `/{id}/notas` | GestionarProyectos |
| GET `/` | VerProyectos |
| GET `/{id}` (ficha) | VerProyectos |
| GET `/opciones` | solo autenticado |

- Produces: `ProyectoApiClient : IProyectoService`, con records `*Wire`/`*Body` internos que reflejan los DTOs públicos de la API. Mismo patrón que `TareaApiClient`.

- [ ] **Step 1: Tests que fallan.**
  - `ProyectosEndpointTests` con la matriz 401/403/404/409 del patrón `TareasEndpointTests`:
    - `GetFicha_OperadorSinVerFinanzas_GastosNullEnJson`;
    - `GetOpciones_OperadorSinPermisosDeProyectos_200`;
    - `PostEstado_TransicionInvalida_409`.
  - Agregar las 7 rutas a la tabla de `PermisosEndpointGuardTests`.
  - `ProyectoApiClientTests`: ida y vuelta de cada método, incluido `Gastos == null`.
- [ ] **Step 2: Correr y ver el rojo.** `dotnet test tests/StockApp.Api.Tests --filter "ProyectosEndpointTests|PermisosEndpointGuardTests"` y `dotnet test tests/StockApp.ApiClient.Tests --filter ProyectoApiClientTests`.
- [ ] **Step 3: Implementar** los endpoints, la DI y el client.
- [ ] **Step 4: Correr Api.Tests y ApiClient.Tests completos.** Esperado: PASS.
- [ ] **Step 5: Commit** `feat(proyectos): endpoints y cliente HTTP de proyectos`.

### Task 6: Tarea ↔ Proyecto (backend)

**Files:**
- Modify: `src/StockApp.Application/Tareas/ITareaService.cs`, `TareaService.cs`, `DatosClasificacionTarea.cs`
- Modify: `src/StockApp.Application/Interfaces/ITareaRepository.cs`, `src/StockApp.Infrastructure/Repositories/TareaRepository.cs`
- Modify: `src/StockApp.Api/Endpoints/TareasEndpoints.cs`, `src/StockApp.ApiClient/TareaApiClient.cs`
- Test: `TareaServiceTests.cs`, `TareaRepositoryTests.cs`, `TareasEndpointTests.cs`, `TareaApiClientTests.cs`, `PermisosEndpointGuardTests.cs`

**Interfaces:**
- Produces:
  - `DatosClasificacionTarea` suma `int? ProyectoId`. Los cuatro clasificadores siguen ahí hasta T9.
  - `ITareaService.ListarPorProyectoAsync(int proyectoId)`, que exige `GestionarTareas`.
  - `GET /tareas?proyectoId=`.
  - `TareaDto` suma `ProyectoId` y `ProyectoTitulo`; `CrearTareaRequest` y `ClasificarTareaRequest` suman `ProyectoId`.
- Validación en `TareaService` (alta y reclasificación): si `ProyectoId` es nuevo (distinto del actual), el proyecto tiene que existir y estar Pendiente o En curso. Si no existe: `EntidadNoEncontradaException` con `"Proyecto {id} no encontrado."`. Si está cerrado: `ReglaDeNegocioException` con `"Solo se pueden vincular proyectos pendientes o en curso."`. Mantener el mismo valor es válido aunque el proyecto esté cerrado. `TareaService` suma `IProyectoRepository` al constructor.

- [ ] **Step 1: Tests que fallan.**
  - `CrearAsync_ConProyectoEnCurso_Vincula`.
  - `CrearAsync_ConProyectoTerminado_Lanza`.
  - `CrearAsync_ConProyectoInexistente_Lanza`.
  - `ReclasificarAsync_MismoProyectoCerrado_Permitido`.
  - `ReclasificarAsync_QuitarProyecto_DejaNull`.
  - `ListarPorProyectoAsync_SoloLasDelProyecto`.
  - Actualizar `ITareaService_ExponeExactamenteLosMetodosEsperados` a 12 métodos.
  - Agregar la fila de la ruta en `PermisosEndpointGuardTests`.
- [ ] **Step 2: Correr y ver el rojo** con esos filtros.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Correr Application.Tests, Api.Tests, ApiClient.Tests e Infrastructure.Tests no-Kit.** Esperado: PASS.
- [ ] **Step 5: Commit** `feat(proyectos): tareas vinculables a un proyecto`.

### Task 7: Gasto ↔ Proyecto (backend)

**Files:**
- Modify: `src/StockApp.Application/Finanzas/GastoService.cs` (`AltaAsync`, `ModificarAsync` + diff `Comparar`)
- Modify: `src/StockApp.Infrastructure/Repositories/GastoRepository.cs` (include `Proyecto`)
- Modify: `src/StockApp.Api/Endpoints/GastosEndpoints.cs` (`GastoDto` + `ProyectoId`/`ProyectoTitulo`; `CrearGastoRequest`/`ModificarGastoRequest` + `ProyectoId`; mapeo :169/:181)
- Modify: `src/StockApp.ApiClient/GastoApiClient.cs`
- Modify: `src/StockApp.Application/Movimientos/IngresoPorFacturaDtos.cs`, `IngresoPorFacturaService.cs` (:176), `src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs` (`IngresoPorFacturaArgs`), `src/StockApp.Api/Endpoints/IngresoPorFacturaEndpoints.cs`, `src/StockApp.ApiClient/IngresoPorFacturaApiClient.cs`
- Test: `GastoServiceTests.cs`, `IngresoPorFacturaServiceTests.cs`, `GastosEndpointTests.cs`, `IngresoPorFacturaEndpointTests.cs`, `GastoApiClientTests.cs`, `IngresoPorFacturaApiClientTests.cs`

**Interfaces:**
- Consumes: `IProyectoRepository` (T3), que se inyecta en `GastoService` y en `IngresoPorFacturaService`.
- Regla: es la misma de T6, con el mensaje `"Solo se pueden imputar proyectos pendientes o en curso."`.
  - Se aplica en `AltaAsync`, en `ModificarAsync` cuando el `ProyectoId` cambia a un valor no nulo, y en `IngresoPorFacturaService.RegistrarAsync`.
  - Un gasto anulado ya se rechaza en `ModificarAsync` (:98), y eso no cambia.
  - Cambiar el proyecto entra en el diff auditado de `ModificacionGasto` con la etiqueta "Proyecto".

- [ ] **Step 1: Tests que fallan.**
  - `AltaAsync_ConProyectoEnCurso_Imputa`.
  - `AltaAsync_ConProyectoCancelado_Lanza`.
  - `ModificarAsync_CambiaProyecto_AuditaDiff`.
  - `ModificarAsync_MismoProyectoYaTerminado_GuardaSinError`.
  - `ModificarAsync_QuitarProyecto_DejaNull`.
  - `RegistrarAsync_FacturaConProyecto_GastoImputado`.
  - Endpoint: `Post_ConProyectoId_DevuelveProyectoTituloEnGet`.
  - ApiClient: ida y vuelta de `ProyectoId`.
- [ ] **Step 2: Correr y ver el rojo** con esos filtros.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Correr Application.Tests, Api.Tests, ApiClient.Tests e Infrastructure.Tests no-Kit.** Esperado: PASS.
- [ ] **Step 5: Commit** `feat(proyectos): gastos imputables a un proyecto`.

### Task 8: El reporte-matriz pasa a contar proyectos

**Files (renombrar con `git mv` y adaptar):**
- `src/StockApp.Application/Reportes/TareasReporteDtos.cs` → `ProyectosReporteDtos.cs`
- `IReporteTareasService.cs`/`ReporteTareasService.cs` → `IReporteProyectosService.cs`/`ReporteProyectosService.cs`
- `Interfaces/IReporteTareasRepository.cs` → `IReporteProyectosRepository.cs`
- `Infrastructure/Repositories/ReporteTareasRepository.cs` → `ReporteProyectosRepository.cs`
- `ApiClient/ReporteTareasApiClient.cs` → `ReporteProyectosApiClient.cs`
- `Presentation/ViewModels/Reportes/ReporteTareasViewModel.cs` + `Views/Reportes/ReporteTareasView.axaml(.cs)` → `ReporteProyectos*`
- Modify: `Api/Endpoints/ReportesEndpoints.cs:27`, `Api/Program.cs:196-197`, `Presentation/App.axaml.cs:337`, `ViewModels/Reportes/ReportesViewModel.cs:58`, `ShellMainViewModel.SeccionPorPantalla`
- Tests: renombrar y adaptar los tests listados para el reporte en el mapa. Son estos, más los guardianes (`GuardianSistemaV2Tests`, `ReflexionVistaViewModelTests`, `ExportarPdfBotonesGuardianTests`, `PermisosEndpointGuardTests`, `ShellMainSeccionActivaTests`):
  - `ReporteTareasServiceTests`
  - `ReporteTareasRepositoryTests`
  - `ReportesEndpointTests`
  - `ReporteTareasApiClientTests`
  - `ReporteTareasViewModelTests`
  - `ColumnasPdfContraDtosRealesTests`
  - `ReporteTareasCriterioCierreAvisoTests`
  - `V2/ReportesConFiltrosViewV2Tests`
  - `DatosReportes`

**Interfaces:**
- Produces:
  ```csharp
  public enum AgrupadorProyectos { Zona = 0, DimensionTematica = 1, OrganismoResponsable = 2, OrigenFinanciamiento = 3 }
  public enum CriterioFechaProyectos { Creacion = 0, Cierre = 1 }      // Cierre usa Proyecto.FechaFin
  public record FiltroReporteProyectos(AgrupadorProyectos Agrupador, CriterioFechaProyectos Criterio, DateTime Desde, DateTime Hasta);
  public record FilaReporteProyectos(string Clasificador, int Pendientes, int EnCurso, int Terminados, int Cancelados, int Total);
  public record ReporteProyectosDto(IReadOnlyList<FilaReporteProyectos> Filas, FilaReporteProyectos TotalGeneral);
  ```
  - Endpoint: `GET /reportes/proyectos`, con `VerReportes`. `/reportes/tareas` desaparece.
  - Título del PDF y de la pantalla: "Estadística de proyectos". El eje Expediente se elimina (spec, objetivo 7).
  - Mismo contrato UTC de Desde/Hasta que el DTO viejo: copiar su comentario.

- [ ] **Step 1: Adaptar los tests** a los tipos nuevos.
  - `ObtenerAsync_PorZona_CuentaProyectosPorEstado`.
  - `ObtenerAsync_CriterioCierre_SoloCerrados`.
  - `AgrupadoresDisponibles_SinExpediente`: el VM expone exactamente 4.
  - `ExportarPdf_TituloEstadisticaDeProyectos`.
  - Borrar los tests del eje Expediente.
- [ ] **Step 2: Correr y ver el rojo.**
- [ ] **Step 3: Implementar el renombre y la adaptación.** El repositorio agrupa `Proyectos` en vez de `Tareas`.
- [ ] **Step 4: Compilar la `.sln` y correr Application.Tests, Infrastructure.Tests no-Kit, Api.Tests, ApiClient.Tests, Presentation.Tests y Presentation.UiTests.** Esperado: PASS.
- [ ] **Step 5: Commit** `feat(proyectos): el reporte por clasificadores cuenta proyectos`.

### Task 9: La Tarea pierde los cuatro clasificadores (DESTRUCTIVA, commit propio)

**Files:**
- Modify: `src/StockApp.Domain/Entities/Tarea.cs` (quitar :49-56 y actualizar el comentario de clase)
- Modify: `DatosClasificacionTarea.cs`: queda `record DatosClasificacionTarea(int? ProyectoId, int? DocumentoAdministrativoId)`
- Modify: `TareaService.cs` (constructor sin los 4 repositorios; `ValidarClasificacionAsync` y `ConstruirDiffClasificacion` solo con proyecto y expediente)
- Modify: `TareaRepository.ConIncludes`, `AppDbContext` (`Entity<Tarea>`), `TareasEndpoints` (DTOs), `TareaApiClient` (wire)
- Modify: `src/StockApp.Presentation/ViewModels/Tareas/ClasificacionTareaPanelViewModel.cs` + `.axaml`. Quedan el buscador de expediente y el selector de proyecto, alimentado por `IProyectoService.ListarOpcionesAsync`; salen los 4 combos. En edición, si el proyecto actual está cerrado, se agrega a las opciones como fallback.
- Modify: `TareaListView.axaml` (la columna CLASIFICACIÓN :179 pasa a PROYECTO, con el título del proyecto o "—") y `TareaFila`
- Create: `src/StockApp.Infrastructure/Migrations/<timestamp>_QuitaClasificadoresDeTarea.cs`
- Test: `TareaTests`, `ClasificadoresTareaTests` (borrar o reducir), `TareaServiceTests`, `TareaRepositoryTests`, `ClasificadoresTareaPersistenciaTests`, `TareasEndpointTests`, `TareaApiClientTests`, `ClasificacionTareaPanelViewModelTests`, `ReclasificarTareaDialogViewModelTests`, `TareaListViewTests`, `TareaFormViewTests`, `V2/TareaListViewV2Tests`, `V2/TareaFormViewV2Tests`, `ReflexionVistaViewModelTests`, `VisualTests/Pantallas/Gestion/DatosTareas`
- Test: `tests/StockApp.Infrastructure.Tests/Migrations/QuitaClasificadoresDeTareaMigrationTests.cs`

- [ ] **Step 1: Tests que fallan.**
  - `QuitaClasificadoresDeTareaMigrationTests.Up_TareaConExpedienteYNotas_ConservaTodoMenosClasificadores`. Siembra una tarea con los 4 clasificadores, expediente, 2 notas, estado EnCurso, TomadaPor y FechaInicio. Después de `Up`, las 4 columnas no existen y todo lo demás está igual.
  - `Up_CatalogosIntactos`: Zonas y demás catálogos conservan sus filas.
  - Panel: `InicializarAsync_ExponeProyectosYExpediente_SinClasificadores`.
  - Panel: `InicializarAsync_ProyectoActualCerrado_ApareceComoOpcion`.
- [ ] **Step 2: Correr y ver el rojo.**
- [ ] **Step 3: Implementar.** La migración es `DropForeignKey` + `DropIndex` + `DropColumn` de las 4 columnas, siguiendo el patrón de `QuitaFuenteDelGasto`. `Down` las recrea vacías.
- [ ] **Step 4: Compilar la `.sln` y correr todas las suites salvo VisualTests y Kit.** Después, VisualTests en dos tandas con `--filter` (tardan entre 12 y 14 min y superan el timeout de 600 s del Bash). Esperado: PASS.
- [ ] **Step 5: Commit** `feat(tareas)!: la tarea deja los clasificadores, que pasan a Proyecto`. El cuerpo del commit avisa que la migración borra datos y que exige un backup verificado antes de desplegar.

### Task 10: Desktop — módulo Proyectos

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Proyectos/ProyectoListViewModel.cs` + `Views/Proyectos/ProyectoListView.axaml(.cs)`
- Create: `src/StockApp.Presentation/ViewModels/Proyectos/ProyectoFormViewModel.cs` + `Views/Proyectos/ProyectoFormView.axaml(.cs)`
- Modify: `ShellMainViewModel.cs`:
  - `PuedeVerProyectos` y `PuedeGestionarProyectos` con el patrón de :71;
  - item `CrearItem("Proyectos", "mdi-folder-outline", NavProyectosCommand, "Proyectos", () => PuedeVerProyectos)` en el grupo GESTIÓN, antes de Tareas;
  - entradas en `SeccionPorPantalla`;
  - `NotificarPermisos`.
- Modify: `App.axaml.cs` (DI de los VMs)
- Modify: `TareaFormViewModel.CargarParaCrear`, que suma el parámetro opcional `int? proyectoId = null` y preselecciona el proyecto en el panel
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Proyectos/ProyectoListViewModelTests.cs`, `ProyectoFormViewModelTests.cs`
- Test: `tests/StockApp.Presentation.UiTests/ProyectoFormViewTests.cs`, `V2/ProyectoListViewV2Tests.cs`, `V2/ProyectoFormViewV2Tests.cs` + guardianes (`GuardianSistemaV2Tests`, `ReflexionVistaViewModelTests`, `CargaProtegidaSinPermisoGuardianTests`, `ShellMainSeccionActivaTests`, `ComandosDeGuardadoGuardianTests`, `GrillasSinRecorteBarridoTests`)
- Test: `tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/ProyectosComparacionTests.cs`

**Interfaces:**
- Consumes: `IProyectoService` (T4/T5), `ITareaService.ListarPorProyectoAsync` (T6), los servicios de catálogos que ya usa `ClasificacionTareaPanelViewModel`.
- La lista es una grilla con TÍTULO · ESTADO · PRIORIDAD · ZONA · PRESUPUESTO · FECHA LÍMITE, con filtro por estado y botón "Nuevo proyecto" (visible con `PuedeGestionarProyectos`).
- El formulario/ficha funciona en dos modos:
  - **`CargarParaCrear()`**: muestra los campos de `DatosProyecto`.
  - **`CargarParaVer(int id)`**:
    - edición de los mismos campos, en solo lectura si `EsCerrado` o sin permiso de gestión;
    - botones de estado según `PuedeTransicionarA`, con los textos "Iniciar", "Volver a pendiente", "Terminar", "Cancelar" y "Reabrir";
    - notas;
    - sección **Tareas** con la grilla de las tareas del proyecto y el botón "Nueva tarea", que navega a `TareaFormViewModel.CargarParaCrear(proyectoId)`;
    - sección **Gastos** con el total gastado, el presupuesto, el disponible y la lista de gastos.
  - Sin presupuesto se ve solo "Total gastado".
  - Con `Disponible < 0`, se ve "Sobregiro" en rojo con el valor absoluto.
  - La lista de gastos (FECHA · PROVEEDOR · DETALLE · MONTO) se muestra solo si `Ficha.Gastos != null`.

- [ ] **Step 1: Tests que fallan.**
  - VM:
    - `CargarParaVer_SinPresupuesto_MuestraSoloTotalGastado`;
    - `CargarParaVer_PresupuestoCero_MuestraSobregiro`;
    - `CargarParaVer_GastosNull_OcultaListaDeGastos`;
    - `BotonReabrir_SoloEnCerrado`;
    - `Guardar_ProyectoCerrado_SoloLectura`;
    - `NuevaTarea_NavegaConProyectoPreseleccionado`.
  - UiTest con la View real y un Operador con `proyectos.ver` sin `finanzas.ver`: `FichaProyecto_SinVerFinanzas_ListaDeGastosNoVisible_TotalesVisibles`.
  - UiTest: `Sidebar_OperadorSinVerProyectos_NoMuestraProyectos`.
- [ ] **Step 2: Correr y ver el rojo.**
- [ ] **Step 3: Implementar** con el sistema visual UI v2. Las Views cargan en `DataContextChanged`. El guardado usa `ComandoDeGuardado`. Los montos van con `FormatoEsUy`.
- [ ] **Step 4: Compilar la `.sln` y correr Presentation.Tests y Presentation.UiTests. Correr VisualTests de `Pantallas/Gestion`.** Esperado: PASS.
- [ ] **Step 5: Commit** `feat(proyectos): pantallas de lista y ficha de proyecto`.

### Task 11: Desktop — selector de proyecto en gasto y factura

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs` (`ProyectosDisponibles`, `ProyectoSeleccionado`; carga en `InicializarAsync` :389-445 con el mismo fallback de maestros inactivos que usa para LineaPoa)
- Modify: `Views/Finanzas/GastoFormView.axaml` (combo opcional "Proyecto" con opción vacía "Sin proyecto")
- Modify: `ViewModels/Movimientos/IngresoPorFacturaViewModel.cs` + su View (mismo combo)
- Test: `GastoFormViewModelTests.cs`, `IngresoPorFacturaViewModelTests.cs`, `UiTests/V2/GastoFormViewV2Tests.cs`, `UiTests/V2/IngresoPorFacturaViewV2Tests.cs`, `ReflexionVistaViewModelTests`
- NO se toca `GastosView.axaml` ni `GastosViewModel.ColumnasPdf` (decisión del spec: sin columna).

- [ ] **Step 1: Tests que fallan.**
  - `InicializarAsync_CargaProyectosDesdeListarOpciones`.
  - `CargarParaEditar_ProyectoTerminado_ApareceSeleccionado`.
  - `Guardar_EnviaProyectoId`.
  - `Guardar_SinProyecto_EnviaNull`.
  - Los mismos tres en la factura.
  - UiTest: el combo existe y es editable para un Operador con `finanzas.gastos` sin `proyectos.ver`.
- [ ] **Step 2: Correr y ver el rojo.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Compilar la `.sln` y correr Presentation.Tests y Presentation.UiTests.** Esperado: PASS.
- [ ] **Step 5: Commit** `feat(proyectos): elegir proyecto al cargar un gasto o una factura`.

### Task 12: Cierre — suites completas y GATE orgánico

- [ ] **Step 1: Suites completas.** Todas, incluido Infrastructure con Kit. VisualTests van en dos tandas. Se reportan los conteos por suite.
- [ ] **Step 2: GATE orgánico.** Un subagente maneja la app real (API + Postgres `stockapp-pg` + desktop) con el toolkit de `scripts/gui-verificacion/` y reporta OK o FALLA con capturas. Si WSLg está caído, se reemplaza por render headless y se deja dicho.
  1. Admin crea un proyecto con zona y presupuesto $ 1.000,00.
  2. Carga un gasto contado de $ 1.200,00 imputado a ese proyecto.
  3. La ficha muestra Total gastado $ 1.200,00 y Sobregiro $ 200,00 en rojo.
  4. Anula el gasto: Total gastado $ 0,00.
  5. Crea una tarea desde la ficha: queda vinculada.
  6. Un Operador con `proyectos.ver` sin `finanzas.ver` ve los totales pero no la lista.
  7. Termina el proyecto: el combo del gasto ya no lo ofrece; lo reabre y vuelve a aparecer.
  8. El reporte por zona cuenta el proyecto.
  9. Una tarea vieja con expediente lo conserva.
- [ ] **Step 3: Actualizar el spec** si algo cambió y dejar anotada la deuda.
