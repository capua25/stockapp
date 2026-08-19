using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// Opción de filtro por usuario para el AutoCompleteBox del log de auditoría (bugfix
/// 2026-08-19). Valor=null representa "Todos" (sin filtro de usuario) — mismo patrón que
/// <c>OpcionProducto</c> en <see cref="Movimientos.MovimientoHistorialViewModel"/>.
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

    private readonly IAuditoriaQueryService _servicio;
    private readonly ICsvExporter _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;
    private readonly IUsuarioService _usuarioService;

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
        IUsuarioService usuarioService)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _usuarioService = usuarioService;
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
        var usuarios = await _usuarioService.ListarAsync();
        Usuarios.Clear();
        Usuarios.Add(new OpcionUsuario("Todos", null));
        foreach (var u in usuarios)
            Usuarios.Add(new OpcionUsuario(u.NombreUsuario, u));

        UsuarioFiltroSeleccionado = Usuarios[0];

        await CargarAsync();
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
}
