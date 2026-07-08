using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>Helpers de seed para los tests de integración de StockApp.Api.</summary>
public static class DatosDePrueba
{
    private static readonly BcryptPasswordHasher Hasher = new();

    public static async Task<Usuario> SeedUsuarioAsync(
        AppDbContext ctx, string nombreUsuario, string contrasena, RolUsuario rol)
    {
        var usuario = new Usuario
        {
            NombreUsuario = nombreUsuario,
            HashContrasena = Hasher.Hash(contrasena),
            Rol = rol,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };

        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        return usuario;
    }
}
