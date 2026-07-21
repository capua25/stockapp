using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;

namespace StockApp.Application.Tests.Finanzas.Fakes;

/// <summary>
/// Fakes de los 3 repos de maestros que consumen tanto el análisis de importación (F5b,
/// read-only) como la validación de confirmación (F5c, Task 3, tampoco escribe pero necesita
/// que los fakes NO exploten si algún test los ejercita). Implementación real en memoria:
/// AgregarAsync/ActualizarAsync mutan una lista interna y auto-incrementan el Id, como haría
/// un repositorio EF real — a diferencia de la versión anterior (F5b), que los hacía tirar
/// NotSupportedException porque en ese entonces ningún código los llamaba nunca.
/// </summary>
public sealed class ProveedorRepositoryFake : IProveedorRepository
{
    private readonly List<Proveedor> _proveedores;
    private int _siguienteId;

    public ProveedorRepositoryFake(IReadOnlyList<Proveedor> proveedores)
    {
        _proveedores = proveedores.ToList();
        _siguienteId = _proveedores.Count == 0 ? 1 : _proveedores.Max(p => p.Id) + 1;
    }

    public Task<Proveedor?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_proveedores.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Proveedor>> ListarTodosAsync() =>
        Task.FromResult((IReadOnlyList<Proveedor>)_proveedores.ToList());

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null) =>
        Task.FromResult(_proveedores.Any(p =>
            p.Nombre == nombre && (excluyendoId is null || p.Id != excluyendoId.Value)));

    public Task<int> AgregarAsync(Proveedor proveedor)
    {
        proveedor.Id = _siguienteId++;
        _proveedores.Add(proveedor);
        return Task.FromResult(proveedor.Id);
    }

    public Task ActualizarAsync(Proveedor proveedor)
    {
        var indice = _proveedores.FindIndex(p => p.Id == proveedor.Id);
        if (indice >= 0)
            _proveedores[indice] = proveedor;
        return Task.CompletedTask;
    }
}

public sealed class RubroGastoRepositoryFake : IRubroGastoRepository
{
    private readonly List<RubroGasto> _rubros;
    private int _siguienteId;

    public RubroGastoRepositoryFake(IReadOnlyList<RubroGasto> rubros)
    {
        _rubros = rubros.ToList();
        _siguienteId = _rubros.Count == 0 ? 1 : _rubros.Max(r => r.Id) + 1;
    }

    public Task<RubroGasto?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_rubros.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<RubroGasto>> ListarTodosAsync() =>
        Task.FromResult((IReadOnlyList<RubroGasto>)_rubros.ToList());

    public Task<bool> ExisteCodigoAsync(int codigo, int? excluyendoId = null) =>
        Task.FromResult(_rubros.Any(r =>
            r.Codigo == codigo && (excluyendoId is null || r.Id != excluyendoId.Value)));

    public Task<int> AgregarAsync(RubroGasto rubro)
    {
        rubro.Id = _siguienteId++;
        _rubros.Add(rubro);
        return Task.FromResult(rubro.Id);
    }

    public Task ActualizarAsync(RubroGasto rubro)
    {
        var indice = _rubros.FindIndex(r => r.Id == rubro.Id);
        if (indice >= 0)
            _rubros[indice] = rubro;
        return Task.CompletedTask;
    }
}

public sealed class FuenteFinanciamientoRepositoryFake : IFuenteFinanciamientoRepository
{
    private readonly List<FuenteFinanciamiento> _fuentes;
    private int _siguienteId;

    public FuenteFinanciamientoRepositoryFake(IReadOnlyList<FuenteFinanciamiento> fuentes)
    {
        _fuentes = fuentes.ToList();
        _siguienteId = _fuentes.Count == 0 ? 1 : _fuentes.Max(f => f.Id) + 1;
    }

    public Task<FuenteFinanciamiento?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_fuentes.FirstOrDefault(f => f.Id == id));

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() =>
        Task.FromResult((IReadOnlyList<FuenteFinanciamiento>)_fuentes.ToList());

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null) =>
        Task.FromResult(_fuentes.Any(f =>
            f.Nombre == nombre && (excluyendoId is null || f.Id != excluyendoId.Value)));

    public Task<int> AgregarAsync(FuenteFinanciamiento fuente)
    {
        fuente.Id = _siguienteId++;
        _fuentes.Add(fuente);
        return Task.FromResult(fuente.Id);
    }

    public Task ActualizarAsync(FuenteFinanciamiento fuente)
    {
        var indice = _fuentes.FindIndex(f => f.Id == fuente.Id);
        if (indice >= 0)
            _fuentes[indice] = fuente;
        return Task.CompletedTask;
    }
}
