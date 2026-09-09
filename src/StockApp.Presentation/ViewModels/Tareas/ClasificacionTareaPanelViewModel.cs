using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// Panel reusable de los cinco clasificadores de una tarea (spec 2026-09-08): cuatro combos
/// (Zona/Dimensión/Organismo/Origen) + buscador de expediente server-side (D22, molde de
/// MovimientoHistorialViewModel.BuscarProductosAsync). Se embebe SIN modificar en dos
/// lugares — alta inline (TareaFormViewModel, Task 12) y el modal de reclasificación
/// (ReclasificarTareaDialogViewModel, Task 10) — mismo criterio de composición que
/// AdjuntosDocumentoPanelViewModel/AdjuntosDocumentoPanelView.
/// </summary>
public partial class ClasificacionTareaPanelViewModel : ViewModelBase
{
    private const int MinimoCaracteresBusquedaExpediente = 3;
    private const int TopeResultadosBusquedaExpediente = 20;

    private readonly IZonaService _zonasService;
    private readonly IDimensionTematicaService _dimensionesService;
    private readonly IOrganismoResponsableService _organismosService;
    private readonly IOrigenFinanciamientoService _origenesService;
    private readonly IDocumentoAdministrativoService _documentosService;

    public ObservableCollection<Zona> ZonasDisponibles { get; } = new();
    public ObservableCollection<DimensionTematica> DimensionesDisponibles { get; } = new();
    public ObservableCollection<OrganismoResponsable> OrganismosDisponibles { get; } = new();
    public ObservableCollection<OrigenFinanciamiento> OrigenesDisponibles { get; } = new();

    [ObservableProperty] private Zona? _zonaSeleccionada;
    [ObservableProperty] private DimensionTematica? _dimensionSeleccionada;
    [ObservableProperty] private OrganismoResponsable? _organismoSeleccionado;
    [ObservableProperty] private OrigenFinanciamiento? _origenSeleccionado;
    [ObservableProperty] private DocumentoAdministrativo? _documentoSeleccionado;
    [ObservableProperty] private string? _mensajeBuscadorExpediente;

    /// <summary>Delegado para AutoCompleteBox.AsyncPopulator (D22 del spec: mínimo de
    /// caracteres antes de disparar la búsqueda y tope de resultados con aviso — mismo
    /// motivo que MovimientoHistorialViewModel.BuscarProductosAsync: ListarActivosAsync NO
    /// pagina ni limita).</summary>
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> BuscarExpedientesAsync { get; }

    public ClasificacionTareaPanelViewModel(
        IZonaService zonasService, IDimensionTematicaService dimensionesService,
        IOrganismoResponsableService organismosService, IOrigenFinanciamientoService origenesService,
        IDocumentoAdministrativoService documentosService)
    {
        _zonasService = zonasService;
        _dimensionesService = dimensionesService;
        _organismosService = organismosService;
        _origenesService = origenesService;
        _documentosService = documentosService;
        BuscarExpedientesAsync = BuscarExpedientesInternalAsync;
    }

    /// <summary>Puebla los cuatro catálogos y, si <paramref name="actual"/> no es null,
    /// precarga la selección (usado por el modal de reclasificación, Task 10 — D21: lo que
    /// el Admin ve precargado es exactamente lo que queda guardado si no toca nada).</summary>
    public async Task InicializarAsync(DatosClasificacionTarea? actual = null)
    {
        var zonas = await _zonasService.ListarActivasAsync();
        ZonasDisponibles.Clear();
        foreach (var z in zonas) ZonasDisponibles.Add(z);

        var dimensiones = await _dimensionesService.ListarActivasAsync();
        DimensionesDisponibles.Clear();
        foreach (var d in dimensiones) DimensionesDisponibles.Add(d);

        var organismos = await _organismosService.ListarActivasAsync();
        OrganismosDisponibles.Clear();
        foreach (var o in organismos) OrganismosDisponibles.Add(o);

        var origenes = await _origenesService.ListarActivasAsync();
        OrigenesDisponibles.Clear();
        foreach (var o in origenes) OrigenesDisponibles.Add(o);

        ZonaSeleccionada = null;
        DimensionSeleccionada = null;
        OrganismoSeleccionado = null;
        OrigenSeleccionado = null;
        DocumentoSeleccionado = null;
        MensajeBuscadorExpediente = null;

        if (actual is null) return;

        ZonaSeleccionada = ZonasDisponibles.FirstOrDefault(z => z.Id == actual.ZonaId);
        DimensionSeleccionada = DimensionesDisponibles.FirstOrDefault(d => d.Id == actual.DimensionTematicaId);
        OrganismoSeleccionado = OrganismosDisponibles.FirstOrDefault(o => o.Id == actual.OrganismoResponsableId);
        OrigenSeleccionado = OrigenesDisponibles.FirstOrDefault(o => o.Id == actual.OrigenFinanciamientoId);
        if (actual.DocumentoAdministrativoId is int documentoId)
            DocumentoSeleccionado = await _documentosService.ObtenerPorIdAsync(documentoId);
    }

    private async Task<IEnumerable<object>> BuscarExpedientesInternalAsync(string? texto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < MinimoCaracteresBusquedaExpediente)
        {
            MensajeBuscadorExpediente = null;
            return Array.Empty<object>();
        }

        var filtro = new FiltroDocumentos(TipoDocumento.Expediente, null, texto, null);
        var resultados = await _documentosService.ListarActivosAsync(filtro);

        if (resultados.Count > TopeResultadosBusquedaExpediente)
        {
            MensajeBuscadorExpediente = "Hay más resultados de los que se muestran. Refiná la búsqueda.";
            return resultados.Take(TopeResultadosBusquedaExpediente).Cast<object>();
        }

        MensajeBuscadorExpediente = null;
        return resultados.Cast<object>();
    }

    /// <summary>Reemplazo total (D21): los cinco campos SIEMPRE reflejan la selección
    /// actual, incluido null como desasignación explícita.</summary>
    public DatosClasificacionTarea ObtenerDatos() => new(
        ZonaSeleccionada?.Id, DimensionSeleccionada?.Id, OrganismoSeleccionado?.Id,
        OrigenSeleccionado?.Id, DocumentoSeleccionado?.Id);
}
