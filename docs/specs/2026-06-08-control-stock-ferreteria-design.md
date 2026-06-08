# Control de Stock — Ferretería

**Spec de diseño** · 2026-06-08 · v2

> **Cambios v2:** se agrega control de usuarios (autenticación + roles) y auditoría
> (trazabilidad de movimientos y cambios sensibles).

---

## 1. Contexto y objetivo

Aplicación de escritorio para llevar el control de stock de una ferretería: catálogo
de productos, registro de movimientos (entradas/salidas), proveedores y reportes.
El objetivo es reemplazar el control manual (planillas) por un sistema que mantenga
el stock siempre exacto y permita valorizar la mercadería y consultar el historial.

**Despliegue:** una sola PC, sin concurrencia ni acceso por red. Aunque corre en una
máquina, la app soporta **varios usuarios** (p. ej. dueño y empleados): cada uno entra
con su cuenta, para controlar el acceso y poder rastrear quién hizo cada cosa.

---

## 2. Stack técnico

| Capa | Tecnología | Motivo |
|------|-----------|--------|
| UI | **Avalonia** + MVVM | Desktop moderno, multiplataforma, separación vista/lógica real |
| Acceso a datos | **EF Core** | Migraciones, LINQ, change tracking; mínimo boilerplate |
| Base de datos | **SQLite** (archivo local) | Transaccional (ACID), cero configuración, robusto para single-user |
| Hashing de contraseñas | **BCrypt** (alternativa: Argon2) | Estándar probado; nunca se guardan contraseñas en texto plano |
| Lenguaje | C# / .NET | — |
| Arquitectura | Por capas pragmático | Domain / Application / Infrastructure / Presentation |

---

## 3. Alcance

### Dentro de v1
- **Autenticación**: login con usuario y contraseña al iniciar la app.
- **Gestión de usuarios** (solo Admin): ABM de usuarios y asignación de rol.
- **Roles y permisos**: Admin y Operador (ver §7).
- ABM de **productos** (catálogo).
- ABM de **categorías**.
- ABM de **proveedores**.
- ABM de **unidades de medida** (tabla editable).
- Registro de **movimientos de stock** (entradas y salidas) con actualización
  transaccional del saldo y registro del usuario que lo hizo.
- **Auditoría** de cambios sensibles (precios, altas/bajas de productos, gestión de usuarios).
- **Búsqueda** de productos por código interno (SKU), código de barras o nombre.
- **Reportes** con exportación a CSV (solo Admin).
- Función de **recálculo de stock** desde los movimientos (red de seguridad).

### Fuera de v1 (futuro)
- Alertas de stock bajo (el modelo ya prevé `StockMinimo`).
- IVA / facturación.
- Multi-usuario concurrente / acceso por red.
- Exportación a PDF.
- Lectura por escáner de código de barras (hardware).
- Bloqueo de cuenta por intentos fallidos / política de expiración de contraseñas.

---

## 4. Modelo de datos

Cantidades de stock y de movimientos son **decimales** (`decimal`), porque la
ferretería vende fraccionado (metros de cable, kilos de tornillos, litros de pintura)
además de por unidad. Precios también `decimal`.

### Usuario
| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | int | PK |
| `NombreUsuario` | string | **único, obligatorio** (login) |
| `NombreCompleto` | string? | opcional, para mostrar |
| `HashContrasena` | string | hash + sal (BCrypt). **Nunca** texto plano |
| `Rol` | enum `RolUsuario` | `Admin` / `Operador` |
| `Activo` | bool | baja lógica (no se borran usuarios con historial) |
| `FechaAlta` | DateTime | — |
| `UltimoAcceso` | DateTime? | se actualiza en cada login |

### Producto
| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | int | PK |
| `Codigo` | string | SKU interno, **único, obligatorio** |
| `CodigoBarras` | string? | EAN, opcional, único si está presente |
| `Nombre` | string | obligatorio |
| `Descripcion` | string? | opcional |
| `CategoriaId` | int? | FK → Categoria |
| `ProveedorId` | int? | FK → Proveedor |
| `UnidadMedidaId` | int | FK → UnidadMedida |
| `PrecioCosto` | decimal | precio de compra |
| `PrecioVenta` | decimal | precio de venta |
| `StockActual` | decimal | saldo denormalizado (ver §6) |
| `StockMinimo` | decimal | previsto para alertas futuras; default 0 |
| `Activo` | bool | baja lógica |
| `FechaAlta` | DateTime | — |

### Categoria
| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | int | PK |
| `Nombre` | string | obligatorio, único |

### Proveedor
| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | int | PK |
| `Nombre` | string | obligatorio |
| `Telefono` | string? | opcional |
| `Email` | string? | opcional |
| `Direccion` | string? | opcional |
| `Notas` | string? | opcional |

### UnidadMedida
| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | int | PK |
| `Nombre` | string | ej: Unidad, Metro, Kilo, Litro, Caja |
| `Abreviatura` | string | ej: u, m, kg, l |

### MovimientoStock
| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | int | PK |
| `ProductoId` | int | FK → Producto |
| `UsuarioId` | int | FK → Usuario (quién lo registró) |
| `Tipo` | enum `TipoMovimiento` | `Entrada` / `Salida` |
| `Cantidad` | decimal | siempre positiva; el `Tipo` define el signo |
| `PrecioUnitario` | decimal | precio del momento (costo en entrada, venta en salida) |
| `Fecha` | DateTime | — |
| `Motivo` | enum `MotivoMovimiento` | Compra / Venta / Ajuste / Merma |
| `Comentario` | string? | opcional |

### LogAuditoria
Registra cambios sensibles (no los movimientos de stock, que ya tienen su `UsuarioId`).

| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | int | PK |
| `UsuarioId` | int | FK → Usuario (quién hizo el cambio) |
| `Fecha` | DateTime | — |
| `Accion` | enum `AccionAuditada` | ver enum abajo |
| `Entidad` | string | entidad afectada, ej: "Producto", "Usuario" |
| `EntidadId` | int | id del registro afectado |
| `Detalle` | string | descripción legible, ej: "PrecioVenta 100,00 → 120,00" |

### Enums
- `RolUsuario`: `Admin`, `Operador`
- `TipoMovimiento`: `Entrada`, `Salida`
- `MotivoMovimiento`: `Compra`, `Venta`, `Ajuste`, `Merma`
- `AccionAuditada`: `CambioPrecio`, `AltaProducto`, `BajaProducto`, `AltaUsuario`, `BajaUsuario`, `CambioRol`, `CambioContrasena`

> `UnidadMedida` es **tabla editable** (no enum) para poder agregar unidades sin
> recompilar. Los demás son enums: valores fijos con lógica asociada.

---

## 5. Seguridad

- **Contraseñas**: se almacena únicamente el **hash con sal** (BCrypt). En ningún momento
  se guarda ni se loguea la contraseña en texto plano. Quien abra el archivo SQLite no
  puede leer ninguna contraseña.
- **Login obligatorio** al iniciar la app. La sesión vive en memoria mientras la app
  está abierta; se puede **cerrar sesión / cambiar de usuario** sin cerrar el programa.
- **Autorización**: los permisos (§7) se verifican en la capa **Application** (los
  servicios validan el rol antes de ejecutar). La UI además oculta/deshabilita lo que el
  rol no puede hacer, pero la verificación real vive en Application (la UI no es la
  frontera de seguridad).
- **Primer arranque**: como no puede haber sistema sin ningún Admin, en el primer inicio
  la app pide **crear el usuario Admin inicial** (asistente). No se usa una contraseña por
  defecto conocida.
- **Baja de usuarios**: lógica (`Activo = false`), nunca física, para no romper el
  historial de movimientos y auditoría que referencia al usuario.

---

## 6. Regla de negocio clave: integridad del stock

`StockActual` se guarda **denormalizado** en `Producto` y se actualiza **dentro de la
misma transacción** que inserta el `MovimientoStock`. Los movimientos son el historial
auditable; el campo es el saldo de lectura rápida.

```
BEGIN TRANSACTION
  INSERT INTO MovimientoStock (..., UsuarioId)
  UPDATE Producto
     SET StockActual = StockActual ± Cantidad   -- + si Entrada, − si Salida
   WHERE Id = @ProductoId
COMMIT   -- o ROLLBACK: nunca queda a medias (SQLite es ACID)
```

**Invariante:** `StockActual` siempre es igual a `Σ(entradas) − Σ(salidas)` de ese producto.

**Validaciones:**
- `Cantidad > 0` en todo movimiento.
- Una `Salida` no puede dejar `StockActual` negativo (configurable: bloquear o advertir).
- `Codigo` (SKU) único; `CodigoBarras` único cuando no es nulo.
- `NombreUsuario` único.

**Red de seguridad:** función *Recalcular stock* que rearma `StockActual` de todos los
productos sumando sus movimientos, por si se sospecha un descuadre.

---

## 7. Roles y permisos

| Acción | Admin | Operador |
|--------|:---:|:---:|
| Gestión de **usuarios** | ✅ | ❌ |
| Ver **reportes** | ✅ | ❌ |
| Catálogo: productos / categorías / unidades | ✅ | ✅ |
| **Precios** (costo y venta) | ✅ | ✅ |
| Proveedores | ✅ | ✅ |
| Registrar movimientos de stock | ✅ | ✅ |
| Recalcular stock | ✅ | ✅ |

El Operador maneja toda la operatoria (catálogo, precios, proveedores, movimientos),
pero no accede a la gestión de usuarios ni a los reportes globales. El Admin puede todo.

---

## 8. Reportes (v1)

Solo accesibles por **Admin**. Todos exportables a **CSV**:

1. **Valorización de stock** — valor total a costo y a venta (`StockActual × precio`),
   por producto y total general.
2. **Stock actual por categoría** — listado agrupado.
3. **Historial de movimientos por producto** — filtrable por rango de fechas; incluye
   el usuario que registró cada movimiento.
4. **Productos más movidos** — ranking por cantidad de movimientos / volumen en un período.
5. **Auditoría** — log de cambios sensibles, filtrable por usuario y rango de fechas.

---

## 9. Funcionalidades de UI (pantallas)

- **Login**: usuario + contraseña al iniciar.
- **Productos**: grilla con búsqueda (SKU / barras / nombre), alta/edición/baja lógica.
- **Movimientos**: registrar entrada o salida sobre un producto; ver historial.
- **Proveedores**: ABM.
- **Categorías**: ABM.
- **Unidades de medida**: ABM.
- **Usuarios** (solo Admin): ABM de usuarios, asignación de rol, reseteo de contraseña.
- **Reportes** (solo Admin): selección de reporte + filtros + exportar CSV.

> Las pantallas y acciones no permitidas para el rol actual se ocultan o deshabilitan.

---

## 10. Decisiones pendientes / a refinar en implementación

- Política ante salida que deja stock negativo: **bloquear** vs **advertir**. *(Sugerido: advertir, pero permitir, con confirmación.)*
- Ubicación del archivo SQLite (carpeta de datos del usuario vs junto al ejecutable).
- Datos semilla iniciales (unidades de medida por defecto).
- ¿El Admin puede ver/editar precios siempre, o también querés que ciertos cambios de precio requieran confirmación extra? *(Por ahora: edición directa, auditada.)*

---

## 11. Distribución, actualización y licenciamiento

### 11.1. Instalador (Velopack)

- El proyecto de presentación (Avalonia) se empaqueta como **self-contained**, con el runtime .NET 10 incluido: la PC de la ferretería no necesita tener nada instalado previamente.
- `vpk pack` genera el instalador para **Windows** (`Setup.exe`) y **Linux** (`AppImage`).
- El instalador **no toca** la base de datos ni ningún dato del usuario.

### 11.2. Actualizador automático in-app (Velopack)

- Al arrancar, la app consulta el feed de actualizaciones desde una **URL propia**, con **fallback** a un repo de GitHub público si el dominio falla.
- **Implementación del fallback:** un `IUpdateSource` personalizado que encadena `[HttpSource(dominio propio), GithubSource(repo público)]`; intenta el primario y, si falla por excepción o timeout, cae al secundario. Velopack no trae fallback nativo entre fuentes, por eso se implementa esta clase (sobreescribiendo `GetReleaseFeed` y `DownloadReleaseEntry` de la interfaz `IUpdateSource`).
- El publish sube los mismos assets a **ambos orígenes** (dominio + GitHub release, vía `vpk upload`). Ambos orígenes deben tener exactamente las mismas versiones y canal para que el fallback sea transparente.
- **Updates diferenciales (delta):** el cliente baja solo lo que cambió.
- **Nota de seguridad:** el repo de GitHub es **público** y contiene solo releases (no código fuente). No se embebe ningún token. El control no es esconder el binario sino el licenciamiento (§11.4).

### 11.3. UX del actualizador por severidad

Cada release declara un metadato `severity` en el feed: `normal` | `important` | `critical`.
La app lee ese nivel y se comporta así:

| Severidad | Comportamiento | Si no puede descargar |
|-----------|---------------|----------------------|
| `normal` | Banner discreto; la actualización se aplica al próximo reinicio voluntario | Reintenta en silencio |
| `important` | Cartel modal llamativo al arrancar; posponible, pero insiste en cada arranque | Reintenta en cada arranque |
| `critical` | Cartel rojo prominente que **bloquea** el uso hasta actualizar | Entra en **MODO DEGRADADO**: la app sigue operable (la ferretería debe poder vender), pero muestra un banner rojo permanente no-cerrable "Actualización crítica pendiente"; reintenta en cada arranque |

El texto de cada cartel sale de las **release notes** (markdown) de esa versión.

### 11.4. Licenciamiento (control de uso)

- **Objetivo:** controlar *quién puede usar la app* (modelo de venta única), no esconder el binario.
- **Dos capas complementarias:**
  - **Licencia** = "¿esta instalación está autorizada?" (nivel instalación).
  - **Usuarios + Roles** (§7) = "¿quién sos dentro?" (nivel persona).
  La autenticación sola no controla el uso; la licencia sola no controla quién opera.
- **Licencia offline firmada** con criptografía asimétrica: el desarrollador firma con su clave **privada** (nunca sale de su poder); la app trae embebida la clave **pública** (no es secreta) y valida la firma 100% local, sin internet.
- **Perpetua, sin expiración** (venta única, sin suscripción).
- **Atada a la máquina** (machine fingerprint) para evitar copiar la misma licencia a varias PCs. Fingerprint cross-platform: Windows → `MachineGuid` del registro; Linux → `/etc/machine-id` (abstraído por plataforma).
- **Flujo de reemisión ante cambio de PC:** la app muestra su "código de máquina"; el cliente se lo envía al desarrollador; un **generador de licencias** (herramienta CLI de uso interno, con la clave privada, que *nunca* se distribuye con la app) emite una licencia nueva atada a la nueva máquina.
- **Sin licencia válida:** la app muestra una pantalla de bloqueo que exhibe el código de máquina para poder solicitar la licencia.
- **Honestidad técnica:** ningún esquema de licencia en una app desktop es inquebrantable; es disuasión proporcional al riesgo (B2B, ferreterías).

### 11.5. Estrategia de backup de la BD SQLite

Dos disparadores, una sola política de limpieza:

- **Backup pre-migración:** antes de aplicar migraciones de esquema (`Database.Migrate()`), para rollback inmediato si la migración falla.
- **Backup periódico:** cada 12 horas copia el `.db` a `backups/` con timestamp. **Trigger híbrido** (la app es desktop, no un servicio 24/7): al arrancar compara el timestamp persistido del último backup y, si pasaron ≥12 h (incluso con la app cerrada toda la noche), hace uno; además un timer dispara cada 12 h mientras la app corre.
- **Retención unificada:** se eliminan *todos* los backups (periódicos y pre-migración) de más de 7 días.
- **Salvaguarda 1:** la limpieza *siempre* conserva al menos el backup más reciente, aunque tenga más de 7 días (evita quedar en cero si la app no se abre por mucho tiempo).
- **Salvaguarda 2:** si un backup falla (disco lleno, permisos), se loguea y **no rompe la app**.

### 11.6. Migraciones de esquema de la BD

- La BD vive en el **directorio de datos del usuario** (`%LOCALAPPDATA%\StockApp\` en Windows, `~/.local/share/StockApp/` en Linux), nunca dentro de la carpeta de la app, porque Velopack reemplaza esa carpeta entera en cada update y se perderían los datos.
- Al arrancar, la app aplica las migraciones pendientes con `Database.Migrate()` (forward-only, versionadas en el repo), siempre **después** del backup pre-migración (§11.5).

### 11.7. Componentes nuevos en la solución

- **Generador de licencias** (CLI de uso interno, nunca distribuido con la app).
- **Validador de licencia + cálculo de machine fingerprint** (en la app, abstraído por OS).
- **Custom `IUpdateSource`** con fallback dominio → GitHub.
- **Servicio de backup** (pre-migración + periódico con retención).
- **Servicio de migración** con backup previo integrado.

### 11.8. Testing

| Componente | Tipo de test | Qué cubre |
|------------|-------------|-----------|
| Migración + backup | xUnit (BD temporal) | Esquema viejo → migrar → datos intactos + backup creado + limpieza respeta 7 días y conserva el más reciente |
| Validación de licencia | xUnit | Licencia válida / inválida / firma adulterada / máquina distinta |
| Backup periódico | xUnit | Lógica "pasaron ≥12 h" y retención |
| Packaging Velopack | Validación manual por OS | No unit-testeable |
