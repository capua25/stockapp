# Canal de alerta ante fallo de backup

Fecha: 2026-08-09
Estado: diseño aprobado, pendiente de plan de implementación

## Problema

El subsistema de backup de StockApp está verificado end-to-end (restore real comprobado, 10/10 pasos verdes el 2026-08-09), pero es **mudo ante sus propios fallos**.

Hoy, cuando `pg_dump` falla, `ServicioBackup` (`src/StockApp.Application/Backups/ServicioBackup.cs:88-104`) solo hace `_logger.LogWarning(...)` y persiste una fila `CorridaBackup` con `Resultado = Fallida`. No hay mail, ni webhook, ni notificación de ningún tipo. No existe **ningún** mecanismo de notificación en ninguna capa del proyecto: cero resultados para `SmtpClient`, `MailKit`, `IEmailSender`, `webhook`; cero registros de `AddHttpClient`.

Consecuencia: en una instalación desatendida, un backup nocturno roto no se descubre hasta que alguien entra manualmente a la pantalla de Mantenimiento. Se puede pasar semanas sin backup con la convicción de tenerlo.

### El modo de falla peligroso

Hay dos escenarios distintos, y el segundo es el grave:

1. **Fallo activo**: `pg_dump` devuelve error. Queda una fila `Fallida` en la base. Es detectable.
2. **Silencio**: el proceso de la API murió, el contenedor no levantó, el server se reinició sin arrancar el servicio. `BackupProgramadoService` es un `BackgroundService` que corre **dentro del proceso de la API**, así que si la API no está, no hay backup, no hay error y **no hay fila**. El silencio es indistinguible de "todo salió bien".

El escenario 2 no se puede resolver desde adentro: el componente que debería avisar es el mismo que se murió. Requiere un testigo externo.

## Restricción de contexto

**No hay acceso al servidor después de la instalación.** Toda configuración debe vivir en la base de datos o en el cliente; nunca en archivos de configuración del servidor. Esto descarta configurar el canal vía `appsettings.json`.

Además, el destinatario de la alerta (el responsable técnico) **no usa la aplicación a diario** — la usan los operativos. Una alerta in-app no alcanza: la notificación tiene que salir a buscarlo.

## Decisiones tomadas

| Decisión | Elección | Motivo |
|---|---|---|
| Alcance | Fallo activo **y** silencio, con un solo mecanismo | Un webhook pingueado en éxito y en fallo resuelve ambos |
| Canal | Webhook HTTP genérico, convención healthchecks.io | Una sola URL configurable, sin secretos SMTP, sin dependencias nuevas |
| Config | Entidad tipada de fila única en Postgres | Tipada y validable; la tabla genérica clave-valor pierde tipos y FKs |
| Destino final | Telegram vía healthchecks.io | Gratis; se configura en la web del servicio, sin tocar código ni redeployar |

### Por qué un testigo externo y no Telegram directo

Pegarle directo a la Bot API de Telegram desde la aplicación también es gratis y evita un tercero, pero **pierde el dead man's switch**: si la aplicación muere, no le pega a nada y se vuelve al silencio.

Healthchecks.io no es un intermediario superfluo — es el único componente que sigue vivo cuando el servidor no lo está. La aplicación no sabe qué es Telegram, no guarda un token de bot y no depende de Telegram: solo postea a una URL. Cambiar el destino (mail, Discord, SMS) se hace en la web del servicio, sin recompilar ni redeployar.

## Arquitectura

```
ServicioBackup ─┐
DisparadorBackupManual ─┼─▶ INotificadorAlertas ─▶ NotificadorWebhook ─POST─▶ healthchecks.io ─▶ Telegram
BackupProgramadoService ─┘         (Application)      (Infrastructure)              │
                                                                                    └─ ¿dejó de llegar
                                        ConfiguracionAlertas (Postgres) ◀───────       el ping? ─▶ Telegram
```

### 1. Dominio y persistencia

Entidad `ConfiguracionAlertas` de **fila única** (`Id` siempre 1):

| Columna | Tipo | Notas |
|---|---|---|
| `Id` | `int` | PK, siempre 1 |
| `UrlWebhook` | `text?` | Nullable; sin configurar por defecto |
| `Habilitado` | `bool` | `false` por defecto |
| `ActualizadoEn` | `timestamptz` | UTC |
| `ActualizadoPorUsuarioId` | `int?` | FK `Restrict` a `Usuarios` |

La FK usa `Restrict` y navegación nullable, siguiendo el patrón establecido en el trabajo de integridad referencial (`CorridaBackup.UsuarioId`).

La migración EF crea la tabla **y siembra la fila** `Id = 1` con `Habilitado = false` y `UrlWebhook = null`, de modo que el código nunca tenga que contemplar su ausencia.

Repositorio `IConfiguracionAlertasRepository` con dos métodos: `ObtenerAsync()` y `GuardarAsync(cfg)`.

### 2. El notificador

- `INotificadorAlertas` — contrato en `StockApp.Application`.
- `NotificadorWebhook` — implementación en `StockApp.Infrastructure/Notificaciones/`, con `HttpClient` registrado vía `AddHttpClient` y timeout de 10 segundos.

Convención de pings (healthchecks.io):

- Corrida **exitosa** → `POST {url}` — actúa como heartbeat. Si dejan de llegar, el servicio externo alerta por su cuenta.
- Corrida **fallida** → `POST {url}/fail`, con `MotivoFallo` en el body, truncado a 2000 caracteres.

Tres reglas invariables:

1. **No-op silencioso** si `Habilitado == false` o `UrlWebhook` está vacía. Config vacía no es un error.
2. **Nunca propaga excepciones.** `try/catch` interno más `LogWarning`. Una caída de red no puede hacer fracasar un backup que salió bien: el notificador es un observador, no un participante.
3. **Lee la configuración en cada llamada.** El backup corre cada 12 horas, no hay costo de performance relevante, y permite cambiar la URL sin reiniciar el servidor — que es precisamente lo que no se puede hacer bajo la restricción de acceso.

### 3. Puntos de enganche

Existen **tres** caminos que terminan en un fallo de backup, no uno:

| Camino | Ubicación | Comportamiento actual |
|---|---|---|
| `pg_dump` falla, o falla el `File.Move` | `ServicioBackup.cs:65-102` | log + persiste fila `Fallida` |
| Fallo inesperado en disparo manual | `DisparadorBackupManual.PersistirFallaAsync` (`src/StockApp.Api/Backups/DisparadorBackupManual.cs:132-159`) | log + persiste fila `Fallida` |
| Última resistencia del scheduler | `BackupProgramadoService.cs:125-133` | solo `LogError`, **no persiste fila** |

La notificación va con **llamadas explícitas en los tres puntos**. Un decorador sobre `ICorridaBackupRepository` sería más compacto, pero esconde el efecto secundario y **no cubre el tercer caso**, que ni siquiera persiste. Se prefieren tres invocaciones visibles a una abstracción que miente.

`ServicioBackup` además notifica el caso exitoso (el heartbeat).

**Cuidado con `MotivoFallo`**: es una columna de doble propósito. También se usa para marcar corridas **exitosas** reconciliadas desde disco huérfano (`ServicioBackup.MarcaFilaReconciliada`, `ServicioBackup.cs:150-151`). Disparar la alerta con la condición `MotivoFallo != null` produciría falsos positivos. La condición correcta es `Resultado == ResultadoBackup.Fallida`.

### 4. Endpoints

Los tres bajo el permiso `Permisos.GestionarDiagnostico` (`"diagnostico.gestionar"`), el mismo que ya protege las cuatro rutas de `/backups`.

- `GET /configuracion/alertas` — devuelve `UrlWebhook`, `Habilitado`, `ActualizadoEn`.
- `PUT /configuracion/alertas` — guarda. Valida que la URL sea absoluta y **https**.
- `POST /configuracion/alertas/probar` — envía un ping real y devuelve el status code obtenido.

El endpoint de prueba no es opcional: es el núcleo de la funcionalidad. Un canal de alerta que nunca se probó no es un canal, es una creencia — la URL mal escrita se descubriría recién el día del fallo, es decir, nunca.

**Nota de seguridad (SSRF)**: `/probar` hace que el servidor emita una petición a una URL provista por el usuario. Mitigaciones: solo administrador autenticado, https obligatorio, timeout de 10 segundos, y se devuelve únicamente el status code — nunca el cuerpo de la respuesta.

### 5. Interfaz de usuario

Sección "Alertas" dentro de `MantenimientoView` (`src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml`), que ya existe y ya está protegida por el permiso correcto.

Controles: campo de texto para la URL, checkbox de habilitado, botón Guardar, botón Probar.

Patrón del repositorio: interfaz `IConfiguracionAlertasService` en Application, implementación `ConfiguracionAlertasApiClient` en `StockApp.ApiClient`, y ViewModel con CommunityToolkit.Mvvm (`[ObservableProperty]` / `[RelayCommand]`).

## Plan de pruebas

Desarrollo dirigido por pruebas, por capas, siguiendo el patrón del repositorio (fakes manuales en Application/Api, Testcontainers en Infrastructure).

**Application** — `ServicioBackupTests` con un fake de `INotificadorAlertas`:
- Notifica éxito tras una corrida exitosa.
- Notifica fallo tras una corrida fallida.
- **Un notificador que lanza excepción no rompe el backup.** Es la prueba más importante del conjunto.

**Infrastructure** — `NotificadorWebhookTests` con `HttpMessageHandler` falso:
- Postea a `{url}` en éxito y a `{url}/fail` en fallo.
- No-op cuando está deshabilitado o la URL está vacía.
- No propaga excepciones ante error de red o timeout.
- Trunca `MotivoFallo` a 2000 caracteres.

**Infrastructure** — `ConfiguracionAlertasRepositoryTests` contra Postgres real (Testcontainers, colección `"Postgres"`): la fila sembrada existe tras la migración; guardar y releer conserva los valores.

**Api** — matriz 401 / 403 / 200 sobre los tres endpoints, más rechazo de URL inválida (no absoluta, o http en vez de https).

**Presentation** — pruebas del ViewModel: carga, guardado y prueba de conexión.

## Fuera de alcance

- SMTP, MailKit o cualquier envío de correo desde la aplicación.
- Plantillas de cuerpo de mensaje configurables.
- Reintentos con backoff: si se pierde un ping, healthchecks.io lo tolera y el siguiente backup reintenta solo.
- Múltiples destinos de notificación.
- Cifrado de la URL en base de datos. Es accesible solo por administrador, y esa base ya almacena hashes de contraseña; ante un compromiso de la base, la URL de healthchecks es el menor de los problemas.

## Limitación conocida

Este diseño cubre "`pg_dump` falló" y "el sistema dejó de reportar". **No** cubre "el backup se generó correctamente pero el archivo está corrupto". Detectar eso requiere un restore automático periódico contra una base descartable, que es un proyecto aparte.

Mitigación actual: el restore se verificó manualmente el 2026-08-09 con `pg_restore` real, conteos de filas comparados y descarga byte-idéntica verificada por sha256.
