using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
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

    public Task<int> ContarAdminsActivosAsync()
        => _ctx.Usuarios.CountAsync(u => u.Rol == RolUsuario.Admin && u.Activo);

    public async Task<int> AgregarAsync(Usuario usuario)
    {
        try
        {
            _ctx.Usuarios.Add(usuario);
            await _ctx.SaveChangesAsync();
            return usuario.Id;
        }
        catch (DbUpdateException ex) when (EsViolacionNombreUsuarioUnico(ex))
        {
            // Fix 4b: cierra la carrera entre el chequeo previo de UsuarioService
            // (BuscarPorNombreAsync) y este insert. Sin este catch, la violación del
            // índice único IX_Usuarios_NombreUsuario llegaría como DbUpdateException
            // cruda y el endpoint respondería 500 en vez de 409.
            throw new ReglaDeNegocioException(
                $"Ya existe un usuario con el nombre '{usuario.NombreUsuario}'.");
        }
    }

    private static bool EsViolacionNombreUsuarioUnico(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
           && pg.ConstraintName == "IX_Usuarios_NombreUsuario";

    public Task ActualizarAsync(Usuario usuario)
    {
        _ctx.Usuarios.Update(usuario);
        return _ctx.SaveChangesAsync();
    }

    public Task ActualizarUltimoAccesoAsync(int usuarioId, DateTime fechaAcceso)
        => _ctx.Usuarios
               .Where(u => u.Id == usuarioId)
               .ExecuteUpdateAsync(s => s.SetProperty(u => u.UltimoAcceso, fechaAcceso));
}
