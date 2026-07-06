using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IUnidadMedidaService
{
    Task<int> AltaAsync(UnidadMedida unidadMedida);
    Task ModificarAsync(UnidadMedida unidadMedida);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync();

    /// <summary>
    /// Unidades activas disponibles para selección (ej. en el alta de un producto).
    /// A diferencia de <see cref="ListarTodasAsync"/>, no exige GestionarTablasMaestras:
    /// cualquier rol con GestionarProductos puede necesitar esta lista.
    /// </summary>
    Task<IReadOnlyList<UnidadMedida>> ListarActivasAsync();

    /// <summary>
    /// Garantiza (idempotente) la existencia de la unidad de medida "Unidad": si no existe
    /// (por nombre, case-insensitive) la crea; si ya existe, la devuelve sin duplicar.
    /// </summary>
    Task<UnidadMedida> GarantizarUnidadPorDefectoAsync();
}
