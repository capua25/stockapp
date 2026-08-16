using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

public class MovimientoHistorialViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static MovimientoHistorialDto CrearDto(int id = 1, int productoId = 1)
        => new MovimientoHistorialDto(
            MovimientoId: id,
            ProductoId: productoId,
            ProductoNombre: "Azúcar",
            Tipo: TipoMovimiento.Entrada,
            Motivo: MotivoMovimiento.Compra,
            Cantidad: 10m,
            PrecioUnitario: 5m,
            StockAnterior: 0m,
            StockNuevo: 10m,
            Comentario: null,
            Fecha: DateTime.UtcNow,
            UsuarioId: 1,
            UsuarioNombre: "Admin");

    private static ProductoDto CrearProducto(int id, string nombre = "Producto", bool activo = true)
        => new ProductoDto(
            Id: id, Codigo: $"SKU{id}", CodigoBarras: null, Nombre: nombre, Descripcion: null,
            CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: 1,
            UnidadMedidaNombre: "Unidad", PrecioCosto: 0m, PrecioVenta: 0m, StockActual: 0m,
            StockMinimo: 0m, Activo: activo, FechaAlta: default);

    private static (
        MovimientoHistorialViewModel vm,
        Mock<IMovimientoStockService> svcMock,
        Mock<INavigationService> navMock,
        Mock<IProductoService> productoSvcMock,
        Mock<IConfirmacionService> confirmacionMock)
        Crear(
            IReadOnlyList<MovimientoHistorialDto>? items = null,
            IReadOnlyList<ProductoDto>? productos = null)
    {
        var svcMock = new Mock<IMovimientoStockService>();
        var navMock = new Mock<INavigationService>();
        var productoSvcMock = new Mock<IProductoService>();
        var confirmacionMock = new Mock<IConfirmacionService>();

        svcMock
            .Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(items ?? new List<MovimientoHistorialDto>());

        productoSvcMock
            .Setup(s => s.BuscarAsync(null, null, null))
            .ReturnsAsync(productos ?? new List<ProductoDto>());

        var vm = new MovimientoHistorialViewModel(
            svcMock.Object, navMock.Object, productoSvcMock.Object, confirmacionMock.Object);
        return (vm, svcMock, navMock, productoSvcMock, confirmacionMock);
    }

    // ── D4 tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1), CrearDto(2) };
        var (vm, svcMock, _, _, _) = Crear(lista);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Once);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task CargarAsync_SinResultados_ItemsVacio()
    {
        var (vm, _, _, _, _) = Crear(new List<MovimientoHistorialDto>());

        await vm.CargarAsync();

        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task BuscarAsync_ConFiltros_DelegaAlServiceConFiltrosConstruidos()
    {
        var (vm, svcMock, _, _, _) = Crear();
        vm.FiltroProductoId = 5;
        vm.FiltroTipo = TipoMovimiento.Salida;
        var fechaDesdeLocal = new DateTime(2026, 1, 1);
        var fechaHastaLocal = new DateTime(2026, 12, 31);
        vm.FechaDesde = fechaDesdeLocal;
        vm.FechaHasta = fechaHastaLocal;

        await vm.BuscarCommand.ExecuteAsync(null);

        // BUG DE HUSO HORARIO: FechaDesde/FechaHasta vienen en hora LOCAL del CalendarDatePicker;
        // el repo compara contra MovimientoStock.Fecha (persistida en UTC), así que el VM debe
        // convertir antes de delegar. Offset calculado desde TimeZoneInfo.Local, no hardcodeado,
        // para no acoplar el test a la TZ del entorno (America/Montevideo, UTC-3, en CI/dev).
        var offsetDesde = TimeZoneInfo.Local.GetUtcOffset(fechaDesdeLocal);
        var offsetHasta = TimeZoneInfo.Local.GetUtcOffset(fechaHastaLocal);
        svcMock.Verify(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f =>
            f.ProductoId == 5 &&
            f.Tipo == TipoMovimiento.Salida &&
            f.FechaDesde == fechaDesdeLocal - offsetDesde &&
            f.FechaHasta == fechaHastaLocal - offsetHasta)), Times.Once);
    }

    /// <summary>
    /// HM-HORARIO: reproduce el bug reportado por el usuario (Argentina, UTC-3) — un
    /// movimiento de las 23:00 hora local caía fuera del filtro "hasta hoy" porque
    /// FechaHasta se comparaba cruda contra Fecha (UTC) sin convertir.
    /// </summary>
    [Fact]
    public async Task BuscarAsync_ConFechaLocal_ConvierteAUtcAntesDeDelegarAlService()
    {
        var (vm, svcMock, _, _, _) = Crear();
        var fechaLocal = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Unspecified);
        vm.FechaDesde = fechaLocal;

        await vm.BuscarCommand.ExecuteAsync(null);

        var offset = TimeZoneInfo.Local.GetUtcOffset(fechaLocal);
        svcMock.Verify(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f =>
            f.FechaDesde == fechaLocal - offset)), Times.Once);
    }

    [Fact]
    public async Task BuscarAsync_SinFiltros_DelegaFiltroVacio()
    {
        var (vm, svcMock, _, _, _) = Crear();

        await vm.BuscarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f =>
            f.ProductoId == null &&
            f.Tipo == null &&
            f.FechaDesde == null &&
            f.FechaHasta == null)), Times.Once);
    }

    [Fact]
    public async Task RecalcularAsync_LlamaRecalcularStockAsync_YActualizaLista()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1) };
        var (vm, svcMock, _, _, _) = Crear(lista);
        vm.ProductoIdParaRecalcular = 1;

        svcMock
            .Setup(s => s.RecalcularStockAsync(1))
            .ReturnsAsync(new RecalculoResultadoDto(
                ProductoId: 1,
                StockAnterior: 10m,
                StockNuevo: 10m,
                TotalMovimientos: 1));

        await vm.RecalcularCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RecalcularStockAsync(1), Times.Once);
        // Después de recalcular, recarga el historial
        svcMock.Verify(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Once);
    }

    [Fact]
    public async Task RecalcularAsync_SinProductoSeleccionado_NoLlamaServicio()
    {
        var (vm, svcMock, _, _, _) = Crear();
        vm.ProductoIdParaRecalcular = null;

        await vm.RecalcularCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RecalcularStockAsync(It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// Feedback faltante (reporte de uso real): el usuario tipea un ProductoId a mano en un
    /// campo libre sin relación con el filtro activo de la grilla — si ese producto no está en
    /// el resultado filtrado (caso normal), CargarAsync no cambia una sola fila y el click queda
    /// sin ninguna señal. Mismo mecanismo que PanelPermisosViewModel.GuardarAsync:
    /// IConfirmacionService.InformarAsync.
    /// </summary>
    [Fact]
    public async Task RecalcularAsync_Exito_InformaConfirmacion()
    {
        var (vm, svcMock, _, _, confirmacionMock) = Crear();
        vm.ProductoIdParaRecalcular = 1;
        svcMock
            .Setup(s => s.RecalcularStockAsync(1))
            .ReturnsAsync(new RecalculoResultadoDto(
                ProductoId: 1,
                StockAnterior: 10m,
                StockNuevo: 12m,
                TotalMovimientos: 3));

        await vm.RecalcularCommand.ExecuteAsync(null);

        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// Sin try/catch, una excepción del servicio (ej. producto inexistente) escapaba de un
    /// AsyncRelayCommand sin observar y terminaba en crash.log en silencio, sin que el usuario
    /// se enterara. Mismo criterio que PanelPermisosViewModel.GuardarAsync: informa el mensaje
    /// de la excepción y NO deja que se propague fuera del comando.
    /// </summary>
    [Fact]
    public async Task RecalcularAsync_ServicioLanzaEntidadNoEncontrada_InformaErrorYNoPropaga()
    {
        var (vm, svcMock, _, _, confirmacionMock) = Crear();
        vm.ProductoIdParaRecalcular = 999;
        svcMock
            .Setup(s => s.RecalcularStockAsync(999))
            .ThrowsAsync(new EntidadNoEncontradaException("Producto 999 no encontrado."));

        var excepcion = await Record.ExceptionAsync(() => vm.RecalcularCommand.ExecuteAsync(null));

        Assert.Null(excepcion);
        confirmacionMock.Verify(c => c.InformarAsync("Producto 999 no encontrado."), Times.Once);
    }

    // ── InicializarAsync / filtro de producto y tipo ──────────────────────────

    [Fact]
    public async Task InicializarAsync_PopulaOpcionTodosYProductosActivos()
    {
        var productos = new List<ProductoDto>
        {
            CrearProducto(1, "Activo", activo: true),
            CrearProducto(2, "Inactivo", activo: false),
        };
        var (vm, _, _, productoSvcMock, _) = Crear(productos: productos);

        await vm.InicializarAsync();

        productoSvcMock.Verify(s => s.BuscarAsync(null, null, null), Times.Once);
        Assert.Equal(2, vm.Productos.Count);
        Assert.Equal("Todos", vm.Productos[0].Nombre);
        Assert.Null(vm.Productos[0].Valor);
        Assert.Equal("Activo", vm.Productos[1].Nombre);
    }

    [Fact]
    public async Task InicializarAsync_PreseleccionaOpcionTodos()
    {
        var (vm, _, _, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.NotNull(vm.ProductoFiltroSeleccionado);
        Assert.Null(vm.ProductoFiltroSeleccionado!.Valor);
        Assert.Null(vm.FiltroProductoId);
    }

    [Fact]
    public async Task InicializarAsync_TambienCargaHistorial()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1) };
        var (vm, svcMock, _, _, _) = Crear(items: lista);

        await vm.InicializarAsync();

        svcMock.Verify(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Once);
        Assert.Single(vm.Items);
    }

    /// <summary>
    /// Bugfix 2026-08-16 (misma familia que 323c007/1ab2cd8, PERO variante distinta):
    /// el combo de producto de esta pantalla es un FILTRO, no un campo obligatorio — a
    /// diferencia de Entrada/Salida e Ingreso por factura, GET /movimientos/historial
    /// (MovimientosEndpoints.cs) exige el MISMO permiso que ya gatea el sidebar
    /// (RegistrarMovimientos), sin relación con GestionarProductos. Por eso acá NO se toca
    /// el gate: si BuscarAsync (productos) devuelve 403, el historial completo debe seguir
    /// cargando igual — antes del fix, InicializarAsync llamaba BuscarAsync ANTES de
    /// CargarAsync, así que una excepción no atrapada abortaba toda la inicialización,
    /// incluida la carga del historial (que no necesita GestionarProductos).
    /// </summary>
    [Fact]
    public async Task InicializarAsync_ProductoServiceLanzaUnauthorized_CargaHistorialIgual()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1) };
        var (vm, svcMock, _, productoSvcMock, _) = Crear(items: lista);
        productoSvcMock
            .Setup(s => s.BuscarAsync(null, null, null))
            .ThrowsAsync(new UnauthorizedAccessException());

        var excepcion = await Record.ExceptionAsync(() => vm.InicializarAsync());

        Assert.Null(excepcion);
        svcMock.Verify(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Once);
        Assert.Single(vm.Items);
    }

    /// <summary>
    /// Complemento del test anterior: además de no romper la carga, oculta el combo de
    /// producto (IsVisible del filtro en la vista) para no dejar un ComboBox vacío sin
    /// explicación — mismo criterio de "ocultar, no reventar" que PuedeRegistrarPagos.
    /// </summary>
    [Fact]
    public async Task InicializarAsync_ProductoServiceLanzaUnauthorized_OcultaFiltroDeProducto()
    {
        var (vm, _, _, productoSvcMock, _) = Crear();
        productoSvcMock
            .Setup(s => s.BuscarAsync(null, null, null))
            .ThrowsAsync(new UnauthorizedAccessException());

        await vm.InicializarAsync();

        Assert.False(vm.PuedeFiltrarPorProducto);
    }

    [Fact]
    public async Task InicializarAsync_ProductoServiceOk_PuedeFiltrarPorProductoQuedaEnTrue()
    {
        var (vm, _, _, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.True(vm.PuedeFiltrarPorProducto);
    }

    [Fact]
    public void ProductoFiltroSeleccionado_AlAsignarProductoReal_DerivaFiltroProductoId()
    {
        var (vm, _, _, _, _) = Crear();
        var producto = CrearProducto(7, "Azúcar");

        vm.ProductoFiltroSeleccionado = new OpcionProducto(producto.Nombre, producto);

        Assert.Equal(7, vm.FiltroProductoId);
    }

    [Fact]
    public void ProductoFiltroSeleccionado_AlSeleccionarTodos_FiltroProductoIdVuelveANull()
    {
        var (vm, _, _, _, _) = Crear();
        vm.ProductoFiltroSeleccionado = new OpcionProducto("Azúcar", CrearProducto(7));

        vm.ProductoFiltroSeleccionado = new OpcionProducto("Todos", null);

        Assert.Null(vm.FiltroProductoId);
    }

    [Fact]
    public void ProductoFiltroSeleccionado_AlAsignarNull_FiltroProductoIdVuelveANull()
    {
        var (vm, _, _, _, _) = Crear();
        vm.ProductoFiltroSeleccionado = new OpcionProducto("Azúcar", CrearProducto(7));

        vm.ProductoFiltroSeleccionado = null;

        Assert.Null(vm.FiltroProductoId);
    }

    [Fact]
    public void TipoFiltroSeleccionado_PorDefecto_EsTodos()
    {
        var (vm, _, _, _, _) = Crear();

        Assert.Null(vm.TipoFiltroSeleccionado!.Valor);
        Assert.Null(vm.FiltroTipo);
    }

    [Fact]
    public void TipoFiltroSeleccionado_AlAsignarSalida_DerivaFiltroTipo()
    {
        var (vm, _, _, _, _) = Crear();
        var opcionSalida = vm.TiposDisponibles.Single(o => o.Valor == TipoMovimiento.Salida);

        vm.TipoFiltroSeleccionado = opcionSalida;

        Assert.Equal(TipoMovimiento.Salida, vm.FiltroTipo);
    }

    // ── ItemsView: fix de ordenamiento por click en encabezados (Avalonia 12, regresión #21129) ──

    [Fact]
    public async Task ItemsView_EsOrdenable()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1), CrearDto(2) };
        var (vm, _, _, _, _) = Crear(lista);

        await vm.CargarAsync();

        Assert.NotNull(vm.ItemsView);
        Assert.IsType<DataGridCollectionView>(vm.ItemsView);
        Assert.True(vm.ItemsView.CanSort);
    }

    [Fact]
    public async Task ItemsView_AlAplicarSortDescription_OrdenaLosItems()
    {
        var desordenados = new List<MovimientoHistorialDto>
        {
            CrearDto(1) with { Fecha = new DateTime(2026, 6, 15) },
            CrearDto(2) with { Fecha = new DateTime(2026, 1, 10) },
            CrearDto(3) with { Fecha = new DateTime(2026, 3, 20) },
        };
        var (vm, _, _, _, _) = Crear(desordenados);
        await vm.CargarAsync();

        vm.ItemsView.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(MovimientoHistorialDto.Fecha), ListSortDirection.Ascending));

        var ordenados = vm.ItemsView.Cast<MovimientoHistorialDto>().ToList();
        Assert.Equal(3, ordenados.Count);
        Assert.Equal(new DateTime(2026, 1, 10), ordenados[0].Fecha);
        Assert.Equal(new DateTime(2026, 3, 20), ordenados[1].Fecha);
        Assert.Equal(new DateTime(2026, 6, 15), ordenados[2].Fecha);
    }

    [Fact]
    public async Task Items_TrasRecarga_SeReflejanEnItemsView()
    {
        var (vm, svcMock, _, _, _) = Crear(new List<MovimientoHistorialDto> { CrearDto(1) });
        await vm.CargarAsync();
        Assert.Single(vm.ItemsView.Cast<MovimientoHistorialDto>());

        var nuevaLista = new List<MovimientoHistorialDto> { CrearDto(10), CrearDto(11), CrearDto(12) };
        svcMock
            .Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(nuevaLista);

        await vm.CargarAsync();

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal(3, vm.ItemsView.Cast<MovimientoHistorialDto>().Count());
    }
}
