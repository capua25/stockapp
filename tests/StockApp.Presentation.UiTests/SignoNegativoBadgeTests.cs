using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Catalogo;
using StockApp.Presentation.Views.Reportes;
using StockApp.Presentation.Views.Tareas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián de la Task B2-T (Ruling B-6, 2026-08-19): custodia que el color rojo de
/// <c>SignoNegativoBrushConverter</c> venga ACOMPAÑADO de un <see cref="BadgeEstado"/> con
/// palabra, en al menos una vista representativa de cada uno de los 4 módulos que tienen sitios
/// (Catálogo, Finanzas, Tareas, Reportes) -- mínimo exigido por el plan (Step 3). Los 10 sitios
/// con badge viven mayormente dentro de <c>CellTemplate</c>/<c>ItemTemplate</c>, invisibles para
/// <c>GuardianDePatronTests</c> (Ruling B-16): sin <c>ItemsSource</c> asignado no se realiza
/// ninguna celda, así que estos tests montan la vista real y asignan datos con signos opuestos,
/// igual que <see cref="ReportesAlineacionNumericaTests"/>.
/// </summary>
public class SignoNegativoBadgeTests
{
    private static Window MontarEnVentana(Avalonia.Controls.Control vista, double width = 900, double height = 500)
    {
        var window = new Window { Width = width, Height = height, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IReadOnlyList<BadgeEstado> BadgesConTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<BadgeEstado>().Where(b => b.Texto == texto).ToList();

    // ---- Catálogo: ProductoListView, sitio StockActual (:90) ----

    [AvaloniaFact]
    public void Montar_ProductoListViewConStockNegativo_MuestraBadgeStockNegativoSoloEnLaFilaNegativa()
    {
        var vista = new ProductoListView();
        var window = MontarEnVentana(vista);

        var grilla = vista.GetVisualDescendants().OfType<DataGrid>().Single();
        grilla.ItemsSource = new[]
        {
            ProductoDe(1, "Tornillos", stockActual: -5m),
            ProductoDe(2, "Tuercas", stockActual: 20m),
        };
        Dispatcher.UIThread.RunJobs();

        var badges = BadgesConTexto(window, "Stock negativo");

        Assert.Single(badges, b => b.IsVisible);
    }

    private static ProductoDto ProductoDe(int id, string nombre, decimal stockActual) => new(
        Id: id, Codigo: $"C{id}", CodigoBarras: null, Nombre: nombre, Descripcion: null,
        CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: 1,
        UnidadMedidaNombre: "Unidad", PrecioCosto: 10m, PrecioVenta: 15m, StockActual: stockActual,
        StockMinimo: 0m, Activo: true, FechaAlta: DateTime.UtcNow);

    // ---- Reportes: StockCategoriaView, sitio StockTotal (:49) ----

    [AvaloniaFact]
    public void Montar_StockCategoriaViewConStockNegativo_MuestraBadgeStockNegativoSoloEnLaFilaNegativa()
    {
        var vista = new StockCategoriaView();
        var window = MontarEnVentana(vista);

        var grilla = vista.GetVisualDescendants().OfType<DataGrid>().Single();
        grilla.ItemsSource = new[]
        {
            new StockCategoriaDto("Ferretería", 12, -3m, 15000m, 22000m),
            new StockCategoriaDto("Electricidad", 5, 8m, 8000m, 9500m),
        };
        Dispatcher.UIThread.RunJobs();

        var badges = BadgesConTexto(window, "Stock negativo");

        Assert.Single(badges, b => b.IsVisible);
    }

    // ---- Tareas: TareaListView, sitio DiasParaVencer (:33, sección Pendientes) ----

    [AvaloniaFact]
    public void Montar_TareaListViewConTareaVencida_MuestraBadgeVencidaSoloEnLaFilaVencida()
    {
        var vencida = TareaDe(1, "Con vencimiento", EstadoTarea.Pendiente, DateTime.UtcNow.Date.AddDays(-5));
        var alDia = TareaDe(2, "Al dia", EstadoTarea.Pendiente, DateTime.UtcNow.Date.AddDays(5));

        var vista = new TareaListView();
        var window = MontarEnVentana(vista);

        var itemsControl = vista.GetVisualDescendants().OfType<ItemsControl>().First();
        itemsControl.ItemsSource = new[]
        {
            new TareaFila(vencida, RolUsuario.Admin),
            new TareaFila(alDia, RolUsuario.Admin),
        };
        Dispatcher.UIThread.RunJobs();

        var badges = BadgesConTexto(window, "Vencida");

        Assert.Single(badges, b => b.IsVisible);

        // Mismo criterio que TareaListViewTests: el color sigue viniendo del Foreground del
        // titulo, no del badge -- esta prueba solo custodia que la PALABRA se agregó.
    }

    private static Tarea TareaDe(int id, string titulo, EstadoTarea estado, DateTime? fechaLimite) => new()
    {
        Id = id, Titulo = titulo, Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = fechaLimite,
    };

    // ---- Finanzas: LibroCajaView, sitio SaldoFinal (:39, barra de filtros) ----

    private sealed class FinanzasVistasServiceFake : IFinanzasVistasService
    {
        private readonly decimal _saldoFinal;

        public FinanzasVistasServiceFake(decimal saldoFinal) => _saldoFinal = saldoFinal;

        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)
            => Task.FromResult(new LibroCajaMesDto(
                anio, mes, 0m, _saldoFinal,
                Array.Empty<MovimientoCajaDto>(), Array.Empty<TotalPorClaveDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio)
            => Task.FromResult(new LibroCajaAnualDto(anio, Array.Empty<TotalMensualDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private sealed class CsvExporterFake : ICsvExporter
    {
        public string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder) => "csv";
    }

    private sealed class ServicioGuardadoArchivoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(true);
        public Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default) => Task.FromResult(true);
    }

    private const string XamlLibroCaja = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=StockApp.Presentation"
                Width="1100" Height="700">
            <vistas:LibroCajaView />
        </Window>
        """;

    private static Window MontarLibroCaja(decimal saldoFinal)
    {
        var vm = new LibroCajaViewModel(
            new FinanzasVistasServiceFake(saldoFinal),
            new CsvExporterFake(),
            new ServicioGuardadoArchivoFake(),
            new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(XamlLibroCaja, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    [AvaloniaFact]
    public void Montar_LibroCajaViewConSaldoFinalNegativo_MuestraBadgeSaldoNegativo()
    {
        var window = MontarLibroCaja(saldoFinal: -100m);

        var badges = BadgesConTexto(window, "Saldo negativo");

        Assert.Single(badges, b => b.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_LibroCajaViewConSaldoFinalPositivo_NoMuestraBadgeSaldoNegativo()
    {
        var window = MontarLibroCaja(saldoFinal: 100m);

        var badges = BadgesConTexto(window, "Saldo negativo");

        Assert.DoesNotContain(badges, b => b.IsVisible);
    }
}
