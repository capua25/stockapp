using System;
using System.Collections.ObjectModel;
using System.Globalization;
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
    /// IsEnabled. No se refresca en caliente: IngresoFormViewModel se registra AddTransient y
    /// se recrea en cada navegación a la pantalla.
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
        MontoTexto         = ingreso.Monto.ToString("N2", CulturaMonto);
        EsEdicion          = true;
    }

    public async Task InicializarAsync()
    {
        try
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
        }
        catch (UnauthorizedAccessException)
        {
            // Red de contención (bugfix 2026-08-15): InicializarAsync la dispara la View
            // (DataContextChanged) fire-and-forget — una excepción no atrapada acá escala a
            // Dispatcher.UIThread.UnhandledException (App.axaml.cs), que loguea a crash.log y
            // muestra el genérico "Ocurrió un error inesperado" como si fuera un bug real. Un
            // 403 ya se avisó ANTES de llegar acá: AuthTokenHandler dispara
            // ApiSession.AccesoRevocado apenas ve el 403 en la respuesta HTTP, y App.axaml.cs
            // ya informa "Tus permisos cambiaron...". Mismo criterio que
            // GastosViewModel.CargarAsync (ec0696c): silencioso, para no duplicar el aviso.
        }
    }

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Concepto)
           && FuenteSeleccionada is not null
           && !string.IsNullOrWhiteSpace(MontoTexto);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;

        if (!decimal.TryParse(MontoTexto, NumberStyles.Number, CulturaMonto, out var monto))
        {
            MensajeError = "El monto no es un número válido.";
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
