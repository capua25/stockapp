using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Calendario de pagos" (spec §7.5): facturas vencidas, a vencer en 7/30 días y
/// pagos recientes. "Registrar pago" trae el Gasto completo y navega a PagosGastoViewModel.
/// </summary>
public partial class CalendarioPagosViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly IGastoService          _gastoService;
    private readonly INavigationService     _navigation;

    public ObservableCollection<FacturaCalendarioDto> Vencidas { get; } = new();
    public ObservableCollection<FacturaCalendarioDto> AVencer7Dias { get; } = new();
    public ObservableCollection<FacturaCalendarioDto> AVencer30Dias { get; } = new();
    public ObservableCollection<PagoRecienteDto> PagosRecientes { get; } = new();

    public CalendarioPagosViewModel(
        IFinanzasVistasService service, IGastoService gastoService, INavigationService navigation)
    {
        _service      = service;
        _gastoService = gastoService;
        _navigation   = navigation;
    }

    public async Task CargarAsync()
    {
        var calendario = await _service.ObtenerCalendarioPagosAsync();

        Vencidas.Clear();
        foreach (var f in calendario.Vencidas) Vencidas.Add(f);
        AVencer7Dias.Clear();
        foreach (var f in calendario.AVencer7Dias) AVencer7Dias.Add(f);
        AVencer30Dias.Clear();
        foreach (var f in calendario.AVencer30Dias) AVencer30Dias.Add(f);
        PagosRecientes.Clear();
        foreach (var p in calendario.PagosRecientes) PagosRecientes.Add(p);
    }

    [RelayCommand]
    private async Task RecargarAsync() => await CargarAsync();

    [RelayCommand]
    private async Task RegistrarPagoAsync(FacturaCalendarioDto? fila)
    {
        if (fila is null) return;
        var gasto = await _gastoService.ObtenerPorIdAsync(fila.GastoId);
        _navigation.Navegar<PagosGastoViewModel>(vm => vm.CargarParaGasto(gasto));
    }
}
