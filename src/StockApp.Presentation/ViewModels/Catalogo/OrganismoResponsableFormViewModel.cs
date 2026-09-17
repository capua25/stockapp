using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de un organismo responsable.
/// </summary>
public partial class OrganismoResponsableFormViewModel : ViewModelBase
{
    private readonly IOrganismoResponsableService _service;
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

    public string Titulo => EsEdicion ? "Editar organismo responsable" : "Nuevo organismo responsable";

    public OrganismoResponsableFormViewModel(IOrganismoResponsableService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public void CargarParaEditar(OrganismoResponsable organismo)
    {
        _idEdicion = organismo.Id;
        Nombre     = organismo.Nombre;
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
                await _service.ModificarAsync(new OrganismoResponsable { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new OrganismoResponsable { Nombre = Nombre });

            _navigation.Navegar<CatalogosTareaViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<CatalogosTareaViewModel>();
}
