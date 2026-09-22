using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Bugfix (fix/grillas-minwidth-scroll): cuando la suma de anchos naturales de las columnas de
/// GastosView.axaml supera el viewport y la vista no declara
/// ScrollViewer.HorizontalScrollBarVisibility, Avalonia.Controls.DataGrid NO scrollea -- comprime
/// columnas arbitrarias hasta el piso global MinColumnWidth=40 (Themes/DataGrid.axaml). A 40px el
/// padding de celda (12,8) no deja entrar ni un carácter: header Y valor desaparecen por
/// clipping duro, sin ellipsis. Reproducido en la app real: a 1300px de ancho colapsan
/// "Proveedor"/"Detalle" (con contenido real adentro) y "Estado" queda en una "P" suelta.
///
/// Este test monta la vista REAL (no el ViewModel -- un test de VM no custodia un gate de UI,
/// ver memoria test-vm-no-custodia-gate-ui) con datos que llenan las 11 columnas, a un ancho de
/// ventana angosto (1300px, el mismo donde se reprodujo a mano), y mide ActualWidth de cada
/// columna DESPUÉS del layout. El umbral (60px) está deliberadamente por encima del piso que
/// causa el bug (40px) y por debajo del MinWidth más chico que declara el fix (70px) para que el
/// test quede rojo hoy y verde después de declarar MinWidth por columna.
/// </summary>
public class GastosViewColumnasAnchoTests
{
    private const int UmbralLegiblePx = 60;

    private sealed class GastoServiceFake : IGastoService
    {
        private readonly IReadOnlyList<Gasto> _gastos;
        public GastoServiceFake(IReadOnlyList<Gasto> gastos) => _gastos = gastos;

        public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<Gasto> ObtenerPorIdAsync(int id)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
            => Task.FromResult(_gastos);
        public Task<int> RegistrarPagoAsync(PagoGasto pago)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AnularPagoAsync(int gastoId, int pagoId)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private sealed class CsvExporterFake : ICsvExporter
    {
        public string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder) => "csv";
    }

    private sealed class ServicioGuardadoArchivoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(true);
        public Task<bool> GuardarBytesAsync(
            System.IO.Stream contenido, string nombreSugerido, System.Threading.CancellationToken ct = default,
            string? extension = null, string? tipoMime = null) => Task.FromResult(true);
    }

    private sealed class PdfExporterNoOpFake : IPdfExporter
    {
        public byte[] Exportar<T>(
            IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos,
            ResumenPdf? resumen = null)
            => Array.Empty<byte>();
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=GestionMunicipal"
                Width="1300" Height="700">
            <vistas:GastosView />
        </Window>
        """;

    /// <summary>
    /// Un solo Gasto con datos REALES que llenan las 11 columnas (nombres/detalle largos, no
    /// "x" ni "abc" -- ese fue exactamente el escenario reproducido a mano: "Proveedor" y
    /// "Detalle" con contenido real adentro colapsando a 40px). Estado calculado "Pendiente" es
    /// el valor más largo del enum EstadoGasto (9 caracteres).
    /// </summary>
    private static Gasto CrearGastoConDatosReales() => new()
    {
        Id = 1,
        Proveedor = new Proveedor { Id = 1, Nombre = "Distribuidora Municipal del Este SRL", Activo = true },
        NumeroFactura = "A-0004521",
        Detalle = "Compra de materiales de construcción para la rambla costanera",
        Fecha = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
        MontoTotal = 125_450.75m,
        FuenteFinanciamiento = new FuenteFinanciamiento { Id = 1, Nombre = "Rentas Generales Departamentales", Activo = true },
        RubroGasto = new RubroGasto { Id = 1, Codigo = 10, Nombre = "Materiales de construcción", Activo = true },
        LineaPoa = new LineaPoa { Id = 1, Nombre = "Obras viales y mantenimiento urbano", Ejercicio = 2026, Activo = true },
        CondicionPago = CondicionPago.Contado,
        Activo = true,
    };

    private static async Task<(Window Window, DataGrid Grid)> MontarAsync(Gasto gasto)
    {
        var vm = new GastosViewModel(
            new GastoServiceFake(new[] { gasto }),
            new SesionFake(RolUsuario.Admin, Array.Empty<string>()),
            new ProveedorServiceFake(Array.Empty<Proveedor>()),
            new FuenteFinanciamientoServiceFake(Array.Empty<FuenteFinanciamiento>()),
            new RubroGastoServiceFake(Array.Empty<RubroGasto>()),
            new LineaPoaServiceFake(Array.Empty<LineaPoa>()),
            new NavigationServiceFake(),
            new ConfirmacionServiceFake(),
            new CsvExporterFake(),
            new ServicioGuardadoArchivoFake(),
            new PdfExporterNoOpFake(),
            new ServicioAperturaArchivoFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        // FiltrarAsync (disparado por CargarAsync) es async -- una tercera pasada asegura que
        // Filas ya esté poblada y el DataGrid haya medido el layout final con filas reales.
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        return (window, grid);
    }

    [AvaloniaFact]
    public async Task Montar_VentanaAngosta_NingunaColumnaQuedaPorDebajoDelMinimoLegible()
    {
        var (window, grid) = await MontarAsync(CrearGastoConDatosReales());

        Assert.Equal(11, grid.Columns.Count);

        var angostas = grid.Columns
            .Where(c => c.ActualWidth < UmbralLegiblePx)
            .Select(c => $"{c.Header} ({c.ActualWidth:0.#}px)")
            .ToList();

        Assert.True(angostas.Count == 0,
            $"Columnas por debajo del mínimo legible ({UmbralLegiblePx}px): {string.Join(", ", angostas)}");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
