using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface ICategoriaService
{
    Task<int> AltaAsync(Categoria categoria);
    Task ModificarAsync(Categoria categoria);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<Categoria>> ListarTodasAsync();

    /// <summary>
    /// Categorías activas disponibles para selección (ej. en el alta de un producto).
    /// A diferencia de <see cref="ListarTodasAsync"/>, no exige GestionarTablasMaestras:
    /// cualquier rol con GestionarProductos puede necesitar esta lista.
    /// </summary>
    Task<IReadOnlyList<Categoria>> ListarActivasAsync();
}
