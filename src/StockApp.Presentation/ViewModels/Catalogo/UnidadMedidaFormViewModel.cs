using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de una unidad de medida.
/// </summary>
public partial class UnidadMedidaFormViewModel : ViewModelBase
{
    private readonly IUnidadMedidaService _service;
    private readonly INavigationService   _navigation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _abreviatura = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    public UnidadMedidaFormViewModel(IUnidadMedidaService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Nombre)
        && !string.IsNullOrWhiteSpace(Abreviatura);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            await _service.AltaAsync(new UnidadMedida
            {
                Nombre      = Nombre,
                Abreviatura = Abreviatura,
            });
            _navigation.Navegar<UnidadMedidaListViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
