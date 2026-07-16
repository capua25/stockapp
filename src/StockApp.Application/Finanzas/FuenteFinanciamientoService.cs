using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM de FuenteFinanciamiento (los "literales" FIGM). Baja lógica con Activo=false.
/// No invalida IVersionReportes: ese caché versiona solo reportes de stock.
/// </summary>
public class FuenteFinanciamientoService : IFuenteFinanciamientoService
{
    private readonly IFuenteFinanciamientoRepository _repo;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public FuenteFinanciamientoService(
        IFuenteFinanciamientoRepository repo,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(FuenteFinanciamiento fuente)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        if (string.IsNullOrWhiteSpace(fuente.Nombre))
            throw new ArgumentException("El nombre de la fuente de financiamiento es obligatorio.");

        if (await _repo.ExisteNombreAsync(fuente.Nombre, null))
            throw new ReglaDeNegocioException(
                $"Ya existe una fuente de financiamiento con el nombre '{fuente.Nombre}'.");

        var id = await _repo.AgregarAsync(fuente);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaFuenteFinanciamiento,
            "FuenteFinanciamiento", id,
            $"Nombre: {fuente.Nombre}");

        return id;
    }

    public async Task ModificarAsync(FuenteFinanciamiento fuente)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        if (string.IsNullOrWhiteSpace(fuente.Nombre))
            throw new ArgumentException("El nombre de la fuente de financiamiento es obligatorio.");

        var original = await _repo.ObtenerPorIdAsync(fuente.Id)
            ?? throw new EntidadNoEncontradaException($"Fuente de financiamiento {fuente.Id} no encontrada.");

        if (original.Nombre != fuente.Nombre
            && await _repo.ExisteNombreAsync(fuente.Nombre, fuente.Id))
            throw new ReglaDeNegocioException(
                $"Ya existe una fuente de financiamiento con el nombre '{fuente.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre != fuente.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {fuente.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = fuente.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionFuenteFinanciamiento,
            "FuenteFinanciamiento", fuente.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        var fuente = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Fuente de financiamiento {id} no encontrada.");

        if (!fuente.Activo)
            throw new ReglaDeNegocioException($"La fuente de financiamiento {id} ya está inactiva.");

        fuente.Activo = false;
        await _repo.ActualizarAsync(fuente);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaFuenteFinanciamiento,
            "FuenteFinanciamiento", id,
            $"Baja lógica de '{fuente.Nombre}'");
    }

    public async Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(f => f.Activo).ToList();
    }
}
