using System;
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

        ItemsView = new DataGridCollectionView(Items);
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
