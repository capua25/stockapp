using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abre el diálogo de "Recalcular stock": un buscador de producto con búsqueda server-side. Devuelve el
/// producto elegido, o null si se cancela (o si no hay ventana donde mostrarlo).
/// </summary>
public interface IRecalcularStockDialogService
{
    /// <param name="buscar">Delegado para <c>AutoCompleteBox.AsyncPopulator</c> del buscador.</param>
    Task<ProductoDto?> ElegirProductoAsync(Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar);
}
