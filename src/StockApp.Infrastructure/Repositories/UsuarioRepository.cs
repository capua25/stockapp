using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly AppDbContext _ctx;

    public UsuarioRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario)
        => _ctx.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario);

    public Task<Usuario?> ObtenerPorIdAsync(int id)
        => _ctx.Usuarios.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<Usuario>> ListarTodosAsync()
        => await _ctx.Usuarios.OrderBy(u => u.NombreUsuario).ToListAsync();

    public Task<bool> ExisteAlgunUsuarioAsync()
        => _ctx.Usuarios.AnyAsync();

    public async Task<int> AgregarAsync(Usuario usuario)
    {
        _ctx.Usuarios.Add(usuario);
        await _ctx.SaveChangesAsync();
        return usuario.Id;
    }

    public Task ActualizarAsync(Usuario usuario)
    {
        _ctx.Usuarios.Update(usuario);
        return _ctx.SaveChangesAsync();
    }

    public async Task ActualizarUltimoAccesoAsync(int usuarioId, DateTime fechaAcceso)
    {
        var usuario = await ObtenerPorIdAsync(usuarioId);
        if (usuario is null) return;
        usuario.UltimoAcceso = fechaAcceso;
        await _ctx.SaveChangesAsync();
    }
}
