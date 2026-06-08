using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Auth;

public class PrimerArranqueServiceTests
{
    private static (PrimerArranqueService service,
                    Mock<IUsuarioRepository> repoMock,
                    Mock<IPasswordHasher> hasherMock)
        Crear(bool hayUsuarios)
    {
        var repo   = new Mock<IUsuarioRepository>();
        var hasher = new Mock<IPasswordHasher>();

        repo.Setup(r => r.ExisteAlgunUsuarioAsync()).ReturnsAsync(hayUsuarios);
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("$2a$12$hashed");

        var svc = new PrimerArranqueService(repo.Object, hasher.Object);
        return (svc, repo, hasher);
    }

    [Fact]
    public async Task RequiereCrearAdmin_BdVacia_RetornaTrue()
    {
        var (svc, _, _) = Crear(hayUsuarios: false);

        Assert.True(await svc.RequiereCrearAdminAsync());
    }

    [Fact]
    public async Task RequiereCrearAdmin_HayUsuarios_RetornaFalse()
    {
        var (svc, _, _) = Crear(hayUsuarios: true);

        Assert.False(await svc.RequiereCrearAdminAsync());
    }

    [Fact]
    public async Task CrearAdminInicial_GuardaConHashYRolAdmin()
    {
        var (svc, repo, hasher) = Crear(hayUsuarios: false);

        await svc.CrearAdminInicialAsync("adminRoot", "contrasenaSegura");

        hasher.Verify(h => h.Hash("contrasenaSegura"), Times.Once);
        repo.Verify(r => r.AgregarAsync(It.Is<Usuario>(u =>
            u.NombreUsuario == "adminRoot" &&
            u.HashContrasena == "$2a$12$hashed" &&
            u.Rol == RolUsuario.Admin &&
            u.Activo == true
        )), Times.Once);
    }

    [Fact]
    public async Task CrearAdminInicial_SiYaHayUsuarios_LanzaExcepcion()
    {
        var (svc, _, _) = Crear(hayUsuarios: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CrearAdminInicialAsync("admin", "pwd"));
    }
}
