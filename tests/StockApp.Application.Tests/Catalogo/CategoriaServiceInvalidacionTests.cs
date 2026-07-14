using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Catalogo;

public class CategoriaServiceInvalidacionTests
{
    [Fact]
    public async Task AltaAsync_Exitosa_InvalidaLaVersionDeReportes()
    {
        var repo    = new Mock<ICategoriaRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();
        var version = new Mock<IVersionReportes>();

        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        session.Setup(s => s.UsuarioActual).Returns(
            new StockApp.Application.Auth.UsuarioSesion(1, "usuario", RolUsuario.Admin, null));
        auth.Setup(a => a.Verificar(RolUsuario.Admin, It.IsAny<string>()));

        repo.Setup(r => r.ExisteNombreAsync("Lácteos", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<Categoria>())).ReturnsAsync(5);

        var svc = new CategoriaService(repo.Object, session.Object, auth.Object, audit.Object, version.Object);

        await svc.AltaAsync(new Categoria { Nombre = "Lácteos" });

        version.Verify(v => v.Invalidar(), Times.Once);
    }
}
