using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de un origen de financiamiento.
/// </summary>
public partial class OrigenFinanciamientoFormViewModel : ViewModelBase
{
    private readonly IOrigenFinanciamientoService _service;
    private readonly INavigationService           _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar origen de financiamiento" : "Nuevo origen de financiamiento";

    public OrigenFinanciamientoFormViewModel(IOrigenFinanciamientoService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public void CargarParaEditar(OrigenFinanciamiento origen)
    {
        _idEdicion = origen.Id;
        Nombre     = origen.Nombre;
        EsEdicion  = true;
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Nombre);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(new OrigenFinanciamiento { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new OrigenFinanciamiento { Nombre = Nombre });

            _navigation.Navegar<CatalogosTareaViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
