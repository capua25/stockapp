using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de OrigenFinanciamiento. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// D3 (spec 2026-09-08): catálogo propio, NO se reusa FuenteFinanciamiento de Finanzas.
/// D14: NO invalida IVersionReportes — ver ZonaService para la nota completa.
/// </summary>
public class OrigenFinanciamientoService : IOrigenFinanciamientoService
{
    private readonly IOrigenFinanciamientoRepository _repo;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public OrigenFinanciamientoService(
        IOrigenFinanciamientoRepository repo,
        ICurrentSession                 session,
        IAuthorizationService           auth,
        IAuditLogger                    audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(OrigenFinanciamiento origen)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(origen.Nombre))
            throw new ArgumentException("El nombre del origen de financiamiento es obligatorio.");

        if (await _repo.ExisteNombreAsync(origen.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe un origen de financiamiento con el nombre '{origen.Nombre}'.");

        var id = await _repo.AgregarAsync(origen);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaOrigenFinanciamiento,
            "OrigenFinanciamiento", id,
            $"Nombre: {origen.Nombre}");

        return id;
    }

    public async Task ModificarAsync(OrigenFinanciamiento origen)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(origen.Id)
            ?? throw new EntidadNoEncontradaException($"Origen de financiamiento {origen.Id} no encontrado.");

        if (original.Nombre != origen.Nombre
            && await _repo.ExisteNombreAsync(origen.Nombre, origen.Id))
            throw new ReglaDeNegocioException($"Ya existe un origen de financiamiento con el nombre '{origen.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre != origen.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {origen.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = origen.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionOrigenFinanciamiento,
            "OrigenFinanciamiento", origen.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var origen = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Origen de financiamiento {id} no encontrado.");

        if (!origen.Activo)
            throw new ReglaDeNegocioException($"El origen de financiamiento {id} ya está inactivo.");

        origen.Activo = false;
        await _repo.ActualizarAsync(origen);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaOrigenFinanciamiento,
            "OrigenFinanciamiento", id,
            $"Baja lógica de '{origen.Nombre}'");
    }

    public async Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync()
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync()
    {
        // Gateada con GestionarTareas — ver nota en ZonaService.ListarActivasAsync.
        _auth.Verificar(_session, Permisos.GestionarTareas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(o => o.Activo).ToList();
    }
}
