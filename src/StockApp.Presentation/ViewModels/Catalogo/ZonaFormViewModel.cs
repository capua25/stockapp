using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de una zona.
/// </summary>
public partial class ZonaFormViewModel : ViewModelBase
{
    private readonly IZonaService       _service;
    private readonly INavigationService _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar zona" : "Nueva zona";

    public ZonaFormViewModel(IZonaService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    /// <summary>Precarga el formulario en modo edición (llamado por el overload de Navegar).</summary>
    public void CargarParaEditar(Zona zona)
    {
        _idEdicion = zona.Id;
        Nombre     = zona.Nombre;
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
                await _service.ModificarAsync(new Zona { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new Zona { Nombre = Nombre });

            _navigation.Navegar<CatalogosTareaViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
