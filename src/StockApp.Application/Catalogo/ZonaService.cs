using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de Zona. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// D14 (spec 2026-09-08): a diferencia de CategoriaService, NO invalida IVersionReportes —
/// el reporte de tareas no se cachea, así que este servicio ni siquiera recibe esa
/// dependencia en el constructor. No agregarla "para ser consistente con Categoria".
/// </summary>
public class ZonaService : IZonaService
{
    private readonly IZonaRepository       _repo;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;

    public ZonaService(
        IZonaRepository       repo,
        ICurrentSession       session,
        IAuthorizationService auth,
        IAuditLogger          audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(Zona zona)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(zona.Nombre))
            throw new ArgumentException("El nombre de la zona es obligatorio.");

        zona.Nombre = zona.Nombre.Trim();

        if (await _repo.ExisteNombreAsync(zona.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe una zona con el nombre '{zona.Nombre}'.");

        var id = await _repo.AgregarAsync(zona);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaZona,
            "Zona", id,
            $"Nombre: {zona.Nombre}");

        return id;
    }

    public async Task ModificarAsync(Zona zona)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(zona.Id)
            ?? throw new EntidadNoEncontradaException($"Zona {zona.Id} no encontrada.");

        if (string.IsNullOrWhiteSpace(zona.Nombre))
            throw new ArgumentException("El nombre de la zona es obligatorio.");

        zona.Nombre = zona.Nombre.Trim();

        if (!string.Equals(original.Nombre, zona.Nombre, StringComparison.OrdinalIgnoreCase)
            && await _repo.ExisteNombreAsync(zona.Nombre, zona.Id))
            throw new ReglaDeNegocioException($"Ya existe una zona con el nombre '{zona.Nombre}'.");

        var cambios = new List<string>();
        if (!string.Equals(original.Nombre, zona.Nombre, StringComparison.OrdinalIgnoreCase))
            cambios.Add($"Nombre: {original.Nombre} → {zona.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = zona.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionZona,
            "Zona", zona.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var zona = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Zona {id} no encontrada.");

        if (!zona.Activo)
            throw new ReglaDeNegocioException($"La zona {id} ya está inactiva.");

        zona.Activo = false;
        await _repo.ActualizarAsync(zona);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaZona,
            "Zona", id,
            $"Baja lógica de '{zona.Nombre}'");
    }

    public async Task<IReadOnlyList<Zona>> ListarTodasAsync()
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<Zona>> ListarActivasAsync()
    {
        // Gateada con GestionarTareas (permiso del consumidor), NO con GestionarTablasMaestras
        // (permiso del administrador) — mismo patrón que CategoriaService.ListarActivasAsync
        // con GestionarProductos. Ver decisión en Global Constraints del plan.
        _auth.Verificar(_session, Permisos.GestionarTareas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(z => z.Activo).ToList();
    }
}
