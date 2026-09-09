using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// Doble uso (spec: "el panel de detalle muestra la descripción y el hilo de notas"): modo
/// alta (EsNuevaTarea = true) para crear una tarea nueva, y modo detalle (EsNuevaTarea =
/// false) para ver una tarea existente, su hilo de notas y —solo Admin— cambiarle la
/// prioridad. Título/descripción/fecha límite NO se editan después de creada: ninguna
/// acción del módulo lo permite (fuera de alcance del spec: "reasignación explícita").
/// Cargar* son síncronos (sin combos que precargar), por eso TareaFormView.axaml.cs no
/// necesita wiring de DataContextChanged.
/// </summary>
public partial class TareaFormViewModel : ViewModelBase
{
    /// <summary>
    /// Mensaje para UnauthorizedAccessException (const, no literal repetido): lo referencian
    /// tanto ResolverMensajeError como los tests, así un test que verifica la rama específica no
    /// pasaría "por accidente" si se la eliminara y el catch-all genérico tomara su lugar.
    /// </summary>
    public const string MensajeSinPermiso =
        "La sesión expiró o no tiene permiso para realizar esta acción. Vuelva a iniciar sesión e intente de nuevo.";

    private readonly ITareaService        _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;
    private readonly IClasificacionTareaDialogService _dialogoClasificacion;

    private int _idTarea;

    /// <summary>
    /// Espejo de Tarea.EsTerminal para la tarea cargada (decisión 14 del spec): se consulta
    /// una sola vez en CargarParaVer, no se recalcula con un estado propio del VM, para no
    /// duplicar el conocimiento de qué estados son terminales fuera del dominio.
    /// </summary>
    private bool _tareaEsTerminal;

    /// <summary>Ids actuales de clasificación de la tarea cargada (spec 2026-09-08): precarga
    /// el modal de reclasificación (Task 10) — D21, el Admin ve exactamente lo que hay
    /// guardado. Se recalcula en CargarParaVer, no en el alta (ahí no hay "actual").</summary>
    private DatosClasificacionTarea _clasificacionActual = new(null, null, null, null, null);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _titulo = string.Empty;

    [ObservableProperty] private string? _descripcion;
    [ObservableProperty] private DateTime? _fechaLimiteSeleccionada;
    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private string? _tomadaPorNombre;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty] private string? _zonaTexto;
    [ObservableProperty] private string? _dimensionTematicaTexto;
    [ObservableProperty] private string? _organismoResponsableTexto;
    [ObservableProperty] private string? _origenFinanciamientoTexto;
    [ObservableProperty] private string? _documentoAdministrativoTexto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuestraCambioPrioridad))]
    [NotifyPropertyChangedFor(nameof(MuestraReclasificar))]
    private bool _esNuevaTarea = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarNotaCommand))]
    private string _nuevaNotaTexto = string.Empty;

    [ObservableProperty] private PrioridadTarea _prioridadSeleccionada;

    public ObservableCollection<NotaTarea> Notas { get; } = new();
    public IReadOnlyList<PrioridadTarea> PrioridadesDisponibles { get; } =
        new[] { PrioridadTarea.Baja, PrioridadTarea.Media, PrioridadTarea.Alta };

    /// <summary>Panel embebido de clasificación (Task 9): solo se usa en modo alta — en
    /// modo detalle los cinco campos se muestran de solo lectura (ZonaTexto, etc.) y se
    /// reclasifican vía el modal (ReclasificarCommand), no editando este panel in-place.</summary>
    public ClasificacionTareaPanelViewModel ClasificacionPanel { get; }

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    /// <summary>
    /// Decisión 14 del spec: además de ser Admin y no estar en modo alta, la tarea no puede
    /// estar en un estado terminal — un botón que siempre va a fallar con 409 es peor que no
    /// tener botón.
    /// </summary>
    public bool MuestraCambioPrioridad => EsAdmin && !EsNuevaTarea && !_tareaEsTerminal;

    /// <summary>
    /// Botón "Reclasificar" (spec 2026-09-08, D9): a propósito SIN la condición
    /// !_tareaEsTerminal que sí tiene MuestraCambioPrioridad — la reclasificación alcanza
    /// también tareas Terminada/Cancelada (corrige un reporte mal salido sin reabrir la
    /// tarea). Copiar MuestraCambioPrioridad tal cual acá sería un bug.
    /// </summary>
    public bool MuestraReclasificar => EsAdmin && !EsNuevaTarea;

    public TareaFormViewModel(
        ITareaService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion,
        ClasificacionTareaPanelViewModel clasificacionPanel,
        IClasificacionTareaDialogService dialogoClasificacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
        ClasificacionPanel = clasificacionPanel;
        _dialogoClasificacion = dialogoClasificacion;
    }

    public void CargarParaCrear()
    {
        _idTarea = 0;
        _tareaEsTerminal = false;
        EsNuevaTarea = true;
        Titulo = string.Empty;
        Descripcion = null;
        FechaLimiteSeleccionada = null;
        EstadoTexto = string.Empty;
        TomadaPorNombre = null;
        MensajeError = null;
        Notas.Clear();
    }

    public void CargarParaVer(Tarea tarea)
    {
        _idTarea = tarea.Id;
        _tareaEsTerminal = tarea.EsTerminal;
        EsNuevaTarea = false;
        Titulo = tarea.Titulo;
        Descripcion = tarea.Descripcion;
        FechaLimiteSeleccionada = tarea.FechaLimite;
        EstadoTexto = tarea.Estado.ToString();
        TomadaPorNombre = tarea.TomadaPor?.NombreUsuario;
        PrioridadSeleccionada = tarea.Prioridad;
        MensajeError = null;

        ZonaTexto = tarea.Zona?.Nombre;
        DimensionTematicaTexto = tarea.DimensionTematica?.Nombre;
        OrganismoResponsableTexto = tarea.OrganismoResponsable?.Nombre;
        OrigenFinanciamientoTexto = tarea.OrigenFinanciamiento?.Nombre;
        // Fix (revisión final, Important 1): antes anteponía Tipo acá Y el XAML ya antepone la
        // etiqueta "Expediente:" -- se leía "Expediente: Expediente 0099/0" (palabra duplicada
        // + año en 0 porque el wire no lo traía). Con Anio viajando de verdad (TareaApiClient),
        // el texto queda "0099/2026" y la etiqueta del XAML aporta el "Expediente:" una sola vez.
        DocumentoAdministrativoTexto = tarea.DocumentoAdministrativo is null
            ? null : $"{tarea.DocumentoAdministrativo.Numero}/{tarea.DocumentoAdministrativo.Anio}";
        _clasificacionActual = new DatosClasificacionTarea(
            tarea.ZonaId, tarea.DimensionTematicaId, tarea.OrganismoResponsableId,
            tarea.OrigenFinanciamientoId, tarea.DocumentoAdministrativoId);

        Notas.Clear();
        foreach (var nota in tarea.Notas)
            Notas.Add(nota);
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Titulo);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            var clasificacion = ClasificacionPanel.ObtenerDatos();
            await _service.CrearAsync(new Tarea
            {
                Titulo = Titulo,
                Descripcion = string.IsNullOrWhiteSpace(Descripcion) ? null : Descripcion,
                // Fix (review final, Important): mismo criterio que el resto de los VMs con
                // fecha del proyecto (Gasto/Ingreso Form, IngresoPorFactura, PagosGasto) —
                // si el CalendarDatePicker entrega Kind=Local, Npgsql rechaza el insert en
                // timestamptz (el converter del servidor solo normaliza Unspecified).
                FechaLimite = FechaLimiteSeleccionada.HasValue
                    ? DateTime.SpecifyKind(FechaLimiteSeleccionada.Value.Date, DateTimeKind.Utc)
                    : null,
                ZonaId = clasificacion.ZonaId,
                DimensionTematicaId = clasificacion.DimensionTematicaId,
                OrganismoResponsableId = clasificacion.OrganismoResponsableId,
                OrigenFinanciamientoId = clasificacion.OrigenFinanciamientoId,
                DocumentoAdministrativoId = clasificacion.DocumentoAdministrativoId,
            });
            _navigation.Navegar<TareaListViewModel>();
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    private bool PuedeAgregarNota() => !string.IsNullOrWhiteSpace(NuevaNotaTexto);

    [RelayCommand(CanExecute = nameof(PuedeAgregarNota))]
    private async Task AgregarNotaAsync()
    {
        MensajeError = null;
        var texto = NuevaNotaTexto;
        try
        {
            await _service.AgregarNotaAsync(_idTarea, texto);
            Notas.Add(new NotaTarea { TareaId = _idTarea, Texto = texto, Fecha = DateTime.UtcNow, EsAutomatica = false });
            NuevaNotaTexto = string.Empty;
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    [RelayCommand]
    private async Task CambiarPrioridadAsync()
    {
        MensajeError = null;
        try
        {
            await _service.CambiarPrioridadAsync(_idTarea, PrioridadSeleccionada);
            await _confirmacion.InformarAsync($"Prioridad actualizada a {PrioridadSeleccionada}.");
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    [RelayCommand]
    private async Task ReclasificarAsync()
    {
        MensajeError = null;
        try
        {
            var resultado = await _dialogoClasificacion.PedirClasificacionAsync(_clasificacionActual);
            if (resultado is null) return; // el Admin canceló el modal

            await _service.ReclasificarAsync(_idTarea, resultado);

            // Molde de DocumentoFormViewModel.RecargarAsync: refresca desde el servidor y
            // vuelve a popular los campos de solo lectura vía CargarParaVer, en vez de
            // mantener una copia local optimista.
            var actualizada = await _service.ObtenerPorIdAsync(_idTarea);
            if (actualizada is not null)
                CargarParaVer(actualizada);

            await _confirmacion.InformarAsync("Clasificación actualizada.");
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    [RelayCommand]
    private void Volver() => _navigation.Navegar<TareaListViewModel>();

    /// <summary>
    /// Único punto de traducción excepción → mensaje para los comandos de esta pantalla (fix
    /// real de IngresoPorFacturaViewModel, propagado acá: un AsyncRelayCommand no capturado no
    /// llega al handler global de Avalonia y termina en crash.log sin avisar al operario).
    /// Cualquier excepción no prevista cae en el catch-all como red de último recurso.
    /// </summary>
    private static string ResolverMensajeError(Exception ex) => ex switch
    {
        ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
            or ServidorNoDisponibleException => ex.Message,
        UnauthorizedAccessException => MensajeSinPermiso,
        _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
    };
}
