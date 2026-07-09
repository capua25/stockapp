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

    public static async Task<Producto> SeedProductoAsync(AppDbContext ctx, string codigo, string nombre)
    {
        // Nombre y Abreviatura de UnidadMedida son únicos; se derivan del código para evitar
        // colisiones cuando un mismo test siembra más de un producto.
        var unidad = new UnidadMedida
        {
            Nombre = $"Unidad-{codigo}",
            Abreviatura = codigo.Length > 10 ? codigo[..10] : codigo,
            Activo = true,
        };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = nombre,
            UnidadMedidaId = unidad.Id,
            PrecioCosto = 10m,
            PrecioVenta = 20m,
            StockActual = 5m,
            StockMinimo = 0m,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };

        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        return producto;
    }

    public static async Task<Producto> SeedProductoConStockAsync(
        AppDbContext ctx, string codigo, string nombre, decimal stockActual)
    {
        var unidad = new UnidadMedida { Nombre = "Unidad", Abreviatura = "u", Activo = true };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = nombre,
            UnidadMedidaId = unidad.Id,
            PrecioCosto = 10m,
            PrecioVenta = 20m,
            StockActual = stockActual,
            StockMinimo = 0m,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };

        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        return producto;
    }
}
