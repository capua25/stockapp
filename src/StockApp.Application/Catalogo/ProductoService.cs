using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de Producto. Admin y Operador tienen permiso (GestionarProductos).
/// Cada operación: auth → validación de negocio → repo → auditoría.
/// </summary>
public class ProductoService : IProductoService
{
    private readonly IProductoRepository     _repo;
    private readonly ICurrentSession         _session;
    private readonly IAuthorizationService   _auth;
    private readonly IAuditLogger            _audit;
    private readonly IUnidadMedidaRepository _umRepo;

    public ProductoService(
        IProductoRepository     repo,
        ICurrentSession         session,
        IAuthorizationService   auth,
        IAuditLogger            audit,
        IUnidadMedidaRepository umRepo)
    {
        _repo   = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
        _umRepo  = umRepo;
    }

    public async Task<int> AltaAsync(Producto producto)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);

        // Validaciones de negocio
        if (string.IsNullOrWhiteSpace(producto.Nombre))
            throw new ArgumentException("El nombre del producto es obligatorio.");
        if (string.IsNullOrWhiteSpace(producto.Codigo))
            throw new ArgumentException("El código (SKU) del producto es obligatorio.");
        if (producto.UnidadMedidaId <= 0)
            throw new ArgumentException("La unidad de medida es obligatoria.");
        if (await _umRepo.ObtenerPorIdAsync(producto.UnidadMedidaId) is null)
            throw new ArgumentException($"La unidad de medida {producto.UnidadMedidaId} no existe.");
        if (producto.PrecioCosto < 0)
            throw new ArgumentException("El precio de costo no puede ser negativo.");
        if (producto.PrecioVenta < 0)
            throw new ArgumentException("El precio de venta no puede ser negativo.");

        if (await _repo.ExisteCodigoAsync(producto.Codigo, null))
            throw new ReglaDeNegocioException($"Ya existe un producto con el código '{producto.Codigo}'.");

        if (!string.IsNullOrWhiteSpace(producto.CodigoBarras)
            && await _repo.ExisteCodigoBarrasAsync(producto.CodigoBarras, null))
            throw new ReglaDeNegocioException($"Ya existe un producto con el código de barras '{producto.CodigoBarras}'.");

        producto.FechaAlta = DateTime.UtcNow;

        var id = await _repo.AgregarAsync(producto);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaProducto,
            "Producto", id,
            $"Alta de '{producto.Codigo}' — {producto.Nombre}");

        return id;
    }

    public async Task ModificarAsync(Producto producto)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);

        var original = await _repo.ObtenerPorIdAsync(producto.Id)
            ?? throw new EntidadNoEncontradaException($"Producto {producto.Id} no encontrado.");

        // Validar unicidad de código de barras si cambió
        if (!string.IsNullOrWhiteSpace(producto.CodigoBarras)
            && producto.CodigoBarras != original.CodigoBarras
            && await _repo.ExisteCodigoBarrasAsync(producto.CodigoBarras, producto.Id))
            throw new ReglaDeNegocioException($"Ya existe un producto con el código de barras '{producto.CodigoBarras}'.");

        // Validar que la UnidadMedida existe si cambió (evita DbUpdateException de FK)
        if (producto.UnidadMedidaId != original.UnidadMedidaId
            && await _umRepo.ObtenerPorIdAsync(producto.UnidadMedidaId) is null)
            throw new ArgumentException($"La unidad de medida {producto.UnidadMedidaId} no existe.");

        if (producto.PrecioCosto < 0)
            throw new ArgumentException("El precio de costo no puede ser negativo.");
        if (producto.PrecioVenta < 0)
            throw new ArgumentException("El precio de venta no puede ser negativo.");

        // Construir diff granular
        var cambiosPrecio   = new List<string>();
        var cambiosGenerales = new List<string>();

        if (original.PrecioCosto != producto.PrecioCosto)
            cambiosPrecio.Add($"PrecioCosto: {original.PrecioCosto} → {producto.PrecioCosto}");
        if (original.PrecioVenta != producto.PrecioVenta)
            cambiosPrecio.Add($"PrecioVenta: {original.PrecioVenta} → {producto.PrecioVenta}");

        if (original.Nombre != producto.Nombre)
            cambiosGenerales.Add($"Nombre: {original.Nombre} → {producto.Nombre}");
        if (original.Descripcion != producto.Descripcion)
            cambiosGenerales.Add($"Descripcion: {original.Descripcion} → {producto.Descripcion}");
        if (original.CodigoBarras != producto.CodigoBarras)
            cambiosGenerales.Add($"CodigoBarras: {original.CodigoBarras} → {producto.CodigoBarras}");
        if (original.CategoriaId != producto.CategoriaId)
            cambiosGenerales.Add($"CategoriaId: {original.CategoriaId} → {producto.CategoriaId}");
        if (original.ProveedorId != producto.ProveedorId)
            cambiosGenerales.Add($"ProveedorId: {original.ProveedorId} → {producto.ProveedorId}");
        if (original.UnidadMedidaId != producto.UnidadMedidaId)
            cambiosGenerales.Add($"UnidadMedidaId: {original.UnidadMedidaId} → {producto.UnidadMedidaId}");
        if (original.StockMinimo != producto.StockMinimo)
            cambiosGenerales.Add($"StockMinimo: {original.StockMinimo} → {producto.StockMinimo}");

        // Sin cambios: no persistir ni auditar
        if (cambiosPrecio.Count == 0 && cambiosGenerales.Count == 0)
            return;

        // Aplicar cambios en la entidad original (para actualizar el objeto trackeado)
        original.Nombre        = producto.Nombre;
        original.Descripcion   = producto.Descripcion;
        original.CodigoBarras  = producto.CodigoBarras;
        original.PrecioCosto   = producto.PrecioCosto;
        original.PrecioVenta   = producto.PrecioVenta;
        original.CategoriaId   = producto.CategoriaId;
        original.ProveedorId   = producto.ProveedorId;
        original.UnidadMedidaId = producto.UnidadMedidaId;
        original.StockMinimo   = producto.StockMinimo;

        await _repo.ActualizarAsync(original);

        // Auditoría granular: precio en entrada separada, resto en ModificacionProducto
        if (cambiosPrecio.Count > 0)
            await _audit.RegistrarAsync(
                _session.UsuarioActual!.Id,
                AccionAuditada.CambioPrecio,
                "Producto", producto.Id,
                string.Join("; ", cambiosPrecio));

        if (cambiosGenerales.Count > 0)
            await _audit.RegistrarAsync(
                _session.UsuarioActual!.Id,
                AccionAuditada.ModificacionProducto,
                "Producto", producto.Id,
                string.Join("; ", cambiosGenerales));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);

        var producto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Producto {id} no encontrado.");

        if (!producto.Activo)
            throw new ReglaDeNegocioException($"El producto {id} ya está inactivo.");

        producto.Activo = false;
        await _repo.ActualizarAsync(producto);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaProducto,
            "Producto", id,
            $"Baja lógica de '{producto.Codigo}' — {producto.Nombre}");
    }

    public async Task CambiarPrecioAsync(int id, decimal precioCosto, decimal precioVenta)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);

        if (precioCosto < 0)
            throw new ArgumentException("El precio de costo no puede ser negativo.");
        if (precioVenta < 0)
            throw new ArgumentException("El precio de venta no puede ser negativo.");

        var producto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Producto {id} no encontrado.");

        var detalle = $"PrecioCosto: {producto.PrecioCosto} → {precioCosto}; PrecioVenta: {producto.PrecioVenta} → {precioVenta}";

        producto.PrecioCosto = precioCosto;
        producto.PrecioVenta = precioVenta;
        await _repo.ActualizarAsync(producto);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.CambioPrecio,
            "Producto", id,
            detalle);
    }

    public async Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);
        var productos = await _repo.BuscarAsync(sku, codigoBarras, nombre);
        return productos.Select(AProductoDto).ToList();
    }

    public async Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);
        var productos = await _repo.BuscarPorTextoAsync(texto);
        return productos.Select(AProductoDto).ToList();
    }

    /// <summary>
    /// Mapeo a mano (no hay AutoMapper en el repo) de la entidad de Domain a ProductoDto.
    /// UnidadMedidaNombre usa "??" porque en tests unitarios la navegación puede no estar
    /// poblada (Producto.UnidadMedida queda null si no se setea explícitamente); en producción
    /// el repo siempre hace .Include(UnidadMedida).
    /// </summary>
    private static ProductoDto AProductoDto(Producto p) => new ProductoDto(
        Id:                 p.Id,
        Codigo:             p.Codigo,
        CodigoBarras:       p.CodigoBarras,
        Nombre:             p.Nombre,
        Descripcion:        p.Descripcion,
        CategoriaId:        p.CategoriaId,
        CategoriaNombre:    p.Categoria?.Nombre,
        ProveedorId:        p.ProveedorId,
        UnidadMedidaId:     p.UnidadMedidaId,
        UnidadMedidaNombre: p.UnidadMedida?.Nombre ?? string.Empty,
        PrecioCosto:        p.PrecioCosto,
        PrecioVenta:        p.PrecioVenta,
        StockActual:        p.StockActual,
        StockMinimo:        p.StockMinimo,
        Activo:             p.Activo,
        FechaAlta:          p.FechaAlta);
}
