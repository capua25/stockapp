using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.ApiClient;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

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

    /// <summary>Nombre del centinela "sin asignar" de los cuatro combos (Important 2, revisión
    /// de integración 2026-09-09) -- molde de DocumentoListViewModel.OpcionTipoDocumento
    /// ("Todos", Valor=null). Antes los combos bindeaban directo a la entidad y no había forma
    /// de volver a "ninguna" desde la UI, pese a que D21 (reemplazo total) lo permite del lado
    /// de Application/Api/ApiClient/VM.</summary>
    private const string NombreOpcionNinguna = "(ninguna)";

    private readonly IZonaService _zonasService;
    private readonly IDimensionTematicaService _dimensionesService;
    private readonly IOrganismoResponsableService _organismosService;
    private readonly IOrigenFinanciamientoService _origenesService;
    private readonly IDocumentoAdministrativoService _documentosService;
    private readonly IConfirmacionService _confirmacion;

    public ObservableCollection<OpcionClasificador> ZonasDisponibles { get; } = new();
    public ObservableCollection<OpcionClasificador> DimensionesDisponibles { get; } = new();
    public ObservableCollection<OpcionClasificador> OrganismosDisponibles { get; } = new();
    public ObservableCollection<OpcionClasificador> OrigenesDisponibles { get; } = new();

    [ObservableProperty] private OpcionClasificador? _zonaSeleccionada;
    [ObservableProperty] private OpcionClasificador? _dimensionSeleccionada;
    [ObservableProperty] private OpcionClasificador? _organismoSeleccionado;
    [ObservableProperty] private OpcionClasificador? _origenSeleccionado;
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
        IDocumentoAdministrativoService documentosService, IConfirmacionService confirmacion)
    {
        _zonasService = zonasService;
        _dimensionesService = dimensionesService;
        _organismosService = organismosService;
        _origenesService = origenesService;
        _documentosService = documentosService;
        _confirmacion = confirmacion;
        BuscarExpedientesAsync = BuscarExpedientesInternalAsync;
    }

    /// <summary>Puebla los cuatro catálogos y precarga la selección (usado por el modal de
    /// reclasificación, Task 10 — D21: lo que el Admin ve precargado es exactamente lo que
    /// queda guardado si no toca nada). Cada combo siempre incluye el centinela "(ninguna)"
    /// primero (Important 2) y, si <paramref name="actual"/> trae un id que ya no está entre
    /// los activos (Important 1: el catálogo fue dado de baja después de asignarse), agrega
    /// una opción adicional que preserva ese id sin perderlo -- ver <see cref="PoblarOpciones"/>.
    ///
    /// Fix (revisión final, Important 2 original): las cuatro llamadas HTTP no tenían
    /// try/catch, y TareaFormView.axaml.cs la dispara desde un handler async void
    /// (DataContextChanged) -- una excepción acá (API caída, 500) no la atrapa nadie y termina
    /// en crash.log sin avisar al operario. Molde de AdjuntosDocumentoPanelViewModel.RecargarAsync:
    /// UnauthorizedAccessException en silencio (ya se avisó aparte, mismo criterio que
    /// BuscarExpedientesInternalAsync no pisa acá), el resto vía ManejarErrorAsync sin mentir
    /// sobre la causa.</summary>
    public async Task InicializarAsync(DatosClasificacionTarea? actual = null)
    {
        try
        {
            var zonas = await _zonasService.ListarActivasAsync();
            PoblarOpciones(ZonasDisponibles, zonas.Select(z => (z.Id, z.Nombre)), actual?.ZonaId);

            var dimensiones = await _dimensionesService.ListarActivasAsync();
            PoblarOpciones(DimensionesDisponibles, dimensiones.Select(d => (d.Id, d.Nombre)), actual?.DimensionTematicaId);

            var organismos = await _organismosService.ListarActivasAsync();
            PoblarOpciones(OrganismosDisponibles, organismos.Select(o => (o.Id, o.Nombre)), actual?.OrganismoResponsableId);

            var origenes = await _origenesService.ListarActivasAsync();
            PoblarOpciones(OrigenesDisponibles, origenes.Select(o => (o.Id, o.Nombre)), actual?.OrigenFinanciamientoId);

            ZonaSeleccionada = ZonasDisponibles.FirstOrDefault(o => o.Id == actual?.ZonaId) ?? ZonasDisponibles[0];
            DimensionSeleccionada = DimensionesDisponibles.FirstOrDefault(o => o.Id == actual?.DimensionTematicaId) ?? DimensionesDisponibles[0];
            OrganismoSeleccionado = OrganismosDisponibles.FirstOrDefault(o => o.Id == actual?.OrganismoResponsableId) ?? OrganismosDisponibles[0];
            OrigenSeleccionado = OrigenesDisponibles.FirstOrDefault(o => o.Id == actual?.OrigenFinanciamientoId) ?? OrigenesDisponibles[0];
            DocumentoSeleccionado = null;
            MensajeBuscadorExpediente = null;

            if (actual?.DocumentoAdministrativoId is int documentoId)
                DocumentoSeleccionado = await _documentosService.ObtenerPorIdAsync(documentoId);
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    /// <summary>Arma las opciones de un combo de clasificador (Important 1 y 2, revisión de
    /// integración 2026-09-09): siempre antepone el centinela "(ninguna)" (Id=null, permite
    /// desasignar desde la UI -- D21) y, si <paramref name="actualId"/> no aparece entre las
    /// opciones activas (el catálogo fue dado de baja después de asignarse a la tarea), agrega
    /// una opción adicional que preserva ese id sin inventarle el nombre real -- este panel
    /// solo tiene permiso GestionarTareas, y ListarTodasAsync (el único lugar con el nombre de
    /// un catálogo inactivo) está gateada con GestionarTablasMaestras (ver ZonaService). El
    /// servidor sigue rechazando guardar un catálogo inactivo (ValidarClasificacionAsync,
    /// D12) -- este combo solo evita que la PRECARGA lo pierda en silencio; si el Admin guarda
    /// sin tocar el campo, el error de negocio real ("La zona '...' está inactiva.") llega
    /// igual, con el nombre correcto (TareaFormViewModel.ResolverMensajeError /
    /// ClasificacionTareaPanelViewModel.ManejarErrorAsync ya traducen ReglaDeNegocioException
    /// con ex.Message, no una excepción cruda).</summary>
    private static void PoblarOpciones(
        ObservableCollection<OpcionClasificador> destino,
        IEnumerable<(int Id, string Nombre)> activos,
        int? actualId)
    {
        destino.Clear();
        destino.Add(new OpcionClasificador(NombreOpcionNinguna, null));

        var encontroActual = actualId is null;
        foreach (var (id, nombre) in activos)
        {
            destino.Add(new OpcionClasificador(nombre, id));
            if (actualId == id) encontroActual = true;
        }

        if (!encontroActual)
            destino.Add(new OpcionClasificador($"(dado de baja — id {actualId})", actualId));
    }

    /// <summary>Mismo molde EXACTO que AdjuntosDocumentoPanelViewModel.ManejarErrorAsync:
    /// UnauthorizedAccessException en silencio (AuthTokenHandler ya avisó "Tus permisos
    /// cambiaron..." apenas vio el 403 -- duplicar el aviso acá sería mentir sobre la causa),
    /// el resto informa con el mensaje real de la excepción cuando es de un tipo conocido y un
    /// genérico solo para lo verdaderamente inesperado.</summary>
    private async Task ManejarErrorAsync(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return;

        var mensaje = ex switch
        {
            ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
                or ServidorNoDisponibleException => ex.Message,
            _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
        };
        await _confirmacion.InformarAsync(mensaje);
    }

    private async Task<IEnumerable<object>> BuscarExpedientesInternalAsync(string? texto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < MinimoCaracteresBusquedaExpediente)
        {
            MensajeBuscadorExpediente = null;
            return Array.Empty<object>();
        }

        var filtro = new FiltroDocumentos(TipoDocumento.Expediente, null, texto, null);

        IReadOnlyList<DocumentoAdministrativo> resultados;
        try
        {
            resultados = await _documentosService.ListarActivosAsync(filtro);
        }
        catch (UnauthorizedAccessException)
        {
            // Gap de diseño detectado en revisión (2026-09-09): ListarActivosAsync está
            // gateado con Permisos.GestionarDocumentos, independiente de GestionarTareas que
            // gatea los cuatro combos de catálogo de este mismo panel. Un usuario con
            // tareas.gestionar pero sin documentos.gestionar recibiría un 403 apenas tipea acá
            // -- y como el panel se embebe tal cual en el modal de reclasificación (Task 10) y
            // en el alta de tarea (Task 12), ambos consumidores heredarían el problema sin
            // enterarse. Degradación con gracia (mismo criterio que
            // InicioViewModel.CargarAsync con el panel de vencimientos de tareas, líneas
            // 249-278): el buscador queda inhabilitado con un mensaje explicativo, pero el
            // resto del panel (los 4 combos, ObtenerDatos) sigue funcionando sin romperse.
            MensajeBuscadorExpediente = "No tenés permiso para buscar expedientes.";
            return Array.Empty<object>();
        }

        if (resultados.Count > TopeResultadosBusquedaExpediente)
        {
            MensajeBuscadorExpediente = "Hay más resultados de los que se muestran. Refiná la búsqueda.";
            return resultados.Take(TopeResultadosBusquedaExpediente).Cast<object>();
        }

        MensajeBuscadorExpediente = null;
        return resultados.Cast<object>();
    }

    /// <summary>Reemplazo total (D21): los cinco campos SIEMPRE reflejan la selección
    /// actual, incluido null como desasignación explícita (el centinela "(ninguna)" tiene
    /// Id=null, Important 2).</summary>
    public DatosClasificacionTarea ObtenerDatos() => new(
        ZonaSeleccionada?.Id, DimensionSeleccionada?.Id, OrganismoSeleccionado?.Id,
        OrigenSeleccionado?.Id, DocumentoSeleccionado?.Id);
}

/// <summary>Opción de ComboBox para los cuatro clasificadores de este panel (Important 1 y 2,
/// revisión de integración 2026-09-09) -- molde de DocumentoListViewModel.OpcionTipoDocumento
/// ("Todos", Valor=null), reutilizado sin distinguir por clasificador porque acá alcanza con
/// Id+Nombre (a diferencia de OpcionTipoDocumento, que carga un enum de dominio distinto por
/// filtro). Id=null es el centinela "sin asignar".</summary>
public sealed record OpcionClasificador(string Nombre, int? Id);
