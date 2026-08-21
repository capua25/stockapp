using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Tab "Historial" (F5d §5): grilla read-only clásica + Revertir por fila, habilitado solo en
/// filas Activas. DataContextChanged de la View dispara CargarAsync() (mismo patrón que
/// GastosViewModel/AuditoriaLogViewModel).
/// </summary>
public partial class HistorialImportacionesViewModel : ViewModelBase
{
    private readonly IImportacionService _service;
    private readonly IConfirmacionService _confirmacion;

    public ObservableCollection<ImportacionHistorialDto> Filas { get; } = new();

    /// <summary>Envuelve Filas para el ordenamiento por click en encabezados (gotcha Avalonia 12,
    /// mismo criterio que GastosViewModel.FilasView).</summary>
    public DataGridCollectionView FilasView { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertirCommand))]
    private ImportacionHistorialDto? _filaSeleccionada;

    public HistorialImportacionesViewModel(IImportacionService service, IConfirmacionService confirmacion)
    {
        _service = service;
        _confirmacion = confirmacion;

        FilasView = new DataGridCollectionView(Filas);
    }

    public async Task CargarAsync()
    {
        LimpiarSinPermiso();
        try
        {
            var historial = await _service.ListarHistorialAsync();
            Filas.Clear();
            foreach (var fila in historial)
                Filas.Add(fila);
        }
        // Fix (bugfix "pantalla muda ante un 403"): el filtro original solo contemplaba
        // ReglaDeNegocioException/EntidadNoEncontradaException, así que un
        // UnauthorizedAccessException no matcheaba, no era atrapado acá y escapaba de CargarAsync
        // sin protección. Se amplía el filtro en vez de agregar un segundo catch al lado (dos
        // mecanismos compitiendo en el mismo método) y se bifurca adentro: UnauthorizedAccessException
        // NO dispara InformarAsync (ese modal ya lo da el aviso global de permisos, ver
        // ViewModelBase.EjecutarCargaProtegidaAsync), solo deja el estado bindeable para EstadoVacio.
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or UnauthorizedAccessException)
        {
            if (ex is UnauthorizedAccessException)
            {
                MarcarSinPermiso("No tenés permiso para ver el historial de importaciones.");
            }
            else
            {
                await _confirmacion.InformarAsync(ex.Message);
            }
        }
    }

    private bool PuedeRevertir() => FilaSeleccionada is { Revertida: false };

    [RelayCommand(CanExecute = nameof(PuedeRevertir))]
    private async Task RevertirAsync()
    {
        if (FilaSeleccionada is not { Revertida: false } fila) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma revertir la importación del ejercicio {fila.Ejercicio} " +
            $"({fila.IdImportacion})? Se darán de baja todos los gastos, ingresos y líneas POA que creó.");
        if (!confirmar) return;

        try
        {
            await _service.RevertirAsync(fila.IdImportacion);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
