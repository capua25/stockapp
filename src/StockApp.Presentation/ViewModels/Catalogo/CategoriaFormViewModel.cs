using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de una categoría.
/// </summary>
public partial class CategoriaFormViewModel : ViewModelBase
{
    private readonly ICategoriaService  _service;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    public CategoriaFormViewModel(ICategoriaService service, INavigationService navigation)
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
            await _service.AltaAsync(new Categoria { Nombre = Nombre });
            _navigation.Navegar<CategoriaListViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
