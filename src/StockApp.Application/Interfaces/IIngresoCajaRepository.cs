using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Interfaces;

public interface IIngresoCajaRepository
{
    /// <summary>Incluye la FuenteFinanciamiento navegable.</summary>
    Task<IngresoCaja?> ObtenerPorIdAsync(int id);

    /// <summary>Incluye la fuente. Ordena por Fecha desc, luego Id desc.</summary>
    Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync();

    Task<int> AgregarAsync(IngresoCaja ingreso);
    Task ActualizarAsync(IngresoCaja ingreso);

    /// <summary>Incluye la fuente. Solo ACTIVOS de la <paramref name="caja"/> con Fecha en [desdeUtc, hastaUtc]. Ordena por Fecha, luego Id.</summary>
    Task<IReadOnlyList<IngresoCaja>> ListarPorRangoAsync(Caja caja, DateTime desdeUtc, DateTime hastaUtc);

    /// <summary>Suma de Monto de los ingresos ACTIVOS de la <paramref name="caja"/> con Fecha &lt; fechaUtc (saldo inicial del libro caja).</summary>
    Task<decimal> TotalActivosAntesDeAsync(Caja caja, DateTime fechaUtc);
}
