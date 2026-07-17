using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Libro caja" (spec §7.3): selector de mes + toggle "Año completo", grilla
/// cronológica con saldo corrido, paneles de totales por rubro/fuente.
/// </summary>
public partial class LibroCajaViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly ICsvExporter           _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;

    [ObservableProperty] private int _anio = DateTime.UtcNow.Year;
    [ObservableProperty] private int _mes = DateTime.UtcNow.Month;
    [ObservableProperty] private bool _verAnioCompleto;

    [ObservableProperty] private decimal _saldoInicial;
    [ObservableProperty] private decimal _saldoFinal;
    [ObservableProperty] private LibroCajaAnualDto? _libroAnual;

    public ObservableCollection<MovimientoCajaDto> Movimientos { get; } = new();
    public DataGridCollectionView MovimientosView { get; }

    public ObservableCollection<TotalPorClaveDto> TotalesPorRubro { get; } = new();
    public ObservableCollection<TotalPorClaveDto> TotalesPorFuente { get; } = new();

    public LibroCajaViewModel(
        IFinanzasVistasService service, ICsvExporter csvExporter, IServicioGuardadoArchivo guardado)
    {
        _service     = service;
        _csvExporter = csvExporter;
        _guardado    = guardado;

        MovimientosView = new DataGridCollectionView(Movimientos);
    }

    /// <summary>Carga el libro caja del mes/año seleccionado, o el año completo si VerAnioCompleto.</summary>
    public async Task CargarAsync()
    {
        Movimientos.Clear();
        TotalesPorRubro.Clear();
        TotalesPorFuente.Clear();
        LibroAnual = null;

        if (VerAnioCompleto)
        {
            LibroAnual = await _service.ObtenerLibroCajaAnualAsync(Anio);
            return;
        }

        var libro = await _service.ObtenerLibroCajaMesAsync(Anio, Mes);
        SaldoInicial = libro.SaldoInicial;
        SaldoFinal = libro.SaldoFinal;
        foreach (var mov in libro.Movimientos)
            Movimientos.Add(mov);
        foreach (var t in libro.TotalesPorRubro)
            TotalesPorRubro.Add(t);
        foreach (var t in libro.TotalesPorFuente)
            TotalesPorFuente.Add(t);
    }

    [RelayCommand]
    private async Task RecargarAsync() => await CargarAsync();

    private static readonly IReadOnlyList<string> ColumnasCsv = new[]
    {
        nameof(MovimientoCajaDto.Fecha), nameof(MovimientoCajaDto.Tipo), nameof(MovimientoCajaDto.Concepto),
        nameof(MovimientoCajaDto.ProveedorNombre), nameof(MovimientoCajaDto.NumeroFactura),
        nameof(MovimientoCajaDto.FuenteNombre), nameof(MovimientoCajaDto.RubroNombre),
        nameof(MovimientoCajaDto.Ingreso), nameof(MovimientoCajaDto.Egreso), nameof(MovimientoCajaDto.SaldoCorrido),
    };

    [RelayCommand]
    private async Task ExportarCsvAsync()
    {
        var contenido = _csvExporter.Exportar(Movimientos, ColumnasCsv);
        await _guardado.GuardarTextoAsync(contenido, $"libro-caja-{Anio:0000}-{Mes:00}.csv");
    }
}
