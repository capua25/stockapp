using System.Collections.Generic;
using System.Linq;
namespace StockApp.Presentation.ViewModels.Proyectos;

/// <summary>
/// Opción del combo "Proyecto" de un gasto o una factura: la primera es el centinela "Sin proyecto" (Id null), que
/// permite desimputar; el resto son los proyectos abiertos (ListarOpcionesAsync) y, en edición, el proyecto actual del
/// gasto aunque ya esté cerrado.
/// </summary>
public sealed record OpcionProyectoImputacion(string Nombre, int? Id)
{
    /// <summary>Texto del centinela "Sin proyecto".</summary>
    public const string NombreSinProyecto = "Sin proyecto";

    /// <summary>Arma la lista del combo: centinela + abiertos + (si no está entre ellos) el actual cerrado, para no
    /// perder el dato histórico (mismo criterio que los maestros inactivos del formulario).</summary>
    public static IReadOnlyList<OpcionProyectoImputacion> Armar(
        IEnumerable<StockApp.Application.Proyectos.OpcionProyectoDto> abiertos, int? actualId, string? tituloActual)
    {
        var lista = new List<OpcionProyectoImputacion> { new(NombreSinProyecto, null) };
        lista.AddRange(abiertos.Select(p => new OpcionProyectoImputacion(p.Titulo, p.Id)));
        if (actualId is int id && lista.All(o => o.Id != id))
            lista.Add(new OpcionProyectoImputacion(tituloActual ?? $"Proyecto {id}", id));
        return lista;
    }
}
