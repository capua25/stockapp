using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de una dimensión temática.
/// </summary>
public partial class DimensionTematicaFormViewModel : ViewModelBase
{
    private readonly IDimensionTematicaService _service;
    private readonly INavigationService        _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar dimensión" : "Nueva dimensión";

    public DimensionTematicaFormViewModel(IDimensionTematicaService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public void CargarParaEditar(DimensionTematica dimension)
    {
        _idEdicion = dimension.Id;
        Nombre     = dimension.Nombre;
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
                await _service.ModificarAsync(new DimensionTematica { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new DimensionTematica { Nombre = Nombre });

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
