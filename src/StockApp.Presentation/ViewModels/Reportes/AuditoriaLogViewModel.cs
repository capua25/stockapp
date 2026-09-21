using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
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
/// ViewModel del reporte de Log de Auditoría (Inc 6). Consulta el historial de
/// acciones auditadas filtrado por usuario y rango de fechas vía
/// <see cref="IAuditoriaQueryService"/> y permite exportarlo a CSV.
/// </summary>
public partial class AuditoriaLogViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para la exportación CSV. Coincide con las propiedades
    /// de <see cref="AuditoriaItemDto"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> ColumnOrder = new[]
    {
        "Fecha",
        "NombreUsuario",
        "Accion",
        "Entidad",
        "EntidadId",
        "Detalle",
    };

    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA, no las
    /// del CSV. En esta pantalla coinciden con <see cref="ColumnOrder"/> (mismas 6 columnas),
    /// pero se mantiene como constante separada a propósito: un cambio futuro en el CSV no debe
    /// alterar el PDF. Caso especial de esta pantalla: <c>Detalle</c> es texto libre sin tope de
    /// largo (campo del log de auditoría) -- la plantilla lo envuelve (wrap) sin truncar.
    /// </summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(AuditoriaItemDto.Fecha), "Fecha"),
        new ColumnaPdf(nameof(AuditoriaItemDto.NombreUsuario), "Usuario"),
        new ColumnaPdf(nameof(AuditoriaItemDto.Accion), "Acción"),
        new ColumnaPdf(nameof(AuditoriaItemDto.Entidad), "Entidad"),
        new ColumnaPdf(nameof(AuditoriaItemDto.EntidadId), "Entidad ID"),
        new ColumnaPdf(nameof(AuditoriaItemDto.Detalle), "Detalle"),
    };

    private readonly IAuditoriaQueryService _servicio;
    private readonly ICsvExporter _csvExporter;
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

    public AuditoriaLogViewModel(
        IAuditoriaQueryService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IUsuarioService usuarioService,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _usuarioService = usuarioService;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;
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
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var usuarios = await _usuarioService.ListarAsync();
            Usuarios.Clear();
            Usuarios.Add(new OpcionUsuario("Todos", null));
            foreach (var u in usuarios)
                Usuarios.Add(new OpcionUsuario(u.NombreUsuario, u));

            UsuarioFiltroSeleccionado = Usuarios[0];

            await CargarAsync();
        }, "No tenés permiso para ver el registro de auditoría.");
    }

    /// <summary>Consulta el log de auditoría filtrado y puebla <see cref="Items"/>.</summary>
    [RelayCommand]
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
        Items = await _servicio.ObtenerLogAsync(UsuarioId, ALocalAUtc(FechaDesde), ALocalAUtc(FechaHasta));
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
    /// Exporta <see cref="Items"/> a CSV con el orden de columnas fijo y delega el guardado.
    /// No hace nada si no hay datos cargados. El guardado a disco corre bajo
    /// <see cref="ExportacionCsv"/> (bugfix 2026-08-14): un fallo DESPUÉS de elegir la ubicación
    /// (permiso denegado, disco lleno) se informa en vez de escapar del comando sin observar.
    /// </summary>
    [RelayCommand]
    private async Task ExportarAsync()
    {
        if (Items.Count == 0)
            return;

        await ExportacionCsv.EjecutarAsync(async () =>
        {
            var csv = _csvExporter.Exportar(Items, ColumnOrder);
            await _guardado.GuardarTextoAsync(csv, "auditoria.csv");
        }, _confirmacion);
    }

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
                Titulo: "Log de auditoría",
                DescripcionFiltros: ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "auditoria.pdf", extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, "auditoria.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
}
