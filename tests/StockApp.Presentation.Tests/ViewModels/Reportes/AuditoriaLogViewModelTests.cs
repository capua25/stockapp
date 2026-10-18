using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Presentation.Navigation;
using StockApp.ApiClient;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
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
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IUsuarioService> usuarioSvcMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(IReadOnlyList<AuditoriaItemDto>? items = null, IReadOnlyList<UsuarioDto>? usuarios = null)
    {
        var servicioMock = new Mock<IAuditoriaQueryService>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var usuarioSvcMock = new Mock<IUsuarioService>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        servicioMock
            .Setup(s => s.ObtenerLogAsync(
                It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(items ?? new List<AuditoriaItemDto>());

        usuarioSvcMock
            .Setup(s => s.ListarAsync())
            .ReturnsAsync(usuarios ?? new List<UsuarioDto>());

        var vm = new AuditoriaLogViewModel(
            servicioMock.Object, guardadoMock.Object, confirmMock.Object,
            usuarioSvcMock.Object, pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);
        return (vm, servicioMock, guardadoMock, confirmMock, usuarioSvcMock, pdfExporterMock, aperturaMock, sessionMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarCommand_LlamaObtenerLogAsync_ConFiltros()
    {
        var items = new List<AuditoriaItemDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _, _, _, _, _) = Crear(items);

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
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();
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
        var (vm, servicioMock, _, _, _, _, _, _) = Crear(items);

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerLogAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    // ── export PDF (spec 2026-09-18) ────────────────────────────────────────

    [Fact]
    public async Task ExportarPdfCommand_LlamaAlExportadorConLasColumnasDeLaGrilla()
    {
        // Literal, NO AuditoriaLogViewModel.ColumnasPdf: comparar la constante contra sí misma
        // sería tautológico. Son 6 columnas.
        var esperado = new[]
        {
            new ColumnaPdf("Fecha", "Fecha"),
            new ColumnaPdf("NombreUsuario", "Usuario"),
            new ColumnaPdf("Accion", "Acción"),
            new ColumnaPdf("Entidad", "Entidad"),
            new ColumnaPdf("EntidadId", "Entidad ID"),
            new ColumnaPdf("Detalle", "Detalle"),
        };

        var items = new List<AuditoriaItemDto> { CrearItem() };
        var (vm, _, guardadoMock, _, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<FilaAuditoriaPdf>>(),
                It.IsAny<IReadOnlyList<ColumnaPdf>>(),
                It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "auditoria.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.Is<IEnumerable<FilaAuditoriaPdf>>(filas => filas.Count() == vm.Items.Count),
            It.Is<IReadOnlyList<ColumnaPdf>>(cols => cols.SequenceEqual(esperado)),
            It.IsAny<MetadatosDocumento>()),
            Times.Once);
        guardadoMock.Verify(g => g.GuardarBytesAsync(
            It.IsAny<Stream>(), "auditoria.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, _, _, pdfExporterMock, _, _) = Crear(new List<AuditoriaItemDto>());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaAuditoriaPdf>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMuchasFilas_PreguntaAntesDeExportar()
    {
        var items = Enumerable.Range(1, 600).Select(i => CrearItem(i)).ToList();
        var (vm, _, guardadoMock, confirmMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaAuditoriaPdf>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SiFallaGuardarBytesAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<AuditoriaItemDto> { CrearItem() };
        var (vm, _, guardadoMock, confirmMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<FilaAuditoriaPdf>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
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
        var items = new List<AuditoriaItemDto> { CrearItem() };
        var (vm, _, guardadoMock, confirmMock, _, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        var pdfBytes = new byte[] { 9, 9 };
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<FilaAuditoriaPdf>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(pdfBytes);
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "auditoria.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync("PDF guardado. ¿Desea abrirlo ahora?"), Times.Once);
        aperturaMock.Verify(a => a.AbrirAsync("auditoria.pdf", pdfBytes), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoCancelado_NoOfreceAbrir()
    {
        var items = new List<AuditoriaItemDto> { CrearItem() };
        var (vm, _, guardadoMock, confirmMock, _, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<FilaAuditoriaPdf>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "auditoria.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_PasaMetadatosConTituloFiltrosYUsuarioEmisor()
    {
        var items = new List<AuditoriaItemDto> { CrearItem() };
        var (vm, _, guardadoMock, _, _, pdfExporterMock, _, sessionMock) = Crear(items);
        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);
        vm.UsuarioFiltroSeleccionado = new OpcionUsuario("ana", CrearUsuario(3, "ana"));
        await vm.BuscarCommand.ExecuteAsync(null);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "jperez", RolUsuario.Admin, "Juan Pérez"));
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<FilaAuditoriaPdf>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "auditoria.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaAuditoriaPdf>>(),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m =>
                m.Titulo == "Log de auditoría" &&
                m.DescripcionFiltros == "Usuario: ana. Período: 01/01/2026 a 31/01/2026." &&
                m.UsuarioEmisor == "Juan Pérez")),
            Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinFiltros_DescripcionDiceTodosYTodoElHistorico()
    {
        var items = new List<AuditoriaItemDto> { CrearItem() };
        var (vm, _, guardadoMock, _, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<FilaAuditoriaPdf>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "auditoria.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaAuditoriaPdf>>(),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m => m.DescripcionFiltros == "Usuario: Todos. Período: Todo el histórico.")),
            Times.Once);
    }

    /// <summary>
    /// Caso extremo de la regla de wrap (Tarea 5): Detalle es texto libre del log de auditoría
    /// sin tope de largo. PlantillaTabular garantiza wrap sin truncar -- este test verifica que
    /// el ViewModel no recorta el contenido antes de mandarlo al exportador, porque en un
    /// documento que se archiva, truncar el detalle es archivar media verdad.
    /// </summary>
    [Fact]
    public async Task ExportarPdfCommand_ConDetalleMuyLargo_LoExportaCompleto()
    {
        var detalleLargo = string.Join(" ", Enumerable.Range(1, 50).Select(i => $"campo{i}=valor{i}"));
        var items = new List<AuditoriaItemDto>
        {
            new(DateTime.UtcNow, "admin", AccionAuditada.CambioPrecio, "Producto", 1, detalleLargo),
        };
        var (vm, _, guardadoMock, _, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaAuditoriaPdf>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "auditoria.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        // La columna Detalle se afirma por su nombre de propiedad literal, NO contra
        // AuditoriaLogViewModel.ColumnasPdf: comparar la constante contra sí misma sería
        // tautológico -- mutar su valor dejaría este assert igual de verde.
        pdfExporterMock.Verify(e => e.Exportar(
            It.Is<IEnumerable<FilaAuditoriaPdf>>(coleccion => coleccion.Single().Detalle == detalleLargo),
            It.Is<IReadOnlyList<ColumnaPdf>>(cols => cols.Any(c => c.Propiedad == "Detalle")),
            It.IsAny<MetadatosDocumento>()), Times.Once);
    }

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();

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
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();

        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);

        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.True(string.IsNullOrEmpty(vm.MensajeError));
        servicioMock.Verify(s => s.ObtenerLogAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
    }

    // ── Bloque "ID imposible de completar" (2026-08-19): NumericUpDown → AutoCompleteBox ──
    // "Usuario ID" pedía una PK a mano sin mostrarse en ninguna vista de la app. A diferencia
    // de los dos casos de Producto (cientos de filas → búsqueda server-side), acá son ~14
    // usuarios que no crecen como el catálogo: se carga la lista completa
    // (IUsuarioService.ListarAsync, sin parámetros, igual que UsuariosAdminViewModel) y se usa
    // el filtrado NATIVO del control (FilterMode/ItemFilter), sin backend nuevo. Este filtro SÍ
    // admite "todos" (UsuarioId: int?, ObtenerLogAsync acepta null) — opción "Todos" explícita
    // vía OpcionUsuario("Todos", null) (el filtro de producto de MovimientoHistorialViewModel
    // ya NO tiene un wrapper equivalente desde el bugfix 2026-08-19: se migró a búsqueda
    // server-side, donde "sin selección" ya es "Todos" de forma natural).

    [Fact]
    public async Task InicializarAsync_PopulaOpcionTodosYUsuarios()
    {
        var usuarios = new List<UsuarioDto> { CrearUsuario(1, "ana"), CrearUsuario(2, "beto") };
        var (vm, _, _, _, usuarioSvcMock, _, _, _) = Crear(usuarios: usuarios);

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
        var (vm, _, _, _, _, _, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.NotNull(vm.UsuarioFiltroSeleccionado);
        Assert.Null(vm.UsuarioFiltroSeleccionado!.Valor);
        Assert.Null(vm.UsuarioId);
    }

    [Fact]
    public async Task InicializarAsync_TambienCargaElLog()
    {
        var items = new List<AuditoriaItemDto> { CrearItem(1) };
        var (vm, servicioMock, _, _, _, _, _, _) = Crear(items: items);

        await vm.InicializarAsync();

        servicioMock.Verify(s => s.ObtenerLogAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
        Assert.Single(vm.Items);
    }

    // ── bugfix "pantalla muda ante un 403": InicializarAsync no debe escalar un 403/401 (ni
    // desde ListarAsync de usuarios ni desde ObtenerLogAsync), y debe dejar un estado bindeable
    // para que la vista muestre EstadoVacio. ──
    [Fact]
    public async Task InicializarAsync_SiListarUsuariosLanzaUnauthorized_NoPropagaYDejaSinPermiso()
    {
        var (vm, _, _, _, usuarioSvcMock, _, _, _) = Crear();
        usuarioSvcMock.Setup(s => s.ListarAsync()).ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.InicializarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeSinPermiso));
    }

    [Fact]
    public void UsuarioFiltroSeleccionado_AlAsignarUsuarioReal_DerivaUsuarioId()
    {
        var (vm, _, _, _, _, _, _, _) = Crear();
        var usuario = CrearUsuario(7, "cami");

        vm.UsuarioFiltroSeleccionado = new OpcionUsuario(usuario.NombreUsuario, usuario);

        Assert.Equal(7, vm.UsuarioId);
    }

    [Fact]
    public void UsuarioFiltroSeleccionado_AlSeleccionarTodos_UsuarioIdVuelveANull()
    {
        var (vm, _, _, _, _, _, _, _) = Crear();
        vm.UsuarioFiltroSeleccionado = new OpcionUsuario("cami", CrearUsuario(7, "cami"));

        vm.UsuarioFiltroSeleccionado = new OpcionUsuario("Todos", null);

        Assert.Null(vm.UsuarioId);
    }

    [Fact]
    public void UsuarioFiltroSeleccionado_AlAsignarNull_UsuarioIdVuelveANull()
    {
        var (vm, _, _, _, _, _, _, _) = Crear();
        vm.UsuarioFiltroSeleccionado = new OpcionUsuario("cami", CrearUsuario(7, "cami"));

        vm.UsuarioFiltroSeleccionado = null;

        Assert.Null(vm.UsuarioId);
    }

    // ── Fase 6: estados v2, consulta vigente, PDF de lo cargado y vuelta al hub ─────────────

    private static (AuditoriaLogViewModel vm, Mock<IAuditoriaQueryService> servicio, Mock<IPdfExporter> pdf, Mock<INavigationService> nav)
        Armar(bool conNavegacion = true, Mock<IUsuarioService>? usuarios = null)
    {
        var servicio = new Mock<IAuditoriaQueryService>();
        var pdf = new Mock<IPdfExporter>();
        var nav = new Mock<INavigationService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));
        servicio.Setup(s => s.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<AuditoriaItemDto> { CrearItem(1) });
        if (usuarios is null)
        {
            usuarios = new Mock<IUsuarioService>();
            usuarios.Setup(u => u.ListarAsync()).ReturnsAsync(new List<UsuarioDto> { CrearUsuario(2, "mlopez") });
        }
        var vm = new AuditoriaLogViewModel(
            servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(), usuarios.Object,
            pdf.Object, Mock.Of<IServicioAperturaArchivo>(), session.Object, conNavegacion ? nav.Object : null);
        return (vm, servicio, pdf, nav);
    }

    private static void PonerFalla(Mock<IAuditoriaQueryService> servicio, Exception ex)
        => servicio.Setup(s => s.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ThrowsAsync(ex);

    [Fact]
    public async Task SinConexion_MarcaSinConexion_VaciaLasFilas_YNoPropaga()
    {
        var (vm, servicio, _, _) = Armar();
        await vm.CargarAsync();
        Assert.Single(vm.Items);
        PonerFalla(servicio, new ServidorNoDisponibleException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Un403_MarcaSinPermiso_YNoPropaga()
    {
        var (vm, servicio, _, _) = Armar();
        PonerFalla(servicio, new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
    }

    [Fact]
    public async Task ExportarPdf_SinFilas_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Armar();

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
        await vm.CargarAsync();
        vm.Items = new List<AuditoriaItemDto>();
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportarPdf_ConFilas_SeHabilita_YSeAvisaElCambio()
    {
        var (vm, _, _, _) = Armar();
        var avisos = 0;
        vm.ExportarPdfCommand.CanExecuteChanged += (_, _) => avisos++;
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));

        await vm.CargarAsync();

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        Assert.True(avisos > 0);
    }

    [Fact]
    public async Task ExportarPdf_TrasUn403_EstaDeshabilitado()
    {
        var (vm, servicio, _, _) = Armar();
        await vm.CargarAsync();
        PonerFalla(servicio, new UnauthorizedAccessException());
        await vm.CargarAsync();

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public void VolverCommand_NavegaAlHub()
    {
        var (vm, _, _, nav) = Armar();

        vm.VolverCommand.Execute(null);

        nav.Verify(n => n.Navegar<ReportesViewModel>(), Times.Once);
    }

    [Fact]
    public void VolverCommand_SinNavegacion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Armar(conNavegacion: false);

        Assert.False(vm.VolverCommand.CanExecute(null));
    }

    [Fact]
    public void TextosDelVacio_SonLosAprobados()
    {
        var (vm, _, _, _) = Armar();

        Assert.Equal("No hay registros para estos filtros", vm.TituloVacio);
        Assert.Equal("Probá con otro usuario o período.", vm.MensajeVacio);
    }

    [Fact]
    public async Task Un403EnBuscar_MarcaSinPermiso_YNoPropaga()
    {
        var (vm, servicio, _, _) = Armar();
        await vm.InicializarAsync();
        PonerFalla(servicio, new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.BuscarCommand.ExecuteAsync(null));

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
    }

    [Fact]
    public async Task SinConexionAlCargarUsuarios_Reintentar_CargaUsuariosYLog()
    {
        var usuarios = new Mock<IUsuarioService>();
        usuarios.Setup(u => u.ListarAsync()).ThrowsAsync(new ServidorNoDisponibleException());
        var (vm, servicio, _, _) = Armar(usuarios: usuarios);
        await vm.InicializarAsync();
        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Usuarios);
        usuarios.Setup(u => u.ListarAsync()).ReturnsAsync(new List<UsuarioDto> { CrearUsuario(2, "mlopez") });

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Equal(2, vm.Usuarios.Count);
        Assert.Single(vm.Items);
        servicio.Verify(s => s.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
    }

    [Fact]
    public async Task DosBusquedas_LaPrimeraTerminaTarde_NoPisaLaSegunda()
    {
        var (vm, servicio, _, _) = Armar();
        await vm.InicializarAsync();
        var primera = new TaskCompletionSource<IReadOnlyList<AuditoriaItemDto>>();
        var segunda = new TaskCompletionSource<IReadOnlyList<AuditoriaItemDto>>();
        servicio.Reset();
        servicio.SetupSequence(s => s.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(primera.Task).Returns(segunda.Task);

        var busquedaVieja = vm.CargarAsync();
        var busquedaNueva = vm.CargarAsync();
        var filasNuevas = new List<AuditoriaItemDto> { CrearItem(2) };
        segunda.SetResult(filasNuevas);
        await busquedaNueva;
        primera.SetResult(new List<AuditoriaItemDto> { CrearItem(1), CrearItem(3) });
        await busquedaVieja;

        Assert.Same(filasNuevas, vm.Items);
    }

    [Fact]
    public async Task ExportarPdf_ConFechasCambiadasSinBuscar_RotulaElPeriodoCargado()
    {
        var (vm, _, pdf, _) = Armar();
        vm.FechaDesde = new DateTime(2026, 9, 1);
        vm.FechaHasta = new DateTime(2026, 9, 30);
        await vm.CargarAsync();
        vm.FechaDesde = new DateTime(2026, 8, 1);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdf.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaAuditoriaPdf>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m => m.DescripcionFiltros.Contains("01/09/2026") && !m.DescripcionFiltros.Contains("01/08/2026")),
            It.IsAny<ResumenPdf?>()), Times.Once);
    }

    [Fact]
    public async Task SinConexion_Reintentar_ConsultaConLosFiltrosDePantalla()
    {
        var (vm, servicio, _, _) = Armar();
        await vm.InicializarAsync();
        PonerFalla(servicio, new ServidorNoDisponibleException());
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);
        servicio.Setup(s => s.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<AuditoriaItemDto> { CrearItem(1) });
        vm.UsuarioId = 2;

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        servicio.Verify(s => s.ObtenerLogAsync(2, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
    }

    [Fact]
    public void ExportarPdf_AlAsignarFilas_SeHabilitaYAvisaElCambio()
    {
        var (vm, _, _, _) = Armar();
        var avisos = 0;
        vm.ExportarPdfCommand.CanExecuteChanged += (_, _) => avisos++;
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));

        vm.Items = new List<AuditoriaItemDto> { CrearItem() };

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        Assert.True(avisos > 0);
    }

    [Fact]
    public async Task SinConexionEnElLogDeInicializar_Reintentar_ConservaElUsuarioElegido()
    {
        var (vm, servicio, _, _) = Armar();
        PonerFalla(servicio, new ServidorNoDisponibleException());
        await vm.InicializarAsync();
        Assert.True(vm.SinConexion);
        Assert.Equal(2, vm.Usuarios.Count);
        vm.UsuarioFiltroSeleccionado = vm.Usuarios[1];
        servicio.Setup(s => s.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<AuditoriaItemDto> { CrearItem(1) });

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Same(vm.Usuarios[1], vm.UsuarioFiltroSeleccionado);
        servicio.Verify(s => s.ObtenerLogAsync(2, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
    }

    [Fact]
    public async Task ExportarPdf_LaAccionSaleEnTextoLegible_NoConElNombreDelEnum()
    {
        var items = new List<AuditoriaItemDto>
        {
            new(new DateTime(2026, 10, 7, 14, 35, 0), "mperez", AccionAuditada.RegistroMovimiento, "MovimientoStock", 209, "Entrada de 40 unidades"),
        };
        var (vm, _, _, _, _, pdfExporterMock, _, _) = Crear(items);
        await vm.CargarAsync();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.Is<IEnumerable<FilaAuditoriaPdf>>(filas => filas.Single().Accion == "Registro de movimiento"),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf?>()), Times.Once);
    }
}
