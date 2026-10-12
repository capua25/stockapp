using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Domain.Formato;
using StockApp.Presentation.Helpers;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Formulario de alta / edición de un ingreso de caja. Montos con cultura FIJA es-UY.</summary>
public partial class IngresoFormViewModel : ViewModelBase
{
    private readonly IIngresoCajaService          _service;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly ICurrentSession              _session;
    private readonly INavigationService           _navigation;

    private int _idEdicion;
    private IngresoCaja? _ingresoParaEditar;

    [ObservableProperty] private DateTime? _fechaSeleccionada = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _concepto = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _montoTexto = string.Empty;

    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar ingreso" : "Nuevo ingreso";

    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();

    /// <summary>
    /// Gatea el botón "Guardar" (bugfix 2026-08-15): antes no tenía ningún gating — se llega
    /// desde IngresosView ("Nuevo ingreso"/"Editar"), que solo exige Permisos.VerFinanzas, pero
    /// IngresoCajaService.AltaAsync/ModificarAsync exigen Permisos.RegistrarIngresos sin
    /// condición. Un Operador con VerFinanzas pero sin RegistrarIngresos llenaba el formulario y
    /// recién al guardar se comía un 403. Se OCULTA (no se deshabilita), mismo patrón que
    /// GastosViewModel.PuedeRegistrarPagos y DocumentoFormViewModel.PuedeAnular/PuedeReabrir:
    /// IsVisible="{Binding Puede*}" en los botones de acción de pantalla del repo, nunca
    /// IsEnabled. Un 403 dispara ManejadorAccesoRevocado: la base notifica todos los bindings de
    /// la pantalla abierta (ViewModelBase.NotificarPermisosActualizados) y el gate se oculta sin
    /// navegar. IngresoFormViewModel se registra AddTransient y se recrea en cada navegación a la
    /// pantalla.
    /// </summary>
    public bool PuedeRegistrarIngresos =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.RegistrarIngresos);

    public IngresoFormViewModel(
        IIngresoCajaService service,
        IFuenteFinanciamientoService fuentesService,
        ICurrentSession session,
        INavigationService navigation)
    {
        _service        = service;
        _fuentesService = fuentesService;
        _session        = session;
        _navigation     = navigation;
    }

    public void CargarParaEditar(IngresoCaja ingreso)
    {
        _idEdicion         = ingreso.Id;
        _ingresoParaEditar = ingreso;
        FechaSeleccionada  = ingreso.Fecha;
        Concepto           = ingreso.Concepto;
        MontoTexto         = FormatoEsUy.Decimal(ingreso.Monto);
        EsEdicion          = true;
    }

    /// <summary>Alerta del formulario cuando el servidor rechaza la carga de las fuentes (403): esa consulta exige
    /// finanzas.ver, así que llega acá quien perdió el acceso a finanzas con el formulario abierto.</summary>
    public const string MensajeSinPermisoFormulario =
        "Ya no tenés permiso para ver finanzas, así que no se puede registrar el ingreso.";

    /// <summary>
    /// La dispara la View (DataContextChanged). Va por EjecutarCargaProtegidaAsync: un 403 deja SinPermiso, que la vista
    /// muestra en una Alerta y que deshabilita Guardar (el aviso global del 403 ya lo dio AuthTokenHandler). Un servidor
    /// caído sigue saliendo por el aviso global, como antes.
    /// </summary>
    public async Task InicializarAsync()
    {
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var fuentes = await _fuentesService.ListarActivasAsync();
            FuentesDisponibles.Clear();
            foreach (var f in fuentes)
                FuentesDisponibles.Add(f);

            if (_ingresoParaEditar is not null)
            {
                FuenteSeleccionada =
                    FuentesDisponibles.FirstOrDefault(f => f.Id == _ingresoParaEditar.FuenteFinanciamientoId)
                    ?? _ingresoParaEditar.FuenteFinanciamiento;
                if (FuenteSeleccionada is not null
                    && FuentesDisponibles.All(f => f.Id != FuenteSeleccionada.Id))
                    FuentesDisponibles.Add(FuenteSeleccionada);
            }
        }, MensajeSinPermisoFormulario);
        GuardarCommand.NotifyCanExecuteChanged();
    }

    private bool PuedeGuardar()
        => !SinPermiso
           && !string.IsNullOrWhiteSpace(Concepto)
           && FuenteSeleccionada is not null
           && !string.IsNullOrWhiteSpace(MontoTexto);

    public IAsyncRelayCommand GuardarCommand => field ??= ComandoDeGuardado(GuardarAsync, PuedeGuardar);

    private async Task GuardarAsync()
    {
        MensajeError = null;

        if (!FormatoEsUy.TryParseDecimal(MontoTexto, out var monto, out var errorMonto))
        {
            MensajeError = MensajeNumeroInvalido.Armar("El monto no es un número válido.", errorMonto);
            return;
        }
        if (FechaSeleccionada is null)
        {
            MensajeError = "La fecha del ingreso es obligatoria.";
            return;
        }

        var ingreso = new IngresoCaja
        {
            Id = EsEdicion ? _idEdicion : 0,
            Fecha = DateTime.SpecifyKind(FechaSeleccionada.Value.Date, DateTimeKind.Utc),
            Concepto = Concepto,
            FuenteFinanciamientoId = FuenteSeleccionada!.Id,
            Monto = monto,
        };

        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(ingreso);
            else
                await _service.AltaAsync(ingreso);

            _navigation.Navegar<IngresosViewModel>();
        }
        catch (Exception ex)
            when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<IngresosViewModel>();
}
