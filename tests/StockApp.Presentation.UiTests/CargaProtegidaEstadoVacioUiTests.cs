using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Verifica, montando la VISTA REAL (no solo el ViewModel), que <c>EstadoVacio</c> realmente
/// aparece en el árbol visual cuando la carga fue rechazada por falta de permiso (bugfix
/// "pantalla muda ante un 403"). Un test de ViewModel NO alcanza para esto: sacar el binding de
/// IsVisible/EsError del .axaml dejaría los tests de VM en verde igual (mismo gotcha que
/// TestVmNoCustodiaGateUiTests ya documentó para otro caso). Dos vistas, dos formas de wrap
/// distintas: FuenteFinanciamientoListView (embebida: Border "card" envuelto en un Grid nuevo) y
/// StockCategoriaView (DockPanel con MargenVista, ya era el root, envuelto en un Grid nuevo).
/// </summary>
public class CargaProtegidaEstadoVacioUiTests
{
    // ── fakes mínimos (este proyecto no referencia Moq, ver MovimientoRegistroFakes.cs) ──

    private sealed class FuenteFinanciamientoServiceUnauthorizedFake : IFuenteFinanciamientoService
    {
        public Task<int> AltaAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException();
        public Task ModificarAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync()
            => throw new UnauthorizedAccessException();
        public Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync()
            => throw new NotSupportedException();
    }

    private sealed class ReporteStockServiceUnauthorizedFake : IReporteStockService
    {
        public Task<ValorizacionReporteDto> ObtenerValorizacionAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
            => throw new UnauthorizedAccessException();
        public Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(DateTime? desde, DateTime? hasta, int topN = 20)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<StockApp.Application.Movimientos.MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(
            int productoId, DateTime? desde, DateTime? hasta) => throw new NotSupportedException();
    }

    private sealed class ServicioGuardadoArchivoNoOpFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarBytesAsync(
            System.IO.Stream contenido, string nombreSugerido, CancellationToken ct = default,
            string? extension = null, string? tipoMime = null)
            => Task.FromResult(false);
    }

    private sealed class PdfExporterNoOpFake : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos, ResumenPdf? resumen = null)
            => Array.Empty<byte>();
    }

    // ── helpers de montaje ──────────────────────────────────────────────────

    private static Window MontarConDataContext(string xaml, object vm)
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await de DataContextChanged
        return window;
    }

    private static EstadoVacio? BuscarEstadoVacio(Window window)
        => window.GetVisualDescendants().OfType<EstadoVacio>().FirstOrDefault();

    // ── CatalogoListadoView (detalle genérico de Catálogos; reemplaza a FuenteFinanciamientoListView) ──

    private const string XamlCatalogoListadoView = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:StockApp.Presentation.Views.Catalogo;assembly=GestionMunicipal"
                Width="800" Height="600">
            <v:CatalogoListadoView />
        </Window>
        """;

    [AvaloniaFact]
    public async System.Threading.Tasks.Task CatalogoListadoView_SiSinPermiso_MuestraEstadoVacioConMensajeYEsError()
    {
        var vm = new FuenteFinanciamientoListViewModel(
            new FuenteFinanciamientoServiceUnauthorizedFake(), new NavigationServiceFake(), new ConfirmacionServiceFake());

        var window = MontarConDataContext(XamlCatalogoListadoView, vm);
        await vm.CargarAsync(); // el detalle no carga solo: lo dispara CatalogosViewModel
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.SinPermiso); // el VM sí quedó marcado -- si esto falla, el problema es del VM, no de la vista.

        // Los estados de la vista v2 son StockApp.Presentation.Controls.V2.EstadoVacio (no el control viejo de BuscarEstadoVacio).
        var estadoVacio = window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.EstadoVacio>()
            .FirstOrDefault(e => e.Name == "EstadoSinPermiso");
        Assert.True(estadoVacio is not null, "CatalogoListadoView no muestra EstadoVacio en su árbol visual.");
        Assert.True(estadoVacio!.IsVisible, "EstadoVacio está en el árbol pero IsVisible es false.");
        Assert.True(estadoVacio.EsError);
        Assert.False(string.IsNullOrWhiteSpace(estadoVacio.Mensaje));
    }

    // ── StockCategoriaView (DockPanel con MargenVista) ──────────────────────

    private const string XamlStockCategoriaView = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:StockApp.Presentation.Views.Reportes;assembly=GestionMunicipal"
                Width="800" Height="600">
            <v:StockCategoriaView />
        </Window>
        """;

    [AvaloniaFact]
    public void StockCategoriaView_SiSinPermiso_MuestraEstadoVacioConMensajeYEsError()
    {
        var vm = new StockCategoriaViewModel(
            new ReporteStockServiceUnauthorizedFake(), new ServicioGuardadoArchivoNoOpFake(), new ConfirmacionServiceFake(),
            new PdfExporterNoOpFake(), new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin));

        var window = MontarConDataContext(XamlStockCategoriaView, vm);

        Assert.True(vm.SinPermiso);

        // Reporte v2: el EstadoVacio de "Sin permiso" es del tema v2 (Controls.V2) y lo pone MarcoReporte.
        var estadoVacio = window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.EstadoVacio>()
            .FirstOrDefault(e => ArbolVisual.EsVisibleEnArbol(e));
        Assert.True(estadoVacio is not null, "StockCategoriaView no muestra EstadoVacio en su árbol visual.");
        Assert.True(estadoVacio!.EsError);
        Assert.False(string.IsNullOrWhiteSpace(estadoVacio.Mensaje));
    }
}
