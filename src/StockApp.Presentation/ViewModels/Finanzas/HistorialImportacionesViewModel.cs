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
        try
        {
            var historial = await _service.ListarHistorialAsync();
            Filas.Clear();
            foreach (var fila in historial)
                Filas.Add(fila);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
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

/// <summary>Texto de la columna Estado del historial: "Activa"/"Revertida".</summary>
public sealed class EstadoRevertidaConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly EstadoRevertidaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "Revertida" : "Activa";

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
