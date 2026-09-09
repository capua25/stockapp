using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de OrganismoResponsable. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// D14 (spec 2026-09-08): NO invalida IVersionReportes — ver ZonaService para la nota completa.
/// </summary>
public class OrganismoResponsableService : IOrganismoResponsableService
{
    private readonly IOrganismoResponsableRepository _repo;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public OrganismoResponsableService(
        IOrganismoResponsableRepository repo,
        ICurrentSession                 session,
        IAuthorizationService           auth,
        IAuditLogger                    audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(OrganismoResponsable organismo)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(organismo.Nombre))
            throw new ArgumentException("El nombre del organismo es obligatorio.");

        if (await _repo.ExisteNombreAsync(organismo.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe un organismo con el nombre '{organismo.Nombre}'.");

        var id = await _repo.AgregarAsync(organismo);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaOrganismoResponsable,
            "OrganismoResponsable", id,
            $"Nombre: {organismo.Nombre}");

        return id;
    }

    public async Task ModificarAsync(OrganismoResponsable organismo)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(organismo.Id)
            ?? throw new EntidadNoEncontradaException($"Organismo {organismo.Id} no encontrado.");

        if (original.Nombre != organismo.Nombre
            && await _repo.ExisteNombreAsync(organismo.Nombre, organismo.Id))
            throw new ReglaDeNegocioException($"Ya existe un organismo con el nombre '{organismo.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre != organismo.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {organismo.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = organismo.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionOrganismoResponsable,
            "OrganismoResponsable", organismo.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var organismo = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Organismo {id} no encontrado.");

        if (!organismo.Activo)
            throw new ReglaDeNegocioException($"El organismo {id} ya está inactivo.");

        organismo.Activo = false;
        await _repo.ActualizarAsync(organismo);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaOrganismoResponsable,
            "OrganismoResponsable", id,
            $"Baja lógica de '{organismo.Nombre}'");
    }

    public async Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync()
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync()
    {
        // Gateada con GestionarTareas — ver nota en ZonaService.ListarActivasAsync.
        _auth.Verificar(_session, Permisos.GestionarTareas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(o => o.Activo).ToList();
    }
}
