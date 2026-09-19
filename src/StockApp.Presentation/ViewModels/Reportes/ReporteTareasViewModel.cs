using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Presentation.Services;

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
    private readonly ICsvExporter _csvExporter;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly IConfirmacionService _confirmacion;
    private readonly ICurrentSession _session;

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

    public ReporteTareasViewModel(
        IReporteTareasService servicio,
        ICsvExporter csvExporter,
        IPdfExporter pdfExporter,
        IServicioGuardadoArchivo guardado,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion,
        ICurrentSession session)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _pdfExporter = pdfExporter;
        _guardado = guardado;
        _apertura = apertura;
        _confirmacion = confirmacion;
        _session = session;
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

    // ── Export CSV/PDF (spec 2026-09-18): única pantalla del alcance sin ninguna exportación
    // previa. Columnas FIJAS de la matriz -- el agrupador cambia filas y contenido (y el texto
    // del Clasificador), nunca las columnas. Mismo orden para CSV y PDF: a diferencia de las
    // otras 8 pantallas, acá coinciden exactamente porque no hay IDs internos que excluir. ──────

    public static readonly IReadOnlyList<string> ColumnasCsv = new[]
    {
        nameof(FilaReporteTareas.Clasificador), nameof(FilaReporteTareas.Pendientes),
        nameof(FilaReporteTareas.EnCurso), nameof(FilaReporteTareas.Terminadas),
        nameof(FilaReporteTareas.Canceladas), nameof(FilaReporteTareas.Total),
    };

    public static readonly IReadOnlyList<string> ColumnasPdf = ColumnasCsv;

    /// <summary>
    /// El mismo reporte agrupado por Zona o por Expediente son documentos completamente
    /// distintos (spec 2026-09-18): sin esto, dos PDFs idénticos en apariencia podrían
    /// significar cosas opuestas. Dice agrupador, criterio de fecha (creación/cierre) y el
    /// rango -- las fechas son obligatorias en esta pantalla (D18), así que siempre hay valor.
    /// </summary>
    private string ConstruirDescripcionFiltros() =>
        $"Agrupado por: {AgrupadorSeleccionado.Nombre}. " +
        $"Criterio: {(EsCriterioCreacion ? "Fecha de creación" : "Fecha de cierre")}. " +
        $"Período: {FechaDesde:dd/MM/yyyy} a {FechaHasta:dd/MM/yyyy}.";

    /// <summary>
    /// Agrega una fila de cierre "Total general" (spec 2026-09-18) SOLO al export PDF, nunca a
    /// <see cref="Items"/> (la grilla en pantalla no se toca -- D23 sigue vigente para lo que ve
    /// el usuario). Un reporte estadístico impreso/archivado sin su total general está
    /// incompleto: el lector no puede verificar que las filas suman lo esperado sin sumar a
    /// mano. La columna Total de la fila de cierre usa <see cref="TotalGeneral"/> tal cual lo
    /// calculó el servicio (nunca recalculado acá, D23) -- las cuatro columnas de estado se
    /// suman de <see cref="Items"/> únicamente para completar la fila visualmente: por el
    /// invariante de la Decisión 17 (Pendientes+EnCurso+Terminadas+Canceladas == Total en cada
    /// fila), esa suma coincide exactamente con TotalGeneral.
    /// </summary>
    private IEnumerable<FilaReporteTareas> ConstruirFilasConTotalGeneral()
    {
        foreach (var fila in Items)
            yield return fila;

        yield return new FilaReporteTareas(
            "Total general",
            Items.Sum(f => f.Pendientes),
            Items.Sum(f => f.EnCurso),
            Items.Sum(f => f.Terminadas),
            Items.Sum(f => f.Canceladas),
            TotalGeneral);
    }

    [RelayCommand]
    private async Task ExportarCsvAsync()
    {
        if (Items.Count == 0)
            return;

        await ExportacionCsv.EjecutarAsync(async () =>
        {
            var csv = _csvExporter.Exportar(Items, ColumnasCsv);
            await _guardado.GuardarTextoAsync(csv, "reporte-tareas.csv");
        }, _confirmacion);
    }

    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Items.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Items.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Estadística de tareas",
                DescripcionFiltros: ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(ConstruirFilasConTotalGeneral(), ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "reporte-tareas.pdf", extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, "reporte-tareas.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
}
