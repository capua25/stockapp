using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de un proveedor.
/// </summary>
public partial class ProveedorFormViewModel : ViewModelBase
{
    private readonly IProveedorService  _service;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _telefono;

    [ObservableProperty]
    private string? _email;

    [ObservableProperty]
    private string? _direccion;

    [ObservableProperty]
    private string? _notas;

    [ObservableProperty]
    private string? _mensajeError;

    public ProveedorFormViewModel(IProveedorService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Nombre);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            await _service.AltaAsync(new Proveedor
            {
                Nombre    = Nombre,
                Telefono  = Telefono,
                Email     = Email,
                Direccion = Direccion,
                Notas     = Notas,
            });
            _navigation.Navegar<ProveedorListViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
