using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Movimientos;

/// <summary>
/// Servicio de ingreso de stock por factura. Patrón: auth → validación → composición de
/// entidades → delegación al repo atómico.
/// </summary>
public class IngresoPorFacturaService : IIngresoPorFacturaService
{
    private readonly IMovimientoStockRepository        _movRepo;
    private readonly IGastoRepository                  _gastoRepo;
    private readonly IProveedorRepository              _proveedores;
    private readonly IFuenteFinanciamientoRepository    _fuentes;
    private readonly IRubroGastoRepository              _rubros;
    private readonly ILineaPoaRepository                _lineasPoa;
    private readonly IProductoRepository                _productos;
    private readonly IUnidadMedidaRepository            _unidades;
    private readonly ICurrentSession                    _session;
    private readonly IAuthorizationService              _auth;
    private readonly IVersionReportes                   _version;

    public IngresoPorFacturaService(
        IMovimientoStockRepository movRepo,
        IGastoRepository gastoRepo,
        IProveedorRepository proveedores,
        IFuenteFinanciamientoRepository fuentes,
        IRubroGastoRepository rubros,
        ILineaPoaRepository lineasPoa,
        IProductoRepository productos,
        IUnidadMedidaRepository unidades,
        ICurrentSession session,
        IAuthorizationService auth,
        IVersionReportes version)
    {
        _movRepo     = movRepo;
        _gastoRepo   = gastoRepo;
        _proveedores = proveedores;
        _fuentes     = fuentes;
        _rubros      = rubros;
        _lineasPoa   = lineasPoa;
        _productos   = productos;
        _unidades    = unidades;
        _session     = session;
        _auth        = auth;
        _version     = version;
    }

    public async Task<IngresoPorFacturaResultadoDto> RegistrarAsync(IngresoPorFacturaDto dto)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarMovimientos);
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var requierePermisoCatalogo = dto.Renglones.Any(r => r.ProductoNuevo is not null || r.ActualizarPrecioCosto);
        if (requierePermisoCatalogo)
            _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);

        if (dto.Renglones.Count == 0)
            throw new ArgumentException("La factura debe tener al menos un renglón.", nameof(dto.Renglones));

        foreach (var renglon in dto.Renglones)
        {
            if (renglon.Cantidad <= 0)
                throw new ArgumentException("La cantidad de cada renglón debe ser mayor que cero.", nameof(renglon.Cantidad));
            if (renglon.PrecioUnitario < 0)
                throw new ArgumentException("El precio unitario no puede ser negativo.", nameof(renglon.PrecioUnitario));
            if (renglon.ProductoId is null && renglon.ProductoNuevo is null)
                throw new ArgumentException("Cada renglón debe indicar un producto existente o los datos de un producto nuevo.");
            if (renglon.ProductoId is not null && renglon.ProductoNuevo is not null)
                throw new ArgumentException("Un renglón no puede traer productoId y productoNuevo a la vez.");
        }

        if (dto.MontoTotal <= 0)
            throw new ArgumentException("El monto total de la factura debe ser mayor que cero.", nameof(dto.MontoTotal));

        if (dto.CondicionPago == CondicionPago.Credito && dto.FechaVencimiento is null)
            throw new ReglaDeNegocioException("Una factura a crédito exige fecha de vencimiento.");
        if (dto.CondicionPago == CondicionPago.Contado && dto.FechaVencimiento is not null)
            throw new ReglaDeNegocioException("Una factura de contado no lleva fecha de vencimiento.");

        var proveedor = await _proveedores.ObtenerPorIdAsync(dto.ProveedorId)
            ?? throw new EntidadNoEncontradaException($"Proveedor {dto.ProveedorId} no encontrado.");
        if (!proveedor.Activo)
            throw new ReglaDeNegocioException($"El proveedor '{proveedor.Nombre}' está dado de baja.");

        var fuente = await _fuentes.ObtenerPorIdAsync(dto.FuenteFinanciamientoId)
            ?? throw new EntidadNoEncontradaException($"Fuente de financiamiento {dto.FuenteFinanciamientoId} no encontrada.");
        if (!fuente.Activo)
            throw new ReglaDeNegocioException($"La fuente de financiamiento '{fuente.Nombre}' está dada de baja.");

        var rubro = await _rubros.ObtenerPorIdAsync(dto.RubroGastoId)
            ?? throw new EntidadNoEncontradaException($"Rubro de gasto {dto.RubroGastoId} no encontrado.");
        if (!rubro.Activo)
            throw new ReglaDeNegocioException($"El rubro '{rubro.Nombre}' está dado de baja.");

        if (dto.LineaPoaId is not null)
        {
            var linea = await _lineasPoa.ObtenerPorIdAsync(dto.LineaPoaId.Value)
                ?? throw new EntidadNoEncontradaException($"Línea POA {dto.LineaPoaId} no encontrada.");
            if (!linea.Activo)
                throw new ReglaDeNegocioException($"La línea POA '{linea.Nombre}' está dada de baja.");
        }

        var renglonesArgs = new List<RenglonIngresoFacturaArgs>(dto.Renglones.Count);
        foreach (var renglon in dto.Renglones)
        {
            if (renglon.ProductoId is int productoId)
            {
                var producto = await _movRepo.ObtenerProductoAsync(productoId)
                    ?? throw new EntidadNoEncontradaException($"Producto {productoId} no encontrado.");
                if (!producto.Activo)
                    throw new ReglaDeNegocioException($"El producto '{producto.Codigo}' está inactivo.");

                renglonesArgs.Add(new RenglonIngresoFacturaArgs(
                    ProductoId:            productoId,
                    ProductoNuevo:         null,
                    Cantidad:              renglon.Cantidad,
                    PrecioUnitario:        renglon.PrecioUnitario,
                    ActualizarPrecioCosto: renglon.ActualizarPrecioCosto,
                    PrecioCostoAnterior:   renglon.ActualizarPrecioCosto ? producto.PrecioCosto : null));
            }
            else
            {
                var nuevo = renglon.ProductoNuevo!;
                if (string.IsNullOrWhiteSpace(nuevo.Codigo))
                    throw new ArgumentException("El código del producto nuevo es obligatorio.");
                if (string.IsNullOrWhiteSpace(nuevo.Nombre))
                    throw new ArgumentException("El nombre del producto nuevo es obligatorio.");
                if (await _unidades.ObtenerPorIdAsync(nuevo.UnidadMedidaId) is null)
                    throw new EntidadNoEncontradaException($"Unidad de medida {nuevo.UnidadMedidaId} no encontrada.");
                if (await _productos.ExisteCodigoAsync(nuevo.Codigo, null))
                    throw new ReglaDeNegocioException($"Ya existe un producto con el código '{nuevo.Codigo}'.");

                var productoNuevo = new Producto
                {
                    Codigo         = nuevo.Codigo,
                    Nombre         = nuevo.Nombre,
                    CategoriaId    = nuevo.CategoriaId,
                    UnidadMedidaId = nuevo.UnidadMedidaId,
                    PrecioCosto    = renglon.PrecioUnitario,
                    PrecioVenta    = nuevo.PrecioVenta,
                    StockActual    = renglon.Cantidad,
                    Activo         = true,
                    FechaAlta      = DateTime.UtcNow,
                };

                renglonesArgs.Add(new RenglonIngresoFacturaArgs(
                    ProductoId:            null,
                    ProductoNuevo:         productoNuevo,
                    Cantidad:              renglon.Cantidad,
                    PrecioUnitario:        renglon.PrecioUnitario,
                    ActualizarPrecioCosto: false,
                    PrecioCostoAnterior:   null));
            }
        }

        var gasto = new Gasto
        {
            ProveedorId            = dto.ProveedorId,
            NumeroFactura          = string.IsNullOrWhiteSpace(dto.NumeroFactura) ? null : dto.NumeroFactura.Trim(),
            NumeroOrden            = string.IsNullOrWhiteSpace(dto.NumeroOrden) ? null : dto.NumeroOrden.Trim(),
            Detalle                = dto.Detalle,
            Destino                = dto.Destino,
            Fecha                  = dto.Fecha,
            MontoTotal             = dto.MontoTotal,
            FuenteFinanciamientoId = dto.FuenteFinanciamientoId,
            RubroGastoId           = dto.RubroGastoId,
            LineaPoaId             = dto.LineaPoaId,
            CondicionPago          = dto.CondicionPago,
            FechaVencimiento       = dto.FechaVencimiento,
        };

        var sumaRenglones = dto.Renglones.Sum(r => r.Cantidad * r.PrecioUnitario);
        var detalle = $"Proveedor={dto.ProveedorId}; Factura={gasto.NumeroFactura ?? "(sin factura)"}; " +
                      $"Renglones={dto.Renglones.Count}; SumaRenglones={sumaRenglones}; MontoTotal={dto.MontoTotal}";

        var args = new IngresoPorFacturaArgs(
            Gasto:            gasto,
            Renglones:        renglonesArgs,
            UsuarioId:        _session.UsuarioActual!.Id,
            DetalleAuditoria: detalle);

        var resultado = await _movRepo.RegistrarIngresoPorFacturaAtomicoAsync(args);

        _version.Invalidar();

        return new IngresoPorFacturaResultadoDto(
            GastoId:            resultado.GastoId,
            MovimientoIds:      resultado.MovimientoIds,
            SumaRenglones:      sumaRenglones,
            DiferenciaConTotal: dto.MontoTotal - sumaRenglones);
    }

    public async Task AnularLoteAsync(int gastoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarMovimientos);
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var gasto = await _gastoRepo.ObtenerPorIdAsync(gastoId)
            ?? throw new EntidadNoEncontradaException($"Gasto {gastoId} no encontrado.");

        if (!gasto.Activo)
            throw new ReglaDeNegocioException($"El gasto {gastoId} ya está anulado.");
        if (gasto.Pagos.Any(p => p.Activo))
            throw new ReglaDeNegocioException(
                "No se puede anular un gasto con pagos activos: primero anulá los pagos.");

        var detalle = $"Anulación de ingreso por factura '{gasto.NumeroFactura ?? "s/n"}' (Gasto {gastoId})";

        var resultado = await _movRepo.AnularIngresoPorFacturaAtomicoAsync(
            gastoId, _session.UsuarioActual!.Id, detalle);

        if (resultado.Estado == ResultadoAnulacionIngresoEstado.StockInsuficiente)
        {
            var detalleFaltantes = string.Join("; ", resultado.Faltantes.Select(f =>
                $"{f.ProductoNombre}: stock {f.StockActual}, necesita {f.CantidadNecesaria}"));
            throw new ReglaDeNegocioException(
                $"No se puede anular: stock insuficiente en {resultado.Faltantes.Count} producto(s). {detalleFaltantes}");
        }

        _version.Invalidar();
    }
}
