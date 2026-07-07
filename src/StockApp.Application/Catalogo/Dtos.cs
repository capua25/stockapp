namespace StockApp.Application.Catalogo;

/// <summary>
/// DTO de lectura de Producto: aplana FK ids + nombres de Categoria/UnidadMedida para
/// servir tanto al listado como al formulario de edición, sin arrastrar navegación de EF
/// (Fase 0 de la migración client-server — ver
/// docs/superpowers/specs/2026-07-07-migracion-client-server-design.md, fricción #1).
/// ProveedorNombre no se incluye: ProductoRepository.BuscarAsync/BuscarPorTextoAsync no
/// hacen .Include(Proveedor), así que solo se expone el id.
/// </summary>
public record ProductoDto(
    int Id,
    string Codigo,
    string? CodigoBarras,
    string Nombre,
    string? Descripcion,
    int? CategoriaId,
    string? CategoriaNombre,
    int? ProveedorId,
    int UnidadMedidaId,
    string UnidadMedidaNombre,
    decimal PrecioCosto,
    decimal PrecioVenta,
    decimal StockActual,
    decimal StockMinimo,
    bool Activo,
    DateTime FechaAlta);
