using System.Threading.Tasks;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Maestros de finanzas": hostea las tres sub-listas (fuentes, rubros,
/// líneas POA) que la vista muestra en tabs. Los formularios de alta/edición navegan
/// a pantalla completa y vuelven acá al guardar o cancelar.
/// </summary>
public partial class MaestrosFinanzasViewModel : ViewModelBase
{
    public FuenteFinanciamientoListViewModel FuentesVm { get; }
    public RubroGastoListViewModel RubrosVm { get; }
    public LineaPoaListViewModel LineasPoaVm { get; }

    public MaestrosFinanzasViewModel(
        FuenteFinanciamientoListViewModel fuentesVm,
        RubroGastoListViewModel rubrosVm,
        LineaPoaListViewModel lineasPoaVm)
    {
        FuentesVm   = fuentesVm;
        RubrosVm    = rubrosVm;
        LineasPoaVm = lineasPoaVm;
    }

    public async Task CargarAsync()
    {
        await FuentesVm.CargarAsync();
        await RubrosVm.CargarAsync();
        await LineasPoaVm.CargarAsync();
    }
}
