# Módulo de tareas para operarios

Fecha: 2026-08-01
Estado: aprobado, pendiente de plan de implementación

## Problema

El cliente pidió "un módulo de tareas donde los operarios puedan ver las tareas que hay en curso, pendientes y terminadas". Hoy no existe nada parecido en el dominio: no hay entidad de tarea, ni estados, ni asignación. Lo más cercano son los lotes del importador de planillas, que no se le parecen.

Ese pedido describe una pantalla, no un problema. Lo que hay que resolver es cómo se ordena y se hace visible el trabajo pendiente de un equipo chico, sin que el registro se vuelva una carga mayor que el trabajo mismo.

## Decisiones

1. **Módulo independiente, sin vínculos con otras entidades.** Las tareas no se adjudican a gastos, productos ni expedientes. El cliente anticipa que a futuro querrá conectarlas con expedientes o finanzas; esa conexión se resolverá con una columna nueva cuando se sepa cómo es. **No se modela una FK polimórfica** (`TipoEntidadRelacionada` + `EntidadRelacionadaId`): la base no puede validarla, rompe los JOIN y obliga a mantener la integridad referencial a mano. Una tabla simple hoy es la mejor preparación para el futuro.
2. **Dos tablas nuevas, con migración.** `Tarea` y `NotaTarea`. A diferencia del ingreso por factura, acá no hay nada que reusar.
3. **Lista común: la toma quien puede.** Las tareas se crean sin responsable. Un usuario la toma cuando la arranca y ahí queda registrado quién.
4. **Cuatro estados**: `Pendiente`, `EnCurso`, `Terminada`, `Cancelada`. `Cancelada` existe para que el trabajo que se descarta tenga una salida honesta: sin ella, los usuarios marcan como terminado lo que nunca hicieron, y se pierde la distinción entre trabajo hecho y trabajo desestimado.
5. **Transiciones válidas**: `Pendiente → EnCurso` (tomar), `EnCurso → Pendiente` (soltar, devuelve a la lista común y limpia el responsable), `EnCurso → Terminada`, `Pendiente → Cancelada`, `EnCurso → Cancelada`. `Terminada` y `Cancelada` son **terminales**. Cualquier otra transición se rechaza en el dominio, no en la pantalla.
6. **Sin baja lógica.** `Tarea` no tiene campo `Activo`. En el resto del sistema `Activo = false` significa "esto ya no existe para vos"; acá la cancelación es un estado del ciclo de vida y la tarea cancelada tiene que seguir siendo visible en el historial. Dos formas de estar muerto serían una de más.
7. **Permisos**: `tareas.gestionar` (crear, tomar, soltar, terminar, comentar) para `Admin` y `Operador`; `tareas.administrar` (cancelar y cambiar prioridad) solo para `Admin`. Son las dos acciones que deciden sobre trabajo que otro cargó, por eso comparten permiso.
8. **La prioridad nace siempre en `Media`**, para todos los usuarios, incluido un `Admin` que cree la tarea. Cambiarla es una acción posterior y separada, reservada a `Admin`. Motivo: si quien carga la tarea puede declararla urgente, en pocas semanas todas son urgentes y el campo deja de significar algo.
9. **Cada cambio de prioridad genera una nota automática** (`Prioridad: Media → Alta`). Si se concentra la decisión en `Admin`, esa decisión tiene que ser visible.
10. **Todas las tareas son visibles para todos**, sin importar quién las haya tomado. `GET /tareas` no filtra por usuario y no existe una vista de "mis tareas". La grilla muestra la columna "Tomada por" con el nombre del responsable actual.
11. **Cualquiera puede terminar o soltar una tarea ajena, y queda registrado.** La tarea guarda dos pares de trazabilidad: `TomadaPor` + `FechaInicio` y `CerradaPor` + `FechaFin`. Toda acción sobre una tarea ajena genera nota automática (`García terminó una tarea tomada por Juan`). Así el dato de quién hizo el trabajo se conserva sin trabar a nadie cuando la persona que la tomó no está.
12. **Las notas son append-only**: se agregan, no se editan ni se borran. Son el registro de lo que pasó.
13. **La API expone las transiciones como acciones explícitas**, no como un `PUT` genérico que reciba el estado nuevo. Cada endpoint tiene una sola precondición y la máquina de estados vive en el dominio, testeable sin levantar la API.
14. **La prioridad no se puede cambiar una vez que la tarea está `Terminada` o `Cancelada`.** Priorizar sirve para ordenar trabajo pendiente; una tarea cerrada no ordena nada, y permitirlo solo dejaría reescribir el pasado. La validación vive en `Tarea.CambiarPrioridad`, no en `TareaService`: se deriva de la misma tabla de transiciones que ya identifica los estados terminales, sin una lista nueva que mantener en paralelo. `TareaFormViewModel` deja de ofrecer el panel de cambio de prioridad sobre una tarea cerrada, para no mostrar una acción que solo va a devolver 409.

## Alcance

Incluido:

- Alta de tareas con título, descripción y fecha límite opcional.
- Listado único agrupado por estado, con las canceladas detrás de un filtro.
- Tomar, soltar, terminar y cancelar, con las transiciones validadas en el dominio.
- Cambio de prioridad por `Admin`.
- Hilo de notas por tarea.
- Resaltado de tareas vencidas (fecha límite pasada y estado no terminal).

Fuera de alcance:

- Notificaciones o avisos de vencimiento. La fecha límite es, en esta versión, solo un resaltado en la grilla. Si el cliente quiere que el sistema le avise, es un proyecto aparte (correo, panel de inicio, o ambos).
- Vinculación con expedientes, gastos o productos.
- Adjuntos en las tareas.
- Tareas recurrentes o plantillas.
- Reasignación explícita de un responsable a otro.

## Diseño técnico

### Domain

`Tarea` (`src/StockApp.Domain/Entities/Tarea.cs`): `Id`, `Titulo`, `Descripcion`, `Estado` (`EstadoTarea`), `Prioridad` (`PrioridadTarea`), `FechaLimite?`, `CreadaPorUsuarioId` + `FechaCreacion`, `TomadaPorUsuarioId?` + `FechaInicio?`, `CerradaPorUsuarioId?` + `FechaFin?`, y la colección de `Notas`.

`NotaTarea` (`src/StockApp.Domain/Entities/NotaTarea.cs`): `Id`, `TareaId`, `UsuarioId`, `Fecha`, `Texto`, `EsAutomatica`.

Enums nuevos en `src/StockApp.Domain/Enums/`: `EstadoTarea` (`Pendiente`, `EnCurso`, `Terminada`, `Cancelada`) y `PrioridadTarea` (`Baja`, `Media`, `Alta`).

La validación de transiciones vive en el dominio, en un método de `Tarea` que recibe el estado destino y rechaza las combinaciones inválidas con `ReglaDeNegocioException`.

### Application

`ITareaService` / `TareaService` en `src/StockApp.Application/Tareas/`, con `CrearAsync`, `ListarAsync(filtro)`, `TomarAsync`, `SoltarAsync`, `TerminarAsync`, `CancelarAsync`, `CambiarPrioridadAsync` y `AgregarNotaAsync`. Cada método verifica autorización como primera línea, con `tareas.gestionar` salvo `CancelarAsync` y `CambiarPrioridadAsync`, que exigen `tareas.administrar`.

Las notas automáticas (cambio de prioridad, acción sobre tarea ajena) las genera el servicio, no el llamador.

`ITareaRepository` en `src/StockApp.Application/Interfaces/`, implementado en `src/StockApp.Infrastructure/Repositories/TareaRepository.cs`.

### Api

Grupo `/tareas` en `src/StockApp.Api/Endpoints/TareasEndpoints.cs`:

- `POST /tareas` — alta. `GET /tareas` — listado con filtro opcional por estado.
- `POST /tareas/{id:int}/tomar`, `/soltar`, `/terminar` — exigen `tareas.gestionar`.
- `POST /tareas/{id:int}/cancelar`, `/prioridad` — exigen `tareas.administrar`.
- `POST /tareas/{id:int}/notas` — exige `tareas.gestionar`.

Las transiciones inválidas devuelven 409; la tarea inexistente, 404.

### ApiClient y Presentation

`TareaApiClient` en `src/StockApp.ApiClient/`.

`TareaListViewModel` y `TareaFormViewModel` en `src/StockApp.Presentation/ViewModels/Tareas/`, con sus vistas en `src/StockApp.Presentation/Views/Tareas/`. Una sola pantalla con las tareas agrupadas por estado y las canceladas detrás de un filtro. Los botones de acción se muestran según el estado de la fila y el rol del usuario. El panel de detalle muestra la descripción y el hilo de notas, con el campo para agregar una nueva. Las tareas vencidas se resaltan reusando el converter de valores negativos que ya existe en `src/StockApp.Presentation/Converters/`.

La vista engancha `DataContextChanged` para cargar sus datos, siguiendo la convención del proyecto.

Alta del ítem en el menú de `ShellMainViewModel` y `ShellMainView.axaml`.

### Auditoría

Valores nuevos al final del enum append-only `AccionAuditada`: alta, cambio de estado, cambio de prioridad y cancelación de tarea.

## Pruebas

El grueso de la cobertura va al dominio, donde la máquina de estados se prueba entera sin base de datos ni HTTP: cada transición válida y, sobre todo, cada transición inválida.

- **Domain**: todas las transiciones válidas e inválidas, y que `Terminada` y `Cancelada` sean terminales.
- **Application**: gating de permisos por método, generación de notas automáticas, prioridad inicial en `Media`, y que un `Operador` no pueda cancelar ni repriorizar.
- **Infrastructure**: persistencia contra PostgreSQL real, incluido el orden del hilo de notas.
- **Api**: matriz 401 / 403 / 400 / 404 / 409, con foco en el 403 de `Operador` sobre cancelar y prioridad, y en el 409 de transición inválida.
- **ApiClient**: serialización y mapeo de errores.
- **Presentation**: agrupación por estado, visibilidad de botones según rol y estado, y resaltado de vencidas.

Estimación: ~20 archivos de producción, ~6 de test y una migración de base de datos.

## Riesgos

- Es el primer módulo del sistema con una máquina de estados explícita. Si la validación de transiciones se filtra hacia la capa de presentación o hacia los endpoints, se vuelve inconsistente entre pantallas. Debe vivir en el dominio y estar cubierta por tests que no dependan de la infraestructura.
- La fecha límite sin avisos puede generar la expectativa de que el sistema notifica. Conviene aclararlo con el cliente antes de la entrega.
- El permiso `tareas.administrar` es el primero que distingue a `Admin` de `Operador` dentro de un mismo módulo funcional. Si más adelante aparece un rol intermedio, este es el primer lugar donde se va a notar.
