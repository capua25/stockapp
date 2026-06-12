using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auditoria;
using StockApp.Application.Exportacion;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// ViewModel del reporte de Log de Auditoría (Inc 6). Consulta el historial de
/// acciones auditadas filtrado por usuario y rango de fechas vía
/// <see cref="IAuditoriaQueryService"/> y permite exportarlo a CSV.
/// </summary>
public partial class AuditoriaLogViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para la exportación CSV. Coincide con las propiedades
    /// de <see cref="AuditoriaItemDto"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> ColumnOrder = new[]
    {
        "Fecha",
        "NombreUsuario",
        "Accion",
        "Entidad",
        "EntidadId",
        "Detalle",
    };

    private readonly IAuditoriaQueryService _servicio;
    private readonly ICsvExporter _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;

    [ObservableProperty]
    private int? _usuarioId;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private IReadOnlyList<AuditoriaItemDto> _items = new List<AuditoriaItemDto>();

    public AuditoriaLogViewModel(
        IAuditoriaQueryService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
    }

    /// <summary>Consulta el log de auditoría filtrado y puebla <see cref="Items"/>.</summary>
    [RelayCommand]
    private async Task BuscarAsync()
    {
        Items = await _servicio.ObtenerLogAsync(UsuarioId, FechaDesde, FechaHasta);
    }

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
        await _guardado.GuardarTextoAsync(csv, "auditoria.csv");
    }
}
