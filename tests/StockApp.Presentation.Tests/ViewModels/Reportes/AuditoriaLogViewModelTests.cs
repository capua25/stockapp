using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class AuditoriaLogViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static AuditoriaItemDto CrearItem(int entidadId = 1)
        => new AuditoriaItemDto(
            Fecha: new DateTime(2026, 1, 15),
            NombreUsuario: "admin",
            Accion: AccionAuditada.CambioPrecio,
            Entidad: "Producto",
            EntidadId: entidadId,
            Detalle: "5 -> 8");

    private static UsuarioDto CrearUsuario(int id, string nombreUsuario = "usuario", bool activo = true)
        => new UsuarioDto(
            Id: id, NombreUsuario: nombreUsuario, NombreCompleto: null,
            Rol: RolUsuario.Operador, Activo: activo, FechaAlta: default);

    private static (
        AuditoriaLogViewModel vm,
        Mock<IAuditoriaQueryService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IUsuarioService> usuarioSvcMock)
        Crear(IReadOnlyList<AuditoriaItemDto>? items = null, IReadOnlyList<UsuarioDto>? usuarios = null)
    {
        var servicioMock = new Mock<IAuditoriaQueryService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var usuarioSvcMock = new Mock<IUsuarioService>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        servicioMock
            .Setup(s => s.ObtenerLogAsync(
                It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(items ?? new List<AuditoriaItemDto>());

        usuarioSvcMock
            .Setup(s => s.ListarAsync())
            .ReturnsAsync(usuarios ?? new List<UsuarioDto>());

        var vm = new AuditoriaLogViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object,
            usuarioSvcMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock, usuarioSvcMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarCommand_LlamaObtenerLogAsync_ConFiltros()
    {
        var items = new List<AuditoriaItemDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _, _, _) = Crear(items);

        var desde = new DateTime(2026, 1, 1);
        var hasta = new DateTime(2026, 1, 31);
        vm.UsuarioId = 9;
        vm.FechaDesde = desde;
        vm.FechaHasta = hasta;

        await vm.BuscarCommand.ExecuteAsync(null);

        // BUG DE HUSO HORARIO: desde/hasta vienen en hora LOCAL del CalendarDatePicker; el VM
        // debe convertirlas a UTC antes de delegar al servicio (que compara contra
        // LogAuditoria.Fecha, persistida en UTC). Offset calculado desde TimeZoneInfo.Local
        // para no acoplar el test a la TZ del entorno.
        var offsetDesde = TimeZoneInfo.Local.GetUtcOffset(desde);
        var offsetHasta = TimeZoneInfo.Local.GetUtcOffset(hasta);
        servicioMock.Verify(s => s.ObtenerLogAsync(9, desde - offsetDesde, hasta - offsetHasta), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    /// <summary>
    /// Reproduce el bug reportado por el usuario (Argentina, UTC-3): sin la conversión, un
    /// registro de auditoría de las 23:00 hora local caía fuera del filtro "hasta hoy".
    /// </summary>
    [Fact]
    public async Task BuscarCommand_ConFechaLocal_ConvierteAUtcAntesDeDelegarAlServicio()
    {
        var (vm, servicioMock, _, _, _, _) = Crear();
        var fechaLocal = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Unspecified);
        vm.FechaDesde = fechaLocal;

        await vm.BuscarCommand.ExecuteAsync(null);

        var offset = TimeZoneInfo.Local.GetUtcOffset(fechaLocal);
        servicioMock.Verify(s => s.ObtenerLogAsync(null, fechaLocal - offset, null), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_LlamaObtenerLogAsync_YPopulaItems()
    {
        var items = new List<AuditoriaItemDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _, _, _) = Crear(items);

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerLogAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    [Fact]
    public async Task ExportarCommand_LlamaExportarConItems()
    {
        var items = new List<AuditoriaItemDto> { CrearItem() };
        var (vm, _, exporterMock, guardadoMock, _, _) = Crear(items);

        var esperado = new[]
        {
            "Fecha", "NombreUsuario", "Accion", "Entidad", "EntidadId", "Detalle"
        };

        const string csvResultante = "csv-generado";
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<AuditoriaItemDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(csvResultante);

        await vm.BuscarCommand.ExecuteAsync(null);
        await vm.ExportarCommand.ExecuteAsync(null);

        exporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(c => c.SequenceEqual(esperado))),
            Times.Once);

        guardadoMock.Verify(g => g.GuardarTextoAsync(csvResultante, "auditoria.csv"), Times.Once);
    }

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _, _, _) = Crear();

        vm.FechaDesde = new DateTime(2026, 2, 1);
        vm.FechaHasta = new DateTime(2026, 1, 1);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerLogAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task BuscarCommand_ConRangoValido_LimpiaMensajeError()
    {
        var (vm, servicioMock, _, _, _, _) = Crear();

        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);

        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.True(string.IsNullOrEmpty(vm.MensajeError));
        servicioMock.Verify(s => s.ObtenerLogAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
    }

    // ── bugfix 2026-08-14: falla silenciosa al guardar el CSV ──────────────────

    [Fact]
    public async Task ExportarCommand_SiFallaGuardarTextoAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<AuditoriaItemDto> { CrearItem() };
        var (vm, _, exporterMock, guardadoMock, confirmMock, _) = Crear(items);
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<AuditoriaItemDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv-generado");
        guardadoMock
            .Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));

        await vm.BuscarCommand.ExecuteAsync(null);
        await vm.ExportarCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    // ── Bloque "ID imposible de completar" (2026-08-19): NumericUpDown → AutoCompleteBox ──
    // "Usuario ID" pedía una PK a mano sin mostrarse en ninguna vista de la app. A diferencia
    // de los dos casos de Producto (cientos de filas → búsqueda server-side), acá son ~14
    // usuarios que no crecen como el catálogo: se carga la lista completa
    // (IUsuarioService.ListarAsync, sin parámetros, igual que UsuariosAdminViewModel) y se usa
    // el filtrado NATIVO del control (FilterMode/ItemFilter), sin backend nuevo. Este filtro SÍ
    // admite "todos" (UsuarioId: int?, ObtenerLogAsync acepta null) — opción "Todos" como el
    // patrón OpcionProducto("Todos", null) de MovimientoHistorialViewModel.

    [Fact]
    public async Task InicializarAsync_PopulaOpcionTodosYUsuarios()
    {
        var usuarios = new List<UsuarioDto> { CrearUsuario(1, "ana"), CrearUsuario(2, "beto") };
        var (vm, _, _, _, _, usuarioSvcMock) = Crear(usuarios: usuarios);

        await vm.InicializarAsync();

        usuarioSvcMock.Verify(s => s.ListarAsync(), Times.Once);
        Assert.Equal(3, vm.Usuarios.Count);
        Assert.Equal("Todos", vm.Usuarios[0].Nombre);
        Assert.Null(vm.Usuarios[0].Valor);
        Assert.Equal("ana", vm.Usuarios[1].Nombre);
        Assert.Equal("beto", vm.Usuarios[2].Nombre);
    }

    [Fact]
    public async Task InicializarAsync_PreseleccionaOpcionTodos()
    {
        var (vm, _, _, _, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.NotNull(vm.UsuarioFiltroSeleccionado);
        Assert.Null(vm.UsuarioFiltroSeleccionado!.Valor);
        Assert.Null(vm.UsuarioId);
    }

    [Fact]
    public async Task InicializarAsync_TambienCargaElLog()
    {
        var items = new List<AuditoriaItemDto> { CrearItem(1) };
        var (vm, servicioMock, _, _, _, _) = Crear(items: items);

        await vm.InicializarAsync();

        servicioMock.Verify(s => s.ObtenerLogAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
        Assert.Single(vm.Items);
    }

    [Fact]
    public void UsuarioFiltroSeleccionado_AlAsignarUsuarioReal_DerivaUsuarioId()
    {
        var (vm, _, _, _, _, _) = Crear();
        var usuario = CrearUsuario(7, "cami");

        vm.UsuarioFiltroSeleccionado = new OpcionUsuario(usuario.NombreUsuario, usuario);

        Assert.Equal(7, vm.UsuarioId);
    }

    [Fact]
    public void UsuarioFiltroSeleccionado_AlSeleccionarTodos_UsuarioIdVuelveANull()
    {
        var (vm, _, _, _, _, _) = Crear();
        vm.UsuarioFiltroSeleccionado = new OpcionUsuario("cami", CrearUsuario(7, "cami"));

        vm.UsuarioFiltroSeleccionado = new OpcionUsuario("Todos", null);

        Assert.Null(vm.UsuarioId);
    }

    [Fact]
    public void UsuarioFiltroSeleccionado_AlAsignarNull_UsuarioIdVuelveANull()
    {
        var (vm, _, _, _, _, _) = Crear();
        vm.UsuarioFiltroSeleccionado = new OpcionUsuario("cami", CrearUsuario(7, "cami"));

        vm.UsuarioFiltroSeleccionado = null;

        Assert.Null(vm.UsuarioId);
    }
}
