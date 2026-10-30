using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Proyectos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fake de IProyectoService para las pantallas de Proyectos (este proyecto no referencia Moq). Como TareaServiceFake, MUTA
/// los Proyecto reales con las reglas del dominio (Proyecto.CambiarEstado), así un click en "Reabrir" seguido de la recarga
/// de la ficha ve el estado nuevo. La ficha se arma como ProyectoService.ObtenerFichaAsync: Disponible = Presupuesto -
/// total, y la lista de gastos solo si <see cref="ConVerFinanzas"/> (lo que la API hace con finanzas.ver).
/// </summary>
internal sealed class ProyectoServiceFake : IProyectoService
{
    private readonly List<Proyecto> _proyectos;

    public ProyectoServiceFake(List<Proyecto>? proyectos = null) => _proyectos = proyectos ?? new List<Proyecto>();

    /// <summary>Gastos activos imputados, por id de proyecto.</summary>
    public Dictionary<int, List<GastoDeProyectoDto>> GastosPorProyecto { get; } = new();

    /// <summary>false simula una sesión sin finanzas.ver: la ficha llega con Gastos == null.</summary>
    public bool ConVerFinanzas { get; set; } = true;

    public Func<Task<IReadOnlyList<Proyecto>>>? ResponderListar { get; set; }

    public List<DatosProyecto> Creados { get; } = new();
    public List<(int Id, DatosProyecto Datos)> Modificados { get; } = new();
    public List<(int Id, EstadoProyecto Destino)> CambiosDeEstado { get; } = new();
    public List<(int Id, string Texto)> NotasAgregadas { get; } = new();

    public Task<int> CrearAsync(DatosProyecto datos)
    {
        Creados.Add(datos);
        var id = _proyectos.Count + 1;
        _proyectos.Add(new Proyecto { Id = id, Titulo = datos.Titulo, Presupuesto = datos.Presupuesto });
        return Task.FromResult(id);
    }

    public Task ModificarAsync(int id, DatosProyecto datos)
    {
        Modificados.Add((id, datos));
        var proyecto = _proyectos.First(p => p.Id == id);
        proyecto.Titulo = datos.Titulo;
        proyecto.Presupuesto = datos.Presupuesto;
        return Task.CompletedTask;
    }

    public Task CambiarEstadoAsync(int id, EstadoProyecto destino)
    {
        _proyectos.First(p => p.Id == id).CambiarEstado(destino, DateTime.UtcNow);
        CambiosDeEstado.Add((id, destino));
        return Task.CompletedTask;
    }

    public Task AgregarNotaAsync(int id, string texto)
    {
        _proyectos.First(p => p.Id == id).Notas.Add(new NotaProyecto { ProyectoId = id, Texto = texto, Fecha = DateTime.UtcNow });
        NotasAgregadas.Add((id, texto));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Proyecto>> ListarAsync() =>
        ResponderListar?.Invoke() ?? Task.FromResult<IReadOnlyList<Proyecto>>(_proyectos.ToList());

    public Task<FichaProyectoDto> ObtenerFichaAsync(int id)
    {
        var proyecto = _proyectos.First(p => p.Id == id);
        var gastos = GastosPorProyecto.TryGetValue(id, out var lista) ? lista : new List<GastoDeProyectoDto>();
        var total = gastos.Sum(g => g.MontoTotal);
        decimal? disponible = proyecto.Presupuesto is decimal p ? p - total : null;
        return Task.FromResult(new FichaProyectoDto(proyecto, total, disponible, ConVerFinanzas ? gastos : null));
    }

    public Task<IReadOnlyList<OpcionProyectoDto>> ListarOpcionesAsync() =>
        Task.FromResult<IReadOnlyList<OpcionProyectoDto>>(_proyectos
            .Where(p => p.Estado is EstadoProyecto.Pendiente or EstadoProyecto.EnCurso)
            .Select(p => new OpcionProyectoDto(p.Id, p.Titulo, p.Estado)).ToList());
}
