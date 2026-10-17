using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// Fila de solo lectura de la lista de tareas: aplana la entidad y agrega la visibilidad de
/// acciones según estado de la fila y rol del usuario logueado (spec: "un Operador no ve las
/// acciones de Admin"). DiasParaVencer alimenta SignoNegativoBrushConverter para el resaltado
/// de vencidas: negativo cuando la fecha límite pasó y el estado sigue abierto, 0 en cualquier
/// otro caso (sin fecha límite, o estado terminal).
/// </summary>
public sealed class TareaFila
{
    public Tarea Tarea { get; }
    private readonly RolUsuario _rol;
    private readonly DateTime _ahoraUtc;
    private readonly TimeZoneInfo _zonaLocal;

    /// <summary>
    /// Overload de uso real (TareaListViewModel, TareaFormViewModel): usa el reloj y la zona
    /// horaria reales de la máquina. La app corre en el escritorio del operador, así que
    /// TimeZoneInfo.Local ya refleja la zona correcta sin configuración adicional.
    /// </summary>
    public TareaFila(Tarea tarea, RolUsuario rol) : this(tarea, rol, DateTime.UtcNow, TimeZoneInfo.Local)
    {
    }

    /// <summary>
    /// Overload testeable (fix panel de vencimientos, 2026-08-06): recibe el instante UTC y la
    /// zona horaria explícitos en vez de leerlos de DateTime.UtcNow/TimeZoneInfo.Local, para que
    /// los tests puedan fijar ambos y no dependan de la máquina ni del momento en que corre la
    /// suite (ver TareaFilaTests para el detalle del bug de zona horaria que motivó esto).
    /// </summary>
    public TareaFila(Tarea tarea, RolUsuario rol, DateTime ahoraUtc, TimeZoneInfo zonaLocal)
    {
        Tarea = tarea;
        _rol = rol;
        _ahoraUtc = ahoraUtc;
        _zonaLocal = zonaLocal;
    }

    public int Id => Tarea.Id;
    public string Titulo => Tarea.Titulo;
    public string PrioridadTexto => Tarea.Prioridad.ToString();
    public DateTime? FechaLimite => Tarea.FechaLimite;
    public string? TomadaPorNombre => Tarea.TomadaPor?.NombreUsuario;

    /// <summary>Etiquetas de clasificación para la tarjeta (spec 2026-09-08, D19): une los
    /// clasificadores asignados con " · ", null si no hay ninguno (así el XAML puede usar
    /// IsNotNull para ocultar la línea entera en vez de mostrar una cadena vacía).
    ///
    /// Fix (revisión final, Important 1b): el expediente quedaba afuera de las cinco porque
    /// DocumentoAdministrativo no tenía un texto presentable sin Anio (Numero solo no
    /// distingue expedientes de años distintos). Con Anio viajando de verdad (TareaApiClient),
    /// "Exp. {Numero}/{Anio}" es una línea más -- la ficha de detalle (TareaFormViewModel)
    /// SIEMPRE mostró el expediente; sin esto la lista quedaba inconsistente con la ficha.</summary>
    public string? EtiquetasClasificacion
    {
        get
        {
            var partes = new List<string>();
            if (Tarea.Zona is not null) partes.Add(Tarea.Zona.Nombre);
            if (Tarea.DimensionTematica is not null) partes.Add(Tarea.DimensionTematica.Nombre);
            if (Tarea.OrganismoResponsable is not null) partes.Add(Tarea.OrganismoResponsable.Nombre);
            if (Tarea.OrigenFinanciamiento is not null) partes.Add(Tarea.OrigenFinanciamiento.Nombre);
            if (Tarea.DocumentoAdministrativo is not null)
                partes.Add($"Exp. {Tarea.DocumentoAdministrativo.Numero}/{Tarea.DocumentoAdministrativo.Anio}");
            return partes.Count == 0 ? null : string.Join(" · ", partes);
        }
    }

    public decimal DiasParaVencer => CalcularDiasParaVencer(Tarea.FechaLimite, Tarea.Estado, _ahoraUtc, _zonaLocal);

    public bool EsVencida => DiasParaVencer < 0m;

    /// <summary>
    /// Texto de la fila del panel de vencimientos de Inicio (spec 2026-08-06): "hoy" para 0,
    /// "N día(s)" para positivos, "-N día(s)" para negativos (vencidas). Vive acá y no en el
    /// panel porque es pura presentación de DiasParaVencer, el mismo dato que ya expone esta
    /// clase para TareaListView.
    /// </summary>
    public string TextoDiasParaVencer
    {
        get
        {
            var dias = (int)DiasParaVencer;
            if (dias == 0) return "hoy";
            if (dias > 0) return dias == 1 ? "1 día" : $"{dias} días";
            var abs = -dias;
            return abs == 1 ? "-1 día" : $"-{abs} días";
        }
    }

    /// <summary>
    /// Cálculo puro de días para vencer (fix panel de vencimientos, 2026-08-06): la versión
    /// anterior comparaba FechaLimite.Date contra DateTime.UtcNow.Date directo. Uruguay es
    /// UTC-3: durante las 3 horas antes de la medianoche LOCAL (21:00-23:59), el reloj UTC ya
    /// pasó a "mañana" mientras el operador sigue "hoy" -- una tarea que vence hoy se mostraba
    /// vencida un día antes de tiempo. FechaLimite se persiste como una fecha de calendario
    /// "etiquetada" UTC (TareaFormViewModel.GuardarAsync: FechaLimiteSeleccionada.Value.Date +
    /// SpecifyKind Utc, NO una conversión real de zona), así que lo único que hay que corregir
    /// es contra qué "hoy" se compara: el día de calendario en la zona LOCAL del operador, nunca
    /// en UTC. Recibe ahoraUtc/zonaLocal como parámetros (no lee el reloj/la zona directo) para
    /// que sea 100% testeable con instantes fijos.
    /// </summary>
    public static decimal CalcularDiasParaVencer(
        DateTime? fechaLimite, EstadoTarea estado, DateTime ahoraUtc, TimeZoneInfo zonaLocal)
    {
        if (fechaLimite is null || estado is EstadoTarea.Terminada or EstadoTarea.Cancelada)
            return 0m;

        var ahoraUtcTagged = DateTime.SpecifyKind(ahoraUtc, DateTimeKind.Utc);
        var hoyLocal = TimeZoneInfo.ConvertTimeFromUtc(ahoraUtcTagged, zonaLocal).Date;
        return (decimal)(fechaLimite.Value.Date - hoyLocal).TotalDays;
    }

    // Fix (review final, Minor): antes recodificaban a mano las transiciones que ya vive
    // en Tarea.TransicionesValidas (vía PuedeTransicionarA) — segunda fuente de verdad que
    // podía desincronizarse en silencio si el dominio agregaba una transición nueva.
    public bool PuedeTomar    => Tarea.PuedeTransicionarA(EstadoTarea.EnCurso);
    public bool PuedeSoltar   => Tarea.PuedeTransicionarA(EstadoTarea.Pendiente);
    public bool PuedeTerminar => Tarea.PuedeTransicionarA(EstadoTarea.Terminada);

    public bool PuedeCancelar =>
        _rol == RolUsuario.Admin && Tarea.PuedeTransicionarA(EstadoTarea.Cancelada);
}

/// <summary>Opción del combo de Estado del listado: el conjunto de estados que deja ver (filtro en el cliente).</summary>
public sealed record OpcionEstadoTarea(string Nombre, IReadOnlyList<EstadoTarea> Estados);

/// <summary>
/// Pantalla "Tareas": una sola colección filtrada en el cliente por Estado ("Abiertas" por defecto); las cuatro
/// colecciones por estado de antes se siguen llenando. La vista dispara CargarAsync() vía DataContextChanged
/// (convención del proyecto).
/// </summary>
public partial class TareaListViewModel : ViewModelBase
{
    /// <summary>
    /// Mensaje para UnauthorizedAccessException (const, no literal repetido): lo referencian
    /// tanto ManejarErrorAsync como los tests, así un test que verifica la rama específica no
    /// pasaría "por accidente" si se la eliminara y el catch-all genérico tomara su lugar.
    /// </summary>
    public const string MensajeSinPermiso =
        "La sesión expiró o no tiene permiso para realizar esta acción. Vuelva a iniciar sesión e intente de nuevo.";

    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403) o se pierde el permiso en caliente.</summary>
    public const string MensajeSinPermisoTareas = "No tenés permiso para ver las tareas.";

    private readonly ITareaService        _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;
    private readonly ConsultaVigente      _consultas = new();

    [ObservableProperty] private bool _mostrarCanceladas;

    public ObservableCollection<TareaFila> Tareas { get; } = new();
    public ObservableCollection<TareaFila> Pendientes { get; } = new();
    public ObservableCollection<TareaFila> EnCurso { get; } = new();
    public ObservableCollection<TareaFila> Terminadas { get; } = new();
    public ObservableCollection<TareaFila> Canceladas { get; } = new();

    /// <summary>La colección de <see cref="Tareas"/> con el filtro de Estado aplicado: lo que ve la grilla.</summary>
    public DataGridCollectionView TareasView { get; }

    /// <summary>Opciones del combo de Estado, en el orden en que se muestran.</summary>
    public IReadOnlyList<OpcionEstadoTarea> FiltrosEstado { get; } = new[]
    {
        new OpcionEstadoTarea("Abiertas", new[] { EstadoTarea.Pendiente, EstadoTarea.EnCurso }),
        new OpcionEstadoTarea("Pendientes", new[] { EstadoTarea.Pendiente }),
        new OpcionEstadoTarea("En curso", new[] { EstadoTarea.EnCurso }),
        new OpcionEstadoTarea("Terminadas", new[] { EstadoTarea.Terminada }),
        new OpcionEstadoTarea("Canceladas", new[] { EstadoTarea.Cancelada }),
        new OpcionEstadoTarea("Todas", new[] { EstadoTarea.Pendiente, EstadoTarea.EnCurso, EstadoTarea.Terminada, EstadoTarea.Cancelada }),
    };

    [ObservableProperty] private OpcionEstadoTarea _filtroEstado;

    /// <summary>La fila sobre la que actúan las acciones del encabezado. Se limpia si el filtro la oculta.</summary>
    [ObservableProperty] private TareaFila? _filaSeleccionada;

    private int _cantidadVisible;

    /// <summary>Filas que deja ver el filtro de Estado (el pie "N tareas").</summary>
    public int CantidadVisible { get => _cantidadVisible; private set => SetProperty(ref _cantidadVisible, value); }

    /// <summary>Gatea la pantalla: Admin o el permiso del módulo. Lo re-lee <see cref="NotificarPermisosActualizados"/>.</summary>
    public bool PuedeGestionarTareas =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.GestionarTareas);

    public TareaListViewModel(
        ITareaService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;

        _filtroEstado = FiltrosEstado[0];
        TareasView = new DataGridCollectionView(Tareas) { Filter = EsVisible };
        Tareas.CollectionChanged += (_, _) => Recalcular();
    }

    /// <summary>Predicado del filtro: la grilla y el pie usan el mismo, así lo que se cuenta es lo que se ve.</summary>
    internal bool EsVisible(object item) =>
        item is TareaFila fila && FiltroEstado.Estados.Contains(fila.Tarea.Estado);

    partial void OnFiltroEstadoChanged(OpcionEstadoTarea value)
    {
        TareasView.Refresh();
        Recalcular();
    }

    /// <summary>Recuenta el pie y, si el filtro esconde la fila seleccionada, limpia la selección: Tomar, Terminar o
    /// Cancelar no pueden actuar sobre una tarea que no se ve.</summary>
    private void Recalcular()
    {
        CantidadVisible = Tareas.Count(f => EsVisible(f));
        if (FilaSeleccionada is { } seleccionada && !EsVisible(seleccionada))
            FilaSeleccionada = null;
    }

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar dentro de la card.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor el listado queda vacío: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion() => VaciarListas();

    private void VaciarListas()
    {
        Tareas.Clear();
        Pendientes.Clear();
        EnCurso.Clear();
        Terminadas.Clear();
        Canceladas.Clear();
    }

    /// <summary>
    /// Un 403 refrescó los permisos en caliente (convención 21): además de re-leer los <c>Puede*</c> (base), si ya no hay
    /// <c>tareas.gestionar</c> se vacía el listado y la pantalla pasa a "Sin permiso" (si no, la grilla vacía diría
    /// "Todavía no hay tareas").
    /// </summary>
    public override void NotificarPermisosActualizados()
    {
        if (!PuedeGestionarTareas)
        {
            VaciarListas();
            MarcarSinPermiso(MensajeSinPermisoTareas);
        }
        base.NotificarPermisosActualizados();
    }

    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var tareas = await _service.ListarAsync();
            if (!turno.EsLaUltima) return;
            var rol = _session.RolActual ?? RolUsuario.Operador;

            VaciarListas();

            foreach (var tarea in tareas)
            {
                var fila = new TareaFila(tarea, rol);
                Tareas.Add(fila);
                switch (tarea.Estado)
                {
                    case EstadoTarea.Pendiente: Pendientes.Add(fila); break;
                    case EstadoTarea.EnCurso:   EnCurso.Add(fila); break;
                    case EstadoTarea.Terminada: Terminadas.Add(fila); break;
                    case EstadoTarea.Cancelada: Canceladas.Add(fila); break;
                }
            }
        }, MensajeSinPermisoTareas, turno);
    }

    [RelayCommand]
    private void Nueva() => _navigation.Navegar<TareaFormViewModel>(vm => vm.CargarParaCrear());

    [RelayCommand]
    private void VerDetalle(TareaFila fila)
        => _navigation.Navegar<TareaFormViewModel>(vm => vm.CargarParaVer(fila.Tarea));

    [RelayCommand]
    private async Task TomarAsync(TareaFila fila)
    {
        try { await _service.TomarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task SoltarAsync(TareaFila fila)
    {
        try { await _service.SoltarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task TerminarAsync(TareaFila fila)
    {
        try { await _service.TerminarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task CancelarAsync(TareaFila fila)
    {
        var confirmar = await _confirmacion.PreguntarAsync($"¿Confirma cancelar la tarea \"{fila.Titulo}\"?");
        if (!confirmar) return;

        try { await _service.CancelarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    /// <summary>
    /// Único punto de traducción excepción → mensaje para todos los comandos de esta pantalla
    /// (fix real de IngresoPorFacturaViewModel, propagado acá: un AsyncRelayCommand no capturado
    /// no llega al handler global de Avalonia y termina en crash.log sin avisar al operario).
    /// Cualquier excepción no prevista cae en el catch-all como red de último recurso.
    /// </summary>
    private async Task ManejarErrorAsync(Exception ex)
    {
        var mensaje = ex switch
        {
            ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
                or ServidorNoDisponibleException => ex.Message,
            UnauthorizedAccessException => MensajeSinPermiso,
            _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
        };
        await _confirmacion.InformarAsync(mensaje);
    }
}
