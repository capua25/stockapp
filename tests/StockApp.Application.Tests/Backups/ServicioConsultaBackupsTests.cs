using Microsoft.Extensions.Logging;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;
using Permisos = StockApp.Application.Authorization.Permisos;

namespace StockApp.Application.Tests.Backups;

public class ServicioConsultaBackupsTests
{
    private static (ServicioConsultaBackups svc, Mock<ICorridaBackupRepository> repoMock, Mock<IAuthSvc> authMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo = new Mock<ICorridaBackupRepository>();
        var session = new Mock<ICurrentSession>();
        var auth = new Mock<IAuthSvc>();
        var logger = new Mock<ILogger<ServicioConsultaBackups>>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", rol, null));

        var svc = new ServicioConsultaBackups(repo.Object, session.Object, auth.Object, logger.Object);
        return (svc, repo, auth);
    }

    private static CorridaBackup CorridaExitosa(int id = 1) => new()
    {
        Id = id, IniciadaEn = DateTime.UtcNow.AddMinutes(-1), FinalizadaEn = DateTime.UtcNow,
        Resultado = ResultadoBackup.Exitosa, NombreArchivo = "backup.dump", TamanioBytes = 1024,
    };

    // ── ListarAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarAsync_VerificaPermisoGestionarDiagnostico()
    {
        var (svc, repo, auth) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<CorridaBackup>());

        await svc.ListarAsync();

        auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.GestionarDiagnostico), Times.Once);
    }

    [Fact]
    public async Task ListarAsync_MapeaCorridasADto()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<CorridaBackup> { CorridaExitosa() });

        var resultado = await svc.ListarAsync();

        var dto = Assert.Single(resultado);
        Assert.Equal("Exitosa", dto.Resultado);
        Assert.Equal("backup.dump", dto.NombreArchivo);
    }

    [Fact]
    public async Task ListarAsync_SinPermiso_PropagaExcepcionYNuncaTocaElRepositorio()
    {
        var (svc, repo, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarDiagnostico))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ListarAsync());

        repo.Verify(r => r.ListarTodasAsync(), Times.Never);
    }

    // ── ObtenerSaludAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerSaludAsync_VerificaPermiso_YSinCorridasDevuelveVencidoTrue()
    {
        var (svc, repo, auth) = Crear();
        repo.Setup(r => r.ObtenerUltimaExitosaAsync()).ReturnsAsync((CorridaBackup?)null);

        var salud = await svc.ObtenerSaludAsync();

        auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.GestionarDiagnostico), Times.Once);
        Assert.True(salud.Vencido);
        Assert.Null(salud.UltimoExitoEn);
        Assert.Equal(26, salud.UmbralHoras);
    }

    [Fact]
    public async Task ObtenerSaludAsync_SinPermiso_PropagaExcepcionYNuncaTocaElRepositorio()
    {
        var (svc, repo, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarDiagnostico))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ObtenerSaludAsync());

        repo.Verify(r => r.ObtenerUltimaExitosaAsync(), Times.Never);
    }

    // ── ResolverArchivoParaDescargaAsync ─────────────────────────────────────

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_SinPermiso_PropagaExcepcionYNuncaTocaElRepositorio()
    {
        var (svc, repo, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarDiagnostico))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ResolverArchivoParaDescargaAsync(1, "/tmp/no-importa"));

        repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_IdInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(999)).ReturnsAsync((CorridaBackup?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ResolverArchivoParaDescargaAsync(999, "/tmp/no-importa"));
    }

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_CorridaFallidaSinArchivo_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        var fallida = new CorridaBackup
        {
            Id = 2, IniciadaEn = DateTime.UtcNow, FinalizadaEn = DateTime.UtcNow,
            Resultado = ResultadoBackup.Fallida, MotivoFallo = "simulado",
        };
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(fallida);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ResolverArchivoParaDescargaAsync(2, "/tmp/no-importa"));
    }

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_ArchivoNoExisteEnDisco_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(CorridaExitosa());
        var directorioVacio = Path.Combine(Path.GetTempPath(), "ServicioConsultaBackupsTests_" + Guid.NewGuid());
        Directory.CreateDirectory(directorioVacio);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ResolverArchivoParaDescargaAsync(1, directorioVacio));
    }

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_ArchivoExiste_DevuelveRutaCompletaYNombre()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(CorridaExitosa());
        var directorio = Path.Combine(Path.GetTempPath(), "ServicioConsultaBackupsTests_" + Guid.NewGuid());
        Directory.CreateDirectory(directorio);
        File.WriteAllBytes(Path.Combine(directorio, "backup.dump"), new byte[] { 1, 2, 3 });

        var (rutaCompleta, nombreArchivo) = await svc.ResolverArchivoParaDescargaAsync(1, directorio);

        Assert.Equal(Path.Combine(directorio, "backup.dump"), rutaCompleta);
        Assert.Equal("backup.dump", nombreArchivo);
    }

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_NombreArchivoConEscapeDeDirectorio_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        var maliciosa = new CorridaBackup
        {
            Id = 3, IniciadaEn = DateTime.UtcNow.AddMinutes(-1), FinalizadaEn = DateTime.UtcNow,
            Resultado = ResultadoBackup.Exitosa, NombreArchivo = "../../../../etc/passwd", TamanioBytes = 1024,
        };
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(maliciosa);
        var directorio = Path.Combine(Path.GetTempPath(), "ServicioConsultaBackupsTests_" + Guid.NewGuid());
        Directory.CreateDirectory(directorio);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ResolverArchivoParaDescargaAsync(3, directorio));
    }
}
