using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class UsuarioTests
{
    [Fact]
    public void Usuario_Nuevo_TieneActivoEnTrue()
    {
        var usuario = new Usuario
        {
            NombreUsuario = "admin",
            HashContrasena = "hash",
            Rol = RolUsuario.Admin,
            FechaAlta = DateTime.UtcNow
        };

        Assert.True(usuario.Activo);
    }

    [Fact]
    public void Usuario_NombreCompleto_EsOpcional()
    {
        var usuario = new Usuario
        {
            NombreUsuario = "operador1",
            HashContrasena = "hash",
            Rol = RolUsuario.Operador,
            FechaAlta = DateTime.UtcNow
        };

        Assert.Null(usuario.NombreCompleto);
        Assert.Null(usuario.UltimoAcceso);
    }
}
