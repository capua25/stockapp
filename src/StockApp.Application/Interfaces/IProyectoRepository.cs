using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IProyectoRepository
{
    Task<int> AgregarAsync(Proyecto proyecto);

    /// <summary>Con los cuatro catálogos y las notas ordenadas por Fecha.</summary>
    Task<Proyecto?> ObtenerPorIdAsync(int id);

    /// <summary>Todos los proyectos con sus catálogos, por FechaCreacion descendente.</summary>
    Task<IReadOnlyList<Proyecto>> ListarAsync();

    /// <summary>Gastos activos (no anulados) imputados al proyecto, con su proveedor, por Fecha descendente.</summary>
    Task<IReadOnlyList<Gasto>> ListarGastosActivosAsync(int proyectoId);

    /// <summary><paramref name="proyecto"/> debe ser la instancia tracked de ObtenerPorIdAsync.</summary>
    Task ActualizarAsync(Proyecto proyecto);
}
