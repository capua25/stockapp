using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Control POA" (spec §7.4): una fila por línea con presupuesto, gastado, saldo
/// y % de ejecución. Doble click abre las facturas de esa línea en GastosViewModel filtrado.
/// </summary>
public partial class ControlPoaViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly INavigationService     _navigation;
    private readonly ICsvExporter           _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;

    [ObservableProperty] private int _ejercicio = DateTime.UtcNow.Year;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirGastosDeLaLineaCommand))]
    private ControlPoaLineaDto? _filaSeleccionada;

    public ObservableCollection<ControlPoaLineaDto> Filas { get; } = new();
    public DataGridCollectionView FilasView { get; }

    public ControlPoaViewModel(
        IFinanzasVistasService service, INavigationService navigation,
        ICsvExporter csvExporter, IServicioGuardadoArchivo guardado)
    {
        _service     = service;
        _navigation  = navigation;
        _csvExporter = csvExporter;
        _guardado    = guardado;

        FilasView = new DataGridCollectionView(Filas);
    }

    public async Task CargarAsync()
    {
        var lineas = await _service.ObtenerControlPoaAsync(Ejercicio);
        Filas.Clear();
        foreach (var l in lineas)
            Filas.Add(l);
    }

    [RelayCommand]
    private async Task RecargarAsync() => await CargarAsync();

    private bool TieneSeleccion() => FilaSeleccionada is not null;

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private void AbrirGastosDeLaLinea()
    {
        if (FilaSeleccionada is null) return;
        var linea = new LineaPoa
        {
            Id = FilaSeleccionada.LineaPoaId, Nombre = FilaSeleccionada.Nombre,
            Programa = FilaSeleccionada.Programa, Ejercicio = FilaSeleccionada.Ejercicio,
        };
        _navigation.Navegar<GastosViewModel>(vm => vm.FiltrarPorLineaPoa(linea));
    }

    private static readonly IReadOnlyList<string> ColumnasCsv = new[]
    {
        nameof(ControlPoaLineaDto.Nombre), nameof(ControlPoaLineaDto.Programa), nameof(ControlPoaLineaDto.Ejercicio),
        nameof(ControlPoaLineaDto.Presupuesto), nameof(ControlPoaLineaDto.Gastado), nameof(ControlPoaLineaDto.Saldo),
        nameof(ControlPoaLineaDto.PorcentajeEjecucion), nameof(ControlPoaLineaDto.Sobregirada),
    };

    [RelayCommand]
    private async Task ExportarCsvAsync()
    {
        var contenido = _csvExporter.Exportar(Filas, ColumnasCsv);
        await _guardado.GuardarTextoAsync(contenido, $"control-poa-{Ejercicio}.csv");
    }
}
