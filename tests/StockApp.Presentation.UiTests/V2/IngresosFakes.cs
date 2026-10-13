using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Servicio de ingresos configurable: lista, carga que no termina, falla de la carga y falla al guardar, y
/// espías de altas, modificaciones y bajas. Lanza lo que lanza ApiErrores.</summary>
internal sealed class IngresoCajaFake : IIngresoCajaService
{
    public List<IngresoCaja> Lista { get; set; } = new();
    public TaskCompletionSource<IReadOnlyList<IngresoCaja>>? Pendiente { get; set; }
    public Exception? Falla { get; set; }
    public Exception? FallaAlGuardar { get; set; }
    public List<IngresoCaja> Altas { get; } = new();
    public List<IngresoCaja> Modificaciones { get; } = new();
    public List<int> Bajas { get; } = new();

    public Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync() =>
        Falla is not null ? Task.FromException<IReadOnlyList<IngresoCaja>>(Falla)
        : Pendiente is not null ? Pendiente.Task
        : Task.FromResult<IReadOnlyList<IngresoCaja>>(Lista.ToList());

    public Task<int> AltaAsync(IngresoCaja ingreso)
    {
        if (FallaAlGuardar is not null)
            return Task.FromException<int>(FallaAlGuardar);
        Altas.Add(ingreso);
        return Task.FromResult(99);
    }

    public Task ModificarAsync(IngresoCaja ingreso)
    {
        if (FallaAlGuardar is not null)
            return Task.FromException(FallaAlGuardar);
        Modificaciones.Add(ingreso);
        return Task.CompletedTask;
    }

    public Task BajaLogicaAsync(int id)
    {
        Bajas.Add(id);
        return Task.CompletedTask;
    }
}

/// <summary>Fuentes activas del formulario; con Falla, ListarActivasAsync la lanza (403 o servidor caído).</summary>
internal sealed class FuentesActivasFake : IFuenteFinanciamientoService
{
    public List<FuenteFinanciamiento> Activas { get; set; } = new();
    public Exception? Falla { get; set; }

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync() =>
        Falla is not null ? Task.FromException<IReadOnlyList<FuenteFinanciamiento>>(Falla)
        : Task.FromResult<IReadOnlyList<FuenteFinanciamiento>>(Activas.ToList());

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() => throw new NotSupportedException();
    public Task<int> AltaAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException();
    public Task ModificarAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException();
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
}
