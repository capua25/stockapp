# Fase 3b — Cliente HTTP: migración del desktop a la API

**Fecha:** 2026-07-09
**Estado:** Diseño aprobado
**Precedente:** Fase 3a (`2026-07-09-fase-3a-servidor-listo-clientes-design.md`) — DEBE estar completa antes de ejecutar esta fase.

## Objetivo

El desktop Avalonia deja de acceder a Postgres y pasa a consumir la API REST — API-only, sin modo dual. Multi-puesto real: N desktops → 1 API → 1 Postgres. El servidor corre en una máquina de la LAN.

## Decisión estructural (enfoque elegido)

Clientes HTTP que implementan las MISMAS interfaces de `StockApp.Application` que los ViewModels ya consumen. Los ~22 ViewModels no se tocan. Descartados: interfaces cliente nuevas con refactor de VMs (costo sin beneficio) y servicios de Application en el cliente con repos HTTP (duplicaría auditoría y autorización).

## Arquitectura

### Proyecto nuevo `src/StockApp.ApiClient`
Referencia SOLO a `StockApp.Application` y `StockApp.Domain`. Contiene:
- ~10 `*ApiClient`, uno por interfaz: `IAuthService`, `IPrimerArranqueService`, `IUsuarioService`, `IProductoService`, `ICategoriaService`, `IProveedorService`, `IUnidadMedidaService`, `IMovimientoStockService`, `IReporteStockService`, `IAuditoriaQueryService`. Cada método traduce a su endpoint con los mismos DTOs.
- `ApiSession : ICurrentSession` (singleton): poblada desde el `LoginResponse` enriquecido (D8 de 3a) tras `LoginAsync`; `CerrarSesion` limpia token y datos. Fuente de la UI (`EsAdmin`, nombre en Shell). La autorización real es del servidor.
- `AuthTokenHandler : DelegatingHandler`: adjunta `Authorization: Bearer` a cada request; ante **401** dispara evento "sesión vencida" → el Shell navega al login con aviso. Un solo lugar.
- Traducción de errores HTTP → excepciones de dominio de 3a: 404→`EntidadNoEncontradaException`, 409→`ReglaDeNegocioException` (con el `detail` del ProblemDetails como mensaje), 400→`ArgumentException`, 403→`UnauthorizedAccessException`. Conexión fallida (`HttpRequestException`/timeout)→`ServidorNoDisponibleException` (nueva, mensaje claro al usuario). Los ViewModels ya muestran esos mensajes — no cambian.

### Cambios en Presentation (los únicos)
- `App.axaml.cs`: reemplazo de registros — salen repos de Infrastructure, servicios de Application, `AppDbContext`, `DatabaseInitializer`, connection string; entran ApiClients + `HttpClient` (BaseAddress de `Api:BaseUrl`) + `ApiSession` + `AuthTokenHandler`. `IAuthorizationService` local queda solo si alguna UI lo consulta (verificar en el plan; ideal: eliminarlo del cliente).
- `StockApp.Presentation.csproj`: SE ELIMINA la referencia a `StockApp.Infrastructure`. Criterio de éxito duro de la fase.
- `appsettings.json`: `Api:BaseUrl` (default `http://localhost:5000`); sale `ConnectionStrings`.
- `ICsvExporter`/`CsvExporter`: la exportación CSV queda local. `CsvExporter` vive en Infrastructure → se muda (a Presentation o proyecto compartido mínimo; lo define el plan). 
- Flujo de primer arranque: `PrimerArranqueViewModel` sigue igual — su `IPrimerArranqueService` ahora es el ApiClient contra los endpoints bootstrap de 3a. El paso "crear segundo admin" ya funciona: login por API + `AltaUsuarioAsync`.

## Manejo de errores (cliente)

| Situación | Comportamiento |
|---|---|
| 401 en cualquier llamada | Evento sesión vencida → login con aviso "Sesión vencida, ingresá de nuevo" |
| 403 | `UnauthorizedAccessException` — los VMs ya la manejan |
| 404 / 409 | Excepciones de dominio con el mensaje del servidor — los VMs ya las muestran |
| Servidor caído / timeout | `ServidorNoDisponibleException` con mensaje accionable |
| Login con servidor caído | El login muestra el error de conexión, permite reintentar |

## Testing

- **Integración real de ApiClients**: suite nueva que levanta la API completa con el `ApiFactory` existente (Testcontainers) y ejercita CADA método de CADA ApiClient contra ella — el `HttpClient` del `WebApplicationFactory` se inyecta en los clients. Cubre contrato, auth (token real), traducción de errores (401/403/404/409) y el flujo bootstrap completo.
- Tests de Presentation existentes: intactos (mockean las mismas interfaces).
- **Verificación orgánica final** (convención del proyecto): API real corriendo + desktop real → primer arranque contra server virgen, login, alta de producto, movimiento con stock, reporte, auditoría. Y el criterio duro: `StockApp.Presentation.csproj` sin referencia a Infrastructure compilando y andando.

## Fuera de alcance

- Instalador/deploy del servidor en la LAN del municipio (operativo, no código — se documenta el arranque en el README).
- Offline/cache local; refresh tokens; TLS interno (LAN; se documenta como supuesto).
- Reintentos automáticos/resiliencia avanzada (Polly) — YAGNI hasta que la realidad lo pida.
