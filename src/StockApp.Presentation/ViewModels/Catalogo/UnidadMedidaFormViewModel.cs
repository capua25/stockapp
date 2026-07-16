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

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _abreviatura = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar unidad de medida" : "Nueva unidad de medida";

    public UnidadMedidaFormViewModel(IUnidadMedidaService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    /// <summary>Precarga el formulario en modo edición (llamado por el overload de Navegar).</summary>
    public void CargarParaEditar(UnidadMedida unidadMedida)
    {
        _idEdicion  = unidadMedida.Id;
        Nombre      = unidadMedida.Nombre;
        Abreviatura = unidadMedida.Abreviatura;
        EsEdicion   = true;
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
            if (EsEdicion)
                await _service.ModificarAsync(new UnidadMedida
                {
                    Id          = _idEdicion,
                    Nombre      = Nombre,
                    Abreviatura = Abreviatura,
                });
            else
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
