using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IZonaRepository
{
    Task<Zona?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<Zona>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(Zona zona);
    Task ActualizarAsync(Zona zona);
}
