using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Formulario de alta / edición de un gasto (factura). Tres modos:
/// alta directa desde Finanzas, edición desde la grilla, y alta DESDE la entrada de stock
/// (CargarDesdeEntrada: monto precargado con cantidad × precio pero editable — la factura
/// real puede traer fletes o redondeos — y el movimiento queda vinculado al guardar; si la
/// factura ya existe para ese proveedor, ofrece asociar los movimientos a la existente).
/// </summary>
public partial class GastoFormViewModel : ViewModelBase
{
    private readonly IGastoService                _service;
    private readonly IProveedorService            _proveedoresService;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly IRubroGastoService           _rubrosService;
    private readonly ILineaPoaService             _lineasService;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;
    private readonly AdjuntosPanelViewModel       _adjuntosPanel;

    public AdjuntosPanelViewModel AdjuntosPanel => _adjuntosPanel;

    private int _idEdicion;
    private Gasto? _gastoParaEditar;
    private int? _movimientoVinculado;   // modo "desde entrada de stock"

    /// <summary>Cultura FIJA es-UY (patrón MonedaConverter / LineaPoaFormViewModel).</summary>
    private static readonly IFormatProvider CulturaMonto = CrearCulturaMonto();

    private static IFormatProvider CrearCulturaMonto()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo
            {
                NumberDecimalSeparator = ",",
                NumberGroupSeparator = ".",
            };
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

    [ObservableProperty] private DateTimeOffset? _fechaSeleccionada = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _montoTexto = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private RubroGasto? _rubroSeleccionado;

    [ObservableProperty] private LineaPoa? _lineaPoaSeleccionada;

    [ObservableProperty] private bool _esCredito;
    [ObservableProperty] private DateTimeOffset? _fechaVencimientoSeleccionada;

    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion
        ? "Editar gasto"
        : _movimientoVinculado is null ? "Nuevo gasto" : "Asociar factura a la entrada";

    public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();
    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
    public ObservableCollection<LineaPoa> LineasPoaDisponibles { get; } = new();

    public GastoFormViewModel(
        IGastoService service,
        IProveedorService proveedoresService,
        IFuenteFinanciamientoService fuentesService,
        IRubroGastoService rubrosService,
        ILineaPoaService lineasService,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        AdjuntosPanelViewModel adjuntosPanel)
    {
        _service            = service;
        _proveedoresService = proveedoresService;
        _fuentesService     = fuentesService;
        _rubrosService      = rubrosService;
        _lineasService      = lineasService;
        _navigation         = navigation;
        _confirmacion       = confirmacion;
        _adjuntosPanel      = adjuntosPanel;
    }

    /// <summary>Modo edición. Corre ANTES de InicializarAsync (contrato de LineaPoaFormViewModel).</summary>
    public void CargarParaEditar(Gasto gasto)
    {
        _idEdicion       = gasto.Id;
        _gastoParaEditar = gasto;
        NumeroFactura    = gasto.NumeroFactura;
        NumeroOrden      = gasto.NumeroOrden;
        Detalle          = gasto.Detalle;
        Destino          = gasto.Destino;
        FechaSeleccionada = new DateTimeOffset(DateTime.SpecifyKind(gasto.Fecha, DateTimeKind.Utc));
        MontoTexto       = gasto.MontoTotal.ToString("N2", CulturaMonto);
        EsCredito        = gasto.CondicionPago == CondicionPago.Credito;
        FechaVencimientoSeleccionada = gasto.FechaVencimiento is null
            ? null : new DateTimeOffset(DateTime.SpecifyKind(gasto.FechaVencimiento.Value, DateTimeKind.Utc));
        EsEdicion        = true;

        // Fire-and-forget consciente: el panel de adjuntos se carga async sin bloquear
        // la apertura del formulario (CargarParaEditar es sincrónico).
        _ = _adjuntosPanel.InicializarAsync(gasto.Id, null);
    }

    /// <summary>
    /// Modo "desde entrada de stock" (spec §5): precarga el monto sugerido
    /// (cantidad × precio unitario) EDITABLE y recuerda el movimiento a vincular.
    /// </summary>
    public void CargarDesdeEntrada(int movimientoId, decimal montoSugerido)
    {
        _movimientoVinculado = movimientoId;
        MontoTexto = montoSugerido.ToString("N2", CulturaMonto);
    }

    /// <summary>Carga los combos. La dispara la View (DataContextChanged).</summary>
    public async Task InicializarAsync()
    {
        var proveedores = await _proveedoresService.ListarTodosAsync();
        ProveedoresDisponibles.Clear();
        foreach (var p in proveedores.Where(p => p.Activo))
            ProveedoresDisponibles.Add(p);

        var fuentes = await _fuentesService.ListarActivasAsync();
        FuentesDisponibles.Clear();
        foreach (var f in fuentes)
            FuentesDisponibles.Add(f);

        var rubros = await _rubrosService.ListarActivosAsync();
        RubrosDisponibles.Clear();
        foreach (var r in rubros)
            RubrosDisponibles.Add(r);

        var lineas = await _lineasService.ListarActivasAsync();
        LineasPoaDisponibles.Clear();
        foreach (var l in lineas)
            LineasPoaDisponibles.Add(l);

        if (_gastoParaEditar is not null)
        {
            // Resuelve las selecciones por Id contra los combos; si un maestro fue dado
            // de baja después, cae al objeto de la nav para no perder el dato histórico.
            ProveedorSeleccionado =
                ProveedoresDisponibles.FirstOrDefault(p => p.Id == _gastoParaEditar.ProveedorId)
                ?? Agregar(ProveedoresDisponibles, _gastoParaEditar.Proveedor);
            FuenteSeleccionada =
                FuentesDisponibles.FirstOrDefault(f => f.Id == _gastoParaEditar.FuenteFinanciamientoId)
                ?? Agregar(FuentesDisponibles, _gastoParaEditar.FuenteFinanciamiento);
            RubroSeleccionado =
                RubrosDisponibles.FirstOrDefault(r => r.Id == _gastoParaEditar.RubroGastoId)
                ?? Agregar(RubrosDisponibles, _gastoParaEditar.RubroGasto);
            if (_gastoParaEditar.LineaPoaId is not null)
                LineaPoaSeleccionada =
                    LineasPoaDisponibles.FirstOrDefault(l => l.Id == _gastoParaEditar.LineaPoaId)
                    ?? Agregar(LineasPoaDisponibles, _gastoParaEditar.LineaPoa);
        }
    }

    private static T? Agregar<T>(ObservableCollection<T> coleccion, T? item) where T : class
    {
        if (item is not null)
            coleccion.Add(item);
        return item;
    }

    private bool PuedeGuardar()
        => ProveedorSeleccionado is not null
           && FuenteSeleccionada is not null
           && RubroSeleccionado is not null
           && !string.IsNullOrWhiteSpace(Detalle)
           && !string.IsNullOrWhiteSpace(MontoTexto);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;

        if (!decimal.TryParse(
                MontoTexto,
                NumberStyles.Number,           // permite miles "." y decimales "," de es-UY
                CulturaMonto,
                out var monto))
        {
            MensajeError = "El monto total no es un número válido.";
            return;
        }

        if (FechaSeleccionada is null)
        {
            MensajeError = "La fecha del gasto es obligatoria.";
            return;
        }
        if (EsCredito && FechaVencimientoSeleccionada is null)
        {
            MensajeError = "Un gasto a crédito exige fecha de vencimiento.";
            return;
        }

        var gasto = new Gasto
        {
            Id = EsEdicion ? _idEdicion : 0,
            ProveedorId = ProveedorSeleccionado!.Id,
            NumeroFactura = string.IsNullOrWhiteSpace(NumeroFactura) ? null : NumeroFactura!.Trim(),
            NumeroOrden = string.IsNullOrWhiteSpace(NumeroOrden) ? null : NumeroOrden!.Trim(),
            Detalle = Detalle,
            Destino = string.IsNullOrWhiteSpace(Destino) ? null : Destino,
            Fecha = DateTime.SpecifyKind(FechaSeleccionada.Value.Date, DateTimeKind.Utc),
            MontoTotal = monto,
            FuenteFinanciamientoId = FuenteSeleccionada!.Id,
            RubroGastoId = RubroSeleccionado!.Id,
            LineaPoaId = LineaPoaSeleccionada?.Id,
            CondicionPago = EsCredito ? CondicionPago.Credito : CondicionPago.Contado,
            FechaVencimiento = EsCredito
                ? DateTime.SpecifyKind(FechaVencimientoSeleccionada!.Value.Date, DateTimeKind.Utc)
                : null,
        };

        try
        {
            var resultado = EsEdicion
                ? await _service.ModificarAsync(gasto)
                : await _service.AltaAsync(gasto,
                    _movimientoVinculado is null ? null : new[] { _movimientoVinculado.Value });

            if (resultado.AdvertenciaSobregiro is not null)
                await _confirmacion.InformarAsync(resultado.AdvertenciaSobregiro);

            _navigation.Navegar<GastosViewModel>();
        }
        catch (ReglaDeNegocioException ex)
            when (_movimientoVinculado is not null && gasto.NumeroFactura is not null)
        {
            // La factura ya existe (cargada antes desde Finanzas): ofrecer asociar los
            // movimientos a la existente en vez de duplicarla (spec §5.1).
            var existente = await _service.ObtenerPorProveedorYFacturaAsync(
                gasto.ProveedorId, gasto.NumeroFactura);
            if (existente is null)
            {
                MensajeError = ex.Message;
                return;
            }

            var asociar = await _confirmacion.PreguntarAsync(
                $"La factura '{gasto.NumeroFactura}' ya existe para ese proveedor " +
                $"(\"{existente.Detalle}\"). ¿Asociar la entrada de stock a esa factura?");
            if (!asociar)
            {
                MensajeError = ex.Message;
                return;
            }

            try
            {
                await _service.AsociarMovimientosAsync(existente.Id, new[] { _movimientoVinculado.Value });
                _navigation.Navegar<GastosViewModel>();
            }
            catch (Exception ex2) when (ex2 is ReglaDeNegocioException or EntidadNoEncontradaException)
            {
                MensajeError = ex2.Message;
            }
        }
        catch (Exception ex)
            when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<GastosViewModel>();
}
