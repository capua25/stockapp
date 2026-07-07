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

    /// <summary>
    /// true cuando el formulario fue precargado vía <see cref="CargarParaEditar"/> (navegación
    /// desde ProductoListViewModel.EditarCommand). Determina el título mostrado y si
    /// GuardarAsync llama a ModificarAsync (edición) o AltaAsync (alta) — ver GuardarAsync.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    /// <summary>Id del producto en edición. Solo tiene sentido cuando <see cref="EsEdicion"/> es true.</summary>
    private int _productoId;

    /// <summary>
    /// UnidadMedidaId/CategoriaId/ProveedorId/StockMinimo originales del producto cargado para
    /// editar. UnidadMedidaId y CategoriaId se usan para preseleccionar el combo correspondiente
    /// una vez que InicializarAsync carga las colecciones (CargarParaEditar corre ANTES de que
    /// esas colecciones existan, porque lo dispara el inicializador de Navegar<TVm> y no la
    /// vista). ProveedorId y StockMinimo no tienen campo en este formulario, así que se preservan
    /// tal cual para no pisarlos con null/0 al llamar ModificarAsync (que persiste la entidad
    /// completa recibida).
    /// </summary>
    private int  _unidadMedidaIdOriginal;
    private int? _categoriaIdOriginal;
    private int? _proveedorIdOriginal;
    private decimal _stockMinimoOriginal;

    /// <summary>Título dinámico del formulario, usado en la vista.</summary>
    public string Titulo => EsEdicion ? "Editar producto" : "Nuevo producto";

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
    /// Precarga el formulario en modo edición con los datos de <paramref name="producto"/>.
    /// Se invoca desde el inicializador pasado a
    /// <see cref="INavigationService.Navegar{TVm}(System.Action{TVm})"/> por
    /// ProductoListViewModel.EditarCommand, ANTES de que la vista dispare
    /// <see cref="InicializarAsync"/> (que recién ahí carga UnidadesMedida/Categorias). Por eso
    /// UnidadMedidaId/CategoriaId se guardan acá y se resuelven contra las colecciones una vez
    /// cargadas en InicializarAsync.
    /// </summary>
    public void CargarParaEditar(Producto producto)
    {
        EsEdicion    = true;
        _productoId  = producto.Id;
        Codigo       = producto.Codigo;
        Nombre       = producto.Nombre;
        CodigoBarras = producto.CodigoBarras;
        Descripcion  = producto.Descripcion;
        PrecioCosto  = producto.PrecioCosto;
        PrecioVenta  = producto.PrecioVenta;

        _unidadMedidaIdOriginal = producto.UnidadMedidaId;
        _categoriaIdOriginal    = producto.CategoriaId;
        _proveedorIdOriginal    = producto.ProveedorId;
        _stockMinimoOriginal    = producto.StockMinimo;
    }

    /// <summary>
    /// Carga las colecciones de unidades de medida y categorías para el formulario.
    /// Debe invocarse al mostrar la vista (ver ProductoFormView.axaml.cs, DataContextChanged),
    /// ya que no hay un hook de navegación que dispare la carga automáticamente.
    /// En modo alta, garantiza (idempotente) que exista la unidad "Unidad" por defecto y la
    /// preselecciona. En modo edición (<see cref="EsEdicion"/>), preselecciona en cambio la
    /// unidad y categoría originales del producto cargado por <see cref="CargarParaEditar"/>.
    /// </summary>
    public async Task InicializarAsync()
    {
        var unidadPorDefecto = await _unidadMedidaService.GarantizarUnidadPorDefectoAsync();

        var unidades = await _unidadMedidaService.ListarActivasAsync();
        UnidadesMedida.Clear();
        foreach (var u in unidades)
            UnidadesMedida.Add(u);

        var categorias = await _categoriaService.ListarActivasAsync();
        Categorias.Clear();
        foreach (var c in categorias)
            Categorias.Add(c);

        if (EsEdicion)
        {
            UnidadMedidaSeleccionada = UnidadesMedida.FirstOrDefault(u => u.Id == _unidadMedidaIdOriginal);
            CategoriaSeleccionada = _categoriaIdOriginal is int categoriaId
                ? Categorias.FirstOrDefault(c => c.Id == categoriaId)
                : null;
            return;
        }

        UnidadMedidaSeleccionada = UnidadesMedida.FirstOrDefault(u => u.Id == unidadPorDefecto.Id)
            ?? unidadPorDefecto;
    }

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Codigo)
        && !string.IsNullOrWhiteSpace(Nombre)
        && UnidadMedidaSeleccionada is not null;

    /// <summary>
    /// Bifurca entre alta y modificación según <see cref="EsEdicion"/>. Ninguna excepción de
    /// dominio (ArgumentException/InvalidOperationException por código duplicado o unidad
    /// inexistente, KeyNotFoundException si el producto ya no existe) debe crashear la app: se
    /// captura y se muestra amigable en <see cref="MensajeError"/>, igual que el resto de los
    /// formularios de catálogo (CategoriaFormViewModel/ProveedorFormViewModel).
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            if (EsEdicion)
            {
                var producto = new Producto
                {
                    Id             = _productoId,
                    Codigo         = Codigo,
                    Nombre         = Nombre,
                    CodigoBarras   = CodigoBarras,
                    Descripcion    = Descripcion,
                    PrecioCosto    = PrecioCosto,
                    PrecioVenta    = PrecioVenta,
                    UnidadMedidaId = UnidadMedidaSeleccionada!.Id,
                    CategoriaId    = CategoriaSeleccionada?.Id,
                    ProveedorId    = _proveedorIdOriginal,
                    StockMinimo    = _stockMinimoOriginal,
                };
                await _service.ModificarAsync(producto);
            }
            else
            {
                var producto = new Producto
                {
                    Codigo         = Codigo,
                    Nombre         = Nombre,
                    CodigoBarras   = CodigoBarras,
                    Descripcion    = Descripcion,
                    PrecioCosto    = PrecioCosto,
                    PrecioVenta    = PrecioVenta,
                    UnidadMedidaId = UnidadMedidaSeleccionada!.Id,
                    CategoriaId    = CategoriaSeleccionada?.Id,
                };
                await _service.AltaAsync(producto);
            }

            _navigation.Navegar<ProductoListViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
