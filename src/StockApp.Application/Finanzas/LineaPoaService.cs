using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM del agregado LineaPoa + AsignacionPresupuestal. Las asignaciones (presupuesto por
/// fuente — financiamiento mixto B+C) viven SIEMPRE dentro de la línea: alta y modificación
/// reciben la lista completa; el repo las reemplaza físicamente (no tienen baja lógica propia).
/// </summary>
public class LineaPoaService : ILineaPoaService
{
    private readonly ILineaPoaRepository             _repo;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public LineaPoaService(
        ILineaPoaRepository repo,
        IFuenteFinanciamientoRepository fuentes,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo    = repo;
        _fuentes = fuentes;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    private async Task ValidarAsync(LineaPoa linea)
    {
        if (string.IsNullOrWhiteSpace(linea.Nombre))
            throw new ArgumentException("El nombre de la línea POA es obligatorio.");
        if (string.IsNullOrWhiteSpace(linea.Programa))
            throw new ArgumentException("El programa de la línea POA es obligatorio.");
        if (linea.Ejercicio <= 0)
            throw new ArgumentException("El ejercicio de la línea POA debe ser un año válido.");

        if (linea.Asignaciones.Count == 0)
            throw new ReglaDeNegocioException(
                "La línea POA debe tener al menos una asignación presupuestal.");

        if (linea.Asignaciones.Any(a => a.Monto <= 0))
            throw new ReglaDeNegocioException(
                "Todas las asignaciones presupuestales deben tener un monto mayor a cero.");

        var fuentesRepetidas = linea.Asignaciones
            .GroupBy(a => a.FuenteFinanciamientoId)
            .Any(g => g.Count() > 1);
        if (fuentesRepetidas)
            throw new ReglaDeNegocioException(
                "Hay una fuente de financiamiento repetida en las asignaciones presupuestales.");

        foreach (var asignacion in linea.Asignaciones)
        {
            _ = await _fuentes.ObtenerPorIdAsync(asignacion.FuenteFinanciamientoId)
                ?? throw new EntidadNoEncontradaException(
                    $"Fuente de financiamiento {asignacion.FuenteFinanciamientoId} no encontrada.");
        }
    }

    public async Task<int> AltaAsync(LineaPoa linea)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        await ValidarAsync(linea);

        if (await _repo.ExisteNombreEjercicioAsync(linea.Nombre, linea.Ejercicio, null))
            throw new ReglaDeNegocioException(
                $"Ya existe una línea POA '{linea.Nombre}' para el ejercicio {linea.Ejercicio}.");

        var id = await _repo.AgregarAsync(linea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaLineaPoa,
            "LineaPoa", id,
            $"Nombre: {linea.Nombre}; Ejercicio: {linea.Ejercicio}; " +
            $"Asignaciones: {linea.Asignaciones.Count}");

        return id;
    }

    public async Task ModificarAsync(LineaPoa linea)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        await ValidarAsync(linea);

        var original = await _repo.ObtenerPorIdAsync(linea.Id)
            ?? throw new EntidadNoEncontradaException($"Línea POA {linea.Id} no encontrada.");

        if ((original.Nombre != linea.Nombre || original.Ejercicio != linea.Ejercicio)
            && await _repo.ExisteNombreEjercicioAsync(linea.Nombre, linea.Ejercicio, linea.Id))
            throw new ReglaDeNegocioException(
                $"Ya existe una línea POA '{linea.Nombre}' para el ejercicio {linea.Ejercicio}.");

        var cambios = new List<string>();
        if (original.Nombre != linea.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {linea.Nombre}");
        if (original.Programa != linea.Programa)
            cambios.Add($"Programa: {original.Programa} → {linea.Programa}");
        if (original.Ejercicio != linea.Ejercicio)
            cambios.Add($"Ejercicio: {original.Ejercicio} → {linea.Ejercicio}");

        // Asignaciones: comparación por conjunto (fuente, monto) — el orden no importa.
        var setOriginal = original.Asignaciones
            .Select(a => (a.FuenteFinanciamientoId, a.Monto)).OrderBy(x => x).ToList();
        var setNuevo = linea.Asignaciones
            .Select(a => (a.FuenteFinanciamientoId, a.Monto)).OrderBy(x => x).ToList();
        if (!setOriginal.SequenceEqual(setNuevo))
            cambios.Add($"Asignaciones: {original.Asignaciones.Count} → {linea.Asignaciones.Count} " +
                        $"(total {linea.Asignaciones.Sum(a => a.Monto)})");

        if (cambios.Count == 0)
            return;

        original.Nombre    = linea.Nombre;
        original.Programa  = linea.Programa;
        original.Ejercicio = linea.Ejercicio;
        await _repo.ActualizarAsync(original, linea.Asignaciones);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionLineaPoa,
            "LineaPoa", linea.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        var linea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Línea POA {id} no encontrada.");

        if (!linea.Activo)
            throw new ReglaDeNegocioException($"La línea POA {id} ya está inactiva.");

        linea.Activo = false;
        // La baja lógica NO toca las asignaciones: se re-pasan las existentes tal cual.
        var asignacionesActuales = linea.Asignaciones
            .Select(a => new AsignacionPresupuestal
            {
                FuenteFinanciamientoId = a.FuenteFinanciamientoId,
                Monto = a.Monto,
            })
            .ToList();
        await _repo.ActualizarAsync(linea, asignacionesActuales);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaLineaPoa,
            "LineaPoa", id,
            $"Baja lógica de '{linea.Nombre}' ({linea.Ejercicio})");
    }

    public async Task<IReadOnlyList<LineaPoa>> ListarTodasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<LineaPoa>> ListarActivasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(l => l.Activo).ToList();
    }
}
