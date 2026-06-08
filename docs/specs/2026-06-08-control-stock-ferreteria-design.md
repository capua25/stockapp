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
