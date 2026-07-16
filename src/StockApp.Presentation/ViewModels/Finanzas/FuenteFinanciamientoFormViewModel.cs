using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Formulario de alta / edición de una fuente de financiamiento.</summary>
public partial class FuenteFinanciamientoFormViewModel : ViewModelBase
{
    private readonly IFuenteFinanciamientoService _service;
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

    public string Titulo => EsEdicion ? "Editar fuente de financiamiento" : "Nueva fuente de financiamiento";

    public FuenteFinanciamientoFormViewModel(IFuenteFinanciamientoService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    /// <summary>Precarga el formulario en modo edición (llamado por el overload de Navegar).</summary>
    public void CargarParaEditar(FuenteFinanciamiento fuente)
    {
        _idEdicion = fuente.Id;
        Nombre     = fuente.Nombre;
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
                await _service.ModificarAsync(new FuenteFinanciamiento { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new FuenteFinanciamiento { Nombre = Nombre });

            _navigation.Navegar<MaestrosFinanzasViewModel>();
        }
        catch (System.Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or System.ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<MaestrosFinanzasViewModel>();
}
