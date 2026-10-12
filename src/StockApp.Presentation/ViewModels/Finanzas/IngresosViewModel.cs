using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Ingresos de caja" (spec §7.2): ABM simple de partidas, multas, préstamos.
/// Alta/edición navegan al formulario; baja lógica con confirmación.
/// </summary>
public partial class IngresosViewModel : ViewModelBase
{
    private readonly IIngresoCajaService  _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private IngresoCaja? _itemSeleccionado;

    public ObservableCollection<IngresoCaja> Items { get; } = new();

    /// <summary>
    /// Vista sobre <see cref="Items"/> que habilita el ordenamiento por click en encabezados
    /// del DataGrid. Necesaria por una regresión de Avalonia 12 (AvaloniaUI/Avalonia#21129):
    /// bindear el DataGrid directo a una ObservableCollection con CanUserSortColumns="True"
    /// ya no ordena. Se crea una única vez envolviendo Items, así los Clear/Add de
    /// CargarAsync se reflejan automáticamente vía INotifyCollectionChanged.
    /// </summary>
    public DataGridCollectionView ItemsView { get; }

    /// <summary>Opciones del combo de estado del listado: "Activos" (por defecto) o "Todos".</summary>
    public static IReadOnlyList<string> OpcionesEstado { get; } = ["Activos", "Todos"];

    /// <summary>Texto del buscador: "contiene" sobre el concepto, sin mayúsculas ni tildes. Filtra en memoria (la API
    /// devuelve el listado entero), así que no hay debounce.</summary>
    [ObservableProperty]
    private string? _textoBusqueda;

    /// <summary>False (por defecto): solo los ingresos activos. True: también los dados de baja.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoSeleccionado))]
    private bool _mostrarInactivos;

    /// <summary>La opción del combo, traducida a <see cref="MostrarInactivos"/>.</summary>
    public string EstadoSeleccionado
    {
        get => MostrarInactivos ? "Todos" : "Activos";
        set => MostrarInactivos = value == "Todos";
    }

    private int _cantidadVisible;
    private int _cantidadDadosDeBaja;
    private decimal _totalActivos;

    /// <summary>Filas que deja ver el filtro.</summary>
    public int CantidadVisible { get => _cantidadVisible; private set => SetProperty(ref _cantidadVisible, value); }

    /// <summary>De las filas visibles, cuántas están dadas de baja.</summary>
    public int CantidadDadosDeBaja { get => _cantidadDadosDeBaja; private set => SetProperty(ref _cantidadDadosDeBaja, value); }

    /// <summary>Suma de los montos de las filas visibles ACTIVAS: un ingreso dado de baja no es plata que entró.</summary>
    public decimal TotalActivos { get => _totalActivos; private set => SetProperty(ref _totalActivos, value); }

    /// <summary>Pie del listado: "8 ingresos · 2 dados de baja · Total $ 6.610.730,55"; el tramo del medio solo si hay alguno.</summary>
    public string TextoPie
    {
        get
        {
            var ingresos = $"{FormatoEsUy.Cantidad(CantidadVisible)} {(CantidadVisible == 1 ? "ingreso" : "ingresos")}";
            var bajas = CantidadDadosDeBaja == 0
                ? string.Empty
                : $" · {FormatoEsUy.Cantidad(CantidadDadosDeBaja)} {(CantidadDadosDeBaja == 1 ? "dado de baja" : "dados de baja")}";
            return $"{ingresos}{bajas} · Total {FormatoEsUy.Moneda(TotalActivos)}";
        }
    }

    /// <summary>
    /// Gatea los botones "Editar" y "Dar de baja" (bugfix 2026-08-15): antes no tenían ningún
    /// gating — basta con Permisos.VerFinanzas para entrar a esta pantalla, pero
    /// IngresoCajaService.ModificarAsync/BajaLogicaAsync exigen Permisos.RegistrarIngresos sin
    /// condición. Un Operador con VerFinanzas pero sin RegistrarIngresos veía ambos botones
    /// habilitados, editaba o pedía la baja y recién ahí se comía un 403. Se OCULTA (no se
    /// deshabilita), mismo patrón que GastosViewModel.PuedeRegistrarPagos y
    /// DocumentoFormViewModel.PuedeAnular/PuedeReabrir: IsVisible="{Binding Puede*}" en los
    /// botones de acción de pantalla del repo, nunca IsEnabled. Un 403 dispara
    /// ManejadorAccesoRevocado: la base notifica todos los bindings de la pantalla abierta
    /// (ViewModelBase.NotificarPermisosActualizados) y el gate se oculta sin navegar.
    /// IngresosViewModel se registra AddTransient y se recrea en cada navegación a la pantalla,
    /// así que ya toma el ICurrentSession vigente en ese momento.
    /// </summary>
    public bool PuedeRegistrarIngresos =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.RegistrarIngresos);

    public IngresosViewModel(
        IIngresoCajaService service,
        ICurrentSession session,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;

        ItemsView = new DataGridCollectionView(Items) { Filter = EsVisible };
        Items.CollectionChanged += (_, _) => RecalcularPie();
    }

    /// <summary>Predicado del filtro: la grilla y el pie usan el mismo, así lo que se cuenta es lo que se ve.</summary>
    internal bool EsVisible(object item) =>
        item is IngresoCaja ingreso && (MostrarInactivos || ingreso.Activo) && CoincideConcepto(ingreso.Concepto);

    private bool CoincideConcepto(string concepto)
    {
        var busqueda = TextoBusqueda?.Trim();
        return string.IsNullOrEmpty(busqueda)
            || CultureInfo.InvariantCulture.CompareInfo.IndexOf(concepto, busqueda,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    partial void OnTextoBusquedaChanged(string? value) => AplicarFiltro();

    partial void OnMostrarInactivosChanged(bool value) => AplicarFiltro();

    /// <summary>Re-filtra la grilla y el pie. Si el filtro esconde la fila seleccionada, la selección se limpia: Editar y
    /// Dar de baja no pueden actuar sobre una fila que no se ve.</summary>
    private void AplicarFiltro()
    {
        ItemsView.Refresh();
        if (ItemSeleccionado is { } seleccionado && !EsVisible(seleccionado))
            ItemSeleccionado = null;
        RecalcularPie();
    }

    private void RecalcularPie()
    {
        var visibles = Items.Where(EsVisible).ToList();
        CantidadVisible = visibles.Count;
        CantidadDadosDeBaja = visibles.Count(i => !i.Activo);
        TotalActivos = visibles.Where(i => i.Activo).Sum(i => i.Monto);
        OnPropertyChanged(nameof(TextoPie));
    }

    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403).</summary>
    public const string MensajeSinPermisoIngresos = "No tenés permiso para ver los ingresos de caja.";

    private readonly ConsultaVigente _consultas = new();

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar (ver <see cref="ViewModelBase.SinConexion"/>).</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor el listado queda vacío: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion() => Items.Clear();

    /// <summary>
    /// La dispara la View (DataContextChanged) y la recarga tras dar de baja. Va por EjecutarCargaProtegidaAsync: un 403
    /// deja SinPermiso (el aviso global ya lo dio AuthTokenHandler), un servidor caído deja SinConexion en vez del modal
    /// global, y mientras espera Cargando está en true. Pide turno: si mientras espera empezó otra carga, su resultado
    /// se descarta (ConsultaVigente).
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        try
        {
            await EjecutarCargaProtegidaAsync(async () =>
            {
                var resultados = await _service.ListarTodosAsync();
                if (!turno.EsLaUltima)
                    return;
                Items.Clear();
                foreach (var ingreso in resultados)
                    Items.Add(ingreso);
            }, MensajeSinPermisoIngresos, turno);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<IngresoFormViewModel>());

    private bool TieneSeleccionActiva()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionado = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<IngresoFormViewModel>(vm => vm.CargarParaEditar(seleccionado)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el ingreso \"{ItemSeleccionado.Concepto}\" " +
            $"({MonedaConverter.Formatear(ItemSeleccionado.Monto)})?");
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
