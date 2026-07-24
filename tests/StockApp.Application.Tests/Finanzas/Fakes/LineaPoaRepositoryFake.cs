using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;

namespace StockApp.Application.Tests.Finanzas.Fakes;

/// <summary>
/// Fake in-memory de ILineaPoaRepository para AnalisisImportacionServicePoaTests (F5d Entrega 2
/// Task 1: cómputo de EsNueva). Mismo patrón que ProveedorRepositoryFake/RubroGastoRepositoryFake/
/// FuenteFinanciamientoRepositoryFake en RepositorioMaestrosFake.cs. AgregarAsync/ActualizarAsync/
/// ActualizarSinAsignacionesAsync no los ejercita ningún test de este módulo todavía — implementados
/// de forma mínima pero funcional (no NotSupportedException) para no sorprender a un test futuro.
/// </summary>
public sealed class LineaPoaRepositoryFake : ILineaPoaRepository
{
    private readonly List<LineaPoa> _lineas;
    private int _siguienteId;

    public LineaPoaRepositoryFake(IReadOnlyList<LineaPoa> lineas)
    {
        _lineas = lineas.ToList();
        _siguienteId = _lineas.Count == 0 ? 1 : _lineas.Max(l => l.Id) + 1;
    }

    public Task<LineaPoa?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_lineas.FirstOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<LineaPoa>> ListarTodasAsync() =>
        Task.FromResult((IReadOnlyList<LineaPoa>)_lineas.ToList());

    public Task<bool> ExisteNombreEjercicioAsync(string nombre, int ejercicio, int? excluyendoId = null) =>
        Task.FromResult(_lineas.Any(l =>
            l.Nombre == nombre && l.Ejercicio == ejercicio && (excluyendoId is null || l.Id != excluyendoId.Value)));

    public Task<int> AgregarAsync(LineaPoa linea)
    {
        linea.Id = _siguienteId++;
        _lineas.Add(linea);
        return Task.FromResult(linea.Id);
    }

    public Task ActualizarAsync(LineaPoa linea, IReadOnlyList<AsignacionPresupuestal> nuevasAsignaciones)
    {
        var indice = _lineas.FindIndex(l => l.Id == linea.Id);
        if (indice >= 0)
        {
            linea.Asignaciones = nuevasAsignaciones.ToList();
            _lineas[indice] = linea;
        }
        return Task.CompletedTask;
    }

    public Task ActualizarSinAsignacionesAsync(LineaPoa linea)
    {
        var indice = _lineas.FindIndex(l => l.Id == linea.Id);
        if (indice >= 0)
            _lineas[indice] = linea;
        return Task.CompletedTask;
    }
}
