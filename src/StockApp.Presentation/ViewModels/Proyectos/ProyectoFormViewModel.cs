using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Proyectos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Domain.Formato;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.ViewModels.Proyectos;

/// <summary>Opción de un combo de clasificador del proyecto. Id=null es el centinela "(ninguna)".</summary>
public sealed record OpcionCatalogoProyecto(string Nombre, int? Id);

/// <summary>
/// Alta y ficha de un proyecto (spec 2026-10-30). Dos modos:
/// <list type="bullet">
/// <item><see cref="CargarParaCrear"/>: los campos de <see cref="DatosProyecto"/>.</item>
/// <item><see cref="CargarParaVer"/>: los mismos campos (solo lectura si el proyecto está cerrado o sin
/// proyectos.gestionar), los botones de estado según <see cref="Proyecto.PuedeTransicionarA"/>, las notas, las tareas
/// del proyecto y su ejecución presupuestaria.</item>
/// </list>
/// Cargar* son síncronos y solo fijan el modo: la View llama <see cref="InicializarAsync"/> en DataContextChanged, que
/// trae la ficha y los catálogos (molde de GastoFormViewModel.CargarParaEditar + InicializarAsync).
/// </summary>
public partial class ProyectoFormViewModel : ViewModelBase, INotifyDataErrorInfo
{
    public const string MensajeSinPermiso =
        "La sesión expiró o no tiene permiso para realizar esta acción. Vuelva a iniciar sesión e intente de nuevo.";

    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga de la ficha (403).</summary>
    public const string MensajeSinPermisoProyecto = "No tenés permiso para ver este proyecto.";

    public const string MensajeTituloObligatorio = "El título es obligatorio.";
    public const string MensajePresupuestoNegativo = "El presupuesto no puede ser negativo.";

    private const string NombreOpcionNinguna = "(ninguna)";

    private readonly IProyectoService _service;
    private readonly ITareaService _tareas;
    private readonly IZonaService _zonas;
    private readonly IDimensionTematicaService _dimensiones;
    private readonly IOrganismoResponsableService _organismos;
    private readonly IOrigenFinanciamientoService _origenes;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly IConfirmacionService _confirmacion;

    private int _idProyecto;

    /// <summary>El proyecto de la ficha tal como lo devolvió el servidor: única fuente de las transiciones válidas.</summary>
    private Proyecto? _proyecto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditable), nameof(EsDetalle), nameof(MuestraAgregarNota), nameof(MuestraTareas),
        nameof(PuedeCrearTarea), nameof(PuedeIniciar), nameof(PuedeVolverAPendiente), nameof(PuedeTerminar),
        nameof(PuedeCancelar), nameof(PuedeReabrir))]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _esNuevo = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _titulo = string.Empty;

    [ObservableProperty] private string? _descripcion;
    [ObservableProperty] private PrioridadTarea _prioridadSeleccionada = PrioridadTarea.Media;
    [ObservableProperty] private DateTime? _fechaLimiteSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _presupuestoTexto = string.Empty;

    [ObservableProperty] private OpcionCatalogoProyecto? _zonaSeleccionada;
    [ObservableProperty] private OpcionCatalogoProyecto? _dimensionSeleccionada;
    [ObservableProperty] private OpcionCatalogoProyecto? _organismoSeleccionado;
    [ObservableProperty] private OpcionCatalogoProyecto? _origenSeleccionado;

    [ObservableProperty] private EstadoProyecto? _estado;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarNotaCommand))]
    private string _nuevaNotaTexto = string.Empty;

    // Ejecución presupuestaria de la ficha.
    [ObservableProperty] private decimal _totalGastado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TienePresupuesto))]
    private decimal? _presupuesto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaySobregiro), nameof(MuestraDisponible), nameof(Sobregiro))]
    private decimal? _disponible;

    [ObservableProperty] private bool _muestraListaGastos;

    public ObservableCollection<OpcionCatalogoProyecto> ZonasDisponibles { get; } = new();
    public ObservableCollection<OpcionCatalogoProyecto> DimensionesDisponibles { get; } = new();
    public ObservableCollection<OpcionCatalogoProyecto> OrganismosDisponibles { get; } = new();
    public ObservableCollection<OpcionCatalogoProyecto> OrigenesDisponibles { get; } = new();

    public IReadOnlyList<PrioridadTarea> PrioridadesDisponibles { get; } =
        new[] { PrioridadTarea.Baja, PrioridadTarea.Media, PrioridadTarea.Alta };

    public ObservableCollection<NotaProyecto> Notas { get; } = new();
    public ObservableCollection<TareaFila> Tareas { get; } = new();
    public ObservableCollection<GastoDeProyectoDto> Gastos { get; } = new();

    public ProyectoFormViewModel(
        IProyectoService service, ITareaService tareas,
        IZonaService zonas, IDimensionTematicaService dimensiones,
        IOrganismoResponsableService organismos, IOrigenFinanciamientoService origenes,
        ICurrentSession session, INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service = service;
        _tareas = tareas;
        _zonas = zonas;
        _dimensiones = dimensiones;
        _organismos = organismos;
        _origenes = origenes;
        _session = session;
        _navigation = navigation;
        _confirmacion = confirmacion;
    }

    // ── Permisos y modo ────────────────────────────────────────────────────────────────────

    public bool PuedeVerProyectos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerProyectos);

    public bool PuedeGestionarProyectos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarProyectos);

    /// <summary>Las tareas del proyecto se listan con el endpoint de tareas, que exige tareas.gestionar.</summary>
    public bool PuedeGestionarTareas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarTareas);

    public bool EsDetalle => !EsNuevo;

    /// <summary>Terminado o Cancelado: los campos quedan en solo lectura hasta reabrir.</summary>
    public bool EsCerrado => _proyecto?.EsCerrado ?? false;

    /// <summary>El alta siempre se edita; la ficha, solo abierta y con proyectos.gestionar.</summary>
    public bool EsEditable => EsNuevo || (PuedeGestionarProyectos && !EsCerrado);

    public bool PuedeIniciar => PuedeTransicion(EstadoProyecto.EnCurso) && _proyecto!.Estado == EstadoProyecto.Pendiente;
    public bool PuedeVolverAPendiente => PuedeTransicion(EstadoProyecto.Pendiente);
    public bool PuedeTerminar => PuedeTransicion(EstadoProyecto.Terminado);
    public bool PuedeCancelar => PuedeTransicion(EstadoProyecto.Cancelado);
    public bool PuedeReabrir => PuedeTransicion(EstadoProyecto.EnCurso) && EsCerrado;

    private bool PuedeTransicion(EstadoProyecto destino) =>
        EsDetalle && PuedeGestionarProyectos && _proyecto is not null && _proyecto.PuedeTransicionarA(destino);

    /// <summary>Las notas se agregan también a un proyecto cerrado (IProyectoService.AgregarNotaAsync).</summary>
    public bool MuestraAgregarNota => EsDetalle && PuedeGestionarProyectos;

    public bool MuestraTareas => EsDetalle && PuedeGestionarTareas;

    /// <summary>"Nueva tarea": el selector de proyecto de la tarea solo ofrece proyectos abiertos.</summary>
    public bool PuedeCrearTarea => MuestraTareas && !EsCerrado;

    // ── Ejecución presupuestaria ───────────────────────────────────────────────────────────

    /// <summary>Sin presupuesto (null) se ve solo el total gastado. Con 0 sí hay presupuesto.</summary>
    public bool TienePresupuesto => Presupuesto is not null;

    public bool HaySobregiro => Disponible is < 0m;

    public bool MuestraDisponible => Disponible is >= 0m;

    /// <summary>Lo gastado por encima del presupuesto, en positivo; null sin sobregiro.</summary>
    public decimal? Sobregiro => HaySobregiro ? Math.Abs(Disponible!.Value) : null;

    // ── Validación ─────────────────────────────────────────────────────────────────────────

    private static readonly string[] CamposValidados = { nameof(Titulo), nameof(PresupuestoTexto) };

    private readonly HashSet<string> _tocados = new();

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => CamposValidados.Any(c => ErrorDe(c) is not null);

    /// <summary>El error que la vista muestra debajo del campo: el de su regla, si el campo se edita y está tocado.</summary>
    public string? ErrorDe(string propiedad) => EsEditable && _tocados.Contains(propiedad) ? ReglaDe(propiedad) : null;

    public IEnumerable GetErrors(string? propertyName)
        => propertyName is not null && ErrorDe(propertyName) is { } error ? new[] { error } : Array.Empty<string>();

    /// <summary>Marca el campo como tocado sin cambiar su valor (la vista lo llama al salir del campo).</summary>
    public void MarcarTocado(string propiedad)
    {
        if (_tocados.Add(propiedad))
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    private void AlCambiarCampo(string propiedad)
    {
        _tocados.Add(propiedad);
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    partial void OnTituloChanged(string value) => AlCambiarCampo(nameof(Titulo));
    partial void OnPresupuestoTextoChanged(string value) => AlCambiarCampo(nameof(PresupuestoTexto));

    private string? ReglaDe(string propiedad) => propiedad switch
    {
        nameof(Titulo) => string.IsNullOrWhiteSpace(Titulo) ? MensajeTituloObligatorio : null,
        nameof(PresupuestoTexto) => ReglaDelPresupuesto(),
        _ => null,
    };

    /// <summary>Opcional: vacío es "sin presupuesto". Si se escribe, número es-UY no negativo.</summary>
    private string? ReglaDelPresupuesto()
    {
        if (string.IsNullOrWhiteSpace(PresupuestoTexto))
            return null;
        if (!FormatoEsUy.TryParseDecimal(PresupuestoTexto, out var valor, out var error))
            return error;
        return valor < 0m ? MensajePresupuestoNegativo : null;
    }

    private decimal? PresupuestoIngresado()
        => FormatoEsUy.TryParseDecimal(PresupuestoTexto, out var valor, out _) ? valor : null;

    // ── Carga ──────────────────────────────────────────────────────────────────────────────

    public void CargarParaCrear()
    {
        _idProyecto = 0;
        _proyecto = null;
        EsNuevo = true;
        Titulo = string.Empty;
        Descripcion = null;
        PrioridadSeleccionada = PrioridadTarea.Media;
        FechaLimiteSeleccionada = null;
        PresupuestoTexto = string.Empty;
        Estado = null;
        MensajeError = null;
        Notas.Clear();
        Tareas.Clear();
        Gastos.Clear();
        // Al final: asignar Titulo arriba lo marcaría como tocado y el alta nacería en rojo.
        _tocados.Clear();
        NotificarModo();
    }

    public void CargarParaVer(int id)
    {
        _idProyecto = id;
        EsNuevo = false;
        MensajeError = null;
    }

    /// <summary>Sin servidor al abrir, la ficha avisa en línea con Reintentar en vez del modal global.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>
    /// Trae la ficha (en modo ver), las tareas del proyecto y los catálogos de los combos. La dispara la View
    /// (DataContextChanged) y la repite Reintentar. Protegida: un 403 deja "Sin permiso" y un servidor caído deja
    /// "Sin conexión", sin modal.
    /// </summary>
    public async Task InicializarAsync()
    {
        await EjecutarCargaProtegidaAsync(async () =>
        {
            if (EsDetalle)
            {
                var ficha = await _service.ObtenerFichaAsync(_idProyecto);
                AplicarFicha(ficha);

                Tareas.Clear();
                if (MuestraTareas)
                {
                    var rol = _session.RolActual ?? RolUsuario.Operador;
                    foreach (var tarea in await _tareas.ListarPorProyectoAsync(_idProyecto))
                        Tareas.Add(new TareaFila(tarea, rol));
                }
            }

            await CargarCatalogosAsync();
        }, MensajeSinPermisoProyecto);
    }

    private void AplicarFicha(FichaProyectoDto ficha)
    {
        var p = ficha.Proyecto;
        _proyecto = p;
        Titulo = p.Titulo;
        Descripcion = p.Descripcion;
        PrioridadSeleccionada = p.Prioridad;
        FechaLimiteSeleccionada = p.FechaLimite;
        PresupuestoTexto = p.Presupuesto is decimal presupuesto ? FormatoEsUy.Decimal(presupuesto) : string.Empty;
        Estado = p.Estado;

        TotalGastado = ficha.TotalGastado;
        Presupuesto = p.Presupuesto;
        Disponible = ficha.Disponible;
        MuestraListaGastos = ficha.Gastos is not null;
        Gastos.Clear();
        foreach (var gasto in ficha.Gastos ?? Array.Empty<GastoDeProyectoDto>())
            Gastos.Add(gasto);

        Notas.Clear();
        foreach (var nota in p.Notas.OrderBy(n => n.Fecha))
            Notas.Add(nota);

        _tocados.Clear();
        NotificarModo();
    }

    /// <summary>
    /// Puebla los cuatro combos con el centinela "(ninguna)" primero. Si el valor actual del proyecto ya no está entre
    /// los activos (el maestro se dio de baja después), lo agrega con el nombre de su nav para no perderlo (mismo criterio
    /// que GastoFormViewModel.InicializarAsync). En solo lectura no se piden los catálogos: alcanza con el valor actual.
    /// </summary>
    private async Task CargarCatalogosAsync()
    {
        IEnumerable<(int Id, string Nombre)> zonas = [], dimensiones = [], organismos = [], origenes = [];
        if (EsEditable)
        {
            try
            {
                zonas = (await _zonas.ListarActivasAsync()).Select(z => (z.Id, z.Nombre)).ToList();
                dimensiones = (await _dimensiones.ListarActivasAsync()).Select(d => (d.Id, d.Nombre)).ToList();
                organismos = (await _organismos.ListarActivasAsync()).Select(o => (o.Id, o.Nombre)).ToList();
                origenes = (await _origenes.ListarActivasAsync()).Select(o => (o.Id, o.Nombre)).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                // Los catálogos los gatea el servidor con tareas.gestionar: sin ese permiso los combos quedan con
                // "(ninguna)" y el valor actual, y el resto del formulario sigue funcionando (no pasa a "Sin permiso").
                zonas = dimensiones = organismos = origenes = [];
            }
        }

        ZonaSeleccionada = Poblar(ZonasDisponibles, zonas, _proyecto?.ZonaId, _proyecto?.Zona?.Nombre);
        DimensionSeleccionada = Poblar(DimensionesDisponibles, dimensiones, _proyecto?.DimensionTematicaId, _proyecto?.DimensionTematica?.Nombre);
        OrganismoSeleccionado = Poblar(OrganismosDisponibles, organismos, _proyecto?.OrganismoResponsableId, _proyecto?.OrganismoResponsable?.Nombre);
        OrigenSeleccionado = Poblar(OrigenesDisponibles, origenes, _proyecto?.OrigenFinanciamientoId, _proyecto?.OrigenFinanciamiento?.Nombre);
    }

    private static OpcionCatalogoProyecto Poblar(
        ObservableCollection<OpcionCatalogoProyecto> destino, IEnumerable<(int Id, string Nombre)> activos,
        int? actualId, string? nombreActual)
    {
        destino.Clear();
        destino.Add(new OpcionCatalogoProyecto(NombreOpcionNinguna, null));
        foreach (var (id, nombre) in activos)
            destino.Add(new OpcionCatalogoProyecto(nombre, id));

        if (actualId is int actual && destino.All(o => o.Id != actual))
            destino.Add(new OpcionCatalogoProyecto(nombreActual ?? $"(id {actual})", actual));

        return destino.First(o => o.Id == actualId);
    }

    /// <summary>Re-avisa todo lo que depende del modo, del proyecto cargado y de los permisos.</summary>
    private void NotificarModo()
    {
        OnPropertyChanged(nameof(EsCerrado));
        OnPropertyChanged(nameof(EsEditable));
        OnPropertyChanged(nameof(PuedeIniciar));
        OnPropertyChanged(nameof(PuedeVolverAPendiente));
        OnPropertyChanged(nameof(PuedeTerminar));
        OnPropertyChanged(nameof(PuedeCancelar));
        OnPropertyChanged(nameof(PuedeReabrir));
        OnPropertyChanged(nameof(MuestraAgregarNota));
        OnPropertyChanged(nameof(MuestraTareas));
        OnPropertyChanged(nameof(PuedeCrearTarea));
        GuardarCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Un 403 refrescó los permisos en caliente: sin proyectos.ver la ficha pasa a "Sin permiso".</summary>
    public override void NotificarPermisosActualizados()
    {
        if (!PuedeVerProyectos)
            MarcarSinPermiso(MensajeSinPermisoProyecto);
        base.NotificarPermisosActualizados();
        GuardarCommand.NotifyCanExecuteChanged();
    }

    // ── Comandos ───────────────────────────────────────────────────────────────────────────

    private bool PuedeGuardar() => EsEditable && CamposValidados.All(c => ReglaDe(c) is null);

    public IAsyncRelayCommand GuardarCommand => field ??= ComandoDeGuardado(GuardarAsync, PuedeGuardar);

    private async Task GuardarAsync()
    {
        MensajeError = null;
        var datos = new DatosProyecto(
            Titulo.Trim(),
            string.IsNullOrWhiteSpace(Descripcion) ? null : Descripcion,
            PrioridadSeleccionada,
            // Mismo criterio que TareaFormViewModel: Npgsql rechaza Kind=Local en timestamptz.
            FechaLimiteSeleccionada.HasValue
                ? DateTime.SpecifyKind(FechaLimiteSeleccionada.Value.Date, DateTimeKind.Utc)
                : null,
            PresupuestoIngresado(),
            ZonaSeleccionada?.Id, DimensionSeleccionada?.Id, OrganismoSeleccionado?.Id, OrigenSeleccionado?.Id);
        try
        {
            if (EsNuevo)
            {
                var id = await _service.CrearAsync(datos);
                _navigation.Navegar<ProyectoFormViewModel>(vm => vm.CargarParaVer(id));
                return;
            }

            await _service.ModificarAsync(_idProyecto, datos);
            await InicializarAsync();
            await _confirmacion.InformarAsync("Proyecto guardado.");
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    private bool PuedeAgregarNota() => !string.IsNullOrWhiteSpace(NuevaNotaTexto);

    public IAsyncRelayCommand AgregarNotaCommand => field ??= ComandoDeGuardado(AgregarNotaAsync, PuedeAgregarNota);

    private async Task AgregarNotaAsync()
    {
        MensajeError = null;
        var texto = NuevaNotaTexto;
        try
        {
            await _service.AgregarNotaAsync(_idProyecto, texto);
            Notas.Add(new NotaProyecto { ProyectoId = _idProyecto, Texto = texto.Trim(), Fecha = DateTime.UtcNow });
            NuevaNotaTexto = string.Empty;
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    [RelayCommand] private Task IniciarAsync() => CambiarEstadoAsync(EstadoProyecto.EnCurso);
    [RelayCommand] private Task VolverAPendienteAsync() => CambiarEstadoAsync(EstadoProyecto.Pendiente);
    [RelayCommand] private Task TerminarAsync() => CambiarEstadoAsync(EstadoProyecto.Terminado);
    [RelayCommand] private Task ReabrirAsync() => CambiarEstadoAsync(EstadoProyecto.EnCurso);

    [RelayCommand]
    private async Task CancelarAsync()
    {
        if (!await _confirmacion.PreguntarAsync($"¿Confirma cancelar el proyecto \"{Titulo}\"?"))
            return;
        await CambiarEstadoAsync(EstadoProyecto.Cancelado);
    }

    /// <summary>Cambia el estado y recarga la ficha desde el servidor: el modo de edición depende del estado nuevo.</summary>
    private async Task CambiarEstadoAsync(EstadoProyecto destino)
    {
        MensajeError = null;
        try
        {
            await _service.CambiarEstadoAsync(_idProyecto, destino);
            await InicializarAsync();
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    [RelayCommand]
    private void NuevaTarea()
    {
        var proyectoId = _idProyecto;
        _navigation.Navegar<TareaFormViewModel>(vm => vm.CargarParaCrear(proyectoId));
    }

    [RelayCommand]
    private void VerTarea(TareaFila fila) => _navigation.Navegar<TareaFormViewModel>(vm => vm.CargarParaVer(fila.Tarea));

    [RelayCommand]
    private void Volver() => _navigation.Navegar<ProyectoListViewModel>();

    private static string ResolverMensajeError(Exception ex) => ex switch
    {
        ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
            or ServidorNoDisponibleException => ex.Message,
        UnauthorizedAccessException => MensajeSinPermiso,
        _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
    };
}
