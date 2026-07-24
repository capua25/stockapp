using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Importar planillas" (F5d): hostea las 2 sub-listas (Nueva importación, Historial)
/// en tabs, mismo patrón que MaestrosFinanzasViewModel.
/// </summary>
public partial class ImportacionViewModel : ViewModelBase
{
    public NuevaImportacionViewModel NuevaVm { get; }
    public HistorialImportacionesViewModel HistorialVm { get; }

    public ImportacionViewModel(NuevaImportacionViewModel nuevaVm, HistorialImportacionesViewModel historialVm)
    {
        NuevaVm = nuevaVm;
        HistorialVm = historialVm;
    }
}
