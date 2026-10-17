using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Documentos;

/// <summary>
/// Fila de solo lectura de la lista de documentos: aplana la entidad y agrega el gating de
/// acciones por transición de estado (dominio, D4) combinado con rol (spec: las acciones
/// documentos.administrar -- Anular/Reabrir -- las oculta un Operador aunque el dominio
/// permita la transición). Molde: TareaFila.PuedeCancelar.
/// </summary>
public sealed class DocumentoFila
{
    public DocumentoAdministrativo Documento { get; }
    private readonly RolUsuario _rol;

    public DocumentoFila(DocumentoAdministrativo documento, RolUsuario rol)
    {
        Documento = documento;
        _rol = rol;
    }

    public int Id => Documento.Id;
    public string Numero => Documento.Numero;
    public int Anio => Documento.Anio;

    /// <summary>"45/2026": el texto de la columna NÚMERO (y con el que se mide).</summary>
    public string NumeroCompleto => $"{Numero}/{Anio}";

    public string TipoTexto => Documento.Tipo.ToString();
    public DateTime FechaEmision => Documento.FechaEmision;
    public string Descripcion => Documento.Descripcion;
    public string EstadoTexto => Documento.Estado.ToString();
    public string? RegistradoPorNombre => Documento.RegistradoPor?.NombreUsuario;

    // PuedeIniciar exige ADEMÁS Estado == Pendiente, no solo PuedeTransicionarA(EnProceso): en
    // la tabla del dominio (D4), Finalizado/Anulado -> EnProceso también son transiciones
    // válidas (son la reapertura), así que PuedeTransicionarA(EnProceso) da true también sobre
    // un documento cerrado. Sin este chequeo extra, el botón "Iniciar" aparecería habilitado
    // sobre un documento Finalizado/Anulado y el usuario comería el 409 que el servicio ya
    // rechaza (Task 8, guarda simétrica a la de ReabrirAsync).
    public bool PuedeIniciar          => Documento.Estado == EstadoDocumento.Pendiente && Documento.PuedeTransicionarA(EstadoDocumento.EnProceso);
    public bool PuedeVolverAPendiente => Documento.PuedeTransicionarA(EstadoDocumento.Pendiente);
    public bool PuedeFinalizar        => Documento.PuedeTransicionarA(EstadoDocumento.Finalizado);

    public bool PuedeAnular =>
        _rol == RolUsuario.Admin && Documento.PuedeTransicionarA(EstadoDocumento.Anulado);

    public bool PuedeReabrir =>
        _rol == RolUsuario.Admin && Documento.EsCerrado;
}

/// <summary>
/// Opción de filtro por tipo de documento para los ComboBox de Activos/Historial.
/// Valor=null representa "Todos" (sin filtro de tipo). Molde: OpcionTipoMovimiento
/// (MovimientoHistorialViewModel).
/// </summary>
public sealed record OpcionTipoDocumento(string Nombre, TipoDocumento? Valor);

/// <summary>
/// Opción de filtro por estado para el ComboBox de Historial. Solo ofrece los dos estados
/// cerrados (Finalizado/Anulado, D9) -- un Pendiente/EnProceso nunca aparecería en
/// ListarCerradosAsync, así que ofrecerlo como opción sería un filtro que siempre da vacío.
/// Valor=null representa "Todos" (sin filtro de estado).
/// </summary>
public sealed record OpcionEstadoDocumento(string Nombre, EstadoDocumento? Valor);

/// <summary>Opción del combo "Ver" de la pantalla: Activos (Pendiente/EnProceso) o Historial (Finalizado/Anulado).</summary>
public sealed record OpcionVistaDocumentos(string Nombre, bool EsHistorial);

/// <summary>
/// Pantalla "Documentos administrativos": dos vistas, Activos (Pendiente/EnProceso) e Historial
/// (Finalizado/Anulado, D9), elegidas con el combo "Ver". Cada cambio de vista o de filtro vuelve a consultar
/// (recarga automática con debounce); el historial no se consulta al abrir la pantalla y exige año (el servidor lo
/// rechaza si viene nulo, D9); la UI precarga el año actual como valor inicial del filtro.
/// La vista dispara CargarAsync() vía DataContextChanged (convención del proyecto).
/// </summary>
public partial class DocumentoListViewModel : ViewModelBase
{
    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403) o se pierde el permiso en caliente.</summary>
    public const string MensajeSinPermisoDocumentos = "No tenés permiso para ver los documentos.";

    private readonly IDocumentoAdministrativoService _service;
    private readonly ICurrentSession                 _session;
    private readonly INavigationService               _navigation;
    private readonly IConfirmacionService              _confirmacion;
    private readonly ConsultaVigente                  _consultas = new();

    /// <summary>Espera del debounce de los filtros que recargan; los tests la cambian después de construir el VM.</summary>
    internal TimeSpan EsperaDebounce { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>La última recarga programada: los tests la esperan para observar el resultado.</summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);

    /// <summary>Se prende en la primera CargarAsync, ANTES de pedir el listado: lo que se puso antes (filtros que la
    /// pantalla restaura al construirse) no programa una consulta; un cambio durante la espera del listado gana por turno.</summary>
    private bool _recargaArmada;

    public IReadOnlyList<OpcionVistaDocumentos> Vistas { get; } = new[]
    {
        new OpcionVistaDocumentos("Activos", false),
        new OpcionVistaDocumentos("Historial", true),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsVistaHistorial))]
    [NotifyPropertyChangedFor(nameof(FilaSeleccionada))]
    [NotifyPropertyChangedFor(nameof(CantidadEnVista))]
    [NotifyPropertyChangedFor(nameof(HayFiltrosEnLaVista))]
    private OpcionVistaDocumentos _vistaSeleccionada;

    public bool EsVistaHistorial => VistaSeleccionada.EsHistorial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilaSeleccionada))]
    private DocumentoFila? _filaSeleccionadaActivos;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilaSeleccionada))]
    private DocumentoFila? _filaSeleccionadaHistorial;

    /// <summary>La fila sobre la que actúan las acciones del encabezado: la seleccionada en la vista actual.</summary>
    public DocumentoFila? FilaSeleccionada => EsVistaHistorial ? FilaSeleccionadaHistorial : FilaSeleccionadaActivos;

    /// <summary>Activos con tipo o texto elegidos: distingue "no hay documentos" de "ningún documento coincide".</summary>
    public bool HayFiltrosEnActivos => FiltroActivosTipo is not null || !string.IsNullOrWhiteSpace(FiltroActivosTexto);

    /// <summary>Filas de la vista ACTUAL (Activos o Historial): con eso se deciden "Cargando", "Sin documentos" y "Sin resultados".</summary>
    public int CantidadEnVista => EsVistaHistorial ? Historial.Count : Activos.Count;

    /// <summary>La vista actual tiene algún filtro que pueda explicar que no haya filas: en Historial siempre (el año), en
    /// Activos solo con Tipo o texto.</summary>
    public bool HayFiltrosEnLaVista => EsVistaHistorial || HayFiltrosEnActivos;

    /// <summary>Gatea la pantalla: Admin o el permiso del módulo. Lo re-lee <see cref="NotificarPermisosActualizados"/>.</summary>
    public bool PuedeGestionarDocumentos =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.GestionarDocumentos);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltrosEnActivos))]
    [NotifyPropertyChangedFor(nameof(HayFiltrosEnLaVista))]
    private TipoDocumento? _filtroActivosTipo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltrosEnActivos))]
    [NotifyPropertyChangedFor(nameof(HayFiltrosEnLaVista))]
    private string? _filtroActivosTexto;

    [ObservableProperty] private TipoDocumento? _filtroHistorialTipo;
    [ObservableProperty] private int? _filtroHistorialAnio = DateTime.UtcNow.Year;
    [ObservableProperty] private string? _filtroHistorialTexto;
    [ObservableProperty] private EstadoDocumento? _filtroHistorialEstado;

    /// <summary>Opción elegida en el ComboBox de Tipo de la solapa Activos (Valor=null = "Todos").</summary>
    [ObservableProperty] private OpcionTipoDocumento? _filtroActivosTipoSeleccionado;

    /// <summary>Opción elegida en el ComboBox de Tipo de la solapa Historial (Valor=null = "Todos").</summary>
    [ObservableProperty] private OpcionTipoDocumento? _filtroHistorialTipoSeleccionado;

    /// <summary>Opción elegida en el ComboBox de Estado de la solapa Historial (Valor=null = "Todos").</summary>
    [ObservableProperty] private OpcionEstadoDocumento? _filtroHistorialEstadoSeleccionado;

    public ObservableCollection<DocumentoFila> Activos { get; } = new();
    public ObservableCollection<DocumentoFila> Historial { get; } = new();

    /// <summary>Opciones fijas para los ComboBox de filtro por tipo ("Todos" + los 3 tipos del dominio).</summary>
    public ObservableCollection<OpcionTipoDocumento> TiposDisponibles { get; } = new()
    {
        new OpcionTipoDocumento("Todos", null),
        new OpcionTipoDocumento("Expediente", TipoDocumento.Expediente),
        new OpcionTipoDocumento("Oficio", TipoDocumento.Oficio),
        new OpcionTipoDocumento("Suministro", TipoDocumento.Suministro),
    };

    /// <summary>Opciones fijas para el ComboBox de filtro por estado de Historial ("Todos" + los 2 estados cerrados).</summary>
    public ObservableCollection<OpcionEstadoDocumento> EstadosDisponibles { get; } = new()
    {
        new OpcionEstadoDocumento("Todos", null),
        new OpcionEstadoDocumento("Finalizado", EstadoDocumento.Finalizado),
        new OpcionEstadoDocumento("Anulado", EstadoDocumento.Anulado),
    };

    public DocumentoListViewModel(
        IDocumentoAdministrativoService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;

        Activos.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CantidadEnVista));
        Historial.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CantidadEnVista));
        _vistaSeleccionada = Vistas[0];
        _filtroActivosTipoSeleccionado = TiposDisponibles[0];
        _filtroHistorialTipoSeleccionado = TiposDisponibles[0];
        _filtroHistorialEstadoSeleccionado = EstadosDisponibles[0];
    }

    partial void OnFiltroActivosTipoSeleccionadoChanged(OpcionTipoDocumento? value)
        => FiltroActivosTipo = value?.Valor;

    partial void OnFiltroHistorialTipoSeleccionadoChanged(OpcionTipoDocumento? value)
        => FiltroHistorialTipo = value?.Valor;

    partial void OnFiltroHistorialEstadoSeleccionadoChanged(OpcionEstadoDocumento? value)
        => FiltroHistorialEstado = value?.Valor;

    partial void OnVistaSeleccionadaChanged(OpcionVistaDocumentos value) => ProgramarRecarga();
    partial void OnFiltroActivosTipoChanged(TipoDocumento? value) => ProgramarRecarga();
    partial void OnFiltroActivosTextoChanged(string? value) => ProgramarRecarga();
    partial void OnFiltroHistorialTipoChanged(TipoDocumento? value) => ProgramarRecarga();
    partial void OnFiltroHistorialAnioChanged(int? value) => ProgramarRecarga();
    partial void OnFiltroHistorialTextoChanged(string? value) => ProgramarRecarga();
    partial void OnFiltroHistorialEstadoChanged(EstadoDocumento? value) => ProgramarRecarga();

    private void ProgramarRecarga()
    {
        if (_recargaArmada)
            _tareaDebounce = Recarga.Programar(CargarAsync);
    }

    private RolUsuario RolActualODefault => _session.RolActual ?? RolUsuario.Operador;

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar dentro de la card.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor la vista actual queda vacía: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion() => (EsVistaHistorial ? Historial : Activos).Clear();

    /// <summary>Reintentar consulta con los filtros que están en pantalla, no con los de la consulta que falló.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    /// <summary>
    /// Un 403 refrescó los permisos en caliente (convención 21): además de re-leer los <c>Puede*</c> (base), si ya no hay
    /// <c>documentos.gestionar</c> se vacían las dos vistas y la pantalla pasa a "Sin permiso" (si no, la grilla vacía
    /// diría "Todavía no hay documentos").
    /// </summary>
    public override void NotificarPermisosActualizados()
    {
        if (!PuedeGestionarDocumentos)
        {
            Activos.Clear();
            Historial.Clear();
            MarcarSinPermiso(MensajeSinPermisoDocumentos);
        }
        base.NotificarPermisosActualizados();
    }

    /// <summary>Carga la vista ACTUAL (Activos o Historial) con los filtros que están en pantalla.</summary>
    public async Task CargarAsync()
    {
        _recargaArmada = true;
        if (EsVistaHistorial)
            await CargarHistorialAsync();
        else
            await CargarActivosAsync();
    }

    private async Task CargarActivosAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var filtro = new FiltroDocumentos(FiltroActivosTipo, null, FiltroActivosTexto, null);
            var documentos = await _service.ListarActivosAsync(filtro);
            if (!turno.EsLaUltima) return;
            var rol = RolActualODefault;

            Activos.Clear();
            foreach (var doc in documentos)
                Activos.Add(new DocumentoFila(doc, rol));
        }, MensajeSinPermisoDocumentos, turno);
    }

    /// <summary>
    /// Recarga el historial SIEMPRE que se invoca. El servidor exige año (D9): sin año, o con un texto inválido en la caja
    /// (<see cref="ViewModelBase.HayErroresDeEntrada"/>), no consulta.
    /// </summary>
    public async Task CargarHistorialAsync()
    {
        if (FiltroHistorialAnio is null || HayErroresDeEntrada)
            return;

        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var filtro = new FiltroDocumentos(FiltroHistorialTipo, FiltroHistorialAnio, FiltroHistorialTexto, FiltroHistorialEstado);
            var documentos = await _service.ListarHistorialAsync(filtro);
            if (!turno.EsLaUltima) return;
            var rol = RolActualODefault;

            Historial.Clear();
            foreach (var doc in documentos)
                Historial.Add(new DocumentoFila(doc, rol));

        }, MensajeSinPermisoDocumentos, turno);
    }

    [RelayCommand]
    private void Nuevo() => _navigation.Navegar<DocumentoFormViewModel>(vm => vm.CargarParaCrear());

    [RelayCommand]
    private void VerDetalle(DocumentoFila fila)
        => _navigation.Navegar<DocumentoFormViewModel>(vm => _ = vm.CargarParaVerAsync(fila.Documento));

    [RelayCommand]
    private async Task Iniciar(DocumentoFila fila)
    {
        try { await _service.IniciarProcesoAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task VolverAPendiente(DocumentoFila fila)
    {
        try { await _service.VolverAPendienteAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task Finalizar(DocumentoFila fila)
    {
        // Cada cambio de vista consulta de nuevo: el documento recién cerrado aparece en Historial al elegirlo.
        try { await _service.FinalizarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    /// <summary>
    /// Único punto de traducción excepción → mensaje para todos los comandos de esta pantalla
    /// (molde: TareaListViewModel.ManejarErrorAsync). A diferencia de Tareas,
    /// UnauthorizedAccessException se atrapa en SILENCIO (spec "Manejo de errores"): el
    /// manejador central del 403 (AuthTokenHandler + AccesoRevocado, App.axaml.cs) ya muestra
    /// el aviso y refresca permisos -- informarlo también acá vuelve el doble aviso que se
    /// corrigió en el commit 093fc7c.
    /// </summary>
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
}
