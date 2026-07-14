# Diseño: Caché de reportes con invalidación por versión global

Fecha: 2026-07-14
Estado: Aprobado

## Problema

El desktop de StockApp es multi-terminal contra una única API REST en la LAN. Cada vez que una terminal navega a un reporte (valorización, stock-por-categoría, más-movidos, historial-por-producto), el `ReporteStockApiClient` hace un `GET` que recomputa la consulta contra PostgreSQL. No hay ningún caché (ni en cliente ni en servidor). Con varias terminales navegando los mismos reportes repetidamente, la base de datos recomputa una y otra vez las mismas consultas de agregación sobre todos los productos/movimientos, aunque los datos no hayan cambiado.

El objetivo es **liberar carga de la base de datos**, no mejorar la UX percibida: evitar el trabajo redundante de recalcular reportes idénticos entre navegaciones y entre terminales.

## Decisión

Caché **en el servidor** (`IMemoryCache` in-process) con **invalidación por versión global**. Al haber una sola instancia de API para toda la LAN, un caché in-process se comparte entre todas las terminales y la invalidación es centralizada: todas las mutaciones pasan por el mismo proceso.

Se descartó el caché en cliente: no hay canal de push/websocket, así que una terminal no se enteraría de las mutaciones de otra y mostraría datos stale — inaceptable en un sistema de inventario.

## Alcance

Se cachean los **4 reportes de stock**:
- `GET /reportes/valorizacion`
- `GET /reportes/stock-por-categoria`
- `GET /reportes/mas-movidos` (parámetros: fechaDesde, fechaHasta, topN)
- `GET /reportes/historial-producto/{productoId}` (parámetros: fechaDesde, fechaHasta)

La **auditoría** (`GET /auditoria`) queda **fuera del caché**: su tabla crece con casi cualquier acción del sistema (incluidos logins) que no toca stock, así que no encaja en la versión global de datos de stock. Sigue leyendo fresh siempre.

## Arquitectura

### Componente 1 — `IVersionReportes` (Application)

Un singleton thread-safe que expone la versión actual del conjunto de datos de reportes:

- `long Actual { get; }` — la versión vigente.
- `void Invalidar()` — incrementa la versión (`Interlocked.Increment`).

Vive en `src/StockApp.Application/Reportes/`. Su implementación `VersionReportes` es trivial (un `long` con `Interlocked`) y **no depende de ningún caché** — es solo un contador. Esto mantiene la capa Application libre de dependencias de infraestructura de caché.

### Componente 2 — Decorator de lectura `ReporteStockServiceCacheado` (Infrastructure)

Un decorator que implementa `IReporteStockService` y envuelve al `ReporteStockService` real. Inyecta `IMemoryCache` y `IVersionReportes`. Es el **único** componente que toca `IMemoryCache`.

Para cada uno de los 4 métodos arma una clave que incluye la versión actual y los parámetros del reporte, y usa `GetOrCreate`:

- `valorizacion@v{N}`
- `stock-categoria@v{N}`
- `mas-movidos:{fechaDesde}:{fechaHasta}:{topN}@v{N}`
- `historial:{productoId}:{fechaDesde}:{fechaHasta}@v{N}`

Si la clave está en caché, devuelve el valor sin tocar la base. Si no, delega en el `ReporteStockService` real, guarda el resultado (con el TTL de respaldo) y lo devuelve.

Al incrementarse la versión, todas las claves con el `@v{N}` anterior quedan huérfanas y se limpian solas por TTL o por presión de tamaño del `IMemoryCache`. No hace falta enumerar ni remover entradas: basta con cambiar el número de versión que forma parte de la clave.

### Componente 3 — Invalidación en los services de mutación (Application)

Los services que mutan datos que los reportes leen inyectan `IVersionReportes` y llaman `Invalidar()` **una sola vez, después del commit exitoso** de su transacción:

- `MovimientoStockService`: `RegistrarAsync`, `RecalcularStockAsync`.
- Service de productos: alta, modificar, baja (lógica), cambiar precio.
- Service de categorías: alta, modificar, baja (lógica).

Ninguno necesita saber qué reporte afecta: solo bumpea la versión global.

## Detalles críticos

### Orden: invalidar DESPUÉS del commit

`Invalidar()` se llama **después** de que la transacción commiteó, nunca antes. Si se invalidara antes del commit, se abriría una carrera: otra terminal podría leer en ese instante, recalcular con el dato viejo aún sin commitear, y guardarlo bajo la versión nueva — envenenando el caché con datos viejos. Invalidando después del commit, cualquier lectura posterior ve la versión nueva y recalcula con el dato ya visible.

Orden garantizado: **mutar → commit → `Invalidar()`**.

### TTL de respaldo: 1 hora

Cada entrada del caché lleva además un TTL absoluto de **1 hora**. La invalidación por versión es la defensa primaria y es inmediata; el TTL es la red de seguridad: si en el futuro alguien agrega un camino de mutación nuevo y olvida llamar a `Invalidar()`, el caché se cura solo en un plazo máximo de una hora en vez de quedar stale para siempre. En operación normal el TTL nunca se alcanza porque la invalidación por versión ocurre antes.

## Registro (Program.cs)

- `AddMemoryCache()`.
- `IVersionReportes` como **singleton** (una sola versión compartida por todo el proceso y todas las terminales).
- El decorator `ReporteStockServiceCacheado` registrado como `IReporteStockService`, envolviendo al `ReporteStockService` concreto mediante una factory en el contenedor (patrón decorator). El concreto se registra por su tipo para poder inyectarlo dentro del decorator.

## Testing

- **`VersionReportes`**: `Invalidar()` incrementa `Actual`; comportamiento correcto ante concurrencia (`Interlocked`).
- **Decorator (unit, con un inner mock que cuenta invocaciones)**: la 1ª lectura calcula y cachea; la 2ª lectura con la misma versión se sirve de caché sin invocar al inner; tras `Invalidar()`, la siguiente lectura recalcula. Verificar que la clave discrimina por parámetros: dos rangos de fecha distintos en más-movidos producen dos entradas; distinto `productoId` en historial produce dos entradas.
- **Invalidación por service de mutación (unit, mock de `IVersionReportes`)**: tras una mutación exitosa se llamó `Invalidar()` exactamente una vez.
- **Integración de API (test de oro)**: `GET valorización` → `POST movimiento` → `GET valorización` refleja el cambio. Prueba end-to-end que la invalidación funciona y que no queda dato stale entre terminales.

## Consecuencias

- La base de datos deja de recomputar reportes idénticos entre navegaciones y terminales; solo recalcula tras una mutación real o cuando expira el TTL de 1 hora.
- Coherencia total entre terminales: cualquier mutación invalida el caché de forma inmediata para todas.
- La capa Application no adquiere dependencia de `IMemoryCache` (solo del contador `IVersionReportes`); la única pieza acoplada al caché es el decorator en Infrastructure.
- La auditoría no se cachea; si en el futuro se quisiera, requeriría su propio versionado independiente.
