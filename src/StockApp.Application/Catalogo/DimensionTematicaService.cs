using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de DimensionTematica. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// D14 (spec 2026-09-08): NO invalida IVersionReportes — ver ZonaService para la nota completa.
/// </summary>
public class DimensionTematicaService : IDimensionTematicaService
{
    private readonly IDimensionTematicaRepository _repo;
    private readonly ICurrentSession               _session;
    private readonly IAuthorizationService         _auth;
    private readonly IAuditLogger                  _audit;

    public DimensionTematicaService(
        IDimensionTematicaRepository repo,
        ICurrentSession              session,
        IAuthorizationService        auth,
        IAuditLogger                 audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(DimensionTematica dimension)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(dimension.Nombre))
            throw new ArgumentException("El nombre de la dimensión es obligatorio.");

        dimension.Nombre = dimension.Nombre.Trim();

        if (await _repo.ExisteNombreAsync(dimension.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe una dimensión con el nombre '{dimension.Nombre}'.");

        var id = await _repo.AgregarAsync(dimension);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaDimensionTematica,
            "DimensionTematica", id,
            $"Nombre: {dimension.Nombre}");

        return id;
    }

    public async Task ModificarAsync(DimensionTematica dimension)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(dimension.Id)
            ?? throw new EntidadNoEncontradaException($"Dimensión {dimension.Id} no encontrada.");

        if (string.IsNullOrWhiteSpace(dimension.Nombre))
            throw new ArgumentException("El nombre de la dimensión es obligatorio.");

        dimension.Nombre = dimension.Nombre.Trim();

        if (!string.Equals(original.Nombre, dimension.Nombre, StringComparison.OrdinalIgnoreCase)
            && await _repo.ExisteNombreAsync(dimension.Nombre, dimension.Id))
            throw new ReglaDeNegocioException($"Ya existe una dimensión con el nombre '{dimension.Nombre}'.");

        var cambios = new List<string>();
        if (!string.Equals(original.Nombre, dimension.Nombre, StringComparison.OrdinalIgnoreCase))
            cambios.Add($"Nombre: {original.Nombre} → {dimension.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = dimension.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionDimensionTematica,
            "DimensionTematica", dimension.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var dimension = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Dimensión {id} no encontrada.");

        if (!dimension.Activo)
            throw new ReglaDeNegocioException($"La dimensión {id} ya está inactiva.");

        dimension.Activo = false;
        await _repo.ActualizarAsync(dimension);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaDimensionTematica,
            "DimensionTematica", id,
            $"Baja lógica de '{dimension.Nombre}'");
    }

    public async Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync()
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync()
    {
        // Gateada con GestionarTareas — ver nota en ZonaService.ListarActivasAsync.
        _auth.Verificar(_session, Permisos.GestionarTareas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(d => d.Activo).ToList();
    }
}
