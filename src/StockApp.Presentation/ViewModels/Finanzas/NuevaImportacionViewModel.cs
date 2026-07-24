using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Paso actual del wizard de importación (F5d §5).</summary>
public enum PasoWizardImportacion { Cargar, Revisar, Resultado }

/// <summary>
/// Tab "Nueva importación" (F5d §5): wizard de 3 pasos como UNA sola VM con estado PasoActual —
/// las 3 vistas de paso comparten DataContext con esta VM y alternan visibilidad por PasoActual.
/// Este task cubre el esqueleto + Paso 1 (Cargar); Paso 2/3 se agregan en Tasks 8/9 sobre esta
/// MISMA clase (misma convención que GastoFila embebido en GastosViewModel.cs).
/// </summary>
public partial class NuevaImportacionViewModel : ViewModelBase
{
    private readonly IImportacionService _service;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    private PasoWizardImportacion _pasoActual = PasoWizardImportacion.Cargar;

    // ── Paso 1: Cargar ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    private string? _gastosNombreArchivo;
    private byte[]? _gastosContenido;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    private string? _poaNombreArchivo;
    private byte[]? _poaContenido;

    [ObservableProperty]
    private int _ejercicio = DateTime.UtcNow.Year;

    [ObservableProperty]
    private bool _forzar;

    public NuevaImportacionViewModel(
        IImportacionService service, IServicioSeleccionArchivo seleccion, IConfirmacionService confirmacion)
    {
        _service = service;
        _seleccion = seleccion;
        _confirmacion = confirmacion;
    }

    [RelayCommand]
    private async Task SeleccionarGastosAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoOdsAsync();
        if (seleccionado is null) return;
        (GastosNombreArchivo, _gastosContenido) = seleccionado.Value;
        AnalizarCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SeleccionarPoaAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoOdsAsync();
        if (seleccionado is null) return;
        (PoaNombreArchivo, _poaContenido) = seleccionado.Value;
        AnalizarCommand.NotifyCanExecuteChanged();
    }

    private bool PuedeAnalizar() => _gastosContenido is not null && _poaContenido is not null;

    [RelayCommand(CanExecute = nameof(PuedeAnalizar))]
    private async Task AnalizarAsync()
    {
        try
        {
            var analisis = await _service.AnalizarAsync(
                GastosNombreArchivo!, _gastosContenido!, PoaNombreArchivo!, _poaContenido!, Ejercicio);

            CargarAnalisis(analisis);
            PasoActual = PasoWizardImportacion.Revisar;
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    /// <summary>Placeholder de Task 8: puebla las colecciones del Paso 2. Se reemplaza por la
    /// implementación real en Task 8 — DEBE quedar reemplazado antes de cerrar ese task, no es
    /// un placeholder permanente de este plan.</summary>
    partial void CargarAnalisisPaso2(ResultadoAnalisisDto analisis);

    private void CargarAnalisis(ResultadoAnalisisDto analisis) => CargarAnalisisPaso2(analisis);
}
