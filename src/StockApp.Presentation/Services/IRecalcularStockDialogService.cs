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
    /// <param name="sinPermiso">
    /// Se consulta después de cada búsqueda: si devuelve true (el servidor rechazó la búsqueda con un 403) el
    /// diálogo deshabilita el buscador y muestra el aviso en línea "No tenés permiso para buscar productos."
    /// (deuda D14 de la Fase 3c-2).
    /// </param>
    Task<ProductoDto?> ElegirProductoAsync(
        Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar, Func<bool> sinPermiso);
}
