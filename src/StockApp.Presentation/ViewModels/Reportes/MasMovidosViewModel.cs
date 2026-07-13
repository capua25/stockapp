using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Reportes;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// ViewModel del reporte de Productos Más Movidos (Inc 6). Consulta el top N de
/// productos por movimientos en un período vía <see cref="IReporteStockService"/>
/// y permite exportarlo a CSV.
/// </summary>
public partial class MasMovidosViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para la exportación CSV. Coincide con las propiedades
    /// de <see cref="MasMovidoDto"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> ColumnOrder = new[]
    {
        "ProductoId",
        "Codigo",
        "Nombre",
        "CantidadMovimientos",
        "VolumenTotal",
    };

    private readonly IReporteStockService _servicio;
    private readonly ICsvExporter _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private int _topN = 20;

    [ObservableProperty]
    private IReadOnlyList<MasMovidoDto> _items = new List<MasMovidoDto>();

    [ObservableProperty]
    private string? _mensajeError;

    public MasMovidosViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
    }

    /// <summary>Consulta los productos más movidos del período y puebla <see cref="Items"/>.</summary>
    [RelayCommand]
    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Consulta los productos más movidos del período y puebla <see cref="Items"/>. Público
    /// para poder engancharse desde el auto-load de la vista (<c>DataContextChanged</c> en
    /// <c>MasMovidosView.axaml.cs</c>), además de desde <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        if (FechaDesde is not null && FechaHasta is not null && FechaDesde > FechaHasta)
        {
            MensajeError = "La fecha 'Desde' no puede ser posterior a 'Hasta'.";
            return;
        }

        MensajeError = null;
        Items = await _servicio.ObtenerMasMovidosAsync(ALocalAUtc(FechaDesde), ALocalAUtc(FechaHasta), TopN);
    }

    /// <summary>
    /// Convierte una fecha LOCAL (la que produce el <c>CalendarDatePicker</c> bindeado a
    /// FechaDesde/FechaHasta, ver XAML) a UTC antes de pasarla al servicio. El repositorio
    /// subyacente (ReporteStockRepository) compara contra <c>MovimientoStock.Fecha</c>,
    /// persistida en UTC — sin esta conversión, con UTC-3 el rango queda desalineado (bug de
    /// huso horario). Contrato: el servicio siempre recibe fechas en UTC.
    /// </summary>
    private static DateTime? ALocalAUtc(DateTime? fechaLocal)
        => fechaLocal.HasValue
            ? DateTime.SpecifyKind(fechaLocal.Value, DateTimeKind.Local).ToUniversalTime()
            : null;

    /// <summary>
    /// Exporta <see cref="Items"/> a CSV con el orden de columnas fijo y delega el guardado.
    /// No hace nada si no hay datos cargados.
    /// </summary>
    [RelayCommand]
    private async Task ExportarAsync()
    {
        if (Items.Count == 0)
            return;

        var csv = _csvExporter.Exportar(Items, ColumnOrder);
        await _guardado.GuardarTextoAsync(csv, "mas-movidos.csv");
    }
}
