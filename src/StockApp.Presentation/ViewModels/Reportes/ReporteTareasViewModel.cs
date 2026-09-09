using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Reportes;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>Opción del combo de agrupador (mismo patrón que OpcionTipoDocumento en
/// DocumentoListViewModel: un Nombre legible junto al valor real del enum).</summary>
public sealed record OpcionAgrupador(string Nombre, AgrupadorTareas Valor);

/// <summary>
/// ViewModel del reporte-matriz de Tareas (spec 2026-09-08). Consulta
/// <see cref="IReporteTareasService"/> con el agrupador/criterio/rango elegidos.
///
/// D23: este ViewModel NO calcula nada -- ni totales, ni porcentajes, ni conteos. Solo asigna
/// <see cref="Items"/>/<see cref="TotalGeneral"/> con lo que devuelve el servicio, tal cual.
/// </summary>
public partial class ReporteTareasViewModel : ViewModelBase
{
    private readonly IReporteTareasService _servicio;

    public IReadOnlyList<OpcionAgrupador> AgrupadoresDisponibles { get; } = new[]
    {
        new OpcionAgrupador("Zona", AgrupadorTareas.Zona),
        new OpcionAgrupador("Dimensión temática", AgrupadorTareas.DimensionTematica),
        new OpcionAgrupador("Organismo responsable", AgrupadorTareas.OrganismoResponsable),
        new OpcionAgrupador("Origen de financiamiento", AgrupadorTareas.OrigenFinanciamiento),
        new OpcionAgrupador("Expediente", AgrupadorTareas.Expediente),
    };

    [ObservableProperty]
    private OpcionAgrupador _agrupadorSeleccionado;

    [ObservableProperty]
    private CriterioFechaTareas _criterioSeleccionado;

    // DateTime? (no DateTime): CalendarDatePicker.SelectedDate es Nullable<DateTime> con binding
    // TwoWay -- mismo tipo que MasMovidosViewModel.FechaDesde/FechaHasta. El rango sigue siendo
    // obligatorio (D18): CargarAsync() rechaza con MensajeError si el usuario borra una fecha.
    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private IReadOnlyList<FilaReporteTareas> _items = new List<FilaReporteTareas>();

    [ObservableProperty]
    private int _totalGeneral;

    [ObservableProperty]
    private string? _mensajeError;

    /// <summary>Binding de los dos RadioButton (decisión 16): getter/setter propios, no
    /// [ObservableProperty], porque derivan de/mutan CriterioSeleccionado en vez de tener
    /// backing field propio.</summary>
    public bool EsCriterioCreacion
    {
        get => CriterioSeleccionado == CriterioFechaTareas.Creacion;
        set { if (value) CriterioSeleccionado = CriterioFechaTareas.Creacion; }
    }

    public bool EsCriterioCierre
    {
        get => CriterioSeleccionado == CriterioFechaTareas.Cierre;
        set { if (value) CriterioSeleccionado = CriterioFechaTareas.Cierre; }
    }

    public ReporteTareasViewModel(IReporteTareasService servicio)
    {
        _servicio = servicio;
        _agrupadorSeleccionado = AgrupadoresDisponibles[0];
        _criterioSeleccionado = CriterioFechaTareas.Creacion;

        // D18: default del año en curso -- el rango sigue siendo obligatorio y se revalida
        // en el servicio, esto es solo la comodidad inicial de la UI.
        var anioActual = DateTime.Now.Year;
        _fechaDesde = new DateTime(anioActual, 1, 1);
        _fechaHasta = new DateTime(anioActual, 12, 31);
    }

    partial void OnCriterioSeleccionadoChanged(CriterioFechaTareas value)
    {
        OnPropertyChanged(nameof(EsCriterioCreacion));
        OnPropertyChanged(nameof(EsCriterioCierre));
    }

    /// <summary>Dispara la búsqueda del reporte y puebla <see cref="Items"/>/<see cref="TotalGeneral"/>.</summary>
    [RelayCommand]
    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Público para poder engancharse desde el auto-load de la vista (<c>DataContextChanged</c>
    /// en <c>ReporteTareasView.axaml.cs</c>), además de desde <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        // D18: el rango es obligatorio. FechaDesde/FechaHasta son DateTime? (el usuario puede
        // borrar el CalendarDatePicker) -- rechazar ACÁ con un mensaje claro, nunca mandar null
        // a FiltroReporteTareas (Desde/Hasta siguen siendo DateTime no nullable en Application).
        if (FechaDesde is null || FechaHasta is null)
        {
            MensajeError = "El rango de fechas es obligatorio.";
            return;
        }

        if (FechaDesde > FechaHasta)
        {
            MensajeError = "La fecha 'Desde' no puede ser posterior a 'Hasta'.";
            return;
        }

        MensajeError = null;
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var filtro = new FiltroReporteTareas(
                AgrupadorSeleccionado.Valor,
                CriterioSeleccionado,
                ALocalAUtc(FechaDesde.Value),
                ALocalAUtc(FechaHasta.Value));

            var reporte = await _servicio.ObtenerAsync(filtro);

            // D23: passthrough exacto -- ni Sum, ni Count, ni Select acá.
            Items = reporte.Filas;
            TotalGeneral = reporte.TotalGeneral;
        }, "No tenés permiso para ver el reporte de tareas.");
    }

    /// <summary>Convierte una fecha LOCAL (la que produce el CalendarDatePicker bindeado a
    /// FechaDesde/FechaHasta) a UTC antes de pasarla al servicio -- mismo criterio que
    /// MasMovidosViewModel.ALocalAUtc: el repositorio compara contra columnas timestamptz.
    /// Toma DateTime no nullable a propósito: los dos call sites ya validaron HasValue arriba
    /// (D18), así que acá no hace falta (ni conviene) volver a manejar null.</summary>
    private static DateTime ALocalAUtc(DateTime fechaLocal)
        => DateTime.SpecifyKind(fechaLocal, DateTimeKind.Local).ToUniversalTime();
}
