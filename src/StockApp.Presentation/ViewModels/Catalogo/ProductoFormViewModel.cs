using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly IProductoService     _service;
    private readonly IUnidadMedidaService _unidadMedidaService;
    private readonly ICategoriaService    _categoriaService;
    private readonly INavigationService   _navigation;

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

    /// <summary>
    /// Unidades de medida activas disponibles para elegir. Se carga en <see cref="InicializarAsync"/>,
    /// junto con la garantía idempotente de que exista la unidad "Unidad" por defecto.
    /// </summary>
    public ObservableCollection<UnidadMedida> UnidadesMedida { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private UnidadMedida? _unidadMedidaSeleccionada;

    /// <summary>
    /// Categorías activas disponibles para elegir. Opcional: el producto puede quedar sin categoría.
    /// </summary>
    public ObservableCollection<Categoria> Categorias { get; } = new();

    [ObservableProperty]
    private Categoria? _categoriaSeleccionada;

    [ObservableProperty]
    private string? _mensajeError;

    public ProductoFormViewModel(
        IProductoService     service,
        IUnidadMedidaService unidadMedidaService,
        ICategoriaService    categoriaService,
        INavigationService   navigation)
    {
        _service             = service;
        _unidadMedidaService = unidadMedidaService;
        _categoriaService    = categoriaService;
        _navigation          = navigation;
    }

    /// <summary>
    /// Carga las colecciones de unidades de medida y categorías para el formulario.
    /// Garantiza (idempotente) que exista la unidad "Unidad" por defecto y la preselecciona.
    /// Debe invocarse al mostrar la vista (ver ProductoFormView.axaml.cs, DataContextChanged),
    /// ya que no hay un hook de navegación que dispare la carga automáticamente.
    /// </summary>
    public async Task InicializarAsync()
    {
        var unidadPorDefecto = await _unidadMedidaService.GarantizarUnidadPorDefectoAsync();

        var unidades = await _unidadMedidaService.ListarActivasAsync();
        UnidadesMedida.Clear();
        foreach (var u in unidades)
            UnidadesMedida.Add(u);

        UnidadMedidaSeleccionada = UnidadesMedida.FirstOrDefault(u => u.Id == unidadPorDefecto.Id)
            ?? unidadPorDefecto;

        var categorias = await _categoriaService.ListarActivasAsync();
        Categorias.Clear();
        foreach (var c in categorias)
            Categorias.Add(c);
    }

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Codigo)
        && !string.IsNullOrWhiteSpace(Nombre)
        && UnidadMedidaSeleccionada is not null;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            var producto = new Producto
            {
                Codigo       = Codigo,
                Nombre       = Nombre,
                CodigoBarras = CodigoBarras,
                Descripcion  = Descripcion,
                PrecioCosto  = PrecioCosto,
                PrecioVenta  = PrecioVenta,
                UnidadMedidaId = UnidadMedidaSeleccionada!.Id,
                CategoriaId    = CategoriaSeleccionada?.Id,
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
