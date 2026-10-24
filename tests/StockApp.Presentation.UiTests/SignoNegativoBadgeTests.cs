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
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Catalogo;
using StockApp.Presentation.Views.Finanzas;
using StockApp.Presentation.Views.Movimientos;
using StockApp.Presentation.Views.Reportes;
using StockApp.Presentation.Views.Tareas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián de la Task B2-T (Ruling B-6, 2026-08-19): custodia que el rojo de un valor negativo
/// venga ACOMPAÑADO de una <see cref="StockApp.Presentation.Controls.V2.Insignia"/> con
/// palabra, en al menos una vista representativa de cada uno de los 4 módulos que tienen sitios
/// (Catálogo, Finanzas, Tareas, Reportes). Los sitios con insignia viven mayormente dentro de
/// <c>CellTemplate</c>/<c>ItemTemplate</c>, invisibles para
/// <c>GuardianSistemaV2Tests</c> (Ruling B-16): sin <c>ItemsSource</c> asignado no se realiza
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

    private static IReadOnlyList<StockApp.Presentation.Controls.V2.Insignia> InsigniasConTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.Insignia>().Where(i => i.Texto == texto).ToList();

    // ---- Catálogo: ProductoListView, sitio StockActual (:90) ----

    [AvaloniaFact]
    public void Montar_ProductoListViewConStockNegativo_MuestraBadgeStockNegativoSoloEnLaFilaNegativa()
    {
        var vista = new ProductoListView();
        // Sin DataContext todo IsVisible enlazado queda visible (sin permiso, cargando y vacío a la vez, unos 270 px
        // sobre la grilla): con 500 de alto la grilla no tiene lugar para realizar filas.
        var window = MontarEnVentana(vista, height: 900);

        var grilla = vista.GetVisualDescendants().OfType<DataGrid>().Single();
        grilla.ItemsSource = new[]
        {
            ProductoDe(1, "Tornillos", stockActual: -5m),
            ProductoDe(2, "Tuercas", stockActual: 20m),
        };
        Dispatcher.UIThread.RunJobs();

        // La palabra la pone una Insignia peligro.
        var insignias = window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.Insignia>()
            .Where(i => i.Texto == "Stock negativo").ToList();

        var visible = Assert.Single(insignias, i => i.IsVisible);
        Assert.Equal(StockApp.Presentation.Controls.V2.VarianteInsignia.Peligro, visible.Variante);
    }

    private static ProductoDto ProductoDe(int id, string nombre, decimal stockActual) => new(
        Id: id, Codigo: $"C{id}", CodigoBarras: null, Nombre: nombre, Descripcion: null,
        CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: 1,
        UnidadMedidaNombre: "Unidad", PrecioCosto: 10m, StockActual: stockActual,
        StockMinimo: 0m, Activo: true, FechaAlta: DateTime.UtcNow);

    // ---- Reportes: StockCategoriaView, sitio StockTotal (:49) ----

    [AvaloniaFact]
    public void Montar_StockCategoriaViewConStockNegativo_MuestraBadgeStockNegativoSoloEnLaFilaNegativa()
    {
        // Reporte v2: la grilla se muestra con filas del ViewModel (MarcoReporte.HayDatos), así que los datos entran por el servicio.
        var servicio = new V2.ReporteStockFake
        {
            Categorias = { new StockCategoriaDto("Ferretería", 12, -3m, 15000m), new StockCategoriaDto("Electricidad", 5, 8m, 8000m) },
        };
        var vm = new StockApp.Presentation.ViewModels.Reportes.StockCategoriaViewModel(servicio, new V2.GuardadoSinUsoFake(),
            new ConfirmacionServiceFake(), new V2.PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin));
        var vista = new StockCategoriaView { DataContext = vm };
        var window = MontarEnVentana(vista);
        Dispatcher.UIThread.RunJobs();

        // Reporte v2: la palabra la pone una Insignia peligro.
        var badges = InsigniasConTexto(window, "Stock negativo");

        Assert.Single(badges, b => b.IsVisible);
    }

    // ---- Tareas: TareaListView, sitio DiasParaVencer (columna VENCE) ----

    [AvaloniaFact]
    public void Montar_TareaListViewConTareaVencida_MuestraBadgeVencidaSoloEnLaFilaVencida()
    {
        var vencida = TareaDe(1, "Con vencimiento", EstadoTarea.Pendiente, DateTime.UtcNow.Date.AddDays(-5));
        var alDia = TareaDe(2, "Al dia", EstadoTarea.Pendiente, DateTime.UtcNow.Date.AddDays(5));

        // La palabra la pone una Insignia peligro en la columna VENCE. La vista se monta con su ViewModel real: la grilla solo realiza las filas de lo que carga.
        var vm = new TareaListViewModel(new TareaServiceFake([vencida, alDia]), new SesionFake(RolUsuario.Admin),
            new NavigationRecorderFake(), new ConfirmacionServiceFake());
        var vista = new TareaListView { DataContext = vm };
        var window = MontarEnVentana(vista, height: 900);
        Dispatcher.UIThread.RunJobs();

        var insignias = window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.Insignia>()
            .Where(i => i.Texto == "Vencida" && ArbolVisual.EsVisibleEnArbol(i)).ToList();

        var visible = Assert.Single(insignias);
        Assert.Equal(StockApp.Presentation.Controls.V2.VarianteInsignia.Peligro, visible.Variante);
    }

    private static Tarea TareaDe(int id, string titulo, EstadoTarea estado, DateTime? fechaLimite) => new()
    {
        Id = id, Titulo = titulo, Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = fechaLimite,
    };

    // ---- Finanzas: LibroCajaView, saldo final ----

    private sealed class FinanzasVistasServiceFake : IFinanzasVistasService
    {
        private readonly decimal _saldoFinal;

        public FinanzasVistasServiceFake(decimal saldoFinal) => _saldoFinal = saldoFinal;

        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(Caja caja, int anio, int mes)
            => Task.FromResult(new LibroCajaMesDto(
                anio, mes, 0m, _saldoFinal,
                Array.Empty<MovimientoCajaDto>(), Array.Empty<TotalPorClaveDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(Caja caja, int anio)
            => Task.FromResult(new LibroCajaAnualDto(anio, Array.Empty<TotalMensualDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private sealed class ServicioGuardadoArchivoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarBytesAsync(
            Stream contenido, string nombreSugerido, CancellationToken ct = default,
            string? extension = null, string? tipoMime = null) => Task.FromResult(true);
    }

    private sealed class PdfExporterNoOpFake : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos, ResumenPdf? resumen = null)
            => Array.Empty<byte>();
    }

    private const string XamlLibroCaja = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=GestionMunicipal"
                Width="1100" Height="700">
            <vistas:LibroCajaView />
        </Window>
        """;

    private static Window MontarLibroCaja(decimal saldoFinal)
    {
        var vm = new LibroCajaViewModel(
            new FinanzasVistasServiceFake(saldoFinal),
            new ServicioGuardadoArchivoFake(),
            new ConfirmacionServiceFake(),
            new PdfExporterNoOpFake(),
            new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Admin));

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

        // Libro caja es v2: la palabra la pone una Insignia peligro.
        var insignias = window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.Insignia>()
            .Where(i => i.Texto == "Saldo negativo").ToList();

        var visible = Assert.Single(insignias, ArbolVisual.EsVisibleEnArbol);
        Assert.Equal(StockApp.Presentation.Controls.V2.VarianteInsignia.Peligro, visible.Variante);
    }

    [AvaloniaFact]
    public void Montar_LibroCajaViewConSaldoFinalPositivo_NoMuestraBadgeSaldoNegativo()
    {
        var window = MontarLibroCaja(saldoFinal: 100m);

        var insignias = window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.Insignia>()
            .Where(i => i.Texto == "Saldo negativo").ToList();

        Assert.NotEmpty(insignias);
        Assert.DoesNotContain(insignias, ArbolVisual.EsVisibleEnArbol);
    }

    // ---- Movimientos: MovimientoHistorialView, sitio StockNuevo (:165) ----

    [AvaloniaFact]
    public void Montar_MovimientoHistorialViewConStockNuevoNegativo_MuestraBadgeStockNegativoSoloEnLaFilaNegativa()
    {
        var vista = new MovimientoHistorialView();
        // Sin DataContext todo IsVisible enlazado queda visible (sin permiso, cargando y vacío a la vez, sobre la
        // grilla): con 500 de alto la grilla no tiene lugar para realizar filas.
        var window = MontarEnVentana(vista, height: 900);

        var grilla = vista.GetVisualDescendants().OfType<DataGrid>().Single();
        grilla.ItemsSource = new[]
        {
            MovimientoHistorialDe(1, stockNuevo: -5m),
            MovimientoHistorialDe(2, stockNuevo: 20m),
        };
        Dispatcher.UIThread.RunJobs();

        // Historial es v2: la palabra la pone una Insignia peligro, como en Productos.
        var insignias = window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.Insignia>()
            .Where(i => i.Texto == "Stock negativo").ToList();

        var visible = Assert.Single(insignias, i => i.IsVisible);
        Assert.Equal(StockApp.Presentation.Controls.V2.VarianteInsignia.Peligro, visible.Variante);
    }

    private static MovimientoHistorialDto MovimientoHistorialDe(int id, decimal stockNuevo) => new(
        MovimientoId: id, ProductoId: 1, ProductoNombre: "Tornillos", Tipo: TipoMovimiento.Entrada,
        Motivo: MotivoMovimiento.Compra, Cantidad: 10m, PrecioUnitario: 5m, StockAnterior: 0m,
        StockNuevo: stockNuevo, Comentario: null, Fecha: DateTime.UtcNow, UsuarioId: 1,
        UsuarioNombre: "admin");

    // ---- Finanzas: ControlPoaView, sitio Saldo (:43) ----

    [AvaloniaFact]
    public void Montar_ControlPoaViewConSaldoNegativo_MuestraBadgeSobreejecutadoSoloEnLaFilaNegativa()
    {
        var vista = new ControlPoaView();
        // Sin DataContext todo IsVisible enlazado queda visible (carga, vacío y sin conexión a la vez, sobre la grilla):
        // con 500 de alto la grilla no tiene lugar para realizar filas.
        var window = MontarEnVentana(vista, height: 900);

        var grilla = vista.GetVisualDescendants().OfType<DataGrid>().Single();
        grilla.ItemsSource = new[]
        {
            ControlPoaLineaDe(1, saldo: -1000m),
            ControlPoaLineaDe(2, saldo: 500m),
        };
        Dispatcher.UIThread.RunJobs();

        // Control POA es v2: la palabra la pone una Insignia peligro.
        var insignias = window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.Insignia>()
            .Where(i => i.Texto == "Sobreejecutado").ToList();

        var visible = Assert.Single(insignias, i => i.IsVisible);
        Assert.Equal(StockApp.Presentation.Controls.V2.VarianteInsignia.Peligro, visible.Variante);
    }

    private static ControlPoaLineaDto ControlPoaLineaDe(int id, decimal saldo) => new(
        LineaPoaId: id, Nombre: $"Línea {id}", Programa: "Programa", Ejercicio: 2026,
        Presupuesto: 10000m, Gastado: 10000m - saldo, Saldo: saldo, PorcentajeEjecucion: 100m,
        Sobregirada: saldo < 0m);

    // ---- Reportes: ValorizacionView, sitio StockActual (:59) ----

    [AvaloniaFact]
    public void Montar_ValorizacionViewConStockNegativo_MuestraBadgeStockNegativoSoloEnLaFilaNegativa()
    {
        var servicio = new V2.ReporteStockFake { Valorizacion = { ValorizacionItemDe(1, stockActual: -5m), ValorizacionItemDe(2, stockActual: 20m) } };
        var vm = new StockApp.Presentation.ViewModels.Reportes.ValorizacionViewModel(servicio, new V2.GuardadoSinUsoFake(),
            new ConfirmacionServiceFake(), new V2.PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin));
        var vista = new ValorizacionView { DataContext = vm };
        var window = MontarEnVentana(vista);
        Dispatcher.UIThread.RunJobs();

        // Reporte v2: la palabra la pone una Insignia peligro.
        var badges = InsigniasConTexto(window, "Stock negativo");

        Assert.Single(badges, b => b.IsVisible);
    }

    private static ValorizacionItemDto ValorizacionItemDe(int id, decimal stockActual) => new(
        ProductoId: id, Codigo: $"C{id}", Nombre: $"Producto {id}", Categoria: "Ferretería",
        StockActual: stockActual, PrecioCosto: 10m,
        ValorCosto: stockActual * 10m);

    // ---- Reportes: HistorialPorProductoView, sitio StockNuevo (:102) ----

    [AvaloniaFact]
    public void Montar_HistorialPorProductoViewConStockNuevoNegativo_MuestraBadgeStockNegativoSoloEnLaFilaNegativa()
    {
        var servicio = new V2.ReporteStockFake { Historial = { MovimientoHistorialDe(1, stockNuevo: -5m), MovimientoHistorialDe(2, stockNuevo: 20m) } };
        var vm = new StockApp.Presentation.ViewModels.Reportes.HistorialPorProductoViewModel(servicio, new V2.GuardadoSinUsoFake(),
            new ConfirmacionServiceFake(), new V2.ProductosBusquedaFake(), new V2.PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(), new SesionFake(RolUsuario.Admin))
        { ProductoId = 1 };
        var vista = new HistorialPorProductoView { DataContext = vm };
        var window = MontarEnVentana(vista);
        Dispatcher.UIThread.RunJobs();

        // Reporte v2: la palabra la pone una Insignia peligro.
        var badges = InsigniasConTexto(window, "Stock negativo");

        Assert.Single(badges, b => b.IsVisible);
    }
}
