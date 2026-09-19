using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class HistorialPorProductoViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static MovimientoHistorialDto CrearItem(int id = 1)
        => new MovimientoHistorialDto(
            MovimientoId: id,
            ProductoId: 7,
            ProductoNombre: "Azúcar",
            Tipo: TipoMovimiento.Entrada,
            Motivo: MotivoMovimiento.Compra,
            Cantidad: 10m,
            PrecioUnitario: 5m,
            StockAnterior: 0m,
            StockNuevo: 10m,
            Comentario: "alta inicial",
            Fecha: new DateTime(2026, 1, 15),
            UsuarioId: 3,
            UsuarioNombre: "Admin");

    private static ProductoDto CrearProducto(int id, string nombre = "Producto")
        => new ProductoDto(
            Id: id, Codigo: $"SKU{id}", CodigoBarras: null, Nombre: nombre, Descripcion: null,
            CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: 1,
            UnidadMedidaNombre: "Unidad", PrecioCosto: 0m, StockActual: 0m,
            StockMinimo: 0m, Activo: true, FechaAlta: default);

    private static (
        HistorialPorProductoViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IProductoService> productoSvcMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(IReadOnlyList<MovimientoHistorialDto>? items = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var productoSvcMock = new Mock<IProductoService>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        servicioMock
            .Setup(s => s.ObtenerHistorialPorProductoAsync(
                It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(items ?? new List<MovimientoHistorialDto>());

        var vm = new HistorialPorProductoViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object,
            productoSvcMock.Object, pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock, productoSvcMock, pdfExporterMock, aperturaMock, sessionMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarCommand_LlamaObtenerHistorialPorProductoAsync_ConParametros()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _, _, _, _, _, _) = Crear(items);

        var desde = new DateTime(2026, 1, 1);
        var hasta = new DateTime(2026, 1, 31);
        vm.ProductoId = 7;
        vm.FechaDesde = desde;
        vm.FechaHasta = hasta;

        await vm.BuscarCommand.ExecuteAsync(null);

        // BUG DE HUSO HORARIO: desde/hasta vienen en hora LOCAL del CalendarDatePicker; el VM
        // debe convertirlas a UTC antes de delegar al servicio (que compara contra
        // MovimientoStock.Fecha, persistida en UTC). Offset calculado desde TimeZoneInfo.Local
        // para no acoplar el test a la TZ del entorno.
        var offsetDesde = TimeZoneInfo.Local.GetUtcOffset(desde);
        var offsetHasta = TimeZoneInfo.Local.GetUtcOffset(hasta);
        servicioMock.Verify(s => s.ObtenerHistorialPorProductoAsync(
            7, desde - offsetDesde, hasta - offsetHasta), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    /// <summary>
    /// Reproduce el bug reportado por el usuario (Argentina, UTC-3): sin la conversión, un
    /// movimiento de las 23:00 hora local caía fuera del filtro "hasta hoy".
    /// </summary>
    [Fact]
    public async Task BuscarCommand_ConFechaLocal_ConvierteAUtcAntesDeDelegarAlServicio()
    {
        var (vm, servicioMock, _, _, _, _, _, _, _) = Crear();
        var fechaLocal = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Unspecified);
        vm.ProductoId = 7;
        vm.FechaDesde = fechaLocal;

        await vm.BuscarCommand.ExecuteAsync(null);

        var offset = TimeZoneInfo.Local.GetUtcOffset(fechaLocal);
        servicioMock.Verify(s => s.ObtenerHistorialPorProductoAsync(
            7, fechaLocal - offset, null), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_LlamaObtenerHistorialPorProductoAsync_YPopulaItems()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _, _, _, _, _, _) = Crear(items);
        vm.ProductoId = 7;

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerHistorialPorProductoAsync(
            7, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    [Fact]
    public async Task ExportarCommand_LlamaExportarConItems()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem() };
        var (vm, _, exporterMock, guardadoMock, _, _, _, _, _) = Crear(items);

        var esperado = new[]
        {
            "MovimientoId", "ProductoId", "ProductoNombre", "Tipo", "Motivo",
            "Cantidad", "PrecioUnitario", "StockAnterior", "StockNuevo",
            "Comentario", "Fecha", "UsuarioId"
        };

        const string csvResultante = "csv-generado";
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MovimientoHistorialDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(csvResultante);

        await vm.BuscarCommand.ExecuteAsync(null);
        await vm.ExportarCommand.ExecuteAsync(null);

        exporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(c => c.SequenceEqual(esperado))),
            Times.Once);

        guardadoMock.Verify(g => g.GuardarTextoAsync(csvResultante, "historial-producto.csv"), Times.Once);
    }

    // ── export PDF (spec 2026-09-18) ────────────────────────────────────────

    [Fact]
    public async Task ExportarPdfCommand_UsaElOrdenDeColumnasDeLaGrilla_NoElDelCsv()
    {
        // Literal, NO HistorialPorProductoViewModel.ColumnasPdf: comparar la constante contra sí
        // misma sería tautológico. El orden es el de la GRILLA (8 columnas), no el del CSV
        // (ColumnOrder, 12 columnas con IDs internos): dispara A4 apaisado (>6 columnas).
        var esperado = new[]
        {
            "Fecha", "Tipo", "Motivo", "Cantidad", "PrecioUnitario", "StockAnterior", "StockNuevo", "Comentario",
        };

        var items = new List<MovimientoHistorialDto> { CrearItem() };
        var (vm, _, _, guardadoMock, _, _, pdfExporterMock, _, _) = Crear(items);
        vm.ProductoId = 7;
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MovimientoHistorialDto>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "historial-producto.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(esperado)),
            It.IsAny<MetadatosDocumento>()),
            Times.Once);
        Assert.Equal(8, HistorialPorProductoViewModel.ColumnasPdf.Count);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, _, _, _, pdfExporterMock, _, _) = Crear(new List<MovimientoHistorialDto>());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoHistorialDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMuchasFilas_PreguntaAntesDeExportar()
    {
        var items = Enumerable.Range(1, 600).Select(CrearItem).ToList();
        var (vm, _, _, guardadoMock, confirmMock, _, pdfExporterMock, _, _) = Crear(items);
        vm.ProductoId = 7;
        await vm.CargarAsync();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoHistorialDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SiFallaGuardarBytesAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem() };
        var (vm, _, _, guardadoMock, confirmMock, _, pdfExporterMock, _, _) = Crear(items);
        vm.ProductoId = 7;
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MovimientoHistorialDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoExitoso_OfreceAbrirElArchivo()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem() };
        var (vm, _, _, guardadoMock, confirmMock, _, pdfExporterMock, aperturaMock, _) = Crear(items);
        vm.ProductoId = 7;
        await vm.CargarAsync();
        var pdfBytes = new byte[] { 9, 9 };
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MovimientoHistorialDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(pdfBytes);
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "historial-producto.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync("PDF guardado. ¿Desea abrirlo ahora?"), Times.Once);
        aperturaMock.Verify(a => a.AbrirAsync("historial-producto.pdf", pdfBytes), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoCancelado_NoOfreceAbrir()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem() };
        var (vm, _, _, guardadoMock, confirmMock, _, pdfExporterMock, aperturaMock, _) = Crear(items);
        vm.ProductoId = 7;
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MovimientoHistorialDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "historial-producto.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_PasaMetadatosConTituloFiltrosYUsuarioEmisor()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem() };
        var (vm, _, _, guardadoMock, _, _, pdfExporterMock, _, sessionMock) = Crear(items);
        vm.ProductoSeleccionado = CrearProducto(7, "Azúcar");
        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);
        await vm.CargarAsync();
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "jperez", RolUsuario.Admin, "Juan Pérez"));
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MovimientoHistorialDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "historial-producto.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoHistorialDto>>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.Is<MetadatosDocumento>(m =>
                m.Titulo == "Historial por producto" &&
                m.DescripcionFiltros == "Producto: SKU7 - Azúcar. Período: 01/01/2026 a 31/01/2026." &&
                m.UsuarioEmisor == "Juan Pérez")),
            Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinFiltros_DescripcionDiceSinProductoYTodoElHistorico()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem() };
        var (vm, _, _, guardadoMock, _, _, pdfExporterMock, _, _) = Crear(items);
        vm.ProductoId = 7;
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MovimientoHistorialDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "historial-producto.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoHistorialDto>>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.Is<MetadatosDocumento>(m => m.DescripcionFiltros == "Producto: (sin producto). Período: Todo el histórico.")),
            Times.Once);
    }

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _, _, _, _, _, _) = Crear();

        vm.FechaDesde = new DateTime(2026, 2, 1);
        vm.FechaHasta = new DateTime(2026, 1, 1);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerHistorialPorProductoAsync(
            It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task BuscarCommand_ConRangoValido_LimpiaMensajeError()
    {
        var (vm, servicioMock, _, _, _, _, _, _, _) = Crear();

        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);

        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.True(string.IsNullOrEmpty(vm.MensajeError));
        servicioMock.Verify(s => s.ObtenerHistorialPorProductoAsync(
            It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
    }

    // ── bugfix 2026-08-14: falla silenciosa al guardar el CSV ──────────────────

    [Fact]
    public async Task ExportarCommand_SiFallaGuardarTextoAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem() };
        var (vm, _, exporterMock, guardadoMock, confirmMock, _, _, _, _) = Crear(items);
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MovimientoHistorialDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv-generado");
        guardadoMock
            .Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));

        await vm.BuscarCommand.ExecuteAsync(null);
        await vm.ExportarCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    // ── bugfix 2026-08-16 (auditoría, fix bug de coherencia de permisos): red de contención ──
    // CargarAsync se dispara fire-and-forget desde DataContextChanged
    // (HistorialPorProductoView.axaml.cs). Antes del fix del gate (endurecido a VerReportes +
    // RegistrarMovimientos en ShellMainViewModel), un Operador con VerReportes pero sin
    // RegistrarMovimientos llegaba a esta pantalla y cada búsqueda le tiraba 403 sin atrapar --
    // escalaba al dispatcher global (App.axaml.cs) y mostraba el genérico "Ocurrió un error
    // inesperado" duplicando el aviso de "Tus permisos cambiaron..." que ya dispara
    // AuthTokenHandler. Mismo criterio que GastosViewModel.CargarAsync: catch silencioso.
    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var servicioMock = new Mock<IReporteStockService>();
        servicioMock
            .Setup(s => s.ObtenerHistorialPorProductoAsync(
                It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var productoSvcMock = new Mock<IProductoService>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var sessionMock = new Mock<ICurrentSession>();

        var vm = new HistorialPorProductoViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object,
            productoSvcMock.Object, pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }

    // ── Bloque "ID imposible de completar" (2026-08-19): NumericUpDown → AutoCompleteBox ──
    // "Producto ID" era el ÚNICO filtro de esta pantalla y pedía una PK a mano sin mostrarse en
    // ninguna vista de la app — el peor caso de los tres, la vista entera era inusable. Se
    // reemplaza por búsqueda server-side (IProductoService.BuscarPorTextoAsync). "Historial de
    // UN producto" no admite "todos", así que no hay opción "Todos" acá.

    [Fact]
    public void BuscarCommand_CanExecute_SinProductoSeleccionado_EsFalse()
    {
        var (vm, _, _, _, _, _, _, _, _) = Crear();

        Assert.False(vm.BuscarCommand.CanExecute(null));
    }

    [Fact]
    public void BuscarCommand_CanExecute_ConProductoSeleccionado_EsTrue()
    {
        var (vm, _, _, _, _, _, _, _, _) = Crear();

        vm.ProductoSeleccionado = CrearProducto(7, "Azúcar");

        Assert.True(vm.BuscarCommand.CanExecute(null));
    }

    [Fact]
    public void ProductoSeleccionado_AlAsignarProducto_DerivaProductoId()
    {
        var (vm, _, _, _, _, _, _, _, _) = Crear();

        vm.ProductoSeleccionado = CrearProducto(7, "Azúcar");

        Assert.Equal(7, vm.ProductoId);
    }

    [Fact]
    public void ProductoSeleccionado_AlAsignarNull_ProductoIdVuelveACero()
    {
        var (vm, _, _, _, _, _, _, _, _) = Crear();
        vm.ProductoSeleccionado = CrearProducto(7, "Azúcar");

        vm.ProductoSeleccionado = null;

        Assert.Equal(0, vm.ProductoId);
    }

    [Fact]
    public async Task BuscarProductosAsync_DelegaEnProductoServiceBuscarPorTextoAsync()
    {
        var productos = new List<ProductoDto> { CrearProducto(1, "Azúcar") };
        var (vm, _, _, _, _, productoSvcMock, _, _, _) = Crear();
        productoSvcMock
            .Setup(s => s.BuscarPorTextoAsync("azu"))
            .ReturnsAsync(productos);

        var resultado = await vm.BuscarProductosAsync("azu", CancellationToken.None);

        Assert.Single(resultado);
        productoSvcMock.Verify(s => s.BuscarPorTextoAsync("azu"), Times.Once);
    }
}
