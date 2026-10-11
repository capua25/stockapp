using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abre el diálogo de alta rápida de producto de Ingreso por factura con las categorías y unidades que la pantalla ya
/// cargó. Devuelve el producto nuevo, o null si se cancela (o si no hay ventana donde mostrarlo).
/// </summary>
public interface IAltaProductoRapidaDialogService
{
    Task<ProductoNuevoDto?> PedirProductoNuevoAsync(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades);
}
