using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Domain.Formato;
using StockApp.Presentation.Helpers;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Formulario de alta / edición de un gasto (factura). Tres modos:
/// alta directa desde Finanzas, edición desde la grilla, y alta DESDE la entrada de stock
/// (CargarDesdeEntrada: monto precargado con cantidad × precio pero editable — la factura
/// real puede traer fletes o redondeos — y el movimiento queda vinculado al guardar; si la
/// factura ya existe para ese proveedor, ofrece asociar los movimientos a la existente).
/// </summary>
public partial class GastoFormViewModel : ViewModelBase, INotifyDataErrorInfo
{
    private readonly IGastoService                _service;
    private readonly ICurrentSession              _session;
    private readonly IProveedorService            _proveedoresService;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly IRubroGastoService           _rubrosService;
    private readonly ILineaPoaService             _lineasService;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;
    private readonly AdjuntosPanelViewModel       _adjuntosPanel;

    public AdjuntosPanelViewModel AdjuntosPanel => _adjuntosPanel;

    /// <summary>
    /// Un 403 refrescó los permisos con la pantalla abierta: además de los Puede* propios (base), se reenvía al panel de
    /// adjuntos. El shell solo avisa a la pantalla abierta, y el OnPropertyChanged(string.Empty) de la base relee
    /// AdjuntosPanel (la misma instancia), así que los gates del panel no se re-evaluarían.
    /// </summary>
    public override void NotificarPermisosActualizados()
    {
        base.NotificarPermisosActualizados();
        GuardarCommand.NotifyCanExecuteChanged();   // la base notifica propiedades, no comandos
        _adjuntosPanel.NotificarPermisosActualizados();
    }

    /// <summary>
    /// Gatea el botón "Guardar" (bugfix 2026-08-15): no tenía NINGÚN gating por permiso.
    /// GastoService.AltaAsync/ModificarAsync exigen Permisos.RegistrarGastos sin condición — se
    /// llega a este formulario desde GastosView ("Nuevo gasto"/"Editar"), que solo exige
    /// VerFinanzas. Mismo patrón que GastosViewModel.PuedeRegistrarGastos.
    /// </summary>
    public bool PuedeRegistrarGastos =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.RegistrarGastos);

    /// <summary>
    /// Formulario de solo lectura: sin permiso de registrar gastos o con el gasto anulado (el servidor no deja modificar un
    /// gasto anulado). Getter sobre la sesión: la revocación en caliente lo re-evalúa (la base notifica todo).
    /// </summary>
    public bool SoloLectura => !PuedeRegistrarGastos || EsAnulado;

    /// <summary>La sección Adjuntos no se muestra con el gasto anulado: su adjunto queda en la base, sin baja, pero no se
    /// ve ni se puede agregar otro (el servidor además lo rechaza).</summary>
    public bool MostrarSeccionAdjuntos => !EsAnulado;

    /// <summary>La condición de pago de un gasto registrado no cambia (el servidor lo rechaza): en edición el tilde queda
    /// deshabilitado; en alta y "desde entrada" se elige.</summary>
    public bool PuedeCambiarCondicionPago => !EsEdicion && !SoloLectura;

    /// <summary>Explicación debajo del tilde deshabilitado en edición.</summary>
    public const string TextoCondicionPagoFija = "La condición de pago no se cambia: anulá el gasto y cargalo de nuevo";

    private int _idEdicion;
    private Gasto? _gastoParaEditar;
    private int? _movimientoVinculado;   // modo "desde entrada de stock"
    private List<FuenteFinanciamiento> _fuentesActivas = new();
    private FuenteFinanciamiento? _fuenteHistorica;
    private bool _fuentesCargadas;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private Proveedor? _proveedorSeleccionado;

    [ObservableProperty] private string? _numeroFactura;
    [ObservableProperty] private string? _numeroOrden;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _detalle = string.Empty;

    [ObservableProperty] private string? _destino;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private DateTime? _fechaSeleccionada = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _montoTexto = string.Empty;

    /// <summary>Sin default: la caja del pago de contado se elige explícitamente (el crédito no la pide).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private Caja? _cajaSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    /// <summary>Aviso cuando la caja elegida no tiene literales activos asignados; null si los tiene, no hay caja o es crédito.</summary>
    [ObservableProperty] private string? _avisoSinLiterales;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private RubroGasto? _rubroSeleccionado;

    [ObservableProperty] private LineaPoa? _lineaPoaSeleccionada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarCajaYFuente))]
    [NotifyPropertyChangedFor(nameof(MostrarLineaPoa))]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _esCredito;

    /// <summary>Caja y literal son del pago de contado: a crédito no se piden (cada pago elige los suyos).</summary>
    public bool MostrarCajaYFuente => !EsCredito;

    /// <summary>La línea POA solo se ofrece en un contado de la caja POA (el servidor rechaza una línea POA en crédito o
    /// en caja Local). No se deduce la caja desde la línea: se elige primero la caja.</summary>
    public bool MostrarLineaPoa => !EsCredito && CajaSeleccionada == Caja.Poa;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private DateTime? _fechaVencimientoSeleccionada;

    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    [NotifyPropertyChangedFor(nameof(PuedeCambiarCondicionPago))]
    private bool _esEdicion;

    /// <summary>El gasto que se editó está anulado (Activo == false): el formulario lo muestra en solo lectura.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoloLectura))]
    [NotifyPropertyChangedFor(nameof(MostrarSeccionAdjuntos))]
    [NotifyPropertyChangedFor(nameof(PuedeCambiarCondicionPago))]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _esAnulado;

    public string Titulo => EsEdicion
        ? "Editar gasto"
        : _movimientoVinculado is null ? "Nuevo gasto" : "Asociar factura a la entrada";

    public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();
    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public IReadOnlyList<Caja> Cajas => CajaExtensions.Todas;
    public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
    public ObservableCollection<LineaPoa> LineasPoaDisponibles { get; } = new();

    public const string MensajeProveedorObligatorio = "Elegí el proveedor.";
    public const string MensajeFechaObligatoria = "La fecha del gasto es obligatoria.";
    public const string MensajeDetalleObligatorio = "El detalle es obligatorio.";
    public const string MensajeMontoObligatorio = "El monto total es obligatorio.";
    public const string MensajeFuenteObligatoria = "Elegí la fuente de financiamiento.";
    public const string MensajeCajaObligatoria = "Elegí la caja.";
    public const string MensajeRubroObligatorio = "Elegí el rubro.";
    public const string MensajeVencimientoObligatorio = "Un gasto a crédito exige fecha de vencimiento.";

    /// <summary>Propiedades bindeadas que se validan en el cliente. ErrorsChanged se levanta con estos nombres: son los
    /// que el binding de cada control le pregunta a GetErrors.</summary>
    private static readonly string[] CamposValidados =
    [
        nameof(ProveedorSeleccionado), nameof(FechaSeleccionada), nameof(Detalle), nameof(MontoTexto),
        nameof(CajaSeleccionada), nameof(FuenteSeleccionada), nameof(RubroSeleccionado),
        nameof(FechaVencimientoSeleccionada),
    ];

    /// <summary>Campos que el usuario ya tocó (cambió el valor, salió del campo o intentó guardar). Un campo sin tocar no
    /// muestra su error: el formulario recién abierto no aparece en rojo.</summary>
    private readonly HashSet<string> _tocados = new();

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => CamposValidados.Any(c => ErrorDe(c) is not null);

    /// <summary>El error que la vista muestra debajo del campo: el de su regla, solo si el campo está tocado.</summary>
    public string? ErrorDe(string propiedad) => _tocados.Contains(propiedad) ? ReglaDe(propiedad) : null;

    public IEnumerable GetErrors(string? propertyName)
        => propertyName is not null && ErrorDe(propertyName) is { } error ? new[] { error } : Array.Empty<string>();

    /// <summary>Marca el campo como tocado sin cambiar su valor (la vista lo llama al salir de un campo).</summary>
    public void MarcarTocado(string propiedad)
    {
        if (_tocados.Add(propiedad))
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    private void AlCambiarCampo(string propiedad)
    {
        _tocados.Add(propiedad);
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    partial void OnProveedorSeleccionadoChanged(Proveedor? value) => AlCambiarCampo(nameof(ProveedorSeleccionado));
    partial void OnFechaSeleccionadaChanged(DateTime? value) => AlCambiarCampo(nameof(FechaSeleccionada));
    partial void OnDetalleChanged(string value) => AlCambiarCampo(nameof(Detalle));
    partial void OnMontoTextoChanged(string value) => AlCambiarCampo(nameof(MontoTexto));
    partial void OnFuenteSeleccionadaChanged(FuenteFinanciamiento? value) => AlCambiarCampo(nameof(FuenteSeleccionada));
    partial void OnRubroSeleccionadoChanged(RubroGasto? value) => AlCambiarCampo(nameof(RubroSeleccionado));
    partial void OnFechaVencimientoSeleccionadaChanged(DateTime? value) => AlCambiarCampo(nameof(FechaVencimientoSeleccionada));

    partial void OnCajaSeleccionadaChanged(Caja? value)
    {
        AlCambiarCampo(nameof(CajaSeleccionada));
        OnPropertyChanged(nameof(MostrarLineaPoa));
        RefrescarFuentes();
        if (FuenteSeleccionada is not null && FuenteSeleccionada.Caja != value)
            FuenteSeleccionada = null;
        if (value != Caja.Poa)
            LineaPoaSeleccionada = null;
    }

    /// <summary>El vencimiento solo es regla a crédito y caja y literal solo a contado: tildar o destildar cambia sus
    /// errores sin tocar los campos. Una línea POA no sobrevive a un crédito.</summary>
    partial void OnEsCreditoChanged(bool value)
    {
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(FechaVencimientoSeleccionada)));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(CajaSeleccionada)));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(FuenteSeleccionada)));
        ActualizarAvisoSinLiterales();
        if (value)
            LineaPoaSeleccionada = null;
    }

    /// <summary>Rellena el combo de fuentes con los literales de la caja elegida (sin caja, vacío).</summary>
    private void RefrescarFuentes()
    {
        if (CajaSeleccionada is not { } caja)
            FuentesDisponibles.Clear();
        else
            LiteralesPorCaja.Refrescar(FuentesDisponibles, _fuentesActivas, caja, _fuenteHistorica);
        ActualizarAvisoSinLiterales();
    }

    /// <summary>Antes de que lleguen las fuentes no avisa: la lista vacía todavía no quiere decir "sin literales". A crédito
    /// tampoco: no se pide literal.</summary>
    private void ActualizarAvisoSinLiterales() =>
        AvisoSinLiterales = _fuentesCargadas && !EsCredito && CajaSeleccionada is { } caja
            ? LiteralesPorCaja.Aviso(caja, _fuentesActivas.Count(f => f.Caja == caja))
            : null;

    /// <summary>
    /// Reglas del cliente, las de antes: proveedor, fuente y rubro elegidos; detalle y monto no vacíos; monto numérico en
    /// es-UY; fecha obligatoria; vencimiento obligatorio solo a crédito. "El monto debe ser mayor a cero", los maestros dados
    /// de baja, la factura duplicada y el cambio de condición de pago son del servidor y salen por la Alerta de error.
    /// </summary>
    private string? ReglaDe(string propiedad) => propiedad switch
    {
        nameof(ProveedorSeleccionado) => ProveedorSeleccionado is null ? MensajeProveedorObligatorio : null,
        nameof(FechaSeleccionada) => FechaSeleccionada is null ? MensajeFechaObligatoria : null,
        nameof(Detalle) => string.IsNullOrWhiteSpace(Detalle) ? MensajeDetalleObligatorio : null,
        nameof(MontoTexto) => ReglaDelMonto(),
        nameof(CajaSeleccionada) => !EsCredito && CajaSeleccionada is null ? MensajeCajaObligatoria : null,
        nameof(FuenteSeleccionada) => !EsCredito && FuenteSeleccionada is null ? MensajeFuenteObligatoria : null,
        nameof(RubroSeleccionado) => RubroSeleccionado is null ? MensajeRubroObligatorio : null,
        nameof(FechaVencimientoSeleccionada) => EsCredito && FechaVencimientoSeleccionada is null ? MensajeVencimientoObligatorio : null,
        _ => null,
    };

    private string? ReglaDelMonto()
    {
        if (string.IsNullOrWhiteSpace(MontoTexto))
            return MensajeMontoObligatorio;
        return FormatoEsUy.TryParseDecimal(MontoTexto, out _, out var detalle)
            ? null
            : MensajeNumeroInvalido.Armar("El monto total no es un número válido.", detalle);
    }

    public GastoFormViewModel(
        IGastoService service,
        ICurrentSession session,
        IProveedorService proveedoresService,
        IFuenteFinanciamientoService fuentesService,
        IRubroGastoService rubrosService,
        ILineaPoaService lineasService,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        AdjuntosPanelViewModel adjuntosPanel)
    {
        _service            = service;
        _session            = session;
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
        FechaSeleccionada = gasto.Fecha;
        MontoTexto       = FormatoEsUy.Decimal(gasto.MontoTotal);
        EsCredito        = gasto.CondicionPago == CondicionPago.Credito;
        FechaVencimientoSeleccionada = gasto.FechaVencimiento;
        CajaSeleccionada = gasto.PagoAutomatico?.Caja;
        EsAnulado        = !gasto.Activo;
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
        MontoTexto = FormatoEsUy.Decimal(montoSugerido);
    }

    /// <summary>Estado "Sin permiso" del formulario: la carga de los combos (que exigen finanzas.ver) respondió 403.</summary>
    public const string MensajeSinPermisoFormulario = "No tenés permiso para ver finanzas, así que no se puede abrir el gasto.";

    /// <summary>Sin servidor al abrir, el formulario avisa en línea con Reintentar en vez del modal global: si no, los
    /// combos quedan vacíos para siempre.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>
    /// Carga los combos (y en edición resuelve las selecciones del gasto). La dispara la View (DataContextChanged) y la
    /// repite Reintentar. Protegida: un 403 deja SinPermiso (el aviso global ya lo dio AuthTokenHandler) y un servidor
    /// caído deja SinConexion, sin modal.
    /// </summary>
    public async Task InicializarAsync()
    {
        await EjecutarCargaProtegidaAsync(async () =>
        {
            // ListarActivasAsync, no ListarTodosAsync (bugfix 2026-08-15): ListarTodosAsync exige GestionarTablasMaestras
            // y un Operador de Finanzas solo tiene VerFinanzas. Los cuatro vienen filtrados a Activo=true del servidor.
            var proveedores = await _proveedoresService.ListarActivasAsync();
            ProveedoresDisponibles.Clear();
            foreach (var p in proveedores)
                ProveedoresDisponibles.Add(p);

            _fuentesActivas = (await _fuentesService.ListarActivasAsync()).ToList();
            _fuentesCargadas = true;

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
                // Resuelve las selecciones por Id contra los combos; si un maestro fue dado de baja después, cae al objeto
                // de la nav para no perder el dato histórico.
                ProveedorSeleccionado =
                    ProveedoresDisponibles.FirstOrDefault(p => p.Id == _gastoParaEditar.ProveedorId)
                    ?? Agregar(ProveedoresDisponibles, _gastoParaEditar.Proveedor);
                // La fuente de un gasto de contado es la de su pago automático; un crédito no tiene.
                var automatico = _gastoParaEditar.PagoAutomatico;
                if (automatico is not null)
                {
                    // El literal puede estar dado de baja: sigue en el combo de SU caja y elegido.
                    _fuenteHistorica =
                        _fuentesActivas.FirstOrDefault(f => f.Id == automatico.FuenteFinanciamientoId)
                        ?? automatico.FuenteFinanciamiento;
                    RefrescarFuentes();
                    FuenteSeleccionada = FuentesDisponibles.FirstOrDefault(f => f.Id == _fuenteHistorica?.Id);
                }
                else
                    RefrescarFuentes();
                RubroSeleccionado =
                    RubrosDisponibles.FirstOrDefault(r => r.Id == _gastoParaEditar.RubroGastoId)
                    ?? Agregar(RubrosDisponibles, _gastoParaEditar.RubroGasto);
                if (_gastoParaEditar.LineaPoaId is not null)
                    LineaPoaSeleccionada =
                        LineasPoaDisponibles.FirstOrDefault(l => l.Id == _gastoParaEditar.LineaPoaId)
                        ?? Agregar(LineasPoaDisponibles, _gastoParaEditar.LineaPoa);
            }
            else
                RefrescarFuentes();
        }, MensajeSinPermisoFormulario);
        GuardarCommand.NotifyCanExecuteChanged();
    }

    private static T? Agregar<T>(ObservableCollection<T> coleccion, T? item) where T : class
    {
        if (item is not null)
            coleccion.Add(item);
        return item;
    }

    /// <summary>Guardar se habilita con todas las reglas del cliente cumplidas (estén o no tocados los campos) y con los
    /// datos de finanzas a la vista.</summary>
    private bool PuedeGuardar() => !SinPermiso && !SoloLectura && CamposValidados.All(c => ReglaDe(c) is null);

    public IAsyncRelayCommand GuardarCommand => field ??= ComandoDeGuardado(GuardarAsync, PuedeGuardar);

    private async Task GuardarAsync()
    {
        MensajeError = null;

        foreach (var campo in CamposValidados)
            MarcarTocado(campo);
        if (CamposValidados.Any(c => ReglaDe(c) is not null))
            return;

        FormatoEsUy.TryParseDecimal(MontoTexto, out var monto, out _);

        var gasto = new Gasto
        {
            Id = EsEdicion ? _idEdicion : 0,
            ProveedorId = ProveedorSeleccionado!.Id,
            NumeroFactura = string.IsNullOrWhiteSpace(NumeroFactura) ? null : NumeroFactura!.Trim(),
            NumeroOrden = string.IsNullOrWhiteSpace(NumeroOrden) ? null : NumeroOrden!.Trim(),
            Detalle = Detalle,
            Destino = string.IsNullOrWhiteSpace(Destino) ? null : Destino,
            Fecha = DateTime.SpecifyKind(FechaSeleccionada!.Value.Date, DateTimeKind.Utc),
            MontoTotal = monto,
            RubroGastoId = RubroSeleccionado!.Id,
            LineaPoaId = LineaPoaSeleccionada?.Id,
            CondicionPago = EsCredito ? CondicionPago.Credito : CondicionPago.Contado,
            FechaVencimiento = EsCredito
                ? DateTime.SpecifyKind(FechaVencimientoSeleccionada!.Value.Date, DateTimeKind.Utc)
                : null,
        };

        // Caja y literal del pago de contado, los dos elegidos explícitamente: solo a contado (un crédito elige los
        // suyos en cada pago).
        var pagoContado = EsCredito
            ? null
            : new PagoContadoDto(CajaSeleccionada!.Value, FuenteSeleccionada!.Id);

        try
        {
            var resultado = EsEdicion
                ? await _service.ModificarAsync(gasto, pagoContado)
                : await _service.AltaAsync(gasto, pagoContado,
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
                gasto.ProveedorId, gasto.NumeroFactura, gasto.NumeroOrden);
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
