using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Proyectos;

public interface IProyectoService
{
    /// <summary>Requiere GestionarProyectos. El proyecto nace Pendiente.</summary>
    Task<int> CrearAsync(DatosProyecto datos);

    /// <summary>Requiere GestionarProyectos. Un proyecto cerrado no se edita: hay que reabrirlo.</summary>
    Task ModificarAsync(int id, DatosProyecto datos);

    /// <summary>Requiere GestionarProyectos.</summary>
    Task CambiarEstadoAsync(int id, EstadoProyecto destino);

    /// <summary>Requiere GestionarProyectos. Permitido también en proyectos cerrados.</summary>
    Task AgregarNotaAsync(int id, string texto);

    /// <summary>Requiere VerProyectos.</summary>
    Task<IReadOnlyList<Proyecto>> ListarAsync();

    /// <summary>Requiere VerProyectos. Los gastos de la ficha solo se incluyen con finanzas.ver.</summary>
    Task<FichaProyectoDto> ObtenerFichaAsync(int id);

    /// <summary>
    /// Solo exige sesión autenticada: alimenta selectores de usuarios que pueden no tener
    /// proyectos.ver. Devuelve los proyectos Pendiente y En curso por título, con id, título y estado.
    /// </summary>
    Task<IReadOnlyList<OpcionProyectoDto>> ListarOpcionesAsync();
}
