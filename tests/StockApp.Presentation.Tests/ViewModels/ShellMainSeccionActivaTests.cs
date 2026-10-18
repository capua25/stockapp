using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Movimientos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// La sección activa del sidebar sigue a la navegación real (INavigationService.Cambiado), no solo a los
/// comandos del sidebar: desde Inicio (accesos, filas de tarea, "Ver todas", calendario) y al loguear.
/// </summary>
public class ShellMainSeccionActivaTests
{
    /// <summary>Navegación real con VMs sin construir: el shell solo mira el tipo del contenido.</summary>
    private static NavigationService NavegacionReal() =>
        new(tipo => RuntimeHelpers.GetUninitializedObject(tipo));

    private static ShellMainViewModel Crear(INavigationService navegacion, RolUsuario rol, params string[] permisos)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));
        return new ShellMainViewModel(sesion.Object, navegacion, Mock.Of<IInfoApp>(i => i.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());
    }

    [Fact]
    public void AlLoguear_InicioQuedaActivo()
    {
        var navegacion = NavegacionReal();
        var vm = Crear(navegacion, RolUsuario.Admin);
        Assert.Null(vm.SeccionActiva); // precondición: nadie tocó el sidebar

        // Lo que hace ShellViewModel tras el login: Navegar<InicioViewModel>().
        navegacion.Navegar<InicioViewModel>();

        Assert.Equal("Inicio", vm.SeccionActiva);
        Assert.True(vm.ItemInicio.EstaActivo);
    }

    public static TheoryData<Type, string> DestinosDeInicio => new()
    {
        { typeof(ProductoListViewModel), "Productos" },
        { typeof(EntradaRegistroViewModel), "RegistrarEntrada" },
        { typeof(SalidaRegistroViewModel), "RegistrarSalida" },
        { typeof(MovimientoHistorialViewModel), "HistorialMovimientos" },
        { typeof(ReportesViewModel), "Reportes" },
        { typeof(ValorizacionViewModel), "Reportes" },
        { typeof(StockCategoriaViewModel), "Reportes" },
        { typeof(HistorialPorProductoViewModel), "Reportes" },
        { typeof(MasMovidosViewModel), "Reportes" },
        { typeof(AuditoriaLogViewModel), "Reportes" },
        { typeof(ReporteTareasViewModel), "Reportes" },
        { typeof(CalendarioPagosViewModel), "CalendarioPagos" },
        { typeof(TareaListViewModel), "Tareas" },
        { typeof(TareaFormViewModel), "Tareas" },
    };

    [Theory]
    [MemberData(nameof(DestinosDeInicio))]
    public void NavegarDesdeInicio_MarcaLaSeccionDelDestino(Type destino, string seccion)
    {
        var navegacion = new Mock<INavigationService>();
        var vm = Crear(navegacion.Object, RolUsuario.Admin);
        vm.NavInicioCommand.Execute(null);
        Assert.Equal("Inicio", vm.SeccionActiva);

        // Lo que hacen los comandos de InicioViewModel: Navegar<Destino>() sin pasar por el sidebar.
        navegacion.SetupGet(n => n.Actual).Returns((ViewModelBase)RuntimeHelpers.GetUninitializedObject(destino));
        navegacion.Raise(n => n.Cambiado += null);

        Assert.Equal(seccion, vm.SeccionActiva);
        Assert.False(vm.ItemInicio.EstaActivo);
        var item = Assert.Single(vm.Grupos.SelectMany(g => g.Items), i => i.EstaActivo);
        Assert.Equal(seccion, item.Seccion);
    }

    /// <summary>
    /// Navegación real con las pantallas de origen construidas de verdad (sus comandos navegan con este mismo
    /// servicio); el resto, sin construir.
    /// </summary>
    private static NavigationService NavegacionConOrigenesReales(Mock<IMovimientoStockService> movimientos)
    {
        NavigationService? navegacion = null;
        navegacion = new NavigationService(tipo =>
        {
            if (tipo == typeof(EntradaRegistroViewModel))
                return new EntradaRegistroViewModel(movimientos.Object, Mock.Of<IProductoService>(), navegacion!, Mock.Of<IConfirmacionService>(), Mock.Of<ICurrentSession>());
            if (tipo == typeof(SalidaRegistroViewModel))
                return new SalidaRegistroViewModel(movimientos.Object, Mock.Of<IProductoService>(), navegacion!, Mock.Of<IConfirmacionService>());
            if (tipo == typeof(ControlPoaViewModel))
            {
                var vistas = new Mock<IFinanzasVistasService>();
                vistas.Setup(v => v.ObtenerControlPoaAsync(It.IsAny<int>()))
                    .ReturnsAsync(new List<ControlPoaLineaDto> { new(1, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false) });
                return new ControlPoaViewModel(vistas.Object, navegacion!, Mock.Of<IServicioGuardadoArchivo>(),
                    Mock.Of<IConfirmacionService>(), Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>(), Mock.Of<ICurrentSession>());
            }
            if (tipo == typeof(GastosViewModel))
                return new GastosViewModel(Mock.Of<IGastoService>(), Mock.Of<ICurrentSession>(), Mock.Of<IProveedorService>(),
                    Mock.Of<IFuenteFinanciamientoService>(), Mock.Of<IRubroGastoService>(), Mock.Of<ILineaPoaService>(), navegacion!,
                    Mock.Of<IConfirmacionService>(), Mock.Of<IServicioGuardadoArchivo>(),
                    Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>());
            return RuntimeHelpers.GetUninitializedObject(tipo);
        });
        return navegacion;
    }

    private static ProductoDto Producto() => new(
        Id: 1, Codigo: "SKU1", CodigoBarras: null, Nombre: "Azúcar", Descripcion: null, CategoriaId: null, CategoriaNombre: null,
        ProveedorId: null, UnidadMedidaId: 1, UnidadMedidaNombre: "Unidad", PrecioCosto: 0m, StockActual: 10m, StockMinimo: 0m,
        Activo: true, FechaAlta: default);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistrarEntradaOSalida_AlGuardarVaAlHistorial_MarcaHistorial(bool entrada)
    {
        // Decisión del usuario 2026-10-06: el sidebar marca la pantalla mostrada por cualquier camino. Al
        // registrar, MovimientoRegistroViewModelBase navega al historial.
        var movimientos = new Mock<IMovimientoStockService>();
        movimientos.Setup(m => m.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), false))
            .ReturnsAsync((RegistrarMovimientoDto d, bool _) =>
                new MovimientoRegistradoDto(1, d.ProductoId, d.Tipo, d.Motivo, d.Cantidad, 0m, 10m, 15m, DateTime.UtcNow));
        var navegacion = NavegacionConOrigenesReales(movimientos);
        var vm = Crear(navegacion, RolUsuario.Admin);
        (entrada ? vm.NavRegistrarEntradaCommand : vm.NavRegistrarSalidaCommand).Execute(null);
        Assert.Equal(entrada ? "RegistrarEntrada" : "RegistrarSalida", vm.SeccionActiva);
        var registro = Assert.IsAssignableFrom<MovimientoRegistroViewModelBase>(navegacion.Actual);
        if (entrada)
            registro.Motivo = MotivoMovimiento.Ajuste; // sin el paso opcional "Asociar factura" de las compras
        registro.ProductoSeleccionado = Producto();
        registro.Cantidad = 5m;

        await registro.RegistrarCommand.ExecuteAsync(null);

        Assert.Null(registro.MensajeError);
        Assert.IsType<MovimientoHistorialViewModel>(navegacion.Actual);
        Assert.Equal("HistorialMovimientos", vm.SeccionActiva);
    }

    [Fact]
    public async Task ControlPoa_AbrirLosGastosDeUnaLinea_MarcaGastos()
    {
        // ControlPoaViewModel abre Gastos filtrado por la línea seleccionada.
        var navegacion = NavegacionConOrigenesReales(new Mock<IMovimientoStockService>());
        var vm = Crear(navegacion, RolUsuario.Admin);
        vm.NavControlPoaCommand.Execute(null);
        Assert.Equal("ControlPoa", vm.SeccionActiva);
        var controlPoa = Assert.IsType<ControlPoaViewModel>(navegacion.Actual);
        await controlPoa.CargarAsync();
        controlPoa.FilaSeleccionada = controlPoa.Filas[0];

        controlPoa.AbrirGastosDeLaLineaCommand.Execute(null);

        Assert.Equal(1, Assert.IsType<GastosViewModel>(navegacion.Actual).LineaPoaSeleccionada?.Id);
        Assert.Equal("Gastos", vm.SeccionActiva);
    }

    [Fact]
    public void NavegarAUnaPantallaSinItemPropio_ConservaLaSeccion()
    {
        // Un formulario abierto desde un listado (Productos → ProductoForm) sigue marcando el listado.
        var navegacion = NavegacionReal();
        var vm = Crear(navegacion, RolUsuario.Admin);
        navegacion.Navegar<ProductoListViewModel>();

        navegacion.Navegar<ProductoFormViewModel>();

        Assert.Equal("Productos", vm.SeccionActiva);
    }

    [Fact]
    public void OperadorMixto_NavegarAUnaSeccionSinPermiso_NoRompeYNoMarcaInicio()
    {
        // Gate: el ítem de Valorización está oculto para este operador. La sección se marca igual (el ítem
        // invisible queda activo, sin efecto en pantalla) y ningún ítem visible queda marcado por error.
        var navegacion = NavegacionReal();
        var vm = Crear(navegacion, RolUsuario.Operador, Permisos.GestionarProductos, Permisos.GestionarTareas);
        navegacion.Navegar<InicioViewModel>();

        navegacion.Navegar<ValorizacionViewModel>();

        Assert.Equal("Reportes", vm.SeccionActiva);
        Assert.False(vm.ItemInicio.EstaActivo);
        Assert.DoesNotContain(vm.Grupos.SelectMany(g => g.Items), i => i.EsVisible && i.EstaActivo);
    }

    [Fact]
    public void CadaItemDelSidebar_SuPantallaMarcaSuSeccion()
    {
        // La tabla pantalla → sección cubre todos los ítems: llegar a la pantalla de cada ítem por fuera del
        // sidebar (como desde Inicio) lo marca a él. El destino de cada ítem se toma de su propio comando.
        var navegacion = new Mock<INavigationService>();
        var vm = Crear(navegacion.Object, RolUsuario.Admin);
        var items = vm.Grupos.SelectMany(g => g.Items).Append(vm.ItemInicio)
            // Importar planillas está deshabilitado (ImportacionHabilitada): su comando no navega.
            .Where(i => i.Seccion != "Importacion" || ShellMainViewModel.ImportacionHabilitada)
            .ToList();
        // Con el hub de Reportes y Catálogos consolidados el sidebar de Admin tiene 17 ítems (Inicio + 16).
        Assert.True(items.Count >= 17);

        foreach (var item in items)
        {
            Type? destino = null;
            var otra = Crear(new NavegacionQueAnotaElTipo(t => destino = t), RolUsuario.Admin);
            otra.Grupos.SelectMany(g => g.Items).Append(otra.ItemInicio).Single(i => i.Seccion == item.Seccion).Comando.Execute(null);
            Assert.NotNull(destino);

            vm.SeccionActiva = null;
            navegacion.SetupGet(n => n.Actual).Returns((ViewModelBase)RuntimeHelpers.GetUninitializedObject(destino));
            navegacion.Raise(n => n.Cambiado += null);

            Assert.True(item.Seccion == vm.SeccionActiva, $"{destino.Name} marca {vm.SeccionActiva ?? "nada"}, no {item.Seccion}");
        }
    }

    private sealed class NavegacionQueAnotaElTipo(Action<Type> anotar) : INavigationService
    {
        public ViewModelBase? Actual => null;
        public event Action? Cambiado { add { } remove { } }
        public void Navegar<TVm>() where TVm : ViewModelBase => anotar(typeof(TVm));
        public void Navegar<TVm>(Action<TVm> inicializar) where TVm : ViewModelBase => anotar(typeof(TVm));
    }
}
