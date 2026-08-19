using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Red de seguridad de los gates de permiso del sidebar. Hasta esta tanda, ShellMainView.axaml
/// tenia 31 IsVisible gateados y CERO tests de UI — y es justo la vista que el refactor reescribe
/// entera.
///
/// Los 52 tests de ShellMainViewModelTests NO cubren esto: son de ViewModel. Si alguien borra un
/// IsVisible del XAML, siguen todos en verde y el boton se le muestra a quien no debe.
///
/// Localizan por identidad del Command a proposito: ShellMainView.axaml no tiene un solo x:Name,
/// y la tanda 5 colapsa los 26 bloques a un ItemsControl — cualquier nombre desapareceria ahi.
/// El RelayCommand generado es el mismo objeto antes y despues del rediseno.
/// </summary>
public class ShellMainViewGatesTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=StockApp.Presentation"
                Width="1000" Height="800">
            <vistas:ShellMainView />
        </Window>
        """;

    private static (Window Window, ShellMainViewModel Vm) Montar(RolUsuario rol, params string[] permisos)
    {
        var vm = new ShellMainViewModel(
            new SesionFake(rol, permisos),
            new NavigationServiceFake(),
            new InfoAppFake(),
            new ConfirmacionServiceFake(),
            new AuthServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    /// <summary>
    /// Identidad estable: el RelayCommand generado por [RelayCommand] es el mismo objeto en el VM
    /// y en el Button del arbol. Sobrevive al rediseno del sidebar.
    /// </summary>
    private static bool EsVisible(Window window, object comando)
    {
        var boton = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => ReferenceEquals(b.Command, comando));

        return boton is not null && ArbolVisual.EsVisibleEnArbol(boton);
    }

    // ── Caso base: sin permisos, no se ve nada salvo Inicio ─────────────────────

    [AvaloniaFact]
    public void OperadorSinPermisos_SoloVeInicio()
    {
        var (window, vm) = Montar(RolUsuario.Operador);

        Assert.True(EsVisible(window, vm.NavInicioCommand), "Inicio no tiene gate: siempre visible.");

        Assert.False(EsVisible(window, vm.NavProductosCommand));
        Assert.False(EsVisible(window, vm.NavHistorialMovimientosCommand));
        Assert.False(EsVisible(window, vm.NavGastosCommand));
        Assert.False(EsVisible(window, vm.NavCategoriasCommand));
        Assert.False(EsVisible(window, vm.NavValorizacionCommand));
        Assert.False(EsVisible(window, vm.NavMantenimientoCommand));
        Assert.False(EsVisible(window, vm.NavUsuariosCommand));
    }

    // ── Gates simples ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void OperadorConGestionarProductos_VeProductosYNadaMas()
    {
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.GestionarProductos);

        Assert.True(EsVisible(window, vm.NavProductosCommand));
        Assert.False(EsVisible(window, vm.NavGastosCommand));
        Assert.False(EsVisible(window, vm.NavCategoriasCommand));
    }

    [AvaloniaFact]
    public void OperadorConVerFinanzas_VeLosCincoDeFinanzasPeroNoMaestros()
    {
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.VerFinanzas);

        Assert.True(EsVisible(window, vm.NavGastosCommand));
        Assert.True(EsVisible(window, vm.NavIngresosCommand));
        Assert.True(EsVisible(window, vm.NavLibroCajaCommand));
        Assert.True(EsVisible(window, vm.NavControlPoaCommand));
        Assert.True(EsVisible(window, vm.NavCalendarioPagosCommand));

        // Maestros de finanzas pide su propio permiso, no alcanza con VerFinanzas.
        Assert.False(EsVisible(window, vm.NavMaestrosFinanzasCommand));
    }

    [AvaloniaFact]
    public void OperadorConGestionarTablasMaestras_VeLosTresCatalogos()
    {
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.GestionarTablasMaestras);

        Assert.True(EsVisible(window, vm.NavCategoriasCommand));
        Assert.True(EsVisible(window, vm.NavProveedoresCommand));
        Assert.True(EsVisible(window, vm.NavUnidadesMedidaCommand));
    }

    // ── Gates COMPUESTOS: aca vive el riesgo ────────────────────────────────────

    [AvaloniaFact]
    public void OperadorConSoloRegistrarMovimientos_NO_VeRegistrarEntrada()
    {
        // Bug de coherencia real, arreglado el 2026-08-16: Registrar Entrada exige
        // RegistrarMovimientos + GestionarProductos, porque el combo de producto pide
        // GestionarProductos del lado del servidor. Con un solo permiso, la pantalla se abria
        // y despues fallaba.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.RegistrarMovimientos);

        Assert.False(EsVisible(window, vm.NavRegistrarEntradaCommand));
        Assert.False(EsVisible(window, vm.NavRegistrarSalidaCommand));

        // El historial si, ese pide solo RegistrarMovimientos.
        Assert.True(EsVisible(window, vm.NavHistorialMovimientosCommand));
    }

    [AvaloniaFact]
    public void OperadorConLosDosPermisos_VeRegistrarEntradaYSalida()
    {
        var (window, vm) = Montar(
            RolUsuario.Operador, Permisos.RegistrarMovimientos, Permisos.GestionarProductos);

        Assert.True(EsVisible(window, vm.NavRegistrarEntradaCommand));
        Assert.True(EsVisible(window, vm.NavRegistrarSalidaCommand));
    }

    [AvaloniaFact]
    public void OperadorConTresDeLosCuatroPermisos_NO_VeIngresoPorFactura()
    {
        // Bug de coherencia real, arreglado el 2026-08-15: exige CUATRO permisos simultaneos.
        var (window, vm) = Montar(
            RolUsuario.Operador,
            Permisos.RegistrarMovimientos, Permisos.RegistrarGastos, Permisos.VerFinanzas);

        Assert.False(EsVisible(window, vm.NavIngresoPorFacturaCommand));
    }

    [AvaloniaFact]
    public void OperadorConLosCuatroPermisos_VeIngresoPorFactura()
    {
        var (window, vm) = Montar(
            RolUsuario.Operador,
            Permisos.RegistrarMovimientos, Permisos.RegistrarGastos, Permisos.VerFinanzas,
            Permisos.GestionarProductos);

        Assert.True(EsVisible(window, vm.NavIngresoPorFacturaCommand));
    }

    [AvaloniaFact]
    public void OperadorConSoloVerReportes_VeLosReportesPeroNO_HistorialPorProducto()
    {
        // Historial por producto exige VerReportes + RegistrarMovimientos.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.VerReportes);

        Assert.True(EsVisible(window, vm.NavValorizacionCommand));
        Assert.True(EsVisible(window, vm.NavStockCategoriaCommand));
        Assert.True(EsVisible(window, vm.NavMasMovidosCommand));
        Assert.True(EsVisible(window, vm.NavAuditoriaLogCommand));

        Assert.False(EsVisible(window, vm.NavHistorialPorProductoCommand));
    }

    // ── Lo estructural: no se puede simular con permisos ────────────────────────

    [AvaloniaFact]
    public void OperadorConTODOSLosPermisos_SIGUE_SinVerLoAdminOnly()
    {
        // ESTE es el test mas importante del archivo. Importacion, Mantenimiento y Usuarios van
        // por EsAdmin, y AuthorizationService.PermisosEstructuralesAdmin corta ANTES de mirar
        // PermisosActuales: un Operador no llega ahi ni con el permiso en la lista.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.Todos.ToArray());

        Assert.False(EsVisible(window, vm.NavImportacionCommand));
        Assert.False(EsVisible(window, vm.NavMantenimientoCommand));
        Assert.False(EsVisible(window, vm.NavUsuariosCommand));
    }

    [AvaloniaFact]
    public void Admin_VeTodo()
    {
        var (window, vm) = Montar(RolUsuario.Admin);

        Assert.True(EsVisible(window, vm.NavInicioCommand));
        Assert.True(EsVisible(window, vm.NavProductosCommand));
        Assert.True(EsVisible(window, vm.NavIngresoPorFacturaCommand));
        Assert.True(EsVisible(window, vm.NavHistorialPorProductoCommand));
        Assert.True(EsVisible(window, vm.NavMaestrosFinanzasCommand));
        Assert.True(EsVisible(window, vm.NavImportacionCommand));
        Assert.True(EsVisible(window, vm.NavMantenimientoCommand));
        Assert.True(EsVisible(window, vm.NavUsuariosCommand));
    }
}
