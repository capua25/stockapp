using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de un producto.
/// </summary>
public partial class ProductoFormViewModel : ViewModelBase
{
    private readonly IProductoService   _service;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _codigo = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _codigoBarras;

    [ObservableProperty]
    private string? _descripcion;

    [ObservableProperty]
    private decimal _precioCosto;

    [ObservableProperty]
    private decimal _precioVenta;

    [ObservableProperty]
    private string? _mensajeError;

    public ProductoFormViewModel(IProductoService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Codigo)
        && !string.IsNullOrWhiteSpace(Nombre);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            var producto = new Producto
            {
                Codigo      = Codigo,
                Nombre      = Nombre,
                CodigoBarras = CodigoBarras,
                Descripcion  = Descripcion,
                PrecioCosto  = PrecioCosto,
                PrecioVenta  = PrecioVenta,
            };
            await _service.AltaAsync(producto);
            _navigation.Navegar<ProductoListViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
