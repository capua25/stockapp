using StockApp.Api.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>Helpers de seed para los tests de integración de StockApp.Api.</summary>
public static class DatosDePrueba
{
    private static readonly BcryptPasswordHasher Hasher = new();

    /// <summary>
    /// Siembra un usuario. Si el rol es Operador, también siembra
    /// AuthorizationService.PermisosInicialesOperador en PermisosUsuario — mismo estado que
    /// tiene en producción cualquier Operador real, ya sea por el backfill de la migración
    /// (Task 1) o por UsuarioService.AltaUsuarioAsync (Task 11). Un Operador sin esas filas es
    /// un estado que no puede existir en producción; sembrarlo "pelado" solo servía para
    /// enmascarar el gap real (ver SeedOperadorConPermisosAsync para el caso de un Operador con
    /// permisos recortados, ej. para probar 403 por falta de un permiso puntual).
    /// </summary>
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

        if (rol == RolUsuario.Operador)
            await SembrarPermisosAsync(ctx, usuario.Id, AuthorizationService.PermisosInicialesOperador);

        return usuario;
    }

    /// <summary>
    /// Siembra un Operador con un subconjunto explícito de permisos configurables, en vez de
    /// los 9 completos de PermisosInicialesOperador. Para tests que necesitan probar el 403 por
    /// falta de UN permiso puntual — refleja el estado real de un Operador al que un Admin le
    /// recortó permisos vía PUT /usuarios/{id}/permisos (Task 10), no un estado inventado.
    /// </summary>
    public static async Task<Usuario> SeedOperadorConPermisosAsync(
        AppDbContext ctx, string nombreUsuario, string contrasena, IReadOnlyCollection<string> permisos)
    {
        var usuario = new Usuario
        {
            NombreUsuario = nombreUsuario,
            HashContrasena = Hasher.Hash(contrasena),
            Rol = RolUsuario.Operador,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };

        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();

        await SembrarPermisosAsync(ctx, usuario.Id, permisos);

        return usuario;
    }

    private static async Task SembrarPermisosAsync(AppDbContext ctx, int usuarioId, IReadOnlyCollection<string> permisos)
    {
        foreach (var permiso in permisos)
            ctx.PermisosUsuario.Add(new PermisoUsuario { UsuarioId = usuarioId, Permiso = permiso });

        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Siembra un Operador real (los 9 PermisosInicialesOperador completos, vía SeedUsuarioAsync)
    /// y firma su JWT a partir del Id que la base le asignó — nunca un Id inventado como "2".
    /// Existe porque con PoblarPermisosMiddleware puesto (spec 2026-08-10, Task 8), un JWT para
    /// un usuarioId que no existe en la tabla Usuarios da 403 por diseño (fail-closed):
    /// IProveedorPermisos.ObtenerAsync no encuentra filas y el conjunto de permisos queda vacío.
    /// Antes de ese middleware esto no importaba (la autorización viejo era solo por rol, nunca
    /// tocaba la base) — varios tests fabricaban `GenerarToken(2, RolUsuario.Operador)` sin
    /// sembrar ningún Usuario y "andaban" por eso mismo. Este helper reemplaza ese patrón por
    /// uno que refleja un estado real de producción: todo Operador real tiene una fila en
    /// Usuarios. Mismo patrón que ya usaba PostGarantizarPorDefecto_ConTokenOperador_Devuelve200
    /// (UnidadesMedidaEndpointTests) de forma inline, antes de que este helper existiera.
    /// </summary>
    public static async Task<(Usuario Usuario, string Token)> SeedOperadorConTokenAsync(
        AppDbContext ctx, IJwtTokenService jwt, string nombreUsuario, string contrasena = "Secreta123!")
    {
        var usuario = await SeedUsuarioAsync(ctx, nombreUsuario, contrasena, RolUsuario.Operador);
        var token = jwt.GenerarToken(usuario.Id, RolUsuario.Operador);
        return (usuario, token);
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
