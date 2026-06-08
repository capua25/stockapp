using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IUsuarioRepository
{
    Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario);
    Task<Usuario?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<Usuario>> ListarTodosAsync();
    Task<bool> ExisteAlgunUsuarioAsync();
    Task<int> AgregarAsync(Usuario usuario);
    Task ActualizarAsync(Usuario usuario);
    Task ActualizarUltimoAccesoAsync(int usuarioId, DateTime fechaAcceso);
}
