using System.Collections.Generic;
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
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
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
            new AuthServiceFake(),
            new PreferenciasSidebarFake());

        // Tanda 5.3: expandir todos los grupos a proposito. Este banco de pruebas verifica que
        // el item exista en el arbol visual y este permitido -- no que el usuario haya dejado
        // ese grupo particular abierto. Sin esto, los items de un grupo colapsado quedan dentro
        // de un ItemsControl con IsVisible="False" y ArbolVisual.EsVisibleEnArbol los reporta
        // como no visibles aunque el gate de permiso los deje pasar, lo que daria falsos
        // negativos en cualquier test que pida Assert.True sobre un item de un grupo cerrado.
        foreach (var grupo in vm.Grupos)
            grupo.EstaExpandido = true;

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    /// <summary>
    /// Variante de <see cref="Montar"/> para el caso de revocación en caliente (Ruling 6): expone
    /// la fake de navegación para poder disparar <see cref="INavigationService.Cambiado"/> a mano
    /// -- el mismo evento que dispara una navegación real -- y arma AuthServiceFake para que el
    /// "refresco desde el servidor" resultante devuelva un set de permisos distinto al que la
    /// sesión tenía al arrancar.
    /// </summary>
    private static (Window Window, ShellMainViewModel Vm, NavigationServiceFake Navegacion) MontarParaRevocacion(
        RolUsuario rol, string[] permisosIniciales, IReadOnlySet<string> permisosTrasRefresco)
    {
        var sesion = new SesionFake(rol, permisosIniciales);
        var navegacion = new NavigationServiceFake();

        var vm = new ShellMainViewModel(
            sesion,
            navegacion,
            new InfoAppFake(),
            new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, permisosTrasRefresco),
            new PreferenciasSidebarFake());

        foreach (var grupo in vm.Grupos)
            grupo.EstaExpandido = true;

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm, navegacion);
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

    [AvaloniaFact]
    public void OperadorConGestionarTareas_VeTareas()
    {
        // Permisos MIXTOS a proposito (target + uno sin relacion): un Admin cortocircuita el
        // chequeo antes de mirar PermisosActuales, asi que este test no prueba nada montado como
        // Admin. Con Operador, el gate real es PuedeGestionarTareas.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.GestionarTareas, Permisos.VerFinanzas);

        Assert.True(EsVisible(window, vm.NavTareasCommand));
    }

    [AvaloniaFact]
    public void OperadorSinGestionarTareas_NoVeTareas()
    {
        // Permisos MIXTOS que NO incluyen GestionarTareas: prueba que el gate no se abre por
        // tener cualquier permiso, sino especificamente el que corresponde a este item.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.GestionarProductos);

        Assert.False(EsVisible(window, vm.NavTareasCommand));
    }

    [AvaloniaFact]
    public void OperadorConGestionarDocumentos_VeDocumentos()
    {
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.GestionarDocumentos, Permisos.RegistrarMovimientos);

        Assert.True(EsVisible(window, vm.NavDocumentosCommand));
    }

    [AvaloniaFact]
    public void OperadorSinGestionarDocumentos_NoVeDocumentos()
    {
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.RegistrarMovimientos, Permisos.VerFinanzas);

        Assert.False(EsVisible(window, vm.NavDocumentosCommand));
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

    // ── Task 5.3, Step 4: gates de grupo ──────────────────────────────────────────

    [AvaloniaFact]
    public void OperadorConSoloGestionarMaestrosFinanzas_VeElGrupoFinanzasCONSuTitulo()
    {
        // Bug preexistente que la tanda 5 arregla: el header "Finanzas" estaba gateado por
        // PuedeVerFinanzas mientras Maestros va por PuedeGestionarMaestrosFinanzas, asi que este
        // operador veia el boton colgando suelto, sin titulo de seccion arriba.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.GestionarMaestrosFinanzas);

        var grupoFinanzas = vm.Grupos.First(g => g.Titulo == "Finanzas");

        Assert.True(grupoFinanzas.EsVisible);
        Assert.Single(grupoFinanzas.ItemsVisibles);
        Assert.True(EsVisible(window, vm.NavMaestrosFinanzasCommand));
        Assert.False(EsVisible(window, vm.NavGastosCommand));
    }

    [AvaloniaFact]
    public void OperadorSinPermisos_NingunGrupoSeRenderiza()
    {
        var (_, vm) = Montar(RolUsuario.Operador);

        Assert.All(vm.Grupos, g => Assert.False(g.EsVisible));
    }

    [AvaloniaFact]
    public void OperadorConTODOSLosPermisos_NO_VeLosGruposAdminOnly()
    {
        var (_, vm) = Montar(RolUsuario.Operador, Permisos.Todos.ToArray());

        Assert.False(vm.Grupos.First(g => g.Titulo == "Importación").EsVisible);
        Assert.False(vm.Grupos.First(g => g.Titulo == "Administración").EsVisible);
    }

    // ── Ruling 6: revocación en caliente ─────────────────────────────────────────

    /// <summary>
    /// Red de seguridad del bug que dejó sin resolver la tanda anterior: ItemNavegacion.EsVisible
    /// era un snapshot tomado una sola vez en el constructor de ShellMainViewModel.
    /// RefrescarPermisosAsync notifica las propiedades Puede* del ViewModel, pero -- antes de este
    /// ruling -- nunca recalculaba el EsVisible de los ItemNavegacion ya construidos. Como el
    /// XAML de la Task 5.3 bindea IsVisible directamente al item, el resultado era: a un usuario
    /// al que se le revoca un permiso en caliente, el botón le queda visible. Este repo tiene
    /// flujo real de revocación en caliente (panel de permisos + refresco best-effort al navegar),
    /// así que era explotable de verdad.
    ///
    /// Dispara el refresco por el mismo camino que usa la app: INavigationService.Cambiado (lo que
    /// levanta cada Navegar&lt;T&gt;() real) -&gt; ShellMainViewModel.OnNavegacionCambiada -&gt;
    /// RefrescarPermisosAsync. AuthServiceFake resuelve de forma síncrona (Task.FromResult), así
    /// que para cuando SimularNavegacion() retorna, el refresco fire-and-forget ya corrió entero
    /// -- no hace falta await sobre el Task interno.
    ///
    /// Limitación anotada al escribir este test: hasta que la Task 5.3 reescriba
    /// ShellMainView.axaml, el botón de Productos sigue bindeado directamente a
    /// PuedeGestionarProductos (no a ItemNavegacion.EsVisible), y esa propiedad SIEMPRE quedó
    /// bien notificada -- el bug de Ruling 6 vive en el ItemNavegacion, todavía no conectado al
    /// árbol visual. Por eso el assert sobre el botón, solo, no alcanzaba para poner este test en
    /// rojo antes del fix: aunque se comentara RecalcularVisibilidad(), el botón seguía
    /// ocultándose igual por el camino viejo. La guardia real de Ruling 6 es el assert directo
    /// sobre ItemNavegacion.EsVisible de abajo, confirmado en rojo por mutación (comentando la
    /// llamada a RecalcularVisibilidad() en RefrescarPermisosAsync) antes de implementar el fix.
    /// El assert sobre el botón queda igual, como guardia punta a punta: pasa a ser una red real
    /// en cuanto la Task 5.3 conecta el XAML al item, más abajo en este mismo commit.
    /// </summary>
    [AvaloniaFact]
    public void RevocacionEnCaliente_OcultaElBotonQueYaEraVisible()
    {
        var (window, vm, navegacion) = MontarParaRevocacion(
            RolUsuario.Operador,
            new[] { Permisos.GestionarProductos },
            new HashSet<string>()); // el "refresco desde el servidor" ya no trae el permiso

        var itemProductos = vm.Grupos.SelectMany(g => g.Items).First(i => i.Seccion == "Productos");

        Assert.True(EsVisible(window, vm.NavProductosCommand),
            "Con el permiso concedido al arrancar, el botón de Productos tiene que ser visible.");
        Assert.True(itemProductos.EsVisible);

        navegacion.SimularNavegacion();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        // Guardia directa del Ruling 6: el ItemNavegacion tiene que recalcular su EsVisible
        // contra las Puede* actuales, no quedarse con el valor tomado en CrearItem.
        Assert.False(itemProductos.EsVisible,
            "Tras revocar el permiso en caliente, ItemNavegacion.EsVisible tiene que pasar a false.");

        // Guardia punta a punta (ver limitación en el doc de este test).
        Assert.False(EsVisible(window, vm.NavProductosCommand),
            "Tras revocar el permiso en caliente, el botón tiene que dejar de ser visible.");
    }
}
