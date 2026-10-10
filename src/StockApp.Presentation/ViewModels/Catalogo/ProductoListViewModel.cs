using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de productos con búsqueda por nombre, SKU o código de barras.
/// </summary>
public partial class ProductoListViewModel : ViewModelBase
{
    /// <summary>Espera de debounce antes de disparar la búsqueda automática mientras se escribe.</summary>
    private static readonly TimeSpan DebouncePorDefecto = TimeSpan.FromMilliseconds(300);

    /// <summary>Espera real del debounce; los tests que no miden el tiempo la bajan a cero para no depender del reloj.</summary>
    internal TimeSpan EsperaDebounce { get; set; } = DebouncePorDefecto;

    /// <summary>Texto del estado "Sin permiso" del listado cuando el servidor rechaza la carga o la búsqueda (403).</summary>
    public const string MensajeSinPermisoListado = "No tenés permiso para ver los productos.";

    private readonly IProductoService     _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;
    private readonly ICurrentSession      _session;

    private CancellationTokenSource? _debounceCts;
    private readonly ConsultaVigente _consultas = new();

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar en la card (ver <see cref="ViewModelBase.SinConexion"/>).</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor el listado queda vacío: el reintento no puede mostrar filas de la consulta anterior.</summary>
    protected override void AlPerderConexion() => Items.Clear();

    /// <summary>
    /// Tarea del debounce disparada por cada cambio de <see cref="FiltroBusqueda"/>. Expuesta
    /// como internal para que los tests puedan awaitarla de forma determinista, igual que el
    /// patrón usado en <see cref="ShellViewModel"/> para tareas de background.
    /// </summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    [ObservableProperty]
    private string? _filtroBusqueda;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    private ProductoDto? _itemSeleccionado;

    /// <summary>
    /// Gate de Nuevo / Editar / Eliminar (bug B3, 2026-10-09): mismo criterio que el sidebar y el
    /// servidor (Admin siempre; Operador con <see cref="Permisos.GestionarProductos"/>), con
    /// IsVisible en la vista como el resto de las pantallas. Es un getter sobre ICurrentSession,
    /// que no notifica: <see cref="ViewModelBase.NotificarPermisosActualizados"/> (la base notifica
    /// todo) lo re-evalúa cuando un 403 refresca los permisos con la pantalla abierta.
    /// </summary>
    public bool PuedeGestionarProductos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarProductos);

    public ObservableCollection<ProductoDto> Items { get; } = new();

    /// <summary>
    /// Vista sobre <see cref="Items"/> que habilita el ordenamiento por click en encabezados
    /// del DataGrid. Necesaria por una regresión de Avalonia 12 (AvaloniaUI/Avalonia#21129):
    /// bindear el DataGrid directo a una ObservableCollection con CanUserSortColumns="True"
    /// ya no ordena. Se crea una única vez envolviendo Items, así los Clear/Add de
    /// CargarAsync/EjecutarBusquedaAsync se reflejan automáticamente vía INotifyCollectionChanged.
    /// </summary>
    public DataGridCollectionView ItemsView { get; }

    public ProductoListViewModel(
        IProductoService     service,
        INavigationService   navigation,
        IConfirmacionService confirmacion,
        ICurrentSession      session)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
        _session      = session;

        ItemsView = new DataGridCollectionView(Items);
    }

    /// <summary>
    /// La dispara la View (DataContextChanged) fire-and-forget. Va por EjecutarCargaProtegidaAsync: un 403
    /// (UnauthorizedAccessException) deja SinPermiso en true en vez de escapar al manejador global, y mientras
    /// espera al servicio Cargando está en true, así la vista no afirma "no hay productos" antes de tenerlos.
    /// El aviso global del 403 ya lo da AuthTokenHandler + App.axaml.cs: acá no se agrega otro.
    /// Pide su turno antes de consultar: si mientras espera empezó otra consulta (búsqueda, recarga), su resultado
    /// se descarta (ver ConsultaVigente).
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var resultados = await _service.BuscarAsync(null, null, null);
            if (!turno.EsLaUltima)
                return;
            Items.Clear();
            foreach (var p in resultados)
                Items.Add(p);
        }, MensajeSinPermisoListado, turno);
    }

    /// <summary>
    /// Dispara la búsqueda automática con debounce cada vez que cambia el filtro, para no
    /// golpear el servicio en cada tecla. Cancela la búsqueda en curso (si la hay) antes de
    /// programar la nueva espera.
    /// </summary>
    partial void OnFiltroBusquedaChanged(string? value)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        _tareaDebounce = DebounceBuscarAsync(_debounceCts.Token);
    }

    private async Task DebounceBuscarAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(EsperaDebounce, ct);
        }
        catch (OperationCanceledException)
        {
            // Búsqueda obsoleta: se tipeó de nuevo antes de que venza la espera, se descarta.
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        await EjecutarBusquedaAsync();
    }

    public IAsyncRelayCommand BuscarCommand => field ??= ComandoDeGuardado(BuscarAsync);

    private async Task BuscarAsync() => await EjecutarBusquedaAsync();

    /// <summary>
    /// Búsqueda por término único: matchea por Nombre, SKU o código de barras (lógica OR). Reusada por el
    /// comando del botón y por el debounce automático. Mismo camino protegido que la carga: un 403 en una
    /// búsqueda marca SinPermiso y no escapa; Items no se vacía hasta tener los resultados.
    /// Pide su turno antes de consultar: si mientras espera empezó otra consulta (búsqueda, recarga), su resultado
    /// se descarta (ver ConsultaVigente).
    /// </summary>
    private async Task EjecutarBusquedaAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var resultados = await _service.BuscarPorTextoAsync(FiltroBusqueda);
            if (!turno.EsLaUltima)
                return;
            Items.Clear();
            foreach (var p in resultados)
                Items.Add(p);
        }, MensajeSinPermisoListado, turno);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<ProductoFormViewModel>());

    /// <summary>
    /// Compartido entre Editar y Baja: un producto inactivo (dado de baja) no admite ninguna
    /// de las dos operaciones, solo se puede operar sobre productos activos.
    /// </summary>
    private bool PuedeOperarSobreSeleccionado()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    /// <summary>
    /// Navega al formulario en modo edición, precargando el producto seleccionado vía el
    /// overload de <see cref="INavigationService.Navegar{TVm}(Action{TVm})"/>: el VM se resuelve
    /// fresco desde DI y luego se inicializa con <see cref="ProductoFormViewModel.CargarParaEditar"/>
    /// antes de publicarse como pantalla actual.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeOperarSobreSeleccionado))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;

        var producto = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<ProductoFormViewModel>(vm => vm.CargarParaEditar(producto)));
    }

    /// <summary>
    /// Da de baja el producto seleccionado, previa confirmación del usuario. Las excepciones
    /// de dominio esperables (ej: ya está inactivo, o fue borrado por otra sesión) NO deben
    /// propagar y matar el proceso — mismo patrón que CategoriaListViewModel/ProveedorListViewModel/
    /// UnidadMedidaListViewModel tras el fix del crash de baja lógica.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeOperarSobreSeleccionado))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el producto \"{ItemSeleccionado.Nombre}\"?");
        if (!confirmar) return;

        try
        {
            await _service.BajaLogicaAsync(ItemSeleccionado.Id);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
