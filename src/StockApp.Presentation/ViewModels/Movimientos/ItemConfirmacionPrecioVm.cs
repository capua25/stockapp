using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>Fila de la lista de confirmación de precio de costo (Task 9). Solo aparecen acá los
/// productos existentes cuyo PrecioCosto difiere del PrecioUnitario cargado en el renglón.</summary>
public partial class ItemConfirmacionPrecioVm : ObservableObject
{
    public required FilaRenglonFacturaVm Fila { get; init; }
    public required string ProductoNombre { get; init; }
    public required decimal PrecioActual { get; init; }
    public required decimal PrecioNuevo { get; init; }

    [ObservableProperty] private bool _confirmado;
}
