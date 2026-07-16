using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Fila editable de la grilla de asignaciones presupuestales (fuente + monto).</summary>
public partial class AsignacionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    [ObservableProperty]
    private string _montoTexto = string.Empty;
}

/// <summary>
/// Formulario de alta / edición de una línea POA con su grilla de asignaciones
/// presupuestales por fuente (financiamiento mixto B+C). El agregado viaja completo
/// al servicio; las reglas finas (montos &gt; 0, sin fuentes repetidas) las valida
/// el servidor y acá se muestran vía MensajeError.
/// </summary>
public partial class LineaPoaFormViewModel : ViewModelBase
{
    private readonly ILineaPoaService             _service;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly INavigationService           _navigation;

    private int _idEdicion;
    private LineaPoa? _lineaParaEditar;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _programa = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _ejercicioTexto = System.DateTime.Now.Year.ToString();

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar línea POA" : "Nueva línea POA";

    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<AsignacionItemViewModel> Asignaciones { get; } = new();

    public LineaPoaFormViewModel(
        ILineaPoaService service,
        IFuenteFinanciamientoService fuentesService,
        INavigationService navigation)
    {
        _service        = service;
        _fuentesService = fuentesService;
        _navigation     = navigation;
    }

    /// <summary>
    /// Precarga el modo edición. Corre ANTES de InicializarAsync (mismo contrato que
    /// ProductoFormViewModel.CargarParaEditar): guarda la línea y difiere el mapeo de las
    /// filas de asignaciones a InicializarAsync, que necesita FuentesDisponibles cargadas
    /// para resolver FuenteSeleccionada por Id.
    /// </summary>
    public void CargarParaEditar(LineaPoa linea)
    {
        _idEdicion       = linea.Id;
        _lineaParaEditar = linea;
        Nombre           = linea.Nombre;
        Programa         = linea.Programa;
        EjercicioTexto   = linea.Ejercicio.ToString();
        EsEdicion        = true;
    }

    /// <summary>Carga el combo de fuentes activas y arma las filas de asignaciones.</summary>
    public async Task InicializarAsync()
    {
        var fuentes = await _fuentesService.ListarActivasAsync();
        FuentesDisponibles.Clear();
        foreach (var f in fuentes)
            FuentesDisponibles.Add(f);

        Asignaciones.Clear();
        if (_lineaParaEditar is not null)
        {
            foreach (var a in _lineaParaEditar.Asignaciones)
            {
                // Resuelve por Id contra el combo; si la fuente fue dada de baja después,
                // cae al objeto de la nav para no perder la fila histórica.
                var fuente = FuentesDisponibles.FirstOrDefault(f => f.Id == a.FuenteFinanciamientoId)
                    ?? a.FuenteFinanciamiento;
                if (fuente is not null && !FuentesDisponibles.Contains(fuente)
                    && FuentesDisponibles.All(f => f.Id != fuente.Id))
                    FuentesDisponibles.Add(fuente);

                Asignaciones.Add(new AsignacionItemViewModel
                {
                    FuenteSeleccionada = fuente,
                    MontoTexto = a.Monto.ToString("0.####"),
                });
            }
        }

        if (Asignaciones.Count == 0)
            Asignaciones.Add(new AsignacionItemViewModel());  // una fila lista para completar
    }

    [RelayCommand]
    private void AgregarAsignacion() => Asignaciones.Add(new AsignacionItemViewModel());

    [RelayCommand]
    private void QuitarAsignacion(AsignacionItemViewModel fila) => Asignaciones.Remove(fila);

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Nombre)
           && !string.IsNullOrWhiteSpace(Programa)
           && int.TryParse(EjercicioTexto, out var ejercicio)
           && ejercicio > 0;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;

        var asignaciones = new List<AsignacionPresupuestal>();
        foreach (var fila in Asignaciones)
        {
            if (fila.FuenteSeleccionada is null
                || !decimal.TryParse(fila.MontoTexto, out var monto))
            {
                MensajeError = "Cada asignación necesita una fuente de financiamiento y un monto válido.";
                return;
            }

            asignaciones.Add(new AsignacionPresupuestal
            {
                FuenteFinanciamientoId = fila.FuenteSeleccionada.Id,
                Monto = monto,
            });
        }

        var linea = new LineaPoa
        {
            Id = EsEdicion ? _idEdicion : 0,
            Nombre = Nombre,
            Programa = Programa,
            Ejercicio = int.Parse(EjercicioTexto),
            Asignaciones = asignaciones,
        };

        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(linea);
            else
                await _service.AltaAsync(linea);

            _navigation.Navegar<MaestrosFinanzasViewModel>();
        }
        catch (System.Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or System.ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<MaestrosFinanzasViewModel>();
}
