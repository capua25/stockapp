using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class PermisoUsuarioRepository : IPermisoUsuarioRepository
{
    private readonly AppDbContext _ctx;

    public PermisoUsuarioRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId)
    {
        var permisos = await _ctx.PermisosUsuario
            .Where(p => p.UsuarioId == usuarioId)
            .Select(p => p.Permiso)
            .ToListAsync();
        return new HashSet<string>(permisos);
    }

    public async Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos)
    {
        await using var transaccion = await _ctx.Database.BeginTransactionAsync();

        var existentes = await _ctx.PermisosUsuario
            .Where(p => p.UsuarioId == usuarioId)
            .ToListAsync();
        _ctx.PermisosUsuario.RemoveRange(existentes);

        foreach (var permiso in permisos)
            _ctx.PermisosUsuario.Add(new PermisoUsuario { UsuarioId = usuarioId, Permiso = permiso });

        await _ctx.SaveChangesAsync();
        await transaccion.CommitAsync();
    }
}
