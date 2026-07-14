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

public class ProductoServiceInvalidacionTests
{
    [Fact]
    public async Task AltaAsync_Exitosa_InvalidaLaVersionDeReportes()
    {
        var repo    = new Mock<IProductoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();
        var umRepo  = new Mock<IUnidadMedidaRepository>();
        var version = new Mock<IVersionReportes>();

        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        session.Setup(s => s.UsuarioActual).Returns(
            new StockApp.Application.Auth.UsuarioSesion(1, "usuario", RolUsuario.Admin, null));
        auth.Setup(a => a.Verificar(RolUsuario.Admin, It.IsAny<string>()));

        umRepo.Setup(r => r.ObtenerPorIdAsync(It.IsAny<int>()))
              .ReturnsAsync(new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u" });
        repo.Setup(r => r.ExisteCodigoAsync("SKU-001", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<Producto>())).ReturnsAsync(42);

        var svc = new ProductoService(repo.Object, session.Object, auth.Object, audit.Object, umRepo.Object, version.Object);

        var p = new Producto { Codigo = "SKU-001", Nombre = "Fideos", UnidadMedidaId = 1, PrecioVenta = 150m };
        await svc.AltaAsync(p);

        version.Verify(v => v.Invalidar(), Times.Once);
    }
}
