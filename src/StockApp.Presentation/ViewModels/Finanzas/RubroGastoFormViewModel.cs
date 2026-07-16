using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Formulario de alta / edición de un rubro de gasto (código numérico + nombre).</summary>
public partial class RubroGastoFormViewModel : ViewModelBase
{
    private readonly IRubroGastoService _service;
    private readonly INavigationService _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _codigoTexto = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar rubro de gasto" : "Nuevo rubro de gasto";

    public RubroGastoFormViewModel(IRubroGastoService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public void CargarParaEditar(RubroGasto rubro)
    {
        _idEdicion  = rubro.Id;
        CodigoTexto = rubro.Codigo.ToString();
        Nombre      = rubro.Nombre;
        EsEdicion   = true;
    }

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Nombre)
           && int.TryParse(CodigoTexto, out var codigo)
           && codigo > 0;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        var codigo = int.Parse(CodigoTexto);
        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(new RubroGasto { Id = _idEdicion, Codigo = codigo, Nombre = Nombre });
            else
                await _service.AltaAsync(new RubroGasto { Codigo = codigo, Nombre = Nombre });

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
