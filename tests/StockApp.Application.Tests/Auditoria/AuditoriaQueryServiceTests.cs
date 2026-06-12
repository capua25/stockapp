using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Auditoria;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Auditoria;

public class AuditoriaQueryServiceTests
{
    // ── helpers de setup ──────────────────────────────────────────────────────

    private static (AuditoriaQueryService svc,
                    Mock<IAuditoriaQueryRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<IAuditoriaQueryRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);

        // Por defecto auth no lanza (permiso concedido)
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new AuditoriaQueryService(repo.Object, session.Object, auth.Object);
        return (svc, repo, session, auth);
    }

    private static AuditoriaItemDto Item(DateTime fecha, string usuario = "admin") =>
        new AuditoriaItemDto(
            Fecha:         fecha,
            NombreUsuario: usuario,
            Accion:        AccionAuditada.AltaProducto,
            Entidad:       "Producto",
            EntidadId:     1,
            Detalle:       "alta");

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerLogAsync_FiltraPorUsuario()
    {
        var (svc, repo, _, _) = Crear();
        var devuelto = new[] { Item(new DateTime(2026, 1, 1), "pepe") };
        repo.Setup(r => r.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(devuelto);

        var result = await svc.ObtenerLogAsync(usuarioId: 7, fechaDesde: null, fechaHasta: null);

        // El usuarioId se pasa tal cual al repo (el filtrado real lo hace C4).
        repo.Verify(r => r.ObtenerLogAsync(7, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
        // Passthrough fiel del resultado del repo.
        Assert.Same(devuelto, result);
    }

    [Fact]
    public async Task ObtenerLogAsync_FiltraPorFechas()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Array.Empty<AuditoriaItemDto>());

        var desde = new DateTime(2026, 1, 1);
        var hasta = new DateTime(2026, 1, 31);

        await svc.ObtenerLogAsync(usuarioId: null, fechaDesde: desde, fechaHasta: hasta);

        // Las fechas se pasan al repo sin alterarlas.
        repo.Verify(r => r.ObtenerLogAsync(It.IsAny<int?>(), desde, hasta), Times.Once);
    }

    [Fact]
    public async Task ObtenerLogAsync_FechaHasta_AplicadaComoFinDeDia()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Array.Empty<AuditoriaItemDto>());

        // A nivel SERVICIO el ajuste fin-de-día NO ocurre: se pasa la FechaHasta CRUDA.
        // El ajuste real (23:59:59.999) lo hace el repo y se prueba en C4 (integración).
        var fechaHastaCruda = new DateTime(2026, 6, 12);

        await svc.ObtenerLogAsync(usuarioId: null, fechaDesde: null, fechaHasta: fechaHastaCruda);

        repo.Verify(
            r => r.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), fechaHastaCruda),
            Times.Once);
    }

    [Fact]
    public async Task ObtenerLogAsync_OrdenadoPorFechaDesc()
    {
        var (svc, repo, _, _) = Crear();
        // El orden lo establece el repo (C4); el service hace passthrough fiel.
        var ordenados = new[]
        {
            Item(new DateTime(2026, 3, 1)),
            Item(new DateTime(2026, 2, 1)),
            Item(new DateTime(2026, 1, 1)),
        };
        repo.Setup(r => r.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(ordenados);

        var result = await svc.ObtenerLogAsync(null, null, null);

        Assert.Equal(3, result.Count);
        Assert.Same(ordenados[0], result[0]);
        Assert.Same(ordenados[1], result[1]);
        Assert.Same(ordenados[2], result[2]);
    }

    [Fact]
    public async Task ObtenerLogAsync_Operador_LanzaUnauthorized()
    {
        var (svc, repo, _, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.VerReportes))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ObtenerLogAsync(null, null, null));

        // Fail-closed: el repo NUNCA debe ser invocado.
        repo.Verify(
            r => r.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()),
            Times.Never);
    }
}
