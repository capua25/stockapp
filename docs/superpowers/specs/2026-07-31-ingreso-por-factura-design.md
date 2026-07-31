# Ingreso de stock por factura

Fecha: 2026-07-31
Estado: aprobado, pendiente de plan de implementación

## Problema

Hoy, cargar una factura de compra con varios artículos obliga a repetir el ciclo completo por cada artículo: registrar la entrada en `EntradaRegistroViewModel`, responder "¿asociás factura?", y navegar a `GastoFormViewModel`. El dato final queda correcto, pero el flujo es tan tedioso que en la práctica se abandona.

El modelo de datos ya soporta el caso: `Gasto` es la cabecera de la factura (proveedor, número, monto, condición de pago, adjuntos) y `MovimientoStock.GastoId` es la relación 1..N. El problema es de flujo, no de modelo.

## Decisiones

1. **Operación atómica.** Una sola operación crea el `Gasto`, los N `MovimientoStock` asociados, aplica los N deltas de `StockActual`, aplica los cambios de precio de costo aceptados y escribe los `LogAuditoria`. Todo o nada.
2. **Cero entidades nuevas, cero migraciones.** Se reusan `Gasto` y `MovimientoStock.GastoId`. No se crea una entidad `Compra`/`LineaCompra`: duplicaría lo existente.
3. **Total de factura a mano, con aviso.** El `MontoTotal` del `Gasto` se carga tal como figura en la factura. La pantalla muestra la suma de renglones y la diferencia en todo momento, pero no bloquea el guardado. Motivo: IVA discriminado, flete y descuentos hacen que la suma de renglones casi nunca coincida con el total; forzar la igualdad empuja al operario a inventar renglones fantasma.
4. **Actualización selectiva del precio de costo.** Al confirmar, se listan solo los productos cuyo `PrecioCosto` difiere del precio del renglón, con un tilde por producto. Se aplican únicamente los tildados, dentro de la misma transacción, vía la lógica equivalente a `ProductoService.CambiarPrecioAsync`.
5. **Sin permisos nuevos.** El endpoint exige `Permisos.RegistrarMovimientos` y el servicio verifica además `Permisos.RegistrarGastos`. El rol `Operador` ya tiene ambos, más `catalogo.productos` para el alta en línea y el cambio de precio (verificado en `AuthorizationService.cs:19-29`).
6. **Anulación por asiento inverso.** El libro de movimientos es append-only por diseño; `MovimientoStock` no tiene campo de anulación y no existe ningún método para revertir un movimiento. Anular un lote genera, por cada entrada, una salida espejo (misma cantidad, mismo precio, `Motivo = Ajuste`, comentario con el número de factura) y anula el `Gasto`, todo en una transacción.
7. **La anulación rechaza si no hay stock.** Si parte de la mercadería ya se consumió, la salida espejo no puede aplicarse: la operación falla entera e informa qué artículos no tienen stock suficiente. Nunca se generan saldos negativos silenciosos.
8. **Fuente de financiamiento y rubro se eligen a mano en cada carga.** No se preseleccionan ni se configura un default: el municipio imputa a partidas distintas según la compra y la elección debe ser consciente.

## Alcance

Incluido en la primera versión:

- Carga de cabecera de factura + N renglones de artículo en una sola pantalla.
- Alta de producto en línea, sin perder los renglones ya cargados.
- Adjuntar el PDF de la factura.
- Anulación del lote completo.

Fuera de alcance:

- Buscador por código de barras con salto automático de renglón. Se evaluará después de ver la pantalla en uso.
- Recepción parcial de una factura (recibir hoy 5 de 10 unidades).
- Órdenes de compra previas a la factura.

## Diseño técnico

### Application

`IIngresoPorFacturaService` / `IngresoPorFacturaService` en `src/StockApp.Application/Movimientos/`.

`RegistrarAsync(IngresoPorFacturaDto dto)`:

1. Verifica `Permisos.RegistrarMovimientos` y `Permisos.RegistrarGastos`; si algún renglón trae producto nuevo o `ActualizarPrecioCosto = true`, verifica también `Permisos.GestionarProductos`.
2. Valida: al menos un renglón; `Cantidad > 0` y `PrecioUnitario >= 0` en cada uno; `MontoTotal > 0`; proveedor, fuente de financiamiento y rubro existentes y activos; productos referenciados existentes y activos.
3. Si el mismo producto aparece en más de un renglón, se aceptan como renglones independientes (una factura puede repetir un artículo en dos líneas con precios distintos).
4. Delega la escritura al repositorio.

`AnularLoteAsync(int gastoId)`: valida que el gasto exista, esté activo, no tenga pagos activos y tenga movimientos asociados; delega la reversa al repositorio.

### Infrastructure

Método nuevo en `IMovimientoStockRepository` / `MovimientoStockRepository`:

- `RegistrarIngresoPorFacturaAtomicoAsync(IngresoPorFacturaArgs args)`: abre una única transacción con `BeginTransactionAsync`, inserta el `Gasto`, inserta los productos nuevos, inserta los N `MovimientoStock` con su `GastoId`, aplica los deltas de `StockActual`, aplica los cambios de `PrecioCosto` aceptados, inserta los `LogAuditoria`, commitea. Cualquier fallo hace rollback completo.
- `AnularIngresoPorFacturaAtomicoAsync(int gastoId)`: en una transacción, verifica stock suficiente para cada salida espejo, las inserta, descuenta `StockActual`, marca `Gasto.Activo = false` y audita. Si algún producto no tiene stock suficiente, hace rollback y devuelve el detalle de los faltantes.

`RegistrarMovimientoAtomicoAsync` queda intacto: la pantalla de entrada suelta sigue funcionando igual.

### Api

- `POST /movimientos/ingreso-factura` — `.RequireAuthorization(Permisos.RegistrarMovimientos)`. Request: cabecera del gasto (`proveedorId`, `numeroFactura?`, `numeroOrden?`, `fecha`, `detalle`, `destino?`, `montoTotal`, `fuenteFinanciamientoId`, `rubroGastoId`, `lineaPoaId?`, `condicionPago`, `fechaVencimiento?`) más `lineas[]`, donde cada línea tiene `productoId?` **o** `productoNuevo?` (`codigo`, `nombre`, `categoriaId`, `unidadMedidaId`, `precioVenta`), más `cantidad`, `precioUnitario` y `actualizarPrecioCosto`. Respuesta: `gastoId`, `movimientoIds[]`, `sumaRenglones`, `diferenciaConTotal`.
- `POST /movimientos/ingreso-factura/{gastoId:int}/anular` — misma autorización. Respuesta 409 con el detalle de artículos sin stock si no puede revertirse.
- El adjunto se sube en una segunda llamada al endpoint ya existente `POST /finanzas/gastos/{id:int}/adjuntos`. No se crea un endpoint combinado.

Las fechas del request se normalizan a UTC en el borde JSON, igual que el resto de la API.

### ApiClient y Presentation

- `IngresoPorFacturaApiClient` en `src/StockApp.ApiClient/`.
- `IngresoPorFacturaViewModel` y `IngresoPorFacturaView.axaml` en `src/StockApp.Presentation/`: cabecera arriba, grilla editable de renglones abajo, y un pie fijo con suma de renglones, total de factura y diferencia resaltada. Diálogo modal de alta rápida de producto que preserva los renglones cargados. Paso de confirmación previo al guardado que lista solo los productos con cambio de precio de costo, con un tilde por producto.
- La vista engancha `DataContextChanged` para cargar sus datos, siguiendo la convención del proyecto.

## Pruebas

TDD por capas, siguiendo el patrón de Proveedores:

- **Application**: atomicidad ante fallo en el renglón N, validaciones de renglones y cabecera, gating de permisos, aplicación selectiva de precios, anulación rechazada por stock insuficiente.
- **Infrastructure**: rollback real contra PostgreSQL, deltas de stock correctos, salidas espejo de la anulación.
- **Api**: matriz 401 / 403 / 400 / 409.
- **ApiClient**: serialización del request y manejo de errores.
- **Presentation**: cálculo de suma y diferencia, alta en línea sin pérdida de renglones, lista de cambios de precio.

Estimación: ~14 archivos de producción y ~6 de test, sin migración de base de datos.

## Riesgos

- La transacción del lote es más larga que la de un movimiento suelto: con facturas de muchos renglones aumenta la ventana de contención sobre las filas de `Producto`. Se mitiga manteniendo la transacción acotada a la escritura, sin validaciones de red ni I/O de archivos dentro de ella.
- El adjunto en segunda llamada implica que un fallo al subir el PDF deja la factura creada sin adjunto. Es recuperable desde la pantalla de Finanzas y es el mismo comportamiento que ya tiene el alta de gastos.
