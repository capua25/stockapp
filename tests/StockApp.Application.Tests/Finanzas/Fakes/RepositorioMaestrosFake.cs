using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;

namespace StockApp.Application.Tests.Finanzas.Fakes;

/// <summary>
/// Fakes de los 3 repos de maestros que consume el análisis de importación (F5b, spec §8):
/// listas fijas pasadas por constructor. Los métodos de escritura lanzan
/// <see cref="NotSupportedException"/> porque el análisis es READ-ONLY y nunca debería
/// llamarlos — si algún día el servicio los invoca por error, el test lo revienta ahí mismo.
/// </summary>
public sealed class ProveedorRepositoryFake : IProveedorRepository
{
    private readonly IReadOnlyList<Proveedor> _proveedores;

    public ProveedorRepositoryFake(IReadOnlyList<Proveedor> proveedores) => _proveedores = proveedores;

    public Task<Proveedor?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_proveedores.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Proveedor>> ListarTodosAsync() => Task.FromResult(_proveedores);

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null) =>
        throw new NotSupportedException("El análisis de importación es read-only.");

    public Task<int> AgregarAsync(Proveedor proveedor) =>
        throw new NotSupportedException("El análisis de importación es read-only.");

    public Task ActualizarAsync(Proveedor proveedor) =>
        throw new NotSupportedException("El análisis de importación es read-only.");
}

public sealed class RubroGastoRepositoryFake : IRubroGastoRepository
{
    private readonly IReadOnlyList<RubroGasto> _rubros;

    public RubroGastoRepositoryFake(IReadOnlyList<RubroGasto> rubros) => _rubros = rubros;

    public Task<RubroGasto?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_rubros.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<RubroGasto>> ListarTodosAsync() => Task.FromResult(_rubros);

    public Task<bool> ExisteCodigoAsync(int codigo, int? excluyendoId = null) =>
        throw new NotSupportedException("El análisis de importación es read-only.");

    public Task<int> AgregarAsync(RubroGasto rubro) =>
        throw new NotSupportedException("El análisis de importación es read-only.");

    public Task ActualizarAsync(RubroGasto rubro) =>
        throw new NotSupportedException("El análisis de importación es read-only.");
}

public sealed class FuenteFinanciamientoRepositoryFake : IFuenteFinanciamientoRepository
{
    private readonly IReadOnlyList<FuenteFinanciamiento> _fuentes;

    public FuenteFinanciamientoRepositoryFake(IReadOnlyList<FuenteFinanciamiento> fuentes) => _fuentes = fuentes;

    public Task<FuenteFinanciamiento?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_fuentes.FirstOrDefault(f => f.Id == id));

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() => Task.FromResult(_fuentes);

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null) =>
        throw new NotSupportedException("El análisis de importación es read-only.");

    public Task<int> AgregarAsync(FuenteFinanciamiento fuente) =>
        throw new NotSupportedException("El análisis de importación es read-only.");

    public Task ActualizarAsync(FuenteFinanciamiento fuente) =>
        throw new NotSupportedException("El análisis de importación es read-only.");
}
