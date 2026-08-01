using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// Cabecera de factura + grilla editable de renglones en una sola pantalla (spec "Ingreso de
/// stock por factura"). Task 8 cubre cabecera/renglones/totales; Task 9 agrega el alta en línea
/// de producto nuevo y la confirmación de cambios de precio; Task 10 la vista y el adjunto.
/// </summary>
public partial class IngresoPorFacturaViewModel : ViewModelBase
{
    private readonly IIngresoPorFacturaService    _service;
    private readonly IProductoService             _productoService;
    private readonly ICategoriaService            _categoriaService;
    private readonly IUnidadMedidaService         _unidadMedidaService;
    private readonly IProveedorService            _proveedorService;
    private readonly IFuenteFinanciamientoService _fuenteService;
    private readonly IRubroGastoService           _rubroService;
    private readonly ILineaPoaService             _lineaService;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;
    private readonly AdjuntosPanelViewModel       _adjuntosPanel;

    public AdjuntosPanelViewModel AdjuntosPanel => _adjuntosPanel;

    private static readonly IFormatProvider CulturaMonto = CrearCulturaMonto();

    private static IFormatProvider CrearCulturaMonto()
    {
        try { return CultureInfo.GetCultureInfo("es-UY"); }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private Proveedor? _proveedorSeleccionado;

    [ObservableProperty] private string? _numeroFactura;
    [ObservableProperty] private string? _numeroOrden;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _detalle = string.Empty;

    [ObservableProperty] private string? _destino;
    [ObservableProperty] private DateTime? _fechaSeleccionada = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _montoTotalTexto = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private RubroGasto? _rubroSeleccionado;

    [ObservableProperty] private LineaPoa? _lineaPoaSeleccionada;
    [ObservableProperty] private bool _esCredito;
    [ObservableProperty] private DateTime? _fechaVencimientoSeleccionada;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _guardadoExitoso;

    [ObservableProperty] private decimal _sumaRenglones;
    [ObservableProperty] private decimal _diferenciaConTotal;
    [ObservableProperty] private int? _gastoIdCreado;

    public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();
    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
    public ObservableCollection<LineaPoa> LineasPoaDisponibles { get; } = new();
    public ObservableCollection<ProductoDto> ProductosDisponibles { get; } = new();
    public ObservableCollection<Categoria> CategoriasDisponibles { get; } = new();
    public ObservableCollection<UnidadMedida> UnidadesMedidaDisponibles { get; } = new();
    public ObservableCollection<FilaRenglonFacturaVm> Renglones { get; } = new();

    public IngresoPorFacturaViewModel(
        IIngresoPorFacturaService service,
        IProductoService productoService,
        ICategoriaService categoriaService,
        IUnidadMedidaService unidadMedidaService,
        IProveedorService proveedorService,
        IFuenteFinanciamientoService fuenteService,
        IRubroGastoService rubroService,
        ILineaPoaService lineaService,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        AdjuntosPanelViewModel adjuntosPanel)
    {
        _service             = service;
        _productoService     = productoService;
        _categoriaService    = categoriaService;
        _unidadMedidaService = unidadMedidaService;
        _proveedorService    = proveedorService;
        _fuenteService       = fuenteService;
        _rubroService        = rubroService;
        _lineaService        = lineaService;
        _navigation          = navigation;
        _confirmacion        = confirmacion;
        _adjuntosPanel       = adjuntosPanel;
    }

    /// <summary>Carga los combos. La dispara la View (DataContextChanged) — sin preselección
    /// (decisión 8 del spec): fuente y rubro arrancan sin seleccionar.</summary>
    public async Task InicializarAsync()
    {
        var proveedores = await _proveedorService.ListarTodosAsync();
        ProveedoresDisponibles.Clear();
        foreach (var p in proveedores.Where(p => p.Activo)) ProveedoresDisponibles.Add(p);

        var fuentes = await _fuenteService.ListarActivasAsync();
        FuentesDisponibles.Clear();
        foreach (var f in fuentes) FuentesDisponibles.Add(f);

        var rubros = await _rubroService.ListarActivosAsync();
        RubrosDisponibles.Clear();
        foreach (var r in rubros) RubrosDisponibles.Add(r);

        var lineas = await _lineaService.ListarActivasAsync();
        LineasPoaDisponibles.Clear();
        foreach (var l in lineas) LineasPoaDisponibles.Add(l);

        var productos = await _productoService.BuscarAsync(null, null, null);
        ProductosDisponibles.Clear();
        foreach (var p in productos.Where(p => p.Activo)) ProductosDisponibles.Add(p);

        var categorias = await _categoriaService.ListarActivasAsync();
        CategoriasDisponibles.Clear();
        foreach (var c in categorias) CategoriasDisponibles.Add(c);

        var unidades = await _unidadMedidaService.ListarActivasAsync();
        UnidadesMedidaDisponibles.Clear();
        foreach (var u in unidades) UnidadesMedidaDisponibles.Add(u);
    }

    private void RecalcularTotales()
    {
        SumaRenglones = Renglones.Sum(r => r.Cantidad * r.PrecioUnitario);
        decimal.TryParse(MontoTotalTexto, NumberStyles.Number, CulturaMonto, out var monto);
        DiferenciaConTotal = monto - SumaRenglones;
    }

    partial void OnMontoTotalTextoChanged(string value) => RecalcularTotales();

    private void Renglon_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilaRenglonFacturaVm.Cantidad) or nameof(FilaRenglonFacturaVm.PrecioUnitario))
            RecalcularTotales();
    }

    [RelayCommand]
    private void AgregarRenglon()
    {
        var fila = new FilaRenglonFacturaVm { Cantidad = 1m };
        fila.PropertyChanged += Renglon_PropertyChanged;
        Renglones.Add(fila);
        RecalcularTotales();
        GuardarCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void QuitarRenglon(FilaRenglonFacturaVm fila)
    {
        fila.PropertyChanged -= Renglon_PropertyChanged;
        Renglones.Remove(fila);
        RecalcularTotales();
        GuardarCommand.NotifyCanExecuteChanged();
    }

    private bool PuedeGuardar()
        => !GuardadoExitoso
           && Renglones.Count > 0
           && ProveedorSeleccionado is not null
           && FuenteSeleccionada is not null
           && RubroSeleccionado is not null
           && !string.IsNullOrWhiteSpace(Detalle)
           && !string.IsNullOrWhiteSpace(MontoTotalTexto);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;

        if (!decimal.TryParse(MontoTotalTexto, NumberStyles.Number, CulturaMonto, out var monto))
        {
            MensajeError = "El monto total no es un número válido.";
            return;
        }

        await GuardarInternoAsync(monto);
    }

    private async Task GuardarInternoAsync(decimal monto)
    {
        var dto = new IngresoPorFacturaDto(
            ProveedorSeleccionado!.Id, NumeroFactura, NumeroOrden,
            FechaSeleccionada is null ? DateTime.UtcNow : DateTime.SpecifyKind(FechaSeleccionada.Value.Date, DateTimeKind.Utc),
            Detalle, Destino, monto,
            FuenteSeleccionada!.Id, RubroSeleccionado!.Id, LineaPoaSeleccionada?.Id,
            EsCredito ? CondicionPago.Credito : CondicionPago.Contado,
            EsCredito && FechaVencimientoSeleccionada is not null
                ? DateTime.SpecifyKind(FechaVencimientoSeleccionada.Value.Date, DateTimeKind.Utc)
                : null,
            Renglones.Select(ARenglonDto).ToList());

        try
        {
            var resultado = await _service.RegistrarAsync(dto);
            GastoIdCreado = resultado.GastoId;
            GuardadoExitoso = true;
            await _adjuntosPanel.InicializarAsync(resultado.GastoId, null);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    private static RenglonFacturaDto ARenglonDto(FilaRenglonFacturaVm fila) => new(
        fila.EsProductoNuevo ? null : fila.Producto?.Id,
        fila.EsProductoNuevo ? new ProductoNuevoDto(
            fila.ProductoNuevoCodigo!, fila.ProductoNuevoNombre!, fila.ProductoNuevoCategoriaId,
            fila.ProductoNuevoUnidadMedidaId, fila.ProductoNuevoPrecioVenta) : null,
        fila.Cantidad, fila.PrecioUnitario, fila.ActualizarPrecioCosto);

    [RelayCommand]
    private void Finalizar() => _navigation.Navegar<StockApp.Presentation.ViewModels.Finanzas.GastosViewModel>();

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<StockApp.Presentation.ViewModels.Finanzas.GastosViewModel>();
}
