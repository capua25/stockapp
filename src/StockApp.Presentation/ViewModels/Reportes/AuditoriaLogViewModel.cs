using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// Opción de filtro por usuario para el AutoCompleteBox del log de auditoría (bugfix
/// 2026-08-19). Valor=null representa "Todos" (sin filtro de usuario). A diferencia del
/// filtro de producto de <see cref="Movimientos.MovimientoHistorialViewModel"/> (que se
/// migró a búsqueda server-side pura, sin wrapper "Todos" explícito porque sin selección YA
/// es "Todos"), acá el universo es chico (~14 usuarios) y se precarga completo, así que SÍ
/// hace falta un ítem "Todos" explícito para poder elegirlo desde la lista fija.
/// </summary>
public sealed record OpcionUsuario(string Nombre, UsuarioDto? Valor);

/// <summary>
/// Fila del PDF de auditoría: lo mismo que la grilla, con la acción en texto legible (<see cref="TextoAccionAuditada"/>) en vez
/// del nombre del enum. Las propiedades se llaman como las del <see cref="AuditoriaItemDto"/>: las columnas no cambian.
/// </summary>
public sealed record FilaAuditoriaPdf(DateTime Fecha, string NombreUsuario, string Accion, string Entidad, int EntidadId, string Detalle);

/// <summary>
/// ViewModel del reporte de Log de Auditoría (Inc 6). Consulta el historial de
/// acciones auditadas filtrado por usuario y rango de fechas vía
/// <see cref="IAuditoriaQueryService"/> y permite exportarlo a PDF.
/// </summary>
public partial class AuditoriaLogViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA.
    /// Caso especial de esta pantalla: <c>Detalle</c> es texto libre sin tope de
    /// largo (campo del log de auditoría) -- la plantilla lo envuelve (wrap) sin truncar.
    /// </summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(FilaAuditoriaPdf.Fecha), "Fecha"),
        new ColumnaPdf(nameof(FilaAuditoriaPdf.NombreUsuario), "Usuario"),
        new ColumnaPdf(nameof(FilaAuditoriaPdf.Accion), "Acción"),
        new ColumnaPdf(nameof(FilaAuditoriaPdf.Entidad), "Entidad"),
        new ColumnaPdf(nameof(FilaAuditoriaPdf.EntidadId), "Entidad ID"),
        new ColumnaPdf(nameof(FilaAuditoriaPdf.Detalle), "Detalle"),
    };

    private readonly IAuditoriaQueryService _servicio;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;
    private readonly IUsuarioService _usuarioService;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    /// <summary>
    /// PK del usuario filtrado. Antes se tipeaba a mano en un NumericUpDown -- un ID que no se
    /// muestra en ninguna vista de la app (bugfix 2026-08-19). Ahora se deriva de
    /// <see cref="UsuarioFiltroSeleccionado"/> (AutoCompleteBox con filtrado client-side), pero
    /// se conserva como ObservableProperty propio porque CargarAsync/ExportarAsync ya lo usan
    /// como fuente de verdad y los tests existentes lo asignan directo.
    /// </summary>
    [ObservableProperty]
    private int? _usuarioId;

    /// <summary>Opciones de usuario disponibles para el AutoCompleteBox de filtro ("Todos" +
    /// todos los usuarios, activos e inactivos -- mismo universo que UsuariosAdminViewModel).</summary>
    public ObservableCollection<OpcionUsuario> Usuarios { get; } = new();

    /// <summary>Opción de usuario seleccionada (Valor=null = "Todos").</summary>
    [ObservableProperty]
    private OpcionUsuario? _usuarioFiltroSeleccionado;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private IReadOnlyList<AuditoriaItemDto> _items = new List<AuditoriaItemDto>();

    [ObservableProperty]
    private string? _mensajeError;

    private readonly INavigationService? _navegacion;
    private readonly ConsultaVigente _consultas = new();

    /// <summary>Descripción de filtros de la consulta CARGADA: el PDF rotula lo que muestra la grilla, no lo que hoy dicen los filtros.</summary>
    private string? _descripcionCargada;

    /// <summary>Vuelve al hub de Reportes (primer tramo del breadcrumb). Sin navegación no hay a dónde volver.</summary>
    public IRelayCommand VolverCommand => field ??= new RelayCommand(
        () => _navegacion?.Navegar<ReportesViewModel>(), () => _navegacion is not null);

    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>
    /// El export serializa las filas ya cargadas: sin filas, tras un 403, sin conexión o con una carga en curso
    /// (las filas pueden ser de otra consulta) no se puede exportar.
    /// </summary>
    public bool PuedeExportar => Items.Count > 0 && !SinPermiso && !SinConexion && !Cargando;

    partial void OnItemsChanged(IReadOnlyList<AuditoriaItemDto> value)
    {
        OnPropertyChanged(nameof(PuedeExportar));
        ExportarPdfCommand.NotifyCanExecuteChanged();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(SinPermiso) or nameof(SinConexion) or nameof(Cargando))
        {
            OnPropertyChanged(nameof(PuedeExportar));
            ExportarPdfCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// El filtro por usuario necesita la lista de usuarios (<c>GET /usuarios</c>, que exige <c>usuarios.gestionar</c>: un permiso
    /// estructural de Admin). Un Operador con <c>reportes.ver</c> no lo tiene: sin esto el 403 dejaba el reporte entero en "Sin permiso".
    /// </summary>
    public bool PuedeFiltrarPorUsuario =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarUsuarios);

    public string TituloVacio => "No hay registros para estos filtros";

    public string MensajeVacio => "Probá con otro usuario o período.";

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion()
    {
        Items = new List<AuditoriaItemDto>();
        _descripcionCargada = null;
    }

    /// <summary>
    /// Reintentar repite lo que falló con los filtros de pantalla: si todavía no hay lista de usuarios (la carga que
    /// falló fue la inicial) vuelve a inicializar; si no, solo consulta el log.
    /// </summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => Usuarios.Count == 0 ? InicializarAsync() : CargarAsync();

    public AuditoriaLogViewModel(
        IAuditoriaQueryService servicio,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IUsuarioService usuarioService,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session,
        INavigationService? navegacion = null)
    {
        _servicio = servicio;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _usuarioService = usuarioService;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;
        _navegacion = navegacion;
    }

    partial void OnUsuarioFiltroSeleccionadoChanged(OpcionUsuario? value)
        => UsuarioId = value?.Valor?.Id;

    /// <summary>
    /// Inicialización de la vista (bugfix 2026-08-19): carga TODOS los usuarios (activos e
    /// inactivos, igual que UsuariosAdminViewModel.CargarAsync) para el AutoCompleteBox del
    /// filtro y el log completo. Se invoca una sola vez al mostrar la vista (no hay hook de
    /// navegación que lo dispare, ver code-behind) -- mismo patrón que
    /// MovimientoHistorialViewModel.InicializarAsync. A diferencia del filtro de Producto
    /// (server-side, cientos de filas), acá son ~14 usuarios que no crecen como el catálogo:
    /// se precarga todo y se usa el filtrado nativo del AutoCompleteBox (FilterMode/ItemFilter,
    /// ver XAML), sin backend nuevo.
    /// </summary>
    public async Task InicializarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            // Sin usuarios.gestionar no hay lista: "Todos" solo y el combo queda oculto (PuedeFiltrarPorUsuario).
            var usuarios = PuedeFiltrarPorUsuario ? await _usuarioService.ListarAsync() : Array.Empty<UsuarioDto>();
            if (!turno.EsLaUltima)
                return;
            Usuarios.Clear();
            Usuarios.Add(new OpcionUsuario("Todos", null));
            foreach (var u in usuarios)
                Usuarios.Add(new OpcionUsuario(u.NombreUsuario, u));

            UsuarioFiltroSeleccionado = Usuarios[0];

            await ConsultarLogAsync(turno);
        }, MensajeSinPermisoAuditoria, turno);
    }

    private const string MensajeSinPermisoAuditoria = "No tenés permiso para ver el registro de auditoría.";

    /// <summary>Consulta el log de auditoría filtrado y puebla <see cref="Items"/>.</summary>
    public IAsyncRelayCommand BuscarCommand => field ??= ComandoDeGuardado(BuscarAsync);

    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Consulta el log de auditoría filtrado y puebla <see cref="Items"/>. Público para poder
    /// engancharse desde el auto-load de la vista (<c>DataContextChanged</c> en
    /// <c>AuditoriaLogView.axaml.cs</c>), además de desde <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        if (FechaDesde is not null && FechaHasta is not null && FechaDesde > FechaHasta)
        {
            MensajeError = "La fecha 'Desde' no puede ser posterior a 'Hasta'.";
            return;
        }

        MensajeError = null;
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(() => ConsultarLogAsync(turno), MensajeSinPermisoAuditoria, turno);
    }

    /// <summary>Consulta el log con los filtros de pantalla de este momento y aplica el resultado solo si sigue siendo la última consulta.</summary>
    private async Task ConsultarLogAsync(ConsultaVigente.Turno turno)
    {
        var usuarioId = UsuarioId;
        var desde = ALocalAUtc(FechaDesde);
        var hasta = ALocalAUtc(FechaHasta);
        var descripcion = ConstruirDescripcionFiltros();
        var items = await _servicio.ObtenerLogAsync(usuarioId, desde, hasta);
        if (!turno.EsLaUltima)
            return;
        Items = items;
        _descripcionCargada = descripcion;
    }

    /// <summary>
    /// Convierte una fecha LOCAL (la que produce el <c>CalendarDatePicker</c> bindeado a
    /// FechaDesde/FechaHasta, ver XAML) a UTC antes de pasarla al servicio. El repositorio
    /// subyacente (AuditoriaQueryRepository) compara contra <c>LogAuditoria.Fecha</c>,
    /// persistida en UTC — sin esta conversión, con UTC-3 el rango queda desalineado (bug de
    /// huso horario). Contrato: el servicio siempre recibe fechas en UTC.
    /// </summary>
    private static DateTime? ALocalAUtc(DateTime? fechaLocal)
        => fechaLocal.HasValue
            ? DateTime.SpecifyKind(fechaLocal.Value, DateTimeKind.Local).ToUniversalTime()
            : null;

    /// <summary>
    /// Describe en criollo los filtros activos de la búsqueda, para el membrete del PDF (spec
    /// 2026-09-18). Un PDF que se archiva sin aclarar su universo de datos no es auditable: por
    /// eso, aun sin filtros, el texto lo dice explícitamente ("Todos"/"Todo el histórico") en vez
    /// de omitirlos.
    /// </summary>
    private string ConstruirDescripcionFiltros()
    {
        var usuario = UsuarioFiltroSeleccionado?.Valor is null ? "Todos" : UsuarioFiltroSeleccionado.Nombre;
        var periodo = FechaDesde is null && FechaHasta is null
            ? "Todo el histórico"
            : $"{FechaDesde?.ToString("dd/MM/yyyy") ?? "(sin desde)"} a {FechaHasta?.ToString("dd/MM/yyyy") ?? "(sin hasta)"}";
        return $"Usuario: {usuario}. Período: {periodo}.";
    }

    /// <summary>
    /// Exporta <see cref="Items"/> a PDF con las columnas de la grilla, membrete institucional
    /// y aviso de volumen si supera 500 filas (spec 2026-09-18). No hace nada si no hay datos
    /// cargados. Ofrece abrir el PDF con el visor del sistema tras guardarlo.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeExportar))]
    private async Task ExportarPdfAsync()
    {
        if (Items.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Items.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Log de auditoría",
                DescripcionFiltros: _descripcionCargada ?? ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var filas = Items
                .Select(i => new FilaAuditoriaPdf(i.Fecha, i.NombreUsuario, TextoAccionAuditada.De(i.Accion), i.Entidad, i.EntidadId, i.Detalle))
                .ToList();
            var pdf = _pdfExporter.Exportar(filas, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "auditoria.pdf", extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, "auditoria.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
}
