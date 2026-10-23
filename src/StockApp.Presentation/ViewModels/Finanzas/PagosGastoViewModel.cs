using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
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
/// Pantalla "Pagos de la factura": lista los pagos (activos y anulados) de un gasto,
/// permite registrar un pago nuevo (sin superar el saldo — lo valida el servidor) y
/// anular pagos existentes. Refresca el gasto tras cada operación para que saldo y
/// estado calculado queden al día.
/// </summary>
public partial class PagosGastoViewModel : ViewModelBase, INotifyDataErrorInfo
{
    private readonly IGastoService        _service;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;
    private readonly AdjuntosPanelViewModel _adjuntosPanel;

    public AdjuntosPanelViewModel AdjuntosPanel => _adjuntosPanel;

    /// <summary>
    /// Un 403 refrescó los permisos con la pantalla abierta: además de los Puede* propios (base), se reenvía al panel de
    /// adjuntos. El shell solo avisa a la pantalla abierta, y el OnPropertyChanged(string.Empty) de la base relee
    /// AdjuntosPanel (la misma instancia), así que los gates del panel no se re-evaluarían.
    /// </summary>
    public override void NotificarPermisosActualizados()
    {
        base.NotificarPermisosActualizados();
        _adjuntosPanel.NotificarPermisosActualizados();
        // Los comandos también: ocultar el botón no alcanza (Enter, un binding viejo): sin el permiso no ejecutan.
        RegistrarPagoCommand.NotifyCanExecuteChanged();
        AnularPagoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Gatea "Registrar pago" y "Anular pago" (bugfix 2026-08-15): ninguno de los dos tenía
    /// gating por permiso — GastoService.RegistrarPagoAsync/AnularPagoAsync exigen
    /// Permisos.RegistrarPagos sin condición, así que un Operador con VerFinanzas (alcanza para
    /// entrar a esta pantalla) pero sin RegistrarPagos llenaba el formulario y recién al guardar
    /// se comía un 403. Esta pantalla es alcanzable por DOS caminos (GastosView, ya gateado, y
    /// CalendarioPagosView, sin gatear) — el gating va acá porque acá vive la ACCIÓN, cubriendo
    /// ambos caminos de navegación por definición. Mismo patrón que
    /// GastosViewModel.PuedeRegistrarPagos / DocumentoFormViewModel.PuedeAnular: se OCULTA
    /// (IsVisible), nunca se deshabilita. Un 403 dispara ManejadorAccesoRevocado: la base notifica
    /// todos los bindings de la pantalla abierta (ViewModelBase.NotificarPermisosActualizados) y el
    /// gate se oculta sin navegar. PagosGastoViewModel se registra AddTransient y se recrea en cada
    /// navegación, toma el ICurrentSession vigente.
    /// </summary>
    public bool PuedeRegistrarPagos =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.RegistrarPagos);

    /// <summary>Solo lectura por permiso (<see cref="PuedeRegistrarPagos"/>) o por estado del registro (gasto anulado o gasto
    /// POA al contado, cuyo pago manual rechaza el servidor): con cualquiera no se registra ni se anula (convención 36). Getter
    /// sobre la sesión, no un valor calculado una vez: <see cref="NotificarPermisosActualizados"/> lo re-evalúa.</summary>
    public bool PuedeRegistrar => PuedeRegistrarPagos && !EsAnulado && !EsGastoPoaAlContado;

    public bool SoloLectura => !PuedeRegistrar;

    /// <summary>Por qué no se ofrece registrar en un gasto POA al contado: el texto del rechazo del servidor. Null si no aplica;
    /// un gasto anulado ya tiene su propia alerta ("anulalo" sobre un gasto anulado confunde).</summary>
    public string? AvisoGastoPoa => EsGastoPoaAlContado && !EsAnulado ? CajaYLiteral.MensajePagoManualEnGastoPoa : null;

    public const string MensajeSinPermisoPagos =
        "No tenés permiso para ver finanzas, así que no se pueden abrir los pagos de la factura.";
    public const string MensajeFechaObligatoria = "La fecha del pago es obligatoria.";
    public const string MensajeMontoObligatorio = "El monto del pago es obligatorio.";
    public const string MensajeMontoInvalido = "El monto del pago no es un número válido.";
    public const string MensajeFuenteObligatoria = "Elegí la fuente de financiamiento.";
    public const string AvisoPagoAnulado = "Pago anulado: los adjuntos son solo de consulta";

    protected override bool AvisaSinConexionEnLinea => true;

    protected override void AlPerderConexion() => Pagos.Clear();

    private int _gastoId;
    private Action _volver;

    [ObservableProperty] private string _tituloGasto = string.Empty;
    [ObservableProperty] private decimal _montoTotal;
    [ObservableProperty] private decimal _totalPagado;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoSaldo))]
    private decimal _saldoPendiente;

    /// <summary>El saldo del resumen: un gasto anulado no tiene saldo, se muestra "—" (GATE 1 4e).</summary>
    public string TextoSaldo => EsAnulado ? "—" : FormatoEsUy.Moneda(SaldoPendiente);
    [ObservableProperty] private string _estado = string.Empty;

    /// <summary>Gasto anulado: la pantalla se puede consultar pero no registra ni anula pagos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeRegistrar), nameof(SoloLectura), nameof(TextoSaldo), nameof(AvisoGastoPoa))]
    [NotifyCanExecuteChangedFor(nameof(RegistrarPagoCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnularPagoCommand))]
    private bool _esAnulado;

    /// <summary>Mismo criterio que GastoService.RegistrarPagoAsync: <see cref="Gasto.PagoAutomatico"/> (ignora Activo) en caja POA.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeRegistrar), nameof(SoloLectura), nameof(AvisoGastoPoa))]
    [NotifyCanExecuteChangedFor(nameof(RegistrarPagoCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnularPagoCommand))]
    private bool _esGastoPoaAlContado;

    /// <summary>De dónde se llegó: lo muestra el breadcrumb y es a donde vuelve <see cref="VolverCommand"/>.</summary>
    [ObservableProperty] private string _textoOrigen = "Gastos y facturas";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarPagoCommand))]
    private DateTime? _fechaSeleccionada = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarPagoCommand))]
    private string _montoTexto = string.Empty;

    [ObservableProperty] private string? _nota;

    /// <summary>Literal que paga el pago. Arranca nulo y no se preselecciona: el usuario lo elige (el pago manual sale
    /// siempre de la caja Local, así que no hay selector de caja). Tras registrar se limpia: cada pago vuelve a elegirlo.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarPagoCommand))]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    /// <summary>Literales activos de la caja Local.</summary>
    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();

    /// <summary>La caja Local no tiene literales activos: se avisa dónde configurarlos. Null si hay al menos uno.</summary>
    [ObservableProperty] private string? _avisoSinLiterales;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarAdjuntos), nameof(SinPagoSeleccionado))]
    private PagoGasto? _pagoSeleccionado;

    /// <summary>Con un pago elegido (activo o anulado) se muestra el panel de adjuntos; el de un pago anulado, en solo lectura.</summary>
    public bool MostrarAdjuntos => PagoSeleccionado is not null;

    public bool SinPagoSeleccionado => PagoSeleccionado is null;

    public ObservableCollection<PagoGasto> Pagos { get; } = new();

    /// <summary>Lo que bindea la grilla: con una ObservableCollection cruda el orden por click no funciona (Avalonia 12).</summary>
    public DataGridCollectionView PagosView { get; }

    public PagosGastoViewModel(
        IGastoService service,
        IFuenteFinanciamientoService fuentesService,
        ICurrentSession session,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        AdjuntosPanelViewModel adjuntosPanel)
    {
        _service       = service;
        _fuentesService = fuentesService;
        _session       = session;
        _navigation    = navigation;
        _confirmacion  = confirmacion;
        _adjuntosPanel = adjuntosPanel;
        _volver        = () => _navigation.Navegar<GastosViewModel>();
        PagosView      = new DataGridCollectionView(Pagos);
    }

    // Fire-and-forget consciente: el panel de adjuntos se carga async sin bloquear
    // la selección del pago en la lista (mismo patrón que GastoFormViewModel).
    partial void OnPagoSeleccionadoChanged(PagoGasto? value)
    {
        if (value is null)
            return;

        // Q-2 B: el pago anulado MUESTRA sus adjuntos, solo para consulta (Agregar y Quitar ocultos, con su aviso).
        _adjuntosPanel.SoloLectura = !value.Activo;
        _adjuntosPanel.AvisoSoloLectura = value.Activo ? null : AvisoPagoAnulado;
        _ = _adjuntosPanel.InicializarAsync(null, value.Id);
    }

    /// <summary>Recibe el gasto de la grilla. Corre ANTES de InicializarAsync.</summary>
    public void CargarParaGasto(Gasto gasto) => _gastoId = gasto.Id;

    /// <summary>
    /// Permite que el ORIGEN de la navegación configure a dónde vuelve <see cref="Volver"/>.
    /// Por defecto (si no se llama) vuelve a <see cref="GastosViewModel"/>.
    /// </summary>
    public void ConfigurarVolver(Action volver, string textoOrigen = "Gastos y facturas")
    {
        _volver = volver;
        TextoOrigen = textoOrigen;
    }

    /// <summary>
    /// Trae el gasto fresco del servidor. La dispara la View (DataContextChanged). Va por EjecutarCargaProtegidaAsync: un 403
    /// deja <c>SinPermiso</c> (el aviso global lo dio AuthTokenHandler) y un servidor caído deja <c>SinConexion</c> con
    /// Reintentar, sin escalar al modal global.
    /// </summary>
    public async Task InicializarAsync()
    {
        try
        {
            await EjecutarCargaProtegidaAsync(CargarAsync, MensajeSinPermisoPagos);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    /// <summary>La carga protegida: el gasto y los literales de la caja Local, juntos (un solo 403 / sin conexión).</summary>
    private async Task CargarAsync()
    {
        await RefrescarAsync();

        var activas = await _fuentesService.ListarActivasAsync();
        LiteralesPorCaja.Refrescar(FuentesDisponibles, activas, Caja.Local);
        AvisoSinLiterales = LiteralesPorCaja.Aviso(Caja.Local, FuentesDisponibles.Count);
    }

    /// <summary>Trae el gasto fresco; tras registrar o anular se llama solo esto.</summary>
    private async Task RefrescarAsync()
    {
        var gasto = await _service.ObtenerPorIdAsync(_gastoId);

        TituloGasto = $"{gasto.Detalle} — factura {gasto.NumeroFactura ?? "s/n"} " +
                      $"({gasto.Proveedor?.Nombre ?? $"proveedor {gasto.ProveedorId}"})";
        MontoTotal     = gasto.MontoTotal;
        TotalPagado    = gasto.TotalPagado;
        SaldoPendiente = gasto.SaldoPendiente;
        Estado         = gasto.CalcularEstado(DateTime.UtcNow).ToString();
        EsAnulado      = !gasto.Activo;
        EsGastoPoaAlContado = gasto.PagoAutomatico is { Caja: Caja.Poa };

        // El pago elegido deja de existir con la lista nueva: se suelta y la card de adjuntos vuelve a pedir uno.
        PagoSeleccionado = null;
        Pagos.Clear();
        foreach (var pago in gasto.Pagos)
            Pagos.Add(pago);
    }

    // ── Errores por campo (Q-4 B, convención 31): Fecha y Monto ─────────────────────────────────────────

    /// <summary>Propiedades bindeadas que se validan en el cliente: ErrorsChanged se levanta con estos nombres.</summary>
    private static readonly string[] CamposValidados = [nameof(FechaSeleccionada), nameof(MontoTexto), nameof(FuenteSeleccionada)];

    /// <summary>Campos que el usuario ya tocó (cambió el valor, salió del campo o intentó registrar).</summary>
    private readonly HashSet<string> _tocados = new();

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => CamposValidados.Any(c => ErrorDe(c) is not null);

    /// <summary>El error que la vista muestra debajo del campo: el de su regla, solo si el campo está tocado.</summary>
    public string? ErrorDe(string propiedad) => _tocados.Contains(propiedad) ? ReglaDe(propiedad) : null;

    public IEnumerable GetErrors(string? propertyName)
        => propertyName is not null && ErrorDe(propertyName) is { } error ? new[] { error } : Array.Empty<string>();

    /// <summary>Marca el campo como tocado sin cambiar su valor (la vista lo llama al salir del campo).</summary>
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

    partial void OnFechaSeleccionadaChanged(DateTime? value) => AlCambiarCampo(nameof(FechaSeleccionada));
    partial void OnMontoTextoChanged(string value) => AlCambiarCampo(nameof(MontoTexto));
    partial void OnFuenteSeleccionadaChanged(FuenteFinanciamiento? value) => AlCambiarCampo(nameof(FuenteSeleccionada));

    private string? ReglaDe(string propiedad) => propiedad switch
    {
        nameof(FechaSeleccionada) => FechaSeleccionada is null ? MensajeFechaObligatoria : null,
        nameof(MontoTexto) => ReglaDelMonto(),
        nameof(FuenteSeleccionada) => FuenteSeleccionada is null ? MensajeFuenteObligatoria : null,
        _ => null,
    };

    /// <summary>Vacío es "obligatorio"; ilegible es el detalle de <see cref="FormatoEsUy.TryParseDecimal"/> ("Usá coma para los
    /// decimales: 1,5"), sin prefijo para que entre en el campo (GATE 1 4e); sin detalle, el mensaje genérico.</summary>
    private string? ReglaDelMonto()
    {
        if (string.IsNullOrWhiteSpace(MontoTexto))
            return MensajeMontoObligatorio;
        if (FormatoEsUy.TryParseDecimal(MontoTexto, out _, out var detalle))
            return null;
        return string.IsNullOrEmpty(detalle) || detalle == FormatoEsUy.MensajeNoEsNumero ? MensajeMontoInvalido : detalle;
    }

    /// <summary>Después de registrar el formulario queda vacío y limpio: ningún campo en rojo hasta que el usuario lo toque.</summary>
    private void SoltarTocados()
    {
        _tocados.Clear();
        foreach (var campo in CamposValidados)
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(campo));
    }

    private bool PuedeRegistrarPago() => PuedeRegistrar && CamposValidados.All(c => ReglaDe(c) is null);

    public IAsyncRelayCommand RegistrarPagoCommand => field ??= ComandoDeGuardado(RegistrarPagoAsync, PuedeRegistrarPago);

    private async Task RegistrarPagoAsync()
    {
        MensajeError = null;

        foreach (var campo in CamposValidados)
            MarcarTocado(campo);
        if (CamposValidados.Any(c => ReglaDe(c) is not null))
            return;

        FormatoEsUy.TryParseDecimal(MontoTexto, out var monto, out _);

        try
        {
            await _service.RegistrarPagoAsync(new PagoGasto
            {
                GastoId = _gastoId,
                Fecha = DateTime.SpecifyKind(FechaSeleccionada!.Value.Date, DateTimeKind.Utc),
                Monto = monto,
                Nota = string.IsNullOrWhiteSpace(Nota) ? null : Nota,
                Caja = Caja.Local,
                FuenteFinanciamientoId = FuenteSeleccionada!.Id,
            });

            MontoTexto = string.Empty;
            Nota = null;
            FuenteSeleccionada = null;
            SoltarTocados();
            await RefrescarAsync();
        }
        catch (Exception ex)
            when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    /// <summary>Texto del diálogo de confirmación de anular (lo usan también los datos fijos de la comparación).</summary>
    public static string MensajeConfirmarAnular(PagoGasto pago) =>
        $"¿Confirma anular el pago de {FormatoEsUy.Decimal(pago.Monto)} del {pago.Fecha:dd/MM/yyyy}?";

    private bool PuedeAnularPago(PagoGasto? pago) => pago is { Activo: true } && PuedeRegistrar;

    [RelayCommand(CanExecute = nameof(PuedeAnularPago))]
    private async Task AnularPagoAsync(PagoGasto pago)
    {
        var confirmar = await _confirmacion.PreguntarAsync(MensajeConfirmarAnular(pago));
        if (!confirmar) return;

        try
        {
            await _service.AnularPagoAsync(_gastoId, pago.Id);
            await RefrescarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private void Volver() => _volver();
}
