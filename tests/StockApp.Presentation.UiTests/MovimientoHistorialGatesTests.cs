using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Red de seguridad de los 3 IsVisible de MovimientoHistorialView.axaml -- Task 6.2 del plan de
/// Fase B, ANTES de tocar la vista (Task 6.3). Estos 3 gates custodian dos bugfixes reales de
/// 2026-08-16 (MovimientoHistorialViewModel.cs), evolucionados por el bugfix 2026-08-19 (migración
/// del ComboBox de precarga completa del filtro "Producto" a AutoCompleteBox de búsqueda
/// server-side, mismo mecanismo que "Producto a recalcular").
///
/// Fórmulas (leidas del ViewModel, ver comentarios ahí):
/// - PuedeFiltrarPorProducto: NO es una propiedad calculada. Desde el bugfix 2026-08-19 tiene DOS
///   puntos de escritura distintos, no uno solo: (a) InicializarAsync la fija de forma OPTIMISTA
///   según el chequeo de sesión (sin HTTP) -- true si tiene GestionarProductos, false si no; (b)
///   BuscarProductosParaFiltroInternalAsync (el AsyncPopulator del filtro) la apaga
///   REACTIVAMENTE si una búsqueda real choca con un 403 del servidor (permiso "aparente" pero
///   revocado). Ya no hay una única llamada eager en InicializarAsync que cubra el caso "permiso
///   presente pero servidor rechaza" -- ese caso hoy requiere simular una búsqueda real.
/// - PuedeRecalcularStock: computada, `_session.RolActual == Admin || PermisosActuales.Contains(
///   Permisos.RecalcularStock)`. Rol Operador con permisos explicitos (nunca Admin: cortocircuita
///   el OR antes de mirar el permiso real).
///
/// En la vista v2 quedan dos IsVisible: el del campo "Producto" de la barra y el del botón "Recalcular stock" del
/// encabezado. El campo "Producto a recalcular:" se mudó al diálogo de Recalcular stock, que solo abre ese botón.
/// </summary>
public class MovimientoHistorialGatesTests
{
    /// <summary>
    /// IMovimientoStockService minimo: ObtenerHistorialAsync se llama siempre (CargarAsync corre
    /// al final de InicializarAsync sin importar el resultado de los gates); el resto no se
    /// ejercita en estos tests.
    /// </summary>
    private sealed class MovimientoStockServiceHistorialFake : IMovimientoStockService
    {
        public Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
            => Task.FromResult<IReadOnlyList<MovimientoHistorialDto>>(Array.Empty<MovimientoHistorialDto>());

        public Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private sealed class RecalcularStockDialogFake : IRecalcularStockDialogService
    {
        public Task<ProductoDto?> ElegirProductoAsync(Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar)
            => Task.FromResult<ProductoDto?>(null);
    }

    /// <summary>
    /// IProductoService configurable para forzar el camino "403 real" de una búsqueda del
    /// filtro. BuscarAsync (el overload de 3 parámetros, usado por la precarga VIEJA) lanza
    /// SIEMPRE: bugfix 2026-08-19 eliminó esa precarga de InicializarAsync, así que si algún
    /// cambio futuro la reintroduce sin querer, este fake explota en vez de devolver
    /// silenciosamente una lista vacía y dejar pasar la regresión.
    /// </summary>
    private sealed class ProductoServiceHistorialFake : IProductoService
    {
        private readonly bool _lanzar403;

        public ProductoServiceHistorialFake(bool lanzar403 = false) => _lanzar403 = lanzar403;

        public Task<int> AltaAsync(StockApp.Domain.Entities.Producto producto)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task ModificarAsync(StockApp.Domain.Entities.Producto producto)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task BajaLogicaAsync(int id)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task CambiarPrecioAsync(int id, decimal precioCosto)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
            => throw new NotSupportedException(
                "No debe invocarse mas: bugfix 2026-08-19 elimino la precarga completa de InicializarAsync.");

        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
            => _lanzar403
                ? Task.FromException<IReadOnlyList<ProductoDto>>(new UnauthorizedAccessException())
                : Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Movimientos;assembly=GestionMunicipal"
                Width="1200" Height="900">
            <vistas:MovimientoHistorialView />
        </Window>
        """;

    private static (Window Window, MovimientoHistorialViewModel Vm) Montar(
        RolUsuario rol, string[] permisos, bool lanzar403AlBuscarPorTexto = false)
    {
        var vm = new MovimientoHistorialViewModel(
            new MovimientoStockServiceHistorialFake(),
            new NavigationServiceFake(),
            new ProductoServiceHistorialFake(lanzar403AlBuscarPorTexto),
            new ConfirmacionServiceFake(),
            new SesionFake(rol, permisos),
            new RecalcularStockDialogFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await InicializarAsync() del DataContextChanged

        return (window, vm);
    }

    /// <summary>
    /// El filtro "Producto" de la barra v2 es un CampoFormulario con esa etiqueta: se ubica por la etiqueta y no por
    /// el TextBlock, que no existe mientras el campo nunca se mostró (la plantilla no se aplica a un control oculto).
    /// </summary>
    private static CampoFormulario CampoPorEtiqueta(Window window, string etiqueta)
        => window.GetVisualDescendants().OfType<CampoFormulario>().First(c => c.Etiqueta == etiqueta);

    private static Button BotonPorContenido(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == texto);

    // ---- PuedeFiltrarPorProducto (:30) ----

    [AvaloniaFact]
    public void PuedeFiltrarPorProducto_ConGestionarProductosYServidorOk_ComboVisible()
    {
        var (window, _) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos, Permisos.GestionarProductos],
            lanzar403AlBuscarPorTexto: false);

        var etiqueta = CampoPorEtiqueta(window, "Producto");

        Assert.True(ArbolVisual.EsVisibleEnArbol(etiqueta));
    }

    [AvaloniaFact]
    public void PuedeFiltrarPorProducto_SinGestionarProductos_ComboOculto()
    {
        var (window, _) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos]);

        var etiqueta = CampoPorEtiqueta(window, "Producto");

        Assert.False(ArbolVisual.EsVisibleEnArbol(etiqueta));
    }

    /// <summary>
    /// Bugfix 2026-08-19 (evolución del gate anterior, mismo nombre de test conservado a
    /// propósito porque el CONTRATO que custodia es el mismo: "permiso presente pero servidor
    /// rechaza -> filtro oculto"): sin la precarga completa de InicializarAsync, ya no hay una
    /// única llamada eager al montar la vista que dispare el 403 -- el filtro arranca VISIBLE
    /// (chequeo de sesión optimista, sin HTTP) y recién se oculta cuando el usuario efectivamente
    /// busca. Se simula esa búsqueda invocando el AsyncPopulator del VM directo (mismo patrón que
    /// usa el AutoCompleteBox real, ver AutoCompleteBox.PopulateAsync), sin necesidad de manejar
    /// el timer de MinimumPopulateDelay en un test headless.
    /// </summary>
    [AvaloniaFact]
    public async Task PuedeFiltrarPorProducto_ConPermisoPeroBusquedaRechaza403_SeOcultaTrasLaBusqueda()
    {
        var (window, vm) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos, Permisos.GestionarProductos],
            lanzar403AlBuscarPorTexto: true);

        var etiqueta = CampoPorEtiqueta(window, "Producto");
        Assert.True(ArbolVisual.EsVisibleEnArbol(etiqueta), "arranca visible: chequeo optimista de sesion, sin HTTP");

        await vm.BuscarProductosParaFiltroAsync("azu", System.Threading.CancellationToken.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ArbolVisual.EsVisibleEnArbol(etiqueta), "se oculta reactivamente tras el 403 real de la busqueda");
    }

    // ---- PuedeRecalcularStock ----
    // El campo "Producto a recalcular:" se mudó al diálogo de Recalcular stock (el encabezado v2 deja solo el botón):
    // su gate es el del botón, y el VM no abre el diálogo sin el permiso (AbrirRecalcular_SinPermiso_NoAbreElDialogo).

    /// <summary>Con stock.recalcular (Operador de permisos mixtos), el botón "Recalcular stock" del encabezado se ve.</summary>
    [AvaloniaFact]
    public void PuedeRecalcularStock_ConPermiso_BotonVisible()
    {
        var (window, _) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos, Permisos.RecalcularStock]);

        var boton = BotonPorContenido(window, "Recalcular stock");

        Assert.True(ArbolVisual.EsVisibleEnArbol(boton));
    }

    /// <summary>Sin stock.recalcular, el botón "Recalcular stock" queda oculto (IsVisible, no IsEnabled).</summary>
    [AvaloniaFact]
    public void PuedeRecalcularStock_SinPermiso_BotonOculto()
    {
        var (window, _) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos]);

        var boton = BotonPorContenido(window, "Recalcular stock");

        Assert.False(ArbolVisual.EsVisibleEnArbol(boton));
    }
}
