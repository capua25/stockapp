using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abre la confirmación de cambios de precio de costo de Ingreso por factura. Devuelve un tilde por cambio, en el mismo
/// orden, o null si se cancela (o si no hay ventana donde mostrarla).
/// </summary>
public interface IConfirmacionPreciosDialogService
{
    Task<IReadOnlyList<bool>?> ConfirmarCambiosAsync(IReadOnlyList<ItemConfirmacionPrecioVm> cambios);
}
