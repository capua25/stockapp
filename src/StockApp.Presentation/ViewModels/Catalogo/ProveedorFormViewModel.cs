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

    private int _idEdicion;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar proveedor" : "Nuevo proveedor";

    public ProveedorFormViewModel(IProveedorService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    /// <summary>Precarga el formulario en modo edición (llamado por el overload de Navegar).</summary>
    public void CargarParaEditar(Proveedor proveedor)
    {
        _idEdicion = proveedor.Id;
        Nombre     = proveedor.Nombre;
        Telefono   = proveedor.Telefono;
        Email      = proveedor.Email;
        Direccion  = proveedor.Direccion;
        Notas      = proveedor.Notas;
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
                await _service.ModificarAsync(new Proveedor
                {
                    Id        = _idEdicion,
                    Nombre    = Nombre,
                    Telefono  = Telefono,
                    Email     = Email,
                    Direccion = Direccion,
                    Notas     = Notas,
                });
            else
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
