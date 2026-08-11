using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Migrations;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Entidades;

public class PermisoUsuarioBackfillTests : PostgresRepositoryTestBase
{
    public PermisoUsuarioBackfillTests(PostgresFixture fixture) : base(fixture) { }

    private static Usuario NuevoUsuario(string nombre, RolUsuario rol) => new()
    {
        NombreUsuario  = nombre,
        HashContrasena = "hash-de-prueba",
        Rol            = rol,
        Activo         = true,
        FechaAlta      = DateTime.UtcNow,
    };

    [Fact]
    public async Task Backfill_OperadorExistente_RecibeExactamenteLos9PermisosDeAccionesOperador()
    {
        var operador = NuevoUsuario("operador.backfill", RolUsuario.Operador);
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);

        var permisos = await Context.PermisosUsuario
            .Where(p => p.UsuarioId == operador.Id)
            .Select(p => p.Permiso)
            .ToListAsync();

        Assert.Equal(9, permisos.Count);
        Assert.Contains("catalogo.productos", permisos);
        Assert.Contains("movimientos.registrar", permisos);
        Assert.Contains("stock.recalcular", permisos);
        Assert.Contains("finanzas.ver", permisos);
        Assert.Contains("finanzas.maestros", permisos);
        Assert.Contains("finanzas.gastos", permisos);
        Assert.Contains("finanzas.pagos", permisos);
        Assert.Contains("finanzas.ingresos", permisos);
        Assert.Contains("tareas.gestionar", permisos);
    }

    [Fact]
    public async Task Backfill_OperadorExistente_NoIncluyeVerReportesNiGestionarTablasMaestras()
    {
        var operador = NuevoUsuario("operador.sinreportes", RolUsuario.Operador);
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);

        var permisos = await Context.PermisosUsuario
            .Where(p => p.UsuarioId == operador.Id)
            .Select(p => p.Permiso)
            .ToListAsync();

        Assert.DoesNotContain("reportes.ver", permisos);
        Assert.DoesNotContain("catalogo.maestras", permisos);
    }

    [Fact]
    public async Task Backfill_Admin_NoRecibeNingunaFila()
    {
        var admin = NuevoUsuario("admin.backfill", RolUsuario.Admin);
        Context.Usuarios.Add(admin);
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);

        var permisos = await Context.PermisosUsuario.Where(p => p.UsuarioId == admin.Id).ToListAsync();

        Assert.Empty(permisos);
    }

    [Fact]
    public async Task IndiceUnico_UsuarioIdPermiso_RechazaDuplicado()
    {
        var operador = NuevoUsuario("operador.duplicado", RolUsuario.Operador);
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();

        Context.PermisosUsuario.Add(new PermisoUsuario { UsuarioId = operador.Id, Permiso = "finanzas.ver" });
        await Context.SaveChangesAsync();

        Context.PermisosUsuario.Add(new PermisoUsuario { UsuarioId = operador.Id, Permiso = "finanzas.ver" });

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ReejecutarBackfill_EsIdempotente_NoDuplicaFilas()
    {
        var operador = NuevoUsuario("operador.idempotente", RolUsuario.Operador);
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);
        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);

        var cantidad = await Context.PermisosUsuario.CountAsync(p => p.UsuarioId == operador.Id);

        Assert.Equal(9, cantidad);
    }
}
