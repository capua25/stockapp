using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Exportacion;
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

    private static (
        HistorialPorProductoViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<MovimientoHistorialDto>? items = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        servicioMock
            .Setup(s => s.ObtenerHistorialPorProductoAsync(
                It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(items ?? new List<MovimientoHistorialDto>());

        var vm = new HistorialPorProductoViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarCommand_LlamaObtenerHistorialPorProductoAsync_ConParametros()
    {
        var items = new List<MovimientoHistorialDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _, _) = Crear(items);

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
        var (vm, servicioMock, _, _, _) = Crear();
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
        var (vm, servicioMock, _, _, _) = Crear(items);
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
        var (vm, _, exporterMock, guardadoMock, _) = Crear(items);

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

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _, _) = Crear();

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
        var (vm, servicioMock, _, _, _) = Crear();

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
        var (vm, _, exporterMock, guardadoMock, confirmMock) = Crear(items);
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

        var vm = new HistorialPorProductoViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }
}
