# Módulo de documentos administrativos

Fecha: 2026-08-11
Estado: aprobado, pendiente de plan de implementación

## Problema

El cliente pidió, textual: registrar y hacer seguimiento de documentos administrativos —expedientes, oficios y suministros—, con como mínimo fecha de emisión/ingreso, funcionario responsable que registra, tipo de documento, breve descripción y estado del trámite (Pendiente / En proceso / Finalizado). El objetivo declarado es control interno: trazabilidad desde la emisión hasta la finalización, y poder identificar qué documentos quedaron pendientes en cada etapa.

Hoy no existe nada parecido en el dominio. Lo más cercano en forma —no en contenido— es el módulo de Tareas (2026-08-01): una entidad con estado, un responsable, y un hilo de eventos append-only. Ese parecido no es casualidad: **este módulo se construye copiando el patrón de Tareas capa por capa**, no inventando arquitectura nueva. Donde el pedido del cliente difiere de Tareas —numeración propia con año explícito, cuatro estados en vez de tres, reapertura, adjuntos— son decisiones nuevas, documentadas abajo con su motivo.

## Decisiones

**D1. Número propio obligatorio.** `Numero` es `string` (conserva ceros a la izquierda, ej. `"0087"`, tal como figura en el papel) y `Anio` es un campo `int` explícito, no derivado de `FechaEmision`. Índice único compuesto `(Tipo, Anio, Numero)` en la base.

Por qué `Anio` no se deriva de la fecha: un expediente que entra el 3 de enero puede pertenecer al ejercicio anterior. Si el año se derivara de la fecha, corregir una fecha mal tipeada cambiaría la identidad del documento (dos expedientes con el mismo número podrían colisionar o dejar de colisionar según un typo). El default del campo `Anio` en el alta es el año de `FechaEmision`, pero es corregible por separado.

"Corregible" necesita un método: nadie carga cientos de expedientes sin equivocarse de número, de año o de fecha alguna vez, y sin una vía de corrección la única salida sería anular el documento mal cargado y volver a registrarlo de cero — lo que ensucia el historial con un documento fantasma que nunca debió existir. Por eso el servicio expone `EditarAsync(int id, DatosEdicionDocumento datos)` (`documentos.gestionar`), que permite corregir `Numero`, `Anio`, `Tipo`, `FechaEmision` y `Descripcion` sobre un documento ya cargado. Reglas:

- Solo si el documento **`EsActivo`**. Un documento cerrado no se edita: se reabre (Admin + motivo, D8) o se deja como está — mismo argumento que D11(a) aplica acá: permitir editar un documento cerrado sin pasar por la reapertura le resta confiabilidad al historial.
- Si la edición cambia `Numero`, `Anio` o `Tipo`, se **revalida el índice único** `(Tipo, Anio, Numero)`: si el nuevo valor choca con otro documento existente, 409 con el mismo mensaje que usa el alta (ver "Api").
- Genera un **evento automático** en el historial detallando qué campos cambiaron (ej. "Se corrigió el número: 1234/2026 → 1235/2026"). Sin este evento, corregir un dato sería la única mutación del módulo sin rastro — rompería D5.

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

`EnProceso -> Pendiente` no es una transición huérfana: tiene su propio método de servicio, `VolverAPendienteAsync` (ver "Application"), análogo exacto de `TareaService.SoltarAsync`. Cubre el caso real de un trámite que se puso en proceso por error, o que vuelve a quedar a la espera de documentación que tiene que traer el interesado.

El dominio permite la transición de reapertura sin condiciones adicionales a las de la tabla; el dominio **no sabe de roles ni de motivos**. El corte "reabrir es solo Admin", "motivo obligatorio" y "solo se puede reabrir lo que está cerrado" vive en `DocumentoAdministrativoService`, que es donde vive la autorización en todo el proyecto — el dominio nunca consulta `RolUsuario` ni `IAuthorizationService`. Este último punto no es cosmético: `Pendiente -> EnProceso` ya es una transición válida por sí sola (la usa `IniciarProcesoAsync`), así que si `ReabrirAsync` se limitara a llamar `CambiarEstado(EnProceso)` sin guardas propias, invocarlo sobre un documento `Pendiente` **no lanzaría ninguna excepción** — el dominio lo dejaría pasar como si fuera un inicio de proceso más. Por eso `ReabrirAsync` valida `EsCerrado` explícitamente antes de tocar la entidad (ver D8 y "Application").

**D5. Historial append-only.** Entidad `EventoDocumento` (molde de `NotaTarea`): `Id`, `DocumentoId`, `Fecha`, `UsuarioId`, `EstadoAnterior` (`EstadoDocumento?`, nulo si es nota manual), `EstadoNuevo` (`EstadoDocumento?`, ídem), `Texto`, `EsAutomatico`. Nunca se edita ni se borra.

Cada cambio de estado genera un evento automático (`EsAutomatico = true`, con `EstadoAnterior`/`EstadoNuevo` completos); el funcionario puede sumar notas a mano (`EsAutomatico = false`, estados nulos). Es lo que responde "por dónde pasó el trámite y cuánto tardó en cada etapa" — sin esto, "trazabilidad" es una palabra vacía en el spec.

**D6. Tipo de documento: enum fijo.** `TipoDocumento { Expediente = 0, Oficio = 1, Suministro = 2 }`.

Se evaluó una tabla maestra configurable con ABM (como `Categoria` o `Proveedor`) y se descartó: cuesta 30-40% más de trabajo —tabla, repositorio, servicio, endpoints, ABM en el desktop, permiso nuevo, tests de todo eso— para evitar una línea de enum y un deploy. El enum se persiste como `int` y es append-only: agregar un tipo nuevo el día de mañana es una línea de código, no una migración de datos. Tampoco encierra: migrar a tabla maestra después queda acotado, con los valores existentes mapeables uno a uno (0→fila, 1→fila, 2→fila).

**D7. Permisos de dos niveles**, misma convención `<modulo>.<verbo>` que ya usa `Permisos.cs` (`tareas.gestionar`, `finanzas.ver`, etc.):

- `GestionarDocumentos = "documentos.gestionar"` — **configurable**: se agrega a `AuthorizationService.PermisosInicialesOperador` (la plantilla de arranque para Operadores nuevos) y queda disponible para que el Admin lo tilde o destilde por operador desde el panel de permisos (spec 2026-08-10).
- `AdministrarDocumentos = "documentos.administrar"` — **estructural**: se agrega a `AuthorizationService.PermisosEstructuralesAdmin`. Admin sí, Operador nunca, sin consultar la tabla `PermisoUsuario` ni la cache — mismo trato que `AdministrarTareas`.

**D8. Reapertura y anulación exigen motivo; `FechaCierre` la sella el servicio, no la entidad.** `ReabrirAsync(id, motivo)` y `AnularAsync(id, motivo)` — las dos acciones `documentos.administrar` que cierran o reabren un trámite — validan en el servicio que el motivo no venga vacío ni en blanco, y lanzan `ReglaDeNegocioException` si lo está: un motivo que se puede dejar vacío es un campo decorativo, no un control.

`ReabrirAsync` exige además que el documento esté **`EsCerrado`**: si está `Pendiente` o `EnProceso`, lanza `ReglaDeNegocioException` antes de tocar la entidad. La guarda es necesaria y no redundante con el dominio (ver D4): `Pendiente -> EnProceso` ya es una transición válida por otra vía (`IniciarProcesoAsync`), así que sin este chequeo explícito se podría generar una auditoría `ReaperturaDocumento` y un evento de "reapertura" sobre un documento que nunca estuvo cerrado, contaminando el historial con un relato falso.

`FinalizarAsync` y `AnularAsync` sellan `FechaCierre = DateTime.UtcNow` **en el servicio**, después de `documento.CambiarEstado(...)`; `ReabrirAsync` la pone en `null`, también en el servicio. `DocumentoAdministrativo.CambiarEstado` no toca fechas — mismo patrón exacto que `TareaService`: `Tarea.CambiarEstado` tampoco setea `FechaFin`, lo hacen a mano `TareaService.TerminarAsync` (`tarea.FechaFin = DateTime.UtcNow`) y `TareaService.CancelarAsync`, después de llamar a `CambiarEstado`. El dominio decide *si* la transición es válida; el servicio decide *qué otros campos* acompañan esa transición.

Las tres acciones quedan registradas dos veces: como evento automático en el historial del documento (con el motivo en `Texto` para Anular/Reabrir) y como entrada de `LogAuditoria` (`ReaperturaDocumento`/`AnulacionDocumento`).

**D9. UI con pestañas Activos / Historial**, `TabControl` — mismo control que ya usa el proyecto en otras pantallas con vistas alternativas. El Historial:

- Tiene filtros propios (número, año, tipo, estado), independientes de los de Activos.
- **Se carga perezoso**, recién al abrir la solapa. Si se cargara junto con Activos, cada consulta de tres expedientes pendientes arrastraría el archivo completo del año.
- **Exige año, y esa exigencia es validación de servicio, no solo un default de UI.** `ListarHistorialAsync` rechaza `Anio` nulo con `ArgumentException` (400 — filtro obligatorio ausente del request, no un choque contra el estado de un recurso; ver "Api" para el contraste explícito con el 409 del número duplicado) — no es que la pantalla precargue el año actual "por comodidad" y el usuario pueda borrarlo: si pudiera, el argumento de "no paginar" de abajo se cae, porque un Operador que limpia el filtro traería el archivo completo. La UI precarga el año actual como valor inicial del filtro; el servidor lo exige siempre, sin excepción para Admin.
- Se decidió **no paginar**: paginar cuesta cambios en las cinco capas (repositorio, servicio, endpoint, cliente HTTP, ViewModel) para un problema que el filtro por año obligatorio ya resuelve con una condición `WHERE`. Si algún año puntual empieza a traer miles de registros, ahí se agrega paginación con el dato real en la mano — no antes.

**D10. Adjuntos: entidad propia `AdjuntoDocumento`, no se reusa `Adjunto` de Finanzas.** El `Adjunto` de Finanzas (`src/StockApp.Domain/Entities/Adjunto.cs`) tiene dos FK reales —`GastoId`/`PagoGastoId`— con un CHECK `CK_Adjuntos_GastoOPago` en la base que impone el invariante XOR: es integridad referencial de verdad, no decorativa.

Las alternativas evaluadas y descartadas:

- (a) Agregar una tercera FK `DocumentoId` al `Adjunto` existente. Mete Documentos adentro de Finanzas y rompe la independencia del módulo — Finanzas no debería saber que Documentos existe.
- (b) Refactorizar a polimorfismo genérico (`EntidadTipo` + `EntidadId`). Pierde la FK real y el CHECK actual, y toca código de Finanzas que hoy tiene tests verdes sin necesidad.

En cambio, se **replica** el servicio (`AdjuntoService` tiene ~150 líneas) para un `AdjuntoDocumentoService` propio, y se **reusa tal cual, sin tocar una línea**: `AdjuntoValidador` (10 MB, PDF/JPG/PNG, `src/StockApp.Application/Finanzas/AdjuntoValidador.cs` — validación por **magic bytes**, no por extensión), `IServicioSeleccionArchivo` y `ServicioAperturaArchivo` (`src/StockApp.Presentation/Services/`), y el patrón de **tabla separada para los bytes**: metadatos en `AdjuntoDocumento`, contenido en `AdjuntoDocumentoContenido` (relación 1:1, `Id` compartido), igual que `Adjunto`/`AdjuntoContenido` — para que listar adjuntos nunca arrastre megabytes de la base.

**D11. Reglas de adjuntos:**

- (a) **Ni agregar ni quitar** un adjunto está permitido salvo que el documento esté **activo** (`EsActivo`) — la regla corta en ambos sentidos, no solo al agregar. Si el documento está cerrado y aparece un papel nuevo, o hace falta sacar uno mal cargado, hay que reabrirlo primero (Admin + motivo + rastro en el historial). El argumento aplica igual o más a remover evidencia que a sumarla: si se pudiera modificar un expediente cerrado —en cualquier dirección— sin dejar rastro, el historial deja de ser confiable como fuente de auditoría.
- (b) **Quitar un adjunto exige `documentos.administrar`**, no `gestionar` — a diferencia de agregar. Un adjunto acá es prueba documental de un trámite (la factura escaneada, la nota firmada), no la foto casual de un ticket: sacarlo es una decisión de mayor peso que subirlo.
- (c) Es **baja lógica** (`Activo = false` en `AdjuntoDocumento`), nunca borrado físico — mismo criterio que `Adjunto.Activo` en Finanzas.
- (d) Adjuntar y quitar generan **evento automático** en `EventoDocumento` (`EsAutomatico = true`, sin cambio de estado — `EstadoAnterior`/`EstadoNuevo` quedan nulos, igual que una nota manual pero marcada como automática).

**D12. Límite de tamaño: 10 MB**, heredado directamente de `AdjuntoValidador.TamanoMaximoBytes` por consistencia con Finanzas — no se define un límite propio para el módulo.

Riesgo anotado explícitamente: un expediente escaneado de 40 páginas puede irse a 15-20 MB, por encima del límite. Es el primer lugar donde este módulo va a chillar en producción. La corrección es subir una constante compartida con Finanzas (`AdjuntoValidador.TamanoMaximoBytes`), lo cual sube el límite para ambos módulos a la vez — no hay forma de subirlo solo para Documentos sin separar la constante, algo a tener en cuenta si el día que esto pase Finanzas todavía quiere quedarse en 10 MB.

## Alcance

Incluido:

- Alta de documentos con número, año, tipo, fecha de emisión/ingreso, descripción y funcionario registrante (automático).
- Edición de esos mismos datos sobre un documento activo, con revalidación del número único y evento automático (D1).
- Listado en dos pestañas: Activos (Pendiente/EnProceso) e Historial (Finalizado/Anulado), este último con año obligatorio, filtros propios y carga perezosa.
- Transiciones de estado validadas en el dominio: iniciar proceso, volver a pendiente, finalizar, anular, reabrir.
- Anulación y reapertura restringidas a Admin, ambas con motivo obligatorio; reabrir exige que el documento esté cerrado.
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

`DocumentoAdministrativo` (`src/StockApp.Domain/Entities/DocumentoAdministrativo.cs`): `Id`, `Numero` (`string`, requerido), `Anio` (`int`), `Tipo` (`TipoDocumento`), `FechaEmision` (`DateTime`), `Descripcion` (`string`, requerido), `Estado` (`EstadoDocumento`, default `Pendiente`), `RegistradoPorUsuarioId` (`int`) + nav `RegistradoPor`, `FechaRegistro` (`DateTime`), `FechaCierre` (`DateTime?`), `List<EventoDocumento> Eventos`.

`FechaCierre` **no se sella sola**: es una propiedad simple, sin lógica propia. Quien la sella y la limpia es `DocumentoAdministrativoService` (D8), exactamente como `Tarea.FechaFin` la sellan `TareaService.TerminarAsync`/`CancelarAsync` y no `Tarea.CambiarEstado`.

Propiedades derivadas `EsActivo` y `EsCerrado` (D4), y los métodos `CambiarEstado(EstadoDocumento destino)` / `PuedeTransicionarA(EstadoDocumento destino)`, mismo contrato que `Tarea.CambiarEstado`/`Tarea.PuedeTransicionarA` — incluida la característica de que **ninguno de los dos toca otros campos además del propio `Estado`**: `CambiarEstado` valida y muta el estado, nada más; las fechas y el resto de las consecuencias de la transición son responsabilidad del servicio que lo invoca.

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
| `EditarAsync(id, datos)` | `documentos.gestionar` |
| `ListarActivosAsync(filtro)` | `documentos.gestionar` |
| `ListarHistorialAsync(filtro)` — rechaza `Anio` nulo con `ArgumentException` (400) | `documentos.gestionar` |
| `ObtenerPorIdAsync(id)` | `documentos.gestionar` |
| `IniciarProcesoAsync(id)` | `documentos.gestionar` |
| `VolverAPendienteAsync(id)` | `documentos.gestionar` |
| `FinalizarAsync(id)` | `documentos.gestionar` |
| `AgregarNotaAsync(id, texto)` | `documentos.gestionar` |
| `AnularAsync(id, motivo)` | `documentos.administrar` |
| `ReabrirAsync(id, motivo)` | `documentos.administrar` |

Más los tres de `IAdjuntoDocumentoService` (ver más abajo): `AgregarAsync`/`ListarAsync`/`ObtenerContenidoAsync` con `documentos.gestionar`, `QuitarAsync` con `documentos.administrar` — la matriz completa queda **12 métodos bajo `documentos.gestionar`** (los 9 de arriba más los 3 de lectura/alta de adjuntos que no son `QuitarAsync`) **y 3 bajo `documentos.administrar`** (`AnularAsync`, `ReabrirAsync`, `QuitarAsync`).

Patrón fijo por método: `_auth.Verificar(_session, Permisos.X)` primero, después validar (motivo no vacío en `Anular`/`Reabrir`, `ReglaDeNegocioException`; `EsCerrado` en `Reabrir`, D8, `ReglaDeNegocioException`; `Anio` no nulo en `ListarHistorialAsync`, D9, **`ArgumentException`** — es el único 400 del módulo, el resto de las validaciones son reglas de negocio y devuelven 409; documento existe, `EntidadNoEncontradaException`), después **mutar vía la entidad** (`documento.CambiarEstado(...)`, nunca reimplementar la máquina de estados en el servicio), después sellar/limpiar `FechaCierre` a mano en `FinalizarAsync`/`AnularAsync`/`ReabrirAsync` (D8 — el dominio no toca fechas), después guardar, después auditar. Mismo orden que `TareaService`.

`VolverAPendienteAsync(id)` es el análogo exacto de `TareaService.SoltarAsync` (D4): vuelve el documento a `Pendiente` y genera evento automático. No lleva motivo — a diferencia de anular/reabrir, no es una decisión que necesite quedar explicada, es una corrección de rumbo dentro del flujo normal.

`EditarAsync(id, datos)` (D1) rechaza con `ReglaDeNegocioException` si el documento no está `EsActivo`. Si `datos` cambia `Numero`, `Anio` o `Tipo`, revalida el índice único antes de guardar — mismo mecanismo de traducción de `DbUpdateException` a 409 que el alta (ver "Api"). Genera evento automático detallando los campos modificados.

`record FiltroDocumentos(TipoDocumento? Tipo, int? Anio, string? Texto, EstadoDocumento? Estado)` en `Application`, compartido por `DocumentoAdministrativoService` y `DocumentoApiClient` — mismo criterio que los demás filtros del proyecto (ej. `FiltroMovimientos`).

`IDocumentoAdministrativoRepository` en `src/StockApp.Application/Interfaces/`, implementado en `src/StockApp.Infrastructure/Repositories/DocumentoAdministrativoRepository.cs`.

Adjuntos, en un servicio separado (mismo criterio que Finanzas: `AdjuntoService` es independiente de `GastoService`):

`IAdjuntoDocumentoService` / `AdjuntoDocumentoService` en `src/StockApp.Application/Documentos/`, con `AgregarAsync(documentoId, nombreArchivo, contenido)` (`documentos.gestionar`, rechaza con `ReglaDeNegocioException` si el documento no está `EsActivo` — D11a), `ListarAsync(documentoId)` (`documentos.gestionar`), `ObtenerContenidoAsync(adjuntoId)` (`documentos.gestionar`), `QuitarAsync(adjuntoId)` (`documentos.administrar`, **también rechaza si el documento dueño no está `EsActivo`** — D11a corregido: la regla de "solo sobre documento activo" corta igual al agregar y al quitar, no únicamente al agregar). `IAdjuntoDocumentoRepository` / `AdjuntoDocumentoRepository`, mismo split metadatos/contenido que `AdjuntoRepository`.

### Api

`DocumentosEndpoints.cs` en `src/StockApp.Api/Endpoints/`, minimal API, grupo `/documentos`, cada endpoint con su `.RequireAuthorization(Permisos.X)`:

- `GET /documentos/activos`, `GET /documentos/historial` (con querystring de `FiltroDocumentos`; `Anio` ausente devuelve **400** vía `ArgumentException` — D9), `GET /documentos/{id:int}` — `documentos.gestionar`.
- `POST /documentos` — alta, `documentos.gestionar`.
- `PUT /documentos/{id:int}` — edición (D1, `EditarAsync`), `documentos.gestionar`.
- `POST /documentos/{id:int}/iniciar`, `POST /documentos/{id:int}/volver-a-pendiente`, `POST /documentos/{id:int}/finalizar`, `POST /documentos/{id:int}/notas` — `documentos.gestionar`.
- `POST /documentos/{id:int}/anular`, `POST /documentos/{id:int}/reabrir` (body con `Motivo`) — `documentos.administrar`.
- `POST /documentos/{id:int}/adjuntos` (multipart, `IFormFile archivo` + `.DisableAntiforgery()`, mismo patrón que `AdjuntosEndpoints.cs`) — `documentos.gestionar`.
- `GET /documentos/{id:int}/adjuntos` — `documentos.gestionar`.
- `GET /documentos/adjuntos/{id:int}/contenido` (`Results.File(contenido.Contenido, contenido.ContentType, contenido.NombreArchivo)`) — `documentos.gestionar`.
- `DELETE /documentos/adjuntos/{id:int}` — `documentos.administrar`.

Transiciones inválidas devuelven 409 (vía `ReglaDeNegocioException`, mapeado genéricamente por `DomainExceptionHandler`); el documento inexistente, 404 (`EntidadNoEncontradaException`).

**Contraste explícito entre los dos códigos de error propios del módulo** (confirmado contra `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`, líneas 31 y 36): **400** es un request mal formado — falta un dato que el cliente tenía que mandar y no mandó (`Anio` ausente en `/documentos/historial`, `ArgumentException`); **409** es un conflicto contra el estado actual de los datos — algo que ya existe y choca con lo que se pide (el número de documento duplicado). No son intercambiables: si ambos devolvieran 409, quien depure la API en producción vería el mismo código para "mandaste mal el pedido" (arreglás el cliente) y para "ya existe ese expediente" (mostrás el mensaje y el usuario corrige el número) — dos problemas sin relación, con soluciones opuestas.

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

`DocumentoApiClient` en `src/StockApp.ApiClient/`, implementa `IDocumentoAdministrativoService` contra HTTP, con records Wire propios y mapeo de errores vía `ApiErrores` (403 → `UnauthorizedAccessException`, 409 → `ReglaDeNegocioException`, 400 → `ArgumentException` — ej. el `Anio` ausente de `ListarHistorialAsync`, D9 —, todos con el mensaje del servidor). `AdjuntoDocumentoApiClient` implementa `IAdjuntoDocumentoService`, mismo criterio que `AdjuntoApiClient`.

### Presentation

`DocumentoListViewModel` (`src/StockApp.Presentation/ViewModels/Documentos/`): dos colecciones (`Activos`, `Historial`), filtros por solapa, carga perezosa del Historial (dispara `CargarHistorialAsync()` solo cuando la solapa se selecciona, no en el `CargarAsync()` inicial).

`DocumentoFila`: aplana la entidad para la grilla y expone el gating de botones consultando `documento.PuedeTransicionarA(...)` — para que no exista una segunda copia de las reglas de transición en la pantalla. El análogo correcto no es `TareaFila.PuedeTomar`/`PuedeSoltar` (que solo miran la transición, porque `tareas.gestionar` alcanza para ambas): es `TareaFila.PuedeCancelar`, que además exige `_rol == RolUsuario.Admin`. Acá aplica igual o más, porque hay dos acciones (`documentos.administrar`) que el dominio permite transicionar pero que un Operador no puede ejecutar:

```csharp
public bool PuedeIniciar   => Documento.PuedeTransicionarA(EstadoDocumento.EnProceso);
public bool PuedeFinalizar => Documento.PuedeTransicionarA(EstadoDocumento.Finalizado);
public bool PuedeAnular    => _rol == RolUsuario.Admin && Documento.PuedeTransicionarA(EstadoDocumento.Anulado);
public bool PuedeReabrir   => _rol == RolUsuario.Admin && Documento.EsCerrado;
```

(`_rol`, igual que en `TareaFila`, se recibe en el constructor.) El chequeo de rol en `PuedeAnular`/`PuedeReabrir` no es opcional: el dominio no sabe de roles (D4), así que `PuedeTransicionarA(Anulado)` da `true` también para un Operador — el gating por rol tiene que vivir en la fila, no en la entidad. Sin el chequeo, el Operador vería esos botones habilitados y comería un 403 al hacer clic, exactamente la experiencia que el manejo central del 403 (`093fc7c`) vino a evitar en el resto del sistema.

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
EdicionDocumento            = 59,
```

`CambioEstadoDocumento` cubre `IniciarProcesoAsync`, `VolverAPendienteAsync` y `FinalizarAsync` — un solo valor semántico ("cambió de estado"), igual que `CambioEstadoTarea` no distingue tomar de terminar. `AnulacionDocumento` y `ReaperturaDocumento` son valores propios porque, a diferencia de las transiciones normales, llevan motivo y exigen `documentos.administrar` — vale la pena poder filtrarlas en el log de auditoría sin ambigüedad. `EdicionDocumento` (D1) es un valor propio y no reusa `CambioEstadoDocumento` porque no es un cambio de estado: es una corrección de datos sobre un documento que puede seguir en el mismo estado antes y después.

## Pruebas

TDD por capas, un archivo por capa, mismo orden que Tareas y Permisos:

- **Domain**: la máquina de estados completa (cada transición válida e inválida, incluida la identidad), `EsActivo`/`EsCerrado` para los cuatro estados, y que `CambiarEstado` no toque `FechaCierre` (queda en manos del servicio, D8).
- **Application**: permiso por método — los 12 de `documentos.gestionar` y los 3 de `documentos.administrar` (`AnularAsync`, `ReabrirAsync`, `QuitarAsync` de adjuntos); motivo obligatorio en `AnularAsync`/`ReabrirAsync` (vacío y en blanco); que `ReabrirAsync` rechace con `ReglaDeNegocioException` sobre un documento que no está `EsCerrado` (D4/D8 — incluido el caso puntual `Pendiente`, que el dominio por sí solo no rechazaría); que `FinalizarAsync`/`AnularAsync` sellen `FechaCierre` y `ReabrirAsync` la limpie; que `ListarHistorialAsync` rechace `Anio` nulo con `ArgumentException`, no `ReglaDeNegocioException` (D9 — es un request mal formado, no un conflicto de negocio); generación de evento automático en cada cambio de estado, en cada edición (D1) y en cada alta/baja de adjunto; auditoría por acción; que `AgregarAsync` **y** `QuitarAsync` de adjuntos rechacen sobre un documento cerrado (D11a); que `EditarAsync` revalide el índice único cuando cambia `Numero`/`Anio`/`Tipo` y rechace sobre un documento cerrado (D1).
- **Infrastructure**: repositorio contra PostgreSQL real (Testcontainers), y **el índice único: el duplicado tiene que explotar** — alta de `(Expediente, 2026, "0087")` dos veces en paralelo, uno gana y el otro recibe `ReglaDeNegocioException`; mismo test repetido para `EditarAsync` cuando la edición choca con otro documento existente.
- **Api**: matriz 401/403 por endpoint, 409 real del duplicado (no simulado — contra Postgres real), **400** de `Anio` ausente en `/documentos/historial` (distinto del 409 del duplicado — mismo test que verifica que no se confundan los dos códigos), policies correctas por acción, multipart para adjuntos.
- **ApiClient**: mapeo Wire↔dominio, mapeo de errores (403 → `UnauthorizedAccessException`, 409 → `ReglaDeNegocioException`, 400 → `ArgumentException` — mismo mapeo genérico ya establecido en `ApiErrores.CrearBadRequest`).
- **Presentation**: filtros por solapa, carga perezosa del Historial (que `CargarAsync()` inicial NO dispare `CargarHistorialAsync()`), gating de botones por `PuedeTransicionarA` combinado con rol (`PuedeAnular`/`PuedeReabrir` en falso para Operador aunque la transición sea válida — Importante 5), y el nuevo `PedirTextoAsync` cancelable.
- **UiTests**: carga por `DataContextChanged`, cambio de solapa dispara la carga perezosa.

Estimación: 20 a 22 tareas de implementación — dos más que la primera pasada, por los métodos `EditarAsync` y `VolverAPendienteAsync` (cada uno con su endpoint, cliente HTTP, UI y tests propios) sumados en el review de este spec. Tamaño comparable al del módulo de permisos (2026-08-10).

## Riesgos

- **El límite de 10 MB heredado de Finanzas (D12) es el punto más probable de queja temprana.** Un expediente escaneado de varias páginas puede superarlo con facilidad. La corrección es una constante, pero está compartida con Finanzas — subirla para Documentos sube el límite para ambos módulos salvo que se decida separar la constante en ese momento.
- **La ausencia de responsable reasignable (D2) es una limitación de producto, no técnica.** Si el cliente pide después "quién tiene el expediente ahora", no alcanza con extender el modelo actual: hace falta un campo nuevo y una decisión de negocio sobre reasignación (¿libre, o solo Admin? ¿genera evento?).
- **`PedirTextoAsync` es infraestructura de UI nueva**, sin precedente en el proyecto (ni siquiera `GastoService.AnularAsync`, que también anula sin pedir motivo hoy). Es la primera vez que un módulo exige capturar texto libre antes de una acción — conviene revisar si el diálogo debe validar "no vacío" en el cliente además del servidor, para no rebotar con 409/400 después de que el usuario ya cerró el modal.
- **Es el segundo módulo del sistema con una máquina de estados con reapertura** (el primero conceptualmente parecido, Tareas, no tiene salida desde sus estados terminales). Si la validación de la reapertura se filtra hacia la UI o hacia el endpoint en vez de vivir en `DocumentoAdministrativo.CambiarEstado`, un estado "cerrado" deja de significar lo mismo en todas las pantallas.
