using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.ApiClient;
using StockApp.Application.Documentos;
using StockApp.Application.Proyectos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// Panel reusable de clasificación de una tarea (spec 2026-10-30): selector de proyecto,
/// alimentado por IProyectoService.ListarOpcionesAsync (solo Pendiente/En curso), + buscador de
/// expediente server-side (D22 del spec 2026-09-08, molde de
/// MovimientoHistorialViewModel.BuscarProductosAsync). Zona/Dimensión/Organismo/Origen pasaron
/// al Proyecto. Se embebe SIN modificar en dos lugares — alta inline (TareaFormViewModel) y el
/// modal de reclasificación (ReclasificarTareaDialogViewModel) — mismo criterio de composición
/// que AdjuntosDocumentoPanelViewModel/AdjuntosDocumentoPanelView.
/// </summary>
public partial class ClasificacionTareaPanelViewModel : ViewModelBase
{
    private const int MinimoCaracteresBusquedaExpediente = 3;
    private const int TopeResultadosBusquedaExpediente = 20;

    /// <summary>Nombre del centinela "sin proyecto" del combo (Important 2, revisión de
    /// integración 2026-09-09) -- molde de DocumentoListViewModel.OpcionTipoDocumento ("Todos",
    /// Valor=null): sin él no habría forma de desvincular el proyecto desde la UI, pese a que
    /// D21 (reemplazo total) lo permite.</summary>
    private const string NombreOpcionNinguno = "(ninguno)";

    private readonly IProyectoService _proyectosService;
    private readonly IDocumentoAdministrativoService _documentosService;
    private readonly IConfirmacionService _confirmacion;

    public ObservableCollection<OpcionProyectoTarea> ProyectosDisponibles { get; } = new();

    [ObservableProperty] private OpcionProyectoTarea? _proyectoSeleccionado;
    [ObservableProperty] private DocumentoAdministrativo? _documentoSeleccionado;
    [ObservableProperty] private string? _mensajeBuscadorExpediente;

    /// <summary>Delegado para AutoCompleteBox.AsyncPopulator (D22 del spec: mínimo de
    /// caracteres antes de disparar la búsqueda y tope de resultados con aviso — mismo
    /// motivo que MovimientoHistorialViewModel.BuscarProductosAsync: ListarActivosAsync NO
    /// pagina ni limita).</summary>
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> BuscarExpedientesAsync { get; }

    public ClasificacionTareaPanelViewModel(
        IProyectoService proyectosService, IDocumentoAdministrativoService documentosService,
        IConfirmacionService confirmacion)
    {
        _proyectosService = proyectosService;
        _documentosService = documentosService;
        _confirmacion = confirmacion;
        BuscarExpedientesAsync = BuscarExpedientesInternalAsync;
    }

    /// <summary>Puebla el combo de proyectos y precarga la selección (usado por el modal de
    /// reclasificación — D21: lo que el Admin ve precargado es exactamente lo que queda guardado
    /// si no toca nada). El combo siempre incluye el centinela "(ninguno)" primero y, si
    /// <paramref name="actual"/> trae un proyecto que ya no está entre los abiertos (se terminó o
    /// se canceló después de vincularse), lo agrega como opción con
    /// <paramref name="tituloProyectoActual"/> para no perder el vínculo: el servidor acepta
    /// mantener el mismo proyecto aunque esté cerrado (TareaService.ReclasificarAsync).
    ///
    /// Las llamadas HTTP van en try/catch: TareaFormView.axaml.cs la dispara desde un handler
    /// async void (DataContextChanged) y una excepción acá terminaría en crash.log sin avisar al
    /// operario. Molde de AdjuntosDocumentoPanelViewModel.RecargarAsync.</summary>
    public async Task InicializarAsync(DatosClasificacionTarea? actual = null, string? tituloProyectoActual = null)
    {
        try
        {
            var proyectos = await _proyectosService.ListarOpcionesAsync();
            PoblarProyectos(proyectos, actual?.ProyectoId, tituloProyectoActual);

            ProyectoSeleccionado = ProyectosDisponibles.FirstOrDefault(o => o.Id == actual?.ProyectoId)
                ?? ProyectosDisponibles[0];
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

    /// <summary>Antepone el centinela "(ninguno)" (Id=null, permite desvincular -- D21) y, si
    /// <paramref name="actualId"/> no aparece entre los proyectos abiertos, agrega una opción que
    /// preserva ese id (mismo criterio que los maestros inactivos en los formularios de gasto).</summary>
    private void PoblarProyectos(IEnumerable<OpcionProyectoDto> abiertos, int? actualId, string? tituloActual)
    {
        ProyectosDisponibles.Clear();
        ProyectosDisponibles.Add(new OpcionProyectoTarea(NombreOpcionNinguno, null));

        var encontroActual = actualId is null;
        foreach (var proyecto in abiertos)
        {
            ProyectosDisponibles.Add(new OpcionProyectoTarea(proyecto.Titulo, proyecto.Id));
            if (actualId == proyecto.Id) encontroActual = true;
        }

        if (!encontroActual)
            ProyectosDisponibles.Add(new OpcionProyectoTarea(tituloActual ?? $"(proyecto cerrado — id {actualId})", actualId));
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
            // gateado con Permisos.GestionarDocumentos, independiente de GestionarTareas. Un
            // usuario con tareas.gestionar pero sin documentos.gestionar recibiría un 403 apenas tipea acá
            // -- y como el panel se embebe tal cual en el modal de reclasificación (Task 10) y
            // en el alta de tarea (Task 12), ambos consumidores heredarían el problema sin
            // enterarse. Degradación con gracia (mismo criterio que
            // InicioViewModel.CargarAsync con el panel de vencimientos de tareas, líneas
            // 249-278): el buscador queda inhabilitado con un mensaje explicativo, pero el
            // resto del panel (el combo de proyecto, ObtenerDatos) sigue funcionando sin romperse.
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

    /// <summary>Reemplazo total (D21): los dos campos SIEMPRE reflejan la selección actual,
    /// incluido null como desvinculación explícita (el centinela "(ninguno)" tiene Id=null).</summary>
    public DatosClasificacionTarea ObtenerDatos() => new(ProyectoSeleccionado?.Id, DocumentoSeleccionado?.Id);
}

/// <summary>Opción del ComboBox de proyecto de este panel -- molde de
/// DocumentoListViewModel.OpcionTipoDocumento ("Todos", Valor=null). Id=null es el centinela
/// "sin proyecto".</summary>
public sealed record OpcionProyectoTarea(string Nombre, int? Id);
