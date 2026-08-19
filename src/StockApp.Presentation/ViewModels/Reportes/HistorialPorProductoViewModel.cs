using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// ViewModel del reporte de Historial por Producto (Inc 6). Consulta el historial
/// de movimientos de un producto filtrado por rango de fechas vía
/// <see cref="IReporteStockService"/> y permite exportarlo a CSV.
/// </summary>
public partial class HistorialPorProductoViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para la exportación CSV. Coincide con las propiedades
    /// de <see cref="MovimientoHistorialDto"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> ColumnOrder = new[]
    {
        "MovimientoId",
        "ProductoId",
        "ProductoNombre",
        "Tipo",
        "Motivo",
        "Cantidad",
        "PrecioUnitario",
        "StockAnterior",
        "StockNuevo",
        "Comentario",
        "Fecha",
        "UsuarioId",
    };

    private readonly IReporteStockService _servicio;
    private readonly ICsvExporter _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;
    private readonly IProductoService _productoService;

    /// <summary>
    /// PK del producto filtrado. Antes se tipeaba a mano en un NumericUpDown -- el ÚNICO
    /// filtro de esta pantalla, y un ID que no se muestra en ninguna vista de la app (bugfix
    /// 2026-08-19). Ahora se deriva de <see cref="ProductoSeleccionado"/> (AutoCompleteBox con
    /// búsqueda server-side), pero se conserva como ObservableProperty propio porque
    /// CargarAsync/ExportarAsync ya lo usan como fuente de verdad y los tests existentes lo
    /// asignan directo.
    /// </summary>
    [ObservableProperty]
    private int _productoId;

    /// <summary>
    /// Producto elegido en el AutoCompleteBox (bugfix 2026-08-19): "historial de UN producto"
    /// no admite "todos", así que no hay opción "Todos" acá (a diferencia de
    /// AuditoriaLogViewModel.UsuarioFiltroSeleccionado).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuscarCommand))]
    private ProductoDto? _productoSeleccionado;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private IReadOnlyList<MovimientoHistorialDto> _items = new List<MovimientoHistorialDto>();

    [ObservableProperty]
    private string? _mensajeError;

    public HistorialPorProductoViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IProductoService productoService)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _productoService = productoService;

        BuscarProductosAsync = BuscarProductosInternalAsync;
    }

    partial void OnProductoSeleccionadoChanged(ProductoDto? value)
        => ProductoId = value?.Id ?? 0;

    /// <summary>
    /// Delegado para <c>AutoCompleteBox.AsyncPopulator</c> del filtro de producto (bugfix
    /// 2026-08-19): búsqueda SERVER-SIDE vía IProductoService.BuscarPorTextoAsync (ILIKE sobre
    /// Codigo/CodigoBarras/Nombre). El catálogo se estima en 100-1000 productos en producción,
    /// así que NO se precarga completo — el propio AutoCompleteBox ya trae debounce
    /// (MinimumPopulateDelay) y cancela búsquedas obsoletas vía el CancellationToken.
    /// </summary>
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> BuscarProductosAsync { get; }

    private async Task<IEnumerable<object>> BuscarProductosInternalAsync(string? texto, CancellationToken ct)
    {
        var resultados = await _productoService.BuscarPorTextoAsync(texto);
        return resultados;
    }

    /// <summary>
    /// Gatea el botón "Buscar" en la UI (bugfix 2026-08-19): sin producto seleccionado, la
    /// pantalla (cuyo ÚNICO filtro es el producto) no tiene nada sensato que consultar. Mismo
    /// criterio que MovimientoHistorialViewModel.PuedeEjecutarRecalcular.
    /// </summary>
    private bool PuedeBuscar() => ProductoSeleccionado is not null;

    /// <summary>Consulta el historial del producto filtrado y puebla <see cref="Items"/>.</summary>
    [RelayCommand(CanExecute = nameof(PuedeBuscar))]
    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Consulta el historial del producto filtrado y puebla <see cref="Items"/>. Público para
    /// poder engancharse desde el auto-load de la vista (<c>DataContextChanged</c> en
    /// <c>HistorialPorProductoView.axaml.cs</c>), además de desde <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        if (FechaDesde is not null && FechaHasta is not null && FechaDesde > FechaHasta)
        {
            MensajeError = "La fecha 'Desde' no puede ser posterior a 'Hasta'.";
            return;
        }

        MensajeError = null;

        try
        {
            Items = await _servicio.ObtenerHistorialPorProductoAsync(
                ProductoId, ALocalAUtc(FechaDesde), ALocalAUtc(FechaHasta));
        }
        catch (UnauthorizedAccessException)
        {
            // Red de contención (bugfix 2026-08-16, auditoría): CargarAsync la dispara la View
            // (DataContextChanged) fire-and-forget -- un 403 sin atrapar acá escala a
            // Dispatcher.UIThread.UnhandledException (App.axaml.cs), que muestra el genérico
            // "Ocurrió un error inesperado" duplicando el aviso "Tus permisos cambiaron..." que
            // ya dispara AuthTokenHandler al ver el 403. Mismo criterio que
            // GastosViewModel.CargarAsync (Finanzas): catch silencioso, sin volver a informar.
        }
    }

    /// <summary>
    /// Convierte una fecha LOCAL (la que produce el <c>CalendarDatePicker</c> bindeado a
    /// FechaDesde/FechaHasta, ver XAML) a UTC antes de pasarla al servicio. El repositorio
    /// subyacente (MovimientoStockRepository) compara contra <c>MovimientoStock.Fecha</c>,
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
            await _guardado.GuardarTextoAsync(csv, "historial-producto.csv");
        }, _confirmacion);
    }
}
