using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Formulario de alta / edición de un ingreso de caja. Montos con cultura FIJA es-UY.</summary>
public partial class IngresoFormViewModel : ViewModelBase
{
    private readonly IIngresoCajaService          _service;
    private readonly IFuenteFinanciamientoService _fuentesService;
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

    [ObservableProperty] private DateTimeOffset? _fechaSeleccionada = DateTimeOffset.Now;

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

    public IngresoFormViewModel(
        IIngresoCajaService service,
        IFuenteFinanciamientoService fuentesService,
        INavigationService navigation)
    {
        _service        = service;
        _fuentesService = fuentesService;
        _navigation     = navigation;
    }

    public void CargarParaEditar(IngresoCaja ingreso)
    {
        _idEdicion         = ingreso.Id;
        _ingresoParaEditar = ingreso;
        FechaSeleccionada  = new DateTimeOffset(DateTime.SpecifyKind(ingreso.Fecha, DateTimeKind.Utc));
        Concepto           = ingreso.Concepto;
        MontoTexto         = ingreso.Monto.ToString("N2", CulturaMonto);
        EsEdicion          = true;
    }

    public async Task InicializarAsync()
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
