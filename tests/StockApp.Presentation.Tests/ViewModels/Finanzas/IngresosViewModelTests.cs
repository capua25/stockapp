using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class IngresosViewModelTests
{
    private static IngresoCaja Ingreso(int id, string concepto, bool activo = true) => new()
    {
        Id = id, Fecha = DateTime.UtcNow, Concepto = concepto,
        FuenteFinanciamientoId = 2,
        FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Literal B" },
        Monto = 1000m, Activo = activo,
    };

    private static (IngresosViewModel vm,
                    Mock<IIngresoCajaService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(
            IReadOnlyList<IngresoCaja>? ingresos = null,
            RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null)
    {
        var svc = new Mock<IIngresoCajaService>();
        svc.Setup(s => s.ListarTodosAsync()).ReturnsAsync(ingresos ?? new List<IngresoCaja>());
        svc.Setup(s => s.BajaLogicaAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos ?? Enumerable.Empty<string>()));

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new IngresosViewModel(svc.Object, session.Object, nav.Object, confirm.Object);
        return (vm, svc, nav, confirm);
    }

    private static IngresoCaja IngresoDe(int id, string concepto, decimal monto, bool activo = true) => new()
    {
        Id = id, Fecha = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc), Concepto = concepto,
        FuenteFinanciamientoId = 2,
        FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Literal B" },
        Monto = monto, Activo = activo,
    };

    [Fact]
    public async Task CargarAsync_MientrasEspera_CargandoEsTrue_YAlTerminarFalse()
    {
        var (vm, svc, _, _) = Crear();
        var pendiente = new TaskCompletionSource<IReadOnlyList<IngresoCaja>>();
        svc.Setup(s => s.ListarTodosAsync()).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);

        pendiente.SetResult(new List<IngresoCaja> { IngresoDe(1, "Partida FIGM", 1000m) });
        await carga;

        Assert.False(vm.Cargando);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task CargarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, svc, _, _) = Crear();
        svc.Setup(s => s.ListarTodosAsync()).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(IngresosViewModel.MensajeSinPermisoIngresos, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ServidorCaido_TrasUnaCargaBuena_MarcaSinConexionYVaciaItems()
    {
        var (vm, svc, _, _) = Crear();
        svc.SetupSequence(s => s.ListarTodosAsync())
            .ReturnsAsync(new List<IngresoCaja> { IngresoDe(1, "Partida FIGM", 1000m) })
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();

        await vm.CargarAsync();

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Reintentar_TrasUnaCaida_VuelveACargar()
    {
        var (vm, svc, _, _) = Crear();
        svc.SetupSequence(s => s.ListarTodosAsync())
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(new List<IngresoCaja> { IngresoDe(1, "Partida FIGM", 1000m), IngresoDe(2, "Multas", 200m) });
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task ConsultaVieja_QueTerminaDespues_NoPisaLaNueva()
    {
        var (vm, svc, _, _) = Crear();
        var vieja = new TaskCompletionSource<IReadOnlyList<IngresoCaja>>();
        var nueva = new TaskCompletionSource<IReadOnlyList<IngresoCaja>>();
        svc.SetupSequence(s => s.ListarTodosAsync()).Returns(vieja.Task).Returns(nueva.Task);

        var primera = vm.CargarAsync();
        var segunda = vm.CargarAsync();
        nueva.SetResult(new List<IngresoCaja> { IngresoDe(1, "Nueva", 1m), IngresoDe(2, "Nueva 2", 2m) });
        await segunda;
        vieja.SetResult(new List<IngresoCaja> { IngresoDe(9, "Vieja", 9m) });
        await primera;

        Assert.Equal(new[] { 1, 2 }, vm.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var (vm, _, _, _) = Crear(new List<IngresoCaja>
        {
            Ingreso(1, "Partida FIGM"), Ingreso(2, "Multas"),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Partida FIGM", vm.Items[0].Concepto);
    }

    // ── bugfix 2026-08-15: red de contención — CargarAsync no debe escalar un 403 ──────────
    // Mismo criterio que GastosViewModel.CargarAsync (ec0696c): CargarAsync la dispara la View
    // (DataContextChanged) fire-and-forget — un UnauthorizedAccessException no atrapado escala
    // a Dispatcher.UIThread.UnhandledException (App.axaml.cs). El 403 ya se avisó ANTES de
    // llegar acá, así que el catch es silencioso.

    [Fact]
    public async Task CargarAsync_SiListarTodosLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svc = new Mock<IIngresoCajaService>();
        svc.Setup(s => s.ListarTodosAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();

        var vm = new IngresosViewModel(svc.Object, session.Object, nav.Object, confirm.Object);

        var excepcion = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(excepcion);
    }

    private static async Task<IngresosViewModel> CargadoConAsync(params IngresoCaja[] ingresos)
    {
        var (vm, _, _, _) = Crear(ingresos.ToList());
        await vm.CargarAsync();
        return vm;
    }

    private static List<int> Visibles(IngresosViewModel vm) => vm.ItemsView.Cast<IngresoCaja>().Select(i => i.Id).ToList();

    [Fact]
    public async Task PorDefecto_MuestraSoloActivos()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multa duplicada", 300m, activo: false));

        Assert.False(vm.MostrarInactivos);
        Assert.Equal("Activos", vm.EstadoSeleccionado);
        Assert.Equal(new[] { 1 }, Visibles(vm));
    }

    [Fact]
    public async Task EstadoTodos_MuestraTambienLosDadosDeBaja()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multa duplicada", 300m, activo: false));

        vm.EstadoSeleccionado = "Todos";

        Assert.True(vm.MostrarInactivos);
        Assert.Equal(new[] { 1, 2 }, Visibles(vm));
    }

    [Fact]
    public async Task Busqueda_PorConcepto_SinDistinguirMayusculasNiTildes()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Préstamo BROU obras", 2000m), IngresoDe(2, "Multas de tránsito", 300m));

        vm.TextoBusqueda = "  prestamo ";

        Assert.Equal(new[] { 1 }, Visibles(vm));
    }

    [Fact]
    public async Task Pie_SumaSoloLosActivosVisibles()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multas", 250m),
            IngresoDe(3, "Multa duplicada", 5000m, activo: false));

        vm.MostrarInactivos = true;

        Assert.Equal(3, vm.CantidadVisible);
        Assert.Equal(1, vm.CantidadDadosDeBaja);
        Assert.Equal(1250m, vm.TotalActivos);
        Assert.Equal("3 ingresos · 1 dado de baja · Total $ 1.250,00", vm.TextoPie);
    }

    [Fact]
    public async Task Pie_SinDadosDeBajaVisibles_NoLosMenciona()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multas", 250m),
            IngresoDe(3, "Multa duplicada", 5000m, activo: false));

        Assert.Equal("2 ingresos · Total $ 1.250,00", vm.TextoPie);
    }

    [Fact]
    public async Task Pie_ConBusqueda_SoloCuentaLoQueSeVe()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida FIGM", 1000m), IngresoDe(2, "Multas", 250m));

        vm.TextoBusqueda = "figm";

        Assert.Equal("1 ingreso · Total $ 1.000,00", vm.TextoPie);
    }

    [Fact]
    public async Task Pie_SeRecalculaAlRecargar()
    {
        var (vm, svc, _, _) = Crear(new List<IngresoCaja> { IngresoDe(1, "Partida", 1000m) });
        await vm.CargarAsync();
        svc.Setup(s => s.ListarTodosAsync()).ReturnsAsync(new List<IngresoCaja>
        {
            IngresoDe(1, "Partida", 1000m, activo: false), IngresoDe(2, "Multas", 40m),
        });

        await vm.CargarAsync();

        Assert.Equal(1, vm.CantidadVisible);
        Assert.Equal(40m, vm.TotalActivos);
    }

    [Fact]
    public async Task Pie_Notifica_CantidadVisibleYTextoPie()
    {
        var vm = await CargadoConAsync(IngresoDe(1, "Partida", 1000m), IngresoDe(2, "Multas", 250m));
        var notificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => notificadas.Add(e.PropertyName);

        vm.TextoBusqueda = "multas";

        Assert.Contains(nameof(IngresosViewModel.CantidadVisible), notificadas);
        Assert.Contains(nameof(IngresosViewModel.TextoPie), notificadas);
    }

    [Fact]
    public async Task FiltroQueOcultaLaSeleccion_LimpiaLaSeleccion()
    {
        var inactivo = IngresoDe(2, "Multa duplicada", 300m, activo: false);
        var activo = IngresoDe(1, "Partida", 1000m);
        var vm = await CargadoConAsync(activo, inactivo);
        vm.ItemSeleccionado = activo;
        Assert.True(vm.BajaCommand.CanExecute(null));

        vm.TextoBusqueda = "multa";

        Assert.Null(vm.ItemSeleccionado);
        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task FiltroQueNoOcultaLaSeleccion_LaConserva()
    {
        var activo = IngresoDe(1, "Partida", 1000m);
        var vm = await CargadoConAsync(activo, IngresoDe(2, "Multas", 40m));
        vm.ItemSeleccionado = activo;

        vm.TextoBusqueda = "part";

        Assert.Same(activo, vm.ItemSeleccionado);
    }

    // ── ItemsView: fix de ordenamiento por click en encabezados (Avalonia 12, regresión #21129) ──

    [Fact]
    public async Task ItemsView_EsOrdenable()
    {
        var (vm, _, _, _) = Crear(new List<IngresoCaja>
        {
            Ingreso(1, "Partida FIGM"), Ingreso(2, "Multas"),
        });

        await vm.CargarAsync();

        Assert.NotNull(vm.ItemsView);
        Assert.IsType<DataGridCollectionView>(vm.ItemsView);
        Assert.True(vm.ItemsView.CanSort);
    }

    [Fact]
    public async Task ItemsView_TrasCargarAsync_ReflejaLosItems()
    {
        var (vm, _, _, _) = Crear(new List<IngresoCaja>
        {
            Ingreso(1, "Partida FIGM"), Ingreso(2, "Multas"),
        });

        await vm.CargarAsync();

        Assert.Equal(vm.Items.Count, vm.ItemsView.Cast<IngresoCaja>().Count());
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, nav, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<IngresoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaEnModoEdicion()
    {
        var ingreso = Ingreso(5, "Editable");
        var (vm, _, nav, _) = Crear(new List<IngresoCaja> { ingreso });
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];

        await vm.EditarCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<IngresoFormViewModel>(
            It.IsAny<Action<IngresoFormViewModel>>()), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_PideConfirmacionConMontoFormateado()
    {
        // Bug real (verificación orgánica): el mensaje mostraba el decimal crudo
        // ("2000.7500") en vez del formato moneda es-UY que usan las grillas ("$ 2.000,75").
        var ingreso = Ingreso(1, "Para baja");
        ingreso.Monto = 2000.7500m;
        var (vm, _, _, confirm) = Crear(new List<IngresoCaja> { ingreso });
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];

        await vm.BajaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(
            s => s.Contains("$ 2.000,75") && !s.Contains("2000.7500"))), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ConConfirmacion_DaDeBajaYRecarga()
    {
        var (vm, svc, _, _) = Crear(new List<IngresoCaja> { Ingreso(1, "Para baja") });
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];

        await vm.BajaCommand.ExecuteAsync(null);

        svc.Verify(s => s.BajaLogicaAsync(1), Times.Once);
        svc.Verify(s => s.ListarTodosAsync(), Times.AtLeast(2));
    }

    [Fact]
    public async Task BajaCommand_ErrorDeRegla_SeInforma()
    {
        var (vm, svc, _, confirm) = Crear(new List<IngresoCaja> { Ingreso(1, "Ya inactivo", activo: false) });
        svc.Setup(s => s.BajaLogicaAsync(1))
            .ThrowsAsync(new ReglaDeNegocioException("Ya está dado de baja."));
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];
        // El CanExecute exige Activo: se fuerza el caso llamando con item inactivo re-seleccionado
        vm.Items[0].Activo = true;

        await vm.BajaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("Ya está dado de baja."), Times.Once);
    }

    [Fact]
    public void EditarYBaja_SinSeleccion_Deshabilitados()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    // ── PuedeRegistrarIngresos: gating de los botones "Editar" y "Dar de baja" (bugfix 2026-08-15) ──
    // Ambos botones no tenían NINGÚN gating por permiso. Basta VerFinanzas para entrar a la
    // pantalla, pero IngresoCajaService.ModificarAsync/BajaLogicaAsync exigen
    // Permisos.RegistrarIngresos sin condición.

    [Fact]
    public void Operador_ConVerFinanzasSinRegistrarIngresos_PuedeRegistrarIngresos_EsFalse()
    {
        var (vm, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarIngresos);
    }

    [Fact]
    public void Operador_ConRegistrarIngresos_PuedeRegistrarIngresos_EsTrue()
    {
        var (vm, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas, Permisos.RegistrarIngresos });

        Assert.True(vm.PuedeRegistrarIngresos);
    }

    [Fact]
    public void Admin_PuedeRegistrarIngresos_EsTrue()
    {
        var (vm, _, _, _) = Crear(rol: RolUsuario.Admin, permisos: Array.Empty<string>());

        Assert.True(vm.PuedeRegistrarIngresos);
    }
}
