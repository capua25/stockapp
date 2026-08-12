# Módulo de documentos administrativos

Fecha: 2026-08-11
Estado: aprobado, pendiente de plan de implementación

## Problema

El cliente pidió, textual: registrar y hacer seguimiento de documentos administrativos —expedientes, oficios y suministros—, con como mínimo fecha de emisión/ingreso, funcionario responsable que registra, tipo de documento, breve descripción y estado del trámite (Pendiente / En proceso / Finalizado). El objetivo declarado es control interno: trazabilidad desde la emisión hasta la finalización, y poder identificar qué documentos quedaron pendientes en cada etapa.

Hoy no existe nada parecido en el dominio. Lo más cercano en forma —no en contenido— es el módulo de Tareas (2026-08-01): una entidad con estado, un responsable, y un hilo de eventos append-only. Ese parecido no es casualidad: **este módulo se construye copiando el patrón de Tareas capa por capa**, no inventando arquitectura nueva. Donde el pedido del cliente difiere de Tareas —numeración propia con año explícito, cuatro estados en vez de tres, reapertura, adjuntos— son decisiones nuevas, documentadas abajo con su motivo.

## Decisiones

**D1. Número propio obligatorio.** `Numero` es `string` (conserva ceros a la izquierda, ej. `"0087"`, tal como figura en el papel) y `Anio` es un campo `int` explícito, no derivado de `FechaEmision`. Índice único compuesto `(Tipo, Anio, Numero)` en la base.

Por qué `Anio` no se deriva de la fecha: un expediente que entra el 3 de enero puede pertenecer al ejercicio anterior. Si el año se derivara de la fecha, corregir una fecha mal tipeada cambiaría la identidad del documento (dos expedientes con el mismo número podrían colisionar o dejar de colisionar según un typo). El default del campo `Anio` en el alta es el año de `FechaEmision`, pero es corregible por separado.

**D2. Un solo funcionario: el usuario logueado.** `RegistradoPorUsuarioId` se completa con el usuario autenticado al momento del alta y no es editable.

Limitación conocida, documentada explícitamente: el sistema responde "quién lo registró", no "en manos de quién está el trámite hoy". Si más adelante el cliente pide un tablero de pendientes por funcionario responsable, hace falta agregar un campo de responsable reasignable — que hoy no existe. Se decidió así por ser literal al pedido del cliente, que solo pide "funcionario responsable que registra".

**D3. Cuatro estados.** Enum `EstadoDocumento`: `Pendiente = 0`, `EnProceso = 1`, `Finalizado = 2`, `Anulado = 3`.

El cuarto estado no lo pidió el cliente, pero es necesario: el trámite que muere sin completarse (el vecino no volvió, el oficio estaba mal emitido) necesita una salida honesta. Sin `Anulado`, o queda colgado en `Pendiente` para siempre, o se lo marca `Finalizado` y se falsea la estadística de trámites completados — el mismo argumento que ya justificó `Cancelada` en Tareas.

**D4. Máquina de estados en la entidad**, mismo molde que `Tarea`: diccionario privado `TransicionesValidas`, `CambiarEstado(EstadoDocumento destino)` que valida contra la tabla y lanza `ReglaDeNegocioException` si la transición no está listada, y `PuedeTransicionarA(EstadoDocumento destino)` de solo lectura para que la UI consulte la misma tabla en vez de recodificarla.

```
Pendiente   -> { EnProceso, Anulado }
EnProceso   -> { Pendiente, Finalizado, Anulado }
Finalizado  -> { EnProceso }   (reapertura)
Anulado     -> { EnProceso }   (reapertura)
```

Desviación consciente respecto de Tareas: ahí `Terminada` y `Cancelada` no tienen salidas listadas y por eso son terminales por construcción (`EsTerminal` se deriva de que `TransicionesValidas[Estado]` esté vacío). Acá `Finalizado` y `Anulado` **sí** tienen salida, por la reapertura — así que el concepto "terminal" no se puede derivar de la misma forma. Se parte en dos propiedades explícitas:

- **`EsActivo`** → `Estado is Pendiente or EnProceso`. Va a la solapa Activos.
- **`EsCerrado`** → `Estado is Finalizado or Anulado`. Va a Historial.

El dominio permite la transición de reapertura sin condiciones; el dominio **no sabe de roles**. El corte "reabrir es solo Admin" y "motivo obligatorio" vive en `DocumentoAdministrativoService`, que es donde vive la autorización en todo el proyecto — el dominio nunca consulta `RolUsuario` ni `IAuthorizationService`.

**D5. Historial append-only.** Entidad `EventoDocumento` (molde de `NotaTarea`): `Id`, `DocumentoId`, `Fecha`, `UsuarioId`, `EstadoAnterior` (`EstadoDocumento?`, nulo si es nota manual), `EstadoNuevo` (`EstadoDocumento?`, ídem), `Texto`, `EsAutomatico`. Nunca se edita ni se borra.

Cada cambio de estado genera un evento automático (`EsAutomatico = true`, con `EstadoAnterior`/`EstadoNuevo` completos); el funcionario puede sumar notas a mano (`EsAutomatico = false`, estados nulos). Es lo que responde "por dónde pasó el trámite y cuánto tardó en cada etapa" — sin esto, "trazabilidad" es una palabra vacía en el spec.

**D6. Tipo de documento: enum fijo.** `TipoDocumento { Expediente = 0, Oficio = 1, Suministro = 2 }`.

Se evaluó una tabla maestra configurable con ABM (como `Categoria` o `Proveedor`) y se descartó: cuesta 30-40% más de trabajo —tabla, repositorio, servicio, endpoints, ABM en el desktop, permiso nuevo, tests de todo eso— para evitar una línea de enum y un deploy. El enum se persiste como `int` y es append-only: agregar un tipo nuevo el día de mañana es una línea de código, no una migración de datos. Tampoco encierra: migrar a tabla maestra después queda acotado, con los valores existentes mapeables uno a uno (0→fila, 1→fila, 2→fila).

**D7. Permisos de dos niveles**, misma convención `<modulo>.<verbo>` que ya usa `Permisos.cs` (`tareas.gestionar`, `finanzas.ver`, etc.):

- `GestionarDocumentos = "documentos.gestionar"` — **configurable**: se agrega a `AuthorizationService.PermisosInicialesOperador` (la plantilla de arranque para Operadores nuevos) y queda disponible para que el Admin lo tilde o destilde por operador desde el panel de permisos (spec 2026-08-10).
- `AdministrarDocumentos = "documentos.administrar"` — **estructural**: se agrega a `AuthorizationService.PermisosEstructuralesAdmin`. Admin sí, Operador nunca, sin consultar la tabla `PermisoUsuario` ni la cache — mismo trato que `AdministrarTareas`.

**D8. Reapertura y anulación exigen motivo.** `ReabrirAsync(id, motivo)` y `AnularAsync(id, motivo)` — las dos acciones `documentos.administrar` que cierran o reabren un trámite — validan en el servicio que el motivo no venga vacío ni en blanco, y lanzan `ReglaDeNegocioException` si lo está: un motivo que se puede dejar vacío es un campo decorativo, no un control. La reapertura vuelve el estado a `EnProceso` y limpia `FechaCierre`; la anulación sella `FechaCierre`. Ambas quedan registradas dos veces: como evento automático en el historial del documento (con el motivo en `Texto`) y como entrada de `LogAuditoria` (`ReaperturaDocumento`/`AnulacionDocumento`).

**D9. UI con pestañas Activos / Historial**, `TabControl` — mismo control que ya usa el proyecto en otras pantallas con vistas alternativas. El Historial:

- Tiene filtros propios (número, año, tipo, estado), independientes de los de Activos.
- **Se carga perezoso**, recién al abrir la solapa. Si se cargara junto con Activos, cada consulta de tres expedientes pendientes arrastraría el archivo completo del año.
- **Exige año**, con el año actual como valor por defecto del filtro. Se decidió **no paginar**: paginar cuesta cambios en las cinco capas (repositorio, servicio, endpoint, cliente HTTP, ViewModel) para un problema que el filtro por año ya resuelve con una condición `WHERE`. Si algún año puntual empieza a traer miles de registros, ahí se agrega paginación con el dato real en la mano — no antes.

**D10. Adjuntos: entidad propia `AdjuntoDocumento`, no se reusa `Adjunto` de Finanzas.** El `Adjunto` de Finanzas (`src/StockApp.Domain/Entities/Adjunto.cs`) tiene dos FK reales —`GastoId`/`PagoGastoId`— con un CHECK `CK_Adjuntos_GastoOPago` en la base que impone el invariante XOR: es integridad referencial de verdad, no decorativa.

Las alternativas evaluadas y descartadas:

- (a) Agregar una tercera FK `DocumentoId` al `Adjunto` existente. Mete Documentos adentro de Finanzas y rompe la independencia del módulo — Finanzas no debería saber que Documentos existe.
- (b) Refactorizar a polimorfismo genérico (`EntidadTipo` + `EntidadId`). Pierde la FK real y el CHECK actual, y toca código de Finanzas que hoy tiene tests verdes sin necesidad.

En cambio, se **replica** el servicio (`AdjuntoService` tiene ~150 líneas) para un `AdjuntoDocumentoService` propio, y se **reusa tal cual, sin tocar una línea**: `AdjuntoValidador` (10 MB, PDF/JPG/PNG, `src/StockApp.Application/Finanzas/AdjuntoValidador.cs` — validación por **magic bytes**, no por extensión), `IServicioSeleccionArchivo` y `ServicioAperturaArchivo` (`src/StockApp.Presentation/Services/`), y el patrón de **tabla separada para los bytes**: metadatos en `AdjuntoDocumento`, contenido en `AdjuntoDocumentoContenido` (relación 1:1, `Id` compartido), igual que `Adjunto`/`AdjuntoContenido` — para que listar adjuntos nunca arrastre megabytes de la base.

**D11. Reglas de adjuntos:**

- (a) Solo se adjunta a documentos **activos** (`EsActivo`). Si el documento está cerrado y aparece un papel nuevo, hay que reabrirlo primero (Admin + motivo + rastro en el historial). Si se pudiera modificar un expediente cerrado sin dejar rastro, el historial deja de ser confiable como fuente de auditoría.
- (b) **Quitar un adjunto exige `documentos.administrar`**, no `gestionar` — a diferencia de agregar. Un adjunto acá es prueba documental de un trámite (la factura escaneada, la nota firmada), no la foto casual de un ticket: sacarlo es una decisión de mayor peso que subirlo.
- (c) Es **baja lógica** (`Activo = false` en `AdjuntoDocumento`), nunca borrado físico — mismo criterio que `Adjunto.Activo` en Finanzas.
- (d) Adjuntar y quitar generan **evento automático** en `EventoDocumento` (`EsAutomatico = true`, sin cambio de estado — `EstadoAnterior`/`EstadoNuevo` quedan nulos, igual que una nota manual pero marcada como automática).

**D12. Límite de tamaño: 10 MB**, heredado directamente de `AdjuntoValidador.TamanoMaximoBytes` por consistencia con Finanzas — no se define un límite propio para el módulo.

Riesgo anotado explícitamente: un expediente escaneado de 40 páginas puede irse a 15-20 MB, por encima del límite. Es el primer lugar donde este módulo va a chillar en producción. La corrección es subir una constante compartida con Finanzas (`AdjuntoValidador.TamanoMaximoBytes`), lo cual sube el límite para ambos módulos a la vez — no hay forma de subirlo solo para Documentos sin separar la constante, algo a tener en cuenta si el día que esto pase Finanzas todavía quiere quedarse en 10 MB.

## Alcance

Incluido:

- Alta de documentos con número, año, tipo, fecha de emisión/ingreso, descripción y funcionario registrante (automático).
- Listado en dos pestañas: Activos (Pendiente/EnProceso) e Historial (Finalizado/Anulado), este último con filtros propios y carga perezosa.
- Transiciones de estado validadas en el dominio: iniciar proceso, finalizar, anular, reabrir.
- Reapertura restringida a Admin, con motivo obligatorio.
- Hilo de eventos por documento: automáticos (cambio de estado, adjunto agregado/quitado) y notas manuales.
- Adjuntos: agregar, listar/descargar, quitar (baja lógica), con las mismas reglas de validación que Finanzas.
- Permisos configurables (`documentos.gestionar`) y estructural (`documentos.administrar`).

Fuera de alcance:

- Tipos de documento configurables por ABM (tabla maestra). Ver D6.
- Responsable reasignable / "en manos de quién está" el trámite hoy. Ver D2.
- Paginación del historial. Ver D9.
- Notificaciones o alertas por trámite demorado.
- Vinculación entre documentos (expediente padre/hijo, oficio que deriva en expediente).
- Numeración automática — el número lo tipea el funcionario, tal como figura en el papel.

## Diseño técnico

### Domain

`DocumentoAdministrativo` (`src/StockApp.Domain/Entities/DocumentoAdministrativo.cs`): `Id`, `Numero` (`string`, requerido), `Anio` (`int`), `Tipo` (`TipoDocumento`), `FechaEmision` (`DateTime`), `Descripcion` (`string`, requerido), `Estado` (`EstadoDocumento`, default `Pendiente`), `RegistradoPorUsuarioId` (`int`) + nav `RegistradoPor`, `FechaRegistro` (`DateTime`), `FechaCierre` (`DateTime?`, se sella al pasar a `Finalizado`/`Anulado` y se limpia al reabrir), `List<EventoDocumento> Eventos`.

Propiedades derivadas `EsActivo` y `EsCerrado` (D4), y los métodos `CambiarEstado(EstadoDocumento destino)` / `PuedeTransicionarA(EstadoDocumento destino)`, mismo contrato que `Tarea.CambiarEstado`/`Tarea.PuedeTransicionarA`.

`EventoDocumento` (`src/StockApp.Domain/Entities/EventoDocumento.cs`): `Id`, `DocumentoId`, `Fecha`, `UsuarioId` + nav, `EstadoAnterior` (`EstadoDocumento?`), `EstadoNuevo` (`EstadoDocumento?`), `Texto`, `EsAutomatico`.

`AdjuntoDocumento` (`src/StockApp.Domain/Entities/AdjuntoDocumento.cs`): `Id`, `DocumentoId`, `NombreArchivo`, `ContentType`, `TamanoBytes`, `Activo` (default `true`), `FechaAltaUtc`. `AdjuntoDocumentoContenido` (`src/StockApp.Domain/Entities/AdjuntoDocumentoContenido.cs`): `Id` (= `AdjuntoDocumento.Id`), `Contenido` (`byte[]`) — mismo patrón 1:1 que `Adjunto`/`AdjuntoContenido`.

Enums en `src/StockApp.Domain/Enums/`: `EstadoDocumento` (D3) y `TipoDocumento` (D6).

### Persistencia

Sección propia en `AppDbContext.cs` (`src/StockApp.Infrastructure/Persistence/AppDbContext.cs`), siguiendo el bloque de Tareas (líneas ~322-348) como molde:

```csharp
// ── Documentos administrativos (módulo independiente, spec 2026-08-11) ────
modelBuilder.Entity<DocumentoAdministrativo>(e =>
{
    e.Property(d => d.Numero).IsRequired();
    e.Property(d => d.Descripcion).IsRequired();
    e.HasIndex(d => new { d.Tipo, d.Anio, d.Numero }).IsUnique();
    e.HasIndex(d => d.Estado);
    e.HasOne(d => d.RegistradoPor).WithMany()
        .HasForeignKey(d => d.RegistradoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
});

modelBuilder.Entity<EventoDocumento>(e =>
{
    e.Property(ev => ev.Texto).IsRequired();
    e.HasIndex(ev => ev.DocumentoId);
    e.HasOne<DocumentoAdministrativo>().WithMany(d => d.Eventos)
        .HasForeignKey(ev => ev.DocumentoId).OnDelete(DeleteBehavior.Restrict);
    e.HasOne(ev => ev.Usuario).WithMany()
        .HasForeignKey(ev => ev.UsuarioId).OnDelete(DeleteBehavior.Restrict);
});

modelBuilder.Entity<AdjuntoDocumento>(e =>
{
    e.HasIndex(a => a.DocumentoId);
    e.HasOne<DocumentoAdministrativo>().WithMany()
        .HasForeignKey(a => a.DocumentoId).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AdjuntoDocumentoContenido>().WithOne()
        .HasForeignKey<AdjuntoDocumentoContenido>(c => c.Id);
});
```

FKs a `Usuario` (`RegistradoPorUsuarioId`, `EventoDocumento.UsuarioId`) con `OnDelete(Restrict)`, nunca cascade — mismo criterio documentado en todo el proyecto (`AppDbContext.cs`, comentario junto al bloque de Tareas: "Restrict [...] mismo criterio que [...] el resto del modelo (Usuarios usa baja lógica, nunca DELETE físico)").

`DbSet<DocumentoAdministrativo> DocumentosAdministrativos`, `DbSet<EventoDocumento> EventosDocumento`, `DbSet<AdjuntoDocumento> AdjuntosDocumento`, `DbSet<AdjuntoDocumentoContenido> AdjuntosDocumentoContenido` — mismo criterio de pluralización que `NotasTarea`, `CorridasBackup`.

Migración `AgregaDocumentosAdministrativos`.

### Application

`IDocumentoAdministrativoService` / `DocumentoAdministrativoService` en `src/StockApp.Application/Documentos/`, con métodos **por acción**, no un `CambiarEstado` genérico: cada acción tiene su propio permiso, su propia validación y su propia línea de auditoría; un método genérico obligaría a un `switch` interno donde se cuelan agujeros de autorización (mismo argumento que ya aplicó Tareas — "la API expone las transiciones como acciones explícitas").

| Método | Permiso |
|---|---|
| `RegistrarAsync(doc)` | `documentos.gestionar` |
| `ListarActivosAsync(filtro)` | `documentos.gestionar` |
| `ListarHistorialAsync(filtro)` | `documentos.gestionar` |
| `ObtenerPorIdAsync(id)` | `documentos.gestionar` |
| `IniciarProcesoAsync(id)` | `documentos.gestionar` |
| `FinalizarAsync(id)` | `documentos.gestionar` |
| `AgregarNotaAsync(id, texto)` | `documentos.gestionar` |
| `AnularAsync(id, motivo)` | `documentos.administrar` |
| `ReabrirAsync(id, motivo)` | `documentos.administrar` |

Patrón fijo por método: `_auth.Verificar(_session, Permisos.X)` primero, después validar (motivo no vacío en `Anular`/`Reabrir`, documento existe), después **mutar vía la entidad** (`documento.CambiarEstado(...)`, nunca reimplementar la máquina de estados en el servicio), después guardar, después auditar. Mismo orden que `TareaService`.

`record FiltroDocumentos(TipoDocumento? Tipo, int? Anio, string? Texto, EstadoDocumento? Estado)` en `Application`, compartido por `DocumentoAdministrativoService` y `DocumentoApiClient` — mismo criterio que los demás filtros del proyecto (ej. `FiltroMovimientos`).

`IDocumentoAdministrativoRepository` en `src/StockApp.Application/Interfaces/`, implementado en `src/StockApp.Infrastructure/Repositories/DocumentoAdministrativoRepository.cs`.

Adjuntos, en un servicio separado (mismo criterio que Finanzas: `AdjuntoService` es independiente de `GastoService`):

`IAdjuntoDocumentoService` / `AdjuntoDocumentoService` en `src/StockApp.Application/Documentos/`, con `AgregarAsync(documentoId, nombreArchivo, contenido)` (`documentos.gestionar`, rechaza con `ReglaDeNegocioException` si el documento no está `EsActivo` — D11a), `ListarAsync(documentoId)` (`documentos.gestionar`), `ObtenerContenidoAsync(adjuntoId)` (`documentos.gestionar`), `QuitarAsync(adjuntoId)` (`documentos.administrar`). `IAdjuntoDocumentoRepository` / `AdjuntoDocumentoRepository`, mismo split metadatos/contenido que `AdjuntoRepository`.

### Api

`DocumentosEndpoints.cs` en `src/StockApp.Api/Endpoints/`, minimal API, grupo `/documentos`, cada endpoint con su `.RequireAuthorization(Permisos.X)`:

- `GET /documentos/activos`, `GET /documentos/historial` (con querystring de `FiltroDocumentos`), `GET /documentos/{id:int}` — `documentos.gestionar`.
- `POST /documentos` — alta, `documentos.gestionar`.
- `POST /documentos/{id:int}/iniciar`, `POST /documentos/{id:int}/finalizar`, `POST /documentos/{id:int}/notas` — `documentos.gestionar`.
- `POST /documentos/{id:int}/anular`, `POST /documentos/{id:int}/reabrir` (body con `Motivo`) — `documentos.administrar`.
- `POST /documentos/{id:int}/adjuntos` (multipart, `IFormFile archivo` + `.DisableAntiforgery()`, mismo patrón que `AdjuntosEndpoints.cs`) — `documentos.gestionar`.
- `GET /documentos/{id:int}/adjuntos` — `documentos.gestionar`.
- `GET /documentos/adjuntos/{id:int}/contenido` (`Results.File(contenido.Contenido, contenido.ContentType, contenido.NombreArchivo)`) — `documentos.gestionar`.
- `DELETE /documentos/adjuntos/{id:int}` — `documentos.administrar`.

Transiciones inválidas devuelven 409 (vía `ReglaDeNegocioException`, mapeado genéricamente por `DomainExceptionHandler`); el documento inexistente, 404 (`EntidadNoEncontradaException`).

**El número duplicado devuelve 409, no un 500 con el error crudo de Postgres adentro.** El índice único `(Tipo, Anio, Numero)` es la última defensa contra dos funcionarios cargando el mismo expediente a la vez — condición de carrera real, no hipotética. Mismo patrón que ya resuelve `GastoRepository.AgregarAsync` para la factura duplicada (`src/StockApp.Infrastructure/Repositories/GastoRepository.cs`, constraint `IX_Gastos_ProveedorId_NumeroFactura_NumeroOrden`): el repositorio atrapa el `DbUpdateException` cuya `PostgresException.SqlState` es `UniqueViolation` y cuyo `ConstraintName` coincide con el índice esperado, y lo traduce a `ReglaDeNegocioException` con mensaje de dominio:

```csharp
catch (DbUpdateException ex) when (EsViolacionNumeroUnico(ex))
{
    throw new ReglaDeNegocioException(
        $"Ya existe un {documento.Tipo} {documento.Numero}/{documento.Anio}.");
}

private static bool EsViolacionNumeroUnico(DbUpdateException ex)
    => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
       && pg.ConstraintName == "IX_DocumentosAdministrativos_Tipo_Anio_Numero";
```

El catch vive en `DocumentoAdministrativoRepository` (no en `Application`, que no referencia EF/Npgsql directamente) — mismo motivo documentado en `GastoRepository`: sin este catch ahí, la violación llega cruda y el endpoint responde 500. Va con test de integración contra Postgres real (Testcontainers): es una condición de carrera real, no una estimación.

### ApiClient

`DocumentoApiClient` en `src/StockApp.ApiClient/`, implementa `IDocumentoAdministrativoService` contra HTTP, con records Wire propios y mapeo de errores vía `ApiErrores` (403 → `UnauthorizedAccessException`, 409 → `ReglaDeNegocioException` con el mensaje del servidor). `AdjuntoDocumentoApiClient` implementa `IAdjuntoDocumentoService`, mismo criterio que `AdjuntoApiClient`.

### Presentation

`DocumentoListViewModel` (`src/StockApp.Presentation/ViewModels/Documentos/`): dos colecciones (`Activos`, `Historial`), filtros por solapa, carga perezosa del Historial (dispara `CargarHistorialAsync()` solo cuando la solapa se selecciona, no en el `CargarAsync()` inicial).

`DocumentoFila`: aplana la entidad para la grilla y expone el gating de botones consultando `documento.PuedeTransicionarA(...)` directamente — para que no exista una segunda copia de las reglas de transición en la pantalla, mismo criterio que `TareaFila.PuedeTomar`/`PuedeSoltar`/etc.

`DocumentoFormViewModel`: alta y detalle, con el hilo de eventos visible y el panel de adjuntos embebido (`AdjuntosDocumentoPanelViewModel`, mismo molde que `AdjuntosPanelViewModel` de Finanzas).

**Gap encontrado, no cubierto por ningún patrón existente**: tanto `AnularAsync` como `ReabrirAsync` reciben un `motivo` (ver tabla de métodos más arriba) — mismo criterio de motivo obligatorio que D8 establece explícitamente para la reapertura, extendido por consistencia a la anulación, ya que ambas son acciones `documentos.administrar` que cierran o reabren un trámite y dejan rastro en el historial. Hoy `IConfirmacionService` (`src/StockApp.Presentation/Services/IConfirmacionService.cs`) solo tiene `PreguntarAsync` (sí/no) e `InformarAsync` (informativo) — no hay, en ningún módulo existente, un diálogo que pida texto libre; ni siquiera `GastoService.AnularAsync` lo pide. Hace falta un método nuevo, `Task<string?> PedirTextoAsync(string mensaje)` (`null` si el usuario cancela), implementado junto a los otros dos. La validación de "no vacío" se mantiene en el servicio (D8) — el diálogo solo recolecta el texto, no decide si es válido.

Vistas con `TabControl`. **Las vistas deben enganchar `DataContextChanged` para disparar la carga inicial** — convención ya documentada del proyecto: las Views de Avalonia no se auto-inicializan, y es un bug recurrente si se omite.

DI en `App.axaml.cs`. Menú lateral: propiedad `PuedeGestionarDocumentos` nueva en `ShellMainViewModel` (mismo patrón que `PuedeGestionarTareas`: `_session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarDocumentos)`), con `IsVisible="{Binding PuedeGestionarDocumentos}"` en `ShellMainView.axaml` y refresco vía `RefrescarPermisosAsync` (se agrega `OnPropertyChanged(nameof(PuedeGestionarDocumentos))` a la lista existente).

### Manejo de errores

`ManejarErrorAsync` centralizado por ViewModel, mismo molde que `TareaListViewModel.ManejarErrorAsync`: `ReglaDeNegocioException`/`EntidadNoEncontradaException`/`ArgumentException`/`ServidorNoDisponibleException` → `ex.Message`; catch-all → mensaje genérico.

**`UnauthorizedAccessException` se atrapa en un catch separado y vacío**, sin mostrar diálogo propio: el manejador central del 403 (`AuthTokenHandler` + evento `AccesoRevocado`, cableado en `App.axaml.cs`) ya muestra el aviso y refresca permisos. Si el módulo también informara la excepción, vuelve el doble aviso que se corrigió en el commit `093fc7c` (`fix(permisos): saca el doble aviso del 403 y reescribe el mensaje central`). El catch existe solo para que la excepción no escape del `AsyncRelayCommand` — mismo patrón que `UsuariosAdminViewModel`.

### Auditoría

`AccionAuditada` (`src/StockApp.Domain/Enums/AccionAuditada.cs`) es append-only — el bloque actual termina en `ModificacionPermisosUsuario = 51`. Bloque nuevo:

```
// ── Documentos administrativos (append-only a partir de 52) ──────────────
AltaDocumentoAdministrativo = 52,
CambioEstadoDocumento       = 53,
ReaperturaDocumento         = 54,
AnulacionDocumento          = 55,
AltaNotaDocumento           = 56,
AltaAdjuntoDocumento        = 57,
BajaAdjuntoDocumento        = 58,
```

`CambioEstadoDocumento` cubre tanto `IniciarProcesoAsync` como `FinalizarAsync` — un solo valor semántico ("cambió de estado"), igual que `CambioEstadoTarea` no distingue tomar de terminar. `AnulacionDocumento` y `ReaperturaDocumento` son valores propios porque, a diferencia de las transiciones normales, llevan motivo y exigen `documentos.administrar` — vale la pena poder filtrarlas en el log de auditoría sin ambigüedad.

## Pruebas

TDD por capas, un archivo por capa, mismo orden que Tareas y Permisos:

- **Domain**: la máquina de estados completa (cada transición válida e inválida, incluida la identidad), `EsActivo`/`EsCerrado` para los cuatro estados, y que reabrir limpie `FechaCierre`.
- **Application**: permiso por método (los 7 de `documentos.gestionar` y los 2 de `documentos.administrar`), motivo obligatorio en `AnularAsync`/`ReabrirAsync` (vacío y en blanco), generación de evento automático en cada cambio de estado y en cada alta/baja de adjunto, auditoría por acción, y que `AgregarAsync` de adjuntos rechace sobre un documento cerrado (D11a).
- **Infrastructure**: repositorio contra PostgreSQL real (Testcontainers), y **el índice único: el duplicado tiene que explotar** — alta de `(Expediente, 2026, "0087")` dos veces en paralelo, uno gana y el otro recibe `ReglaDeNegocioException`.
- **Api**: matriz 401/403 por endpoint, 409 real del duplicado (no simulado — contra Postgres real), policies correctas por acción, multipart para adjuntos.
- **ApiClient**: mapeo Wire↔dominio, mapeo de errores (403, 409).
- **Presentation**: filtros por solapa, carga perezosa del Historial (que `CargarAsync()` inicial NO dispare `CargarHistorialAsync()`), gating de botones por `PuedeTransicionarA`, y el nuevo `PedirTextoAsync` cancelable.
- **UiTests**: carga por `DataContextChanged`, cambio de solapa dispara la carga perezosa.

Estimación: 18 a 20 tareas de implementación, del mismo tamaño que las del módulo de permisos (2026-08-10).

## Riesgos

- **El límite de 10 MB heredado de Finanzas (D12) es el punto más probable de queja temprana.** Un expediente escaneado de varias páginas puede superarlo con facilidad. La corrección es una constante, pero está compartida con Finanzas — subirla para Documentos sube el límite para ambos módulos salvo que se decida separar la constante en ese momento.
- **La ausencia de responsable reasignable (D2) es una limitación de producto, no técnica.** Si el cliente pide después "quién tiene el expediente ahora", no alcanza con extender el modelo actual: hace falta un campo nuevo y una decisión de negocio sobre reasignación (¿libre, o solo Admin? ¿genera evento?).
- **`PedirTextoAsync` es infraestructura de UI nueva**, sin precedente en el proyecto (ni siquiera `GastoService.AnularAsync`, que también anula sin pedir motivo hoy). Es la primera vez que un módulo exige capturar texto libre antes de una acción — conviene revisar si el diálogo debe validar "no vacío" en el cliente además del servidor, para no rebotar con 409/400 después de que el usuario ya cerró el modal.
- **Es el segundo módulo del sistema con una máquina de estados con reapertura** (el primero conceptualmente parecido, Tareas, no tiene salida desde sus estados terminales). Si la validación de la reapertura se filtra hacia la UI o hacia el endpoint en vez de vivir en `DocumentoAdministrativo.CambiarEstado`, un estado "cerrado" deja de significar lo mismo en todas las pantallas.
