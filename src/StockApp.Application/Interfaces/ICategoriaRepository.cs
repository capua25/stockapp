using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface ICategoriaRepository
{
    Task<Categoria?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<Categoria>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(Categoria categoria);
    Task ActualizarAsync(Categoria categoria);

    /// <summary>
    /// true si la categoría tiene al menos un producto ACTIVO (los inactivos no cuentan).
    /// Consulta de existencia (EXISTS), no trae filas.
    /// </summary>
    Task<bool> TieneProductosActivosAsync(int categoriaId);
}
