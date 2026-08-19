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
using StockApp.Presentation.ViewModels.Movimientos;
using System.Threading.Tasks;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Red de seguridad de los 3 IsVisible de MovimientoHistorialView.axaml (:30, :86, :92) --
/// Task 6.2 del plan de Fase B, ANTES de tocar la vista (Task 6.3). Hasta esta tanda, la vista
/// tenia cero tests de UI y estos 3 gates custodian dos bugfixes reales de 2026-08-16
/// (MovimientoHistorialViewModel.cs:72-93, :152-163).
///
/// Fórmulas (leidas del ViewModel, ver comentarios ahi):
/// - PuedeFiltrarPorProducto: NO es una propiedad calculada, es un campo que InicializarAsync
///   setea en false si (a) la sesion no tiene GestionarProductos (ni es Admin), o (b)
///   IProductoService.BuscarAsync lanza UnauthorizedAccessException (403 real del servidor, aun
///   con el permiso "aparente"). Por eso hay tres casos, no dos: sin permiso, con permiso pero
///   el servidor igual rechaza, y el camino feliz.
/// - PuedeRecalcularStock: computada, `_session.RolActual == Admin || PermisosActuales.Contains(
///   Permisos.RecalcularStock)`. Rol Operador con permisos explicitos (nunca Admin: cortocircuita
///   el OR antes de mirar el permiso real).
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

    /// <summary>
    /// IProductoService configurable para forzar el camino "403 real" de InicializarAsync:
    /// GestionarProductos presente en la sesion, pero el servidor igual rechaza BuscarAsync.
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

        public Task CambiarPrecioAsync(int id, decimal precioCosto, decimal precioVenta)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
            => _lanzar403
                ? Task.FromException<IReadOnlyList<ProductoDto>>(new UnauthorizedAccessException())
                : Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());

        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Movimientos;assembly=StockApp.Presentation"
                Width="1200" Height="900">
            <vistas:MovimientoHistorialView />
        </Window>
        """;

    private static (Window Window, MovimientoHistorialViewModel Vm) Montar(
        RolUsuario rol, string[] permisos, bool lanzar403AlBuscarProductos = false)
    {
        var vm = new MovimientoHistorialViewModel(
            new MovimientoStockServiceHistorialFake(),
            new NavigationServiceFake(),
            new ProductoServiceHistorialFake(lanzar403AlBuscarProductos),
            new ConfirmacionServiceFake(),
            new SesionFake(rol, permisos));

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await InicializarAsync() del DataContextChanged

        return (window, vm);
    }

    private static TextBlock TextoPorContenido(Window window, string texto)
        => window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == texto);

    private static Button BotonPorContenido(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == texto);

    // ---- PuedeFiltrarPorProducto (:30) ----

    [AvaloniaFact]
    public void PuedeFiltrarPorProducto_ConGestionarProductosYServidorOk_ComboVisible()
    {
        var (window, _) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos, Permisos.GestionarProductos],
            lanzar403AlBuscarProductos: false);

        var etiqueta = TextoPorContenido(window, "Producto");

        Assert.True(ArbolVisual.EsVisibleEnArbol(etiqueta));
    }

    [AvaloniaFact]
    public void PuedeFiltrarPorProducto_SinGestionarProductos_ComboOculto()
    {
        var (window, _) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos]);

        var etiqueta = TextoPorContenido(window, "Producto");

        Assert.False(ArbolVisual.EsVisibleEnArbol(etiqueta));
    }

    [AvaloniaFact]
    public void PuedeFiltrarPorProducto_ConPermisoPeroServidorRechaza403_ComboOculto()
    {
        var (window, _) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos, Permisos.GestionarProductos],
            lanzar403AlBuscarProductos: true);

        var etiqueta = TextoPorContenido(window, "Producto");

        Assert.False(ArbolVisual.EsVisibleEnArbol(etiqueta));
    }

    // ---- PuedeRecalcularStock (:86 boton, :92 campo) ----

    [AvaloniaFact]
    public void PuedeRecalcularStock_ConPermiso_BotonYCampoVisibles()
    {
        var (window, _) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos, Permisos.RecalcularStock]);

        var boton = BotonPorContenido(window, "Recalcular stock");
        var etiquetaCampo = TextoPorContenido(window, "Producto a recalcular (ID):");

        Assert.True(ArbolVisual.EsVisibleEnArbol(boton));
        Assert.True(ArbolVisual.EsVisibleEnArbol(etiquetaCampo));
    }

    [AvaloniaFact]
    public void PuedeRecalcularStock_SinPermiso_BotonYCampoOcultos()
    {
        var (window, _) = Montar(
            RolUsuario.Operador,
            [Permisos.RegistrarMovimientos]);

        var boton = BotonPorContenido(window, "Recalcular stock");
        var etiquetaCampo = TextoPorContenido(window, "Producto a recalcular (ID):");

        Assert.False(ArbolVisual.EsVisibleEnArbol(boton));
        Assert.False(ArbolVisual.EsVisibleEnArbol(etiquetaCampo));
    }
}
